using UnityEngine;

/// <summary>
/// TerrainSurfaceData
/// ─────────────────────────────────────────────────────────────────────────
/// Globálna "dátová" textúra terénu pre shadery RetroTerrain/Terrain a
/// RetroTerrain/Water. Jeden pixel = jeden VRCHOL terénu (terrainWidth+1)².
///
///   R = výška vrcholu (coordsF.y)
///   G = SIGNED vzdialenosť k pobrežiu v tiloch (SDF):
///         > 0  … pevnina, vzdialenosť k najbližšiemu vodnému vrcholu
///         < 0  … voda,    vzdialenosť k najbližšiemu vrcholu pevniny
///         ≈ 0  … pobrežná čiara (stred pobrežného tile)
///
/// Textúra je bilineárne filtrovaná, takže shader dostane plynulé hodnoty
/// kdekoľvek na mape. Vďaka tomu:
///   • voda vie svoju skutočnú HĹBKU (plytčina / hlbina) a vzdialenosť od
///     brehu (pena, príbojové vlny) – bez _CameraDepthTexture, ktorá pri
///     izometrickej kamere nefunguje spoľahlivo,
///   • terén vie, kde je breh (pláž, bahno, sýtejšia tráva pri vode) a kde
///     sú priehlbiny (jemné zatienenie – "cavity AO").
///
/// Vodný vrchol = vrchol s Y ≤ TerrainManager.MinTerrainHeight (2.75),
/// rovnaká definícia ako IndicatrixAPI.IsFaceWater.
///
/// PREPOČET: stačí zavolať TerrainSurfaceData.MarkDirty() – robí to
/// TerrainElement.BuildMesh (pokrýva generovanie, LevelUp/LevelDown aj Load).
/// Celá mapa sa prepočíta raz v LateUpdate (256×256 → rádovo 1–3 ms).
///
/// V scéne ho netreba nastavovať – vytvorí sa sám. Ak chceš ladiť parametre
/// v Inspectore, pridaj komponent na ľubovoľný GameObject; použije sa ten.
///
/// PREDPOKLAD: TerrainManager je v (0,0,0) (rovnako ako v TerrainBorder).
/// </summary>
[DisallowMultipleComponent]
public class TerrainSurfaceData : MonoBehaviour
{
    public static TerrainSurfaceData instance;

    [Tooltip("O koľko je vizuálna hladina nad MinTerrainHeight (2.75). Pôvodný shader mal priemerne ~0.05.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float waterSurfaceOffset = 0.05f;

    [Tooltip("Maximálna počítaná vzdialenosť od brehu v tiloch. Väčšie = dlhšie prechody farby vody.")]
    [Range(2f, 64f)]
    [SerializeField] private float maxDistance = 16f;

    // ── ID globálnych shader premenných (musia sedieť so shadermi) ──
    static readonly int ID_Map      = Shader.PropertyToID("_TerrainDataMap");
    static readonly int ID_Size     = Shader.PropertyToID("_TerrainDataSize");
    static readonly int ID_Enabled  = Shader.PropertyToID("_TerrainDataEnabled");
    static readonly int ID_WaterY   = Shader.PropertyToID("_TerrainWaterY");

    const float Big = 1e6f;
    const float Diag = 1.41421356f;

    Texture2D map;
    ushort[] pixels;
    float[] distToWater;
    float[] distToLand;
    bool[] isWater;
    int size;
    int channels;
    bool dirty = true;

    public float WaterSurfaceY => TerrainManager.MinTerrainHeight + waterSurfaceOffset;

    // =====================================================================
    // SINGLETON
    // =====================================================================

    public static TerrainSurfaceData GetOrCreate()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<TerrainSurfaceData>();
        if (instance == null)
            instance = new GameObject("TerrainSurfaceData").AddComponent<TerrainSurfaceData>();

        return instance;
    }

    /// <summary>Označí dáta na prepočet (vykoná sa raz v najbližšom LateUpdate).</summary>
    public static void MarkDirty()
    {
        if (!Application.isPlaying) return;
        GetOrCreate().dirty = true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void OnValidate()
    {
        if (map != null) Shader.SetGlobalFloat(ID_WaterY, WaterSurfaceY);
    }

    private void LateUpdate()
    {
        if (dirty) Rebuild();
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        instance = null;
        Shader.SetGlobalFloat(ID_Enabled, 0f);

        if (map != null) Destroy(map);
        map = null;
    }

    // =====================================================================
    // PREPOČET
    // =====================================================================

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null || tm.coordsF.Length == 0 || tm.terrainWidth <= 0)
            return; // skúsime znova v ďalšom frame (dirty ostáva true)

        int n = tm.terrainWidth + 1;
        if (tm.coordsF.Length < n * n) return;

        if (!EnsureTexture(n)) { dirty = false; return; }

        Vector3[] c = tm.coordsF;
        int count = n * n;
        float waterLimit = TerrainManager.MinTerrainHeight + 0.0001f;

        for (int i = 0; i < count; i++)
        {
            bool w = c[i].y <= waterLimit;
            isWater[i] = w;
            distToWater[i] = w ? 0f : Big;
            distToLand[i] = w ? Big : 0f;
        }

        Chamfer(distToWater, n);
        Chamfer(distToLand, n);

        for (int i = 0; i < count; i++)
        {
            // Posun o 0.5 → nulová hodnota leží v strede pobrežného tile.
            float sdf = isWater[i] ? -(distToLand[i] - 0.5f) : (distToWater[i] - 0.5f);
            sdf = Mathf.Clamp(sdf, -maxDistance, maxDistance);

            int p = i * channels;
            pixels[p] = Mathf.FloatToHalf(c[i].y);
            pixels[p + 1] = Mathf.FloatToHalf(sdf);
        }

        map.SetPixelData(pixels, 0);
        map.Apply(false, false);

        Shader.SetGlobalTexture(ID_Map, map);
        Shader.SetGlobalVector(ID_Size, new Vector4(n, n, 1f / n, 1f / n));
        Shader.SetGlobalFloat(ID_WaterY, WaterSurfaceY);
        Shader.SetGlobalFloat(ID_Enabled, 1f);

        dirty = false;
    }

