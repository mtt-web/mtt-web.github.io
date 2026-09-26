using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// EconomySystem
/// ============================================================================
/// Herná EKONOMICKÁ SLUČKA. Jeden MonoBehaviour singleton (rovnaký vzor ako
/// GameClock / GameEconomy / TrainSystem / VehicleSystem) – stačí ho hodiť na
/// ľubovoľný GameObject v scéne. Spája dokopy tri už hotové systémy:
///   • <see cref="GameClock"/>    – zdroj herného času (event OnDayElapsed),
///   • <see cref="GameEconomy"/>  – peňažné konto (DeductAllowNegative),
///   • <see cref="FactoryRegistry"/>, <see cref="TrainSystem"/>,
///     <see cref="VehicleSystem"/> – objekty, z ktorých sa čítajú dáta.
///
/// ČO ROBÍ:
///   1) PRODUKCIA SUROVÍN v surovinových továrňach (Forest, Coal Mine,
///      Iron Ore Mine, Oil Wells, Farm, Gold Mine, Silver Mine) – každý herný
///      DEŇ navýši "amount" podľa LevelSalary (+3 / +6 / +10), orezané kapacitou.
///   1b) SPRACOVANIE v "set 2" továrňach (Sawmill, Smelter, Oil Refinery,
///      Glass / Grain / Furniture / Electronics Factory, Slaughterhouse,
///      Power Station) – každý herný DEŇ sa zo VŠETKÝCH vstupných (Load)
///      slotov odoberie rovnaké množstvo a o toľko sa navýšia výstupné
///      (UnLoad) sloty. Pomer je 1:1 na jednotku, tempo je rovnaké ako pri
///      ťažbe (+3 / +6 / +10 podľa LevelSalary). Power Station nemá UnLoad
///      slot, takže vstup sa len spotrebuje.
///   2) MZDY zamestnancov tovární – každý herný MESIAC, mzda = EmployeeSalary
///      * (1.0 / 1.5 / 2.0 podľa LevelSalary). Strhne sa LEN ak bola v uplynulom
///      mesiaci zaznamenaná aspoň jedna zmena "amount" (a továreň bola obsadená).
///   3) PREVÁDZKOVÉ NÁKLADY flotily – každý herný MESIAC sa spočíta OperatingCosts
///      všetkých aktívnych vlakov a vozidiel a strhne sa z konta.
///   4) SERVIS / ŽIVOTNOSŤ – pre každý vlak/vozidlo sa odpočítava čas; po
///      dosiahnutí ServicingInterval (herné dni) ALEBO ServiceLife (herné roky)
///      sa dopravný prostriedok pošle do depa cez existujúce ReturnToDepot(...).
///   5) DEGRADÁCIA preplnených spracovateľských tovární – ak v "set 2" továrni
///      ostane slot na plnej kapacite a bez zmeny 3 herné mesiace, začne klesať
///      o -10/deň až do nuly (potom sa uvoľní pre ďalší dovoz).
///   6) DYNAMICKÝ CENOVÝ NÁSOBITEĽ predaja surovín – každý herný MESIAC sa podľa
///      stavu konta nastaví ResourcePricing.Multiplier: konto v mínuse ceny
///      zvyšuje (×2, ×3, ×4 … kým sa nedostane do plusu), v plusu ich vráti na ×1.
///
/// ── PREČO ŽIADNE VLÁKNA / PARALELIZMUS (vedomé rozhodnutie) ────────────────
/// Zadanie necháva voľbu na mne. NEPOUŽÍVAM vlákna ani Job System a je to
/// správna voľba:
///   • Objem výpočtov je mizivý. Produkcia beží raz za herný DEŇ (každé ~3 s
///     reálne), mzdy/prevádzka raz za herný MESIAC. Aj pri stovkách tovární a
///     dopravných prostriedkov je to jednoduchá O(n) slučka cez krátke zoznamy –
///     rádovo mikrosekundy. Réžia vlákien (spustenie, synchronizácia) by bola
///     násobne drahšia než samotná práca.
///   • Bezpečnosť. Takmer všetko, čoho sa dotýkame (FactoryRegistry, ResourceSlot,
///     ReturnToDepot, GameEconomy, GameObjecty vlakov/vozidiel), sú Unity / herné
///     objekty, ktoré NIE SÚ thread-safe. Volať ich z iného vlákna by viedlo k
///     pádom alebo nedeterministickým chybám. Unity API smie bežať len na hlavnom
///     vlákne.
///   • Determinizmus a ladenie. Sekvenčný beh na hlavnom vlákne dáva
///     reprodukovateľné, ľahko odladiteľné správanie (dôležité pri hernej
///     ekonomike, kde záleží na poradí "odpočítaj mzdu → potom produkuj").
/// Ak by v budúcnosti vznikla naozaj ťažká dávková úloha (napr. simulácia
/// desaťtisícov entít), správny nástroj by bol Unity Job System + Burst nad
/// čistými dátami (struct/NativeArray), NIE ručné Thready – ale dnes to nie je
/// potrebné.
///
/// ── ČASOVANIE ─────────────────────────────────────────────────────────────
/// Systém sa NEspolieha na vlastný odpočet reálnych sekúnd – načúva
/// <see cref="GameClock.OnDayElapsed"/>. Hranicu mesiaca si deteguje sám
/// (porovnaním <see cref="GameClock.Month"/>), aby vedel ZÚČTOVAŤ práve uplynulý
/// mesiac PRED tým, než sa do nového mesiaca započíta produkcia prvého dňa.
/// Tým je každá zmena "amount" priradená do správneho mesiaca aj pri lagu
/// (GameClock posúva dni po jednom a pre každý zmeškaný deň vyvolá event).
/// </summary>
public class EconomySystem : MonoBehaviour
{
    public static EconomySystem instance;

