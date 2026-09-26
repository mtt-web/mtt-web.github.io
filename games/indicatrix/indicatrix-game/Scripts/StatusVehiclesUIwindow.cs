using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusVehiclesUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusVehiclesMenuUI (root paneli
/// okna). Analógia k StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "RCCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa cez StatusVehiclesMenuUI
///      panel skryje.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri prvom otvorení a verejné, z kódu
///      volateľné metódy SetWindowPosition(...). Žiadna Inspector
///      referencia netreba.
///   4. Voliteľnú FIXNÚ pozíciu okna cez Inspector (useFixedPosition,
///      fixedAnchoredPosition, applyOnEveryOpen) – analógia
///      k FactoryConstructionUIwindow. Pri vypnutom useFixedPosition sa
///      okno správa ako doteraz (centrovanie).
///
/// ROZDIEL OPROTI StatusFactoryUIwindow:
///   StatusVehiclesMenuUI nedrží "aktuálne vybranú" entitu (ako Factory svoju
///   currentFactory) – je to zoznamové okno všetkých vozidiel. Pri zatváraní
///   teda nie je čo "vyčistiť"; zatvorenie len skryje panel. Caption sa
///   nanovo naplní pri ďalšom otvorení (ToggleWindow / OpenWindow).
///
/// ROZDIEL OPROTI construction oknám:
///   Status okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim,
///   takže pri zatváraní NIE JE čo rušiť cez GameManager.SetTerrainMode(None).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject ("FactoryConstructionMenuUI - Title") a drag-uje root
/// panel. Týmto sa dosahuje správanie "okno sa ťahá len za titulok".
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusVehiclesUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"RCCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusVehiclesMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusVehiclesMenuUI statusVehiclesMenuUI;

    [Header("Fixná pozícia okna (výnimka oproti centrovaniu)")]
    [Tooltip("Ak je zapnuté, okno sa NEcentruje, ale otvorí sa na fixných " +
             "súradniciach nižšie. Ak je vypnuté, správa sa ako ostatné okná.")]
    [SerializeField] private bool useFixedPosition = false;

    [Tooltip("Anchored position (x, y) v pixeloch voči STREDU rodiča. " +
             "(0,0) = stred obrazovky, +X vpravo, +Y hore.")]
    [SerializeField] private Vector2 fixedAnchoredPosition = new Vector2(0f, 0f);

    [Tooltip("Ak je zapnuté, okno skočí na fixnú pozíciu pri KAŽDOM otvorení " +
             "(zahodí polohu z dragu). Ak je vypnuté, fixná pozícia sa použije " +
             "len pri prvom otvorení a ďalej si okno pamätá drag – rovnako " +
             "ako ostatné okná v hre.")]
    [SerializeField] private bool applyOnEveryOpen = false;

    // Vlastný príznak – _hasBeenPositioned v UIWindowBase je private,
    // takže si prvé umiestnenie musíme evidovať tu.
    private bool _hasBeenPlaced;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť
        // na rovnakom GameObjecte – StatusVehiclesMenuUI aj StatusVehiclesUIwindow
        // sedia spolu na root paneli okna (rovnaký vzor ako Factory dvojica).
        if (statusVehiclesMenuUI == null)
            statusVehiclesMenuUI = GetComponent<StatusVehiclesMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    /// <summary>
    /// Výnimka z jednotného správania: namiesto vycentrovania umiestni okno
    /// na fixné súradnice. Ak je useFixedPosition vypnuté, deleguje sa na
    /// štandardnú logiku UIWindowBase (centrovanie pri prvom otvorení).
    /// (Analógia k FactoryConstructionUIwindow.)
    /// </summary>
    protected override void OnEnable()
    {
        if (!useFixedPosition)
        {
            base.OnEnable();
            return;
        }

        if (applyOnEveryOpen || !_hasBeenPlaced)
        {
            _hasBeenPlaced = true;
            SetWindowPosition(fixedAnchoredPosition);
        }
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusVehiclesMenuUI.CloseWindow().
        if (statusVehiclesMenuUI != null)
        {
            statusVehiclesMenuUI.CloseWindow();
            return;
        }

        // Fallback: ak referencia na menu chýba, aspoň skry panel.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez externý script, deaktiváciu rodiča, ...), zavoláme CloseWindow().
    ///
    /// POZN.: Opätovné SetActive(false) na už neaktívnom paneli je
    /// idempotentné a neškodí.
    /// </summary>
    void OnDisable()
    {
        if (statusVehiclesMenuUI != null)
            statusVehiclesMenuUI.CloseWindow();
    }

    /// <summary>
    /// Pomôcka pre ladenie: presuň okno v Game view dragom na požadované
    /// miesto a počas Play Mode klikni pravým na hlavičku komponentu →
    /// "Capture Current Position". Hodnotu si potom prepíš aj mimo Play Mode.
    /// </summary>
    [ContextMenu("Capture Current Position")]
    private void CaptureCurrentPosition()
    {
        fixedAnchoredPosition = Rect.anchoredPosition;
        Debug.Log($"[StatusVehiclesUIwindow] Fixed position = {fixedAnchoredPosition}", this);
    }
}
