using UnityEngine;


/// <summary>
/// ResourcePricing
/// ============================================================================
/// Centrálny CENNÍK PREDAJA surovín na staniciach + dynamický cenový NÁSOBITEĽ
/// pri zápornom konte. Čisto statická trieda bez stavu hry (rovnaký vzor ako
/// <see cref="ConstructionCosts"/>) – jedno miesto pravdy pre "koľko CR sa
/// zarobí za predaj suroviny X cez stanicu".
///
/// ── AKO SA POČÍTA CENA ───────────────────────────────────────────────────
/// Cena je definovaná ZA PLNÝ "kus dopravcu":
///   • RAIL – za každý PLNÝ vagón danej suroviny,
///   • ROAD – za každé PLNÉ vozidlo danej suroviny.
/// Ak kus nie je plný (alebo sa predala len jeho časť), cena sa počíta
/// ALIKVOTNE – úmerne k tomu, koľko sa z plnej kapacity reálne predalo.
///
/// Keďže všetky vagóny vlaku sú rovnakého typu (rovnaká MaximumCapacity), platí:
///
///     zárobok = cena_suroviny × (predané_jednotky / kapacita_kusu)
///
/// zaokrúhlené na celé CR. Príklad (RAIL, Coal, cena 300, kapacita vagónu 15
/// podľa WagonSpec.MaximumCapacity):
///   • predaný 1 plný vagón (15/15)        → 300 × 15/15 = 300 CR,
///   • predaná polovica vagónu (7/15)      → 300 × 7/15  = 140 CR,
///   • 5-vagónový vlak predal 75 jednotiek → 300 × 75/15 = 1 500 CR.
///
/// Predaj (a teda aj zárobok) vzniká IBA z VYLOŽENIA tovaru do príjmovej
/// továrne – naloženie surovín z výdajovej továrne do vagónov je len presun
/// (rovnaké pravidlo ako doteraz v TrainTradeSystem / VehicleTradeSystem).
///
/// ── DYNAMICKÝ NÁSOBITEĽ (zadanie) ─────────────────────────────────────────
/// Keď herné konto klesne do MÍNUSU, ceny predaja (RAIL aj ROAD) sa zdvihnú.
/// Hodnotu <see cref="Multiplier"/> riadi <see cref="EconomySystem"/> RAZ ZA
/// HERNÝ MESIAC (na hranici mesiaca), po zúčtovaní mesačných nákladov, podľa
/// stavu konta:
///   • konto &lt; 0 → násobiteľ ESKALUJE: 1. záporný mesiac ×2, ďalší ×3, ×4 …
///   • konto &gt; 0 → RESET späť na ×1 (pôvodné ceny) a drží sa, kým je v pluse,
///   • konto = 0 → ponechá sa aktuálny násobiteľ (nie je v mínuse ani v pluse).
///
/// Trade systémy tento násobiteľ pri výpočte zárobku len ČÍTAJÚ – nikdy ho
/// nemenia. Jediný "vlastník" zápisu je EconomySystem.
/// </summary>
public static class ResourcePricing
{
    // =====================================================================
    // ZÁKLADNÉ CENY (×1) – indexované cez (int)ResourceType (0..16)
    // =====================================================================
    private static readonly int[] RailPrices = BuildRailPrices();
    private static readonly int[] RoadPrices = BuildRoadPrices();

    /// <summary>
    /// Aktuálny cenový násobiteľ. Default 1 = pôvodné ceny. Nastavuje ho
    /// výhradne <see cref="EconomySystem"/> na hranici mesiaca podľa stavu
    /// konta. Hodnota je vždy ≥ 1 (čítač pri výpočte navyše poistí dolnú hranicu).
    /// </summary>
    public static int Multiplier { get; set; } = 1;

    /// <summary>
    /// Vráti na pôvodný stav (×1). Volá EconomySystem pri štarte – statické
    /// pole v Unity totiž prežíva reload scény / nový rozohraný zápas, takže
    /// bez resetu by sa zvýšený násobiteľ mohol preniesť do novej hry.
    /// </summary>
    public static void ResetMultiplier() => Multiplier = 1;

    // =====================================================================
    // VEREJNÉ API – základné ceny (bez násobiteľa)
    // =====================================================================
    public static int RailBasePrice(ResourceType resource) => PriceFromTable(RailPrices, resource);
    public static int RoadBasePrice(ResourceType resource) => PriceFromTable(RoadPrices, resource);

    // =====================================================================
    // VEREJNÉ API – výpočet zárobku z predaja (vrátane násobiteľa)
    // =====================================================================