    // ── Zachytené referencie na ostatné singletony (cache) ──
    private GameClock clock;
    private GameEconomy economy;

    // ── Sledovanie hranice mesiaca (z eventu OnDayElapsed) ──
    private int lastMonth = -1;
    private int lastYear = -1;
    private bool subscribed;

    // ── Stav tovární (produkcia/mzdy/degradácia) ──
    private readonly Dictionary<FactoryInstance, FactoryEconomyState> factoryStates
        = new Dictionary<FactoryInstance, FactoryEconomyState>();

    // ── Stav životnosti/servisu dopravných prostriedkov (kľúč = depo) ──
    private readonly Dictionary<long, FleetLifecycleState> trainLifecycles
        = new Dictionary<long, FleetLifecycleState>();
    private readonly Dictionary<long, FleetLifecycleState> vehicleLifecycles
        = new Dictionary<long, FleetLifecycleState>();

    // Pomocné scratch sady na reconciliáciu (aby sme negenerovali GC každý tik).
    private readonly HashSet<FactoryInstance> seenFactories = new HashSet<FactoryInstance>();
    private readonly HashSet<long> seenKeys = new HashSet<long>();
    private readonly List<FactoryInstance> toRemoveFactories = new List<FactoryInstance>();
    private readonly List<long> toRemoveKeys = new List<long>();

    private static long DepotKey(int x, int z) => ((long)x << 32) | (uint)z;

    /// <summary>
    /// READ-ONLY pre HUD (GameMenuHUDPanel): počet tovární, v ktorých práve
    /// beží degradácia aspoň jedného slotu (tovar klesá −10/deň).
    /// </summary>
    public int DecayingFactoryCount
    {
        get
        {
            int n = 0;
            foreach (var kvp in factoryStates)
                if (kvp.Value.IsDecaying) n++;
            return n;
        }
    }

