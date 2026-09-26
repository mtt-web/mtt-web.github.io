using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using TMPro;


/// <summary>
/// GameMenuHUDPanel
/// ============================================================================
/// HORNÁ HUD LIŠTA hry – čisto zobrazovací (read-only) VIEW, rovnaký princíp
/// ako GameMenuUI pri captionoch času a konta. Nič nemení, len ČÍTA stav z
/// existujúcich systémov a prepisuje TMP labely:
///   • GameEconomy / GameClock      – konto, dátum (len ako vstup výpočtov),
///   • BudgetSystem                 – tržby / výdavky / výsledok aktuálneho roka,
///   • EconomySystem                – továrne v degradácii,
///   • ResourcePricing              – cenový násobiteľ,
///   • TrainSystem / VehicleSystem  – flotila (počty, náklad, depá, hodnota),
///   • FactoryRegistry              – továrne, produkcia, zásoby,
///   • IndicatrixAPI                – stanice a dĺžka tratí / ciest.
///
/// Stav konta a dátum sa tu ZÁMERNE NEZOBRAZUJÚ – sú v spodnom menu (GameMenuUI).
///
/// ── NAPOJENIE LABELOV ────────────────────────────────────────────────────
/// Každý ukazovateľ má vlastné [SerializeField] TMP_Text pole. VŠETKY sú
/// voliteľné – nepriradený label sa jednoducho preskočí. Ak ostanú polia v
/// Inspectore prázdne, skript si labely NÁJDE SÁM podľa názvu GameObjectu
/// (autoBindByName) pod searchRoot (default = tento GameObject, teda aj
/// BackgroundPanel a všetky jeho deti). Názov sa porovnáva bez ohľadu na
/// veľkosť písmen a bez prípon Text / Caption / Label, takže "TrainsText",
/// "TrainsTextCaption" aj "Trains" sa napoja na ten istý ukazovateľ.
/// Nerozpoznané labely vypíše do Console (verboseLog) – tie stačí priradiť
/// v Inspectore ručne.
///
/// ── OBNOVA V REÁLNOM ČASE ─────────────────────────────────────────────────
///   • okamžite na eventy: zmena konta, uplynutý deň / mesiac, predaj tovaru,
///   • rýchly cyklus (default 0,5 s): financie, flotila, obchod,
///   • pomalý cyklus (default 2 s): továrne, stanice, trate (prechádzajú mapu).
/// Text sa do TMP zapisuje len ak sa naozaj zmenil (žiadne zbytočné rebuildy).
///
/// ── MESAČNÉ ŠTATISTIKY ────────────────────────────────────────────────────
/// "Month" = skutočný pohyb konta od začiatku herného mesiaca (tržby − všetky
/// výdavky vrátane stavby tratí; mesačné zúčtovanie miezd a prevádzky patrí
/// ešte do mesiaca, ktorý práve skončil). Počty predajov / prepravené
/// jednotky sa zbierajú cez <see cref="ReportSale"/>, ktorú volajú
/// TrainSystem a VehicleSystem. Mesačné štatistiky sa neukladajú do save –
/// po načítaní hry začínajú od nuly (ročné hodnoty z BudgetSystem sa ukladajú).
/// </summary>
public class GameMenuHUDPanel : MonoBehaviour
{
    public static GameMenuHUDPanel instance;

    /// <summary>Druh dopravy pri predaji (pre podiel tržieb RAIL / ROAD).</summary>
    public enum TransportKind { Rail, Road }

    // =====================================================================
    // LABELY – FINANCIE
    // =====================================================================
    [Header("Finance")]
    [Tooltip("Pohyb konta od začiatku mesiaca (napr. 'MonthResultText').")]
    [SerializeField] private TMP_Text monthResultText;
    [Tooltip("Výsledok hospodárenia aktuálneho roka – rovnaké číslo ako v ročnej uzávierke.")]
    [SerializeField] private TMP_Text yearProfitText;
    [Tooltip("Tržby aktuálneho roka.")]
    [SerializeField] private TMP_Text yearRevenueText;
    [Tooltip("Výdavky aktuálneho roka.")]
    [SerializeField] private TMP_Text yearExpensesText;
    [Tooltip("Prognóza mesačných fixných nákladov (prevádzka flotily + mzdy).")]
    [SerializeField] private TMP_Text monthlyCostsText;
    [Tooltip("Prognóza mesačných miezd tovární.")]
    [SerializeField] private TMP_Text salariesText;
    [Tooltip("Cenový násobiteľ predaja (×1 = pôvodné ceny).")]
    [SerializeField] private TMP_Text priceMultiplierText;
    [Tooltip("Počet dní do mesačného zúčtovania.")]
    [SerializeField] private TMP_Text nextSettlementText;
    [Tooltip("Na koľko mesiacov vystačí konto pri aktuálnych fixných nákladoch.")]
    [SerializeField] private TMP_Text runwayText;
    [Tooltip("Posledný predaj (suma, surovina, RAIL/ROAD).")]
    [SerializeField] private TMP_Text lastSaleText;

