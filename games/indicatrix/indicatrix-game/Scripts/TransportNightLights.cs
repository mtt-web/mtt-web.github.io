using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TransportNightLights
/// ─────────────────────────────────────────────────────────────────────────
/// NOČNÉ SVETLÁ pre DEPÁ, STANICE (rail aj road), VLAKY a CESTNÉ VOZIDLÁ.
/// Všetko je SIMULOVANÉ (žiadne Light komponenty) – výkonovo zanedbateľné.
///
///   • Depá a stanice: svietiace okná (časť okien, občas zhasnú/rozsvietia
///     sa) + svetelné kruhy na zemi okolo budovy (lampy).
///   • Vlaky: svietiace okná (osobné vozne, kabíny), svetlomety lokomotívy
///     na modeli + svetelný kužeľ na koľajach pred lokomotívou.
///   • Cestné vozidlá: okná kabíny, predné svetlá, červené zadné svetlá
///     + svetelný kužeľ na ceste pred vozidlom.
///
/// Kde sú na modeli okná a svetlá, určujú PROFILY: pravidlá „UV obdĺžnik
/// v textúre + farba“. Zo zdrojovej textúry sa vyrobí mapa svetiel
/// (RAZ na textúru, cez GPU – bez Read/Write). Preto fungujú aj modely,
/// ktoré sú len farebnou kópiou iného modelu: rozloženie textúry (UV) je
/// rovnaké, líši sa len farba.
///
///   Profil 0 – Synty Simple Trains (SimpleTrains_Texture_01/02/03):
///              lokomotívy, vagóny, SM_Bld_Station_Large, SM_Bld_Warehouse
///   Profil 1 – Vehicles (CoalTruck … ElectronicsVan, 512×512 textúry)
///
/// Nič v existujúcich skriptoch sa NEMENÍ. Komponent si údaje číta cez
/// verejné API: IndicatrixAPI.GetTileByIndexAny (depá, stanice),
/// TrainSystem.GetAllDepotCoords / GetTrain, VehicleSystem.GetAllDepotCoords
/// / GetVehicle. Model depa/stanice nájde podľa mena „TileModel_x_z“
/// (tak ho pomenúva IndicatrixAPI.InstantiateTileModel).
///
/// VOLITEĽNE (Darken Rails And Roads): koľaje a cesty sú v noci tmavé a
/// „odhalí“ ich len svetlo – kužele svetlometov (a kruhy pri depách/staniciach).
///
/// SHADERY sa priraďujú cez Inspector (NightLightFeatures, NightLightPool,
/// NightRoadDarken).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[DisallowMultipleComponent]
public class TransportNightLights : MonoBehaviour
{
    public static TransportNightLights instance;

    // =====================================================================
    // TYPY PRE INSPECTOR
    // =====================================================================

    public enum FeatureKind
    {
        Window = 0,     // okno (G)
        Headlight = 1,  // predné svetlo (B)
        Taillight = 2   // zadné svetlo (A)
    }

    [Serializable]
    public class FeatureRule
    {
        public string name = "Pravidlo";
        public FeatureKind kind = FeatureKind.Window;
        [Tooltip("Obdĺžnik v textúre (UV 0–1: x, y, šírka, výška; y od spodku textúry).")]
        public Vector4 uvRect = new Vector4(0, 0, 1, 1);
        [Tooltip("Farby, ktoré v obdĺžniku patria k svetlu. Prázdne = celý obdĺžnik.")]
        public Color[] colors = new Color[0];
        [Tooltip("Tolerancia farby (0.05 ≈ 13/255, pokryje aj kompresiu textúry).")]
        [Range(0f, 0.3f)] public float tolerance = 0.05f;
    }

    [Serializable]
    public class FeatureProfile
    {
        public string name = "Profil";
        public FeatureRule[] rules = new FeatureRule[0];
    }

    [Serializable]
    public class PoolSpot
    {
        [Tooltip("Pozícia voči pôdorysu modelu: -1..1 v osi X a Z modelu " +
                 "(1 = okraj modelu, >1 = mimo modelu).")]
        public Vector2 normalizedXZ;
        [Tooltip("Polomer svetelného kruhu v dlaždiciach.")]
        [Range(0.05f, 1.5f)] public float radius = 0.3f;

        public PoolSpot() { }
        public PoolSpot(float x, float z, float r) { normalizedXZ = new Vector2(x, z); radius = r; }
    }

    // =====================================================================
    // INSPECTOR
    // =====================================================================

    [Header("Shadery (pretiahni sem)")]
    [Tooltip("RetroCity/NightLightFeatures (NightLightFeatures.shader)")]
    [SerializeField] private Shader featureShader;
    [Tooltip("RetroCity/NightLightPool (NightLightPool.shader)")]
    [SerializeField] private Shader poolShader;
    [Tooltip("RetroCity/NightRoadDarken (NightRoadDarken.shader) – len pre Darken Rails And Roads.")]
    [SerializeField] private Shader roadDarkenShader;

    [Header("Čo zapnúť")]
    [SerializeField] private bool depotsAndStations = true;
    [SerializeField] private bool trains = true;
    [SerializeField] private bool roadVehicles = true;

    [Header("Profily svetiel (podľa balíčka modelov)")]
    [SerializeField] private FeatureProfile[] profiles = CreateDefaultProfiles();
    [Tooltip("Profil pre depá a stanice (rail aj road).")]
    [SerializeField] private int depotStationProfile = 0;
    [Tooltip("Profil pre lokomotívy a vagóny.")]
    [SerializeField] private int trainProfile = 0;
    [Tooltip("Profil pre cestné vozidlá.")]
    [SerializeField] private int vehicleProfile = 1;

    [Header("Okná – depá a stanice")]
    [SerializeField] private Color windowColorA = new Color(1.00f, 0.78f, 0.40f, 1f);
    [SerializeField] private Color windowColorB = new Color(1.00f, 0.92f, 0.70f, 1f);
    [Range(0f, 8f)][SerializeField] private float buildingWindowIntensity = 2.2f;
    [Tooltip("Aký podiel okien svieti (0.75 = 75 %).")]
    [Range(0f, 1f)][SerializeField] private float buildingLitFraction = 0.75f;
    [Tooltip("Podiel okien, ktoré občas zhasnú alebo sa rozsvietia.")]
    [Range(0f, 1f)][SerializeField] private float buildingDynamicFraction = 0.5f;
    [Range(0.5f, 120f)][SerializeField] private float switchIntervalMin = 5f;
    [Range(0.5f, 120f)][SerializeField] private float switchIntervalMax = 10f;

