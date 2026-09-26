using UnityEngine;
using Game.TrainStock;
using Game.VehicleStock;


/// <summary>
/// Centrálny CENNÍK všetkých herných úkonov (jedno miesto na úpravu cien).
/// Čisto statická trieda bez stavu – len mapuje "čo sa stavia / ruší" na sumu
/// kreditov. Samotnú zmenu konta robí <see cref="GameEconomy"/>; tu sa len
/// počíta, KOĽKO.
///
/// Konvencia znamienok:
///   • STAVBA (build) vracia KLADNÚ cenu, ktorá sa od konta ODPOČÍTA
///     (GameEconomy.TrySpendCredits).
///   • DEMOLÁCIA / ODSTRÁNENIE vracia KLADNÝ refund = 50 % zo základnej ceny
///     (zaokrúhlené NADOL), ktorý sa ku kontu PRIPOČÍTA (GameEconomy.AddCredits).
///
/// 50 % refund sa počíta vždy z KLADNEJ základnej sumy (od 0 nahor), takže
/// napr. základ 100 → +50, základ 300 → +150, základ 75 → +37 (37,5 nadol).
/// </summary>
public static class ConstructionCosts
{
    // =====================================================================
    // RAIL – cena stavby podľa režimu
    // =====================================================================
    public static uint RailBuildCost(GameManager.RailConstructionMode mode)
    {
        switch (mode)
        {
            case GameManager.RailConstructionMode.LevelUp:
            case GameManager.RailConstructionMode.LevelDown:
                return 10u;

            case GameManager.RailConstructionMode.RailHorizontal:
            case GameManager.RailConstructionMode.RailVertical:
                return 25u;

            case GameManager.RailConstructionMode.RailCrossroad:
            case GameManager.RailConstructionMode.RailCurveRightBottom:
            case GameManager.RailConstructionMode.RailCurveLeftBottom:
            case GameManager.RailConstructionMode.RailCurveRightTop:
            case GameManager.RailConstructionMode.RailCurveLeftTop:
            case GameManager.RailConstructionMode.RailSwitchHorizontalBottom:
            case GameManager.RailConstructionMode.RailSwitchHorizontalTop:
            case GameManager.RailConstructionMode.RailSwitchVerticalBottom:
            case GameManager.RailConstructionMode.RailSwitchVerticalTop:
                return 25u;

            case GameManager.RailConstructionMode.StationHorizontal:
            case GameManager.RailConstructionMode.StationVertical:
                return 30u;

            case GameManager.RailConstructionMode.DepotHorizontalBottom:
            case GameManager.RailConstructionMode.DepotVerticalBottom:
            case GameManager.RailConstructionMode.DepotHorizontalTop:
            case GameManager.RailConstructionMode.DepotVerticalTop:
                return 50u;

            // None, Demolish – stavba nič nestojí.
            default:
                return 0u;
        }
    }

    // =====================================================================
    // ROAD – cena stavby podľa režimu
    // =====================================================================
    public static uint RoadBuildCost(GameManager.RoadConstructionMode mode)
    {
        switch (mode)
        {
            case GameManager.RoadConstructionMode.LevelUp:
            case GameManager.RoadConstructionMode.LevelDown:
                return 10u;

            case GameManager.RoadConstructionMode.RoadHorizontal:
            case GameManager.RoadConstructionMode.RoadVertical:
                return 25u;

            case GameManager.RoadConstructionMode.RoadCrossroad:
                return 25u;

            case GameManager.RoadConstructionMode.RoadCurveRightBottom:
            case GameManager.RoadConstructionMode.RoadCurveLeftBottom:
            case GameManager.RoadConstructionMode.RoadCurveRightTop:
            case GameManager.RoadConstructionMode.RoadCurveLeftTop:
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom:
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalTop:
            case GameManager.RoadConstructionMode.RoadSwitchVerticalBottom:
            case GameManager.RoadConstructionMode.RoadSwitchVerticalTop:
                return 25u;

            case GameManager.RoadConstructionMode.StationHorizontal:
            case GameManager.RoadConstructionMode.StationVertical:
                return 30u;

            case GameManager.RoadConstructionMode.DepotHorizontalBottom:
            case GameManager.RoadConstructionMode.DepotVerticalBottom:
            case GameManager.RoadConstructionMode.DepotHorizontalTop:
            case GameManager.RoadConstructionMode.DepotVerticalTop:
                return 50u;

            // None, Demolish – stavba nič nestojí.
            default:
                return 0u;
        }
    }

