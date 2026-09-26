using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.HID;
using UnityEngine.Splines;


public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    // =====================================================================
    // KURZOR MYŠI
    //
    // Statický vlastný kurzor pre celú hru. Sprite sa natiahne cez Inspector
    // (pole "Cursor Sprite"). Nastaví sa raz v Start() cez Cursor.SetCursor
    // a platí až do konca behu hry.
    //
    // POZOR – dôležité pre správne fungovanie:
    //  • Cursor.SetCursor vyžaduje Texture2D, nie Sprite. Sprite si preto
    //    nižšie prevádzame na textúru (ApplyCustomCursor / ExtractCursorTexture).
    //  • Ak je sprite samostatný obrázok (celá textúra = 1 sprite), použije sa
    //    priamo – funguje bez ďalších nastavení.
    //  • Ak je sprite výrez z atlasu, musíme pixely vykopírovať, čo vyžaduje
    //    zapnuté "Read/Write Enabled" na textúre v import settings. Preto sa
    //    ako kurzor odporúča samostatný PNG (Texture Type = Sprite).
    //  • CursorMode.Auto použije rýchlejší hardvérový kurzor, ktorý má však na
    //    niektorých platformách limit veľkosti (napr. 32×32). Pri väčšom sprite,
    //    ktorý sa nezobrazuje, prepni na ForceSoftware.
    // =====================================================================

    [Header("Kurzor myši")]
    [Tooltip("Sprite použitý ako kurzor myši v celej hre. Natiahni ho sem cez Inspector. " +
             "Ak ostane prázdny, ponechá sa systémový kurzor.")]
    [SerializeField] private Sprite cursorSprite;

    [Tooltip("Hotspot (aktívny bod) kurzora v pixeloch od ĽAVÉHO HORNÉHO rohu obrázka. " +
             "(0,0) = špička kurzora je v ľavom hornom rohu; stred = (šírka/2, výška/2).")]
    [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

    [Tooltip("Auto = hardvérový kurzor (rýchly, ale s limitmi veľkosti podľa platformy). " +
             "ForceSoftware = spoľahlivý pre ľubovoľnú veľkosť sprite.")]
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [Tooltip("Help - ClickTooltip pre Hire Staff")]
    [SerializeField] private ClickTooltip clickTooltip;

    // =====================================================================
    // HUDBA NA POZADÍ
    //
    // Hudba sa spustí hneď pri štarte hry a hrá donekonečna. Vždy hrá jedna
    // z dvoch alternatív (TracklistA / TracklistB) – OBE sú zoznamom mp3
    // klipov prehrávaných v NÁHODNOM poradí (shuffle). Hlasitosť oboch sa
    // nastavuje sliderom "Music Volume".
    //
    // "Music Clip" (jeden mp3 v nekonečnej slučke) ostáva ako ZÁLOHA – použije
    // sa len vtedy, ak je zoznam klipov zvolenej alternatívy prázdny.
    //
    // POZOR – dôležité pre správne fungovanie:
    //  • AudioSource sa vytvára automaticky v Awake(), netreba ho pridávať
    //    ručne na GameObject. Ak už na objekte AudioSource je, použije sa ten.
    //  • V import settings MP3 súboru odporúčam pri dlhom albume nastaviť
    //    Load Type = Streaming (inak sa celý súbor dekóduje do RAM) a
    //    vypnúť "Preload Audio Data", ak ti štart scény trvá dlho.
    //  • Klávesa M počas hry len prepína mute (hudba beží ďalej, nereštartuje sa).
    // =====================================================================

    [Header("Hudba na pozadí")]
    [Tooltip("ZÁLOŽNÝ MP3 súbor s hudbou (jeden klip v nekonečnej slučke). Použije sa LEN vtedy, " +
             "ak je zoznam klipov zvolenej alternatívy (Tracklist A / Tracklist B) prázdny. " +
             "Ak ostane prázdny aj tento, hudba sa jednoducho neprehráva.")]
    [SerializeField] private AudioClip musicClip;

    [Tooltip("Hlasitosť hudby použitá pri spustení hry. 0 = ticho, 1 = plná hlasitosť.")]
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.5f;

    // AudioSource, cez ktorý hudba beží. Vytvorí sa automaticky v Awake().
    private AudioSource musicSource;

    /// <summary>
    /// True ak je hudba práve stlmená klávesou M. Čítateľné z UI
    /// (napr. pre ikonku reproduktora v HUD).
    /// </summary>
    public bool IsMusicMuted => musicSource != null && musicSource.mute;

    // =====================================================================
    // HUDBA NA POZADÍ – DVE ALTERNATÍVY "TRACKLIST A" / "TRACKLIST B" (shuffle)
    //
    // V hre sú DVE alternatívy hudby na pozadí a hráč si medzi nimi vyberá
    // v Main Menu (SettingsPanel -> ToggleGroup -> TracklistA / TracklistB):
    //
    //   • TracklistA = pole klipov "Tracklist A Music Clips" (napr. 10 mp3),
    //                  ktoré sa prehrávajú NÁHODNE za sebou (shuffle),
    //                  donekonečna.
    //   • TracklistB = pole klipov "Shuffle Music Clips" (napr. 10 mp3),
    //                  ktoré sa prehrávajú NÁHODNE za sebou (shuffle), tiež
    //                  donekonečna.
    //
    // Obe alternatívy fungujú ÚPLNE rovnako a idú cez ten istý kód – líšia sa
    // len obsahom poľa, do každého natiahneš inú sadu skladieb. Ak je pole
    // zvolenej alternatívy prázdne, spadne sa na záložný jeden klip
    // "Music Clip" v loope (aby hra nikdy nebola ticho).
    //
    // Voľba sa ukladá cez statickú triedu MusicSettings (PlayerPrefs), takže
    // prežije prechod MainMenu -> IndicatrixScene aj reštart hry. GameManager
    // ju načíta RAZ v Awake(), takže hudba hrá hneď od prvého snímku hry.
    //
    // DÔLEŽITÉ: obe alternatívy idú cez ROVNAKÝ AudioSource (musicSource),
    // takže "Music Volume", SetMusicVolume() aj MusicSlider v SettingsMenuUI
    // fungujú úplne rovnako pre prvú aj druhú alternatívu – netreba nikde
    // rozlišovať, ktorá práve hrá.
    // =====================================================================

    [Tooltip("TRACKLIST A – zoznam mp3 klipov (napr. 10 skladieb), ktoré sa prehrávajú v náhodnom " +
             "poradí (shuffle). Použije sa LEN ak je v Main Menu zvolený TracklistA. " +
             "Ak je pole prázdne, použije sa náhradne 'Music Clip' (aby hra nikdy nebola ticho).")]
    [SerializeField] private AudioClip[] tracklistAMusicClips = new AudioClip[0];

    [Tooltip("TRACKLIST B – zoznam mp3 klipov (napr. 10 skladieb), ktoré sa prehrávajú v náhodnom " +
             "poradí (shuffle). Použije sa LEN ak je v Main Menu zvolený TracklistB. " +
             "Ak je pole prázdne, použije sa náhradne 'Music Clip' (aby hra nikdy nebola ticho).")]
    [SerializeField] private AudioClip[] shuffleMusicClips = new AudioClip[0];

    [Tooltip("Zapnuté = po prehratí všetkých skladieb sa poradie znova náhodne premieša. " +
             "Vypnuté = raz vygenerované náhodné poradie sa opakuje stále dokola.")]
    [SerializeField] private bool reshuffleAfterEachRound = true;

    // Alternatíva, ktorá je práve aktívna (načítaná z MusicSettings v Awake()).
    private MusicTracklist activeTracklist = MusicTracklist.TracklistA;

    // Pole klipov patriace PRÁVE AKTÍVNEJ alternatíve (A alebo B). Nastaví sa
    // v StartActiveTracklist() a celá shuffle logika nižšie pracuje už len s ním,
    // takže je pre obe alternatívy spoločná.
    private AudioClip[] activeShuffleClips;

    // Náhodné poradie indexov do activeShuffleClips + pozícia v tomto poradí.
    private readonly List<int> shuffleOrder = new List<int>();
    private int shuffleIndex = -1;

    // True = beží shuffle režim (platí pre TracklistA aj TracklistB). Vtedy
    // Update() sleduje, či skladba dohrala, a hneď púšťa ďalšiu z poradia.
    private bool shuffleRunning = false;

    // =====================================================================
    // TOOLTIPY (HoverTooltip) – globálny ON/OFF prepínač
    //
    // Klávesa F1 prepína zobrazovanie VŠETKÝCH tooltipov v hre naraz (je ich
    // v hre cca 10, každý samostatná inštancia HoverTooltip na inom objekte).
    // GameManager si žiadny zoznam tooltipov nedrží – stačí prepnúť statický
    // flag HoverTooltip.GlobalTooltipsEnabled a každá inštancia si ho sama
    // skontroluje vo svojom Update(). Práve viditeľný tooltip sa pri vypnutí
    // okamžite skryje.
    // =====================================================================

    [Header("Tooltipy")]
    [Tooltip("Klávesa, ktorá počas hry prepína zobrazovanie VŠETKÝCH tooltipov v hre (ON/OFF).")]
    [SerializeField] private KeyCode tooltipToggleKey = KeyCode.F1;

    /// <summary>
    /// True ak sú tooltipy práve zapnuté. Čítateľné z UI (napr. pre ikonku v HUD).
    /// </summary>
    public bool AreTooltipsEnabled => HoverTooltip.GlobalTooltipsEnabled;

    // =====================================================================
    // ZVUKOVÉ EFEKTY (SFX)
    //
    // Osem samostatných slotov – do každého sa cez Inspector natiahne jeden
    // zvukový klip a k nemu patrí vlastný slider hlasitosti (0–1). Nad nimi
    // je ešte spoločný "SFX Master Volume", ktorý stlmí všetky efekty naraz
    // (výsledná hlasitosť = master × hlasitosť konkrétneho efektu).
    //
    // ── AKO ZVUK SPUSTIŤ ──────────────────────────────────────────────────
    // Zvnútra GameManager-u stačí zavolať metódu priamo:
    //
    //     PlaySfxAnnualReport();
    //     PlaySfxBuildConstruction();
    //     PlaySfxDemolish();
    //     PlaySfxError();
    //     PlaySfxTrainStart();
    //     PlaySfxVehicleStart();
    //     PlaySfxBuildTile();
    //     PlaySfxUIclick();
    //
    // Z INÉHO skriptu (UI okná, TrainSystem, VehicleSystem, ...) cez singleton
    // – s otáznikom, aby to bolo bezpečné aj keď GameManager ešte neexistuje:
    //
    //     GameManager.instance?.PlaySfxUIclick();
    //     GameManager.instance?.PlaySfxTrainStart();
    //
    // Ôsmy efekt – Cash – je JEDINÝ samočinný a je už zapojený v kóde
    // (TrainSystem / VehicleSystem pri úspešnom predaji). Má vlastnú metódu
    // PlaySfxCash(Vector3 worldPosition) a nižšie vlastnú sekciu s vysvetlením.
    //
    // ── POZNÁMKA K IMPLEMENTÁCII ──────────────────────────────────────────
    // Všetky efekty idú cez JEDEN spoločný AudioSource metódou PlayOneShot().
    // Vďaka tomu sa efekty môžu prekrývať (nový neuseká predchádzajúci) a
    // netreba osem samostatných AudioSource komponentov. AudioSource sa
    // vytvorí automaticky v Awake() – v scéne netreba nič nastavovať.
    // Prázdny slot (nenatiahnutý klip) sa jednoducho ticho preskočí.
    // =====================================================================

    [Header("Zvukové efekty – spoločná hlasitosť")]
    [Tooltip("Spoločný násobiteľ hlasitosti pre VŠETKY zvukové efekty. " +
             "0 = efekty vypnuté, 1 = plná hlasitosť podľa jednotlivých sliderov nižšie.")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxMasterVolume = 1f;

    [Header("SFX – Annual Report (ročná uzávierka)")]
    [SerializeField] private AudioClip sfxAnnualReportClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxAnnualReportVolume = 1f;

    [Header("SFX – Build Construction (postavenie stavby)")]
    [SerializeField] private AudioClip sfxBuildConstructionClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxBuildConstructionVolume = 1f;

    [Header("SFX – Cash (zárobok z predaja)")]
    [SerializeField] private AudioClip sfxCashClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxCashVolume = 1f;

    [Header("SFX – Demolish (demolácia)")]
    [SerializeField] private AudioClip sfxDemolishClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxDemolishVolume = 1f;

    [Header("SFX – Error (chybová hláška)")]
    [SerializeField] private AudioClip sfxErrorClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxErrorVolume = 1f;

    [Header("SFX – Train Start (rozjazd vlaku)")]
    [SerializeField] private AudioClip sfxTrainStartClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxTrainStartVolume = 1f;

    [Header("SFX – Vehicle Start (rozjazd vozidla)")]
    [SerializeField] private AudioClip sfxVehicleStartClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVehicleStartVolume = 1f;

    [Header("SFX – Build Tile (postavenie tile budovy)")]
    [SerializeField] private AudioClip sfxBuildTileClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxBuildTileVolume = 1f;

    [Header("SFX – UI Click (kliknutie v menu)")]
    [SerializeField] private AudioClip sfxUIclickClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxUIclickVolume = 1f;

    // Spoločný AudioSource pre všetky efekty. Vytvorí sa automaticky v Awake().
    private AudioSource sfxSource;

    // =====================================================================
    // ZVUK "CASH" – NASTAVENIA PODMIENKY PREHRATIA
    //
    // Cash je jediný SAMOČINNÝ zvuk v hre – nespúšťa ho hráč klikaním, ale
    // dopravný prostriedok, ktorý dorazil na stanicu a úspešne predal tovar.
    // Keďže vlakov a vozidiel môžu byť po mape desiatky a obchodujú nezávisle
    // od seba, bez obmedzenia by zvuk hral takmer nepretržite. Preto platia
    // dve podmienky, obe nastaviteľné nižšie:
    //
    //   1) VIDITEĽNOSŤ – zvuk sa prehrá LEN ak je miesto predaja práve
    //      v zábere kamery. Hráč tak počuje len to, čo aj vidí; obchody na
    //      druhom konci mapy sú ticho.
    //   2) MINIMÁLNY ODSTUP – aj v zábere sa zvuk zopakuje najskôr po
    //      uplynutí nastaveného času, aby sa pri viacerých súčasných
    //      predajoch neprekrývalo desať kópií naraz.
    // =====================================================================

    [Header("SFX Cash – podmienky prehratia")]
    [Tooltip("Ak je zapnuté, zvuk Cash sa prehrá len keď je predávajúci vlak / vozidlo " +
             "v zábere kamery. Vypnutím bude Cash znieť pri každom predaji na celej mape.")]
    [SerializeField] private bool cashOnlyWhenVisible = true;

    [Tooltip("Tolerancia okraja obrazovky pri kontrole viditeľnosti. " +
             "0 = presne hranica obrazu; kladná hodnota (napr. 0,05) povolí zvuk aj tesne " +
             "za okrajom; záporná vyžaduje, aby bol prostriedok viac v strede obrazovky.")]
    [Range(-0.25f, 0.25f)]
    [SerializeField] private float cashViewportMargin = 0.02f;

    [Tooltip("Minimálny odstup medzi dvoma prehratiami zvuku Cash v sekundách. " +
             "Chráni pred kakofóniou, keď viacero vlakov predá naraz.")]
    [Range(0f, 2f)]
    [SerializeField] private float cashMinInterval = 0.25f;

    // Čas posledného prehratia Cash (unscaled – funguje aj pri pauze / zmene rýchlosti hry).
    private float lastCashPlayTime = -999f;

    // =====================================================================
    // REŽIMY KONŠTRUKCIE (pôvodné)
    // =====================================================================

    public enum RailConstructionMode
    {
        None,
        LevelUp,
        LevelDown,
        Demolish,
        RailHorizontal,
        RailVertical,
        RailCrossroad,
        RailCurveRightBottom,
        RailCurveLeftBottom,
        RailCurveRightTop,
        RailCurveLeftTop,
        StationHorizontal,
        StationVertical,
        DepotHorizontalBottom,
        DepotVerticalBottom,
        DepotHorizontalTop,
        DepotVerticalTop,
        RailSwitchHorizontalBottom,
        RailSwitchHorizontalTop,
        RailSwitchVerticalBottom,
        RailSwitchVerticalTop,

        // ── MOSTY A TUNELY (RailCrossingSystem) ──
        // Pridané na KONIEC enumu, aby sa nezmenili číselné hodnoty (stateID)
        // už uložené v save súboroch.
        Tunnel,             // UI režim: čaká sa na klik na rampu (portál)
        Bridge,             // UI režim: čakajú sa 2 kliky (začiatok a koniec)
        TunnelHorizontal,   // stateID hlavy tunela v tileGrid (os X)
        TunnelVertical,     // stateID hlavy tunela v tileGrid (os Z)
        BridgeHorizontal,   // stateID hlavy mosta v tileGrid (os X)
        BridgeVertical      // stateID hlavy mosta v tileGrid (os Z)
    }

    public RailConstructionMode CurrentRailConstructionMode { get; private set; }

    public void SetTerrainMode(RailConstructionMode RCmode)
    {
        CurrentRailConstructionMode = RCmode;
        // Pri prepnutí do iného režimu ukončíme prípadné čakanie na vstup vlaku
        if (RCmode != RailConstructionMode.None)
        {
            // Aktivácia RAIL režimu vyzbrojí aj ROAD a FACTORY režim (mutex)
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRoadConstructionMode = RoadConstructionMode.None;
            CurrentFactoryConstructionMode = FactoryConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // ROAD CONSTRUCTION MODE (analógia k RailConstructionMode)
    //
    // Cesty a železnice sú dva oddelené systémy – obe používajú rovnaký
    // tile grid v IndicatrixAPI, ale s rôznou TileCategory (Rail / Road),
    // čo umožňuje aby SetTile pre ROAD nezasiahol existujúci RAIL graf
    // a naopak. V jednom momente môže byť aktívny BUĎ rail BUĎ road režim
    // (mutex), takže OnMovement / OnClick prebehne čistú vetvu podľa toho,
    // ktorý režim je nastavený.
    // =====================================================================

    public enum RoadConstructionMode
    {
        None,
        LevelUp,
        LevelDown,
        Demolish,
        RoadHorizontal,
        RoadVertical,
        RoadCrossroad,
        RoadCurveRightBottom,
        RoadCurveLeftBottom,
        RoadCurveRightTop,
        RoadCurveLeftTop,
        StationHorizontal,
        StationVertical,
        DepotHorizontalBottom,
        DepotVerticalBottom,
        DepotHorizontalTop,
        DepotVerticalTop,
        RoadSwitchHorizontalBottom,
        RoadSwitchHorizontalTop,
        RoadSwitchVerticalBottom,
        RoadSwitchVerticalTop,

        // ── CESTNÉ MOSTY A TUNELY (RoadCrossingSystem) ──
        // Pridané na KONIEC enumu, aby sa nezmenili číselné hodnoty (stateID)
        // už uložené v save súboroch. Analógia k RailConstructionMode.
        Tunnel,             // UI režim: čaká sa na klik na rampu (portál)
        Bridge,             // UI režim: čakajú sa 2 kliky (začiatok a koniec)
        TunnelHorizontal,   // stateID hlavy tunela v tileGrid (os X)
        TunnelVertical,     // stateID hlavy tunela v tileGrid (os Z)
        BridgeHorizontal,   // stateID hlavy mosta v tileGrid (os X)
        BridgeVertical      // stateID hlavy mosta v tileGrid (os Z)
    }

    public RoadConstructionMode CurrentRoadConstructionMode { get; private set; }

    /// <summary>
    /// Preťaženie SetTerrainMode pre ROAD systém. Analogicky k RAIL verzii.
    /// Mutex: aktivácia ROAD režimu zruší RAIL režim (a opačne).
    /// </summary>
    public void SetTerrainMode(RoadConstructionMode RCmode)
    {
        CurrentRoadConstructionMode = RCmode;
        if (RCmode != RoadConstructionMode.None)
        {
            // Aktivácia ROAD režimu zruší RAIL režim aj vlakový vstup
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRailConstructionMode = RailConstructionMode.None;
            CurrentFactoryConstructionMode = FactoryConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // FACTORY CONSTRUCTION MODE (analógia k RAIL/ROAD ConstructionMode)
    //
    // Továrne sú tretí, samostatný systém. Na rozdiel od RAIL/ROAD NIE SÚ
    // dopravným grafom – sú to staticky umiestnené viac-tile objekty
    // (footprint 2×3, 3×3, 2×2 ...). Zdieľajú ten istý tile grid v
    // IndicatrixAPI, ale s TileCategory.Factory, takže TrainSystem ani
    // VehicleSystem ich nevidia (GetTileByIndex filtruje na Rail/Road).
    //
    // tileID konvencia:
    //   4 = Factory     (CoalMine, Forest, IronOreMine, GoldMine, SilverMine,
    //                    Farm, OilWells)
    //   5 = Processing  (PowerStation, SawMill, OilRefinery, ElectronicsFactory,
    //                    FurnitureFactory, Slaughterhouse, GrainFactory, Smelter,
    //                    GlassFactory)
    // Konkrétny typ továrne rozlišuje stateID = (int)FactoryConstructionMode.
    //
    // V jednom momente je aktívny BUĎ rail, BUĎ road, BUĎ factory režim
    // (mutex) – rovnako ako medzi RAIL a ROAD.
    // =====================================================================

    public enum FactoryConstructionMode
    {
        None,

        // ── Factory (tileID 4) – ťažba surovín ──
        CoalMine,            // tileID 4 (Factory),    footprint 2×3
        Forest,              // tileID 4 (Factory),    footprint 3×3
        IronOreMine,         // tileID 4 (Factory),    footprint 3×3
        GoldMine,            // tileID 4 (Factory),    footprint 3×3
        SilverMine,          // tileID 4 (Factory),    footprint 3×3
        Farm,                // tileID 4 (Factory),    footprint 3×3
        OilWells,            // tileID 4 (Factory),    footprint 3×3

        // ── Processing (tileID 5) – spracovanie surovín ──
        PowerStation,        // tileID 5 (Processing), footprint 2×2
        SawMill,             // tileID 5 (Processing), footprint 2×2
        OilRefinery,         // tileID 5 (Processing), footprint 2×2
        ElectronicsFactory,  // tileID 5 (Processing), footprint 3×3
        FurnitureFactory,    // tileID 5 (Processing), footprint 3×3
        Slaughterhouse,      // tileID 5 (Processing), footprint 2×2
        GrainFactory,        // tileID 5 (Processing), footprint 2×3
        Smelter,             // tileID 5 (Processing), footprint 2×3
        GlassFactory         // tileID 5 (Processing), footprint 2×2
    }

    public FactoryConstructionMode CurrentFactoryConstructionMode { get; private set; }

    /// <summary>
    /// Rotácia, s ktorou sa umiestňuje továreň. Od prechodu tovární na 2D
    /// sprity je NATRVALO Deg0 a hráč ju NEVIE zmeniť.
    ///
    /// PREČO SA ROTÁCIA ZRUŠILA:
    ///   Sprite je nakreslený z JEDNEJ strany (jeden izometrický pohľad).
    ///   Otočenie footprintu o 90° by otočilo obdĺžnik (2×3 → 3×2), ale obrázok
    ///   by ostal rovnaký – footprint by prestal sedieť s tým, čo hráč vidí.
    ///   Preto sa ovládanie rotácie (koliesko myši) odstránilo úplne.
    ///
    /// Vlastnosť tu ZOSTÁVA ako konštanta, aby sa nemuselo meniť API
    /// IndicatrixAPI.SetTile / SnapAreaFace / TryGetFactoryFootprintBounds ani
    /// formát uloženej hry (rotácia sa naďalej ukladá a načítava, len je vždy
    /// Deg0). Ak by si niekedy dokreslil 4 pohľady na každú továreň, stačí
    /// vrátiť sem menič hodnoty – zvyšok reťazca je pripravený.
    /// </summary>
    public IndicatrixAPI.FactoryRotation CurrentFactoryRotation
        => IndicatrixAPI.FactoryRotation.Deg0;

    /// <summary>
    /// Preťaženie SetTerrainMode pre FACTORY systém. Analogicky k RAIL/ROAD.
    /// Mutex: aktivácia FACTORY režimu zruší RAIL aj ROAD režim.
    /// </summary>
    public void SetTerrainMode(FactoryConstructionMode FCmode)
    {
        CurrentFactoryConstructionMode = FCmode;
        if (FCmode != FactoryConstructionMode.None)
        {
            // POZN.: rotácia továrne sa už nenastavuje – CurrentFactoryRotation
            // je natrvalo Deg0 (továrne sú 2D sprity kreslené z jednej strany).

            // Aktivácia FACTORY režimu zruší RAIL aj ROAD režim aj vstupy
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRailConstructionMode = RailConstructionMode.None;
            CurrentRoadConstructionMode = RoadConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // STAFF → FACTORY ASSIGNMENT MODE (ConstructionModeStaffToFactory)
    //
    // Štvrtý "konštrukčný" režim – NIE je to ale stavba na tile mape, ale
    // PRIDELENIE personálu (StaffManagement) do už existujúcej továrne.
    // Spúšťa ho UI okno StaffManagementMenuUI po kliknutí na *HireButton
    // (napr. MinerHireButton) – odovzdá naplnenú StaffManagement štruktúru.
    //
    // SPRÁVANIE (analogické k FACTORY/RAIL/ROAD vetvám v Update):
    //   • OnMovement: IndAPI.SnapMeshFace(...) – zvýrazní 1×1 ŠTVOREC + FACE
    //     pod kurzorom (rovnaký vizuál ako pri kliku na hotovú továreň).
    //   • OnClick:    hráč klikne na ĽUBOVOĽNÚ továreň. Keďže
    //     FactoryRegistry.GetFactoryAt(...) mapuje KAŽDÝ tile footprintu na tú
    //     istú FactoryInstance, footprint (2×3 / 3×3 / 2×2) je zohľadnený
    //     automaticky – stačí kliknúť na hociktorý tile továrne.
    //
    // POROVNANIE pri kliku (StaffManagement vs FactoryInstance):
    //   • ak sa StaffManagement.ID == FactoryInstance.ID →
    //         továreň.LevelSalary    = staff.LevelSalary;
    //         továreň.EmployeeSalary = staff.EmployeeSalary;
    //         továreň.OccupancyFlag  = true;
    //   • inak →
    //         továreň.OccupancyFlag  = false;  (ostatné polia bez zmeny)
    //
    // Mutex: aktivácia tohto režimu zruší RAIL/ROAD/FACTORY režim aj vstupy
    // (a opačne – aktivácia ktoréhokoľvek konštrukčného režimu zruší tento).
    // =====================================================================

    /// <summary>True, kým je aktívny režim prideľovania personálu do továrne.</summary>
    public bool IsStaffToFactoryMode { get; private set; }

    /// <summary>
    /// Práve "nesená" dávka personálu, ktorú UI naplnilo pri kliku na
    /// *HireButton. Po kliknutí na továreň sa porovná/prenesie do nej.
    /// </summary>
    StaffManagement pendingStaff;

    /// <summary>
    /// Spustí režim prideľovania personálu do továrne. Volá ho StaffManagement
    /// UI (napr. SMMinerPanelUI) po naplnení StaffManagement a zatvorení okna.
    /// Mutex – zhodí všetky ostatné režimy (rovnako ako SetTerrainMode).
    /// </summary>
    public void EnterStaffToFactoryMode(StaffManagement staff)
    {
        if (staff == null)
        {
            Debug.LogWarning("[GameManager] EnterStaffToFactoryMode: staff == null – ignorované.");
            return;
        }

        pendingStaff = staff;
        IsStaffToFactoryMode = true;

        // Mutex – zruš RAIL/ROAD/FACTORY režim aj vlakový/cestný vstup.
        CurrentRailConstructionMode = RailConstructionMode.None;
        CurrentRoadConstructionMode = RoadConstructionMode.None;
        CurrentFactoryConstructionMode = FactoryConstructionMode.None;
        trainInputMode = TrainInputMode.None;
        pendingDepotTile = null;
        collectingStations = false;
        pendingRoadDepotTile = null;
        collectingRoadStations = false;
        IndAPI?.HideAllSnapVisuals();

        Debug.Log($"[GameManager] Staff→Factory režim AKTÍVNY. Klikni na továreň. {staff}");
    }

    /// <summary>
    /// Ukončí režim prideľovania personálu (po vykonaní akcie alebo cez ESC)
    /// a skryje snap vizuál.
    /// </summary>
    public void ExitStaffToFactoryMode()
    {
        IsStaffToFactoryMode = false;
        pendingStaff = null;
        IndAPI?.HideAllSnapVisuals();
    }

    // =====================================================================
    // STAV VSTUPU VLAKU
    //
    // POZNÁMKA: Klávesové skratky Q, W, S, P, R, T boli ODSTRÁNENÉ.
    // Funkcionalita bola presunutá do DepotRailConstructionMenuUI, ktoré sa
    // otvára kliknutím na Depot tile.
    //
    // Z pôvodných režimov ostal LEN AddStations (W) – pretože vyžaduje
    // viacero klikov na tile mapu (na stanice). Ostatné akcie sa volajú
    // priamo cez Request*ForDepot(...) metódy bez čakania na klik na tile.
    // =====================================================================

    enum TrainInputMode
    {
        None,
        AddStations,        // Čakáme na klik na RAIL Station(e) pre vopred vybrané RAIL depo
        AddStationsRoad     // Čakáme na klik na ROAD Station(e) pre vopred vybrané ROAD depo
    }

    TrainInputMode trainInputMode = TrainInputMode.None;

    // Pre režim AddStations – zapamätáme depot, potom čakáme na kliky na stanice
    Vector2Int? pendingDepotTile = null;
    bool collectingStations = false;

    // Pre režim AddStationsRoad – zapamätáme ROAD depot, potom čakáme na
    // kliky na ROAD stanice (autobus./nákl. zastávky).
    Vector2Int? pendingRoadDepotTile = null;
    bool collectingRoadStations = false;

    // Spätná väzba pre DepotRailConstructionMenuUI – aby vedelo, či práve
    // beží "Define Route" režim pre dané depo
    public bool IsCollectingStationsForDepot(int dx, int dz)
    {
        return trainInputMode == TrainInputMode.AddStations
               && collectingStations
               && pendingDepotTile.HasValue
               && pendingDepotTile.Value.x == dx
               && pendingDepotTile.Value.y == dz;
    }

    /// <summary>
    /// ROAD ekvivalent IsCollectingStationsForDepot – spätná väzba pre
    /// DepotRoadConstructionMenuUI.
    /// </summary>
    public bool IsCollectingStationsForRoadDepot(int dx, int dz)
    {
        return trainInputMode == TrainInputMode.AddStationsRoad
               && collectingRoadStations
               && pendingRoadDepotTile.HasValue
               && pendingRoadDepotTile.Value.x == dx
               && pendingRoadDepotTile.Value.y == dz;
    }

    Camera cam;

    // =====================================================================
    // NULL-SAFE LAZY PROPERTIES
    // Chránia pred NullReferenceException ak Awake/Start prebehli v zlom poradí
    // =====================================================================

    static TrainSystem TrainSys
    {
        get
        {
            if (TrainSystem.instance == null)
                TrainSystem.instance = UnityEngine.Object.FindFirstObjectByType<TrainSystem>();
            return TrainSystem.instance;
        }
    }

    /// <summary>
    /// Lazy referencia na VehicleSystem (cestný ekvivalent TrainSystem).
    /// VehicleSystem je úplne autonómny – obsluhuje len ROAD dlaždice
    /// (skrz GetTileByIndexAny v IndicatrixAPI s filtrom kategórie).
    /// </summary>
    static VehicleSystem VehicleSys
    {
        get
        {
            if (VehicleSystem.instance == null)
                VehicleSystem.instance = UnityEngine.Object.FindFirstObjectByType<VehicleSystem>();
            return VehicleSystem.instance;
        }
    }

    static IndicatrixAPI IndAPI
    {
        get
        {
            if (IndicatrixAPI.instance == null)
                IndicatrixAPI.instance = UnityEngine.Object.FindFirstObjectByType<IndicatrixAPI>();
            return IndicatrixAPI.instance;
        }
    }

    // Lazy referencia na DepotRailConstructionMenuUI (môže byť v scéne neaktívne)
    DepotRailConstructionMenuUI _depotRailMenuUI;
    DepotRailConstructionMenuUI DepotRailMenuUI
    {
        get
        {
            if (_depotRailMenuUI == null)
                _depotRailMenuUI = UnityEngine.Object.FindFirstObjectByType<DepotRailConstructionMenuUI>(FindObjectsInactive.Include);
            return _depotRailMenuUI;
        }
    }

    // Lazy referencia na DepotRoadConstructionMenuUI (môže byť v scéne neaktívne)
    DepotRoadConstructionMenuUI _depotRoadMenuUI;
    DepotRoadConstructionMenuUI DepotRoadMenuUI
    {
        get
        {
            if (_depotRoadMenuUI == null)
                _depotRoadMenuUI = UnityEngine.Object.FindFirstObjectByType<DepotRoadConstructionMenuUI>(FindObjectsInactive.Include);
            return _depotRoadMenuUI;
        }
    }

    // Lazy referencia na StatusFactoryMenuUI (môže byť v scéne neaktívne).
    // Okno je pri štarte skryté (SetActive(false) v jeho Start()), preto sa
    // hľadá s FindObjectsInactive.Include – rovnako ako Depot menu okná.
    StatusFactoryMenuUI _statusFactoryMenuUI;
    StatusFactoryMenuUI StatusFactoryMenuUI
    {
        get
        {
            if (_statusFactoryMenuUI == null)
                _statusFactoryMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusFactoryMenuUI>(FindObjectsInactive.Include);
            return _statusFactoryMenuUI;
        }
    }

    // Lazy referencia na StatusStationMenuUI (môže byť v scéne neaktívne).
    // Okno je pri štarte skryté (SetActive(false) v jeho Start()), preto sa
    // hľadá s FindObjectsInactive.Include – rovnako ako StatusFactoryMenuUI
    // a Depot menu okná.
    StatusStationMenuUI _statusStationMenuUI;
    StatusStationMenuUI StatusStationMenuUI
    {
        get
        {
            if (_statusStationMenuUI == null)
                _statusStationMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusStationMenuUI>(FindObjectsInactive.Include);
            return _statusStationMenuUI;
        }
    }

    // Lazy referencia na StatusErrorMenuUI (môže byť v scéne neaktívne).
    // Univerzálne chybové okno – pri štarte skryté (SetActive(false) v jeho
    // Start()), preto sa hľadá s FindObjectsInactive.Include, rovnako ako
    // ostatné Status / Depot menu okná.
    StatusErrorMenuUI _statusErrorMenuUI;
    StatusErrorMenuUI StatusErrorMenuUI
    {
        get
        {
            if (_statusErrorMenuUI == null)
                _statusErrorMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusErrorMenuUI>(FindObjectsInactive.Include);
            return _statusErrorMenuUI;
        }
    }

    // =====================================================================
    // CHYBOVÉ HLÁSENIA (univerzálne okno StatusErrorMenuUI)
    //
    // Chyby sa VYVOLÁVAJÚ na mieste incidentu (tu v OnClick build vetvách,
    // v budúcnosti hocikde inde). Vždy sa otvorí TO ISTÉ okno, len s INÝM
    // textom – kanonické znenia sú v GameErrors.cs.
    // =====================================================================

    /// <summary>
    /// Zobrazí hráčovi chybovú hlášku v univerzálnom okne StatusErrorMenuUI.
    /// Jediné miesto, cez ktoré GameManager otvára chybové okno – volajúci
    /// dodá text (najlepšie konštantu z GameErrors).
    /// </summary>
    void ReportError(string message)
    {
        PlaySfxError();

        var errMenu = StatusErrorMenuUI;
        if (errMenu != null)
        {
            errMenu.OpenWithError(message);
        }
        else
        {
            // Okno nie je v scéne – aspoň nech sa chyba neutopí ticho.
            Debug.LogWarning($"[GameManager] StatusErrorMenuUI nie je v scéne. " +
                             $"Chyba: \"{message}\"");
        }
    }

    // =====================================================================
    // EKONOMIKA KONŠTRUKCIE – spoplatnenie stavby a refund za demoláciu
    //
    // Cenník je centralizovaný v ConstructionCosts.cs; tu sú len tenké
    // obaly, ktoré cenu odpočítajú/pripočítajú cez GameEconomy a v prípade
    // nedostatku kreditov stavbu zablokujú (ReportError + return false).
    //
    // Ak GameEconomy.instance nie je v scéne, spoplatnenie sa preskočí
    // (hra ostane funkčná aj bez peňažného systému).
    // =====================================================================

    /// <summary>
    /// Pokus o zaplatenie RAIL stavby. true = zaplatené (alebo zadarmo / bez
    /// ekonomiky), false = nedostatok kreditov → volajúci NESMIE stavať.
    /// </summary>
    bool TryChargeRailBuild(RailConstructionMode mode)
        => TryChargeBuild(ConstructionCosts.RailBuildCost(mode));

    /// <summary>
    /// Pokus o zaplatenie ROAD stavby. true = zaplatené, false = nedostatok.
    /// </summary>
    bool TryChargeRoadBuild(RoadConstructionMode mode)
        => TryChargeBuild(ConstructionCosts.RoadBuildCost(mode));

    /// <summary>
    /// Pokus o zaplatenie postavenia TOVÁRNE. true = zaplatené (alebo zadarmo /
    /// bez ekonomiky), false = nedostatok kreditov → továreň sa NESMIE položiť.
    /// Cena sa berie z ConstructionCosts.FactoryBuildCost (= FactoryDefinition.Cost).
    /// </summary>
    bool TryChargeFactoryBuild(FactoryConstructionMode mode)
    {
        uint cost = ConstructionCosts.FactoryBuildCost(mode);
        bool charged = TryChargeBuild(cost);

        // EVIDENCIA pre ročnú uzávierku: cena továrne sa odpočítava HNEĎ pri
        // položení (geometria je už overená vyššie, SetTile nezlyhá), takže
        // úspešné spoplatnenie = postavená továreň. Zaznamenáme len reálne
        // platený nákup (cost > 0).
        if (charged && cost > 0u)
            BudgetSystem.instance?.RecordFactoryPurchase(cost);

        return charged;
    }

    /// <summary>Spoločné jadro – odpočíta cenu, ak je dosť kreditov.</summary>
    bool TryChargeBuild(uint cost)
    {
        if (cost == 0u) return true;                    // úkon nič nestojí
        if (GameEconomy.instance == null) return true;  // ekonomika nie je v scéne

        if (!GameEconomy.instance.TrySpendCredits(cost))
        {
            ReportError($"Nedostatok kreditov – tento úkon stojí {cost} CR.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Pripočíta na konto refund za zdemolovaný tile (50 % zo základnej ceny).
    /// tileInfo treba prečítať PRED vymazaním dlaždice (drží stateID + category).
    /// </summary>
    void RefundDemolishedTile(IndicatrixAPI.TileData tileInfo)
    {
        if (GameEconomy.instance == null) return;

        uint refund = ConstructionCosts.DemolishRefund(tileInfo);
        if (refund > 0u)
            GameEconomy.instance.AddCredits(refund);
    }

    /// <summary>
    /// Vráti true, ak je daný RAIL režim PLACEMENT (kladie tile cez SetTile s
    /// tileID != 0 – koľaje, stanice, depá, výhybky). Pre None, LevelUp,
    /// LevelDown (úprava terénu, nevolá SetTile) a Demolish (SetTile = 0)
    /// vracia false – tieto sa proti obsadenosti NEkontrolujú.
    /// </summary>
    static bool IsRailPlacementMode(RailConstructionMode mode)
    {
        switch (mode)
        {
            case RailConstructionMode.None:
            case RailConstructionMode.LevelUp:
            case RailConstructionMode.LevelDown:
            case RailConstructionMode.Demolish:
            // Mosty a tunely majú vlastnú vetvu (HandleCrossingInput) –
            // viac-tile validácia, vlastná cena, vlastné hlášky.
            case RailConstructionMode.Tunnel:
            case RailConstructionMode.Bridge:
            case RailConstructionMode.TunnelHorizontal:
            case RailConstructionMode.TunnelVertical:
            case RailConstructionMode.BridgeHorizontal:
            case RailConstructionMode.BridgeVertical:
                return false;
            default:
                return true;
        }
    }

    /// <summary>True pre UI režimy stavby mosta / tunela.</summary>
    static bool IsRailCrossingMode(RailConstructionMode mode)
        => mode == RailConstructionMode.Tunnel
        || mode == RailConstructionMode.Bridge;

    /// <summary>
    /// ROAD ekvivalent IsRailPlacementMode.
    /// </summary>
    static bool IsRoadPlacementMode(RoadConstructionMode mode)
    {
        switch (mode)
        {
            case RoadConstructionMode.None:
            case RoadConstructionMode.LevelUp:
            case RoadConstructionMode.LevelDown:
            case RoadConstructionMode.Demolish:
            // Cestné mosty a tunely majú vlastnú vetvu (HandleCrossingInput).
            case RoadConstructionMode.Tunnel:
            case RoadConstructionMode.Bridge:
            case RoadConstructionMode.TunnelHorizontal:
            case RoadConstructionMode.TunnelVertical:
            case RoadConstructionMode.BridgeHorizontal:
            case RoadConstructionMode.BridgeVertical:
                return false;
            default:
                return true;
        }
    }

    /// <summary>True pre UI režimy stavby cestného mosta / tunela.</summary>
    static bool IsRoadCrossingMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.Tunnel
        || mode == RoadConstructionMode.Bridge;

    // =====================================================================
    // OCHRANNÁ ZÓNA STANÍC (stanica nesmie vzniknúť pri inej stanici)
    //
    // Okolo KAŽDEJ stanice (tileID == 2) je graficky neviditeľná ochranná
    // zóna. Do tejto zóny nie je možné postaviť ďalšiu stanicu. Pravidlo je
    // kategória-agnostické: RAIL stanica blokuje aj ROAD stanicu a naopak
    // (kontroluje sa len tileID == 2, nie category).
    //
    // VEĽKOSŤ ZÓNY (STATION_EXCLUSION_RADIUS) je Chebyshev polomer okolo
    // stanice:
    //   1 → región 3×3 vycentrovaný na stanici (stanica + 8 susedov).
    //       Novú stanicu možno postaviť až vo vzdialenosti ≥ 2 tile, t.j.
    //       medzi dvoma stanicami ostane minimálne 1 voľný tile.
    //   2 → región 5×5 (väčší odstup – medzi stanicami min. 2 voľné tile).
    // Stačí zmeniť toto jediné číslo, zvyšok logiky sa prispôsobí.
    // =====================================================================

    const int STATION_EXCLUSION_RADIUS = 2;

    /// <summary>
    /// Vráti true, ak je daný RAIL režim umiestnením STANICE
    /// (StationHorizontal / StationVertical). Len pre tieto režimy sa
    /// kontroluje ochranná zóna okolo iných staníc.
    /// </summary>
    static bool IsRailStationMode(RailConstructionMode mode)
        => mode == RailConstructionMode.StationHorizontal
        || mode == RailConstructionMode.StationVertical;

    /// <summary>
    /// ROAD ekvivalent IsRailStationMode.
    /// </summary>
    static bool IsRoadStationMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.StationHorizontal
        || mode == RoadConstructionMode.StationVertical;

    /// <summary>
    /// Vráti true, ak je daný RAIL režim umiestnením DEPA (Depot* varianty).
    /// Len pre tieto režimy sa kontroluje vodorovnosť terénu (situácia č.2).
    /// </summary>
    static bool IsRailDepotMode(RailConstructionMode mode)
        => mode == RailConstructionMode.DepotHorizontalBottom
        || mode == RailConstructionMode.DepotVerticalBottom
        || mode == RailConstructionMode.DepotHorizontalTop
        || mode == RailConstructionMode.DepotVerticalTop;

    /// <summary>
    /// ROAD ekvivalent IsRailDepotMode.
    /// </summary>
    static bool IsRoadDepotMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.DepotHorizontalBottom
        || mode == RoadConstructionMode.DepotVerticalBottom
        || mode == RoadConstructionMode.DepotHorizontalTop
        || mode == RoadConstructionMode.DepotVerticalTop;

    // =====================================================================
    // KLASIFIKÁCIA KOĽAJOVÝCH/CESTNÝCH DIELOV PODĽA POVOLENÉHO TERÉNU
    //
    //   • ROVNÉ diely (Horizontal/Vertical) – smú na rovinu AJ na rampu.
    //   • OSTATNÉ track diely (crossroad, curves, switches) – len na rovinu.
    //
    // Stanice a depá majú vlastné guardy (vlastné hlášky), preto sú z
    // "flat-only track" skupiny vylúčené.
    // =====================================================================

    /// <summary>
    /// Vráti true, ak je RAIL režim ROVNÝ diel (RailHorizontal/RailVertical).
    /// Tieto sa smú stavať na rovine aj na šikmom tile (rampe).
    /// </summary>
    static bool IsRailStraightMode(RailConstructionMode mode)
        => mode == RailConstructionMode.RailHorizontal
        || mode == RailConstructionMode.RailVertical;

    /// <summary>
    /// ROAD ekvivalent IsRailStraightMode.
    /// </summary>
    static bool IsRoadStraightMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.RoadHorizontal
        || mode == RoadConstructionMode.RoadVertical;

    /// <summary>
    /// Vráti true pre RAIL track diel, ktorý sa smie stavať LEN na rovine –
    /// t.j. placement diel, ktorý NIE JE rovný, ani stanica, ani depo
    /// (zostávajú: crossroad, 4 curves, 4 switches).
    /// </summary>
    static bool IsRailFlatOnlyTrackMode(RailConstructionMode mode)
        => IsRailPlacementMode(mode)
        && !IsRailStraightMode(mode)
        && !IsRailStationMode(mode)
        && !IsRailDepotMode(mode);

    /// <summary>
    /// ROAD ekvivalent IsRailFlatOnlyTrackMode (RoadCrossroad, curves, switches).
    /// </summary>
    static bool IsRoadFlatOnlyTrackMode(RoadConstructionMode mode)
        => IsRoadPlacementMode(mode)
        && !IsRoadStraightMode(mode)
        && !IsRoadStationMode(mode)
        && !IsRoadDepotMode(mode);

    /// <summary>
    /// Vráti true, ak sa v okolí cieľového tile [cx,cz] (Chebyshev polomer
    /// STATION_EXCLUSION_RADIUS) už nachádza ĽUBOVOĽNÁ stanica (tileID == 2,
    /// RAIL aj ROAD). Slúži ako guard pred postavením novej stanice.
    ///
    /// Okrajové tiles mimo mriežky rieši GetTileByIndexAny – pre indexy mimo
    /// 0..GRID_SIZE-1 vracia prázdnu dlaždicu (tileID 0), takže sa nezarátajú.
    /// </summary>
    bool IsStationNearby(int cx, int cz)
    {
        if (IndAPI == null) return false;

        for (int dx = -STATION_EXCLUSION_RADIUS; dx <= STATION_EXCLUSION_RADIUS; dx++)
        {
            for (int dz = -STATION_EXCLUSION_RADIUS; dz <= STATION_EXCLUSION_RADIUS; dz++)
            {
                var t = IndAPI.GetTileByIndexAny(cx + dx, cz + dz);
                if (t.tileID == 2) // stanica (RAIL alebo ROAD)
                    return true;
            }
        }
        return false;
    }

    // =====================================================================
    // OCHRANNÁ ZÓNA TOVÁRNÍ (továreň nesmie vzniknúť pri inej továrni)
    //
    // Plná analógia k STATION_EXCLUSION_RADIUS / IsStationNearby, ale pre
    // viac-tile objekty (footprint). Okolo KAŽDEJ továrne (TileCategory.
    // Factory, tileID 4/5) je neviditeľná ochranná zóna. Do tejto zóny nie
    // je možné položiť ďalšiu továreň.
    //
    // FACTORY_EXCLUSION_RADIUS je Chebyshev polomer MERANÝ OD OKRAJA
    // footprintu plánovanej továrne (nie od stredu) – t.j. medzi dvoma
    // továrňami ostane minimálne toľko voľných tilov. Stačí zmeniť toto
    // jediné číslo, zvyšok logiky sa prispôsobí.
    // =====================================================================

    const int FACTORY_EXCLUSION_RADIUS = 3;

    /// <summary>
    /// Vráti true, ak sa v okolí PLÁNOVANÉHO footprintu továrne (ľavý-dolný
    /// roh [originX,originZ], rozmery width×depth) – rozšíreného o Chebyshev
    /// polomer FACTORY_EXCLUSION_RADIUS na všetky strany – už nachádza tile
    /// patriaci inej továrni (TileCategory.Factory). Slúži ako guard pred
    /// položením novej továrne (analógia k IsStationNearby).
    ///
    /// Tily samotného plánovaného footprintu sa preskakujú; okrajové indexy
    /// mimo mriežky rieši GetTileByIndexAny (vráti prázdnu dlaždicu).
    /// </summary>
    bool IsFactoryNearby(int originX, int originZ, int width, int depth)
    {
        if (IndAPI == null) return false;

        int minX = originX - FACTORY_EXCLUSION_RADIUS;
        int minZ = originZ - FACTORY_EXCLUSION_RADIUS;
        int maxX = originX + width - 1 + FACTORY_EXCLUSION_RADIUS;
        int maxZ = originZ + depth - 1 + FACTORY_EXCLUSION_RADIUS;

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // Vnútro plánovaného footprintu nie je "iná" továreň – preskoč.
                if (x >= originX && x < originX + width &&
                    z >= originZ && z < originZ + depth)
                    continue;

                var t = IndAPI.GetTileByIndexAny(x, z);
                if (t.category == IndicatrixAPI.TileCategory.Factory)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Zdemoluje CELÚ továreň, ktorej footprint obsahuje kliknutý tile – nie
    /// len jeden tile. Vyčistí všetky tily footprintu z tile mapy
    /// (IndicatrixAPI.ClearTileByIndex) a odregistruje FactoryInstance z
    /// FactoryRegistry. Volá sa z RAIL aj ROAD Demolish vetvy potom, ako sa
    /// zistí, že kliknutý tile patrí kategórii Factory a továreň NIE JE vo
    /// výstavbe.
    ///
    /// REFUND: pripočíta 50 % z ceny továrne (ConstructionCosts.FactoryBuildCost
    /// = FactoryDefinition.Cost) – rovnaká politika ako pri RAIL/ROAD demolácii.
    /// Pri stavbe sa odpočítal plný Cost, pri demolácii sa vráti polovica
    /// (zaokrúhlené nadol, vždy z kladného základu). Refund je RAZ za celý
    /// objekt (nie per-tile).
    /// </summary>
    void DemolishFactory(FactoryInstance factory)
    {
        if (factory == null || IndAPI == null) return;

        for (int x = factory.OriginX; x < factory.OriginX + factory.Width; x++)
            for (int z = factory.OriginZ; z < factory.OriginZ + factory.Depth; z++)
                IndAPI.ClearTileByIndex(x, z);

        FactoryRegistry.Unregister(factory);

        // Refund 50 % z ceny továrne (raz za celý objekt).
        if (GameEconomy.instance != null && factory.Definition != null)
        {
            uint refund = ConstructionCosts.HalfRefund(
                ConstructionCosts.FactoryBuildCost(factory.Definition.Mode));
            if (refund > 0u) GameEconomy.instance.AddCredits(refund);
        }

        Debug.Log($"[GameManager] Továreň '{factory.Name}' zdemolovaná celá " +
                  $"({factory.Width}x{factory.Depth} tilov) z rohu " +
                  $"[{factory.OriginX},{factory.OriginZ}].");
    }

    /// <summary>
    /// Vráti true, ak by úprava terénu (LevelUp/LevelDown) klikom na vrchol
    /// snapPoint zmenila výšku aspoň jedného vrcholu patriaceho OBSADENÉMU
    /// tile (tileID != 0).
    ///
    /// Nestačí kontrolovať len klikaný vrchol: TerrainVertexLevel cez
    /// TerrainCollapse kaskádovo posúva aj okolité vrcholy. Preto si necháme
    /// od TerrainManager-a SIMULOVAŤ (dry-run) celú operáciu a vrátiť zoznam
    /// VŠETKÝCH vrcholov, ktoré by sa zmenili. Každý z nich potom overíme cez
    /// IndAPI.IsVertexOnOccupiedTile (vrchol je rohom až 4 tilov).
    ///
    /// Simulácia nič reálne nemení – terén sa upraví až keď táto metóda vráti
    /// false (žiadny dotknutý vrchol nepatrí obsadenému tile).
    /// </summary>
    bool WouldTerrainEditHitOccupied(Vector3 snapPoint, bool levelUp)
    {
        if (TerrainManager.instance == null || IndAPI == null) return false;

        var affected = TerrainManager.instance.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        for (int i = 0; i < affected.Count; i++)
        {
            if (IndAPI.IsVertexOnOccupiedTile(affected[i].x, affected[i].y))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Analógia k <see cref="WouldTerrainEditHitOccupied"/>, ale pre MESTSKÉ
    /// BUDOVY (CityManager). Mestské budovy NIE SÚ v tileGrid, takže ich
    /// IsVertexOnOccupiedTile nezachytí – kontrolujeme ich samostatne.
    ///
    /// Pre každý vrchol, ktorý by úprava terénu zmenila (vrátane kaskády cez
    /// TerrainCollapse – simulované cez PredictVertexLevelChanges), overíme až
    /// 4 tily, ktorých je daný vrchol rohom. Ak ktorýkoľvek z nich nesie mestskú
    /// budovu, úprava terénu sa zablokuje.
    /// </summary>
    bool WouldTerrainEditHitCityBuilding(Vector3 snapPoint, bool levelUp)
    {
        if (TerrainManager.instance == null || CityManager.instance == null) return false;

        var affected = TerrainManager.instance.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        for (int i = 0; i < affected.Count; i++)
        {
            int vx = affected[i].x;
            int vz = affected[i].y;

            // Vrchol (vx,vz) je rohom až 4 tilov:
            //   (vx,   vz), (vx,   vz-1), (vx-1, vz), (vx-1, vz-1).
            // IsCityTile bezpečne vráti false pre indexy mimo mapy.
            if (CityManager.instance.IsCityTile(vx, vz) ||
                CityManager.instance.IsCityTile(vx, vz - 1) ||
                CityManager.instance.IsCityTile(vx - 1, vz) ||
                CityManager.instance.IsCityTile(vx - 1, vz - 1))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Vykoná úpravu terénu (LevelUp/LevelDown) a spustí vizuálne zvýraznenie
    /// VŠETKÝCH tilov, ktorých sa zmena dotkla – vrátane kaskády TerrainCollapse.
    /// Tily sa cez fade sfarbia do hneda, chvíľu tak ostanú a cez fade sa vrátia
    /// k pôvodnej textúre terénu (TerrainEditHighlight + RetroTerrain/Terrain shader).
    ///
    /// POZOR NA PORADIE: dotknuté vrcholy treba zistiť PRED TerrainVertexLevel –
    /// po úprave už snapPoint.y nesedí s terénom a simulácia by nič nenašla.
    /// Volá sa až po prejdení všetkých guardov a úspešnom spoplatnení, takže
    /// zamietnutá úprava (chybová hláška) nič nezvýrazní.
    /// </summary>
    void ApplyTerrainEdit(Vector3 snapPoint, bool levelUp)
    {
        var tm = TerrainManager.instance;
        if (tm == null) return;

        var affectedVertices = tm.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        tm.TerrainVertexLevel(snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        TerrainEditHighlight.GetOrCreate().FlashVertices(affectedVertices);
    }

    // =====================================================================
    // ENVIRONMENT GUARDY (stromy / kamene / landing locations)
    //
    // Environment objekty NIE SÚ v tileGrid – vedie ich EnvironmentManager
    // (rovnaký princíp ako mestské budovy v CityManager). Preto sa kontrolujú
    // samostatne, presne tak, ako sa dnes kontroluje CityManager.IsCityTile.
    // =====================================================================

    /// <summary>
    /// True, ak na tile [x,z] stojí strom, kameň alebo landing location.
    /// Na takýto tile sa NESMIE postaviť nič (žiadny konštrukčný mód).
    /// </summary>
    bool IsEnvironmentBlockedTile(int x, int z)
        => EnvironmentManager.instance != null
        && EnvironmentManager.instance.IsEnvironmentTile(x, z);

    /// <summary>
    /// Ohlási správnu chybu pre zablokovaný environment tile: landing location
    /// má vlastnú (je trvalá), strom/kameň odkazuje na Demolish.
    /// </summary>
    void ReportEnvironmentBlocked(int x, int z)
    {
        bool landing = EnvironmentManager.instance != null
                    && EnvironmentManager.instance.IsProtectedEnvironmentTile(x, z);

        ReportError(landing ? GameErrors.CannotBuildOnLandingLocation
                            : GameErrors.CannotBuildOnEnvironment);
    }

    /// <summary>
    /// Analógia k <see cref="WouldTerrainEditHitCityBuilding"/>, ale pre
    /// ENVIRONMENT objekty. Pre každý vrchol, ktorý by úprava terénu zmenila
    /// (vrátane kaskády cez TerrainCollapse), overí až 4 tily, ktorých je daný
    /// vrchol rohom. Ak ktorýkoľvek nesie strom/kameň/landing location, úprava
    /// terénu sa zablokuje.
    /// </summary>
    bool WouldTerrainEditHitEnvironment(Vector3 snapPoint, bool levelUp)
    {
        if (TerrainManager.instance == null || EnvironmentManager.instance == null) return false;

        var affected = TerrainManager.instance.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        for (int i = 0; i < affected.Count; i++)
        {
            int vx = affected[i].x;
            int vz = affected[i].y;

            if (IsEnvironmentBlockedTile(vx, vz) ||
                IsEnvironmentBlockedTile(vx, vz - 1) ||
                IsEnvironmentBlockedTile(vx - 1, vz) ||
                IsEnvironmentBlockedTile(vx - 1, vz - 1))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Spracuje Demolish nad environment tile.
    ///   • STROM / KAMEŇ  → odstráni sa. ZADARMO – žiadna cena ani refund
    ///     (ConstructionCosts.EnvironmentDemolishRefund).
    ///   • LANDING LOCATION → neodstráni sa, ohlási sa chyba (je trvalá).
    ///
    /// Vracia true, ak bol klik spotrebovaný environmentom (volajúci má
    /// prerušiť ďalšie spracovanie Demolish). False = na tile nie je nič naše.
    /// </summary>
    bool TryDemolishEnvironmentTile(int x, int z)
    {
        var env = EnvironmentManager.instance;
        if (env == null || !env.IsEnvironmentTile(x, z)) return false;

        if (env.IsProtectedEnvironmentTile(x, z))
        {
            ReportError(GameErrors.CannotDemolishLandingLocation);
            return true;   // klik spotrebovaný, nič sa nezbúralo
        }

        env.RemoveAt(x, z);   // strom / kameň – zadarmo

        // Environment nie je v tileGrid → efekt demolácie treba spustiť ručne.
        TerrainEditHighlight.GetOrCreate().FlashTile(x, z, TerrainEditHighlight.FlashType.Demolish);
        return true;
    }


    // =====================================================================
    // INICIALIZÁCIA
    // =====================================================================

    private void Awake()
    {
        instance = this;

        // Hudba sa pripraví a spustí čo najskôr – ešte pred Start() ostatných
        // systémov, aby hrala okamžite od prvého snímku hry.
        InitMusic();

        // AudioSource pre zvukové efekty – pripravený skôr, než ho ktorýkoľvek
        // systém stihne použiť.
        InitSfx();
    }

    private void Start()
    {
        cam = Camera.main;
        if (cam == null)
            cam = UnityEngine.Object.FindFirstObjectByType<Camera>();

        // Nastavenie vlastného statického kurzora pre celú hru.
        ApplyCustomCursor();
    }

    // =====================================================================
    // KURZOR MYŠI – implementácia
    // =====================================================================

    /// <summary>
    /// Nastaví vlastný kurzor podľa <see cref="cursorSprite"/> natiahnutého
    /// v Inspektore. Volá sa raz v Start(); kurzor potom platí v celej hre.
    /// Ak sprite nie je priradený, ponechá sa systémový kurzor.
    /// </summary>
    private void ApplyCustomCursor()
    {
        if (cursorSprite == null) return; // žiadny kurzor → systémový ostáva

        Texture2D cursorTexture = ExtractCursorTexture(cursorSprite);
        if (cursorTexture == null) return; // konverzia zlyhala (dôvod už zalogovaný)

        Cursor.SetCursor(cursorTexture, cursorHotspot, cursorMode);
    }

    /// <summary>
    /// Získa <see cref="Texture2D"/> zo sprite pre Cursor.SetCursor.
    ///
    /// Ak sprite pokrýva celú svoju textúru (bežný prípad – samostatný PNG),
    /// vráti textúru priamo (bez potreby Read/Write). Ak je sprite výrezom
    /// z väčšej textúry (atlas), vykopíruje príslušné pixely – to už ale
    /// vyžaduje zapnuté "Read/Write Enabled" na zdrojovej textúre.
    /// </summary>
    private Texture2D ExtractCursorTexture(Sprite sprite)
    {
        Texture2D srcTex = sprite.texture;
        if (srcTex == null)
        {
            Debug.LogError($"[GameManager] Kurzor '{sprite.name}' nemá platnú textúru.");
            return null;
        }

        Rect rect = sprite.textureRect;

        // Sprite = celá textúra? Potom ju vieme použiť priamo.
        bool isWholeTexture =
               Mathf.Approximately(rect.x, 0f)
            && Mathf.Approximately(rect.y, 0f)
            && Mathf.Approximately(rect.width, srcTex.width)
            && Mathf.Approximately(rect.height, srcTex.height);

        if (isWholeTexture)
            return srcTex;

        // Výrez z atlasu → skopírujeme pixely daného rectu do novej textúry.
        // GetPixels používa počiatok v ĽAVOM DOLNOM rohu, rovnako ako textureRect,
        // takže výsledok ostane správne orientovaný.
        try
        {
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);

            Texture2D cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cropped.SetPixels(srcTex.GetPixels(
                Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), w, h));
            cropped.Apply();
            return cropped;
        }
        catch (UnityException)
        {
            Debug.LogError(
                $"[GameManager] Kurzor '{sprite.name}' je výrez z atlasu, ale jeho " +
                $"textúra nemá zapnuté 'Read/Write Enabled'. Zapni ho v import " +
                $"settings textúry, alebo použi samostatný PNG obrázok ako kurzor.");
            return null;
        }
    }

    // =====================================================================
    // HUDBA NA POZADÍ – implementácia
    // =====================================================================

    /// <summary>
    /// Vytvorí AudioSource pre hudbu a spustí tú alternatívu, ktorú si hráč
    /// zvolil v Main Menu (TracklistA aj TracklistB = náhodné prehrávanie
    /// vlastného poľa klipov). Volá sa raz v Awake().
    ///
    /// AudioSource sa vytvorí VŽDY (aj keď nie je čo hrať), aby hlasitosť
    /// hudby aj MusicSlider fungovali pre obe alternatívy cez jedno miesto.
    /// </summary>
    private void InitMusic()
    {
        // Použijeme existujúci AudioSource na tomto GameObjecte, ak tam je;
        // inak si vlastný vytvoríme, aby netreba nič nastavovať ručne v scéne.
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            musicSource = gameObject.AddComponent<AudioSource>();

        musicSource.playOnAwake = false;  // spustenie riadime sami nižšie
        musicSource.volume = Mathf.Clamp01(musicVolume);
        musicSource.mute = false;
        musicSource.spatialBlend = 0f;    // 2D zvuk – nezávislý od pozície kamery

        // Ktorú alternatívu si hráč zvolil v Main Menu (SettingsPanel -> ToggleGroup)?
        activeTracklist = MusicSettings.SelectedTracklist;

        StartActiveTracklist();
    }

    /// <summary>
    /// Spustí hudbu podľa <see cref="activeTracklist"/>. Dá sa volať aj
    /// opakovane – najprv všetko zastaví a vynuluje stav shuffle.
    /// </summary>
    private void StartActiveTracklist()
    {
        if (musicSource == null) return;

        shuffleRunning = false;
        shuffleOrder.Clear();
        shuffleIndex = -1;
        musicSource.Stop();

        // Obe alternatívy sú shuffle zoznamy – rozdiel je len v tom, ktoré pole
        // klipov sa použije. Prázdne pole → záložný jeden klip v loope.
        activeShuffleClips = GetClipsForTracklist(activeTracklist);

        if (HasAnyClip(activeShuffleClips)) StartShuffleTracklist();
        else StartSingleTracklist();
    }

    /// <summary>
    /// Vráti pole klipov, ktoré patrí danej alternatíve:
    /// TracklistA → 'Tracklist A Music Clips', TracklistB → 'Shuffle Music Clips'.
    /// </summary>
    private AudioClip[] GetClipsForTracklist(MusicTracklist tracklist)
    {
        return (tracklist == MusicTracklist.TracklistB)
            ? shuffleMusicClips
            : tracklistAMusicClips;
    }

    /// <summary>Je v danom poli aspoň jeden priradený klip?</summary>
    private bool HasAnyClip(AudioClip[] clips)
    {
        if (clips == null) return false;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) return true;
        return false;
    }

    /// <summary>
    /// ZÁLOHA – jeden klip "Music Clip" v nekonečnej slučke. Použije sa len
    /// vtedy, keď je pole klipov zvolenej alternatívy prázdne.
    /// </summary>
    private void StartSingleTracklist()
    {
        if (musicClip == null)
        {
            // Žiadny klip → hra beží bez hudby (rovnako ako pôvodne).
            Debug.Log("[GameManager] Hudba: zoznam klipov zvolenej alternatívy je prázdny " +
                      "a ani záložný 'Music Clip' nie je priradený – hra beží bez hudby.");
            return;
        }

        musicSource.clip = musicClip;
        musicSource.loop = true;          // album sa opakuje donekonečna
        musicSource.Play();
    }

    /// <summary>
    /// SPOLOČNÉ PRE OBE ALTERNATÍVY – náhodné prehrávanie poľa klipov (shuffle).
    /// Vygeneruje náhodné poradie a pustí prvú skladbu; ďalšie púšťa Update()
    /// vždy, keď predchádzajúca dohrá.
    /// </summary>
    private void StartShuffleTracklist()
    {
        BuildShuffleOrder();

        if (shuffleOrder.Count == 0)
        {
            // Poistka – nemalo by nastať (HasAnyClip() to už overil).
            StartSingleTracklist();
            return;
        }

        musicSource.loop = false;   // striedanie skladieb riadime sami
        shuffleRunning = true;
        shuffleIndex = -1;
        PlayNextShuffleTrack();
    }

    /// <summary>
    /// Vytvorí náhodné poradie (Fisher–Yates) indexov tých prvkov aktívneho
    /// poľa klipov (activeShuffleClips), ktoré naozaj obsahujú klip. Prázdne
    /// sloty v Inspectore sa teda jednoducho preskočia.
    /// </summary>
    private void BuildShuffleOrder()
    {
        shuffleOrder.Clear();
        if (activeShuffleClips == null) return;

        for (int i = 0; i < activeShuffleClips.Length; i++)
            if (activeShuffleClips[i] != null) shuffleOrder.Add(i);

        for (int i = shuffleOrder.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int tmp = shuffleOrder[i];
            shuffleOrder[i] = shuffleOrder[j];
            shuffleOrder[j] = tmp;
        }
    }

    /// <summary>
    /// Pustí ďalšiu skladbu z náhodného poradia. Po dohratí posledného kola
    /// sa poradie premieša nanovo (ak je zapnuté 'Reshuffle After Each Round')
    /// tak, aby sa tá istá skladba nezopakovala hneď dvakrát po sebe.
    /// </summary>
    private void PlayNextShuffleTrack()
    {
        if (musicSource == null || activeShuffleClips == null || shuffleOrder.Count == 0) return;

        shuffleIndex++;

        if (shuffleIndex >= shuffleOrder.Count)
        {
            int lastPlayed = shuffleOrder[shuffleOrder.Count - 1];

            if (reshuffleAfterEachRound)
            {
                BuildShuffleOrder();

                // Aby prvá skladba nového kola nebola tá istá, ktorá práve dohrala.
                if (shuffleOrder.Count > 1 && shuffleOrder[0] == lastPlayed)
                {
                    int last = shuffleOrder.Count - 1;
                    int tmp = shuffleOrder[0];
                    shuffleOrder[0] = shuffleOrder[last];
                    shuffleOrder[last] = tmp;
                }
            }

            shuffleIndex = 0;
        }

        AudioClip next = activeShuffleClips[shuffleOrder[shuffleIndex]];
        if (next == null) return; // poistka (klip odstránený za behu)

        musicSource.clip = next;
        musicSource.loop = false;
        musicSource.Play();
    }

    /// <summary>
    /// Volá sa z Update() – v shuffle režime (TracklistA aj TracklistB) sleduje,
    /// či skladba dohrala, a hneď púšťa ďalšiu. Ak beží len záložný jeden klip
    /// v loope, nerobí nič (tam loopuje priamo AudioSource).
    /// </summary>
    private void UpdateMusicShuffle()
    {
        if (!shuffleRunning) return;
        if (musicSource == null || musicSource.clip == null) return;
        if (musicSource.isPlaying) return;   // skladba stále hrá (mute ju nezastaví)

        PlayNextShuffleTrack();
    }

    /// <summary>Alternatíva hudby, ktorá je práve aktívna (A alebo B – obe shuffle).</summary>
    public MusicTracklist ActiveTracklist => activeTracklist;

    /// <summary>
    /// Prepnutie alternatívy hudby za behu hry (hudba sa reštartuje). Hlavné
    /// prepínanie je v Main Menu, toto je len voliteľné API, keby si chcel
    /// rovnaký prepínač pridať aj do hernej scény.
    /// </summary>
    public void SetMusicTracklist(MusicTracklist tracklist, bool saveToSettings = true)
    {
        activeTracklist = tracklist;
        if (saveToSettings) MusicSettings.SelectedTracklist = tracklist;
        StartActiveTracklist();
    }


    /// <summary>
    /// Nastavenie hlasitosti hudby za behu (0–1), napr. z options menu.
    ///
    /// Platí pre OBE alternatívy naraz (TracklistA aj TracklistB), pretože
    /// obe hrajú cez ten istý <see cref="musicSource"/>. MusicSlider v
    /// SettingsMenuUI teda netreba nijako vetviť.
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null) musicSource.volume = musicVolume;
    }

    /// <summary>
    /// Aktuálna hlasitosť hudby (0–1). Slúži na inicializáciu MusicSlider
    /// v SettingsMenuUI pri otvorení okna nastavení.
    /// </summary>
    public float MusicVolume => musicVolume;

    /// <summary>
    /// Klávesa F1 – prepnutie zobrazovania VŠETKÝCH tooltipov v hre (toggle).
    /// </summary>
    private void HandleTooltipToggleShortcut()
    {
        if (!Input.GetKeyDown(tooltipToggleKey)) return;

        ToggleTooltips();
    }

    /// <summary>
    /// Prepne globálne zobrazovanie tooltipov (HoverTooltip.GlobalTooltipsEnabled).
    /// Verejné, takže sa dá zavolať aj z UI tlačidla (napr. Options menu), nielen klávesou F1.
    /// </summary>
    public void ToggleTooltips()
    {
        bool newState = !HoverTooltip.GlobalTooltipsEnabled;
        HoverTooltip.SetGlobalEnabled(newState);
        Debug.Log($"[GameManager] Tooltipy {(newState ? "ZAPNUTÉ" : "VYPNUTÉ")} (klávesa {tooltipToggleKey}).");
    }

    // =====================================================================
    // ZVUKOVÉ EFEKTY – implementácia
    // =====================================================================

    /// <summary>
    /// Pripraví spoločný AudioSource pre všetky zvukové efekty.
    /// Volá sa raz v Awake(), rovnako ako InitMusic().
    /// </summary>
    private void InitSfx()
    {
        // Vlastný AudioSource (oddelený od hudobného), aby sa efekty a hudba
        // navzájom neovplyvňovali – napr. mute hudby klávesou M nechá efekty hrať.
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.clip = null;
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;   // 2D zvuk – rovnaká hlasitosť bez ohľadu na pozíciu
        sfxSource.volume = 1f;         // reálnu hlasitosť rieši PlayOneShot nižšie
    }

    /// <summary>
    /// Spoločný "motor" pre všetky efekty: prehrá klip cez PlayOneShot
    /// s hlasitosťou master × hlasitosť daného efektu.
    ///
    /// PlayOneShot (namiesto Play) znamená, že sa efekty môžu prekrývať –
    /// nový zvuk neuseká ten predchádzajúci. Chýbajúci klip sa ticho preskočí,
    /// takže hra funguje aj s nenatiahnutými slotmi.
    /// </summary>
    private void PlaySfx(AudioClip clip, float volume)
    {
        if (clip == null || sfxSource == null) return;

        float finalVolume = Mathf.Clamp01(sfxMasterVolume) * Mathf.Clamp01(volume);
        if (finalVolume <= 0f) return;

        sfxSource.PlayOneShot(clip, finalVolume);
    }

    // ── Verejné spúšťače jednotlivých efektov ────────────────────────────
    // Volanie zvnútra GameManager-u:   PlaySfxUIclick();
    // Volanie z iného skriptu:         GameManager.instance?.PlaySfxUIclick();

    /// <summary>Zvuk ročnej účtovnej uzávierky.</summary>
    public void PlaySfxAnnualReport() => PlaySfx(sfxAnnualReportClip, sfxAnnualReportVolume);

    /// <summary>Zvuk postavenia stavby (rail / road / factory).</summary>
    public void PlaySfxBuildConstruction() => PlaySfx(sfxBuildConstructionClip, sfxBuildConstructionVolume);

    /// <summary>Zvuk demolácie.</summary>
    public void PlaySfxDemolish() => PlaySfx(sfxDemolishClip, sfxDemolishVolume);

    /// <summary>Zvuk chybovej hlášky.</summary>
    public void PlaySfxError() => PlaySfx(sfxErrorClip, sfxErrorVolume);

    /// <summary>Zvuk rozjazdu vlaku.</summary>
    public void PlaySfxTrainStart() => PlaySfx(sfxTrainStartClip, sfxTrainStartVolume);

    /// <summary>Zvuk rozjazdu cestného vozidla.</summary>
    public void PlaySfxVehicleStart() => PlaySfx(sfxVehicleStartClip, sfxVehicleStartVolume);

    /// <summary>Zvuk postavenia tile budovy.</summary>
    public void PlaySfxBuildTile() => PlaySfx(sfxBuildTileClip, sfxBuildTileVolume);

    /// <summary>Zvuk kliknutia v UI.</summary>
    public void PlaySfxUIclick() => PlaySfx(sfxUIclickClip, sfxUIclickVolume);

    /// <summary>
    /// Nastavenie spoločnej hlasitosti efektov za behu (0–1), napr. z options menu.
    /// </summary>
    public void SetSfxMasterVolume(float volume) => sfxMasterVolume = Mathf.Clamp01(volume);

    /// <summary>
    /// Aktuálna spoločná hlasitosť zvukových efektov (0–1). Slúži na
    /// inicializáciu SoundEffectsSlider v SettingsMenuUI pri otvorení okna.
    /// </summary>
    public float SfxMasterVolume => sfxMasterVolume;

    // =====================================================================
    // ZVUK "CASH" – samočinný efekt pri úspešnom predaji
    //
    // Volá sa z TrainSystem.TryExecuteTradeAtStation a
    // VehicleSystem.TryExecuteTradeAtStation – teda presne v momente, keď sa
    // zárobok pripisuje na konto (GameEconomy.AddCredits). Nikde inde sa
    // nevolá; je to jediný zvuk hry, ktorý nespúšťa priama akcia hráča.
    //
    // Prečo sa pozícia rieši cez kameru a nie cez minimapu:
    // Unity má na presne túto otázku hotovú funkciu – Camera.WorldToViewportPoint
    // prevedie svetovú súradnicu na súradnice v zábere (0..1 = na obrazovke).
    // Funguje rovnako pre ortografickú izometrickú kameru, automaticky
    // zohľadňuje zoom aj posun kamery a nevyžaduje žiadne prepočty cez tile
    // indexy ani cez MapSystem. Riešenie cez minimapu (MapViewportIndicator)
    // by muselo tú istú informáciu zložito rekonštruovať z rozmerov obdĺžnika
    // viewportu – výsledok by bol rovnaký, len krehkejší a naviazaný na to,
    // či je minimapa v scéne vôbec prítomná a aktívna.
    // =====================================================================

    /// <summary>
    /// Prehrá zvuk Cash pri úspešnom predaji na stanici – ale LEN ak je
    /// <paramref name="worldPosition"/> (pozícia predávajúceho vlaku / vozidla)
    /// práve v zábere kamery a od posledného Cash uplynul minimálny odstup.
    ///
    /// Obe podmienky sú tu zámerne, a nie na strane volajúceho: TrainSystem
    /// aj VehicleSystem tak volajú metódu bezpodmienečne pri každom predaji
    /// a všetka logika "kedy je vhodné zaznieť" ostáva na jednom mieste.
    /// </summary>
    public void PlaySfxCash(Vector3 worldPosition)
    {
        // 1) Mimo záberu kamery = ticho. Hráč počuje len obchody, ktoré vidí.
        if (cashOnlyWhenVisible && !IsVisibleOnScreen(worldPosition)) return;

        // 2) Anti-kakofónia: viac predajov v tom istom okamihu zaznie ako
        //    jeden zvuk. Unscaled čas, aby limit platil aj pri zrýchlenej hre.
        if (Time.unscaledTime - lastCashPlayTime < cashMinInterval) return;
        lastCashPlayTime = Time.unscaledTime;

        PlaySfx(sfxCashClip, sfxCashVolume);
    }

    /// <summary>
    /// Je zadaná svetová pozícia práve viditeľná v zábere hlavnej kamery?
    ///
    /// Viewport súradnice: (0,0) = ľavý dolný roh obrazu, (1,1) = pravý horný.
    /// Hodnota z je vzdialenosť pred kamerou – záporná znamená "za kamerou".
    /// Tolerancia okraja sa berie z <see cref="cashViewportMargin"/>.
    ///
    /// Ak kamera z akéhokoľvek dôvodu chýba, vráti true – radšej zvuk zahrať,
    /// než kvôli chýbajúcej referencii stratiť spätnú väzbu o zárobku.
    /// </summary>
    private bool IsVisibleOnScreen(Vector3 worldPosition)
    {
        // cam sa nastavuje v Start(); defenzívne ho vieme dohľadať aj tu
        // (napr. ak by prvý obchod prebehol skôr, než sa Start() stihol vykonať).
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) return true;
        }

        Vector3 vp = cam.WorldToViewportPoint(worldPosition);

        if (vp.z < 0f) return false;   // za kamerou

        float m = cashViewportMargin;
        return vp.x >= -m && vp.x <= 1f + m
            && vp.y >= -m && vp.y <= 1f + m;
    }

    // =====================================================================
    // UPDATE – KLÁVESOVÉ SKRATKY + KLIKANIE + POHYB MYŠI
    // =====================================================================

    private void Update()
    {
        HandleEscapeShortcut();

        // Toggle tooltipov musia fungovať kedykoľvek počas hry –
        // preto je volanie ešte PRED bezpečnostnými returnmi na cam / IndAPI nižšie.
        HandleTooltipToggleShortcut();

        // Hudba v režime TracklistB (shuffle) musí plynulo pokračovať ďalšou
        // skladbou aj keď je kamera/IndAPI ešte neinicializované – preto je
        // volanie tiež PRED bezpečnostnými returnmi nižšie.
        UpdateMusicShuffle();

        if (cam == null) return; // bezpečnosť pred null kamerou
        if (IndAPI == null) return; // bezpečnosť pred neinicializovaným IndicatrixAPI

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100000))
        {
            // =========================================================
            // RAIL CONSTRUCTION MENU UI (pôvodná funkcionalita)
            // =========================================================

            // =========================================================
            // RAIL – TUNEL / MOST (RailCrossingSystem)
            // =========================================================
            if (IsRailCrossingMode(CurrentRailConstructionMode)
                && trainInputMode == TrainInputMode.None)
            {
                HandleCrossingInput(hit, RailCrossingSystem.GetOrCreate(),
                    CurrentRailConstructionMode == RailConstructionMode.Tunnel);
            }

            if (CurrentRailConstructionMode != RailConstructionMode.None
                && !IsRailCrossingMode(CurrentRailConstructionMode)
                && trainInputMode == TrainInputMode.None)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    switch (CurrentRailConstructionMode)
                    {
                        case RailConstructionMode.LevelUp:
                        case RailConstructionMode.LevelDown:
                            IndAPI.SnapVertex(hit.point);
                            break;

                        case RailConstructionMode.Demolish:
                        case RailConstructionMode.StationHorizontal:
                        case RailConstructionMode.StationVertical:
                        case RailConstructionMode.RailHorizontal:
                        case RailConstructionMode.RailVertical:
                        case RailConstructionMode.RailCrossroad:
                        case RailConstructionMode.RailCurveRightBottom:
                        case RailConstructionMode.RailCurveLeftBottom:
                        case RailConstructionMode.RailCurveRightTop:
                        case RailConstructionMode.RailCurveLeftTop:
                        case RailConstructionMode.DepotHorizontalBottom:
                        case RailConstructionMode.DepotVerticalBottom:
                        case RailConstructionMode.DepotHorizontalTop:
                        case RailConstructionMode.DepotVerticalTop:
                        case RailConstructionMode.RailSwitchHorizontalBottom:
                        case RailConstructionMode.RailSwitchHorizontalTop:
                        case RailConstructionMode.RailSwitchVerticalBottom:
                        case RailConstructionMode.RailSwitchVerticalTop:
                            IndAPI.SnapLineFace(hit.point);
                            break;
                    }
                }

                // ON CLICK
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // [ERROR GUARD] Stavba (placement) na UŽ OBSADENOM tile
                        // je zakázaná. Ak je aktuálny režim placement (kladie
                        // SetTile s tileID != 0) a cieľový tile už nie je
                        // prázdny, NIČ sa nepostaví a hráčovi sa zobrazí chyba.
                        // Demolish (SetTile = 0) a Level Up/Down sem nespadajú –
                        // IsRailPlacementMode ich vyradí.
                        if (IsRailPlacementMode(CurrentRailConstructionMode))
                        {
                            Vector2Int targetIdx = IndAPI.SnapTileIndex(hit.point);
                            var targetTile = IndAPI.GetTileByIndexAny(targetIdx.x, targetIdx.y);

                            // [ERROR GUARD] Na tile s MESTSKOU BUDOVOU sa nesmie
                            // stavať. Budovy miest nie sú v tileGrid – vedie ich
                            // CityManager – preto sa kontrolujú samostatne. Blokuje
                            // sa LEN tile s budovou, nie celý región mesta.
                            if (CityManager.instance != null
                                && CityManager.instance.IsCityTile(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildOnBuilding);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Na tile so STROMOM / KAMEŇOM /
                            // LANDING LOCATION sa nesmie stavať. Objekty nie sú
                            // v tileGrid – vedie ich EnvironmentManager.
                            if (IsEnvironmentBlockedTile(targetIdx.x, targetIdx.y))
                            {
                                ReportEnvironmentBlocked(targetIdx.x, targetIdx.y);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }


                            // [ERROR GUARD] Na TOVÁREŇ (TileCategory.Factory) sa
                            // nesmie postaviť nič z RAIL výstavbových módov.
                            // Špecifickejšia hláška ako generická "occupied tile"
                            // nižšie – preto sa kontroluje skôr.
                            if (targetTile.category == IndicatrixAPI.TileCategory.Factory)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // ─────────────────────────────────────────────────
                            // ZMIEŠANÁ KRIŽOVATKA RAIL + ROAD
                            // ─────────────────────────────────────────────────
                            // [ERROR GUARD] Na existujúcu zmiešanú križovatku sa
                            // nesmie položiť už nič (ani križovatka, ani koľaj).
                            if (targetTile.category == IndicatrixAPI.TileCategory.RailRoadCrossing)
                            {
                                ReportError(GameErrors.CannotBuildOnLevelCrossing);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // Priama koľaj KOLMO na priamu cestu → automaticky vznikne
                            // zmiešaná križovatka (výnimka z guardu obsadeného tile).
                            var railLevelCrossing = IndicatrixAPI.LevelCrossingForRailOnRoad(
                                CurrentRailConstructionMode, targetTile);
                            if (railLevelCrossing != IndicatrixAPI.LevelCrossingMode.None)
                            {
                                // [ERROR GUARD] Križovatka len na rovine (nie na rampe).
                                if (!IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                                {
                                    ReportError(GameErrors.CannotBuildLevelCrossingOnTerrain);
                                    IndAPI.HideAllSnapVisuals();
                                    return;
                                }

                                // Platí sa cena položenej koľaje (RailHorizontal/Vertical).
                                if (!TryChargeRailBuild(CurrentRailConstructionMode))
                                {
                                    IndAPI.HideAllSnapVisuals();
                                    return;
                                }

                                PlaySfxBuildTile();
                                IndAPI.SetLevelCrossing(targetIdx.x, targetIdx.y, railLevelCrossing);
                                IndAPI.SnapMeshFace(hit.point);
                                TrainSys?.OnMapChanged();
                                VehicleSys?.OnMapChanged();
                                return;
                            }

                            // [ERROR GUARD] Priama koľaj ROVNOBEŽNE s priamou cestou.
                            if (IsRailStraightMode(CurrentRailConstructionMode)
                                && targetTile.category == IndicatrixAPI.TileCategory.Road
                                && IndicatrixAPI.IsStraightTile(targetTile, out _))
                            {
                                ReportError(GameErrors.CannotBuildLevelCrossingParallel);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            if (targetTile.tileID != 0)
                            {
                                ReportError(GameErrors.CannotBuildOnOccupiedTile);
                                IndAPI.HideAllSnapVisuals();
                                return; // mutex režimy – ostatné vetvy sú neaktívne, návrat je bezpečný
                            }

                            // [ERROR GUARD] Na tile, ktorý leží na alebo pod
                            // hladinou vody, sa nesmie stavať NIČ z RAIL
                            // výstavbových módov. Terén treba najprv navýšiť
                            // (LevelUp) nad hladinu vody.
                            if (IndAPI.IsFaceWater(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildOnWater);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Pod MOSTOM smie stáť koľaj/cesta/stanica/
                            // depo len pri dostatočnej svetlej výške – mosty RAIL aj ROAD.
                            if (!CrossingSystemBase.HasClearanceForStructureAny(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildUnderBridge);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica nesmie vzniknúť v ochrannej
                            // zóne (3×3) inej stanice. Kontrola je kategória-
                            // agnostická – blokuje RAIL aj ROAD stanicu v okolí.
                            // Len pre režimy umiestnenia stanice (tileID 2).
                            if (IsRailStationMode(CurrentRailConstructionMode)
                                && IsStationNearby(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationNearStation);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica sa smie stavať len na
                            // VODOROVNOM tile (4 vertexy face s rovnakým Y).
                            if (IsRailStationMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Mesto, do ktorého teritória stanica
                            // patrí, vyčerpalo zoznam názvov (max 20 prípon = max
                            // 20 staníc na mesto, RAIL + ROAD spolu).
                            if (IsRailStationMode(CurrentRailConstructionMode)
                                && CityManager.instance != null
                                && !CityManager.instance.CanAssignStationName(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildMoreStationsForCity);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Depo sa rovnako smie stavať len na
                            // vodorovnom tile.
                            if (IsRailDepotMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildDepotOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] ROVNÝ koľajový diel (Horizontal/
                            // Vertical) sa smie stavať na rovine ALEBO na rampe.
                            if (IsRailStraightMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y)
                                && !IndAPI.IsFaceRamp(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Ostatné koľajové diely (crossroad,
                            // curves, switches) sa smú stavať len na rovine.
                            if (IsRailFlatOnlyTrackMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov. Cena sa odpočíta
                            // hneď tu (placement vetvy v switch už nezlyhajú),
                            // takže odpočet aj postavenie sú atomické.
                            if (!TryChargeRailBuild(CurrentRailConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }
                        }

                        switch (CurrentRailConstructionMode)
                        {
                            case RailConstructionMode.LevelUp:
                                {
                                    PlaySfxBuildTile();

                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Okrajové vrcholy mapy sú
                                    // zamknuté – terén na samom kraji mriežky
                                    // sa upravovať nedá.
                                    if (TerrainManager.instance.IsProtectedBorderVertex(snapPoint.x, snapPoint.z))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainAtMapEdge);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Nepresiahnuť maximálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, true))
                                    {
                                        ReportError(GameErrors.MaxTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zmeniť
                                    // žiadny vrchol obsadeného tile – a to ani
                                    // cez kaskádu (TerrainCollapse), preto vopred
                                    // simulujeme celú operáciu.
                                    if (WouldTerrainEditHitOccupied(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť MOST
                                    // (LevelUp pod mostovkou) ani TUNEL (LevelDown nad tunelom).
                                    if (WouldTerrainEditHitCrossing(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainNearCrossing);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile s MESTSKOU BUDOVOU (mimo tileGrid).
                                    if (WouldTerrainEditHitCityBuilding(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile so stromom / kameňom / landing location.
                                    if (WouldTerrainEditHitEnvironment(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOnEnvironment);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRailBuild(RailConstructionMode.LevelUp))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    ApplyTerrainEdit(snapPoint, true); // úprava terénu + hnedé zvýraznenie dotknutých tilov
                                }
                                break;

                            case RailConstructionMode.LevelDown:
                                {
                                    PlaySfxBuildTile();

                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Okrajové vrcholy mapy sú
                                    // zamknuté – terén na samom kraji mriežky
                                    // sa upravovať nedá.
                                    if (TerrainManager.instance.IsProtectedBorderVertex(snapPoint.x, snapPoint.z))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainAtMapEdge);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Nepodliezť minimálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, false))
                                    {
                                        ReportError(GameErrors.MinTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (WouldTerrainEditHitOccupied(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť MOST
                                    // (LevelUp pod mostovkou) ani TUNEL (LevelDown nad tunelom).
                                    if (WouldTerrainEditHitCrossing(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainNearCrossing);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile s MESTSKOU BUDOVOU (mimo tileGrid).
                                    if (WouldTerrainEditHitCityBuilding(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile so stromom / kameňom / landing location.
                                    if (WouldTerrainEditHitEnvironment(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOnEnvironment);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRailBuild(RailConstructionMode.LevelDown))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    ApplyTerrainEdit(snapPoint, false); // úprava terénu + hnedé zvýraznenie dotknutých tilov
                                }
                                break;

                            case RailConstructionMode.Demolish:
                                {
                                    PlaySfxDemolish();

                                    Vector2Int tileIdx = IndAPI.SnapTileIndex(hit.point);
                                    IndicatrixAPI.TileData tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);

                                    // [ERROR GUARD] Mestská budova sa nedá zbúrať –
                                    // nie je v tileGrid (vedie ju CityManager).
                                    if (CityManager.instance != null
                                        && CityManager.instance.IsCityTile(tileIdx.x, tileIdx.y))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                                    {
                                        Debug.LogWarning($"[GameManager] RAIL Demolish ignorovaný – tile [{tileIdx.x},{tileIdx.y}] patrí ROAD systému. Použite ROAD menu.");
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ZMIEŠANÁ KRIŽOVATKA] RAIL Demolish odstráni LEN
                                    // KOĽAJ – na tile ostane priama cesta. Refund 50 %
                                    // z ceny koľaje.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.RailRoadCrossing)
                                    {
                                        if (TrainSys != null && TrainSys.IsTileOccupiedByTrain(tileIdx.x, tileIdx.y))
                                        {
                                            ReportError(GameErrors.CannotDemolishTrackWithTrain);
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }

                                        var remainingRoad = IndicatrixAPI.RoadViewOf(tileInfo);
                                        IndAPI.SetTile(hit.point, 1, (RoadConstructionMode)remainingRoad.stateID);
                                        IndAPI.SnapMeshFace(hit.point);
                                        TrainSys?.OnMapChanged();
                                        VehicleSys?.OnMapChanged();
                                        RefundDemolishedTile(IndicatrixAPI.RailViewOf(tileInfo));
                                        break;
                                    }

                                    // [ENVIRONMENT] Strom / kameň sa dá zbúrať
                                    // (ZADARMO – bez ceny aj bez refundu).
                                    // Landing location je trvalá – ohlási chybu.
                                    if (TryDemolishEnvironmentTile(tileIdx.x, tileIdx.y))
                                    {
                                        IndAPI.SnapMeshFace(hit.point);
                                        break;
                                    }

                                    // [MOST / TUNEL] Demolish na hlavu mosta/tunela
                                    // (alebo na PRÁZDNY tile pod mostom) zbúra CELÝ
                                    // prechod – nie je možné zbúrať len jeho časť.
                                    // Refund 50 % z reálne zaplatenej ceny, raz za objekt.
                                    if (RailCrossingSystem.instance != null)
                                    {
                                        var crossingResult = RailCrossingSystem.instance.TryDemolishAt(
                                            tileIdx.x, tileIdx.y, tileInfo, out uint crossingRefund);

                                        if (crossingResult == CrossingSystemBase.CrossingDemolishResult.BlockedByVehicle)
                                        {
                                            ReportError(GameErrors.CannotDemolishCrossingWithTrain);
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }

                                        if (crossingResult == CrossingSystemBase.CrossingDemolishResult.Demolished)
                                        {
                                            if (GameEconomy.instance != null && crossingRefund > 0u)
                                                GameEconomy.instance.AddCredits(crossingRefund);
                                            IndAPI.SnapMeshFace(hit.point);
                                            TrainSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // [MOST] Prázdny tile pod CESTNÝM mostom – RAIL
                                    // Demolish most zbúrať nesmie.
                                    if (tileInfo.tileID == 0
                                        && RoadCrossingSystem.instance != null
                                        && RoadCrossingSystem.instance.IsBridgeSpanTile(tileIdx.x, tileIdx.y))
                                    {
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // TOVÁREŇ: demolish zmaže CELÚ továreň (celý
                                    // footprint), nie len kliknutý tile. Klik na
                                    // ľubovoľný tile footprintu nájde inštanciu cez
                                    // FactoryRegistry. Vo výstavbe sa demolovať
                                    // nesmie – až po dosiahnutí 100 % výstavby.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                                    {
                                        FactoryInstance fInst = FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
                                        if (fInst != null)
                                        {
                                            if (fInst.IsUnderConstruction)
                                            {
                                                Debug.LogWarning($"[GameManager] Demolish zamietnutý – továreň '{fInst.Name}' " +
                                                                 $"je vo výstavbe ({fInst.BuildPercent}%). Demoláciu skúste po dokončení.");
                                                IndAPI.HideAllSnapVisuals();
                                                break;
                                            }

                                            DemolishFactory(fInst);
                                            IndAPI.SnapMeshFace(hit.point);
                                            TrainSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // Depo nie je možné zmazať ak existuje vlak prináležiaci tomuto depu.
                                    // Depo je možné zmazať až po zmazaní vlaku cez DCRemoveTrainButton.
                                    if (tileInfo.tileID == 3)
                                    {
                                        var existingTrain = TrainSys?.GetTrain(tileIdx.x, tileIdx.y);
                                        if (existingTrain != null)
                                        {
                                            Debug.LogWarning($"[GameManager] Depo [{tileIdx.x},{tileIdx.y}] nie je možné zmazať – vlak stále existuje. Najprv vráťte vlak do depa a potom ho vymažte cez UI.");
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }
                                    }

                                    // [ERROR GUARD] Na dlaždici sa PRÁVE nachádza vlak
                                    // (lokomotíva alebo vagón) – koľaj/stanicu/výhybku
                                    // pod idúcou súpravou zbúrať nemožno. Hráč musí
                                    // počkať, kým vlak prejde, alebo ho vrátiť do depa.
                                    if (TrainSys != null && TrainSys.IsTileOccupiedByTrain(tileIdx.x, tileIdx.y))
                                    {
                                        Debug.LogWarning($"[GameManager] RAIL Demolish zamietnutý – na tile [{tileIdx.x},{tileIdx.y}] sa práve nachádza vlak.");
                                        ReportError(GameErrors.CannotDemolishTrackWithTrain);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // Demolish – vymažeme dlaždicu
                                    IndAPI.SetTile(hit.point, 0, RailConstructionMode.None);
                                    IndAPI.SnapMeshFace(hit.point);
                                    TrainSys?.OnMapChanged();

                                    // Ak bola zmazaná STANICA (tileID == 2), zmaž aj
                                    // jej trvalý popisok. tileInfo bol prečítaný PRED
                                    // mazaním, takže ešte nesie pôvodné tileID.
                                    if (tileInfo.tileID == 2)
                                    {
                                        StationLabelManager.Instance.RemoveLabel(tileIdx.x, tileIdx.y);
                                        // Uvoľni príponu názvu → znova dostupná pre mesto.
                                        CityManager.instance?.ReleaseStationName(tileIdx.x, tileIdx.y);
                                    }

                                    // Refund 50 % zo základnej ceny zdemolovaného
                                    // prvku (tileInfo bol prečítaný pred mazaním).
                                    RefundDemolishedTile(tileInfo);
                                }
                                break;

                            case RailConstructionMode.RailHorizontal:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailVertical:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCrossroad:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCrossroad);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RailConstructionMode.RailCurveLeftBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveLeftBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveRightBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveRightBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveLeftTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveLeftTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveRightTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveRightTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchHorizontalBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchHorizontalTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchVerticalBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchVerticalTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RailConstructionMode.StationHorizontal:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 2, RailConstructionMode.StationHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Rezervuj názov stanice (mesto + prvá voľná
                                    // prípona) a vytvor popisok s menným názvom.
                                    Vector2Int sIdx = IndAPI.SnapTileIndex(hit.point);
                                    CityManager.instance?.AssignStationName(sIdx.x, sIdx.y);
                                    StationLabelManager.Instance.CreateLabel(sIdx.x, sIdx.y);
                                }
                                break;
                            case RailConstructionMode.StationVertical:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 2, RailConstructionMode.StationVertical);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Rezervuj názov stanice (mesto + prvá voľná
                                    // prípona) a vytvor popisok s menným názvom.
                                    Vector2Int sIdx = IndAPI.SnapTileIndex(hit.point);
                                    CityManager.instance?.AssignStationName(sIdx.x, sIdx.y);
                                    StationLabelManager.Instance.CreateLabel(sIdx.x, sIdx.y);
                                }
                                break;

                            case RailConstructionMode.DepotHorizontalTop:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotHorizontalBottom:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotVerticalTop:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotVerticalBottom:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                        }
                    }
                }
            }

            // =========================================================
            // ROAD CONSTRUCTION MENU UI (analógia k RAIL vetve)
            //
            // Spracovanie OnMovement a OnClick pre cestný systém. Volá
            // IndAPI.SetTile preťaženie pre RoadConstructionMode, ktoré
            // ukladá tile s TileCategory.Road (a tým je oddelené od RAIL
            // grafu, takže TrainSystem cestné tiles nevidí).
            // =========================================================

            // =========================================================
            // ROAD – TUNEL / MOST (RoadCrossingSystem) – analógia k RAIL
            // =========================================================
            if (IsRoadCrossingMode(CurrentRoadConstructionMode)
                && trainInputMode == TrainInputMode.None)
            {
                HandleCrossingInput(hit, RoadCrossingSystem.GetOrCreate(),
                    CurrentRoadConstructionMode == RoadConstructionMode.Tunnel);
            }

            if (CurrentRoadConstructionMode != RoadConstructionMode.None
                && !IsRoadCrossingMode(CurrentRoadConstructionMode)
                && trainInputMode == TrainInputMode.None)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    switch (CurrentRoadConstructionMode)
                    {
                        case RoadConstructionMode.LevelUp:
                        case RoadConstructionMode.LevelDown:
                            IndAPI.SnapVertex(hit.point);
                            break;

                        case RoadConstructionMode.Demolish:
                        case RoadConstructionMode.StationHorizontal:
                        case RoadConstructionMode.StationVertical:
                        case RoadConstructionMode.RoadHorizontal:
                        case RoadConstructionMode.RoadVertical:
                        case RoadConstructionMode.RoadCrossroad:
                        case RoadConstructionMode.RoadCurveRightBottom:
                        case RoadConstructionMode.RoadCurveLeftBottom:
                        case RoadConstructionMode.RoadCurveRightTop:
                        case RoadConstructionMode.RoadCurveLeftTop:
                        case RoadConstructionMode.DepotHorizontalBottom:
                        case RoadConstructionMode.DepotVerticalBottom:
                        case RoadConstructionMode.DepotHorizontalTop:
                        case RoadConstructionMode.DepotVerticalTop:
                        case RoadConstructionMode.RoadSwitchHorizontalBottom:
                        case RoadConstructionMode.RoadSwitchHorizontalTop:
                        case RoadConstructionMode.RoadSwitchVerticalBottom:
                        case RoadConstructionMode.RoadSwitchVerticalTop:
                            IndAPI.SnapLineFace(hit.point);
                            break;
                    }
                }

                // ON CLICK
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // [ERROR GUARD] Rovnako ako pri RAIL: stavba (placement)
                        // na už obsadenom tile je zakázaná. Kontrola je
                        // kategória-agnostická (GetTileByIndexAny) – obsadený
                        // RAIL aj ROAD tile rovnako blokuje prepísanie.
                        if (IsRoadPlacementMode(CurrentRoadConstructionMode))
                        {
                            Vector2Int targetIdx = IndAPI.SnapTileIndex(hit.point);
                            var targetTile = IndAPI.GetTileByIndexAny(targetIdx.x, targetIdx.y);

                            // [ERROR GUARD] Na tile s MESTSKOU BUDOVOU sa nesmie
                            // stavať (rovnako ako pri RAIL). Blokuje sa LEN tile
                            // s budovou, nie celý región mesta.
                            if (CityManager.instance != null
                                && CityManager.instance.IsCityTile(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildOnBuilding);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Na tile so STROMOM / KAMEŇOM /
                            // LANDING LOCATION sa nesmie stavať. Objekty nie sú
                            // v tileGrid – vedie ich EnvironmentManager.
                            if (IsEnvironmentBlockedTile(targetIdx.x, targetIdx.y))
                            {
                                ReportEnvironmentBlocked(targetIdx.x, targetIdx.y);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Na TOVÁREŇ (TileCategory.Factory) sa
                            // nesmie postaviť nič z ROAD výstavbových módov.
                            if (targetTile.category == IndicatrixAPI.TileCategory.Factory)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // ─────────────────────────────────────────────────
                            // ZMIEŠANÁ KRIŽOVATKA RAIL + ROAD (analógia k RAIL)
                            // ─────────────────────────────────────────────────
                            if (targetTile.category == IndicatrixAPI.TileCategory.RailRoadCrossing)
                            {
                                ReportError(GameErrors.CannotBuildOnLevelCrossing);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // Priama cesta KOLMO na priamu koľaj → zmiešaná križovatka.
                            var roadLevelCrossing = IndicatrixAPI.LevelCrossingForRoadOnRail(
                                CurrentRoadConstructionMode, targetTile);
                            if (roadLevelCrossing != IndicatrixAPI.LevelCrossingMode.None)
                            {
                                if (!IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                                {
                                    ReportError(GameErrors.CannotBuildLevelCrossingOnTerrain);
                                    IndAPI.HideAllSnapVisuals();
                                    return;
                                }

                                // Platí sa cena položenej cesty (RoadHorizontal/Vertical).
                                if (!TryChargeRoadBuild(CurrentRoadConstructionMode))
                                {
                                    IndAPI.HideAllSnapVisuals();
                                    return;
                                }

                                PlaySfxBuildTile();
                                IndAPI.SetLevelCrossing(targetIdx.x, targetIdx.y, roadLevelCrossing);
                                IndAPI.SnapMeshFace(hit.point);
                                TrainSys?.OnMapChanged();
                                VehicleSys?.OnMapChanged();
                                return;
                            }

                            // [ERROR GUARD] Priama cesta ROVNOBEŽNE s priamou koľajou.
                            if (IsRoadStraightMode(CurrentRoadConstructionMode)
                                && targetTile.category == IndicatrixAPI.TileCategory.Rail
                                && IndicatrixAPI.IsStraightTile(targetTile, out _))
                            {
                                ReportError(GameErrors.CannotBuildLevelCrossingParallel);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            if (targetTile.tileID != 0)
                            {
                                ReportError(GameErrors.CannotBuildOnOccupiedTile);
                                IndAPI.HideAllSnapVisuals();
                                return; // mutex režimy – ostatné vetvy sú neaktívne, návrat je bezpečný
                            }

                            // [ERROR GUARD] Na tile, ktorý leží na alebo pod
                            // hladinou vody, sa nesmie stavať NIČ z ROAD
                            // výstavbových módov (analógia k RAIL). Terén treba
                            // najprv navýšiť (LevelUp) nad hladinu vody.
                            if (IndAPI.IsFaceWater(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildOnWater);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Pod MOSTOM smie stáť koľaj/cesta/stanica/
                            // depo len pri dostatočnej svetlej výške – mosty RAIL aj ROAD.
                            if (!CrossingSystemBase.HasClearanceForStructureAny(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildUnderBridge);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica nesmie vzniknúť v ochrannej
                            // zóne (3×3) inej stanice. Rovnako ako pri RAIL je
                            // kontrola kategória-agnostická (blokuje RAIL aj ROAD
                            // stanicu v okolí). Len pre režimy stanice (tileID 2).
                            if (IsRoadStationMode(CurrentRoadConstructionMode)
                                && IsStationNearby(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationNearStation);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica sa smie stavať len na
                            // VODOROVNOM tile (4 vertexy face s rovnakým Y).
                            if (IsRoadStationMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Mesto, do ktorého teritória stanica
                            // patrí, vyčerpalo zoznam názvov (max 20 prípon = max
                            // 20 staníc na mesto, RAIL + ROAD spolu).
                            if (IsRoadStationMode(CurrentRoadConstructionMode)
                                && CityManager.instance != null
                                && !CityManager.instance.CanAssignStationName(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildMoreStationsForCity);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Depo sa rovnako smie stavať len na
                            // vodorovnom tile.
                            if (IsRoadDepotMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildDepotOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] ROVNÝ cestný diel (Horizontal/
                            // Vertical) sa smie stavať na rovine ALEBO na rampe.
                            if (IsRoadStraightMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y)
                                && !IndAPI.IsFaceRamp(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Ostatné cestné diely (crossroad,
                            // curves, switches) sa smú stavať len na rovine.
                            if (IsRoadFlatOnlyTrackMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov (analógia k RAIL).
                            if (!TryChargeRoadBuild(CurrentRoadConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }
                        }

                        switch (CurrentRoadConstructionMode)
                        {
                            case RoadConstructionMode.LevelUp:
                                {
                                    PlaySfxBuildTile();

                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Okrajové vrcholy mapy sú
                                    // zamknuté – terén na samom kraji mriežky
                                    // sa upravovať nedá.
                                    if (TerrainManager.instance.IsProtectedBorderVertex(snapPoint.x, snapPoint.z))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainAtMapEdge);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Nepresiahnuť maximálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, true))
                                    {
                                        ReportError(GameErrors.MaxTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Rovnako ako pri RAIL –
                                    // simulujeme celú operáciu vrátane kaskády.
                                    if (WouldTerrainEditHitOccupied(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť MOST
                                    // (LevelUp pod mostovkou) ani TUNEL (LevelDown nad tunelom).
                                    if (WouldTerrainEditHitCrossing(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainNearCrossing);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile s MESTSKOU BUDOVOU (mimo tileGrid).
                                    if (WouldTerrainEditHitCityBuilding(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile so stromom / kameňom / landing location.
                                    if (WouldTerrainEditHitEnvironment(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOnEnvironment);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRoadBuild(RoadConstructionMode.LevelUp))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    ApplyTerrainEdit(snapPoint, true); // úprava terénu + hnedé zvýraznenie dotknutých tilov
                                }
                                break;

                            case RoadConstructionMode.LevelDown:
                                {
                                    PlaySfxBuildTile();

                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Okrajové vrcholy mapy sú
                                    // zamknuté – terén na samom kraji mriežky
                                    // sa upravovať nedá.
                                    if (TerrainManager.instance.IsProtectedBorderVertex(snapPoint.x, snapPoint.z))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainAtMapEdge);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Nepodliezť minimálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, false))
                                    {
                                        ReportError(GameErrors.MinTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (WouldTerrainEditHitOccupied(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť MOST
                                    // (LevelUp pod mostovkou) ani TUNEL (LevelDown nad tunelom).
                                    if (WouldTerrainEditHitCrossing(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainNearCrossing);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile s MESTSKOU BUDOVOU (mimo tileGrid).
                                    if (WouldTerrainEditHitCityBuilding(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zasiahnuť
                                    // tile so stromom / kameňom / landing location.
                                    if (WouldTerrainEditHitEnvironment(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOnEnvironment);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRoadBuild(RoadConstructionMode.LevelDown))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    ApplyTerrainEdit(snapPoint, false); // úprava terénu + hnedé zvýraznenie dotknutých tilov
                                }
                                break;

                            case RoadConstructionMode.Demolish:
                                {
                                    PlaySfxDemolish();

                                    // Demolish v ROAD režime zmaže len cestnú dlaždicu
                                    // (rail dlaždice cez ROAD Demolish nemusí byť cieľom,
                                    // ale IndAPI.SetTile pre ROAD prepíše len Road kategóriu).
                                    // Ak by sme chceli aby ROAD Demolish nezasiahol RAIL tile,
                                    // pridáme kontrolu kategórie:
                                    Vector2Int rdTileIdx = IndAPI.SnapTileIndex(hit.point);
                                    var tileInfo = IndAPI.GetTileByIndexAny(rdTileIdx.x, rdTileIdx.y);

                                    // [ERROR GUARD] Mestská budova sa nedá zbúrať –
                                    // nie je v tileGrid (vedie ju CityManager).
                                    if (CityManager.instance != null
                                        && CityManager.instance.IsCityTile(rdTileIdx.x, rdTileIdx.y))
                                    {
                                        ReportError(GameErrors.CannotBuildOnBuilding);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ENVIRONMENT] Strom / kameň sa dá zbúrať
                                    // (ZADARMO – bez ceny aj bez refundu).
                                    // Landing location je trvalá – ohlási chybu.
                                    if (TryDemolishEnvironmentTile(rdTileIdx.x, rdTileIdx.y))
                                    {
                                        IndAPI.SnapMeshFace(hit.point);
                                        break;
                                    }


                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                                    {
                                        Debug.LogWarning("[GameManager] ROAD Demolish ignorovaný – tile patrí RAIL systému. Pre demoláciu RAIL prvkov použite RAIL menu.");
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ZMIEŠANÁ KRIŽOVATKA] ROAD Demolish odstráni LEN
                                    // CESTU – na tile ostane priama koľaj. Refund 50 %
                                    // z ceny cesty.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.RailRoadCrossing)
                                    {
                                        if (VehicleSys != null && VehicleSys.IsTileOccupiedByVehicle(rdTileIdx.x, rdTileIdx.y))
                                        {
                                            ReportError(GameErrors.CannotDemolishRoadWithVehicle);
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }

                                        var remainingRail = IndicatrixAPI.RailViewOf(tileInfo);
                                        IndAPI.SetTile(hit.point, 1, (RailConstructionMode)remainingRail.stateID);
                                        IndAPI.SnapMeshFace(hit.point);
                                        TrainSys?.OnMapChanged();
                                        VehicleSys?.OnMapChanged();
                                        RefundDemolishedTile(IndicatrixAPI.RoadViewOf(tileInfo));
                                        break;
                                    }

                                    // [MOST / TUNEL] Demolish na hlavu CESTNÉHO mosta/
                                    // tunela (alebo na PRÁZDNY tile pod ním) zbúra
                                    // CELÝ prechod. Analógia k RAIL Demolish.
                                    if (RoadCrossingSystem.instance != null)
                                    {
                                        var roadCrossingResult = RoadCrossingSystem.instance.TryDemolishAt(
                                            rdTileIdx.x, rdTileIdx.y, tileInfo, out uint roadCrossingRefund);

                                        if (roadCrossingResult == CrossingSystemBase.CrossingDemolishResult.BlockedByVehicle)
                                        {
                                            ReportError(GameErrors.CannotDemolishCrossingWithVehicle);
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }

                                        if (roadCrossingResult == CrossingSystemBase.CrossingDemolishResult.Demolished)
                                        {
                                            if (GameEconomy.instance != null && roadCrossingRefund > 0u)
                                                GameEconomy.instance.AddCredits(roadCrossingRefund);
                                            IndAPI.SnapMeshFace(hit.point);
                                            VehicleSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // [MOST] Prázdny tile pod RAIL mostom – ROAD
                                    // Demolish most zbúrať nesmie.
                                    if (tileInfo.tileID == 0
                                        && RailCrossingSystem.instance != null
                                        && RailCrossingSystem.instance.IsBridgeSpanTile(rdTileIdx.x, rdTileIdx.y))
                                    {
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (TryDemolishEnvironmentTile(rdTileIdx.x, rdTileIdx.y))
                                    {
                                        IndAPI.SnapMeshFace(hit.point);
                                        break;
                                    }

                                    // TOVÁREŇ: demolish zmaže CELÚ továreň (celý
                                    // footprint), nie len kliknutý tile – rovnako
                                    // ako v RAIL Demolish vetve. Vo výstavbe sa
                                    // demolovať nesmie.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                                    {
                                        FactoryInstance fInst = FactoryRegistry.GetFactoryAt(rdTileIdx.x, rdTileIdx.y);
                                        if (fInst != null)
                                        {
                                            if (fInst.IsUnderConstruction)
                                            {
                                                Debug.LogWarning($"[GameManager] Demolish zamietnutý – továreň '{fInst.Name}' " +
                                                                 $"je vo výstavbe ({fInst.BuildPercent}%). Demoláciu skúste po dokončení.");
                                                IndAPI.HideAllSnapVisuals();
                                                break;
                                            }

                                            DemolishFactory(fInst);
                                            IndAPI.SnapMeshFace(hit.point);
                                            VehicleSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // ROAD Depo nie je možné zmazať ak existuje vozidlo prináležiace
                                    // tomuto depu. Vozidlo treba najprv odstrániť cez UI
                                    // (DCRemoveVehicleButton v DepotRoadConstructionMenuUI).
                                    // Analogické správanie s RAIL Demolish vyššie.
                                    if (tileInfo.tileID == 3 && tileInfo.category == IndicatrixAPI.TileCategory.Road)
                                    {
                                        var existingVehicle = VehicleSys?.GetVehicle(rdTileIdx.x, rdTileIdx.y);
                                        if (existingVehicle != null)
                                        {
                                            Debug.LogWarning($"[GameManager] ROAD Depo [{rdTileIdx.x},{rdTileIdx.y}] nie je možné zmazať – vozidlo stále existuje. Najprv vráťte vozidlo do depa a potom ho vymažte cez UI.");
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }
                                    }

                                    // [ERROR GUARD] Na dlaždici sa PRÁVE nachádza vozidlo –
                                    // cestu/stanicu pod idúcim vozidlom zbúrať nemožno.
                                    // Hráč musí počkať, kým vozidlo prejde, alebo ho
                                    // vrátiť do depa.
                                    if (VehicleSys != null && VehicleSys.IsTileOccupiedByVehicle(rdTileIdx.x, rdTileIdx.y))
                                    {
                                        Debug.LogWarning($"[GameManager] ROAD Demolish zamietnutý – na tile [{rdTileIdx.x},{rdTileIdx.y}] sa práve nachádza vozidlo.");
                                        ReportError(GameErrors.CannotDemolishRoadWithVehicle);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    IndAPI.SetTile(hit.point, 0, RoadConstructionMode.None);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Ak bola zmazaná STANICA (tileID == 2), zmaž aj
                                    // jej trvalý popisok. tileInfo bol prečítaný PRED
                                    // mazaním, takže ešte nesie pôvodné tileID.
                                    if (tileInfo.tileID == 2)
                                    {
                                        StationLabelManager.Instance.RemoveLabel(rdTileIdx.x, rdTileIdx.y);
                                        // Uvoľni príponu názvu → znova dostupná pre mesto.
                                        CityManager.instance?.ReleaseStationName(rdTileIdx.x, rdTileIdx.y);
                                    }

                                    // Refund 50 % zo základnej ceny zdemolovaného
                                    // prvku (tileInfo bol prečítaný pred mazaním).
                                    RefundDemolishedTile(tileInfo);
                                }
                                break;

                            case RoadConstructionMode.RoadHorizontal:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadVertical:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCrossroad:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCrossroad);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.RoadCurveLeftBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveLeftBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveRightBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveRightBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveLeftTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveLeftTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveRightTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveRightTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.RoadSwitchHorizontalBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchHorizontalTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchVerticalBottom:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchVerticalTop:
                                {
                                    PlaySfxBuildTile();

                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.StationHorizontal:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 2, RoadConstructionMode.StationHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Rezervuj názov stanice (mesto + prvá voľná
                                    // prípona) a vytvor popisok s menným názvom.
                                    Vector2Int sIdx = IndAPI.SnapTileIndex(hit.point);
                                    CityManager.instance?.AssignStationName(sIdx.x, sIdx.y);
                                    StationLabelManager.Instance.CreateLabel(sIdx.x, sIdx.y);
                                }
                                break;
                            case RoadConstructionMode.StationVertical:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 2, RoadConstructionMode.StationVertical);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Rezervuj názov stanice (mesto + prvá voľná
                                    // prípona) a vytvor popisok s menným názvom.
                                    Vector2Int sIdx = IndAPI.SnapTileIndex(hit.point);
                                    CityManager.instance?.AssignStationName(sIdx.x, sIdx.y);
                                    StationLabelManager.Instance.CreateLabel(sIdx.x, sIdx.y);
                                }
                                break;

                            case RoadConstructionMode.DepotHorizontalTop:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotHorizontalBottom:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotVerticalTop:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotVerticalBottom:
                                {
                                    PlaySfxBuildConstruction();

                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                        }

                        // Notifikácia pre VehicleSystem – akákoľvek modifikácia
                        // ROAD grafu (tile placement, Demolish) vyvolá debounce-d
                        // reroute pre bežiace vozidlá. LevelUp/LevelDown sem
                        // tiež spadnú (úprava terénu mení Y waypointov, ale
                        // grafovo nič – debounce timer to ošetrí, žiadne
                        // zbytočné prepočty pri rovine).
                        VehicleSys?.OnMapChanged();
                    }
                }
            }

            // =========================================================
            // FACTORY CONSTRUCTION MENU UI (analógia k RAIL/ROAD vetve)
            //
            // Spracovanie OnMovement a OnClick pre továrenský systém.
            //
            // ODLIŠNOSTI oproti RAIL/ROAD:
            //   - Továreň je VIAC-TILE objekt (footprint 2×3, 3×3, 2×2).
            //   - OnMovement používa IndAPI.SnapAreaFace(...) – zvýrazní
            //     celý obdĺžnik footprintu, nie len 1×1 tile.
            //   - OnClick volá multi-tile IndAPI.SetTile(...) FACTORY
            //     preťaženie, ktoré zapíše textúru do všetkých N tilov a
            //     vráti bool (úspech/zlyhanie kvôli kolízii alebo okraju).
            //   - tileID: 4 = Factory (CoalMine, Forest, IronOreMine, GoldMine,
            //                            SilverMine, Farm, OilWells),
            //             5 = Processing (PowerStation, SawMill, OilRefinery,
            //                            ElectronicsFactory, FurnitureFactory,
            //                            Slaughterhouse, GrainFactory, Smelter,
            //                            GlassFactory).
            // =========================================================

            if (CurrentFactoryConstructionMode != FactoryConstructionMode.None
                && trainInputMode == TrainInputMode.None)
            {
                // ROTÁCIA TOVÁRNE – ODSTRÁNENÁ.
                // Predtým sa footprint otáčal kolieskom myši (Input.mouseScrollDelta).
                // Po prechode tovární na 2D sprity to stratilo zmysel: obrázok je
                // nakreslený z jednej strany, takže otočený footprint (2×3 → 3×2)
                // by prestal sedieť s tým, čo hráč vidí. Koliesko myši tu teraz
                // NEROBÍ NIČ a CurrentFactoryRotation je natrvalo Deg0.

                // ON MOVEMENT – náhľad footprintu pod kurzorom
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapAreaFace(hit.point, CurrentFactoryConstructionMode,
                                        CurrentFactoryRotation);
                }

                // ON CLICK – umiestnenie továrne
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // =================================================
                        // [ERROR GUARD] – PRED-VALIDÁCIA UMIESTNENIA TOVÁRNE
                        // Footprintovú matematiku počíta IndicatrixAPI (jediný
                        // zdroj pravdy). Najprv zistíme rozmery a roh footprintu
                        // pod kurzorom, potom overíme tri situácie a každú
                        // ohlásime VLASTNOU hláškou (SetTile vracia len bool a
                        // nevie POVEDAŤ, prečo by zlyhal).
                        // =================================================
                        if (IndAPI.TryGetFactoryFootprintBounds(
                                hit.point, CurrentFactoryConstructionMode, CurrentFactoryRotation,
                                out int fpOriginX, out int fpOriginZ, out int fpWidth, out int fpDepth))
                        {
                            // Klasifikácia obsadenosti footprintu:
                            //   • aspoň jeden tile je TOVÁREŇ   → továreň na továreň (situácia 2)
                            //   • aspoň jeden tile je RAIL/ROAD → továreň na inú stavbu (situácia 4)
                            //   • aspoň jeden tile je MESTSKÁ BUDOVA → na budovu (nová situácia)
                            bool footprintHasFactory = false;
                            bool footprintHasOther = false;
                            bool footprintHasBuilding = false;
                            bool footprintHasWater = false;
                            bool footprintHasEnvironment = false;
                            bool footprintHasLanding = false;

                            for (int x = fpOriginX; x < fpOriginX + fpWidth; x++)
                            {
                                for (int z = fpOriginZ; z < fpOriginZ + fpDepth; z++)
                                {
                                    // Mestská budova nie je v tileGrid – kontrola cez CityManager.
                                    if (CityManager.instance != null
                                        && CityManager.instance.IsCityTile(x, z))
                                        footprintHasBuilding = true;

                                    // Strom / kameň / landing location – tiež mimo tileGrid.
                                    if (IsEnvironmentBlockedTile(x, z))
                                    {
                                        footprintHasEnvironment = true;
                                        if (EnvironmentManager.instance != null
                                            && EnvironmentManager.instance.IsProtectedEnvironmentTile(x, z))
                                            footprintHasLanding = true;
                                    }

                                    // Voda – stačí, že čo i len jeden tile footprintu
                                    // leží na alebo pod hladinou vody.
                                    if (IndAPI.IsFaceWater(x, z))
                                        footprintHasWater = true;

                                    var ft = IndAPI.GetTileByIndexAny(x, z);
                                    if (ft.tileID == 0) continue;

                                    if (ft.category == IndicatrixAPI.TileCategory.Factory)
                                        footprintHasFactory = true;
                                    else
                                        footprintHasOther = true;
                                }
                            }

                            // (0) Továreň NEMOŽNO postaviť na tile s mestskou budovou.
                            // Stačí jediný tile footprintu s budovou.
                            if (footprintHasBuilding)
                            {
                                ReportError(GameErrors.CannotBuildOnBuilding);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // (0b) Továreň NEMOŽNO postaviť na tile so stromom,
                            // kameňom ani landing location.
                            if (footprintHasEnvironment)
                            {
                                ReportError(footprintHasLanding
                                            ? GameErrors.CannotBuildOnLandingLocation
                                            : GameErrors.CannotBuildOnEnvironment);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // [ERROR GUARD] Továreň NEMOŽNO postaviť, ak čo i len
                            // jeden tile footprintu leží na alebo pod hladinou vody.
                            if (footprintHasWater)
                            {
                                ReportError(GameErrors.CannotBuildOnWater);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // [ERROR GUARD] Továreň (vysoký sprite) NEMOŽNO postaviť
                            // pod most – ani čiastočne.
                            if (CrossingSystemBase.AnyBridgeSpanInRectAny(fpOriginX, fpOriginZ, fpWidth, fpDepth))
                            {
                                ReportError(GameErrors.CannotBuildUnderBridge);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // (2) Továreň NEMOŽNO postaviť na už existujúcu továreň.
                            if (footprintHasFactory)
                            {
                                ReportError(GameErrors.CannotBuildFactoryOnFactory);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // (4) Továreň NEMOŽNO postaviť na koľaj/cestu/stanicu/depo.
                            if (footprintHasOther)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // (3) Továreň NEMOŽNO postaviť v ochrannej zóne inej továrne.
                            if (IsFactoryNearby(fpOriginX, fpOriginZ, fpWidth, fpDepth))
                            {
                                ReportError(GameErrors.CannotBuildFactoryNearFactory);
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov. Cena továrne
                            // (ConstructionCosts.FactoryBuildCost) sa odpočíta HNEĎ
                            // pri položení – hráč nečaká na 100 % výstavby. Geometria
                            // je už overená vyššie, takže nasledujúci SetTile už
                            // nezlyhá → odpočet a postavenie sú atomické.
                            if (!TryChargeFactoryBuild(CurrentFactoryConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                PerformEscapeReset(); // [ESC] po kliknutí s továrňou – zruš FACTORY režim
                                return;
                            }
                        }

                        bool placed = false;

                        // Footprint položenej továrne – vyplní ho rozšírené
                        // SetTile preťaženie cez out parametre. Slúži na
                        // následnú registráciu FactoryInstance.
                        int fOriginX = 0, fOriginZ = 0, fWidth = 0, fDepth = 0;

                        switch (CurrentFactoryConstructionMode)
                        {
                            // ── tileID = 4 → Factory (ťažba surovín) ──
                            case FactoryConstructionMode.CoalMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.CoalMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Forest:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.Forest,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.IronOreMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.IronOreMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GoldMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.GoldMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.SilverMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.SilverMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Farm:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.Farm,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.OilWells:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.OilWells,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;

                            // ── tileID = 5 → Processing (spracovanie surovín) ──
                            case FactoryConstructionMode.PowerStation:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.PowerStation,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.SawMill:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.SawMill,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.OilRefinery:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.OilRefinery,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.ElectronicsFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.ElectronicsFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.FurnitureFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.FurnitureFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Slaughterhouse:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.Slaughterhouse,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GrainFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.GrainFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Smelter:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.Smelter,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GlassFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.GlassFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                        }

                        // Po úspešnom položení:
                        //  1) zaevidujeme FactoryInstance vo FactoryRegistry –
                        //     vznikne dátový objekt továrne (Name, Load/UnLoad
                        //     sklady, pozícia footprintu). Tile mapa drží len
                        //     textúru; ekonomické dáta žijú tu.
                        //  2) obnovíme náhľad footprintu na pozícii kurzora.
                        if (placed)
                        {
                            PlaySfxBuildConstruction();

                            FactoryDefinition def =
                                FactoryDatabase.GetDefinition(CurrentFactoryConstructionMode);

                            FactoryInstance newFactory =
                                FactoryRegistry.Register(def, fOriginX, fOriginZ,
                                                         fWidth, fDepth, CurrentFactoryRotation);

                            // DEFAULTNÉ hodnoty pri položení továrne na tile map:
                            // EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
                            // Konštruktor FactoryInstance ich síce už nastavuje, no
                            // explicitne ich tu (priamo na mieste položenia) zaručíme aj
                            // z ovládacieho kódu, takže pri KAŽDOM položení sú garantované.
                            // ID typu (fixné, v poradí databázy) si inštancia prevzala
                            // z FactoryDefinition.ID už v konštruktore.
                            if (newFactory != null)
                                newFactory.ApplyPlacementDefaults();

                            // Spustíme fázu výstavby: nad stredom továrne sa v
                            // preddefinovanej výške Y zobrazí plávajúci label
                            // "Building process: NN%" a beží časovač s dĺžkou
                            // def.BuildingTime. Kým progres nedosiahne 100 %, je
                            // továreň zablokovaná pre zmeny hodnôt aj demoláciu;
                            // po 100 % label ešte 2 s zostane a potom zmizne.
                            if (newFactory != null)
                                FactoryConstructionManager.Instance.BeginConstruction(newFactory);

                            IndAPI.SnapAreaFace(hit.point, CurrentFactoryConstructionMode,
                                                CurrentFactoryRotation);
                        }
                        else
                        {
                            // FACTORY SetTile vráti false, ak je čo i len jeden
                            // tile footprintu obsadený alebo mimo gridu – t.j.
                            // "nedá sa tu stavať". Ohlásime tú istú chybu ako pri
                            // RAIL/ROAD (jedno univerzálne okno, rovnaký text).
                            ReportError(GameErrors.CannotBuildOnOccupiedTile);
                            IndAPI.HideAllSnapVisuals();
                        }

                        // =================================================
                        // [ESC] PO KLIKNUTÍ S VYBRANOU TOVÁRŇOU
                        // Po každom kliknutí na terén vo FACTORY režime (či už
                        // sa továreň postavila, alebo nie) vykonáme rovnakú
                        // akciu ako klávesa ESC: zruší sa FACTORY režim a
                        // schovajú sa snap visuals (SnapAreaFace).
                        // Rovnaké volanie je aj pred každým "return" v
                        // pred-validácii vyššie.
                        //
                        // "return" – rovnako ako chybové vetvy vyššie ukončí
                        // Update(). Režim je už None, takže bez neho by ten
                        // istý klik v tomto frame spracovala aj vetva
                        // "OTVORENIE DEPOT / FACTORY MENU" nižšie (klik na
                        // práve položenú továreň).
                        // =================================================
                        PerformEscapeReset();
                        return;
                    }
                }
            }

            // =========================================================
            // STAFF → FACTORY ASSIGNMENT (ConstructionModeStaffToFactory)
            //
            // Analógia k FACTORY vetve vyššie, ale namiesto stavania:
            //   • OnMovement – SnapMeshFace(...) zvýrazní 1×1 ŠTVOREC + FACE.
            //   • OnClick    – klik na ľubovoľnú továreň (footprint rieši
            //                  FactoryRegistry.GetFactoryAt) → priradenie /
            //                  porovnanie StaffManagement vs FactoryInstance.
            // =========================================================
            if (IsStaffToFactoryMode)
            {
                // ON MOVEMENT – štvorec + face pod kurzorom.
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapMeshFace(hit.point);
                }

                // ON CLICK – klik na továreň.
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleStaffToFactoryClick(hit.point);
                    }
                }
            }

            // =========================================================
            // VLAKOVÝ VSTUP – LEN AddStations (po stlačení DCDefineRouteButton)
            // =========================================================

            if (trainInputMode == TrainInputMode.AddStations)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapLineFace(hit.point);
                }

                // ON CLICK – akceptujeme len kliky na stanice (depo už máme zapamätané)
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleStationClick(hit.point);
                    }
                }
            }
            // =========================================================
            // CESTNÝ VSTUP – AddStationsRoad (po stlačení DCDefineRouteButton
            // v DepotRoadConstructionMenuUI). Analogická vetva k vlakovej
            // vyššie – akceptuje IBA kliky na ROAD Station tiles.
            // =========================================================
            else if (trainInputMode == TrainInputMode.AddStationsRoad)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapLineFace(hit.point);
                }

                // ON CLICK – akceptujeme len kliky na ROAD stanice
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleRoadStationClick(hit.point);
                    }
                }
            }
            else if (CurrentRailConstructionMode == RailConstructionMode.None
                     && CurrentRoadConstructionMode == RoadConstructionMode.None
                     && CurrentFactoryConstructionMode == FactoryConstructionMode.None
                     && !IsStaffToFactoryMode
                     && trainInputMode == TrainInputMode.None)
            {
                // =====================================================
                // OTVORENIE DEPOT / FACTORY MENU pri kliku na tile (mimo
                // akéhokoľvek režimu konštrukcie / vlakového či cestného
                // vstupu).
                //   • Klik na DEPOT tile (tileID 3) → podľa kategórie depa
                //     sa otvorí buď RAIL alebo ROAD variant Depot menu.
                //   • Klik na ľubovoľný tile FOOTPRINTU továrne → otvorí sa
                //     StatusFactoryMenuUI s informáciami o danej továrni.
                // =====================================================
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // -------------------------------------------------
                        // KLIK NA VLAK (lokomotíva / vagón) alebo VOZIDLO
                        // – idúce aj zastavené. Otvorí sa depo, ktorému
                        // vlak/vozidlo patrí (RAIL alebo ROAD okno).
                        // Má prednosť pred klikom na tile pod ním.
                        // -------------------------------------------------
                        if (TryOpenDepotForClickedConsist(ray, hit.distance))
                            return;

                        Vector2Int tileIdx = IndAPI.SnapTileIndex(hit.point);
                        var tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);
                        if (tileInfo.tileID == 3)
                        {
                            if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                            {
                                DepotRailMenuUI?.OpenForDepot(tileIdx.x, tileIdx.y);
                            }
                            else if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                            {
                                DepotRoadMenuUI?.OpenForDepot(tileIdx.x, tileIdx.y);
                            }
                        }



                        else if (tileInfo.tileID == 2)
                        {
                            // [DIAG] Klik na stanicu – vetva trafená.
                            Debug.Log($"[DIAG][GameManager] Klik na tile [{tileIdx.x},{tileIdx.y}] " +
                                      $"tileID=2, category={tileInfo.category}.");

                            StationInstance station = null;
                            if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                            {
                                // Lacný re-scan – istota, že register vidí všetky
                                // stanice z tile mapy + má aktuálne továrne v zóne.
                                RailStationRegistry.RescanAll();
                                Debug.Log($"[DIAG][GameManager] RailStationRegistry po Rescane: " +
                                          $"Count={RailStationRegistry.Count}");
                                station = RailStationRegistry.GetStationAt(tileIdx.x, tileIdx.y);
                            }
                            else if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                            {
                                RoadStationRegistry.RescanAll();
                                Debug.Log($"[DIAG][GameManager] RoadStationRegistry po Rescane: " +
                                          $"Count={RoadStationRegistry.Count}");
                                station = RoadStationRegistry.GetStationAt(tileIdx.x, tileIdx.y);
                            }
                            else
                            {
                                Debug.LogWarning($"[DIAG][GameManager] tileID==2 ale category={tileInfo.category} " +
                                                 $"– neznáma kategória stanice, ignorujem.");
                            }

                            if (station != null)
                            {
                                Debug.Log($"[DIAG][GameManager] StationInstance OK: {station}. " +
                                          $"Tovární v zóne: {station.Factories.Count}.");

                                var menuRef = StatusStationMenuUI;
                                if (menuRef != null)
                                {
                                    // Odovzdáme aj súradnice tile [x, z] stanice,
                                    // aby okno mohlo zobraziť názov "Station [x, z]".
                                    // tileIdx pochádza z IndAPI.SnapTileIndex →
                                    // Vector2Int(x, z), platí pre RAIL aj ROAD.
                                    menuRef.OpenForStation(station, tileIdx.x, tileIdx.y);
                                }
                                else
                                {
                                    Debug.LogError("[DIAG][GameManager] StatusStationMenuUI ref je NULL – " +
                                                   "komponent nie je v scéne!");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[DIAG][GameManager] StationInstance pre [{tileIdx.x},{tileIdx.y}] " +
                                                 $"je null aj po Rescane. Skontroluj: IndicatrixAPI.GetTileByIndexAny " +
                                                 $"vrátil tileID={tileInfo.tileID}, category={tileInfo.category}, " +
                                                 $"ale register túto stanicu nevidí.");
                            }
                        }






                        else if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                        {
                            // Továreň pozostáva z viac tilov (footprint 2×3,
                            // 3×3, 2×2 ...). FactoryRegistry pri položení
                            // zaregistroval KAŽDÝ tile footprintu, takže klik
                            // na ľubovoľný z nich nájde tú istú FactoryInstance
                            // v O(1). Footprint preto netreba mapovať ručne.
                            FactoryInstance factory =
                                FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
                            if (factory != null)
                            {
                                // Počas výstavby (progres < 100 %) je továreň
                                // zablokovaná – status okno neotvárame, aby sa
                                // nedali meniť množstvá (amount) ani kapacity.
                                if (factory.IsUnderConstruction)
                                {
                                    Debug.Log($"[GameManager] Továreň '{factory.Name}' je vo výstavbe " +
                                              $"({factory.BuildPercent}%) – zablokovaná, skúste po dokončení.");
                                }
                                else
                                {
                                    clickTooltip.HideTooltip();
                                    StatusFactoryMenuUI?.OpenForFactory(factory);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // =====================================================================
    // KLIK NA VLAK / VOZIDLO → OTVORENIE PRÍSLUŠNÉHO DEPA
    //
    // Vlaky aj vozidlá nemajú kolízne komponenty, preto ich Physics.Raycast
    // netrafí. Výber sa robí cez TrainSystem.TryPickTrain /
    // VehicleSystem.TryPickVehicle (lúč vs. Renderer.bounds). Ak lúč trafí
    // oboje, vyhráva bližší objekt. Objekt schovaný za terénom (ďalej než
    // bod zásahu terénu) sa ignoruje.
    // =====================================================================
    bool TryOpenDepotForClickedConsist(Ray ray, float terrainHitDistance)
    {
        // Tolerancia – súprava stojí NA teréne, jej bounds sa môžu mierne
        // prekrývať s bodom zásahu terénu.
        float maxDist = terrainHitDistance + 0.5f;

        bool trainHit = false, vehicleHit = false;
        Vector2Int trainDepot = default, vehicleDepot = default;
        float trainDist = float.MaxValue, vehicleDist = float.MaxValue;

        if (TrainSys != null)
            trainHit = TrainSys.TryPickTrain(ray, out trainDepot, out trainDist)
                       && trainDist <= maxDist;
        if (VehicleSys != null)
            vehicleHit = VehicleSys.TryPickVehicle(ray, out vehicleDepot, out vehicleDist)
                         && vehicleDist <= maxDist;

        if (trainHit && (!vehicleHit || trainDist <= vehicleDist))
        {
            DepotRailMenuUI?.OpenForDepot(trainDepot.x, trainDepot.y);
            return true;
        }
        if (vehicleHit)
        {
            DepotRoadMenuUI?.OpenForDepot(vehicleDepot.x, vehicleDepot.y);
            return true;
        }
        return false;
    }

    // =====================================================================
    // MOSTY A TUNELY (RAIL aj ROAD) – vstup myši
    //
    // Spoločný handler pre obe siete – líši sa len systém (RailCrossingSystem
    // / RoadCrossingSystem), cenník a to, ktorý dopravný systém sa po stavbe
    // prepočíta (TrainSystem / VehicleSystem).
    //
    // TUNEL: 1 klik na rampu. Systém v smere stúpania nájde opačnú rampu
    //   (výjazd). Počas pohybu myši sa na nájdených 2 tiloch zobrazí náhľad
    //   spritu; ak sa nenájdu, náhľad sa nezobrazí a klik ohlási chybu.
    // MOST: 2 kliky – začiatok a koniec. Po prvom kliku sa pri pohybe myši
    //   ukazuje náhľad celého mosta, ak je platný.
    // ESC / pravé tlačidlo: zruší prvý klik mosta, ďalšie ukončí režim.
    // =====================================================================

    void HandleCrossingInput(RaycastHit hit, CrossingSystemBase crossings, bool isTunnel)
    {
        if (crossings == null) return;

        // Otvorené okno výberu typu mosta – mapa nereaguje (náhľad plánovaného
        // mosta drží CrossingSystemBase, kým hráč nevyberie typ alebo nezruší).
        if (crossings.IsAwaitingVariantSelection) return;

        // Kurzor nad UI → náhľad sa v CrossingSystemBase.LateUpdate sám skryje.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // ON MOVEMENT – štvorec + face pod kurzorom a náhľad spritu
        IndAPI.SnapLineFace(hit.point);
        Vector2Int tile = IndAPI.SnapTileIndex(hit.point);

        if (isTunnel) crossings.UpdateTunnelPreview(tile);
        else crossings.UpdateBridgePreview(tile);

        // ON CLICK
        if (!Input.GetMouseButtonDown(0)) return;

        if (isTunnel)
        {
            if (!crossings.ValidateTunnel(tile, out var tunnelPlan, out string tunnelError))
            {
                ReportError(tunnelError);
                IndAPI.HideAllSnapVisuals();
                return;
            }

            uint tunnelCost = crossings.TunnelBuildCost(tunnelPlan.Length);
            if (!TryChargeBuild(tunnelCost))
            {
                IndAPI.HideAllSnapVisuals();
                return;
            }

            PlaySfxBuildConstruction();
            crossings.Build(tunnelPlan, tunnelCost);
            NotifyCrossingNetworkChanged(crossings);
            return;
        }

        // ── MOST – 1. klik: začiatok ──
        if (!crossings.HasPendingBridgeStart)
        {
            if (!crossings.ValidateBridgeHead(tile, out string headError))
            {
                ReportError(headError);
                IndAPI.HideAllSnapVisuals();
                return;
            }

            PlaySfxBuildTile();
            crossings.SetPendingBridgeStart(tile);
            return;
        }

        // ── MOST – 2. klik: koniec ──
        Vector2Int bridgeStart = crossings.PendingBridgeStart;
        crossings.ClearPendingBridgeStart();

        if (!crossings.ValidateBridge(bridgeStart, tile, out var bridgePlan, out string bridgeError))
        {
            ReportError(bridgeError);
            IndAPI.HideAllSnapVisuals();
            return;
        }

        // ── MOST – výber TYPU (A / B / C) ──
        // Pred stavbou sa zobrazí okno výberu typu príslušnej siete. Most sa
        // postaví až po kliku na typ; zavretie okna (X) = ESC → nič sa nepostaví.
        GameSelectBridgesMenuUIBase selector = crossings.Network == CrossingNetwork.Road
            ? (GameSelectBridgesMenuUIBase)GameRoadSelectBridgesMenuUI.Instance
            : GameRailSelectBridgesMenuUI.Instance;

        if (selector == null)
        {
            Debug.LogWarning($"[GameManager] Okno výberu typu mosta ({crossings.Network}) nie je v scéne – " +
                             "stavia sa most typu A bez výberu.");
            BuildBridgeVariant(crossings, bridgePlan, 0);
            return;
        }

        var variantCosts = new uint[CrossingSystemBase.BridgeVariantCount];
        for (int v = 0; v < variantCosts.Length; v++)
            variantCosts[v] = crossings.BridgeBuildCost(bridgePlan.Length, v);

        IndAPI.HideAllSnapVisuals();

        selector.Open(bridgePlan.Length, variantCosts,
            onSelect: variant =>
            {
                crossings.EndVariantSelection();
                BuildBridgeVariant(crossings, bridgePlan, variant);
            },
            onCancel: () => crossings.EndVariantSelection());

        crossings.BeginVariantSelection(bridgePlan);
    }

    /// <summary>
    /// Postaví most vybraného TYPU (0 = A, 1 = B, 2 = C). Plán sa overí ZNOVA –
    /// kým bolo otvorené okno výberu, mapa sa mohla zmeniť (napr. peniaze, iný
    /// systém). Chyby a nedostatok kreditov sa hlásia rovnako ako doteraz.
    /// </summary>
    void BuildBridgeVariant(CrossingSystemBase crossings, CrossingSystemBase.CrossingData plan, int variant)
    {
        if (crossings == null || plan == null) return;

        if (!crossings.ValidateBridge(plan.startHead, plan.endHead, out var freshPlan, out string error))
        {
            ReportError(error);
            IndAPI.HideAllSnapVisuals();
            return;
        }

        freshPlan.variant = Mathf.Clamp(variant, 0, CrossingSystemBase.BridgeVariantCount - 1);

        uint cost = crossings.BridgeBuildCost(freshPlan.Length, freshPlan.variant);
        if (!TryChargeBuild(cost))
        {
            IndAPI.HideAllSnapVisuals();
            return;
        }

        PlaySfxBuildConstruction();
        crossings.Build(freshPlan, cost);
        NotifyCrossingNetworkChanged(crossings);
    }

    /// <summary>Po stavbe prechodu prepočíta trasy dopravného systému danej siete.</summary>
    static void NotifyCrossingNetworkChanged(CrossingSystemBase crossings)
    {
        if (crossings.Network == CrossingNetwork.Road) VehicleSys?.OnMapChanged();
        else TrainSys?.OnMapChanged();
    }

    /// <summary>
    /// Analógia k WouldTerrainEditHitOccupied pre mosty a tunely OBOCH sietí.
    /// Hlavy sú v tileGrid (stráži ich existujúci guard), tu sa kontroluje
    /// priestor POD mostom (LevelUp) a NAD tunelom (LevelDown).
    /// </summary>
    bool WouldTerrainEditHitCrossing(Vector3 snapPoint, bool levelUp)
    {
        if (!CrossingSystemBase.AnyCrossingsExist() || TerrainManager.instance == null)
            return false;

        var affected = TerrainManager.instance.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        return CrossingSystemBase.WouldTerrainEditHitCrossingAny(affected, levelUp);
    }

    // =====================================================================
    // KLÁVESOVÉ SKRATKY – už len ESC
    // =====================================================================

    void HandleEscapeShortcut()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) && !Input.GetMouseButtonDown(2))
            return;

        // [MOST] Ak je zadaný len ZAČIATOK mosta, ESC / pravé tlačidlo zruší
        // najprv LEN ten (režim Most ostáva aktívny). Ďalšie ESC ukončí režim.
        if (RailCrossingSystem.instance != null
            && CurrentRailConstructionMode == RailConstructionMode.Bridge
            && RailCrossingSystem.instance.TryCancelPendingBridgeStart())
            return;

        if (RoadCrossingSystem.instance != null
            && CurrentRoadConstructionMode == RoadConstructionMode.Bridge
            && RoadCrossingSystem.instance.TryCancelPendingBridgeStart())
            return;

        // Celá reset logika je vyčlenená do verejnej metódy PerformEscapeReset(),
        // aby ju bolo možné vyvolať aj programovo (napr. po kliknutí na
        // CloseWindowButton ktoréhokoľvek konštrukčného okna) – nielen klávesou ESC.
        PerformEscapeReset();
    }

    /// <summary>
    /// Vykoná presne tú istú akciu ako stlačenie klávesy ESC: zruší aktuálny
    /// Staff→Factory / Define Route / RAIL / ROAD / FACTORY režim a schová
    /// všetky snap visuals (SnapLineFace, SnapVertex, SnapAreaFace, ...).
    ///
    /// Metóda je verejná a bezpečne volateľná z UI (napr. z OnCloseButtonClick
    /// jednotlivých *UIwindow skriptov), aby sa po zatvorení okna nezasekol
    /// snapping indikátor na tile mape. Je idempotentná – ak nie je aktívny
    /// žiadny režim, len defenzívne schová visuals.
    /// </summary>
    public void PerformEscapeReset()
    {
        bool didSomething = false;

        clickTooltip.HideTooltip();

        // Mosty / tunely – zruš prvý klik mosta aj náhľad pod kurzorom.
        RailCrossingSystem.instance?.CancelAll();
        RoadCrossingSystem.instance?.CancelAll();

        // Okná výberu typu mosta – zavrieť bez stavby (bez vyhľadávania v scéne).
        if (GameRailSelectBridgesMenuUI.ExistingInstance != null)
            GameRailSelectBridgesMenuUI.ExistingInstance.Close();
        if (GameRoadSelectBridgesMenuUI.ExistingInstance != null)
            GameRoadSelectBridgesMenuUI.ExistingInstance.Close();

        // ESC – Zrušenie režimu prideľovania personálu (Staff→Factory).
        if (IsStaffToFactoryMode)
        {
            ExitStaffToFactoryMode();
            Debug.Log("[GameManager] Staff→Factory režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho vlakového ALEBO cestného režimu (Define Route)
        if (trainInputMode != TrainInputMode.None)
        {
            // Pre korektné notifikácie zachováme info o zrušenom režime
            // pred resetom – aby sme vedeli, ktoré UI okno notifikovať.
            bool wasRail = (trainInputMode == TrainInputMode.AddStations);
            bool wasRoad = (trainInputMode == TrainInputMode.AddStationsRoad);

            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI?.HideAllSnapVisuals();

            if (wasRail)
            {
                DepotRailMenuUI?.OnRouteDefineCancelled();
                Debug.Log("[GameManager] Vlakový režim (Define Route) zrušený cez ESC.");
            }
            else if (wasRoad)
            {
                DepotRoadMenuUI?.OnRouteDefineCancelled();
                Debug.Log("[GameManager] Cestný režim (Define Route) zrušený cez ESC.");
            }
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho režimu konštrukcie koľají
        // (Rail/Station/Depot/LevelUp/Demolish ...).
        // Toto rieši aj prípad, keď hráč zatvorí RailConstructionMenuUI okno
        // (cez X tlačidlo alebo iným spôsobom) bez toho, aby sa explicitne
        // resetoval režim – zaseknutý SnapLineFace indikátor zmizne.
        if (CurrentRailConstructionMode != RailConstructionMode.None)
        {
            SetTerrainMode(RailConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] RAIL konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho ROAD režimu konštrukcie
        // Analogicky k RAIL bloku vyššie.
        if (CurrentRoadConstructionMode != RoadConstructionMode.None)
        {
            SetTerrainMode(RoadConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho FACTORY režimu konštrukcie
        // Analogicky k RAIL/ROAD blokom vyššie. Rieši aj prípad, keď hráč
        // zatvorí FactoryConstructionMenuUI okno bez explicitného resetu –
        // zaseknutý SnapAreaFace indikátor footprintu zmizne.
        if (CurrentFactoryConstructionMode != FactoryConstructionMode.None)
        {
            SetTerrainMode(FactoryConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] FACTORY konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        if (!didSomething)
        {
            // Defenzívne – aj keby niečo ostalo zaseknuté, schováme visuals
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // KLIK NA TOVÁREŇ PRI Staff→Factory PRIDEĽOVANÍ PERSONÁLU
    //
    // Volá sa z Update() v režime IsStaffToFactoryMode po kliku ĽAVÝM
    // tlačidlom mimo UI. Vyhodnotí, či sa kliklo na továreň, a podľa zhody
    // ID prenesie hodnoty z pendingStaff (StaffManagement) do FactoryInstance.
    // =====================================================================
    void HandleStaffToFactoryClick(Vector3 hitPoint)
    {
        if (IndAPI == null) return;

        // Bezpečnostná poistka – bez naplnenej dávky personálu nie je čo robiť.
        if (pendingStaff == null)
        {
            Debug.LogWarning("[GameManager] Staff→Factory: pendingStaff == null – režim ukončený.");
            ExitStaffToFactoryMode();
            return;
        }

        // Tile pod kurzorom (kategória-agnosticky – chceme vidieť aj továreň).
        Vector2Int tileIdx = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);

        // Klik mimo továrne – ostávame v režime (môže kliknúť znova).
        if (tileInfo.category != IndicatrixAPI.TileCategory.Factory)
        {
            Debug.Log($"[GameManager] Staff→Factory: tile [{tileIdx.x},{tileIdx.y}] nie je továreň – ignorujem.");
            ReportError(GameErrors.StaffTargetNotAFactory);
            return;
        }

        // Footprint (2×3 / 3×3 / 2×2) je zohľadnený: GetFactoryAt mapuje
        // ktorýkoľvek tile footprintu na tú istú FactoryInstance.
        FactoryInstance factory = FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
        if (factory == null)
        {
            Debug.LogWarning($"[GameManager] Staff→Factory: na tile [{tileIdx.x},{tileIdx.y}] " +
                             $"je kategória Factory, ale FactoryRegistry továreň nevidí.");
            return;
        }

        // Vo výstavbe (progres < 100 %) – továreň je zablokovaná pre zmeny.
        if (factory.IsUnderConstruction)
        {
            Debug.Log($"[GameManager] Továreň '{factory.Name}' je vo výstavbe " +
                      $"({factory.BuildPercent}%) – personál nemožno priradiť. Skúste po dokončení.");
            return;
        }

        // ── POROVNANIE ID (StaffManagement vs FactoryInstance) ──
        if (factory.ID == pendingStaff.ID)
        {
            // Zhoda ID → prenes hodnoty a označ továreň ako obsadenú.
            factory.LevelSalary = pendingStaff.LevelSalary;
            factory.EmployeeSalary = pendingStaff.EmployeeSalary;
            factory.OccupancyFlag = true;

            Debug.Log($"[GameManager] Staff PRIRADENÝ do '{factory.Name}' " +
                      $"(ID={factory.ID}): LevelSalary={factory.LevelSalary}, " +
                      $"EmployeeSalary={factory.EmployeeSalary}, OccupancyFlag=true.");
        }
        else
        {
            // Nezhoda ID → len OccupancyFlag = false, ostatné polia bez zmeny.
            factory.OccupancyFlag = false;

            Debug.Log($"[GameManager] Staff ID={pendingStaff.ID} ≠ továreň '{factory.Name}' " +
                      $"ID={factory.ID} → OccupancyFlag=false (ostatné hodnoty nezmenené).");
        }

        // Jedno priradenie = jeden klik na továreň. Po vykonaní akcie režim
        // ukončíme a skryjeme snap vizuál.
        ExitStaffToFactoryMode();
    }

    // =====================================================================
    // KLIK NA STANICU PRI Define Route (analogicky pôvodnej W-fáze)
    // =====================================================================

    void HandleStationClick(Vector3 hitPoint)
    {
        if (IndAPI == null || TrainSys == null) return;
        if (!pendingDepotTile.HasValue) return;

        Vector2Int tile = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileData = IndAPI.GetTileByIndexAny(tile.x, tile.y);

        // Stanica musí byť RAIL kategórie – ROAD stanice (autobusové
        // zastávky) pre vlaky neplatia.
        if (tileData.tileID == 2 && tileData.category == IndicatrixAPI.TileCategory.Rail)
        {
            var depTile = pendingDepotTile.Value;
            bool ok = TrainSys.AddStation(depTile.x, depTile.y, tile.x, tile.y);
            if (ok)
            {
                Debug.Log($"[GameManager] Stanica [{tile.x},{tile.y}] pridaná do zoznamu.");
                DepotRailMenuUI?.OnStationAdded(depTile.x, depTile.y);
            }
            else
            {
                Debug.LogWarning($"[GameManager] Stanica [{tile.x},{tile.y}] nebola pridaná.");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Kliknite na políčko RAIL Station! Pre ukončenie znovu stlačte tlačidlo Define Route alebo ESC.");
        }
    }

    /// <summary>
    /// ROAD analógia HandleStationClick. Akceptuje iba kliky na ROAD Station
    /// dlaždice (autobus./nákl. zastávky kategórie Road). RAIL stanice
    /// pre vozidlá neplatia.
    /// </summary>
    void HandleRoadStationClick(Vector3 hitPoint)
    {
        if (IndAPI == null || VehicleSys == null) return;
        if (!pendingRoadDepotTile.HasValue) return;

        Vector2Int tile = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileData = IndAPI.GetTileByIndexAny(tile.x, tile.y);

        if (tileData.tileID == 2 && tileData.category == IndicatrixAPI.TileCategory.Road)
        {
            var depTile = pendingRoadDepotTile.Value;
            bool ok = VehicleSys.AddStation(depTile.x, depTile.y, tile.x, tile.y);
            if (ok)
            {
                Debug.Log($"[GameManager] ROAD Stanica [{tile.x},{tile.y}] pridaná do zoznamu vozidla.");
                DepotRoadMenuUI?.OnStationAdded(depTile.x, depTile.y);
            }
            else
            {
                Debug.LogWarning($"[GameManager] ROAD Stanica [{tile.x},{tile.y}] nebola pridaná.");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Kliknite na políčko ROAD Station! Pre ukončenie znovu stlačte tlačidlo Define Route alebo ESC.");
        }
    }

    // =====================================================================
    // PUBLIC API PRE DepotRailConstructionMenuUI
    //
    // Tieto metódy nahrádzajú pôvodné klávesové skratky Q, S, P, R, T.
    // UI volá konkrétnu metódu s identifikáciou depa (dx, dz), nad ktorým
    // bolo otvorené – takže nie je potrebný žiadny ďalší klik na tile mapu.
    // =====================================================================

    /// <summary>
    /// Q-ekvivalent: vytvorenie vlaku v zadanom depe.
    ///
    /// trainTypeIndex / wagonTypeIndex sú voľby z TrainTypeDropdown /
    /// WagonTypeDropdown – posúvajú sa do TrainSystem.CreateTrain, kde sa
    /// podľa nich zostaví dátová štruktúra súpravy (TrainStock.cs).
    /// </summary>
    public bool RequestCreateTrainForDepot(int dx, int dz, int wagonCount,
                                           int trainTypeIndex, int wagonTypeIndex)
    {
        if (TrainSys == null)
        {
            Debug.LogError("[GameManager] TrainSystem.instance je null!");
            return false;
        }
        if (IndAPI == null) return false;

        var tileInfo = IndAPI.GetTileByIndexAny(dx, dz);
        if (tileInfo.tileID != 3 || tileInfo.category != IndicatrixAPI.TileCategory.Rail)
        {
            Debug.LogWarning($"[GameManager] Tile [{dx},{dz}] nie je RAIL Depot.");
            return false;
        }

        // Aplikujeme aktuálne nastavený počet vagónov pre vlak
        int clamped = Mathf.Clamp(wagonCount, 1, 10);
        TrainSys.wagonCount = clamped;

        // EKONOMIKA: cena vlaku = Cost lokomotívy + clamped * Cost vagónu.
        // Odpočítame PRED vytvorením; ak vytvorenie zlyhá, sumu vrátime.
        uint trainCost = ConstructionCosts.TrainBuildCost(trainTypeIndex, wagonTypeIndex, clamped);
        if (GameEconomy.instance != null && !GameEconomy.instance.TrySpendCredits(trainCost))
        {
            ReportError($"Nedostatok kreditov – vlak stojí {trainCost} CR.");
            return false;
        }

        bool ok = TrainSys.CreateTrain(dx, dz, trainTypeIndex, wagonTypeIndex);
        if (ok)
        {
            Debug.Log($"[GameManager] Vlak vytvorený v depe [{dx},{dz}] s {clamped} vagónmi (cez UI).");

            // EVIDENCIA pre ročnú uzávierku – počet + reálne zaplatená cena vlaku.
            BudgetSystem.instance?.RecordTrainPurchase(trainCost);
        }
        else
        {
            // Vytvorenie sa nepodarilo (vlak už existuje / tile nie je Depot) –
            // vrátime odpočítanú sumu späť.
            if (GameEconomy.instance != null && trainCost > 0u)
                GameEconomy.instance.AddCredits(trainCost);
            Debug.LogWarning($"[GameManager] Vlak pre depo [{dx},{dz}] už existuje alebo tile nie je Depot.");
        }
        return ok;
    }

    /// <summary>
    /// Spätne kompatibilný preťažený podpis – vytvorí vlak s prvým typom
    /// vlaku aj vagónu (index 0). Pre volania bez voľby typu.
    /// </summary>
    public bool RequestCreateTrainForDepot(int dx, int dz, int wagonCount)
    {
        return RequestCreateTrainForDepot(dx, dz, wagonCount, 0, 0);
    }

    /// <summary>
    /// S-ekvivalent: spustenie vlaku v zadanom depe.
    /// </summary>
    public bool RequestStartTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;
        bool ok = TrainSys.StartTrain(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal spustiť (chýba vlak alebo stanice).");
        return ok;
    }

    /// <summary>
    /// P-ekvivalent: zastavenie vlaku.
    /// </summary>
    public bool RequestStopTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;
        bool ok = TrainSys.StopTrain(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal zastaviť.");
        return ok;
    }

    /// <summary>
    /// R-ekvivalent: návrat vlaku do depa.
    /// </summary>
    public TrainSystem.ReturnToDepotResult RequestSendToDepotForDepot(int dx, int dz)
    {
        if (TrainSys == null) return TrainSystem.ReturnToDepotResult.Error;
        var result = TrainSys.ReturnToDepot(dx, dz);
        if (result == TrainSystem.ReturnToDepotResult.StoppedAwaitingSecondR)
            Debug.Log($"[GameManager] Vlak z depa [{dx},{dz}] zastavil – žiadne dostupné stanice. Kliknite znovu Send To Depot pre priamy návrat.");
        else if (result == TrainSystem.ReturnToDepotResult.Error)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal vrátiť do depa (chyba).");
        return result;
    }

    /// <summary>
    /// T-ekvivalent: odstránenie vlaku (musí byť v depe a zastavený).
    /// </summary>
    public bool RequestRemoveTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;

        // EKONOMIKA: cenu súpravy (lokomotíva + všetky vagóny) spočítame PRED
        // odstránením, kým ešte existuje. Refund (50 %) pripočítame až po
        // úspešnom odstránení.
        int consistCost = 0;
        var td = TrainSys.GetTrain(dx, dz);
        if (td != null && td.consist != null)
        {
            consistCost = td.consist.Cost;
            foreach (var w in td.consist.Wagons)
                consistCost += w.Cost;
        }

        bool ok = TrainSys.RemoveTrain(dx, dz);
        if (ok)
        {
            if (GameEconomy.instance != null)
            {
                uint refund = ConstructionCosts.HalfRefund(consistCost);
                if (refund > 0u) GameEconomy.instance.AddCredits(refund);
            }
        }
        else
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] nemôže byť odstránený (nie je v depe alebo beží).");
        return ok;
    }

    /// <summary>
    /// Vymaže všetky definované stanice pre vlak v zadanom depe a tiež zruší
    /// prípadne prebiehajúci "Define Route" režim pre toto depo (aby stanice
    /// nepribúdali do už-vyprázdneného zoznamu).
    ///
    /// POZNÁMKA: Táto metóda NEZASTAVÍ vlak. Volajúci (UI) je zodpovedný za
    /// to, aby vlak pred volaním zastavil cez RequestStopTrainForDepot v
    /// prípade, že vlak beží – inak by sa pohybová logika počas frame
    /// dostala k prázdnemu zoznamu staníc s nedefinovaným indexom.
    ///
    /// Vracia true ak boli stanice úspešne vyčistené (vlak existuje),
    /// false ak vlak v depe neexistuje.
    /// </summary>
    public bool RequestClearStationsForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;

        var td = TrainSys.GetTrain(dx, dz);
        if (td == null)
        {
            Debug.LogWarning($"[GameManager] V depe [{dx},{dz}] nie je vlak – nie je čo mazať.");
            return false;
        }

        // Ak práve zbierame stanice pre toto depo, najprv ukončíme zbieranie
        // (aby ďalšie kliky na mapu už nepridávali do vyčisteného zoznamu).
        if (trainInputMode == TrainInputMode.AddStations
            && collectingStations
            && pendingDepotTile.HasValue
            && pendingDepotTile.Value.x == dx
            && pendingDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] Define Route režim ukončený kvôli Delete All Routes.");
        }

        td.stations.Clear();
        td.currentStationIndex = 0;
        Debug.Log($"[GameManager] Stanice pre vlak v depe [{dx},{dz}] vymazané.");
        return true;
    }

    /// <summary>
    /// W-ekvivalent: prepnutie do/zo režimu definovania trasy (klikania na stanice).
    /// Volá sa pri stlačení DCDefineRouteButton.
    /// Vracia true ak práve začalo "collecting stations", false ak ho ukončilo.
    /// </summary>
    public bool RequestToggleDefineRouteForDepot(int dx, int dz)
    {
        if (TrainSys == null || IndAPI == null) return false;

        // Ak práve zbierame stanice pre rovnaké depo → ukončenie
        if (trainInputMode == TrainInputMode.AddStations
            && collectingStations
            && pendingDepotTile.HasValue
            && pendingDepotTile.Value.x == dx
            && pendingDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            IndAPI.HideAllSnapVisuals();
            Debug.Log("[GameManager] Define Route: Zadávanie staníc UKONČENÉ.");
            return false;
        }

        // Začiatok – overíme že v depe je vlak
        var td = TrainSys.GetTrain(dx, dz);
        if (td == null)
        {
            Debug.LogWarning($"[GameManager] V depe [{dx},{dz}] nie je vlak. Najprv vytvorte vlak.");
            return false;
        }

        // Aktivujeme režim AddStations rovno s vybraným depom (nepotrebujeme klik na depot)
        CurrentRailConstructionMode = RailConstructionMode.None;
        IndAPI.HideAllSnapVisuals();
        trainInputMode = TrainInputMode.AddStations;
        pendingDepotTile = new Vector2Int(dx, dz);
        collectingStations = true;
        td.stations.Clear();
        Debug.Log($"[GameManager] Define Route: Depo [{dx},{dz}] vybrané. Klikajte na Stanice. Ukončite opätovným stlačením Define Route alebo ESC.");
        return true;
    }

    // =====================================================================
    // PUBLIC API PRE DepotRoadConstructionMenuUI
    //
    // ROAD ekvivalent vyššie uvedeného API. Každá metóda volá VehicleSystem
    // namiesto TrainSystem a operuje nad pendingRoadDepotTile / collectingRoadStations
    // namiesto pendingDepotTile / collectingStations. Logika je inak zhodná.
    // =====================================================================

    /// <summary>
    /// Q-ekvivalent: vytvorenie vozidla v zadanom ROAD depe.
    ///
    /// Parameter vehicleTypeName (napr. "Vehicle 1", "Vehicle 2") je názov
    /// typu z VehicleCatalog (VehicleStock.cs). VehicleSystem podľa neho
    /// vyhľadá VehicleSpec a vytvorí dátovú štruktúru vozidla.
    /// </summary>
    public bool RequestCreateVehicleForRoadDepot(int dx, int dz, string vehicleTypeName)
    {
        if (VehicleSys == null)
        {
            Debug.LogError("[GameManager] VehicleSystem.instance je null!");
            return false;
        }
        if (IndAPI == null) return false;

        var tileInfo = IndAPI.GetTileByIndexAny(dx, dz);
        if (tileInfo.tileID != 3 || tileInfo.category != IndicatrixAPI.TileCategory.Road)
        {
            Debug.LogWarning($"[GameManager] Tile [{dx},{dz}] nie je ROAD Depot.");
            return false;
        }

        // EKONOMIKA: cena vozidla = Cost vozidla. Odpočítame PRED vytvorením;
        // pri zlyhaní sumu vrátime.
        uint vehicleCost = ConstructionCosts.VehicleBuildCost(vehicleTypeName);
        if (GameEconomy.instance != null && !GameEconomy.instance.TrySpendCredits(vehicleCost))
        {
            ReportError($"Nedostatok kreditov – vozidlo stojí {vehicleCost} CR.");
            return false;
        }

        bool ok = VehicleSys.CreateVehicle(dx, dz, vehicleTypeName);
        if (ok)
        {
            Debug.Log($"[GameManager] Vozidlo '{vehicleTypeName}' vytvorené v ROAD depe [{dx},{dz}] (cez UI).");

            // EVIDENCIA pre ročnú uzávierku – počet + reálne zaplatená cena vozidla.
            BudgetSystem.instance?.RecordVehiclePurchase(vehicleCost);
        }
        else
        {
            if (GameEconomy.instance != null && vehicleCost > 0u)
                GameEconomy.instance.AddCredits(vehicleCost);
            Debug.LogWarning($"[GameManager] Vozidlo pre ROAD depo [{dx},{dz}] už existuje alebo tile nie je ROAD Depot.");
        }
        return ok;
    }

    /// <summary>
    /// S-ekvivalent: spustenie vozidla v zadanom ROAD depe.
    /// </summary>
    public bool RequestStartVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;
        bool ok = VehicleSys.StartVehicle(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo spustiť (chýba vozidlo alebo stanice).");
        return ok;
    }

    /// <summary>
    /// P-ekvivalent: zastavenie vozidla.
    /// </summary>
    public bool RequestStopVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;
        bool ok = VehicleSys.StopVehicle(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo zastaviť.");
        return ok;
    }

    /// <summary>
    /// R-ekvivalent: návrat vozidla do depa.
    /// </summary>
    public VehicleSystem.ReturnToDepotResult RequestSendVehicleToDepotForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return VehicleSystem.ReturnToDepotResult.Error;
        var result = VehicleSys.ReturnToDepot(dx, dz);
        if (result == VehicleSystem.ReturnToDepotResult.StoppedAwaitingSecondR)
            Debug.Log($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] zastavilo – žiadne dostupné stanice. Kliknite znovu Send To Depot pre priamy návrat.");
        else if (result == VehicleSystem.ReturnToDepotResult.Error)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo vrátiť do depa (chyba).");
        return result;
    }

    /// <summary>
    /// T-ekvivalent: odstránenie vozidla (musí byť v depe a zastavené).
    /// </summary>
    public bool RequestRemoveVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;

        // EKONOMIKA: cenu vozidla spočítame PRED odstránením, refund (50 %)
        // pripočítame po úspešnom odstránení.
        int vehicleCost = 0;
        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd != null && vd.vehicleInstance != null)
            vehicleCost = vd.vehicleInstance.Cost;

        bool ok = VehicleSys.RemoveVehicle(dx, dz);
        if (ok)
        {
            if (GameEconomy.instance != null)
            {
                uint refund = ConstructionCosts.HalfRefund(vehicleCost);
                if (refund > 0u) GameEconomy.instance.AddCredits(refund);
            }
        }
        else
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] nemôže byť odstránené (nie je v depe alebo beží).");
        return ok;
    }

    /// <summary>
    /// Vymaže všetky definované stanice pre vozidlo v zadanom ROAD depe a
    /// tiež zruší prípadne prebiehajúci "Define Route" režim pre toto depo
    /// (aby stanice nepribúdali do už-vyprázdneného zoznamu).
    ///
    /// POZNÁMKA: Táto metóda NEZASTAVÍ vozidlo. Volajúci (UI) je zodpovedný
    /// za to, aby vozidlo pred volaním zastavil cez RequestStopVehicleForRoadDepot
    /// v prípade, že vozidlo beží.
    /// </summary>
    public bool RequestClearStationsForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;

        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd == null)
        {
            Debug.LogWarning($"[GameManager] V ROAD depe [{dx},{dz}] nie je vozidlo – nie je čo mazať.");
            return false;
        }

        if (trainInputMode == TrainInputMode.AddStationsRoad
            && collectingRoadStations
            && pendingRoadDepotTile.HasValue
            && pendingRoadDepotTile.Value.x == dx
            && pendingRoadDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD Define Route režim ukončený kvôli Delete All Routes.");
        }

        vd.stations.Clear();
        vd.currentStationIndex = 0;
        Debug.Log($"[GameManager] Stanice pre vozidlo v ROAD depe [{dx},{dz}] vymazané.");
        return true;
    }

    /// <summary>
    /// W-ekvivalent: prepnutie do/zo režimu definovania trasy pre ROAD vozidlo.
    /// Volá sa pri stlačení DCDefineRouteButton v DepotRoadConstructionMenuUI.
    /// Vracia true ak práve začalo "collecting road stations", false ak ho ukončilo.
    /// </summary>
    public bool RequestToggleDefineRouteForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null || IndAPI == null) return false;

        // Ak práve zbierame stanice pre rovnaké depo → ukončenie
        if (trainInputMode == TrainInputMode.AddStationsRoad
            && collectingRoadStations
            && pendingRoadDepotTile.HasValue
            && pendingRoadDepotTile.Value.x == dx
            && pendingRoadDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD Define Route: Zadávanie staníc UKONČENÉ.");
            return false;
        }

        // Začiatok – overíme že v depe je vozidlo
        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd == null)
        {
            Debug.LogWarning($"[GameManager] V ROAD depe [{dx},{dz}] nie je vozidlo. Najprv vytvorte vozidlo.");
            return false;
        }

        // Aktivujeme režim AddStationsRoad rovno s vybraným depom
        CurrentRoadConstructionMode = RoadConstructionMode.None;
        IndAPI.HideAllSnapVisuals();
        trainInputMode = TrainInputMode.AddStationsRoad;
        pendingRoadDepotTile = new Vector2Int(dx, dz);
        collectingRoadStations = true;
        vd.stations.Clear();
        Debug.Log($"[GameManager] ROAD Define Route: Depo [{dx},{dz}] vybrané. Klikajte na ROAD Stanice. Ukončite opätovným stlačením Define Route alebo ESC.");
        return true;
    }
}