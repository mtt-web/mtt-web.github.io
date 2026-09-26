using UnityEngine;

/// <summary>
/// RoadCrossingSystem
/// ─────────────────────────────────────────────────────────────────────────
/// MOSTY a TUNELY pre CESTY – úplná analógia k <see cref="RailCrossingSystem"/>.
/// Celá logika je v <see cref="CrossingSystemBase"/>. Tu sú len ROAD špecifiká:
///   • hlava sa zapíše ako ROAD dlaždica (TileCategory.Road) so stateID
///     RoadConstructionMode.TunnelHorizontal / TunnelVertical /
///     BridgeHorizontal / BridgeVertical → VehicleSystem ju vidí, TrainSystem nie,
///   • obsadenosť hlavy sa overuje cez VehicleSystem,
///   • ceny z ConstructionCosts (RoadTunnel* / RoadBridge*),
///   • UI režimy RoadConstructionMode.Tunnel / Bridge.
///
/// Kontroly naprieč sieťami (cestný most cez železničný most, tunel cez
/// tunel, cesta pod železničným mostom …) rieši základná trieda cez *Any.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class RoadCrossingSystem : CrossingSystemBase
{
    public static RoadCrossingSystem instance;

    public override CrossingNetwork Network => CrossingNetwork.Road;

    protected override void Awake()
    {
        base.Awake();
        if (instance == null) instance = this;
        else if (instance != this)
            Debug.LogWarning("[RoadCrossingSystem] V scéne je viac inštancií – používa sa prvá.");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (instance == this) instance = null;
    }

    /// <summary>Vráti inštanciu; ak v scéne chýba, vytvorí ju (s predvolenými nastaveniami).</summary>
    public static RoadCrossingSystem GetOrCreate()
    {
        if (instance == null) instance = FindOrCreate<RoadCrossingSystem>("RoadCrossingSystem");
        return instance;
    }

    protected override void WriteHeadTile(Vector2Int tile, CrossingData c)
    {
        GameManager.RoadConstructionMode mode;
        if (c.type == CrossingType.Tunnel)
            mode = c.axis == CrossingAxis.Horizontal
                ? GameManager.RoadConstructionMode.TunnelHorizontal
                : GameManager.RoadConstructionMode.TunnelVertical;
        else
            mode = c.axis == CrossingAxis.Horizontal
                ? GameManager.RoadConstructionMode.BridgeHorizontal
                : GameManager.RoadConstructionMode.BridgeVertical;

        IndicatrixAPI.instance.SetTile(new Vector3(tile.x + 0.5f, 0f, tile.y + 0.5f), 1, mode);
    }

    protected override bool IsVehicleOnHead(Vector2Int head)
        => VehicleSystem.instance != null && VehicleSystem.instance.IsTileOccupiedByVehicle(head.x, head.y);

    protected override void GetUiMode(out bool tunnelMode, out bool bridgeMode)
    {
        GameManager gm = GameManager.instance;
        var mode = gm != null ? gm.CurrentRoadConstructionMode : GameManager.RoadConstructionMode.None;
        tunnelMode = mode == GameManager.RoadConstructionMode.Tunnel;
        bridgeMode = mode == GameManager.RoadConstructionMode.Bridge;
    }

    public override uint TunnelBuildCost(int length) => ConstructionCosts.RoadTunnelBuildCost(length);
    public override uint BridgeBuildCost(int length, int variant) => ConstructionCosts.RoadBridgeBuildCost(length, variant);
}
