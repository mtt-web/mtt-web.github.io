using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// RoadStationSystem.cs
/// ------------------------------------------------------------------
/// DÁTOVÁ + EVIDENČNÁ VRSTVA PRE CESTNÉ STANICE (zastávky vozidiel).
///
/// Toto je CESTNÝ EKVIVALENT triedy RailStationRegistry zo RailStationSystem.cs –
/// presne tak, ako je VehicleSystem.cs cestným ekvivalentom TrainSystem.cs.
///
/// ------------------------------------------------------------------
/// PREČO SAMOSTATNÝ REGISTER A NIE LEN RailStationRegistry?
///
/// Železnice a cesty ZDIEĽAJÚ jeden tile grid v IndicatrixAPI, ale líšia sa
/// kategóriou (TileCategory.Rail vs TileCategory.Road). Stanica RAIL aj
/// stanica ROAD majú zhodou okolností rovnaké tileID == 2 – rozlišuje ich
/// IBA kategória.
///
/// RailStationRegistry (RAIL) skenuje mapu cez IndicatrixAPI.GetTileByIndex(),
/// ktorá ZÁMERNE maskuje ROAD tiles (vracia prázdny tile pre Road kategóriu).
/// Preto by RAIL register cestné stanice NIKDY nenašiel. Cestný register
/// preto musí skenovať cez GetTileByIndexAny() a sám filtrovať
/// TileCategory.Road – čo je presne to, čo robí SyncWithTileMap() nižšie.
///
/// Samotné dátové štruktúry StationCatchment a StationInstance sú
/// KATEGÓRIOVO NEUTRÁLNE (sú to len geometria + zoznam tovární), takže sa
/// BEZ ZMENY znovu používajú aj tu. Duplikuje sa len evidenčná vrstva
/// (register + sken), ktorá musí poznať kategóriu.
///
/// ------------------------------------------------------------------
/// PRAVIDLO EVIDENCIE TOVÁRNE (zhodné s RAIL):
///   Továreň patrí cestnej stanici, ak ASPOŇ JEDEN tile jej footprintu
///   leží v 9×9 catchmente stanice. Továrne sú UNIVERZÁLNE pre vlaky aj
///   vozidlá (FactorySystem.cs / FactoryRegistry), takže sa berú z toho
///   istého FactoryRegistry ako pri železniciach.
///
/// KEDY VOLAŤ RescanFactories() / RescanAll():
///   - po položení / zbúraní cestnej stanice,
///   - po položení / zbúraní továrne.
///   Najpohodlnejšie je zavolať RescanAll() z VehicleSystem.TriggerMapChangedReroute()
///   (analógia k TrainSystem.TriggerMapChangedReroute(), ktorý volá
///   StationRegistry.RescanAll()).
/// ------------------------------------------------------------------
/// </summary>
public static class RoadStationRegistry
{
    /// <summary>Cestná stanica podľa kľúča tile [x,z].</summary>
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
    //     • VehicleSystem.TryExecuteTradeAtStation() – najprv GetStationAt(...)
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