    /// <summary>
    /// Zárobok za RAIL predaj danej suroviny: základná cena × aktuálny
    /// násobiteľ × (predané jednotky / kapacita jedného vagónu), zaokrúhlené
    /// na najbližšie celé CR. Všetky vagóny vlaku majú rovnakú kapacitu, takže
    /// <paramref name="wagonCapacity"/> = MaximumCapacity jedného vagónu.
    /// </summary>
    public static int RailSaleRevenue(ResourceType resource, int unloadedUnits, int wagonCapacity)
        => ComputeRevenue(RailBasePrice(resource), unloadedUnits, wagonCapacity);

    /// <summary>
    /// Zárobok za ROAD predaj danej suroviny: základná cena × aktuálny
    /// násobiteľ × (predané jednotky / kapacita vozidla), zaokrúhlené na
    /// najbližšie celé CR.
    /// </summary>
    public static int RoadSaleRevenue(ResourceType resource, int unloadedUnits, int vehicleCapacity)
        => ComputeRevenue(RoadBasePrice(resource), unloadedUnits, vehicleCapacity);

    // =====================================================================
    // VNÚTORNÉ
    // =====================================================================

    /// <summary>
    /// Spoločný výpočet: cena × násobiteľ × (predané / kapacita), zaokrúhlené
    /// na najbližšie celé číslo (round-half-up). Akýkoľvek nezmyselný vstup
    /// (nulová cena / nič nepredané / nulová kapacita) → 0 CR.
    /// </summary>
    private static int ComputeRevenue(int basePrice, int unloadedUnits, int unitCapacity)
    {
        if (basePrice <= 0 || unloadedUnits <= 0 || unitCapacity <= 0) return 0;

        int mult = Multiplier < 1 ? 1 : Multiplier;   // poistka: násobiteľ nikdy < 1

        // long, aby pri vysokom násobiteľi a kapacite nedošlo k pretečeniu.
        long numerator = (long)basePrice * mult * unloadedUnits;
        long rounded = (numerator + unitCapacity / 2) / unitCapacity;   // round-half-up
        return (int)rounded;
    }

    private static int PriceFromTable(int[] table, ResourceType resource)
    {
        int i = (int)resource;
        return (i >= 0 && i < table.Length) ? table[i] : 0;
    }

    // =====================================================================
    // TABUĽKY ZÁKLADNÝCH CIEN (zo zadania)
    // =====================================================================

    /// <summary>RAIL – cena za PLNÝ vagón podľa suroviny.</summary>
    private static int[] BuildRailPrices()
    {
        var p = new int[17];   // index 0..16 = (int)ResourceType
        p[(int)ResourceType.None] = 0;
        p[(int)ResourceType.Coal] = 300;
        p[(int)ResourceType.Wood] = 300;
        p[(int)ResourceType.IronOre] = 300;
        p[(int)ResourceType.Gold] = 300;
        p[(int)ResourceType.Silver] = 300;
        p[(int)ResourceType.Livestock] = 350;
        p[(int)ResourceType.Grain] = 350;
        p[(int)ResourceType.Oil] = 300;
        p[(int)ResourceType.Boards] = 300;
        p[(int)ResourceType.Plastic] = 300;
        p[(int)ResourceType.Meat] = 350;
        p[(int)ResourceType.Flour] = 300;
        p[(int)ResourceType.Metals] = 400;
        p[(int)ResourceType.Glass] = 200;
        p[(int)ResourceType.Furniture] = 400;
        p[(int)ResourceType.ElectronicsProducts] = 500;
        return p;
    }

    /// <summary>ROAD – cena za PLNÉ vozidlo podľa suroviny.</summary>
    private static int[] BuildRoadPrices()
    {
        var p = new int[17];   // index 0..16 = (int)ResourceType
        p[(int)ResourceType.None] = 0;
        p[(int)ResourceType.Coal] = 500;
        p[(int)ResourceType.Wood] = 500;
        p[(int)ResourceType.IronOre] = 500;
        p[(int)ResourceType.Gold] = 500;
        p[(int)ResourceType.Silver] = 500;
        p[(int)ResourceType.Livestock] = 550;
        p[(int)ResourceType.Grain] = 550;
        p[(int)ResourceType.Oil] = 500;
        p[(int)ResourceType.Boards] = 500;
        p[(int)ResourceType.Plastic] = 500;
        p[(int)ResourceType.Meat] = 550;
        p[(int)ResourceType.Flour] = 500;
        p[(int)ResourceType.Metals] = 600;
        p[(int)ResourceType.Glass] = 300;
        p[(int)ResourceType.Furniture] = 500;
        p[(int)ResourceType.ElectronicsProducts] = 600;
        return p;
    }
}
