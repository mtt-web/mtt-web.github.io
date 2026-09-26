/// <summary>
/// GameErrors
/// ─────────────────────────────────────────────────────────────────────────
/// Centrálny KATALÓG chybových hlášok hry – jedno miesto pravdy pre texty,
/// ktoré sa zobrazujú v univerzálnom okne StatusErrorMenuUI.
///
/// PREČO TENTO SÚBOR EXISTUJE (a prečo to NIE JE "ErrorLog"):
///   Chyby sa v hre VYVOLÁVAJÚ na MIESTE incidentu – tam, kde reálne vznikli
///   (napr. v GameManager.OnClick pri pokuse o stavbu na obsadený tile).
///   Tento súbor NEROBÍ žiadne logovanie, neeviduje históriu chýb ani nič
///   podobné. Drží len KONŠTANTNÉ reťazce hlášok, aby:
///     • sa rovnaké znenie neopakovalo ako "magic string" na viacerých
///       miestach (a nerozišlo sa preklepmi),
///     • boli všetky definované chyby prehľadne na jednom mieste,
///     • sa text dal zmeniť (napr. lokalizácia) bez hľadania po celom kóde.
///
/// POUŽITIE (na mieste incidentu):
///     StatusErrorMenuUI.OpenWithError(GameErrors.CannotBuildOnOccupiedTile);
///
/// PRIDANIE NOVEJ CHYBY:
///   Stačí pridať ďalšiu public const string s výstižným názvom a anglickým
///   textom. Žiadna ďalšia infraštruktúra netreba – okno (StatusErrorMenuUI)
///   ostáva to isté, mení sa iba odovzdaný text.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public static class GameErrors
{
    /// <summary>
    /// Hráč sa pokúsil postaviť (SetTile s tileID != 0) na tile, ktorý je už
    /// obsadený iným tile. Stavba je zablokovaná, prepísanie nie je možné –
    /// hráč musí najprv políčko zbúrať (Demolish, SetTile = 0).
    /// </summary>
    public const string CannotBuildOnOccupiedTile =
        "This tile is already occupied.";

    /// <summary>
    /// Hráč sa pokúsil postaviť stanicu (RAIL alebo ROAD, tileID == 2) v
    /// tesnej blízkosti už existujúcej stanice. Okolo každej stanice je
    /// neviditeľná ochranná zóna (predvolene región 3×3 vycentrovaný na
    /// stanici) – do tejto zóny nie je možné umiestniť ďalšiu stanicu.
    /// Platí naprieč kategóriami: RAIL stanica blokuje aj ROAD stanicu a naopak.
    /// </summary>
    public const string CannotBuildStationNearStation =
        "Cannot build a station too close to another station.";

    /// <summary>
    /// Hráč sa pokúsil postaviť stanicu (RAIL alebo ROAD) na tile, ktorý NIE JE
    /// vodorovný – t.j. 4 vertexy jeho face nemajú rovnaké Y (svah/kopec).
    /// Stanice sa smú stavať len na rovine.
    /// </summary>
    public const string CannotBuildStationOnTerrain =
        "Stations can only be built on flat terrain.";

    /// <summary>
    /// Analógia k CannotBuildStationOnTerrain pre depá (RAIL alebo ROAD).
    /// Depo sa smie stavať len na vodorovnom tile (4 vertexy s rovnakým Y).
    /// </summary>
    public const string CannotBuildDepotOnTerrain =
        "Depots can only be built on flat terrain.";

    /// <summary>
    /// Hráč sa pokúsil upraviť terén (LevelUp/LevelDown) tak, že by sa zmenil
    /// aspoň jeden vertex obsadeného tile (tileID != 0). Keďže vrchol je
    /// zdieľaný až 4 tilmi, blokuje sa to aj keď hráč klikol „vedľa“
    /// obsadeného tile – stačí, že upravovaný vrchol patrí obsadenému face.
    /// </summary>
    public const string CannotEditTerrainOccupied =
        "Cannot modify terrain under an existing structure.";

    /// <summary>
    /// Hráč sa pokúsil postaviť koľajový/cestný diel na nevhodnom type terénu:
    ///   • rovné diely (RailHorizontal/Vertical, RoadHorizontal/Vertical) sa
    ///     smú stavať na rovine ALEBO na šikmom tile (rampe),
    ///   • ostatné diely (crossroad, curves, switches – RAIL aj ROAD) len na
    ///     rovine.
    /// Ak tile nevyhovuje, stavba sa zablokuje touto hláškou.
    /// </summary>
    public const string CannotBuildTrackOnTerrain =
        "Cannot build this track type on this terrain.";

    /// <summary>
    /// Hráč sa pokúsil cez LevelUp navýšiť terén nad maximálnu povolenú úroveň
    /// (TerrainManager.MaxElevationLevel = 10, t.j. Y = 5.5f). Ďalšie navýšenie
    /// je zablokované.
    /// </summary>
    public const string MaxTerrainElevationReached =
        "Maximum terrain elevation reached.";

    /// <summary>
    /// Hráč sa pokúsil cez LevelDown znížiť terén pod minimálnu povolenú úroveň
    /// (TerrainManager.MinElevationLevel = -1, t.j. Y = 2.75f). Ďalšie zníženie
    /// je zablokované.
    /// </summary>
    public const string MinTerrainElevationReached =
        "Minimum terrain elevation reached.";

    /// <summary>
    /// Hráč sa pokúsil postaviť továreň (FACTORY/PROCESSING, tileID 4/5) tak,
    /// že aspoň jeden tile jej footprintu prekrýva už existujúcu inú továreň.
    /// Stavba na obsadenom mieste továrňou je zakázaná – hráč musí najprv
    /// pôvodnú továreň zbúrať (RAIL/ROAD Demolish nad ľubovoľným jej tile).
    /// </summary>
    public const string CannotBuildFactoryOnFactory =
        "Cannot build a factory here. Another factory already occupies this area.";

    /// <summary>
    /// Hráč sa pokúsil postaviť továreň v tesnej blízkosti už existujúcej
    /// továrne. Okolo každej továrne je neviditeľná ochranná zóna (Chebyshev
    /// región s polomerom FACTORY_EXCLUSION_RADIUS, predvolene 3) – do tejto
    /// zóny nie je možné umiestniť ďalšiu továreň. Analógia ku
    /// CannotBuildStationNearStation pre stanice.
    /// </summary>
    public const string CannotBuildFactoryNearFactory =
        "Cannot build a factory too close to another factory.";

    /// <summary>
    /// Univerzálna hláška pre konflikt medzi výstavbovými systémami:
    ///   • hráč chce položiť koľaj/cestu/stanicu/depo/výhybku na tile, ktorý
    ///     už zaberá TOVÁREŇ, alebo
    ///   • hráč chce položiť TOVÁREŇ na tile, ktorý už zaberá koľaj/cesta/
    ///     stanica/depo (RAIL alebo ROAD).
    /// V oboch smeroch je prepísanie zakázané – existujúci prvok treba najprv
    /// zbúrať.
    /// </summary>
    public const string CannotBuildOnExisting =
        "Cannot build here. Another structure already exists.";

    /// <summary>
    /// Hráč je v režime Staff→Factory (po kliku na SM...HireButton sa zavrelo
    /// bázické okno a čaká sa na výber továrne) a klikol na tile, ktorý NIE JE
    /// továreň. Pridelenie personálu sa neuskutoční – hráč ostáva v režime a
    /// môže kliknúť znova na ľubovoľnú továreň.
    /// </summary>
    public const string StaffTargetNotAFactory =
        "Staff can only be assigned to factories.";

    /// <summary>
    /// Hráč použil Demolish z nesprávneho dopravného systému na cudzom tile:
    ///   • RAIL Demolish na tile patriaci ROAD systému (TileCategory.Road), alebo
    ///   • ROAD Demolish na tile patriaci RAIL systému (TileCategory.Rail).
    /// Demolácia sa neuskutoční – hráč musí prepnúť na zodpovedajúce menu
    /// (RAIL prvky búrať RAIL Demolish, ROAD prvky ROAD Demolish). Rovnaká
    /// hláška platí pre oba smery konfliktu.
    /// </summary>
    public const string CannotDemolishWrongSystem =
        "Wrong demolition tool. Select the correct transport system.";

    /// <summary>
    /// Hráč sa pokúsil postaviť (RAIL / ROAD / FACTORY) na tile, na ktorom STOJÍ
    /// MESTSKÁ BUDOVA. Mestské budovy NIE SÚ v tileGrid (IndicatrixAPI) – vedie ich
    /// CityManager – preto sa kontrolujú samostatne cez CityManager.IsCityTile.
    ///
    /// POZN.: Blokuje sa LEN konkrétny tile s budovou, NIE celý región mesta.
    /// Hráč teda smie stavať na voľných (prázdnych) tiloch vnútri regiónu mesta,
    /// ale nie tam, kde reálne stojí budova.
    /// </summary>
    public const string CannotBuildOnBuilding =
        "Cannot build here. A building already occupies this tile.";

    /// <summary>
    /// Hráč sa pokúsil postaviť ďalšiu stanicu (RAIL alebo ROAD) v teritóriu
    /// mesta, ktoré už vyčerpalo svoj zoznam názvov staníc. Každé mesto má
    /// fixný počet prípon (Wordlist, predvolene 20) → max. 20 staníc na mesto
    /// (RAIL + ROAD spolu). Po vyčerpaní sa ďalšia stanica v tomto meste
    /// nepostaví, kým hráč nejakú existujúcu stanicu mesta nezbúra.
    /// </summary>
    public const string CannotBuildMoreStationsForCity =
        "This city cannot support any more stations.";

    /// <summary>
    /// Hráč sa pokúsil postaviť čokoľvek (RAIL / ROAD / FACTORY – koľaj, cesta,
    /// stanica, depo, výhybka, crossroad, alebo footprint továrne) na tile,
    /// ktorý leží na alebo pod hladinou vody (aspoň jeden roh face má Y ≤
    /// TerrainManager.MinTerrainHeight). Stavba je zablokovaná – hráč musí
    /// najprv terén vyplniť nad hladinu vody (LevelUp), až potom je možné
    /// na danom tile stavať.
    /// </summary>
    public const string CannotBuildOnWater =
        "Cannot build on water. Raise the terrain above water level first.";

    /// <summary>
    /// Hráč sa pokúsil upraviť terén (LevelUp/LevelDown) klikom na OKRAJOVÝ
    /// vrchol mapy – teda na vrchol ležiaci na poslednom (vonkajšom) prstenci
    /// mriežky terénu (vertex index 0 alebo terrainWidth v osi X či Z).
    ///
    /// Okrajové vrcholy tvoria hranicu mesh-u; ich posun by roztrhol obvod mapy
    /// (vertikálna stena po obvode) a zároveň by kaskáda TerrainCollapse nemala
    /// kam pokračovať za hranicou. Preto sú tieto vrcholy pre editor terénu
    /// zamknuté – hráč musí kliknúť ďalej od kraja mapy.
    ///
    /// Šírku chráneného pásu určuje TerrainManager.ProtectedBorderRings.
    /// </summary>
    public const string CannotEditTerrainAtMapEdge =
        "Cannot modify terrain this close to the map edge.";

    /// <summary>
    /// OZNÁMENIE (nie chyba): hra bola úspešne uložená do jediného save súboru
    /// "Assets/SaveGame/savegame.dat". Zobrazuje sa v tom istom univerzálnom okne
    /// StatusErrorMenuUI ako chyby – okno je generické, mení sa iba text. Slúži
    /// ako spätná väzba hráčovi po kliknutí na Save v in-game menu.
    /// </summary>
    public const string GameSavedSuccessfully =
        "The game was saved successfully.";

    // ── Sem pribúdajú ďalšie chyby, ako budú vznikať na rôznych miestach hry ──
    // public const string SomeOtherError = "....";

    /// <summary>
    /// Hráč sa pokúsil postaviť (RAIL / ROAD / FACTORY) na tile, na ktorom stojí
    /// STROM alebo KAMEŇ (environmentálny prefab). Tieto objekty NIE SÚ v tileGrid
    /// (IndicatrixAPI) – vedie ich EnvironmentManager – preto sa kontrolujú
    /// samostatne cez EnvironmentManager.IsEnvironmentTile.
    ///
    /// Strom a kameň sa dajú odstrániť cez Demolish (zadarmo), potom je tile voľný.
    /// </summary>
    public const string CannotBuildOnEnvironment =
        "Cannot build here. Remove the tree or rock with the Demolish tool first.";

    /// <summary>
    /// Hráč sa pokúsil postaviť čokoľvek na tile, ktorý zaberá LANDING LOCATION
    /// (footprint 2×2 = 4 tile). Landing locations sú pri generovaní FIXNÉ –
    /// nedajú sa ani zastavať, ani zbúrať.
    /// </summary>
    public const string CannotBuildOnLandingLocation =
        "Cannot build here. This landing location is permanent.";

    /// <summary>
    /// Hráč použil Demolish na tile s LANDING LOCATION. Na rozdiel od stromov a
    /// kameňov sa landing location odstrániť NEDÁ.
    /// </summary>
    public const string CannotDemolishLandingLocation =
        "This landing location cannot be demolished.";

    /// <summary>
    /// Hráč sa pokúsil upraviť terén (LevelUp/LevelDown) tak, že by sa zmenil
    /// vrchol tile so stromom, kameňom alebo landing location. Keďže vrchol je
    /// zdieľaný až 4 tilmi, blokuje sa to aj keď hráč klikol „vedľa“.
    /// Analógia ku CannotEditTerrainOccupied pre environmentálne objekty.
    /// </summary>
    public const string CannotEditTerrainOnEnvironment =
        "Cannot modify terrain under trees, rocks or a landing location.";

    /// <summary>
    /// Hráč sa pokúsil cez RAIL Demolish zbúrať koľajový prvok (koľaj, výhybku,
    /// stanicu, crossroad), na ktorom sa PRÁVE NACHÁDZA VLAK. Kým je súprava
    /// (lokomotíva alebo ktorýkoľvek vagón) nad daným tile, demolácia je
    /// zablokovaná – hráč musí počkať, kým vlak prejde, alebo ho vrátiť do depa.
    /// </summary>
    public const string CannotDemolishTrackWithTrain =
        "Cannot demolish this track. A train is currently on it.";

    /// <summary>
    /// Analógia ku CannotDemolishTrackWithTrain pre cestný systém: hráč sa
    /// pokúsil cez ROAD Demolish zbúrať cestný prvok (cesta, stanica, crossroad),
    /// na ktorom sa práve nachádza vozidlo. Demolácia je zablokovaná, kým
    /// vozidlo tile neopustí (alebo sa nevráti do depa).
    /// </summary>
    public const string CannotDemolishRoadWithVehicle =
        "Cannot demolish this road. A vehicle is currently on it.";

    // =====================================================================
    // MOSTY A TUNELY (RAIL) – RailCrossingSystem
    // =====================================================================

    /// <summary>
    /// Hráč klikol v režime TUNEL na tile, kde tunel postaviť nemožno:
    ///   • tile nie je rampa (šikmá plocha 1 tile), alebo
    ///   • v smere stúpania sa nenašiel náprotivok – opačne orientovaná rampa
    ///     s rovnakou dolnou hranou, kde by vlak mohol vyjsť, alebo
    ///   • tunel by bol kratší než minimum (napr. úzky hrebeň).
    /// </summary>
    public const string CannotBuildTunnelOnTerrain =
        "A tunnel cannot be built on this type of terrain.";

    /// <summary>
    /// Náprotivok tunela sa nenašiel v rámci maximálnej dĺžky tunela
    /// (RailCrossingSystem.maxTunnelLength).
    /// </summary>
    public const string CannotBuildTunnelTooLong =
        "The tunnel would be too long.";

    /// <summary>Nový tunel by pretínal už existujúci tunel.</summary>
    public const string CannotBuildTunnelThroughTunnel =
        "This tunnel would cross another tunnel.";

    /// <summary>
    /// Dva kliknuté tily mosta si nezodpovedajú: nie sú obe vodorovné v
    /// rovnakej výške, ani to nie sú dve navzájom opačné rampy s rovnakou
    /// hornou hranou (prípadne tile nie je ani rovina, ani rampa).
    /// </summary>
    public const string CannotBuildBridgeOnTerrain =
        "A bridge cannot be built between these tiles.";

    /// <summary>
    /// Hráč sa pokúsil postaviť most (RAIL alebo ROAD) medzi DVOMA VODOROVNÝMI
    /// tilmi (4 vertexy oboch hláv majú rovnaké Y). Most sa smie stavať len
    /// medzi dvomi šikmými tilmi (rampami). Ovláda CrossingSystemBase.allowFlatBridges.
    /// </summary>
    public const string CannotBuildBridgeOnFlatTerrain =
        "A bridge cannot be built between two flat tiles. Both ends must be on slopes.";

    /// <summary>Začiatok a koniec mosta neležia v jednej priamke (os X alebo Z).</summary>
    public const string CannotBuildBridgeNotStraight =
        "A bridge must be built in a straight horizontal or vertical line.";

    /// <summary>
    /// Dĺžka mosta mimo povoleného rozsahu. POZOR: čísla v texte musia
    /// zodpovedať CrossingSystemBase.MinBridgeLength / MaxBridgeLength (RAIL aj ROAD).
    /// </summary>
    public const string CannotBuildBridgeLength =
        "A bridge must be between 3 and 15 tiles long.";

    /// <summary>
    /// Pod mostom je niečo vyššie než mostovka (terén, koľaj bez dostatočnej
    /// svetlej výšky, mestská budova vyššia než mostovka) alebo objekt, ktorý
    /// pod mostom nesmie byť nikdy (továreň, strom, kameň).
    /// </summary>
    public const string CannotBuildBridgeObstacle =
        "Cannot build a bridge here. An obstacle below is too high.";

    /// <summary>
    /// Most by bol príliš nízko: mostovka je nad najnižším bodom terénu pod
    /// mostom menej než CrossingSystemBase.minBridgeHeight (predvolene 0.5 =
    /// 2 úrovne). Typicky most cez plytké údolie hlboké len 1 úroveň.
    /// POZOR: číslo v texte musí zodpovedať nastaveniu Min Bridge Height
    /// na RailCrossingSystem / RoadCrossingSystem.
    /// </summary>
    public const string CannotBuildBridgeTooLow =
        "Cannot build a bridge here. A bridge must be at least 2 levels above the terrain below (over water at least 1 level).";

    /// <summary>Nový most by križoval iný most.</summary>
    public const string CannotBuildBridgeOverBridge =
        "This bridge would cross another bridge.";

    /// <summary>
    /// Stavba (koľaj, cesta, stanica, depo, továreň, hlava mosta/tunela) pod
    /// existujúcim mostom bez dostatočnej svetlej výšky.
    /// </summary>
    public const string CannotBuildUnderBridge =
        "Cannot build here. There is not enough clearance under the bridge.";

    /// <summary>
    /// Demolish mosta/tunela, na ktorom sa práve nachádza vlak. Keďže sa most
    /// aj tunel búrajú vždy CELÉ, blokuje ich vlak kdekoľvek na prechode.
    /// </summary>
    public const string CannotDemolishCrossingWithTrain =
        "Cannot demolish this bridge or tunnel. A train is currently on it.";

    /// <summary>
    /// ROAD analógia k CannotDemolishCrossingWithTrain – na cestnom moste /
    /// v cestnom tuneli sa práve nachádza vozidlo.
    /// </summary>
    public const string CannotDemolishCrossingWithVehicle =
        "Cannot demolish this bridge or tunnel. A vehicle is currently on it.";

    /// <summary>
    /// Úprava terénu by zasiahla most (LevelUp pod mostovkou) alebo tunel
    /// (LevelDown nad tunelom – odkrytie tunela).
    /// </summary>
    public const string CannotEditTerrainNearCrossing =
        "Cannot modify terrain under a bridge or above a tunnel.";

    // =====================================================================
    // ZMIEŠANÉ KRIŽOVATKY RAIL + ROAD (úrovňové prejazdy)
    // =====================================================================

    /// <summary>
    /// Hráč sa pokúsil položiť ľubovoľný RAIL alebo ROAD diel na tile, na
    /// ktorom už je zmiešaná križovatka. Najprv treba jednu z častí zbúrať
    /// (RAIL Demolish ponechá cestu, ROAD Demolish ponechá koľaj).
    /// </summary>
    public const string CannotBuildOnLevelCrossing =
        "Cannot build here. There is already a level crossing.";

    /// <summary>
    /// Priamu koľaj a priamu cestu nemožno spojiť ROVNOBEŽNE – zmiešaná
    /// križovatka vznikne len vtedy, keď sa križujú kolmo
    /// (RailHorizontal × RoadVertical alebo RailVertical × RoadHorizontal).
    /// </summary>
    public const string CannotBuildLevelCrossingParallel =
        "A railway and a road can only cross at a right angle.";

    /// <summary>
    /// Zmiešaná križovatka sa smie vytvoriť len na VODOROVNOM tile – nie na
    /// rampe (analógia k CannotBuildTrackOnTerrain pre ostatné križovatky).
    /// </summary>
    public const string CannotBuildLevelCrossingOnTerrain =
        "A level crossing can only be built on flat terrain.";

}
