using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Jednoúčelový tooltip vyvolaný kliknutím (nie hoverom) + voliteľná IKONKA
/// pri kurzore, ktorá sa zobrazuje spolu s ním.
///
/// POUŽITIE:
///   1. Tento skript pridaj na ľubovoľný objekt v scéne (napr. na ten istý,
///      kde máš iné UI-manažér skripty, alebo priamo na prvý button).
///   2. V Inspectore vyplň text tooltipu, farby, veľkosť atď.
///   3. Na PRVOM buttone (OnClick ()) pretiahni tento objekt a vyber
///      ClickTooltip -> ShowTooltip().
///   4. Na DRUHOM buttone (OnClick ()) rovnako vyber ClickTooltip -> HideTooltip().
///
/// Tooltip aj ikonka sa zobrazia pri kurzore a sledujú ho, až kým sa nezavolá
/// HideTooltip() (klik na továreň / ESC / PerformEscapeReset v GameManageri).
///
/// ROZDIEL MEDZI TOOLTIPOM A IKONKOU:
///   • TEXTOVÝ TOOLTIP rešpektuje globálny prepínač HoverTooltip.GlobalTooltipsEnabled
///     (F1) – ak sú tooltipy vypnuté, ShowTooltip() text nezobrazí a viditeľný
///     text sa okamžite skryje.
///   • IKONKA (Sprite) na F1 NEREAGUJE – zobrazí sa vždy po zavolaní
///     ShowTooltip() a zmizne až pri HideTooltip().
/// </summary>
public class ClickTooltip : MonoBehaviour
{
    [Header("Obsah")]
    [Tooltip("Text zobrazený v tooltipe. Podporuje viac riadkov aj TMP rich text (<b>, <color>, ...).")]
    [TextArea(2, 8)]
    [SerializeField] private string tooltipText = "Popis";

    [Tooltip("Voliteľný TMP font. Prázdne = default font z TMP Settings.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    [Header("Veľkosť a odsadenie")]
    [SerializeField] private Vector2 size = new Vector2(250f, 100f);
    [SerializeField] private float padding = 10f;
    [Tooltip("Posun okna voči mieste kliknutia (px). X doprava, Y nahor.")]
    [SerializeField] private Vector2 cursorOffset = new Vector2(18f, -18f);

    [Header("Farby")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.92f);
    [SerializeField] private Color textColor = Color.white;
    [Tooltip("Voliteľný sprite pozadia. Ak je priradený, má prednosť pred Background Color.")]
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Color spriteTint = Color.white;

    [Header("Text")]
    [SerializeField] private float fontSize = 22f;
    [SerializeField] private TextAlignmentOptions textAlignment = TextAlignmentOptions.Center;

    // ---------------------------------------------------------------------
    // IKONKA PRI KURZORE (zobrazuje sa spolu s tooltipom, ale NEREAGUJE na F1)
    // ---------------------------------------------------------------------
    [Header("Ikonka pri kurzore (nezávislá od F1)")]
    [Tooltip("Sprite ikonky zobrazenej pri kurzore. Prázdne = žiadna ikonka.")]
    [SerializeField] private Sprite iconSprite;

    [Tooltip("Veľkosť ikonky v pixeloch (šírka × výška).")]
    [SerializeField] private Vector2 iconSize = new Vector2(48f, 48f);

    [Tooltip("Posun ikonky voči pozícii kurzora (px). X doprava, Y nahor.")]
    [SerializeField] private Vector2 iconOffset = new Vector2(18f, 18f);

    [Tooltip("Pivot ikonky: (0,1) = ľavý horný roh pri kurzore, (0.5,0.5) = ikonka vycentrovaná na kurzore.")]
    [SerializeField] private Vector2 iconPivot = new Vector2(0f, 1f);

    [Tooltip("Farba / tint ikonky. Biela = originálne farby sprite.")]
    [SerializeField] private Color iconColor = Color.white;

    [Tooltip("Zachovať pomer strán sprite vnútri zadanej veľkosti.")]
    [SerializeField] private bool iconPreserveAspect = true;

    [Tooltip("Ak je zapnuté, ikonka sa vždy udrží vnútri plochy Canvasu (neprelezie cez okraj obrazovky).")]
    [SerializeField] private bool iconClampToScreen = true;

    [Header("Canvas (voliteľné)")]
    [Tooltip("Root Canvas, do ktorého sa tooltip vytvorí. Ak necháš prázdne, nájde sa automaticky (najbližší Canvas v rodičoch, inak akýkoľvek Canvas v scéne).")]
    [SerializeField] private Canvas rootCanvas;

    // ---------------------------------------------------------------------

    private RectTransform canvasRect;
    private RectTransform tooltipRect;
    private Image backgroundImage;
    private TextMeshProUGUI label;

    private RectTransform iconRect;
    private Image iconImage;

    /// <summary>True od ShowTooltip() po HideTooltip() – riadi IKONKU (bez ohľadu na F1).</summary>
    private bool isActive;

    /// <summary>True, kým je viditeľné TEXTOVÉ okno tooltipu (rešpektuje F1).</summary>
    private bool isVisible;

    private void Awake()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null)
            rootCanvas = FindObjectOfType<Canvas>();

        if (rootCanvas != null)
        {
            rootCanvas = rootCanvas.rootCanvas;
            canvasRect = rootCanvas.transform as RectTransform;
        }
    }

