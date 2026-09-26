using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Dopravná sieť, ku ktorej most / tunel patrí.</summary>
public enum CrossingNetwork { Rail = 0, Road = 1 }

/// <summary>Typ mimoúrovňového prechodu.</summary>
public enum CrossingType { Tunnel = 0, Bridge = 1 }

/// <summary>Os prechodu: Horizontal = pozdĺž X (Left↔Right), Vertical = pozdĺž Z (Bottom↔Top).</summary>
public enum CrossingAxis { Horizontal = 0, Vertical = 1 }

/// <summary>
/// CrossingSystemBase
/// ─────────────────────────────────────────────────────────────────────────
/// SPOLOČNÁ LOGIKA MOSTOV a TUNELOV pre železnicu (RailCrossingSystem) aj
/// cesty (RoadCrossingSystem). Podtriedy dodávajú len to, čím sa siete líšia:
///   • zápis hlavy do tileGrid (RailConstructionMode vs RoadConstructionMode),
///   • kontrolu, či je na hlave vlak / cestné vozidlo,
///   • ceny (ConstructionCosts),
///   • ktorý UI režim GameManager-a je "môj" tunel / most.
///
/// DÁTOVÝ MODEL – "DVE HLAVY + IMPLICITNÝ PRECHOD"
/// ───────────────────────────────────────────────
///   Do tileGrid (IndicatrixAPI) sa zapisujú LEN DVE HLAVY prechodu:
///     • tunel – 2 portály (vstup/výstup) na rampách,
///     • most  – nástupný a výstupný tile.
///   Hlava je bežná RAIL / ROAD dlaždica (tileID 1) so stateID
///   TunnelHorizontal / TunnelVertical / BridgeHorizontal / BridgeVertical
///   príslušného enumu a spojeniami Left|Right alebo Top|Bottom. Na vonkajšiu
///   stranu hlavy sa preto napojí akýkoľvek existujúci diel siete.
///
///   Tily MEDZI hlavami sa do tileGrid NEZAPISUJÚ – jazdná dráha je daná
///   implicitne (CrossingData.TrackY). Preto:
///     • pod mostom môže zostať iná koľaj/cesta (pri dostatočnej svetlej výške),
///     • nad tunelom sa dá stavať,
///     • most / tunel sa búra vždy CELÝ.
///
/// PATHFINDING (TrainSystem / VehicleSystem)
/// ─────────────────────────────────────────
///   Pre A* je prechod SKOK medzi hlavami s cenou Length-1. Po nájdení trasy
///   sa skok rozvinie na súvislé tily a výška waypointov sa berie z TrackY.
///
/// VIAC SIETÍ NARAZ
/// ────────────────
///   Každá sieť má vlastný komponent (vlastný register, sprity, save blok).
///   Kontroly, ktoré sa týkajú OBOCH sietí (most cez most, tunel cez tunel,
///   svetlá výška pod mostom, terén, továrne, mestá), idú cez statické
///   metódy *Any, ktoré prejdú všetky aktívne systémy.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public abstract class CrossingSystemBase : MonoBehaviour
{
    public const int GRID_SIZE = 256;
    protected const float EPS = 0.001f;

    /// <summary>Minimálna dĺžka mosta vrátane oboch hláv (rampa + 1 stred + rampa).</summary>
    public const int MinBridgeLength = 3;

    /// <summary>Maximálna dĺžka mosta vrátane oboch hláv.</summary>
    public const int MaxBridgeLength = 30;

    /// <summary>
    /// Počet TYPOV mostov na sieť (A, B, C). Hráč typ vyberá v okne
    /// GameRailSelectBridgesMenuUI / GameRoadSelectBridgesMenuUI. Typ určuje
    /// vizuál (sprity v CrossingSpriteLibrary) a cenu (ConstructionCosts).
    /// Pravidlá stavby (dĺžka, výška mostovky, svetlá výška) sú pre typy spoločné.
    /// </summary>
    public const int BridgeVariantCount = 3;

    // =====================================================================
    // DÁTA JEDNÉHO PRECHODU
    // =====================================================================

    /// <summary>
    /// Jeden postavený most alebo tunel. startHead má VŽDY menšiu súradnicu
    /// (západ / juh), endHead väčšiu – nezávisle od poradia klikov.
    /// </summary>
    public class CrossingData
    {
        public int id;
        public CrossingNetwork network;
        public CrossingType type;
        public CrossingAxis axis;
        public Vector2Int startHead;
        public Vector2Int endHead;

        /// <summary>Výška jazdnej dráhy (bez offsetu vozidla) na VONKAJŠEJ hrane štartovej hlavy.</summary>
        public float startOuterY;
        /// <summary>Výška jazdnej dráhy na VONKAJŠEJ hrane koncovej hlavy.</summary>
        public float endOuterY;
        /// <summary>Výška mostovky (pri tuneli výška tunela).</summary>
        public float deckY;

        /// <summary>Reálne zaplatená cena – refund pri demolácii je 50 % z nej.</summary>
        public uint buildCost;

        /// <summary>Typ mosta: 0 = A, 1 = B, 2 = C. Pri tuneli vždy 0.</summary>
        public int variant;

        public int Length => axis == CrossingAxis.Horizontal
            ? endHead.x - startHead.x + 1
            : endHead.y - startHead.y + 1;

        public Vector2Int Step => axis == CrossingAxis.Horizontal
            ? new Vector2Int(1, 0)
            : new Vector2Int(0, 1);

        public Vector2Int TileAt(int i) => startHead + Step * i;

        public bool IsHead(Vector2Int t) => t == startHead || t == endHead;

        /// <summary>Krok z hlavy smerom DO prechodu.</summary>
        public Vector2Int SpanStepFrom(Vector2Int head) => head == startHead ? Step : -Step;

        /// <summary>Smer z hlavy smerom DO prechodu.</summary>
        public IndicatrixAPI.DirectionMask SpanDirFrom(Vector2Int head)
        {
            bool start = head == startHead;
            if (axis == CrossingAxis.Horizontal)
                return start ? IndicatrixAPI.DirectionMask.Right : IndicatrixAPI.DirectionMask.Left;
            return start ? IndicatrixAPI.DirectionMask.Top : IndicatrixAPI.DirectionMask.Bottom;
        }

        /// <summary>
        /// Výška jazdnej dráhy (bez offsetu) v bode so súradnicou axisCoord
        /// pozdĺž osi prechodu (x pre Horizontal, z pre Vertical).
        ///   vonkajšia hrana štartovej hlavy → startOuterY
        ///   vnútorná hrana štartovej hlavy  → deckY (lineárne)
        ///   celý stred                       → deckY
        ///   vnútorná hrana koncovej hlavy   → deckY
        ///   vonkajšia hrana koncovej hlavy  → endOuterY
        /// Vonkajšie hrany sa zhodujú s terénom → plynulé napojenie bez schodu.
        /// </summary>
        public float TrackY(float axisCoord)
        {
            float s0 = axis == CrossingAxis.Horizontal ? startHead.x : startHead.y;
            float s1 = s0 + 1f;
            float e1 = (axis == CrossingAxis.Horizontal ? endHead.x : endHead.y) + 1f;
            float e0 = e1 - 1f;

            if (axisCoord <= s0) return startOuterY;
            if (axisCoord < s1) return Mathf.Lerp(startOuterY, deckY, axisCoord - s0);
            if (axisCoord <= e0) return deckY;
            if (axisCoord < e1) return Mathf.Lerp(deckY, endOuterY, axisCoord - e0);
            return endOuterY;
        }
    }

    public enum CrossingDemolishResult { NotCrossing, Demolished, BlockedByVehicle }

    // =====================================================================
    // NASTAVENIA (Inspector) – spoločné pre obe siete
    // =====================================================================

    [Header("Tunel – pravidlá")]
    [Tooltip("Minimálna dĺžka tunela vrátane oboch portálov.")]
    [SerializeField] private int minTunnelLength = 3;

    [Tooltip("Maximálna dĺžka tunela vrátane oboch portálov (koľko tilov sa prehľadáva).")]
    [SerializeField] private int maxTunnelLength = 40;

    [Header("Most – pravidlá")]
    [Tooltip("Povolí most medzi DVOMA VODOROVNÝMI tilmi (obe hlavy na rovine). " +
             "Predvolene VYPNUTÉ – most sa smie stavať len medzi dvomi šikmými " +
             "tilmi (rampami). Pri pokuse o most medzi vodorovnými tilmi sa zobrazí " +
             "GameErrors.CannotBuildBridgeOnFlatTerrain.")]
    [SerializeField] private bool allowFlatBridges = false;

    [Tooltip("O koľko je mostovka VYŠŠIE než hlavy, ak sú obe hlavy na ROVINE " +
             "(uplatní sa len pri zapnutom Allow Flat Bridges) " +
             "(0.25 = 1 úroveň terénu). Hlava mosta je vtedy nájazdová rampa.")]
    [SerializeField] private float flatBridgeDeckRise = 0.5f;

    [Tooltip("MINIMÁLNA VÝŠKA MOSTA: mostovka musí byť aspoň o toľko vyššie než " +
             "NAJNIŽŠÍ bod terénu pod mostom (0.25 = 1 úroveň, 0.5 = 2 úrovne). " +
             "Nižší most sa nepostaví (GameErrors.CannotBuildBridgeTooLow) a okno " +
             "výberu typu mosta sa ani nezobrazí. POZOR: text chyby uvádza 2 úrovne.")]
    [SerializeField] private float minBridgeHeight = 0.5f;

    [Tooltip("VÝNIMKA PRE MOSTY CEZ VODU: ak most prechádza nad vodnou hladinou, " +
             "namiesto Min Bridge Height stačí, aby bola mostovka nad hladinou aspoň " +
             "o túto hodnotu (0.25 = 1 úroveň = úplné minimum). Hlavami takého mosta " +
             "smú byť aj ŠIKMÉ BREHOVÉ tily, ktorých dolná hrana sa dotýka vody. " +
             "Mosty na pevnine sa riadia naďalej Min Bridge Height.")]
    [SerializeField] private float minBridgeHeightOverWater = 0.25f;

    [Tooltip("Minimálna svetlá výška medzi mostovkou a HOLÝM terénom pod mostom " +
             "na KAŽDOM tile (napr. kopec medzi hlavami). Celkovú výšku mosta " +
             "stráži Min Bridge Height.")]
    [SerializeField] private float minClearanceOverTerrain = 0f;

    [Tooltip("Minimálna svetlá výška medzi mostovkou a terénom pod KOĽAJOU/CESTOU/" +
             "STANICOU/DEPOM (tie pod mostom smú byť – z ľubovoľnej siete).")]
    [SerializeField] private float minClearanceOverStructure = 0.5f;

    [Tooltip("Minimálna medzera medzi mostovkou a VRCHOM MESTSKEJ BUDOVY pod mostom. " +
             "Budova smie byť pod mostom, len ak ho neprevyšuje (výška z CityManager).")]
    [SerializeField] private float minClearanceOverBuilding = 0.1f;

    [Header("Vizuál")]
    [Tooltip("Knižnica spritov TEJTO siete. Prázdne = dohľadá sa CrossingSpriteLibrary " +
             "s rovnakou sieťou (Network). Chýbajúci sprite = náhradná geometria.")]
    [SerializeField] private CrossingSpriteLibrary spriteLibrary;

    [SerializeField] private RailCrossingSpriteGlobalSettings spriteGlobal = new RailCrossingSpriteGlobalSettings();

    [Tooltip("Farba náhradnej mostovky (keď chýba sprite).")]
    [SerializeField] private Color fallbackBridgeColor = new Color(0.55f, 0.55f, 0.58f, 1f);

    [Tooltip("Farba náhradného portálu tunela (keď chýba sprite).")]
    [SerializeField] private Color fallbackTunnelColor = new Color(0.15f, 0.13f, 0.12f, 1f);

    [Tooltip("Farba značky prvého kliku mosta.")]
    [SerializeField] private Color pendingMarkerColor = new Color(1f, 0.85f, 0.2f, 0.6f);

    [Tooltip("Skryť vlak / vozidlo, kým ide VO VNÚTRI tunela (pod terénom).")]
    [FormerlySerializedAs("hideTrainsInsideTunnels")]
    [SerializeField] private bool hideVehiclesInsideTunnels = true;

    // =====================================================================
    // ŠPECIFIKÁ SIETE (implementujú podtriedy)
    // =====================================================================

    /// <summary>Sieť, ktorú tento komponent spravuje.</summary>
    public abstract CrossingNetwork Network { get; }

    /// <summary>Zapíše HLAVU prechodu do tileGrid (správny enum + kategória).</summary>
    protected abstract void WriteHeadTile(Vector2Int tile, CrossingData c);

    /// <summary>True, ak na (alebo tesne pri) hlave stojí / ide vlak či vozidlo tejto siete.</summary>
    protected abstract bool IsVehicleOnHead(Vector2Int head);

    /// <summary>Aktuálny UI režim GameManager-a pre túto sieť.</summary>
    protected abstract void GetUiMode(out bool tunnelMode, out bool bridgeMode);

    public abstract uint TunnelBuildCost(int length);

    /// <summary>Cena mosta danej dĺžky a TYPU (0 = A, 1 = B, 2 = C).</summary>
    public abstract uint BridgeBuildCost(int length, int variant);

    // =====================================================================
    // STAV
    // =====================================================================

    private readonly List<CrossingData> crossings = new List<CrossingData>();
    private readonly Dictionary<Vector2Int, CrossingData> heads = new Dictionary<Vector2Int, CrossingData>();
    private readonly Dictionary<Vector2Int, List<CrossingData>> spans = new Dictionary<Vector2Int, List<CrossingData>>();
    private readonly Dictionary<int, List<GameObject>> visuals = new Dictionary<int, List<GameObject>>();
    private readonly HashSet<Vector2Int> coveredTiles = new HashSet<Vector2Int>();
    private int nextId = 1;

    // Náhľad
    private readonly List<GameObject> previewObjects = new List<GameObject>();
    private string previewKey;
    private int previewTouchedFrame = -1;
    private int mapVersion;

    // Most – prvý klik
    private Vector2Int? pendingBridgeStart;
    private GameObject pendingMarker;

    // Most – overený plán čakajúci na výber TYPU v okne Select Bridges
    private CrossingData awaitingVariantPlan;

    private static readonly Dictionary<Color32, Material> fallbackMaterials = new Dictionary<Color32, Material>();

    public int Count => crossings.Count;
    public IReadOnlyList<CrossingData> All => crossings;
    public bool HideVehiclesInsideTunnels => hideVehiclesInsideTunnels;

    /// <summary>Alias pre TrainSystem (pôvodný názov).</summary>
    public bool HideTrainsInsideTunnels => hideVehiclesInsideTunnels;

    // =====================================================================
    // REGISTER VŠETKÝCH SYSTÉMOV (pre kontroly naprieč sieťami)
    // =====================================================================

    private static readonly List<CrossingSystemBase> systems = new List<CrossingSystemBase>();

    /// <summary>Všetky aktívne systémy mostov/tunelov (RAIL, ROAD).</summary>
    public static IReadOnlyList<CrossingSystemBase> Systems => systems;

    protected virtual void Awake()
    {
        if (!systems.Contains(this)) systems.Add(this);
    }

    protected virtual void OnDestroy()
    {
        systems.Remove(this);
    }

    /// <summary>True, ak niektorá sieť má tile [x,z] POD MOSTOM.</summary>
    public static bool IsBridgeSpanTileAny(int x, int z)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].IsBridgeSpanTile(x, z)) return true;
        return false;
    }

    /// <summary>True, ak niektorá sieť má tile [x,z] NAD TUNELOM (vnútro tunela).</summary>
    public static bool IsTunnelSpanTileAny(int x, int z)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].IsTunnelSpanTile(x, z)) return true;
        return false;
    }

    /// <summary>
    /// Smie sa na tile [x,z] postaviť koľaj / cesta / stanica / depo vzhľadom
    /// na mosty OBOCH sietí nad ním?
    /// </summary>
    public static bool HasClearanceForStructureAny(int x, int z)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && !systems[i].HasClearanceForStructure(x, z)) return false;
        return true;
    }

    /// <summary>True, ak ktorýkoľvek tile obdĺžnika leží pod mostom ľubovoľnej siete (továrne).</summary>
    public static bool AnyBridgeSpanInRectAny(int originX, int originZ, int width, int depth)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].AnyBridgeSpanInRect(originX, originZ, width, depth)) return true;
        return false;
    }

    /// <summary>Zasiahla by úprava terénu most / tunel ľubovoľnej siete?</summary>
    public static bool WouldTerrainEditHitCrossingAny(List<Vector2Int> affectedVertices, bool levelUp)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].WouldTerrainEditHitCrossing(affectedVertices, levelUp)) return true;
        return false;
    }

    /// <summary>True, ak niektorá sieť má aspoň jeden most alebo tunel.</summary>
    public static bool AnyCrossingsExist()
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].Count > 0) return true;
        return false;
    }

    private static bool IsBridgeHeadTileAny(Vector2Int t)
    {
        for (int i = 0; i < systems.Count; i++)
            if (systems[i] != null && systems[i].heads.TryGetValue(t, out CrossingData c) && c.type == CrossingType.Bridge)
                return true;
        return false;
    }

    /// <summary>Spoločná implementácia GetOrCreate pre podtriedy.</summary>
    protected static T FindOrCreate<T>(string objectName) where T : CrossingSystemBase
    {
        T found = FindFirstObjectByType<T>();
        if (found != null) return found;

        Debug.LogWarning($"[{typeof(T).Name}] Komponent nie je v scéne – vytváram ho automaticky. " +
                         "Pre nastavenia v Inspectore ho pridaj na GameObject v scéne.");
        return new GameObject(objectName).AddComponent<T>();
    }

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    protected virtual void LateUpdate()
    {
        GetUiMode(out bool tunnelMode, out bool bridgeMode);

        // Čaká sa na výber typu mosta: náhľad plánovaného mosta ostáva zobrazený
        // (GameManager vtedy mapu neobnovuje). Ak medzitým režim Most zanikol
        // (ESC, zatvorenie okna, iný režim), výber sa zruší.
        if (awaitingVariantPlan != null)
        {
            if (!bridgeMode) EndVariantSelection();
            else return;
        }

        // Mimo režimu mosta nesmie zostať "visieť" prvý klik.
        if (!bridgeMode && pendingBridgeStart.HasValue)
            ClearPendingBridgeStart();

        // Náhľad len v režime tunel/most a len keď ho GameManager v tomto
        // frame obnovil (kurzor nad mapou, nie nad UI).
        if ((!tunnelMode && !bridgeMode) || previewTouchedFrame != Time.frameCount)
            ClearPreview();
    }

    // =====================================================================
    // DOTAZY – TERÉN
    // =====================================================================

    protected static bool IsInGrid(Vector2Int t)
        => t.x >= 0 && t.y >= 0 && t.x < GRID_SIZE && t.y < GRID_SIZE;

    protected static float VertexY(int vx, int vz)
    {
        TerrainManager tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null) return 0f;
        int w = tm.terrainWidth;
        vx = Mathf.Clamp(vx, 0, w);
        vz = Mathf.Clamp(vz, 0, w);
        int idx = vz * (w + 1) + vx;
        return (idx >= 0 && idx < tm.coordsF.Length) ? tm.coordsF[idx].y : 0f;
    }

    private static void Corners(Vector2Int t, out float h00, out float h10, out float h01, out float h11)
    {
        h00 = VertexY(t.x, t.y);
        h10 = VertexY(t.x + 1, t.y);
        h01 = VertexY(t.x, t.y + 1);
        h11 = VertexY(t.x + 1, t.y + 1);
    }

    protected static float MinCorner(Vector2Int t)
    {
        Corners(t, out float a, out float b, out float c, out float d);
        return Mathf.Min(a, Mathf.Min(b, Mathf.Min(c, d)));
    }

    protected static float MaxCorner(Vector2Int t)
    {
        Corners(t, out float a, out float b, out float c, out float d);
        return Mathf.Max(a, Mathf.Max(b, Mathf.Max(c, d)));
    }

    private static bool IsFlat(Vector2Int t)
    {
        Corners(t, out float a, out float b, out float c, out float d);
        return Mathf.Abs(a - b) < EPS && Mathf.Abs(a - c) < EPS && Mathf.Abs(a - d) < EPS;
    }

    /// <summary>
    /// Čistá rampa (jedna hrana nižšie, protiľahlá vyššie). Vráti os sklonu,
    /// smer STÚPANIA a výšku dolnej/hornej hrany.
    /// </summary>
    private static bool TryGetRamp(Vector2Int t, out CrossingAxis axis,
                                   out IndicatrixAPI.DirectionMask climbDir,
                                   out float lowY, out float highY)
    {
        Corners(t, out float h00, out float h10, out float h01, out float h11);
        axis = CrossingAxis.Horizontal;
        climbDir = IndicatrixAPI.DirectionMask.None;
        lowY = highY = h00;

        bool alongX = Mathf.Abs(h00 - h01) < EPS && Mathf.Abs(h10 - h11) < EPS && Mathf.Abs(h00 - h10) >= EPS;
        bool alongZ = Mathf.Abs(h00 - h10) < EPS && Mathf.Abs(h01 - h11) < EPS && Mathf.Abs(h00 - h01) >= EPS;

        if (alongX)
        {
            axis = CrossingAxis.Horizontal;
            climbDir = h10 > h00 ? IndicatrixAPI.DirectionMask.Right : IndicatrixAPI.DirectionMask.Left;
            lowY = Mathf.Min(h00, h10);
            highY = Mathf.Max(h00, h10);
            return true;
        }
        if (alongZ)
        {
            axis = CrossingAxis.Vertical;
            climbDir = h01 > h00 ? IndicatrixAPI.DirectionMask.Top : IndicatrixAPI.DirectionMask.Bottom;
            lowY = Mathf.Min(h00, h01);
            highY = Mathf.Max(h00, h01);
            return true;
        }
        return false;
    }

    private static float EdgeY(Vector2Int t, IndicatrixAPI.DirectionMask side)
    {
        Corners(t, out float h00, out float h10, out float h01, out float h11);
        switch (side)
        {
            case IndicatrixAPI.DirectionMask.Left: return (h00 + h01) * 0.5f;
            case IndicatrixAPI.DirectionMask.Right: return (h10 + h11) * 0.5f;
            case IndicatrixAPI.DirectionMask.Bottom: return (h00 + h10) * 0.5f;
            case IndicatrixAPI.DirectionMask.Top: return (h01 + h11) * 0.5f;
            default: return (h00 + h10 + h01 + h11) * 0.25f;
        }
    }

    private static Vector2Int StepOf(IndicatrixAPI.DirectionMask d)
    {
        switch (d)
        {
            case IndicatrixAPI.DirectionMask.Right: return new Vector2Int(1, 0);
            case IndicatrixAPI.DirectionMask.Left: return new Vector2Int(-1, 0);
            case IndicatrixAPI.DirectionMask.Top: return new Vector2Int(0, 1);
            case IndicatrixAPI.DirectionMask.Bottom: return new Vector2Int(0, -1);
            default: return Vector2Int.zero;
        }
    }

    // =====================================================================
    // DOTAZY – PRECHODY TEJTO SIETE
    // =====================================================================

    public bool TryGetHead(Vector2Int tile, out CrossingData c) => heads.TryGetValue(tile, out c);

    public bool IsBridgeSpanTile(int x, int z) => HasSpanOfType(new Vector2Int(x, z), CrossingType.Bridge);

    public bool IsTunnelSpanTile(int x, int z) => HasSpanOfType(new Vector2Int(x, z), CrossingType.Tunnel);

    private bool HasSpanOfType(Vector2Int t, CrossingType type)
    {
        if (!spans.TryGetValue(t, out var list)) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i].type == type) return true;
        return false;
    }

    public bool AnyBridgeSpanInRect(int originX, int originZ, int width, int depth)
    {
        if (spans.Count == 0) return false;
        for (int x = originX; x < originX + width; x++)
            for (int z = originZ; z < originZ + depth; z++)
                if (IsBridgeSpanTile(x, z)) return true;
        return false;
    }

    /// <summary>Svetlá výška pod mostami TEJTO siete. Pre všetky siete použi HasClearanceForStructureAny.</summary>
    public bool HasClearanceForStructure(int x, int z)
    {
        var t = new Vector2Int(x, z);
        if (!spans.TryGetValue(t, out var list)) return true;

        float maxC = MaxCorner(t);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].type != CrossingType.Bridge) continue;
            if (maxC > list[i].deckY - minClearanceOverStructure + EPS) return false;
        }
        return true;
    }

    /// <summary>
    /// Zasiahla by úprava terénu most / tunel tejto siete?
    ///   • LevelUp   – terén iba stúpa → ohrozuje MOSTY.
    ///   • LevelDown – terén iba klesá → ohrozuje TUNELY.
    /// Hlavy sú v tileGrid – tie stráži existujúci guard obsadenosti.
    /// </summary>
    public bool WouldTerrainEditHitCrossing(List<Vector2Int> affectedVertices, bool levelUp)
    {
        if (affectedVertices == null || spans.Count == 0) return false;
        CrossingType threatened = levelUp ? CrossingType.Bridge : CrossingType.Tunnel;

        for (int i = 0; i < affectedVertices.Count; i++)
        {
            int vx = affectedVertices[i].x;
            int vz = affectedVertices[i].y;
            for (int tx = vx - 1; tx <= vx; tx++)
                for (int tz = vz - 1; tz <= vz; tz++)
                    if (HasSpanOfType(new Vector2Int(tx, tz), threatened)) return true;
        }
        return false;
    }

    /// <summary>
    /// Je bod JAZDNEJ DRÁHY (waypoint trasy, t.j. vrátane offsetu vozidla nad
    /// dráhou) vo VNÚTRI tunela tejto siete?
    ///
    /// Tile musí byť vnútorným tile tunela (portály sa nepočítajú) a bod musí
    /// ležať v úrovni tunela, nie na povrchu nad ním. Porovnáva sa s výškou
    /// TUNELA (deckY + trackOffset), nie s terénom – terén nad tunelom môže byť
    /// len o 1 úroveň (0.25) vyššie, čo je menej než offset vozidla (0.3).
    /// Povrchová koľaj/cesta nad tunelom je vždy aspoň o 0.25 vyššie → hranica
    /// v polovici (0.125) ich spoľahlivo rozlíši.
    /// </summary>
    public bool IsPointInsideTunnel(Vector3 p, float trackOffset = 0.3f)
    {
        if (spans.Count == 0) return false;
        var t = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        if (!spans.TryGetValue(t, out var list)) return false;

        for (int i = 0; i < list.Count; i++)
        {
            CrossingData c = list[i];
            if (c.type != CrossingType.Tunnel) continue;
            if (p.y < c.deckY + trackOffset + 0.125f) return true;
        }
        return false;
    }

    /// <summary>
    /// Je ktorákoľvek časť TELA vozidla (vlakového člena / cestného vozidla)
    /// vo vnútri tunela? Telo je úsek trasy od ZADKU po PREDOK.
    ///
    ///   • predok vojde za portál do tunela → true  (vozidlo sa skryje),
    ///   • zadok vyjde z tunela za portál   → false (vozidlo sa odkryje).
    ///
    /// Tunel je priamy a jeho vnútorné tily súvislé, preto stačí test predku,
    /// stredu a zadku – časť tela medzi nimi nemôže byť v tuneli sama.
    /// </summary>
    public bool IsBodyInsideTunnel(Vector3 rear, Vector3 center, Vector3 front, float trackOffset = 0.3f)
    {
        if (spans.Count == 0) return false;
        return IsPointInsideTunnel(front, trackOffset)
            || IsPointInsideTunnel(center, trackOffset)
            || IsPointInsideTunnel(rear, trackOffset);
    }

    // =====================================================================
    // PATHFINDING – podpora pre TrainSystem / VehicleSystem
    // =====================================================================

    /// <summary>
    /// Vyplní polia snapshotu pre A* (HLAVNÉ vlákno). Len prechody TEJTO siete.
    ///   jumpTarget[i] – index partnerskej hlavy (-1 = nie je hlava),
    ///   jumpDir[i]    – DirectionMask smerom do prechodu,
    ///   jumpCost[i]   – cena skoku (Length - 1).
    /// </summary>
    public void FillSnapshot(int[] jumpTarget, int[] jumpDir, int[] jumpCost, int gridSize)
    {
        for (int k = 0; k < crossings.Count; k++)
        {
            CrossingData c = crossings[k];
            int s = c.startHead.y * gridSize + c.startHead.x;
            int e = c.endHead.y * gridSize + c.endHead.x;
            if (s < 0 || e < 0 || s >= jumpTarget.Length || e >= jumpTarget.Length) continue;

            jumpTarget[s] = e;
            jumpTarget[e] = s;
            jumpDir[s] = (int)c.SpanDirFrom(c.startHead);
            jumpDir[e] = (int)c.SpanDirFrom(c.endHead);
            jumpCost[s] = jumpCost[e] = Mathf.Max(1, c.Length - 1);
        }
    }

    /// <summary>
    /// Pre SÚVISLÚ tile trasu vráti, ktoré indexy patria mostu/tunelu tejto siete
    /// (hlavy + vnútro prechádzané PO prechode). entryHeadIdx[i] = index vstupnej
    /// hlavy pre vnútorné tily, inak -1. Vráti null, ak trasa prechod nemá.
    /// </summary>
    public CrossingData[] ResolvePathCrossings(IList<Vector2Int> path, out int[] entryHeadIdx)
    {
        entryHeadIdx = null;
        if (path == null || path.Count == 0 || crossings.Count == 0) return null;

        CrossingData[] res = null;
        int[] entry = null;

        for (int i = 0; i < path.Count; i++)
        {
            if (!heads.TryGetValue(path[i], out CrossingData c)) continue;

            if (res == null)
            {
                res = new CrossingData[path.Count];
                entry = new int[path.Count];
                for (int k = 0; k < entry.Length; k++) entry[k] = -1;
            }

            res[i] = c;
            Vector2Int step = c.SpanStepFrom(path[i]);
            if (i + 1 < path.Count && path[i + 1] == path[i] + step)
            {
                int interior = c.Length - 2;
                int k = 1;
                for (; k <= interior && i + k < path.Count; k++)
                {
                    if (path[i + k] != path[i] + step * k) break; // poistka
                    res[i + k] = c;
                    entry[i + k] = i;
                }
                i += k - 1; // ďalšia iterácia pokračuje partnerskou hlavou
            }
        }

        entryHeadIdx = entry;
        return res;
    }

    // =====================================================================
    // VALIDÁCIA
    // =====================================================================

    /// <summary>Spoločná kontrola tile pre HLAVU (portál / nástup). null = OK.</summary>
    /// <param name="allowShoreRamp">
    /// True = hlava MOSTA smie byť aj šikmý BREHOVÝ tile (rampa, ktorej dolná hrana
    /// sa dotýka vodnej hladiny). Tunely a ostatné stavby túto výnimku nemajú.
    /// </param>
    private string CheckHeadTile(Vector2Int t, bool allowShoreRamp = false)
    {
        if (!IsInGrid(t)) return GameErrors.CannotBuildOnOccupiedTile;

        if (CityManager.instance != null && CityManager.instance.IsCityTile(t.x, t.y))
            return GameErrors.CannotBuildOnBuilding;

        if (EnvironmentManager.instance != null && EnvironmentManager.instance.IsEnvironmentTile(t.x, t.y))
            return EnvironmentManager.instance.IsProtectedEnvironmentTile(t.x, t.y)
                ? GameErrors.CannotBuildOnLandingLocation
                : GameErrors.CannotBuildOnEnvironment;

        IndicatrixAPI api = IndicatrixAPI.instance;
        if (api != null)
        {
            var td = api.GetTileByIndexAny(t.x, t.y);
            if (td.category == IndicatrixAPI.TileCategory.Factory) return GameErrors.CannotBuildOnExisting;
            if (td.tileID != 0) return GameErrors.CannotBuildOnOccupiedTile;
            if (api.IsFaceWater(t.x, t.y) && !(allowShoreRamp && IsShoreRamp(t)))
                return GameErrors.CannotBuildOnWater;
        }

        if (IsBridgeSpanTileAny(t.x, t.y)) return GameErrors.CannotBuildUnderBridge;
        return null;
    }

    /// <summary>
    /// ŠIKMÝ BREHOVÝ TILE: čistá rampa, ktorej dolná hrana leží na vodnej hladine
    /// (IsFaceWater) a horná hrana nad ňou. Len taký tile pri vode smie byť hlavou mosta.
    /// </summary>
    private static bool IsShoreRamp(Vector2Int t)
    {
        IndicatrixAPI api = IndicatrixAPI.instance;
        if (api == null || !api.IsFaceWater(t.x, t.y)) return false;
        return TryGetRamp(t, out _, out _, out float lowY, out float highY) && highY - lowY >= EPS;
    }

    /// <summary>True, ak tile leží (aspoň jedným rohom) na vodnej hladine.</summary>
    private static bool IsWaterTile(Vector2Int t)
    {
        IndicatrixAPI api = IndicatrixAPI.instance;
        return api != null && api.IsFaceWater(t.x, t.y);
    }

    /// <summary>
    /// TUNEL z kliknutého portálu. Portál musí byť rampa; tunel vedie smerom
    /// DO KOPCA (smer stúpania). Prvý tile, kde terén klesne na úroveň tunela,
    /// musí byť opačne orientovaná rampa s rovnakou dolnou hranou (výjazd).
    /// </summary>
    public bool ValidateTunnel(Vector2Int portal, out CrossingData plan, out string error)
    {
        plan = null;
        error = null;

        if (!IsInGrid(portal) ||
            !TryGetRamp(portal, out CrossingAxis axis, out IndicatrixAPI.DirectionMask climb,
                        out float lowY, out _))
        {
            error = GameErrors.CannotBuildTunnelOnTerrain;
            return false;
        }

        Vector2Int step = StepOf(climb);
        Vector2Int partner = portal;
        bool found = false;
        int maxLen = Mathf.Max(minTunnelLength, maxTunnelLength);

        for (int k = 1; k < maxLen; k++)
        {
            Vector2Int t = portal + step * k;
            if (!IsInGrid(t))
            {
                error = GameErrors.CannotBuildTunnelOnTerrain;
                return false;
            }

            if (MinCorner(t) <= lowY + EPS)
            {
                bool match = TryGetRamp(t, out CrossingAxis a2, out IndicatrixAPI.DirectionMask c2,
                                        out float low2, out _)
                             && a2 == axis
                             && c2 == IndicatrixAPI.Opposite(climb)
                             && Mathf.Abs(low2 - lowY) < EPS;

                if (!match || k + 1 < minTunnelLength)
                {
                    error = GameErrors.CannotBuildTunnelOnTerrain;
                    return false;
                }

                partner = t;
                found = true;
                break;
            }

            // Tunely sa nesmú pretínať – ani tunel inej siete.
            if (IsTunnelSpanTileAny(t.x, t.y))
            {
                error = GameErrors.CannotBuildTunnelThroughTunnel;
                return false;
            }
        }

        if (!found)
        {
            error = GameErrors.CannotBuildTunnelTooLong;
            return false;
        }

        error = CheckHeadTile(portal) ?? CheckHeadTile(partner);
        if (error != null) return false;

        plan = MakePlan(CrossingType.Tunnel, axis, portal, partner, lowY, lowY, lowY);
        return true;
    }

    /// <summary>Kontrola PRVÉHO kliku mosta.</summary>
    public bool ValidateBridgeHead(Vector2Int tile, out string error)
    {
        // Brehová rampa je povolená už pri 1. kliku; či most naozaj vedie cez
        // vodu, sa overí pri 2. kliku (ValidateBridge).
        error = CheckHeadTile(tile, allowShoreRamp: true);
        if (error != null) return false;

        if (!IsFlat(tile) && !TryGetRamp(tile, out _, out _, out _, out _))
        {
            error = GameErrors.CannotBuildBridgeOnTerrain;
            return false;
        }
        return true;
    }

    /// <summary>
    /// MOST medzi dvomi tilmi v jednej priamke.
    ///   • obe hlavy na ROVINE → ZAKÁZANÉ (GameErrors.CannotBuildBridgeOnFlatTerrain);
    ///     len pri zapnutom allowFlatBridges: rovnaká výška → mostovka = výška + flatBridgeDeckRise,
    ///   • obe hlavy RAMPY pozdĺž osi, zrkadlovo opačné, s rovnakou hornou hranou.
    /// Dĺžka 3..15 tilov. Nič pod mostom nesmie prevyšovať mostovku.
    /// </summary>
    public bool ValidateBridge(Vector2Int a, Vector2Int b, out CrossingData plan, out string error)
    {
        plan = null;
        error = null;

        if (a.x != b.x && a.y != b.y)
        {
            error = GameErrors.CannotBuildBridgeNotStraight;
            return false;
        }

        CrossingAxis axis = (a.y == b.y && a.x != b.x) ? CrossingAxis.Horizontal : CrossingAxis.Vertical;
        Vector2Int start, end;
        if (axis == CrossingAxis.Horizontal) { start = a.x < b.x ? a : b; end = a.x < b.x ? b : a; }
        else { start = a.y < b.y ? a : b; end = a.y < b.y ? b : a; }

        int length = axis == CrossingAxis.Horizontal ? end.x - start.x + 1 : end.y - start.y + 1;
        if (length < MinBridgeLength || length > MaxBridgeLength)
        {
            error = GameErrors.CannotBuildBridgeLength;
            return false;
        }

        error = CheckHeadTile(start, allowShoreRamp: true) ?? CheckHeadTile(end, allowShoreRamp: true);
        if (error != null) return false;

        int kindS = HeadProfile(start, axis, true, out float outerS, out float innerS);
        int kindE = HeadProfile(end, axis, false, out float outerE, out float innerE);

        float deckY;
        if (kindS == 1 && kindE == 1)
        {
            // [ERROR GUARD] MOST MEDZI DVOMA VODOROVNÝMI TILMI – zakázaný (RAIL aj ROAD).
            // Kontroluje sa tu, v ValidateBridge → pri 2. kliku sa ohlási chyba,
            // okno výberu typu mosta sa neotvorí a neplatný most nemá náhľad.
            if (!allowFlatBridges)
            {
                error = GameErrors.CannotBuildBridgeOnFlatTerrain;
                return false;
            }

            if (Mathf.Abs(outerS - outerE) >= EPS) { error = GameErrors.CannotBuildBridgeOnTerrain; return false; }
            deckY = outerS + flatBridgeDeckRise;
        }
        else if (kindS == 2 && kindE == 2)
        {
            float dS = innerS - outerS;
            float dE = innerE - outerE;
            float deckS = Mathf.Max(innerS, outerS);
            float deckE = Mathf.Max(innerE, outerE);
            if (Mathf.Sign(dS) != Mathf.Sign(dE) || Mathf.Abs(deckS - deckE) >= EPS)
            {
                error = GameErrors.CannotBuildBridgeOnTerrain;
                return false;
            }
            deckY = deckS;
        }
        else
        {
            error = GameErrors.CannotBuildBridgeOnTerrain;
            return false;
        }

        // [ERROR GUARD] BREHOVÁ HLAVA (rampa dotýkajúca sa vody) je povolená len
        // vtedy, ak jej vodná (dolná) hrana smeruje DO mosta – most teda z brehu
        // vychádza nad vodu. Opačne orientovaná brehová hlava = stavba na vode.
        bool shoreS = IsWaterTile(start);
        bool shoreE = IsWaterTile(end);
        if ((shoreS && !(innerS < outerS - EPS)) || (shoreE && !(innerE < outerE - EPS)))
        {
            error = GameErrors.CannotBuildOnWater;
            return false;
        }

        IndicatrixAPI api = IndicatrixAPI.instance;
        Vector2Int step = axis == CrossingAxis.Horizontal ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
        float lowestTerrainUnder = float.MaxValue;   // pre Min Bridge Height
        bool spansWater = false;                     // most prechádza nad vodnou hladinou
        for (int i = 1; i < length - 1; i++)
        {
            Vector2Int t = start + step * i;
            lowestTerrainUnder = Mathf.Min(lowestTerrainUnder, MinCorner(t));
            if (!spansWater && IsWaterTile(t)) spansWater = true;

            // Mosty sa nesmú križovať – ani most inej siete.
            if (IsBridgeSpanTileAny(t.x, t.y) || IsBridgeHeadTileAny(t))
            {
                error = GameErrors.CannotBuildBridgeOverBridge;
                return false;
            }

            var td = api != null ? api.GetTileByIndexAny(t.x, t.y) : default;

            // Továreň (sprite neznámej výšky) a environment (strom/kameň) – vždy prekážka.
            if (td.category == IndicatrixAPI.TileCategory.Factory ||
                (EnvironmentManager.instance != null && EnvironmentManager.instance.IsEnvironmentTile(t.x, t.y)))
            {
                error = GameErrors.CannotBuildBridgeObstacle;
                return false;
            }

            // Mestská budova – smie byť pod mostom, ak neprevyšuje mostovku.
            if (CityManager.instance != null && CityManager.instance.IsCityTile(t.x, t.y))
            {
                if (!CityManager.instance.TryGetBuildingTopY(t.x, t.y, out float buildingTop) ||
                    buildingTop > deckY - minClearanceOverBuilding + EPS)
                {
                    error = GameErrors.CannotBuildBridgeObstacle;
                    return false;
                }
                continue;
            }

            // Koľaj / cesta / stanica / depo (RAIL aj ROAD) – svetlá výška.
            float need = td.tileID != 0 ? minClearanceOverStructure : minClearanceOverTerrain;
            if (MaxCorner(t) > deckY - need + EPS)
            {
                error = GameErrors.CannotBuildBridgeObstacle;
                return false;
            }
        }

        // [ERROR GUARD] MINIMÁLNA VÝŠKA MOSTA – mostovka nad najnižším bodom
        // terénu pod mostom. Most na rovine má výšku flatBridgeDeckRise; most
        // medzi svahmi výšku podľa hĺbky údolia (plytké údolie = 1 úroveň → chyba).
        // Kontroluje sa tu, v ValidateBridge → pri 2. kliku sa chyba ohlási
        // SKÔR, než sa otvorí okno výberu typu mosta, a neplatný most nemá náhľad.
        //
        // VÝNIMKA – MOST CEZ VODU: ak most prechádza nad vodnou hladinou, stačí
        // minBridgeHeightOverWater (predvolene 1 úroveň = úplné minimum).
        // Mosty na pevnine sa riadia minBridgeHeight bez zmeny.
        //
        // Brehová hlava bez vody pod mostom sa nepripúšťa (poistka).
        if ((shoreS || shoreE) && !spansWater)
        {
            error = GameErrors.CannotBuildOnWater;
            return false;
        }

        float requiredHeight = spansWater
            ? Mathf.Min(minBridgeHeight, Mathf.Max(minBridgeHeightOverWater, EPS * 2f))
            : minBridgeHeight;

        if (deckY - lowestTerrainUnder < requiredHeight - EPS)
        {
            error = GameErrors.CannotBuildBridgeTooLow;
            return false;
        }

        plan = MakePlan(CrossingType.Bridge, axis, start, end, outerS, outerE, deckY);
        return true;
    }

    /// <summary>0 = nevhodný tile, 1 = rovina, 2 = rampa pozdĺž osi.</summary>
    private static int HeadProfile(Vector2Int t, CrossingAxis axis, bool isStart,
                                   out float outerY, out float innerY)
    {
        IndicatrixAPI.DirectionMask inner, outer;
        if (axis == CrossingAxis.Horizontal)
        {
            inner = isStart ? IndicatrixAPI.DirectionMask.Right : IndicatrixAPI.DirectionMask.Left;
            outer = isStart ? IndicatrixAPI.DirectionMask.Left : IndicatrixAPI.DirectionMask.Right;
        }
        else
        {
            inner = isStart ? IndicatrixAPI.DirectionMask.Top : IndicatrixAPI.DirectionMask.Bottom;
            outer = isStart ? IndicatrixAPI.DirectionMask.Bottom : IndicatrixAPI.DirectionMask.Top;
        }

        outerY = EdgeY(t, outer);
        innerY = EdgeY(t, inner);

        if (IsFlat(t)) return 1;
        if (TryGetRamp(t, out CrossingAxis rampAxis, out _, out _, out _) && rampAxis == axis) return 2;
        return 0;
    }

    private CrossingData MakePlan(CrossingType type, CrossingAxis axis,
                                  Vector2Int p, Vector2Int q,
                                  float outerP, float outerQ, float deckY)
    {
        bool pFirst = axis == CrossingAxis.Horizontal ? p.x < q.x : p.y < q.y;
        return new CrossingData
        {
            id = -1,
            network = Network,
            type = type,
            axis = axis,
            startHead = pFirst ? p : q,
            endHead = pFirst ? q : p,
            startOuterY = pFirst ? outerP : outerQ,
            endOuterY = pFirst ? outerQ : outerP,
            deckY = deckY
        };
    }

    // =====================================================================
    // STAVBA / DEMOLÁCIA
    // =====================================================================

    /// <summary>Postaví overený plán. Cenu už zaplatil volajúci.</summary>
    public CrossingData Build(CrossingData plan, uint paidCost)
    {
        if (plan == null) return null;

        plan.id = nextId++;
        plan.network = Network;
        plan.variant = plan.type == CrossingType.Bridge ? Mathf.Clamp(plan.variant, 0, BridgeVariantCount - 1) : 0;
        plan.buildCost = paidCost;
        Register(plan, writeHeadTiles: true);
        mapVersion++;
        previewKey = null;

        FlashCrossingTiles(plan, TerrainEditHighlight.FlashType.Build);

        Debug.Log($"[{GetType().Name}] {plan.type} ({plan.axis}, typ {(char)('A' + plan.variant)}) postavený " +
                  $"[{plan.startHead.x},{plan.startHead.y}]–[{plan.endHead.x},{plan.endHead.y}], " +
                  $"dĺžka {plan.Length}, výška {plan.deckY:F2}, cena {paidCost}.");
        return plan;
    }

    /// <summary>
    /// Demolish nad tile [x,z]. Zbúra CELÝ prechod tejto siete, ak je tile:
    ///   • hlavou mosta/tunela, alebo
    ///   • PRÁZDNYM tile pod mostom (tileInfo.tileID == 0).
    /// Vnútro tunela navonok neexistuje – tunel sa búra cez portál.
    /// </summary>
    public CrossingDemolishResult TryDemolishAt(int x, int z, IndicatrixAPI.TileData tileInfo, out uint refund)
    {
        refund = 0u;
        var t = new Vector2Int(x, z);

        CrossingData c = null;
        if (heads.TryGetValue(t, out CrossingData hc))
        {
            c = hc;
        }
        else if (tileInfo.tileID == 0 && spans.TryGetValue(t, out var list))
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].type == CrossingType.Bridge) { c = list[i]; break; }
        }

        if (c == null) return CrossingDemolishResult.NotCrossing;

        // Kým je vozidlo vo vnútri prechodu, jeho currentTile ostáva na VSTUPNEJ
        // hlave → stačí skontrolovať obe hlavy.
        if (IsVehicleOnHead(c.startHead) || IsVehicleOnHead(c.endHead))
            return CrossingDemolishResult.BlockedByVehicle;

        refund = ConstructionCosts.CrossingDemolishRefund(c.buildCost);
        Unregister(c, clearHeadTiles: true);

        FlashCrossingTiles(c, TerrainEditHighlight.FlashType.Demolish);
        mapVersion++;
        previewKey = null;

        Debug.Log($"[{GetType().Name}] {c.type} zbúraný celý ({c.Length} tilov), refund {refund}.");
        return CrossingDemolishResult.Demolished;
    }

    /// <summary>
    /// Efekt výstavby / demolácie na teréne pre most alebo tunel.
    /// MOST: všetky tily (hlavy + tily pod mostovkou).
    /// TUNEL: len portály – vnútro je pod zemou, povrch nad ním sa nemení.
    /// Hlavy by rozsvietil aj IndicatrixAPI pri zápise tile; opakované
    /// spustenie na tom istom tile je neškodné (efekt plynulo pokračuje).
    /// </summary>
    private static void FlashCrossingTiles(CrossingData c, TerrainEditHighlight.FlashType type)
    {
        if (c == null || !Application.isPlaying) return;

        var tiles = new List<Vector2Int>();
        if (c.type == CrossingType.Tunnel)
        {
            tiles.Add(c.startHead);
            tiles.Add(c.endHead);
        }
        else
        {
            for (int i = 0; i < c.Length; i++)
                tiles.Add(c.TileAt(i));
        }

        TerrainEditHighlight.GetOrCreate().FlashTiles(tiles, type);
    }

    private void Register(CrossingData c, bool writeHeadTiles)
    {
        crossings.Add(c);
        heads[c.startHead] = c;
        heads[c.endHead] = c;

        for (int i = 1; i < c.Length - 1; i++)
        {
            Vector2Int t = c.TileAt(i);
            if (!spans.TryGetValue(t, out var list)) spans[t] = list = new List<CrossingData>();
            list.Add(c);
        }

        if (writeHeadTiles && IndicatrixAPI.instance != null)
        {
            WriteHeadTile(c.startHead, c);
            WriteHeadTile(c.endHead, c);
        }

        var objs = new List<GameObject>();
        BuildVisuals(c, false, objs);
        visuals[c.id] = objs;
    }

    private void Unregister(CrossingData c, bool clearHeadTiles)
    {
        crossings.Remove(c);
        heads.Remove(c.startHead);
        heads.Remove(c.endHead);

        for (int i = 1; i < c.Length - 1; i++)
        {
            Vector2Int t = c.TileAt(i);
            if (spans.TryGetValue(t, out var list))
            {
                list.Remove(c);
                if (list.Count == 0) spans.Remove(t);
            }
        }

        if (visuals.TryGetValue(c.id, out var objs))
        {
            DestroyAll(objs);
            visuals.Remove(c.id);
        }

        SetCovered(c.startHead, false);
        SetCovered(c.endHead, false);

        IndicatrixAPI api = IndicatrixAPI.instance;
        if (clearHeadTiles && api != null)
        {
            api.ClearTileByIndex(c.startHead.x, c.startHead.y);
            api.ClearTileByIndex(c.endHead.x, c.endHead.y);
        }
    }

    /// <summary>Zruší VŠETKY prechody tejto siete (visuals + register). tileGrid nemení (volá sa pred Load).</summary>
    public void ClearAll()
    {
        foreach (var kv in visuals) DestroyAll(kv.Value);
        visuals.Clear();

        var covered = new List<Vector2Int>(coveredTiles);
        foreach (var t in covered) SetCovered(t, false);

        crossings.Clear();
        heads.Clear();
        spans.Clear();
        nextId = 1;
        mapVersion++;
        CancelAll();
    }

    private void SetCovered(Vector2Int t, bool covered)
    {
        bool changed = covered ? coveredTiles.Add(t) : coveredTiles.Remove(t);
        if (changed && IndicatrixAPI.instance != null)
            IndicatrixAPI.instance.SetCrossingTileCovered(t.x, t.y, covered);
    }

    // =====================================================================
    // MOST – PRVÝ KLIK
    // =====================================================================

    public bool HasPendingBridgeStart => pendingBridgeStart.HasValue;
    public Vector2Int PendingBridgeStart => pendingBridgeStart ?? Vector2Int.zero;

    public void SetPendingBridgeStart(Vector2Int tile)
    {
        pendingBridgeStart = tile;
        previewKey = null;

        if (pendingMarker != null) Destroy(pendingMarker);
        pendingMarker = CreateBox("BridgePendingMarker",
            new Vector3(tile.x + 0.5f, (MinCorner(tile) + MaxCorner(tile)) * 0.5f + 0.04f, tile.y + 0.5f),
            Quaternion.identity, new Vector3(0.92f, 0.03f, 0.92f), pendingMarkerColor);
    }

    public void ClearPendingBridgeStart()
    {
        pendingBridgeStart = null;
        previewKey = null;
        if (pendingMarker != null) { Destroy(pendingMarker); pendingMarker = null; }
    }

    /// <summary>ESC / pravé tlačidlo: zruší LEN prvý klik mosta. True, ak niečo zrušil.</summary>
    public bool TryCancelPendingBridgeStart()
    {
        if (!pendingBridgeStart.HasValue) return false;
        ClearPendingBridgeStart();
        ClearPreview();
        return true;
    }

    /// <summary>Úplný reset (ESC / Close okna / zmena režimu).</summary>
    public void CancelAll()
    {
        ClearPendingBridgeStart();
        awaitingVariantPlan = null;
        ClearPreview();
    }

    // =====================================================================
    // MOST – VÝBER TYPU (okno GameRail/RoadSelectBridgesMenuUI)
    // =====================================================================

    /// <summary>True, kým je otvorený výber typu mosta pre overený plán.</summary>
    public bool IsAwaitingVariantSelection => awaitingVariantPlan != null;

    /// <summary>Plán mosta, ktorý čaká na výber typu (alebo null).</summary>
    public CrossingData AwaitingVariantPlan => awaitingVariantPlan;

    /// <summary>
    /// Začne výber typu pre OVERENÝ plán mosta: prvý klik sa zruší a na mape
    /// ostane náhľad plánovaného mosta (typ A), kým hráč nevyberie typ.
    /// </summary>
    public void BeginVariantSelection(CrossingData plan)
    {
        ClearPendingBridgeStart();
        DestroyPreviewObjects();

        awaitingVariantPlan = plan;
        if (plan == null) { previewKey = null; return; }

        previewKey = "SELECT";
        BuildVisuals(plan, true, previewObjects);
    }

    /// <summary>Ukončí výber typu (vybraný typ aj zrušenie) a skryje náhľad.</summary>
    public void EndVariantSelection()
    {
        awaitingVariantPlan = null;
        ClearPreview();
    }

    // =====================================================================
    // NÁHĽAD
    // =====================================================================

    public void UpdateTunnelPreview(Vector2Int hovered)
    {
        previewTouchedFrame = Time.frameCount;

        string key = $"T:{hovered.x},{hovered.y}:{mapVersion}";
        if (key == previewKey) return;

        DestroyPreviewObjects();
        previewKey = key;

        if (ValidateTunnel(hovered, out CrossingData plan, out _))
            BuildVisuals(plan, true, previewObjects);
    }

    public void UpdateBridgePreview(Vector2Int hovered)
    {
        previewTouchedFrame = Time.frameCount;
        if (awaitingVariantPlan != null) return;   // náhľad drží výber typu
        if (!pendingBridgeStart.HasValue)
        {
            if (previewKey != null) { DestroyPreviewObjects(); previewKey = null; }
            return;
        }

        Vector2Int s = pendingBridgeStart.Value;
        string key = $"B:{s.x},{s.y}>{hovered.x},{hovered.y}:{mapVersion}";
        if (key == previewKey) return;

        DestroyPreviewObjects();
        previewKey = key;

        if (ValidateBridge(s, hovered, out CrossingData plan, out _))
            BuildVisuals(plan, true, previewObjects);
    }

    public void ClearPreview()
    {
        if (previewObjects.Count == 0 && previewKey == null) return;
        DestroyPreviewObjects();
        previewKey = null;
    }

    private void DestroyPreviewObjects() => DestroyAll(previewObjects);

    private static void DestroyAll(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) Destroy(list[i]);
        list.Clear();
    }

    // =====================================================================
    // VIZUÁL
    // =====================================================================

    private CrossingSpriteLibrary ResolveLibrary()
    {
        if (spriteLibrary != null) return spriteLibrary;
        return spriteLibrary = CrossingSpriteLibrary.FindFor(Network);
    }

    private void BuildVisuals(CrossingData c, bool preview, List<GameObject> into)
    {
        if (c.type == CrossingType.Bridge) BuildBridgeVisuals(c, preview, into);
        else BuildTunnelVisuals(c, preview, into);
    }

    private Color Tint(bool preview)
    {
        if (!preview) return Color.white;
        Color t = spriteGlobal.previewTint;
        t.a = spriteGlobal.previewAlpha;
        return t;
    }

    private string NamePrefix(bool preview) => (preview ? "Preview_" : "") + Network + "_";

    private void BuildBridgeVisuals(CrossingData c, bool preview, List<GameObject> into)
    {
        CrossingSpriteLibrary lib = ResolveLibrary();
        int len = c.Length;
        bool horizontal = c.axis == CrossingAxis.Horizontal;
        Quaternion baseRot = horizontal ? Quaternion.identity : Quaternion.Euler(0f, -90f, 0f);

        // ── Krátky most (3 tily) jedným roztiahnutým obrázkom ──
        if (lib != null && len == MinBridgeLength)
        {
            Sprite shortSprite = lib.GetBridgeSprite(c.variant, c.axis, CrossingSpriteLibrary.BridgePart.Short, out var shortSt);
            if (shortSprite != null)
            {
                Vector3[] corners = SpanCorners(c, c.startHead, c.endHead);
                into.Add(CreateSpriteObject($"{NamePrefix(preview)}Bridge_{c.id}_Short", shortSprite, corners,
                                            Average(corners), baseRot, len, shortSt, preview));
                CoverHeads(c, preview, spriteGlobal.hideBridgeHeadTextures);
                return;
            }
        }

        bool headSprites = false;
        for (int i = 0; i < len; i++)
        {
            Vector2Int t = c.TileAt(i);
            var part = i == 0 ? CrossingSpriteLibrary.BridgePart.Start
                     : i == len - 1 ? CrossingSpriteLibrary.BridgePart.End
                     : CrossingSpriteLibrary.BridgePart.Middle;

            Sprite sp = null;
            CrossingSpriteSettings st = null;
            if (lib != null) sp = lib.GetBridgeSprite(c.variant, c.axis, part, out st);

            if (sp != null)
            {
                Vector3[] corners = SpanCorners(c, t, t);
                into.Add(CreateSpriteObject($"{NamePrefix(preview)}Bridge_{c.id}_{part}_{i}", sp, corners,
                                            Average(corners), baseRot, 1f, st, preview));
                if (i == 0 || i == len - 1) headSprites = true;
            }
            else
            {
                into.Add(CreateFallbackDeck(c, t, preview));
            }
        }

        if (headSprites) CoverHeads(c, preview, spriteGlobal.hideBridgeHeadTextures);
    }

    /// <summary>
    /// PORTÁLY TUNELA. Sprite NIE JE billboard (ako továrne / mosty), ale leží
    /// NA POVRCHU šikmej dlaždice portálu – presne ako textúra Rail_Tex /
    /// Road_Tex alebo sprite RailHorizontal z TileModelLibrary. Pitch/Yaw/Roll
    /// z CrossingSpriteLibrary sa aplikujú v súradniciach UŽ NAKLONENEJ
    /// dlaždice (viď CrossingSpriteVisual.SetupTileSurface). Sprite nahrádza
    /// textúru dlaždice, preto sa textúra pod ním skryje.
    /// </summary>
    private void BuildTunnelVisuals(CrossingData c, bool preview, List<GameObject> into)
    {
        CrossingSpriteLibrary lib = ResolveLibrary();

        for (int side = 0; side < 2; side++)
        {
            bool isEnd = side == 1;
            Vector2Int head = isEnd ? c.endHead : c.startHead;

            Sprite sp = null;
            CrossingSpriteSettings st = null;
            if (lib != null) sp = lib.GetTunnelPortalSprite(c.axis, isEnd, out st);

            if (sp != null)
            {
                into.Add(CreateTileSurfaceSpriteObject(
                    $"{NamePrefix(preview)}Tunnel_{c.id}_{(isEnd ? "End" : "Start")}",
                    sp, TerrainCorners(head), st, preview));

                // Textúra dlaždice pod portálom sa skryje (sprite ju nahrádza).
                if (!preview) SetCovered(head, true);
            }
            else
            {
                into.Add(CreateFallbackPortal(c, head, isEnd, preview));
            }
        }
    }

    private void CoverHeads(CrossingData c, bool preview, bool hide)
    {
        if (preview || !hide) return;
        SetCovered(c.startHead, true);
        SetCovered(c.endHead, true);
    }

    private static Vector3[] SpanCorners(CrossingData c, Vector2Int a, Vector2Int b)
    {
        int x0 = a.x, z0 = a.y, x1 = b.x + 1, z1 = b.y + 1;
        Vector3 P(int x, int z) => new Vector3(x, c.TrackY(c.axis == CrossingAxis.Horizontal ? x : z), z);
        return new[] { P(x0, z0), P(x1, z0), P(x1, z1), P(x0, z1) };
    }

    private static Vector3[] TerrainCorners(Vector2Int t)
    {
        return new[]
        {
            new Vector3(t.x,     VertexY(t.x, t.y),         t.y),
            new Vector3(t.x + 1, VertexY(t.x + 1, t.y),     t.y),
            new Vector3(t.x + 1, VertexY(t.x + 1, t.y + 1), t.y + 1),
            new Vector3(t.x,     VertexY(t.x, t.y + 1),     t.y + 1),
        };
    }

    private static Vector3 Average(Vector3[] pts)
    {
        Vector3 s = Vector3.zero;
        for (int i = 0; i < pts.Length; i++) s += pts[i];
        return s / Mathf.Max(1, pts.Length);
    }

    private GameObject CreateSpriteObject(string name, Sprite sprite, Vector3[] corners, Vector3 center,
                                          Quaternion baseRot, float worldWidth,
                                          CrossingSpriteSettings st, bool preview)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<SpriteRenderer>();
        var vis = go.AddComponent<CrossingSpriteVisual>();
        vis.Setup(sprite, corners, center, baseRot, worldWidth, st, spriteGlobal, Tint(preview));
        return go;
    }

    private GameObject CreateTileSurfaceSpriteObject(string name, Sprite sprite, Vector3[] tileCorners,
                                                     CrossingSpriteSettings st, bool preview)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<SpriteRenderer>();
        var vis = go.AddComponent<CrossingSpriteVisual>();
        vis.SetupTileSurface(sprite, tileCorners, st, spriteGlobal, Tint(preview));
        return go;
    }

    // ── Náhradná geometria (keď v knižnici chýba sprite) ───────────────────

    private GameObject CreateFallbackDeck(CrossingData c, Vector2Int t, bool preview)
    {
        bool h = c.axis == CrossingAxis.Horizontal;
        float a0 = h ? t.x : t.y;
        float y0 = c.TrackY(a0);
        float y1 = c.TrackY(a0 + 1f);

        Vector3 dir = h ? new Vector3(1f, y1 - y0, 0f) : new Vector3(0f, y1 - y0, 1f);
        Vector3 pos = new Vector3(t.x + 0.5f, (y0 + y1) * 0.5f + 0.11f, t.y + 0.5f);

        Color col = fallbackBridgeColor;
        if (preview) { col = spriteGlobal.previewTint * col; col.a = spriteGlobal.previewAlpha; }

        return CreateBox($"{NamePrefix(preview)}BridgeDeck_{c.id}_{t.x}_{t.y}",
                         pos, Quaternion.LookRotation(dir.normalized, Vector3.up),
                         new Vector3(0.75f, 0.08f, dir.magnitude), col);
    }

    private GameObject CreateFallbackPortal(CrossingData c, Vector2Int head, bool isEnd, bool preview)
    {
        bool h = c.axis == CrossingAxis.Horizontal;
        // Portál stojí na HORNEJ (kopcovej) hrane portálového tile.
        float ex = h ? head.x + (isEnd ? 0f : 1f) : head.x + 0.5f;
        float ez = h ? head.y + 0.5f : head.y + (isEnd ? 0f : 1f);

        Vector3 pos = new Vector3(ex, c.deckY + 0.35f, ez);
        Vector3 size = h ? new Vector3(0.15f, 0.7f, 0.9f) : new Vector3(0.9f, 0.7f, 0.15f);

        Color col = fallbackTunnelColor;
        if (preview) { col = spriteGlobal.previewTint * col; col.a = spriteGlobal.previewAlpha; }

        return CreateBox($"{NamePrefix(preview)}TunnelPortal_{c.id}_{(isEnd ? "End" : "Start")}",
                         pos, Quaternion.identity, size, col);
    }

    private GameObject CreateBox(string name, Vector3 pos, Quaternion rot, Vector3 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);     // nesmie blokovať raycast terénu

        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = size;

        var mr = go.GetComponent<MeshRenderer>();
        Material m = FallbackMaterial(color);
        if (m != null) mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private static Material FallbackMaterial(Color color)
    {
        Color32 key = color;
        if (fallbackMaterials.TryGetValue(key, out Material m) && m != null) return m;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) return null;
        m = new Material(sh) { name = "CrossingFallbackMat", color = color };
        fallbackMaterials[key] = m;
        return m;
    }

    // =====================================================================
    // SAVE / LOAD (volá IndicatrixAPI – jeden blok na sieť)
    // =====================================================================

    // v1: základné dáta, v2: + typ mosta (variant)
    private const int SAVE_BLOCK_VERSION = 2;

    public void WriteSave(BinaryWriter bw)
    {
        bw.Write(SAVE_BLOCK_VERSION);
        bw.Write(crossings.Count);
        foreach (CrossingData c in crossings)
        {
            bw.Write((int)c.type);
            bw.Write((int)c.axis);
            bw.Write(c.startHead.x); bw.Write(c.startHead.y);
            bw.Write(c.endHead.x); bw.Write(c.endHead.y);
            bw.Write(c.startOuterY);
            bw.Write(c.endOuterY);
            bw.Write(c.deckY);
            bw.Write(c.buildCost);
            bw.Write(c.variant);                     // v2
        }
    }

    /// <summary>Zapíše prázdny blok (keď komponent siete v scéne nie je).</summary>
    public static void WriteEmptySave(BinaryWriter bw)
    {
        bw.Write(SAVE_BLOCK_VERSION);
        bw.Write(0);
    }

    /// <summary>Načíta prechody tejto siete. tileGrid s hlavami je už načítaný.</summary>
    public void LoadSave(BinaryReader br)
    {
        ClearAll();

        int blockVersion = br.ReadInt32();
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var c = new CrossingData
            {
                network = Network,
                type = (CrossingType)br.ReadInt32(),
                axis = (CrossingAxis)br.ReadInt32(),
                startHead = new Vector2Int(br.ReadInt32(), br.ReadInt32()),
                endHead = new Vector2Int(br.ReadInt32(), br.ReadInt32()),
                startOuterY = br.ReadSingle(),
                endOuterY = br.ReadSingle(),
                deckY = br.ReadSingle(),
                buildCost = br.ReadUInt32()
            };
            c.variant = blockVersion >= 2 ? Mathf.Clamp(br.ReadInt32(), 0, BridgeVariantCount - 1) : 0;
            if (c.type != CrossingType.Bridge) c.variant = 0;
            c.id = nextId++;
            Register(c, writeHeadTiles: true);
        }

        mapVersion++;
        Debug.Log($"[{GetType().Name}] Načítaných mostov/tunelov: {count}.");
    }

    /// <summary>Preskočí blok (zarovnanie streamu).</summary>
    public static void SkipSave(BinaryReader br)
    {
        int blockVersion = br.ReadInt32();
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            for (int k = 0; k < 6; k++) br.ReadInt32();
            br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
            br.ReadUInt32();
            if (blockVersion >= 2) br.ReadInt32();
        }
    }
}