    // =====================================================================
    // LABELY – FLOTILA
    // =====================================================================
    [Header("Fleet")]
    [SerializeField] private TMP_Text trainsText;
    [SerializeField] private TMP_Text vehiclesText;
    [SerializeField] private TMP_Text wagonsText;
    [Tooltip("Vyťaženosť nákladu: náklad / kapacita všetkých vagónov a vozidiel.")]
    [SerializeField] private TMP_Text cargoLoadText;
    [Tooltip("Počet vlakov a vozidiel odstavených v depe.")]
    [SerializeField] private TMP_Text inDepotText;
    [Tooltip("Nákupná hodnota celej flotily.")]
    [SerializeField] private TMP_Text fleetValueText;

    // =====================================================================
    // LABELY – INFRAŠTRUKTÚRA
    // =====================================================================
    [Header("Infrastructure")]
    [SerializeField] private TMP_Text trainStationsText;
    [SerializeField] private TMP_Text vehicleStationsText;
    [SerializeField] private TMP_Text factoriesText;
    [Tooltip("Počet tileov železnice.")]
    [SerializeField] private TMP_Text tracksText;
    [Tooltip("Počet tileov ciest.")]
    [SerializeField] private TMP_Text roadsText;

    // =====================================================================
    // LABELY – TOVÁRNE A SUROVINY
    // =====================================================================
    [Header("Factories & Resources")]
    [Tooltip("Vyrábajúce továrne / dostavané továrne.")]
    [SerializeField] private TMP_Text productionText;
    [Tooltip("Denná produkcia ťažobných tovární.")]
    [SerializeField] private TMP_Text dailyProductionText;
    [Tooltip("Tovar na skladoch tovární čakajúci na odvoz.")]
    [SerializeField] private TMP_Text stockText;
    [Tooltip("Továrne s plným výstupným skladom.")]
    [SerializeField] private TMP_Text fullStoragesText;
    [Tooltip("Obsadené spracovateľské továrne, ktorým chýba vstupná surovina.")]
    [SerializeField] private TMP_Text noInputText;
    [Tooltip("Továrne v degradácii (tovar sa kazí −10/deň).")]
    [SerializeField] private TMP_Text decayText;

    // =====================================================================
    // LABELY – OBCHOD (aktuálny mesiac)
    // =====================================================================
    [Header("Trade (this month)")]
    [SerializeField] private TMP_Text salesText;
    [SerializeField] private TMP_Text deliveredText;
    [SerializeField] private TMP_Text averageSaleText;
    [SerializeField] private TMP_Text railRoadShareText;
    [SerializeField] private TMP_Text topResourceText;

    // =====================================================================
    // LABEL – UPOZORNENIA
    // =====================================================================
    [Header("Alerts")]
    [SerializeField] private TMP_Text alertsText;

    // =====================================================================
    // NASTAVENIA
    // =====================================================================
    [Header("Settings")]
    [Tooltip("Nepriradené labely sa nájdu automaticky podľa názvu GameObjectu.")]
    [SerializeField] private bool autoBindByName = true;

    [Tooltip("Kde hľadať labely. Prázdne = tento GameObject (vrátane BackgroundPanel).")]
    [SerializeField] private Transform searchRoot;

    [Tooltip("Zapnuté: dva riadky – 'Trains:' a pod ním '7'. Vypnuté: len '7' (ak máš popisky v samostatných labeloch).")]
    [SerializeField] private bool showCaptions = true;

    [Tooltip("Interval rýchlej obnovy (financie, flotila, obchod) v sekundách.")]
    [SerializeField, Min(0.1f)] private float fastRefreshInterval = 0.5f;

    [Tooltip("Interval pomalej obnovy (továrne, stanice, trate) v sekundách.")]
    [SerializeField, Min(0.5f)] private float slowRefreshInterval = 2f;

