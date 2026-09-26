using UnityEngine;

/// <summary>
/// GameRailSelectBridgesMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Okno výberu TYPU ŽELEZNIČNÉHO MOSTA – "GameRailSelectBridgesUIPanel - Window".
/// Obsluhuje CONTENT sekciu (GameRailSelectBridgesText, GRBridgeAButton,
/// GRBridgeBButton, GRBridgeCButton). Analógia k GameWarningMenuUI; celá
/// logika je v GameSelectBridgesMenuUIBase. Cestný/železničný ekvivalent:
/// GameRoadSelectBridgesMenuUI.
///
/// Otvára ho GameManager po overení mosta (RailCrossingSystem). Klik na button typu
/// A/B/C zavrie okno a postaví most daného typu. Close (X)
/// "GRCloseWindowButton" rieši GameRailSelectBridgesUIwindow (= ESC).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameRailSelectBridgesMenuUI : GameSelectBridgesMenuUIBase
{
    private static GameRailSelectBridgesMenuUI _instance;

    /// <summary>
    /// Inštancia okna v scéne (nájde aj neaktívny panel). Null, ak v scéne nie je.
    /// </summary>
    public static GameRailSelectBridgesMenuUI Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<GameRailSelectBridgesMenuUI>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    /// <summary>Už inicializovaná inštancia BEZ vyhľadávania v scéne (pre ESC reset).</summary>
    public static GameRailSelectBridgesMenuUI ExistingInstance => _instance;

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
        && GameManager.instance.CurrentRailConstructionMode == GameManager.RailConstructionMode.Bridge;
}