    // =====================================================================
    // ŽIVOTNÝ CYKLUS MonoBehaviour
    // =====================================================================
    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        TryBindAndSubscribe();
    }

    void OnDestroy()
    {
        if (clock != null)
            clock.OnDayElapsed -= HandleDayElapsed;
        subscribed = false;
    }

    /// <summary>
    /// Zaviaže referencie a prihlási sa na denný tik hodín. Ak hodiny ešte nie
    /// sú v scéne (poradie inicializácie), skúsi to znova v Update.
    /// </summary>
    private void TryBindAndSubscribe()
    {
        if (subscribed) return;

        clock = GameClock.instance;
        economy = GameEconomy.instance;

        if (clock == null) return;   // skúsi sa znova v Update

        lastMonth = clock.Month;
        lastYear = clock.Year;

        // Statické pole ResourcePricing.Multiplier prežíva reload scény / nový
        // rozohraný zápas – pri (re)štarte ekonomiky ho vrátime na pôvodné ceny.
        ResourcePricing.ResetMultiplier();

        clock.OnDayElapsed += HandleDayElapsed;
        subscribed = true;
    }

    void Update()
    {
        // Neskoré prihlásenie, ak hodiny nabehli až po našom Start.
        if (!subscribed)
        {
            TryBindAndSubscribe();
            if (!subscribed) return;
        }

        // Drž stavy tovární zosúladené s registrom aj medzi dennými tikmi, nech
        // per-frame detekcia obchodu funguje hneď aj pre čerstvo postavenú továreň.
        ReconcileFactories();

        // Per-frame detekcia VONKAJŠÍCH zmien "amount" (vlaky/vozidlá nakladajú a
        // vykladajú priebežne, nielen na hraniciach dňa). Lacné: pár desiatok
        // tovární × pár slotov. Vďaka tomu vieme presne, či sa za mesiac niečo
        // pohlo (→ či sa má strhnúť mzda) aj pri rýchlom obchode.
        ScanExternalChanges();
    }

    // =====================================================================
    // DENNÝ TIK (z GameClock.OnDayElapsed) – produkcia, degradácia, servis,
    //            a na hranici mesiaca zúčtovanie predošlého mesiaca.
    // =====================================================================
    private void HandleDayElapsed()
    {
        if (clock == null) return;

        ReconcileFactories();   // pridaj/odober stavy podľa aktuálneho registra

        // 1) HRANICA MESIACA: ak sa zmenil mesiac/rok oproti poslednému dňu,
        //    najprv ZÚČTUJ uplynulý mesiac (mzdy + prevádzka flotily) z dát
        //    nazbieraných POČAS neho, a až potom započítaj dnešný (už nový) deň.
        if (clock.Month != lastMonth || clock.Year != lastYear)
        {
            ProcessMonthBoundary();
            lastMonth = clock.Month;
            lastYear = clock.Year;
        }

        // 2) DENNÁ PRODUKCIA + DEGRADÁCIA tovární (patrí už do aktuálneho mesiaca).
        ProcessDailyFactories();

        // 3) DENNÝ ODPOČET servisu/životnosti dopravných prostriedkov.
        ProcessDailyFleetLifecycle();
    }

    // =====================================================================
    // TOVÁRNE – reconciliácia stavov s registrom
    // =====================================================================
    private void ReconcileFactories()
    {
        seenFactories.Clear();

        var all = FactoryRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            FactoryInstance f = all[i];
            if (f == null) continue;
            seenFactories.Add(f);

            if (!factoryStates.ContainsKey(f))
                factoryStates[f] = new FactoryEconomyState(f);
        }

        // Odober stavy tovární, ktoré už nie sú v registri (zbúrané).
        toRemoveFactories.Clear();
        foreach (var kvp in factoryStates)
            if (!seenFactories.Contains(kvp.Key))
                toRemoveFactories.Add(kvp.Key);

        for (int i = 0; i < toRemoveFactories.Count; i++)
            factoryStates.Remove(toRemoveFactories[i]);
    }

    /// <summary>
    /// Po načítaní hry (keď IndicatrixAPI prepísal GameClock) zladí internú
    /// hranicu mesiaca/roka s aktuálnym dátumom hodín.
    /// </summary>
    public void ResyncToClock()
    {
        if (clock == null) clock = GameClock.instance;
        if (clock == null) return;
        lastMonth = clock.Month;
        lastYear = clock.Year;
    }

    /// <summary>Per-frame: zaznamenaj zmeny "amount" spôsobené zvonku (obchod).</summary>
    private void ScanExternalChanges()
    {
        foreach (var kvp in factoryStates)
            kvp.Value.ScanExternal();
    }

    /// <summary>Denná produkcia surovinových tovární + degradácia preplnených.</summary>
    private void ProcessDailyFactories()
    {
        foreach (var kvp in factoryStates)
            kvp.Value.DailyTick();
    }

    /// <summary>
    /// Zúčtovanie uplynulého mesiaca: mzdy zamestnancov tovární + prevádzkové
    /// náklady flotily. Všetko sa zráta a strhne JEDNÝM odpočtom (jedna zmena
    /// stavu konta = jedna notifikácia pre UI). Konto smie ísť do mínusu.
    /// </summary>
    private void ProcessMonthBoundary()
    {
        // (a) Mzdy tovární – plná výška iba ak bol mesiac "aktívny"
        //     (továreň obsadená a aspoň jedna zmena amount). Zároveň sa tu
        //     vyhodnotia série plnej kapacity a spustí prípadná degradácia.
        long salaries = 0;
        foreach (var kvp in factoryStates)
            salaries += kvp.Value.CloseMonthAndGetSalary();

        // (b) Prevádzkové náklady flotily – aktuálny stav vlakov + vozidiel.
        //     Sčítavame ich ODDELENE (vlaky vs vozidlá) kvôli ročnému výkazu
        //     v BudgetSystem, ktorý ich vykazuje ako samostatné položky.
        long trainOperating = SumTrainOperatingCosts();
        long vehicleOperating = SumVehicleOperatingCosts();

        long totalOutflow = salaries + trainOperating + vehicleOperating;

        if (totalOutflow != 0 && economy != null)
            economy.DeductAllowNegative(totalOutflow);

        // (c) EVIDENCIA pre ročnú uzávierku. BudgetSystem len kumuluje sumy,
        //     samotný odpočet z konta sme už urobili vyššie. Hlásime presne tie
        //     mesačné sumy, ktoré sme reálne strhli.
        if (BudgetSystem.instance != null)
        {
            BudgetSystem.instance.RecordMonthlySettlement(salaries, trainOperating, vehicleOperating);

            // Hranica ROKA: ProcessMonthBoundary beží PRED aktualizáciou
            // lastMonth/lastYear (tá je až v HandleDayElapsed po návrate odtiaľto).
            // Ak je clock.Year už iný ako lastYear, práve sa začal nový rok –
            // uplynulý rok (lastYear) je kompletne zúčtovaný (vrátane decembra),
            // takže ho môžeme uzavrieť a zobraziť výkaz.
            if (clock.Year != lastYear)
                BudgetSystem.instance.CloseYearAndShow(lastYear);
        }

        // Po zúčtovaní mesiaca prehodnoť cenový násobiteľ predaja podľa toho,
        // ako konto reálne STOJÍ na začiatku nového mesiaca.
        UpdatePriceMultiplier();
    }

    /// <summary>
    /// Dynamický cenový NÁSOBITEĽ predaja surovín (RAIL aj ROAD) podľa stavu
    /// konta – kontroluje sa RAZ ZA HERNÝ MESIAC (na hranici mesiaca):
    ///
    ///   • konto &lt; 0  → ceny sa zdvihnú. Prvý záporný mesiac ×2, ak je konto
    ///                    na ďalšej mesačnej kontrole STÁLE v mínuse ×3, potom
    ///                    ×4, ×5 … (každý ďalší záporný mesiac +1 k násobiteľu),
    ///   • konto &gt; 0  → reset na ×1 (pôvodné ceny) a drží sa, kým je v pluse,
    ///   • konto = 0  → ponechá sa aktuálny násobiteľ (nie je v mínuse ani v pluse).
    ///
    /// Aktuálna hodnota násobiteľa zároveň slúži ako "počítadlo" série záporných
    /// mesiacov, takže netreba ďalší stav: ďalší krok eskalácie je current+1.
    /// </summary>
    private void UpdatePriceMultiplier()
    {
        if (economy == null) return;

        long balance = economy.Balance;

        if (balance < 0)
        {
            // Eskalácia: prvý záporný mesiac na ×2, každý ďalší +1 (×3, ×4 …).
            int current = ResourcePricing.Multiplier;
            ResourcePricing.Multiplier = current < 2 ? 2 : current + 1;
        }
        else if (balance > 0)
        {
            // Konto v pluse → späť na pôvodné ceny.
            ResourcePricing.ResetMultiplier();
        }
        // balance == 0: zámerne bez zmeny (medzistav – ani mínus, ani plus).
    }

    /// <summary>Súčet OperatingCosts všetkých aktívnych VLAKOV.</summary>
    private long SumTrainOperatingCosts()
    {
        long sum = 0;

        TrainSystem ts = TrainSystem.instance;
        if (ts != null)
        {
            var trains = ts.GetActiveTrainInfos();
            for (int i = 0; i < trains.Count; i++)
            {
                var consist = trains[i].Consist;
                if (consist != null && consist.OperatingCosts > 0)
                    sum += consist.OperatingCosts;
            }
        }

        return sum;
    }

    /// <summary>Súčet OperatingCosts všetkých aktívnych VOZIDIEL.</summary>
    private long SumVehicleOperatingCosts()
    {
        long sum = 0;

        VehicleSystem vs = VehicleSystem.instance;
        if (vs != null)
        {
            var vehicles = vs.GetActiveVehicleInfos();
            for (int i = 0; i < vehicles.Count; i++)
            {
                var inst = vehicles[i].Instance;
                if (inst != null && inst.OperatingCosts > 0)
                    sum += inst.OperatingCosts;
            }
        }

        return sum;
    }

    // =====================================================================
    // FLOTILA – servisný interval (dni) a životnosť (roky) → poslať do depa
    // =====================================================================
    private void ProcessDailyFleetLifecycle()
    {
        DateTime today = clock.CurrentDate;

        // ── VLAKY ──
        TrainSystem ts = TrainSystem.instance;
        if (ts != null)
        {
            var trains = ts.GetActiveTrainInfos();

            // reconciliácia (nové depá → nový stav; zaniknuté → zmazať)
            seenKeys.Clear();
            for (int i = 0; i < trains.Count; i++)
            {
                var info = trains[i];
                long key = DepotKey(info.DepotX, info.DepotZ);
                seenKeys.Add(key);
                if (!trainLifecycles.ContainsKey(key))
                    trainLifecycles[key] = new FleetLifecycleState(today);
            }
            PruneLifecycles(trainLifecycles);

            // odpočet + prípadné poslanie do depa
            for (int i = 0; i < trains.Count; i++)
            {
                var info = trains[i];
                long key = DepotKey(info.DepotX, info.DepotZ);
                var consist = info.Consist;
                if (consist == null) continue;

                var st = trainLifecycles[key];
                bool send = st.AdvanceDayAndCheck(today, consist.ServiceLife, consist.ServicingInterval);
                if (send)
                    ts.ReturnToDepot(info.DepotX, info.DepotZ);
            }
        }

        // ── VOZIDLÁ ──
        VehicleSystem vs = VehicleSystem.instance;
        if (vs != null)
        {
            var vehicles = vs.GetActiveVehicleInfos();

            seenKeys.Clear();
            for (int i = 0; i < vehicles.Count; i++)
            {
                var info = vehicles[i];
                long key = DepotKey(info.DepotX, info.DepotZ);
                seenKeys.Add(key);
                if (!vehicleLifecycles.ContainsKey(key))
                    vehicleLifecycles[key] = new FleetLifecycleState(today);
            }
            PruneLifecycles(vehicleLifecycles);

            for (int i = 0; i < vehicles.Count; i++)
            {
                var info = vehicles[i];
                long key = DepotKey(info.DepotX, info.DepotZ);
                var inst = info.Instance;
                if (inst == null) continue;

                var st = vehicleLifecycles[key];
                bool send = st.AdvanceDayAndCheck(today, inst.ServiceLife, inst.ServicingInterval);
                if (send)
                    vs.ReturnToDepot(info.DepotX, info.DepotZ);
            }
        }
    }

    /// <summary>Vymaže lifecycle stavy depot-kľúčov, ktoré už neexistujú (zmazané vozidlo).</summary>
    private void PruneLifecycles(Dictionary<long, FleetLifecycleState> dict)
    {
        toRemoveKeys.Clear();
        foreach (var kvp in dict)
            if (!seenKeys.Contains(kvp.Key))
                toRemoveKeys.Add(kvp.Key);
        for (int i = 0; i < toRemoveKeys.Count; i++)
            dict.Remove(toRemoveKeys[i]);
    }
}