    // =====================================================================
    // FACTORY – cena stavby podľa typu továrne
    //
    // Hodnota je definovaná per-typ v FactoryDatabase (pole FactoryDefinition.
    // Cost) – tu sa len dohľadá, aby ConstructionCosts ostal jediným vstupným
    // bodom pre "koľko stojí X" (rovnako ako RailBuildCost / RoadBuildCost).
    // Neznámy typ (None / chýbajúca definícia) → 0 (zadarmo).
    // =====================================================================
    public static uint FactoryBuildCost(GameManager.FactoryConstructionMode mode)
    {
        FactoryDefinition def = FactoryDatabase.GetDefinition(mode);
        if (def == null) return 0u;
        return def.Cost <= 0 ? 0u : (uint)def.Cost;
    }

    // =====================================================================
    // 50 % REFUND – spoločný výpočet (zaokrúhlené nadol, vždy z kladného základu)
    // =====================================================================
    public static uint HalfRefund(uint baseCost) => baseCost / 2u;

    public static uint HalfRefund(int baseCost)
        => baseCost <= 0 ? 0u : (uint)baseCost / 2u;

    /// <summary>
    /// Refund za demoláciu konkrétneho tile. Podľa kategórie (Rail/Road) a
    /// uloženého stateID dohľadá pôvodnú stavebnú cenu a vráti 50 % z nej.
    /// Prázdny tile (tileID 0) → 0.
    /// </summary>
    public static uint DemolishRefund(IndicatrixAPI.TileData tile)
    {
        if (tile.tileID == 0) return 0u;

        // TOVÁREŇ: 50 % z CELKOVEJ ceny továrne (FactoryDefinition.Cost).
        // POZOR: refund za továreň sa pripočítava RAZ za celý objekt
        // (GameManager.DemolishFactory), NIE per-tile cez footprint – túto
        // vetvu preto nevolaj v cykle nad jednotlivými tilmi továrne.
        if (tile.category == IndicatrixAPI.TileCategory.Factory)
            return HalfRefund(FactoryBuildCost((GameManager.FactoryConstructionMode)tile.stateID));

        // ZMIEŠANÁ KRIŽOVATKA: 50 % z koľaje + 50 % z cesty. (GameManager pri
        // Demolish búra len JEDNU časť a refunduje ju cez RailViewOf/RoadViewOf;
        // táto vetva je poistka pre prípad odstránenia celej križovatky.)
        if (tile.category == IndicatrixAPI.TileCategory.RailRoadCrossing)
            return DemolishRefund(IndicatrixAPI.RailViewOf(tile))
                 + DemolishRefund(IndicatrixAPI.RoadViewOf(tile));

        uint baseCost = tile.category == IndicatrixAPI.TileCategory.Road
            ? RoadBuildCost((GameManager.RoadConstructionMode)tile.stateID)
            : RailBuildCost((GameManager.RailConstructionMode)tile.stateID);

        return HalfRefund(baseCost);
    }

    // =====================================================================
    // ENVIRONMENT (stromy / kamene / landing locations)
    //
    // Podľa zadania NEMAJÚ ŽIADNU CENU:
    //   • nedajú sa stavať (generujú sa automaticky pri štarte hry),
    //   • demolácia stromu a kameňa je ZADARMO – hráčovi sa nič neúčtuje
    //     a nič sa mu ani nevracia,
    //   • landing location sa demolovať nedá vôbec.
    //
    // Metóda existuje kvôli explicitnosti (ConstructionCosts zostáva jediným
    // vstupným bodom pre „koľko stojí X“) – vracia vždy 0.
    //
    // POZN.: Environment objekty NIE SÚ v tileGrid (rovnako ako mestské budovy),
    // takže DemolishRefund(tile) by pre ne aj tak vrátil 0 (tileID == 0).
    // =====================================================================
    public static uint EnvironmentDemolishRefund() => 0u;

    // =====================================================================
    // VLAKY – cena stavby = Cost lokomotívy + wagonCount * Cost vagónu
    // =====================================================================
    public static uint TrainBuildCost(int trainTypeIndex, int wagonTypeIndex, int wagonCount)
    {
        TrainSpec trainSpec = TrainCatalog.ByIndex(trainTypeIndex);
        WagonSpec wagonSpec = WagonCatalog.ByIndex(wagonTypeIndex);

        int loco = trainSpec != null ? trainSpec.Cost : 0;
        int perWagon = wagonSpec != null ? wagonSpec.Cost : 0;
        int n = Mathf.Max(0, wagonCount);

        int total = loco + n * perWagon;
        return total <= 0 ? 0u : (uint)total;
    }