    [Header("Okná – vlaky a vozidlá (svietia stále)")]
    [SerializeField] private Color vehicleWindowColor = new Color(1.00f, 0.88f, 0.62f, 1f);
    [Range(0f, 8f)][SerializeField] private float vehicleWindowIntensity = 1.8f;
    [Range(0f, 1f)][SerializeField] private float vehicleLitFraction = 1f;

    [Header("Svetlomety a zadné svetlá (na modeli)")]
    [SerializeField] private Color headlightColor = new Color(1.00f, 0.95f, 0.80f, 1f);
    [Range(0f, 10f)][SerializeField] private float headlightIntensity = 4f;
    [SerializeField] private Color taillightColor = new Color(1.00f, 0.08f, 0.04f, 1f);
    [Range(0f, 10f)][SerializeField] private float taillightIntensity = 2.5f;

    [Header("Svetelný kužeľ pred lokomotívou / vozidlom")]
    [SerializeField] private bool trainBeams = true;
    [SerializeField] private bool vehicleBeams = true;
    [SerializeField] private Color beamColor = new Color(1.00f, 0.92f, 0.72f, 1f);
    [Range(0f, 4f)][SerializeField] private float beamIntensity = 0.9f;
    [Tooltip("Dĺžka / šírka kužeľa pred lokomotívou (dlaždice).")]
    [SerializeField] private Vector2 trainBeamLengthWidth = new Vector2(1.3f, 0.75f);
    [Tooltip("Dĺžka / šírka kužeľa pred vozidlom (dlaždice).")]
    [SerializeField] private Vector2 vehicleBeamLengthWidth = new Vector2(0.75f, 0.45f);

    [Header("Svetelné kruhy na zemi – depá a stanice")]
    [SerializeField] private bool groundPools = true;
    [SerializeField] private Color poolColor = new Color(1.00f, 0.82f, 0.55f, 1f);
    [Range(0f, 4f)][SerializeField] private float poolIntensity = 0.8f;
    [Tooltip("Kruhy okolo STANICE. Model stanice má dlhú os X, nástupištia po stranách ±Z.")]
    [SerializeField]
    private PoolSpot[] stationPools =
    {
        new PoolSpot(-0.55f,  1.45f, 0.26f), new PoolSpot(0.55f,  1.45f, 0.26f),
        new PoolSpot(-0.55f, -1.45f, 0.26f), new PoolSpot(0.55f, -1.45f, 0.26f)
    };
    [Tooltip("Kruhy okolo DEPA. Model depa (Warehouse) má vráta na strane +Z.")]
    [SerializeField]
    private PoolSpot[] depotPools =
    {
        new PoolSpot(0f, 1.25f, 0.36f),
        new PoolSpot(-1.6f, 0.3f, 0.18f), new PoolSpot(1.6f, 0.3f, 0.18f)
    };

    [Header("Koľaje a cesty v noci – osvetlené LEN svetlometmi")]
    [Tooltip("ZAPNUTÉ = koľaje a cesty (vrátane priecestí) sú v noci tmavé a vidno " +
             "ich len v kuželi svetlometov vlakov/vozidiel (a v kruhoch pri depách " +
             "a staniciach, ak je zapnuté Pools Reveal Roads). Dá sa prepínať počas hry.")]
    [SerializeField] private bool darkenRailsAndRoads = false;
    [Tooltip("Ako tmavé sú koľaje a cesty mimo svetla (1 = úplne čierne).")]
    [Range(0f, 1f)][SerializeField] private float railRoadDarkness = 0.95f;
    [Tooltip("Ako silno kužeľ svetlometu rozjasní koľaj/cestu (1 = jas ako v noci bez " +
             "stmavenia; viac ako 1 = ešte jasnejšie, len s HDR kamerou).")]
    [Range(0f, 6f)][SerializeField] private float beamRevealStrength = 2.5f;
    [Tooltip("Aj svetelné kruhy pri depách a staniciach odhalia koľaje a cesty.")]
    [SerializeField] private bool poolsRevealRoads = true;
    [Range(0f, 6f)][SerializeField] private float poolRevealStrength = 1.5f;
    [Tooltip("Rozlíšenie mapy svetla nad celou mapou (px). 2048 = 8 px na dlaždicu.")]
    [SerializeField] private int lightMapResolution = 2048;

    [Header("Ostatné")]
    [Tooltip("Ako často sa hľadajú nové vlaky a vozidlá (sekundy).")]
    [Range(0.1f, 5f)][SerializeField] private float scanInterval = 0.5f;
    [Tooltip("Veľkosť mapy v dlaždiciach (IndicatrixAPI GRID_SIZE).")]
    [SerializeField] private int gridSize = 256;
    [Tooltip("Koľko riadkov mapy sa prezrie za snímok pri hľadaní depí a staníc.")]
    [Range(4, 256)][SerializeField] private int gridRowsPerFrame = 32;

    // =====================================================================
    // SHADER IDs
    // =====================================================================

    static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ID_Color = Shader.PropertyToID("_Color");
    static readonly int ID_Glossiness = Shader.PropertyToID("_Glossiness");
    static readonly int ID_Metallic = Shader.PropertyToID("_Metallic");
    static readonly int ID_MetalMap = Shader.PropertyToID("_MetallicGlossMap");
    static readonly int ID_GlossMapScale = Shader.PropertyToID("_GlossMapScale");
    static readonly int ID_UseMetalMap = Shader.PropertyToID("_UseMetallicMap");
    static readonly int ID_FeatureMap = Shader.PropertyToID("_FeatureMap");
    static readonly int ID_StaticSeed = Shader.PropertyToID("_StaticSeed");
    static readonly int ID_ColA = Shader.PropertyToID("_WindowColorA");
    static readonly int ID_ColB = Shader.PropertyToID("_WindowColorB");
    static readonly int ID_WinIntensity = Shader.PropertyToID("_WindowIntensity");
    static readonly int ID_LitFraction = Shader.PropertyToID("_LitFraction");
    static readonly int ID_DynFraction = Shader.PropertyToID("_DynamicFraction");
    static readonly int ID_SwitchMin = Shader.PropertyToID("_SwitchIntervalMin");
    static readonly int ID_SwitchMax = Shader.PropertyToID("_SwitchIntervalMax");
    static readonly int ID_HeadColor = Shader.PropertyToID("_HeadlightColor");
    static readonly int ID_HeadIntensity = Shader.PropertyToID("_HeadlightIntensity");
    static readonly int ID_TailColor = Shader.PropertyToID("_TaillightColor");
    static readonly int ID_TailIntensity = Shader.PropertyToID("_TaillightIntensity");
    static readonly int ID_PoolColor = Shader.PropertyToID("_Color");
    static readonly int ID_PoolIntensity = Shader.PropertyToID("_Intensity");
    static readonly int ID_PoolShape = Shader.PropertyToID("_Shape");
    static readonly int ID_RevealMode = Shader.PropertyToID("_RevealMode");
    static readonly int ID_RoadDarkness = Shader.PropertyToID("_Darkness");
    static readonly int ID_UseAlpha = Shader.PropertyToID("_UseAlpha");
    static readonly int ID_NightAmount = Shader.PropertyToID("_CityNightAmount");
    static readonly int ID_LightMap = Shader.PropertyToID("_NightLightMap");
    static readonly int ID_LightMapParams = Shader.PropertyToID("_NightLightMapParams");