    /// <summary>
    /// Dvojprechodová chamfer transformácia vzdialenosti (8-susedstvo,
    /// váhy 1 a √2) – dostatočne presná aproximácia euklidovskej vzdialenosti.
    /// </summary>
    static void Chamfer(float[] d, int n)
    {
        // dopredný prechod
        for (int z = 0; z < n; z++)
        {
            int row = z * n;
            for (int x = 0; x < n; x++)
            {
                int i = row + x;
                float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + 1f);
                if (z > 0)
                {
                    v = Mathf.Min(v, d[i - n] + 1f);
                    if (x > 0) v = Mathf.Min(v, d[i - n - 1] + Diag);
                    if (x < n - 1) v = Mathf.Min(v, d[i - n + 1] + Diag);
                }
                d[i] = v;
            }
        }

        // spätný prechod
        for (int z = n - 1; z >= 0; z--)
        {
            int row = z * n;
            for (int x = n - 1; x >= 0; x--)
            {
                int i = row + x;
                float v = d[i];
                if (x < n - 1) v = Mathf.Min(v, d[i + 1] + 1f);
                if (z < n - 1)
                {
                    v = Mathf.Min(v, d[i + n] + 1f);
                    if (x < n - 1) v = Mathf.Min(v, d[i + n + 1] + Diag);
                    if (x > 0) v = Mathf.Min(v, d[i + n - 1] + Diag);
                }
                d[i] = v;
            }
        }
    }

    bool EnsureTexture(int n)
    {
        if (map != null && size == n) return true;

        TextureFormat format;
        if (SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf))
        {
            format = TextureFormat.RGHalf;
            channels = 2;
        }
        else if (SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
        {
            format = TextureFormat.RGBAHalf;
            channels = 4;
        }
        else
        {
            Debug.LogWarning("[TerrainSurfaceData] GPU nepodporuje half textúry – " +
                             "pláže/pena/hĺbka vody bežia v zjednodušenom režime.");
            Shader.SetGlobalFloat(ID_Enabled, 0f);
            return false;
        }

        if (map != null) Destroy(map);

        size = n;
        map = new Texture2D(n, n, format, false, true)
        {
            name = "TerrainDataMap",
            filterMode = FilterMode.Bilinear,   // plynulé hodnoty medzi vrcholmi
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        pixels = new ushort[n * n * channels];
        distToWater = new float[n * n];
        distToLand = new float[n * n];
        isWater = new bool[n * n];
        return true;
    }
}
