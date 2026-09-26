using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zvýraznenie tilov na teréne cez shader RetroTerrain/Terrain. Tri typy:
///
///   • TerrainEdit – LevelUp / LevelDown: hnedá zemina (pôvodný efekt)
///   • Build       – výstavba čohokoľvek: zlatistý rám tile, svetelná vlna
///                   od stredu k okraju, jemné iskrenie, "pripravený" podklad
///   • Demolish    – búranie: krátky žeravý záblesk v prasklinách, rozvírený
///                   prach, sutina a spálenina, ktorá postupne zarastie trávou
///
/// PRINCÍP:
///   • Skript drží malú float textúru (terrainWidth × terrainWidth), kde každý
///     pixel = jeden tile: R = ČAS štartu efektu, G = TYP efektu (0/1/2).
///   • Každý frame sa do shadera pošle len aktuálny čas (_TerrainFlashTime).
///     Priebeh si shader počíta sám na GPU. Textúra sa nahráva na GPU LEN pri
///     spustení nového efektu a mesh terénu sa kvôli efektu neprestavuje.
///   • Parametre (farby, časy, sila) sú globálne shader hodnoty.
///
/// POUŽITIE:
///   TerrainEditHighlight.GetOrCreate().FlashVertices(zmenenéVrcholy);                // úprava terénu
///   TerrainEditHighlight.GetOrCreate().FlashTile(x, z, FlashType.Build);             // stavba
///   TerrainEditHighlight.GetOrCreate().FlashTiles(tily, FlashType.Demolish);         // búranie
///
/// Výstavbu a búranie spúšťa automaticky IndicatrixAPI pri každej zmene tile
/// (plus mosty/tunely v CrossingSystemBase a stromy/kamene v GameManager).
///
/// V scéne ho netreba nastavovať – vytvorí sa sám. Ak chceš ladiť farby/časy
/// v Inspectore, pridaj komponent na ľubovoľný GameObject; GetOrCreate použije ten.
/// </summary>
public class TerrainEditHighlight : MonoBehaviour
{
    public static TerrainEditHighlight instance;

    public enum FlashType
    {
        TerrainEdit = 0,
        Build = 1,
        Demolish = 2
    }

    // =====================================================================
    // ÚPRAVA TERÉNU (pôvodné polia – názvy zachované, aby ostali hodnoty
    // nastavené v Inspectore)
    // =====================================================================

    [Header("ÚPRAVA TERÉNU – farba")]
    [Tooltip("Farba, do ktorej sa upravené tily sfarbia.")]
    [SerializeField] private Color flashColor = new Color(0.45f, 0.29f, 0.14f, 1f);

    [Tooltip("Sila sfarbenia na vrchole efektu. 1 = úplne hnedá, 0.5 = napoly zmiešaná s terénom.")]
    [Range(0f, 1f)]
    [SerializeField] private float strength = 1f;

    [Tooltip("0 = plochá hnedá farba, 1 = hnedá so zachovanou štruktúrou (svetlosťou) textúry terénu.")]
    [Range(0f, 1f)]
    [SerializeField] private float textureDetail = 0.3f;

    [Header("ÚPRAVA TERÉNU – časovanie (sekundy)")]
    [Tooltip("Dĺžka prechodu z pôvodnej textúry do hnedej.")]
    [Min(0f)][SerializeField] private float fadeInDuration = 0.3f;

    [Tooltip("Ako dlho tily zostanú úplne hnedé.")]
    [Min(0f)][SerializeField] private float holdDuration = 1f;

    [Tooltip("Dĺžka prechodu z hnedej späť do pôvodnej textúry.")]
    [Min(0f)][SerializeField] private float fadeOutDuration = 0.5f;

    // =====================================================================
    // VÝSTAVBA
    // =====================================================================

    [Header("VÝSTAVBA – farby")]
    [Tooltip("Farba svietiaceho rámu a vlny (emisia – svieti aj v tieni).")]
    [SerializeField] private Color buildGlowColor = new Color(1f, 0.82f, 0.42f, 1f);

    [Tooltip("Farba pripraveného podkladu (udupaná zem / štrk pod stavbou).")]
    [SerializeField] private Color buildGroundColor = new Color(0.78f, 0.68f, 0.50f, 1f);

    [Tooltip("Sila celého efektu výstavby.")]
    [Range(0f, 1f)]
    [SerializeField] private float buildStrength = 1f;

