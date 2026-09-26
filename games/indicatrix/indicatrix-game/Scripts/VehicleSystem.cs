using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.VehicleStock;

/// <summary>
/// VehicleSystem.cs
///
/// CESTNÝ EKVIVALENT TrainSystem.cs – plne analogický systém pre ROAD
/// dopravu. Funguje úplne autonómne a nezávisle od TrainSystem.
///
/// PARALELIZMUS a DEBOUNCE:
/// ─────────────────────────────────────────────────────────────────────────
/// ✔ A* pathfinding beží na Thread Pool cez Task.Run (AStarPathThreaded).
///   Pracuje s int[] snapshotom road grafu – žiadne Unity API z vlákna.
///   Výsledky sa prenášajú cez ConcurrentQueue, aplikujú sa v Update().
///
/// ✔ OnMapChanged() – DEBOUNCE:
///   Každé volanie len nastaví flag + resetuje timer.
///   Update() odpočítava timer; až po uplynutí DEBOUNCE_DELAY od posledného
///   kliku spustí RoadSnapshot + Task.Run A* pre bežiace vozidlá.
///
/// ✘ UpdateVehicle / pohyb – hlavné vlákno (Unity API, thread-unsafe).
/// ✘ CreateVehicle / RemoveVehicle – hlavné vlákno (GameObject API).
/// ─────────────────────────────────────────────────────────────────────────
///
/// CESTNÉ VOZIDLO – "Distance-Based Path System":
/// ─────────────────────────────────────────────────────────────────────────
/// Najväčší rozdiel oproti TrainSystem: vozidlo je JEDINÝ kváder
/// (žiadne vagóny). Žiadny wagonCount, žiadny wagonSpacing.
///
/// • Trasa = PathData (List<Vector3> bodov + predpočítané kumulatívne dĺžky).
///   Zdroj: A* ako List<Vector2Int> → konvertovaný na waypointy cez
///   BuildWaypointPath (centrá + hrany + krivkové rohy).
/// • Vozidlo má skalárnu hodnotu vehicleDistance (vzdialenosť pozdĺž cesty).
///   Každý frame: vehicleDistance += speed * deltaTime.
/// • Pozícia = PathData.GetPositionAtDistance(d)   → Vector3.Lerp na segmente.
/// • Rotácia = PathData.GetDirectionAtDistance(d)  → Quaternion.LookRotation.
/// • Deterministické: rovnaký vstup → rovnaký výstup každý frame.
///
/// WAYPOINT-BASED MOVEMENT (zhodné s TrainSystem):
///   Trasa nie je center→center. Pre každý prechod A→B sa generuje:
///     A.center → A.edge(smer A→B) → B.edge(smer B→A) → B.center
///   Krivka:    B.entryEdge → B.exitEdge  (priama diagonála ~45°)
///   T-križovatka (RoadSwitch):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge
///     • ODBOČKA: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge
///   Križovatka (RoadCrossroad):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge (90°)
///     • ROHOVÝ: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge (45°)
///   Krivka, odbočka aj rohový prechod používajú VÝHRADNE priame čiarové
///   segmenty spojené lineárnym Lerp-om.
///
/// PREJAZD CEZ SVAH (LevelUp / LevelDown):
///   Identická logika ako v TrainSystem – biliniárna interpolácia výšky
///   povrchu vo waypointoch + hladké zdieľanie rohových vertexov medzi
///   susednými dlaždicami.
///
/// OBRAT SMERU NA STANICI:
///   path.Reverse() + vehicleDistance = totalLength - vehicleDistance
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class VehicleSystem : MonoBehaviour
{
    public static VehicleSystem instance;

    // =====================================================================
    // POMOCNÁ TRIEDA – PathData
    // Ukladá trasu ako Vector3 body + predpočítané kumulatívne vzdialenosti.
    // (Štruktúra zhodná s TrainSystem.PathData – nezávislá kópia, aby
    // VehicleSystem nezávisel typovo od TrainSystem.)
    // =====================================================================

    public class PathData
    {
        public readonly List<Vector3> points;
        public readonly float[] cumulativeLengths;
        public readonly float totalLength;

        public int Count => points.Count;

        public PathData(List<Vector3> pts)
        {
            points = pts;
            cumulativeLengths = new float[pts.Count];
            cumulativeLengths[0] = 0f;
            for (int i = 1; i < pts.Count; i++)
                cumulativeLengths[i] = cumulativeLengths[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            totalLength = pts.Count > 0 ? cumulativeLengths[pts.Count - 1] : 0f;
        }

        /// <summary>
        /// Vráti pozíciu na trase pre zadanú vzdialenosť od začiatku.
        /// Vzdialenosť je upnutá na [0, totalLength].
        /// </summary>
        public Vector3 GetPositionAtDistance(float dist)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];

            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            float segStart = cumulativeLengths[lo];
            float segEnd = cumulativeLengths[hi];
            float segLen = segEnd - segStart;

            if (segLen < 1e-6f) return points[hi];

            float localT = (dist - segStart) / segLen;
            return Vector3.Lerp(points[lo], points[hi], localT);
        }

        /// <summary>
        /// Vráti normalizovaný smer pohybu na zadanej vzdialenosti.
        /// </summary>
        public Vector3 GetDirectionAtDistance(float dist)
        {
            if (points.Count < 2) return Vector3.forward;

            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            Vector3 dir = points[hi] - points[lo];
            return dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.forward;
        }

        public PathData Reversed()
        {
            var rev = new List<Vector3>(points);
            rev.Reverse();
            return new PathData(rev);
        }

        /// <summary>
        /// Vráti sklon (gradient) trasy na zadanej vzdialenosti – pomer
        /// vertikálnej zmeny výšky k horizontálnej vzdialenosti segmentu,
        /// v ktorom dist leží. Kladná hodnota = stúpanie (do kopca),
        /// záporná = klesanie (z kopca), nula = rovina. Používa sa v
        /// TargetSpeedForGrade() na spomalenie do kopca / zrýchlenie z kopca.
        /// </summary>
        public float GetGradeAtDistance(float dist)
        {
            if (points.Count < 2) return 0f;
            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            Vector3 a = points[lo], b = points[hi];
            float dy = b.y - a.y;
            float horizontal = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            return horizontal > 1e-5f ? dy / horizontal : 0f;
        }
    }

    // =====================================================================
    // DÁTOVÉ ŠTRUKTÚRY
    // =====================================================================

    /// <summary>
    /// Stav životného cyklu vozidla v danom depe – z pohľadu UI.
    ///
    /// Analógia k požadovanému stavu pre vlak. Slúži na to, aby UI okno
    /// (DepotRoadConstructionMenuUI) vedelo rozlíšiť tri situácie a podľa
    /// nich nastaviť StatusTypeTextCaption:
    ///
    ///   • NotCreated – vozidlo v tomto depe ešte nikdy nebolo vytvorené
    ///                  (pred prvým kliknutím na DCCreateVehicleButton).
    ///   • Created    – vozidlo existuje (po DCCreateVehicleButton).
    ///                  StatusTypeTextCaption potom zobrazuje "Running"
    ///                  alebo "Stopped" podľa isRunning.
    ///   • Removed    – vozidlo existovalo, ale bolo zmazané cez
    ///                  DCRemoveVehicleButton.
    ///
    /// Pre StatusTypeTextCaption sa NotCreated aj Removed prejavia rovnako –
    /// textom "NO VEHICLE". Stav je rozlíšený samostatne, lebo neskoršie
    /// UI (detail vozidla) môže obidve situácie odlíšiť, ak bude treba.
    /// </summary>
    public enum VehicleLifecycleState
    {
        NotCreated,
        Created,
        Removed
    }

    public class VehicleData
    {
        public int depotX, depotZ;

        /// <summary>Skrytá kocka (Renderer off) – pohybová logika.</summary>
        public GameObject vehicleObject;

        /// <summary>Vizuálne vozidlo (MAGENTA kváder). 1 kus, žiadne vagóny.</summary>
        public GameObject vehicleBody;

        // ------------------------------------------------------------------
        // DÁTOVÁ ŠTRUKTÚRA VOZIDLA (VehicleStock.cs)
        //
        // VehicleData = pohybový/herný stav (cesta, rýchlosť, stanice...).
        // VehicleInstance = "čo je vozidlo" – nemenné parametre cez Spec
        // (Cost, Speed, Power...) + premenlivý stav kusu (Age,
        // CurrentCapacity). Obidve veci sú zámerne oddelené, presne tak ako
        // TrainData (TrainSystem) vs. TrainInstance (TrainStock).
        //
        // Inštancia vznikne v CreateVehicle a uvoľní sa v RemoveVehicle –
        // viď tieto metódy. UI okno s detailmi vozidla bude tieto údaje
        // len ČÍTAŤ cez vd.vehicleInstance.
        // ------------------------------------------------------------------

        /// <summary>
        /// Dátová štruktúra vozidla (predloha + premenlivý stav).
        /// Nastavená v CreateVehicle, vynulovaná v RemoveVehicle.
        /// </summary>
        public VehicleInstance vehicleInstance;

        // ------------------------------------------------------------------
        // DISTANCE-BASED PATH STATE
        // ------------------------------------------------------------------

        public PathData activePath;
        public float vehicleDistance;

        /// <summary>
        /// AKTUÁLNA rýchlosť vozidla (jednotky/sekunda). Mení sa plynulo
        /// každý frame smerom k cieľovej rýchlosti (podľa sklonu cesty a
        /// blízkosti konca trasy) rýchlosťou acceleration/deceleration.
        /// </summary>
        public float vehicleSpeed;

        /// <summary>
        /// Maximálna rýchlosť vozidla NA ROVINE (jednotky/sekunda), odvodená
        /// z VehicleSpec.Speed (viď VehicleSystem.SpeedUnitsPerSecond).
        /// Nemení sa počas jazdy, iba pri vytvorení vozidla
        /// (ComputeMotionParams).
        /// </summary>
        public float maxSpeed;

        /// <summary>Zrýchlenie vozidla (jednotky/sekunda²) – z pomeru Power/Weight.</summary>
        public float acceleration;

        /// <summary>Spomalenie/brzdenie vozidla (jednotky/sekunda²) – silnejšie než acceleration.</summary>
        public float deceleration;

        // ------------------------------------------------------------------
        // Stavové polia (zhodné s TrainData, ale bez vagónov)
        // ------------------------------------------------------------------
        public List<Vector2Int> stations;
        public int currentStationIndex;
        public bool isRunning;
        public bool isWaiting;
        public bool isReturningToDepot;
        public bool isAtDepot;
        public bool reverseDirection;
        public List<Vector2Int> currentPath;
        public int pathIndex;
        public Vector2Int currentTile;
        public float moveTimer;
        public float waitTimer;
        public bool isComputingPath;
        public List<Vector2Int> pendingPath;
        public bool isGoingToDepotViaStation;
        public bool isStoppedAwaitingDepotReturn;
        public bool pendingReturnToDepot;

        // ------------------------------------------------------------------
        // OBCHOD NA STANICI (VehicleTradeSystem)
        // ------------------------------------------------------------------
        /// <summary>
        /// True, ak v AKTUÁLNEJ zastávke na stanici už prebehol pokus o
        /// transakciu (výmenu tovaru). Bráni tomu, aby sa obchod spustil
        /// každý frame – spustí sa práve raz, po 2 s čakania. Resetuje sa
        /// pri každom novom príchode na cieľovú stanicu (OnPathComplete).
        ///
        /// Analógia k TrainData.tradeDoneAtStation.
        /// </summary>
        public bool tradeDoneAtStation;

        public int pathIndexAtPathStart;
        public int[] waypointToTileIdx;

        /// <summary>
        /// CESTNÉ MOSTY / TUNELY: pre každý tile SUB-PATH index vstupnej hlavy
        /// prechodu, ak tile leží VNÚTRI mosta/tunela; inak -1 (celé pole null,
        /// ak trasa prechod nemá). Kým je vozidlo vo vnútri prechodu, currentTile
        /// ostáva na VSTUPNEJ hlave (tile pod mostom môže niesť inú cestu).
        /// </summary>
        public int[] subPathEntryHead;

        public VehicleData(int dx, int dz)
        {
            depotX = dx; depotZ = dz;
            stations = new List<Vector2Int>();
            currentStationIndex = 0;
            isRunning = false; isWaiting = false;
            isReturningToDepot = false; isAtDepot = true;
            reverseDirection = false;
            currentPath = new List<Vector2Int>();
            pathIndex = 0;
            currentTile = new Vector2Int(dx, dz);
            moveTimer = 0f; waitTimer = 0f;
            isComputingPath = false;
            pendingPath = null;
            isGoingToDepotViaStation = false;
            isStoppedAwaitingDepotReturn = false;
            pendingReturnToDepot = false;
            tradeDoneAtStation = false;

            activePath = null;
            vehicleDistance = 0f;
            vehicleSpeed = 0f;
            maxSpeed = 0f;
            acceleration = 0f;
            deceleration = 0f;
            pathIndexAtPathStart = 0;
            waypointToTileIdx = null;
        }
    }

    // =====================================================================
    // VÝSLEDKOVÁ FRONTA
    // =====================================================================

    enum PathDestination { Station, Depot, ReturnViaStation }

    struct PathResult
    {
        public int depotKey;
        public List<Vector2Int> path;
        public bool isReturnToDepot;
        public PathDestination destination;
    }

    public enum ReturnToDepotResult { Dispatched, StoppedAwaitingSecondR, Error }

    readonly System.Collections.Concurrent.ConcurrentQueue<PathResult> _pendingPathResults
        = new System.Collections.Concurrent.ConcurrentQueue<PathResult>();

    // =====================================================================
    // DEBOUNCE
    // =====================================================================

    const float DEBOUNCE_DELAY = 0.4f;
    bool _mapChangePending = false;
    float _mapChangedDebounceTimer = 0f;

    // =====================================================================
    // SNAPSHOT ROAD GRIDU
    // =====================================================================

    const int GRID_SIZE = 256;

    /// <summary>
    /// Snapshot road časti tileGrid. Pre tiles, ktoré nie sú ROAD kategórie,
    /// uloží tileID=0 → A* ich pokladá za nepriechodné. Tým je VehicleSystem
    /// úplne izolovaný od RAIL grafu, hoci obidva systémy zdieľajú jeden
    /// tile grid v IndicatrixAPI.
    /// </summary>
    struct TileGridSnapshot
    {
        public int[] tileIDs;
        public int[] connections;

        // CESTNÉ MOSTY / TUNELY (RoadCrossingSystem) – skokové hrany medzi hlavami:
        //   jumpTarget[i] – index partnerskej hlavy, -1 = tile nie je hlava
        //   jumpDir[i]    – DirectionMask smerom DO prechodu (0 = žiadny)
        //   jumpCost[i]   – cena skoku pre A* (dĺžka prechodu - 1)
        public int[] jumpTarget;
        public int[] jumpDir;
        public int[] jumpCost;
    }

    TileGridSnapshot SnapshotTileGrid()
    {
        var snap = new TileGridSnapshot
        {
            tileIDs = new int[GRID_SIZE * GRID_SIZE],
            connections = new int[GRID_SIZE * GRID_SIZE]
        };

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                // GetRoadTileByIndex vracia LEN ROAD tiles; zmiešanú križovatku
                // RAIL + ROAD ukáže ako priamu cestu po jej osi (vozidlo prejde krížom).
                var td = IndicatrixAPI.instance.GetRoadTileByIndex(x, z);
                int idx = z * GRID_SIZE + x;
                if (td.category == IndicatrixAPI.TileCategory.Road)
                {
                    snap.tileIDs[idx] = td.tileID;
                    snap.connections[idx] = (int)td.connections;
                }
                else
                {
                    // Ne-ROAD tiles sa do road snapshotu nezapočítavajú.
                    snap.tileIDs[idx] = 0;
                    snap.connections[idx] = 0;
                }
            }
        }

        // Cestné mosty a tunely – skokové hrany (len ak nejaké existujú).
        // RoadCrossingSystem pozná LEN cestné prechody → izolácia od RAIL ostáva.
        var crossings = RoadCrossingSystem.instance;
        if (crossings != null && crossings.Count > 0)
        {
            int n = GRID_SIZE * GRID_SIZE;
            snap.jumpTarget = new int[n];
            snap.jumpDir = new int[n];
            snap.jumpCost = new int[n];
            for (int i = 0; i < n; i++) snap.jumpTarget[i] = -1;
            crossings.FillSnapshot(snap.jumpTarget, snap.jumpDir, snap.jumpCost, GRID_SIZE);
        }
        return snap;
    }

    // =====================================================================
    // KONŠTANTY A STAV
    // =====================================================================

    Dictionary<int, VehicleData> vehicles = new Dictionary<int, VehicleData>();

    /// <summary>
    /// Stav životného cyklu vozidla per depo.
    ///
    /// Prečo samostatný slovník a nie pole na VehicleData:
    /// VehicleData v `vehicles` existuje IBA kým vozidlo existuje – po
    /// RemoveVehicle sa záznam z `vehicles` odstráni. Stav "Removed" sa
    /// teda nemá kam uložiť. Tento slovník prežíva odstránenie vozidla a
    /// pamätá si, či vozidlo v danom depe ešte nebolo vytvorené
    /// (depo v slovníku chýba → NotCreated) alebo bolo zmazané (Removed).
    /// </summary>
    Dictionary<int, VehicleLifecycleState> vehicleLifecycle
        = new Dictionary<int, VehicleLifecycleState>();

    /// <summary>
    /// Vráti stav životného cyklu vozidla pre dané depo.
    /// Ak depo nie je v evidencii, vozidlo ešte nikdy nebolo vytvorené.
    /// </summary>
    public VehicleLifecycleState GetVehicleLifecycleState(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return vehicleLifecycle.TryGetValue(key, out var state)
            ? state
            : VehicleLifecycleState.NotCreated;
    }

    /// <summary>Čas prechodu jednej dlaždice v sekundách.</summary>
    const float MOVE_TIME = 2.0f;
    const float STATION_WAIT = 10.0f;
    const float BREAK_WAIT = 1.0f;

    // =====================================================================
    // FYZIKA POHYBU – rýchlosť, zrýchlenie/spomalenie, sklon cesty
    // (zhodné s TrainSystem – viď tam podrobný komentár k zdôvodneniu)
    // =====================================================================

    /// <summary>Referenčná hodnota VehicleSpec.Speed zodpovedajúca pôvodnému tempu (1 dlaždica / MOVE_TIME s, na rovine).</summary>
    const float SPEED_REFERENCE = 100f;

    /// <summary>Ladiaca konštanta: prevod pomeru Power/Weight na zrýchlenie (jednotky/s²).</summary>
    const float ACCEL_TUNING = 0.02f;

    /// <summary>Brzdenie je vždy silnejšie než rozjazd.</summary>
    const float BRAKE_MULTIPLIER = 1.6f;

    /// <summary>Absolútne minimum zrýchlenia/spomalenia.</summary>
    const float MIN_ACCEL = 0.15f;

    /// <summary>Citlivosť spomalenia do kopca (na jednotku sklonu cesty).</summary>
    const float UPHILL_GRADE_SENSITIVITY = 1.3f;

    /// <summary>Citlivosť zrýchlenia z kopca (menšia než do kopca – bezpečnostný limit).</summary>
    const float DOWNHILL_GRADE_SENSITIVITY = 0.6f;

    /// <summary>Aj na najprudšom stúpaní neklesne cieľová rýchlosť pod tento podiel z maxSpeed.</summary>
    const float MIN_UPHILL_SPEED_FRACTION = 0.35f;

    /// <summary>Aj na najprudšom klesaní nevystúpi cieľová rýchlosť nad tento násobok maxSpeed.</summary>
    const float MAX_DOWNHILL_SPEED_FACTOR = 1.35f;

    /// <summary>
    /// Minimálna "dobiehacia" rýchlosť tesne pred cieľom trasy – zaručí, že
    /// vozidlo pri brzdení vždy reálne dorazí na koniec trasy (viď rovnaký
    /// komentár v TrainSystem).
    /// </summary>
    const float MIN_ARRIVAL_CRAWL_SPEED = 0.05f;

    /// <summary>
    /// Oneskorenie od príchodu na cieľovú stanicu, po ktorom sa vykoná
    /// výmena tovaru (VehicleTradeSystem). Zadanie: štandardné čakanie je
    /// 10 s (STATION_WAIT), transakcia prebehne po 2 s od príchodu.
    ///
    /// Spúšťacia podmienka v UpdateVehicle:
    /// (STATION_WAIT - waitTimer) >= TRADE_DELAY, t.j. po 2 s čakania.
    /// </summary>
    const float TRADE_DELAY = 2.0f;

    /// <summary>Rozmery kvádra vozidla.</summary>
    static readonly Vector3 VEHICLE_SCALE = new Vector3(0.3f, 0.3f, 0.7f);

    // =====================================================================
    // KNIŽNICA 3D MODELOV (voliteľná)
    // ---------------------------------------------------------------------
    // Referencia na komponent VehicleModelLibrary, ktorý drží voliteľné
    // prefab-y skutočných 3D modelov pre jednotlivé typy vozidiel.
    //
    // LOGIKA NAHRADENIA (zadanie):
    //   • Ak je pre daný typ vozidla v knižnici priradený prefab
    //         → CreateVehiclePart vytvorí inštanciu tohto modelu namiesto kvádra.
    //   • Ak prefab priradený NIE JE (null)
    //         → vytvorí sa pôvodný MAGENTA kváder – presne ako doteraz.
    //
    // Referencia je voliteľná: ak nie je priradená v Inspectore, systém ju
    // dohľadá cez VehicleModelLibrary.instance, prípadne FindObjectOfType.
    // Ak knižnica v scéne vôbec nie je, všetko ostane na pôvodných kvádroch.
    // =====================================================================
    [SerializeField] private VehicleModelLibrary modelLibrary;

    // =====================================================================
    // PRISPÔSOBENIE VEĽKOSTI 3D MODELOV DLAŽDICI (fit-to-tile)
    // ---------------------------------------------------------------------
    // Cestný ekvivalent riešenia z TrainSystem. Prefab-y dlaždíc sa v
    // IndicatrixAPI proporčne škálujú na 1 dlaždicu, prefab-y vozidiel doteraz
    // NIE (scale sa zámerne nechával autorský). Modely v reálnych metroch
    // (nákladiak ~8 m = 8 Unity jednotiek = 8 dlaždíc) sú preto obrovské.
    // =====================================================================

    [Header("3D modely vozidiel – prispôsobenie veľkosti")]

    [Tooltip("Zapnuté = inštancia prefabu z VehicleModelLibrary sa proporčne " +
             "preškáluje na rozmer dlaždice a vycentruje na os cesty. " +
             "Vypnuté = pôvodné správanie (model si nesie vlastnú mierku).")]
    [SerializeField] private bool fitVehicleModelsToTile = true;

    [Tooltip("Cieľová DĹŽKA vozidla v dlaždiciach (1.0 = celá dlaždica). " +
             "Pôvodný kváder má 0.7.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float vehicleModelLength = 0.7f;

    [Tooltip("Maximálna ŠÍRKA vozidla v dlaždiciach. Ak je model po " +
             "preškálovaní na dĺžku širší, doškáluje sa nadol. " +
             "Pôvodný kváder má 0.3.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float vehicleModelMaxWidth = 0.35f;

    [Tooltip("Zvislý posun modelu voči ceste. Trasa vedie ROAD_OFFSET_Y (0.3) " +
             "nad terénom a kváder na nej mal svoj STRED, takže jeho spodok " +
             "končil o polovicu výšky nižšie. Predvolené -0.15 usadí model " +
             "presne tam, kde bol spodok kvádra.")]
    [SerializeField] private float vehicleModelYOffset = -0.15f;

    [Tooltip("Diagnostika: vypíše do konzoly namerané rozmery pred/po fite a " +
             "výsledný stred (očakávané center.x≈0 a center.z≈0).")]
    [SerializeField] private bool logVehicleFit = false;

    [Tooltip("Diagnostika: k modelu pridá CYAN referenčný kváder v presne tej " +
             "polohe a veľkosti, akú mal pôvodný kváder – teda vycentrovaný na " +
             "pivot. Rozlíši, či je vychýlený MODEL voči pivotu, alebo celý " +
             "PIVOT voči ceste.")]
    [SerializeField] private bool debugShowReferenceCube = false;

    void Awake()
    {
        instance = this;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    int DepotKey(int x, int z) => x * 10000 + z;

    /// <summary>Prevedie abstraktnú hodnotu VehicleSpec.Speed na jednotky/sekundu (viď SPEED_REFERENCE).</summary>
    float SpeedUnitsPerSecond(int specSpeed) => (specSpeed / SPEED_REFERENCE) * (1f / MOVE_TIME);

    /// <summary>
    /// Vypočíta a uloží maxSpeed/acceleration/deceleration vozidla z jeho
    /// VehicleSpec (Speed/Weight/Power). Volá sa raz pri vytvorení vozidla
    /// (CreateVehicle) – vozidlo (na rozdiel od vlaku) nemá vagóny, takže
    /// stačí čítať priamo z vehicleInstance.
    /// </summary>
    void ComputeMotionParams(VehicleData vd)
    {
        if (vd?.vehicleInstance == null) return;

        VehicleInstance v = vd.vehicleInstance;
        int weight = Mathf.Max(v.Weight, 1);

        vd.maxSpeed = SpeedUnitsPerSecond(v.Speed);

        float powerToWeight = (float)v.Power / weight;
        vd.acceleration = Mathf.Max(MIN_ACCEL, powerToWeight * ACCEL_TUNING);
        vd.deceleration = Mathf.Max(MIN_ACCEL, vd.acceleration * BRAKE_MULTIPLIER);
    }

    /// <summary>
    /// Cieľová rýchlosť vozidla pre daný sklon cesty (grade) – do kopca sa
    /// znižuje, z kopca zvyšuje (v medziach MIN_UPHILL_SPEED_FRACTION /
    /// MAX_DOWNHILL_SPEED_FACTOR). Viď TrainSystem.TargetSpeedForGrade.
    /// </summary>
    float TargetSpeedForGrade(VehicleData vd, float grade)
    {
        float factor = (grade > 0f)
            ? Mathf.Max(MIN_UPHILL_SPEED_FRACTION, 1f - grade * UPHILL_GRADE_SENSITIVITY)
            : Mathf.Min(MAX_DOWNHILL_SPEED_FACTOR, 1f - grade * DOWNHILL_GRADE_SENSITIVITY);

        return vd.maxSpeed * factor;
    }

    Vector3 TileCenter(Vector2Int tile)
    {
        float y = GetTileSurfaceY(tile, 0.5f, 0.5f) + ROAD_OFFSET_Y;
        return new Vector3(tile.x + 0.5f, y, tile.y + 0.5f);
    }

    float GetTerrainY(int x, int z)
    {
        try
        {
            int width = TerrainManager.instance.terrainWidth + 1;
            int index = z * width + x;
            if (index >= 0 && index < TerrainManager.instance.coordsF.Length)
                return TerrainManager.instance.coordsF[index].y;
        }
        catch { }
        return 0f;
    }

    /// <summary>Vertikálny offset vozovky nad povrchom terénu.</summary>
    const float ROAD_OFFSET_Y = 0.3f;

    /// <summary>
    /// Biliniárna interpolácia výšky terénu vnútri jednej dlaždice medzi
    /// 4 rohovými vertexmi. Logika zhodná s TrainSystem.GetTileSurfaceY.
    ///
    /// PREČO TOTO POTREBUJEME (kopce, LevelUp / LevelDown):
    ///   Bez tejto interpolácie by vozidlo na svahu ostalo vodorovne a
    ///   na hrane medzi dlaždicami "skočilo" na novú výšku → 90° schod.
    ///   S biliniárnou interpoláciou má každý waypoint správnu výšku na
    ///   šikmej ploche dlaždice → pohyb cez svah je plynulý.
    ///
    /// HLADKOSŤ NA HRANÁCH MEDZI DLAŽDICAMI:
    ///   A.right-edge a B.left-edge zdieľajú ten istý pár rohových vertexov,
    ///   takže interpolovaná Y vychádza identická → žiadny schod, žiadny lom.
    /// </summary>
    float GetTileSurfaceY(Vector2Int tile, float u, float v)
    {
        float h00 = GetTerrainY(tile.x, tile.y);
        float h10 = GetTerrainY(tile.x + 1, tile.y);
        float h01 = GetTerrainY(tile.x, tile.y + 1);
        float h11 = GetTerrainY(tile.x + 1, tile.y + 1);

        float omu = 1f - u;
        float omv = 1f - v;

        return omu * omv * h00
             + u * omv * h10
             + omu * v * h01
             + u * v * h11;
    }

    /// <summary>
    /// Vráti tileID pre danú ROAD dlaždicu. Pre tiles, ktoré nie sú ROAD
    /// kategórie (alebo sú mimo gridu), vracia -1 / 0. Slúži ako sentinel
    /// pre VehicleSystem – RAIL tiles sú pre nás "neviditeľné".
    /// </summary>
    int GetRoadTileID(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE) return -1;
        var td = IndicatrixAPI.instance.GetRoadTileByIndex(x, z);   // ROAD + zmiešaná križovatka
        if (td.category != IndicatrixAPI.TileCategory.Road) return 0;
        return td.tileID;
    }

    bool IsPassable(int x, int z)
    {
        int id = GetRoadTileID(x, z);
        return id == 1 || id == 2 || id == 3;
    }

    // =====================================================================
    // DIRECTION HELPERS – mapovanie Vector2Int <-> DirectionMask
    // =====================================================================

    static IndicatrixAPI.DirectionMask DirFromStep(Vector2Int step)
    {
        if (step.x == 1 && step.y == 0) return IndicatrixAPI.DirectionMask.Right;
        if (step.x == -1 && step.y == 0) return IndicatrixAPI.DirectionMask.Left;
        if (step.x == 0 && step.y == 1) return IndicatrixAPI.DirectionMask.Top;
        if (step.x == 0 && step.y == -1) return IndicatrixAPI.DirectionMask.Bottom;
        return IndicatrixAPI.DirectionMask.None;
    }

    static IndicatrixAPI.DirectionMask GetDirection(Vector2Int from, Vector2Int to)
    {
        return DirFromStep(to - from);
    }

    /// <summary>
    /// Pre danú dlaždicu (x, z) a smer 'dir' vráti svetový bod uprostred
    /// danej hrany dlaždice. Logika zhodná s TrainSystem.EdgePoint, len
    /// s ROAD_OFFSET_Y namiesto TRACK_OFFSET_Y.
    /// </summary>
    Vector3 EdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f, GetTileSurfaceY(tile, 1.0f, 0.5f) + ROAD_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f, GetTileSurfaceY(tile, 0.0f, 0.5f) + ROAD_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 1.0f) + ROAD_OFFSET_Y, fz + 1.0f);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 0.0f) + ROAD_OFFSET_Y, fz + 0.0f);
            default: return TileCenter(tile);
        }
    }

    /// <summary>
    /// Krivka (Road) = presne 2 nastavené smery, ktoré NIE SÚ proti-smery.
    /// </summary>
    static bool IsCurveTile(IndicatrixAPI.DirectionMask conns)
    {
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        if (bits != 2) return false;

        bool horiz = (conns & (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right))
                       == (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right);
        bool vert = (conns & (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom))
                       == (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom);

        return !horiz && !vert;
    }

    /// <summary>
    /// T-križovatka / Y-rozdvojenie (RoadSwitch) – 3 spojenia, plne obojsmerné.
    /// </summary>
    static bool IsSwitchTile(IndicatrixAPI.DirectionMask conns)
    {
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        return bits == 3;
    }

    /// <summary>
    /// Križovatka (RoadCrossroad) – všetky 4 smery.
    /// </summary>
    static bool IsCrossroadTile(IndicatrixAPI.DirectionMask conns)
    {
        const IndicatrixAPI.DirectionMask ALL =
            IndicatrixAPI.DirectionMask.Left
          | IndicatrixAPI.DirectionMask.Right
          | IndicatrixAPI.DirectionMask.Top
          | IndicatrixAPI.DirectionMask.Bottom;
        return (conns & ALL) == ALL;
    }

    /// <summary>
    /// True ak sú dva smery navzájom kolmé (jeden horizontálny + jeden vertikálny).
    /// </summary>
    static bool IsPerpendicular(IndicatrixAPI.DirectionMask a, IndicatrixAPI.DirectionMask b)
    {
        bool aHoriz = a == IndicatrixAPI.DirectionMask.Left || a == IndicatrixAPI.DirectionMask.Right;
        bool aVert = a == IndicatrixAPI.DirectionMask.Top || a == IndicatrixAPI.DirectionMask.Bottom;
        bool bHoriz = b == IndicatrixAPI.DirectionMask.Left || b == IndicatrixAPI.DirectionMask.Right;
        bool bVert = b == IndicatrixAPI.DirectionMask.Top || b == IndicatrixAPI.DirectionMask.Bottom;
        return (aHoriz && bVert) || (aVert && bHoriz);
    }

    /// <summary>
    /// Vnútorný bod pre 45° turnout / rohový prejazd. Zhodné s TrainSystem.
    /// </summary>
    Vector3 SwitchInnerEdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir, float t)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f - t,
                    GetTileSurfaceY(tile, 1.0f - t, 0.5f) + ROAD_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f + t,
                    GetTileSurfaceY(tile, 0.0f + t, 0.5f) + ROAD_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 1.0f - t) + ROAD_OFFSET_Y,
                    fz + 1.0f - t);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 0.0f + t) + ROAD_OFFSET_Y,
                    fz + 0.0f + t);
            default: return TileCenter(tile);
        }
    }

    // =====================================================================
    // CAN MOVE – validácia smeru pre A* (thread-safe, snapshot)
    // =====================================================================

    /// <summary>
    /// Validácia A* prechodu z 'fromTile' na 'toTile' v smere 'direction'.
    /// Identické pravidlá ako TrainSystem.CanMove – pracuje so snapshot
    /// poľami, ktoré pre VehicleSystem obsahujú IBA ROAD tiles (RAIL tiles
    /// majú tileID=0 v snapshot, takže sú nepriechodné).
    /// </summary>
    static bool CanMove(int[] tileIDs, int[] conns, Vector2Int fromTile, Vector2Int toTile,
                        IndicatrixAPI.DirectionMask direction)
    {
        if (toTile.x < 0 || toTile.x >= GRID_SIZE || toTile.y < 0 || toTile.y >= GRID_SIZE)
            return false;
        if (fromTile.x < 0 || fromTile.x >= GRID_SIZE || fromTile.y < 0 || fromTile.y >= GRID_SIZE)
            return false;

        int toIdx = toTile.y * GRID_SIZE + toTile.x;
        int toID = tileIDs[toIdx];
        if (!(toID == 1 || toID == 2 || toID == 3)) return false;

        int fromIdx = fromTile.y * GRID_SIZE + fromTile.x;
        IndicatrixAPI.DirectionMask fromConn = (IndicatrixAPI.DirectionMask)conns[fromIdx];
        IndicatrixAPI.DirectionMask toConn = (IndicatrixAPI.DirectionMask)conns[toIdx];

        if ((fromConn & direction) == 0) return false;

        IndicatrixAPI.DirectionMask oppositeDir = IndicatrixAPI.Opposite(direction);
        if ((toConn & oppositeDir) == 0) return false;

        return true;
    }

    /// <summary>
    /// CanMove rozšírený o pravidlá CESTNÝCH MOSTOV a TUNELOV (analógia
    /// k TrainSystem):
    ///   • z hlavy sa smerom DO prechodu nedá ísť bežným krokom – len skokom,
    ///   • do hlavy sa nedá vojsť bežným krokom zo strany prechodu.
    /// Bez prechodov (jumpDir == null) je správanie totožné s CanMove.
    /// </summary>
    static bool CanMove(TileGridSnapshot snap, Vector2Int fromTile, Vector2Int toTile,
                        IndicatrixAPI.DirectionMask direction)
    {
        if (!CanMove(snap.tileIDs, snap.connections, fromTile, toTile, direction))
            return false;

        if (snap.jumpDir != null)
        {
            int fromIdx = fromTile.y * GRID_SIZE + fromTile.x;
            int toIdx = toTile.y * GRID_SIZE + toTile.x;

            if ((snap.jumpDir[fromIdx] & (int)direction) != 0) return false;
            if ((snap.jumpDir[toIdx] & (int)IndicatrixAPI.Opposite(direction)) != 0) return false;
        }
        return true;
    }

    /// <summary>
    /// CESTNÉ MOSTY / TUNELY: bod na hrane (alebo v strede pri dir == None)
    /// dlaždice prechodu. X/Z ako EdgePoint/TileCenter, Y z profilu prechodu
    /// (CrossingData.TrackY) + ROAD_OFFSET_Y.
    /// </summary>
    Vector3 CrossingTrackPoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir,
                               CrossingSystemBase.CrossingData crossing)
    {
        float x = tile.x + 0.5f, z = tile.y + 0.5f;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right: x = tile.x + 1f; break;
            case IndicatrixAPI.DirectionMask.Left: x = tile.x; break;
            case IndicatrixAPI.DirectionMask.Top: z = tile.y + 1f; break;
            case IndicatrixAPI.DirectionMask.Bottom: z = tile.y; break;
        }

        float axisCoord = crossing.axis == CrossingAxis.Horizontal ? x : z;
        return new Vector3(x, crossing.TrackY(axisCoord) + ROAD_OFFSET_Y, z);
    }

    // =====================================================================
    // BUILD WAYPOINT PATH – konvertuje List<Vector2Int> na waypointy
    // (logika identická s TrainSystem.BuildWaypointPath – pozri tam podrobné
    //  komentáre k pravidlám pre krivky, výhybky, križovatky a tile-boundary
    //  semantike)
    // =====================================================================

    void BuildWaypointPath(List<Vector2Int> tilePath, out List<Vector3> waypoints,
                           out List<int> tileIdxPerWaypoint)
    {
        var wp = new List<Vector3>();
        var tIdx = new List<int>();

        if (tilePath == null || tilePath.Count == 0)
        {
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        if (tilePath.Count == 1)
        {
            wp.Add(TileCenter(tilePath[0]));
            tIdx.Add(0);
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        // Lokálna funkcia – deduplikuje len waypointy z rovnakej dlaždice
        // (intra-tile duplikáty). Hraničné waypointy A.exitEdge a B.entryEdge
        // sa zachovajú ako 2 samostatné waypointy aj keď sa v priestore kryjú.
        void AddWaypoint(Vector3 pt, int tileIdx)
        {
            if (wp.Count > 0)
            {
                Vector3 last = wp[wp.Count - 1];
                int lastTileIdx = tIdx[tIdx.Count - 1];
                bool sameTile = (lastTileIdx == tileIdx);
                bool samePos = (pt - last).sqrMagnitude < 1e-8f;
                if (sameTile && samePos) return;
            }
            wp.Add(pt);
            tIdx.Add(tileIdx);
        }

        // CESTNÉ MOSTY / TUNELY – ktoré tily trasy patria prechodu (hlavy + vnútro).
        CrossingSystemBase.CrossingData[] tileCrossing =
            RoadCrossingSystem.instance != null
                ? RoadCrossingSystem.instance.ResolvePathCrossings(tilePath, out _)
                : null;

        for (int i = 0; i < tilePath.Count; i++)
        {
            Vector2Int tile = tilePath[i];
            var conns = IndicatrixAPI.instance.GetRoadTileByIndex(tile.x, tile.y).connections;   // zmiešaná križovatka = len os cesty

            bool isFirst = (i == 0);
            bool isLast = (i == tilePath.Count - 1);

            IndicatrixAPI.DirectionMask outDir = IndicatrixAPI.DirectionMask.None;
            if (!isLast) outDir = GetDirection(tile, tilePath[i + 1]);

            IndicatrixAPI.DirectionMask inDir = IndicatrixAPI.DirectionMask.None;
            if (!isFirst) inDir = IndicatrixAPI.Opposite(GetDirection(tilePath[i - 1], tile));

            // ── MOST / TUNEL: hlava aj vnútro sú PRIAME dlaždice, Y podľa profilu
            //    prechodu. PRED detekciou kriviek/križovatiek – tile pod mostom môže
            //    niesť inú cestu, ktorej connections tu nesmú zavážiť.
            if (tileCrossing != null && tileCrossing[i] != null)
            {
                var crossing = tileCrossing[i];
                if (isFirst)
                {
                    AddWaypoint(CrossingTrackPoint(tile, IndicatrixAPI.DirectionMask.None, crossing), i);
                    AddWaypoint(CrossingTrackPoint(tile, outDir, crossing), i);
                }
                else if (isLast)
                {
                    AddWaypoint(CrossingTrackPoint(tile, inDir, crossing), i);
                    AddWaypoint(CrossingTrackPoint(tile, IndicatrixAPI.DirectionMask.None, crossing), i);
                }
                else
                {
                    AddWaypoint(CrossingTrackPoint(tile, inDir, crossing), i);
                    AddWaypoint(CrossingTrackPoint(tile, IndicatrixAPI.DirectionMask.None, crossing), i);
                    AddWaypoint(CrossingTrackPoint(tile, outDir, crossing), i);
                }
                continue;
            }

            // ── KRIVKA: entryEdge → exitEdge (priama diagonála ~45°)
            if (!isFirst && !isLast && IsCurveTile(conns))
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── T-KRIŽOVATKA (RoadSwitch) – 3 spojenia
            if (!isFirst && !isLast && IsSwitchTile(conns))
            {
                if (IsPerpendicular(inDir, outDir))
                {
                    const float SWITCH_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── KRIŽOVATKA (RoadCrossroad) – 4 spojenia
            if (!isFirst && !isLast && IsCrossroadTile(conns))
            {
                if (IsPerpendicular(inDir, outDir))
                {
                    const float CROSSROAD_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── PRIAMA / KONCOVÁ DLAŽDICA
            if (isFirst)
            {
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
            else if (isLast)
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
            }
            else
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
        }

        waypoints = wp;
        tileIdxPerWaypoint = tIdx;
    }

    // =====================================================================
    // VOZIDLO – pomocná metóda vytvorenia kvádra
    // =====================================================================

    /// <summary>
    /// Dohľadá komponent <see cref="VehicleModelLibrary"/> s prefab-mi 3D
    /// modelov. Priorita: Inspector referencia → statická inštancia → scéna.
    /// Môže vrátiť null – vtedy sa použijú výhradne pôvodné kvádre.
    /// </summary>
    VehicleModelLibrary ResolveModelLibrary()
    {
        if (modelLibrary != null) return modelLibrary;
        if (VehicleModelLibrary.instance != null)
        {
            modelLibrary = VehicleModelLibrary.instance;
            return modelLibrary;
        }
        // Posledný pokus – nájsť komponent kdekoľvek v scéne (raz, výsledok sa
        // cache-ne do modelLibrary, aby sa FindObjectOfType nevolal opakovane).
        //modelLibrary = FindObjectOfType<VehicleModelLibrary>();
        modelLibrary = UnityEngine.Object.FindAnyObjectByType<VehicleModelLibrary>();

        return modelLibrary;
    }

    /// <summary>
    /// Vráti index daného <see cref="VehicleSpec"/> v katalógu VehicleCatalog.All
    /// (poradie je 1:1 so slotmi vo VehicleModelLibrary). Ak sa nenájde,
    /// vráti 0 (prvý typ). Porovnáva sa referenčne – ByName/ByIndex vracajú
    /// tie isté inštancie, ktoré sú uložené v All.
    /// </summary>
    int IndexOfVehicleSpec(VehicleSpec spec)
    {
        if (spec == null) return 0;
        var all = VehicleCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], spec)) return i;
        return 0;
    }

    /// <summary>
    /// Spätne kompatibilný podpis (bez prefabu) – vždy vytvorí pôvodný kváder.
    /// </summary>
    GameObject CreateVehiclePart(string name, Color color, Vector3 position)
        => CreateVehiclePart(name, color, position, null);

    /// <summary>
    /// Vytvorí vizuálne telo vozidla.
    ///
    /// VOLITEĽNÝ 3D MODEL:
    ///   • modelPrefab != null → vytvorí sa inštancia tohto prefabu (skutočný
    ///     3D model). Zachová sa scale aj materiály z prefabu (model si nesie
    ///     vlastný vzhľad), kváder ani VEHICLE_SCALE sa NEAPLIKUJÚ.
    ///   • modelPrefab == null → vytvorí sa pôvodný kváder s farbou `color`
    ///     a rozmermi VEHICLE_SCALE – identické správanie ako pôvodný kód.
    ///
    /// V oboch prípadoch je výsledný objekt zhodne nakonfigurovaný pre
    /// pohybovú logiku: bez kolíznych komponentov (aby neblokoval klikanie
    /// po mape), s vypnutým aktívnym stavom (zobrazí sa až na trase).
    ///
    /// POZN. K ORIENTÁCII MODELU: pohybová logika (UpdateVehicleVisual)
    /// natáča telo cez Quaternion.LookRotation(dir, up), t.j. lokálna os +Z
    /// modelu smeruje v smere jazdy (rovnako ako dlhšia os kvádra 0.7 v Z).
    /// Prefab by mal byť "tvárou" otočený na +Z. Ak model mieri inou osou,
    /// stačí ho vnoriť pod prázdny rodičovský objekt natočený tak, aby +Z
    /// rodiča zodpovedalo prednej časti modelu, a ako prefab priradiť rodiča.
    /// </summary>
    GameObject CreateVehiclePart(string name, Color color, Vector3 position, GameObject modelPrefab)
    {
        GameObject go;

        if (modelPrefab != null)
        {
            // ── SKUTOČNÝ 3D MODEL ─────────────────────────────────────────
            // Model NEVKLADÁME priamo ako pohybovaný objekt, ale pod prázdny
            // KOREŇ (pivot). Dôvod: UpdateVehicleVisual prepisuje position aj
            // rotation každý snímok, takže prípadný posun pivotu prefabu (model
            // vymodelovaný mimo počiatku) by sa nedal kompenzovať na tom istom
            // transforme – vozidlo by išlo vedľa cesty. Koreň je ten, s ktorým
            // hýbe pohybová logika; dieťa nesie mierku a korekciu pivotu.
            go = new GameObject(name);
            go.transform.position = position;

            GameObject model = Instantiate(modelPrefab);
            model.name = "Model";
            // worldPositionStays: false → prefabu ostane jeho autorská lokálna
            // pozícia/rotácia/mierka ako LOKÁLNA voči koreňu (t.j. zachová sa
            // aj prípadné natočenie prefabu, len sa stane relatívnym).
            model.transform.SetParent(go.transform, false);

            // Odstránime prípadné kolízne komponenty, aby model – rovnako ako
            // pôvodný kváder (Destroy BoxCollider) – neblokoval raycasty pri
            // klikaní na mapu/cesty. Ak by si chcel vozidlá klikateľné, tento
            // blok stačí vynechať.
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++)
                if (colliders[c] != null) Destroy(colliders[c]);

            // Proporčné prispôsobenie na veľkosť 1 dlaždice (viď FitVehicleModel).
            if (fitVehicleModelsToTile)
                FitVehicleModel(go, model);

            // Diagnostický referenčný kváder (viď debugShowReferenceCube).
            if (debugShowReferenceCube)
                AttachReferenceCube(go);
        }
        else
        {
            // ── PÔVODNÝ KVÁDER (fallback) ─────────────────────────────────
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = VEHICLE_SCALE;
            go.transform.position = position;
            Destroy(go.GetComponent<BoxCollider>());
            var rend = go.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Standard"));
            rend.material.color = color;
        }

        go.SetActive(false);
        return go;
    }

    /// <summary>
    /// Proporčne (uniformne) preškáluje 3D model vozidla tak, aby jeho DĹŽKA
    /// pozdĺž osi +Z zodpovedala <see cref="vehicleModelLength"/> dlaždice,
    /// a vycentruje ho na pivot koreňa (os cesty).
    ///
    /// Referenciou je DĹŽKA (os Z, smer jazdy), nie väčší z rozmerov pôdorysu –
    /// inak by sa dlhé vozidlo skrátilo na dlaždicu a bolo neúmerne široké.
    ///
    /// Meria sa v LOKÁLNOM priestore koreňa, takže netreba nič dočasne otáčať.
    ///
    /// KOREKCIA PIVOTU:
    ///   Po preškálovaní posunieme dieťa tak, aby stred jeho X/Z pôdorysu ležal
    ///   na pivote koreňa a spodok sedel na úrovni cesty (plus prípadné
    ///   doladenie cez <see cref="vehicleModelYOffset"/>).
    /// </summary>
    void FitVehicleModel(GameObject root, GameObject model)
    {
        if (root == null || model == null) return;

        if (!TryGetLocalBounds(root.transform, model, out Bounds b))
        {
            Debug.LogWarning($"[VehicleSystem] '{root.name}': prefab nemá žiadny " +
                             "zapnutý MeshRenderer/SkinnedMeshRenderer – " +
                             "veľkosť ani centrovanie sa nedajú určiť.");
            return;
        }
        if (b.size.z <= 1e-5f || b.size.x <= 1e-5f) return;

        // 1) Uniformná mierka podľa dĺžky (os Z = smer jazdy).
        float s = vehicleModelLength / b.size.z;

        // 2) Poistka na šírku – ak by bol model po škálovaní na dĺžku širší
        //    než povolené, doškálujeme nadol (stále uniformne).
        if (b.size.x * s > vehicleModelMaxWidth)
            s = vehicleModelMaxWidth / b.size.x;

        model.transform.localScale *= s;

        // 3) Re-centrovanie. Cieľ v lokálnych súradniciach koreňa:
        //       X = 0  (na os cesty)
        //       Z = 0  (stred dĺžky presne na pivote → zatáčanie ho nevychýli)
        //       min Y = vehicleModelYOffset
        //    Koreň má mierku 1, takže lokálne jednotky = svetové jednotky.
        if (!TryGetLocalBounds(root.transform, model, out Bounds b2)) return;

        model.transform.localPosition += new Vector3(
            -b2.center.x,
            -b2.min.y + vehicleModelYOffset,
            -b2.center.z);

        if (logVehicleFit)
        {
            TryGetLocalBounds(root.transform, model, out Bounds bf);
            Debug.Log($"[VehicleSystem] FIT '{root.name}': pôvodne {b.size} @ {b.center} " +
                      $"→ mierka ×{s:F4} → výsledok {bf.size} @ {bf.center} " +
                      $"(očakávané center.x≈0, center.z≈0; min.y={bf.min.y:F4}) | " +
                      $"pivot vo svete = {root.transform.position}");
        }
    }

    /// <summary>
    /// Spočíta AABB celého modelu v LOKÁLNOM priestore zadaného transformu.
    ///
    /// Prečo nie <c>Renderer.bounds</c>:
    ///   • Renderer.bounds je world-space AABB. Pri natočenom modeli je väčší
    ///     než skutočné teleso a jeho stred sa posúva – z toho vzniká vychýlenie.
    ///   • Renderer.bounds NIE JE spoľahlivý pre neaktívne / ešte nevykreslené
    ///     objekty; vtedy môže vrátiť neaktuálnu alebo nulovú hodnotu.
    ///
    /// Namiesto toho berieme <c>sharedMesh.bounds</c> (čistá geometria) a jeho
    /// 8 rohov transformujeme do priestoru koreňa. Výsledok je deterministický.
    ///
    /// Zahrnú sa LEN zapnuté MeshRenderer / SkinnedMeshRenderer. Vypnuté
    /// renderery (kolízne proxy, shadow-only pomocníky, LOD varianty) sa
    /// ignorujú – práve tie inak ťahajú stred bounding boxu mimo tela modelu.
    /// </summary>
    static bool TryGetLocalBounds(Transform space, GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Matrix4x4 toSpace = space.worldToLocalMatrix;

        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            var mf = filters[i];
            if (mf == null || mf.sharedMesh == null) continue;

            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;

            AccumulateCorners(mf.sharedMesh.bounds,
                              toSpace * mf.transform.localToWorldMatrix,
                              ref bounds, ref any);
        }

        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            var sm = skinned[i];
            if (sm == null || sm.sharedMesh == null) continue;
            if (!sm.enabled || !sm.gameObject.activeInHierarchy) continue;

            AccumulateCorners(sm.sharedMesh.bounds,
                              toSpace * sm.transform.localToWorldMatrix,
                              ref bounds, ref any);
        }

        return any;
    }

    /// <summary>
    /// Pridá do akumulovaného AABB 8 rohov zadaného mesh-bounds po prechode
    /// transformačnou maticou. Rohy (nie stred+extent) sú nutné preto, aby sa
    /// korektne podchytilo aj natočenie a nerovnomerná mierka v hierarchii.
    /// </summary>
    static void AccumulateCorners(Bounds meshBounds, Matrix4x4 m,
                                  ref Bounds acc, ref bool any)
    {
        Vector3 c = meshBounds.center;
        Vector3 e = meshBounds.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                c.x + (((i & 1) == 0) ? -e.x : e.x),
                c.y + (((i & 2) == 0) ? -e.y : e.y),
                c.z + (((i & 4) == 0) ? -e.z : e.z));

            Vector3 p = m.MultiplyPoint3x4(corner);

            if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
            else acc.Encapsulate(p);
        }
    }

    /// <summary>
    /// Pripne ku koreňu vozidla CYAN referenčný kváder s rozmermi
    /// <see cref="VEHICLE_SCALE"/>, vycentrovaný presne na pivot koreňa – teda
    /// v tej istej polohe, akú mal pôvodný kváder pred zavedením 3D modelov.
    ///
    /// Farba je zámerne CYAN, nie magenta: pôvodný kvádrový fallback vozidla
    /// je magenta, takže by sa oba nedali od seba rozoznať.
    ///
    /// DIAGNOSTICKÝ ZMYSEL:
    ///   • Kváder sedí na ceste, model je vedľa neho
    ///       → chyba je v centrovaní modelu (FitVehicleModel).
    ///   • Kváder aj model sú vychýlené rovnako
    ///       → centrovanie je v poriadku a vychýlená je trajektória alebo
    ///         poloha cestných dlaždíc.
    ///   • Model je oproti kvádru posunutý len ZVISLE
    ///       → dolaď vehicleModelYOffset.
    /// </summary>
    void AttachReferenceCube(GameObject root)
    {
        if (root == null) return;

        var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        refCube.name = "DEBUG_ReferenceCube";

        var bc = refCube.GetComponent<BoxCollider>();
        if (bc != null) Destroy(bc);

        refCube.transform.SetParent(root.transform, false);
        refCube.transform.localPosition = Vector3.zero;
        refCube.transform.localRotation = Quaternion.identity;
        refCube.transform.localScale = VEHICLE_SCALE;

        var rend = refCube.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = new Color(0f, 1f, 1f);
    }

    // =====================================================================
    // SPRÁVA VOZIDIEL
    // =====================================================================

    /// <summary>
    /// Vytvorí vozidlo v zadanom ROAD depe.
    ///
    /// Parameter vehicleTypeName je názov typu vozidla z VehicleCatalog
    /// (napr. "Vehicle 1", "Vehicle 2"). Slúži na dve veci:
    ///   1. Vyhľadanie VehicleSpec v katalógu → vytvorenie VehicleInstance
    ///      (dátová štruktúra vozidla s atribútmi Cost, Speed, Power...).
    ///   2. Pomenovanie vizuálneho GameObjectu.
    /// Vizuálne ide stále o ten istý magenta kváder – atribúty žijú v
    /// dátovej štruktúre vd.vehicleInstance, nie vo vizuáli.
    /// </summary>
    public bool CreateVehicle(int dx, int dz, string vehicleTypeName = "Vehicle 1")
    {
        int key = DepotKey(dx, dz);
        if (vehicles.ContainsKey(key)) return false;
        if (GetRoadTileID(dx, dz) != 3) return false;

        // Vyhľadáme predlohu vozidla v katalógu. Ak názov nesedí (napr.
        // neznámy typ), spadneme na prvý typ v katalógu, aby vozidlo malo
        // vždy platnú dátovú štruktúru.
        VehicleSpec spec = VehicleCatalog.ByName(vehicleTypeName)
                           ?? VehicleCatalog.ByIndex(0);
        if (spec == null)
        {
            Debug.LogError("[VehicleSystem] VehicleCatalog je prázdny – vozidlo sa nedá vytvoriť.");
            return false;
        }

        VehicleData vd = new VehicleData(dx, dz);
        Vector3 depotPos = TileCenter(new Vector2Int(dx, dz));

        // Dátová štruktúra vozidla – nová inštancia z katalógovej predlohy.
        // Age = 0, CurrentCapacity = 0 (nastaví konštruktor VehicleInstance).
        vd.vehicleInstance = new VehicleInstance(spec);

        // Rýchlosť/zrýchlenie/spomalenie vozidla – z parametrov VehicleSpec
        // (viď ComputeMotionParams). Typ vozidla sa po vytvorení nemení.
        ComputeMotionParams(vd);

        // -----------------------------------------------------------------
        // VOLITEĽNÝ 3D MODEL (VehicleModelLibrary)
        // Zistíme index zvoleného typu v katalógu (poradie 1:1 so slotmi
        // knižnice) a podľa neho prefab. Ak je null → CreateVehiclePart
        // vytvorí pôvodný kváder; ak nie je → vytvorí 3D model.
        // -----------------------------------------------------------------
        int vehicleTypeIndex = IndexOfVehicleSpec(spec);
        VehicleModelLibrary lib = ResolveModelLibrary();
        GameObject vehiclePrefab = (lib != null) ? lib.GetVehiclePrefab(vehicleTypeIndex) : null;

        // Skrytá kocka – pohybová logika
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = VEHICLE_SCALE;
        cube.transform.position = depotPos;
        cube.name = $"VehicleHead_{dx}_{dz}";
        Destroy(cube.GetComponent<BoxCollider>());
        cube.GetComponent<Renderer>().enabled = false;
        vd.vehicleObject = cube;

        // Vozidlo – MAGENTA kváder, alebo (ak je definovaný) 3D model
        string bodyName = $"{spec.Name}_{dx}_{dz}";
        vd.vehicleBody = CreateVehiclePart(bodyName, new Color(1f, 0f, 1f), depotPos, vehiclePrefab);

        vehicles[key] = vd;

        // Životný cyklus → Created (StatusTypeTextCaption potom zobrazí
        // Running/Stopped namiesto "NO VEHICLE").
        vehicleLifecycle[key] = VehicleLifecycleState.Created;

        Debug.Log($"[VehicleSystem] Vozidlo '{spec.Name}' ({spec.Type}) vytvorené v depe [{dx},{dz}]. " +
                  $"Kapacita {spec.MaximumCapacity}, cena {spec.Cost}, rýchlosť {spec.Speed}.");
        return true;
    }

    /// <summary>
    /// KLIK NA VOZIDLO: zistí, či lúč (z kamery cez kurzor myši) zasiahol
    /// viditeľné vozidlo. Analógia k TrainSystem.TryPickTrain.
    ///
    /// Platí pre IDÚCE aj ZASTAVENÉ vozidlo (Stop, čakanie na stanici,
    /// dokončená trasa). Vozidlo odstavené v depe (isAtDepot) sa preskakuje.
    /// Test cez Renderer.bounds (vozidlo nemá kolízne komponenty).
    /// Metóda NEMENÍ žiadny stav – iba číta.
    /// </summary>
    public bool TryPickVehicle(Ray ray, out Vector2Int depot, out float distance)
    {
        depot = default;
        distance = float.MaxValue;
        bool found = false;

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd == null || vd.isAtDepot) continue;

            GameObject body = vd.vehicleBody;
            if (body == null || !body.activeInHierarchy) continue;

            var renderers = body.GetComponentsInChildren<Renderer>(false);
            bool has = false;
            Bounds b = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) continue;

            // Mierne zväčšenie – menšie modely sa dajú pohodlnejšie trafiť.
            b.Expand(0.05f);
            if (b.IntersectRay(ray, out float d) && d < distance)
            {
                distance = d;
                depot = new Vector2Int(vd.depotX, vd.depotZ);
                found = true;
            }
        }
        return found;
    }

    public VehicleData GetVehicle(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return vehicles.TryGetValue(key, out var vd) ? vd : null;
    }

    /// <summary>
    /// READ-ONLY dotaz: nachádza sa PRÁVE TERAZ na dlaždici [tx,tz] niektoré
    /// vozidlo? Analógia k TrainSystem.IsTileOccupiedByTrain.
    ///
    /// Slúži výhradne ako ochrana pred demoláciou cesty/stanice pod idúcim
    /// vozidlom (GameManager → RoadConstructionMode.Demolish). Metóda NEMENÍ
    /// žiadny stav – iba číta.
    ///
    /// Vozidlo je jediné teleso (bez vagónov). Najprv sa skontroluje jeho
    /// SKUTOČNÁ pozícia (vehicleBody) – to platí rovnako pre idúce aj pre
    /// ZASTAVENÉ vozidlo (Stop, čakanie na stanici, dokončená trasa) –, potom
    /// currentTile a nakoniec, ak existuje trasa, krátky úsek activePath okolo
    /// jeho pozície (BODY_MARGIN = presah modelu pred/za pivotom). Vozidlo
    /// odstavené v depe (isAtDepot) je skryté a dlaždice neblokuje – depo chráni
    /// samostatná kontrola v GameManager.
    /// </summary>
    public bool IsTileOccupiedByVehicle(int tx, int tz)
    {
        const float SAMPLE_STEP = 0.25f;   // krok vzorkovania pozdĺž trasy
        const float BODY_MARGIN = 0.30f;   // presah modelu pred/za pivotom

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd == null) continue;

            // Vozidlo odstavené (skryté) v depe cestu neblokuje.
            if (vd.isAtDepot) continue;

            // (A) SKUTOČNÁ POZÍCIA VOZIDLA – funguje rovnako pre idúce aj pre
            //     ZASTAVENÉ vozidlo (Stop, čakanie na stanici, dokončená
            //     trasa), pretože objekt ostáva stáť tam, kde ho hráč vidí.
            if (vd.vehicleBody != null && vd.vehicleBody.activeSelf)
            {
                Vector3 center = vd.vehicleBody.transform.position;
                Vector3 fwd = vd.vehicleBody.transform.forward;

                // Pivot + presah tela pred a za pivotom (model môže
                // presahovať cez hranicu dlaždice).
                for (int s = -1; s <= 1; s++)
                {
                    Vector3 bp = center + fwd * (s * BODY_MARGIN);
                    if (Mathf.FloorToInt(bp.x) == tx && Mathf.FloorToInt(bp.z) == tz)
                        return true;
                }
            }

            // (B) Dlaždica, na ktorej vozidlo logicky stojí.
            if (vd.currentTile.x == tx && vd.currentTile.y == tz) return true;

            // (C) Krátky úsek cesty pod vozidlom (len ak existuje trasa).
            if (vd.activePath == null) continue;

            float pos = Mathf.Clamp(vd.vehicleDistance, 0f, vd.activePath.totalLength);
            float head = pos + BODY_MARGIN;
            float tail = Mathf.Max(0f, pos - BODY_MARGIN);

            int steps = Mathf.Max(1, Mathf.CeilToInt((head - tail) / SAMPLE_STEP));
            for (int i = 0; i <= steps; i++)
            {
                float d = tail + (head - tail) * i / steps;

                // GetPositionAtDistance si vzdialenosť sám upne na [0, totalLength].
                Vector3 p = vd.activePath.GetPositionAtDistance(d);
                if (Mathf.FloorToInt(p.x) == tx && Mathf.FloorToInt(p.z) == tz)
                    return true;
            }

            if (vd.currentTile.x == tx && vd.currentTile.y == tz) return true;
        }

        return false;
    }

    /// <summary>
    /// Vráti súradnice [depotX, depotZ] VŠETKÝCH aktuálne existujúcich
    /// vozidlových dep. Keďže platí "1 vehicle depo = 1 vehicle", počet
    /// prvkov zoznamu = počet vozidiel v hre.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI). Vnútorný slovník
    /// `vehicles` ostáva privátny – navonok dávame len read-only kópiu
    /// kľúčových údajov, takže volajúci nemôže poškodiť interný stav.
    /// </summary>
    public List<Vector2Int> GetAllDepotCoords()
    {
        var result = new List<Vector2Int>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            result.Add(new Vector2Int(vd.depotX, vd.depotZ));
        }
        return result;
    }

    /// <summary>
    /// Celkový počet vozidlových dep (= počet vozidiel). Pohodlný getter,
    /// aby UI nemuselo kvôli počtu vytvárať celý zoznam cez
    /// GetAllDepotCoords().
    /// </summary>
    public int GetDepotCount()
    {
        return vehicles.Count;
    }

    /// <summary>
    /// Vráti dátové štruktúry (VehicleInstance) VŠETKÝCH aktuálne
    /// existujúcich vozidiel. Keďže platí "1 vehicle depo = 1 vehicle",
    /// počet prvkov zoznamu = počet vozidiel v hre.
    ///
    /// Slúži pre informačné UI (StatusVehiclesMenuUI), ktoré z každej
    /// VehicleInstance prečíta názov vozidla (Name) a typ suroviny
    /// (Type.Name).
    ///
    /// Vnútorný slovník `vehicles` ostáva privátny – navonok dávame len
    /// read-only kópiu zoznamu referencií na VehicleInstance. Volajúci tak
    /// nemôže pridať/odobrať vozidlo (zmeniť `vehicles`), čítať atribúty
    /// jednotlivých vozidiel ale môže. Analógia k GetAllDepotCoords().
    /// </summary>
    public List<VehicleInstance> GetAllVehicleInstances()
    {
        var result = new List<VehicleInstance>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd != null && vd.vehicleInstance != null)
                result.Add(vd.vehicleInstance);
        }
        return result;
    }

    /// <summary>
    /// EKONOMIKA (read-only snapshot): pre KAŽDÉ existujúce vozidlo spáruje jeho
    /// DEPO súradnice [depotX, depotZ] (= kľúč pre <see cref="ReturnToDepot"/>)
    /// s jeho dátovou inštanciou <see cref="VehicleInstance"/> (OperatingCosts,
    /// ServiceLife, ServicingInterval). Interný slovník `vehicles` ostáva privátny.
    ///
    /// Slúži pre EconomySystem: mesačné prevádzkové náklady + automatické
    /// poslanie vozidla do depa po uplynutí servisného intervalu alebo životnosti.
    /// Analógia k TrainSystem.GetActiveTrainInfos().
    /// </summary>
    public List<ActiveVehicleInfo> GetActiveVehicleInfos()
    {
        var result = new List<ActiveVehicleInfo>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd != null && vd.vehicleInstance != null)
                result.Add(new ActiveVehicleInfo(vd.depotX, vd.depotZ, vd.vehicleInstance));
        }
        return result;
    }

    /// <summary>Read-only dvojica [depo súradnice + dátová inštancia vozidla] pre EconomySystem.</summary>
    public readonly struct ActiveVehicleInfo
    {
        public readonly int DepotX;
        public readonly int DepotZ;
        public readonly VehicleInstance Instance;

        public ActiveVehicleInfo(int depotX, int depotZ, VehicleInstance instance)
        {
            DepotX = depotX;
            DepotZ = depotZ;
            Instance = instance;
        }
    }

    public bool AddStation(int depotX, int depotZ, int stX, int stZ)
    {
        VehicleData vd = GetVehicle(depotX, depotZ);
        if (vd == null) return false;
        if (GetRoadTileID(stX, stZ) != 2) return false;

        Vector2Int st = new Vector2Int(stX, stZ);
        if (!vd.stations.Contains(st))
        {
            vd.stations.Add(st);
            Debug.Log($"[VehicleSystem] Stanica [{stX},{stZ}] pridaná pre vozidlo v depe [{depotX},{depotZ}]. Celkom: {vd.stations.Count}");
        }
        return true;
    }

    public bool StartVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;
        if (vd.stations.Count < 2)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] nemá aspoň 2 stanice!");
            return false;
        }

        bool isResume = !vd.isAtDepot && vd.currentPath != null && vd.currentPath.Count > 0;

        vd.isRunning = true;
        vd.isWaiting = false;
        vd.isReturningToDepot = false;

        if (isResume)
        {
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] OBNOVENÉ (resume).");
        }
        else
        {
            vd.isAtDepot = false;
            vd.currentStationIndex = 0;
            vd.reverseDirection = false;
            vd.moveTimer = 0f;
            vd.waitTimer = 0f;
            vd.pendingPath = null;
            vd.pendingReturnToDepot = false;

            vd.activePath = null;
            vd.vehicleDistance = 0f;

            ComputeNextPathAsync(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] SPUSTENÉ (prvý štart).");
        }

        return true;
    }

    public bool StopVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;
        vd.isRunning = false;
        vd.isComputingPath = false;
        vd.pendingPath = null;
        vd.pendingReturnToDepot = false;
        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] ZASTAVENÉ.");
        return true;
    }

    public ReturnToDepotResult ReturnToDepot(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return ReturnToDepotResult.Error;

        if (GetRoadTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[VehicleSystem] Depo [{dx},{dz}] neexistuje – vozidlo zastane.");
            vd.isRunning = false;
            vd.isStoppedAwaitingDepotReturn = false;
            return ReturnToDepotResult.Error;
        }

        if (vd.isStoppedAwaitingDepotReturn)
        {
            vd.isStoppedAwaitingDepotReturn = false;
            vd.pendingReturnToDepot = false;
            vd.isRunning = true;
            vd.isWaiting = false;
            vd.waitTimer = 0f;
            vd.isGoingToDepotViaStation = false;
            vd.isComputingPath = false;
            vd.pendingPath = null;
            DispatchToDepot(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] → depo priamo.");
            return ReturnToDepotResult.Dispatched;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);

        if (vd.isWaiting)
        {
            vd.pendingReturnToDepot = false;
            vd.isGoingToDepotViaStation = true;
            vd.isStoppedAwaitingDepotReturn = false;
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] čaká na stanici → po odpočítaní pôjde do depa.");
            return ReturnToDepotResult.Dispatched;
        }

        if (IsDepotOnCurrentPath(vd, depotTile))
        {
            vd.pendingReturnToDepot = false;
            vd.isGoingToDepotViaStation = false;
            vd.isStoppedAwaitingDepotReturn = false;
            TrimPathToDepot(vd, depotTile);
            vd.isReturningToDepot = true;
            RebuildActivePathFromCurrentPath(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] → depo PRIAMO (na aktuálnej ceste).");
            return ReturnToDepotResult.Dispatched;
        }

        vd.pendingReturnToDepot = true;
        vd.isGoingToDepotViaStation = false;
        vd.isStoppedAwaitingDepotReturn = false;
        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}]: depo nie je v smere jazdy → dokončí cestu a potom pôjde do depa.");
        return ReturnToDepotResult.Dispatched;
    }

    bool IsDepotOnCurrentPath(VehicleData vd, Vector2Int depotTile)
    {
        if (vd.currentPath == null || vd.pathIndex >= vd.currentPath.Count) return false;
        for (int i = vd.pathIndex; i < vd.currentPath.Count; i++)
            if (vd.currentPath[i] == depotTile) return true;
        return false;
    }

    void TrimPathToDepot(VehicleData vd, Vector2Int depotTile)
    {
        if (vd.currentPath == null) return;
        for (int i = vd.pathIndex; i < vd.currentPath.Count; i++)
        {
            if (vd.currentPath[i] == depotTile)
            {
                vd.currentPath = vd.currentPath.GetRange(0, i + 1);
                return;
            }
        }
    }

    void DispatchToDepot(VehicleData vd)
    {
        int key = DepotKey(vd.depotX, vd.depotZ);
        Vector2Int goal = new Vector2Int(vd.depotX, vd.depotZ);
        Vector2Int start = vd.currentTile;
        var snap = SnapshotTileGrid();

        vd.isReturningToDepot = true;
        vd.isGoingToDepotViaStation = false;
        vd.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = true,
                destination = PathDestination.Depot
            });
        });

        Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] → depo.");
    }

    /// <summary>
    /// Odstráni vozidlo. Povolené iba ak je vozidlo fyzicky v depe a zastavené.
    /// Logika zhodná s TrainSystem.RemoveTrain (pozri tam podrobný popis
    /// scenárov, ktoré vyžadujú túto guardu).
    ///
    /// ZRKADLOVOSŤ s CreateVehicle:
    /// CreateVehicle vytvorí dátové štruktúry vozidla (VehicleInstance) a
    /// vizuálne GameObjecty. RemoveVehicle musí to isté uvoľniť – teda
    /// okrem zničenia GameObjectov aj uvoľniť vd.vehicleInstance a
    /// nastaviť životný cyklus na Removed.
    /// </summary>
    public bool RemoveVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;

        if (vd.isRunning)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] sa nedá odstrániť – stále beží. Najprv ho zastavte.");
            return false;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);
        if (vd.currentTile != depotTile)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] sa nedá odstrániť – nie je fyzicky v depe (aktuálne na [{vd.currentTile.x},{vd.currentTile.y}]).");
            return false;
        }

        if (GetRoadTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[VehicleSystem] Tile [{dx},{dz}] už nie je ROAD depo – vozidlo sa nedá odstrániť.");
            return false;
        }

        vd.isComputingPath = false;
        vd.pendingPath = null;

        if (vd.vehicleObject != null) Destroy(vd.vehicleObject);
        if (vd.vehicleBody != null) Destroy(vd.vehicleBody);

        // Uvoľnenie dátovej štruktúry vozidla – zrkadlovo k CreateVehicle,
        // ktorý ju vytvoril. Po tomto bode vozidlo nemá žiadne atribúty.
        vd.vehicleInstance = null;

        int key = DepotKey(dx, dz);
        vehicles.Remove(key);

        // Životný cyklus → Removed (StatusTypeTextCaption potom zobrazí
        // "NO VEHICLE", kým sa vozidlo znovu nevytvorí cez DCCreateVehicleButton).
        vehicleLifecycle[key] = VehicleLifecycleState.Removed;

        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] ODSTRÁNENÉ (dátová štruktúra uvoľnená).");
        return true;
    }

    // =====================================================================
    // NOTIFIKÁCIA PRI ZMENE MAPY – DEBOUNCE
    // =====================================================================

    public void OnMapChanged()
    {
        _mapChangePending = true;
        _mapChangedDebounceTimer = DEBOUNCE_DELAY;
    }

    // =====================================================================
    // UPDATE
    // =====================================================================

    void Update()
    {
        if (_mapChangePending)
        {
            _mapChangedDebounceTimer -= Time.deltaTime;
            if (_mapChangedDebounceTimer <= 0f)
            {
                _mapChangePending = false;
                _mapChangedDebounceTimer = 0f;
                TriggerMapChangedReroute();
            }
        }

        ApplyPendingPathResults();

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd.vehicleObject == null) continue;
            if (!vd.isRunning) continue;
            UpdateVehicle(vd);
        }
    }

    // =====================================================================
    // POHYB VOZIDLA – DISTANCE-BASED
    // =====================================================================

    void UpdateVehicle(VehicleData vd)
    {
        // ── Čakanie (stanica / pauza)
        if (vd.isWaiting)
        {
            vd.waitTimer -= Time.deltaTime;

            // ── OBCHOD: po 2 s čakania na cieľovej stanici ────────────────
            // Spustí sa práve raz za zastávku. tradeDoneAtStation je nastavené
            // na false iba pri príchode na cieľovú stanicu (OnPathComplete),
            // takže pri BREAK_WAIT ani pri ceste do depa sa obchod nespustí.
            if (!vd.tradeDoneAtStation
                && (STATION_WAIT - vd.waitTimer) >= TRADE_DELAY)
            {
                vd.tradeDoneAtStation = true;
                TryExecuteTradeAtStation(vd);
            }

            if (vd.waitTimer <= 0f)
            {
                vd.isWaiting = false;
                vd.waitTimer = 0f;

                if (vd.isReturningToDepot)
                    DispatchToDepot(vd);
                else if (vd.isGoingToDepotViaStation)
                    DispatchToDepot(vd);
                else
                    AdvanceStation(vd);
            }
            return;
        }

        // ── Čakáme na výpočet novej trasy
        if (vd.activePath == null)
        {
            if (vd.isComputingPath) return;
            OnPathComplete(vd);
            return;
        }

        // ── Cieľová rýchlosť podľa sklonu cesty (do kopca pomalšie, z kopca rýchlejšie) ──
        float grade = vd.activePath.GetGradeAtDistance(vd.vehicleDistance);
        float targetSpeed = TargetSpeedForGrade(vd, grade);

        // ── Brzdenie pred koncom trasy (stanica / depo) ──────────────────
        float remaining = vd.activePath.totalLength - vd.vehicleDistance;
        float brakingDistance = (vd.vehicleSpeed * vd.vehicleSpeed) / (2f * Mathf.Max(vd.deceleration, MIN_ACCEL));
        if (remaining <= brakingDistance)
            targetSpeed = 0f;

        // ── Plynulé zrýchľovanie/spomaľovanie smerom k cieľovej rýchlosti ──
        float rate = (targetSpeed > vd.vehicleSpeed) ? vd.acceleration : vd.deceleration;
        vd.vehicleSpeed = Mathf.MoveTowards(vd.vehicleSpeed, targetSpeed, rate * Time.deltaTime);

        // Poistka proti "zamrznutiu" tesne pred cieľom (viď TrainSystem).
        if (targetSpeed <= 0f && remaining > 0.001f)
            vd.vehicleSpeed = Mathf.Max(vd.vehicleSpeed, MIN_ARRIVAL_CRAWL_SPEED);

        // ── Pohyb vozidla pozdĺž trasy
        vd.vehicleDistance += vd.vehicleSpeed * Time.deltaTime;

        UpdateCurrentTile(vd);

        Vector3 headPos = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
        vd.vehicleObject.transform.position = headPos;

        // ── Vizuálne vozidlo (1 kváder)
        UpdateVehicleVisual(vd);

        // ── Detekcia konca trasy
        if (vd.vehicleDistance >= vd.activePath.totalLength)
        {
            vd.vehicleDistance = vd.activePath.totalLength;
            vd.currentTile = vd.currentPath != null && vd.currentPath.Count > 0
                ? vd.currentPath[vd.currentPath.Count - 1]
                : vd.currentTile;

            if (vd.pendingPath != null)
            {
                List<Vector2Int> pending = vd.pendingPath;
                vd.pendingPath = null;
                ApplyNewPath(vd, pending);
                return;
            }

            vd.activePath = null;
            OnPathComplete(vd);
        }
    }

    void UpdateCurrentTile(VehicleData vd)
    {
        if (vd.currentPath == null || vd.activePath == null) return;
        if (vd.waypointToTileIdx == null || vd.waypointToTileIdx.Length == 0) return;

        float[] cumLens = vd.activePath.cumulativeLengths;
        int lastPassedIdx = 0;
        for (int i = 0; i < cumLens.Length; i++)
        {
            if (cumLens[i] <= vd.vehicleDistance)
                lastPassedIdx = i;
            else
                break;
        }

        int subPathTileIdx = vd.waypointToTileIdx[lastPassedIdx];

        // MOST / TUNEL: vo vnútri prechodu drž currentTile na VSTUPNEJ hlave.
        if (vd.subPathEntryHead != null
            && subPathTileIdx >= 0 && subPathTileIdx < vd.subPathEntryHead.Length
            && vd.subPathEntryHead[subPathTileIdx] >= 0)
            subPathTileIdx = vd.subPathEntryHead[subPathTileIdx];

        int tileIdx = vd.pathIndexAtPathStart + subPathTileIdx;
        if (tileIdx >= 0 && tileIdx < vd.currentPath.Count)
            vd.currentTile = vd.currentPath[tileIdx];
    }

    // =====================================================================
    // VIZUÁLNE VOZIDLO – 1 KVÁDER (žiadne vagóny)
    // =====================================================================

    /// <summary>
    /// Aktualizuje pozíciu, rotáciu a viditeľnosť vozidla.
    /// Oproti TrainSystem UpdateConsistVisuals (kde sa iteruje cez
    /// lokomotívu + N vagónov) ide o jediný kváder bez offsetu.
    /// </summary>
    void UpdateVehicleVisual(VehicleData vd)
    {
        if (vd.activePath == null) return;
        if (vd.vehicleBody == null) return;

        const float DEPOT_HIDE_RADIUS = 0.55f;
        bool isReturning = vd.isReturningToDepot;
        Vector3 depotCenter = TileCenter(new Vector2Int(vd.depotX, vd.depotZ));

        bool shouldBeVisible = true;

        // ── Skrývanie pri návrate do depa
        if (isReturning)
        {
            Vector3 pos = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
            if (Vector3.Distance(pos, depotCenter) < DEPOT_HIDE_RADIUS)
                shouldBeVisible = false;
        }

        // ── Skrývanie v TUNELI
        // Vozidlo sa skryje, keď jeho PREDOK vojde za portál do tunela, a
        // odkryje, keď z tunela vyjde jeho ZADOK – model tak netrčí cez kopec.
        if (shouldBeVisible)
        {
            var crossings = RoadCrossingSystem.instance;
            if (crossings != null && crossings.Count > 0 && crossings.HideVehiclesInsideTunnels)
            {
                float halfLen = vehicleModelLength * 0.5f;
                if (crossings.IsBodyInsideTunnel(
                        vd.activePath.GetPositionAtDistance(vd.vehicleDistance - halfLen),
                        vd.activePath.GetPositionAtDistance(vd.vehicleDistance),
                        vd.activePath.GetPositionAtDistance(vd.vehicleDistance + halfLen),
                        ROAD_OFFSET_Y))
                    shouldBeVisible = false;
            }
        }

        if (vd.vehicleBody.activeSelf != shouldBeVisible)
            vd.vehicleBody.SetActive(shouldBeVisible);

        if (!shouldBeVisible) return;

        Vector3 posBody = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
        if (float.IsNaN(posBody.x) || float.IsNaN(posBody.y) || float.IsNaN(posBody.z))
        {
            Debug.LogWarning($"[VehicleSystem] NaN pozícia – preskočené.");
            return;
        }

        Vector3 dir = vd.activePath.GetDirectionAtDistance(vd.vehicleDistance);
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        vd.vehicleBody.transform.position = posBody;
        vd.vehicleBody.transform.rotation = rot;

        // ── DIAGNOSTIKA: je model vycentrovaný na svojom pivote? ─────────
        // Meria sa až POČAS jazdy, teda v presne tom stave, ktorý vidíš na
        // obrazovke – nie pri vytvorení v depe.
        LogRuntimeCentering(vd.vehicleBody);
    }

    /// <summary>Koľko riadkov runtime-diagnostiky ešte vypísať (aby konzola nezahltila).</summary>
    private int _runtimeFitLogsLeft = 12;

    /// <summary>
    /// Vypíše pre vozidlo porovnanie jeho pivotu so skutočným stredom geometrie
    /// modelu, vyjadreným v LOKÁLNYCH súradniciach pivotu.
    ///   lokálny stred ≈ (0, ?, 0)  → model JE na pivote, vychýlená je
    ///                                trajektória alebo cestné dlaždice
    ///   lokálny stred výrazne ≠ 0  → zlyháva centrovanie modelu a číslo rovno
    ///                                hovorí o koľko a kam
    /// Meria len podobjekt "Model" – prípadný referenčný kváder je vynechaný.
    /// </summary>
    void LogRuntimeCentering(GameObject body)
    {
        if (!logVehicleFit || _runtimeFitLogsLeft <= 0 || body == null) return;

        Transform modelTf = body.transform.Find("Model");
        if (modelTf == null) return; // kvádrový fallback – nie je čo merať

        if (!TryGetLocalBounds(body.transform, modelTf.gameObject, out Bounds lb)) return;

        _runtimeFitLogsLeft--;
        Debug.Log($"[VehicleSystem] RUNTIME '{body.name}': pivot vo svete = {body.transform.position} | " +
                  $"stred modelu v lokále pivotu = ({lb.center.x:F3}, {lb.center.y:F3}, {lb.center.z:F3}) | " +
                  $"rozmery modelu = ({lb.size.x:F3}, {lb.size.y:F3}, {lb.size.z:F3}) " +
                  $"→ očakávané x≈0 a z≈0");
    }

    // =====================================================================
    // DOKONČENIE CESTY
    // =====================================================================

    void OnPathComplete(VehicleData vd)
    {
        if (vd.isReturningToDepot)
        {
            vd.isRunning = false;
            vd.isAtDepot = true;
            vd.isReturningToDepot = false;
            vd.isGoingToDepotViaStation = false;
            vd.currentTile = new Vector2Int(vd.depotX, vd.depotZ);
            vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
            vd.activePath = null;
            vd.vehicleDistance = 0f;

            HideVehicle(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] dorazilo do DEPA – skryté.");
            return;
        }

        if (vd.isGoingToDepotViaStation)
        {
            bool onStation = vd.stations.Contains(vd.currentTile)
                             && GetRoadTileID(vd.currentTile.x, vd.currentTile.y) == 2;
            if (onStation)
            {
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{vd.currentTile.x},{vd.currentTile.y}] pred depom – čaká {STATION_WAIT}s.");
                vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
                vd.isWaiting = true;
                vd.waitTimer = STATION_WAIT;
                // Zastávka pred návratom do depa – obchod sa nerealizuje.
                vd.tradeDoneAtStation = true;
            }
            else
            {
                Debug.LogWarning($"[VehicleSystem] Vozidlo nedosiahlo stanicu pred depom. Zastane.");
                vd.isRunning = false;
                vd.isGoingToDepotViaStation = false;
            }
            return;
        }

        Vector2Int targetStation = vd.stations[vd.currentStationIndex];
        if (vd.currentTile == targetStation)
        {
            if (vd.pendingReturnToDepot)
            {
                vd.pendingReturnToDepot = false;
                vd.isGoingToDepotViaStation = true;
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{targetStation.x},{targetStation.y}] (pending návrat) – čaká {STATION_WAIT}s, potom depo.");
                // Posledná zastávka pred depom – obchod sa nerealizuje.
                vd.tradeDoneAtStation = true;
            }
            else
            {
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{targetStation.x},{targetStation.y}] – čaká {STATION_WAIT}s.");
                // Riadna zastávka na cieľovej stanici – po 2 s prebehne obchod.
                vd.tradeDoneAtStation = false;
            }
            vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
            vd.isWaiting = true;
            vd.waitTimer = STATION_WAIT;
        }
        else
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo nedosiahlo stanicu [{targetStation.x},{targetStation.y}]. Čaká {BREAK_WAIT}s.");
            vd.isWaiting = true;
            vd.waitTimer = BREAK_WAIT;
            // Núdzové prerušenie – nie je to zastávka na stanici, žiadny obchod.
            vd.tradeDoneAtStation = true;
        }
    }

    void HideVehicle(VehicleData vd)
    {
        if (vd.vehicleBody != null) vd.vehicleBody.SetActive(false);
    }

    // =====================================================================
    // OBCHOD NA STANICI – TRADE (VehicleTradeSystem)
    // =====================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi vozidlom a továrňami stanice, na ktorej
    /// vozidlo práve čaká. Volá sa z UpdateVehicle po 2 s čakania
    /// (TRADE_DELAY), práve raz za zastávku.
    ///
    /// Postup (analógia k TrainSystem.TryExecuteTradeAtStation):
    ///   1) Zisti tile, na ktorom vozidlo stojí (cieľová stanica).
    ///   2) Nájdi StationInstance pre tento tile v RoadStationRegistry.
    ///      Ak ešte nie je zaregistrovaná (napr. stanica pribudla bez
    ///      následného scanu), dorovná sa cez RescanAll.
    ///   3) Odovzdaj vozidlo + stanicu do VehicleTradeSystem.Execute.
    ///   4) Podľa výsledku vypíš Debug.Log (úspech / neúspech).
    ///
    /// Výpisy presne zodpovedajú zadaniu:
    ///   úspech  → "Predaj prebehol úspešne, cena {N} euro"
    ///   neúspech→ "Obchod neprebehol."
    /// </summary>
    void TryExecuteTradeAtStation(VehicleData vd)
    {
        // Bezpečnostné kontroly – bez dátovej štruktúry vozidla nemá
        // obchod zmysel.
        if (vd == null || vd.vehicleInstance == null)
        {
            Debug.Log("Obchod neprebehol.");
            return;
        }

        Vector2Int stationTile = vd.currentTile;

        // Nájdi cestnú stanicu v registri. Ak chýba, skús dorovnať register.
        StationInstance station = RoadStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        if (station == null)
        {
            RoadStationRegistry.RescanAll();
            station = RoadStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        }

        if (station == null)
        {
            Debug.Log("Obchod neprebehol.");
            Debug.LogWarning($"[VehicleSystem] Pre tile [{stationTile.x},{stationTile.y}] " +
                             $"sa nenašla cestná StationInstance – obchod sa neuskutočnil.");
            return;
        }

        // Vykonaj samotnú transakciu (čisto dátová operácia).
        VehicleTradeSystem.TradeResult result =
            VehicleTradeSystem.Execute(vd.vehicleInstance, station);

        if (result.Success)
        {
            // Pripíš zárobok na herné konto – preprava tovaru je jediný zdroj
            // príjmu, ktorý drží konto v pluse (a umožní reset cenového
            // násobiteľa po prepadnutí do mínusu).
            if (result.Revenue > 0)
            {
                if (GameEconomy.instance != null)
                    GameEconomy.instance.AddCredits((uint)result.Revenue);

                // EVIDENCIA pre ročnú uzávierku – tržba z úspešnej prepravy
                // (jediný zdroj príjmu hry, rovnako pre vlaky aj vozidlá).
                BudgetSystem.instance?.RecordRevenue(result.Revenue);

                // HUD – mesačné štatistiky obchodu v hornej lište.
                GameMenuHUDPanel.ReportSale(GameMenuHUDPanel.TransportKind.Road,
                    VehicleTradeSystem.VehicleResource(vd.vehicleInstance),
                    result.UnloadedUnits, result.Revenue);

                // ZVUK "Cash" – jediný samočinný zvuk hry. Volá sa pri každom
                // zárobku bezpodmienečne; GameManager si sám overí, či je
                // vozidlo v zábere kamery a či od posledného Cash uplynul
                // minimálny odstup – zvuk teda zaznie len pri obchode, ktorý
                // hráč vidí.
                if (vd.vehicleBody != null)
                    GameManager.instance?.PlaySfxCash(vd.vehicleBody.transform.position);
            }

            Debug.Log($"Obchodná transakcia bola vykonaná úspešne, suma {result.Revenue} CR.");
            Debug.Log($"[VehicleSystem] Obchod na stanici [{stationTile.x},{stationTile.y}]: " +
                      $"naložené {result.LoadedUnits}, vyložené {result.UnloadedUnits}, " +
                      $"zárobok {result.Revenue} CR.");

            // Plávajúci cenový label nad vozidlom na 3 s. Volá sa presne tu –
            // teda po 2 s od príchodu na stanicu (keď prebehne transakcia).
            if (vd.vehicleBody != null)
                SalePriceLabelManager.Instance.ShowPrice(
                    vd.vehicleBody.transform.position, result.Revenue);
        }
        else
        {
            Debug.Log("Obchod neprebehol.");
            Debug.Log($"[VehicleSystem] Obchod na stanici [{stationTile.x},{stationTile.y}] " +
                      $"neprebehol – dôvod: {result.Reason}.");
        }
    }

    // =====================================================================
    // ADVANCE STATION – OBRAT SMERU
    // =====================================================================

    void AdvanceStation(VehicleData vd)
    {
        if (!vd.reverseDirection)
        {
            vd.currentStationIndex++;
            if (vd.currentStationIndex >= vd.stations.Count)
            {
                vd.reverseDirection = true;
                vd.currentStationIndex = vd.stations.Count - 2;
                if (vd.currentStationIndex < 0) vd.currentStationIndex = 0;
            }
        }
        else
        {
            vd.currentStationIndex--;
            if (vd.currentStationIndex < 0)
            {
                vd.reverseDirection = false;
                vd.currentStationIndex = 1;
                if (vd.currentStationIndex >= vd.stations.Count)
                    vd.currentStationIndex = 0;
            }
        }

        ComputeNextPathAsync(vd);
    }

    // =====================================================================
    // ASYNC A* WRAPPER
    // =====================================================================

    void ComputeNextPathAsync(VehicleData vd)
    {
        if (vd.stations.Count == 0) return;
        if (vd.isComputingPath) return;

        Vector2Int goal = vd.stations[vd.currentStationIndex];
        Vector2Int start = vd.currentTile;
        int key = DepotKey(vd.depotX, vd.depotZ);
        var snap = SnapshotTileGrid();

        vd.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = false
            });
        });
    }

    // =====================================================================
    // TRIGGER MAP CHANGED REROUTE
    // =====================================================================

    void TriggerMapChangedReroute()
    {
        // Mapa sa zmenila (pribudli/ubudli cesty, stanice alebo továrne) –
        // prepočítaj 9×9 zóny cestných staníc a ich evidenciu tovární.
        // Lacná operácia (analógia k TrainSystem.TriggerMapChangedReroute,
        // ktorý volá StationRegistry.RescanAll()).
        RoadStationRegistry.RescanAll();

        var snap = SnapshotTileGrid();

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (!vd.isRunning) continue;
            if (vd.isComputingPath) continue;

            int key = kvp.Key;

            if (vd.isReturningToDepot)
            {
                Vector2Int start = vd.currentTile;
                Vector2Int goal = new Vector2Int(vd.depotX, vd.depotZ);
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = true,
                        destination = PathDestination.Depot
                    });
                });
            }
            else if (vd.isGoingToDepotViaStation)
            {
                Vector2Int? nearest = FindNearestStation(vd);
                if (!nearest.HasValue) continue;
                Vector2Int start = vd.currentTile;
                Vector2Int goal = nearest.Value;
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.ReturnViaStation
                    });
                });
            }
            else
            {
                if (vd.stations.Count == 0) continue;
                Vector2Int start = vd.currentTile;
                Vector2Int goal = vd.stations[vd.currentStationIndex];
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.Station
                    });
                });
            }
        }
    }

    Vector2Int? FindNearestStation(VehicleData vd)
    {
        Vector2Int? nearest = null;
        int bestDist = int.MaxValue;
        foreach (var st in vd.stations)
        {
            if (GetRoadTileID(st.x, st.y) != 2) continue;
            int dist = Math.Abs(vd.currentTile.x - st.x) + Math.Abs(vd.currentTile.y - st.y);
            if (dist < bestDist) { bestDist = dist; nearest = st; }
        }
        return nearest;
    }

    // =====================================================================
    // APLIKÁCIA VÝSLEDKOV A*
    // =====================================================================

    void ApplyPendingPathResults()
    {
        while (_pendingPathResults.TryDequeue(out PathResult result))
        {
            if (!vehicles.TryGetValue(result.depotKey, out VehicleData vd)) continue;

            vd.isComputingPath = false;
            bool hasPath = result.path != null && result.path.Count > 0;

            if (result.destination == PathDestination.Depot)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    vd.isRunning = false; vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] nemôže nájsť cestu do depa, čaká.");
                }
                continue;
            }

            if (result.destination == PathDestination.ReturnViaStation)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    Debug.LogWarning($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] nemôže nájsť cestu na stanicu. Zastane.");
                    vd.isRunning = false; vd.isWaiting = false; vd.isGoingToDepotViaStation = false;
                }
                continue;
            }

            if (result.isReturnToDepot)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    vd.isRunning = false; vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[VehicleSystem] Vozidlo nemôže nájsť cestu do depa, čaká.");
                }
            }
            else
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    bool anyStationExists = false;
                    foreach (var st in vd.stations)
                        if (GetRoadTileID(st.x, st.y) == 2) { anyStationExists = true; break; }

                    if (!anyStationExists)
                    {
                        Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}]: žiadne dostupné stanice – vozidlo zastane. Použite Send To Depot pre návrat.");
                        vd.isRunning = false; vd.isWaiting = false;
                        vd.isStoppedAwaitingDepotReturn = true;
                        vd.currentPath = new List<Vector2Int>(); vd.pathIndex = 0;
                        vd.activePath = null;

                        if (vd.currentTile.x == vd.depotX && vd.currentTile.y == vd.depotZ)
                            vd.isAtDepot = true;
                    }
                    else
                    {
                        if (vd.stations.Count > 0)
                        {
                            var goal = vd.stations[vd.currentStationIndex];
                            Debug.LogWarning($"[VehicleSystem] A* nenašiel cestu do [{goal.x},{goal.y}]. Čakám {BREAK_WAIT}s.");
                        }
                        vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                        vd.currentPath = new List<Vector2Int>(); vd.pathIndex = 0;
                        vd.activePath = null;

                        if (vd.currentTile.x == vd.depotX && vd.currentTile.y == vd.depotZ)
                            vd.isAtDepot = true;
                    }
                }
            }
        }
    }

    // =====================================================================
    // APPLY NEW PATH – konvertuje List<Vector2Int> na PathData (waypoint-based)
    // =====================================================================

    void ApplyNewPath(VehicleData vd, List<Vector2Int> newPath)
    {
        bool isMoving = vd.activePath != null && vd.vehicleDistance < vd.activePath.totalLength;

        Vector2Int startTile = vd.currentTile;
        int startIdxInNew = -1;
        for (int i = 0; i < newPath.Count; i++)
        {
            if (newPath[i] == startTile) { startIdxInNew = i; break; }
        }

        if (startIdxInNew < 0)
        {
            if (isMoving)
            {
                vd.pendingPath = newPath;
                return;
            }
            startIdxInNew = 0;
        }

        List<Vector2Int> subPath = newPath.GetRange(startIdxInNew, newPath.Count - startIdxInNew);

        vd.currentPath = newPath;
        vd.pathIndex = startIdxInNew;
        vd.pathIndexAtPathStart = startIdxInNew;
        vd.moveTimer = 0f;

        BuildWaypointPath(subPath, out var pts, out var tileIdxList);
        var newPathData = new PathData(pts);

        // Rýchlosť (maxSpeed/acceleration/deceleration) je fixná od
        // vytvorenia vozidla (ComputeMotionParams). vd.vehicleSpeed (aktuálna
        // rýchlosť) sa NERESETUJE – pohyb medzi po sebe idúcimi úsekmi trasy
        // zostáva plynulý, UpdateVehicle ju každý frame priblíži k cieľu.
        vd.vehicleDistance = 0f;
        vd.activePath = newPathData;
        vd.waypointToTileIdx = tileIdxList.ToArray();
        vd.subPathEntryHead = ResolveEntryHeads(subPath);
    }

    void RebuildActivePathFromCurrentPath(VehicleData vd)
    {
        if (vd.currentPath == null || vd.currentPath.Count == 0)
        {
            vd.activePath = null;
            return;
        }

        int startIdx = vd.pathIndex;
        if (startIdx >= vd.currentPath.Count) { vd.activePath = null; return; }

        List<Vector2Int> sub = vd.currentPath.GetRange(startIdx, vd.currentPath.Count - startIdx);

        BuildWaypointPath(sub, out var pts, out var tileIdxList);
        var pd = new PathData(pts);

        vd.pathIndexAtPathStart = startIdx;
        // Rýchlosť je fixná od vytvorenia vozidla (ComputeMotionParams) –
        // vd.vehicleSpeed (aktuálna rýchlosť) sa nemení, len sa resetuje pozícia.
        vd.vehicleDistance = 0f;
        vd.activePath = pd;
        vd.waypointToTileIdx = tileIdxList.ToArray();
        vd.subPathEntryHead = ResolveEntryHeads(sub);
    }

    /// <summary>CESTNÉ MOSTY / TUNELY: index vstupnej hlavy pre tily vo vnútri prechodu.</summary>
    static int[] ResolveEntryHeads(List<Vector2Int> subPath)
    {
        if (RoadCrossingSystem.instance == null) return null;
        RoadCrossingSystem.instance.ResolvePathCrossings(subPath, out int[] entryHeads);
        return entryHeads;
    }

    // =====================================================================
    // A* PATHFINDING – THREAD-SAFE
    // =====================================================================

    class AStarNode
    {
        public Vector2Int pos;
        public AStarNode parent;
        public float g, h;
        public float f => g + h;

        public AStarNode(Vector2Int p, AStarNode par, float g, float h)
        {
            pos = p; parent = par; this.g = g; this.h = h;
        }
    }

    static readonly Vector2Int[] DIRECTIONS =
    {
        new Vector2Int( 1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int( 0, 1),
        new Vector2Int( 0,-1)
    };

    static List<Vector2Int> AStarPathThreaded(TileGridSnapshot snap, Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return new List<Vector2Int>();

        var open = new List<AStarNode>();
        var closed = new HashSet<Vector2Int>();
        var nodeMap = new Dictionary<Vector2Int, AStarNode>();

        open.Add(new AStarNode(start, null, 0f, Heuristic(start, goal)));
        nodeMap[start] = open[0];

        int iterations = 0;
        const int MAX_ITER = 10000;

        while (open.Count > 0 && iterations < MAX_ITER)
        {
            iterations++;
            int bestIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bestIdx].f) bestIdx = i;

            AStarNode current = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (current.pos == goal)
                return ReconstructPath(current);

            closed.Add(current.pos);

            foreach (var dir in DIRECTIONS)
            {
                Vector2Int neighbor = current.pos + dir;
                if (closed.Contains(neighbor)) continue;

                IndicatrixAPI.DirectionMask dirMask = DirFromStep(dir);
                if (!CanMove(snap, current.pos, neighbor, dirMask))
                    continue;

                float newG = current.g + 1f;
                if (nodeMap.TryGetValue(neighbor, out AStarNode existing))
                {
                    if (newG < existing.g) { existing.g = newG; existing.parent = current; }
                }
                else
                {
                    var node = new AStarNode(neighbor, current, newG, Heuristic(neighbor, goal));
                    open.Add(node);
                    nodeMap[neighbor] = node;
                }
            }

            // ── MOST / TUNEL: skoková hrana z hlavy na partnerskú hlavu ──
            if (snap.jumpTarget != null)
            {
                int curIdx = current.pos.y * GRID_SIZE + current.pos.x;
                int target = snap.jumpTarget[curIdx];
                if (target >= 0)
                {
                    var jumpNeighbor = new Vector2Int(target % GRID_SIZE, target / GRID_SIZE);
                    if (!closed.Contains(jumpNeighbor))
                    {
                        float jumpG = current.g + Mathf.Max(1, snap.jumpCost[curIdx]);
                        if (nodeMap.TryGetValue(jumpNeighbor, out AStarNode existingJump))
                        {
                            if (jumpG < existingJump.g) { existingJump.g = jumpG; existingJump.parent = current; }
                        }
                        else
                        {
                            var jumpNode = new AStarNode(jumpNeighbor, current, jumpG, Heuristic(jumpNeighbor, goal));
                            open.Add(jumpNode);
                            nodeMap[jumpNeighbor] = jumpNode;
                        }
                    }
                }
            }
        }

        return null;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
        => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    /// <summary>
    /// Zostaví trasu z A* uzlov (BEZ štartového tile – pôvodné správanie).
    /// CESTNÉ MOSTY / TUNELY: skok medzi hlavami sa rozvinie na všetky tily
    /// medzi nimi → trasa je vždy SÚVISLÁ. Ak je PRVÝ krok skokom, štartový
    /// tile sa ponechá (inak by trasa začínala vnútorným tile mosta a nedalo
    /// by sa rozlíšiť most od cesty pod ním). Analógia k TrainSystem.
    /// </summary>
    static List<Vector2Int> ReconstructPath(AStarNode node)
    {
        var chain = new List<Vector2Int>();
        for (var cur = node; cur != null; cur = cur.parent) chain.Add(cur.pos);
        chain.Reverse();                         // chain[0] = štart

        var path = new List<Vector2Int>();
        if (chain.Count >= 2 && !IsAdjacentTile(chain[0], chain[1]))
            path.Add(chain[0]);

        for (int i = 1; i < chain.Count; i++)
        {
            Vector2Int a = chain[i - 1];
            Vector2Int b = chain[i];
            if (!IsAdjacentTile(a, b))
            {
                var step = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                if (step.x != 0 && step.y != 0) { path.Add(b); continue; } // poistka
                for (Vector2Int p = a + step; p != b; p += step) path.Add(p);
            }
            path.Add(b);
        }
        return path;
    }

    static bool IsAdjacentTile(Vector2Int a, Vector2Int b)
        => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) == 1;

    public void WriteSave(System.IO.BinaryWriter bw)
    {
        bw.Write(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            var inst = vd.vehicleInstance;

            bw.Write(vd.depotX);
            bw.Write(vd.depotZ);

            // Typ vozidla ukladáme ako Name (CreateVehicle berie názov typu).
            bw.Write(inst != null ? inst.Spec.Name : "");

            bw.Write(inst != null ? inst.Age : 0);
            bw.Write(inst != null ? inst.CurrentCapacity : 0);

            bw.Write(vd.stations != null ? vd.stations.Count : 0);
            if (vd.stations != null)
                foreach (var s in vd.stations) { bw.Write(s.x); bw.Write(s.y); }

            bw.Write(vd.isRunning);
        }
    }

    // =====================================================================
    // ODLOŽENÉ SPUSTENIE VOZIDIEL PO LOAD
    // ─────────────────────────────────────────────────────────────────────
    // Analógia k TrainSystem.StartTrainsAfterLoad. ReadSave beží v
    // IndicatrixAPI.LoadGame SKÔR, než sa načítajú cestné mosty a tunely
    // (RoadCrossingSystem). Okamžité StartVehicle by vzalo A* snapshot bez
    // skokových hrán → trasa cez most/tunel by sa nenašla a vozidlo by
    // preskočilo prvú stanicu. Bežiace vozidlá sa preto spustia až na konci
    // IndicatrixAPI.LoadGame.
    // =====================================================================
    private readonly List<Vector2Int> _startAfterLoad = new List<Vector2Int>();

    /// <summary>
    /// Spustí vozidlá, ktoré v uloženej hre bežali. Volá IndicatrixAPI.LoadGame
    /// AŽ PO načítaní mostov a tunelov.
    /// </summary>
    public void StartVehiclesAfterLoad()
    {
        foreach (var d in _startAfterLoad)
            StartVehicle(d.x, d.y);
        _startAfterLoad.Clear();
    }

    public void ReadSave(System.IO.BinaryReader br)
    {
        _startAfterLoad.Clear();

        int count = br.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            int dx = br.ReadInt32();
            int dz = br.ReadInt32();
            string typeName = br.ReadString();
            int age = br.ReadInt32();
            int cargo = br.ReadInt32();

            int stationCnt = br.ReadInt32();
            var stations = new System.Collections.Generic.List<Vector2Int>(stationCnt);
            for (int i = 0; i < stationCnt; i++)
                stations.Add(new Vector2Int(br.ReadInt32(), br.ReadInt32()));

            bool wasRunning = br.ReadBoolean();

            if (!CreateVehicle(dx, dz, typeName))
                continue;

            VehicleData vd = GetVehicle(dx, dz);
            if (vd == null) continue;

            if (vd.vehicleInstance != null)
            {
                vd.vehicleInstance.Age = age;
                vd.vehicleInstance.CurrentCapacity = cargo;
            }

            foreach (var s in stations)
                AddStation(dx, dz, s.x, s.y);

            // NESPÚŠŤAŤ tu – mosty/tunely ešte nie sú načítané (pozri
            // StartVehiclesAfterLoad). Spustí sa na konci IndicatrixAPI.LoadGame.
            if (wasRunning)
                _startAfterLoad.Add(new Vector2Int(dx, dz));
        }
    }

}