using System.Collections.Generic;
using UnityEngine;
using Game.TrainStock;

/// <summary>
/// TrainTradeSystem.cs
/// ------------------------------------------------------------------
/// TRANSAKČNÝ ALGORITMUS – výmena tovaru medzi vlakom a továrňami stanice.
///
/// Tento súbor implementuje to, čo zadanie nazýva "ALGORITMUS TRANSAKCIE
/// tovaru cez stanicu". Volá sa z TrainSystem-u v momente, keď vlak čaká
/// na stanici (po 2 s z 10 s čakania – časovanie rieši TrainSystem).
///
/// ------------------------------------------------------------------
/// AKO TO FUNGUJE – KONCEPT
///
/// Vlak má vagóny VŽDY len jedného typu (napr. "Coal Truck", #1). Typ
/// vagónu nesie ResourceType, s ktorým vie pracovať:
///     WagonType.Id == (int)ResourceType
///     ("Coal Truck" #1 → ResourceType.Coal,  "Wood Truck" #2 → Wood)
/// Vďaka tomu netreba do TrainStock.cs pridávať nové pole – mapovanie
/// je 1:1 cez číselné ID (pozri WagonResource() nižšie).
///
/// Stanica eviduje 0..N tovární vo svojej 9×9 zóne (StationInstance.Factories).
/// Každá továreň má pre danú surovinu buď VÝDAJOVÝ slot (UnLoad – surovinu
/// produkuje, napr. Coal Mine) alebo PRÍJMOVÝ slot (Load – surovinu
/// spotrebúva, napr. Power Station).
///
/// Keď vlak zastane na stanici, pre svoj typ suroviny urobí:
///
///   KROK 1 – VYLOŽENIE (Unload):
///     Ak je v zóne stanice továreň, ktorá danú surovinu PRIJÍMA (Load slot),
///     a vo vagónoch niečo je → vyprázdni vagóny do tej továrne (Load slot
///     amount sa zvýši, vagóny idú na 0).
///
///   KROK 2 – NALOŽENIE (Load):
///     Ak je v zóne stanice továreň, ktorá danú surovinu VYDÁVA (UnLoad slot),
///     a vo vagónoch je voľné miesto → naloží z tej továrne do vagónov
///     (UnLoad slot amount sa zníži, vagóny sa naplnia "alikvotne":
///     vagón po vagóne do max kapacity – pozri FillWagons()).
///
/// Obchod sa pokladá za ÚSPEŠNÝ ("predaj prebehol") IBA vtedy, keď sa pri
/// zastávke SKUTOČNE VYLOŽILA aspoň 1 jednotka tovaru do príjmovej továrne.
/// Samotné naloženie surovín do vagónov NEROBÍ obchod úspešným a negeneruje
/// žiadny výnos – je to len presun, príprava na predaj na ďalšej stanici.
///
/// BONUS zo zadania (vlak príde viackrát do výdajovej stanice a vo vagónoch
/// už niečo má): rieši sa automaticky. KROK 1 sa vykoná len ak je v zóne
/// PRÍJMOVÁ továreň – pri čisto výdajovej stanici sa teda nevyloží a KROK 2
/// len pripočíta ďalšiu surovinu k tomu, čo už vo vagónoch je (FillWagons
/// dopĺňa do voľného miesta, existujúci náklad nemaže).
///
/// ------------------------------------------------------------------
/// CENA / VÝNOS
///
/// Po úspešnom obchode (vyloží sa aspoň 1 jednotka do príjmovej továrne) sa
/// vyčísli zárobok cez centrálny cenník <see cref="ResourcePricing"/>:
/// cena je definovaná ZA PLNÝ VAGÓN podľa suroviny (napr. Coal 50, Oil 70,
/// Furniture 75) a ak sa predal len zlomok plného vagónu, počíta sa ALIKVOTNE.
/// Cena sa navyše násobí dynamickým násobiteľom konta (pri zápornom konte
/// rastie – pozri ResourcePricing / EconomySystem). Výslednú sumu pripíše na
/// konto volajúci (TrainSystem) a vypíše log so sumou v CR.
/// ------------------------------------------------------------------
/// </summary>
public static class TrainTradeSystem
{
    // Cena predaja sa už NEráta paušálne za jednotku. Kompletný cenník (cena
    // ZA PLNÝ VAGÓN podľa suroviny + alikvotná časť pri neúplnom vagóne +
    // dynamický násobiteľ pri zápornom konte) je centralizovaný v
    // ResourcePricing.RailSaleRevenue(...). Pozri krok 4 v Execute(...).

