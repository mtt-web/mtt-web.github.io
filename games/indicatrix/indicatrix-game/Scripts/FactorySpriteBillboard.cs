using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FactorySpriteSettings
/// ─────────────────────────────────────────────────────────────────────────
/// Nastavenia vykreslenia spritu pre JEDNU KONKRÉTNU továreň. V Inspectore
/// sedí v TileModelLibrary hneď pri slote s obrázkom danej továrne, takže
/// každá zo 16 tovární sa ladí samostatne (Coal Mine môže mať iný posun ako
/// Power Station).
///
/// Veci, ktoré sú pre všetky továrne spoločné (sorting layer, render queue,
/// diagnostika) sú v <see cref="FactorySpriteGlobalSettings"/> na komponente
/// IndicatrixAPI.
///
/// DÔLEŽITÉ – SAMOOPRAVA HODNÔT:
///   Keď Unity pridá do už existujúceho komponentu NOVÉ serializované pole,
///   naplní ho NULAMI namiesto hodnôt z konštruktora. Nula vo widthMultiplier
///   znamená sprite so šírkou 0 = NEVIDITEĽNÁ továreň (presne tento prípad
///   nastal pri prvom nasadení). Preto <see cref="Normalize"/> nezmyselné
///   hodnoty opraví a nahlási; navyše to isté robí OnValidate v
///   TileModelLibrary, takže sa to opraví hneď pri pohľade do Inspectora.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[System.Serializable]
public class FactorySpriteSettings
{
    [Tooltip("Násobok šírky spritu. 1 = sprite je presne taký široký, ako je " +
             "na obrazovke široký izometrický kosoštvorec footprintu tejto " +
             "továrne. Zväčši/zmenši, ak obrázok presahuje alebo nedosahuje. " +
             "NULA = neviditeľná továreň (automaticky sa opraví na 1).")]
    public float widthMultiplier = 1f;

    [Tooltip("Zvislý posun spritu v TILE jednotkách (+ = hore, − = dole). " +
             "Toto je hlavná páka, keď továreň \"lieta\" nad terénom alebo je " +
             "naopak zapichnutá v zemi. Typicky stačí −0,2 až +0,2.")]
    public float verticalOffset = 0f;

    [Tooltip("Kam sa v hĺbke (Z-buffer) položí rovina spritu:\n" +
             "  Near   – na predný roh footprintu (ODPORÚČANÉ).\n" +
             "  Center – do stredu footprintu.\n" +
             "  Far    – na zadný roh: pred sprite sa kreslí naozaj všetko, ale " +
             "sprite prekryje aj vlastný terén footprintu (obrázok sa zareže).")]
    public FactorySpriteBillboard.DepthAnchor depthAnchor
        = FactorySpriteBillboard.DepthAnchor.Near;

    [Tooltip("Dodatočný posun roviny spritu v hĺbke (svetové jednotky). " +
             "+ = ďalej od kamery (viac vecí sa kreslí pred továreň), " +
             "− = bližšie ku kamere.")]
    public float depthBias = -0.05f;

    [Tooltip("Doladenie poradia kreslenia oproti ostatným továrňam. Pripočíta " +
             "sa k spoločnému sortingOrder z IndicatrixAPI. Bežne nechaj 0.")]
    public int sortingOrderOffset = 0;

    /// <summary>
    /// Opraví nezmyselné hodnoty (typicky nuly po pridaní nového poľa do už
    /// existujúceho komponentu v scéne). Vráti popis opráv, alebo prázdny
    /// string, ak bolo všetko v poriadku.
    /// </summary>
    public string Normalize()
    {
        string fixes = "";

        if (widthMultiplier <= 0.0001f)
        {
            fixes += $" widthMultiplier {widthMultiplier}→1;";
            widthMultiplier = 1f;
        }

        return fixes;
    }

    /// <summary>Neutrálne nastavenia – fallback, keď sa pre typ nič nenájde.</summary>
    public static readonly FactorySpriteSettings Default = new FactorySpriteSettings();
}

