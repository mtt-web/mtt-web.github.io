using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// RailStationSystem.cs
/// ------------------------------------------------------------------
/// DÁTOVÁ + EVIDENČNÁ VRSTVA PRE STANICE.
///
/// Doteraz bola stanica reprezentovaná IBA ako jeden tile na mape
/// (IndicatrixAPI: tileID == 2, TileCategory.Rail). To stačí na to, aby
/// vlak vedel, KDE stanica je (TrainData.stations je len List&lt;Vector2Int&gt;),
/// ale stanica nenesie žiadnu informáciu o tom, AKÉ TOVÁRNE k nej patria.
///
/// Tento súbor dopĺňa práve to a NEZASAHUJE do tile enginu:
///
///   - StationCatchment  – fiktívny 9×9 región (oblasť pôsobnosti) okolo
///                         tile stanice. NEVYKRESLÍ sa na mape, existuje
///                         iba "v pamäti hry".
///
///   - StationInstance   – jedna konkrétna stanica: jej tile + catchment +
///                         zoznam FactoryInstance, ktoré do catchmentu
///                         zasahujú aspoň jedným tilom svojho footprintu.
///
///   - StationRegistry   – centrálna evidencia VŠETKÝCH staníc na mape +
///                         (re)scan, ktorý priradí stanici jej továrne.
///
/// PREČO 9×9 a "stanica v strede":
///   Zadanie hovorí o fixnej zóne 9×9 tilov so stanicou (1 tile) presne
///   v strede. 9 je nepárne, takže stred je jednoznačný: od stredového
///   tilu 4 tily na každú stranu (4 + 1 + 4 = 9). Preto:
///       minX = stationX - 4 ... maxX = stationX + 4
///       minZ = stationZ - 4 ... maxZ = stationZ + 4
///
/// PRAVIDLO EVIDENCIE TOVÁRNE:
///   Továreň patrí stanici, ak ASPOŇ JEDEN tile jej footprintu leží
///   v catchmente stanice. Footprint továrne je obdĺžnik (Origin + Width
///   + Depth) – berieme ho z už existujúcej FactoryInstance (FactorySystem).
///   Jedna továreň môže patriť VIACERÝM staniciam (prekrývajúce sa zóny) –
///   to je v poriadku, transakcia sa vždy viaže na konkrétnu stanicu.
/// ------------------------------------------------------------------
/// </summary>

// ==================================================================
// CATCHMENT – fiktívna 9×9 zóna stanice
// ==================================================================

/// <summary>
/// StationCatchment
/// ------------------------------------------------------------------
/// Fiktívna štvorcová oblasť pôsobnosti stanice. NEVYKRESLÍ sa na tile
/// mape – je to čisto geometrický pomocník (obdĺžnik v tile súradniciach).
///
/// Veľkosť je fixná 9×9 (HALF = 4 tily na každú stranu od stredu).
/// </summary>
public struct StationCatchment
{
    /// <summary>Polovičná šírka zóny – 4 tily na každú stranu od stredu.</summary>
    public const int HALF = 4;

    /// <summary>Hrana štvorca – 2*HALF + 1 = 9 tilov.</summary>
    public const int SIZE = 2 * HALF + 1;

    /// <summary>Stredový tile zóny – samotná dlaždica stanice.</summary>
    public readonly int CenterX;
    public readonly int CenterZ;

    /// <summary>Ľavý-dolný roh zóny (vrátane).</summary>
    public int MinX => CenterX - HALF;
    public int MinZ => CenterZ - HALF;

    /// <summary>Pravý-horný roh zóny (vrátane).</summary>
    public int MaxX => CenterX + HALF;
    public int MaxZ => CenterZ + HALF;

    public StationCatchment(int centerX, int centerZ)
    {
        CenterX = centerX;
        CenterZ = centerZ;
    }

    /// <summary>True, ak tile [x,z] leží vnútri tejto 9×9 zóny.</summary>
    public bool ContainsTile(int x, int z)
    {
        return x >= MinX && x <= MaxX
            && z >= MinZ && z <= MaxZ;
    }

