using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CityNightWindows
/// ─────────────────────────────────────────────────────────────────────────
/// Rozsvietené OKNÁ MESTSKÝCH BUDOV v noci (NightModeManager, tlačidlo NightToggleUIButton).
///
/// Pre každý z 15 slotov <see cref="CityBuildingLibrary"/> (Low / Medium /
/// High × 5) je v Inspectore políčko „Enabled“ a index PROFILU. Profil
/// určuje, ako sa v danom modeli nájdu okná:
///
///   • UV Rect         – Low Poly Simple Urban (tex.png): sklo má UV v jednom
///                       políčku atlasu. (LOW 1–5)
///   • Texture Color   – SimplePoly City: sklo má v textúre presnú farbu.
///                       Z textúry sa vyrobí mapa okien, každá tabuľa svieti
///                       samostatne. (MEDIUM 1–5)
///   • Emission Map    – Skyscrapers: okná sú v pôvodnej emission mape
///                       modelu (autor ich tam nakreslil). (HIGH 1–5)
///
/// Ako to funguje (CityManager sa NEMENÍ):
///   • Raz za checkInterval sa porovná „odtlačok“ miest; po zmene
///     (generovanie, Load) sa prejdú budovy. City.buildings a
///     City.buildingRecords sú 1:1, takže z receptu vieme tier + variant.
///   • Budove sa vymení materiál za kópiu s shaderom
///     RetroCity/BuildingNightWindows. Textúry, farba, metallic a smoothness
///     sa prevezmú z pôvodného materiálu → cez deň vyzerá rovnako.
///     Materiál sa vytvára RAZ na (pôvodný materiál, profil) a je zdieľaný.
///   • Mapa okien (Texture Color / Emission Map) sa počíta RAZ na textúru –
///     cez GPU kópiu, takže textúra NEMUSÍ mať Read/Write.
///   • Mesh: každé okno / plocha dostane náhodné číslo do UV3 (RAZ na mesh).
///     Vyžaduje Read/Write na FBX (SimplePoly a Skyscrapers ho už majú,
///     Low Poly Urban ho treba zapnúť). Bez neho to funguje tiež, len
///     s menšou rozmanitosťou.
///
/// Všetky nastavenia vzhľadu a blikania sa dajú ladiť aj počas hry.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[DisallowMultipleComponent]
public class CityNightWindows : MonoBehaviour
{
    public static CityNightWindows instance;

    public enum WindowDetection
    {
        UVRect = 0,        // sklo = UV v obdĺžniku atlasu
        TextureColor = 1,  // sklo = farba v hlavnej textúre
        EmissionMap = 2    // sklo = svetlé miesta v emission mape
    }

    [Serializable]
    public class WindowProfile
    {
        public string name = "Profil";
        public WindowDetection detection = WindowDetection.UVRect;

        [Tooltip("UV Rect: políčko atlasu so sklom (x, y, šírka, výška v UV 0–1).")]
        public Vector4 uvRect = new Vector4(0.1016f, 0.9609f, 0.0391f, 0.0391f);

        [Tooltip("Texture Color: farby skla v textúre.")]
        public Color[] glassColors = new Color[0];
        [Tooltip("Texture Color: tolerancia farby (0.04 ≈ 10/255, pokryje aj kompresiu textúry).")]
        [Range(0f, 0.2f)] public float colorTolerance = 0.04f;
        [Tooltip("Texture Color: plocha modelu je okno, len ak na ňu je natiahnutý " +
                 "väčší kus textúry (min. rozmer v UV). Vylúči zábradlia, rámy a " +
                 "iné tenké diely rovnakej farby.")]
        [Range(0f, 0.2f)] public float minWindowUVSize = 0.035f;

        [Tooltip("Emission Map: od akého jasu emission mapy je pixel okno.")]
        [Range(0.01f, 0.5f)] public float emissionThreshold = 0.06f;
        [Tooltip("Emission Map: 0 = pôvodné farby z emission mapy, 1 = farby okien nižšie.")]
        [Range(0f, 1f)] public float emissionRecolor = 0.35f;

        [Tooltip("Násobok jasu okien pre tento profil.")]
        [Range(0f, 4f)] public float intensityScale = 1f;

        [Tooltip("Zapnuté = tento profil má vlastný podiel rozsvietených okien " +
                 "namiesto globálneho Lit Fraction.")]
        public bool overrideLitFraction = false;
        [Range(0f, 1f)] public float litFraction = 0.75f;