/// <summary>
/// FactorySpriteGlobalSettings
/// ─────────────────────────────────────────────────────────────────────────
/// Nastavenia SPOLOČNÉ pre sprity všetkých tovární – vrstva, render queue,
/// diagnostika. Sedí v Inspectore na komponente IndicatrixAPI.
///
/// Per-továrňové veci (mierka, zvislý posun, hĺbka) sú v TileModelLibrary
/// v bloku danej továrne – viď <see cref="FactorySpriteSettings"/>.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[System.Serializable]
public class FactorySpriteGlobalSettings
{
    [Tooltip("Základný Sorting Order spritov tovární. Zámerne veľmi nízke " +
             "číslo – továreň je \"najspodnejšia\" vrstva, takže ostatné " +
             "sprity a objekty sa kreslia pred ňu. K tomuto sa pripočíta " +
             "sortingOrderOffset danej továrne.")]
    public int sortingOrder = -1000;

    [Tooltip("Sorting Layer spritov tovární. Prázdne = \"Default\".")]
    public string sortingLayerName = "Default";

    [Tooltip("Render queue materiálu. 3000 = štandardná priehľadná geometria " +
             "(najbezpečnejšie). 2450 = tesne za nepriehľadnou geometriou. " +
             "NULA je neplatná – automaticky sa opraví na 3000.")]
    public int renderQueue = 3000;

    [Tooltip("ZAPNUTÉ (odporúčané) = sprite sa zarovná podľa SKUTOČNE " +
             "nakresleného obsahu obrázka, nie podľa celého (často priehľadným " +
             "okrajom nafúknutého) rámu. Vyžaduje v importe obrázka " +
             "Mesh Type = Tight. Práve priehľadný okraj je najčastejší dôvod, " +
             "prečo niektorá továreň \"lieta\" vyššie nad terénom než iné.")]
    public bool useTightBounds = true;

    [Tooltip("DIAGNOSTIKA: vypíše do Console podrobnosti o každom vytvorenom " +
             "sprite (pozícia, mierka, viditeľnosť, materiál).")]
    public bool verboseLog = false;

    [Tooltip("DIAGNOSTIKA: sprite sa kreslí ÚPLNE NAVRCH, bez testu hĺbky. " +
             "Ak sa pri zapnutí továreň ZOBRAZÍ, problém je v hĺbke/Z-poradí. " +
             "Ak sa NEZOBRAZÍ ani tak, problém je v pozícii, mierke alebo v " +
             "tom, že sprite nie je priradený. V hre nechaj VYPNUTÉ.")]
    public bool debugIgnoreDepth = false;

    [Tooltip("Zapnuté = pod spritom sa PONECHAJÚ pôvodné textúry footprintu " +
             "(Factory_Tex / Processing_Tex). Užitočné pri ladení – hneď vidno, " +
             "či footprint vôbec vznikol a kde presne leží.")]
    public bool keepFootprintTextures = false;

    /// <summary>Opraví nezmyselné hodnoty (nuly po pridaní nového poľa).</summary>
    public string Normalize()
    {
        string fixes = "";

        if (renderQueue <= 0)
        {
            fixes += $" renderQueue {renderQueue}→3000;";
            renderQueue = 3000;
        }

        if (string.IsNullOrEmpty(sortingLayerName))
            sortingLayerName = "Default";

        return fixes;
    }
}

