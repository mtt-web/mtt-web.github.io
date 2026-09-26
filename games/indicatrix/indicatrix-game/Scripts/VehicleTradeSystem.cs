using UnityEngine;
using Game.VehicleStock;

/// <summary>
/// VehicleTradeSystem.cs
/// ------------------------------------------------------------------
/// TRANSAKČNÝ ALGORITMUS PRE CESTNÉ VOZIDLÁ – výmena tovaru medzi
/// vozidlom a továrňami stanice.
///
/// Toto je CESTNÝ EKVIVALENT triedy TradeSystem (ktorá rieši obchod pre
/// VLAKY). Princíp obchodu je medzi vlakmi a vozidlami ROVNAKÝ – kontroluje
/// sa len surovina a množstvo – líši sa iba dátová štruktúra "dopravcu":
///
///     VLAK    = TrainInstance + List&lt;WagonInstance&gt;  (náklad nesú vagóny)
///     VOZIDLO = VehicleInstance                       (JEDEN kus, žiadne vagóny)
///
/// Vozidlo sa správa "akoby jeden vagón": má MaximumCapacity a premenlivý
/// CurrentCapacity (VehicleStock.cs). Preto je cestná transakcia jednoduchšia
/// – tam, kde TradeSystem prechádza vagón po vagóne (FillWagons / UnloadWagons),
/// tu pracujeme s jediným kusom (FillVehicle / UnloadVehicle).
///
/// ------------------------------------------------------------------
/// AKO TO FUNGUJE – KONCEPT (zhodný s TradeSystem)
///
/// Vozidlo má typ (VehicleType), ktorý nesie ResourceType cez ČÍSELNÉ ID:
///     VehicleType.Id == (int)ResourceType
///     ("Coal Truck" #1 → ResourceType.Coal,  "Wood Truck" #2 → Wood)
/// Mapovanie je 1:1 – presne ako WagonType.Id v TradeSystem.
///
/// Stanica (StationInstance, evidovaná v RoadStationRegistry) eviduje
/// 0..N tovární vo svojej 9×9 zóne. Každá továreň má pre danú surovinu buď
/// VÝDAJOVÝ slot (UnLoad – surovinu produkuje) alebo PRÍJMOVÝ slot
/// (Load – surovinu spotrebúva).
///
/// Keď vozidlo zastane na stanici, pre svoj typ suroviny urobí:
///
///   KROK 1 – VYLOŽENIE (Unload):
///     Ak je v zóne stanice továreň, ktorá danú surovinu PRIJÍMA (Load slot),
///     a vo vozidle niečo je → vyloží náklad do tej továrne.
///
///   KROK 2 – NALOŽENIE (Load):
///     Ak je v zóne stanice továreň, ktorá danú surovinu VYDÁVA (UnLoad slot),
///     a vo vozidle je voľné miesto → naloží z tej továrne do vozidla.
///
/// Obchod je ÚSPEŠNÝ ("Predaj prebehol") IBA vtedy, keď sa pri zastávke
/// SKUTOČNE VYLOŽILA aspoň 1 jednotka tovaru do príjmovej továrne.
/// Naloženie surovín do vozidla NEROBÍ obchod úspešným – je to len presun,
/// príprava na predaj na ďalšej stanici (rovnaké pravidlo ako pri vlakoch).
/// ------------------------------------------------------------------
/// </summary>
public static class VehicleTradeSystem
{
    // Cena predaja sa už NEráta paušálne za jednotku. Kompletný cenník (cena
    // ZA PLNÉ VOZIDLO podľa suroviny + alikvotná časť pri neúplnom vozidle +
    // dynamický násobiteľ pri zápornom konte) je centralizovaný v
    // ResourcePricing.RoadSaleRevenue(...). Pozri krok 4 v Execute(...).

    // ==================================================================
    // VÝSLEDOK TRANSAKCIE
    // ==================================================================

    /// <summary>
    /// Výsledok jednej zastávky vozidla na stanici. VehicleSystem si z neho
    /// prečíta Success + Revenue a vypíše príslušný Debug.Log.
    /// (Štruktúra zhodná s TradeSystem.TradeResult.)
    /// </summary>
    public struct TradeResult
    {
        /// <summary>True, ak sa vyložila (predala) aspoň 1 jednotka tovaru.</summary>
        public bool Success;

        /// <summary>Koľko jednotiek sa naložilo z továrne do vozidla.</summary>
        public int LoadedUnits;

        /// <summary>Koľko jednotiek sa vyložilo z vozidla do továrne.</summary>
        public int UnloadedUnits;

        /// <summary>Celkový výnos z obchodu (euro).</summary>
        public int Revenue;

        /// <summary>Stručný textový dôvod (najmä pri neúspechu) – na debug.</summary>
        public string Reason;

        /// <summary>Celkový počet presunutých jednotiek (load + unload).</summary>
        public int MovedUnits => LoadedUnits + UnloadedUnits;
    }