    const string BeamName = "NightHeadlightBeam";
    const string PoolName = "NightLightPool";
    const string RevealName = "NightLightReveal";
    const string RoadDarkName = "NightRoadDark";

    // =====================================================================
    // STAV / CACHE
    // =====================================================================

    enum Usage { Building = 0, Moving = 1 }

    private struct MatKey : IEquatable<MatKey>
    {
        public Material src; public int profile; public int usage;
        public bool Equals(MatKey o) => src == o.src && profile == o.profile && usage == o.usage;
        public override bool Equals(object o) => o is MatKey k && Equals(k);
        public override int GetHashCode() =>
            ((src != null ? src.GetHashCode() : 0) * 31 + profile) * 31 + usage;
    }

    private sealed class NightMat { public Material mat; public int usage; }

    private sealed class SiteEntry
    {
        public GameObject model;
        public bool isStation;
        public float retryAt;
        public int seenCycle;
    }

    private readonly Dictionary<MatKey, NightMat> nightMaterials = new Dictionary<MatKey, NightMat>();
    private readonly Dictionary<long, Texture2D> featureMaps = new Dictionary<long, Texture2D>();
    private readonly HashSet<UnityEngine.Object> warned = new HashSet<UnityEngine.Object>();
    private readonly HashSet<GameObject> processedMovers = new HashSet<GameObject>();
    private readonly Dictionary<int, SiteEntry> sites = new Dictionary<int, SiteEntry>();
    private readonly Dictionary<int, SiteEntry> roads = new Dictionary<int, SiteEntry>();
    private readonly Dictionary<string, GameObject> rootIndex = new Dictionary<string, GameObject>();
    private readonly List<GameObject> rootList = new List<GameObject>();
    private int rootIndexFrame = -1;

    // Stmavenie koľají a ciest: materiál "tieňa" pre každý pôvodný materiál
    private readonly Dictionary<Material, Material> roadDarkMats = new Dictionary<Material, Material>();
    private Material beamWriterMat, poolWriterMat;
    private bool roadDarkeningApplied;
    private bool roadCompanionsVisible = true;
    private int lastReportedRoads = -1;

    // Mapa svetla (pohľad zhora na celú mapu) – kreslia sa do nej kužele a kruhy
    private RenderTexture lightMap;
    private UnityEngine.Rendering.CommandBuffer lightCmd;
    private readonly List<Transform> beamEmitters = new List<Transform>();
    private readonly List<Transform> poolEmitters = new List<Transform>();

    private Material stationPoolMat, depotPoolMat, trainBeamMat, vehicleBeamMat;
    private Mesh poolMesh, beamMesh;

    private float nextMoverScan;
    private int gridRow;
    private int gridCycle;
    private bool warnedShaders;

    // =====================================================================
    // PREDVOLENÉ PROFILY (zistené z modelov a textúr)
    // =====================================================================

    /// <summary>UV obdĺžnik z pixelov obrázka (y zhora, ako v editore obrázkov).</summary>
    private static Vector4 Px(float x0, float y0, float x1, float y1, float size)
    {
        return new Vector4(x0 / size, 1f - y1 / size, (x1 - x0) / size, (y1 - y0) / size);
    }

    private static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

    private static FeatureProfile[] CreateDefaultProfiles()
    {
        return new[]
        {
            new FeatureProfile
            {
                name = "0 – Synty Simple Trains (SimpleTrains_Texture_0x, 1024)",
                rules = new[]
                {
                    new FeatureRule
                    {
                        name = "Sklo okien (vlaky, kabíny)", kind = FeatureKind.Window,
                        uvRect = Px(0, 478, 35, 512, 1024), colors = new[] { C(37, 39, 41) }, tolerance = 0.05f
                    },
                    new FeatureRule
                    {
                        name = "Tabule okien (stanica, depo)", kind = FeatureKind.Window,
                        uvRect = Px(390, 320, 640, 500, 1024), colors = new[] { C(64, 64, 64) }, tolerance = 0.05f
                    },
                    new FeatureRule
                    {
                        name = "Svetlomet (nos lokomotívy)", kind = FeatureKind.Headlight,
                        uvRect = Px(384, 558, 416, 582, 1024), colors = new[] { C(218, 215, 187) }, tolerance = 0.06f
                    }
                }
            },
            new FeatureProfile
            {
                name = "1 – Vehicles (CoalTruck … ElectronicsVan, 512)",
                rules = new[]
                {
                    new FeatureRule
                    {
                        name = "Sklo kabíny", kind = FeatureKind.Window,
                        uvRect = Px(0, 400, 134, 512, 512), colors = new[] { C(51, 50, 50) }, tolerance = 0.06f
                    },
                    new FeatureRule
                    {
                        name = "Predné svetlo ľavé", kind = FeatureKind.Headlight,
                        uvRect = Px(17, 148, 44, 182, 512),
                        colors = new[] { C(245, 207, 161), C(224, 201, 171), C(243, 177, 98) }, tolerance = 0.1f
                    },
                    new FeatureRule
                    {
                        name = "Predné svetlo pravé", kind = FeatureKind.Headlight,
                        uvRect = Px(202, 148, 229, 182, 512),
                        colors = new[] { C(245, 207, 161), C(224, 201, 171), C(243, 177, 98) }, tolerance = 0.1f
                    },
                    new FeatureRule
                    {
                        name = "Zadné svetlo ľavé", kind = FeatureKind.Taillight,
                        uvRect = Px(140, 346, 174, 361, 512),
                        colors = new[] { C(186, 28, 35), C(252, 182, 97), C(243, 177, 98) }, tolerance = 0.12f
                    },
                    new FeatureRule
                    {
                        name = "Zadné svetlo pravé", kind = FeatureKind.Taillight,
                        uvRect = Px(266, 346, 302, 361, 512),
                        colors = new[] { C(186, 28, 35), C(252, 182, 97), C(243, 177, 98) }, tolerance = 0.12f
                    }
                }
            }
        };
    }

