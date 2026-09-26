using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.TrainStock;

/// <summary>
/// TrainSystem.cs
///
/// PARALELIZMUS a DEBOUNCE:
/// ─────────────────────────────────────────────────────────────────────────
/// ✔ A* pathfinding beží na Thread Pool cez Task.Run (AStarPathThreaded).
///   Pracuje s int[] snapshotom tileGrid – žiadne Unity API z vlákna.
///   Výsledky sa prenášajú cez ConcurrentQueue, aplikujú sa v Update().
///
/// ✔ OnMapChanged() – DEBOUNCE (riešenie trhnutia vlakov):
///   Každé volanie len nastaví flag + resetuje timer (_mapChangedDebounceTimer).
///   Žiadna práca na hlavnom vlákne pri kliku (žiadny SnapshotTileGrid).
///   Update() odpočítava timer; až po uplynutí DEBOUNCE_DELAY od posledného
///   kliku spustí SnapshotTileGrid() + Task.Run A* pre bežiace vlaky.
///   Vlaky sa hýbu ďalej po starej ceste počas čakania na debounce –
///   žiadne zastavenie, žiadne trhnutie.
///
///   Prepočítavajú sa len vlaky, ktorých aktuálna cesta mohla byť dotknutá
///   (isRunning && !isComputingPath). Vlaky, ktoré práve čakajú na stanici
///   alebo pri prerušení, sa neprepočítavajú zbytočne.
///
/// ✘ UpdateTrain / pohyb – hlavné vlákno (Unity API, thread-unsafe).
/// ✘ CreateTrain / RemoveTrain – hlavné vlákno (GameObject API).
/// ─────────────────────────────────────────────────────────────────────────
///
/// VLAKOVÁ SÚPRAVA – „Distance-Based Path System":
/// ─────────────────────────────────────────────────────────────────────────
/// • Trasa je reprezentovaná ako PathData (List<Vector3> bodov + predpočítané
///   kumulatívne dĺžky segmentov). Zdroj: A* ako List<Vector2Int> → konvertovaný
///   na waypointy cez BuildWaypointPath (centrá + hrany + krivkové rohy).
/// • Lokomotíva má skalárnu hodnotu locomotiveDistance (vzdialenosť pozdĺž cesty).
///   Každý frame: locomotiveDistance += speed * deltaTime.
/// • Vagón i má offsetDist = i * wagonSpacing za lokomotívou.
///   wagonDistance = locomotiveDistance - (i * wagonSpacing), min 0.
/// • Pozícia = PathData.GetPositionAtDistance(d)   → Vector3.Lerp na segmente.
/// • Rotácia = PathData.GetDirectionAtDistance(d)  → Quaternion.LookRotation.
/// • ŽIADNA história, ŽIADNE oneskorenie, ŽIADNE posHistory / historyAccum.
/// • Deterministické: rovnaký vstup → rovnaký výstup každý frame.
///
/// WAYPOINT-BASED MOVEMENT (NOVÉ):
///   Trasa už nie je center→center. Pre každý prechod A→B sa generuje:
///     A.center → A.edge(smer A→B) → B.edge(smer B→A) → B.center
///   Pri krivkovej dlaždici sa center NEPOUŽÍVA – generuje sa:
///     B.entryEdge → B.exitEdge  (priama diagonála cez vnútro dlaždice, ~45°)
///   Pri výhybkovej dlaždici (RailSwitch*) sa správanie líši podľa toho,
///   či ide o priamy prechod (proti-smery) alebo o odbočku (kolmé smery):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge (ako priama dlaždica)
///     • ODBOČKA: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge
///       (3 priame segmenty, stredný diagonálny ~45°; vizuálne železničný
///        turnout, BEZ bezier kriviek, BEZ splajnov, BEZ smoothingu.)
///   Pri križovatkovej dlaždici (RailCrossroad, 4 spojenia) platí to isté:
///     • PRIAMY (Top↔Bottom, Left↔Right): edge → center → edge (90°)
///     • ROHOVÝ (napr. Top↔Right): edge → innerEntry → innerExit → edge
///       (45° turnout, identický s odbočkou výhybky)
///   Krivka, odbočka výhybky aj rohový prechod križovatky používajú
///   VÝHRADNE priame čiarové segmenty spojené lineárnym Lerp-om medzi
///   waypointmi.
///
/// PREJAZD CEZ SVAH (LevelUp / LevelDown):
///   Terén je výškový raster v ROHOCH dlaždíc – jedna dlaždica je
///   naklonený quad medzi 4 rohovými vertexmi s rôznymi Y. Y-súradnica
///   každého waypointu sa získava biliniárnou interpoláciou výšok týchto
///   4 rohov (GetTileSurfaceY), takže každý bod na trase leží presne na
///   šikmej ploche dlaždice. Vlak na svahu ide pod skutočným uhlom
///   stúpania – uhol je daný geometriou terénu, nie pevnou konštantou.
///   Hladkosť na hrane medzi dlaždicami je zaručená automaticky:
///   A.right-edge a B.left-edge zdieľajú tie isté dva rohové vertexy,
///   takže ich interpolovaná Y vychádza identická → žiadny schod, žiadny
///   90° lom medzi dlaždicami. Logika svahu je transparentná pre všetky
///   typy dlaždíc – priame, krivky, výhybky, križovatky.
///
/// OBRAT SMERU NA STANICI:
///   path.Reverse()  +  locomotiveDistance = totalLength - locomotiveDistance
///   → okamžitý, presný, bez usadzovania.
///
/// PARAMETRIZÁCIA:
///   wagonCount   – počet vagónov (1 až 10, predvolene 5).
///   wagonSpacing – rozostup členov súpravy pozdĺž trajektórie (predvolene 0.85).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class TrainSystem : MonoBehaviour
{
    public static TrainSystem instance;

    // =====================================================================
    // KONFIGURÁCIA VLAKOVEJ SÚPRAVY
    // =====================================================================

    [Range(1, 10)]
    public int wagonCount = 5;

    public float wagonSpacing = 0.85f;

    // =====================================================================
    // KNIŽNICA 3D MODELOV (voliteľná)
    // ---------------------------------------------------------------------
    // Referencia na komponent TrainModelLibrary, ktorý drží voliteľné
    // prefab-y skutočných 3D modelov pre jednotlivé typy lokomotív a vagónov.
    //
    // LOGIKA NAHRADENIA (zadanie):
    //   • Ak je pre daný typ v knižnici priradený prefab  → CreateConsistPart
    //     vytvorí inštanciu tohto modelu namiesto kvádra.
    //   • Ak prefab priradený NIE JE (null)               → vytvorí sa pôvodný
    //     kváder (CYAN lokomotíva / GREY vagón) – presne ako doteraz.
    //
    // Referencia je voliteľná: ak nie je priradená v Inspectore, systém ju
    // dohľadá cez TrainModelLibrary.instance, prípadne FindObjectOfType.
    // Ak knižnica v scéne vôbec nie je, všetko ostane na pôvodných kvádroch.
    // =====================================================================
    [SerializeField] private TrainModelLibrary modelLibrary;

    // =====================================================================
    // PRISPÔSOBENIE VEĽKOSTI 3D MODELOV DLAŽDICI (fit-to-tile)
    // ---------------------------------------------------------------------
    // Analógia k IndicatrixAPI.fitTileModelsToTile / FitTileModel: prefab-y
    // dlaždíc sa proporčne preškálujú na 1 dlaždicu, prefab-y vlakov doteraz
    // NIE (scale sa zámerne nechával autorský). Preto sa modely v reálnych
    // metroch (lokomotíva ~20 m = 20 Unity jednotiek = 20 dlaždíc) javia
    // obrovské. Tieto polia zapínajú rovnaké normalizovanie aj pre súpravu.
    // =====================================================================

    [Header("3D modely súpravy – prispôsobenie veľkosti")]

    [Tooltip("Zapnuté = inštancia prefabu z TrainModelLibrary sa proporčne " +
             "preškáluje na rozmer dlaždice a vycentruje na os trate. " +
             "Vypnuté = pôvodné správanie (model si nesie vlastnú mierku).")]
    [SerializeField] private bool fitTrainModelsToTile = true;

    [Tooltip("Cieľová DĹŽKA jedného člena súpravy v dlaždiciach (1.0 = celá " +
             "dlaždica). Pôvodný kváder má 0.7 – pri wagonSpacing 0.85 to " +
             "necháva medzeru 0.15 medzi vozňami.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float consistMemberLength = 0.7f;

    [Tooltip("Maximálna ŠÍRKA člena súpravy v dlaždiciach. Ak je model po " +
             "preškálovaní na dĺžku širší, doškáluje sa nadol (aby sa vošiel " +
             "medzi koľajnice). Pôvodný kváder má 0.3.")]
    [Range(0.1f, 1.0f)]
    [SerializeField] private float consistMemberMaxWidth = 0.35f;

    [Tooltip("Zvislý posun modelu voči trati. 0 = model stojí spodkom presne " +
             "na trati. Pôvodný kváder mal na trati svoj STRED, teda bol o pol " +
             "svojej výšky zapustený – ak chceš rovnaké usadenie ako kváder, " +
             "daj zápornú hodnotu (napr. -0.05).")]
    [SerializeField] private float consistModelYOffset = 0f;

    [Tooltip("Diagnostika: pre každý vytvorený model vypíše do konzoly namerané " +
             "rozmery pred/po fite a výsledný stred. Slúži na overenie, či je " +
             "model naozaj vycentrovaný (očakávané center.x≈0, center.z≈0, min.y≈0).")]
    [SerializeField] private bool logConsistFit = false;

    [Tooltip("Diagnostika: k modelu pridá MAGENTOVÝ referenčný kváder v presne " +
             "tej polohe a veľkosti, akú mal pôvodný kváder (CONSIST_SCALE) – " +
             "teda vycentrovaný na pivot. Slúži na rozlíšenie, či je vychýlený " +
             "MODEL voči pivotu, alebo celý PIVOT voči koľajniciam.")]
    [SerializeField] private bool debugShowReferenceCube = false;

    // =====================================================================
    // POMOCNÁ TRIEDA – PathData
    // Ukladá trasu ako Vector3 body + predpočítané kumulatívne vzdialenosti.
    // =====================================================================

    public class PathData
    {
        /// <summary>Body trasy (waypointy: centrá + hrany + krivkové rohy). Nemeniť po inicializácii.</summary>
        public readonly List<Vector3> points;

        /// <summary>
        /// cumulativeLengths[i] = vzdialenosť od points[0] po points[i].
        /// cumulativeLengths[0] == 0 vždy.
        /// </summary>
        public readonly float[] cumulativeLengths;

        /// <summary>Celková dĺžka trasy = cumulativeLengths[Count-1].</summary>
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
        /// Interpolácia je VÝHRADNE lineárna (Vector3.Lerp) medzi dvoma susednými bodmi.
        /// </summary>
        public Vector3 GetPositionAtDistance(float dist)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];

            dist = Mathf.Clamp(dist, 0f, totalLength);

            // Binárne vyhľadávanie segmentu
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
        /// Smer = (nextPoint - currentPoint).normalized pre segment, v ktorom leží dist.
        /// NIKDY neakumuluje rotácie, NIKDY nepoužíva dáta z predchádzajúceho snímku.
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

        /// <summary>
        /// Vráti obrátenú kópiu PathData (pre obrat smeru na stanici).
        /// Pôvodný objekt zostáva nezmenený.
        /// </summary>
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
        /// záporná = klesanie (z kopca), nula = rovina.
        ///
        /// Keďže waypointy (viď BuildWaypointPath / GetTileSurfaceY) už majú
        /// fyzicky správnu výšku podľa naklonenia terénu, sklon segmentu
        /// zodpovedá skutočnému stúpaniu/klesaniu dlaždice, po ktorej sa
        /// súprava práve pohybuje. Používa sa v TargetSpeedForGrade() na
        /// spomalenie do kopca / zrýchlenie z kopca.
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

    public class TrainData
    {
        public int depotX, depotZ;

        /// <summary>
        /// Dátový popis vlakovej súpravy – typ vlaku + jeho atribúty + vagóny
        /// a ich atribúty (Name, Cost, Speed, kapacity vagónov atď.).
        /// Definované v TrainStock.cs. Pohybový/pathfinding stav zostáva
        /// v poliach TrainData nižšie; toto je oddelená "obsahová" časť.
        ///
        /// Naplní sa pri CreateTrain podľa voľby z TrainTypeDropdown /
        /// WagonTypeDropdown. UI okno s detailmi vlaku si odtiaľto len číta.
        /// </summary>
        public TrainInstance consist;

        /// <summary>Skrytá kocka (Renderer off) – pohybová logika (pozícia hlavy).</summary>
        public GameObject trainObject;

        /// <summary>Vizuálna lokomotíva (CYAN kváder).</summary>
        public GameObject locomotive;

        /// <summary>Vizuálne vagóny (GREY kvádre). Count = wagonCount pri vytvorení.</summary>
        public List<GameObject> wagons = new List<GameObject>();

        // ------------------------------------------------------------------
        // DISTANCE-BASED PATH STATE
        // ------------------------------------------------------------------

        /// <summary>
        /// Aktuálna trasa ako PathData (waypointy + kumulatívne dĺžky).
        /// null = žiadna aktívna trasa.
        /// </summary>
        public PathData activePath;

        /// <summary>
        /// Vzdialenosť lokomotívy od začiatku activePath.
        /// Každý frame: locomotiveDistance += trainSpeed * Time.deltaTime.
        /// </summary>
        public float locomotiveDistance;

        /// <summary>
        /// AKTUÁLNA rýchlosť súpravy (jednotky/sekunda). Na rozdiel od pôvodnej
        /// verzie sa NEJEDNÁ o konštantu – mení sa plynulo každý frame smerom
        /// k okamžitej cieľovej rýchlosti (podľa sklonu trate a blízkosti konca
        /// trasy), rýchlosťou danou poľami <see cref="acceleration"/> /
        /// <see cref="deceleration"/>. Viď UpdateTrain().
        /// </summary>
        public float trainSpeed;

        /// <summary>
        /// Maximálna rýchlosť súpravy NA ROVINE (jednotky/sekunda), odvodená
        /// z TrainSpec.Speed lokomotívy (viď TrainSystem.SpeedUnitsPerSecond).
        /// Do kopca sa skutočná cieľová rýchlosť znižuje, z kopca zvyšuje –
        /// pozri TargetSpeedForGrade(). Nemení sa počas jazdy, iba pri
        /// vytvorení vlaku (ComputeMotionParams) – zloženie súpravy je fixné.
        /// </summary>
        public float maxSpeed;

        /// <summary>
        /// Zrýchlenie súpravy (jednotky/sekunda²) – odvodené z pomeru
        /// Power/Weight lokomotívy a celej súpravy (ComputeMotionParams).
        /// Výkonnejšia/ľahšia súprava sa rozbieha rýchlejšie.
        /// </summary>
        public float acceleration;

        /// <summary>
        /// Spomalenie/brzdenie súpravy (jednotky/sekunda²) – vždy o niečo
        /// silnejšie než acceleration (reálne brzdy spomaľujú rýchlejšie,
        /// než dokáže motor zrýchľovať).
        /// </summary>
        public float deceleration;

        // ------------------------------------------------------------------
        // Pôvodné polia TrainData (nezmenené)
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
        // OBCHOD NA STANICI (TradeSystem)
        // ------------------------------------------------------------------
        /// <summary>
        /// True, ak v AKTUÁLNEJ zastávke na stanici už prebehol pokus o
        /// transakciu (výmenu tovaru). Bráni tomu, aby sa obchod spustil
        /// každý frame – spustí sa práve raz, po 2 s čakania. Resetuje sa
        /// pri každom novom príchode na stanicu (OnPathComplete).
        /// </summary>
        public bool tradeDoneAtStation;

        /// <summary>Index v currentPath kde activePath zacina. Pouziva sa v UpdateCurrentTile.</summary>
        public int pathIndexAtPathStart;

        /// <summary>
        /// Mapovanie waypoint-index → currentPath-index.
        /// Pre activePath.points[i] hovorí, ku ktorej dlaždici v currentPath waypoint patrí.
        /// Používa sa v UpdateCurrentTile pri waypoint-based pohybe.
        /// </summary>
        public int[] waypointToTileIdx;

        /// <summary>
        /// MOSTY / TUNELY: pre každý tile SUB-PATH (activePath) index vstupnej
        /// hlavy prechodu, ak tile leží VNÚTRI mosta/tunela; inak -1 (alebo
        /// celé pole null, ak trasa žiadny prechod nemá). Kým je vlak vo vnútri
        /// prechodu, currentTile ostáva na VSTUPNEJ hlave – tile pod mostom môže
        /// patriť inej koľaji a A* musí štartovať z jednoznačného miesta.
        /// </summary>
        public int[] subPathEntryHead;

        public TrainData(int dx, int dz)
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
            locomotiveDistance = 0f;
            trainSpeed = 0f;
            maxSpeed = 0f;
            acceleration = 0f;
            deceleration = 0f;
            pathIndexAtPathStart = 0;
            waypointToTileIdx = null;

            consist = null;
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
    // SNAPSHOT TILE GRIDU
    // =====================================================================

    const int GRID_SIZE = 256;

    /// <summary>
    /// Snapshot tileGrid. Obsahuje DVA paralelné polia:
    ///   tileIDs[i]     – tileID dlaždice (0 = empty, 1 = rail, 2 = station, 3 = depot)
    ///   connections[i] – DirectionMask dlaždice (uložený ako int pre vlákno-bezpečné použitie)
    /// Indexovanie: i = z * GRID_SIZE + x
    /// </summary>
    struct TileGridSnapshot
    {
        public int[] tileIDs;
        public int[] connections;

        // MOSTY / TUNELY (RailCrossingSystem) – skokové hrany medzi hlavami:
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
                var td = IndicatrixAPI.instance.GetTileByIndex(x, z);
                int idx = z * GRID_SIZE + x;
                snap.tileIDs[idx] = td.tileID;
                snap.connections[idx] = (int)td.connections;
            }
        }

        // Mosty a tunely – skokové hrany (len ak nejaké existujú).
        var crossings = RailCrossingSystem.instance;
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

    Dictionary<int, TrainData> trains = new Dictionary<int, TrainData>();

    /// <summary>Čas prechodu jednej dlaždice v sekundách (1.0 = 1 s/dlaždica).</summary>
    const float MOVE_TIME = 2.0f;
    const float STATION_WAIT = 10.0f;
    const float BREAK_WAIT = 1.0f;

    // =====================================================================
    // FYZIKA POHYBU – rýchlosť, zrýchlenie/spomalenie, sklon trate
    // =====================================================================

    /// <summary>
    /// Referenčná hodnota TrainSpec.Speed, ktorá zodpovedá pôvodnému tempu
    /// pohybu (1 dlaždica / MOVE_TIME sekúnd, na rovine). Vlak so
    /// Speed == SPEED_REFERENCE sa na rovine pohybuje presne tak rýchlo ako
    /// predtým (zachované vyváženie hry) – rýchlejšie/pomalšie typy vlakov
    /// sa teraz už reálne líšia (predtým TrainSpec.Speed nemal na pohyb
    /// žiadny vplyv).
    /// </summary>
    const float SPEED_REFERENCE = 100f;

    /// <summary>Ladiaca konštanta: prevod pomeru Power/Weight na zrýchlenie (jednotky/s²).</summary>
    const float ACCEL_TUNING = 0.02f;

    /// <summary>Brzdenie je vždy silnejšie než rozjazd (reálne správanie bŕzd).</summary>
    const float BRAKE_MULTIPLIER = 1.6f;

    /// <summary>Absolútne minimum zrýchlenia/spomalenia – aby extrémne ťažké súpravy nezamrzli na mieste.</summary>
    const float MIN_ACCEL = 0.15f;

    /// <summary>Citlivosť spomalenia do kopca (na jednotku sklonu trate).</summary>
    const float UPHILL_GRADE_SENSITIVITY = 1.3f;

    /// <summary>Citlivosť zrýchlenia z kopca (menšia než do kopca – bezpečnostný limit).</summary>
    const float DOWNHILL_GRADE_SENSITIVITY = 0.6f;

    /// <summary>Aj na najprudšom stúpaní neklesne cieľová rýchlosť pod tento podiel z maxSpeed.</summary>
    const float MIN_UPHILL_SPEED_FRACTION = 0.35f;

    /// <summary>Aj na najprudšom klesaní nevystúpi cieľová rýchlosť nad tento násobok maxSpeed.</summary>
    const float MAX_DOWNHILL_SPEED_FACTOR = 1.35f;

    /// <summary>
    /// Minimálna "dobiehacia" rýchlosť (jednotky/sekunda) tesne pred cieľom
    /// trasy. Bez nej by lineárne spomaľovanie (Mathf.MoveTowards smerom k
    /// targetSpeed = 0) mohlo teoreticky dosiahnuť presnú nulu skôr, než
    /// locomotiveDistance dosiahne totalLength – vlak by "zamrzol" tesne
    /// pred stanicou. Táto konštanta zaručí, že súprava vždy nakoniec
    /// dorazí na koniec trasy v konečnom čase.
    /// </summary>
    const float MIN_ARRIVAL_CRAWL_SPEED = 0.05f;

    /// <summary>
    /// Po koľkých sekundách čakania na cieľovej stanici vlak realizuje
    /// výmenu tovaru (TradeSystem). Zadanie: štandardné čakanie je 10 s
    /// (STATION_WAIT), transakcia prebehne po 2 s od príchodu.
    ///
    /// Trigger je naviazaný na UPLYNULÝ čas: spustí sa, keď
    /// (STATION_WAIT - waitTimer) >= TRADE_DELAY, t.j. po 2 s čakania.
    /// </summary>
    const float TRADE_DELAY = 2.0f;

    /// <summary>Rozmery kvádra lokomotívy aj vagónov.</summary>
    static readonly Vector3 CONSIST_SCALE = new Vector3(0.3f, 0.3f, 0.7f);

    void Awake()
    {
        instance = this;
        wagonCount = Mathf.Clamp(wagonCount, 1, 10);
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    int DepotKey(int x, int z) => x * 10000 + z;

    /// <summary>Prevedie abstraktnú hodnotu TrainSpec.Speed na jednotky/sekundu (viď SPEED_REFERENCE).</summary>
    float SpeedUnitsPerSecond(int specSpeed) => (specSpeed / SPEED_REFERENCE) * (1f / MOVE_TIME);

    /// <summary>
    /// Vypočíta a uloží maxSpeed/acceleration/deceleration súpravy z parametrov
    /// lokomotívy a vagónov (TrainStock.cs). Volá sa raz pri vytvorení vlaku
    /// (CreateTrain) – zloženie súpravy sa počas hry nemení, netreba prepočítavať
    /// pri každej zmene trasy.
    ///
    /// FYZIKÁLNE ZDÔVODNENIE:
    ///   • maxSpeed     – priamo z TrainSpec.Speed lokomotívy (rýchlosť na rovine).
    ///   • acceleration – Power lokomotívy / celková hmotnosť súpravy (lokomotíva
    ///                    + všetky vagóny). Výkonnejšia/ľahšia súprava sa rozbieha
    ///                    rýchlejšie, ťažká/podvýkonná pomalšie.
    ///   • deceleration – o niečo silnejšie než acceleration (brzdy).
    /// </summary>
    void ComputeMotionParams(TrainData td)
    {
        if (td?.consist == null) return;

        TrainInstance c = td.consist;
        int totalWeight = c.Weight;
        foreach (var w in c.Wagons) totalWeight += w.Weight;
        totalWeight = Mathf.Max(totalWeight, 1);

        td.maxSpeed = SpeedUnitsPerSecond(c.Speed);

        float powerToWeight = (float)c.Power / totalWeight;
        td.acceleration = Mathf.Max(MIN_ACCEL, powerToWeight * ACCEL_TUNING);
        td.deceleration = Mathf.Max(MIN_ACCEL, td.acceleration * BRAKE_MULTIPLIER);
    }

    /// <summary>
    /// Cieľová rýchlosť súpravy pre daný sklon trate (grade, viď
    /// PathData.GetGradeAtDistance): do kopca sa znižuje (najviac po
    /// MIN_UPHILL_SPEED_FRACTION z maxSpeed), z kopca sa zvyšuje (najviac po
    /// MAX_DOWNHILL_SPEED_FACTOR z maxSpeed). Výsledok slúži ako cieľ pre
    /// plynulé zrýchľovanie/spomaľovanie (Mathf.MoveTowards v UpdateTrain).
    /// </summary>
    float TargetSpeedForGrade(TrainData td, float grade)
    {
        float factor = (grade > 0f)
            ? Mathf.Max(MIN_UPHILL_SPEED_FRACTION, 1f - grade * UPHILL_GRADE_SENSITIVITY)
            : Mathf.Min(MAX_DOWNHILL_SPEED_FACTOR, 1f - grade * DOWNHILL_GRADE_SENSITIVITY);

        return td.maxSpeed * factor;
    }

    Vector3 TileCenter(Vector2Int tile)
    {
        // Stred dlaždice: (u, v) = (0.5, 0.5) v lokálnych súradniciach.
        // Y sa získa biliniárnou interpoláciou výšok 4 rohov dlaždice
        // → priemer výšok všetkých 4 rohov (na rovine konštanta, na svahu
        // sa správne škáluje).
        float y = GetTileSurfaceY(tile, 0.5f, 0.5f) + TRACK_OFFSET_Y;
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

    /// <summary>Vertikálny offset koľaje nad povrchom terénu (track height).</summary>
    const float TRACK_OFFSET_Y = 0.3f;

    /// <summary>
    /// Biliniárna interpolácia výšky terénu vnútri jednej dlaždice (tile)
    /// medzi 4 rohovými vertexmi.
    ///
    /// Lokálne súradnice (u, v) ∈ [0, 1] × [0, 1]:
    ///   (u=0, v=0) = ľavý-dolný roh dlaždice  → vertex (tile.x,   tile.y)
    ///   (u=1, v=0) = pravý-dolný roh dlaždice → vertex (tile.x+1, tile.y)
    ///   (u=0, v=1) = ľavý-horný roh dlaždice  → vertex (tile.x,   tile.y+1)
    ///   (u=1, v=1) = pravý-horný roh dlaždice → vertex (tile.x+1, tile.y+1)
    /// kde u zodpovedá X-osi (Left→Right), v zodpovedá Z-osi (Bottom→Top).
    ///
    /// Vzorec biliniárnej interpolácie:
    ///   y = (1-u)(1-v) * h00 + u*(1-v) * h10 + (1-u)*v * h01 + u*v * h11
    ///
    /// PREČO TOTO POTREBUJEME (kopce, LevelUp / LevelDown):
    ///   Aktuálny terénny model ukladá výšku v ROHOCH dlaždice. Jedna
    ///   dlaždica je naklonený quad medzi 4 rohmi s rôznymi Y. Ak by sme
    ///   pre celý waypoint v rámci dlaždice používali iba výšku jedného
    ///   rohu (napr. ľavého-dolného), všetky waypointy v dlaždici by mali
    ///   rovnaké Y → vlak ide vodorovne a na hrane medzi dlaždicami
    ///   "vyskočí" na novú výšku → ostrý 90° schod namiesto 45° svahu.
    ///
    ///   Biliniárnou interpoláciou dostáva každý waypoint správnu výšku
    ///   podľa svojej polohy (u, v) na šikmej ploche, takže pohyb cez
    ///   svah je plynulý a uhol stúpania presne zodpovedá skutočnému
    ///   prevýšeniu medzi rohmi terénu.
    ///
    /// HLADKOSŤ NA HRANÁCH MEDZI DLAŽDICAMI:
    ///   Pre dve susediace dlaždice A a B (B = A + Right) platí:
    ///     A.exitEdge(Right)  → použije priemer výšok dvoch pravých rohov A
    ///     B.entryEdge(Left)  → použije priemer výšok dvoch ľavých rohov B
    ///   Ľavé rohy B sú TOTOŽNÉ vertexy ako pravé rohy A → výška vychádza
    ///   identická. Prechod cez hranu je teda C0-spojitý automaticky.
    ///   Žiadna explicitná konštanta uhla ani manuálne dorovnávanie nie je
    ///   potrebné – uhol svahu je daný geometriou terénu.
    ///
    /// Funkcia je deterministická a thread-safe pokiaľ ide o vstupy
    /// (číta len TerrainManager.instance.coordsF, ktoré sa nemení mimo
    /// hlavného vlákna v rámci jedného frame-u).
    /// </summary>
    float GetTileSurfaceY(Vector2Int tile, float u, float v)
    {
        float h00 = GetTerrainY(tile.x, tile.y);     // ľavý-dolný roh
        float h10 = GetTerrainY(tile.x + 1, tile.y);     // pravý-dolný roh
        float h01 = GetTerrainY(tile.x, tile.y + 1); // ľavý-horný roh
        float h11 = GetTerrainY(tile.x + 1, tile.y + 1); // pravý-horný roh

        float omu = 1f - u;
        float omv = 1f - v;

        return omu * omv * h00
             + u * omv * h10
             + omu * v * h01
             + u * v * h11;
    }

    int GetTileID(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE) return -1;
        return IndicatrixAPI.instance.GetTileByIndex(x, z).tileID;
    }

    bool IsPassable(int x, int z)
    {
        int id = GetTileID(x, z);
        return id == 1 || id == 2 || id == 3;
    }

    // =====================================================================
    // DIRECTION HELPERS – mapovanie Vector2Int <-> DirectionMask
    // =====================================================================

    /// <summary>
    /// Konvertuje 4-smerový vektor (zo step v A*) na DirectionMask.
    /// (+1, 0)  → Right
    /// (-1, 0)  → Left
    /// ( 0,+1)  → Top
    /// ( 0,-1)  → Bottom
    /// </summary>
    static IndicatrixAPI.DirectionMask DirFromStep(Vector2Int step)
    {
        if (step.x == 1 && step.y == 0) return IndicatrixAPI.DirectionMask.Right;
        if (step.x == -1 && step.y == 0) return IndicatrixAPI.DirectionMask.Left;
        if (step.x == 0 && step.y == 1) return IndicatrixAPI.DirectionMask.Top;
        if (step.x == 0 && step.y == -1) return IndicatrixAPI.DirectionMask.Bottom;
        return IndicatrixAPI.DirectionMask.None;
    }

    /// <summary>
    /// Vráti smer pohybu z dlaždice 'from' na dlaždicu 'to' (musia byť susedia).
    /// </summary>
    static IndicatrixAPI.DirectionMask GetDirection(Vector2Int from, Vector2Int to)
    {
        return DirFromStep(to - from);
    }

    /// <summary>
    /// Pre danú dlaždicu (x, z) a smer 'dir' vráti svetový bod uprostred danej hrany dlaždice.
    /// Hrana je medzi vnútrom dlaždice a susedom v smere 'dir'.
    ///   Right  → x = tile.x + 1.0, z = tile.z + 0.5
    ///   Left   → x = tile.x + 0.0, z = tile.z + 0.5
    ///   Top    → x = tile.x + 0.5, z = tile.z + 1.0
    ///   Bottom → x = tile.x + 0.5, z = tile.z + 0.0
    /// Y: biliniárna interpolácia výšky šikmej plochy dlaždice v polohe
    ///    stredu danej hrany. Pre rovinu vychádza priemer 4 rohov; pre svah
    ///    (LevelUp/LevelDown) vychádza presne stred danej hrany dlaždice,
    ///    takže vlak na svahu ide pod skutočným uhlom stúpania – nie po
    ///    schodoch s 90° lomami.
    /// Hladkosť na hrane medzi susednými dlaždicami je zaručená
    /// automaticky: A.right-edge a B.left-edge zdieľajú tie isté dva
    /// rohové vertexy → ich priemer je totožný.
    /// </summary>
    Vector3 EdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f, GetTileSurfaceY(tile, 1.0f, 0.5f) + TRACK_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f, GetTileSurfaceY(tile, 0.0f, 0.5f) + TRACK_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 1.0f) + TRACK_OFFSET_Y, fz + 1.0f);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 0.0f) + TRACK_OFFSET_Y, fz + 0.0f);
            default: return TileCenter(tile);
        }
    }

    /// <summary>
    /// MOSTY / TUNELY: bod na hrane (alebo v strede pri dir == None) dlaždice,
    /// ktorá patrí prechodu. X/Z je rovnaké ako pri EdgePoint/TileCenter,
    /// ale Y sa neberie z terénu, ale z výškového profilu prechodu
    /// (CrossingData.TrackY) – mostovka nad údolím, tunel pod kopcom.
    /// Na VONKAJŠEJ hrane hlavy sa profil výškou zhoduje s terénom, takže
    /// napojenie na susednú koľaj je plynulé (bez schodu).
    /// </summary>
    Vector3 CrossingTrackPoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir,
                               RailCrossingSystem.CrossingData crossing)
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
        return new Vector3(x, crossing.TrackY(axisCoord) + TRACK_OFFSET_Y, z);
    }

    /// <summary>
    /// Detekcia, či je daná dlaždica krivkou.
    /// Krivka = presne 2 nastavené smery, ktoré NIE SÚ navzájom opačné
    /// (t.j. nie Left+Right ani Top+Bottom).
    /// </summary>
    static bool IsCurveTile(IndicatrixAPI.DirectionMask conns)
    {
        // Počet bitov
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
    /// Detekcia, či je daná dlaždica výhybkou (rail switch / turnout).
    /// Výhybka má presne 3 nastavené smery:
    ///   • RailSwitchHorizontalBottom: Left + Right + Bottom
    ///   • RailSwitchHorizontalTop:    Left + Right + Top
    ///   • RailSwitchVerticalBottom:   Top + Bottom + Right
    ///   • RailSwitchVerticalTop:      Top + Bottom + Left
    ///
    /// Výhybka je plne obojsmerná – ktorékoľvek dva z troch smerov sa
    /// môžu navzájom prepojiť. Pohyb cez výhybku má dva režimy:
    ///   1. Priamy prechod (proti-smery, napr. Left↔Right) – správa sa ako
    ///      klasická priama koľaj: edge → center → edge.
    ///   2. Odbočka (kolmé smery, napr. Left↔Bottom) – generuje sa diagonálny
    ///      vnútorný layout (~45° turnout), nie ostrý 90° roh ako pri krivke.
    ///
    /// Všetky 4 križovatkové smery (crossroad) majú 4 bity, takže sú
    /// odlíšené: switch == 3 bity, crossroad == 4 bity.
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
    /// Detekcia, či je daná dlaždica plnou križovatkou (RailCrossroad).
    /// Križovatka má všetky 4 smery (Left + Right + Top + Bottom) –
    /// jediný typ s 4 bitmi v DirectionMask.
    ///
    /// Pohyb cez križovatku má dva režimy podľa entry/exit smerov:
    ///   1. Priamy prechod (proti-smery, 90°):
    ///         Top ↔ Bottom    (vertikálny prejazd)
    ///         Left ↔ Right    (horizontálny prejazd)
    ///      → edge → center → edge (klasická priama)
    ///   2. Rohový prechod (kolmé smery, 45°):
    ///         Top ↔ Right, Top ↔ Left
    ///         Bottom ↔ Right, Bottom ↔ Left
    ///      → edge → innerEntry → innerExit → edge (45° turnout layout,
    ///        identický s odbočkou výhybky)
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
    /// Vráti true, ak sú dva smery navzájom kolmé (jeden horizontálny + jeden
    /// vertikálny). Vracia false ak sú totožné, alebo proti-smery, alebo None.
    /// Používa sa na odlíšenie odbočky vs priameho prechodu cez výhybku.
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
    /// Vnútorný bod výhybky pre 45° turnout geometriu.
    ///
    /// Pre danú dlaždicu, smer (z perspektívy hrany dlaždice, t.j. v ktorej
    /// hrane sa nachádzame) a parameter t ∈ (0, 0.5) vráti bod ležiaci na osi
    /// danej hrany, ale posunutý dovnútra dlaždice o vzdialenosť t.
    ///
    /// Príklady (tile na (0,0), t = 0.25):
    ///   dir = Left   → edge je (0.0, y, 0.5), inner = (0.25, y, 0.5)
    ///   dir = Right  → edge je (1.0, y, 0.5), inner = (0.75, y, 0.5)
    ///   dir = Top    → edge je (0.5, y, 1.0), inner = (0.5,  y, 0.75)
    ///   dir = Bottom → edge je (0.5, y, 0.0), inner = (0.5,  y, 0.25)
    ///
    /// Použitie: pre odbočkový prechod cez výhybku skladáme cestu ako
    ///   entryEdge → innerEntry(t) → innerExit(t) → exitEdge
    /// Stredný segment (innerEntry → innerExit) je pri t = 0.25 a kolmých
    /// smeroch presne 45° diagonálny – vizuálne pripomína skutočný turnout
    /// koľajnice, bez ostrého 90° lomu cez stred dlaždice.
    /// </summary>
    Vector3 SwitchInnerEdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir, float t)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f - t,
                    GetTileSurfaceY(tile, 1.0f - t, 0.5f) + TRACK_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f + t,
                    GetTileSurfaceY(tile, 0.0f + t, 0.5f) + TRACK_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 1.0f - t) + TRACK_OFFSET_Y,
                    fz + 1.0f - t);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 0.0f + t) + TRACK_OFFSET_Y,
                    fz + 0.0f + t);
            default: return TileCenter(tile);
        }
    }

    // =====================================================================
    // CAN MOVE – validácia smeru pre A* (thread-safe – pracuje so snapshotom)
    // =====================================================================

    /// <summary>
    /// Validácia A* prechodu z 'fromTile' na 'toTile' v smere 'direction'.
    /// Pravidlá:
    ///   1. toTile musí byť priechodná (rail/station/depot)
    ///   2. fromTile musí mať connection v smere 'direction'
    ///   3. toTile musí mať connection v opačnom smere (Opposite(direction))
    ///   4. Výnimka pre štart: ak je fromTile depo (tileID == 3), neaplikujeme
    ///      pravidlo č. 2 striktne (depo má jeden výstup, ten musí ladiť so smerom);
    ///      táto výnimka NIE JE potrebná – depo má vlastný DirectionMask, takže
    ///      vlak môže opustiť depo iba povoleným smerom. Ak by toto bolo príliš
    ///      reštriktívne, dá sa relaxovať.
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

        // fromTile musí povoľovať odchod v smere direction
        if ((fromConn & direction) == 0) return false;

        // toTile musí povoľovať vstup z opačného smeru
        IndicatrixAPI.DirectionMask oppositeDir = IndicatrixAPI.Opposite(direction);
        if ((toConn & oppositeDir) == 0) return false;

        return true;
    }

    /// <summary>
    /// CanMove rozšírený o pravidlá MOSTOV a TUNELOV:
    ///   • z hlavy prechodu sa smerom DO prechodu nedá ísť bežným krokom
    ///     (tile hneď za hlavou môže byť iná koľaj POD mostom) – tam vedie
    ///     výhradne skoková hrana na partnerskú hlavu,
    ///   • do hlavy sa nedá vojsť bežným krokom zo strany prechodu.
    /// Bez prechodov v hre (jumpDir == null) je správanie totožné s CanMove.
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

    // =====================================================================
    // BUILD WAYPOINT PATH – konvertuje List<Vector2Int> na waypointy
    // =====================================================================

    /// <summary>
    /// Z tile-cesty vygeneruje waypoint cestu podľa pravidiel:
    ///
    /// Pre každú dlaždicu B medzi predchádzajúcou A a nasledujúcou C:
    ///   • Ak je B priama (rail/station/depot s opačnými smermi):
    ///       waypoint sekvencia: B.entryEdge, B.center, B.exitEdge
    ///   • Ak je B krivka (RailCurveX):
    ///       waypoint sekvencia: B.entryEdge, B.exitEdge
    ///       (center sa NEPOUŽÍVA, vnútorný roh sa NEPOUŽÍVA – PathData
    ///       ich spojí jedným lineárnym Lerp segmentom, ktorý prechádza
    ///       vnútrom dlaždice diagonálne pod ~45°.)
    ///   • Ak je B výhybka (RailSwitch*, 3 spojenia):
    ///       - PRIAMY prechod (proti-smery): B.entryEdge, B.center, B.exitEdge
    ///         (rovnako ako priama dlaždica)
    ///       - ODBOČKA (kolmé smery): B.entryEdge, B.innerEntry,
    ///         B.innerExit, B.exitEdge
    ///         (4 waypointy → 3 priame segmenty, stredný 45° diagonálny;
    ///          vizuálne 45° turnout, NIE ostrý 90° roh.)
    ///   • Ak je B križovatka (RailCrossroad, 4 spojenia):
    ///       - PRIAMY prechod (proti-smery, Top↔Bottom alebo Left↔Right):
    ///         B.entryEdge, B.center, B.exitEdge (90° prejazd cez stred)
    ///       - ROHOVÝ prechod (kolmé smery, napr. Top↔Right):
    ///         B.entryEdge, B.innerEntry, B.innerExit, B.exitEdge
    ///         (45° turnout layout, identický s odbočkou výhybky)
    ///
    /// Štart a koniec:
    ///   • Prvá dlaždica (štart): pridá sa A.center, potom A.exitEdge.
    ///   • Posledná dlaždica (cieľ): pridá sa B.entryEdge, potom B.center.
    ///
    /// Pre jednodlaždičovú cestu (count==1): vráti len [center].
    ///
    /// HRANIČNÉ WAYPOINTY (kritické):
    ///   Tile-prechod A→B vygeneruje DVA waypointy s identickou Vector3
    ///   pozíciou, ktoré sú VŽDY zachované ako dva samostatné body:
    ///     - A.exitEdge(dir)         ← patrí dlaždici A
    ///     - B.entryEdge(opposite)   ← patrí dlaždici B
    ///   Aj keď sú geometricky totožné, logicky reprezentujú prechod cez
    ///   hranu a poradie je dôležité: zachováva smerovosť a umožňuje
    ///   správne priradenie currentTile cez waypointToTileIdx. Krivka,
    ///   výhybka aj križovatka spotrebúvajú SVOJ vstupný edge waypoint pre
    ///   korektné odvodenie exit-smeru. Z toho dôvodu deduplikujeme len
    ///   waypointy z ROVNAKEJ dlaždice (pozri AddWaypoint).
    ///
    /// VÝSTUP:
    ///   waypoints – List<Vector3> waypointov v poradí pohybu
    ///   tileIdxPerWaypoint – pre každý waypoint index do tilePath, ku ktorému patrí
    ///                        (potrebné pre UpdateCurrentTile)
    /// </summary>
    void BuildWaypointPath(List<Vector2Int> tilePath, out List<Vector3> waypoints,
                           out List<int> tileIdxPerWaypoint)
    {
        // Pracujeme cez lokálne premenné, nie cez 'out' parametre. Lokálna
        // funkcia AddWaypoint nemôže zachytávať 'out'/'ref' parametre
        // (CS1628), preto vyplníme lokálne zoznamy a na konci ich priradíme
        // do 'out' parametrov.
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

        // Pomocný local fn na pridanie waypointu.
        //
        // PRAVIDLO (kritické pre tile-boundary semantiku):
        // Waypointy s rovnakou Vector3 pozíciou sa NESMÚ zlučovať,
        // ak patria DVOM RÔZNYM dlaždiciam. Konkrétne A.exitEdge(dir) a
        // B.entryEdge(opposite(dir)) sú v priestore identické, ale logicky
        // ide o dva po sebe idúce waypointy:
        //   - A.exitEdge patrí dlaždici A (tileIdx = A)
        //   - B.entryEdge patrí dlaždici B (tileIdx = B)
        // Toto poradie zachováva smerovosť cesty a umožňuje korektné
        // priradenie currentTile pri prekročení hranice (UpdateCurrentTile
        // používa waypointToTileIdx mapovanie). Bez toho sa hraničný
        // waypoint krivkovej dlaždice "stratí" a UpdateCurrentTile by
        // hlásil zlú dlaždicu pre frame priamo po prekročení hrany.
        //
        // Deduplikujeme IBA vtedy, keď sú obidva waypointy z TEJ ISTEJ
        // dlaždice a v priestore sa kryjú – vtedy ide o degenerovaný
        // intra-tile prípad bez dopadu na sémantiku.
        void AddWaypoint(Vector3 pt, int tileIdx)
        {
            if (wp.Count > 0)
            {
                Vector3 last = wp[wp.Count - 1];
                int lastTileIdx = tIdx[tIdx.Count - 1];
                bool sameTile = (lastTileIdx == tileIdx);
                bool samePos = (pt - last).sqrMagnitude < 1e-8f;
                if (sameTile && samePos) return; // intra-tile duplikát → preskoč
            }
            wp.Add(pt);
            tIdx.Add(tileIdx);
        }

        // MOSTY / TUNELY – ktoré tily trasy patria prechodu (hlavy + vnútro).
        // null = trasa žiadny prechod neobsahuje (bežná cesta, bez zmeny).
        RailCrossingSystem.CrossingData[] tileCrossing =
            RailCrossingSystem.instance != null
                ? RailCrossingSystem.instance.ResolvePathCrossings(tilePath, out _)
                : null;

        for (int i = 0; i < tilePath.Count; i++)
        {
            Vector2Int tile = tilePath[i];
            var conns = IndicatrixAPI.instance.GetTileByIndex(tile.x, tile.y).connections;

            bool isFirst = (i == 0);
            bool isLast = (i == tilePath.Count - 1);

            // Smer odchodu z aktuálnej dlaždice (do nasledujúcej)
            IndicatrixAPI.DirectionMask outDir = IndicatrixAPI.DirectionMask.None;
            if (!isLast) outDir = GetDirection(tile, tilePath[i + 1]);

            // Smer príchodu do aktuálnej dlaždice (z predchádzajúcej)
            IndicatrixAPI.DirectionMask inDir = IndicatrixAPI.DirectionMask.None;
            if (!isFirst) inDir = IndicatrixAPI.Opposite(GetDirection(tilePath[i - 1], tile));

            // ────────────────────────────────────────────────────────────────
            // MOST / TUNEL: hlava aj vnútro prechodu sú vždy PRIAME dlaždice
            // (edge → center → edge), Y podľa profilu prechodu. Musí to byť
            // PRED detekciou kriviek/výhybiek – tile pod mostom môže niesť
            // úplne inú koľaj, ktorej connections tu nesmú zavážiť.
            // ────────────────────────────────────────────────────────────────
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

            // ────────────────────────────────────────────────────────────────
            // KRIVKA: entryEdge → exitEdge  (BEZ centra, BEZ rohového bodu)
            //
            // PathData spojí dva po sebe idúce waypointy lineárnym Lerp-om.
            // Keďže entryEdge je stred jednej hrany dlaždice a exitEdge je
            // stred susediacej (kolmnej) hrany, priamka medzi nimi prechádza
            // vnútrom dlaždice diagonálne — vizuálne pod ~45°. To je presne
            // požadovaný "ostrý" angular pohyb cez krivku.
            //
            // Predtým sme sem vkladali aj rohový bod (vrchol bunky), čo
            // produkovalo dva segmenty s ostrým 90° lomom v rohu — pohyb
            // skákal cez vrchol dlaždice von z dráhy. Rohový bod sa preto
            // už NEpridáva.
            //
            // Funkcia CurveCornerPoint zostáva v zdrojáku pre prípadné
            // budúce vizuálne pomocníky, ale BuildWaypointPath ju NEvolá.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsCurveTile(conns))
            {
                // entryEdge je geometricky totožný bod ako A.exitEdge predchádzajúcej
                // dlaždice, ale logicky patrí TEJTO krivkovej dlaždici –
                // AddWaypoint ho zachová ako samostatný waypoint, pretože tileIdx
                // je iný od predchádzajúceho waypointu.
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // VÝHYBKA (RAIL SWITCH) – 3 spojenia, plne obojsmerná.
            //
            // Vnútorná logika výhybky závisí od toho, či ide o priamy prechod
            // alebo o odbočku:
            //
            //   1. PRIAMY PRECHOD (proti-smery, napr. Left↔Right na
            //      RailSwitchHorizontalBottom alebo Top↔Bottom na
            //      RailSwitchVerticalBottom):
            //         entryEdge → center → exitEdge
            //      Identické správanie ako klasická priama koľaj. Stred
            //      dlaždice je súčasťou cesty.
            //
            //   2. ODBOČKA (kolmé smery, napr. Left↔Bottom alebo Right↔Top):
            //         entryEdge → innerEntry(t) → innerExit(t) → exitEdge
            //      kde t = SWITCH_TURNOUT_T (= 0.25). Vizuálne to vytvorí
            //      pohyb, ktorý:
            //         • vstúpi do dlaždice po hrane vstupu,
            //         • prejde krátky úsek pozdĺž osi vstupnej hrany,
            //         • diagonálne (45°) sa stočí dovnútra,
            //         • prejde krátky úsek pozdĺž osi výstupnej hrany,
            //         • opustí dlaždicu cez výstupnú hranu.
            //      Pohyb používa IBA priame čiarové segmenty (žiadne
            //      bezier krivky, žiadne splajny, žiadna interpolácia okrem
            //      lineárneho Lerp medzi dvoma susednými waypointmi).
            //      Vizuálne to pripomína skutočný železničný výhybkový
            //      úsek (turnout) namiesto ostrého 90° rohu cez stred.
            //
            // Tile-prechodové pravidlá zostávajú nezmenené:
            //   A.center → A.edge → B.edge   (medzi-tile)
            // entryEdge a exitEdge sú ŠTANDARDNÉ hranové waypointy zhodné
            // s ostatnými tile-typmi → tile-boundary semantika je zachovaná.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsSwitchTile(conns))
            {
                // ── ODBOČKA: entry a exit sú kolmé smery ─────────────────
                if (IsPerpendicular(inDir, outDir))
                {
                    const float SWITCH_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                // ── PRIAMY PRECHOD: entry a exit sú proti-smery ─────────
                // (napr. Left ↔ Right na horizontálnej výhybke)
                // → správa sa ako klasická priama dlaždica: edge → center → edge
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // KRIŽOVATKA (RAIL CROSSROAD) – 4 spojenia (všetky 4 smery).
            //
            // Plná 4-cestná križovatka. Pohyb cez ňu závisí od entry/exit
            // smerov rovnako ako pri výhybke, len s jedným spojením naviac:
            //
            //   1. PRIAMY PRECHOD – 90° osový prejazd (proti-smery):
            //         • Top    ↔ Bottom   (vertikálny prejazd)
            //         • Left   ↔ Right    (horizontálny prejazd)
            //      → entryEdge → center → exitEdge
            //         (rovnako ako klasická priama dlaždica – cez stred)
            //
            //   2. ROHOVÝ PRECHOD – 45° turnout (kolmé smery):
            //         • Top    ↔ Right     • Top    ↔ Left
            //         • Bottom ↔ Right     • Bottom ↔ Left
            //      → entryEdge → innerEntry(t) → innerExit(t) → exitEdge
            //         (presne ten istý layout ako odbočka výhybky –
            //          stredný segment je 45° diagonálny, vstupný a výstupný
            //          segment idú pozdĺž osí príslušných hrán)
            //
            // Princíp je identický s odbočkou výhybky:
            //   • IBA priame čiarové segmenty (žiadne bezier krivky,
            //     žiadne splajny, žiadny smoothing).
            //   • Stred dlaždice sa NEPOUŽÍVA pre rohové prechody –
            //     pohyb sa stočí pred dosiahnutím stredu.
            //   • Tile-prechodové pravidlá zostávajú nezmenené:
            //     entryEdge a exitEdge sú štandardné hranové waypointy
            //     zhodné s ostatnými typmi.
            //
            // POZN: Crossroad detekcia musí prísť AŽ ZA switch detekciou,
            // pretože ako oddelenie používame počet bitov:
            //   curve = 2, switch = 3, crossroad = 4.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsCrossroadTile(conns))
            {
                // ── ROHOVÝ PRECHOD: entry a exit sú kolmé smery ──────────
                if (IsPerpendicular(inDir, outDir))
                {
                    const float CROSSROAD_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                // ── PRIAMY PRECHOD: entry a exit sú proti-smery ─────────
                // Top↔Bottom alebo Left↔Right → klasický prejazd cez stred
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // PRIAMA / KONCOVÁ DLAŽDICA: edge → center → edge
            // ────────────────────────────────────────────────────────────────

            if (isFirst)
            {
                // Štart – začneme v centre
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
            else if (isLast)
            {
                // Cieľ – vstúpime cez hranu a skončíme v centre
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
            }
            else
            {
                // Stredná priama dlaždica: entryEdge → center → exitEdge
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
        }

        // Priradíme lokálne zoznamy do 'out' parametrov.
        waypoints = wp;
        tileIdxPerWaypoint = tIdx;
    }

    /// <summary>
    /// Vráti vrchol bunky pre krivku (vrchol, kde by sa dve hrany krivky
    /// stretli pri 90° lome). NEPOUŽÍVA sa v BuildWaypointPath – pohyb cez
    /// krivku je priamy Lerp medzi entryEdge a exitEdge (45° diagonála).
    /// Funkcia ostáva pre prípadné budúce vizuálne pomôcky / debug overlay.
    ///
    /// Pre RailCurveRightBottom (Right + Bottom):
    ///   Right hrana je x = tile.x+1, Bottom hrana je z = tile.z
    ///   → vrchol bunky = (tile.x+1, y, tile.z)
    /// </summary>
    Vector3 CurveCornerPoint(Vector2Int tile, IndicatrixAPI.DirectionMask conns)
    {
        float fx = tile.x, fz = tile.y;

        bool right = (conns & IndicatrixAPI.DirectionMask.Right) != 0;
        bool left = (conns & IndicatrixAPI.DirectionMask.Left) != 0;
        bool top = (conns & IndicatrixAPI.DirectionMask.Top) != 0;
        bool bottom = (conns & IndicatrixAPI.DirectionMask.Bottom) != 0;

        // X súradnica rohu: ak Right → fx+1, ak Left → fx
        float cx = right ? fx + 1.0f : fx + 0.0f;
        // Z súradnica rohu: ak Top → fz+1, ak Bottom → fz
        float cz = top ? fz + 1.0f : fz + 0.0f;

        // Y v rohu = priamo výška vertexu terénu v tom rohu (biliniárna
        // interpolácia v rohu sa zredukuje na hodnotu samotného rohového
        // vertexu). Korektné aj na svahu.
        float u = right ? 1f : 0f;
        float v = top ? 1f : 0f;
        float y = GetTileSurfaceY(tile, u, v) + TRACK_OFFSET_Y;

        return new Vector3(cx, y, cz);
    }

    // =====================================================================
    // (Pôvodný BuildPathData zachovaný pre spätnú kompatibilitu, ale už
    //  nepoužívaný v hlavnej ceste – ApplyNewPath používa BuildWaypointPath.)
    // =====================================================================

    PathData BuildPathData(List<Vector2Int> tilePath)
    {
        if (tilePath == null || tilePath.Count == 0) return null;
        BuildWaypointPath(tilePath, out var pts, out _);
        return pts.Count > 0 ? new PathData(pts) : null;
    }

    // =====================================================================
    // VIZUÁLNA SÚPRAVA – pomocná metóda vytvorenia
    // =====================================================================

    /// <summary>
    /// Dohľadá komponent <see cref="TrainModelLibrary"/> s prefab-mi 3D modelov.
    /// Priorita: priradená Inspector referencia → statická inštancia → scéna.
    /// Môže vrátiť null – vtedy sa použijú výhradne pôvodné kvádre.
    /// </summary>
    TrainModelLibrary ResolveModelLibrary()
    {
        if (modelLibrary != null) return modelLibrary;
        if (TrainModelLibrary.instance != null)
        {
            modelLibrary = TrainModelLibrary.instance;
            return modelLibrary;
        }
        // Posledný pokus – nájsť komponent kdekoľvek v scéne (raz, výsledok sa
        // cache-ne do modelLibrary, aby sa FindObjectOfType nevolal opakovane).
        //modelLibrary = FindObjectOfType<TrainModelLibrary>();
        modelLibrary = UnityEngine.Object.FindAnyObjectByType<TrainModelLibrary>();

        return modelLibrary;
    }

    /// <summary>
    /// Spätne kompatibilný podpis (bez prefabu) – vždy vytvorí pôvodný kváder.
    /// </summary>
    GameObject CreateConsistPart(string name, Color color, Vector3 position)
        => CreateConsistPart(name, color, position, null);

    /// <summary>
    /// Vytvorí jeden vizuálny člen súpravy (lokomotívu alebo vagón).
    ///
    /// VOLITEĽNÝ 3D MODEL:
    ///   • modelPrefab != null → vytvorí sa inštancia tohto prefabu (skutočný
    ///     3D model). Zachová sa scale aj materiály z prefabu (model si nesie
    ///     vlastný vzhľad), kváder ani CONSIST_SCALE sa NEAPLIKUJÚ.
    ///   • modelPrefab == null → vytvorí sa pôvodný kváder s farbou `color`
    ///     a rozmermi CONSIST_SCALE – identické správanie ako pôvodný kód.
    ///
    /// V oboch prípadoch je výsledný objekt zhodne nakonfigurovaný pre
    /// pohybovú logiku: bez kolíznych komponentov (aby neblokoval klikanie
    /// po mape), s vypnutým aktívnym stavom (zobrazí sa až na trase).
    ///
    /// POZN. K ORIENTÁCII MODELU: pohybová logika (UpdateConsistVisuals)
    /// natáča člena cez Quaternion.LookRotation(dir, up), t.j. lokálna os +Z
    /// modelu smeruje v smere jazdy (rovnako ako dlhšia os kvádra 0.7 v Z).
    /// Prefab by mal byť "tvárou" otočený na +Z. Ak model mieri inou osou,
    /// stačí ho vnoriť pod prázdny rodičovský objekt natočený tak, aby +Z
    /// rodiča zodpovedalo prednej časti modelu, a ako prefab priradiť rodiča.
    /// </summary>
    GameObject CreateConsistPart(string name, Color color, Vector3 position, GameObject modelPrefab)
    {
        GameObject go;

        if (modelPrefab != null)
        {
            // ── SKUTOČNÝ 3D MODEL ─────────────────────────────────────────
            // Model NEVKLADÁME priamo ako pohybovaný objekt, ale pod prázdny
            // KOREŇ (pivot). Dôvod: UpdateConsistVisuals prepisuje position aj
            // rotation každý snímok, takže prípadný posun pivotu prefabu (model
            // vymodelovaný mimo počiatku) by sa nedal kompenzovať na tom istom
            // transforme – vlak by išiel vedľa koľaje. Koreň je ten, s ktorým
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
            // klikaní na mapu/koľaje. Ak by si chcel vlaky klikateľné, tento
            // blok stačí vynechať.
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++)
                if (colliders[c] != null) Destroy(colliders[c]);

            // Proporčné prispôsobenie na veľkosť 1 dlaždice (viď FitConsistModel).
            if (fitTrainModelsToTile)
                FitConsistModel(go, model);

            // Diagnostický referenčný kváder (viď debugShowReferenceCube).
            if (debugShowReferenceCube)
                AttachReferenceCube(go);
        }
        else
        {
            // ── PÔVODNÝ KVÁDER (fallback) ─────────────────────────────────
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = CONSIST_SCALE;
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
    /// Proporčne (uniformne) preškáluje 3D model člena súpravy tak, aby jeho
    /// DĹŽKA pozdĺž osi +Z zodpovedala <see cref="consistMemberLength"/>
    /// dlaždice, a vycentruje ho na pivot koreňa (os trate).
    ///
    /// Je to priama analógia k IndicatrixAPI.FitTileModel (ktorý dlaždicové
    /// prefab-y škáluje na 1 dlaždicu). Rozdiel: pri súprave je referenciou
    /// DĹŽKA (os Z, smer jazdy), nie väčší z rozmerov pôdorysu – inak by sa
    /// dlhý vozeň skrátil na dlaždicu a bol by neúmerne široký.
    ///
    /// MERANIE V NEUTRÁLNEJ ROTÁCII:
    ///   Renderer.bounds je world-space AABB. Keby sme merali už natočený
    ///   koreň, dĺžka by vyšla nadhodnotená a navyše rôzna podľa smeru jazdy.
    ///   Preto koreň dočasne otočíme na identitu, odmeriame, a rotáciu vrátime.
    ///   Lokálna rotácia dieťaťa (autorské natočenie prefabu) sa NEMENÍ – tá
    ///   sa má do merania premietnuť, lebo definuje, ktorá os je "dopredu".
    ///
    /// KOREKCIA PIVOTU:
    ///   Po preškálovaní posunieme dieťa tak, aby stred jeho X/Z pôdorysu
    ///   ležal na pivote koreňa a spodok (min Y) sedel na úrovni koľaje.
    /// </summary>
    void FitConsistModel(GameObject root, GameObject model)
    {
        if (root == null || model == null) return;

        // Meriame v LOKÁLNOM priestore koreňa – nezávisle od jeho svetovej
        // rotácie aj pozície. Netreba teda nič dočasne otáčať ani vracať späť.
        if (!TryGetLocalBounds(root.transform, model, out Bounds b))
        {
            Debug.LogWarning($"[TrainSystem] '{root.name}': prefab nemá žiadny " +
                             "zapnutý MeshRenderer/SkinnedMeshRenderer – " +
                             "veľkosť ani centrovanie sa nedajú určiť.");
            return;
        }
        if (b.size.z <= 1e-5f || b.size.x <= 1e-5f) return;

        // 1) Uniformná mierka podľa dĺžky (os Z = smer jazdy).
        float s = consistMemberLength / b.size.z;

        // 2) Poistka na šírku – ak by bol model po škálovaní na dĺžku širší
        //    než povolené, doškálujeme nadol (stále uniformne).
        if (b.size.x * s > consistMemberMaxWidth)
            s = consistMemberMaxWidth / b.size.x;

        model.transform.localScale *= s;

        // 3) Re-centrovanie. Cieľ v lokálnych súradniciach koreňa:
        //       X = 0  (na os trate)
        //       Z = 0  (stred dĺžky presne na pivote → yaw ho nevychýli)
        //       min Y = 0 (spodok sadne na koľaj)
        //    Koreň má mierku 1, takže lokálne jednotky = svetové jednotky.
        if (!TryGetLocalBounds(root.transform, model, out Bounds b2)) return;

        model.transform.localPosition += new Vector3(
            -b2.center.x,
            -b2.min.y + consistModelYOffset,
            -b2.center.z);

        if (logConsistFit)
        {
            TryGetLocalBounds(root.transform, model, out Bounds bf);
            Debug.Log($"[TrainSystem] FIT '{root.name}': pôvodne {b.size} @ {b.center} " +
                      $"→ mierka ×{s:F4} → výsledok {bf.size} @ {bf.center} " +
                      $"(očakávané center.x≈0, center.z≈0; min.y={bf.min.y:F4}) | " +
                      $"pivot vo svete = {root.transform.position}");
        }
    }

    /// <summary>
    /// Pripne ku koreňu člena súpravy MAGENTOVÝ referenčný kváder s rozmermi
    /// <see cref="CONSIST_SCALE"/>, vycentrovaný presne na pivot koreňa – teda
    /// v tej istej polohe, akú mal pôvodný kváder pred zavedením 3D modelov.
    ///
    /// DIAGNOSTICKÝ ZMYSEL:
    ///   Kváder ukazuje, KDE pohybová logika daný člen súpravy skutočne drží.
    ///   Porovnaním s modelom sa dá jednoznačne rozhodnúť medzi dvoma úplne
    ///   odlišnými príčinami vychýlenia:
    ///
    ///     • Kváder sedí na koľajniciach, model je vedľa neho
    ///         → chyba je v centrovaní modelu (FitConsistModel).
    ///
    ///     • Kváder aj model sú vychýlené rovnako
    ///         → centrovanie modelu je v poriadku a vychýlená je samotná
    ///           trajektória alebo poloha koľajnicových dlaždíc. Vtedy sa
    ///           chyba nehľadá v TrainSystem, ale v umiestnení dlaždíc
    ///           (IndicatrixAPI.InstantiateTileModel / FitTileModel).
    ///
    /// Kváder je dieťa koreňa, takže sa hýbe aj otáča spolu s modelom a nijako
    /// nezasahuje do hernej logiky. Po diagnostike prepínač jednoducho vypni.
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
        refCube.transform.localScale = CONSIST_SCALE;

        var rend = refCube.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = new Color(1f, 0f, 1f);
    }

    /// <summary>
    /// Spočíta AABB celého modelu v LOKÁLNOM priestore zadaného transformu.
    ///
    /// Prečo nie <c>Renderer.bounds</c> (ako v IndicatrixAPI):
    ///   • Renderer.bounds je world-space AABB. Pri natočenom modeli je väčší
    ///     než skutočné teleso a jeho stred sa posúva – z toho vzniká vychýlenie.
    ///   • Renderer.bounds NIE JE spoľahlivý pre neaktívne / ešte nevykreslené
    ///     objekty; vtedy môže vrátiť neaktuálnu alebo nulovú hodnotu.
    ///
    /// Namiesto toho berieme <c>sharedMesh.bounds</c> (čistá geometria) a jeho
    /// 8 rohov transformujeme do priestoru koreňa. Výsledok je deterministický.
    ///
    /// Zahrnú sa LEN zapnuté MeshRenderer / SkinnedMeshRenderer. Vypnuté
    /// renderery (kolízne proxy, shadow-only pomocníky, LOD varianty, ktoré
    /// autor modelu necháva v prefabe) sa ignorujú – práve tie inak ťahajú
    /// stred bounding boxu mimo skutočného tela modelu.
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
    /// LEGACY – už sa nepoužíva (nahradené TryGetLocalBounds). Ponechané len
    /// pre referenciu: takto to robí IndicatrixAPI.FitTileModel pre dlaždice,
    /// ktoré sa nehýbu ani neotáčajú, takže im world-space AABB stačí.
    ///
    /// Spočíta spoločný (world-space) Bounds všetkých Rendererov v hierarchii.
    /// Vráti false, ak objekt nemá žiadny Renderer.
    /// (Zhodné s IndicatrixAPI.TryGetCombinedBounds – tam je private.)
    /// </summary>
    static bool TryGetCombinedBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // =====================================================================
    // SPRÁVA VLAKOV
    // =====================================================================

    /// <summary>
    /// Vytvorí vlak v depe [dx,dz].
    ///
    /// trainTypeIndex / wagonTypeIndex sú indexy zvolené v TrainTypeDropdown /
    /// WagonTypeDropdown (viď DepotRailConstructionMenuUI). Podľa nich sa
    /// z katalógov (TrainCatalog / WagonCatalog v TrainStock.cs) zostaví
    /// dátová štruktúra súpravy (TrainInstance) a uloží do td.consist.
    ///
    /// Všetky vagóny v súprave sú zatiaľ rovnakého zvoleného typu; ich počet
    /// je daný wagonCount. UI sa nemení – mapovanie atribútov na konkrétne UI
    /// prvky nie je potrebné, detailné okno si ich prečíta z td.consist neskôr.
    /// </summary>
    public bool CreateTrain(int dx, int dz, int trainTypeIndex, int wagonTypeIndex)
    {
        int key = DepotKey(dx, dz);
        if (trains.ContainsKey(key)) return false;
        if (GetTileID(dx, dz) != 3) return false;

        int count = Mathf.Clamp(wagonCount, 1, 10);
        TrainData td = new TrainData(dx, dz);
        Vector3 depotPos = TileCenter(new Vector2Int(dx, dz));

        // -----------------------------------------------------------------
        // DÁTOVÁ ŠTRUKTÚRA SÚPRAVY (TrainStock.cs)
        // -----------------------------------------------------------------
        TrainSpec trainSpec = TrainCatalog.ByIndex(trainTypeIndex);
        WagonSpec wagonSpec = WagonCatalog.ByIndex(wagonTypeIndex);

        if (trainSpec == null)
        {
            Debug.LogWarning($"[TrainSystem] Neznámy index typu vlaku ({trainTypeIndex}) – použijem prvý z katalógu.");
            trainTypeIndex = 0;
            trainSpec = TrainCatalog.ByIndex(0);
        }
        if (wagonSpec == null)
        {
            Debug.LogWarning($"[TrainSystem] Neznámy index typu vagónu ({wagonTypeIndex}) – použijem prvý z katalógu.");
            wagonTypeIndex = 0;
            wagonSpec = WagonCatalog.ByIndex(0);
        }

        td.consist = new TrainInstance(trainSpec);
        for (int i = 0; i < count; i++)
            td.consist.Wagons.Add(new WagonInstance(wagonSpec));

        // Rýchlosť/zrýchlenie/spomalenie súpravy – z parametrov lokomotívy
        // a vagónov (viď ComputeMotionParams). Zloženie súpravy je odteraz
        // fixné, preto stačí prepočítať raz, nie pri každej zmene trasy.
        ComputeMotionParams(td);

        // -----------------------------------------------------------------
        // VOLITEĽNÉ 3D MODELY (TrainModelLibrary)
        // Podľa zvoleného typu vlaku/vagónu zistíme prefab. Ak je null,
        // CreateConsistPart vytvorí pôvodný kváder; ak nie je, vytvorí model.
        // -----------------------------------------------------------------
        TrainModelLibrary lib = ResolveModelLibrary();
        GameObject locoPrefab = (lib != null) ? lib.GetLocomotivePrefab(trainTypeIndex) : null;
        GameObject wagonPrefab = (lib != null) ? lib.GetWagonPrefab(wagonTypeIndex) : null;

        // Skrytá kocka – pohybová logika
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = CONSIST_SCALE;
        cube.transform.position = depotPos;
        cube.name = $"TrainHead_{dx}_{dz}";
        Destroy(cube.GetComponent<BoxCollider>());
        cube.GetComponent<Renderer>().enabled = false;
        td.trainObject = cube;

        // Lokomotíva – CYAN kváder, alebo (ak je definovaný) 3D model
        td.locomotive = CreateConsistPart($"Loco_{dx}_{dz}", new Color(0f, 1f, 1f), depotPos, locoPrefab);

        // Vagóny – GREY kváder, alebo (ak je definovaný) 3D model
        for (int i = 0; i < count; i++)
        {
            var wagon = CreateConsistPart($"Wagon_{dx}_{dz}_{i}",
                new Color(0.502f, 0.502f, 0.502f), depotPos, wagonPrefab);
            td.wagons.Add(wagon);
        }

        trains[key] = td;
        Debug.Log($"[TrainSystem] Vlak '{td.consist.Name}' vytvorený v depe [{dx},{dz}] – "
                + $"1 lokomotíva + {count}× '{wagonSpec.Name}' (spacing={wagonSpacing}).");
        return true;
    }

    /// <summary>
    /// Spätne kompatibilný preťažený podpis – vytvorí vlak s prvým typom
    /// vlaku aj vagónu z katalógu (index 0). Volá sa z miest, kde voľba
    /// typu nie je k dispozícii (napr. klávesová skratka Q bez UI kontextu).
    /// </summary>
    public bool CreateTrain(int dx, int dz)
    {
        return CreateTrain(dx, dz, 0, 0);
    }

    /// <summary>
    /// KLIK NA VLAK: zistí, či lúč (z kamery cez kurzor myši) zasiahol
    /// viditeľnú lokomotívu alebo ktorýkoľvek vagón niektorého vlaku.
    ///
    /// Platí pre IDÚCI aj ZASTAVENÝ vlak (Stop, čakanie na stanici,
    /// dokončená trasa) – rozhoduje len to, či je súprava viditeľná na mape.
    /// Vlak odstavený v depe (isAtDepot) sa preskakuje.
    ///
    /// Členovia súpravy nemajú kolízne komponenty (zámerne – neblokujú
    /// klikanie po mape), preto sa test robí cez Renderer.bounds.
    /// Metóda NEMENÍ žiadny stav – iba číta.
    /// </summary>
    /// <param name="ray">Lúč z kamery (cam.ScreenPointToRay).</param>
    /// <param name="depot">Súradnice depa zasiahnutého vlaku.</param>
    /// <param name="distance">Vzdialenosť zásahu po lúči (najbližší vlak).</param>
    public bool TryPickTrain(Ray ray, out Vector2Int depot, out float distance)
    {
        depot = default;
        distance = float.MaxValue;
        bool found = false;

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td == null || td.isAtDepot) continue;

            int memberCount = 1 + (td.wagons != null ? td.wagons.Count : 0);
            for (int m = 0; m < memberCount; m++)
            {
                GameObject member = (m == 0) ? td.locomotive : td.wagons[m - 1];
                if (member == null || !member.activeInHierarchy) continue;

                if (RayHitsMember(ray, member, out float d) && d < distance)
                {
                    distance = d;
                    depot = new Vector2Int(td.depotX, td.depotZ);
                    found = true;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Pomocná metóda pre TryPickTrain: lúč vs. spojené bounds všetkých
    /// zapnutých Rendererov člena súpravy (model aj fallback kváder).
    /// </summary>
    static bool RayHitsMember(Ray ray, GameObject member, out float dist)
    {
        dist = 0f;
        var renderers = member.GetComponentsInChildren<Renderer>(false);
        bool has = false;
        Bounds b = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!has) return false;

        // Mierne zväčšenie – menšie modely sa dajú pohodlnejšie trafiť.
        b.Expand(0.05f);
        return b.IntersectRay(ray, out dist);
    }

    public TrainData GetTrain(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return trains.TryGetValue(key, out var td) ? td : null;
    }

    /// <summary>
    /// READ-ONLY dotaz: nachádza sa PRÁVE TERAZ na dlaždici [tx,tz] niektorá
    /// vlaková súprava (lokomotíva alebo ktorýkoľvek jej vagón)?
    ///
    /// Slúži výhradne ako ochrana pred demoláciou koľaje/stanice/výhybky pod
    /// idúcim vlakom (GameManager → RailConstructionMode.Demolish). Metóda
    /// NEMENÍ žiadny stav – iba číta.
    ///
    /// AKO SA URČUJE OBSADENIE:
    ///   • Vlak odstavený v depe (isAtDepot) je skrytý → dlaždice neblokuje
    ///     (depo samotné chráni samostatná kontrola v GameManager).
    ///   • (A) Skontrolujú sa SKUTOČNÉ pozície viditeľných členov súpravy
    ///     (lokomotíva + vagóny). Toto platí rovnako pre IDÚCI aj pre
    ///     ZASTAVENÝ vlak (Stop tlačidlo, čakanie na stanici, dokončená
    ///     trasa) – objekty stoja tam, kde ich hráč vidí.
    ///   • (B) Porovná sa currentTile (dlaždica, na ktorej vlak logicky stojí).
    ///   • (C) Ak vlak má activePath, tá sa navyše vzorkuje od chvosta súpravy
    ///     (locomotiveDistance − wagons.Count * wagonSpacing) po jej čelo, takže
    ///     sa podchytí CELÝ súvislý úsek trate pod súpravou.
    ///     BODY_MARGIN pokrýva presah modelov pred/za pivotom.
    /// </summary>
    public bool IsTileOccupiedByTrain(int tx, int tz)
    {
        const float SAMPLE_STEP = 0.25f;   // krok vzorkovania pozdĺž trasy
        const float BODY_MARGIN = 0.30f;   // presah modelu pred/za pivotom

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td == null) continue;

            // Súprava odstavená (skrytá) v depe trať neblokuje.
            if (td.isAtDepot) continue;

            // (A) SKUTOČNÉ POZÍCIE ČLENOV SÚPRAVY – funguje rovnako pre idúci
            //     aj pre ZASTAVENÝ vlak (Stop, čakanie na stanici, dokončená
            //     trasa), pretože objekty ostávajú stáť tam, kde ich hráč vidí.
            //     Skrytí členovia (SetActive(false)) sa preskakujú.
            int memberCount = 1 + (td.wagons != null ? td.wagons.Count : 0);
            for (int m = 0; m < memberCount; m++)
            {
                GameObject member = (m == 0) ? td.locomotive : td.wagons[m - 1];
                if (member == null || !member.activeSelf) continue;

                Vector3 center = member.transform.position;
                Vector3 fwd = member.transform.forward;

                // Pivot + presah tela pred a za pivotom (model môže
                // presahovať cez hranicu dlaždice).
                for (int s = -1; s <= 1; s++)
                {
                    Vector3 mp = center + fwd * (s * BODY_MARGIN);
                    if (Mathf.FloorToInt(mp.x) == tx && Mathf.FloorToInt(mp.z) == tz)
                        return true;
                }
            }

            // (B) Dlaždica, na ktorej vlak logicky stojí.
            if (td.currentTile.x == tx && td.currentTile.y == tz) return true;

            // (C) Súvislé pokrytie trate pod súpravou (len ak existuje trasa).
            if (td.activePath == null) continue;

            float loco = Mathf.Clamp(td.locomotiveDistance, 0f, td.activePath.totalLength);
            float consistLength = (td.wagons != null ? td.wagons.Count : 0) * wagonSpacing;
            float head = loco + BODY_MARGIN;
            float tail = Mathf.Max(0f, loco - consistLength - BODY_MARGIN);

            int steps = Mathf.Max(1, Mathf.CeilToInt((head - tail) / SAMPLE_STEP));
            for (int i = 0; i <= steps; i++)
            {
                float d = tail + (head - tail) * i / steps;

                // GetPositionAtDistance si vzdialenosť sám upne na [0, totalLength].
                Vector3 p = td.activePath.GetPositionAtDistance(d);
                if (Mathf.FloorToInt(p.x) == tx && Mathf.FloorToInt(p.z) == tz)
                    return true;
            }

            if (td.currentTile.x == tx && td.currentTile.y == tz) return true;
        }

        return false;
    }

    /// <summary>
    /// Vráti súradnice [depotX, depotZ] VŠETKÝCH aktuálne existujúcich
    /// vlakových dep. Keďže platí "1 train depo = 1 train", počet prvkov
    /// zoznamu = počet vlakov v hre.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI). Vnútorný slovník
    /// `trains` ostáva privátny – navonok dávame len read-only kópiu
    /// kľúčových údajov, takže volajúci nemôže poškodiť interný stav.
    /// </summary>
    public List<Vector2Int> GetAllDepotCoords()
    {
        var result = new List<Vector2Int>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            result.Add(new Vector2Int(td.depotX, td.depotZ));
        }
        return result;
    }

    /// <summary>
    /// Celkový počet vlakových dep (= počet vlakov). Pohodlný getter, aby
    /// UI nemuselo kvôli počtu vytvárať celý zoznam cez GetAllDepotCoords().
    /// </summary>
    public int GetDepotCount()
    {
        return trains.Count;
    }

    /// <summary>
    /// Vráti dátové štruktúry (TrainInstance) VŠETKÝCH aktuálne existujúcich
    /// vlakov. Keďže platí "1 train depo = 1 train", počet prvkov zoznamu =
    /// počet vlakov v hre.
    ///
    /// Slúži pre informačné UI (StatusTrainsMenuUI), ktoré z každej
    /// TrainInstance prečíta názov vlaku (Name), počet vagónov
    /// (Wagons.Count) a typ suroviny (Wagons[0].Type.Name).
    ///
    /// Vnútorný slovník `trains` ostáva privátny – navonok dávame len
    /// read-only kópiu zoznamu referencií na TrainInstance. Volajúci tak
    /// nemôže pridať/odobrať vlak (zmeniť `trains`), čítať atribúty
    /// jednotlivých vlakov ale môže. Analógia k GetAllDepotCoords().
    /// </summary>
    public List<TrainInstance> GetAllTrainConsists()
    {
        var result = new List<TrainInstance>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td != null && td.consist != null)
                result.Add(td.consist);
        }
        return result;
    }

    /// <summary>
    /// EKONOMIKA (read-only snapshot): pre KAŽDÝ existujúci vlak spáruje jeho
    /// DEPO súradnice [depotX, depotZ] (= kľúč pre <see cref="ReturnToDepot"/>)
    /// s jeho dátovou inštanciou <see cref="TrainInstance"/> (OperatingCosts,
    /// ServiceLife, ServicingInterval). Interný slovník `trains` ostáva privátny.
    ///
    /// Slúži pre EconomySystem: mesačné prevádzkové náklady + automatické
    /// poslanie vlaku do depa po uplynutí servisného intervalu alebo životnosti
    /// (EconomySystem zavolá <see cref="ReturnToDepot"/> s vrátenými súradnicami).
    /// </summary>
    public List<ActiveTrainInfo> GetActiveTrainInfos()
    {
        var result = new List<ActiveTrainInfo>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td != null && td.consist != null)
                result.Add(new ActiveTrainInfo(td.depotX, td.depotZ, td.consist));
        }
        return result;
    }

    /// <summary>Read-only dvojica [depo súradnice + dátová inštancia vlaku] pre EconomySystem.</summary>
    public readonly struct ActiveTrainInfo
    {
        public readonly int DepotX;
        public readonly int DepotZ;
        public readonly TrainInstance Consist;

        public ActiveTrainInfo(int depotX, int depotZ, TrainInstance consist)
        {
            DepotX = depotX;
            DepotZ = depotZ;
            Consist = consist;
        }
    }

    public bool AddStation(int depotX, int depotZ, int stX, int stZ)
    {
        TrainData td = GetTrain(depotX, depotZ);
        if (td == null) return false;
        if (GetTileID(stX, stZ) != 2) return false;

        Vector2Int st = new Vector2Int(stX, stZ);
        if (!td.stations.Contains(st))
        {
            td.stations.Add(st);
            Debug.Log($"[TrainSystem] Stanica [{stX},{stZ}] pridaná pre vlak v depe [{depotX},{depotZ}]. Celkom: {td.stations.Count}");
        }
        return true;
    }

    public bool StartTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;
        if (td.stations.Count < 2)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] nemá aspoň 2 stanice!");
            return false;
        }

        bool isResume = !td.isAtDepot && td.currentPath != null && td.currentPath.Count > 0;

        td.isRunning = true;
        td.isWaiting = false;
        td.isReturningToDepot = false;

        if (isResume)
        {
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] OBNOVENÝ (resume).");
        }
        else
        {
            td.isAtDepot = false;
            td.currentStationIndex = 0;
            td.reverseDirection = false;
            td.moveTimer = 0f;
            td.waitTimer = 0f;
            td.pendingPath = null;
            td.pendingReturnToDepot = false;

            // Reset path state
            td.activePath = null;
            td.locomotiveDistance = 0f;

            ComputeNextPathAsync(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] SPUSTENÝ (prvý štart).");
        }

        return true;
    }

    public bool StopTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;
        td.isRunning = false;
        td.isComputingPath = false;
        td.pendingPath = null;
        td.pendingReturnToDepot = false;
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] ZASTAVENÝ.");
        return true;
    }

    public ReturnToDepotResult ReturnToDepot(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return ReturnToDepotResult.Error;

        if (GetTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[TrainSystem] Depo [{dx},{dz}] neexistuje – vlak zastane.");
            td.isRunning = false;
            td.isStoppedAwaitingDepotReturn = false;
            return ReturnToDepotResult.Error;
        }

        if (td.isStoppedAwaitingDepotReturn)
        {
            td.isStoppedAwaitingDepotReturn = false;
            td.pendingReturnToDepot = false;
            td.isRunning = true;
            td.isWaiting = false;
            td.waitTimer = 0f;
            td.isGoingToDepotViaStation = false;
            td.isComputingPath = false;
            td.pendingPath = null;
            DispatchToDepot(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] → depo priamo (R po zastavení bez staníc).");
            return ReturnToDepotResult.Dispatched;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);

        if (td.isWaiting)
        {
            td.pendingReturnToDepot = false;
            td.isGoingToDepotViaStation = true;
            td.isStoppedAwaitingDepotReturn = false;
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] čaká na stanici → po odpočítaní pôjde do depa.");
            return ReturnToDepotResult.Dispatched;
        }

        if (IsDepotOnCurrentPath(td, depotTile))
        {
            td.pendingReturnToDepot = false;
            td.isGoingToDepotViaStation = false;
            td.isStoppedAwaitingDepotReturn = false;
            TrimPathToDepot(td, depotTile);
            td.isReturningToDepot = true;
            // Prebuduj PathData pre skrátenú cestu
            RebuildActivePathFromCurrentPath(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] → depo PRIAMO (depo je na aktuálnej ceste).");
            return ReturnToDepotResult.Dispatched;
        }

        td.pendingReturnToDepot = true;
        td.isGoingToDepotViaStation = false;
        td.isStoppedAwaitingDepotReturn = false;
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}]: depo nie je v smere jazdy → vlak dokončí cestu na plánovanú stanicu a potom pôjde do depa.");
        return ReturnToDepotResult.Dispatched;
    }

    bool IsDepotOnCurrentPath(TrainData td, Vector2Int depotTile)
    {
        if (td.currentPath == null || td.pathIndex >= td.currentPath.Count) return false;
        for (int i = td.pathIndex; i < td.currentPath.Count; i++)
            if (td.currentPath[i] == depotTile) return true;
        return false;
    }

    void TrimPathToDepot(TrainData td, Vector2Int depotTile)
    {
        if (td.currentPath == null) return;
        for (int i = td.pathIndex; i < td.currentPath.Count; i++)
        {
            if (td.currentPath[i] == depotTile)
            {
                td.currentPath = td.currentPath.GetRange(0, i + 1);
                return;
            }
        }
    }

    void DispatchToDepot(TrainData td)
    {
        int key = DepotKey(td.depotX, td.depotZ);
        Vector2Int goal = new Vector2Int(td.depotX, td.depotZ);
        Vector2Int start = td.currentTile;
        var snap = SnapshotTileGrid();

        td.isReturningToDepot = true;
        td.isGoingToDepotViaStation = false;
        td.isComputingPath = true;

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

        Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] → depo.");
    }

    /// <summary>
    /// Odstráni vlak z hry. Vymazanie je povolené VÝLUČNE vtedy, keď je
    /// vlak fyzicky v depe (currentTile == depot tile) a nie je v pohybe
    /// (isRunning == false). Flag isAtDepot sa NEPOUŽÍVA – v praxi sa
    /// dostal mimo synchronizáciu pri scenári, keď je vlak po stlačení S
    /// zaregistrovaný ako "vyšiel z depa" (isAtDepot = false), ale A*
    /// nenašiel cestu von, takže vlak zostal fyzicky stáť na depote.
    /// Po následnom P (StopTrain) by stary guard zablokoval T (RemoveTrain),
    /// hoci vlak depo nikdy neopustil.
    ///
    /// Nový guard kontroluje:
    ///   1. Vlak v evidencii existuje (td != null).
    ///   2. Vlak nie je v pohybe (!td.isRunning).
    ///   3. Vlak je fyzicky na tile depota (td.currentTile == (dx, dz)).
    ///   4. Tile (dx, dz) je stále depo (GetTileID == 3).
    ///
    /// Vyrieši oba scenáre používateľa:
    ///   • Q (CreateTrain) → T (RemoveTrain) bezprostredne:
    ///     vlak ešte nikdy nevyšiel, currentTile = (dx,dz), isRunning = false
    ///     → povolené.
    ///   • S (StartTrain) → A* nenájde cestu → P (StopTrain) → T:
    ///     vlak depo neopustil, currentTile = (dx,dz), isRunning = false
    ///     → povolené.
    /// V iných stavoch (vlak na trati, hoci aj zastavený) vymazanie
    /// zlyhá, čo je v súlade s požiadavkou.
    /// </summary>
    public bool RemoveTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;

        if (td.isRunning)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] sa nedá odstrániť – stále beží. Najprv ho zastavte (P).");
            return false;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);
        if (td.currentTile != depotTile)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] sa nedá odstrániť – nie je fyzicky v depe (aktuálne na [{td.currentTile.x},{td.currentTile.y}]).");
            return false;
        }

        if (GetTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[TrainSystem] Tile [{dx},{dz}] už nie je depo – vlak sa nedá odstrániť.");
            return false;
        }

        // Defenzívne: ak by ešte stále prebiehal asynchrónny A* výpočet,
        // jeho výsledok bude zahodený (TryGetValue v ApplyPendingPathResults
        // neuspeje, lebo sme záznam už odstránili z trains).
        td.isComputingPath = false;
        td.pendingPath = null;

        // Uvoľníme VŠETKY štruktúry, ktoré vytvoril CreateTrain – symetricky.
        DestroyTrainStructures(td);

        trains.Remove(DepotKey(dx, dz));
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] ODSTRÁNENÝ.");
        return true;
    }

    /// <summary>
    /// Uvoľní všetky štruktúry vlakovej súpravy, ktoré boli vytvorené
    /// v CreateTrain. Táto metóda je presným ZRKADLOM CreateTrain –
    /// pre každú štruktúru vytvorenú pri vytvorení vlaku tu existuje
    /// zodpovedajúce zrušenie:
    ///
    ///   CreateTrain vytvorí          →  DestroyTrainStructures uvoľní
    ///   ─────────────────────────────────────────────────────────────
    ///   td.trainObject (GameObject)   →  Destroy(td.trainObject)
    ///   td.locomotive  (GameObject)   →  Destroy(td.locomotive)
    ///   td.wagons      (GameObjecty)  →  Destroy(každý) + zoznam vyčistený
    ///   td.consist     (TrainInstance →  Wagons vyčistené + referencia null
    ///                  + WagonInstance)
    ///
    /// Týmto je zaručené, že ak sa pri vytvorení vlaku alokuje nejaká
    /// štruktúra, tá istá štruktúra sa pri odstránení vlaku aj korektne
    /// uvoľní – žiadne "visiace" GameObjecty ani mŕtve referencie
    /// v td.wagons po Destroy().
    /// </summary>
    void DestroyTrainStructures(TrainData td)
    {
        if (td == null) return;

        // Skrytá kocka – pohybová logika (zrkadlí: cube → td.trainObject).
        if (td.trainObject != null) Destroy(td.trainObject);
        td.trainObject = null;

        // Vizuálna lokomotíva (zrkadlí: CreateConsistPart → td.locomotive).
        if (td.locomotive != null) Destroy(td.locomotive);
        td.locomotive = null;

        // Vizuálne vagóny (zrkadlí: CreateConsistPart slučka → td.wagons).
        if (td.wagons != null)
        {
            foreach (var w in td.wagons)
                if (w != null) Destroy(w);
            // Vyčistíme zoznam, aby v td.wagons neostali mŕtve referencie
            // na už zničené GameObjecty.
            td.wagons.Clear();
        }

        // Dátová štruktúra súpravy (zrkadlí: new TrainInstance + WagonInstance
        // slučka → td.consist). Uvoľníme vagóny a samotnú referenciu.
        if (td.consist != null)
        {
            td.consist.Wagons?.Clear();
            td.consist = null;
        }

        // Vyčistíme aj pohybový / pathfinding stav, aby objekt TrainData
        // neostal v nekonzistentnom stave, ak naň ešte drží referenciu
        // prebiehajúci asynchrónny výpočet.
        td.activePath = null;
        td.currentPath = null;
        td.pendingPath = null;
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

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td.trainObject == null) continue;
            if (!td.isRunning) continue;
            UpdateTrain(td);
        }
    }

    // =====================================================================
    // POHYB VLAKU – DISTANCE-BASED
    // =====================================================================

    void UpdateTrain(TrainData td)
    {
        // ── Čakanie (stanica / pauza) ─────────────────────────────────────
        if (td.isWaiting)
        {
            td.waitTimer -= Time.deltaTime;

            // ── OBCHOD: po 2 s čakania na cieľovej stanici ────────────────
            // Spustí sa práve raz za zastávku. tradeDoneAtStation je nastavené
            // na false iba pri príchode na cieľovú stanicu (OnPathComplete),
            // takže pri BREAK_WAIT ani pri ceste do depa sa obchod nespustí.
            if (!td.tradeDoneAtStation
                && (STATION_WAIT - td.waitTimer) >= TRADE_DELAY)
            {
                td.tradeDoneAtStation = true;
                TryExecuteTradeAtStation(td);
            }

            if (td.waitTimer <= 0f)
            {
                td.isWaiting = false;
                td.waitTimer = 0f;

                if (td.isReturningToDepot)
                    DispatchToDepot(td);
                else if (td.isGoingToDepotViaStation)
                    DispatchToDepot(td);
                else
                    AdvanceStation(td);
            }
            return;
        }

        // ── Čakáme na výpočet novej trasy ────────────────────────────────
        if (td.activePath == null)
        {
            if (td.isComputingPath) return;
            // Ak niet aktívnej trasy a nie sme počítajú, skúsime spustiť prepočet
            OnPathComplete(td);
            return;
        }

        // ── Cieľová rýchlosť podľa sklonu trate (do kopca pomalšie, z kopca rýchlejšie) ──
        float grade = td.activePath.GetGradeAtDistance(td.locomotiveDistance);
        float targetSpeed = TargetSpeedForGrade(td, grade);

        // ── Brzdenie pred koncom trasy (stanica / depo) ──────────────────
        // Brzdná dráha pri rovnomernom spomaľovaní: d = v² / (2·a). Ak
        // zostávajúca vzdialenosť do konca trasy klesne pod brzdnú dráhu,
        // cieľová rýchlosť je 0 – vlak plynulo zabrzdí namiesto náhleho
        // zastavenia pri dosiahnutí totalLength.
        float remaining = td.activePath.totalLength - td.locomotiveDistance;
        float brakingDistance = (td.trainSpeed * td.trainSpeed) / (2f * Mathf.Max(td.deceleration, MIN_ACCEL));
        if (remaining <= brakingDistance)
            targetSpeed = 0f;

        // ── Plynulé zrýchľovanie/spomaľovanie smerom k cieľovej rýchlosti ──
        float rate = (targetSpeed > td.trainSpeed) ? td.acceleration : td.deceleration;
        td.trainSpeed = Mathf.MoveTowards(td.trainSpeed, targetSpeed, rate * Time.deltaTime);

        // Poistka proti "zamrznutiu" tesne pred cieľom – ak brzdíme na
        // targetSpeed 0, ale ešte zostáva kus trasy, udržíme minimálnu
        // dobiehaciu rýchlosť, aby súprava v konečnom čase reálne dorazila.
        if (targetSpeed <= 0f && remaining > 0.001f)
            td.trainSpeed = Mathf.Max(td.trainSpeed, MIN_ARRIVAL_CRAWL_SPEED);

        // ── Pohyb lokomotívy pozdĺž trasy ────────────────────────────────
        td.locomotiveDistance += td.trainSpeed * Time.deltaTime;

        // Aktualizujeme currentTile podľa najbližšieho bodu na trase
        UpdateCurrentTile(td);

        // Pohyb vizuálnej hlavy (skrytá kocka)
        Vector3 headPos = td.activePath.GetPositionAtDistance(td.locomotiveDistance);
        td.trainObject.transform.position = headPos;

        // ── Aktualizácia vizuálnej súpravy ───────────────────────────────
        UpdateConsistVisuals(td);

        // ── Detekcia konca trasy ──────────────────────────────────────────
        if (td.locomotiveDistance >= td.activePath.totalLength)
        {
            // Lokomotíva dorazila na koniec – zafixujeme na posledný bod
            td.locomotiveDistance = td.activePath.totalLength;
            td.currentTile = td.currentPath != null && td.currentPath.Count > 0
                ? td.currentPath[td.currentPath.Count - 1]
                : td.currentTile;

            // Aplicujeme pendingPath ak existuje
            if (td.pendingPath != null)
            {
                List<Vector2Int> pending = td.pendingPath;
                td.pendingPath = null;
                ApplyNewPath(td, pending);
                return;
            }

            td.activePath = null;
            OnPathComplete(td);
        }
    }

    /// <summary>
    /// Udržiava currentTile synchronizované s aktuálnou pozíciou lokomotívy na trase.
    /// Použije waypointToTileIdx mapovanie pre presné určenie aktuálnej dlaždice
    /// pri waypoint-based pohybe.
    /// </summary>
    void UpdateCurrentTile(TrainData td)
    {
        if (td.currentPath == null || td.activePath == null) return;
        if (td.waypointToTileIdx == null || td.waypointToTileIdx.Length == 0) return;

        // Nájdeme index waypointu, kde sa lokomotíva práve nachádza
        float[] cumLens = td.activePath.cumulativeLengths;
        int lastPassedIdx = 0;
        for (int i = 0; i < cumLens.Length; i++)
        {
            if (cumLens[i] <= td.locomotiveDistance)
                lastPassedIdx = i;
            else
                break;
        }

        // Mapuj waypoint-index na sub-path tile-index, potom na currentPath-index
        int subPathTileIdx = td.waypointToTileIdx[lastPassedIdx];

        // MOST / TUNEL: vo vnútri prechodu drž currentTile na VSTUPNEJ hlave.
        if (td.subPathEntryHead != null
            && subPathTileIdx >= 0 && subPathTileIdx < td.subPathEntryHead.Length
            && td.subPathEntryHead[subPathTileIdx] >= 0)
            subPathTileIdx = td.subPathEntryHead[subPathTileIdx];

        int tileIdx = td.pathIndexAtPathStart + subPathTileIdx;
        if (tileIdx >= 0 && tileIdx < td.currentPath.Count)
            td.currentTile = td.currentPath[tileIdx];
    }

    // =====================================================================
    // VIZUÁLNA SÚPRAVA – DISTANCE-BASED
    // =====================================================================

    /// <summary>
    /// Aktualizuje pozíciu, rotáciu a viditeľnosť každého člena súpravy.
    ///
    /// Člen i:
    ///   offsetDist = i * wagonSpacing
    ///   memberDist = locomotiveDistance - offsetDist  (min 0)
    ///   pos = activePath.GetPositionAtDistance(memberDist)
    ///   rot = Quaternion.LookRotation(activePath.GetDirectionAtDistance(memberDist))
    ///
    /// Zobrazenie: člen i sa zobrazí keď locomotiveDistance >= i * wagonSpacing.
    /// Skrývanie pri návrate do depa: keď memberDist >= totalLength - DEPOT_HIDE_MARGIN.
    /// </summary>
    void UpdateConsistVisuals(TrainData td)
    {
        if (td.activePath == null) return;

        const float DEPOT_HIDE_RADIUS = 0.55f;
        bool isReturning = td.isReturningToDepot;
        Vector3 depotCenter = TileCenter(new Vector2Int(td.depotX, td.depotZ));
        int totalMembers = 1 + td.wagons.Count;

        for (int i = 0; i < totalMembers; i++)
        {
            float offsetDist = i * wagonSpacing;
            GameObject member = (i == 0) ? td.locomotive : td.wagons[i - 1];
            if (member == null) continue;

            // ── Kritérium zobrazenia ───────────────────────────────────────
            bool shouldBeVisible = td.locomotiveDistance >= offsetDist;

            float memberDist = Mathf.Max(0f, td.locomotiveDistance - offsetDist);

            // ── Skrývanie pri návrate do depa ─────────────────────────────
            if (isReturning && shouldBeVisible)
            {
                Vector3 memberPos = td.activePath.GetPositionAtDistance(memberDist);
                if (Vector3.Distance(memberPos, depotCenter) < DEPOT_HIDE_RADIUS)
                    shouldBeVisible = false;
            }

            // ── Skrývanie v TUNELI ─────────────────────────────────────────
            // Člen (lokomotíva / vagón) sa skryje, keď jeho PREDOK vojde za
            // portál do tunela, a odkryje, keď z tunela vyjde jeho ZADOK –
            // žiadna časť modelu tak netrčí cez terén kopca.
            if (shouldBeVisible)
            {
                var crossings = RailCrossingSystem.instance;
                if (crossings != null && crossings.Count > 0 && crossings.HideTrainsInsideTunnels)
                {
                    float halfLen = consistMemberLength * 0.5f;
                    if (crossings.IsBodyInsideTunnel(
                            td.activePath.GetPositionAtDistance(memberDist - halfLen),
                            td.activePath.GetPositionAtDistance(memberDist),
                            td.activePath.GetPositionAtDistance(memberDist + halfLen),
                            TRACK_OFFSET_Y))
                        shouldBeVisible = false;
                }
            }

            if (member.activeSelf != shouldBeVisible)
                member.SetActive(shouldBeVisible);

            if (!shouldBeVisible) continue;

            // ── Pozícia ───────────────────────────────────────────────────
            Vector3 pos = td.activePath.GetPositionAtDistance(memberDist);

            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
            {
                Debug.LogWarning($"[TrainSystem] NaN pozícia pre člen {i} – preskočené.");
                continue;
            }

            // ── Rotácia – priamo zo smeru segmentu, bez akumulácie ────────
            Vector3 dir = td.activePath.GetDirectionAtDistance(memberDist);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            member.transform.position = pos;
            member.transform.rotation = rot;

            // ── DIAGNOSTIKA: je model vycentrovaný na svojom pivote? ──────
            // Meria sa až POČAS jazdy, teda v presne tom stave, ktorý vidíš
            // na obrazovke – nie pri vytvorení v depe. Odpoveď je jednoznačná:
            //   lokálny stred ≈ (0, ?, 0)  → model JE na pivote, vychýlená je
            //                                trajektória alebo koľajnice
            //   lokálny stred výrazne ≠ 0  → zlyháva centrovanie modelu a
            //                                číslo rovno hovorí o koľko a kam
            LogRuntimeCentering(member);
        }
    }

    /// <summary>Koľko riadkov runtime-diagnostiky ešte vypísať (aby konzola nezahltila).</summary>
    private int _runtimeFitLogsLeft = 12;

    /// <summary>
    /// Vypíše pre člena súpravy porovnanie jeho pivotu so skutočným stredom
    /// geometrie modelu, vyjadreným v LOKÁLNYCH súradniciach pivotu.
    /// Meria len podobjekt "Model" – prípadný diagnostický referenčný kváder
    /// je zámerne vynechaný, aby výsledok neskresľoval.
    /// </summary>
    void LogRuntimeCentering(GameObject member)
    {
        if (!logConsistFit || _runtimeFitLogsLeft <= 0 || member == null) return;

        Transform modelTf = member.transform.Find("Model");
        if (modelTf == null) return; // kvádrový fallback – nie je čo merať

        if (!TryGetLocalBounds(member.transform, modelTf.gameObject, out Bounds lb)) return;

        _runtimeFitLogsLeft--;
        Debug.Log($"[TrainSystem] RUNTIME '{member.name}': pivot vo svete = {member.transform.position} | " +
                  $"stred modelu v lokále pivotu = ({lb.center.x:F3}, {lb.center.y:F3}, {lb.center.z:F3}) | " +
                  $"rozmery modelu = ({lb.size.x:F3}, {lb.size.y:F3}, {lb.size.z:F3}) " +
                  $"→ očakávané x≈0 a z≈0");
    }

    // =====================================================================
    // DOKONČENIE CESTY
    // =====================================================================

    void OnPathComplete(TrainData td)
    {
        if (td.isReturningToDepot)
        {
            td.isRunning = false;
            td.isAtDepot = true;
            td.isReturningToDepot = false;
            td.isGoingToDepotViaStation = false;
            td.currentTile = new Vector2Int(td.depotX, td.depotZ);
            td.trainObject.transform.position = TileCenter(td.currentTile);
            td.activePath = null;
            td.locomotiveDistance = 0f;

            HideAllConsist(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] dorazil do DEPA – súprava skrytá.");
            return;
        }

        if (td.isGoingToDepotViaStation)
        {
            bool onStation = td.stations.Contains(td.currentTile)
                             && GetTileID(td.currentTile.x, td.currentTile.y) == 2;
            if (onStation)
            {
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{td.currentTile.x},{td.currentTile.y}] pred depom – čaká {STATION_WAIT}s.");
                td.trainObject.transform.position = TileCenter(td.currentTile);
                td.isWaiting = true;
                td.waitTimer = STATION_WAIT;
                // Zastávka pred návratom do depa – obchod sa nerealizuje.
                td.tradeDoneAtStation = true;
            }
            else
            {
                Debug.LogWarning($"[TrainSystem] Vlak nedosiahol stanicu pred depom. Zastane.");
                td.isRunning = false;
                td.isGoingToDepotViaStation = false;
            }
            return;
        }

        Vector2Int targetStation = td.stations[td.currentStationIndex];
        if (td.currentTile == targetStation)
        {
            if (td.pendingReturnToDepot)
            {
                td.pendingReturnToDepot = false;
                td.isGoingToDepotViaStation = true;
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{targetStation.x},{targetStation.y}] (pending návrat do depa) – čaká {STATION_WAIT}s, potom depo.");
                // Posledná zastávka pred depom – obchod sa nerealizuje.
                td.tradeDoneAtStation = true;
            }
            else
            {
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{targetStation.x},{targetStation.y}] – čaká {STATION_WAIT}s.");
                // Riadna zastávka na cieľovej stanici – po 2 s prebehne obchod.
                td.tradeDoneAtStation = false;
            }
            td.trainObject.transform.position = TileCenter(td.currentTile);
            td.isWaiting = true;
            td.waitTimer = STATION_WAIT;
        }
        else
        {
            Debug.LogWarning($"[TrainSystem] Vlak nedosiahol stanicu [{targetStation.x},{targetStation.y}]. Čaká {BREAK_WAIT}s.");
            td.isWaiting = true;
            td.waitTimer = BREAK_WAIT;
            // Núdzové prerušenie – nie je to zastávka na stanici, žiadny obchod.
            td.tradeDoneAtStation = true;
        }
    }

    // =====================================================================
    // OBCHOD NA STANICI – TRADE (TradeSystem)
    // =====================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi súpravou vlaku a továrňami stanice, na
    /// ktorej vlak práve čaká. Volá sa z UpdateTrain po 2 s čakania
    /// (TRADE_DELAY), práve raz za zastávku.
    ///
    /// Postup:
    ///   1) Zisti tile, na ktorom vlak stojí (cieľová stanica).
    ///   2) Nájdi StationInstance pre tento tile v StationRegistry.
    ///      Ak ešte nie je zaregistrovaná (napr. stanica pribudla bez
    ///      následného scanu), dorovná sa cez RescanAll.
    ///   3) Odovzdaj súpravu + stanicu do TradeSystem.Execute.
    ///   4) Podľa výsledku vypíš Debug.Log (úspech / neúspech).
    ///
    /// Výpisy presne zodpovedajú zadaniu:
    ///   úspech  → "Predaj prebehol úspešne, cena {N} euro"
    ///   neúspech→ "Obchod neprebehol."
    /// </summary>
    void TryExecuteTradeAtStation(TrainData td)
    {
        // Bezpečnostné kontroly – bez dátovej súpravy nemá obchod zmysel.
        if (td == null || td.consist == null)
        {
            Debug.Log("Obchod neprebehol.");
            return;
        }

        Vector2Int stationTile = td.currentTile;

        // Nájdi stanicu v registri. Ak chýba, skús dorovnať register.
        StationInstance station = RailStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        if (station == null)
        {
            RailStationRegistry.RescanAll();
            station = RailStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        }

        if (station == null)
        {
            Debug.Log("Obchod neprebehol.");
            Debug.LogWarning($"[TrainSystem] Pre tile [{stationTile.x},{stationTile.y}] " +
                             $"sa nenašla StationInstance – obchod sa neuskutočnil.");
            return;
        }

        // Vykonaj samotnú transakciu (čisto dátová operácia).
        TrainTradeSystem.TradeResult result = TrainTradeSystem.Execute(td.consist, station);

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
                ResourceType soldResource = td.consist.Wagons.Count > 0
                    ? TrainTradeSystem.WagonResource(td.consist.Wagons[0])
                    : ResourceType.None;
                GameMenuHUDPanel.ReportSale(GameMenuHUDPanel.TransportKind.Rail,
                    soldResource, result.UnloadedUnits, result.Revenue);

                // ZVUK "Cash" – jediný samočinný zvuk hry. Volá sa pri každom
                // zárobku bezpodmienečne; GameManager si sám overí, či je vlak
                // v zábere kamery a či od posledného Cash uplynul minimálny
                // odstup – zvuk teda zaznie len pri obchode, ktorý hráč vidí.
                if (td.locomotive != null)
                    GameManager.instance?.PlaySfxCash(td.locomotive.transform.position);
            }

            Debug.Log($"Obchodná transakcia bola vykonaná úspešne, suma {result.Revenue} CR.");
            Debug.Log($"[TrainSystem] Obchod na stanici [{stationTile.x},{stationTile.y}]: " +
                      $"naložené {result.LoadedUnits}, vyložené {result.UnloadedUnits}, " +
                      $"zárobok {result.Revenue} CR.");

            // Plávajúci cenový label nad vlakom na 3 s. Volá sa presne tu –
            // teda po 2 s od príchodu na stanicu (keď prebehne transakcia).
            if (td.locomotive != null)
                SalePriceLabelManager.Instance.ShowPrice(
                    td.locomotive.transform.position, result.Revenue);
        }
        else
        {
            Debug.Log("Obchod neprebehol.");
            Debug.Log($"[TrainSystem] Obchod na stanici [{stationTile.x},{stationTile.y}] " +
                      $"neprebehol – dôvod: {result.Reason}.");
        }
    }

    void HideAllConsist(TrainData td)
    {
        if (td.locomotive != null) td.locomotive.SetActive(false);
        foreach (var w in td.wagons)
            if (w != null) w.SetActive(false);
    }

    // =====================================================================
    // ADVANCE STATION – OBRAT SMERU
    // =====================================================================

    void AdvanceStation(TrainData td)
    {
        if (!td.reverseDirection)
        {
            td.currentStationIndex++;
            if (td.currentStationIndex >= td.stations.Count)
            {
                td.reverseDirection = true;
                td.currentStationIndex = td.stations.Count - 2;
                if (td.currentStationIndex < 0) td.currentStationIndex = 0;
            }
        }
        else
        {
            td.currentStationIndex--;
            if (td.currentStationIndex < 0)
            {
                td.reverseDirection = false;
                td.currentStationIndex = 1;
                if (td.currentStationIndex >= td.stations.Count)
                    td.currentStationIndex = 0;
            }
        }

        ComputeNextPathAsync(td);
    }

    // =====================================================================
    // ASYNC A* WRAPPER
    // =====================================================================

    void ComputeNextPathAsync(TrainData td)
    {
        if (td.stations.Count == 0) return;
        if (td.isComputingPath) return;

        Vector2Int goal = td.stations[td.currentStationIndex];
        Vector2Int start = td.currentTile;
        int key = DepotKey(td.depotX, td.depotZ);
        var snap = SnapshotTileGrid();

        td.isComputingPath = true;

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
        // Mapa sa zmenila (pribudli/ubudli koľaje, stanice alebo továrne) –
        // prepočítaj 9×9 zóny staníc a ich evidenciu tovární. Lacná operácia.
        RailStationRegistry.RescanAll();

        var snap = SnapshotTileGrid();

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (!td.isRunning) continue;
            if (td.isComputingPath) continue;

            int key = kvp.Key;

            if (td.isReturningToDepot)
            {
                Vector2Int start = td.currentTile;
                Vector2Int goal = new Vector2Int(td.depotX, td.depotZ);
                td.isComputingPath = true;
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
            else if (td.isGoingToDepotViaStation)
            {
                Vector2Int? nearest = FindNearestStation(td);
                if (!nearest.HasValue) continue;
                Vector2Int start = td.currentTile;
                Vector2Int goal = nearest.Value;
                td.isComputingPath = true;
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
                if (td.stations.Count == 0) continue;
                Vector2Int start = td.currentTile;
                Vector2Int goal = td.stations[td.currentStationIndex];
                td.isComputingPath = true;
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

    // =====================================================================
    // POMOCNÁ – Nearest Station
    // =====================================================================

    Vector2Int? FindNearestStation(TrainData td)
    {
        Vector2Int? nearest = null;
        int bestDist = int.MaxValue;
        foreach (var st in td.stations)
        {
            if (GetTileID(st.x, st.y) != 2) continue;
            int dist = Math.Abs(td.currentTile.x - st.x) + Math.Abs(td.currentTile.y - st.y);
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
            if (!trains.TryGetValue(result.depotKey, out TrainData td)) continue;

            td.isComputingPath = false;
            bool hasPath = result.path != null && result.path.Count > 0;

            if (result.destination == PathDestination.Depot)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    td.isRunning = false; td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] nemôže nájsť cestu do depa, čaká.");
                }
                continue;
            }

            if (result.destination == PathDestination.ReturnViaStation)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    Debug.LogWarning($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] nemôže nájsť cestu na stanicu. Zastane.");
                    td.isRunning = false; td.isWaiting = false; td.isGoingToDepotViaStation = false;
                }
                continue;
            }

            if (result.isReturnToDepot)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    td.isRunning = false; td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[TrainSystem] Vlak nemôže nájsť cestu do depa, čaká.");
                }
            }
            else
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    bool anyStationExists = false;
                    foreach (var st in td.stations)
                        if (GetTileID(st.x, st.y) == 2) { anyStationExists = true; break; }

                    if (!anyStationExists)
                    {
                        Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}]: žiadne dostupné stanice – vlak zastane. Stlačte R pre návrat do depa.");
                        td.isRunning = false; td.isWaiting = false;
                        td.isStoppedAwaitingDepotReturn = true;
                        td.currentPath = new List<Vector2Int>(); td.pathIndex = 0;
                        td.activePath = null;

                        // Synchronizácia flag-u isAtDepot s reálnou polohou.
                        // StartTrain (ne-resume) ho už nastavil na false, ale
                        // vlak fyzicky depo neopustil → vraciame flag späť na
                        // true, aby zodpovedal skutočnosti.
                        if (td.currentTile.x == td.depotX && td.currentTile.y == td.depotZ)
                            td.isAtDepot = true;
                    }
                    else
                    {
                        if (td.stations.Count > 0)
                        {
                            var goal = td.stations[td.currentStationIndex];
                            Debug.LogWarning($"[TrainSystem] A* nenašiel cestu do [{goal.x},{goal.y}]. Čakám {BREAK_WAIT}s.");
                        }
                        td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                        td.currentPath = new List<Vector2Int>(); td.pathIndex = 0;
                        td.activePath = null;

                        // Synchronizácia flag-u isAtDepot s reálnou polohou.
                        // Vlak po neúspešnom A* zostáva fyzicky na depe –
                        // flag musí zodpovedať tomuto stavu, aby StopTrain (P)
                        // a následný RemoveTrain (T) fungovali korektne.
                        if (td.currentTile.x == td.depotX && td.currentTile.y == td.depotZ)
                            td.isAtDepot = true;
                    }
                }
            }
        }
    }

    // =====================================================================
    // APPLY NEW PATH – konvertuje List<Vector2Int> na PathData (waypoint-based)
    // =====================================================================

    void ApplyNewPath(TrainData td, List<Vector2Int> newPath)
    {
        bool isMoving = td.activePath != null && td.locomotiveDistance < td.activePath.totalLength;

        // Ak vlak práve prechádza medzi dvoma dlaždicami, aplikujeme nový
        // path od dlaždice, na ktorej sa aktuálne nachádza
        Vector2Int startTile = td.currentTile;
        int startIdxInNew = -1;
        for (int i = 0; i < newPath.Count; i++)
        {
            if (newPath[i] == startTile) { startIdxInNew = i; break; }
        }

        if (startIdxInNew < 0)
        {
            // Vlak nie je na novej ceste – odložíme na neskôr alebo aplikujeme od začiatku
            if (isMoving)
            {
                td.pendingPath = newPath;
                return;
            }
            startIdxInNew = 0;
        }

        // Subpath od aktuálneho tielu po koniec
        List<Vector2Int> subPath = newPath.GetRange(startIdxInNew, newPath.Count - startIdxInNew);

        td.currentPath = newPath;
        td.pathIndex = startIdxInNew;
        td.pathIndexAtPathStart = startIdxInNew;
        td.moveTimer = 0f;

        // Zostav PathData – WAYPOINT-BASED (centrá + hrany + krivkové rohy)
        BuildWaypointPath(subPath, out var pts, out var tileIdxList);
        var newPathData = new PathData(pts);

        // Rýchlosť (maxSpeed/acceleration/deceleration) je fixná od vytvorenia
        // vlaku (ComputeMotionParams) – neprepočítava sa podľa dĺžky cesty.
        // td.trainSpeed (aktuálna rýchlosť) sa NERESETUJE, aby pohyb medzi
        // dvomi po sebe idúcimi úsekmi trasy (napr. pendingPath) zostal plynulý
        // bez trhnutia; UpdateTrain ju každý frame priblíži k cieľovej rýchlosti.
        td.locomotiveDistance = 0f;
        td.activePath = newPathData;
        td.waypointToTileIdx = tileIdxList.ToArray();
        td.subPathEntryHead = ResolveEntryHeads(subPath);
    }

    /// <summary>MOSTY / TUNELY: index vstupnej hlavy pre tily vo vnútri prechodu (viď TrainData.subPathEntryHead).</summary>
    static int[] ResolveEntryHeads(List<Vector2Int> subPath)
    {
        if (RailCrossingSystem.instance == null) return null;
        RailCrossingSystem.instance.ResolvePathCrossings(subPath, out int[] entryHeads);
        return entryHeads;
    }

    /// <summary>
    /// Prebuduje activePath z currentPath (po TrimPathToDepot).
    /// </summary>
    void RebuildActivePathFromCurrentPath(TrainData td)
    {
        if (td.currentPath == null || td.currentPath.Count == 0)
        {
            td.activePath = null;
            return;
        }

        int startIdx = td.pathIndex;
        if (startIdx >= td.currentPath.Count) { td.activePath = null; return; }

        List<Vector2Int> sub = td.currentPath.GetRange(startIdx, td.currentPath.Count - startIdx);

        BuildWaypointPath(sub, out var pts, out var tileIdxList);
        var pd = new PathData(pts);

        td.pathIndexAtPathStart = startIdx;
        // Rýchlosť je fixná od vytvorenia vlaku (ComputeMotionParams) –
        // td.trainSpeed (aktuálna rýchlosť) sa nemení, len sa resetuje pozícia.
        td.locomotiveDistance = 0f;
        td.activePath = pd;
        td.waypointToTileIdx = tileIdxList.ToArray();
        td.subPathEntryHead = ResolveEntryHeads(sub);
    }

    // =====================================================================
    // A* PATHFINDING – THREAD-SAFE (modifikované – CanMove namiesto IsPassable)
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
        new Vector2Int( 1, 0),  // Right
        new Vector2Int(-1, 0),  // Left
        new Vector2Int( 0, 1),  // Top
        new Vector2Int( 0,-1)   // Bottom
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
    ///
    /// MOSTY / TUNELY: skok medzi dvomi hlavami (nesusedné uzly) sa rozvinie
    /// na všetky tily medzi nimi, takže výsledná trasa je vždy SÚVISLÁ a
    /// zvyšok TrainSystem-u (BuildWaypointPath, UpdateCurrentTile, ...)
    /// pracuje so susednými dlaždicami ako doteraz.
    ///
    /// Výnimka: ak je hneď PRVÝ krok skokom (vlak stojí na hlave a ide do
    /// prechodu), štartový tile sa v trase PONECHÁ. Bez neho by trasa začínala
    /// vnútorným tile mosta a nedalo by sa jednoznačne určiť, že ide o most
    /// a nie o koľaj pod ním.
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
                if (step.x != 0 && step.y != 0) { path.Add(b); continue; } // poistka – nemalo by nastať
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
        bw.Write(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            var consist = td.consist;

            bw.Write(td.depotX);
            bw.Write(td.depotZ);

            // Typy (indexy v katalógu – mapovanie cez Name, ktoré je unikátne).
            bw.Write(TrainTypeIndexOf(consist));
            bw.Write(WagonTypeIndexOf(consist));

            // Počet vagónov + vek.
            int wc = consist != null ? consist.Wagons.Count : 0;
            bw.Write(wc);
            bw.Write(consist != null ? consist.Age : 0);

            // Náklad jednotlivých vagónov.
            for (int i = 0; i < wc; i++)
                bw.Write(consist.Wagons[i].CurrentCapacity);

            // Priradené stanice.
            bw.Write(td.stations != null ? td.stations.Count : 0);
            if (td.stations != null)
                foreach (var s in td.stations) { bw.Write(s.x); bw.Write(s.y); }

            // Beh/stop.
            bw.Write(td.isRunning);
        }
    }

    // =====================================================================
    // ODLOŽENÉ SPUSTENIE VLAKOV PO LOAD
    // ─────────────────────────────────────────────────────────────────────
    // ReadSave beží v IndicatrixAPI.LoadGame SKÔR, než sa načítajú mosty a
    // tunely (RailCrossingSystem). Keby sa vlak spustil priamo v ReadSave,
    // StartTrain → ComputeNextPathAsync → SnapshotTileGrid by vzal snapshot
    // BEZ skokových hrán prechodov: A* by trasu cez most/tunel nenašiel,
    // vlak by po BREAK_WAIT zavolal AdvanceStation a preskočil prvú stanicu
    // (alebo by išiel obchádzkou). Preto si ReadSave bežiace vlaky len
    // zapamätá a spustí ich až IndicatrixAPI.LoadGame na úplnom konci,
    // keď sú prechody zaregistrované.
    // =====================================================================
    private readonly List<Vector2Int> _startAfterLoad = new List<Vector2Int>();

    /// <summary>
    /// Spustí vlaky, ktoré v uloženej hre bežali. Volá IndicatrixAPI.LoadGame
    /// AŽ PO načítaní mostov a tunelov.
    /// </summary>
    public void StartTrainsAfterLoad()
    {
        foreach (var d in _startAfterLoad)
            StartTrain(d.x, d.y);
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
            int trainTypeIndex = br.ReadInt32();
            int wagonTypeIndex = br.ReadInt32();
            int wagonCnt = br.ReadInt32();
            int age = br.ReadInt32();

            int[] cargo = new int[wagonCnt];
            for (int i = 0; i < wagonCnt; i++) cargo[i] = br.ReadInt32();

            int stationCnt = br.ReadInt32();
            var stations = new System.Collections.Generic.List<Vector2Int>(stationCnt);
            for (int i = 0; i < stationCnt; i++)
                stations.Add(new Vector2Int(br.ReadInt32(), br.ReadInt32()));

            bool wasRunning = br.ReadBoolean();

            // CreateTrain použije pole `wagonCount` ako počet vagónov –
            // nastavíme ho na uloženú hodnotu (CreateTrain si ho ešte oreže 1..10).
            wagonCount = Mathf.Clamp(wagonCnt, 1, 10);
            if (!CreateTrain(dx, dz, trainTypeIndex, wagonTypeIndex))
                continue;

            TrainData td = GetTrain(dx, dz);
            if (td == null) continue;

            if (td.consist != null)
            {
                td.consist.Age = age;
                int n = Mathf.Min(cargo.Length, td.consist.Wagons.Count);
                for (int i = 0; i < n; i++)
                    td.consist.Wagons[i].CurrentCapacity = cargo[i];
            }

            foreach (var s in stations)
                AddStation(dx, dz, s.x, s.y);

            // NESPÚŠŤAŤ tu – mosty/tunely ešte nie sú načítané (pozri
            // StartTrainsAfterLoad). Spustí sa na konci IndicatrixAPI.LoadGame.
            if (wasRunning)
                _startAfterLoad.Add(new Vector2Int(dx, dz));
        }
    }

    /// <summary>Index typu vlaku v TrainCatalog (podľa unikátneho Name); 0 ak sa nenájde.</summary>
    private int TrainTypeIndexOf(Game.TrainStock.TrainInstance consist)
    {
        if (consist == null) return 0;
        var all = Game.TrainStock.TrainCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Name == consist.Spec.Name) return i;
        return 0;
    }

    /// <summary>Index typu vagónu v WagonCatalog (podľa Name 1. vagónu); 0 ak sa nenájde.</summary>
    private int WagonTypeIndexOf(Game.TrainStock.TrainInstance consist)
    {
        if (consist == null || consist.Wagons.Count == 0) return 0;
        string name = consist.Wagons[0].Spec.Name;
        var all = Game.TrainStock.WagonCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Name == name) return i;
        return 0;
    }

}