    // ==================================================================
    // HLAVNÝ VSTUP – vykonaj transakciu pre vozidlo na stanici
    // ==================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi vozidlom <paramref name="vehicle"/> a
    /// továrňami v 9×9 zóne stanice <paramref name="station"/>.
    ///
    /// Postup (analogický k TradeSystem.Execute):
    ///   0) Validácia (vozidlo existuje, stanica eviduje aspoň 1 továreň).
    ///   1) Zisti surovinu, s ktorou vozidlo pracuje (z typu vozidla).
    ///   2) UNLOAD – ak je v zóne príjmová továreň, vyloží náklad do nej.
    ///   3) LOAD   – ak je v zóne výdajová továreň, naloží z nej do vozidla.
    ///   4) Vyhodnoť úspech a vyčísli cenu.
    ///
    /// Metóda je čisto dátová (žiadne Unity GameObject API), takže sa dá
    /// volať z hlavného vlákna v Update slučke VehicleSystem-u bez rizika.
    /// </summary>
    public static TradeResult Execute(VehicleInstance vehicle, StationInstance station)
    {
        var result = new TradeResult
        {
            Success = false,
            LoadedUnits = 0,
            UnloadedUnits = 0,
            Revenue = 0,
            Reason = ""
        };

        // ---- 0) VALIDÁCIA -------------------------------------------------
        if (vehicle == null)
        {
            result.Reason = "vozidlo nemá dátovú štruktúru (VehicleInstance == null)";
            return result;
        }
        if (station == null)
        {
            result.Reason = "neznáma stanica";
            return result;
        }
        if (!station.HasFactories)
        {
            result.Reason = "stanica neeviduje žiadnu továreň v 9×9 zóne";
            return result;
        }

        // ---- 1) SUROVINA VOZIDLA -----------------------------------------
        ResourceType vehicleResource = VehicleResource(vehicle);
        if (vehicleResource == ResourceType.None)
        {
            result.Reason = $"typ vozidla '{vehicle.Type}' nemá priradenú surovinu";
            return result;
        }

        // ---- 2) UNLOAD (vyloženie do príjmovej továrne) -------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // PRIJÍMA. Pri čisto výdajovej stanici sa tento krok preskočí –
        // tým je ošetrený "bonus": vozidlo môže prejsť cez výdajovú stanicu
        // a náklad mu zostane.
        FactoryInstance consumer = station.FindConsumer(vehicleResource);
        if (consumer != null)
        {
            ResourceSlot loadSlot = consumer.GetLoadSlot(vehicleResource);
            result.UnloadedUnits = UnloadVehicle(vehicle, loadSlot);
            if (result.UnloadedUnits > 0)
                Debug.Log($"[VehicleTradeSystem] Vyložené {result.UnloadedUnits}× {vehicleResource} " +
                          $"do '{consumer.Name}' (slot {loadSlot}).");
        }

        // ---- 3) LOAD (naloženie z výdajovej továrne) ----------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // VYDÁVA. FillVehicle dopĺňa do VOĽNÉHO miesta, takže existujúci
        // náklad (z inej stanice) zostáva a nová surovina sa k nemu pripočíta.
        FactoryInstance supplier = station.FindSupplier(vehicleResource);
        if (supplier != null)
        {
            ResourceSlot unloadSlot = supplier.GetUnLoadSlot(vehicleResource);
            result.LoadedUnits = FillVehicle(vehicle, unloadSlot);
            if (result.LoadedUnits > 0)
                Debug.Log($"[VehicleTradeSystem] Naložené {result.LoadedUnits}× {vehicleResource} " +
                          $"z '{supplier.Name}' (slot {unloadSlot}).");
        }

        // ---- 4) VYHODNOTENIE ---------------------------------------------
        // DÔLEŽITÉ: peniaze plynú IBA z VYLOŽENIA (predaja tovaru továrni).
        // Naloženie surovín z výdajovej továrne do vozidla je len presun –
        // NEGENERUJE žiadny výnos a samo o sebe nerobí obchod úspešným.
        result.Success = result.UnloadedUnits > 0;

        if (result.Success)
        {
            // Cena ZA PLNÉ VOZIDLO podľa suroviny, alikvotne podľa reálne
            // predaného (vyloženého) množstva, prenásobená aktuálnym cenovým
            // násobiteľom konta. Menovateľom alikvotného prepočtu je
            // MaximumCapacity vozidla.
            result.Revenue = ResourcePricing.RoadSaleRevenue(
                vehicleResource, result.UnloadedUnits, vehicle.MaximumCapacity);
        }
        else
        {
            // Žiadny PREDAJ (vyloženie) – upresni dôvod pre debug.
            if (consumer == null)
            {
                if (result.LoadedUnits > 0)
                    result.Reason = $"naložené {result.LoadedUnits}× {vehicleResource}, " +
                                    $"ale stanica nemá príjmovú továreň – predaj sa nekoná";
                else if (supplier == null)
                    result.Reason = $"v zóne stanice nie je továreň pre surovinu {vehicleResource}";
                else
                    result.Reason = "stanica nemá príjmovú továreň – nie je kam predať";
            }
            else
            {
                result.Reason = "vozidlo prázdne alebo príjmová továreň plná – nič sa nevyložilo";
            }
        }

        return result;
    }