    // ==================================================================
    // VÝSLEDOK TRANSAKCIE
    // ==================================================================

    /// <summary>
    /// Výsledok jednej zastávky vlaku na stanici. TrainSystem si z neho
    /// prečíta Success + Revenue a vypíše príslušný Debug.Log.
    /// </summary>
    public struct TradeResult
    {
        /// <summary>True, ak sa presunula aspoň 1 jednotka tovaru.</summary>
        public bool Success;

        /// <summary>Koľko jednotiek sa naložilo z továrne do vagónov.</summary>
        public int LoadedUnits;

        /// <summary>Koľko jednotiek sa vyložilo z vagónov do továrne.</summary>
        public int UnloadedUnits;

        /// <summary>Celkový výnos z obchodu (euro).</summary>
        public int Revenue;

        /// <summary>Stručný textový dôvod (najmä pri neúspechu) – na debug.</summary>
        public string Reason;

        /// <summary>Celkový počet presunutých jednotiek (load + unload).</summary>
        public int MovedUnits => LoadedUnits + UnloadedUnits;
    }

    // ==================================================================
    // HLAVNÝ VSTUP – vykonaj transakciu pre vlak na stanici
    // ==================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi súpravou <paramref name="consist"/> a
    /// továrňami v zóne stanice <paramref name="station"/>.
    ///
    /// Postup:
    ///   0) Validácia (consist má vagóny, stanica eviduje aspoň 1 továreň).
    ///   1) Zisti surovinu, s ktorou vlak pracuje (z typu vagónov).
    ///   2) UNLOAD – ak je v zóne príjmová továreň, vyprázdni vagóny do nej.
    ///   3) LOAD   – ak je v zóne výdajová továreň, naplň vagóny z nej.
    ///   4) Vyhodnoť úspech a vyčísli cenu.
    ///
    /// Metóda je čisto dátová (žiadne Unity GameObject API), takže sa dá
    /// volať z hlavného vlákna v Update slučke TrainSystem-u bez rizika.
    /// </summary>
    public static TradeResult Execute(TrainInstance consist, StationInstance station)
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
        if (consist == null)
        {
            result.Reason = "vlak nemá dátovú súpravu (consist == null)";
            return result;
        }
        if (consist.Wagons == null || consist.Wagons.Count == 0)
        {
            result.Reason = "vlak nemá žiadne vagóny";
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

        // ---- 1) SUROVINA VLAKU -------------------------------------------
        // Všetky vagóny sú rovnakého typu – stačí pozrieť prvý.
        ResourceType wagonResource = WagonResource(consist.Wagons[0]);
        if (wagonResource == ResourceType.None)
        {
            result.Reason = $"typ vagónu '{consist.Wagons[0].Type}' nemá priradenú surovinu";
            return result;
        }

        // ---- 2) UNLOAD (vyloženie do príjmovej továrne) -------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // PRIJÍMA. Pri čisto výdajovej stanici sa tento krok preskočí –
        // tým je ošetrený "bonus": vlak môže prejsť cez výdajovú stanicu
        // a náklad mu zostane vo vagónoch.
        FactoryInstance consumer = station.FindConsumer(wagonResource);
        if (consumer != null)
        {
            ResourceSlot loadSlot = consumer.GetLoadSlot(wagonResource);
            result.UnloadedUnits = UnloadWagons(consist, loadSlot);
            if (result.UnloadedUnits > 0)
                Debug.Log($"[TradeSystem] Vyložené {result.UnloadedUnits}× {wagonResource} " +
                          $"do '{consumer.Name}' (slot {loadSlot}).");
        }

        // ---- 3) LOAD (naloženie z výdajovej továrne) ----------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // VYDÁVA. FillWagons dopĺňa do VOĽNÉHO miesta, takže existujúci
        // náklad vo vagónoch (z inej stanice) zostáva a nová surovina sa
        // k nemu pripočíta.
        FactoryInstance supplier = station.FindSupplier(wagonResource);
        if (supplier != null)
        {
            ResourceSlot unloadSlot = supplier.GetUnLoadSlot(wagonResource);
            result.LoadedUnits = FillWagons(consist, unloadSlot);
            if (result.LoadedUnits > 0)
                Debug.Log($"[TradeSystem] Naložené {result.LoadedUnits}× {wagonResource} " +
                          $"z '{supplier.Name}' (slot {unloadSlot}).");
        }

        // ---- 4) VYHODNOTENIE ---------------------------------------------
        // DÔLEŽITÉ: peniaze plynú IBA z VYLOŽENIA (predaja tovaru továrni).
        // Naloženie surovín z výdajovej továrne do vagónov je len presun –
        // NEGENERUJE žiadny výnos a samo o sebe nerobí obchod úspešným.
        //
        // Obchod sa teda pokladá za úspešný ("Predaj prebehol úspešne")
        // práve vtedy, keď sa do nejakej príjmovej továrne SKUTOČNE vyložila
        // aspoň 1 jednotka tovaru. Cena = počet VYLOŽENÝCH jednotiek.
        result.Success = result.UnloadedUnits > 0;

        if (result.Success)
        {
            // Cena ZA PLNÝ VAGÓN podľa suroviny, alikvotne podľa reálne
            // predaného (vyloženého) množstva, prenásobená aktuálnym cenovým
            // násobiteľom konta. Všetky vagóny sú rovnakého typu → rovnaká
            // MaximumCapacity, ktorá je menovateľom alikvotného prepočtu.
            int wagonCapacity = FirstWagonCapacity(consist);
            result.Revenue = ResourcePricing.RailSaleRevenue(
                wagonResource, result.UnloadedUnits, wagonCapacity);
        }
        else
        {
            // Žiadny PREDAJ (vyloženie) – upresni dôvod pre debug.
            // Pozn.: aj keď sa niečo naložilo (LoadedUnits > 0), obchod sa
            // bez vyloženia neráta ako úspešný – naloženie je len príprava
            // na predaj na nasledujúcej stanici.
            if (consumer == null)
            {
                if (result.LoadedUnits > 0)
                    result.Reason = $"naložené {result.LoadedUnits}× {wagonResource}, " +
                                    $"ale stanica nemá príjmovú továreň – predaj sa nekoná";
                else if (supplier == null)
                    result.Reason = $"v zóne stanice nie je továreň pre surovinu {wagonResource}";
                else
                    result.Reason = "stanica nemá príjmovú továreň – nie je kam predať";
            }
            else
            {
                result.Reason = "vagóny prázdne alebo príjmová továreň plná – nič sa nevyložilo";
            }
        }

        return result;
    }