    /// <summary>
    /// True, ak sa zóna prekrýva s obdĺžnikovým footprintom továrne
    /// (Origin + Width + Depth). Stačí prienik aspoň jedného tilu –
    /// použije sa štandardný AABB (axis-aligned bounding box) test.
    ///
    /// Footprint zaberá tily [originX .. originX+width-1] × [originZ .. originZ+depth-1].
    /// </summary>
    public bool OverlapsFootprint(int originX, int originZ, int width, int depth)
    {
        int fMaxX = originX + width - 1;
        int fMaxZ = originZ + depth - 1;

        // AABB prienik: NEPREKRÝVAJÚ sa práve vtedy, keď je jeden celý
        // naľavo / napravo / nad / pod druhým. Inak sa prekrývajú.
        bool noOverlap = fMaxX < MinX || originX > MaxX
                      || fMaxZ < MinZ || originZ > MaxZ;
        return !noOverlap;
    }

    public override string ToString()
        => $"Catchment 9x9 stred[{CenterX},{CenterZ}] [{MinX},{MinZ}]–[{MaxX},{MaxZ}]";
}

// ==================================================================
// STATION INSTANCE – jedna konkrétna stanica
// ==================================================================

/// <summary>
/// StationInstance
/// ------------------------------------------------------------------
/// Jedna konkrétna stanica položená na mape. Spája:
///   - tile stanice (TileX, TileZ)  – kde je vykreslená na mape,
///   - catchment (9×9 zóna)         – fiktívna oblasť pôsobnosti,
///   - factories                    – zoznam tovární v dosahu zóny.
///
/// Zoznam tovární sa NEpočíta priebežne – naplní ho StationRegistry pri
/// (re)scane (po položení/zbúraní stanice alebo továrne). Vlak si potom
/// pri zastávke len prečíta StationInstance.Factories.
/// </summary>
public class StationInstance
{
    /// <summary>Tile súradnice samotnej dlaždice stanice (tileID == 2).</summary>
    public int TileX { get; private set; }
    public int TileZ { get; private set; }

    /// <summary>Fiktívna 9×9 zóna so stanicou v strede.</summary>
    public StationCatchment Catchment { get; private set; }

    /// <summary>
    /// Továrne, ktorých footprint zasahuje do catchmentu tejto stanice.
    /// Môže byť prázdny (stanica bez tovární v okolí), 1 alebo viac.
    /// Napĺňa StationRegistry.RescanFactories().
    /// </summary>
    public readonly List<FactoryInstance> Factories = new List<FactoryInstance>();

    public StationInstance(int tileX, int tileZ)
    {
        TileX = tileX;
        TileZ = tileZ;
        Catchment = new StationCatchment(tileX, tileZ);
    }

    /// <summary>Stabilný kľúč stanice (zhodný s formátom v StationRegistry).</summary>
    public long Key => ((long)TileX << 32) | (uint)TileZ;

    /// <summary>True, ak stanica eviduje aspoň jednu továreň.</summary>
    public bool HasFactories => Factories.Count > 0;

    /// <summary>
    /// Prejde evidované továrne a vráti PRVÚ, ktorá má pre danú surovinu
    /// VÝDAJOVÝ slot (UnLoad) s nenulovým množstvom – t.j. odkiaľ sa dá
    /// surovina naložiť. Vráti null, ak žiadna taká nie je.
    /// </summary>
    public FactoryInstance FindSupplier(ResourceType type)
    {
        foreach (var f in Factories)
        {
            var slot = f.GetUnLoadSlot(type);
            if (slot != null) return f;
        }
        return null;
    }

    /// <summary>
    /// Prejde evidované továrne a vráti PRVÚ, ktorá má pre danú surovinu
    /// PRÍJMOVÝ slot (Load) – t.j. kam sa dá surovina vyložiť. Vráti null,
    /// ak žiadna taká nie je.
    /// </summary>
    public FactoryInstance FindConsumer(ResourceType type)
    {
        foreach (var f in Factories)
        {
            var slot = f.GetLoadSlot(type);
            if (slot != null) return f;
        }
        return null;
    }

    public override string ToString()
        => $"StationInstance [{TileX},{TileZ}] (tovární v dosahu: {Factories.Count})";
}

// ==================================================================
// STATION REGISTRY – centrálna evidencia staníc
// ==================================================================

