using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EnvironmentManager
/// ─────────────────────────────────────────────────────────────────────────
/// NÁHODNÉ GENEROVANIE ENVIRONMENTÁLNYCH PREFABOV na tile mapu – priama
/// analógia k <see cref="CityManager"/> (ten generuje mestá), len pre prírodné
/// objekty:
///
///   • STROMY            – 1 tile, zhlukovanie do lesov (Perlin/fBm),
///                         bonus pri brehoch vodných tokov
///   • KAMENE            – 1 tile, vlastné pole hustoty + mierna preferencia
///                         vyššie položeného terénu
///   • LANDING LOCATIONS – 4 tile (footprint 2×2), 7 presne definovaných typov,
///                         každý s vlastným indexom náhodnosti 0–1
///
/// ─────────────────────────────────────────────────────────────────────────
/// PORADIE (podľa zadania):
///   1. terén (TerrainManager)  →  2. mestá (CityManager)  →  3. TENTO generátor
///   V rámci generátora: najprv STROMY, potom KAMENE, potom LANDING LOCATIONS.
///   Každý objekt dostane iný tile – obsadenosť je vedená v jednom spoločnom
///   slovníku, takže sa navzájom nikdy neprekryjú.
///
/// PODMIENKY UMIESTNENIA (spoločné pre všetky tri skupiny):
///   • tile je VOĽNÝ – prázdny v tileGrid (IndicatrixAPI), bez mestskej budovy
///     (CityManager.IsCityTile) a bez iného environment objektu,
///   • tile je VODOROVNÝ – 4 rohové vertexy majú rovnaké Y,
///   • tile NIE JE vodná hladina – žiadny roh na/pod TerrainManager.MinTerrainHeight.
///   Pri landing location musia tieto podmienky platiť pre VŠETKY tily footprintu
///   a navyše musia byť v rovnakej výške (rovná plošina).
///
/// OBSADENOSŤ MIMO tileGrid (rovnaký princíp ako mestské budovy):
///   Environment objekty sa ZÁMERNE nezapisujú do IndicatrixAPI.tileGrid – tak
///   ako mestské budovy. Vďaka tomu ich nevidí TrainSystem/VehicleSystem A*,
///   FactoryRegistry ani žiadny existujúci systém a pôvodná funkcionalita hry
///   zostáva nedotknutá. Obsadenosť sa vedie tu (occupiedTiles) a GameManager
///   sa na ňu pýta cez <see cref="IsEnvironmentTile"/> presne tak, ako sa dnes
///   pýta CityManager.IsCityTile.
///
/// DEMOLÁCIA:
///   • STROM a KAMEŇ – hráč ich smie odstrániť cez RAIL/ROAD Demolish. Je to
///     ZADARMO (žiadna cena ani refund – pozri ConstructionCosts).
///   • LANDING LOCATION – fixná, odstrániť sa NEDÁ (guard v GameManager).
///
/// SAVE / LOAD:
///   Ukladá sa zoznam objektov (typ, variant, pozícia, footprint, náhodný yaw).
///   Prefaby sa neukladajú ako asset – pri Load sa dohľadajú z knižnice, presne
///   ako to robí CityManager s CityBuildingLibrary. Volá sa z IndicatrixAPI.
///   SaveGame/LoadGame ako posledný blok streamu.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class EnvironmentManager : MonoBehaviour
{
    public static EnvironmentManager instance;

    /// <summary>Veľkosť tile gridu – musí sedieť s IndicatrixAPI.GRID_SIZE (256).</summary>
    public const int TileGridSize = 256;

    // =====================================================================
    // NASTAVENIA – SPOLOČNÉ
    // =====================================================================

    [Header("SEED")]
    [Tooltip("Seed generovania. 0 = pri každom spustení nový náhodný.")]
    public int environmentSeed = 0;

    [Header("ČASOVANIE (generuje sa AŽ PO teréne a PO mestách)")]
    [Tooltip("Maximálne čakanie na dokončenie generovania miest (sekundy). " +
             "Po uplynutí sa generuje aj tak – poistka proti zaseknutiu.")]
    public float maxWaitForCitiesSeconds = 15f;

    [Tooltip("Dodatočná pauza po dokončení miest, kým sa spustí generovanie " +
             "prírody (istota, že CityManager dobehol aj svoj auto-retry).")]
    public float postCityDelaySeconds = 0.5f;

    [Header("SPOLOČNÉ – umiestňovanie")]
    [Tooltip("Ochranné pásmo pri okraji mapy (v tiloch), kam sa negeneruje nič.")]
    [Min(0)] public int borderMargin = 2;

    [Tooltip("Veľkosť jedného tile vo svetových jednotkách (rovnako ako CityManager).")]
    public float tileWorldSize = 1f;

    [Tooltip("Ponechať kolízne komponenty na vygenerovaných modeloch? " +
             "VYPNUTÉ (odporúčané) – model potom neblokuje raycast terénu, " +
             "takže klik/guardy fungujú rovnako ako pri mestských budovách.")]
    public bool keepColliders = false;

    // =====================================================================
    // NASTAVENIA – STROMY
    // =====================================================================

    [Header("STROMY – hustota a zhlukovanie")]
    [Tooltip("INDEX HUSTOTY stromov: 0 = žiadne stromy, 1 = strom takmer na " +
             "každom voľnom tile mapy.")]
    [Range(0f, 1f)] public float treeDensity = 0.35f;

    [Tooltip("Veľkosť zhlukov (lesov). Menšie číslo = väčšie súvislé lesy, " +
             "väčšie číslo = drobnejšie háje.")]
    [Range(0.005f, 0.2f)] public float treeClusterScale = 0.035f;

    [Tooltip("Kontrast zhlukov: vyššia hodnota = ostrejší prechod medzi hustým " +
             "lesom a takmer prázdnou krajinou (prirodzenejšie rozloženie).")]
    [Range(1f, 6f)] public float treeClusterContrast = 2.6f;

    [Tooltip("Šanca, že na danom mieste \"vyskočí\" strom inej typologickej " +
             "skupiny, než je v zhluku prevládajúca (zmiešaný les).")]
    [Range(0f, 1f)] public float treeSpeciesMixChance = 0.2f;

    [Tooltip("Veľkosť oblastí s prevládajúcou skupinou drevín (typológia lesa).")]
    [Range(0.002f, 0.1f)] public float treeSpeciesScale = 0.012f;

    [Tooltip("Maximálny počet stromov (výkonnostná poistka pri vysokej hustote).")]
    [Min(0)] public int maxTrees = 12000;

    [Header("STROMY – brehová vegetácia pri vode")]
    [Tooltip("Prídavná pravdepodobnosť stromu v páse okolo vodnej hladiny " +
             "(0 = vypnuté). Simuluje brehovú vegetáciu okolo tokov a jazier.")]
    [Range(0f, 1f)] public float waterEdgeTreeBonus = 0.55f;

    [Tooltip("Šírka brehového pásu v tiloch (vzdialenosť od vodného tile).")]
    [Range(1, 6)] public int waterEdgeRadius = 3;

    [Tooltip("Podiel vodných tokov, pri ktorých sa brehová vegetácia uplatní " +
             "(0.4 = zhruba 40 % brehov je zarastených, zvyšok holý). " +
             "Zabezpečuje, že to nevyzerá schematicky pri každej vode.")]
    [Range(0f, 1f)] public float riparianCoverage = 0.45f;

    // =====================================================================
    // NASTAVENIA – KAMENE
    // =====================================================================

    [Header("KAMENE – hustota a zhlukovanie")]
    [Tooltip("INDEX HUSTOTY kameňov: 0 = žiadne kamene, 1 = kameň takmer na " +
             "každom voľnom tile mapy.")]
    [Range(0f, 1f)] public float rockDensity = 0.12f;

    [Tooltip("Veľkosť kamenných polí. Väčšie číslo = drobnejšie roztrúsené skupinky.")]
    [Range(0.005f, 0.3f)] public float rockClusterScale = 0.08f;

    [Tooltip("Kontrast zhlukov kameňov (vyššie = ostrejšie ohraničené kamenné polia).")]
    [Range(1f, 6f)] public float rockClusterContrast = 3.2f;

    [Tooltip("Preferencia vyššie položeného terénu (1 = žiadna, 3 = v horách " +
             "trojnásobne viac kameňov než v nížine).")]
    [Range(1f, 4f)] public float rockHighGroundBoost = 2f;

    [Tooltip("Maximálny počet kameňov (výkonnostná poistka).")]
    [Min(0)] public int maxRocks = 6000;

    // =====================================================================
    // NASTAVENIA – LANDING LOCATIONS
    // =====================================================================

    [Header("LANDING LOCATIONS – hustota")]
    [Tooltip("INDEX HUSTOTY landing locations: 0 = žiadne, 1 = landing location " +
             "takmer na každých 4 tiloch mapy (na každom bloku footprintu). " +
             "Per-typ náhodnosť (0–1) sa nastavuje v EnvironmentPrefabLibrary.")]
    [Range(0f, 1f)] public float landingDensity = 0.05f;

    [Tooltip("Minimálny odstup medzi dvomi landing locations v tiloch " +
             "(Chebyshev). 0 = môžu susediť – vtedy index 1 naozaj zaplní mapu.")]
    [Min(0)] public int landingMinSpacing = 0;

    [Tooltip("Maximálny počet landing locations (výkonnostná poistka).")]
    [Min(0)] public int maxLandings = 400;

    // =====================================================================
    // DÁTOVÝ MODEL
    // =====================================================================

    /// <summary>Druh environmentálneho objektu. Hodnoty sú súčasťou save formátu.</summary>
    public enum EnvKind
    {
        Tree = 0,
        Rock = 1,
        Landing = 2
    }

    /// <summary>
    /// Jeden vygenerovaný objekt. Drží všetko, čo treba na obnovu po Load
    /// (typ, variant, pozícia, footprint, náhodné pootočenie) + referenciu na
    /// scénový GameObject.
    /// </summary>
    public class EnvObject
    {
        public EnvKind kind;
        public int variant;      // index typu v knižnici (0..9 / 0..9 / 0..6)
        public int originX;      // ľavý-dolný roh footprintu
        public int originZ;
        public int width = 1;    // footprint v tiloch (strom/kameň = 1×1)
        public int depth = 1;
        public float yawJitter;  // náhodné pootočenie okolo Y (stupne)
        public GameObject go;
    }

    /// <summary>Všetky vygenerované objekty (poradie = poradie generovania).</summary>
    public readonly List<EnvObject> Objects = new List<EnvObject>();

    // Obsadenosť tilov: tile → objekt (jeden objekt môže držať viac tilov).
    private readonly Dictionary<Vector2Int, EnvObject> occupiedTiles
        = new Dictionary<Vector2Int, EnvObject>();

    // Korene v hierarchii (prehľadnosť v scéne).
    private GameObject root, treesRoot, rocksRoot, landingsRoot;

    private System.Random rng;

    // Offsety šumových polí (aby nebolo generovanie viazané na absolútnu pozíciu).
    private Vector2 treeNoiseOffset, rockNoiseOffset, speciesNoiseOffset, riparianNoiseOffset;

    // True, ak stav prišiel zo save – zabráni tomu, aby ešte bežiaca štartová
    // coroutine prepísala načítané objekty novým náhodným generovaním.
    private bool _loadedFromSave = false;

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        StartCoroutine(GenerateWhenReady());
    }

    /// <summary>
    /// Počká na terén, na pripravený tile engine a NA DOKONČENIE MIEST, až
    /// potom generuje. Presne v duchu CityManager.GenerateWhenReady, len s
    /// jednou závislosťou navyše (mestá).
    /// </summary>
    private IEnumerator GenerateWhenReady()
    {
        // 1) Terén (coordsF).
        while (TerrainManager.instance == null
               || TerrainManager.instance.coordsF == null
               || TerrainManager.instance.coordsF.Length == 0)
        {
            yield return null;
        }

        // 2) IndicatrixAPI – tileGrid sa inicializuje až v Start(); do vtedy
        //    by GetTileByIndexAny hodil NullReferenceException.
        while (!IndicatrixReadyOrAbsent())
            yield return null;

        // 3) MESTÁ – generujeme až po nich (požiadavka zadania). Čakáme, kým
        //    CityManager postaví aspoň jedno mesto, najviac však
        //    maxWaitForCitiesSeconds (poistka, keby mapa žiadne mesto nedala).
        if (CityManager.instance != null)
        {
            float waited = 0f;
            while (CityManager.instance.Cities.Count == 0 && waited < maxWaitForCitiesSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        // 4) Krátka pauza – nech dobehne aj prípadný auto-retry CityManagera.
        if (postCityDelaySeconds > 0f)
            yield return new WaitForSeconds(postCityDelaySeconds);

        // Ak medzitým prebehol Load, NEgeneruj – prepísali by sme načítaný stav.
        if (_loadedFromSave) yield break;

        GenerateEnvironment();
    }

    /// <summary>
    /// True, ak IndicatrixAPI buď nie je v scéne, alebo už má inicializovaný
    /// tileGrid (probe dotaz nehodí výnimku). Prevzaté z CityManager.
    /// </summary>
    private static bool IndicatrixReadyOrAbsent()
    {
        var api = IndicatrixAPI.instance;
        if (api == null) return true;
        try
        {
            api.GetTileByIndexAny(0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // =====================================================================
    // HLAVNÁ GENERÁCIA
    // =====================================================================

    /// <summary>
    /// Vyčistí prípadný starý obsah a vygeneruje novú prírodu:
    /// najprv STROMY, potom KAMENE, potom LANDING LOCATIONS.
    /// </summary>
    [ContextMenu("Regenerate Environment")]
    public void GenerateEnvironment()
    {
        if (TerrainManager.instance == null || TerrainManager.instance.coordsF == null)
        {
            Debug.LogWarning("[EnvironmentManager] Terén nie je pripravený – generácia preskočená.");
            return;
        }

        EnvironmentPrefabLibrary lib = ResolveLibrary();
        if (lib == null)
        {
            Debug.LogWarning("[EnvironmentManager] EnvironmentPrefabLibrary nie je v scéne – " +
                             "nie je z čoho generovať. Pridaj komponent a priraď prefaby.");
            return;
        }

        ClearAll();
        EnsureRoots();

        int seed = (environmentSeed == 0) ? Environment.TickCount : environmentSeed;
        rng = new System.Random(seed);

        // Offsety šumu z nášho seedovaného RNG → generovanie je reprodukovateľné.
        treeNoiseOffset = RandomOffset();
        rockNoiseOffset = RandomOffset();
        speciesNoiseOffset = RandomOffset();
        riparianNoiseOffset = RandomOffset();

        int minTile = Mathf.Max(0, borderMargin);
        int maxTile = Mathf.Min(TileGridSize, TerrainManager.instance.terrainWidth) - borderMargin;

        // Predpočet vzdialenosti k vode (pre brehovú vegetáciu).
        int[,] waterDist = BuildWaterDistanceField(minTile, maxTile);

        int trees = GenerateTrees(lib, minTile, maxTile, waterDist);
        int rocks = GenerateRocks(lib, minTile, maxTile);
        int landings = GenerateLandings(lib, minTile, maxTile);

        Debug.Log($"[EnvironmentManager] Vygenerované: {trees} stromov, {rocks} kameňov, " +
                  $"{landings} landing locations (seed={seed}).");
    }

    /// <summary>Zničí všetky vygenerované objekty a vyčistí obsadenosť.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < Objects.Count; i++)
            if (Objects[i] != null && Objects[i].go != null) Destroy(Objects[i].go);

        Objects.Clear();
        occupiedTiles.Clear();
    }

    // ---------------------------------------------------------------------
    // STROMY
    // ---------------------------------------------------------------------

    /// <summary>
    /// Rozmiestni stromy podľa fBm poľa hustoty (zhluky = lesy) s bonusom pri
    /// brehoch vodných tokov. Typ stromu sa volí podľa lokálne prevládajúcej
    /// typologickej skupiny (listnaté / ihličnaté / farebné / solitér), s malou
    /// šancou na prímes inej skupiny.
    /// </summary>
    private int GenerateTrees(EnvironmentPrefabLibrary lib, int minTile, int maxTile,
                              int[,] waterDist)
    {
        if (treeDensity <= 0f || !lib.HasAnyTree()) return 0;

        int placed = 0;

        for (int x = minTile; x < maxTile; x++)
        {
            for (int z = minTile; z < maxTile; z++)
            {
                if (placed >= maxTrees) return placed;
                if (!IsFreeNaturalTile(x, z, out float surfaceY)) continue;

                // Základná pravdepodobnosť z poľa zhlukov.
                float n = Fbm(x, z, treeClusterScale, treeNoiseOffset, 3);
                float shaped = Mathf.Pow(n, treeClusterContrast);
                float p = treeDensity * Mathf.Lerp(shaped, 1f, treeDensity);

                // Brehová vegetácia – len na "zarastených" úsekoch vody.
                if (waterEdgeTreeBonus > 0f && waterDist != null)
                {
                    int d = waterDist[x, z];
                    if (d > 0 && d <= waterEdgeRadius && IsRiparianHere(x, z))
                    {
                        float falloff = 1f - (d - 1) / (float)Mathf.Max(1, waterEdgeRadius);
                        float bonus = waterEdgeTreeBonus * falloff;
                        // Kombinácia bez prekročenia 1 (pravdepodobnosť zjednotenia).
                        p = 1f - (1f - p) * (1f - bonus);
                    }
                }

                if (rng.NextDouble() >= p) continue;

                int variant = PickTreeVariant(lib, x, z);
                if (variant < 0) continue;

                var slot = lib.GetTreeSlot(variant);
                var obj = SpawnObject(EnvKind.Tree, variant, slot, x, z, 1, 1, surfaceY, 0f, treesRoot);
                if (obj != null) placed++;
            }
        }

        return placed;
    }

    /// <summary>
    /// Vyberie typ stromu podľa lokálne prevládajúcej skupiny (šum typológie)
    /// s malou šancou na prímes inej skupiny. V rámci skupiny sa vyberá váženo.
    /// </summary>
    private int PickTreeVariant(EnvironmentPrefabLibrary lib, int x, int z)
    {
        // Prevládajúca skupina pre toto miesto (4 pásma šumu).
        float s = Fbm(x, z, treeSpeciesScale, speciesNoiseOffset, 2);
        int groupCount = 4; // Broadleaf / Conifer / Colorful / Solitary
        int dominant = Mathf.Clamp(Mathf.FloorToInt(s * groupCount), 0, groupCount - 1);

        bool mix = rng.NextDouble() < treeSpeciesMixChance;
        var wanted = (EnvironmentPrefabLibrary.TreeGroup)(mix ? rng.Next(0, groupCount) : dominant);

        int chosen = PickWeightedTree(lib, wanted);
        if (chosen >= 0) return chosen;

        // Skupina nemá priradený žiadny prefab – vezmi ľubovoľný použiteľný.
        return PickWeightedTree(lib, null);
    }

    /// <summary>Vážený výber stromu (voliteľne obmedzený na jednu skupinu). -1 = nič.</summary>
    private int PickWeightedTree(EnvironmentPrefabLibrary lib,
                                 EnvironmentPrefabLibrary.TreeGroup? group)
    {
        float total = 0f;
        for (int i = 0; i < EnvironmentPrefabLibrary.TreeVariants; i++)
        {
            var s = lib.GetTreeSlot(i);
            if (s == null || !s.IsUsable) continue;
            if (group.HasValue && s.group != group.Value) continue;
            total += s.weight;
        }
        if (total <= 0f) return -1;

        double r = rng.NextDouble() * total;
        for (int i = 0; i < EnvironmentPrefabLibrary.TreeVariants; i++)
        {
            var s = lib.GetTreeSlot(i);
            if (s == null || !s.IsUsable) continue;
            if (group.HasValue && s.group != group.Value) continue;

            r -= s.weight;
            if (r <= 0d) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------------
    // KAMENE
    // ---------------------------------------------------------------------

    /// <summary>
    /// Rozmiestni kamene podľa vlastného poľa hustoty. Vyššie položený terén
    /// je zvýhodnený (rockHighGroundBoost) – kamenisté svahy a hrebene.
    /// </summary>
    private int GenerateRocks(EnvironmentPrefabLibrary lib, int minTile, int maxTile)
    {
        if (rockDensity <= 0f || !lib.HasAnyRock()) return 0;

        float minY = TerrainManager.MinTerrainHeight;
        float maxY = TerrainManager.MaxTerrainHeight;

        int placed = 0;

        for (int x = minTile; x < maxTile; x++)
        {
            for (int z = minTile; z < maxTile; z++)
            {
                if (placed >= maxRocks) return placed;
                if (!IsFreeNaturalTile(x, z, out float surfaceY)) continue;

                float n = Fbm(x, z, rockClusterScale, rockNoiseOffset, 3);
                float shaped = Mathf.Pow(n, rockClusterContrast);
                float p = rockDensity * Mathf.Lerp(shaped, 1f, rockDensity);

                // Výškové zvýhodnenie.
                float elev = Mathf.InverseLerp(minY, maxY, surfaceY);
                p = Mathf.Clamp01(p * Mathf.Lerp(1f, rockHighGroundBoost, elev));

                if (rng.NextDouble() >= p) continue;

                int variant = PickWeightedRock(lib);
                if (variant < 0) continue;

                var slot = lib.GetRockSlot(variant);
                var obj = SpawnObject(EnvKind.Rock, variant, slot, x, z, 1, 1, surfaceY, 0f, rocksRoot);
                if (obj != null) placed++;
            }
        }

        return placed;
    }

    /// <summary>Vážený výber kameňa. -1 = žiadny použiteľný.</summary>
    private int PickWeightedRock(EnvironmentPrefabLibrary lib)
    {
        float total = 0f;
        for (int i = 0; i < EnvironmentPrefabLibrary.RockVariants; i++)
        {
            var s = lib.GetRockSlot(i);
            if (s != null && s.IsUsable) total += s.weight;
        }
        if (total <= 0f) return -1;

        double r = rng.NextDouble() * total;
        for (int i = 0; i < EnvironmentPrefabLibrary.RockVariants; i++)
        {
            var s = lib.GetRockSlot(i);
            if (s == null || !s.IsUsable) continue;

            r -= s.weight;
            if (r <= 0d) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------------
    // LANDING LOCATIONS
    // ---------------------------------------------------------------------

    /// <summary>
    /// Rozmiestni landing locations. Mapa sa prechádza po blokoch veľkosti
    /// footprintu (default 2×2 = 4 tile), takže index hustoty 1 znamená
    /// "takmer na každých 4 tiloch". Typ sa vyberá váženo podľa per-typ indexu
    /// náhodnosti nastaveného v knižnici.
    /// </summary>
    private int GenerateLandings(EnvironmentPrefabLibrary lib, int minTile, int maxTile)
    {
        if (landingDensity <= 0f || !lib.HasAnyLanding()) return 0;

        // Krok = najmenší footprint spomedzi použiteľných typov (default 2).
        int step = 99;
        for (int i = 0; i < EnvironmentPrefabLibrary.LandingVariants; i++)
        {
            var s = lib.GetLandingSlot(i);
            if (s == null || !s.IsUsable || s.randomness <= 0f) continue;
            step = Mathf.Min(step, Mathf.Max(1, Mathf.Min(s.footprintWidth, s.footprintDepth)));
        }
        if (step == 99) return 0;

        var placedOrigins = new List<Vector2Int>();
        int placed = 0;

        for (int x = minTile; x < maxTile; x += step)
        {
            for (int z = minTile; z < maxTile; z += step)
            {
                if (placed >= maxLandings) return placed;
                if (rng.NextDouble() >= landingDensity) continue;

                int variant = PickWeightedLanding(lib);
                if (variant < 0) continue;

                var slot = lib.GetLandingSlot(variant);
                int w = Mathf.Max(1, slot.footprintWidth);
                int d = Mathf.Max(1, slot.footprintDepth);

                if (x + w > maxTile || z + d > maxTile) continue;
                if (!IsFreeNaturalArea(x, z, w, d, out float surfaceY)) continue;
                if (!RespectsLandingSpacing(placedOrigins, x, z, w, d)) continue;

                float yaw = RandomYawFor(slot);
                var obj = SpawnObject(EnvKind.Landing, variant, slot, x, z, w, d,
                                      surfaceY, slot.sinkOffsetY, landingsRoot, yaw);
                if (obj != null)
                {
                    placedOrigins.Add(new Vector2Int(x, z));
                    placed++;
                }
            }
        }

        return placed;
    }

    /// <summary>Vážený výber typu landing location podľa per-typ indexu náhodnosti.</summary>
    private int PickWeightedLanding(EnvironmentPrefabLibrary lib)
    {
        float total = 0f;
        for (int i = 0; i < EnvironmentPrefabLibrary.LandingVariants; i++)
        {
            var s = lib.GetLandingSlot(i);
            if (s != null && s.IsUsable) total += s.randomness * s.weight;
        }
        if (total <= 0f) return -1;

        double r = rng.NextDouble() * total;
        for (int i = 0; i < EnvironmentPrefabLibrary.LandingVariants; i++)
        {
            var s = lib.GetLandingSlot(i);
            if (s == null || !s.IsUsable) continue;

            r -= s.randomness * s.weight;
            if (r <= 0d) return i;
        }
        return -1;
    }

    /// <summary>Overí minimálny odstup (Chebyshev) od už položených landing locations.</summary>
    private bool RespectsLandingSpacing(List<Vector2Int> origins, int x, int z, int w, int d)
    {
        if (landingMinSpacing <= 0) return true;

        for (int i = 0; i < origins.Count; i++)
        {
            int dx = Mathf.Max(0, Mathf.Max(origins[i].x - (x + w - 1), x - origins[i].x));
            int dz = Mathf.Max(0, Mathf.Max(origins[i].y - (z + d - 1), z - origins[i].y));
            if (Mathf.Max(dx, dz) < landingMinSpacing) return false;
        }
        return true;
    }

    // =====================================================================
    // VYTVORENIE MODELU
    // =====================================================================

    /// <summary>
    /// Vytvorí inštanciu prefabu, aplikuje fixné pootočenie + (voliteľne)
    /// náhodný yaw, proporčne ho preškáluje na požadovaný počet tilov, usadí
    /// ho na povrch (s prípadným zaborením) a zaeviduje obsadené tily.
    ///
    /// PORADIE (rovnaká logika ako IndicatrixAPI.InstantiateTileModel):
    ///   1. rotácia prefabu, 2. fixný Euler z knižnice, 3. náhodný yaw,
    ///   4. mierka podľa AABB, 5. vycentrovanie + sadnutie na povrch.
    /// Rotácia MUSÍ prebehnúť pred meraním bounds – mení svetový AABB.
    /// </summary>
    private EnvObject SpawnObject(EnvKind kind, int variant,
                                  EnvironmentPrefabLibrary.EnvSlot slot,
                                  int originX, int originZ, int width, int depth,
                                  float surfaceY, float sinkOffsetY, GameObject parent,
                                  float? presetYaw = null)
    {
        if (slot == null || slot.prefab == null) return null;

        float yawJitter = presetYaw ?? RandomYawFor(slot);

        GameObject go = InstantiateModel(slot, originX, originZ, width, depth,
                                         surfaceY, sinkOffsetY, yawJitter);
        if (go == null) return null;

        go.name = ObjectName(kind, variant, originX, originZ);
        if (parent != null) go.transform.SetParent(parent.transform, true);

        var obj = new EnvObject
        {
            kind = kind,
            variant = variant,
            originX = originX,
            originZ = originZ,
            width = width,
            depth = depth,
            yawJitter = yawJitter,
            go = go
        };

        Objects.Add(obj);
        for (int x = originX; x < originX + width; x++)
            for (int z = originZ; z < originZ + depth; z++)
                occupiedTiles[new Vector2Int(x, z)] = obj;

        return obj;
    }

    /// <summary>Samotná inštanciácia + transformácia modelu (bez evidencie).</summary>
    private GameObject InstantiateModel(EnvironmentPrefabLibrary.EnvSlot slot,
                                        int originX, int originZ, int width, int depth,
                                        float surfaceY, float sinkOffsetY, float yawJitter)
    {
        GameObject go = Instantiate(slot.prefab);

        // 1) FIXNÉ POOTOČENIE z knižnice (Yaw/Pitch/Roll po 90°).
        Vector3 euler = slot.EulerAngles;
        if (euler.sqrMagnitude > 0.001f)
            go.transform.rotation = Quaternion.Euler(euler) * go.transform.rotation;

        // 2) NÁHODNÝ YAW (ak je pre slot zapnutý) – aplikuje sa navyše.
        if (Mathf.Abs(yawJitter) > 0.001f)
            go.transform.rotation = Quaternion.Euler(0f, yawJitter, 0f) * go.transform.rotation;

        // 3) KOLÍZNE KOMPONENTY – preč (model nesmie blokovať raycast terénu,
        //    inak by prestali fungovať klik a guardy v GameManager).
        if (!keepColliders)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) Destroy(cols[i]);
        }

        // 4) MIERKA – uniformné prispôsobenie pôdorysu na scaleTiles tilov.
        float targetFootprint = tileWorldSize * Mathf.Clamp(slot.scaleTiles, 0.05f, 4f);
        if (TryGetCombinedBounds(go, out Bounds b0))
        {
            float footprint = Mathf.Max(b0.size.x, b0.size.z);
            if (footprint > 1e-5f)
                go.transform.localScale *= (targetFootprint / footprint);
        }

        // 5) POZÍCIA – stred footprintu, spodok modelu na povrch, mínus zaborenie.
        float cx = originX + width * 0.5f;
        float cz = originZ + depth * 0.5f;

        if (TryGetCombinedBounds(go, out Bounds b))
        {
            Vector3 p = go.transform.position;
            p.x += cx - b.center.x;
            p.z += cz - b.center.z;
            p.y += (surfaceY + 0.001f) - b.min.y;
            p.y -= Mathf.Max(0f, sinkOffsetY);   // zaborenie pod úroveň tile mapy
            go.transform.position = p;
        }
        else
        {
            go.transform.position = new Vector3(cx, surfaceY - Mathf.Max(0f, sinkOffsetY), cz);
        }

        return go;
    }

    /// <summary>Náhodné pootočenie okolo Y podľa nastavenia slotu (0 = vypnuté).</summary>
    private float RandomYawFor(EnvironmentPrefabLibrary.EnvSlot slot)
    {
        if (slot == null || !slot.randomYaw || rng == null) return 0f;
        return slot.randomYawQuantized
            ? 90f * rng.Next(0, 4)
            : (float)(rng.NextDouble() * 360d);
    }

    private string ObjectName(EnvKind kind, int variant, int x, int z)
    {
        switch (kind)
        {
            case EnvKind.Tree: return $"Tree_{variant + 1:00}_{x}_{z}";
            case EnvKind.Rock: return $"Rock_{variant + 1:00}_{x}_{z}";
            default: return $"Landing_{EnvironmentPrefabLibrary.LandingName(variant)}_{x}_{z}";
        }
    }

    private void EnsureRoots()
    {
        if (root == null)
        {
            root = new GameObject("Environment");
            root.transform.SetParent(this.transform, false);
        }
        if (treesRoot == null)
        {
            treesRoot = new GameObject("Trees");
            treesRoot.transform.SetParent(root.transform, false);
        }
        if (rocksRoot == null)
        {
            rocksRoot = new GameObject("Rocks");
            rocksRoot.transform.SetParent(root.transform, false);
        }
        if (landingsRoot == null)
        {
            landingsRoot = new GameObject("LandingLocations");
            landingsRoot.transform.SetParent(root.transform, false);
        }
    }

    // =====================================================================
    // PODMIENKY UMIESTNENIA
    // =====================================================================

    /// <summary>
    /// True, ak je tile [x,z] použiteľný pre prírodný objekt:
    /// voľný (tileGrid + mestská budova + iný environment objekt), VODOROVNÝ
    /// a NIE JE vodná hladina. surfaceY = výška povrchu.
    /// </summary>
    public bool IsFreeNaturalTile(int x, int z, out float surfaceY)
    {
        surfaceY = 0f;

        if (x < 0 || z < 0 || x >= TileGridSize || z >= TileGridSize) return false;

        // Už obsadené environmentom.
        if (occupiedTiles.ContainsKey(new Vector2Int(x, z))) return false;

        // Mestská budova (mimo tileGrid – vedie ju CityManager).
        if (CityManager.instance != null && CityManager.instance.IsCityTile(x, z)) return false;

        // Koľaj / cesta / stanica / depo / továreň.
        if (IndicatrixAPI.instance != null &&
            IndicatrixAPI.instance.GetTileByIndexAny(x, z).tileID != 0) return false;

        // Rovinatosť + voda (4 rohové vertexy face).
        float y0 = VertexY(x, z);
        float y1 = VertexY(x, z + 1);
        float y2 = VertexY(x + 1, z + 1);
        float y3 = VertexY(x + 1, z);

        const float EPS = 0.0001f;
        bool flat = Mathf.Abs(y0 - y1) < EPS
                 && Mathf.Abs(y0 - y2) < EPS
                 && Mathf.Abs(y0 - y3) < EPS;
        if (!flat) return false;

        float water = TerrainManager.MinTerrainHeight;
        if (y0 <= water + EPS || y1 <= water + EPS ||
            y2 <= water + EPS || y3 <= water + EPS) return false;

        surfaceY = y0;
        return true;
    }

    /// <summary>
    /// Ako <see cref="IsFreeNaturalTile"/>, ale pre celý obdĺžnik footprintu
    /// (landing location). Navyše vyžaduje, aby bola celá plocha v ROVNAKEJ
    /// výške – landing location potrebuje súvislú rovnú plošinu.
    /// </summary>
    public bool IsFreeNaturalArea(int originX, int originZ, int width, int depth,
                                  out float surfaceY)
    {
        surfaceY = 0f;
        bool first = true;

        for (int x = originX; x < originX + width; x++)
        {
            for (int z = originZ; z < originZ + depth; z++)
            {
                if (!IsFreeNaturalTile(x, z, out float y)) return false;

                if (first) { surfaceY = y; first = false; }
                else if (Mathf.Abs(y - surfaceY) > 0.0001f) return false;
            }
        }
        return !first;
    }

    /// <summary>Výška vertexu (x,z) z TerrainManager.coordsF (prevzaté z CityManager).</summary>
    private float VertexY(int x, int z)
    {
        int w = TerrainManager.instance.terrainWidth + 1;
        x = Mathf.Clamp(x, 0, w - 1);
        z = Mathf.Clamp(z, 0, w - 1);
        return TerrainManager.instance.coordsF[z * w + x].y;
    }

    /// <summary>True, ak je face tile [x,z] vodná hladina (aspoň jeden roh na/pod hladinou).</summary>
    private bool IsWaterTile(int x, int z)
    {
        const float EPS = 0.0001f;
        float water = TerrainManager.MinTerrainHeight;
        return VertexY(x, z) <= water + EPS
            || VertexY(x, z + 1) <= water + EPS
            || VertexY(x + 1, z + 1) <= water + EPS
            || VertexY(x + 1, z) <= water + EPS;
    }

    // =====================================================================
    // ŠUM A VODNÁ BLÍZKOSŤ
    // =====================================================================

    /// <summary>Náhodný offset šumového poľa zo seedovaného RNG.</summary>
    private Vector2 RandomOffset()
        => new Vector2((float)(rng.NextDouble() * 10000d), (float)(rng.NextDouble() * 10000d));

    /// <summary>
    /// Fraktálny (viac-oktávový) Perlin šum v rozsahu 0–1, roztiahnutý tak, aby
    /// pokryl celý rozsah (Perlin sám sa drží okolo stredu). Slúži ako pole
    /// hustoty – práve on vytvára prirodzené zhluky (lesy, kamenné polia).
    /// </summary>
    private float Fbm(float x, float z, float scale, Vector2 offset, int octaves)
    {
        float amp = 1f, freq = 1f, sum = 0f, norm = 0f;

        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Mathf.PerlinNoise(offset.x + x * scale * freq,
                                           offset.y + z * scale * freq);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }

        float v = sum / Mathf.Max(0.0001f, norm);
        // Roztiahnutie z typického rozsahu ~0.25–0.75 na plných 0–1.
        return Mathf.Clamp01((v - 0.25f) * 2f);
    }

    /// <summary>
    /// True, ak sa v tomto mieste má uplatniť brehová vegetácia. Nízkofrekvenčný
    /// šum tvorí súvislé úseky – časť brehov je teda zarastená a časť holá,
    /// namiesto uniformného lemu okolo každej vody.
    /// </summary>
    private bool IsRiparianHere(int x, int z)
    {
        if (riparianCoverage <= 0f) return false;
        if (riparianCoverage >= 1f) return true;

        float n = Fbm(x, z, 0.02f, riparianNoiseOffset, 2);
        return n < riparianCoverage;
    }

    /// <summary>
    /// Predpočíta vzdialenosť (v tiloch, 4-susedstvo) od najbližšieho vodného
    /// tile pomocou BFS z viacerých zdrojov. 0 = samotná voda, -1 = mimo dosahu
    /// waterEdgeRadius. Beží raz za generovanie – 256×256 je zanedbateľné.
    /// </summary>
    private int[,] BuildWaterDistanceField(int minTile, int maxTile)
    {
        if (waterEdgeTreeBonus <= 0f) return null;

        var dist = new int[TileGridSize, TileGridSize];
        for (int x = 0; x < TileGridSize; x++)
            for (int z = 0; z < TileGridSize; z++)
                dist[x, z] = -1;

        var queue = new Queue<Vector2Int>();

        int lo = Mathf.Max(0, minTile - waterEdgeRadius);
        int hi = Mathf.Min(TileGridSize, maxTile + waterEdgeRadius);

        for (int x = lo; x < hi; x++)
        {
            for (int z = lo; z < hi; z++)
            {
                if (IsWaterTile(x, z))
                {
                    dist[x, z] = 0;
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
        }

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int cd = dist[c.x, c.y];
            if (cd >= waterEdgeRadius) continue;

            for (int i = 0; i < 4; i++)
            {
                int nx = c.x + dx[i];
                int nz = c.y + dz[i];
                if (nx < lo || nz < lo || nx >= hi || nz >= hi) continue;
                if (dist[nx, nz] != -1) continue;

                dist[nx, nz] = cd + 1;
                queue.Enqueue(new Vector2Int(nx, nz));
            }
        }

        return dist;
    }

    // =====================================================================
    // VEREJNÉ DOTAZY PRE GameManager (analógia CityManager.IsCityTile)
    // =====================================================================

    /// <summary>
    /// True, ak na tile [x,z] stojí ĽUBOVOĽNÝ environment objekt (strom, kameň
    /// alebo landing location). GameManager to používa ako guard – na takýto
    /// tile sa nesmie stavať nič a nesmie sa nad ním upravovať terén.
    /// </summary>
    public bool IsEnvironmentTile(int x, int z)
        => occupiedTiles.ContainsKey(new Vector2Int(x, z));

    /// <summary>
    /// True, ak je tile obsadený LANDING LOCATION – tá je fixná a nedá sa
    /// odstrániť ani cez Demolish.
    /// </summary>
    public bool IsProtectedEnvironmentTile(int x, int z)
    {
        return occupiedTiles.TryGetValue(new Vector2Int(x, z), out EnvObject o)
            && o != null && o.kind == EnvKind.Landing;
    }

    /// <summary>
    /// True, ak je tile obsadený STROMOM alebo KAMEŇOM – tie hráč cez Demolish
    /// odstrániť môže (zadarmo, bez refundu).
    /// </summary>
    public bool IsRemovableEnvironmentTile(int x, int z)
    {
        return occupiedTiles.TryGetValue(new Vector2Int(x, z), out EnvObject o)
            && o != null && (o.kind == EnvKind.Tree || o.kind == EnvKind.Rock);
    }

    /// <summary>Druh objektu na tile, alebo null ak tam nič nie je.</summary>
    public EnvKind? GetEnvironmentKind(int x, int z)
    {
        return occupiedTiles.TryGetValue(new Vector2Int(x, z), out EnvObject o) && o != null
            ? o.kind
            : (EnvKind?)null;
    }

    /// <summary>
    /// Odstráni STROM alebo KAMEŇ na tile [x,z] (Demolish). Landing location
    /// sa NEODSTRÁNI – metóda pre ňu vráti false. Vracia true, ak sa reálne
    /// niečo odstránilo. Odstránenie je ZADARMO – žiadna cena ani refund.
    /// </summary>
    public bool RemoveAt(int x, int z)
    {
        var key = new Vector2Int(x, z);
        if (!occupiedTiles.TryGetValue(key, out EnvObject o) || o == null) return false;
        if (o.kind == EnvKind.Landing) return false;   // fixná, nedá sa zbúrať

        for (int tx = o.originX; tx < o.originX + o.width; tx++)
            for (int tz = o.originZ; tz < o.originZ + o.depth; tz++)
                occupiedTiles.Remove(new Vector2Int(tx, tz));

        if (o.go != null) Destroy(o.go);
        Objects.Remove(o);
        return true;
    }

    // =====================================================================
    // SAVE / LOAD
    // ─────────────────────────────────────────────────────────────────────
    // Volá sa z IndicatrixAPI.SaveGame / LoadGame ako posledný blok streamu
    // (za blokom miest). Formát jedného záznamu – 7 hodnôt v tomto poradí:
    //     int kind, int variant, int originX, int originZ,
    //     int width, int depth, float yawJitter
    // Prefab sa neukladá – pri Load sa dohľadá z EnvironmentPrefabLibrary
    // (rovnaký princíp ako CityManager + CityBuildingLibrary). Výška povrchu
    // sa dopočíta z už načítaného terénu, takže sa tiež neukladá.
    // =====================================================================

    /// <summary>Zapíše všetky environment objekty do save streamu.</summary>
    public void WriteSave(System.IO.BinaryWriter bw)
    {
        bw.Write(Objects.Count);

        foreach (var o in Objects)
        {
            bw.Write((int)o.kind);
            bw.Write(o.variant);
            bw.Write(o.originX);
            bw.Write(o.originZ);
            bw.Write(o.width);
            bw.Write(o.depth);
            bw.Write(o.yawJitter);
        }
    }

    /// <summary>
    /// Načíta environment objekty zo save streamu. Najprv zmaže existujúce
    /// (náhodne vygenerované) objekty a postaví uložené. Terén aj tile grid
    /// musia byť už načítané (poradie blokov to garantuje), aby modely sadli
    /// na správnu výšku.
    /// </summary>
    public void LoadSave(System.IO.BinaryReader br)
    {
        ClearAll();
        EnsureRoots();

        // Zabráni tomu, aby štartová coroutine prepísala načítaný stav.
        _loadedFromSave = true;

        EnvironmentPrefabLibrary lib = ResolveLibrary();

        int count = br.ReadInt32();
        int restored = 0;

        for (int i = 0; i < count; i++)
        {
            var kind = (EnvKind)br.ReadInt32();
            int variant = br.ReadInt32();
            int originX = br.ReadInt32();
            int originZ = br.ReadInt32();
            int width = br.ReadInt32();
            int depth = br.ReadInt32();
            float yaw = br.ReadSingle();

            if (lib == null) continue;

            EnvironmentPrefabLibrary.EnvSlot slot;
            GameObject parent;
            float sink = 0f;

            switch (kind)
            {
                case EnvKind.Tree:
                    slot = lib.GetTreeSlot(variant); parent = treesRoot; break;
                case EnvKind.Rock:
                    slot = lib.GetRockSlot(variant); parent = rocksRoot; break;
                default:
                    var ls = lib.GetLandingSlot(variant);
                    slot = ls; parent = landingsRoot;
                    sink = (ls != null) ? ls.sinkOffsetY : 0f;
                    break;
            }

            if (slot == null || slot.prefab == null)
            {
                Debug.LogWarning($"[EnvironmentManager] Zo save chýba prefab pre {kind} " +
                                 $"variant {variant} – objekt preskočený.");
                continue;
            }

            // Výška povrchu z (už načítaného) terénu – objekty stáli na rovine.
            float surfaceY = VertexY(originX, originZ);

            var obj = SpawnObject(kind, variant, slot, originX, originZ, width, depth,
                                  surfaceY, sink, parent, yaw);
            if (obj != null) restored++;
        }

        Debug.Log($"[EnvironmentManager] Načítaných {restored}/{count} environment objektov zo save.");
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private EnvironmentPrefabLibrary ResolveLibrary()
    {
        if (EnvironmentPrefabLibrary.instance != null)
            return EnvironmentPrefabLibrary.instance;
        return FindAnyObjectByType<EnvironmentPrefabLibrary>();
    }

    /// <summary>Spoločný (world-space) Bounds všetkých Rendererov v hierarchii.</summary>
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
}
