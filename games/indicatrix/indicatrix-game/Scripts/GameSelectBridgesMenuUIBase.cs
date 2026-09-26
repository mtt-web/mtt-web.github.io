using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameSelectBridgesMenuUIBase
/// ─────────────────────────────────────────────────────────────────────────
/// SPOLOČNÁ LOGIKA okien výberu TYPU MOSTA:
///   • GameRailSelectBridgesMenuUI  ("GameRailSelectBridgesUIPanel - Window")
///   • GameRoadSelectBridgesMenuUI  ("GameRoadSelectBridgesUIPanel - Window")
/// Vzor: GameWarningMenuUI (okno samo nevie, čo sa má stať – volajúci mu
/// odovzdá akcie cez Open(...)).
///
/// PRIEBEH:
///   1. Hráč v režime Most klikne na začiatok a koniec mosta.
///   2. GameManager most OVERÍ a namiesto okamžitej stavby otvorí toto okno
///      (na mape ostane náhľad plánovaného mosta).
///   3. Hráč klikne na GRBridgeAButton / GRBridgeBButton / GRBridgeCButton
///      → okno sa zavrie a GameManager postaví most vybraného typu (A/B/C).
///   4. Zavretie okna cez X (GRCloseWindowButton) = klávesa ESC: nepostaví
///      sa nič a zruší sa výstavbový režim (rieši *SelectBridgesUIwindow).
///
/// ZRUŠENIE BEZ VÝBERU:
///   Okno sa samo zavrie (ako X, bez stavby), ak režim Most príslušnej siete
///   zanikne inak – ESC, pravé tlačidlo, zatvorenie konštrukčného okna,
///   prepnutie na iný režim. Odovzdaná akcia onCancel sa vtedy zavolá presne raz.
///
/// Obsah okna (v "...SelectBridgesUI - Content"):
///   - ...SelectBridgesText → text výzvy (TMP_Text, voliteľný formát s dĺžkou)
///   - GRBridgeAButton / GRBridgeBButton / GRBridgeCButton → typ A / B / C
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public abstract class GameSelectBridgesMenuUIBase : MonoBehaviour
{
    [Header("Root Panel")]
    [Tooltip("Root panel '...SelectBridgesUIPanel - Window'. Ak ostane prázdny, " +
             "použije sa tento gameObject (skript je na root paneli).")]
    [SerializeField] private GameObject selectBridgesUIPanel;

    [Header("Select Bridges – Content")]
    [Tooltip("...SelectBridgesText – text výzvy na výber mosta.")]
    [SerializeField] private TMP_Text selectBridgesText;

    [Tooltip("GRBridgeAButton – postaví most TYPU A.")]
    [SerializeField] private Button bridgeAButton;

    [Tooltip("GRBridgeBButton – postaví most TYPU B.")]
    [SerializeField] private Button bridgeBButton;

    [Tooltip("GRBridgeCButton – postaví most TYPU C.")]
    [SerializeField] private Button bridgeCButton;

    [Header("Voliteľné – text a ceny")]
    [Tooltip("Formát textu výzvy. {0} = dĺžka mosta v tiloch. " +
             "Prázdne = ponechá text nastavený priamo na ...SelectBridgesText.")]
    [TextArea(2, 4)]
    [SerializeField] private string messageFormat = "";

    [Tooltip("Voliteľný TMP text pre cenu mosta typu A (napr. popisok na buttone).")]
    [SerializeField] private TMP_Text bridgeACostText;

    [Tooltip("Voliteľný TMP text pre cenu mosta typu B.")]
    [SerializeField] private TMP_Text bridgeBCostText;

    [Tooltip("Voliteľný TMP text pre cenu mosta typu C.")]
    [SerializeField] private TMP_Text bridgeCCostText;

    [Tooltip("Formát ceny. {0} = cena v CR.")]
    [SerializeField] private string costFormat = "{0} CR";

    private Action<int> _onSelect;
    private Action _onCancel;
    private bool _pending;

    // True, ak už niekto zavolal Open() – Start() potom panel neskryje.
    private bool _openRequested;

    private GameObject Panel => selectBridgesUIPanel != null ? selectBridgesUIPanel : gameObject;

    /// <summary>True, ak je okno práve zobrazené.</summary>
    public bool IsOpen => Panel.activeInHierarchy;

    /// <summary>True, ak je aktívny režim Most siete, ku ktorej okno patrí.</summary>
    protected abstract bool IsBridgeModeActive();

    protected virtual void Awake()
    {
        if (bridgeAButton != null) bridgeAButton.onClick.AddListener(() => OnBridgeButtonClick(0));
        if (bridgeBButton != null) bridgeBButton.onClick.AddListener(() => OnBridgeButtonClick(1));
        if (bridgeCButton != null) bridgeCButton.onClick.AddListener(() => OnBridgeButtonClick(2));
    }

    protected virtual void Start()
    {
        // Rovnaký vzor ako GameWarningMenuUI: panel skryjeme až v Start().
        // Poistka: ak bol panel pri štarte neaktívny, Awake/Start zbehnú až pri
        // prvom Open() – vtedy ho tu NESMIEME hneď skryť.
        if (!_openRequested)
            Panel.SetActive(false);
    }

    protected virtual void Update()
    {
        if (!_pending || !Panel.activeSelf) return;

        // Režim Most zanikol (ESC, iný režim, zatvorené konštrukčné okno)
        // → výber zrušíme bez stavby.
        if (!IsBridgeModeActive())
            Close();
    }

    protected virtual void OnDisable()
    {
        // Okno sa skrylo akýmkoľvek spôsobom bez výberu → zrušenie.
        CancelPending();
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Zobrazí okno výberu typu mosta.
    /// </summary>
    /// <param name="bridgeLength">Dĺžka mosta v tiloch (pre text výzvy).</param>
    /// <param name="variantCosts">Ceny typov A/B/C (voliteľné, pre popisky).</param>
    /// <param name="onSelect">Akcia po výbere typu (0 = A, 1 = B, 2 = C).</param>
    /// <param name="onCancel">Akcia pri zatvorení bez výberu.</param>
    public void Open(int bridgeLength, uint[] variantCosts, Action<int> onSelect, Action onCancel)
    {
        CancelPending();   // prípadný starší výber sa zruší

        _openRequested = true;
        _onSelect = onSelect;
        _onCancel = onCancel;
        _pending = true;

        if (selectBridgesText != null && !string.IsNullOrEmpty(messageFormat))
            selectBridgesText.text = SafeFormat(messageFormat, bridgeLength);

        SetCost(bridgeACostText, variantCosts, 0);
        SetCost(bridgeBCostText, variantCosts, 1);
        SetCost(bridgeCCostText, variantCosts, 2);

        GameObject panel = Panel;
        panel.transform.SetAsLastSibling();   // nad konštrukčným oknom
        panel.SetActive(true);
    }

    /// <summary>Zavrie okno BEZ výberu (zavolá onCancel, ak výber ešte čakal).</summary>
    public void Close()
    {
        CancelPending();
        Panel.SetActive(false);
    }

    // =====================================================================
    // BUTTON HANDLERS
    // =====================================================================

    private void OnBridgeButtonClick(int variant)
    {
        GameManager.instance?.PlaySfxUIclick();

        if (!_pending)
        {
            Panel.SetActive(false);
            return;
        }

        // Akciu si odložíme PRED zatvorením a vykonáme ju až po skrytí okna –
        // OnDisable tak nezavolá onCancel a prípadná chyba stavby okno nenechá otvorené.
        Action<int> select = _onSelect;
        ClearPending();
        Panel.SetActive(false);
        select?.Invoke(variant);
    }

    private void CancelPending()
    {
        if (!_pending) return;
        Action cancel = _onCancel;
        ClearPending();
        cancel?.Invoke();
    }

    private void ClearPending()
    {
        _pending = false;
        _onSelect = null;
        _onCancel = null;
    }

    private void SetCost(TMP_Text label, uint[] costs, int index)
    {
        if (label == null || costs == null || index >= costs.Length) return;
        label.text = SafeFormat(costFormat, costs[index]);
    }

    private static string SafeFormat(string format, object value)
    {
        try { return string.Format(format, value); }
        catch (FormatException) { return format; }
    }
}