    [Tooltip("Jas svetelnej emisie (rám, vlna, iskry).")]
    [Range(0f, 3f)]
    [SerializeField] private float buildGlowIntensity = 0.9f;

    [Header("VÝSTAVBA – časovanie (sekundy)")]
    [Min(0f)][SerializeField] private float buildFadeIn = 0.12f;
    [Min(0f)][SerializeField] private float buildHold = 0.45f;
    [Min(0f)][SerializeField] private float buildFadeOut = 0.9f;

    // =====================================================================
    // DEMOLÁCIA
    // =====================================================================

    [Header("DEMOLÁCIA – farby")]
    [Tooltip("Farba sutiny / spáleniny.")]
    [SerializeField] private Color demolishRubbleColor = new Color(0.30f, 0.26f, 0.22f, 1f);

    [Tooltip("Farba rozvíreného prachu.")]
    [SerializeField] private Color demolishDustColor = new Color(0.70f, 0.65f, 0.57f, 1f);

    [Tooltip("Farba žeravých prasklín na začiatku (emisia).")]
    [SerializeField] private Color demolishEmberColor = new Color(1f, 0.45f, 0.12f, 1f);

    [Tooltip("Sila celého efektu demolácie.")]
    [Range(0f, 1f)]
    [SerializeField] private float demolishStrength = 1f;

    [Tooltip("Jas žeravého záblesku v prasklinách.")]
    [Range(0f, 3f)]
    [SerializeField] private float demolishEmberIntensity = 1.1f;

    [Header("DEMOLÁCIA – časovanie (sekundy)")]
    [Min(0f)][SerializeField] private float demolishFadeIn = 0.08f;
    [Min(0f)][SerializeField] private float demolishHold = 0.9f;
    [Min(0f)][SerializeField] private float demolishFadeOut = 1.4f;

    // =====================================================================

    [Header("Všeobecné")]
    [Tooltip("Zapnuté = efekt beží v reálnom čase (dobehne aj pri pauze / zmenenej rýchlosti hry).")]
    [SerializeField] private bool useUnscaledTime = true;

    // ── ID globálnych shader premenných (musia sedieť s terrainShader.shader) ──
    static readonly int ID_Map = Shader.PropertyToID("_TerrainFlashMap");
    static readonly int ID_MapSize = Shader.PropertyToID("_TerrainFlashMapSize");
    static readonly int ID_Enabled = Shader.PropertyToID("_TerrainFlashEnabled");
    static readonly int ID_Time = Shader.PropertyToID("_TerrainFlashTime");
    static readonly int ID_Params = Shader.PropertyToID("_TerrainFlashParams");
    static readonly int ID_Color = Shader.PropertyToID("_TerrainFlashColor");
    static readonly int ID_Detail = Shader.PropertyToID("_TerrainFlashDetail");

    static readonly int ID_BuildParams = Shader.PropertyToID("_TerrainBuildParams");
    static readonly int ID_BuildGlow = Shader.PropertyToID("_TerrainBuildGlow");
    static readonly int ID_BuildGround = Shader.PropertyToID("_TerrainBuildGround");

    static readonly int ID_DemoParams = Shader.PropertyToID("_TerrainDemolishParams");
    static readonly int ID_DemoRubble = Shader.PropertyToID("_TerrainDemolishRubble");
    static readonly int ID_DemoDust = Shader.PropertyToID("_TerrainDemolishDust");
    static readonly int ID_DemoEmber = Shader.PropertyToID("_TerrainDemolishEmber");

    // Čas štartu pre tile, ktorý nikdy nesvietil – dosť ďaleko v minulosti,
    // aby shader vyhodnotil intenzitu 0.
    const float InactiveStart = -1000000f;

    Texture2D flashMap;
    float[] data;         // pixel dáta: [start, type, (0, 0)] na tile
    int mapSize;
    int channels;         // 2 pre RGFloat, 4 pre RGBAFloat (záloha)
    bool dirty;

    float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

    // =====================================================================
    // SINGLETON
    // =====================================================================

