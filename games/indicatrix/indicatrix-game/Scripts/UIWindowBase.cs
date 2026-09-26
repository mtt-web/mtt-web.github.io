using UnityEngine;

/// <summary>
/// UIWindowBase
/// ─────────────────────────────────────────────────────────────────────────
/// Zdieľaný základ pre všetky UI okná hry (RailConstruction, RoadConstruction,
/// FactoryConstruction, ich Depot varianty a StatusFactory).
///
/// Poskytuje JEDNOTNÚ pozičnú logiku:
///   • Automatické vycentrovanie okna na stred obrazovky pri PRVOM otvorení
///     (prvý OnEnable). Pri ďalších otvoreniach si okno PAMÄTÁ polohu, na
///     ktorú ho hráč presunul dragom – nevycentruje sa znova.
///   • Verejné, preťažené metódy SetWindowPosition(...), ktorými sa dá poloha
///     okna nastaviť ČISTO Z KÓDU – bez akejkoľvek Inspector referencie.
///
/// POUŽITIE Z KÓDU (príklad):
///     var win = panel.GetComponent&lt;RailConstructionUIwindow&gt;();
///     win.SetWindowPosition();                                  // stred
///     win.SetWindowPosition(UIWindowBase.WindowAnchor.TopRight); // roh
///     win.SetWindowPosition(new Vector2(120f, -80f));            // presne
///
/// IMPLEMENTAČNÁ POZN.: pracuje sa s anchoredPosition na RectTransform okna.
/// Aby centrovanie fungovalo deterministicky nezávisle od toho, ako má okno
/// v prefab-e nastavené kotvy (anchors) a pivot, metóda EnsureCenteredAnchors()
/// jednorázovo zarovná anchorMin/anchorMax/pivot na stred (0.5, 0.5).
/// Tým "anchoredPosition = (0,0)" vždy znamená presný stred rodičovského
/// Canvas-u / kontajnera. Drag (WindowDragHandle) mení anchoredPosition počas
/// relácie normálne ďalej – a keďže okno sa po prvom otvorení už znova
/// necentruje, presunutá poloha zostane zachovaná aj po zatvorení a opätovnom
/// otvorení okna (v rámci behu hry).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(RectTransform))]
public abstract class UIWindowBase : MonoBehaviour
{
    /// <summary>
    /// Preddefinované ukotvenie okna voči rodičovskému kontajneru.
    /// Center = stred obrazovky (predvolené správanie).
    /// </summary>
    public enum WindowAnchor
    {
        Center,
        TopLeft,
        Top,
        TopRight,
        Left,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }

    [Header("Window Positioning")]
    [Tooltip("Ak je zapnuté, okno sa pri PRVOM otvorení (prvý OnEnable) " +
             "automaticky vycentruje na stred obrazovky. Pri ďalších " +
             "otvoreniach si zachová polohu, na ktorú ho hráč presunul dragom.")]
    [SerializeField] private bool centerOnFirstOpen = true;

    [Tooltip("Voliteľný okraj (v pixeloch) od kraja obrazovky pre ukotvenia " +
             "mimo stredu (rohy/strany). Pri Center sa ignoruje.")]
    [SerializeField] private Vector2 edgeMargin = new Vector2(16f, 16f);

    // Cache RectTransformu okna.
    private RectTransform _rect;

    // True po prvom umiestnení okna (cez OnEnable alebo cez SetWindowPosition).
    // Zabraňuje opätovnému vycentrovaniu pri ďalších otvoreniach – okno si
    // tak "pamätá" polohu z dragu počas behu hry.
    private bool _hasBeenPositioned;

    /// <summary>RectTransform tohto okna (lazy-cached).</summary>
    protected RectTransform Rect
    {
        get
        {
            if (_rect == null)
                _rect = GetComponent<RectTransform>();
            return _rect;
        }
    }

    /// <summary>
    /// Pri PRVOM otvorení okna ho (ak je to povolené) vycentruj na stred.
    /// Pri ďalších otvoreniach sa už nič nerobí – okno si zachová polohu,
    /// na ktorú ho hráč presunul dragom.
    /// OnEnable sa volá vždy, keď sa panel aktivuje cez SetActive(true).
    /// </summary>
    protected virtual void OnEnable()
    {
        if (centerOnFirstOpen && !_hasBeenPositioned)
            SetWindowPosition(); // bez argumentu = Center
    }

