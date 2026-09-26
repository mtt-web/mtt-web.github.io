using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// RoadConstructionMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k RailConstructionMenuUI – obsluhuje kliky tlačidiel pre stavbu
/// cestných prvkov a prepína GameManager do príslušného RoadConstructionMode.
///
/// Štruktúra je úplne analogická s RailConstructionMenuUI:
///   - LevelUp / LevelDown / Demolish  (univerzálne, ID = 0 pre Demolish)
///   - Road priame (Horizontal, Vertical) + Crossroad
///   - 4 zatáčky (RB, LB, RT, LT)
///   - 2 stanice/zastávky (Horizontal, Vertical) – v cestnom kontexte
///     ide o BusStop/Truck stop
///   - 4 depá (Horizontal Bottom/Top, Vertical Bottom/Top) – v cestnom
///     kontexte garáže/dopravné depo
///   - 4 výhybky/Y-križovatky (HB, HT, VB, VT) – v cestnom kontexte ide
///     o T-križovatky / Y-rozdvojenia
///
/// SPRÁVANIE TLAČIDIEL (toggle) – rovnaké ako v RailConstructionMenuUI:
///   • Klik na button          → nastaví RoadConstructionMode a button ostane
///                               vizuálne ZATLAČENÝ (Pressed Sprite).
///   • Klik na zatlačený button → zavolá GameManager.PerformEscapeReset()
///                               (presne to isté ako klávesa ESC) a button
///                               sa vráti do Default stavu.
///   • Klik na iný button       → predchádzajúci sa odtlačí, nový zatlačí.
///
/// SYNCHRONIZÁCIA SO STAVOM HRY:
///   Vizuál sa riadi priamo podľa GameManager.CurrentRoadConstructionMode
///   (kontrola v LateUpdate, prekreslí sa len pri zmene). Buttony sa teda
///   správne odtlačia aj pri ESC, Close (X) okna, výbere RAIL / FACTORY
///   režimu (mutex), Define Route v depe, Staff→Factory režime, ...
///
/// CESTNÉ MOSTY A TUNELY (RCTunnelButton, RCBridgeButton):
///   Úplná analógia k RailConstructionMenuUI. Rovnaké toggle správanie ako
///   ostatné buttony; pri aktívnom režime sa zobrazí pomocný ClickTooltip pri
///   kurzore (analógia HelpClickTooltipHireStaff) – ESC / pravé tlačidlo myši
///   režim ukončí. Pri moste sa text tooltipu po prvom kliku prepne na výzvu
///   ku kliku na koniec mosta. Logiku stavby rieši RoadCrossingSystem.
///
/// POZN.: V Hierarchy zostali názvy GameObjectov ako "RC..." (Rail prefix),
/// to je v poriadku – v Inspectore stačí priradiť konkrétne tlačidlá z
/// RoadConstructionMenuUI panelu. Názvy [SerializeField] polí sú nezmenené,
/// takže referencie v Inspectore ostávajú zachované. Nové polia (mosty,
/// tunely, tooltipy) treba priradiť.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class RoadConstructionMenuUI : MonoBehaviour
{
    [SerializeField] private Button roadConstructionMenuLevelUpButton;
    [SerializeField] private Button roadConstructionMenuLevelDownButton;
    [SerializeField] private Button roadConstructionMenuDemolishButton;

    [Header("Cestné mosty a tunely")]
    [Tooltip("RCTunnelButton (v RoadConstructionMenuUI paneli)")]
    [SerializeField] private Button roadConstructionMenuTunnelButton;
    [Tooltip("RCBridgeButton (v RoadConstructionMenuUI paneli)")]
    [SerializeField] private Button roadConstructionMenuBridgeButton;

    [Tooltip("Voliteľný ClickTooltip zobrazený pri kurzore v režime TUNEL. " +
             "Použi SAMOSTATNÝ objekt – nie ten istý ako v RailConstructionMenuUI.")]
    [SerializeField] private ClickTooltip tunnelHelpTooltip;

    [Tooltip("Voliteľný ClickTooltip zobrazený pri kurzore v režime MOST. " +
             "Použi SAMOSTATNÝ objekt – nie ten istý ako v RailConstructionMenuUI.")]
    [SerializeField] private ClickTooltip bridgeHelpTooltip;

    [Tooltip("Text tooltipu mosta PRED prvým klikom. Prázdne = ponechá text z ClickTooltip.")]
    [TextArea(2, 4)]
    [SerializeField] private string bridgeFirstClickText = "Click the START tile of the road bridge.\nESC / right click to cancel.";

    [Tooltip("Text tooltipu mosta PO prvom kliku. Prázdne = text sa nemení.")]
    [TextArea(2, 4)]
    [SerializeField] private string bridgeSecondClickText = "Click the END tile of the road bridge.\nESC / right click to cancel the start.";

    [Space]

    [SerializeField] private Button roadConstructionMenuRoadHorizontalButton;
    [SerializeField] private Button roadConstructionMenuRoadVerticalButton;

    [SerializeField] private Button roadConstructionMenuRoadCrossroadButton;

    [SerializeField] private Button roadConstructionMenuRoadCurveRightBottomButton;
    [SerializeField] private Button roadConstructionMenuRoadCurveLeftBottomButton;
    [SerializeField] private Button roadConstructionMenuRoadCurveRightTopButton;
    [SerializeField] private Button roadConstructionMenuRoadCurveLeftTopButton;

    [SerializeField] private Button roadConstructionMenuStationHorizontalButton;
    [SerializeField] private Button roadConstructionMenuStationVerticalButton;

    [SerializeField] private Button roadConstructionMenuDepotHorizontalBottomButton;
    [SerializeField] private Button roadConstructionMenuDepotHorizontalTopButton;
    [SerializeField] private Button roadConstructionMenuDepotVerticalBottomButton;
    [SerializeField] private Button roadConstructionMenuDepotVerticalTopButton;

    [SerializeField] private Button roadConstructionMenuRoadSwitchHorizontalBottomButton;
    [SerializeField] private Button roadConstructionMenuRoadSwitchHorizontalTopButton;
    [SerializeField] private Button roadConstructionMenuRoadSwitchVerticalBottomButton;
    [SerializeField] private Button roadConstructionMenuRoadSwitchVerticalTopButton;

    // Mapa: režim → button (s pamäťou pôvodných spritov)
    private readonly Dictionary<GameManager.RoadConstructionMode, StickyPressedButton> _buttons =
        new Dictionary<GameManager.RoadConstructionMode, StickyPressedButton>();

    private GameManager.RoadConstructionMode _shownMode = GameManager.RoadConstructionMode.None;
    private bool _visualsValid;

    // Stav pomocných tooltipov mostov / tunelov (aby sa volali len pri zmene).
    private GameManager.RoadConstructionMode _tooltipMode = GameManager.RoadConstructionMode.None;
    private int _bridgeTooltipStage = -1;   // -1 = neznámy, 0 = pred 1. klikom, 1 = po 1. kliku

    void Awake()
    {
        Bind(roadConstructionMenuLevelUpButton, GameManager.RoadConstructionMode.LevelUp);
        Bind(roadConstructionMenuLevelDownButton, GameManager.RoadConstructionMode.LevelDown);
        Bind(roadConstructionMenuDemolishButton, GameManager.RoadConstructionMode.Demolish);

        Bind(roadConstructionMenuTunnelButton, GameManager.RoadConstructionMode.Tunnel);
        Bind(roadConstructionMenuBridgeButton, GameManager.RoadConstructionMode.Bridge);

        Bind(roadConstructionMenuRoadHorizontalButton, GameManager.RoadConstructionMode.RoadHorizontal);
        Bind(roadConstructionMenuRoadVerticalButton, GameManager.RoadConstructionMode.RoadVertical);

        Bind(roadConstructionMenuRoadCrossroadButton, GameManager.RoadConstructionMode.RoadCrossroad);

        Bind(roadConstructionMenuRoadCurveRightBottomButton, GameManager.RoadConstructionMode.RoadCurveRightBottom);
        Bind(roadConstructionMenuRoadCurveLeftBottomButton, GameManager.RoadConstructionMode.RoadCurveLeftBottom);
        Bind(roadConstructionMenuRoadCurveRightTopButton, GameManager.RoadConstructionMode.RoadCurveRightTop);
        Bind(roadConstructionMenuRoadCurveLeftTopButton, GameManager.RoadConstructionMode.RoadCurveLeftTop);

        Bind(roadConstructionMenuStationHorizontalButton, GameManager.RoadConstructionMode.StationHorizontal);
        Bind(roadConstructionMenuStationVerticalButton, GameManager.RoadConstructionMode.StationVertical);

        Bind(roadConstructionMenuDepotHorizontalBottomButton, GameManager.RoadConstructionMode.DepotHorizontalBottom);
        Bind(roadConstructionMenuDepotHorizontalTopButton, GameManager.RoadConstructionMode.DepotHorizontalTop);
        Bind(roadConstructionMenuDepotVerticalBottomButton, GameManager.RoadConstructionMode.DepotVerticalBottom);
        Bind(roadConstructionMenuDepotVerticalTopButton, GameManager.RoadConstructionMode.DepotVerticalTop);

        Bind(roadConstructionMenuRoadSwitchHorizontalBottomButton, GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom);
        Bind(roadConstructionMenuRoadSwitchHorizontalTopButton, GameManager.RoadConstructionMode.RoadSwitchHorizontalTop);
        Bind(roadConstructionMenuRoadSwitchVerticalBottomButton, GameManager.RoadConstructionMode.RoadSwitchVerticalBottom);
        Bind(roadConstructionMenuRoadSwitchVerticalTopButton, GameManager.RoadConstructionMode.RoadSwitchVerticalTop);
    }

    void OnEnable()
    {
        // Pri (znovu)otvorení okna vynútime prekreslenie podľa aktuálneho režimu.
        _visualsValid = false;
        RefreshVisuals();
    }

    void LateUpdate()
    {
        // LateUpdate – zachytí aj zmenu režimu, ktorú v tom istom frame urobil
        // GameManager.Update (napr. ESC).
        RefreshVisuals();
        RefreshCrossingTooltips();
    }

    void OnDisable()
    {
        HideCrossingTooltips();
    }

    /// <summary>
    /// Zobrazí / skryje pomocný tooltip pre režim TUNEL a MOST podľa
    /// GameManager.CurrentRoadConstructionMode (zachytí aj ESC, pravé tlačidlo,
    /// Close okna, prepnutie režimu). Analógia k RailConstructionMenuUI.
    /// </summary>
    private void RefreshCrossingTooltips()
    {
        GameManager gm = GameManager.instance;
        GameManager.RoadConstructionMode mode =
            gm != null ? gm.CurrentRoadConstructionMode : GameManager.RoadConstructionMode.None;

        if (mode != _tooltipMode)
        {
            HideCrossingTooltips();
            _tooltipMode = mode;
            _bridgeTooltipStage = -1;

            if (mode == GameManager.RoadConstructionMode.Tunnel && tunnelHelpTooltip != null)
                tunnelHelpTooltip.ShowTooltip();
        }

        if (mode == GameManager.RoadConstructionMode.Bridge && bridgeHelpTooltip != null)
        {
            var crossingSys = RoadCrossingSystem.instance;
            bool pending = crossingSys != null && crossingSys.HasPendingBridgeStart;
            bool selecting = crossingSys != null && crossingSys.IsAwaitingVariantSelection;
            int stage = selecting ? 2 : pending ? 1 : 0;   // 2 = otvorené okno výberu typu
            if (stage != _bridgeTooltipStage)
            {
                _bridgeTooltipStage = stage;
                if (selecting)
                {
                    bridgeHelpTooltip.HideTooltip();
                }
                else
                {
                    string text = pending ? bridgeSecondClickText : bridgeFirstClickText;
                    if (!string.IsNullOrEmpty(text)) bridgeHelpTooltip.SetText(text);
                    bridgeHelpTooltip.ShowTooltip();
                }
            }
        }
    }

    private void HideCrossingTooltips()
    {
        if (tunnelHelpTooltip != null) tunnelHelpTooltip.HideTooltip();
        if (bridgeHelpTooltip != null) bridgeHelpTooltip.HideTooltip();
        _tooltipMode = GameManager.RoadConstructionMode.None;
        _bridgeTooltipStage = -1;
    }

    private void Bind(Button button, GameManager.RoadConstructionMode mode)
    {
        if (button == null)
        {
            Debug.LogWarning($"[RoadConstructionMenuUI] Button pre režim {mode} nie je priradený v Inspectore.", this);
            return;
        }

        _buttons[mode] = new StickyPressedButton(button);
        button.onClick.AddListener(() => OnModeButtonClick(mode));
    }

    private void OnModeButtonClick(GameManager.RoadConstructionMode mode)
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        if (gm.CurrentRoadConstructionMode == mode)
        {
            // Opätovný klik na zatlačený button = rovnaká akcia ako klávesa ESC.
            gm.PerformEscapeReset();
        }
        else
        {
            gm.SetTerrainMode(mode);
        }

        RefreshVisuals();

        // Zrušíme UI "Selected" stav buttonu – inak by Unity nechalo button
        // označený (Space/Enter by ho znovu prepínal) a po odtlačení by pri
        // hoveri neukazoval Highlight Sprite.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void RefreshVisuals()
    {
        GameManager gm = GameManager.instance;
        GameManager.RoadConstructionMode current =
            gm != null ? gm.CurrentRoadConstructionMode : GameManager.RoadConstructionMode.None;

        if (_visualsValid && current == _shownMode) return;

        _visualsValid = true;
        _shownMode = current;

        foreach (KeyValuePair<GameManager.RoadConstructionMode, StickyPressedButton> pair in _buttons)
            pair.Value.SetPressed(pair.Key == current);
    }
}
