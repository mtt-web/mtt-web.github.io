using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// RailConstructionMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluhuje kliky tlačidiel pre stavbu železničných prvkov.
///
/// SPRÁVANIE TLAČIDIEL (toggle):
///   • Klik na button          → nastaví RailConstructionMode a button ostane
///                               vizuálne ZATLAČENÝ (Pressed Sprite).
///   • Klik na zatlačený button → zavolá GameManager.PerformEscapeReset()
///                               (presne to isté ako klávesa ESC) a button
///                               sa vráti do Default stavu.
///   • Klik na iný button       → predchádzajúci sa odtlačí, nový zatlačí.
///
/// SYNCHRONIZÁCIA SO STAVOM HRY:
///   Vizuál sa NEriadi vlastným príznakom, ale priamo podľa
///   GameManager.CurrentRailConstructionMode (kontrola v LateUpdate, prekreslí
///   sa len pri zmene). Vďaka tomu sa buttony správne odtlačia aj keď režim
///   zruší niečo iné: klávesa ESC, Close (X) okna, výber ROAD / FACTORY
///   režimu (mutex), Define Route v depe, Staff→Factory režim, ...
///
/// MOSTY A TUNELY (RCTunnelButton, RCBridgeButton):
///   Rovnaké toggle správanie ako ostatné buttony. Navyše sa pri aktívnom
///   režime zobrazí pomocný ClickTooltip pri kurzore (analógia
///   HelpClickTooltipHireStaff) – ESC / pravé tlačidlo myši režim ukončí.
///   Pri moste sa text tooltipu po prvom kliku prepne na výzvu ku kliku
///   na koniec mosta (ak je vyplnený bridgeSecondClickText).
///
/// POZN.: Názvy [SerializeField] polí sú nezmenené, takže referencie
/// v Inspectore ostávajú zachované. Nové polia treba priradiť.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class RailConstructionMenuUI : MonoBehaviour
{
    [SerializeField] private Button railConstructionMenuLevelUpButton;
    [SerializeField] private Button railConstructionMenuLevelDownButton;
    [SerializeField] private Button railConstructionMenuDemolishButton;

    [Header("Mosty a tunely")]
    [Tooltip("RCTunnelButton")]
    [SerializeField] private Button railConstructionMenuTunnelButton;
    [Tooltip("RCBridgeButton")]
    [SerializeField] private Button railConstructionMenuBridgeButton;

    [Tooltip("Voliteľný ClickTooltip zobrazený pri kurzore v režime TUNEL " +
             "(napr. \"Click on a slope to build a tunnel. ESC / right click to cancel.\").")]
    [SerializeField] private ClickTooltip tunnelHelpTooltip;

    [Tooltip("Voliteľný ClickTooltip zobrazený pri kurzore v režime MOST.")]
    [SerializeField] private ClickTooltip bridgeHelpTooltip;

    [Tooltip("Text tooltipu mosta PRED prvým klikom. Prázdne = ponechá text z ClickTooltip.")]
    [TextArea(2, 4)]
    [SerializeField] private string bridgeFirstClickText = "Click the START tile of the bridge.\nESC / right click to cancel.";

    [Tooltip("Text tooltipu mosta PO prvom kliku. Prázdne = text sa nemení.")]
    [TextArea(2, 4)]
    [SerializeField] private string bridgeSecondClickText = "Click the END tile of the bridge.\nESC / right click to cancel the start.";

    [Space]

    [SerializeField] private Button railConstructionMenuRailHorizontalButton;
    [SerializeField] private Button railConstructionMenuRailVerticalButton;

    [SerializeField] private Button railConstructionMenuRailCrossroadButton;

    [SerializeField] private Button railConstructionMenuRailCurveRightBottomButton;
    [SerializeField] private Button railConstructionMenuRailCurveLeftBottomButton;
    [SerializeField] private Button railConstructionMenuRailCurveRightTopButton;
    [SerializeField] private Button railConstructionMenuRailCurveLeftTopButton;

    [SerializeField] private Button railConstructionMenuStationHorizontalButton;
    [SerializeField] private Button railConstructionMenuStationVerticalButton;

    [SerializeField] private Button railConstructionMenuDepotHorizontalBottomButton;
    [SerializeField] private Button railConstructionMenuDepotHorizontalTopButton;
    [SerializeField] private Button railConstructionMenuDepotVerticalBottomButton;
    [SerializeField] private Button railConstructionMenuDepotVerticalTopButton;

    [SerializeField] private Button railConstructionMenuRailSwitchHorizontalBottomButton;
    [SerializeField] private Button railConstructionMenuRailSwitchHorizontalTopButton;
    [SerializeField] private Button railConstructionMenuRailSwitchVerticalBottomButton;
    [SerializeField] private Button railConstructionMenuRailSwitchVerticalTopButton;

    // Mapa: režim → button (s pamäťou pôvodných spritov)
    private readonly Dictionary<GameManager.RailConstructionMode, StickyPressedButton> _buttons =
        new Dictionary<GameManager.RailConstructionMode, StickyPressedButton>();

    private GameManager.RailConstructionMode _shownMode = GameManager.RailConstructionMode.None;
    private bool _visualsValid;

    // Stav pomocných tooltipov mostov / tunelov (aby sa volali len pri zmene).
    private GameManager.RailConstructionMode _tooltipMode = GameManager.RailConstructionMode.None;
    private int _bridgeTooltipStage = -1;   // -1 = neznámy, 0 = pred 1. klikom, 1 = po 1. kliku

    void Awake()
    {
        Bind(railConstructionMenuLevelUpButton, GameManager.RailConstructionMode.LevelUp);
        Bind(railConstructionMenuLevelDownButton, GameManager.RailConstructionMode.LevelDown);
        Bind(railConstructionMenuDemolishButton, GameManager.RailConstructionMode.Demolish);

        Bind(railConstructionMenuTunnelButton, GameManager.RailConstructionMode.Tunnel);
        Bind(railConstructionMenuBridgeButton, GameManager.RailConstructionMode.Bridge);

        Bind(railConstructionMenuRailHorizontalButton, GameManager.RailConstructionMode.RailHorizontal);
        Bind(railConstructionMenuRailVerticalButton, GameManager.RailConstructionMode.RailVertical);

        Bind(railConstructionMenuRailCrossroadButton, GameManager.RailConstructionMode.RailCrossroad);

        Bind(railConstructionMenuRailCurveRightBottomButton, GameManager.RailConstructionMode.RailCurveRightBottom);
        Bind(railConstructionMenuRailCurveLeftBottomButton, GameManager.RailConstructionMode.RailCurveLeftBottom);
        Bind(railConstructionMenuRailCurveRightTopButton, GameManager.RailConstructionMode.RailCurveRightTop);
        Bind(railConstructionMenuRailCurveLeftTopButton, GameManager.RailConstructionMode.RailCurveLeftTop);

        Bind(railConstructionMenuStationHorizontalButton, GameManager.RailConstructionMode.StationHorizontal);
        Bind(railConstructionMenuStationVerticalButton, GameManager.RailConstructionMode.StationVertical);

        Bind(railConstructionMenuDepotHorizontalBottomButton, GameManager.RailConstructionMode.DepotHorizontalBottom);
        Bind(railConstructionMenuDepotHorizontalTopButton, GameManager.RailConstructionMode.DepotHorizontalTop);
        Bind(railConstructionMenuDepotVerticalBottomButton, GameManager.RailConstructionMode.DepotVerticalBottom);
        Bind(railConstructionMenuDepotVerticalTopButton, GameManager.RailConstructionMode.DepotVerticalTop);

        Bind(railConstructionMenuRailSwitchHorizontalBottomButton, GameManager.RailConstructionMode.RailSwitchHorizontalBottom);
        Bind(railConstructionMenuRailSwitchHorizontalTopButton, GameManager.RailConstructionMode.RailSwitchHorizontalTop);
        Bind(railConstructionMenuRailSwitchVerticalBottomButton, GameManager.RailConstructionMode.RailSwitchVerticalBottom);
        Bind(railConstructionMenuRailSwitchVerticalTopButton, GameManager.RailConstructionMode.RailSwitchVerticalTop);
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
    /// aktuálneho režimu v GameManager-i (rovnaký princíp ako vizuál buttonov –
    /// zachytí aj ESC, pravé tlačidlo, Close okna, prepnutie režimu).
    /// </summary>
    private void RefreshCrossingTooltips()
    {
        GameManager gm = GameManager.instance;
        GameManager.RailConstructionMode mode =
            gm != null ? gm.CurrentRailConstructionMode : GameManager.RailConstructionMode.None;

        if (mode != _tooltipMode)
        {
            HideCrossingTooltips();
            _tooltipMode = mode;
            _bridgeTooltipStage = -1;

            if (mode == GameManager.RailConstructionMode.Tunnel && tunnelHelpTooltip != null)
                tunnelHelpTooltip.ShowTooltip();
        }

        if (mode == GameManager.RailConstructionMode.Bridge && bridgeHelpTooltip != null)
        {
            var crossingSys = RailCrossingSystem.instance;
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
        _tooltipMode = GameManager.RailConstructionMode.None;
        _bridgeTooltipStage = -1;
    }

    private void Bind(Button button, GameManager.RailConstructionMode mode)
    {
        if (button == null)
        {
            Debug.LogWarning($"[RailConstructionMenuUI] Button pre režim {mode} nie je priradený v Inspectore.", this);
            return;
        }

        _buttons[mode] = new StickyPressedButton(button);
        button.onClick.AddListener(() => OnModeButtonClick(mode));
    }

    private void OnModeButtonClick(GameManager.RailConstructionMode mode)
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        if (gm.CurrentRailConstructionMode == mode)
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
        GameManager.RailConstructionMode current =
            gm != null ? gm.CurrentRailConstructionMode : GameManager.RailConstructionMode.None;

        if (_visualsValid && current == _shownMode) return;

        _visualsValid = true;
        _shownMode = current;

        foreach (KeyValuePair<GameManager.RailConstructionMode, StickyPressedButton> pair in _buttons)
            pair.Value.SetPressed(pair.Key == current);
    }
}
