using UnityEngine;
using UnityEngine.UI;

public class FactoryConstructionUIwindow : UIWindowBase
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
    [SerializeField] private bool useFixedPosition = true;

    [Tooltip("Anchored position (x, y) v pixeloch voči STREDU rodiča. " +
             "(0,0) = stred obrazovky, +X vpravo, +Y hore.")]
    [SerializeField] private Vector2 fixedAnchoredPosition = new Vector2(-600f, 0f);

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
        if (GameManager.instance != null)
            GameManager.instance.PerformEscapeReset();

        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    void OnDisable()
    {
        if (GameManager.instance != null)
            GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.None);
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
        Debug.Log($"[FactoryConstructionUIwindow] Fixed position = {fixedAnchoredPosition}", this);
    }
}