        [Tooltip("Voliteľné: textúra namiesto tej z pôvodného materiálu " +
                 "(UV Rect / Texture Color = albedo, Emission Map = emission mapa).")]
        public Texture textureOverride;
    }

    [Serializable]
    public class SlotSetup
    {
        public bool enabled = true;
        [Tooltip("Index do zoznamu Profiles.")]
        public int profile;

        public SlotSetup() { }
        public SlotSetup(int profile) { this.profile = profile; }
    }

    [Header("Shader (pretiahni sem RetroCity/BuildingNightWindows)")]
    [Tooltip("Shader BuildingNightWindows.shader. Priraď ho tu – Shader.Find sa " +
             "použije len ako núdzová záloha.")]
    [SerializeField] private Shader windowShader;

    [Header("Profily rozpoznania okien (podľa balíčka modelov)")]
    [SerializeField] private WindowProfile[] profiles = CreateDefaultProfiles();

    [Header("Sloty CityBuildingLibrary (Enabled + index profilu)")]
    [SerializeField]
    private SlotSetup[] lowSlots =
        { new SlotSetup(0), new SlotSetup(0), new SlotSetup(0), new SlotSetup(0), new SlotSetup(0) };
    [SerializeField]
    private SlotSetup[] mediumSlots =
        { new SlotSetup(1), new SlotSetup(1), new SlotSetup(1), new SlotSetup(1), new SlotSetup(1) };
    [SerializeField]
    private SlotSetup[] highSlots =
        { new SlotSetup(2), new SlotSetup(2), new SlotSetup(2), new SlotSetup(2), new SlotSetup(2) };

    [Header("Vzhľad okien v noci")]
    [SerializeField] private Color windowColorA = new Color(1.00f, 0.78f, 0.40f, 1f);
    [SerializeField] private Color windowColorB = new Color(1.00f, 0.92f, 0.70f, 1f);
    [Tooltip("Jas rozsvietených okien.")]
    [Range(0f, 8f)][SerializeField] private float windowIntensity = 2.2f;
    [Tooltip("Aký podiel okien svieti (0.75 = 75 %).")]
    [Range(0f, 1f)][SerializeField] private float litFraction = 0.75f;
    [Tooltip("Rozdiely jasu medzi oknami.")]
    [Range(0f, 1f)][SerializeField] private float brightnessVariation = 0.35f;
    [Tooltip("Pri stmievaní sa okná rozsvecujú postupne (0 = všetky naraz).")]
    [Range(0f, 0.95f)][SerializeField] private float stagger = 0.6f;
    [Tooltip("Farba zhasnutého skla v noci.")]
    [SerializeField] private Color unlitGlassNight = new Color(0.06f, 0.08f, 0.13f, 1f);
    [Range(0f, 1f)][SerializeField] private float unlitGlassDarken = 0.85f;

    [Header("Zapínanie / vypínanie okien")]
    [Tooltip("Podiel okien, ktoré občas zhasnú alebo sa rozsvietia (0 = okná sú stále).")]
    [Range(0f, 1f)][SerializeField] private float dynamicFraction = 0.5f;
    [Tooltip("Každé dynamické okno si raz za interval (náhodne v rozsahu min–max " +
             "sekúnd) znovu vyberie, či svieti.")]
    [Range(0.5f, 120f)][SerializeField] private float switchIntervalMin = 5f;
    [Range(0.5f, 120f)][SerializeField] private float switchIntervalMax = 10f;
    [Tooltip("Dĺžka plynulého prechodu pri zapnutí/vypnutí okna (s).")]
    [Range(0f, 2f)][SerializeField] private float switchFade = 0.3f;

    [Header("Ostatné")]
    [Tooltip("Náhoda pre každé okno / plochu modelu (vyžaduje Read/Write Enabled na FBX).")]
    [SerializeField] private bool perWindowVariation = true;
    [Tooltip("Ako často sa kontroluje, či pribudli budovy (sekundy).")]
    [Range(0.1f, 5f)][SerializeField] private float checkInterval = 0.5f;