    public static TerrainEditHighlight GetOrCreate()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<TerrainEditHighlight>();
        if (instance == null)
            instance = new GameObject("TerrainEditHighlight").AddComponent<TerrainEditHighlight>();

        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
        PushParams();
    }

    private void OnValidate()
    {
        // Zmeny v Inspectore sa prejavia okamžite aj počas hry.
        PushParams();
    }

    private void Update()
    {
        if (flashMap != null)
            Shader.SetGlobalFloat(ID_Time, Now);
    }

    private void LateUpdate()
    {
        if (dirty) Upload();
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        instance = null;
        Shader.SetGlobalFloat(ID_Enabled, 0f);

        if (flashMap != null)
            Destroy(flashMap);
        flashMap = null;
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Zvýrazní všetky tily, ktorých ROHOM je aspoň jeden zo zadaných vrcholov
    /// (efekt úpravy terénu). Vstup je presne výstup
    /// TerrainManager.PredictVertexLevelChanges.
    /// </summary>
    public void FlashVertices(List<Vector2Int> vertices)
    {
        FlashVertices(vertices, FlashType.TerrainEdit);
    }

    /// <summary>Ako FlashVertices vyššie, ale s voliteľným typom efektu.</summary>
    public void FlashVertices(List<Vector2Int> vertices, FlashType type)
    {
        if (vertices == null || vertices.Count == 0) return;

        var tiles = new HashSet<Vector2Int>();
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2Int v = vertices[i];
            for (int tx = v.x - 1; tx <= v.x; tx++)
                for (int tz = v.y - 1; tz <= v.y; tz++)
                    tiles.Add(new Vector2Int(tx, tz));
        }

        FlashTiles(tiles, type);
    }

    /// <summary>Spustí efekt úpravy terénu pre zadané tily (pôvodné API).</summary>
    public void FlashTiles(IEnumerable<Vector2Int> tiles)
    {
        FlashTiles(tiles, FlashType.TerrainEdit);
    }

    /// <summary>Spustí efekt daného typu na jednom tile [x,z].</summary>
    public void FlashTile(int x, int z, FlashType type)
    {
        if (!EnsureMap()) return;
        FlashOne(x, z, type, Now);
        Shader.SetGlobalFloat(ID_Time, Now);
    }

    /// <summary>Spustí efekt na obdĺžniku tilov (napr. footprint továrne).</summary>
    public void FlashRect(int originX, int originZ, int width, int depth, FlashType type)
    {
        if (!EnsureMap()) return;
        float now = Now;
        for (int x = originX; x < originX + width; x++)
            for (int z = originZ; z < originZ + depth; z++)
                FlashOne(x, z, type, now);
        Shader.SetGlobalFloat(ID_Time, now);
    }

    /// <summary>
    /// Spustí efekt daného typu pre zadané tily [x,z]. Indexy mimo mapy sa ignorujú.
    ///
    /// Opätovné spustenie na tile, ktorý práve svieti, neresetuje efekt skokom:
    /// tile plynule pokračuje z aktuálnej intenzity smerom k plnému efektu
    /// (dôležité pri rýchlom klikaní na ten istý tile).
    /// </summary>
    public void FlashTiles(IEnumerable<Vector2Int> tiles, FlashType type)
    {
        if (tiles == null || !EnsureMap()) return;

        float now = Now;
        foreach (Vector2Int t in tiles)
            FlashOne(t.x, t.y, type, now);

        // Čas pošleme hneď, aby shader v tomto frame nevidel starú hodnotu.
        Shader.SetGlobalFloat(ID_Time, now);
    }

    /// <summary>Okamžite zruší všetky prebiehajúce efekty (napr. po načítaní hry).</summary>
    public void ClearAll()
    {
        if (data == null) return;
        for (int i = 0; i < data.Length; i += channels)
        {
            data[i] = InactiveStart;
            data[i + 1] = 0f;
        }
        dirty = true;
    }

    // =====================================================================
    // INTERNÉ
    // =====================================================================

    void FlashOne(int x, int z, FlashType type, float now)
    {
        if (x < 0 || z < 0 || x >= mapSize || z >= mapSize) return;

        int i = (z * mapSize + x) * channels;

        // Aktuálna intenzita (0..1) podľa doteraz bežiaceho efektu → bod na
        // fade-in krivke nového efektu, aby nevznikol skok.
        FlashType oldType = (FlashType)Mathf.RoundToInt(data[i + 1]);
        float current = EnvelopeAt(now - data[i], oldType);
        float fadeInProgress = InverseSmoothstep(current);

        GetTiming(type, out float fadeIn, out _, out _);
        data[i] = now - fadeInProgress * fadeIn;
        data[i + 1] = (float)(int)type;
        dirty = true;
    }

    void GetTiming(FlashType type, out float fadeIn, out float hold, out float fadeOut)
    {
        switch (type)
        {
            case FlashType.Build:
                fadeIn = buildFadeIn; hold = buildHold; fadeOut = buildFadeOut; break;
            case FlashType.Demolish:
                fadeIn = demolishFadeIn; hold = demolishHold; fadeOut = demolishFadeOut; break;
            default:
                fadeIn = fadeInDuration; hold = holdDuration; fadeOut = fadeOutDuration; break;
        }
    }

    /// <summary>
    /// Pripraví (alebo pri zmene veľkosti terénu znovu vytvorí) textúru.
    /// Vracia false, ak terén ešte neexistuje alebo GPU nepodporuje float textúry.
    /// </summary>
    bool EnsureMap()
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.terrainWidth <= 0) return false;

        if (flashMap != null && mapSize == tm.terrainWidth) return true;

        TextureFormat format;
        if (SystemInfo.SupportsTextureFormat(TextureFormat.RGFloat))
        {
            format = TextureFormat.RGFloat;
            channels = 2;
        }
        else if (SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
        {
            format = TextureFormat.RGBAFloat;
            channels = 4;
        }
        else
        {
            Debug.LogWarning("[TerrainEditHighlight] GPU nepodporuje float textúry – zvýraznenie terénu je vypnuté.");
            return false;
        }

        if (flashMap != null) Destroy(flashMap);

        mapSize = tm.terrainWidth;
        flashMap = new Texture2D(mapSize, mapSize, format, false, true)
        {
            name = "TerrainFlashMap",
            filterMode = FilterMode.Point,     // žiadne rozmazanie medzi tilmi
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        data = new float[mapSize * mapSize * channels];
        for (int i = 0; i < data.Length; i += channels)
            data[i] = InactiveStart;
        Upload();

        Shader.SetGlobalTexture(ID_Map, flashMap);
        Shader.SetGlobalVector(ID_MapSize, new Vector4(mapSize, mapSize, 1f / mapSize, 1f / mapSize));
        Shader.SetGlobalFloat(ID_Time, Now);
        PushParams();
        Shader.SetGlobalFloat(ID_Enabled, 1f);

        return true;
    }

    void Upload()
    {
        if (flashMap == null) return;
        flashMap.SetPixelData(data, 0);
        flashMap.Apply(false, false);
        dirty = false;
    }

    void PushParams()
    {
        Shader.SetGlobalVector(ID_Params, new Vector4(fadeInDuration, holdDuration, fadeOutDuration, strength));
        Shader.SetGlobalColor(ID_Color, flashColor);
        Shader.SetGlobalFloat(ID_Detail, textureDetail);

        Shader.SetGlobalVector(ID_BuildParams, new Vector4(buildFadeIn, buildHold, buildFadeOut, buildStrength));
        Color glow = buildGlowColor * buildGlowIntensity; glow.a = 1f;
        Shader.SetGlobalColor(ID_BuildGlow, glow);
        Shader.SetGlobalColor(ID_BuildGround, buildGroundColor);

        Shader.SetGlobalVector(ID_DemoParams, new Vector4(demolishFadeIn, demolishHold, demolishFadeOut, demolishStrength));
        Shader.SetGlobalColor(ID_DemoRubble, demolishRubbleColor);
        Shader.SetGlobalColor(ID_DemoDust, demolishDustColor);
        Color ember = demolishEmberColor * demolishEmberIntensity; ember.a = 1f;
        Shader.SetGlobalColor(ID_DemoEmber, ember);
    }

    /// <summary>
    /// Priebeh intenzity v čase t od štartu (bez 'strength').
    /// MUSÍ zodpovedať funkcii FlashEnvelope v terrainShader.shader.
    /// </summary>
    float EnvelopeAt(float t, FlashType type)
    {
        GetTiming(type, out float fadeInD, out float holdD, out float fadeOutD);
        float fadeIn = Mathf.Clamp01(t / Mathf.Max(fadeInD, 0.0001f));
        float fadeOut = 1f - Mathf.Clamp01((t - fadeInD - holdD) / Mathf.Max(fadeOutD, 0.0001f));
        return Smoothstep(fadeIn) * Smoothstep(fadeOut);
    }

    static float Smoothstep(float x) => x * x * (3f - 2f * x);

    /// <summary>Inverzia k Smoothstep na intervale 0..1.</summary>
    static float InverseSmoothstep(float y)
    {
        y = Mathf.Clamp01(y);
        return 0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * y) / 3f);
    }
}
