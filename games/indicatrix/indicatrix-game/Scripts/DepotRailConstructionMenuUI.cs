using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.TrainStock;

/// <summary>
/// DepotRailConstructionMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre konfiguráciu vlaku konkrétneho depa.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na DCCloseWindowButton.
///  1. Okno sa otvorí kliknutím na Depot tile (volá GameManager.OpenForDepot).
///     Mimo toho je okno skryté.
///  2. TrainTypeDropdown obsahuje 5 typov lokomotív (vizuál sa nemení, ide
///     len o uloženú voľbu).
///  3. WagonTypeDropdown obsahuje 10 typov vagónov (vizuál sa nemení, ide
///     len o uloženú voľbu).
///  4. Počet vagónov 1..10 sa volí Minus/Plus tlačidlami a aplikuje sa
///     pri vytvorení vlaku.
///  5. DCCreateTrainButton: vytvorí vlak v aktuálne otvorenom depe.
///  6. DCStartTrainButton : spustí vlak v aktuálne otvorenom depe.
///  7. DCStopTrainButton  : zastaví vlak v aktuálne otvorenom depe.
///  8. DCRemoveTrainButton: odstráni vlak v aktuálne otvorenom depe.
///  9. DCSendToDepotButton: pošle vlak do depa.
/// 10. DCDefineRouteButton: toggle režimu definovania staníc – klikať
///     potom priamo na tile mapu na stanice. Opätovné stlačenie ukončí.
/// 11. StatusTypeTextCaption: "NO TRAIN" ak vlak pre depo neexistuje
///     (ešte nebol vytvorený alebo bol odstránený cez DCRemoveTrainButton),
///     inak "Stopped" alebo "Running" podľa stavu vlaku depa.
/// 12. CurrentRouteTextCaption: počet staníc (Vector2Int) v zozname trasy.
/// 13. DepotNameTextCaption: zobrazuje názov depa v tvare "Depot (x,z)"
///     podľa pozície aktuálne otvoreného depa na tile mape.
/// 14. DCDeleteAllRoutesButton: najprv otvorí varovné okno
///     "GameWarningUIPanel - Window" (GameWarningMenuUI). Stanice sa vymažú
///     až po kliknutí na GWYesGameButton; GWNoGameButton / X nerobí nič.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class DepotRailConstructionMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [SerializeField] private GameObject depotRailConstructionMenuUIPanel;

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown trainTypeDropdown;
    [SerializeField] private TMP_Dropdown wagonTypeDropdown;

    [Header("Wagon Count")]
    [SerializeField] private TMP_Text wagonCountIterationText;   // zobrazuje aktuálny počet (1..10)
    [SerializeField] private Button dcWagonCountMinusButton;
    [SerializeField] private Button dcWagonCountPlusButton;

    [Header("Depot Name Caption")]
    [Tooltip("UI.Text DepotNameTextCaption – zobrazuje názov aktuálne otvoreného depa, napr. \"Depot (21,17)\".")]
    [SerializeField] private TMP_Text depotNameTextCaption;

    [Header("Status / Route Captions")]
    [SerializeField] private TMP_Text statusTypeTextCaption;
    [SerializeField] private TMP_Text currentRouteTextCaption;

    [Header("Action Buttons")]
    [SerializeField] private Button dcCreateTrainButton;
    [SerializeField] private Button dcStartTrainButton;
    [SerializeField] private Button dcRemoveTrainButton;
    [SerializeField] private Button dcDefineRouteButton;
    [SerializeField] private Button dcStopTrainButton;
    [SerializeField] private Button dcSendToDepotButton;
    [SerializeField] private Button dcDeleteAllRoutesButton;

    [Header("Define Route Button Label")]
    [Tooltip("TMP_Text label na DCDefineRouteButton. Ak nie je priradený, label sa nebude prepínať.")]
    [SerializeField] private TMP_Text dcDefineRouteButtonLabel;

    [Header("Defined Routes Dropdown")]
    [Tooltip("TMP_Dropdown DefinedRoutesDropdown – zobrazuje zoznam definovaných staníc pre vlak " +
             "v aktuálne otvorenom depe vo formáte \"Station N - (x,z)\". " +
             "Ak vlak nemá definované stanice, zobrazí sa jediná položka \"No Stations\".")]
    [SerializeField] private TMP_Dropdown definedRoutesDropdown;

    [Header("Delete All Routes – potvrdenie")]
    [Tooltip("Referencia na GameWarningMenuUI (okno 'GameWarningUIPanel - Window'). " +
             "Ak ostane prázdna, nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private GameWarningMenuUI gameWarningMenuUI;

    // Texty na DCDefineRouteButton podľa stavu
    private const string DEFINE_ROUTE_LABEL_IDLE = "Define Route";
    private const string DEFINE_ROUTE_LABEL_ACTIVE = "End Tracing";

    // Placeholder text v DefinedRoutesDropdown keď vlak nemá žiadne definované stanice
    private const string NO_STATIONS_LABEL = "No Stations";

    // Text v StatusTypeTextCaption keď vlak pre aktuálne otvorené depo
    // neexistuje – t.j. ešte nebol vytvorený alebo bol odstránený
    // tlačidlom DCRemoveTrainButton. Stavy "Running"/"Stopped" majú zmysel
    // len vtedy, keď vlak reálne existuje (TrainSystem.GetTrain != null).
    private const string NO_TRAIN_STATUS = "NO TRAIN";

    // Minimálny počet staníc, aby trasa bola "validná" a uložila sa pri End Tracing.
    // Pri menšom počte sa stanice vyčistia (dropdown ostane "No Stations").
    private const int MIN_STATIONS_FOR_VALID_ROUTE = 2;

    // Text otázky vo varovnom okne pri DCDeleteAllRoutesButton. {0},{1} = pozícia depa.
    private const string DELETE_ALL_ROUTES_WARNING_FORMAT =
        "Are you sure you want to delete all station connections ?";

    // Lazy referencia na univerzálne varovné okno GameWarningMenuUI – rovnaký
    // vzor ako StatusErrorMenuUI v GameManager. Okno je pri štarte skryté,
    // preto sa hľadá s FindObjectsInactive.Include.
    private GameWarningMenuUI GameWarningMenu
    {
        get
        {
            if (gameWarningMenuUI == null)
                gameWarningMenuUI = UnityEngine.Object.FindFirstObjectByType<GameWarningMenuUI>(FindObjectsInactive.Include);
            return gameWarningMenuUI;
        }
    }

    // =====================================================================
    // PER-DEPOT PERZISTENCIA (per Depot konfigurácia)
    //
    // Každé depo má vlastné: trainTypeIndex, wagonTypeIndex, wagonCount.
    // Ukladáme do slovníka kľúčovaného (x,z) depota. Keď hráč znovu otvorí
    // dané depo, načítajú sa jeho posledné nastavenia.
    // =====================================================================

    private class DepotConfig
    {
        public int trainTypeIndex = 0;
        public int wagonTypeIndex = 0;
        public int wagonCount = 5;
    }

    private readonly Dictionary<Vector2Int, DepotConfig> depotConfigs
        = new Dictionary<Vector2Int, DepotConfig>();

    // =====================================================================
    // AKTUÁLNY STAV
    // =====================================================================

    // Depo, pre ktoré je okno otvorené. null = okno zavreté.
    private Vector2Int? currentDepotTile = null;

    private const int MIN_WAGONS = 1;
    private const int MAX_WAGONS = 10;

    // =====================================================================
    // KONFIGURÁCIA OBSAHU DROPDOWNOV
    //
    // REVÍZIA: pôvodné hardcoded polia "Locomotive01".."Locomotive05" a
    // "Wagon01".."Wagon010" boli len placeholdery. Názvy v dropdownoch sú
    // teraz ťahané priamo z katalógov (TrainCatalog / WagonCatalog v
    // TrainStock.cs) – jeden zdroj pravdy. Index zvolený v dropdowne
    // 1:1 zodpovedá indexu v katalógu, takže ho stačí poslať do CreateTrain.
    //
    // Zatiaľ teda TrainTypeDropdown obsahuje 1 položku ("Train 1") a
    // WagonTypeDropdown 2 položky ("Coal Truck", "Wood Truck"). Pridanie
    // ďalších typov = doplniť ich do katalógu v TrainStock.cs, UI sa
    // prispôsobí automaticky.
    // =====================================================================

    private static string[] TRAIN_TYPE_OPTIONS => TrainCatalog.Names();

    private static string[] WAGON_TYPE_OPTIONS => WagonCatalog.Names();

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod: na rovnakom GameObjecte (root paneli) je pripojený aj
        // sprievodný script DepotRailConstructionUIwindow, ktorý si v svojom
        // Awake() registruje listener na DCCloseWindowButton.onClick a
        // implementuje IDragHandler pre ťahanie okna. Poradie volania Awake()
        // medzi viacerými skriptami na tom istom GameObjecte je v Unity
        // nedeterministické (riadené Script Execution Order). Ak by tento
        // Awake() zbehol PRED Awake() v DepotRailConstructionUIwindow a hneď
        // by panel deaktivoval, druhý script by svoj Awake() už nemusel
        // stihnúť spustiť – Unity nevolá Awake() na neaktívnych GameObjectoch.
        // Výsledkom by bolo, že Close tlačidlo ani drag okna nefungujú.
        //
        // Riešenie: deaktivujeme panel v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania na všetkých skriptoch v scéne.
    }

    void Start()
    {
        // Naplníme dropdowny implicitnými hodnotami
        PopulateDropdown(trainTypeDropdown, TRAIN_TYPE_OPTIONS);
        PopulateDropdown(wagonTypeDropdown, WAGON_TYPE_OPTIONS);

        // Naviazanie eventov tlačidiel

        if (dcWagonCountMinusButton != null)
            dcWagonCountMinusButton.onClick.AddListener(OnWagonCountMinusClick);
        if (dcWagonCountPlusButton != null)
            dcWagonCountPlusButton.onClick.AddListener(OnWagonCountPlusClick);

        if (dcCreateTrainButton != null)
            dcCreateTrainButton.onClick.AddListener(OnCreateTrainClick);
        if (dcStartTrainButton != null)
            dcStartTrainButton.onClick.AddListener(OnStartTrainClick);
        if (dcRemoveTrainButton != null)
            dcRemoveTrainButton.onClick.AddListener(OnRemoveTrainClick);
        if (dcDefineRouteButton != null)
            dcDefineRouteButton.onClick.AddListener(OnDefineRouteClick);
        if (dcStopTrainButton != null)
            dcStopTrainButton.onClick.AddListener(OnStopTrainClick);
        if (dcSendToDepotButton != null)
            dcSendToDepotButton.onClick.AddListener(OnSendToDepotClick);
        if (dcDeleteAllRoutesButton != null)
            dcDeleteAllRoutesButton.onClick.AddListener(OnDeleteAllRoutesClick);

        // Dropdowny – uložiť výber per depot
        if (trainTypeDropdown != null)
            trainTypeDropdown.onValueChanged.AddListener(OnTrainTypeDropdownChanged);
        if (wagonTypeDropdown != null)
            wagonTypeDropdown.onValueChanged.AddListener(OnWagonTypeDropdownChanged);

        // Okno skryjeme až teraz – po Start() máme istotu, že všetky Awake()
        // na sprievodných skriptoch (DepotRailConstructionUIwindow –
        // close button listener + IDragHandler) už zbehli. Okno sa otvorí
        // až po kliku na Depot tile cez OpenForDepot().
        if (depotRailConstructionMenuUIPanel != null)
            depotRailConstructionMenuUIPanel.SetActive(false);
    }

    void Update()
    {
        // Pokiaľ je okno otvorené, periodicky obnovujeme Status a Current Route
        // (vlak môže meniť stav nezávisle – dorazí do stanice, návrat do depa, atď.)
        // a tiež label tlačidla Define Route (môže byť zrušený zvonku cez ESC).
        if (currentDepotTile.HasValue
            && depotRailConstructionMenuUIPanel != null
            && depotRailConstructionMenuUIPanel.activeSelf)
        {
            RefreshStatusAndRoute();
            UpdateDefineRouteButtonLabel();
            RefreshButtonStates();
        }
    }

    // =====================================================================
    // PUBLIC API – volá GameManager pri kliknutí na Depot tile
    // =====================================================================

    /// <summary>
    /// Otvorí okno pre dané depo (x, z). Načíta uloženú konfiguráciu
    /// pre toto konkrétne depo (alebo vytvorí novú s defaultmi).
    /// </summary>
    public void OpenForDepot(int dx, int dz)
    {
        Vector2Int tile = new Vector2Int(dx, dz);

        // Načítaj alebo vytvor konfiguráciu pre dané depo
        if (!depotConfigs.TryGetValue(tile, out DepotConfig cfg))
        {
            cfg = new DepotConfig();
            depotConfigs[tile] = cfg;
        }

        // Ak je otvorené varovanie "Delete All Routes" pre PREDCHÁDZAJÚCE depo,
        // zrušíme ho (ako No) – okno sa práve prepína na iné depo.
        if (gameWarningMenuUI != null)
            gameWarningMenuUI.CloseIfOwnedBy(WarningOwner);

        currentDepotTile = tile;

        // Aplikuj uloženú konfiguráciu do UI – BEZ vyvolania onValueChanged
        if (trainTypeDropdown != null)
            trainTypeDropdown.SetValueWithoutNotify(Mathf.Clamp(cfg.trainTypeIndex, 0, TRAIN_TYPE_OPTIONS.Length - 1));
        if (wagonTypeDropdown != null)
            wagonTypeDropdown.SetValueWithoutNotify(Mathf.Clamp(cfg.wagonTypeIndex, 0, WAGON_TYPE_OPTIONS.Length - 1));

        UpdateWagonCountText(cfg.wagonCount);

        // Zobraz panel
        if (depotRailConstructionMenuUIPanel != null)
            depotRailConstructionMenuUIPanel.SetActive(true);

        // Nastav názov depa do DepotNameTextCaption: "Depot (x,z)"
        UpdateDepotNameCaption(dx, dz);

        // Aktualizuj status, route, label tlačidla Define Route
        // a zoznam definovaných staníc v DefinedRoutesDropdown.
        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();

        Debug.Log($"[DepotConstructionMenuUI] Otvorené pre depo [{dx},{dz}].");
    }

    /// <summary>
    /// Skryje okno (volá DCCloseWindowButton, ale aj zvonku napr. pri prepnutí
    /// do iného režimu, ak by to bolo potrebné).
    /// </summary>
    public void CloseWindow()
    {
        if (depotRailConstructionMenuUIPanel != null)
            depotRailConstructionMenuUIPanel.SetActive(false);
        currentDepotTile = null;
    }

    /// <summary>
    /// Volá GameManager keď bola pridaná stanica v "Define Route" režime.
    /// UI prepočíta CurrentRouteTextCaption.
    /// </summary>
    public void OnStationAdded(int dx, int dz)
    {
        if (currentDepotTile.HasValue
            && currentDepotTile.Value.x == dx
            && currentDepotTile.Value.y == dz)
        {
            RefreshStatusAndRoute();
        }
    }

    /// <summary>
    /// Volá GameManager pri zrušení Define Route režimu (napr. ESC).
    ///
    /// Aplikujeme rovnakú validáciu ako pri stlačení "End Tracing" (v
    /// OnDefineRouteClick): ak vlak nemá aspoň MIN_STATIONS_FOR_VALID_ROUTE
    /// staníc, trasa sa zahodí (stanice sa vyčistia). Tým udržíme stav UI
    /// konzistentný – DefinedRoutesDropdown sa nikdy nezobrazí s neplatnou
    /// 0- alebo 1-stanicou trasou.
    /// </summary>
    public void OnRouteDefineCancelled()
    {
        // Validácia: ak po zrušení režimu ostalo menej staníc ako minimum, vyčisti.
        if (currentDepotTile.HasValue
            && GameManager.instance != null
            && TrainSystem.instance != null)
        {
            var t = currentDepotTile.Value;
            var td = TrainSystem.instance.GetTrain(t.x, t.y);
            if (td != null
                && td.stations.Count > 0
                && td.stations.Count < MIN_STATIONS_FOR_VALID_ROUTE)
            {
                Debug.LogWarning($"[DepotConstructionMenuUI] Define Route zrušené (ESC) – trasa zahodená, počet staníc {td.stations.Count} < {MIN_STATIONS_FOR_VALID_ROUTE}.");
                GameManager.instance.RequestClearStationsForDepot(t.x, t.y);
            }
        }

        // Aktualizujeme stav, ak je okno otvorené
        if (currentDepotTile.HasValue
            && depotRailConstructionMenuUIPanel != null
            && depotRailConstructionMenuUIPanel.activeSelf)
        {
            RefreshStatusAndRoute();
            RefreshDefinedRoutesDropdown();
        }
        // Label sa môže zmeniť aj keď okno nie je otvorené (užívateľ ho otvorí
        // neskôr a očakáva "Define Route") – preto aktualizujeme vždy.
        UpdateDefineRouteButtonLabel();
        RefreshButtonStates();
    }

    // =====================================================================
    // BUTTON HANDLERS
    // =====================================================================

    private void OnCloseWindowClick()
    {
        CloseWindow();
    }

    private void OnWagonCountMinusClick()
    {
        if (!currentDepotTile.HasValue) return;
        var cfg = depotConfigs[currentDepotTile.Value];
        cfg.wagonCount = Mathf.Max(MIN_WAGONS, cfg.wagonCount - 1);
        UpdateWagonCountText(cfg.wagonCount);
    }

    private void OnWagonCountPlusClick()
    {
        if (!currentDepotTile.HasValue) return;
        var cfg = depotConfigs[currentDepotTile.Value];
        cfg.wagonCount = Mathf.Min(MAX_WAGONS, cfg.wagonCount + 1);
        UpdateWagonCountText(cfg.wagonCount);
    }

    private void OnCreateTrainClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        var cfg = depotConfigs[t];
        // trainTypeIndex / wagonTypeIndex zodpovedajú indexom v katalógoch
        // (TrainCatalog / WagonCatalog) – posúvajú sa do CreateTrain, kde sa
        // podľa nich zostaví dátová štruktúra súpravy.
        GameManager.instance.RequestCreateTrainForDepot(
            t.x, t.y, cfg.wagonCount, cfg.trainTypeIndex, cfg.wagonTypeIndex);
        RefreshStatusAndRoute();
        RefreshButtonStates();
    }

    private void OnStartTrainClick()
    {
        GameManager.instance?.PlaySfxTrainStart();

        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestStartTrainForDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnStopTrainClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestStopTrainForDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnRemoveTrainClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestRemoveTrainForDepot(t.x, t.y);
        RefreshStatusAndRoute();
        RefreshButtonStates();
    }

    private void OnSendToDepotClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestSendToDepotForDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnDefineRouteClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;

        // Pred prepnutím režimu zistíme, či sme práve teraz vo fáze "End Tracing"
        // (= zbierame stanice). Ak áno, po toggle sa zbieranie skončí a treba
        // zvalidovať počet staníc a naplniť DefinedRoutesDropdown.
        bool wasCollecting = GameManager.instance.IsCollectingStationsForDepot(t.x, t.y);

        // Toggle režim
        GameManager.instance.RequestToggleDefineRouteForDepot(t.x, t.y);

        // Ak sme práve ukončili zbieranie staníc (klik na "End Tracing"),
        // validujeme: minimálne 2 stanice. Ak menej → vyčisti (zostane
        // "No Stations" placeholder, hráč musí začať odznova).
        if (wasCollecting && TrainSystem.instance != null)
        {
            var td = TrainSystem.instance.GetTrain(t.x, t.y);
            if (td != null && td.stations.Count < MIN_STATIONS_FOR_VALID_ROUTE)
            {
                Debug.LogWarning($"[DepotConstructionMenuUI] Trasa zahodená – počet staníc {td.stations.Count} < {MIN_STATIONS_FOR_VALID_ROUTE}.");
                GameManager.instance.RequestClearStationsForDepot(t.x, t.y);
            }
        }

        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();
    }

    /// <summary>
    /// DCDeleteAllRoutesButton handler.
    ///
    /// Stanice sa NEMAŽÚ hneď – najprv sa zobrazí varovné okno
    /// "GameWarningUIPanel - Window" (GameWarningMenuUI):
    ///   • GWNoGameButton / X  → okno sa zavrie, nestane sa nič.
    ///   • GWYesGameButton     → zavolá sa DeleteAllRoutesForDepot(...).
    ///
    /// Pozícia depa sa zachytí V MOMENTE KLIKU, takže sa vymažú stanice
    /// presne toho depa, o ktorom je otázka. Ak hráč medzitým zatvorí okno
    /// depa alebo prepne na iné depo, varovanie sa samo zruší (ako No).
    ///
    /// Ak GameWarningMenuUI v scéne nie je, stanice sa vymažú bez potvrdenia
    /// (pôvodné správanie) a do konzoly sa vypíše varovanie.
    /// </summary>
    private void OnDeleteAllRoutesClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        Vector2Int t = currentDepotTile.Value;

        GameWarningMenuUI warning = GameWarningMenu;
        if (warning == null)
        {
            Debug.LogWarning("[DepotConstructionMenuUI] GameWarningMenuUI nie je v scéne – " +
                             "stanice sa mažú bez potvrdenia.");
            DeleteAllRoutesForDepot(t);
            return;
        }

        warning.Open(
            string.Format(DELETE_ALL_ROUTES_WARNING_FORMAT, t.x, t.y),
            () => DeleteAllRoutesForDepot(t),
            WarningOwner);
    }

    /// <summary>
    /// Samotné vymazanie staníc – volá sa po potvrdení cez GWYesGameButton.
    /// (Pôvodná logika OnDeleteAllRoutesClick, len s pozíciou depa ako parametrom.)
    ///
    /// SPRÁVANIE PODĽA STAVU VLAKU:
    ///
    /// • Vlak v stave "Stopped" (vrátane prípadu, keď vlak ešte neexistuje
    ///   alebo nemá žiadne stanice):
    ///   PRED definovaním, POČAS definovania alebo PO definovaní staníc
    ///   sa vždy vymažú všetky definované stanice pre tento vlak. Ak práve
    ///   prebieha "Define Route" režim, ten sa zároveň ukončí
    ///   (zariadi RequestClearStationsForDepot v GameManager).
    ///   DefinedRoutesDropdown ostane s placeholder hodnotou "No Stations".
    ///
    /// • Vlak v stave "Running":
    ///   1) Vlak okamžite zastavíme (ekvivalent kliku na DCStopTrainButton).
    ///   2) Až potom vymažeme stanice.
    ///   Hráč má následne dve možnosti (NIE sú vynucované týmto handlerom –
    ///   ide o prirodzený dôsledok stavu po zastavení):
    ///     a) Preroutuje cez DCDefineRouteButton a spustí cez DCStartTrainButton.
    ///     b) Pošle vlak do depa cez DCSendToDepotButton (funguje aj bez
    ///        definovaných staníc – nájde najbližšiu stanicu alebo ide priamo).
    /// </summary>
    private void DeleteAllRoutesForDepot(Vector2Int t)
    {
        if (GameManager.instance == null) return;

        // Zisti aktuálny stav vlaku – ak beží, najprv ho zastavíme.
        if (TrainSystem.instance != null)
        {
            var td = TrainSystem.instance.GetTrain(t.x, t.y);
            if (td != null && td.isRunning)
            {
                Debug.Log($"[DepotConstructionMenuUI] Vlak v depe [{t.x},{t.y}] beží – pred mazaním staníc ho zastavujem.");
                GameManager.instance.RequestStopTrainForDepot(t.x, t.y);
            }
        }

        // Vymaž stanice (a zruš prípadný Define Route režim pre toto depo).
        GameManager.instance.RequestClearStationsForDepot(t.x, t.y);

        // Aktualizuj UI
        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();
    }

    /// <summary>
    /// Okno, z ktorého sa varovanie vyvoláva. Keď sa skryje, varovanie sa zruší.
    /// </summary>
    private GameObject WarningOwner =>
        depotRailConstructionMenuUIPanel != null ? depotRailConstructionMenuUIPanel : gameObject;

    private void OnTrainTypeDropdownChanged(int newIndex)
    {
        if (!currentDepotTile.HasValue) return;
        depotConfigs[currentDepotTile.Value].trainTypeIndex = newIndex;
        // Per zadanie: stále sa jedná o ten istý kváder v hre pre lokomotívu.
        // Voľba sa len uloží – žiadna vizuálna zmena lokomotívy v scéne.
    }

    private void OnWagonTypeDropdownChanged(int newIndex)
    {
        if (!currentDepotTile.HasValue) return;
        depotConfigs[currentDepotTile.Value].wagonTypeIndex = newIndex;
        // Per zadanie: stále sa jedná o ten istý kváder v hre pre vagón.
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private void PopulateDropdown(TMP_Dropdown dd, string[] options)
    {
        if (dd == null) return;
        dd.ClearOptions();
        var list = new List<string>(options);
        dd.AddOptions(list);
        dd.RefreshShownValue();
    }

    private void UpdateWagonCountText(int value)
    {
        if (wagonCountIterationText != null)
            wagonCountIterationText.text = value.ToString();
    }

    /// <summary>
    /// Nastaví text DepotNameTextCaption vo formáte "Depot (x,z)" podľa pozície
    /// aktuálne otvoreného depa na tile mape.
    /// </summary>
    private void UpdateDepotNameCaption(int dx, int dz)
    {
        if (depotNameTextCaption == null) return;
        depotNameTextCaption.text = $"Depot ({dx},{dz})";
    }

    /// <summary>
    /// Aktualizuje text na DCDefineRouteButton podľa toho, či práve prebieha
    /// zbieranie staníc pre aktuálne otvorené depo. Idempotentné – text sa
    /// prepíše len ak je iný (aby sme zbytočne nedirty-li layout).
    /// </summary>
    private void UpdateDefineRouteButtonLabel()
    {
        if (dcDefineRouteButtonLabel == null) return;

        bool active = false;
        if (currentDepotTile.HasValue && GameManager.instance != null)
        {
            var t = currentDepotTile.Value;
            active = GameManager.instance.IsCollectingStationsForDepot(t.x, t.y);
        }

        string target = active ? DEFINE_ROUTE_LABEL_ACTIVE : DEFINE_ROUTE_LABEL_IDLE;
        if (dcDefineRouteButtonLabel.text != target)
            dcDefineRouteButtonLabel.text = target;
    }

    /// <summary>
    /// Aktualizuje StatusTypeTextCaption a CurrentRouteTextCaption pre vlak
    /// aktuálne otvoreného depa.
    ///
    /// STATUS:
    ///   • Vlak neexistuje (nie je otvorené depo, TrainSystem chýba, alebo
    ///     vlak ešte nebol vytvorený / bol odstránený cez DCRemoveTrainButton):
    ///     StatusTypeTextCaption = "NO TRAIN".
    ///   • Vlak existuje: "Running" ak beží, inak "Stopped".
    /// </summary>
    private void RefreshStatusAndRoute()
    {
        if (!currentDepotTile.HasValue)
        {
            // Okno nie je viazané na žiadne depo → vlak nemôže existovať.
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = NO_TRAIN_STATUS;
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        var t = currentDepotTile.Value;

        if (TrainSystem.instance == null)
        {
            // Bez TrainSystem nevieme overiť existenciu vlaku → správame sa,
            // akoby vlak neexistoval.
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = NO_TRAIN_STATUS;
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        var td = TrainSystem.instance.GetTrain(t.x, t.y);
        if (td == null)
        {
            // Vlak pre toto depo neexistuje – ešte nebol vytvorený alebo
            // bol odstránený tlačidlom DCRemoveTrainButton.
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = NO_TRAIN_STATUS;
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        // Vlak existuje – Status: Running ak beží, inak Stopped.
        if (statusTypeTextCaption != null)
            statusTypeTextCaption.text = td.isRunning ? "Running" : "Stopped";

        // Počet staníc trasy
        if (currentRouteTextCaption != null)
            currentRouteTextCaption.text = td.stations.Count.ToString();
    }

    /// <summary>
    /// Naplní DefinedRoutesDropdown podľa td.stations pre vlak aktuálne
    /// otvoreného depa.
    ///
    /// FORMÁT POLOŽIEK:
    ///   • Ak vlak nemá žiadne stanice (alebo vlak/depo nie je vybraté):
    ///     dropdown obsahuje jednu položku "No Stations".
    ///   • Ak vlak má stanice: dropdown obsahuje N položiek vo formáte
    ///     "Station 1 - (x,z)", "Station 2 - (x,z)", ... (1-based indexovanie).
    ///
    /// Položky sú len informatívne (read-only zobrazenie aktuálnej trasy);
    /// zmena vybraného indexu nevyvoláva žiadnu akciu na pozadí.
    /// Použijeme SetValueWithoutNotify, aby sa nespustil prípadný listener.
    /// </summary>
    private void RefreshDefinedRoutesDropdown()
    {
        if (definedRoutesDropdown == null) return;

        var options = new List<string>();

        // Predvolený stav: nemáme otvorené depo alebo vlak ešte neexistuje.
        List<Vector2Int> stations = null;
        if (currentDepotTile.HasValue && TrainSystem.instance != null)
        {
            var t = currentDepotTile.Value;
            var td = TrainSystem.instance.GetTrain(t.x, t.y);
            if (td != null) stations = td.stations;
        }

        if (stations == null || stations.Count == 0)
        {
            options.Add(NO_STATIONS_LABEL);
        }
        else
        {
            for (int i = 0; i < stations.Count; i++)
            {
                Vector2Int s = stations[i];
                // Menný názov stanice podľa mesta (rovnaký ako na mape/labeli).
                // Predčíslovanie zachováva poradie trasy.
                string name = (CityManager.instance != null)
                    ? CityManager.instance.GetStationDisplayName(s.x, s.y)
                    : $"Station [{s.x}, {s.y}]";
                options.Add($"{i + 1}. {name}");
            }
        }

        definedRoutesDropdown.ClearOptions();
        definedRoutesDropdown.AddOptions(options);
        // Reset výberu na prvú položku bez vyvolania onValueChanged.
        definedRoutesDropdown.SetValueWithoutNotify(0);
        definedRoutesDropdown.RefreshShownValue();
    }

    // =====================================================================
    // ENABLE / DISABLE TLAČIDIEL PODĽA STAVU DEPA
    //
    // Stav okna je plne odvoditeľný z dvoch vecí pre aktuálne otvorené depo:
    //   • či pre depo EXISTUJE vlak (TrainSystem.GetTrain != null) – 1 depo
    //     = max. 1 vlak,
    //   • či je pre depo DEFINOVANÁ trasa (td.stations.Count > 0) a či práve
    //     prebieha "Define Route" zbieranie staníc (IsCollectingStationsForDepot).
    //
    // Z toho plynú tri skupiny ovládacích prvkov:
    //
    //   A) "Creation" prvky – povolené IBA keď vlak NEEXISTUJE:
    //        DCCreateTrainButton, TrainTypeDropdown, WagonTypeDropdown,
    //        DCWagonCountMinusButton, DCWagonCountPlusButton.
    //
    //   B) "Prevádzkové" prvky – povolené IBA keď vlak EXISTUJE:
    //        DCStartTrainButton, DCRemoveTrainButton, DCStopTrainButton,
    //        DCSendToDepotButton, DefinedRoutesDropdown.
    //
    //   C) "Routovacie" prvky – vzájomne sa vylučujúca dvojica (platí len keď
    //      vlak existuje):
    //        DCDefineRouteButton    : povolené keď práve traceujeme (tlačidlo
    //                                 slúži ako "End Tracing") ALEBO keď trasa
    //                                 ešte NIE JE definovaná.
    //        DCDeleteAllRoutesButton: povolené keď NEtraceujeme a trasa JE
    //                                 definovaná.
    //
    // Volá sa pri otvorení depa, po každom relevantnom kliku a priebežne v
    // Update() (vlak môže meniť stav aj sám – dorazí do depa, atď.).
    // </summary>
    private void RefreshButtonStates()
    {
        bool trainExists = false;
        bool hasRoute = false;
        bool isTracing = false;

        if (currentDepotTile.HasValue)
        {
            var t = currentDepotTile.Value;

            if (TrainSystem.instance != null)
            {
                var td = TrainSystem.instance.GetTrain(t.x, t.y);
                if (td != null)
                {
                    trainExists = true;
                    hasRoute = td.stations != null && td.stations.Count > 0;
                }
            }

            if (GameManager.instance != null)
                isTracing = GameManager.instance.IsCollectingStationsForDepot(t.x, t.y);
        }

        // A) Creation prvky – povolené len keď vlak NEEXISTUJE.
        SetInteractable(dcCreateTrainButton, !trainExists);
        SetInteractable(trainTypeDropdown, !trainExists);
        SetInteractable(wagonTypeDropdown, !trainExists);
        SetInteractable(dcWagonCountMinusButton, !trainExists);
        SetInteractable(dcWagonCountPlusButton, !trainExists);

        // B) Prevádzkové prvky – povolené len keď vlak EXISTUJE.
        SetInteractable(dcStartTrainButton, trainExists);
        SetInteractable(dcRemoveTrainButton, trainExists);
        SetInteractable(dcStopTrainButton, trainExists);
        SetInteractable(dcSendToDepotButton, trainExists);
        SetInteractable(definedRoutesDropdown, trainExists);

        // C) Define Route vs Delete All Routes (len pri existujúcom vlaku).
        SetInteractable(dcDefineRouteButton, trainExists && (isTracing || !hasRoute));
        SetInteractable(dcDeleteAllRoutesButton, trainExists && !isTracing && hasRoute);
    }

    /// <summary>
    /// Pomocník: bezpečne nastaví interactable na ľubovoľnom Selectable
    /// (Button aj TMP_Dropdown z neho dedia), ošetrí null referenciu.
    /// </summary>
    private static void SetInteractable(Selectable selectable, bool value)
    {
        if (selectable != null)
            selectable.interactable = value;
    }
}