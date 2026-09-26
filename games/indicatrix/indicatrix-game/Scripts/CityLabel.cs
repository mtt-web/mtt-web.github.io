using UnityEngine;
using TMPro;

/// <summary>
/// CityLabel
/// ─────────────────────────────────────────────────────────────────────────
/// Plávajúci 3D popis (názov) mesta. Vychádza z rovnakej techniky ako label
/// vo <see cref="FactoryConstructionManager"/>:
///   • 3D TextMeshPro text,
///   • čierne pozadie (quad) za textom pre čitateľnosť,
///   • billboard = natočenie do roviny obrazovky (rotácia kamery), korektné aj
///     pri ortografickej izometrickej kamere,
///   • text obojstranný a kreslený navrch (ZTest Always cez TMP materiál).
///
/// Na rozdiel od FactoryConstructionManager-u je tento label TRVALÝ (neodpočítava
/// percentá ani nemizne) – drží sa nad stredom mestského regiónu po celú hru.
///
/// Komponent sa sám stará o billboard v Update; CityManager mu len nastaví text
/// a svetovú pozíciu (vrátane výšky, ktorá je v CityManager-i laditeľná).
/// ─────────────────────────────────────────────────────────────────────────
/// PREKRYTIE VYSOKÝMI BUDOVAMI („navrchu, ale pod UI dialógmi“)
/// ─────────────────────────────────────────────────────────────────────────
/// Samotné nastavenie ZTest=Always na materiáloch (nižšie) nestačí spoľahlivo
/// pre všetky TMP shader varianty/pipeline – pri vysokých budovách (mrakodrapy)
/// sa vedelo stať, že text zmizol, hoci pozadie zostalo viditeľné.
///
/// Preto label beží na VLASTNEJ VRSTVE (layer) "CityLabelOverlay" a je
/// vykresľovaný samostatnou OVERLAY KAMEROU, ktorá:
///   • sníma VÝLUČNE vrstvu CityLabelOverlay,
///   • vykresľuje sa AŽ PO hlavnej kamere (vyššia Camera.depth),
///   • maže si len depth buffer (CameraClearFlags.Depth), takže obraz hlavnej
///     kamery zostáva zachovaný a label sa nad neho jednoducho "domaľuje".
///
/// Výsledok: label je vždy navrchu 3D scény bez ohľadu na budovy (nezávisí to
/// od shaderu/ZTest-u). Bežné UI dialógy (Canvas, Render Mode = Screen Space –
/// Overlay) sa v Unity kreslia úplne posledné, nezávisle od kamier, takže
/// zostávajú automaticky NAD labelom – netreba nič ďalšie riešiť.
///
/// Overlay vrstva aj kamera sú STATICKÉ → zdieľané všetkými používateľmi
/// tohto komponentu naraz (CityManager – labely miest, StationLabelManager –
/// labely staníc, FactoryConstructionManager – labely rozostavaných tovární).
/// Vytvoria sa naozaj len raz, bez ohľadu na to, kto ich prvý vyvolá.
///
/// SETUP V UNITY (jednorazovo, nutné urobiť v editore):
///   Edit → Project Settings → Tags and Layers → pridaj novú vrstvu
///   s presným názvom "CityLabelOverlay" (viď <see cref="OverlayLayerName"/>).
/// Ak vrstva chýba, komponent to zaloguje raz ako varovanie a automaticky
/// spadne späť na pôvodné ZTest-Always správanie (nič nespadne, len sa
/// nevyrieši prekrytie budovami).
///
/// Ak projekt používa pre UI dialógy Canvas v režime Screen Space – Camera
/// alebo World Space (nie Overlay), over si, že kamera priradená tomuto
/// Canvasu má vyššiu Camera.depth ako overlay kamera labelu (tá dostane
/// depth hlavnej kamery + 1).
/// ─────────────────────────────────────────────────────────────────────────
/// FONT (nastaviteľný cez Inspector)
/// ─────────────────────────────────────────────────────────────────────────
/// Font sa vyberá v tomto poradí (prvý nenulový vyhráva):
///   1. font pre konkrétny label – pole „Font Asset“ v Inspectore (ak je
///      CityLabel na prefabe/v scéne), parameter Initialize(..., font) alebo
///      <see cref="SetFont"/>,
///   2. globálny font – komponent <see cref="CityLabelFontProvider"/> v scéne
///      (pole „Font“ v Inspectore), platí pre všetky labely naraz,
///   3. default font z TMP Settings,
///   4. záloha „Fonts &amp; Materials/LiberationSans SDF“ z Resources.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class CityLabel : MonoBehaviour
{
    /// <summary>
    /// Názov Unity vrstvy (Layer), na ktorej beží label + jeho pozadie.
    /// MUSÍ existovať v Edit → Project Settings → Tags and Layers, inak sa
    /// overlay-kamerové riešenie preskočí (fallback na ZTest-Always trik).
    /// </summary>
    private const string OverlayLayerName = "CityLabelOverlay";

    /// <summary>O koľko je Camera.depth overlay kamery vyššia ako hlavná kamera.</summary>
    private const float OverlayDepthOffset = 1f;

    /// <summary>Zdieľaná overlay kamera pre všetky CityLabel inštancie v scéne.</summary>
    private static Camera overlayCamera;
    private static bool overlaySetupAttempted;
    private static int overlayLayer = -1;

    private TextMeshPro label;
    private Transform background;
    private Camera cachedCam;

    private Color textColor = Color.white;
    private Color backgroundColor = Color.black;
    private Color outlineColor = Color.black;
    private Vector2 backgroundPadding = new Vector2(1.5f, 0.5f);

    private const float OutlineWidth = 0.2f;

    [Header("Font")]
    [Tooltip("Font pre tento konkrétny label. Ak je prázdny, použije sa globálny font " +
             "z CityLabelFontProvider v scéne, inak default font z TMP Settings.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    /// <summary>
    /// Globálny font pre všetky CityLabel inštancie (mestá, stanice, továrne...).
    /// Nastavuje ho <see cref="CityLabelFontProvider"/> z Inspectora, dá sa však
    /// nastaviť aj z kódu. Použije sa, ak label nemá vlastný <c>fontAsset</c>.
    /// </summary>
    public static TMP_FontAsset GlobalFont { get; set; }

    private static bool warnedNoFont;
    private static bool warnedNoLayer;

    /// <summary>
    /// Inicializuje label: vytvorí text + pozadie, nastaví obsah, pozíciu a vzhľad.
    /// </summary>
    public void Initialize(string cityName, Vector3 worldPosition, float fontSize,
                           float labelScale, Color textCol, Color bgCol, Color outlineCol,
                           Vector2 padding, TMP_FontAsset font = null)
    {
        if (font != null) fontAsset = font;

        textColor = textCol;
        backgroundColor = bgCol;
        outlineColor = outlineCol;
        backgroundPadding = padding;

        transform.position = worldPosition;
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, labelScale);

        EnsureOverlaySetup();
        ApplyOverlayLayer(gameObject);

        CreateText(cityName, fontSize);
        CreateBackground();
        ResizeBackground();
    }

    /// <summary>
    /// Verejný prístup pre iné label managery (napr. SalePriceLabelManager), aby
    /// mohli svoje labely vykresľovať na tej istej overlay vrstve/kamere ako
    /// CityLabel – teda garantovane NAD stanicami/mestami, nezávisle od poradia
    /// v rámci hlavnej kamery. Zabezpečí (ak ešte neprebehlo) vytvorenie overlay
    /// vrstvy a kamery a vráti index vrstvy, alebo -1, ak vrstva "CityLabelOverlay"
    /// v projekte chýba.
    /// </summary>
    public static int EnsureOverlayLayer()
    {
        EnsureOverlaySetup();
        return overlayLayer;
    }

    /// <summary>Zmení zobrazený názov mesta (a prispôsobí pozadie).</summary>
    public void SetText(string cityName)
    {
        if (label == null) return;
        label.text = cityName;
        ResizeBackground();
    }

    /// <summary>
    /// Zmení font labelu aj po inicializácii. <c>null</c> = vráť sa ku globálnemu
    /// fontu (<see cref="GlobalFont"/>) / TMP default fontu.
    /// </summary>
    public void SetFont(TMP_FontAsset font)
    {
        fontAsset = font;
        if (label == null) return; // použije sa pri Initialize

        TMP_FontAsset resolved = ResolveFont();
        if (resolved == null || label.font == resolved) return;

        label.font = resolved;
        ApplyFontDependentStyle(label); // nový font = nový materiál → znovu outline + ZTest
        UpdateBackgroundRenderQueue();
        ResizeBackground();
    }

    /// <summary>Nastaví svetovú pozíciu labelu (napr. pri zmene výšky titulku).</summary>
    public void SetPosition(Vector3 worldPosition)
    {
        transform.position = worldPosition;
    }

    /// <summary>Aktuálne rozmery textu (lokálny priestor) – len pre diagnostiku/logging.</summary>
    public Vector3 GetTextSize()
    {
        if (label == null) return Vector3.zero;
        label.ForceMeshUpdate();
        return label.textBounds.size;
    }

    void Update()
    {
        Camera cam = ResolveCamera();
        if (cam != null)
        {
            // Billboard pre ORTHO kameru: label dostane presne rotáciu kamery,
            // takže je rovnobežný s rovinou obrazovky a čitateľný (nezrkadlený).
            transform.rotation = cam.transform.rotation;
        }
    }

    // =====================================================================
    // OVERLAY KAMERA – zaručené vykreslenie navrchu (nezávisle od ZTest/shaderu)
    // =====================================================================

    /// <summary>
    /// Jednorazovo (statické, zdieľané pre všetky mestá) vytvorí overlay kameru,
    /// ktorá sníma len vrstvu <see cref="OverlayLayerName"/> a kreslí sa po
    /// hlavnej kamere s vymazaním iba depth bufferu. Zároveň z hlavnej kamery
    /// túto vrstvu vylúči (aby sa nekreslila dvakrát – raz normálne prekrytá
    /// budovou, raz navrchu).
    /// </summary>
    private static void EnsureOverlaySetup()
    {
        // Ak už bolo raz nastavené: ak layer chýba trvalo, netreba nič skúšať.
        // Ak layer existuje, ale overlay kamera bola medzičasom zničená (napr.
        // scénový reload MainMenu → IndicatrixScene pri Load Game), treba ju
        // vytvoriť znova – inak zostane vrstva CityLabelOverlay bez kamery,
        // ktorá by ju vykresľovala (presne to spôsobuje "background je, text nie").
        if (overlaySetupAttempted)
        {
            if (overlayLayer < 0) return;
            if (overlayCamera != null) return;
            // inak pokračuj a znovu vytvor kameru
        }
        overlaySetupAttempted = true;

        overlayLayer = LayerMask.NameToLayer(OverlayLayerName);
        if (overlayLayer < 0)
        {
            if (!warnedNoLayer)
            {
                warnedNoLayer = true;
                Debug.LogWarning($"[CityLabel] Chýba Unity vrstva \"{OverlayLayerName}\" ...");
            }
            return;
        }

        Camera main = ResolvePrimaryCameraStatic();
        if (main == null)
        {
            Debug.LogWarning("[CityLabel] Nenašla sa žiadna kamera v scéne – overlay kamera sa nevytvorila.");
            return;
        }

        main.cullingMask &= ~(1 << overlayLayer);

        var camGO = new GameObject("CityLabelOverlayCamera");
        overlayCamera = camGO.AddComponent<Camera>();
        overlayCamera.clearFlags = CameraClearFlags.Depth;
        overlayCamera.cullingMask = 1 << overlayLayer;
        overlayCamera.depth = main.depth + OverlayDepthOffset;
        overlayCamera.orthographic = main.orthographic;
        overlayCamera.orthographicSize = main.orthographicSize;
        overlayCamera.fieldOfView = main.fieldOfView;
        overlayCamera.nearClipPlane = main.nearClipPlane;
        overlayCamera.farClipPlane = main.farClipPlane;
        overlayCamera.allowHDR = false;
        overlayCamera.allowMSAA = false;

        camGO.AddComponent<CityLabelOverlayCameraFollow>().Setup(main, overlayCamera);
    }

    /// <summary>Priradí objekt na overlay vrstvu, ak je nastavená.</summary>
    public static void ApplyOverlayLayer(GameObject go)
    {
        if (overlayLayer < 0) return;
        go.layer = overlayLayer;
    }

    private static Camera ResolvePrimaryCameraStatic()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        return cam;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;
        cachedCam = Camera.main;
        if (cachedCam == null) cachedCam = FindFirstObjectByType<Camera>();
        return cachedCam;
    }

    private void CreateText(string cityName, float fontSize)
    {
        label = gameObject.AddComponent<TextMeshPro>();

        // Font z Inspectora (vlastný alebo globálny) má prednosť pred TMP defaultom,
        // ktorý TextMeshPro priradí automaticky pri AddComponent.
        TMP_FontAsset resolved = ResolveFont();
        if (resolved != null) label.font = resolved;

        if (label.font == null)
        {
            var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (fallback != null) label.font = fallback;
            else if (!warnedNoFont)
            {
                warnedNoFont = true;
                Debug.LogWarning("[CityLabel] TextMeshPro nemá default font asset. Importuj TMP Essentials.");
            }
        }

        label.text = cityName;
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.rectTransform.sizeDelta = new Vector2(40f, 8f);

        ApplyFontDependentStyle(label);
    }

    /// <summary>
    /// Vráti font podľa priority: vlastný (Inspector/Initialize/SetFont) →
    /// globálny (CityLabelFontProvider). <c>null</c> = nechaj TMP default.
    /// </summary>
    private TMP_FontAsset ResolveFont()
    {
        if (fontAsset != null) return fontAsset;

        // Poistka pre poradie inicializácie: ak label vzniká skôr, než provider
        // stihol v Awake nastaviť GlobalFont, nájdeme ho sami.
        if (GlobalFont == null)
        {
            var provider = FindAnyObjectByType<CityLabelFontProvider>();
            if (provider != null) provider.Register();
        }
        return GlobalFont;
    }

    /// <summary>
    /// Vlastnosti uložené v materiáli fontu (outline, ZTest, cull). Po zmene
    /// fontu vzniká nová inštancia materiálu, preto sa musia nastaviť znova.
    /// </summary>
    private void ApplyFontDependentStyle(TextMeshPro tmp)
    {
        tmp.outlineColor = outlineColor;
        tmp.outlineWidth = OutlineWidth;
        ApplyRobustMaterial(tmp);
    }

    private void UpdateBackgroundRenderQueue()
    {
        if (background == null || label == null) return;
        var mr = background.GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterial == null) return;

        int textQueue = (label.fontMaterial != null) ? label.fontMaterial.renderQueue : 3000;
        if (textQueue < 0) textQueue = 3000;
        mr.sharedMaterial.renderQueue = textQueue - 1;
    }

    private void ApplyRobustMaterial(TextMeshPro tmp)
    {
        // Záložné opatrenie popri overlay kamere (užitočné aj vtedy, ak by
        // vrstva "CityLabelOverlay" v projekte chýbala). Nemá negatívny vplyv,
        // ak overlay kamera už problém vyriešila.
        try
        {
            Material m = tmp.fontMaterial;
            if (m == null) return;
            if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 0f);   // obojstranne
            if (m.HasProperty("_ZTestMode")) m.SetFloat("_ZTestMode", 8f); // vždy navrch
        }
        catch { /* shader nemusí mať tieto vlastnosti – nevadí */ }
    }

    private void CreateBackground()
    {
        if (label == null) return;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "CityLabelBG";

        var col = quad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreateBackgroundMaterial(label);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        quad.transform.SetParent(transform, false);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localPosition = Vector3.zero;

        ApplyOverlayLayer(quad);

        background = quad.transform;
    }

    private Material CreateBackgroundMaterial(TextMeshPro tmp)
    {
        Shader sh = Shader.Find("Hidden/Internal-Colored");
        var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

        m.SetColor("_Color", backgroundColor);
        m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.SetInt("_ZWrite", 0);
        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);

        int textQueue = (tmp.fontMaterial != null) ? tmp.fontMaterial.renderQueue : 3000;
        if (textQueue < 0) textQueue = 3000;
        m.renderQueue = textQueue - 1; // box tesne PRED textom → text ostane navrchu
        return m;
    }

    private void ResizeBackground()
    {
        if (background == null || label == null) return;

        label.ForceMeshUpdate();
        Bounds b = label.textBounds;

        float w = b.size.x + backgroundPadding.x;
        float h = b.size.y + backgroundPadding.y;
        if (w <= 0f) w = backgroundPadding.x;
        if (h <= 0f) h = backgroundPadding.y;

        background.localScale = new Vector3(w, h, 1f);
        background.localPosition = new Vector3(b.center.x, b.center.y, 0f);
    }
}

/// <summary>
/// Malý pomocný komponent na overlay kamere: každý frame preberie z hlavnej
/// kamery transform a objektívové nastavenia (pozícia, rotácia, ortho size /
/// FOV, near/far), aby overlay kamera presne kopírovala pohľad hlavnej
/// (izometrickej) kamery, aj keď sa hýbe/zooming/paning.
/// </summary>
[DisallowMultipleComponent]
public class CityLabelOverlayCameraFollow : MonoBehaviour
{
    private Camera source;
    private Camera target;

    public void Setup(Camera sourceCam, Camera targetCam)
    {
        source = sourceCam;
        target = targetCam;
    }

    void LateUpdate()
    {
        if (source == null || target == null) return;

        transform.position = source.transform.position;
        transform.rotation = source.transform.rotation;

        target.orthographic = source.orthographic;
        target.orthographicSize = source.orthographicSize;
        target.fieldOfView = source.fieldOfView;
        target.nearClipPlane = source.nearClipPlane;
        target.farClipPlane = source.farClipPlane;
        target.depth = source.depth + 1f;
    }
}