    // ==================================================================
    // POMOCNÉ – kapacita jedného vagónu (menovateľ alikvotnej ceny)
    // ==================================================================

    /// <summary>
    /// Maximálna kapacita JEDNÉHO vagónu súpravy. Všetky vagóny vlaku sú
    /// rovnakého typu (rovnaká MaximumCapacity), takže stačí prvý platný.
    /// Slúži ako menovateľ pri výpočte ceny "za plný vagón" (alikvotne).
    /// Vráti 0, ak súprava nemá platný vagón (vtedy je zárobok 0 CR).
    /// </summary>
    private static int FirstWagonCapacity(TrainInstance consist)
    {
        if (consist == null || consist.Wagons == null) return 0;
        for (int i = 0; i < consist.Wagons.Count; i++)
        {
            var w = consist.Wagons[i];
            if (w != null && w.MaximumCapacity > 0) return w.MaximumCapacity;
        }
        return 0;
    }

    // ==================================================================
    // POMOCNÉ – mapovanie typu vagónu na surovinu
    // ==================================================================

    /// <summary>
    /// Vráti surovinu, s ktorou vie daný vagón pracovať.
    ///
    /// Mapovanie je cez ČÍSELNÉ ID: WagonType.Id == (int)ResourceType.
    /// Katalógy sú zámerne zladené:
    ///     "Coal Truck" má Id 1  →  ResourceType.Coal == 1
    ///     "Wood Truck" má Id 2  →  ResourceType.Wood == 2
    /// Vďaka tomu sa do TrainStock.cs nemusí pridávať nové pole.
    ///
    /// Ak by v budúcnosti ID nesedeli, stačí toto mapovanie nahradiť
    /// explicitnou tabuľkou (Dictionary&lt;int, ResourceType&gt;).
    /// </summary>
    public static ResourceType WagonResource(WagonInstance wagon)
    {
        if (wagon == null) return ResourceType.None;

        int id = wagon.Type.Id;
        if (System.Enum.IsDefined(typeof(ResourceType), id))
            return (ResourceType)id;

        return ResourceType.None;
    }

    // ==================================================================
    // POMOCNÉ – LOAD: naplnenie vagónov z výdajového slotu továrne
    // ==================================================================