/// <summary>
/// StationRegistry
/// ------------------------------------------------------------------
/// Centrálna evidencia VŠETKÝCH staníc na mape – analógia k FactoryRegistry.
///
/// Tile mapa (IndicatrixAPI) drží len to, že na nejakom tile je tileID == 2
/// (stanica). Tento register navyše pre každú stanicu drží StationInstance
/// s jej 9×9 catchmentom a evidenciou tovární.
///
/// PREČO STATIC:
///   Mapa je herne jedinečná (jedna sada staníc), rovnako ako FactoryRegistry.
///   Static prístup je konzistentný s FactoryRegistry a netreba riešiť
///   serializáciu MonoBehaviour referencie.
///
/// KEDY VOLAŤ RescanFactories():
///   - po položení / zbúraní stanice,
///   - po položení / zbúraní továrne.
///   Najpohodlnejšie je zavolať RescanAll() z TrainSystem.OnMapChanged()
///   (alebo z GameManagera po úspešnom SetTile pre stanicu/továreň) –
///   scan 9×9 × počet staníc × počet tovární je lacný.
/// </summary>
public static class RailStationRegistry
{
    /// <summary>Stanica podľa kľúča tile [x,z].</summary>
    private static readonly Dictionary<long, StationInstance> stations
        = new Dictionary<long, StationInstance>();

    private static long TileKey(int x, int z) => ((long)x << 32) | (uint)z;

    // =====================================================================
    // AUTOMATICKÝ RESET PRI NOVEJ SCÉNE  (MainMenu → New Game)
    // ─────────────────────────────────────────────────────────────────────
    // Rovnaká oprava ako vo FactoryRegistry (FactorySystem.cs). Register je
    // STATIC, takže jeho obsah prežíva prechod scén – po návrate do MainMenu
    // a spustení "New Game" v ňom ostávali stanice z predchádzajúceho sedenia.
    //
    // Clear() sa pritom NEVOLAL NIKDE v celom projekte, takže register nemal
    // ako sa vyčistiť. RescanAll() síce cez SyncWithTileMap() staré stanice
    // nakoniec vyradí, ale AŽ keď ho niekto zavolá – a niektoré miesta sa
    // pýtajú REGISTRA SKÔR, než rescan prebehne:
    //
    //     • TrainSystem.TryExecuteTradeAtStation() – najprv GetStationAt(...)
    //       a RescanAll() spustí len vtedy, keď vráti null.
    //
    //   V takom prípade by dostali StationInstance z minulej hry aj s jej
    //   zoznamom Factories, ktorý ukazuje na FactoryInstance z minulej hry.
    //
    // Preto sa register čistí SÁM pri každom načítaní scény (Single mode).
    // Poradie je bezpečné: sceneLoaded beží PO Awake() objektov novej scény,
    // ale ešte PRED ich Start(), a stanice sa registrujú až neskôr – pri
    // položení hráčom, pri rescane, alebo pri načítaní hry (IndicatrixAPI
    // volá RescanAll() z LoadGame, teda z korutiny spustenej v Start()).
    //
    // SubsystemRegistration navyše zaručí korektný stav aj pri vypnutom
    // Domain Reload ("Enter Play Mode Options") v editore.
    // =====================================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewPlaySession()
    {
        stations.Clear();

        // Odhlásiť + prihlásiť = idempotentné (nikdy nevznikne dvojitý odber).
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Additívne načítanie scény nie je zmena mapy – register nechávame tak.
        if (mode != LoadSceneMode.Single) return;
        if (stations.Count == 0) return;

        int stale = stations.Count;
        stations.Clear();

        Debug.Log($"[RailStationRegistry] Načítaná scéna '{scene.name}' – zahodených " +
                  $"{stale} staníc z predchádzajúceho sedenia (nový register je prázdny).");
    }

    /// <summary>Všetky aktuálne evidované stanice (len na čítanie).</summary>
    public static IEnumerable<StationInstance> All => stations.Values;

    /// <summary>Počet evidovaných staníc.</summary>
    public static int Count => stations.Count;

    /// <summary>
    /// Vráti StationInstance pre tile [x,z], alebo null, ak na tom tile
    /// žiadna evidovaná stanica nie je.
    /// </summary>
    public static StationInstance GetStationAt(int x, int z)
    {
        return stations.TryGetValue(TileKey(x, z), out var st) ? st : null;
    }

    /// <summary>
    /// Zabezpečí, že pre tile [x,z] existuje StationInstance. Ak ešte
    /// neexistuje, vytvorí ju (a hneď jej catchment). Vráti inštanciu.
    ///
    /// Voláva sa pri položení stanice, alebo lazy z RescanAll(), ktorý si
    /// stanice sám doplní podľa tile mapy.
    /// </summary>
    public static StationInstance Register(int x, int z)
    {
        long key = TileKey(x, z);
        if (stations.TryGetValue(key, out var existing))
            return existing;

        var st = new StationInstance(x, z);
        stations[key] = st;
        Debug.Log($"[RailStationRegistry] Zaregistrovaná {st}. Spolu staníc: {stations.Count}.");
        return st;
    }

