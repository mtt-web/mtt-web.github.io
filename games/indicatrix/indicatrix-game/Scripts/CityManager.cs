using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CityManager
/// ─────────────────────────────────────────────────────────────────────────
/// GENERÁTOR MIEST pre Transport-Tycoon hru. Po vygenerovaní terénu
/// (TerrainManager) rozmiestni na mapu náhodne pozičné mestá a vystavia ich
/// budovami.
///
/// HLAVNÉ PRAVIDLÁ (podľa zadania):
///   • Fixný počet miest: 25 – 30.
///   • Región mesta: 10×10 až 15×15 tilov (priemer ~12×12), nemusí byť štvorec
///     (napr. 10×15, 11×13 …).
///   • Počet budov = ~30 % z počtu tilov regiónu (alikvotne pre ľubovoľnú veľkosť).
///   • Tri typy miest podľa výškových sád budov (CityBuildingLibrary.BuildingTier):
///       – Small Town  → len nízke budovy (Low),
///       – Medium Town → MIX Low + Medium (stred stredné, okraje nízke),
///       – Big Town    → MIX Low + Medium + High (stred vysoké, okolo stredné,
///         na okraji nízke) → mestská aglomerácia.
///   • Min. variabilita: mesto použije aspoň 3 rôzne typy budov.
///   • Budovy hneď vedľa seba (bez medzier), občasná medzera hlavne na okraji.
///
/// UMIESTNENIE BUDOVY – budova smie stáť LEN na tile, ktorý:
///   • je rovný (všetky 4 rohové Y rovnaké),
///   • je na úrovni terénu E ≥ 0 (Y ≥ 3.00, t.j. NIE na hladine vody E=-1),
///   • je na úrovni E ≤ maxBuildLevel (predvolene 5, Y ≤ 4.25),
///   • nie je už obsadený inou budovou / mestom ani tilom IndicatrixAPI.
///
/// DVE FÁZY MODELU BUDOVY:
///   1. Bez prefabu (CityBuildingLibrary slot = null) → primitíva (box, šírka =
///      1 tile, výška podľa tieru, farba podľa typu).
///   2. S prefabom → použije sa prefab (primitíva sa negeneruje).
///
/// POPIS (label) mesta: plávajúci 3D nápis nad stredom regiónu, výška laditeľná,
/// názov náhodne z prepisovateľného zoznamu (cityNames / default WordList).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class CityManager : MonoBehaviour
{
    public static CityManager instance;

    // =====================================================================
    // PARAMETRE (laditeľné v Inspectore)
    // =====================================================================

    [Header("Počet a veľkosť miest")]
    [Tooltip("0 = pri každom spustení iné náhodné mestá; iná hodnota = opakovateľné.")]
    public int citySeed = 0;

    [Tooltip("Minimálny počet miest (zadanie: 25).")]
    public int minCities = 25;
    [Tooltip("Maximálny počet miest (zadanie: 30).")]
    public int maxCities = 30;

    [Tooltip("Min. rozmer regiónu mesta v tiloch (zadanie: 10).")]
    public int minRegionSize = 10;
    [Tooltip("Max. rozmer regiónu mesta v tiloch (zadanie: 15).")]
    public int maxRegionSize = 15;

    [Tooltip("Min. počet prázdnych tilov medzi regiónmi dvoch miest.")]
    public int cityRegionSpacing = 1;

    [Tooltip("Koľkokrát skúsiť nájsť voľné miesto pre jedno mesto, než ho vynecháme.")]
    public int maxPlacementAttempts = 80;

    [Header("Hustota a typy budov")]
    [Tooltip("Podiel tilov regiónu zastavaných budovami (zadanie: 0.30 = 30 %).")]
    [Range(0.05f, 1f)]
    public float buildingDensity = 0.30f;

    [Tooltip("Váhy výberu typu mesta [Small, Medium, Big]. Nemusí dávať súčet 1.")]
    public Vector3 townTypeWeights = new Vector3(0.35f, 0.40f, 0.25f);

    [Header("Umiestnenie na terén (úrovne E)")]
    [Tooltip("Najnižšia povolená úroveň terénu pre budovu (E). 0 = rovina, NIE voda.")]
    public int minBuildLevel = 0;
    [Tooltip("Najvyššia povolená úroveň terénu pre budovu (E). Predbežne 5.")]
    public int maxBuildLevel = 5;

    [Header("Rozmery budov")]
    [Tooltip("Šírka jedného tile vo svetových jednotkách (terén = 1).")]
    public float tileWorldSize = 1f;
    [Tooltip("Pôdorys budovy ako podiel tile (1 = celý tile; <1 = jemná medzera medzi budovami).")]
    [Range(0.5f, 1f)]
    public float buildingFootprintScale = 1f;
    [Tooltip("Výška NÍZKEJ budovy = násobok šírky tile (zadanie: 1×).")]
    public float lowHeightMultiplier = 1f;
    [Tooltip("Výška STREDNEJ budovy = násobok šírky tile (zadanie: 2×).")]
    public float mediumHeightMultiplier = 2f;
    [Tooltip("Výška VYSOKEJ budovy = násobok šírky tile (zadanie: 4×).")]
    public float highHeightMultiplier = 4f;
    [Tooltip("Ponechať budovám kolíznu zložku (collider). Vyp. = nebráni raycastu terénu.")]
    public bool keepBuildingColliders = false;

    [Header("Vzhľad primitívnych budov")]
    [Tooltip("Použiť procedurálny shader RetroCity/Building: poschodia, okná, " +
             "rímsy a strechy. Vypnuté = pôvodný jednofarebný kváder. " +
             "Farba z paliet nižšie ostáva v oboch prípadoch.")]
    public bool useProceduralBuildingShader = true;

    [Header("Natočenie budov (izometrická kamera)")]
    [Tooltip("Natočiť 3D modely (prefaby) budov čelom ku kamere. Vypnuté = prefab " +
             "si ponechá pôvodnú rotáciu tak, ako je uložená v prefabe.")]
    public bool orientBuildingsToCamera = true;
    [Tooltip("Náhodne striedať dve možné pravouhlé natočenia ku kamere " +
             "(\"vertikálne\" / \"horizontálne\"). Vypnuté = všetky budovy rovnako.")]
    public bool randomizeBuildingFacing = true;
    [Tooltip("Korekcia v stupňoch, ak model v prefabe nemá \"čelo\" (priečelie) v smere " +
             "svojej lokálnej osi +Z. Typicky 0 / 90 / 180 / 270.")]
    public float buildingFacingOffsetY = 0f;

    [Header("Aglomerácia (rozloženie výšok v regióne)")]
    [Tooltip("Big Town: podiel polomeru (0..1) so VYSOKÝMI budovami v strede.")]
    [Range(0f, 1f)] public float bigHighCoreFraction = 0.34f;
    [Tooltip("Big Town: podiel polomeru (0..1), pod ktorým sú aspoň STREDNÉ budovy.")]
    [Range(0f, 1f)] public float bigMediumFraction = 0.67f;
    [Tooltip("Medium Town: podiel polomeru (0..1) so STREDNÝMI budovami v strede.")]
    [Range(0f, 1f)] public float mediumCoreFraction = 0.55f;
    [Tooltip("Náhodné rozkmitanie hraníc pásiem (0 = ostré sústredné prstence).")]
    [Range(0f, 0.4f)] public float agglomerationJitter = 0.12f;

    [Header("Medzery medzi budovami")]
    [Tooltip("Pravdepodobnosť vynechania tile (medzera) v STREDE mesta.")]
    [Range(0f, 1f)] public float centerGapChance = 0.0f;
    [Tooltip("Pravdepodobnosť vynechania tile (medzera) na OKRAJI mesta.")]
    [Range(0f, 1f)] public float edgeGapChance = 0.35f;

    [Header("Popis (label) mesta")]
    [Tooltip("Svetová výška, v ktorej sa zobrazí nápis mesta (zadanie napr. 10).")]
    public float labelHeight = 10f;
    [Tooltip("Ak zapnuté, labelHeight sa pripočíta k výške terénu v strede mesta " +
             "(inak je labelHeight absolútna Y).")]
    public bool labelHeightRelativeToTerrain = false;
    [Tooltip("Veľkosť písma názvu mesta.")]
    public float labelFontSize = 6f;
    [Tooltip("Uniformná mierka labelu.")]
    public float labelScale = 1f;
    public Color labelTextColor = Color.white;
    public Color labelBackgroundColor = Color.black;
    public Color labelOutlineColor = Color.black;
    public Vector2 labelBackgroundPadding = new Vector2(1.5f, 0.5f);

    [Header("Farby primitív (per typ; 5 typov na sadu)")]
    [Tooltip("5 farieb pre NÍZKE budovy (Low).")]
    public Color[] lowColors = {
        new Color(0.60f, 0.78f, 0.55f), new Color(0.55f, 0.70f, 0.45f),
        new Color(0.70f, 0.80f, 0.50f), new Color(0.50f, 0.72f, 0.60f),
        new Color(0.65f, 0.75f, 0.42f)
    };
    [Tooltip("5 farieb pre STREDNÉ budovy (Medium).")]
    public Color[] mediumColors = {
        new Color(0.85f, 0.72f, 0.45f), new Color(0.80f, 0.65f, 0.40f),
        new Color(0.88f, 0.78f, 0.52f), new Color(0.78f, 0.60f, 0.38f),
        new Color(0.82f, 0.70f, 0.48f)
    };
    [Tooltip("5 farieb pre VYSOKÉ budovy (High).")]
    public Color[] highColors = {
        new Color(0.55f, 0.62f, 0.85f), new Color(0.48f, 0.55f, 0.80f),
        new Color(0.62f, 0.68f, 0.90f), new Color(0.50f, 0.58f, 0.78f),
        new Color(0.58f, 0.64f, 0.88f)
    };

    [Header("Zoznam názvov miest (WordList – prepisovateľný)")]
    [Tooltip("Ak prázdne, použije sa vstavaný DefaultCityNames. Inak sa berie odtiaľto.")]
    public List<string> cityNames = new List<string>();

    // =====================================================================
    // VEREJNÝ MODEL DÁT
    // =====================================================================

    public enum CityType { SmallTown = 0, MediumTown = 1, BigTown = 2 }

    /// <summary>
    /// METADÁTA JEDNEJ BUDOVY pre SAVE/LOAD. Samotný GameObject (kváder alebo
    /// inštancia prefabu) sa NEUKLADÁ – do save streamu ide len "recept", z
    /// ktorého sa budova pri Load znovu postaví úplne rovnakou cestou ako pri
    /// generovaní (BuildBuilding → prefab z CityBuildingLibrary, alebo primitíva).
    ///
    /// tier+variant určujú, KTORÝ slot v CityBuildingLibrary sa použije:
    ///   • ak preň existuje prefab (3D model je v projekte priradený) → model,
    ///   • ak je slot prázdny (null)                                  → primitíva.
    /// Tým je splnené pravidlo "model sa uloží/nahrá, len ak je v projekte
    /// uploadnutý; inak ostane primitíva" – rovnaký princíp ako pri vlakoch,
    /// vozidlách a továrňach (ukladá sa typ, nie samotná mesh/asset).
    /// (x,z) je tile, surfaceY sa pri Load dopočíta z (už načítaného) terénu.
    /// </summary>
    public struct BuildingRecord
    {
        public CityBuildingLibrary.BuildingTier tier;
        public int variant;
        public int x;
        public int z;

        public BuildingRecord(CityBuildingLibrary.BuildingTier tier, int variant, int x, int z)
        {
            this.tier = tier;
            this.variant = variant;
            this.x = x;
            this.z = z;
        }
    }

    /// <summary>Jedno vygenerované mesto (región + typ + budovy + label).</summary>
    public class City
    {
        public string name;
        public CityType type;
        public RectInt region;                 // x,z = ľavý-dolný roh; size = W×D
        public readonly List<GameObject> buildings = new List<GameObject>();
        public CityLabel label;
        public GameObject root;                // rodič pre všetky objekty mesta

        // "Recept" každej budovy (tier+variant+pozícia) pre SAVE/LOAD. Plní sa
        // súbežne s `buildings` (1:1 poradie), aby sa dali mestá verne uložiť
        // a znovu postaviť bez náhody.
        public readonly List<BuildingRecord> buildingRecords = new List<BuildingRecord>();

        // Obsadenosť prípon staníc (Wordlist). Index = poradie prípony; true =
        // už použitá, false = voľná. Pri demolácii stanice sa prípona uvoľní.
        public readonly bool[] suffixUsed = new bool[MaxStationsPerCity];

        /// <summary>Stred mesta (svetové X/Z) – určuje teritórium (Voronoi).</summary>
        public Vector2 Center =>
            new Vector2(region.xMin + region.width * 0.5f, region.yMin + region.height * 0.5f);
    }

    public readonly List<City> Cities = new List<City>();

    // Globálna obsadenosť tilov mestskými budovami (kľúč = tile [x,z]).
    private readonly HashSet<Vector2Int> occupiedTiles = new HashSet<Vector2Int>();

    // Tile → GameObject budovy (1:1 s occupiedTiles). Slúži na dotaz výšky
    // budovy (TryGetBuildingTopY) – napr. RailCrossingSystem overuje, či
    // budova pod plánovaným mostom neprevyšuje mostovku.
    private readonly Dictionary<Vector2Int, GameObject> buildingByTile = new Dictionary<Vector2Int, GameObject>();
    // Už umiestnené regióny (na zabránenie prekryvu miest).
    private readonly List<RectInt> placedRegions = new List<RectInt>();

    private System.Random rng;

    // Dve pravouhlé natočenia (yaw), pri ktorých budova stojí čelom ku kamere.
    // Odvodené raz z rotácie hlavnej (fixnej izometrickej) kamery.
    private bool facingYawsResolved;
    private float facingYawA;   // pri izometrii 45° → 180° (čelo do -Z, "vertikálne")
    private float facingYawB;   // pri izometrii 45° → 270° (čelo do -X, "horizontálne")

    // Zdieľané materiály primitív (max 15 = 3 tiers × 5 typov), kľúč = tier*100+variant.
    private readonly Dictionary<int, Material> primitiveMaterials = new Dictionary<int, Material>();
    private Material basePrimitiveMaterial;

    // =====================================================================
    // VSTAVANÝ ZOZNAM NÁZVOV (default WordList) – prepisovateľný cez cityNames.
    // =====================================================================
    private static readonly string[] DefaultCityNames =
    {
        "Oakridge", "Stoneford", "Pinehaven", "Westmore", "Northwick", "Ashbrook", "Riverholt",
        "Goldmore", "Hillford", "Ironbrook", "Greenfall", "Foxhaven", "Brightmoor", "Woodcrest",
        "Clearford", "Deepfield", "Redbrook", "Longmore", "Easthaven", "Southridge", "Windford",
        "Maplecross", "Silverbrook", "Highmead", "Bridgeford", "Millhaven", "Ravenfield", "Elmstead",
        "Kingsmore", "Fairbrook", "Stormhill", "Whiteford", "Amberfield", "Blackmore"
    };

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    void Awake()
    {
        instance = this;
    }

    // True, ak boli mestá načítané zo save (LoadSave). Zabráni tomu, aby ešte
    // bežiaca štartová coroutine GenerateWhenReady prepísala načítané mestá
    // novým náhodným generovaním (race medzi Load a oneskoreným generovaním).
    private bool _loadedFromSave = false;

    void Start()
    {
        // Terén sa generuje v TerrainManager.Start(); IndicatrixAPI sa inicializuje
        // tiež v Start(). Počkáme, kým budú obe pripravené, a až potom staviame mestá.
        StartCoroutine(GenerateWhenReady());
    }

    private IEnumerator GenerateWhenReady()
    {
        // 1) Počkaj na hotový terén (coordsF).
        while (TerrainManager.instance == null
               || TerrainManager.instance.coordsF == null
               || TerrainManager.instance.coordsF.Length == 0)
        {
            yield return null;
        }

        // 2) Počkaj, kým je IndicatrixAPI BEZPEČNE dotazovateľný. POZOR: API si
        //    nastaví instance v Awake, ale jeho tileGrid sa inicializuje až v
        //    Start(). Medzitým GetTileByIndexAny hodí NullReferenceException
        //    (číta tileGrid[x,z] bez null-checku). Ak by sme generovali v tomto
        //    okne, IsBuildableTile by spadlo a per-city try/catch by zahodil
        //    VŠETKY mestá → "občas sa pri štarte mestá nevygenerujú; po reštarte
        //    OK" (race podľa Script Execution Order). Preto čakáme na pripravenosť.
        while (!IndicatrixReadyOrAbsent())
            yield return null;

        // 3) Ešte 1 frame, aby dobehli zvyšné Start()/init v scéne.
        yield return null;

        // Ak medzitým prebehol Load (mestá sú už načítané zo save), NEgeneruj –
        // inak by sme prepísali načítaný stav novými náhodnými mestami.
        if (_loadedFromSave) yield break;

        GenerateCities();

        // 4) POISTKA: ak sa (z akéhokoľvek dôvodu) neumiestnilo nič, skús to
        //    ešte raz po krátkej pauze – samoopravná ochrana proti štartovým
        //    race-om, aby hráč nemusel reštartovať hru.
        if (Cities.Count == 0 && !_loadedFromSave)
        {
            yield return new WaitForSeconds(0.25f);
            Debug.LogWarning("[CityManager] Prvé generovanie nedalo žiadne mesto – " +
                             "skúšam ešte raz (auto-retry).");
            if (!_loadedFromSave) GenerateCities();
        }
    }

    /// <summary>
    /// True, ak IndicatrixAPI buď nie je v scéne (vtedy IsBuildableTile jeho
    /// kontrolu bezpečne preskočí), alebo už je pripravený (tileGrid
    /// inicializovaný – probe GetTileByIndexAny nehodí výnimku).
    /// </summary>
    private static bool IndicatrixReadyOrAbsent()
    {
        var api = IndicatrixAPI.instance;
        if (api == null) return true;
        try
        {
            api.GetTileByIndexAny(0, 0); // probe – ak tileGrid == null, hodí výnimku
            return true;
        }
        catch
        {
            return false; // instance je, ale grid ešte nie je hotový
        }
    }

    // =====================================================================
    // HLAVNÁ GENERÁCIA
    // =====================================================================

    /// <summary>
    /// Vyčistí prípadné staré mestá a vygeneruje novú sadu miest. Dá sa volať
    /// aj ručne (napr. z kontextového menu alebo po novom teréne).
    /// </summary>
    [ContextMenu("Regenerate Cities")]
    public void GenerateCities()
    {
        if (TerrainManager.instance == null || TerrainManager.instance.coordsF == null)
        {
            Debug.LogWarning("[CityManager] Terén ešte nie je pripravený – generácia preskočená.");
            return;
        }

        ClearCities();

        // Seed: 0 = nový náhodný pri každom spustení.
        int seed = (citySeed == 0) ? Environment.TickCount : citySeed;
        rng = new System.Random(seed);

        int terrainW = TerrainManager.instance.terrainWidth;
        // Tile indexy musia ostať v platnom rozsahu terénu aj gridu IndicatrixAPI.
        int maxTile = terrainW; // tile (x,z) používa vertexy do x+1,z+1 ≤ terrainW

        int targetCount = RandRange(minCities, maxCities + 1);

        var nameSource = BuildNamePool();
        int nameCursor = 0;

        // ROVNOMERNÉ ROZMIESTNENIE (jittered grid / stratified sampling):
        // Mapu rozdelíme na mriežku buniek (cols×rows ≥ targetCount) a do každej
        // zvolenej bunky umiestnime 1 mesto na náhodnej (jittered) pozícii v
        // rámci bunky. Tým je rozmiestnenie rovnomerné po CELEJ mape a regióny
        // miest sa NEPREKRÝVAJÚ (každý je uzavretý vo svojej bunke).
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(targetCount)));
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)targetCount / cols));
        float cellW = (float)maxTile / cols;
        float cellH = (float)maxTile / rows;

        // Zoznam buniek, zamiešaný – z neho vyberieme prvých targetCount.
        var cells = new List<Vector2Int>(cols * rows);
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
                cells.Add(new Vector2Int(cx, cz));
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        int placed = 0;
        for (int k = 0; k < cells.Count && placed < targetCount; k++)
        {
            if (!TryPlaceCityInCell(cells[k], cellW, cellH, maxTile, out RectInt region))
                continue;

            CityType type = PickCityType();
            string cityName = (nameSource.Count > 0)
                ? nameSource[nameCursor++ % nameSource.Count]
                : "City " + (placed + 1);

            City city = BuildCity(region, type, cityName);
            Cities.Add(city);
            placedRegions.Add(region);
            placed++;
        }

        Debug.Log($"[CityManager] Vygenerovaných miest: {placed}/{targetCount} " +
                  $"(seed={seed}, mriežka {cols}×{rows}).");
    }

    /// <summary>Zničí všetky vygenerované mestá a vyčistí obsadenosť.</summary>
    public void ClearCities()
    {
        foreach (var city in Cities)
            if (city != null && city.root != null) Destroy(city.root);

        Cities.Clear();
        occupiedTiles.Clear();
        buildingByTile.Clear();
        placedRegions.Clear();
        stationAssignments.Clear();
    }

    // =====================================================================
    // VÝBER REGIÓNU MESTA
    // =====================================================================

    /// <summary>
    /// Umiestni jedno mesto do zadanej bunky mriežky. Región (W×D ∈
    /// [minRegionSize, maxRegionSize]) sa náhodne (jittered) posadí DO bunky tak,
    /// aby celý ležal v jej hraniciach (s odstupom cityRegionSpacing). Keďže
    /// bunky sú disjunktné a región je vždy vnútri svojej bunky, regióny rôznych
    /// miest sa NIKDY neprekryjú. Ak je bunka tesnejšia než región, región sa
    /// vycentruje (núdzový prípad – pri 256×256 a ~27 mestách nenastáva).
    /// </summary>
    private bool TryPlaceCityInCell(Vector2Int cell, float cellW, float cellH,
                                    int maxTile, out RectInt region)
    {
        region = default;

        int w = RandRange(minRegionSize, maxRegionSize + 1);
        int d = RandRange(minRegionSize, maxRegionSize + 1);
        if (w >= maxTile || d >= maxTile) return false;

        int cellLeft = Mathf.FloorToInt(cell.x * cellW);
        int cellRight = Mathf.FloorToInt((cell.x + 1) * cellW);
        int cellBottom = Mathf.FloorToInt(cell.y * cellH);
        int cellTop = Mathf.FloorToInt((cell.y + 1) * cellH);

        int margin = Mathf.Max(0, cityRegionSpacing);

        int minOx = Mathf.Max(cellLeft + margin, 0);
        int minOz = Mathf.Max(cellBottom + margin, 0);
        int maxOx = Mathf.Min(cellRight - margin - w, maxTile - w);
        int maxOz = Mathf.Min(cellTop - margin - d, maxTile - d);

        int ox, oz;
        if (maxOx < minOx || maxOz < minOz)
        {
            // Bunka je tesná – región vycentruj do nej (orezané na mapu).
            ox = Mathf.Clamp(cellLeft + (cellRight - cellLeft - w) / 2, 0, Mathf.Max(0, maxTile - w));
            oz = Mathf.Clamp(cellBottom + (cellTop - cellBottom - d) / 2, 0, Mathf.Max(0, maxTile - d));
        }
        else
        {
            ox = RandRange(minOx, maxOx + 1);
            oz = RandRange(minOz, maxOz + 1);
        }

        region = new RectInt(ox, oz, w, d);
        return true;
    }

    private CityType PickCityType()
    {
        float wS = Mathf.Max(0f, townTypeWeights.x);
        float wM = Mathf.Max(0f, townTypeWeights.y);
        float wB = Mathf.Max(0f, townTypeWeights.z);
        float sum = wS + wM + wB;
        if (sum <= 0f) return (CityType)RandRange(0, 3);

        float r = (float)rng.NextDouble() * sum;
        if (r < wS) return CityType.SmallTown;
        if (r < wS + wM) return CityType.MediumTown;
        return CityType.BigTown;
    }

    // =====================================================================
    // VÝSTAVBA MESTA
    // =====================================================================

    private City BuildCity(RectInt region, CityType type, string cityName)
    {
        var city = new City { name = cityName, type = type, region = region };
        city.root = new GameObject($"City_{cityName}");
        city.root.transform.SetParent(this.transform, false);

        // 1) Zozbieraj zastaviteľné tily v regióne + ich normalizovanú vzdialenosť
        //    od stredu (Chebyshev v normalizovaných osiach → obdĺžnikové prstence).
        float halfW = region.width * 0.5f;
        float halfD = region.height * 0.5f;
        float centerX = region.xMin + halfW;
        float centerZ = region.yMin + halfD;

        var candidates = new List<Candidate>();
        for (int x = region.xMin; x < region.xMax; x++)
        {
            for (int z = region.yMin; z < region.yMax; z++)
            {
                if (!IsBuildableTile(x, z, out float surfaceY))
                    continue;

                float dx = (x + 0.5f - centerX) / Mathf.Max(0.0001f, halfW);
                float dz = (z + 0.5f - centerZ) / Mathf.Max(0.0001f, halfD);
                float dist = Mathf.Clamp01(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)));

                // Jitter vypočítaný RAZ a uložený do sortKey – sort tak ostáva
                // deterministický a konzistentný (a == a, anti-symetria). Pôvodný
                // dist zostáva čistý pre gap/tier logiku nižšie.
                float sortKey = dist + (float)(rng.NextDouble() - 0.5) * 0.02f;

                candidates.Add(new Candidate { x = x, z = z, surfaceY = surfaceY, dist = dist, sortKey = sortKey });
            }
        }

        // 2) Zoraď od stredu von (centrum sa zaplní husto). Triedi sa podľa
        //    VOPRED vypočítaného sortKey (dist + jednorazový jitter) – komparátor
        //    je tým deterministický a konzistentný. (Predtým sa jitter losoval
        //    vnútri Compare → porušené striktné usporiadanie → občasná
        //    ArgumentException "IComparer returns inconsistent results".)
        candidates.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

        // 3) Cieľový počet budov = ~30 % z tilov regiónu (alikvotne k veľkosti).
        int regionTiles = region.width * region.height;
        int targetBuildings = Mathf.RoundToInt(regionTiles * buildingDensity);
        targetBuildings = Mathf.Min(targetBuildings, candidates.Count);

        // 4) PASS 1 – plníme od stredu, s občasnou medzerou (viac k okraju).
        var chosen = new List<Candidate>();
        var skipped = new List<Candidate>();
        foreach (var cand in candidates)
        {
            if (chosen.Count >= targetBuildings) { skipped.Add(cand); continue; }

            float gapChance = Mathf.Lerp(centerGapChance, edgeGapChance, cand.dist);
            if (rng.NextDouble() < gapChance) { skipped.Add(cand); continue; }

            chosen.Add(cand);
        }

        // 5) PASS 2 – doplň na cieľový počet zo zvyšných (preskočených) tilov.
        for (int i = 0; i < skipped.Count && chosen.Count < targetBuildings; i++)
            chosen.Add(skipped[i]);

        // 6) Prideľ každej zvolenej pozícii tier (podľa aglomerácie) a typ (0..4),
        //    so zaručenou variabilitou ≥ 3 rôzne typy.
        AssignAndBuild(city, chosen);

        // 7) Label mesta nad stredom regiónu.
        CreateCityLabel(city, centerX, centerZ);

        return city;
    }

    private struct Candidate { public int x, z; public float surfaceY, dist, sortKey; }

    /// <summary>
    /// Pre každú zvolenú pozíciu určí tier (Low/Medium/High) podľa typu mesta a
    /// vzdialenosti od stredu, vyberie typ budovy (0..4) a postaví ju.
    /// Zaručí, že mesto použije aspoň 3 rôzne typy (tier+variant).
    /// </summary>
    private void AssignAndBuild(City city, List<Candidate> chosen)
    {
        // Najprv si pre každú pozíciu predpočítame tier + variant.
        int n = chosen.Count;
        var tiers = new CityBuildingLibrary.BuildingTier[n];
        var variants = new int[n];

        var usedKeys = new HashSet<int>(); // kľúč = tier*100 + variant

        for (int i = 0; i < n; i++)
        {
            CityBuildingLibrary.BuildingTier tier = TierForDistance(city.type, chosen[i].dist);
            int variant = rng.Next(0, CityBuildingLibrary.VariantsPerTier);

            tiers[i] = tier;
            variants[i] = variant;
            usedKeys.Add((int)tier * 100 + variant);
        }

        // Zaručenie variability: ak je rôznych typov < 3 a máme dosť budov,
        // prepíšeme prvé pozície na odlišné varianty v rámci ich tieru.
        if (n >= 3 && usedKeys.Count < 3)
            EnsureVariety(tiers, variants, usedKeys);

        // Postavíme budovy.
        for (int i = 0; i < n; i++)
        {
            GameObject go = BuildBuilding(city, tiers[i], variants[i], chosen[i]);
            if (go != null)
            {
                city.buildings.Add(go);
                // Záznam pre SAVE/LOAD (1:1 s `buildings`).
                city.buildingRecords.Add(
                    new BuildingRecord(tiers[i], variants[i], chosen[i].x, chosen[i].z));
                occupiedTiles.Add(new Vector2Int(chosen[i].x, chosen[i].z));
                buildingByTile[new Vector2Int(chosen[i].x, chosen[i].z)] = go;
            }
        }
    }

    private void EnsureVariety(CityBuildingLibrary.BuildingTier[] tiers, int[] variants,
                              HashSet<int> usedKeys)
    {
        // Posnažíme sa dosiahnuť aspoň 3 rôzne (tier+variant) kľúče tým, že
        // upravíme varianty prvých pozícií (ich tier ponecháme – rešpektuje aglomeráciu).
        int i = 0;
        while (usedKeys.Count < 3 && i < tiers.Length)
        {
            var tier = tiers[i];
            for (int v = 0; v < CityBuildingLibrary.VariantsPerTier; v++)
            {
                int key = (int)tier * 100 + v;
                if (!usedKeys.Contains(key))
                {
                    usedKeys.Add(key);
                    variants[i] = v;
                    break;
                }
            }
            i++;
        }
    }

    /// <summary>
    /// Mapuje typ mesta + normalizovanú vzdialenosť od stredu (0=stred,1=okraj)
    /// na výškovú sadu budovy. Implementuje mestskú aglomeráciu:
    ///   • Small Town  – vždy Low.
    ///   • Medium Town – stred Medium, okraj Low.
    ///   • Big Town    – stred High, okolo Medium, okraj Low.
    /// Hranice pásiem sú jemne rozkmitané (agglomerationJitter), takže prechod
    /// nie je striktne sústredný, ale približný (ako reálna infraštruktúra).
    /// </summary>
    private CityBuildingLibrary.BuildingTier TierForDistance(CityType type, float dist)
    {
        float jitter = (float)(rng.NextDouble() - 0.5) * 2f * agglomerationJitter;
        float d = Mathf.Clamp01(dist + jitter);

        switch (type)
        {
            case CityType.SmallTown:
                return CityBuildingLibrary.BuildingTier.Low;

            case CityType.MediumTown:
                return (d < mediumCoreFraction)
                    ? CityBuildingLibrary.BuildingTier.Medium
                    : CityBuildingLibrary.BuildingTier.Low;

            case CityType.BigTown:
            default:
                if (d < bigHighCoreFraction) return CityBuildingLibrary.BuildingTier.High;
                if (d < bigMediumFraction) return CityBuildingLibrary.BuildingTier.Medium;
                return CityBuildingLibrary.BuildingTier.Low;
        }
    }

    // =====================================================================
    // STAVBA JEDNEJ BUDOVY (2 fázy: prefab vs. primitíva)
    // =====================================================================

    private GameObject BuildBuilding(City city, CityBuildingLibrary.BuildingTier tier,
                                     int variant, Candidate cand)
    {
        CityBuildingLibrary lib = ResolveBuildingLibrary();
        GameObject prefab = (lib != null) ? lib.GetBuildingPrefab(tier, variant) : null;

        GameObject go = (prefab != null)
            ? BuildPrefabBuilding(prefab, cand)             // 2. fáza – definovaný prefab
            : BuildPrimitiveBuilding(tier, variant, cand);  // 1. fáza – vygenerovaná primitíva

        if (go != null)
        {
            go.transform.SetParent(city.root.transform, true);
            if (!keepBuildingColliders)
            {
                var cols = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                    if (cols[i] != null) Destroy(cols[i]);
            }
        }
        return go;
    }

    /// <summary>
    /// 1. FÁZA – vygenerovaná primitíva: box kopírujúci šírku 1 tile, s výškou
    /// podľa tieru (Low 1×, Medium 2×, High 4× šírky tile), farbou podľa typu.
    /// Spodok boxu sadne presne na povrch (rovný tile).
    /// </summary>
    private GameObject BuildPrimitiveBuilding(CityBuildingLibrary.BuildingTier tier,
                                              int variant, Candidate cand)
    {
        float w = tileWorldSize * Mathf.Clamp01(buildingFootprintScale);
        float h = tileWorldSize * HeightMultiplier(tier);

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = $"Building_{tier}_{variant}_{cand.x}_{cand.z}";
        box.transform.localScale = new Vector3(w, h, w);
        box.transform.position = new Vector3(cand.x + 0.5f, cand.surfaceY + h * 0.5f, cand.z + 0.5f);

        var rend = box.GetComponent<Renderer>();
        if (rend != null)
            rend.sharedMaterial = GetPrimitiveMaterial(tier, variant);

        return box;
    }

    /// <summary>
    /// Vráti (a pri prvom použití vytvorí) zdieľaný materiál pre daný typ budovy.
    /// Materiál sa klonuje z DEFAULT materiálu primitívy, takže používa shader
    /// správny pre aktívny render pipeline (Built-in aj URP). Farba sa nastaví
    /// robustne (_BaseColor pre URP, _Color pre Built-in).
    /// </summary>
    private Material GetPrimitiveMaterial(CityBuildingLibrary.BuildingTier tier, int variant)
    {
        int key = (int)tier * 100 + variant;
        if (primitiveMaterials.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        Material mat = null;

        // Procedurálny shader budov (ak je v projekte) – z kvádra urobí dom.
        if (useProceduralBuildingShader)
        {
            Shader cityShader = Shader.Find("RetroCity/Building");
            if (cityShader != null)
                mat = new Material(cityShader);
        }

        if (mat == null)
        {
            if (basePrimitiveMaterial == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var r = tmp.GetComponent<Renderer>();
                basePrimitiveMaterial = (r != null) ? r.sharedMaterial : null;
                Destroy(tmp);
            }

            mat = (basePrimitiveMaterial != null)
                ? new Material(basePrimitiveMaterial)
                : new Material(Shader.Find("Standard"));
        }

        SetMaterialColor(mat, ColorFor(tier, variant));
        primitiveMaterials[key] = mat;
        return mat;
    }

    /// <summary>Nastaví hlavnú farbu materiálu naprieč pipeline-mi.</summary>
    private static void SetMaterialColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP / HDRP
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);         // Built-in
        m.color = c;                                                  // fallback
    }

    // =====================================================================
    // NATOČENIE BUDOVY VOČI IZOMETRICKEJ KAMERE
    // =====================================================================

    /// <summary>
    /// Z rotácie hlavnej (fixnej, ortografickej) kamery odvodí DVE natočenia
    /// okolo osi Y, pri ktorých budova stojí čelom ku kamere.
    ///
    /// Kamera je izometrická, rotácia (30, 45, 0) – jej vodorovný smer pohľadu
    /// je teda (+X, +Z), t.j. pozerá sa "z prednej-ľavej strany" mapy. Budova sa
    /// smie otáčať len po 90° (tile je v pôdoryse pravouhlý), takže zo štyroch
    /// možností (0/90/180/270) sú ku kamere obrátené práve tie dve, ktorých
    /// lokálna os +Z mieri proti smeru pohľadu kamery:
    ///
    ///     yaw 180° → čelo mieri do -Z  ("vertikálne" ku kamere)
    ///     yaw 270° → čelo mieri do -X  ("horizontálne" ku kamere)
    ///
    /// Zvyšné dve (0°, 90°) by kamere ukázali zadnú stenu modelu.
    /// Uhly sa neberú natvrdo – počítajú sa z reálnej rotácie kamery, takže ak
    /// sa izometria niekedy prekloní (napr. yaw 135°), výsledok ostane správny.
    /// </summary>
    private void ResolveFacingYaws()
    {
        if (facingYawsResolved) return;

        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();

        // Vodorovný smer pohľadu kamery (fallback = klasická izometria 45°).
        Vector3 fwd = (cam != null) ? cam.transform.forward : new Vector3(1f, 0f, 1f);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = new Vector3(1f, 0f, 1f);
        fwd.Normalize();

        // Uhol, pri ktorom by budova stála presne proti kamere (nie je pravouhlý).
        float toCamera = Mathf.Atan2(-fwd.x, -fwd.z) * Mathf.Rad2Deg;

        // Dva najbližšie násobky 90° okolo tohto uhla – pri izometrii sú od neho
        // vzdialené presne ±45°, takže obidva sú ku kamere rovnako "čelné".
        float snapped = Mathf.Floor(toCamera / 90f) * 90f;
        facingYawA = Mathf.Repeat(snapped, 360f);
        facingYawB = Mathf.Repeat(snapped + 90f, 360f);

        facingYawsResolved = true;
    }

    /// <summary>
    /// Vráti natočenie budovy okolo osi Y – náhodne jedno z dvoch pravouhlých
    /// natočení čelom ku kamere ("vertikálne" alebo "horizontálne").
    /// Voľba je deterministická z pozície tile, takže sa nemieša do poradia
    /// generátora <see cref="rng"/> a po regenerácii mesta z receptu (load hry)
    /// vyzerá mesto rovnako.
    /// </summary>
    private Quaternion FacingRotationFor(Candidate cand)
    {
        ResolveFacingYaws();

        bool useB = randomizeBuildingFacing && ((Hash2D(cand.x, cand.z) & 1) == 1);
        float yaw = (useB ? facingYawB : facingYawA) + buildingFacingOffsetY;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>Stabilný hash 2D súradnice – deterministický "šum" bez rng.</summary>
    private int Hash2D(int x, int z)
    {
        unchecked
        {
            int h = (x * 73856093) ^ (z * 19349663) ^ (citySeed * 83492791);
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return h & 0x7FFFFFFF;
        }
    }

    /// <summary>
    /// 2. FÁZA – definovaný prefab: použije sa tak ako je. Pôdorys sa proporčne
    /// (uniformne) prispôsobí 1 tile a spodok modelu sadne na povrch. Model sa
    /// navyše natočí čelom k izometrickej kamere (pravouhlo, náhodne V/H);
    /// proporcie modelu sa zachovajú.
    /// </summary>
    private GameObject BuildPrefabBuilding(GameObject prefab, Candidate cand)
    {
        GameObject go = Instantiate(prefab);
        go.name = $"BuildingModel_{cand.x}_{cand.z}";

        // Natočenie MUSÍ prebehnúť PRED meraním bounds – rotácia okolo Y mení
        // svetový AABB modelu, a podľa neho sa ďalej škáluje aj centruje.
        if (orientBuildingsToCamera)
            go.transform.rotation = FacingRotationFor(cand) * go.transform.rotation;

        float cx = cand.x + 0.5f;
        float cz = cand.z + 0.5f;

        // Uniformné prispôsobenie pôdorysu na 1 tile (× footprintScale).
        if (TryGetCombinedBounds(go, out Bounds b0))
        {
            float footprint = Mathf.Max(b0.size.x, b0.size.z);
            if (footprint > 1e-5f)
            {
                float target = tileWorldSize * Mathf.Clamp01(buildingFootprintScale);
                go.transform.localScale *= (target / footprint);
            }
        }

        // Re-centrovanie X/Z na stred tile + sadnutie spodku na povrch.
        if (TryGetCombinedBounds(go, out Bounds b))
        {
            Vector3 p = go.transform.position;
            p.x += cx - b.center.x;
            p.z += cz - b.center.z;
            p.y += (cand.surfaceY + 0.001f) - b.min.y;
            go.transform.position = p;
        }
        else
        {
            go.transform.position = new Vector3(cx, cand.surfaceY, cz);
        }

        return go;
    }

    private float HeightMultiplier(CityBuildingLibrary.BuildingTier tier)
    {
        switch (tier)
        {
            case CityBuildingLibrary.BuildingTier.Low: return lowHeightMultiplier;
            case CityBuildingLibrary.BuildingTier.Medium: return mediumHeightMultiplier;
            case CityBuildingLibrary.BuildingTier.High: return highHeightMultiplier;
            default: return lowHeightMultiplier;
        }
    }

    private Color ColorFor(CityBuildingLibrary.BuildingTier tier, int variant)
    {
        Color[] palette;
        switch (tier)
        {
            case CityBuildingLibrary.BuildingTier.Medium: palette = mediumColors; break;
            case CityBuildingLibrary.BuildingTier.High: palette = highColors; break;
            default: palette = lowColors; break;
        }

        if (palette == null || palette.Length == 0)
            return Color.gray;

        return palette[Mathf.Clamp(variant, 0, palette.Length - 1)];
    }

    // =====================================================================
    // LABEL MESTA
    // =====================================================================

    private void CreateCityLabel(City city, float centerX, float centerZ)
    {
        float y = labelHeight;
        if (labelHeightRelativeToTerrain)
            y = TerrainCenterHeight(city.region) + labelHeight;

        var labelGO = new GameObject($"CityLabel_{city.name}");
        labelGO.transform.SetParent(city.root.transform, false);

        var label = labelGO.AddComponent<CityLabel>();
        label.Initialize(city.name, new Vector3(centerX, y, centerZ),
                         labelFontSize, labelScale,
                         labelTextColor, labelBackgroundColor, labelOutlineColor,
                         labelBackgroundPadding);

        city.label = label;
    }

    private float TerrainCenterHeight(RectInt region)
    {
        int cx = Mathf.Clamp(region.xMin + region.width / 2, 0, TerrainManager.instance.terrainWidth);
        int cz = Mathf.Clamp(region.yMin + region.height / 2, 0, TerrainManager.instance.terrainWidth);
        return VertexY(cx, cz);
    }

    // =====================================================================
    // TERÉNNE DOTAZY
    // =====================================================================

    /// <summary>
    /// Vráti true, ak na tile [x,z] SMIE stáť budova:
    ///   • tile je rovný (4 rohové Y rovnaké),
    ///   • výška je v rozsahu [minBuildLevel, maxBuildLevel] (E ≥ 0, NIE voda),
    ///   • tile nie je obsadený inou budovou ani tilom IndicatrixAPI.
    /// out surfaceY = spoločná výška rovného tile (na sadnutie budovy).
    /// </summary>
    private bool IsBuildableTile(int x, int z, out float surfaceY)
    {
        surfaceY = 0f;

        var key = new Vector2Int(x, z);
        if (occupiedTiles.Contains(key))
            return false;

        // Obsadené tily iných systémov (železnica/cesta/továreň) – ak je API v scéne.
        if (IndicatrixAPI.instance != null &&
            IndicatrixAPI.instance.GetTileByIndexAny(x, z).tileID != 0)
            return false;

        // Tily POD MOSTOM (RailCrossingSystem). Hlavy mostov a tunelov sú v tile
        // gride (kontrola vyššie), ale vnútro mosta nie – tile je v gride prázdny.
        // Relevantné len pri "Regenerate Cities" počas hry, keď už mosty stoja.
        // Terén NAD tunelom ostáva zastaviteľný.
        if (CrossingSystemBase.IsBridgeSpanTileAny(x, z))   // RAIL aj ROAD mosty
            return false;

        // Rovnosť tile – 4 rohové vertexy.
        float y0 = VertexY(x, z);
        float y1 = VertexY(x, z + 1);
        float y2 = VertexY(x + 1, z + 1);
        float y3 = VertexY(x + 1, z);

        const float EPS = 0.0001f;
        bool flat = Mathf.Abs(y0 - y1) < EPS
                 && Mathf.Abs(y0 - y2) < EPS
                 && Mathf.Abs(y0 - y3) < EPS;
        if (!flat)
            return false;

        // Výškový rozsah (na úrovni E).
        float minY = LevelToHeight(minBuildLevel);
        float maxY = LevelToHeight(maxBuildLevel);
        if (y0 < minY - EPS || y0 > maxY + EPS)
            return false;

        surfaceY = y0;
        return true;
    }

    /// <summary>Výška vertexu (x,z) z TerrainManager.coordsF.</summary>
    private float VertexY(int x, int z)
    {
        int w = TerrainManager.instance.terrainWidth + 1;
        x = Mathf.Clamp(x, 0, w - 1);
        z = Mathf.Clamp(z, 0, w - 1);
        return TerrainManager.instance.coordsF[z * w + x].y;
    }

    /// <summary>
    /// Prepočet úrovne E na svetovú výku Y. Odvodené z verejných konštánt
    /// TerrainManager (robustné voči budúcim zmenám škály):
    ///   Y = MinTerrainHeight + (E - MinElevationLevel) * step,
    /// kde step = (MaxTerrainHeight - MinTerrainHeight) / (MaxElev - MinElev).
    /// </summary>
    private float LevelToHeight(int level)
    {
        float minY = TerrainManager.MinTerrainHeight;   // E=-1 → 2.75
        float maxY = TerrainManager.MaxTerrainHeight;   // E=10 → 5.50
        int minE = TerrainManager.MinElevationLevel;    // -1
        int maxE = TerrainManager.MaxElevationLevel;    // 10
        float step = (maxY - minY) / Mathf.Max(1, (maxE - minE));
        return minY + (level - minE) * step;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private CityBuildingLibrary ResolveBuildingLibrary()
    {
        if (CityBuildingLibrary.instance != null)
            return CityBuildingLibrary.instance;
        return FindAnyObjectByType<CityBuildingLibrary>();
    }

    private static bool TryGetCombinedBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    /// <summary>Zostaví zamiešaný zoznam názvov (override cityNames, inak default).</summary>
    private List<string> BuildNamePool()
    {
        var src = (cityNames != null && cityNames.Count > 0)
            ? new List<string>(cityNames)
            : new List<string>(DefaultCityNames);

        // Fisher–Yates zamiešanie cez náš seedovaný RNG.
        for (int i = src.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (src[i], src[j]) = (src[j], src[i]);
        }
        return src;
    }

    /// <summary>Inkluzívne-exkluzívny náhodný int [minIncl, maxExcl) cez seedovaný RNG.</summary>
    private int RandRange(int minIncl, int maxExcl)
    {
        if (maxExcl <= minIncl) return minIncl;
        return rng.Next(minIncl, maxExcl);
    }

    /// <summary>Verejný dotaz: je tile [x,z] obsadený mestskou budovou?</summary>
    public bool IsCityTile(int x, int z)
    {
        return occupiedTiles.Contains(new Vector2Int(x, z));
    }

    /// <summary>
    /// Svetová výška VRCHU budovy na tile [x,z] (max Y všetkých rendererov –
    /// funguje pre primitívu aj prefab). False, ak na tile budova nie je.
    /// Používa RailCrossingSystem: pod mostom smie stáť budova, ktorá
    /// neprevyšuje mostovku.
    /// </summary>
    public bool TryGetBuildingTopY(int x, int z, out float topY)
    {
        topY = 0f;
        if (!buildingByTile.TryGetValue(new Vector2Int(x, z), out GameObject go) || go == null)
            return false;
        if (!TryGetCombinedBounds(go, out Bounds b))
            return false;
        topY = b.max.y;
        return true;
    }

    // =====================================================================
    // NÁZVY STANÍC PODĽA MESTA (Wordlist prípon + teritórium mesta)
    // ─────────────────────────────────────────────────────────────────────
    // TERITÓRIUM MESTA = množina tilov, pre ktoré je toto mesto NAJBLIŽŠIE
    // (Voronoi podľa stredov miest). Tým je celá mapa rozdelená medzi mestá
    // BEZ prekryvu a BEZ medzier (každý tile patrí práve jednému mestu) – presne
    // ako žiada zadanie. Je to ODLIŠNÝ región od regiónu budov (ten ostáva v
    // pôvodnom stave). Teritórium môže zasahovať aj do vody (stanicu tam aj tak
    // nemožno postaviť). Pri rovnomernom rozmiestnení miest (jittered grid v
    // GenerateCities) majú teritóriá ~rovnakú veľkosť (≈ mapa/počet ≈ 50×50 pre
    // ~27 miest na 256×256).
    //
    // Názov stanice = názov mesta + prípona z Wordlistu (pevné poradie podľa
    // priority). Priraďuje sa VŽDY prvá VOĽNÁ prípona (najnižší index). Pri
    // demolácii stanice sa prípona uvoľní → znova dostupná. Max. počet staníc na
    // mesto = počet prípon (20), RAIL + ROAD spolu.
    //
    // Generické: názov sa skladá z AKTUÁLNEHO city.name, takže pri premenovaní
    // mesta v generátore sa prispôsobí aj názov stanice.
    // =====================================================================

    /// <summary>Wordlist prípon staníc – PEVNÉ poradie podľa priority (index 0 = najvyššia).</summary>
    public static readonly string[] StationSuffixes =
    {
        "Central", "North", "South", "East", "West",
        "Old Town", "New Town", "Industrial", "Harbour", "Airport",
        "Junction", "Terminal", "Market", "University", "Stadium",
        "Heights", "Riverside", "Hills", "Park", "Depot"
    };

    /// <summary>Max. počet staníc na mesto = počet prípon (20).</summary>
    public const int MaxStationsPerCity = 20;

    private sealed class StationAssignment
    {
        public City city;
        public int suffixIndex;
    }

    // Priradenie názvu konkrétnej stanici podľa jej tile [x,z].
    private readonly Dictionary<long, StationAssignment> stationAssignments
        = new Dictionary<long, StationAssignment>();

    private static long StationKey(int x, int z) => ((long)x << 32) | (uint)z;

    /// <summary>
    /// Vráti mesto, do ktorého TERITÓRIA tile [x,z] patrí – mesto s najbližším
    /// stredom (Voronoi). Null, ak nie sú žiadne mestá.
    /// </summary>
    public City GetOwningCity(int tileX, int tileZ)
    {
        if (Cities.Count == 0) return null;

        float px = tileX + 0.5f;
        float pz = tileZ + 0.5f;

        City best = null;
        float bestSqr = float.MaxValue;
        foreach (var c in Cities)
        {
            if (c == null) continue;
            Vector2 ctr = c.Center;
            float dx = ctr.x - px;
            float dz = ctr.y - pz;
            float sqr = dx * dx + dz * dz;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }
        return best;
    }

    /// <summary>
    /// True, ak na tile [x,z] SMIE vzniknúť nová stanica z hľadiska názvov –
    /// vlastniace mesto má aspoň jednu voľnú príponu (alebo tile už priradenie
    /// má). Ak nie sú žiadne mestá, povolí sa (fallback názov).
    /// </summary>
    public bool CanAssignStationName(int x, int z)
    {
        if (stationAssignments.ContainsKey(StationKey(x, z))) return true;

        City c = GetOwningCity(x, z);
        if (c == null) return true; // žiadne mestá → povoliť s fallback názvom

        return FirstFreeSuffix(c) >= 0;
    }

    /// <summary>
    /// Priradí stanici na tile [x,z] názov (mesto + prvá voľná prípona) a
    /// príponu označí ako obsadenú. Idempotentné – ak tile už priradenie má,
    /// vráti existujúci názov. Ak je mesto plné (alebo nie sú mestá), vráti
    /// fallback "Station [x, z]".
    /// </summary>
    public string AssignStationName(int x, int z)
    {
        long key = StationKey(x, z);
        if (stationAssignments.TryGetValue(key, out StationAssignment existing))
            return ComposeName(existing);

        City c = GetOwningCity(x, z);
        if (c == null) return FallbackName(x, z);

        int idx = FirstFreeSuffix(c);
        if (idx < 0) return FallbackName(x, z); // plné – nemalo by nastať (guard v GameManager)

        c.suffixUsed[idx] = true;
        var a = new StationAssignment { city = c, suffixIndex = idx };
        stationAssignments[key] = a;
        return ComposeName(a);
    }

    /// <summary>
    /// Uvoľní názov (príponu) stanice na tile [x,z] – volá sa pri demolácii.
    /// Prípona sa stane znova dostupná pre ďalšiu stanicu daného mesta.
    /// </summary>
    public void ReleaseStationName(int x, int z)
    {
        long key = StationKey(x, z);
        if (!stationAssignments.TryGetValue(key, out StationAssignment a)) return;

        if (a.city != null && a.suffixIndex >= 0 && a.suffixIndex < a.city.suffixUsed.Length)
            a.city.suffixUsed[a.suffixIndex] = false;

        stationAssignments.Remove(key);
    }

    /// <summary>
    /// ZOBRAZOVACÍ názov stanice na tile [x,z] (mesto + prípona). Ak stanica
    /// nemá priradenie, vráti interný/fallback názov "Station [x, z]". Počíta sa
    /// z aktuálneho city.name (generické voči premenovaniu mesta).
    /// </summary>
    public string GetStationDisplayName(int x, int z)
    {
        if (stationAssignments.TryGetValue(StationKey(x, z), out StationAssignment a))
            return ComposeName(a);
        return FallbackName(x, z);
    }

    private static int FirstFreeSuffix(City c)
    {
        for (int i = 0; i < c.suffixUsed.Length; i++)
            if (!c.suffixUsed[i]) return i;
        return -1;
    }

    private static string ComposeName(StationAssignment a)
    {
        if (a == null || a.city == null) return "Station";
        string suffix = (a.suffixIndex >= 0 && a.suffixIndex < StationSuffixes.Length)
            ? StationSuffixes[a.suffixIndex] : "";
        return string.IsNullOrEmpty(suffix) ? a.city.name : $"{a.city.name} {suffix}";
    }

    /// <summary>Interný/fallback názov stanice = súradnica tile (ako doteraz).</summary>
    private static string FallbackName(int x, int z) => $"Station [{x}, {z}]";

    // =====================================================================
    // SAVE / LOAD MIEST A NÁZVOV STANÍC
    // ─────────────────────────────────────────────────────────────────────
    // Mestá sa generujú NÁHODNE (seedovaný RNG), takže ich nemožno spoľahlivo
    // znovu vyrobiť len zo seedu – ručné úpravy terénu aj poradie stavania by
    // sa nemuseli zhodovať. Preto sa ukladá ÚPLNÝ "recept" každého mesta:
    //   • názov, typ, región (x,z,w,h),
    //   • zoznam budov ako (tier, variant, tileX, tileZ).
    // Pri Load sa mestá najprv zmažú (ClearCities) a potom sa každá budova
    // postaví ROVNAKOU cestou ako pri generovaní (BuildBuilding) – takže ak je
    // pre daný tier+variant v CityBuildingLibrary priradený 3D model (prefab),
    // použije sa model; ak nie, postaví sa primitíva. Samotné meshe/asset-y sa
    // do save NEUKLADAJÚ (to v Unity ani nejde) – ukladá sa len typ budovy.
    //
    // Druhý blok ukladá PRIRADENIE NÁZVOV STANÍC (mesto + prípona z Wordlistu),
    // ktoré je runtime stav (vzniká pri stavbe stanice). Bez neho by stanice po
    // Load dostali len fallback názov "Station [x,z]". Ukladá sa
    // (tileX, tileZ, index mesta, index prípony); pri Load sa po obnove miest
    // priradenia vrátia a príslušné prípony sa označia ako obsadené.
    //
    // POZN.: Volá sa z IndicatrixAPI.SaveGame/LoadGame ako súčasť jediného
    // save streamu. Poradie zápisu a čítania je ZHODNÉ.

    /// <summary>Zapíše mestá + priradenia názvov staníc do save streamu.</summary>
    public void WriteSave(System.IO.BinaryWriter bw)
    {
        // ── Mestá ──
        bw.Write(Cities.Count);
        foreach (var city in Cities)
        {
            bw.Write(city.name ?? string.Empty);
            bw.Write((int)city.type);
            bw.Write(city.region.x);
            bw.Write(city.region.y);
            bw.Write(city.region.width);
            bw.Write(city.region.height);

            bw.Write(city.buildingRecords.Count);
            foreach (var rec in city.buildingRecords)
            {
                bw.Write((int)rec.tier);
                bw.Write(rec.variant);
                bw.Write(rec.x);
                bw.Write(rec.z);
            }
        }

        // ── Priradenia názvov staníc (tile → mesto + prípona) ──
        // Index mesta = poradie v zozname Cities (rovnaké pri zápise aj čítaní).
        var cityIndex = new Dictionary<City, int>(Cities.Count);
        for (int i = 0; i < Cities.Count; i++) cityIndex[Cities[i]] = i;

        bw.Write(stationAssignments.Count);
        foreach (var kvp in stationAssignments)
        {
            long key = kvp.Key;
            int x = (int)(key >> 32);
            int z = (int)(uint)key;

            int ci = (kvp.Value != null && kvp.Value.city != null
                      && cityIndex.TryGetValue(kvp.Value.city, out int idx)) ? idx : -1;
            int suffix = (kvp.Value != null) ? kvp.Value.suffixIndex : -1;

            bw.Write(x);
            bw.Write(z);
            bw.Write(ci);
            bw.Write(suffix);
        }
    }

    /// <summary>
    /// Načíta mestá + priradenia názvov staníc zo save streamu. Najprv zmaže
    /// existujúce (náhodne vygenerované) mestá a postaví uložené, potom obnoví
    /// priradenia názvov staníc. Terén aj tile grid musia byť už načítané (Load
    /// to garantuje poradím blokov), aby budovy sadli na správnu výšku.
    /// </summary>
    public void LoadSave(System.IO.BinaryReader br)
    {
        // Zmaž náhodne vygenerované mestá (vrátane stationAssignments).
        ClearCities();

        // Ak by sme práve generovali a coroutine ešte beží, prípadný neskorší
        // GenerateCities by nám prepísal načítané. Príznak zabráni autogenerácii.
        _loadedFromSave = true;

        // ── Mestá ──
        int cityCount = br.ReadInt32();
        for (int c = 0; c < cityCount; c++)
        {
            string name = br.ReadString();
            CityType type = (CityType)br.ReadInt32();
            int rx = br.ReadInt32();
            int rz = br.ReadInt32();
            int rw = br.ReadInt32();
            int rh = br.ReadInt32();

            var city = new City
            {
                name = name,
                type = type,
                region = new RectInt(rx, rz, rw, rh)
            };
            city.root = new GameObject($"City_{name}");
            city.root.transform.SetParent(this.transform, false);

            int buildingCount = br.ReadInt32();
            for (int b = 0; b < buildingCount; b++)
            {
                var tier = (CityBuildingLibrary.BuildingTier)br.ReadInt32();
                int variant = br.ReadInt32();
                int bx = br.ReadInt32();
                int bz = br.ReadInt32();

                // surfaceY dopočítaj z (už načítaného) terénu – budovy stáli na
                // rovnom tile, takže výška ľubovoľného rohu = povrch.
                float surfaceY = VertexY(bx, bz);

                var cand = new Candidate { x = bx, z = bz, surfaceY = surfaceY, dist = 0f, sortKey = 0f };
                GameObject go = BuildBuilding(city, tier, variant, cand);
                if (go != null)
                {
                    city.buildings.Add(go);
                    city.buildingRecords.Add(new BuildingRecord(tier, variant, bx, bz));
                    occupiedTiles.Add(new Vector2Int(bx, bz));
                    buildingByTile[new Vector2Int(bx, bz)] = go;
                }
            }

            // Label nad stredom regiónu (rovnaký výpočet ako pri generovaní).
            float centerX = city.region.xMin + city.region.width * 0.5f;
            float centerZ = city.region.yMin + city.region.height * 0.5f;
            CreateCityLabel(city, centerX, centerZ);

            placedRegions.Add(city.region);
            Cities.Add(city);
        }

        // ── Priradenia názvov staníc ──
        int assignCount = br.ReadInt32();
        for (int i = 0; i < assignCount; i++)
        {
            int x = br.ReadInt32();
            int z = br.ReadInt32();
            int ci = br.ReadInt32();
            int suffix = br.ReadInt32();

            if (ci < 0 || ci >= Cities.Count) continue;
            City city = Cities[ci];
            if (suffix < 0 || suffix >= city.suffixUsed.Length) continue;

            city.suffixUsed[suffix] = true;
            stationAssignments[StationKey(x, z)] =
                new StationAssignment { city = city, suffixIndex = suffix };
        }

        Debug.Log($"[CityManager] Načítaných {Cities.Count} miest zo save.");
    }
}