    // ─────────────────────────────────────────────────────────────────────
    //  VEREJNÉ API – SetWindowPosition (preťažené)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vycentruje okno na stred obrazovky. Bezargumentové preťaženie –
    /// najčastejší prípad a predvolené správanie.
    /// </summary>
    public void SetWindowPosition()
    {
        SetWindowPosition(WindowAnchor.Center);
    }

    /// <summary>
    /// Umiestni okno podľa preddefinovaného ukotvenia (Center, TopLeft, ...).
    /// Pri ukotveniach mimo stredu sa zohľadní edgeMargin.
    /// </summary>
    /// <param name="anchor">Požadované ukotvenie okna.</param>
    public void SetWindowPosition(WindowAnchor anchor)
    {
        EnsureCenteredAnchors();
        _hasBeenPositioned = true;

        if (anchor == WindowAnchor.Center)
        {
            Rect.anchoredPosition = Vector2.zero;
            return;
        }

        // Rozmery rodičovského kontajnera (Canvas / panel rodič).
        RectTransform parent = Rect.parent as RectTransform;
        Vector2 parentSize = parent != null ? parent.rect.size : GetCanvasSize();
        Vector2 winSize = Rect.rect.size;

        // Maximálny posun od stredu tak, aby okno zostalo celé v kontajneri.
        float maxX = Mathf.Max(0f, (parentSize.x - winSize.x) * 0.5f - edgeMargin.x);
        float maxY = Mathf.Max(0f, (parentSize.y - winSize.y) * 0.5f - edgeMargin.y);

        float x = 0f, y = 0f;

        switch (anchor)
        {
            case WindowAnchor.TopLeft: x = -maxX; y = maxY; break;
            case WindowAnchor.Top: x = 0f; y = maxY; break;
            case WindowAnchor.TopRight: x = maxX; y = maxY; break;
            case WindowAnchor.Left: x = -maxX; y = 0f; break;
            case WindowAnchor.Right: x = maxX; y = 0f; break;
            case WindowAnchor.BottomLeft: x = -maxX; y = -maxY; break;
            case WindowAnchor.Bottom: x = 0f; y = -maxY; break;
            case WindowAnchor.BottomRight: x = maxX; y = -maxY; break;
        }

        Rect.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// Umiestni okno na presnú anchoredPosition (v pixeloch, voči stredu
    /// rodičovského kontajnera). (0,0) = presný stred.
    /// </summary>
    /// <param name="anchoredPosition">Cieľová anchoredPosition.</param>
    public void SetWindowPosition(Vector2 anchoredPosition)
    {
        EnsureCenteredAnchors();
        _hasBeenPositioned = true;
        Rect.anchoredPosition = anchoredPosition;
    }

    /// <summary>
    /// Pohodlné preťaženie pre presné súradnice cez (x, y).
    /// </summary>
    public void SetWindowPosition(float x, float y)
    {
        SetWindowPosition(new Vector2(x, y));
    }

    /// <summary>
    /// Zruší "pamäť polohy" – pri ďalšom otvorení okna (OnEnable) sa okno
    /// znova vycentruje na stred (ak je centerOnFirstOpen zapnuté).
    /// Užitočné, ak treba okno zámerne resetovať do východiskovej polohy.
    /// </summary>
    public void ResetPositionMemory()
    {
        _hasBeenPositioned = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  POMOCNÉ METÓDY
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zarovná anchorMin/anchorMax/pivot okna na stred (0.5, 0.5). Tým sa
    /// zaručí, že anchoredPosition = (0,0) vždy znamená presný stred rodiča,
    /// bez ohľadu na to, ako bol prefab pôvodne nakonfigurovaný v Inspectore.
    /// </summary>
    private void EnsureCenteredAnchors()
    {
        Vector2 center = new Vector2(0.5f, 0.5f);

        // Nastavujeme len ak treba – zbytočne nešahať na RectTransform.
        if (Rect.anchorMin != center) Rect.anchorMin = center;
        if (Rect.anchorMax != center) Rect.anchorMax = center;
        if (Rect.pivot != center) Rect.pivot = center;
    }

    /// <summary>
    /// Fallback pre veľkosť plochy, ak okno nemá RectTransform rodiča
    /// (napr. je priamo na root Canvas-e bez medzipanelu).
    /// </summary>
    private Vector2 GetCanvasSize()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            RectTransform crt = canvas.GetComponent<RectTransform>();
            if (crt != null)
                return crt.rect.size;
        }
        return new Vector2(Screen.width, Screen.height);
    }
}