// ============================================================================
// LADIACE / VYVAŽOVACIE KONŠTANTY A VZORCE
// ============================================================================

/// <summary>
/// Jediné miesto pre čísla z ekonomického zadania (produkčné rýchlosti, mzdové
/// koeficienty, prah a tempo degradácie) + klasifikácia tovární na "set 1"
/// (surovinové) a "set 2" (spracovateľské).
/// </summary>
public static class EconomyTuning
{
    // ── SET 1: surovinové továrne (systém im NAVYŠUJE amount) ──
    // Zhoduje sa presne s továrňami TileID == 4 (ťažba).
    private static readonly HashSet<GameManager.FactoryConstructionMode> RawProducers
        = new HashSet<GameManager.FactoryConstructionMode>
    {
        GameManager.FactoryConstructionMode.Forest,
        GameManager.FactoryConstructionMode.CoalMine,
        GameManager.FactoryConstructionMode.IronOreMine,
        GameManager.FactoryConstructionMode.OilWells,
        GameManager.FactoryConstructionMode.Farm,
        GameManager.FactoryConstructionMode.GoldMine,
        GameManager.FactoryConstructionMode.SilverMine,
    };

    /// <summary>True pre "set 1" – surovinovú továreň, ktorej amount rastie systémom.</summary>
    public static bool IsRawProducer(FactoryInstance f)
    {
        if (f == null || f.Definition == null) return false;
        return RawProducers.Contains(f.Definition.Mode);
    }