        Debug.Log($"[RoadStationRegistry] Načítaná scéna '{scene.name}' – zahodených " +
                  $"{stale} staníc z predchádzajúceho sedenia (nový register je prázdny).");
    }

    /// <summary>Všetky aktuálne evidované cestné stanice (len na čítanie).</summary>
    public static IEnumerable<StationInstance> All => stations.Values;

    /// <summary>Počet evidovaných cestných staníc.</summary>
    public static int Count => stations.Count;

    /// <summary>
    /// Vráti StationInstance pre cestný tile [x,z], alebo null, ak na tom
    /// tile žiadna evidovaná cestná stanica nie je.
    /// </summary>
    public static StationInstance GetStationAt(int x, int z)
    {
        return stations.TryGetValue(TileKey(x, z), out var st) ? st : null;
    }

    /// <summary>
    /// Zabezpečí, že pre tile [x,z] existuje StationInstance. Ak ešte
    /// neexistuje, vytvorí ju (a hneď jej 9×9 catchment). Vráti inštanciu.
    ///
    /// Voláva sa pri položení cestnej stanice, alebo lazy z RescanAll(),
    /// ktorý si stanice sám doplní podľa tile mapy.
    /// </summary>
    public static StationInstance Register(int x, int z)
    {
        long key = TileKey(x, z);
        if (stations.TryGetValue(key, out var existing))
            return existing;

        var st = new StationInstance(x, z);
        stations[key] = st;
        Debug.Log($"[RoadStationRegistry] Zaregistrovaná cestná {st}. Spolu staníc: {stations.Count}.");
        return st;
    }

    /// <summary>Odregistruje cestnú stanicu na tile [x,z] (napr. pri demolish).</summary>
    public static bool Unregister(int x, int z)
    {
        long key = TileKey(x, z);
        if (!stations.Remove(key)) return false;
        Debug.Log($"[RoadStationRegistry] Odregistrovaná cestná stanica [{x},{z}]. Spolu staníc: {stations.Count}.");
        return true;
    }

    /// <summary>Vymaže celý register (napr. pri načítaní novej mapy).</summary>
    public static void Clear()
    {
        stations.Clear();
        Debug.Log("[RoadStationRegistry] Register vyčistený.");
    }

    // ------------------------------------------------------------------
    // SCAN – priradenie tovární cestným staniciam
    // ------------------------------------------------------------------

    /// <summary>
    /// Pre JEDNU cestnú stanicu prejde všetky položené továrne
    /// (FactoryRegistry – univerzálny pre RAIL aj ROAD) a do
    /// StationInstance.Factories vloží tie, ktorých footprint zasahuje do
    /// 9×9 catchmentu stanice.
    ///
    /// Identický algoritmus ako StationRegistry.RescanFactories – stanice
    /// aj továrne majú rovnakú geometriu bez ohľadu na kategóriu.
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

        Debug.Log($"[RoadStationRegistry] {station} – po scane eviduje {station.Factories.Count} tovární.");
    }

    /// <summary>
    /// KOMPLETNÝ (RE)SCAN cestných staníc.
    ///
    /// 1) Synchronizuje register staníc s tile mapou:
    ///    - dorovná cestné stanice, ktoré sú na mape (TileCategory.Road,
    ///      tileID == 2), ale ešte nie sú v registri,
    ///    - vyradí stanice z registra, ktoré na mape už nie sú (zbúrané
    ///      alebo prerobené na iný cestný prvok).
    ///
    /// 2) Pre každú stanicu prepočíta zoznam tovární v jej catchmente.
    ///
    /// Lacná operácia – kľudne sa volá pri každej zmene mapy
    /// (napr. z VehicleSystem.TriggerMapChangedReroute).
    /// </summary>
    public static void RescanAll()
    {
        SyncWithTileMap();

        foreach (var st in stations.Values)
            RescanFactories(st);
    }

    /// <summary>
    /// Dorovná register cestných staníc podľa aktuálnej tile mapy.
    ///
    /// Cestná stanica je tile s TileCategory.Road A SÚČASNE tileID == 2.
    /// Číta sa cez IndicatrixAPI.GetTileByIndexAny() – BEZ filtra kategórie,
    /// lebo GetTileByIndex() by ROAD tiles zamaskoval (pozri komentár v hlavičke).
    /// </summary>
    private static void SyncWithTileMap()
    {
        var api = IndicatrixAPI.instance;
        if (api == null)
        {
            Debug.LogWarning("[RoadStationRegistry] IndicatrixAPI.instance == null – scan preskočený.");
            return;
        }

        // GRID_SIZE je v IndicatrixAPI privátne; 256 je fixná veľkosť gridu
        // (zhodná s VehicleSystem.GRID_SIZE). Ak sa zmení, uprav aj tu.
        const int GRID = 256;

        // 1) Pridaj chýbajúce cestné stanice (Road kategória, tileID == 2,
        //    ale nie v registri).
        for (int x = 0; x < GRID; x++)
        {
            for (int z = 0; z < GRID; z++)
            {
                var tile = api.GetTileByIndexAny(x, z);
                if (tile.category == IndicatrixAPI.TileCategory.Road
                    && tile.tileID == 2
                    && !stations.ContainsKey(TileKey(x, z)))
                {
                    Register(x, z);
                }
            }
        }

        // 2) Odober stanice, ktoré na mape už nie sú cestnou stanicou.
        //    Zbierame do pomocného zoznamu, lebo nemožno mazať počas iterácie.
        List<long> toRemove = null;
        foreach (var kvp in stations)
        {
            var st = kvp.Value;
            var tile = api.GetTileByIndexAny(st.TileX, st.TileZ);
            bool stillStation = tile.category == IndicatrixAPI.TileCategory.Road
                                && tile.tileID == 2;
            if (!stillStation)
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
                Debug.Log($"[RoadStationRegistry] Cestná stanica s kľúčom {key} už nie je na mape – odstránená.");
            }
        }
    }
}