    /// <summary>
    /// "Alikvotné" naplnenie vagónov surovinou zo skladu továrne
    /// (<paramref name="source"/> = UnLoad slot).
    ///
    /// ALGORITMUS (presne podľa príkladu zo zadania):
    ///   Ide vagón po vagóne. Každý vagón sa naplní do svojej maximálnej
    ///   kapacity, pokým je v továrni surovina k dispozícii. Posledný
    ///   čiastočne naplnený vagón dostane zvyšok.
    ///
    ///   Príklad zo zadania: 8 vagónov × max 30 = kapacita 240, v továrni
    ///   je 173 → naplní sa 5 plných vagónov (5×30 = 150) + 6. vagón
    ///   dostane zvyšok 23. Vagóny 7 a 8 zostanú prázdne. Z továrne sa
    ///   odpočíta presne 173 (resp. 150+23 = 173).
    ///
    /// Rešpektuje aj BONUS: ak vo vagóne už nejaká surovina je (z inej
    /// stanice), dopĺňa sa len jeho VOĽNÉ miesto – existujúce množstvo
    /// sa nemaže.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE naložilo (a teda aj odpočítalo
    /// z továrne).
    /// </summary>
    public static int FillWagons(TrainInstance consist, ResourceSlot source)
    {
        if (consist == null || source == null) return 0;
        if (source.IsEmpty) return 0;

        int totalLoaded = 0;

        foreach (var wagon in consist.Wagons)
        {
            if (wagon == null) continue;

            // Voľné miesto v tomto vagóne.
            int free = wagon.MaximumCapacity - wagon.CurrentCapacity;
            if (free <= 0) continue;            // vagón je plný – ďalší

            // Koľko ešte zostáva v továrni.
            int available = source.amount - totalLoaded;
            if (available <= 0) break;          // továreň vyčerpaná – koniec

            // Do vagónu ide min(voľné miesto, dostupné v továrni).
            int toLoad = Mathf.Min(free, available);
            wagon.CurrentCapacity += toLoad;
            totalLoaded += toLoad;
        }

        // Odpočítaj naloženú surovinu zo skladu továrne.
        // ResourceSlot.Remove je orezané dostupným množstvom (bezpečné).
        if (totalLoaded > 0)
            source.Remove(totalLoaded);

        return totalLoaded;
    }

    // ==================================================================
    // POMOCNÉ – UNLOAD: vyprázdnenie vagónov do príjmového slotu továrne
    // ==================================================================

    /// <summary>
    /// Vyprázdnenie vagónov do skladu továrne (<paramref name="target"/>
    /// = Load slot).
    ///
    /// ALGORITMUS:
    ///   Ide vagón po vagóne. Z každého vagónu sa presunie jeho náklad do
    ///   príjmového slotu továrne. Slot má kapacitu – ak by sa nezmestilo
    ///   všetko, presunie sa len toľko, koľko je v slote voľné, a zvyšok
    ///   ZOSTANE vo vagóne (vlak ho odvezie ďalej).
    ///
    ///   V základnom scenári zadania ("všetky vagóny vyprázdni") má
    ///   príjmová továreň dosť kapacity (Power Station Load 0/300), takže
    ///   sa vagóny vyprázdnia úplne. Orezanie kapacitou je poistka pre
    ///   prípad plnej továrne.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE vyložilo (a pripočítalo k továrni).
    /// </summary>
    public static int UnloadWagons(TrainInstance consist, ResourceSlot target)
    {
        if (consist == null || target == null) return 0;

        int totalUnloaded = 0;

        foreach (var wagon in consist.Wagons)
        {
            if (wagon == null) continue;
            if (wagon.CurrentCapacity <= 0) continue;   // prázdny vagón

            // Voľné miesto v príjmovom slote továrne (po doterajšom vykladaní).
            int free = target.capacity - (target.amount + totalUnloaded);
            if (free <= 0) break;                       // továreň plná – koniec

            // Z vagónu presunieme min(jeho náklad, voľné miesto v továrni).
            int toUnload = Mathf.Min(wagon.CurrentCapacity, free);
            wagon.CurrentCapacity -= toUnload;
            totalUnloaded += toUnload;
        }

        // Pripočítaj vyloženú surovinu do skladu továrne.
        // ResourceSlot.Add je orezané kapacitou (bezpečné).
        if (totalUnloaded > 0)
            target.Add(totalUnloaded);

        return totalUnloaded;
    }
}
