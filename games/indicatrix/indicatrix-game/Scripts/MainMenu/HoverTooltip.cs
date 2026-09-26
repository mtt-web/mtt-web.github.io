using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Kontextová ponuka (tooltip) pri prejdení myšou nad UI prvkom.
///
/// POUŽITIE:
///   1. Tento skript pridaj na objekt "CCBYattributionText" (ten s TMP textom).
///   2. Na TMP komponente MUSÍ byť zapnuté "Raycast Target" (skript to skúsi zapnúť sám).
///   3. V scéne musí byť EventSystem (v Hierarchy ho máš) a Canvas musí mať GraphicRaycaster.
///   4. Text tooltipu, farby, veľkosť aj font sa nastavujú nižšie v Inspectore
///      – dajú sa meniť aj naživo počas Play mode.
///
/// Tooltip sa vytvorí za behu ako dieťa root Canvasu (nad všetkým ostatným),
/// takže v Hierarchy netreba nič ručne pridávať. Neblokuje kliky ani raycasty,
/// preto neblikáte, keď sa myš dostane "pod" neho.
///
/// Pri vypnutí objektu / panelu (napr. Back z Credits) sa tooltip automaticky skryje.
///
/// GLOBÁLNY PREPÍNAČ (F1):
///   Klávesa F1 (nastavená v GameManager) prepína zobrazovanie VŠETKÝCH tooltipov
///   v hre naraz cez statický flag HoverTooltip.GlobalTooltipsEnabled. Nič iné
///   netreba nastavovať – každá inštancia HoverTooltip ho automaticky rešpektuje.
/// </summary>
public class HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Obsah")]
    [Tooltip("Text zobrazený v kontextovej ponuke. Podporuje viac riadkov aj TMP rich text (<b>, <color>, ...).")]
    [TextArea(2, 8)]
    [SerializeField] private string tooltipText = "CC BY";

    [Tooltip("Voliteľný TMP font. Prázdne = default font z TMP Settings.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    [Header("Veľkosť a odsadenie")]
    [Tooltip("Veľkosť okna v pixeloch (šírka x výška).")]
    [SerializeField] private Vector2 size = new Vector2(250f, 100f);
    [Tooltip("Vnútorné odsadenie textu od okraja okna (px).")]
    [SerializeField] private float padding = 10f;
    [Tooltip("Posun okna voči kurzoru (px). X doprava, Y nahor.")]
    [SerializeField] private Vector2 cursorOffset = new Vector2(18f, -18f);

    [Header("Farby")]
    [Tooltip("Farba pozadia, POUŽIJE SA len ak nie je priradený Background Sprite (plná farebná plocha).")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.92f);
    [SerializeField] private Color textColor = Color.white;
    [Tooltip("Voliteľný sprite pozadia (napr. rámik, obrázok). Ak je priradený, má prednosť pred Background Color.")]
    [SerializeField] private Sprite backgroundSprite;
    [Tooltip("Tint (prefarbenie) aplikované NA VRCH Background Sprite. Default biela = sprite sa zobrazí v pôvodných farbách bez zmeny.")]
    [SerializeField] private Color spriteTint = Color.white;

    [Header("Text")]
    [SerializeField] private float fontSize = 22f;
    [SerializeField] private TextAlignmentOptions textAlignment = TextAlignmentOptions.Center;

    [Header("Správanie")]
    [Tooltip("Zapnuté = okno sleduje kurzor. Vypnuté = zostane tam, kde sa objavilo.")]
    [SerializeField] private bool followMouse = true;
    [Tooltip("Oneskorenie zobrazenia po najetí myšou (sekundy). 0 = okamžite.")]
    [SerializeField] private float showDelay = 0f;

    // ---------------------------------------------------------------------

    private Canvas rootCanvas;
    private RectTransform canvasRect;
    private RectTransform tooltipRect;
    private Image backgroundImage;
    private TextMeshProUGUI label;

    private bool isHovered;
    private bool isVisible;
    private float hoverTimer;

    // ---------------------------------------------------------------------
    // GLOBÁLNY PREPÍNAČ (napr. klávesa F1 v GameManager)
    //
    // Statické pole = spoločné pre VŠETKY inštancie HoverTooltip v hre naraz,
    // takže nie je potrebné ich nikde ručne registrovať/zoraďovať. Každá
    // inštancia si ho sama skontroluje vo svojom Update().
    // ---------------------------------------------------------------------

    private static bool globalTooltipsEnabled = true;

    /// <summary>True = tooltipy sa smú zobrazovať. False = žiadny tooltip v hre sa nezobrazí.</summary>
    public static bool GlobalTooltipsEnabled => globalTooltipsEnabled;

    /// <summary>
    /// Zapne/vypne zobrazovanie VŠETKÝCH tooltipov v hre naraz (napr. klávesa F1).
    /// Práve viditeľné tooltipy sa pri vypnutí okamžite skryjú.
    /// </summary>
    public static void SetGlobalEnabled(bool enabled)
    {
        globalTooltipsEnabled = enabled;
    }

    private void Awake()
    {
        // Bez Raycast Target by TMP text vôbec nedostal OnPointerEnter.
        Graphic g = GetComponent<Graphic>();
        if (g != null) g.raycastTarget = true;

        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null)
        {
            rootCanvas = rootCanvas.rootCanvas;
            canvasRect = rootCanvas.transform as RectTransform;
        }
    }

    private void OnDisable()
    {
        // Panel sa vypol (napr. Back) – tooltip nesmie zostať visieť na obrazovke.
        isHovered = false;
        HideTooltip();
    }

    private void OnDestroy()
    {
        if (tooltipRect != null) Destroy(tooltipRect.gameObject);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        hoverTimer = 0f;
        if (showDelay <= 0f) ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        HideTooltip();
    }

    private void Update()
    {
        // Tooltipy sú globálne vypnuté (F1) → prípadný viditeľný tooltip okamžite
        // skry a ďalej nič neriešime (ani hoverTimer, ani sledovanie kurzora).
        if (!globalTooltipsEnabled)
        {
            if (isVisible) HideTooltip();
            return;
        }

        if (isHovered && !isVisible)
        {
            hoverTimer += Time.unscaledDeltaTime;
            if (hoverTimer >= showDelay) ShowTooltip();
        }

        if (isVisible && followMouse) UpdatePosition();
    }

    // ---------------------------------------------------------------------

    private void ShowTooltip()
    {
        if (!globalTooltipsEnabled) return;
        if (rootCanvas == null) return;

        EnsureTooltip();
        if (tooltipRect == null) return;

        ApplyStyle();
        tooltipRect.gameObject.SetActive(true);
        tooltipRect.SetAsLastSibling(); // vždy navrchu
        isVisible = true;
        UpdatePosition();
    }

    private void HideTooltip()
    {
        isVisible = false;
        if (tooltipRect != null) tooltipRect.gameObject.SetActive(false);
    }

    /// <summary>Vyrobí objekt tooltipu (pozadie + TMP text) ako dieťa root Canvasu.</summary>
    private void EnsureTooltip()
    {
        if (tooltipRect != null) return;

        GameObject go = new GameObject("HoverTooltip (runtime)",
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));

        tooltipRect = go.GetComponent<RectTransform>();
        tooltipRect.SetParent(rootCanvas.transform, false);
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0f, 1f); // ľavý horný roh = kotva pri kurzore

        // Nesmie chytať myš, inak by "prekryl" text a tooltip by blikal.
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;

        backgroundImage = go.GetComponent<Image>();
        backgroundImage.raycastTarget = false;

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;

        go.SetActive(false);
    }

    /// <summary>Aplikuje veľkosť, farby a text – volá sa pri každom zobrazení, takže sa dá ladiť naživo.</summary>
    private void ApplyStyle()
    {
        if (tooltipRect == null) return;

        tooltipRect.sizeDelta = size;

        if (backgroundImage != null)
        {
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = backgroundSprite != null ? Image.Type.Sliced : Image.Type.Simple;

            // Ak je priradený sprite, farbí sa cez samostatný "spriteTint" (default biela = bez zmeny).
            // Ak sprite nie je, ide o plnú farebnú plochu cez "backgroundColor".
            backgroundImage.color = backgroundSprite != null ? spriteTint : backgroundColor;
        }

        if (label != null)
        {
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(padding, padding);
            lr.offsetMax = new Vector2(-padding, -padding);

            if (fontAsset != null) label.font = fontAsset;
            label.text = tooltipText;
            label.color = textColor;
            label.fontSize = fontSize;
            label.alignment = textAlignment;
        }
    }

    /// <summary>Umiestni okno k myši a udrží ho vnútri obrazovky.</summary>
    private void UpdatePosition()
    {
        if (tooltipRect == null || canvasRect == null) return;

        Camera cam = (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : rootCanvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, Input.mousePosition, cam, out local))
            return;

        Vector2 pos = local + cursorOffset;
        Rect r = canvasRect.rect;
        float w = size.x;
        float h = size.y;

        // Ak by okno pretieklo vpravo / dole, preklop ho na druhú stranu kurzora.
        if (pos.x + w > r.xMax) pos.x = local.x - cursorOffset.x - w;
        if (pos.y - h < r.yMin) pos.y = local.y - cursorOffset.y + h;

        // Poistka, aby nikdy nevyliezlo mimo plochy.
        pos.x = Mathf.Clamp(pos.x, r.xMin, Mathf.Max(r.xMin, r.xMax - w));
        pos.y = Mathf.Clamp(pos.y, Mathf.Min(r.yMax, r.yMin + h), r.yMax);

        tooltipRect.anchoredPosition = pos;
    }

    /// <summary>Zmena textu z iného skriptu za behu: GetComponent&lt;HoverTooltip&gt;().SetText("...");</summary>
    public void SetText(string text)
    {
        tooltipText = text;
        if (label != null) label.text = text;
    }
}