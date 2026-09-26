using UnityEngine;

/// <summary>
/// EnvironmentPrefabLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 3D MODELOV / PREFABOV pre ENVIRONMENTÁLNE OBJEKTY – priama
/// analógia k <see cref="CityBuildingLibrary"/> (prefaby budov miest), doplnená
/// o nastavenie ROTÁCIE presne v duchu <see cref="TileModelLibrary"/>
/// (fixné 90° kroky Yaw/Pitch/Roll) a o MIERKU (scale faktor v tiloch).
///
/// Drží VOLITEĽNÉ prefab-y (drag-and-drop v Inspectore) pre tri skupiny:
///
///   • STROMY            – 10 typov, obsadenosť 1 tile
///   • KAMENE            – 10 typov, obsadenosť 1 tile
///   • LANDING LOCATIONS –  7 typov, obsadenosť 4 tile (footprint 2×2)
///
/// ─────────────────────────────────────────────────────────────────────────
/// PRÁZDNY SLOT = TYP SA NEGENERUJE
///   Na rozdiel od CityBuildingLibrary sa pre chýbajúci prefab NEGENERUJE
///   žiadna primitíva – strom/kameň bez modelu nedáva zmysel. Ak je slot
///   prázdny, <see cref="EnvironmentManager"/> daný typ jednoducho preskočí
///   (vyberie iný, ktorý prefab má). Ak nie je priradený ani jeden prefab
///   danej skupiny, skupina sa negeneruje vôbec.
///
/// ROTÁCIA (analógia TileModelLibrary):
///   Každý slot má fixné pootočenie po 90° krokoch pre všetky tri osi:
///     • Yaw   – okolo Y (rovina X-Z, pôdorys)
///     • Pitch – okolo X (rovina Z-Y)
///     • Roll  – okolo Z (rovina X-Y)
///   Je to JEDNORAZOVÁ korekcia toho, ako je model vyexportovaný v prefabe;
///   herná logika s ňou ďalej nepracuje. Navyše (voliteľne, per-slot) sa dá
///   zapnúť náhodné pootočenie okolo Y pri generovaní – aby les nevyzeral
///   ako klonovaná mriežka. To sa ukladá do save, takže po Load je scéna
///   identická.
///
/// MIERKA (scaleTiles):
///   Udáva, koľko TILOV má zaberať PÔDORYS modelu po proporčnom (uniformnom)
///   prispôsobení. 1 = presne 1 tile, 4 = maximum podľa zadania. Logická
///   obsadenosť tile mapy sa tým NEMENÍ (strom/kameň = 1 tile, landing = 4
///   tile) – mení sa len vizuálna veľkosť modelu.
///
/// ZABORENIE (len LANDING LOCATIONS):
///   sinkOffsetY posunie model kolmo NADOL pod úroveň tile mapy (v svetových
///   jednotkách). 0 = model sadne spodkom presne na povrch.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class EnvironmentPrefabLibrary : MonoBehaviour
{
    /// <summary>Statická inštancia pre pohodlné dohľadanie z EnvironmentManager.</summary>
    public static EnvironmentPrefabLibrary instance;

    /// <summary>Počet typov v jednotlivých skupinách (podľa zadania).</summary>
    public const int TreeVariants = 10;
    public const int RockVariants = 10;
    public const int LandingVariants = 7;

    /// <summary>
    /// Fixný 90° krok pootočenia modelu – identická konvencia ako
    /// <see cref="TileModelLibrary.TileYaw"/>. Hodnoty sú priamo stupne.
    /// </summary>
    public enum EnvYaw
    {
        Deg0 = 0,
        Deg90 = 90,
        Deg180 = 180,
        Deg270 = 270
    }

    /// <summary>
    /// Typológia stromov – používa ju generátor na tvorbu ZHLUKOV (lesov):
    /// v rámci jedného zhluku prevažuje jedna skupina, takže vznikajú listnaté
    /// háje, ihličnaté lesy a pod. namiesto rovnomernej "polievky" typov.
    /// Rozdelenie 10 typov: 3 listnaté + 3 ihličnaté + 3 inak sfarbené + 1 solitér.
    /// </summary>
    public enum TreeGroup
    {
        Broadleaf = 0,  // listnaté
        Conifer = 1,    // ihličnaté
        Colorful = 2,   // inak sfarbené (jeseň, kvitnúce…)
        Solitary = 3    // solitér / krovie / exot
    }

    /// <summary>
    /// Presne definované typy landing locations (podľa zadania, v tomto poradí).
    /// Index enumu = index slotu v knižnici aj v save súbore – poradie preto
    /// NEMEŇ, len prípadne pridávaj na koniec.
    /// </summary>
    public enum LandingType
    {
        AutumnAtmosphereForest = 0,
        CampingForest = 1,
        DesertPyramids = 2,
        Downtown = 3,
        JapaneseIsland = 4,
        TreasureIsland = 5,
        WildWestRailwayStation = 6
    }

    /// <summary>Čitateľné názvy typov landing locations (pre názvy objektov a logy).</summary>
    public static readonly string[] LandingDisplayNames =
    {
        "Autumn atmosphere forest",
        "Camping forest",
        "Desert pyramids",
        "Downtown",
        "Japanes island",
        "Treasure Island",
        "Wild west railway station"
    };

    // =========================================================================
    // SLOT – jeden prefab + jeho fixné nastavenia (rotácia, mierka, váha)
    // =========================================================================

    /// <summary>
    /// Jeden slot knižnice: prefab + fixné pootočenie (3 osi po 90°) + mierka
    /// + relatívna váha výberu. Prázdny prefab = typ sa negeneruje.
    /// </summary>
    [System.Serializable]
    public class EnvSlot
    {
        [Tooltip("Prefab modelu. Prázdne = tento typ sa vôbec negeneruje.")]
        public GameObject prefab;

        [Tooltip("Fixné pootočenie okolo osi Y (rovina X-Z). Nastav raz podľa prefabu.")]
        public EnvYaw yaw = EnvYaw.Deg0;

        [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y). Nastav raz podľa prefabu.")]
        public EnvYaw pitch = EnvYaw.Deg0;

        [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y). Nastav raz podľa prefabu.")]
        public EnvYaw roll = EnvYaw.Deg0;

        [Tooltip("Mierka: koľko TILOV má zaberať pôdorys modelu (1 = presne 1 tile, " +
                 "max. 4 tile podľa zadania). Logická obsadenosť tile mapy sa nemení.")]
        [Range(0.25f, 4f)] public float scaleTiles = 1f;

        [Tooltip("Náhodné pootočenie okolo Y pri generovaní (aby modely nevyzerali " +
                 "klonované). Aplikuje sa NAVYŠE k fixnému pootočeniu vyššie a ukladá " +
                 "sa do save, takže po Load je scéna identická.")]
        public bool randomYaw = false;

        [Tooltip("Ak je randomYaw zapnuté: true = náhodne len po 90° krokoch, " +
                 "false = ľubovoľný uhol 0–360°.")]
        public bool randomYawQuantized = false;

        [Tooltip("Relatívna váha výberu tohto typu v rámci jeho skupiny (0 = nepoužiť).")]
        [Range(0f, 1f)] public float weight = 1f;

        /// <summary>Fixné pootočenie ako Euler uhly v stupňoch (x=Pitch, y=Yaw, z=Roll).</summary>
        public Vector3 EulerAngles => new Vector3((int)pitch, (int)yaw, (int)roll);

        /// <summary>True, ak je slot použiteľný (má prefab a nenulovú váhu).</summary>
        public bool IsUsable => prefab != null && weight > 0f;
    }

    /// <summary>
    /// Slot stromu = EnvSlot + typologická skupina (kvôli zhlukovaniu do lesov).
    /// </summary>
    [System.Serializable]
    public class TreeSlot : EnvSlot
    {
        [Tooltip("Typologická skupina – generátor podľa nej tvorí zhluky (lesy).")]
        public TreeGroup group = TreeGroup.Broadleaf;
    }

    /// <summary>
    /// Slot landing location = EnvSlot + footprint (default 2×2 = 4 tile) +
    /// zaborenie pod úroveň mapy + vlastný index náhodnosti (0–1).
    /// </summary>
    [System.Serializable]
    public class LandingSlot : EnvSlot
    {
        [Tooltip("Index náhodnosti tohto konkrétneho typu (0 = nikdy, 1 = maximum). " +
                 "Kombinuje sa s globálnou hustotou v EnvironmentManager.")]
        [Range(0f, 1f)] public float randomness = 1f;

        [Tooltip("ZABORENIE pod úroveň tile mapy: o koľko svetových jednotiek sa " +
                 "model posunie kolmo NADOL (0 = spodok presne na povrchu).")]
        [Range(0f, 2f)] public float sinkOffsetY = 0f;

        [Tooltip("Šírka footprintu v tiloch (os X). Podľa zadania 2 (2×2 = 4 tile). " +
                 "Systematika je zhodná s IndicatrixAPI.FactoryFootprint, takže sa dá " +
                 "nastaviť aj 2×3, 3×3 a pod.")]
        [Min(1)] public int footprintWidth = 2;

        [Tooltip("Hĺbka footprintu v tiloch (os Z). Podľa zadania 2 (2×2 = 4 tile).")]
        [Min(1)] public int footprintDepth = 2;
    }

    // =========================================================================
    // STROMY – 10 typov (3 listnaté, 3 ihličnaté, 3 inak sfarbené, 1 solitér)
    // =========================================================================
    [Header("STROMY – listnaté (3 typy)")]
    [SerializeField] private TreeSlot tree01_BroadleafA = new TreeSlot { group = TreeGroup.Broadleaf };
    [SerializeField] private TreeSlot tree02_BroadleafB = new TreeSlot { group = TreeGroup.Broadleaf };
    [SerializeField] private TreeSlot tree03_BroadleafC = new TreeSlot { group = TreeGroup.Broadleaf };

    [Header("STROMY – ihličnaté (3 typy)")]
    [SerializeField] private TreeSlot tree04_ConiferA = new TreeSlot { group = TreeGroup.Conifer };
    [SerializeField] private TreeSlot tree05_ConiferB = new TreeSlot { group = TreeGroup.Conifer };
    [SerializeField] private TreeSlot tree06_ConiferC = new TreeSlot { group = TreeGroup.Conifer };

    [Header("STROMY – inak sfarbené (3 typy)")]
    [SerializeField] private TreeSlot tree07_ColorfulA = new TreeSlot { group = TreeGroup.Colorful };
    [SerializeField] private TreeSlot tree08_ColorfulB = new TreeSlot { group = TreeGroup.Colorful };
    [SerializeField] private TreeSlot tree09_ColorfulC = new TreeSlot { group = TreeGroup.Colorful };

    [Header("STROMY – solitér / krovie (1 typ)")]
    [SerializeField] private TreeSlot tree10_Solitary = new TreeSlot { group = TreeGroup.Solitary };

    // =========================================================================
    // KAMENE – 10 typov
    // =========================================================================
    [Header("KAMENE (10 typov)")]
    [SerializeField] private EnvSlot rock01 = new EnvSlot();
    [SerializeField] private EnvSlot rock02 = new EnvSlot();
    [SerializeField] private EnvSlot rock03 = new EnvSlot();
    [SerializeField] private EnvSlot rock04 = new EnvSlot();
    [SerializeField] private EnvSlot rock05 = new EnvSlot();
    [SerializeField] private EnvSlot rock06 = new EnvSlot();
    [SerializeField] private EnvSlot rock07 = new EnvSlot();
    [SerializeField] private EnvSlot rock08 = new EnvSlot();
    [SerializeField] private EnvSlot rock09 = new EnvSlot();
    [SerializeField] private EnvSlot rock10 = new EnvSlot();

    // =========================================================================
    // LANDING LOCATIONS – 7 presne definovaných typov (footprint 2×2 = 4 tile)
    // =========================================================================
    [Header("LANDING LOCATIONS (7 typov, footprint 2×2 = 4 tile)")]
    [Tooltip("1. typ – Autumn atmosphere forest")]
    [SerializeField] private LandingSlot landing1_AutumnAtmosphereForest = NewLanding();
    [Tooltip("2. typ – Camping forest")]
    [SerializeField] private LandingSlot landing2_CampingForest = NewLanding();
    [Tooltip("3. typ – Desert pyramids")]
    [SerializeField] private LandingSlot landing3_DesertPyramids = NewLanding();
    [Tooltip("4. typ – Downtown")]
    [SerializeField] private LandingSlot landing4_Downtown = NewLanding();
    [Tooltip("5. typ – Japanes island")]
    [SerializeField] private LandingSlot landing5_JapanesIsland = NewLanding();
    [Tooltip("6. typ – Treasure Island")]
    [SerializeField] private LandingSlot landing6_TreasureIsland = NewLanding();
    [Tooltip("7. typ – Wild west railway station")]
    [SerializeField] private LandingSlot landing7_WildWestRailwayStation = NewLanding();

    /// <summary>Default pre landing slot – footprint 2×2 a mierka na 2 tile.</summary>
    private static LandingSlot NewLanding()
        => new LandingSlot { footprintWidth = 2, footprintDepth = 2, scaleTiles = 2f };

    // =========================================================================
    // LOOKUP – polia zostavené zo serializovaných slotov
    // =========================================================================
    private TreeSlot[] _trees;
    private EnvSlot[] _rocks;
    private LandingSlot[] _landings;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Debug.LogWarning("[EnvironmentPrefabLibrary] V scéne je viac inštancií – " +
                             "ponechávam prvú. Tento komponent sa ignoruje pri auto-dohľadaní.");

        BuildMaps();
    }

    /// <summary>
    /// Naplní lookup polia z pomenovaných serializovaných slotov.
    /// Idempotentné – bezpečné volať aj opakovane.
    /// </summary>
    private void BuildMaps()
    {
        _trees = new[]
        {
            tree01_BroadleafA, tree02_BroadleafB, tree03_BroadleafC,
            tree04_ConiferA,   tree05_ConiferB,   tree06_ConiferC,
            tree07_ColorfulA,  tree08_ColorfulB,  tree09_ColorfulC,
            tree10_Solitary
        };

        _rocks = new[]
        {
            rock01, rock02, rock03, rock04, rock05,
            rock06, rock07, rock08, rock09, rock10
        };

        _landings = new[]
        {
            landing1_AutumnAtmosphereForest,
            landing2_CampingForest,
            landing3_DesertPyramids,
            landing4_Downtown,
            landing5_JapanesIsland,
            landing6_TreasureIsland,
            landing7_WildWestRailwayStation
        };
    }

    // =========================================================================
    // VEREJNÉ GETTERY (analógia CityBuildingLibrary.GetBuildingPrefab)
    // =========================================================================

    /// <summary>Slot stromu podľa indexu 0..9, alebo null pri neplatnom indexe.</summary>
    public TreeSlot GetTreeSlot(int variantIndex)
    {
        if (_trees == null) BuildMaps();
        if (variantIndex < 0 || variantIndex >= _trees.Length) return null;
        return _trees[variantIndex];
    }

    /// <summary>Slot kameňa podľa indexu 0..9, alebo null pri neplatnom indexe.</summary>
    public EnvSlot GetRockSlot(int variantIndex)
    {
        if (_rocks == null) BuildMaps();
        if (variantIndex < 0 || variantIndex >= _rocks.Length) return null;
        return _rocks[variantIndex];
    }

    /// <summary>Slot landing location podľa indexu 0..6, alebo null pri neplatnom indexe.</summary>
    public LandingSlot GetLandingSlot(int variantIndex)
    {
        if (_landings == null) BuildMaps();
        if (variantIndex < 0 || variantIndex >= _landings.Length) return null;
        return _landings[variantIndex];
    }

    /// <summary>Slot landing location podľa typu (enum).</summary>
    public LandingSlot GetLandingSlot(LandingType type) => GetLandingSlot((int)type);

    /// <summary>Prefab stromu (alebo null, ak nie je priradený).</summary>
    public GameObject GetTreePrefab(int variantIndex) => GetTreeSlot(variantIndex)?.prefab;

    /// <summary>Prefab kameňa (alebo null, ak nie je priradený).</summary>
    public GameObject GetRockPrefab(int variantIndex) => GetRockSlot(variantIndex)?.prefab;

    /// <summary>Prefab landing location (alebo null, ak nie je priradený).</summary>
    public GameObject GetLandingPrefab(int variantIndex) => GetLandingSlot(variantIndex)?.prefab;

    /// <summary>True, ak je aspoň jeden strom použiteľný (má prefab a váhu > 0).</summary>
    public bool HasAnyTree()
    {
        if (_trees == null) BuildMaps();
        for (int i = 0; i < _trees.Length; i++)
            if (_trees[i] != null && _trees[i].IsUsable) return true;
        return false;
    }

    /// <summary>True, ak je aspoň jeden kameň použiteľný.</summary>
    public bool HasAnyRock()
    {
        if (_rocks == null) BuildMaps();
        for (int i = 0; i < _rocks.Length; i++)
            if (_rocks[i] != null && _rocks[i].IsUsable) return true;
        return false;
    }

    /// <summary>True, ak je aspoň jedna landing location použiteľná.</summary>
    public bool HasAnyLanding()
    {
        if (_landings == null) BuildMaps();
        for (int i = 0; i < _landings.Length; i++)
            if (_landings[i] != null && _landings[i].IsUsable && _landings[i].randomness > 0f)
                return true;
        return false;
    }

    /// <summary>Čitateľný názov typu landing location (pre názvy objektov / logy).</summary>
    public static string LandingName(int variantIndex)
        => (variantIndex >= 0 && variantIndex < LandingDisplayNames.Length)
           ? LandingDisplayNames[variantIndex]
           : "Landing";
}