    // ── Shader property IDs ─────────────────────────────────────────────
    static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ID_Color = Shader.PropertyToID("_Color");
    static readonly int ID_Glossiness = Shader.PropertyToID("_Glossiness");
    static readonly int ID_Metallic = Shader.PropertyToID("_Metallic");
    static readonly int ID_MetalMap = Shader.PropertyToID("_MetallicGlossMap");
    static readonly int ID_GlossMapScale = Shader.PropertyToID("_GlossMapScale");
    static readonly int ID_UseMetalMap = Shader.PropertyToID("_UseMetallicMap");
    static readonly int ID_EmissionMap = Shader.PropertyToID("_EmissionMap");
    static readonly int ID_Mode = Shader.PropertyToID("_WindowMode");
    static readonly int ID_UVRect = Shader.PropertyToID("_WindowUVRect");
    static readonly int ID_IdMap = Shader.PropertyToID("_WindowIdMap");
    static readonly int ID_EmisThreshold = Shader.PropertyToID("_EmissionThreshold");
    static readonly int ID_EmisRecolor = Shader.PropertyToID("_EmissionRecolor");
    static readonly int ID_ColA = Shader.PropertyToID("_WindowColorA");
    static readonly int ID_ColB = Shader.PropertyToID("_WindowColorB");
    static readonly int ID_Intensity = Shader.PropertyToID("_WindowIntensity");
    static readonly int ID_LitFraction = Shader.PropertyToID("_LitFraction");
    static readonly int ID_Variation = Shader.PropertyToID("_BrightnessVariation");
    static readonly int ID_DynFraction = Shader.PropertyToID("_DynamicFraction");
    static readonly int ID_SwitchMin = Shader.PropertyToID("_SwitchIntervalMin");
    static readonly int ID_SwitchMax = Shader.PropertyToID("_SwitchIntervalMax");
    static readonly int ID_SwitchFade = Shader.PropertyToID("_SwitchFade");
    static readonly int ID_Stagger = Shader.PropertyToID("_Stagger");
    static readonly int ID_UnlitGlass = Shader.PropertyToID("_UnlitGlassNight");
    static readonly int ID_UnlitDarken = Shader.PropertyToID("_UnlitGlassDarken");

    // ── Cache (zdieľané medzi všetkými budovami) ────────────────────────
    private struct MatKey : IEquatable<MatKey>
    {
        public Material src; public int profile;
        public bool Equals(MatKey o) => src == o.src && profile == o.profile;
        public override bool Equals(object o) => o is MatKey k && Equals(k);
        public override int GetHashCode() => (src != null ? src.GetHashCode() : 0) * 31 + profile;
    }

    private struct MeshKey : IEquatable<MeshKey>
    {
        public Mesh mesh; public int profile; public Texture tex;
        public bool Equals(MeshKey o) => mesh == o.mesh && profile == o.profile && tex == o.tex;
        public override bool Equals(object o) => o is MeshKey k && Equals(k);
        public override int GetHashCode() =>
            ((mesh != null ? mesh.GetHashCode() : 0) * 31 + profile) * 31 + (tex != null ? tex.GetHashCode() : 0);
    }

    private sealed class NightMat
    {
        public Material mat;
        public int profile;
        public Texture sourceTex;   // albedo (UV Rect / Texture Color) alebo emission (Emission Map)
    }

    private sealed class PixelData
    {
        public Color32[] px;
        public int w, h;
    }

    private readonly Dictionary<MatKey, NightMat> nightMaterials = new Dictionary<MatKey, NightMat>();
    private readonly Dictionary<Material, NightMat> nightByMaterial = new Dictionary<Material, NightMat>();
    private readonly Dictionary<MeshKey, Mesh> bakedMeshes = new Dictionary<MeshKey, Mesh>();
    private readonly HashSet<Mesh> ownBakedMeshes = new HashSet<Mesh>();
    private readonly Dictionary<long, Texture2D> idMaps = new Dictionary<long, Texture2D>();
    private readonly Dictionary<Texture, PixelData> pixelCache = new Dictionary<Texture, PixelData>();
    private readonly HashSet<UnityEngine.Object> warned = new HashSet<UnityEngine.Object>();

    private float nextCheck;
    private int lastSignature = int.MinValue;
    private bool warnedNoShader;

    // =====================================================================
    // PREDVOLENÉ PROFILY
    // =====================================================================

    private static WindowProfile[] CreateDefaultProfiles()
    {
        return new[]
        {
            new WindowProfile
            {
                name = "0 – Low Poly Simple Urban (tex.png)",
                detection = WindowDetection.UVRect,
                uvRect = new Vector4(0.1016f, 0.9609f, 0.0391f, 0.0391f)
            },
            new WindowProfile
            {
                name = "1 – SimplePoly City (farba skla)",
                detection = WindowDetection.TextureColor,
                glassColors = new[]
                {
                    new Color(79f / 255f, 82f / 255f, 84f / 255f, 1f), // Sky_big / Sky_small
                    new Color(64f / 255f, 64f / 255f, 64f / 255f, 1f)  // Residential
                },
                colorTolerance = 0.04f,
                minWindowUVSize = 0.035f
            },
            new WindowProfile
            {
                name = "2 – Skyscrapers (emission mapa)",
                detection = WindowDetection.EmissionMap,
                emissionThreshold = 0.06f,
                emissionRecolor = 0.35f
            }
        };
    }

