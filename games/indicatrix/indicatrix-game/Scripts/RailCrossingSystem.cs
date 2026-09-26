using UnityEngine;

/// <summary>
/// RailCrossingSystem
/// ─────────────────────────────────────────────────────────────────────────
/// MOSTY a TUNELY pre ŽELEZNICU. Celá logika (validácia, stavba, demolácia,
/// náhľad, sprity, save/load, podpora A*) je v <see cref="CrossingSystemBase"/>.
/// Tu sú len RAIL špecifiká:
///   • hlava sa zapíše ako RAIL dlaždica so stateID
///     RailConstructionMode.TunnelHorizontal / TunnelVertical /
///     BridgeHorizontal / BridgeVertical,
///   • obsadenosť hlavy sa overuje cez TrainSystem,
///   • ceny z ConstructionCosts (Tunnel* / Bridge*),
///   • UI režimy RailConstructionMode.Tunnel / Bridge.
///
/// Cestný ekvivalent: <see cref="RoadCrossingSystem"/>.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class RailCrossingSystem : CrossingSystemBase
{
    public static RailCrossingSystem instance;

    public override CrossingNetwork Network => CrossingNetwork.Rail;

    protected override void Awake()
    {
        base.Awake();
        if (instance == null) instance = this;
        else if (instance != this)
            Debug.LogWarning("[RailCrossingSystem] V scéne je viac inštancií – používa sa prvá.");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (instance == this) instance = null;
    }

    /// <summary>Vráti inštanciu; ak v scéne chýba, vytvorí ju (s predvolenými nastaveniami).</summary>
    public static RailCrossingSystem GetOrCreate()
    {
        if (instance == null) instance = FindOrCreate<RailCrossingSystem>("RailCrossingSystem");
        return instance;
    }

    protected override void WriteHeadTile(Vector2Int tile, CrossingData c)
    {
        GameManager.RailConstructionMode mode;
        if (c.type == CrossingType.Tunnel)
            mode = c.axis == CrossingAxis.Horizontal
                ? GameManager.RailConstructionMode.TunnelHorizontal
                : GameManager.RailConstructionMode.TunnelVertical;
        else
            mode = c.axis == CrossingAxis.Horizontal
                ? GameManager.RailConstructionMode.BridgeHorizontal
                : GameManager.RailConstructionMode.BridgeVertical;

        IndicatrixAPI.instance.SetTile(new Vector3(tile.x + 0.5f, 0f, tile.y + 0.5f), 1, mode);
    }

    protected override bool IsVehicleOnHead(Vector2Int head)
        => TrainSystem.instance != null && TrainSystem.instance.IsTileOccupiedByTrain(head.x, head.y);

    protected override void GetUiMode(out bool tunnelMode, out bool bridgeMode)
    {
        GameManager gm = GameManager.instance;
        var mode = gm != null ? gm.CurrentRailConstructionMode : GameManager.RailConstructionMode.None;
        tunnelMode = mode == GameManager.RailConstructionMode.Tunnel;
        bridgeMode = mode == GameManager.RailConstructionMode.Bridge;
    }

    public override uint TunnelBuildCost(int length) => ConstructionCosts.TunnelBuildCost(length);
    public override uint BridgeBuildCost(int length, int variant) => ConstructionCosts.BridgeBuildCost(length, variant);
}