/// <summary>
/// FactorySpriteBillboard
/// ─────────────────────────────────────────────────────────────────────────
/// Vykreslí JEDNU továreň ako JEDEN 2D sprite (izometrický obrázok, napr.
/// "CoalMineUI.png") namiesto 3D modelu. Komponent vytvára a spravuje
/// IndicatrixAPI pri registrácii továrne (FactoryRegistry.Register →
/// IndicatrixAPI.OnFactoryRegistered) a ničí ho pri demolácii / Clear.
///
/// ČO KOMPONENT RIEŠI
/// ──────────────────
/// 1. BILLBOARD – sprite dostane presne rotáciu kamery, takže je vždy
///    rovnobežný s rovinou obrazovky (rovnaký princíp ako CityLabel).
///
/// 2. MIERKA PODĽA FOOTPRINTU – šírka spritu sa nastaví tak, aby zodpovedala
///    šírke izometrického kosoštvorca footprintu na obrazovke. Nepočíta sa to
///    z natvrdo zapísaného uhla 30/45, ale priemetom 4 rohov footprintu do
///    smeru "doprava" kamery, takže to platí pre ľubovoľný uhol kamery.
///    Škáluje sa UNIFORMNE → obrázok sa nikdy nedeformuje.
///
/// 3. POZÍCIA – vodorovne stred footprintu, zvisle SPODNÁ HRANA NAKRESLENÉHO
///    OBSAHU na prednom (na obrazovke najnižšom) rohu footprintu.
///
///    Kľúčové slovo je "nakresleného obsahu": obrázky mávajú okolo seba
///    priehľadný okraj a KAŽDÝ inak veľký. Ak by sa počítalo s celým rámom
///    obrázka, továreň s hrubším spodným okrajom by visela vyššie nad terénom
///    než ostatné – presne to bol pozorovaný problém. Preto sa pri zapnutom
///    useTightBounds berú rozmery zo skutočnej geometrie spritu
///    (Sprite.vertices, t.j. Mesh Type = Tight), ktorá priehľadný okraj
///    neobsahuje. Zvyšok doladí verticalOffset danej továrne.
///
/// 4. Z-BUFFER – materiál má ZWrite OFF (sprite nikdy nič nezakryje v hĺbke),
///    sortingOrder je veľmi nízky a rovina spritu sa v hĺbke položí podľa
///    <see cref="DepthAnchor"/>.
///
/// ČO KOMPONENT ZÁMERNE NEROBÍ
/// ───────────────────────────
///   • Nepridáva Collider – klik na továreň funguje ako doteraz cez raycast
///     na terén + FactoryRegistry.GetFactoryAt.
///   • Nerieši label výstavby – ten ostáva v réžii FactoryConstructionManager.
///   • Nerieši rotáciu – tá je od prechodu na 2D sprity zrušená (obrázok je
///     nakreslený z jednej strany), viď GameManager.CurrentFactoryRotation.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class FactorySpriteBillboard : MonoBehaviour
{
    /// <summary>Kam sa v hĺbke položí rovina spritu (viď dokumentácia hore).</summary>
    public enum DepthAnchor
    {
        /// <summary>Na predný (ku kamere najbližší) roh footprintu – ODPORÚČANÉ.</summary>
        Near = 0,
        /// <summary>Do stredu footprintu.</summary>
        Center = 1,
        /// <summary>Na zadný (od kamery najvzdialenejší) roh footprintu.</summary>
        Far = 2
    }

    // =====================================================================
    // STAV
    // =====================================================================

    private SpriteRenderer spriteRenderer;
    private Sprite sprite;

    /// <summary>4 rohy footprintu vo svete (vrátane výšky terénu).</summary>
    private readonly Vector3[] corners = new Vector3[4];

    /// <summary>Stred footprintu vo svete (X/Z stred, Y = priemer terénu).</summary>
    private Vector3 footprintCenter;

    private FactorySpriteSettings settings;          // per-továreň
    private FactorySpriteGlobalSettings global;      // spoločné

    // Rozmery SKUTOČNE nakresleného obsahu spritu (v jednotkách pri scale 1)
    // a posun jeho stredu voči pivotu. Počíta sa raz v Setup.
    private Vector2 contentSize;
    private Vector2 contentCenter;

    private Camera cachedCam;
    private Quaternion lastCamRotation;
    private bool applied;
    private bool warnedNoCamera;

    // Diagnostika: isVisible má zmysel až po prvom vykreslení, preto sa výpis
    // odloží o pár snímkov.
    private int diagnosticsCountdown = -1;

    // Zdieľané materiály – aby 100 tovární nevytvorilo 100 kópií materiálu.
    private static readonly Dictionary<int, Material> sharedMaterials
        = new Dictionary<int, Material>();

    // =====================================================================
    // VEREJNÉ API (volá IndicatrixAPI)
    // =====================================================================

    /// <summary>
    /// Nastaví sprite a geometriu footprintu. Volá sa raz, hneď po vytvorení
    /// GameObjectu v IndicatrixAPI.
    /// </summary>
    /// <param name="sprite">2D obrázok továrne z TileModelLibrary.</param>
    /// <param name="footprintCorners">
    /// 4 rohy footprintu vo svetových súradniciach (vrátane Y z terénu).
    /// Poradie nie je kritické – kód berie min/max.
    /// </param>
    /// <param name="footprintCenterWorld">Stred footprintu vo svete.</param>
    /// <param name="globalSettings">Spoločné nastavenia (z IndicatrixAPI).</param>
    /// <param name="factorySettings">Nastavenia TEJTO továrne (z TileModelLibrary).</param>
    public void Setup(Sprite sprite,
                      Vector3[] footprintCorners,
                      Vector3 footprintCenterWorld,
                      FactorySpriteGlobalSettings globalSettings,
                      FactorySpriteSettings factorySettings)
    {
        this.sprite = sprite;
        this.global = globalSettings ?? new FactorySpriteGlobalSettings();
        this.settings = factorySettings ?? new FactorySpriteSettings();
        this.footprintCenter = footprintCenterWorld;

        // SAMOOPRAVA: nuly z Inspectora by znamenali sprite so šírkou 0.
        string fixes = this.global.Normalize() + this.settings.Normalize();
        if (!string.IsNullOrEmpty(fixes))
            Debug.LogWarning($"[FactorySprite] Nastavenia mali neplatné hodnoty a " +
                             $"boli opravené:{fixes} Oprav ich aj v Inspectore " +
                             $"(TileModelLibrary / IndicatrixAPI), nech to platí natrvalo.");

        if (sprite == null)
        {
            Debug.LogError("[FactorySprite] Setup dostal sprite == null – továreň sa " +
                           "nevykreslí. Skontroluj priradenie v TileModelLibrary.");
            return;
        }

        if (footprintCorners != null)
            for (int i = 0; i < 4 && i < footprintCorners.Length; i++)
                corners[i] = footprintCorners[i];

        ComputeContentBounds();

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        spriteRenderer.sprite = sprite;
        spriteRenderer.enabled = true;
        spriteRenderer.color = Color.white;              // istota: plná alfa
        spriteRenderer.sortingOrder = this.global.sortingOrder + this.settings.sortingOrderOffset;
        spriteRenderer.sortingLayerName = this.global.sortingLayerName;

        // Plochý obrázok – tiene nemá zmysel počítať.
        spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        Material mat = ResolveMaterial(this.global.renderQueue, this.global.debugIgnoreDepth);
        if (mat != null) spriteRenderer.sharedMaterial = mat;

        applied = false;
        Apply();

        if (this.global.verboseLog)
            diagnosticsCountdown = 2;   // vypíše sa po prvom vykreslení
    }

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void LateUpdate()
    {
        Camera cam = ResolveCamera();
        if (cam == null) return;

        // Kamera hry má pevný uhol (rotáciou nehýbe ani zoom, ani posun), takže
        // prepočet spustíme len pri skutočnej zmene rotácie.
        if (!applied || cam.transform.rotation != lastCamRotation)
            Apply();

        if (diagnosticsCountdown > 0)
        {
            diagnosticsCountdown--;
            if (diagnosticsCountdown == 0) LogDiagnostics();
        }
    }

    // =====================================================================
    // ROZMERY NAKRESLENÉHO OBSAHU
    // =====================================================================

    /// <summary>
    /// Zistí rozmer a stred SKUTOČNE nakresleného obsahu spritu (v jednotkách
    /// pri localScale = 1), relatívne k pivotu.
    ///
    ///   • useTightBounds = true → z geometrie spritu (Sprite.vertices). Pri
    ///     importe Mesh Type = Tight to je obrys bez priehľadného okraja, takže
    ///     sa všetkých 16 tovární usadí na terén rovnako, bez ručného ladenia.
    ///     Pri Mesh Type = Full Rect vyjde to isté ako celý rám (žiadna škoda).
    ///   • useTightBounds = false → celý rám obrázka (Sprite.bounds).
    /// </summary>
    private void ComputeContentBounds()
    {
        contentSize = sprite.bounds.size;
        contentCenter = sprite.bounds.center;

        if (global == null || !global.useTightBounds) return;

        Vector2[] verts = sprite.vertices;
        if (verts == null || verts.Length < 3) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].x < minX) minX = verts[i].x;
            if (verts[i].x > maxX) maxX = verts[i].x;
            if (verts[i].y < minY) minY = verts[i].y;
            if (verts[i].y > maxY) maxY = verts[i].y;
        }

        float w = maxX - minX;
        float h = maxY - minY;
        if (w <= 1e-5f || h <= 1e-5f) return;   // niečo je zle → nechaj rám

        contentSize = new Vector2(w, h);
        contentCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    // =====================================================================
    // JADRO – umiestnenie a mierka spritu
    // =====================================================================

    private void Apply()
    {
        Camera cam = ResolveCamera();
        if (cam == null || sprite == null || spriteRenderer == null) return;

        Transform camT = cam.transform;
        Vector3 camRight = camT.right;     // "doprava" na obrazovke
        Vector3 camUp = camT.up;        // "hore" na obrazovke
        Vector3 camFwd = camT.forward;   // "do hĺbky" (+ = ďalej od kamery)

        // ── 1) BILLBOARD ─────────────────────────────────────────────────
        transform.rotation = camT.rotation;

        // ── 2) PRIEMET FOOTPRINTU DO ROVINY OBRAZOVKY ────────────────────
        float minRight = float.MaxValue, maxRight = float.MinValue;
        float minUp = float.MaxValue;
        float minFwd = float.MaxValue, maxFwd = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            float r = Vector3.Dot(corners[i], camRight);
            float u = Vector3.Dot(corners[i], camUp);
            float f = Vector3.Dot(corners[i], camFwd);

            if (r < minRight) minRight = r;
            if (r > maxRight) maxRight = r;
            if (u < minUp) minUp = u;      // najnižší roh na obrazovke = "predný"
            if (f < minFwd) minFwd = f;
            if (f > maxFwd) maxFwd = f;
        }

        float footprintScreenWidth = maxRight - minRight;

        if (footprintScreenWidth < 0.0001f)
        {
            Debug.LogError("[FactorySprite] Šírka footprintu vyšla 0 – rohy footprintu " +
                           "neboli odovzdané správne. Sprite by bol neviditeľný.");
            return;
        }

        // ── 3) MIERKA ────────────────────────────────────────────────────
        // Škáluje sa tak, aby ŠÍRKA NAKRESLENÉHO OBSAHU sedela na šírku
        // kosoštvorca footprintu. Uniformne → obrázok sa nedeformuje.
        float targetWidth = footprintScreenWidth * settings.widthMultiplier;
        float scale = (contentSize.x > 1e-5f) ? (targetWidth / contentSize.x) : 1f;
        transform.localScale = new Vector3(scale, scale, scale);

        float halfContentHeight = contentSize.y * 0.5f * scale;

        // ── 4) POZÍCIA ───────────────────────────────────────────────────
        // Vodorovne (camRight): stred footprintu – dosiahne sa tým, že
        //   vychádzame z footprintCenter a pripočítavame LEN zložky v smeroch
        //   camUp a camFwd (tie sú na camRight kolmé).
        // Zvisle (camUp): spodná hrana OBSAHU = najnižší roh footprintu.
        // Hĺbka (camFwd): podľa DepthAnchor + depthBias.
        float targetUp = minUp + halfContentHeight + settings.verticalOffset;

        float targetFwd;
        switch (settings.depthAnchor)
        {
            case DepthAnchor.Far: targetFwd = maxFwd; break;
            case DepthAnchor.Center: targetFwd = (minFwd + maxFwd) * 0.5f; break;
            default: targetFwd = minFwd; break;   // Near
        }
        targetFwd += settings.depthBias;

        // Cieľová pozícia STREDU NAKRESLENÉHO OBSAHU.
        Vector3 contentWorldCenter = footprintCenter
            + camUp * (targetUp - Vector3.Dot(footprintCenter, camUp))
            + camFwd * (targetFwd - Vector3.Dot(footprintCenter, camFwd));

        // Transform je na PIVOTE spritu, nie na strede obsahu – rozdiel
        // odčítame. Vďaka tomu funguje ľubovoľný pivot aj ľubovoľný
        // priehľadný okraj obrázka.
        transform.position = contentWorldCenter
                           - transform.rotation * ((Vector3)contentCenter * scale);

        lastCamRotation = camT.rotation;
        applied = true;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;

        cachedCam = Camera.main;
        if (cachedCam == null) cachedCam = FindFirstObjectByType<Camera>();

        if (cachedCam == null && !warnedNoCamera)
        {
            warnedNoCamera = true;
            Debug.LogWarning("[FactorySprite] Nenašla sa žiadna kamera – sprite " +
                             "továrne sa nedá natočiť ani umiestniť.");
        }
        return cachedCam;
    }

    /// <summary>
    /// Zdieľaný materiál pre sprity tovární.
    ///
    /// NORMÁLNY REŽIM – shader "Sprites/Default": ZWrite OFF (sprite nikdy nič
    /// nezakryje v hĺbke) a ZTest LEqual (bližšia nepriehľadná geometria ho
    /// korektne zakryje).
    ///
    /// DIAGNOSTICKÝ REŽIM (debugIgnoreDepth) – shader "UI/Default" s
    /// unity_GUIZTestMode = Always: sprite sa kreslí úplne navrch bez ohľadu na
    /// hĺbku. Slúži VÝLUČNE na overenie, či je problém v hĺbke alebo v pozícii.
    /// </summary>
    private static Material ResolveMaterial(int renderQueue, bool ignoreDepth)
    {
        int key = ignoreDepth ? -1 : renderQueue;

        if (sharedMaterials.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        Material mat;

        if (ignoreDepth)
        {
            Shader ui = Shader.Find("UI/Default");
            if (ui == null)
            {
                Debug.LogWarning("[FactorySprite] Shader \"UI/Default\" sa nenašiel – " +
                                 "debugIgnoreDepth sa ignoruje.");
                return ResolveMaterial(renderQueue, false);
            }

            mat = new Material(ui) { name = "FactorySpriteMat_DEBUG_OnTop" };
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 4000;
        }
        else
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null)
            {
                Debug.LogWarning("[FactorySprite] Nenašiel sa shader \"Sprites/Default\" – " +
                                 "použije sa predvolený materiál SpriteRenderera.");
                return null;
            }

            mat = new Material(sh) { name = $"FactorySpriteMat_{renderQueue}" };
            mat.renderQueue = renderQueue;
        }

        sharedMaterials[key] = mat;
        return mat;
    }

    /// <summary>
    /// Jeden riadok do Console so VŠETKÝM, čo treba na určenie príčiny, keď
    /// továreň nevidno alebo sedí zle. Vypisuje sa po prvom vykreslenom snímku
    /// (isVisible má zmysel až vtedy).
    /// </summary>
    private void LogDiagnostics()
    {
        Camera cam = ResolveCamera();

        string spriteInfo = (sprite != null)
            ? $"sprite='{sprite.name}' rám={sprite.bounds.size.x:F2}×{sprite.bounds.size.y:F2} " +
              $"obsah={contentSize.x:F2}×{contentSize.y:F2} (tight={(global != null && global.useTightBounds)})"
            : "sprite=NULL (!!)";

        string camInfo = "kamera=NULL (!!)";
        string viewInfo = "";
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(transform.position);
            bool onScreen = vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            camInfo = $"kamera='{cam.name}' ortho={cam.orthographic} size={cam.orthographicSize} " +
                      $"layer={LayerMask.LayerToName(gameObject.layer)}({gameObject.layer})";
            viewInfo = $" viewport=({vp.x:F2},{vp.y:F2}) onScreen={onScreen}";
        }

        string matInfo = (spriteRenderer != null && spriteRenderer.sharedMaterial != null)
            ? $"shader='{spriteRenderer.sharedMaterial.shader.name}' queue={spriteRenderer.sharedMaterial.renderQueue}"
            : "materiál=NULL";

        Debug.Log($"[FactorySprite] '{name}': {spriteInfo}; pos={transform.position} " +
                  $"scale={transform.localScale.x:F3} widthMul={settings.widthMultiplier} " +
                  $"vOffset={settings.verticalOffset}; " +
                  $"isVisible={(spriteRenderer != null && spriteRenderer.isVisible)} " +
                  $"order={(spriteRenderer != null ? spriteRenderer.sortingOrder : 0)}; " +
                  $"{matInfo}; {camInfo}.{viewInfo}");
    }
}