    private void OnDisable()
    {
        HideTooltip();
    }

    private void OnDestroy()
    {
        if (tooltipRect != null) Destroy(tooltipRect.gameObject);
        if (iconRect != null) Destroy(iconRect.gameObject);
    }

    private void Update()
    {
        if (!isActive) return;

        // Ak sa medzitým vypli tooltipy globálne (F1), skry LEN textové okno.
        // Ikonka ostáva – tá na F1 zámerne nereaguje.
        if (isVisible && !HoverTooltip.GlobalTooltipsEnabled)
        {
            isVisible = false;
            if (tooltipRect != null) tooltipRect.gameObject.SetActive(false);
        }

        // Priebežne sleduj kurzor, kým je tooltip / ikonka viditeľná.
        UpdatePositionAtCursor();
        UpdateIconPositionAtCursor();
    }

    // ---------------------------------------------------------------------
    // Toto priraď na OnClick() prvého buttonu (Hire).
    // ---------------------------------------------------------------------
    public void ShowTooltip()
    {
        if (rootCanvas == null || canvasRect == null) return;

        // ── IKONKA – zobrazí sa VŽDY, bez ohľadu na F1 ──
        isActive = true;
        EnsureIcon();
        if (iconRect != null)
        {
            ApplyIconStyle();
            iconRect.gameObject.SetActive(iconSprite != null);
            iconRect.SetAsLastSibling();
            UpdateIconPositionAtCursor();
        }

        // ── TEXTOVÝ TOOLTIP – len ak nie sú tooltipy vypnuté cez F1 ──
        if (!HoverTooltip.GlobalTooltipsEnabled) return;

        EnsureTooltip();
        if (tooltipRect == null) return;

        ApplyStyle();
        tooltipRect.gameObject.SetActive(true);
        tooltipRect.SetAsLastSibling();
        // Ikonka nech je vždy nad textovým oknom.
        if (iconRect != null) iconRect.SetAsLastSibling();
        isVisible = true;
        UpdatePositionAtCursor();
    }

    // ---------------------------------------------------------------------
    // Toto priraď na OnClick() druhého buttonu.
    // Volá sa aj z GameManager.PerformEscapeReset() / pri kliku na továreň.
    // Skryje tooltip AJ ikonku.
    // ---------------------------------------------------------------------
    public void HideTooltip()
    {
        isVisible = false;
        isActive = false;
        if (tooltipRect != null) tooltipRect.gameObject.SetActive(false);
        if (iconRect != null) iconRect.gameObject.SetActive(false);
    }

    /// <summary>Vyrobí objekt tooltipu (pozadie + TMP text) ako dieťa root Canvasu.</summary>
    private void EnsureTooltip()
    {
        if (tooltipRect != null) return;

        GameObject go = new GameObject("ClickTooltip (runtime)",
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));