    /// <summary>Odregistruje stanicu na tile [x,z] (napr. pri demolish).</summary>
    public static bool Unregister(int x, int z)
    {
        long key = TileKey(x, z);
        if (!stations.Remove(key)) return false;
        Debug.Log($"[RailStationRegistry] Odregistrovaná stanica [{x},{z}]. Spolu staníc: {stations.Count}.");
        return true;
    }

    /// <summary>Vymaže celý register (napr. pri načítaní novej mapy).</summary>
    public static void Clear()
    {
        stations.Clear();
        Debug.Log("[RailStationRegistry] Register vyčistený.");
    }

    // ------------------------------------------------------------------
    // SCAN – priradenie tovární staniciam
    // ------------------------------------------------------------------

    /// <summary>
    /// Pre JEDNU stanicu prejde všetky položené továrne (FactoryRegistry)
    /// a do StationInstance.Factories vloží tie, ktorých footprint zasahuje
    /// do 9×9 catchmentu stanice.
    ///
    /// Volá sa interne z RescanAll, ale dá sa zavolať aj samostatne, ak sa
    /// zmenil len jeden uzol.
    /// </summary>
    public static void RescanFactories(StationInstance station)
    {
        if (station == null) return;

        station.Factories.Clear();

        foreach (var factory in FactoryRegistry.All)
        {
            if (factory == null) continue;

            bool inZone = station.Catchment.OverlapsFootprint(
                factory.OriginX, factory.OriginZ,
                factory.Width, factory.Depth);

            if (inZone)
                station.Factories.Add(factory);
        }

        Debug.Log($"[RailStationRegistry] {station} – po scane eviduje {station.Factories.Count} tovární.");
    }

    /// <summary>
    /// KOMPLETNÝ (RE)SCAN.
    ///
    /// 1) Synchronizuje register staníc s tile mapou:
    ///    - dorovná stanice, ktoré sú na mape (tileID == 2), ale ešte nie
    ///      sú v registri (napr. načítaná uložená hra),
    ///    - vyradí stanice z registra, ktoré na mape už nie sú (zbúrané).
    ///
    /// 2) Pre každú stanicu prepočíta zoznam tovární v jej catchmente.
    ///
    /// Lacná operácia – kľudne sa volá pri každej zmene mapy.
    /// </summary>
    public static void RescanAll()
    {
        SyncWithTileMap();

        foreach (var st in stations.Values)
            RescanFactories(st);
    }

    /// <summary>
    /// Dorovná register staníc podľa aktuálnej tile mapy. Stanica je tile
    /// s tileID == 2. Číta sa cez IndicatrixAPI.GetTileByIndex.
    ///
    /// Bez tohto kroku by register poznal len stanice, ktoré niekto ručne
    /// zaregistroval. SyncWithTileMap zaručí, že stačí položiť stanicu na
    /// mapu (tile engine) a register sa sám doplní pri najbližšom scane.
    /// </summary>
    private static void SyncWithTileMap()
    {
        var api = IndicatrixAPI.instance;
        if (api == null)
        {
            Debug.LogWarning("[RailStationRegistry] IndicatrixAPI.instance == null – scan preskočený.");
            return;
        }

        // GRID_SIZE je v IndicatrixAPI privátne; 256 je fixná veľkosť gridu
        // (zhodná s TrainSystem.GRID_SIZE). Ak sa zmení, uprav aj tu.
        const int GRID = 256;

        // 1) Pridaj chýbajúce stanice (tile == 2, ale nie v registri).
        for (int x = 0; x < GRID; x++)
        {
            for (int z = 0; z < GRID; z++)
            {
                var tile = api.GetTileByIndex(x, z);
                if (tile.tileID == 2 && !stations.ContainsKey(TileKey(x, z)))
                    Register(x, z);
            }
        }

        // 2) Odober stanice, ktoré na mape už nie sú (zbúrané).
        //    Zbieramе do pomocného zoznamu, lebo nemožno mazať počas iterácie.
        List<long> toRemove = null;
        foreach (var kvp in stations)
        {
            var st = kvp.Value;
            var tile = api.GetTileByIndex(st.TileX, st.TileZ);
            if (tile.tileID != 2)
            {
                if (toRemove == null) toRemove = new List<long>();
                toRemove.Add(kvp.Key);
            }
        }
        if (toRemove != null)
        {
            foreach (var key in toRemove)
            {
                stations.Remove(key);
                Debug.Log($"[RailStationRegistry] Stanica s kľúčom {key} už nie je na mape – odstránená.");
            }
        }
    }
}
