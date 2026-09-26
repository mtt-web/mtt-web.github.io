using UnityEngine;

/// <summary>
/// CitySurroundingData
/// ─────────────────────────────────────────────────────────────────────────
/// Globálna dátová textúra OKOLIA MIEST pre shader RetroTerrain/Terrain.
/// Analógia k <see cref="TerrainSurfaceData"/>: jeden pixel = jeden VRCHOL
/// terénu (terrainWidth+1)², hodnota R = vzdialenosť k najbližšej MESTSKEJ
/// BUDOVE v tiloch (0 = priamo na budove, orezané na maxDistance).
///
/// Terénny shader z toho robí prirodzené pásma okolo mesta:
///
///     0 – 1.5 tile   udupané dvory a chodníky medzi domami
///     1.5 – 5        kosený trávnik / záhrady (sýtejšia, tmavšia tráva)
///     5 – 14         zelený pás a lesík, ktorý smerom von rednie
///
/// Zdroj dát: CityManager.IsCityTile nad regiónmi jednotlivých miest, takže
/// CityManager sa nijako nemení a stačí mu jeho verejné API.
///
/// PREPOČET: automaticky, keď sa zmení počet miest alebo budov (generovanie
/// pri štarte, Load hry, prípadný rast miest). Kontrola beží raz za pol
/// sekundy a je len porovnaním niekoľkých čísel.
///
/// V scéne netreba nič nastavovať – komponent sa vytvorí sám. Ak chceš ladiť
/// dosah v Inspectore, pridaj ho na ľubovoľný GameObject; použije sa ten.
///
/// PREDPOKLAD: TerrainManager je v (0,0,0) (rovnako ako v TerrainBorder).
/// </summary>
[DisallowMultipleComponent]
public class CitySurroundingData : MonoBehaviour
{
    public static CitySurroundingData instance;

    [Tooltip("Maximálna počítaná vzdialenosť od mesta v tiloch. Musí byť väčšia " +
             "ako najväčší dosah použitý v shaderi (lesný pás).")]
    [Range(4f, 48f)]
    [SerializeField] private float maxDistance = 20f;

    [Tooltip("Ako často sa kontroluje, či sa mestá zmenili (sekundy).")]
    [Range(0.1f, 5f)]
    [SerializeField] private float checkInterval = 0.5f;

    static readonly int ID_Map     = Shader.PropertyToID("_CityDataMap");
    static readonly int ID_Size    = Shader.PropertyToID("_CityDataSize");
    static readonly int ID_Enabled = Shader.PropertyToID("_CityDataEnabled");

    const float Big = 1e6f;
    const float Diag = 1.41421356f;

    Texture2D map;
    ushort[] pixels;
    float[] dist;
    int size;
    int channels;
    bool dirty = true;
    float nextCheck;
    int lastSignature = -1;

    // =====================================================================
    // SINGLETON
    // =====================================================================

    public static CitySurroundingData GetOrCreate()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<CitySurroundingData>();
        if (instance == null)
            instance = new GameObject("CitySurroundingData").AddComponent<CitySurroundingData>();

        return instance;
    }

    /// <summary>Vynúti prepočet (napr. po ručnej zmene miest).</summary>
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

    private void OnDestroy()
    {
        if (instance != this) return;

        instance = null;
        Shader.SetGlobalFloat(ID_Enabled, 0f);

        if (map != null) Destroy(map);
        map = null;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + checkInterval;

            int sig = CitySignature();
            if (sig != lastSignature)
            {
                lastSignature = sig;
                dirty = true;
            }
        }

        if (dirty) Rebuild();
    }

    /// <summary>Lacný "odtlačok" stavu miest – zmení sa pri generovaní aj po Load.</summary>
    int CitySignature()
    {
        var cm = CityManager.instance;
        if (cm == null || cm.Cities == null) return 0;

        unchecked
        {
            int sig = 17 + cm.Cities.Count * 31;
            for (int i = 0; i < cm.Cities.Count; i++)
            {
                var c = cm.Cities[i];
                if (c == null) continue;
                sig = sig * 31 + c.buildingRecords.Count;
                sig = sig * 31 + c.region.x * 7919 + c.region.y;
            }
            return sig;
        }
    }

    // =====================================================================
    // PREPOČET
    // =====================================================================

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        var tm = TerrainManager.instance;
        var cm = CityManager.instance;
        if (tm == null || tm.terrainWidth <= 0) return;   // skúsime v ďalšom frame

        int n = tm.terrainWidth + 1;
        if (!EnsureTexture(n)) { dirty = false; return; }

        for (int i = 0; i < dist.Length; i++) dist[i] = Big;

        // Každý mestský tile [x,z] označí svoje 4 rohové vrcholy ako vzdialenosť 0.
        if (cm != null && cm.Cities != null)
        {
            for (int ci = 0; ci < cm.Cities.Count; ci++)
            {
                var city = cm.Cities[ci];
                if (city == null) continue;

                RectInt r = city.region;
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    for (int z = r.yMin; z < r.yMax; z++)
                    {
                        if (!cm.IsCityTile(x, z)) continue;

                        MarkVertex(x, z, n);
                        MarkVertex(x + 1, z, n);
                        MarkVertex(x, z + 1, n);
                        MarkVertex(x + 1, z + 1, n);
                    }
                }
            }
        }

        Chamfer(dist, n);

        for (int i = 0; i < dist.Length; i++)
            pixels[i * channels] = Mathf.FloatToHalf(Mathf.Min(dist[i], maxDistance));

        map.SetPixelData(pixels, 0);
        map.Apply(false, false);

        Shader.SetGlobalTexture(ID_Map, map);
        Shader.SetGlobalVector(ID_Size, new Vector4(n, n, 1f / n, 1f / n));
        Shader.SetGlobalFloat(ID_Enabled, 1f);

        dirty = false;
    }

    void MarkVertex(int x, int z, int n)
    {
        if (x < 0 || z < 0 || x >= n || z >= n) return;
        dist[z * n + x] = 0f;
    }

    /// <summary>Dvojprechodová chamfer transformácia vzdialenosti (váhy 1 a √2).</summary>
    static void Chamfer(float[] d, int n)
    {
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
        if (SystemInfo.SupportsTextureFormat(TextureFormat.RHalf))
        {
            format = TextureFormat.RHalf;
            channels = 1;
        }
        else if (SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
        {
            format = TextureFormat.RGBAHalf;
            channels = 4;
        }
        else
        {
            Debug.LogWarning("[CitySurroundingData] GPU nepodporuje half textúry – " +
                             "okolie miest sa v teréne nevykreslí.");
            Shader.SetGlobalFloat(ID_Enabled, 0f);
            return false;
        }

        if (map != null) Destroy(map);

        size = n;
        map = new Texture2D(n, n, format, false, true)
        {
            name = "CityDataMap",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        pixels = new ushort[n * n * channels];
        dist = new float[n * n];
        return true;
    }
}