    // =====================================================================
    // VOZIDLÁ – cena stavby = Cost vozidla
    //
    // Fallback na ByIndex(0) zrkadlí VehicleSystem.CreateVehicle, ktorý pri
    // neznámom názve tiež vytvorí prvé vozidlo z katalógu – aby odpočítaná
    // suma sedela s tým, čo sa reálne vytvorí.
    // =====================================================================
    public static uint VehicleBuildCost(string vehicleTypeName)
    {
        VehicleSpec spec = VehicleCatalog.ByName(vehicleTypeName)
                           ?? VehicleCatalog.ByIndex(0);
        int cost = spec != null ? spec.Cost : 0;
        return cost <= 0 ? 0u : (uint)cost;
    }

    // =====================================================================
    // MOSTY A TUNELY (RAIL) – cena podľa dĺžky (vrátane oboch hláv)
    //
    // POZN.: Ceny sú ZATIAĽ ORIENTAČNÉ (na revíziu). Refund pri demolácii sa
    // počíta z REÁLNE ZAPLATENEJ ceny uloženej pri prechode (buildCost), takže
    // neskoršia zmena cenníka neovplyvní už postavené mosty/tunely.
    // =====================================================================
    public const uint TunnelBaseCost = 200u;
    public const uint TunnelCostPerTile = 40u;
    public const uint BridgeBaseCost = 150u;
    public const uint BridgeCostPerTile = 35u;

    // TYPY MOSTOV A / B / C – cena = základ[typ] + za tile[typ] × dĺžka.
    // Typ A = pôvodné hodnoty. B a C ZATIAĽ rovnaké – na revíziu.
    private static readonly uint[] BridgeBaseCostByVariant = { BridgeBaseCost, 150u, 150u };
    private static readonly uint[] BridgeCostPerTileByVariant = { BridgeCostPerTile, 35u, 35u };

    public static uint TunnelBuildCost(int length)
        => TunnelBaseCost + TunnelCostPerTile * (uint)Mathf.Max(0, length);

    public static uint BridgeBuildCost(int length)
        => BridgeBuildCost(length, 0);

    /// <summary>Cena RAIL mosta daného typu (0 = A, 1 = B, 2 = C).</summary>
    public static uint BridgeBuildCost(int length, int variant)
    {
        int v = Mathf.Clamp(variant, 0, BridgeBaseCostByVariant.Length - 1);
        return BridgeBaseCostByVariant[v] + BridgeCostPerTileByVariant[v] * (uint)Mathf.Max(0, length);
    }

    // ── CESTY (ROAD) – analogicky, zatiaľ ORIENTAČNÉ (lacnejšie než železnica) ──
    public const uint RoadTunnelBaseCost = 150u;
    public const uint RoadTunnelCostPerTile = 30u;
    public const uint RoadBridgeBaseCost = 100u;
    public const uint RoadBridgeCostPerTile = 25u;

    // TYPY CESTNÝCH MOSTOV A / B / C – typ A = pôvodné hodnoty, B a C na revíziu.
    private static readonly uint[] RoadBridgeBaseCostByVariant = { RoadBridgeBaseCost, 100u, 100u };
    private static readonly uint[] RoadBridgeCostPerTileByVariant = { RoadBridgeCostPerTile, 25u, 25u };

    public static uint RoadTunnelBuildCost(int length)
        => RoadTunnelBaseCost + RoadTunnelCostPerTile * (uint)Mathf.Max(0, length);

    public static uint RoadBridgeBuildCost(int length)
        => RoadBridgeBuildCost(length, 0);

    /// <summary>Cena ROAD mosta daného typu (0 = A, 1 = B, 2 = C).</summary>
    public static uint RoadBridgeBuildCost(int length, int variant)
    {
        int v = Mathf.Clamp(variant, 0, RoadBridgeBaseCostByVariant.Length - 1);
        return RoadBridgeBaseCostByVariant[v] + RoadBridgeCostPerTileByVariant[v] * (uint)Mathf.Max(0, length);
    }

    /// <summary>50 % refund za zbúranie CELÉHO mosta/tunela (RAIL aj ROAD, raz za objekt).</summary>
    public static uint CrossingDemolishRefund(uint paidBuildCost) => HalfRefund(paidBuildCost);
}