        tooltipRect = go.GetComponent<RectTransform>();
        tooltipRect.SetParent(rootCanvas.transform, false);
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0f, 1f); // ľavý horný roh = kotva pri kurzore

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false; // nesmie blokovať klik na ďalší button

        backgroundImage = go.GetComponent<Image>();
        backgroundImage.raycastTarget = false;

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        label = textGo.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;

        go.SetActive(false);
    }

    /// <summary>Vyrobí objekt ikonky (samostatný Image) ako dieťa root Canvasu.</summary>
    private void EnsureIcon()
    {
        if (iconRect != null) return;
        if (rootCanvas == null) return;

        GameObject go = new GameObject("ClickTooltipIcon (runtime)",
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));

        iconRect = go.GetComponent<RectTransform>();
        iconRect.SetParent(rootCanvas.transform, false);
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = iconPivot;

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false; // nesmie blokovať klik na mapu ani na UI

        iconImage = go.GetComponent<Image>();
        iconImage.raycastTarget = false;

        go.SetActive(false);
    }

    /// <summary>Aplikuje veľkosť, farby a text – volá sa pri každom zobrazení.</summary>
    private void ApplyStyle()
    {
        if (tooltipRect == null) return;

        tooltipRect.sizeDelta = size;

        if (backgroundImage != null)
        {
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = backgroundSprite != null ? Image.Type.Sliced : Image.Type.Simple;
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

    /// <summary>Aplikuje sprite, veľkosť, pivot a tint ikonky – volá sa pri každom zobrazení.</summary>
    private void ApplyIconStyle()
    {
        if (iconRect == null) return;

        iconRect.pivot = iconPivot;
        iconRect.sizeDelta = iconSize;

        if (iconImage != null)
        {
            iconImage.sprite = iconSprite;
            iconImage.type = Image.Type.Simple;
            iconImage.preserveAspect = iconPreserveAspect;
            iconImage.color = iconColor;
            iconImage.enabled = iconSprite != null;
        }
    }

    /// <summary>Prepočíta pozíciu kurzora na lokálne súradnice root Canvasu.</summary>
    private bool TryGetCursorLocalPoint(out Vector2 local)
    {
        local = Vector2.zero;
        if (canvasRect == null || rootCanvas == null) return false;

        Camera cam = (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : rootCanvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, Input.mousePosition, cam, out local);
    }

    /// <summary>Umiestni okno na aktuálnu pozíciu kurzora (volá sa každý frame, kým je tooltip viditeľný) a udrží ho vnútri obrazovky.</summary>
    private void UpdatePositionAtCursor()
    {
        if (!isVisible) return;
        if (tooltipRect == null || canvasRect == null) return;

        Vector2 local;
        if (!TryGetCursorLocalPoint(out local)) return;

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

    /// <summary>
    /// Umiestni IKONKU na aktuálnu pozíciu kurzora. Beží nezávisle od textového
    /// tooltipu (a teda aj nezávisle od F1) – stačí, že je aktívny režim (isActive).
    /// </summary>
    private void UpdateIconPositionAtCursor()
    {
        if (!isActive) return;
        if (iconRect == null || canvasRect == null) return;
        if (!iconRect.gameObject.activeSelf) return;

        Vector2 local;
        if (!TryGetCursorLocalPoint(out local)) return;

        Vector2 pos = local + iconOffset;

        if (iconClampToScreen)
        {
            Rect r = canvasRect.rect;
            Vector2 pv = iconRect.pivot;

            // Ľavý dolný roh ikonky pri danej anchoredPosition a pivote.
            float left = pos.x - pv.x * iconSize.x;
            float bottom = pos.y - pv.y * iconSize.y;

            left = Mathf.Clamp(left, r.xMin, Mathf.Max(r.xMin, r.xMax - iconSize.x));
            bottom = Mathf.Clamp(bottom, r.yMin, Mathf.Max(r.yMin, r.yMax - iconSize.y));

            pos.x = left + pv.x * iconSize.x;
            pos.y = bottom + pv.y * iconSize.y;
        }

        iconRect.anchoredPosition = pos;
    }

    /// <summary>Zmena textu z iného skriptu za behu: GetComponent&lt;ClickTooltip&gt;().SetText("...");</summary>
    public void SetText(string text)
    {
        tooltipText = text;
        if (label != null) label.text = text;
    }

    /// <summary>Zmena ikonky z iného skriptu za behu (napr. iná ikona pre iný typ pracovníka).</summary>
    public void SetIcon(Sprite sprite)
    {
        iconSprite = sprite;
        if (iconImage != null)
        {
            iconImage.sprite = sprite;
            iconImage.enabled = sprite != null;
        }
        if (iconRect != null && isActive)
            iconRect.gameObject.SetActive(sprite != null);
    }
}