    /// <summary>Denný prírastok amount podľa LevelSalary: 0→+3, 1→+6, 2→+10.</summary>
    public static int ProductionPerDay(int levelSalary)
    {
        switch (levelSalary)
        {
            case 1: return 6;
            case 2: return 10;
            default: return 3;   // 0 (a bezpečný fallback)
        }
    }

    /// <summary>
    /// Mesačná mzda = EmployeeSalary × koeficient(LevelSalary):
    /// 0 → ×1.0, 1 → ×1.5, 2 → ×2.0. Výsledok je celé číslo (×1.5 zaokrúhlené).
    /// </summary>
    public static long MonthlySalary(int employeeSalary, int levelSalary)
    {
        if (employeeSalary <= 0) return 0;
        switch (levelSalary)
        {
            case 1: return (long)Mathf.Round(employeeSalary * 1.5f);
            case 2: return employeeSalary * 2L;
            default: return employeeSalary;            // ×1.0
        }
    }

    // ── SET 2: degradácia preplnenej spracovateľskej továrne ──
    /// <summary>Koľko súvislých plných mesiacov spustí degradáciu (3 = "viac než 3, vrátane").</summary>
    public const int DecayFullMonthsThreshold = 3;

    /// <summary>O koľko klesá amount za herný deň počas degradácie (až do nuly).</summary>
    public const int DecayPerDay = 10;
}