    // ==================================================================
    // POMOCNÉ – mapovanie typu vozidla na surovinu
    // ==================================================================

    /// <summary>
    /// Vráti surovinu, s ktorou vie dané vozidlo pracovať.
    ///
    /// Mapovanie je cez ČÍSELNÉ ID: VehicleType.Id == (int)ResourceType.
    /// Katalógy sú zámerne zladené (rovnako ako pri vlakoch):
    ///     "Coal Truck" má Id 1  →  ResourceType.Coal == 1
    ///     "Wood Truck" má Id 2  →  ResourceType.Wood == 2
    ///
    /// Ak by v budúcnosti ID nesedeli, stačí toto mapovanie nahradiť
    /// explicitnou tabuľkou (Dictionary&lt;int, ResourceType&gt;).
    /// </summary>
    public static ResourceType VehicleResource(VehicleInstance vehicle)
    {
        if (vehicle == null) return ResourceType.None;

        int id = vehicle.Type.Id;
        if (System.Enum.IsDefined(typeof(ResourceType), id))
            return (ResourceType)id;

        return ResourceType.None;
    }

    // ==================================================================
    // POMOCNÉ – LOAD: naplnenie vozidla z výdajového slotu továrne
    // ==================================================================

    /// <summary>
    /// Naplnenie vozidla surovinou zo skladu továrne
    /// (<paramref name="source"/> = UnLoad slot).
    ///
    /// ALGORITMUS:
    ///   Vozidlo je JEDEN kus – naplní sa do svojej maximálnej kapacity,
    ///   pokým je v továrni surovina k dispozícii. Naloží sa
    ///   min(voľné miesto vo vozidle, dostupné v továrni).
    ///
    ///   (Toto je zjednodušená verzia TradeSystem.FillWagons, ktorý
    ///   prechádza vagón po vagóne – vozidlo má len jeden "vagón".)
    ///
    /// Rešpektuje BONUS: ak vo vozidle už nejaká surovina je (z inej
    /// stanice), dopĺňa sa len jeho VOĽNÉ miesto – existujúce množstvo
    /// sa nemaže.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE naložilo (a odpočítalo z továrne).
    /// </summary>
    public static int FillVehicle(VehicleInstance vehicle, ResourceSlot source)
    {
        if (vehicle == null || source == null) return 0;
        if (source.IsEmpty) return 0;

        // Voľné miesto vo vozidle.
        int free = vehicle.MaximumCapacity - vehicle.CurrentCapacity;
        if (free <= 0) return 0;            // vozidlo plné

        // Do vozidla ide min(voľné miesto, dostupné v továrni).
        int toLoad = Mathf.Min(free, source.amount);
        if (toLoad <= 0) return 0;

        vehicle.CurrentCapacity += toLoad;

        // Odpočítaj naloženú surovinu zo skladu továrne.
        // ResourceSlot.Remove je orezané dostupným množstvom (bezpečné).
        source.Remove(toLoad);

        return toLoad;
    }

    // ==================================================================
    // POMOCNÉ – UNLOAD: vyloženie vozidla do príjmového slotu továrne
    // ==================================================================

    /// <summary>
    /// Vyloženie nákladu vozidla do skladu továrne
    /// (<paramref name="target"/> = Load slot).
    ///
    /// ALGORITMUS:
    ///   Náklad vozidla sa presunie do príjmového slotu továrne. Slot má
    ///   kapacitu – ak by sa nezmestilo všetko, presunie sa len toľko,
    ///   koľko je v slote voľné, a zvyšok ZOSTANE vo vozidle (odvezie ho ďalej).
    ///
    ///   (Toto je zjednodušená verzia TradeSystem.UnloadWagons – vozidlo
    ///   má len jeden "vagón".)
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE vyložilo (a pripočítalo k továrni).
    /// </summary>
    public static int UnloadVehicle(VehicleInstance vehicle, ResourceSlot target)
    {
        if (vehicle == null || target == null) return 0;
        if (vehicle.CurrentCapacity <= 0) return 0;     // prázdne vozidlo

        // Voľné miesto v príjmovom slote továrne.
        int free = target.capacity - target.amount;
        if (free <= 0) return 0;                        // továreň plná

        // Z vozidla presunieme min(jeho náklad, voľné miesto v továrni).
        int toUnload = Mathf.Min(vehicle.CurrentCapacity, free);
        if (toUnload <= 0) return 0;

        vehicle.CurrentCapacity -= toUnload;

        // Pripočítaj vyloženú surovinu do skladu továrne.
        // ResourceSlot.Add je orezané kapacitou (bezpečné).
        target.Add(toUnload);

        return toUnload;
    }
}
