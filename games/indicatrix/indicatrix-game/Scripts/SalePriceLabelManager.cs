using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// SalePriceLabelManager
/// ─────────────────────────────────────────────────────────────────────────
/// CESTNÝ/ŽELEZNIČNÝ EKVIVALENT k <see cref="FactoryConstructionManager"/>, ale
/// namiesto progresu výstavby zobrazuje PLÁVAJÚCI 3D LABEL S CENOU PREDAJA nad
/// vlakom alebo vozidlom po úspešnej obchodnej transakcii.
///
/// SPRÁVANIE (podľa zadania):
///   • Vlak/vozidlo dorazí na stanicu a čaká 10 s (STATION_WAIT). Už po 2 s
///     prebehne transakcia (TrainSystem / VehicleSystem volajú TradeSystem a
///     vypíšu Debug.Log). PRESNE v tom momente (po 2 s) sa nad dopravným
///     prostriedkom zobrazí tento label.
///   • Label ukáže cenu v tvare "135 CR" (rovnaké formátovanie ako FCM:
///     biely text na čiernom pozadí, čierny obrys, billboard do roviny kamery).
///   • Po 3 s (displayDuration) label zmizne a odstráni sa.
///
/// VÝTVARNE je label identický s FactoryConstructionManager – zámerne sa
/// kopíruje ten istý vzor (čierne pozadie = samostatný quad za textom, lebo
/// 3D TextMeshPro "background color" nemá), aby boli všetky herné labely
/// konzistentné. Drží sa parametricky oddelene (vlastné Inspector polia), aby
/// sa dali cenové labely ladiť nezávisle od labelu výstavby.
///
/// POUŽITIE (z TrainSystem / VehicleSystem po úspešnom predaji):
///     SalePriceLabelManager.Instance.ShowPrice(
///         td.locomotive.transform.position,   // kotva = pozícia vlaku/vozidla
///         result.Revenue);                    // suma v CR
/// Výšku nad dopravným prostriedkom (labelHeight) si manager pridá sám.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SalePriceLabelManager : MonoBehaviour
{
    // =====================================================================
    // SINGLETON (rovnaký vzor ako FactoryConstructionManager)
    // =====================================================================

    private static SalePriceLabelManager _instance;

    public static SalePriceLabelManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SalePriceLabelManager>();
                if (_instance == null)
                {
                    var go = new GameObject("SalePriceLabelManager");
                    _instance = go.AddComponent<SalePriceLabelManager>();
                }
            }
            return _instance;
        }
    }

    // =====================================================================
    // NASTAVENIA (laditeľné v Inspectore)
    // =====================================================================

    [Header("Label – pozícia a vzhľad")]
    [Tooltip("Výška labelu nad vlakom/vozidlom (svetové jednotky). Analógia k " +
             "labelHeight z FactoryConstructionManager.")]
    [SerializeField] private float labelHeight = 1.0f;

    [Tooltip("Veľkosť písma 3D TextMeshPro labelu.")]
    [SerializeField] private float fontSize = 3.0f;

    [Tooltip("Uniformná mierka celého labelu (jemné doladenie veľkosti).")]
    [SerializeField] private float labelScale = 1.0f;

    [Tooltip("Farba textu.")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("Farba POZADIA labelu (čierny box za textom).")]
    [SerializeField] private Color backgroundColor = Color.black;

    [Tooltip("Farba obrysu textu (kontrast).")]
    [SerializeField] private Color outlineColor = Color.black;

    [Tooltip("Okraj pozadia okolo textu (X = vodorovne, Y = zvisle) v jednotkách textu.")]
    [SerializeField] private Vector2 backgroundPadding = new Vector2(1.5f, 0.5f);

    [Header("Časovanie")]
    [Tooltip("Koľko sekúnd je label viditeľný, než zmizne (zadanie: 3 s).")]
    [SerializeField] private float displayDuration = 3f;

    [Tooltip("Suffix meny za sumou (napr. \"135 CR\").")]
    [SerializeField] private string currencySuffix = "CR";

    [Tooltip("Vypisovať diagnostiku do Console.")]
    [SerializeField] private bool verboseLog = false;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    private class ActivePriceLabel
    {
        public GameObject labelGO;
        public TextMeshPro label;
        public Transform background;            // čierny box za textom
        public MeshRenderer backgroundRenderer;
        public float remaining;                 // zostávajúci čas zobrazenia (s)
    }

    private readonly List<ActivePriceLabel> active = new List<ActivePriceLabel>();

    private Camera cachedCam;
    private bool warnedNoCamera;
    private bool warnedNoFont;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Camera cam = ResolveCamera();

        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActivePriceLabel pl = active[i];
            if (pl.labelGO == null) { active.RemoveAt(i); continue; }

            pl.remaining -= dt;
            if (pl.remaining <= 0f) { DestroyEntry(i); continue; }

            // Billboard pre ORTHO kameru: label dostane PRESNE rotáciu kamery
            // (rovnako ako FactoryConstructionManager) – čitateľný, neskosený.
            if (cam != null)
                pl.labelGO.transform.rotation = cam.transform.rotation;
        }
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Zobrazí nad zadanou kotvou (pozícia vlaku/vozidla) cenový label
    /// "{suma} CR" na <see cref="displayDuration"/> sekúnd. Výšku
    /// (<see cref="labelHeight"/>) pridá manager sám.
    /// </summary>
    public void ShowPrice(Vector3 anchorWorldPos, int amountCR)
    {
        var pl = new ActivePriceLabel { remaining = displayDuration };

        pl.labelGO = CreateLabel(out pl.label);
        pl.label.text = $"{amountCR} {currencySuffix}";

        CreateBackground(pl);

        pl.labelGO.transform.position = anchorWorldPos + Vector3.up * labelHeight;


        // Použi rovnakú overlay vrstvu/kameru ako CityLabel (stanice/mestá), aby sa
        // price label garantovane vykreslil NAD nimi – to sa nedá zaistiť len
        // poradím v rámci hlavnej kamery, lebo CityLabel kreslí samostatná overlay
        // kamera AŽ PO hlavnej kamere.
        int overlayLayer = CityLabel.EnsureOverlayLayer();
        if (overlayLayer >= 0)
        {
            CityLabel.ApplyOverlayLayer(pl.labelGO);

            if (pl.background != null)
                CityLabel.ApplyOverlayLayer(pl.background.gameObject);

            // V rámci tej istej overlay kamery zaisti, že price label sa nakreslí
            // NAD station/city labelom aj keby mali rovnaký pôvodný queue.
            BumpRenderQueue(pl);
        }
        else
        {
            // Fallback: vrstva "CityLabelOverlay" v projekte chýba – pôvodné
            // správanie cez hlavnú kameru a ZTest-Always trik.
            Camera cam = ResolveCamera();
            if (cam != null)
            {
                pl.labelGO.layer = ChooseRenderableLayer(cam, pl.labelGO.layer);

                if (pl.background != null)
                    pl.background.gameObject.layer = pl.labelGO.layer;
            }
        }

        active.Add(pl);

        if (verboseLog)
            Debug.Log($"[SalePriceLabel] Zobrazený '{pl.label.text}' na {pl.labelGO.transform.position} " +
                      $"na {displayDuration}s.");
    }

    /// <summary>
    /// O koľko sa posunie renderQueue price labelu nad bežný TMP queue,
    /// aby sa v rámci overlay kamery vykreslil NAD station/city labelom.
    /// </summary>
    private const int OverlayQueueBoost = 100;

    private void BumpRenderQueue(ActivePriceLabel pl)
    {
        if (pl.label != null && pl.label.fontMaterial != null)
            pl.label.fontMaterial.renderQueue += OverlayQueueBoost;

        if (pl.backgroundRenderer != null && pl.backgroundRenderer.sharedMaterial != null)
            pl.backgroundRenderer.sharedMaterial.renderQueue += OverlayQueueBoost;
    }

    /// <summary>Okamžite odstráni všetky zobrazené cenové labely.</summary>
    public void ClearAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            DestroyEntry(i);
    }

    // =====================================================================
    // POMOCNÉ – tvorba labelu (zhodné s FactoryConstructionManager)
    // =====================================================================

    private GameObject CreateLabel(out TextMeshPro tmp)
    {
        var go = new GameObject("SalePriceLabel", typeof(RectTransform));
        go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, labelScale);

        tmp = go.AddComponent<TextMeshPro>();

        if (tmp.font == null)
        {
            var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fallback != null) tmp.font = fallback;
            else if (!warnedNoFont)
            {
                warnedNoFont = true;
                Debug.LogWarning("[SalePriceLabel] TextMeshPro nemá default font asset. Importuj TMP Essentials.");
            }
        }

        tmp.text = "0 " + currencySuffix;
        tmp.fontSize = fontSize;
        tmp.color = new Color(0.25f, 0.85f, 0.4f); //#40D966
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.outlineColor = outlineColor;
        tmp.outlineWidth = 0.2f;
        tmp.rectTransform.sizeDelta = new Vector2(40f, 8f);

        ApplyRobustMaterial(tmp);
        return go;
    }

    private void ApplyRobustMaterial(TextMeshPro tmp)
    {
        try
        {
            Material m = tmp.fontMaterial;
            if (m == null) return;
            if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 0f);  // obojstranne
            if (m.HasProperty("_ZTestMode")) m.SetFloat("_ZTestMode", 8f); // vždy navrch
        }
        catch { /* shader nemusí mať tieto vlastnosti – nevadí */ }
    }

    // =====================================================================
    // POZADIE LABELU (čierny box za bielym textom)
    //
    // Identický prístup ako vo FactoryConstructionManager: 3D TextMeshPro nemá
    // "background color", takže box je samostatný quad za textom. Shader
    // "Hidden/Internal-Colored" (vstavaný) umožní _ZTest/_Cull/_ZWrite, takže
    // box je vždy navrch a obojstranný, presne ako text.
    // =====================================================================

    private void CreateBackground(ActivePriceLabel pl)
    {
        if (pl.labelGO == null || pl.label == null) return;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SalePriceLabelBG";

        var col = quad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreateBackgroundMaterial(pl.label);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        quad.transform.SetParent(pl.labelGO.transform, false);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localPosition = Vector3.zero;

        pl.background = quad.transform;
        pl.backgroundRenderer = mr;

        ResizeBackground(pl);
    }

    private Material CreateBackgroundMaterial(TextMeshPro tmp)
    {
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

        m.SetColor("_Color", backgroundColor);
        m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);  // vždy navrch
        m.SetInt("_ZWrite", 0);
        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);             // obojstranne
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);       // nepriehľadná

        int textQueue = (tmp.fontMaterial != null) ? tmp.fontMaterial.renderQueue : 3000;
        if (textQueue < 0) textQueue = 3000;
        m.renderQueue = textQueue - 1;   // tesne pred textom → text ostane navrchu
        return m;
    }

    private void ResizeBackground(ActivePriceLabel pl)
    {
        if (pl.background == null || pl.label == null) return;

        pl.label.ForceMeshUpdate();
        Bounds b = pl.label.textBounds;            // v lokálnom priestore textu

        float w = b.size.x + backgroundPadding.x;
        float h = b.size.y + backgroundPadding.y;
        if (w <= 0f) w = backgroundPadding.x;
        if (h <= 0f) h = backgroundPadding.y;

        pl.background.localScale = new Vector3(w, h, 1f);
        pl.background.localPosition = new Vector3(b.center.x, b.center.y, 0f);
    }

    // =====================================================================
    // POMOCNÉ – kamera / vrstva / likvidácia (zhodné s FCM)
    // =====================================================================

    private Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;

        cachedCam = Camera.main;
        if (cachedCam == null) cachedCam = FindFirstObjectByType<Camera>();

        if (cachedCam == null && !warnedNoCamera)
        {
            warnedNoCamera = true;
            Debug.LogWarning("[SalePriceLabel] Nenašla sa žiadna kamera – billboard nebude fungovať.");
        }
        return cachedCam;
    }

    /// <summary>Vráti vrstvu, ktorú daná kamera renderuje (preferuje zadanú).</summary>
    private int ChooseRenderableLayer(Camera cam, int preferred)
    {
        if ((cam.cullingMask & (1 << preferred)) != 0) return preferred;
        for (int l = 0; l < 32; l++)
            if ((cam.cullingMask & (1 << l)) != 0) return l;
        return preferred;
    }

    private void DestroyEntry(int index)
    {
        ActivePriceLabel pl = active[index];
        if (pl.labelGO != null) Destroy(pl.labelGO);   // zničí aj dieťa (pozadie)
        active.RemoveAt(index);
    }
}