// ============================================================================
// STAV JEDNEJ TOVÁRNE (produkcia / mzda / séria plnej kapacity / degradácia)
// ============================================================================

/// <summary>
/// Per-instancia ekonomický stav továrne. Sleduje sa cez VŠETKY sloty
/// (Load + UnLoad), pretože pri viacsurovinových továrňach (napr. Farm:
/// livestock + grain) sa amount mení pre každý slot zvlášť. Mzda sa však počíta
/// VŽDY raz za továreň, bez ohľadu na počet slotov.
/// </summary>
public class FactoryEconomyState
{
    private readonly FactoryInstance factory;
    private readonly bool isRawProducer;

    // Sledované sloty (Load aj UnLoad) + paralelné stavové polia.
    private readonly List<ResourceSlot> slots = new List<ResourceSlot>();
    private readonly bool[] isUnloadSlot;   // true = UnLoad (surovinová produkcia ide sem)
    private readonly int[] expected;        // naposledy "naše" videné množstvo (detekcia obchodu)
    private readonly bool[] changedThisMonth;
    private readonly int[] fullMonths;      // súvislé plné-konštantné mesiace
    private readonly bool[] decaying;       // beží degradácia tohto slotu?

    private bool occupiedThisMonth;

    /// <summary>True, ak na niektorom slote tejto továrne beží degradácia (pre HUD).</summary>
    public bool IsDecaying
    {
        get
        {
            for (int i = 0; i < decaying.Length; i++)
                if (decaying[i]) return true;
            return false;
        }
    }

    public FactoryEconomyState(FactoryInstance f)
    {
        factory = f;
        isRawProducer = EconomyTuning.IsRawProducer(f);

        if (f.Load != null)
            foreach (var s in f.Load) { slots.Add(s); }
        int loadCount = slots.Count;
        if (f.UnLoad != null)
            foreach (var s in f.UnLoad) { slots.Add(s); }

        int n = slots.Count;
        isUnloadSlot = new bool[n];
        expected = new int[n];
        changedThisMonth = new bool[n];
        fullMonths = new int[n];
        decaying = new bool[n];

        for (int i = 0; i < n; i++)
        {
            isUnloadSlot[i] = i >= loadCount;
            expected[i] = slots[i] != null ? slots[i].amount : 0;
        }
    }

