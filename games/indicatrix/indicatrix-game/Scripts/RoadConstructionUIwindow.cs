using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RoadConstructionUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k RailConstructionUIwindow. Komponent umiestnený na samotnom
/// paneli RoadConstructionMenuUI. Zabezpečuje:
///   1. Obsluhu voliteľného Close (X) tlačidla.
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa zruší aktuálny konštrukčný
///      režim cez GameManager.SetTerrainMode((RoadConstructionMode)None),
///      aby nezostal "zaseknutý" SnapLineFace/SnapVertex indikátor na mape.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///   4. Voliteľnú FIXNÚ pozíciu okna cez Inspector (useFixedPosition,
///      fixedAnchoredPosition, applyOnEveryOpen) – analógia
///      k FactoryConstructionUIwindow. Pri vypnutom useFixedPosition sa
///      okno správa ako doteraz (centrovanie).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je
/// pripojený na Title GameObject a drag-uje root panel. Týmto sa dosahuje
/// správanie "okno sa ťahá len za titulok", štandardné pre desktopové UI.
///
/// POZN.: GameManager.SetTerrainMode má dve preťaženia – pre RAIL a pre
/// ROAD. Tu používame ROAD verziu, aby sa nastavil CurrentRoadConstructionMode
/// = None (a zároveň sa zachoval prípadný iný RAIL režim, ak by hráč mal
/// otvorené aj druhé okno – v praxi by nemal, ale je to robustnejšie).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class RoadConstructionUIwindow : UIWindowBase
{
    [Header("Optional Close (X) Button")]
    [Tooltip("Voliteľné tlačidlo X na zatvorenie okna. Ak je priradené, " +
             "zavretie zruší aj aktuálny konštrukčný režim.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

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
        // Vyvolaj rovnakú akciu ako klávesa ESC: zruší aktuálny ROAD režim
        // A SCHOVÁ snap visuals (SnapLineFace/SnapVertex) – inak by indikátor
        // ostal "zaseknutý" na tile mape až do stlačenia ESC.
        if (GameManager.instance != null)
            GameManager.instance.PerformEscapeReset();

        // Skryť panel
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez GameMenuUI toggle, externý script, deaktiváciu rodiča, ...),
    /// zrušíme aktuálny ROAD konštrukčný režim. SetTerrainMode(None) je
    /// idempotentné – ak režim už bol None, nič sa nestane.
    /// </summary>
    void OnDisable()
    {
        // GameManager.instance môže byť null pri ukončovaní hry / zmene scény
        if (GameManager.instance != null)
            GameManager.instance.SetTerrainMode(GameManager.RoadConstructionMode.None);
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
        Debug.Log($"[RoadConstructionUIwindow] Fixed position = {fixedAnchoredPosition}", this);
    }
}