    [ContextMenu("Reset profiles to defaults")]
    private void ResetProfiles() => profiles = CreateDefaultProfiles();

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[TransportNightLights] V scéne je viac inštancií – ponechávam prvú.");
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

        foreach (var t in featureMaps.Values)
            if (t != null) Destroy(t);
        featureMaps.Clear();

        DestroySafe(stationPoolMat); DestroySafe(depotPoolMat);
        DestroySafe(trainBeamMat); DestroySafe(vehicleBeamMat);
        DestroySafe(poolMesh); DestroySafe(beamMesh);
        foreach (var m in roadDarkMats.Values) DestroySafe(m);
        roadDarkMats.Clear();
        DestroySafe(beamWriterMat); DestroySafe(poolWriterMat);
        if (lightMap != null) { lightMap.Release(); Destroy(lightMap); lightMap = null; }
        if (lightCmd != null) { lightCmd.Release(); lightCmd = null; }
        Shader.SetGlobalTexture(ID_LightMap, Texture2D.blackTexture);
    }

    private static void DestroySafe(UnityEngine.Object o)
    {
        if (o != null) Destroy(o);
    }

    private void LateUpdate()
    {
        if (!ResolveShaders()) return;

        // Prepínač Darken Rails And Roads (aj počas hry)
        if (roadDarkeningApplied && !darkenRailsAndRoads) RemoveRoadDarkening();
        roadDarkeningApplied = darkenRailsAndRoads && roadDarkenShader != null;
        if (roadDarkeningApplied) UpdateRoadCompanionVisibility();

        if (depotsAndStations || roadDarkeningApplied) ScanGridStep();
        if (roadDarkeningApplied && roadCompanionsVisible) RenderLightMap();

        if (Time.unscaledTime >= nextMoverScan)
        {
            nextMoverScan = Time.unscaledTime + scanInterval;
            if (trains) ScanTrains();
            if (roadVehicles) ScanVehicles();
            processedMovers.RemoveWhere(g => g == null);
        }
    }

    private void OnValidate()
    {
        if (switchIntervalMax < switchIntervalMin) switchIntervalMax = switchIntervalMin;
        if (!Application.isPlaying) return;

        foreach (var nm in nightMaterials.Values)
            if (nm.mat != null) ApplyParams(nm.mat, (Usage)nm.usage);
        ApplyPoolParams();
        foreach (var m in roadDarkMats.Values)
            if (m != null) m.SetFloat(ID_RoadDarkness, railRoadDarkness);
    }

    private bool ResolveShaders()
    {
        if (featureShader == null) featureShader = Shader.Find("RetroCity/NightLightFeatures");
        if (poolShader == null) poolShader = Shader.Find("RetroCity/NightLightPool");
        if (roadDarkenShader == null) roadDarkenShader = Shader.Find("RetroCity/NightRoadDarken");
        if (darkenRailsAndRoads && roadDarkenShader == null && warned.Add(this))
            Debug.LogError("[TransportNightLights] Darken Rails And Roads je zapnuté, ale nie je " +
                           "priradený shader NightRoadDarken.");

        if (featureShader != null && poolShader != null) return true;

        if (!warnedShaders)
        {
            warnedShaders = true;
            Debug.LogError("[TransportNightLights] Nie sú priradené shadery! Pretiahni " +
                           "NightLightFeatures.shader a NightLightPool.shader do Inspectora.");
        }
        return false;
    }

    // =====================================================================
    // DEPÁ A STANICE – postupné prezeranie mapy (pár riadkov za snímok)
    // =====================================================================

    private void ScanGridStep()
    {
        var api = IndicatrixAPI.instance;
        if (api == null) return;

        int n = Mathf.Max(1, gridSize);
        int rows = Mathf.Clamp(gridRowsPerFrame, 1, n);

        for (int r = 0; r < rows; r++)
        {
            int x = gridRow;
            for (int z = 0; z < n; z++)
            {
                var td = api.GetTileByIndexAny(x, z);

                if (td.tileID == 1)
                {
                    // Koľaj / cesta / priecestie
                    if (roadDarkeningApplied &&
                        (td.category == IndicatrixAPI.TileCategory.Rail ||
                         td.category == IndicatrixAPI.TileCategory.Road ||
                         td.category == IndicatrixAPI.TileCategory.RailRoadCrossing))
                        VisitRoad(x, z);
                    continue;
                }

                if (!depotsAndStations) continue;
                if (td.tileID != 2 && td.tileID != 3) continue;
                if (td.category != IndicatrixAPI.TileCategory.Rail &&
                    td.category != IndicatrixAPI.TileCategory.Road) continue;

                VisitSite(x, z, td.tileID == 2);
            }

            gridRow++;
            if (gridRow >= n)
            {
                gridRow = 0;
                EndGridCycle();
            }
        }
    }

    private void VisitSite(int x, int z, bool isStation)
    {
        int key = x * 65536 + z;
        if (!sites.TryGetValue(key, out SiteEntry e) || e.isStation != isStation)
        {
            e = new SiteEntry { isStation = isStation };
            sites[key] = e;
        }
        e.seenCycle = gridCycle;

        if (e.model != null) return;                       // hotovo, model žije
        if (Time.unscaledTime < e.retryAt) return;         // model sa ešte nenašiel

        GameObject model = FindRootObject($"TileModel_{x}_{z}");
        if (model == null)
        {
            e.retryAt = Time.unscaledTime + 3f;             // napr. dlaždica bez modelu (quad)
            return;
        }

        e.model = model;
        ApplyToBuilding(model, isStation);
    }

    private void EndGridCycle()
    {
        // Odstránime záznamy dlaždíc, ktoré už nie sú depom/stanicou.
        // (Kruhy na zemi sú deťmi modelu – zmiznú spolu s ním.)
        var remove = new List<int>();
        foreach (var kv in sites)
            if (kv.Value.seenCycle != gridCycle) remove.Add(kv.Key);
        for (int i = 0; i < remove.Count; i++) sites.Remove(remove[i]);

        remove.Clear();
        foreach (var kv in roads)
            if (kv.Value.seenCycle != gridCycle) remove.Add(kv.Key);
        for (int i = 0; i < remove.Count; i++) roads.Remove(remove[i]);

        if (roadDarkeningApplied)
        {
            int found = 0;
            foreach (var e in roads.Values) if (e.model != null) found++;
            if (found != lastReportedRoads)
            {
                lastReportedRoads = found;
                Debug.Log($"[TransportNightLights] Stmavené dlaždice koľají/ciest: {found} " +
                          $"(nájdených na mape: {roads.Count}).");
            }
        }

        gridCycle++;
    }

    private void ApplyToBuilding(GameObject model, bool isStation)
    {
        SwapMaterials(model, depotStationProfile, Usage.Building);

        if (!groundPools) return;

        MeshFilter mf = LargestMeshFilter(model);
        if (mf == null) return;

        PoolSpot[] spots = isStation ? stationPools : depotPools;
        Material mat = isStation ? GetStationPoolMat() : GetDepotPoolMat();
        Bounds b = mf.sharedMesh.bounds;

        for (int i = 0; i < spots.Length; i++)
        {
            PoolSpot s = spots[i];
            if (s == null) continue;

            // Bod na spodku modelu v jeho priestore → svet
            Vector3 local = new Vector3(
                b.center.x + s.normalizedXZ.x * b.extents.x,
                b.min.y,
                b.center.z + s.normalizedXZ.y * b.extents.z);
            Vector3 world = mf.transform.TransformPoint(local);
            world.y += 0.02f;

            var go = new GameObject(PoolName);
            go.AddComponent<MeshFilter>().sharedMesh = GetPoolMesh();
            var mr = go.AddComponent<MeshRenderer>();
            ConfigureLightRenderer(mr, mat);

            go.transform.position = world;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(s.radius * 2f, 1f, s.radius * 2f);
            go.transform.SetParent(model.transform, true);  // zanikne spolu s modelom

            poolEmitters.Add(go.transform);
        }
    }

    // =====================================================================
    // VLAKY A VOZIDLÁ
    // =====================================================================

    private void ScanTrains()
    {
        var ts = TrainSystem.instance;
        if (ts == null) return;

        List<Vector2Int> depots = ts.GetAllDepotCoords();
        for (int i = 0; i < depots.Count; i++)
        {
            var td = ts.GetTrain(depots[i].x, depots[i].y);
            if (td == null) continue;

            if (td.locomotive != null) ApplyToMover(td.locomotive, trainProfile, true, trainBeams, trainBeamLengthWidth, false);
            for (int w = 0; w < td.wagons.Count; w++)
                if (td.wagons[w] != null) ApplyToMover(td.wagons[w], trainProfile, false, false, Vector2.zero, false);
        }
    }

    private void ScanVehicles()
    {
        var vs = VehicleSystem.instance;
        if (vs == null) return;

        List<Vector2Int> depots = vs.GetAllDepotCoords();
        for (int i = 0; i < depots.Count; i++)
        {
            var vd = vs.GetVehicle(depots[i].x, depots[i].y);
            if (vd == null || vd.vehicleBody == null) continue;
            ApplyToMover(vd.vehicleBody, vehicleProfile, true, vehicleBeams, vehicleBeamLengthWidth, true);
        }
    }

    /// <param name="root">Koreň člena (pohybová logika ho natáča +Z = smer jazdy).</param>
    private void ApplyToMover(GameObject root, int profile, bool isFront, bool beam, Vector2 beamLW, bool isRoad)
    {
        if (processedMovers.Contains(root)) return;
        processedMovers.Add(root);

        // Len skutočné 3D modely (TrainSystem/VehicleSystem ich vkladajú ako dieťa "Model").
        // Pôvodné kvádre (bez prefabu) necháme tak.
        Transform model = root.transform.Find("Model");
        if (model == null) return;

        SwapMaterials(model.gameObject, profile, Usage.Moving);

        if (!isFront || !beam || beamLW.x <= 0f || beamLW.y <= 0f) return;
        if (!TryGetLocalBounds(root.transform, model.gameObject, out Bounds lb)) return;

        var go = new GameObject(BeamName);
        go.AddComponent<MeshFilter>().sharedMesh = GetBeamMesh();
        var mr = go.AddComponent<MeshRenderer>();
        ConfigureLightRenderer(mr, isRoad ? GetVehicleBeamMat() : GetTrainBeamMat());

        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, lb.min.y + 0.02f, lb.max.z - 0.02f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(beamLW.y, 1f, beamLW.x);

        beamEmitters.Add(go.transform);
    }

    // =====================================================================
    // MATERIÁLY
    // =====================================================================

    private void SwapMaterials(GameObject go, int profile, Usage usage)
    {
        if (profiles == null || profile < 0 || profile >= profiles.Length) return;

        var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var rend = renderers[r];
            if (rend == null || IsOwnObject(rend.gameObject)) continue;

            Material[] mats = rend.sharedMaterials;
            bool changed = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Material src = mats[m];
                if (src == null || src.shader == featureShader) continue;
                mats[m] = GetNightMaterial(src, profile, usage);
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }
    }

    private Material GetNightMaterial(Material src, int profile, Usage usage)
    {
        var key = new MatKey { src = src, profile = profile, usage = (int)usage };
        if (nightMaterials.TryGetValue(key, out NightMat cached) && cached.mat != null)
            return cached.mat;

        var m = new Material(featureShader) { name = src.name + " (NightLights)" };

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
        m.SetTexture(ID_MainTex, albedo);
        m.SetTextureScale(ID_MainTex, scale);
        m.SetTextureOffset(ID_MainTex, offset);

        if (src.HasProperty(ID_Color)) m.SetColor(ID_Color, src.GetColor(ID_Color));
        else if (src.HasProperty("_BaseColor")) m.SetColor(ID_Color, src.GetColor("_BaseColor"));

        if (src.HasProperty(ID_Glossiness)) m.SetFloat(ID_Glossiness, src.GetFloat(ID_Glossiness));
        else if (src.HasProperty("_Smoothness")) m.SetFloat(ID_Glossiness, src.GetFloat("_Smoothness"));
        if (src.HasProperty(ID_Metallic)) m.SetFloat(ID_Metallic, src.GetFloat(ID_Metallic));

        Texture metalMap = src.HasProperty(ID_MetalMap) ? src.GetTexture(ID_MetalMap) : null;
        m.SetFloat(ID_UseMetalMap, metalMap != null ? 1f : 0f);
        if (metalMap != null)
        {
            m.SetTexture(ID_MetalMap, metalMap);
            if (src.HasProperty(ID_GlossMapScale)) m.SetFloat(ID_GlossMapScale, src.GetFloat(ID_GlossMapScale));
        }

        if (albedo != null)
        {
            Texture2D fm = GetFeatureMap(albedo, profile);
            if (fm != null) m.SetTexture(ID_FeatureMap, fm);
        }
        else if (warned.Add(src))
        {
            Debug.LogWarning($"[TransportNightLights] Materiál '{src.name}' nemá textúru – svetlá sa nenájdu.");
        }

        ApplyParams(m, usage);
        nightMaterials[key] = new NightMat { mat = m, usage = (int)usage };
        return m;
    }

    private void ApplyParams(Material m, Usage usage)
    {
        bool building = usage == Usage.Building;

        m.SetFloat(ID_StaticSeed, building ? 1f : 0f);
        m.SetColor(ID_ColA, building ? windowColorA : vehicleWindowColor);
        m.SetColor(ID_ColB, building ? windowColorB : vehicleWindowColor);
        m.SetFloat(ID_WinIntensity, building ? buildingWindowIntensity : vehicleWindowIntensity);
        m.SetFloat(ID_LitFraction, building ? buildingLitFraction : vehicleLitFraction);
        m.SetFloat(ID_DynFraction, building ? buildingDynamicFraction : 0f);
        m.SetFloat(ID_SwitchMin, switchIntervalMin);
        m.SetFloat(ID_SwitchMax, Mathf.Max(switchIntervalMin, switchIntervalMax));
        m.SetColor(ID_HeadColor, headlightColor);
        m.SetFloat(ID_HeadIntensity, headlightIntensity);
        m.SetColor(ID_TailColor, taillightColor);
        m.SetFloat(ID_TailIntensity, taillightIntensity);
    }

    // ── Svetelné kruhy a kužele ─────────────────────────────────────────

    private Material MakePoolMat(string name, float shape)
    {
        var m = new Material(poolShader) { name = name };
        m.SetFloat(ID_PoolShape, shape);
        return m;
    }

    private Material GetStationPoolMat()
    {
        if (stationPoolMat == null) { stationPoolMat = MakePoolMat("NightPool_Station", 0f); ApplyPoolParams(); }
        return stationPoolMat;
    }

    private Material GetDepotPoolMat()
    {
        if (depotPoolMat == null) { depotPoolMat = MakePoolMat("NightPool_Depot", 0f); ApplyPoolParams(); }
        return depotPoolMat;
    }

    private Material GetTrainBeamMat()
    {
        if (trainBeamMat == null) { trainBeamMat = MakePoolMat("NightBeam_Train", 1f); ApplyPoolParams(); }
        return trainBeamMat;
    }

    private Material GetVehicleBeamMat()
    {
        if (vehicleBeamMat == null) { vehicleBeamMat = MakePoolMat("NightBeam_Vehicle", 1f); ApplyPoolParams(); }
        return vehicleBeamMat;
    }

    private void ApplyPoolParams()
    {
        foreach (var m in new[] { stationPoolMat, depotPoolMat })
        {
            if (m == null) continue;
            m.SetColor(ID_PoolColor, poolColor);
            m.SetFloat(ID_PoolIntensity, poolIntensity);
        }
        foreach (var m in new[] { trainBeamMat, vehicleBeamMat })
        {
            if (m == null) continue;
            m.SetColor(ID_PoolColor, beamColor);
            m.SetFloat(ID_PoolIntensity, beamIntensity);
        }

        // Mapa svetla (odhalenie koľají a ciest)
        if (beamWriterMat != null) beamWriterMat.SetFloat(ID_PoolIntensity, beamRevealStrength);
        if (poolWriterMat != null) poolWriterMat.SetFloat(ID_PoolIntensity, poolsRevealRoads ? poolRevealStrength : 0f);
    }

    private static void ConfigureLightRenderer(MeshRenderer mr, Material mat)
    {
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    /// <summary>Štvorec 1×1 v rovine XZ, stred v počiatku (svetelný kruh).</summary>
    private Mesh GetPoolMesh()
    {
        if (poolMesh != null) return poolMesh;
        poolMesh = BuildQuad("NightPoolQuad", -0.5f, 0.5f);
        return poolMesh;
    }

    /// <summary>Štvorec 1×1 v rovine XZ, začína v počiatku a ide do +Z (kužeľ).</summary>
    private Mesh GetBeamMesh()
    {
        if (beamMesh != null) return beamMesh;
        beamMesh = BuildQuad("NightBeamQuad", 0f, 1f);
        return beamMesh;
    }

    private static Mesh BuildQuad(string name, float z0, float z1)
    {
        var m = new Mesh { name = name };
        m.vertices = new[]
        {
            new Vector3(-0.5f, 0f, z0), new Vector3(0.5f, 0f, z0),
            new Vector3(-0.5f, 0f, z1), new Vector3(0.5f, 0f, z1)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        return m;
    }

    // =====================================================================
    // KOĽAJE A CESTY – stmavenie v noci, odhalenie svetlom
    // =====================================================================

    private void VisitRoad(int x, int z)
    {
        int key = x * 65536 + z;
        if (!roads.TryGetValue(key, out SiteEntry e))
        {
            e = new SiteEntry();
            roads[key] = e;
        }
        e.seenCycle = gridCycle;

        if (e.model != null) return;
        if (Time.unscaledTime < e.retryAt) return;

        // 3D model dlaždice (TileModelLibrary) alebo textúrovaný quad
        GameObject obj = FindRootObject($"TileModel_{x}_{z}");
        if (obj == null) obj = FindRootObject($"Tile_{x}_{z}");
        if (obj == null)
        {
            e.retryAt = Time.unscaledTime + 3f;   // napr. most/tunel kreslený spritom
            return;
        }

        e.model = obj;
        AddRoadCompanions(obj);
    }

    /// <summary>
    /// Každému rendereru dlaždice pridá „tieňový“ objekt s rovnakým mesh-om,
    /// ktorý hneď po ňom (fronta pôvodného materiálu + 1) vynásobí obraz tmou
    /// a svetlom z mapy svetla. Pôvodný materiál sa NEMENÍ – funguje s
    /// akýmkoľvek shaderom (aj Unlit a priehľadnými textúrami koľají).
    /// </summary>
    private void AddRoadCompanions(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<MeshRenderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var rend = renderers[r];
            if (rend == null || IsOwnObject(rend.gameObject)) continue;
            var mf = rend.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Material[] src = rend.sharedMaterials;
            int subs = Mathf.Max(1, mf.sharedMesh.subMeshCount);
            var mats = new Material[subs];
            for (int i = 0; i < subs; i++)
            {
                Material sm = (src != null && src.Length > 0) ? src[Mathf.Min(i, src.Length - 1)] : null;
                mats[i] = GetRoadDarkMat(sm);
            }

            var go = new GameObject(RoadDarkName);
            go.transform.SetParent(rend.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            ConfigureLightRenderer(mr, mats[0]);
            mr.sharedMaterials = mats;
            mr.enabled = roadCompanionsVisible;
        }
    }

    private void RemoveRoadDarkening()
    {
        foreach (var e in roads.Values)
        {
            if (e.model == null) continue;
            var ts = e.model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < ts.Length; i++)
                if (ts[i] != null && ts[i].gameObject.name == RoadDarkName) Destroy(ts[i].gameObject);
        }
        roads.Clear();
        roadDarkeningApplied = false;
        lastReportedRoads = -1;
    }

    /// <summary>Cez deň tieňové objekty vypneme – nestoja nič.</summary>
    private void UpdateRoadCompanionVisibility()
    {
        bool visible = Shader.GetGlobalFloat(ID_NightAmount) > 0.001f;
        if (visible == roadCompanionsVisible) return;
        roadCompanionsVisible = visible;

        foreach (var e in roads.Values)
        {
            if (e.model == null) continue;
            var mrs = e.model.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
                if (mrs[i] != null && mrs[i].gameObject.name == RoadDarkName) mrs[i].enabled = visible;
        }
    }

    /// <summary>Tieňový materiál pre pôvodný materiál koľaje/cesty (prevezme textúru kvôli priehľadnosti).</summary>
    private Material GetRoadDarkMat(Material src)
    {
        if (src != null && roadDarkMats.TryGetValue(src, out Material cached) && cached != null) return cached;

        var m = new Material(roadDarkenShader) { name = (src != null ? src.name : "null") + " (NightDark)" };
        if (src != null)
        {
            if (src.HasProperty(ID_MainTex))
            {
                m.SetTexture(ID_MainTex, src.GetTexture(ID_MainTex));
                m.SetTextureScale(ID_MainTex, src.GetTextureScale(ID_MainTex));
                m.SetTextureOffset(ID_MainTex, src.GetTextureOffset(ID_MainTex));
            }
            if (src.HasProperty(ID_Color)) m.SetColor(ID_Color, src.GetColor(ID_Color));
            // Hneď PO pôvodnom materiáli (aj keď je priehľadný / Unlit)
            m.renderQueue = src.renderQueue + 1;
            // Alfu textúry berieme do úvahy len pri priehľadných / cutout materiáloch
            bool transparent = src.renderQueue >= 2450 ||
                               src.GetTag("RenderType", false, "") == "TransparentCutout" ||
                               src.GetTag("RenderType", false, "") == "Transparent";
            m.SetFloat(ID_UseAlpha, transparent ? 1f : 0f);
        }
        m.SetFloat(ID_RoadDarkness, railRoadDarkness);

        if (src != null) roadDarkMats[src] = m;
        return m;
    }

    // ── Mapa svetla ─────────────────────────────────────────────────────

    private void RenderLightMap()
    {
        int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(256, lightMapResolution)), 256, 8192);
        if (lightMap == null || lightMap.width != res)
        {
            if (lightMap != null) { lightMap.Release(); Destroy(lightMap); }

            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)
                ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            if (!SystemInfo.SupportsRenderTextureFormat(fmt)) fmt = RenderTextureFormat.ARGB32;

            lightMap = new RenderTexture(res, res, 0, fmt, RenderTextureReadWrite.Linear)
            {
                name = "NightLightMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false
            };
            lightMap.Create();
        }

        float size = Mathf.Max(1, gridSize);
        Shader.SetGlobalTexture(ID_LightMap, lightMap);
        Shader.SetGlobalVector(ID_LightMapParams, new Vector4(0f, 0f, 1f / size, 1f / size));

        if (lightCmd == null) lightCmd = new UnityEngine.Rendering.CommandBuffer { name = "NightLightMap" };
        lightCmd.Clear();
        lightCmd.SetRenderTarget(lightMap);
        lightCmd.ClearRenderTarget(false, true, Color.black);

        Material bw = GetBeamWriterMat();
        Material pw = GetPoolWriterMat();
        DrawEmitters(beamEmitters, GetBeamMesh(), bw);
        if (poolsRevealRoads) DrawEmitters(poolEmitters, GetPoolMesh(), pw);

        Graphics.ExecuteCommandBuffer(lightCmd);
    }

    private void DrawEmitters(List<Transform> list, Mesh mesh, Material mat)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Transform t = list[i];
            if (t == null) { list.RemoveAt(i); continue; }
            if (!t.gameObject.activeInHierarchy) continue;
            lightCmd.DrawMesh(mesh, t.localToWorldMatrix, mat, 0, 0);
        }
    }

    private Material MakeWriterMat(string name, float shape)
    {
        var m = new Material(poolShader) { name = name };
        m.SetFloat(ID_PoolShape, shape);
        m.SetFloat(ID_RevealMode, 1f);
        return m;
    }

    private Material GetBeamWriterMat()
    {
        if (beamWriterMat == null) { beamWriterMat = MakeWriterMat("NightLightMap_Beam", 1f); ApplyPoolParams(); }
        return beamWriterMat;
    }

    private Material GetPoolWriterMat()
    {
        if (poolWriterMat == null) { poolWriterMat = MakeWriterMat("NightLightMap_Pool", 0f); ApplyPoolParams(); }
        return poolWriterMat;
    }

    // =====================================================================
    // MAPA SVETIEL – R id okna, G okno, B predné, A zadné svetlo
    // =====================================================================

    private Texture2D GetFeatureMap(Texture src, int profileIndex)
    {
        long key = ((long)src.GetHashCode() << 8) ^ profileIndex;
        if (featureMaps.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

        int w = src.width, h = src.height;
        if (w <= 0 || h <= 0) return null;
        Color32[] px = ReadPixels(src, w, h);
        if (px == null) return null;

        FeatureProfile p = profiles[profileIndex];
        int n = w * h;
        var win = new bool[n];
        var head = new bool[n];
        var tail = new bool[n];
        int found = 0;

        if (p.rules != null)
        {
            for (int ri = 0; ri < p.rules.Length; ri++)
            {
                FeatureRule rule = p.rules[ri];
                if (rule == null) continue;

                int x0 = Mathf.Clamp(Mathf.FloorToInt(rule.uvRect.x * w), 0, w);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((rule.uvRect.x + rule.uvRect.z) * w), 0, w);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(rule.uvRect.y * h), 0, h);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((rule.uvRect.y + rule.uvRect.w) * h), 0, h);
                bool[] target = rule.kind == FeatureKind.Window ? win
                              : rule.kind == FeatureKind.Headlight ? head : tail;

                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = row + x;
                        if (MatchesColor(px[i], rule)) { target[i] = true; found++; }
                    }
                }
            }
        }

        if (found == 0 && warned.Add(src))
            Debug.LogWarning($"[TransportNightLights] V textúre '{src.name}' sa nenašli žiadne " +
                             $"svetlá (profil '{p.name}') – skontroluj priradenie profilu.");

        // Súvislé oblasti okien → náhodné ID (každá tabuľa zvlášť)
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (!win[i]) continue;
                if (x > 0 && win[i - 1]) Union(parent, i, i - 1);
                if (y > 0 && win[i - w]) Union(parent, i, i - w);
            }
        }

        var rng = new System.Random(unchecked(w * 7919 + h * 104729 + found));
        var rootId = new Dictionary<int, byte>();
        var level0 = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            byte id = 0;
            if (win[i])
            {
                int root = Find(parent, i);
                if (!rootId.TryGetValue(root, out id))
                {
                    id = (byte)rng.Next(1, 256);
                    rootId[root] = id;
                }
            }
            level0[i] = new Color32(id, win[i] ? (byte)255 : (byte)0,
                                    head[i] ? (byte)255 : (byte)0, tail[i] ? (byte)255 : (byte)0);
        }

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
        {
            name = src.name + "_NightFeatureMap",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            anisoLevel = 0
        };

        // Vlastné mipmapy: masky (G, B, A) priemer, ID (R) prvé nenulové v bloku
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
                    int xa = Mathf.Min(x * 2, cw - 1), xb = Mathf.Min(x * 2 + 1, cw - 1);
                    int ya = Mathf.Min(y * 2, ch - 1), yb = Mathf.Min(y * 2 + 1, ch - 1);
                    Color32 a = cur[ya * cw + xa], b = cur[ya * cw + xb];
                    Color32 c = cur[yb * cw + xa], d = cur[yb * cw + xb];

                    byte id = a.r != 0 ? a.r : b.r != 0 ? b.r : c.r != 0 ? c.r : d.r;
                    next[y * nw + x] = new Color32(id,
                        (byte)((a.g + b.g + c.g + d.g + 2) / 4),
                        (byte)((a.b + b.b + c.b + d.b + 2) / 4),
                        (byte)((a.a + b.a + c.a + d.a + 2) / 4));
                }
            }
            tex.SetPixels32(next, mip);
            cur = next; cw = nw; ch = nh;
        }
        tex.Apply(false, true);

        featureMaps[key] = tex;
        return tex;
    }

    private static bool MatchesColor(Color32 c, FeatureRule rule)
    {
        if (rule.colors == null || rule.colors.Length == 0) return true;
        float tol = rule.tolerance * 255f + 0.5f;
        for (int k = 0; k < rule.colors.Length; k++)
        {
            Color32 key = rule.colors[k];
            if (Mathf.Abs(c.r - key.r) <= tol && Mathf.Abs(c.g - key.g) <= tol && Mathf.Abs(c.b - key.b) <= tol)
                return true;
        }
        return false;
    }

    /// <summary>Pixely textúry cez GPU kópiu – textúra NEMUSÍ mať Read/Write.</summary>
    private static Color32[] ReadPixels(Texture tex, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        tmp.Apply(false, false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = tmp.GetPixels32();
        Destroy(tmp);
        return px;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private static bool IsOwnObject(GameObject go)
    {
        string n = go.name;
        return n == PoolName || n == BeamName || n == RevealName || n == RoadDarkName;
    }

    /// <summary>
    /// Nájde objekt v koreni scény podľa mena. Index koreňových objektov sa
    /// zostaví najviac raz za snímok (namiesto pomalého GameObject.Find).
    /// Dlaždice vytvára IndicatrixAPI vždy v koreni scény.
    /// </summary>
    private GameObject FindRootObject(string name)
    {
        if (rootIndex.TryGetValue(name, out GameObject go) && go != null) return go;
        if (rootIndexFrame == Time.frameCount) return null;

        rootIndexFrame = Time.frameCount;
        rootIndex.Clear();
        rootList.Clear();
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects(rootList);
        for (int i = 0; i < rootList.Count; i++)
        {
            var g = rootList[i];
            if (g == null || !g.name.StartsWith("Tile")) continue;
            rootIndex[g.name] = g;
        }
        return rootIndex.TryGetValue(name, out go) ? go : null;
    }

    private static MeshFilter LargestMeshFilter(GameObject go)
    {
        MeshFilter best = null;
        float bestSize = -1f;
        var mfs = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < mfs.Length; i++)
        {
            var mf = mfs[i];
            if (mf == null || mf.sharedMesh == null) continue;
            if (IsOwnObject(mf.gameObject)) continue;
            float s = mf.sharedMesh.bounds.size.sqrMagnitude;
            if (s > bestSize) { bestSize = s; best = mf; }
        }
        return best;
    }

    /// <summary>Hranice modelu v lokálnom priestore koreňa (funguje aj pri neaktívnom objekte).</summary>
    private static bool TryGetLocalBounds(Transform root, GameObject model, out Bounds result)
    {
        result = default;
        bool any = false;
        var mfs = model.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < mfs.Length; i++)
        {
            var mf = mfs[i];
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            Vector3 mn = b.min, mx = b.max;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? mn.x : mx.x,
                                         (k & 2) == 0 ? mn.y : mx.y,
                                         (k & 4) == 0 ? mn.z : mx.z);
                Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return any;
    }

    private static int Find(int[] p, int x)
    {
        while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
        return x;
    }

    private static void Union(int[] p, int a, int b)
    {
        int ra = Find(p, a), rb = Find(p, b);
        if (ra != rb) p[rb] = ra;
    }
}