    /// <summary>
    /// Per-frame detekcia VONKAJŠÍCH zmien amount (vlaky/vozidlá). Ak sa slot
    /// líši od nami naposledy videnej hodnoty, niekto ho zvonku zmenil →
    /// označí sa zmena (pre mzdu) a preruší sa séria plnej kapacity.
    /// Degradácia ani naša produkcia sem nezasiahnu – tie si "expected"
    /// aktualizujú samy hneď po zmene.
    /// </summary>
    public void ScanExternal()
    {
        if (factory.OccupancyFlag)
            occupiedThisMonth = true;

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null) continue;
            if (s.amount != expected[i])
            {
                changedThisMonth[i] = true;
                expected[i] = s.amount;
            }
        }
    }

    /// <summary>Denný tik: produkcia (set 1), spracovanie a degradácia (set 2).</summary>
    public void DailyTick()
    {
        if (factory.OccupancyFlag)
            occupiedThisMonth = true;

        // ── SET 1: produkcia surovín do UnLoad slotov ──
        // Len ak je továreň obsadená (OccupancyFlag) a výstavba dokončená.
        if (isRawProducer && factory.OccupancyFlag && factory.IsComplete)
        {
            int rate = EconomyTuning.ProductionPerDay(factory.LevelSalary);
            for (int i = 0; i < slots.Count; i++)
            {
                if (!isUnloadSlot[i]) continue;       // produkcia ide do UnLoad
                var s = slots[i];
                if (s == null || s.capacity <= 0) continue;

                int before = s.amount;
                s.amount = Mathf.Min(s.capacity, s.amount + rate);
                if (s.amount != before)
                    changedThisMonth[i] = true;       // produkcia = aktivita → mzda

                expected[i] = s.amount;               // naša zmena nie je "obchod"
            }
        }

        // ── SET 2: spracovanie vstupov na výstup ──
        // Spracovateľská továreň premení raz za herný deň vstupné suroviny na
        // výstupné v pomere 1:1 NA JEDNOTKU: aby vzniklo N jednotiek výstupu,
        // musí byť vo VŠETKÝCH vstupných slotoch aspoň N jednotiek a vo
        // VŠETKÝCH výstupných aspoň N voľného miesta. Denné tempo je rovnaké
        // ako pri ťažbe, takže LevelSalary ovplyvňuje spracovanie rovnako ako
        // ťažbu a mzdový systém platí pre obe sady tovární rovnako.
        //
        // Power Station je zvláštny prípad: nemá žiadny UnLoad slot, takže sa
        // vstup iba spotrebuje (uhlie sa spáli) a nič nevznikne – presne to,
        // čo od koncového spotrebiča čakáme.
        if (!isRawProducer && factory.OccupancyFlag && factory.IsComplete)
        {
            int batch = ConvertibleBatch(EconomyTuning.ProductionPerDay(factory.LevelSalary));
            if (batch > 0)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s == null || s.capacity <= 0) continue;

                    if (isUnloadSlot[i])
                        s.amount = Mathf.Min(s.capacity, s.amount + batch);
                    else
                        s.amount = Mathf.Max(0, s.amount - batch);

                    changedThisMonth[i] = true;   // spracovanie = aktivita → mzda
                    expected[i] = s.amount;       // naša zmena nie je "obchod"
                }
            }
        }

        // ── SET 2: degradácia preplnených slotov (-10/deň po nulu) ──
        if (!isRawProducer)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (!decaying[i]) continue;
                var s = slots[i];
                if (s == null) { decaying[i] = false; continue; }

                s.amount = Mathf.Max(0, s.amount - EconomyTuning.DecayPerDay);
                expected[i] = s.amount;               // degradácia nie je "obchod" ani "zmena pre mzdu"

                if (s.amount <= 0)
                {
                    decaying[i] = false;              // dosiahnutá nula → uvoľnené pre ďalší dovoz
                    fullMonths[i] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Koľko jednotiek vie spracovateľská továreň dnes premeniť. Výsledok je
    /// obmedzený denným tempom <paramref name="rate"/>, dostupným množstvom v
    /// KAŽDOM vstupnom slote a voľným miestom v KAŽDOM výstupnom slote –
    /// chýbajúca jedna vstupná surovina teda zastaví celú výrobu (Furniture
    /// Factory bez dosiek nevyrobí nič, aj keby mala plný plast, sklo a kovy).
    ///
    /// Vráti 0 aj vtedy, keď na ktoromkoľvek slote práve beží degradácia –
    /// preplnená továreň sa najprv musí vyprázdniť, než začne znovu spracúvať.
    /// Továreň bez jediného vstupného slotu nekonvertuje vôbec.
    /// </summary>
    private int ConvertibleBatch(int rate)
    {
        if (rate <= 0) return 0;

        int batch = rate;
        bool hasInput = false;

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null || s.capacity <= 0) continue;
            if (decaying[i]) return 0;

            if (isUnloadSlot[i])
            {
                batch = Mathf.Min(batch, s.FreeSpace);   // výstup sa musí zmestiť
            }
            else
            {
                hasInput = true;
                batch = Mathf.Min(batch, s.amount);      // vstup musí byť k dispozícii
            }

            if (batch <= 0) return 0;
        }

        return hasInput ? batch : 0;
    }

    /// <summary>
    /// Uzávierka mesiaca: vráti dlžnú mzdu (0 ak sa nemá strhnúť) a zároveň
    /// aktualizuje série plnej kapacity / spustí degradáciu. Na konci resetuje
    /// mesačné príznaky.
    /// </summary>
    public long CloseMonthAndGetSalary()
    {
        // Mzda – plná výška iba ak bola továreň cez mesiac obsadená A nastala
        // aspoň jedna zmena amount (produkcia alebo obchod). Nikdy nie alikvotne.
        bool changedAny = false;
        for (int i = 0; i < changedThisMonth.Length; i++)
            if (changedThisMonth[i]) { changedAny = true; break; }

        long salary = (occupiedThisMonth && changedAny)
            ? EconomyTuning.MonthlySalary(factory.EmployeeSalary, factory.LevelSalary)
            : 0;

        // Série plnej kapacity / degradácia (relevantné len pre set 2; set 1 sa
        // pri plnej kapacite jednoducho prestane meniť → žiadna mzda).
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null) continue;

            if (decaying[i])
            {
                // Degradácia práve beží – nechaj ju dobehnúť do nuly (DailyTick).
                fullMonths[i] = 0;
            }
            else
            {
                bool fullConstant = s.capacity > 0 && s.amount >= s.capacity && !changedThisMonth[i];
                if (fullConstant)
                {
                    fullMonths[i]++;
                    if (!isRawProducer && fullMonths[i] >= EconomyTuning.DecayFullMonthsThreshold)
                        decaying[i] = true;
                }
                else
                {
                    fullMonths[i] = 0;
                }
            }
        }

        // Reset mesačných príznakov.
        for (int i = 0; i < changedThisMonth.Length; i++)
            changedThisMonth[i] = false;
        occupiedThisMonth = false;

        return salary;
    }
}