    [Tooltip("Veľkosť tile mapy (IndicatrixAPI.GRID_SIZE) – len pre Tracks/Roads.")]
    [SerializeField] private int gridSize = 256;

    [Tooltip("Vypísať do Console, ktoré labely sa napojili a ktoré nie.")]
    [SerializeField] private bool verboseLog = true;

    [Header("References (optional – nájdu sa automaticky)")]
    [SerializeField] private GameClock gameClock;
    [SerializeField] private GameEconomy gameEconomy;

    // =====================================================================
    // FARBY A FORMÁTOVANIE (zhodné s BudgetSystem)
    // =====================================================================
    private const string ColorPositive = "#2ECC71";
    private const string ColorNegative = "#E74C3C";
    private const string ColorWarning = "#F39C12";
    private const string Currency = "CR";

    private static readonly CultureInfo DotGroupingCulture = CreateDotGroupingCulture();

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================
    private float fastTimer;
    private float slowTimer;
    private bool fastDirty = true;
    private bool slowDirty = true;
    private bool subscribed;

    // Mesačný pohyb konta.
    private long monthStartBalance;
    private bool monthSnapshotValid;
    private int monthRolledFrame = -1;

    // Mesačné obchodné štatistiky (plní ReportSale).
    private int monthSales;
    private long monthDelivered;
    private long monthRevenue;
    private long monthRailRevenue;
    private long monthRoadRevenue;
    private long[] monthRevenueByResource;
    private string lastSale;

    // Hodnoty vypočítané v rýchlom / pomalom cykle (používajú ich financie a alerty).
    private long fleetOperatingCosts;
    private long salaryEstimate;
    private int fullStorageCount;
    private int noInputCount;
    private int decayCount;

    // Auto-binding.
    private Dictionary<string, TMP_Text> labelsByName;
    private HashSet<TMP_Text> boundLabels;

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================
    void Awake()
    {
        instance = this;

        int resourceCount = 0;
        foreach (ResourceType v in Enum.GetValues(typeof(ResourceType)))
            resourceCount = Mathf.Max(resourceCount, (int)v + 1);
        monthRevenueByResource = new long[resourceCount];

        if (searchRoot == null) searchRoot = transform;
        BindLabels();
    }

    void Start()
    {
        TrySubscribe();
        SnapshotMonthStart();
        RefreshAll();
    }

    void OnEnable()
    {
        fastDirty = true;
        slowDirty = true;
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!subscribed) TrySubscribe();

        // unscaledDeltaTime – lišta sa obnovuje aj pri prípadnej pauze (timeScale 0).
        float dt = Time.unscaledDeltaTime;
        fastTimer += dt;
        slowTimer += dt;

        if (slowDirty || slowTimer >= slowRefreshInterval)
        {
            slowTimer = 0f;
            slowDirty = false;
            RefreshSlow();
            fastDirty = true;   // alerty a financie závisia od pomalých dát
        }