    [ContextMenu("Reset profiles to defaults")]
    private void ResetProfiles()
    {
        profiles = CreateDefaultProfiles();
    }

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[CityNightWindows] V scéne je viac inštancií – ponechávam prvú.");
            enabled = false;
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;

        foreach (var nm in nightMaterials.Values)
            if (nm.mat != null) Destroy(nm.mat);
        nightMaterials.Clear();
        nightByMaterial.Clear();

        foreach (var m in ownBakedMeshes)
            if (m != null) Destroy(m);
        ownBakedMeshes.Clear();
        bakedMeshes.Clear();

        foreach (var t in idMaps.Values)
            if (t != null) Destroy(t);
        idMaps.Clear();
        pixelCache.Clear();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + checkInterval;

        int sig = Signature();
        if (sig == lastSignature) return;
        lastSignature = sig;

        Rescan();

        // Pixely textúr sú potrebné len počas spracovania – uvoľníme pamäť.
        pixelCache.Clear();
    }

    private void OnValidate()
    {
        if (switchIntervalMax < switchIntervalMin) switchIntervalMax = switchIntervalMin;

        // Ladenie v Inspectore počas hry – prenesie sa hneď do materiálov.
        if (!Application.isPlaying) return;
        foreach (var nm in nightMaterials.Values)
            if (nm.mat != null) ApplyParams(nm.mat, nm.profile);
    }

    // =====================================================================
    // DETEKCIA ZMIEN
    // =====================================================================

    /// <summary>Lacný „odtlačok“ miest – zmení sa po generovaní aj po Load.</summary>
    private int Signature()
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

                int n = c.buildings.Count;
                sig = sig * 31 + n;
                if (n > 0)
                {
                    var first = c.buildings[0];
                    var last = c.buildings[n - 1];
                    sig = sig * 31 + (first != null ? first.GetHashCode() : 0);
                    sig = sig * 31 + (last != null ? last.GetHashCode() : 0);
                }
            }
            return sig;
        }
    }

    /// <summary>Vynúti nové prejdenie budov (napr. po ručnej zmene miest).</summary>
    [ContextMenu("Rescan buildings")]
    public void MarkDirty()
    {
        lastSignature = int.MinValue;
        nextCheck = 0f;
    }

    // =====================================================================
    // SPRACOVANIE BUDOV
    // =====================================================================

    private void Rescan()
    {
        var cm = CityManager.instance;
        if (cm == null || cm.Cities == null) return;

        Shader shader = ResolveShader();
        if (shader == null) return;

        CityBuildingLibrary lib = CityBuildingLibrary.instance != null
            ? CityBuildingLibrary.instance
            : FindAnyObjectByType<CityBuildingLibrary>();
        if (lib == null) return;

        int applied = 0;
        for (int ci = 0; ci < cm.Cities.Count; ci++)
        {
            var city = cm.Cities[ci];
            if (city == null) continue;

            int n = Mathf.Min(city.buildings.Count, city.buildingRecords.Count);
            for (int i = 0; i < n; i++)
            {
                var rec = city.buildingRecords[i];
                SlotSetup slot = GetSlot(rec.tier, rec.variant);
                if (slot == null || !slot.enabled) continue;
                if (profiles == null || slot.profile < 0 || slot.profile >= profiles.Length) continue;

                // Len budovy postavené z PREFABU (prázdny slot = primitíva → nechať).
                if (lib.GetBuildingPrefab(rec.tier, rec.variant) == null) continue;

                GameObject go = city.buildings[i];
                if (go == null) continue;

                if (ApplyToBuilding(go, shader, slot.profile)) applied++;
            }
        }

        if (applied > 0)
            Debug.Log($"[CityNightWindows] Nočné okná nastavené na {applied} budovách.");
    }

    private SlotSetup GetSlot(CityBuildingLibrary.BuildingTier tier, int variant)
    {
        SlotSetup[] arr;
        switch (tier)
        {
            case CityBuildingLibrary.BuildingTier.Low: arr = lowSlots; break;
            case CityBuildingLibrary.BuildingTier.Medium: arr = mediumSlots; break;
            case CityBuildingLibrary.BuildingTier.High: arr = highSlots; break;
            default: return null;
        }
        if (arr == null || variant < 0 || variant >= arr.Length) return null;
        return arr[variant];
    }

    private Shader ResolveShader()
    {
        if (windowShader != null) return windowShader;

        windowShader = Shader.Find("RetroCity/BuildingNightWindows");
        if (windowShader == null && !warnedNoShader)
        {
            warnedNoShader = true;
            Debug.LogError("[CityNightWindows] Nie je priradený shader! Pretiahni " +
                           "BuildingNightWindows.shader do poľa 'Window Shader'.");
        }
        return windowShader;
    }

    /// <summary>Vymení materiály (a prípadne mesh) jednej budovy. True = niečo sa zmenilo.</summary>
    private bool ApplyToBuilding(GameObject go, Shader shader, int profileIndex)
    {
        bool changed = false;
        var renderers = go.GetComponentsInChildren<MeshRenderer>(true);

        for (int r = 0; r < renderers.Length; r++)
        {
            var rend = renderers[r];
            if (rend == null) continue;

            // ── Materiály ──
            Material[] mats = rend.sharedMaterials;
            bool matsChanged = false;
            Texture meshTex = null;   // textúra pre spracovanie mesh-u (Texture Color)
            for (int m = 0; m < mats.Length; m++)
            {
                Material src = mats[m];
                if (src == null) continue;

                NightMat nm;
                if (src.shader == shader)
                {
                    if (!nightByMaterial.TryGetValue(src, out nm)) continue;
                }
                else
                {
                    nm = GetNightMaterial(src, shader, profileIndex);
                    mats[m] = nm.mat;
                    matsChanged = true;
                }
                if (meshTex == null) meshTex = nm.sourceTex;
            }
            if (matsChanged)
            {
                rend.sharedMaterials = mats;
                changed = true;
            }

            // ── Mesh s náhodou pre každé okno / plochu ──
            if (!perWindowVariation) continue;
            var mf = rend.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (ownBakedMeshes.Contains(mf.sharedMesh)) continue;

            Mesh baked = GetBakedMesh(mf.sharedMesh, profileIndex, meshTex);
            if (baked != mf.sharedMesh)
            {
                mf.sharedMesh = baked;
                changed = true;
            }
        }
        return changed;
    }

    // =====================================================================
    // MATERIÁL
    // =====================================================================

    private NightMat GetNightMaterial(Material src, Shader shader, int profileIndex)
    {
        var key = new MatKey { src = src, profile = profileIndex };
        if (nightMaterials.TryGetValue(key, out NightMat cached) && cached.mat != null)
            return cached;

        WindowProfile p = profiles[profileIndex];
        var m = new Material(shader) { name = src.name + " (NightWindows)" };

        // ── Albedo ──
        Texture albedo = null;
        Vector2 scale = Vector2.one, offset = Vector2.zero;
        if (src.HasProperty(ID_MainTex))
        {
            albedo = src.GetTexture(ID_MainTex);
            scale = src.GetTextureScale(ID_MainTex);
            offset = src.GetTextureOffset(ID_MainTex);
        }
        else if (src.HasProperty("_BaseMap"))
        {
            albedo = src.GetTexture("_BaseMap");
        }
        if (p.textureOverride != null && p.detection != WindowDetection.EmissionMap)
            albedo = p.textureOverride;

        m.SetTexture(ID_MainTex, albedo);
        m.SetTextureScale(ID_MainTex, scale);
        m.SetTextureOffset(ID_MainTex, offset);

        // ── Farba, metallic, smoothness ──
        if (src.HasProperty(ID_Color)) m.SetColor(ID_Color, src.GetColor(ID_Color));
        else if (src.HasProperty("_BaseColor")) m.SetColor(ID_Color, src.GetColor("_BaseColor"));

        if (src.HasProperty(ID_Glossiness)) m.SetFloat(ID_Glossiness, src.GetFloat(ID_Glossiness));
        else if (src.HasProperty("_Smoothness")) m.SetFloat(ID_Glossiness, src.GetFloat("_Smoothness"));

        if (src.HasProperty(ID_Metallic)) m.SetFloat(ID_Metallic, src.GetFloat(ID_Metallic));

        Texture metalMap = src.HasProperty(ID_MetalMap) ? src.GetTexture(ID_MetalMap) : null;
        if (metalMap != null)
        {
            m.SetTexture(ID_MetalMap, metalMap);
            m.SetFloat(ID_UseMetalMap, 1f);
            if (src.HasProperty(ID_GlossMapScale)) m.SetFloat(ID_GlossMapScale, src.GetFloat(ID_GlossMapScale));
        }
        else
        {
            m.SetFloat(ID_UseMetalMap, 0f);
        }

        // ── Zdroj okien podľa profilu ──
        Texture sourceTex = albedo;
        if (p.detection == WindowDetection.EmissionMap)
        {
            Texture emis = p.textureOverride;
            if (emis == null && src.HasProperty(ID_EmissionMap)) emis = src.GetTexture(ID_EmissionMap);
            m.SetTexture(ID_EmissionMap, emis);
            sourceTex = emis;

            if (emis == null && warned.Add(src))
                Debug.LogWarning($"[CityNightWindows] Materiál '{src.name}' nemá emission mapu – " +
                                 "okná profilu Emission Map nebudú svietiť.");
        }

        if (sourceTex == null && warned.Add(src))
            Debug.LogWarning($"[CityNightWindows] Materiál '{src.name}' nemá textúru – " +
                             "priraď ju do 'Texture Override' v profile.");

        // Mapa okien (ID + maska) – len pre Texture Color / Emission Map
        if (p.detection != WindowDetection.UVRect && sourceTex != null)
        {
            Texture2D idMap = GetIdMap(sourceTex, profileIndex);
            if (idMap != null) m.SetTexture(ID_IdMap, idMap);
        }

        var nm = new NightMat { mat = m, profile = profileIndex, sourceTex = sourceTex };
        ApplyParams(m, profileIndex);
        nightMaterials[key] = nm;
        nightByMaterial[m] = nm;
        return nm;
    }

    private void ApplyParams(Material m, int profileIndex)
    {
        WindowProfile p = (profiles != null && profileIndex >= 0 && profileIndex < profiles.Length)
            ? profiles[profileIndex] : null;
        if (p == null) return;

        m.SetFloat(ID_Mode, (float)p.detection);
        m.SetVector(ID_UVRect, p.uvRect);
        m.SetFloat(ID_EmisThreshold, p.emissionThreshold);
        m.SetFloat(ID_EmisRecolor, p.emissionRecolor);

        m.SetColor(ID_ColA, windowColorA);
        m.SetColor(ID_ColB, windowColorB);
        m.SetFloat(ID_Intensity, windowIntensity * p.intensityScale);
        m.SetFloat(ID_LitFraction, p.overrideLitFraction ? p.litFraction : litFraction);
        m.SetFloat(ID_Variation, brightnessVariation);
        m.SetFloat(ID_DynFraction, dynamicFraction);
        m.SetFloat(ID_SwitchMin, switchIntervalMin);
        m.SetFloat(ID_SwitchMax, Mathf.Max(switchIntervalMin, switchIntervalMax));
        m.SetFloat(ID_SwitchFade, switchFade);
        m.SetFloat(ID_Stagger, stagger);
        m.SetColor(ID_UnlitGlass, unlitGlassNight);
        m.SetFloat(ID_UnlitDarken, unlitGlassDarken);
    }

    // =====================================================================
    // PIXELY TEXTÚRY (cez GPU – textúra nemusí mať Read/Write)
    // =====================================================================

    private PixelData GetPixels(Texture tex)
    {
        if (tex == null) return null;
        if (pixelCache.TryGetValue(tex, out PixelData cached)) return cached;

        int w = tex.width, h = tex.height;
        if (w <= 0 || h <= 0) return null;

        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        tmp.Apply(false, false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var data = new PixelData { px = tmp.GetPixels32(), w = w, h = h };
        Destroy(tmp);

        pixelCache[tex] = data;
        return data;
    }

    private bool IsWindowPixel(Color32 c, WindowProfile p)
    {
        if (p.detection == WindowDetection.EmissionMap)
        {
            int lum = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return lum > p.emissionThreshold * 255f;
        }

        if (p.glassColors == null) return false;
        float tol = p.colorTolerance * 255f + 0.5f;
        for (int i = 0; i < p.glassColors.Length; i++)
        {
            Color32 k = p.glassColors[i];
            if (Mathf.Abs(c.r - k.r) <= tol && Mathf.Abs(c.g - k.g) <= tol && Mathf.Abs(c.b - k.b) <= tol)
                return true;
        }
        return false;
    }

    // =====================================================================
    // MAPA OKIEN – R = náhodné číslo okna (súvislá oblasť), G = maska
    // =====================================================================

    private Texture2D GetIdMap(Texture src, int profileIndex)
    {
        long key = ((long)src.GetHashCode() << 8) ^ profileIndex;
        if (idMaps.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

        PixelData pd = GetPixels(src);
        if (pd == null) return null;

        WindowProfile p = profiles[profileIndex];
        int w = pd.w, h = pd.h, n = w * h;

        var mask = new bool[n];
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            mask[i] = IsWindowPixel(pd.px[i], p);
            if (mask[i]) count++;
        }

        if (count == 0)
        {
            if (warned.Add(src))
                Debug.LogWarning($"[CityNightWindows] V textúre '{src.name}' sa nenašli okná " +
                                 $"(profil '{p.name}') – skontroluj farby / prah.");
        }

        // Súvislé oblasti (4-susednosť) → každé okno / tabuľa jedno ID
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (!mask[i]) continue;
                if (x > 0 && mask[i - 1]) Union(parent, i, i - 1);
                if (y > 0 && mask[i - w]) Union(parent, i, i - w);
            }
        }

        var rng = new System.Random(unchecked(w * 7919 + h * 104729 + count));
        var rootId = new Dictionary<int, byte>();
        var level0 = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            if (!mask[i]) { level0[i] = new Color32(0, 0, 0, 255); continue; }

            int root = Find(parent, i);
            if (!rootId.TryGetValue(root, out byte id))
            {
                id = (byte)rng.Next(1, 256);   // 1..255 (0 = bez okna)
                rootId[root] = id;
            }
            level0[i] = new Color32(id, 255, 0, 255);
        }

        // Textúra s VLASTNÝMI mipmapami: G = priemer masky, R = ID prvého okna
        // v bloku. Point filter → ID sa nikdy nezmieša, maska sa v diaľke nemrví.
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
        {
            name = src.name + "_WindowIdMap",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            anisoLevel = 0
        };

        Color32[] cur = level0;
        int cw = w, ch = h;
        tex.SetPixels32(cur, 0);
        for (int mip = 1; mip < tex.mipmapCount; mip++)
        {
            int nw = Mathf.Max(1, cw / 2), nh = Mathf.Max(1, ch / 2);
            var next = new Color32[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Mathf.Min(x * 2, cw - 1), x1 = Mathf.Min(x * 2 + 1, cw - 1);
                    int y0 = Mathf.Min(y * 2, ch - 1), y1 = Mathf.Min(y * 2 + 1, ch - 1);
                    Color32 a = cur[y0 * cw + x0], b = cur[y0 * cw + x1];
                    Color32 c = cur[y1 * cw + x0], d = cur[y1 * cw + x1];

                    byte id = a.r != 0 ? a.r : b.r != 0 ? b.r : c.r != 0 ? c.r : d.r;
                    byte g = (byte)((a.g + b.g + c.g + d.g + 2) / 4);
                    next[y * nw + x] = new Color32(id, g, 0, 255);
                }
            }
            tex.SetPixels32(next, mip);
            cur = next; cw = nw; ch = nh;
        }
        tex.Apply(false, true);   // bez prepočtu mip, CPU kópiu zahodiť

        idMaps[key] = tex;
        return tex;
    }

    // =====================================================================
    // MESH – náhodné číslo pre každé okno / plochu (UV3.x)
    // =====================================================================

    private bool InRect(Vector2 uv, Vector4 r)
    {
        const float e = 1e-4f;
        return uv.x >= r.x - e && uv.x <= r.x + r.z + e
            && uv.y >= r.y - e && uv.y <= r.y + r.w + e;
    }

    /// <summary>
    /// Vráti kópiu mesh-u, kde každé okno (UV Rect / Texture Color) alebo
    /// každá súvislá plocha (Emission Map) má v UV3.x vlastné náhodné číslo
    /// 0.02–1. Pri nečitateľnom mesh-i (bez Read/Write) vráti pôvodný mesh.
    /// </summary>
    private Mesh GetBakedMesh(Mesh src, int profileIndex, Texture tex)
    {
        WindowProfile p = profiles[profileIndex];
        if (p.detection != WindowDetection.TextureColor) tex = null;   // textúra potrebná len tu

        var key = new MeshKey { mesh = src, profile = profileIndex, tex = tex };
        if (bakedMeshes.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;

        if (!src.isReadable)
        {
            if (warned.Add(src))
                Debug.LogWarning($"[CityNightWindows] Mesh '{src.name}' nemá zapnuté " +
                                 "Read/Write Enabled (Import Settings FBX → Model). Okná " +
                                 "budú svietiť po fasádach, nie jednotlivo.");
            bakedMeshes[key] = src;
            return src;
        }

        int vc = src.vertexCount;
        var uvs = new List<Vector2>(vc);
        src.GetUVs(0, uvs);
        if (uvs.Count != vc)
        {
            bakedMeshes[key] = src;
            return src;
        }
        Vector3[] pos = src.vertices;

        PixelData pd = (p.detection == WindowDetection.TextureColor) ? GetPixels(tex) : null;

        var parent = new int[vc];
        for (int i = 0; i < vc; i++) parent[i] = i;
        var used = new bool[vc];

        // Rovnaká pozícia = to isté okno (sklo + ostenie okna majú rôzne UV,
        // teda rôzne vrcholy – spojíme ich cez pozíciu, ale len v rámci okien).
        var byPos = new Dictionary<Vector3Int, int>();
        bool mergeByPosition = p.detection != WindowDetection.EmissionMap;

        for (int s = 0; s < src.subMeshCount; s++)
        {
            if (src.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] t = src.GetTriangles(s);
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                int a = t[k], b = t[k + 1], c = t[k + 2];
                if (!IsWindowTriangle(p, pd, uvs[a], uvs[b], uvs[c])) continue;

                Union(parent, a, b);
                Union(parent, a, c);
                used[a] = used[b] = used[c] = true;

                if (!mergeByPosition) continue;
                MergeByPos(byPos, parent, pos, a);
                MergeByPos(byPos, parent, pos, b);
                MergeByPos(byPos, parent, pos, c);
            }
        }

        var rng = new System.Random(unchecked(vc * 7919 + 17 + profileIndex * 131));
        var compRandom = new Dictionary<int, float>();
        var ids = new List<Vector2>(vc);
        for (int i = 0; i < vc; i++)
        {
            // x = náhoda (0.01 = spracované, nie okno), y = 1 ak je vrchol súčasťou okna
            if (!used[i]) { ids.Add(new Vector2(0.01f, 0f)); continue; }

            int root = Find(parent, i);
            if (!compRandom.TryGetValue(root, out float f))
            {
                f = 0.02f + 0.98f * (float)rng.NextDouble();
                compRandom[root] = f;
            }
            ids.Add(new Vector2(f, 1f));
        }

        if (compRandom.Count == 0)
        {
            if (warned.Add(src))
                Debug.LogWarning($"[CityNightWindows] V mesh-i '{src.name}' sa nenašli okná " +
                                 $"(profil '{p.name}').");
            bakedMeshes[key] = src;
            return src;
        }

        Mesh m = Instantiate(src);
        m.name = src.name + "_NightWindows";
        m.SetUVs(3, ids);

        bakedMeshes[key] = m;
        ownBakedMeshes.Add(m);
        return m;
    }

    private bool IsWindowTriangle(WindowProfile p, PixelData pd, Vector2 a, Vector2 b, Vector2 c)
    {
        switch (p.detection)
        {
            case WindowDetection.UVRect:
                return InRect(a, p.uvRect) && InRect(b, p.uvRect) && InRect(c, p.uvRect);

            case WindowDetection.TextureColor:
                if (pd == null) return false;
                Vector2 mn = Vector2.Min(a, Vector2.Min(b, c));
                Vector2 mx = Vector2.Max(a, Vector2.Max(b, c));
                if (Mathf.Min(mx.x - mn.x, mx.y - mn.y) < p.minWindowUVSize) return false;
                Vector2 uv = (a + b + c) / 3f;
                int x = Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * pd.w);
                int y = Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * pd.h);
                x = Mathf.Clamp(x, 0, pd.w - 1);
                y = Mathf.Clamp(y, 0, pd.h - 1);
                return IsWindowPixel(pd.px[y * pd.w + x], p);

            default:
                return true;   // Emission Map: každá súvislá plocha dostane vlastnú náhodu
        }
    }

    private static void MergeByPos(Dictionary<Vector3Int, int> byPos, int[] parent, Vector3[] pos, int v)
    {
        Vector3 q = pos[v] * 1000f;
        var key = new Vector3Int(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), Mathf.RoundToInt(q.z));
        if (byPos.TryGetValue(key, out int other)) Union(parent, v, other);
        else byPos[key] = v;
    }

    private static int Find(int[] p, int x)
    {
        while (p[x] != x)
        {
            p[x] = p[p[x]];
            x = p[x];
        }
        return x;
    }

    private static void Union(int[] p, int a, int b)
    {
        int ra = Find(p, a), rb = Find(p, b);
        if (ra != rb) p[rb] = ra;
    }
}