// ============================================================================
// STAV ŽIVOTNOSTI / SERVISU JEDNÉHO DOPRAVNÉHO PROSTRIEDKU
// ============================================================================

/// <summary>
/// Odpočet času od vytvorenia dopravného prostriedku. ServicingInterval sa
/// počíta v HERNÝCH DŇOCH (recidivuje – po servise sa počítadlo vynuluje),
/// ServiceLife v HERNÝCH ROKOCH (koniec životnosti – pošle sa do depa raz).
/// Identita prostriedku = súradnice jeho depa (platí "1 depo = 1 vlak/vozidlo").
/// </summary>
public class FleetLifecycleState
{
    private readonly DateTime created;
    private int daysSinceService;
    private bool serviceLifeHandled;

    public FleetLifecycleState(DateTime createdDate)
    {
        created = createdDate;
        daysSinceService = 0;
        serviceLifeHandled = false;
    }

    /// <summary>
    /// Posunie deň a vráti true, ak sa má prostriedok TERAZ poslať do depa –
    /// pri dosiahnutí servisného intervalu (dni) ALEBO životnosti (roky).
    /// </summary>
    public bool AdvanceDayAndCheck(DateTime today, int serviceLifeYears, int servicingIntervalDays)
    {
        daysSinceService++;

        bool serviceDue = servicingIntervalDays > 0 && daysSinceService >= servicingIntervalDays;

        int yearsAlive = FullYearsBetween(created, today);
        bool lifeDue = serviceLifeYears > 0 && yearsAlive >= serviceLifeYears;

        bool send = serviceDue || (lifeDue && !serviceLifeHandled);

        if (serviceDue) daysSinceService = 0;     // servisný interval sa opakuje
        if (lifeDue) serviceLifeHandled = true;   // koniec životnosti rieš raz

        return send;
    }

    /// <summary>Počet celých rokov medzi dvomi dátumami (kalendárne).</summary>
    private static int FullYearsBetween(DateTime from, DateTime to)
    {
        int years = to.Year - from.Year;
        if (to.Month < from.Month || (to.Month == from.Month && to.Day < from.Day))
            years--;
        return years < 0 ? 0 : years;
    }
}