        if (fastDirty || fastTimer >= fastRefreshInterval)
        {
            fastTimer = 0f;
            fastDirty = false;
            RefreshFast();
        }
    }

    // =====================================================================
    // EVENTY
    // =====================================================================
    private void TrySubscribe()
    {
        if (subscribed) return;

        if (gameClock == null) gameClock = GameClock.instance != null
            ? GameClock.instance : UnityEngine.Object.FindFirstObjectByType<GameClock>();
        if (gameEconomy == null) gameEconomy = GameEconomy.instance != null
            ? GameEconomy.instance : UnityEngine.Object.FindFirstObjectByType<GameEconomy>();

        if (gameClock == null || gameEconomy == null) return;   // skúsi sa znova v Update

        gameEconomy.OnBalanceChanged += HandleBalanceChanged;
        gameClock.OnDayElapsed += HandleDayElapsed;
        gameClock.OnMonthElapsed += HandleMonthElapsed;
        gameClock.OnDateChanged += HandleDateChanged;
        subscribed = true;

        if (!monthSnapshotValid) SnapshotMonthStart();
    }

    private void Unsubscribe()
    {
        if (gameEconomy != null) gameEconomy.OnBalanceChanged -= HandleBalanceChanged;
        if (gameClock != null)
        {
            gameClock.OnDayElapsed -= HandleDayElapsed;
            gameClock.OnMonthElapsed -= HandleMonthElapsed;
            gameClock.OnDateChanged -= HandleDateChanged;
        }
        subscribed = false;
    }

    private void HandleBalanceChanged() => fastDirty = true;

    private void HandleDayElapsed()
    {
        fastDirty = true;   // odpočet dní do zúčtovania
        slowDirty = true;   // produkcia tovární sa zmenila
    }

    /// <summary>
    /// Nový herný mesiac. EconomySystem už v tom istom kroku (OnDayElapsed ide
    /// pred OnMonthElapsed) strhol mzdy a prevádzku za skončený mesiac, takže
    /// snapshot konta teraz začína čistý nový mesiac.
    /// </summary>
    private void HandleMonthElapsed()
    {
        monthRolledFrame = Time.frameCount;
        SnapshotMonthStart();
        ResetMonthTradeStats();
        fastDirty = true;
        slowDirty = true;
    }

    /// <summary>
    /// OnDateChanged prichádza aj pri bežnom prechode mesiaca (vtedy už bol
    /// snapshot urobený v HandleMonthElapsed v tom istom frame), ale aj pri
    /// ŠTARTE hry a pri NAČÍTANÍ uloženej hry (GameClock.LoadDate). V týchto
    /// dvoch prípadoch treba mesačné počítadlá začať odznova od aktuálneho konta.
    /// </summary>
    private void HandleDateChanged()
    {
        if (Time.frameCount != monthRolledFrame)
        {
            SnapshotMonthStart();
            ResetMonthTradeStats();
        }
        fastDirty = true;
        slowDirty = true;
    }

    private void SnapshotMonthStart()
    {
        if (gameEconomy == null) return;
        monthStartBalance = gameEconomy.Balance;
        monthSnapshotValid = true;
    }

    private void ResetMonthTradeStats()
    {
        monthSales = 0;
        monthDelivered = 0;
        monthRevenue = 0;
        monthRailRevenue = 0;
        monthRoadRevenue = 0;
        if (monthRevenueByResource != null)
            Array.Clear(monthRevenueByResource, 0, monthRevenueByResource.Length);
    }

    // =====================================================================
    // VEREJNÉ API – hlásenie predaja (volá TrainSystem / VehicleSystem)
    // =====================================================================

    /// <summary>
    /// Zaeviduje úspešný predaj do mesačných štatistík lišty. Bezpečné volať
    /// vždy – ak HUD v scéne nie je (alebo ešte nenabehol), nič sa nestane.
    /// </summary>
    public static void ReportSale(TransportKind kind, ResourceType resource, int unloadedUnits, int revenue)
    {
        if (instance != null)
            instance.RegisterSale(kind, resource, unloadedUnits, revenue);
    }

    private void RegisterSale(TransportKind kind, ResourceType resource, int unloadedUnits, int revenue)
    {
        monthSales++;
        if (unloadedUnits > 0) monthDelivered += unloadedUnits;

        if (revenue > 0)
        {
            monthRevenue += revenue;
            if (kind == TransportKind.Rail) monthRailRevenue += revenue;
            else monthRoadRevenue += revenue;

            int r = (int)resource;
            if (r > 0 && r < monthRevenueByResource.Length)
                monthRevenueByResource[r] += revenue;
        }

        string kindText = kind == TransportKind.Rail ? "RAIL" : "ROAD";
        lastSale = $"{Colored("+" + Num(revenue) + " " + Currency, ColorPositive)} · {resource} ({kindText})";

        fastDirty = true;
    }

    /// <summary>Okamžité prekreslenie celej lišty (napr. po načítaní hry).</summary>
    public void RefreshAll()
    {
        RefreshSlow();
        RefreshFast();
    }

    // =====================================================================
    // RÝCHLY CYKLUS – flotila, obchod, financie, alerty
    // =====================================================================
    private void RefreshFast()
    {
        RefreshFleet();
        RefreshTrade();
        RefreshFinance();
        RefreshAlerts();
    }

    private void RefreshFleet()
    {
        int trains = 0, wagons = 0, vehicles = 0, trainsInDepot = 0, vehiclesInDepot = 0;
        long cargo = 0, capacity = 0, value = 0, operating = 0;

        TrainSystem ts = TrainSystem.instance;
        if (ts != null)
        {
            var infos = ts.GetActiveTrainInfos();
            for (int i = 0; i < infos.Count; i++)
            {
                var consist = infos[i].Consist;
                if (consist == null) continue;

                trains++;
                value += Mathf.Max(0, consist.Cost);
                operating += Mathf.Max(0, consist.OperatingCosts);

                foreach (var w in consist.Wagons)
                {
                    if (w == null) continue;
                    wagons++;
                    cargo += Mathf.Max(0, w.CurrentCapacity);
                    capacity += Mathf.Max(0, w.MaximumCapacity);
                    value += Mathf.Max(0, w.Cost);
                }

                var td = ts.GetTrain(infos[i].DepotX, infos[i].DepotZ);
                if (td != null && td.isAtDepot) trainsInDepot++;
            }
        }

        VehicleSystem vs = VehicleSystem.instance;
        if (vs != null)
        {
            var infos = vs.GetActiveVehicleInfos();
            for (int i = 0; i < infos.Count; i++)
            {
                var inst = infos[i].Instance;
                if (inst == null) continue;

                vehicles++;
                cargo += Mathf.Max(0, inst.CurrentCapacity);
                capacity += Mathf.Max(0, inst.MaximumCapacity);
                value += Mathf.Max(0, inst.Cost);
                operating += Mathf.Max(0, inst.OperatingCosts);

                var vd = vs.GetVehicle(infos[i].DepotX, infos[i].DepotZ);
                if (vd != null && vd.isAtDepot) vehiclesInDepot++;
            }
        }

        fleetOperatingCosts = operating;

        SetText(trainsText, Caption("Trains", Num(trains)));
        SetText(vehiclesText, Caption("Vehicles", Num(vehicles)));
        SetText(wagonsText, Caption("Wagons", Num(wagons)));

        string load = capacity > 0 ? Mathf.RoundToInt(100f * cargo / capacity) + " %" : "–";
        SetText(cargoLoadText, Caption("Cargo load", load));

        SetText(inDepotText, Caption("In depot", $"{trainsInDepot} / {vehiclesInDepot}"));
        SetText(fleetValueText, Caption("Fleet value", Num(value) + " " + Currency));
    }

    private void RefreshTrade()
    {
        SetText(salesText, Caption("Sales", Num(monthSales)));
        SetText(deliveredText, Caption("Delivered", Num(monthDelivered) + " u"));

        string avg = monthSales > 0 ? Num(monthRevenue / monthSales) + " " + Currency : "–";
        SetText(averageSaleText, Caption("Avg. sale", avg));

        string share = "–";
        if (monthRevenue > 0)
        {
            int railPct = Mathf.RoundToInt(100f * monthRailRevenue / monthRevenue);
            share = $"{railPct} % / {100 - railPct} %";
        }
        SetText(railRoadShareText, Caption("RAIL / ROAD", share));

        string top = "–";
        long best = 0;
        for (int r = 1; r < monthRevenueByResource.Length; r++)
        {
            if (monthRevenueByResource[r] > best)
            {
                best = monthRevenueByResource[r];
                top = $"{(ResourceType)r} ({Num(best)} {Currency})";
            }
        }
        SetText(topResourceText, Caption("Top resource", top));

        SetText(lastSaleText, Caption("Last sale", string.IsNullOrEmpty(lastSale) ? "–" : lastSale));
    }

    private void RefreshFinance()
    {
        long balance = gameEconomy != null ? gameEconomy.Balance : 0;

        // Pohyb konta od začiatku mesiaca.
        if (monthResultText != null)
        {
            string v = monthSnapshotValid ? SignedMoney(balance - monthStartBalance) : "–";
            SetText(monthResultText, Caption("Month", v));
        }

        // Ročné hodnoty – zhodné s ročnou uzávierkou.
        BudgetSystem bs = BudgetSystem.instance;
        if (bs != null)
        {
            SetText(yearProfitText, Caption("Year profit", SignedMoney(bs.YearNetResult)));
            SetText(yearRevenueText, Caption("Revenue", SignedMoney(bs.YearRevenue)));
            SetText(yearExpensesText, Caption("Expenses", SignedMoney(-bs.YearExpenses)));
        }

        long monthlyCosts = fleetOperatingCosts + salaryEstimate;
        SetText(monthlyCostsText, Caption("Monthly costs", SignedMoney(-monthlyCosts)));
        SetText(salariesText, Caption("Salaries", SignedMoney(-salaryEstimate)));

        int mult = Mathf.Max(1, ResourcePricing.Multiplier);
        string multText = mult > 1 ? Colored("×" + mult, ColorWarning) : "×1";
        SetText(priceMultiplierText, Caption("Prices", multText));

        if (nextSettlementText != null && gameClock != null)
        {
            int days = DateTime.DaysInMonth(gameClock.Year, gameClock.Month) - gameClock.Day + 1;
            SetText(nextSettlementText, Caption("Settlement", $"{days} d"));
        }

        if (runwayText != null)
        {
            string rw;
            if (balance <= 0) rw = Colored("0 mo.", ColorNegative);
            else if (monthlyCosts <= 0) rw = "∞";
            else
            {
                long months = balance / monthlyCosts;
                rw = months > 999 ? "> 999 mo." : months + " mo.";
                if (months < 3) rw = Colored(rw, ColorWarning);
            }
            SetText(runwayText, Caption("Reserve", rw));
        }
    }

    private void RefreshAlerts()
    {
        if (alertsText == null) return;

        var alerts = new List<string>(5);
        long balance = gameEconomy != null ? gameEconomy.Balance : 0;
        int mult = Mathf.Max(1, ResourcePricing.Multiplier);

        if (balance < 0) alerts.Add(Colored("Negative balance!", ColorNegative));
        if (mult > 1) alerts.Add(Colored($"Prices ×{mult} (debt)", ColorWarning));
        if (decayCount > 0) alerts.Add(Colored($"Decay: {decayCount}", ColorNegative));
        if (noInputCount > 0) alerts.Add(Colored($"No input: {noInputCount}", ColorWarning));
        if (fullStorageCount > 0) alerts.Add(Colored($"Full storage: {fullStorageCount}", ColorWarning));

        string text = alerts.Count == 0
            ? Colored("No alerts", ColorPositive)
            : alerts[0] + (alerts.Count > 1 ? $" (+{alerts.Count - 1})" : "");

        SetText(alertsText, Caption("Alerts", text));
    }

    // =====================================================================
    // POMALÝ CYKLUS – továrne, stanice, trate
    // =====================================================================
    private void RefreshSlow()
    {
        RefreshFactories();
        RefreshInfrastructure();
    }

    private void RefreshFactories()
    {
        int total = 0, underConstruction = 0, completed = 0, working = 0;
        int dailyProduction = 0, full = 0, noInput = 0;
        long stock = 0, salaries = 0;

        var all = FactoryRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            FactoryInstance f = all[i];
            if (f == null) continue;
            total++;

            if (!f.IsComplete) { underConstruction++; continue; }
            completed++;

            bool raw = EconomyTuning.IsRawProducer(f);
            bool occupied = f.OccupancyFlag;

            // Výstupy: zásoby, plné sklady, voľné výstupy (pre ťažbu).
            bool anyOutputFull = false;
            int freeOutputs = 0;
            if (f.UnLoad != null)
            {
                foreach (var s in f.UnLoad)
                {
                    if (s == null || s.capacity <= 0) continue;
                    stock += Mathf.Max(0, s.amount);
                    if (s.IsFull) anyOutputFull = true;
                    else freeOutputs++;
                }
            }
            if (anyOutputFull) full++;

            // Vstupy: chýbajúca surovina zastaví celú spracovateľskú výrobu.
            bool missingInput = false;
            if (!raw && f.Load != null && f.Load.Count > 0)
            {
                foreach (var s in f.Load)
                    if (s != null && s.capacity > 0 && s.amount <= 0) { missingInput = true; break; }
            }
            if (missingInput && occupied) noInput++;

            // Vyrába továreň práve teraz?
            int rate = EconomyTuning.ProductionPerDay(f.LevelSalary);
            if (raw)
            {
                if (occupied && freeOutputs > 0)
                {
                    working++;
                    dailyProduction += rate * freeOutputs;
                }
            }
            else if (occupied && !missingInput && !anyOutputFull)
            {
                working++;
            }

            // Odhad miezd (EconomySystem ich strhne len pri aktivite v mesiaci).
            if (occupied && f.EmployeeSalary > 0)
                salaries += EconomyTuning.MonthlySalary(f.EmployeeSalary, f.LevelSalary);
        }

        salaryEstimate = salaries;
        fullStorageCount = full;
        noInputCount = noInput;
        decayCount = EconomySystem.instance != null ? EconomySystem.instance.DecayingFactoryCount : 0;

        string factories = Num(total) + (underConstruction > 0 ? $" (+{underConstruction} building)" : "");
        SetText(factoriesText, Caption("Factories", factories));
        SetText(productionText, Caption("Production", $"{working} / {completed}"));
        SetText(dailyProductionText, Caption("Mining", $"+{Num(dailyProduction)} / day"));
        SetText(stockText, Caption("Stock", Num(stock) + " u"));
        SetText(fullStoragesText, Caption("Full storage", WarnCount(full, ColorWarning)));
        SetText(noInputText, Caption("No input", WarnCount(noInput, ColorWarning)));
        SetText(decayText, Caption("Decay", WarnCount(decayCount, ColorNegative)));
    }

    private void RefreshInfrastructure()
    {
        IndicatrixAPI api = IndicatrixAPI.instance;
        if (api == null) return;

        if (trainStationsText != null)
            SetText(trainStationsText, Caption("Train stations",
                Num(api.GetStationCount(IndicatrixAPI.TileCategory.Rail))));

        if (vehicleStationsText != null)
            SetText(vehicleStationsText, Caption("Vehicle stations",
                Num(api.GetStationCount(IndicatrixAPI.TileCategory.Road))));

        // Trate / cesty – jeden prechod mapou, len ak je aspoň jeden z labelov použitý.
        if (tracksText == null && roadsText == null) return;

        int rail = 0, road = 0;
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                var td = api.GetTileByIndexAny(x, z);
                if (td.tileID == 0) continue;

                switch (td.category)
                {
                    case IndicatrixAPI.TileCategory.Rail: rail++; break;
                    case IndicatrixAPI.TileCategory.Road: road++; break;
                    case IndicatrixAPI.TileCategory.RailRoadCrossing: rail++; road++; break;
                }
            }
        }

        SetText(tracksText, Caption("Tracks", Num(rail) + " tiles"));
        SetText(roadsText, Caption("Roads", Num(road) + " tiles"));
    }

    // =====================================================================
    // AUTO-BINDING LABELOV PODĽA NÁZVU
    // =====================================================================
    private void BindLabels()
    {
        labelsByName = new Dictionary<string, TMP_Text>();
        boundLabels = new HashSet<TMP_Text>();

        if (autoBindByName && searchRoot != null)
        {
            foreach (var t in searchRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                string key = NormalizeName(t.gameObject.name);
                if (!labelsByName.ContainsKey(key)) labelsByName[key] = t;
            }
        }

        // Finance
        monthResultText = Bind(monthResultText, "monthresult", "monthlyresult", "monthprofit", "monthlyprofit", "monthcashflow", "cashflow", "month");
        yearProfitText = Bind(yearProfitText, "yearprofit", "yearresult", "annualprofit", "yearlyprofit", "profit", "netresult");
        yearRevenueText = Bind(yearRevenueText, "yearrevenue", "revenue", "yearincome", "income", "annualrevenue", "yearlyrevenue", "revenues");
        yearExpensesText = Bind(yearExpensesText, "yearexpenses", "expenses", "annualexpenses", "yearlyexpenses", "yearcosts");
        monthlyCostsText = Bind(monthlyCostsText, "monthlycosts", "fixedcosts", "monthlyfixedcosts", "operatingcosts", "monthcosts", "costs", "upkeep");
        salariesText = Bind(salariesText, "salaries", "wages", "monthlysalaries", "employeesalaries");
        priceMultiplierText = Bind(priceMultiplierText, "pricemultiplier", "multiplier", "prices", "pricesmultiplier");
        nextSettlementText = Bind(nextSettlementText, "nextsettlement", "settlement", "settlementin", "daystosettlement");
        runwayText = Bind(runwayText, "runway", "reserve", "reserves");
        lastSaleText = Bind(lastSaleText, "lastsale", "lasttrade", "lasttransaction");

        // Fleet
        trainsText = Bind(trainsText, "trains", "traincount", "numberoftrains");
        vehiclesText = Bind(vehiclesText, "vehicles", "vehiclecount", "numberofvehicles");
        wagonsText = Bind(wagonsText, "wagons", "wagoncount");
        cargoLoadText = Bind(cargoLoadText, "cargoload", "loadfactor", "cargo", "load", "capacityusage");
        inDepotText = Bind(inDepotText, "indepot", "atdepot", "depot", "depots", "inservice");
        fleetValueText = Bind(fleetValueText, "fleetvalue", "fleetworth");

        // Infrastructure
        trainStationsText = Bind(trainStationsText, "trainstations", "railstations", "stationsrail", "trainstation");
        vehicleStationsText = Bind(vehicleStationsText, "vehiclestations", "roadstations", "stationsroad", "vehiclestation");
        factoriesText = Bind(factoriesText, "factories", "factorycount", "numberoffactories");
        tracksText = Bind(tracksText, "tracks", "rails", "railtiles", "tracklength", "raillength");
        roadsText = Bind(roadsText, "roads", "roadtiles", "roadlength");

        // Factories & resources
        productionText = Bind(productionText, "production", "activefactories", "workingfactories", "factoriesactive");
        dailyProductionText = Bind(dailyProductionText, "dailyproduction", "productionperday", "mining", "extraction");
        stockText = Bind(stockText, "stock", "stocks", "storage", "instock", "goods", "resources");
        fullStoragesText = Bind(fullStoragesText, "fullstorages", "fullstorage", "full", "fullwarehouses");
        noInputText = Bind(noInputText, "noinput", "missinginput", "withoutinput");
        decayText = Bind(decayText, "decay", "degradation", "decaying");

        // Trade
        salesText = Bind(salesText, "sales", "salescount", "trades", "transactions");
        deliveredText = Bind(deliveredText, "delivered", "transported", "cargodelivered", "deliveredunits");
        averageSaleText = Bind(averageSaleText, "averagesale", "avgsale", "averagerevenue");
        railRoadShareText = Bind(railRoadShareText, "railroadshare", "revenueshare", "share", "railroad");
        topResourceText = Bind(topResourceText, "topresource", "bestresource", "topgoods");

        // Alerts
        alertsText = Bind(alertsText, "alerts", "alert", "warnings", "warning");

        if (!verboseLog) return;

        foreach (var kvp in labelsByName)
        {
            if (!boundLabels.Contains(kvp.Value))
                Debug.LogWarning($"[GameMenuHUDPanel] Label '{kvp.Value.gameObject.name}' nebol rozpoznaný – " +
                                 "priraď ho v Inspectore ručne (alebo ide o statický popisok).", kvp.Value);
        }
        Debug.Log($"[GameMenuHUDPanel] Napojených labelov: {boundLabels.Count}.");
    }

    private TMP_Text Bind(TMP_Text current, params string[] aliases)
    {
        if (current != null) { boundLabels.Add(current); return current; }

        foreach (string a in aliases)
        {
            if (labelsByName.TryGetValue(a, out var t) && !boundLabels.Contains(t))
            {
                boundLabels.Add(t);
                return t;
            }
        }
        return null;
    }

    /// <summary>"TrainsTextCaption" → "trains", "VehicleStations" → "vehiclestations".</summary>
    private static string NormalizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);

        string s = sb.ToString();
        string[] suffixes = { "textcaption", "caption", "text", "label", "tmp", "value" };

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string suf in suffixes)
            {
                if (s.Length > suf.Length && s.EndsWith(suf))
                {
                    s = s.Substring(0, s.Length - suf.Length);
                    changed = true;
                }
            }
        }

        if (s.Length > 3 && s.StartsWith("hud")) s = s.Substring(3);
        return s;
    }

    // =====================================================================
    // FORMÁTOVANIE
    // =====================================================================
    private static void SetText(TMP_Text label, string value)
    {
        if (label == null) return;
        if (label.text != value) label.text = value;   // prepis len pri zmene
    }

    /// <summary>
    /// Text labelu na DVA riadky: 1. riadok popis s dvojbodkou ("Trains:"),
    /// 2. riadok hodnota ("7"). Pri vypnutom showCaptions len hodnota.
    /// </summary>
    private string Caption(string caption, string value)
        => showCaptions ? $"{caption}:\n{value}" : value;

    private static string Num(long value) => value.ToString("#,0", DotGroupingCulture);

    private static string Colored(string text, string color) => $"<color={color}>{text}</color>";

    /// <summary>"+1.234 CR" zelenou, "-1.234 CR" červenou, "0 CR" neutrálne.</summary>
    private static string SignedMoney(long amount)
    {
        if (amount == 0) return "0 " + Currency;
        string sign = amount > 0 ? "+" : "-";
        return Colored($"{sign}{Num(Math.Abs(amount))} {Currency}", amount > 0 ? ColorPositive : ColorNegative);
    }

    private static string WarnCount(int count, string color)
        => count > 0 ? Colored(count.ToString(), color) : "0";

    private static CultureInfo CreateDotGroupingCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.NumberGroupSizes = new[] { 3 };
        return culture;
    }
}
