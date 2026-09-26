using UnityEngine;

/// <summary>
/// GameRoadSelectBridgesMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Okno výberu TYPU CESTNÉHO MOSTA – "GameRoadSelectBridgesUIPanel - Window".
/// Obsluhuje CONTENT sekciu (GameRoadSelectBridgesText, GRBridgeAButton,
/// GRBridgeBButton, GRBridgeCButton). Analógia k GameWarningMenuUI; celá
/// logika je v GameSelectBridgesMenuUIBase. Cestný/železničný ekvivalent:
/// GameRailSelectBridgesMenuUI.
///
/// Otvára ho GameManager po overení mosta (RoadCrossingSystem). Klik na button typu
/// A/B/C zavrie okno a postaví most daného typu. Close (X)
/// "GRCloseWindowButton" rieši GameRoadSelectBridgesUIwindow (= ESC).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameRoadSelectBridgesMenuUI : GameSelectBridgesMenuUIBase
{
    private static GameRoadSelectBridgesMenuUI _instance;

    /// <summary>
    /// Inštancia okna v scéne (nájde aj neaktívny panel). Null, ak v scéne nie je.
    /// </summary>
    public static GameRoadSelectBridgesMenuUI Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<GameRoadSelectBridgesMenuUI>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    /// <summary>Už inicializovaná inštancia BEZ vyhľadávania v scéne (pre ESC reset).</summary>
    public static GameRoadSelectBridgesMenuUI ExistingInstance => _instance;

    protected override void Awake()
    {
        base.Awake();
        if (_instance == null) _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    protected override bool IsBridgeModeActive()
        => GameManager.instance != null
        && GameManager.instance.CurrentRoadConstructionMode == GameManager.RoadConstructionMode.Bridge;
}
