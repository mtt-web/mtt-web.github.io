using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// DepotRoadConstructionMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre konfiguráciu vozidla konkrétneho ROAD depa.
///
/// Analógia k DepotRailConstructionMenuUI s týmito rozdielmi (per zadanie):
///   • ŽIADNY Wagon Type dropdown – cestné vozidlo je 1 kváder bez vagónov.
///   • ŽIADNE Wagon Count tlačidlá (Plus/Minus + iteration text).
///   • Vehicle Type dropdown obsahuje typy vozidiel z VehicleCatalog
///     (VehicleStock.cs) – zatiaľ "Vehicle 1" a "Vehicle 2". Po kliknutí
///     na DCCreateVehicleButton sa pre vybraný typ vytvoria dátové
///     štruktúry vozidla (VehicleInstance s atribútmi Cost, Speed...).
///   • Voláme VehicleSystem (nie TrainSystem) a GameManager Request*ForRoadDepot
///     (nie Request*ForDepot, ktoré sú rail-špecifické).
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na DCCloseWindowButton.
///  1. Okno sa otvorí kliknutím na ROAD Depot tile (GameManager.OpenForRoadDepot).
///     Mimo toho je okno skryté.
///  2. VehicleTypeDropdown obsahuje typy vozidiel z VehicleCatalog.
///  3. DCCreateVehicleButton: vytvorí vozidlo v aktuálne otvorenom depe.
///  4. DCStartVehicleButton: spustí vozidlo v aktuálne otvorenom depe.
///  5. DCStopVehicleButton: zastaví vozidlo v aktuálne otvorenom depe.
///  6. DCRemoveVehicleButton: odstráni vozidlo v aktuálne otvorenom depe.
///  7. DCSendToDepotButton: pošle vozidlo do depa.
///  8. DCDefineRouteButton: toggle režimu definovania staníc – klikať
///     potom priamo na tile mapu na ROAD stanice (autobus./nákl. zastávky).
///     Opätovné stlačenie ukončí.
///  9. DCDeleteAllRoutesButton: najprv otvorí varovné okno
///     "GameWarningUIPanel - Window" (GameWarningMenuUI). Stanice sa vymažú
///     až po kliknutí na GWYesGameButton; GWNoGameButton / X nerobí nič.
/// 10. StatusTypeTextCaption: "NO VEHICLE" (vozidlo neexistuje – ešte
///     nevytvorené alebo zmazané), inak "Stopped" / "Running".
/// 11. CurrentRouteTextCaption: počet staníc (Vector2Int) v zozname trasy.
/// 12. DepotNameTextCaption: zobrazuje názov depa v tvare "Depot (x,z)".
/// 13. DefinedRoutesDropdown: zobrazuje aktuálne stanice trasy.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class DepotRoadConstructionMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [SerializeField] private GameObject depotRoadConstructionMenuUIPanel;

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown vehicleTypeDropdown;

    [Header("Depot Name Caption")]
    [Tooltip("UI.Text DepotNameTextCaption – zobrazuje názov aktuálne otvoreného depa, napr. \"Depot (21,17)\".")]
    [SerializeField] private TMP_Text depotNameTextCaption;

    [Header("Status / Route Captions")]
    [SerializeField] private TMP_Text statusTypeTextCaption;
    [SerializeField] private TMP_Text currentRouteTextCaption;

    [Header("Action Buttons")]
    [SerializeField] private Button dcCreateVehicleButton;
    [SerializeField] private Button dcStartVehicleButton;
    [SerializeField] private Button dcRemoveVehicleButton;
    [SerializeField] private Button dcDefineRouteButton;
    [SerializeField] private Button dcStopVehicleButton;
    [SerializeField] private Button dcSendToDepotButton;
    [SerializeField] private Button dcDeleteAllRoutesButton;

    [Header("Define Route Button Label")]
    [Tooltip("TMP_Text label na DCDefineRouteButton. Ak nie je priradený, label sa nebude prepínať.")]
    [SerializeField] private TMP_Text dcDefineRouteButtonLabel;

    [Header("Defined Routes Dropdown")]
    [Tooltip("TMP_Dropdown DefinedRoutesDropdown – zobrazuje zoznam definovaných staníc pre vozidlo " +
             "v aktuálne otvorenom depe vo formáte \"Station N - (x,z)\". " +
             "Ak vozidlo nemá definované stanice, zobrazí sa jediná položka \"No Stations\".")]
    [SerializeField] private TMP_Dropdown definedRoutesDropdown;

    [Header("Delete All Routes – potvrdenie")]
    [Tooltip("Referencia na GameWarningMenuUI (okno 'GameWarningUIPanel - Window'). " +
             "Ak ostane prázdna, nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private GameWarningMenuUI gameWarningMenuUI;

    // Texty na DCDefineRouteButton podľa stavu
    private const string DEFINE_ROUTE_LABEL_IDLE = "Define Route";
    private const string DEFINE_ROUTE_LABEL_ACTIVE = "End Tracing";

    // Placeholder text v DefinedRoutesDropdown
    private const string NO_STATIONS_LABEL = "No Stations";

    // Text pre StatusTypeTextCaption, keď vozidlo v depe neexistuje –
    // buď ešte nebolo vytvorené (NotCreated), alebo bolo zmazané cez
    // DCRemoveVehicleButton (Removed).
    private const string NO_VEHICLE_LABEL = "NO VEHICLE";

    // Minimálny počet staníc pre platnú trasu (rovnako ako rail).
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
    // PER-DEPOT PERZISTENCIA
    //
    // Pre cestné vozidlo si pamätáme IBA vehicleTypeIndex (počet vagónov
    // ani wagonTypeIndex tu neexistujú).
    // =====================================================================

    private class DepotConfig
    {
        public int vehicleTypeIndex = 0;
    }

    private readonly Dictionary<Vector2Int, DepotConfig> depotConfigs
        = new Dictionary<Vector2Int, DepotConfig>();

    // =====================================================================
    // AKTUÁLNY STAV
    // =====================================================================

    private Vector2Int? currentDepotTile = null;

    // =====================================================================
    // KONFIGURÁCIA OBSAHU DROPDOWNU
    //
    // Zdrojom názvov je teraz VehicleCatalog z VehicleStock.cs – jediné
    // miesto, kde sú typy vozidiel definované. Tým je VehicleTypeDropdown
    // vždy konzistentný s dátovými štruktúrami, ktoré vytvorí CreateVehicle.
    //
    // Zatiaľ sú v katalógu dva typy: "Vehicle 1" a "Vehicle 2".
    // =====================================================================

    private static readonly string[] VEHICLE_TYPE_OPTIONS
        = Game.VehicleStock.VehicleCatalog.Names();

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod identický s DepotRailConstructionMenuUI: na rovnakom
        // GameObjecte (root paneli) je pripojený aj sprievodný script
        // DepotRoadConstructionUIwindow, ktorý si v svojom Awake() registruje
        // listener na DCCloseWindowButton.onClick. Poradie volania Awake()
        // medzi viacerými skriptami na tom istom GameObjecte je v Unity
        // nedeterministické. Ak by tento Awake() zbehol PRED Awake() v
        // DepotRoadConstructionUIwindow a hneď by panel deaktivoval, druhý
        // script by svoj Awake() už nemusel stihnúť spustiť – Unity nevolá
        // Awake() na neaktívnych GameObjectoch.
        //
        // Riešenie: deaktivujeme panel v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania.
    }

    void Start()
    {
        // Naplníme dropdown implicitnými hodnotami
        PopulateDropdown(vehicleTypeDropdown, VEHICLE_TYPE_OPTIONS);

        // Naviazanie eventov tlačidiel
        if (dcCreateVehicleButton != null)
            dcCreateVehicleButton.onClick.AddListener(OnCreateVehicleClick);
        if (dcStartVehicleButton != null)
            dcStartVehicleButton.onClick.AddListener(OnStartVehicleClick);
        if (dcRemoveVehicleButton != null)
            dcRemoveVehicleButton.onClick.AddListener(OnRemoveVehicleClick);
        if (dcDefineRouteButton != null)
            dcDefineRouteButton.onClick.AddListener(OnDefineRouteClick);
        if (dcStopVehicleButton != null)
            dcStopVehicleButton.onClick.AddListener(OnStopVehicleClick);
        if (dcSendToDepotButton != null)
            dcSendToDepotButton.onClick.AddListener(OnSendToDepotClick);
        if (dcDeleteAllRoutesButton != null)
            dcDeleteAllRoutesButton.onClick.AddListener(OnDeleteAllRoutesClick);

        // Dropdown – uložiť výber per depot
        if (vehicleTypeDropdown != null)
            vehicleTypeDropdown.onValueChanged.AddListener(OnVehicleTypeDropdownChanged);

        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodných skriptoch (DepotRoadConstructionUIwindow) už zbehol.
        if (depotRoadConstructionMenuUIPanel != null)
            depotRoadConstructionMenuUIPanel.SetActive(false);
    }

    void Update()
    {
        // Pokiaľ je okno otvorené, periodicky obnovujeme Status a Current Route
        if (currentDepotTile.HasValue
            && depotRoadConstructionMenuUIPanel != null
            && depotRoadConstructionMenuUIPanel.activeSelf)
        {
            RefreshStatusAndRoute();
            UpdateDefineRouteButtonLabel();
            RefreshButtonStates();
        }
    }

    // =====================================================================
    // PUBLIC API – volá GameManager pri kliknutí na ROAD Depot tile
    // =====================================================================

    /// <summary>
    /// Otvorí okno pre dané ROAD depo (x, z). Načíta uloženú konfiguráciu
    /// pre toto konkrétne depo (alebo vytvorí novú s defaultmi).
    /// </summary>
    public void OpenForDepot(int dx, int dz)
    {
        Vector2Int tile = new Vector2Int(dx, dz);

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
        if (vehicleTypeDropdown != null)
            vehicleTypeDropdown.SetValueWithoutNotify(Mathf.Clamp(cfg.vehicleTypeIndex, 0, VEHICLE_TYPE_OPTIONS.Length - 1));

        if (depotRoadConstructionMenuUIPanel != null)
            depotRoadConstructionMenuUIPanel.SetActive(true);

        UpdateDepotNameCaption(dx, dz);

        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();

        Debug.Log($"[DepotRoadConstructionMenuUI] Otvorené pre ROAD depo [{dx},{dz}].");
    }

    public void CloseWindow()
    {
        if (depotRoadConstructionMenuUIPanel != null)
            depotRoadConstructionMenuUIPanel.SetActive(false);
        currentDepotTile = null;
    }

    /// <summary>
    /// Volá GameManager keď bola pridaná ROAD stanica v "Define Route" režime.
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
    /// Aplikuje rovnakú validáciu ako pri stlačení "End Tracing" v UI:
    /// ak vozidlo nemá aspoň MIN_STATIONS_FOR_VALID_ROUTE staníc, trasa
    /// sa zahodí.
    /// </summary>
    public void OnRouteDefineCancelled()
    {
        if (currentDepotTile.HasValue
            && GameManager.instance != null
            && VehicleSystem.instance != null)
        {
            var t = currentDepotTile.Value;
            var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
            if (vd != null
                && vd.stations.Count > 0
                && vd.stations.Count < MIN_STATIONS_FOR_VALID_ROUTE)
            {
                Debug.LogWarning($"[DepotRoadConstructionMenuUI] Define Route zrušené (ESC) – trasa zahodená, počet staníc {vd.stations.Count} < {MIN_STATIONS_FOR_VALID_ROUTE}.");
                GameManager.instance.RequestClearStationsForRoadDepot(t.x, t.y);
            }
        }

        if (currentDepotTile.HasValue
            && depotRoadConstructionMenuUIPanel != null
            && depotRoadConstructionMenuUIPanel.activeSelf)
        {
            RefreshStatusAndRoute();
            RefreshDefinedRoutesDropdown();
        }
        UpdateDefineRouteButtonLabel();
        RefreshButtonStates();
    }

    // =====================================================================
    // BUTTON HANDLERS
    // =====================================================================

    private void OnCreateVehicleClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        var cfg = depotConfigs[t];
        string typeName = VEHICLE_TYPE_OPTIONS[
            Mathf.Clamp(cfg.vehicleTypeIndex, 0, VEHICLE_TYPE_OPTIONS.Length - 1)];
        GameManager.instance.RequestCreateVehicleForRoadDepot(t.x, t.y, typeName);
        RefreshStatusAndRoute();
        RefreshButtonStates();
    }

    private void OnStartVehicleClick()
    {
        GameManager.instance?.PlaySfxVehicleStart();

        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestStartVehicleForRoadDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnStopVehicleClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestStopVehicleForRoadDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnRemoveVehicleClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestRemoveVehicleForRoadDepot(t.x, t.y);
        RefreshStatusAndRoute();
        RefreshButtonStates();
    }

    private void OnSendToDepotClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;
        GameManager.instance.RequestSendVehicleToDepotForRoadDepot(t.x, t.y);
        RefreshStatusAndRoute();
    }

    private void OnDefineRouteClick()
    {
        if (!currentDepotTile.HasValue || GameManager.instance == null) return;
        var t = currentDepotTile.Value;

        bool wasCollecting = GameManager.instance.IsCollectingStationsForRoadDepot(t.x, t.y);

        GameManager.instance.RequestToggleDefineRouteForRoadDepot(t.x, t.y);

        // Ak sme práve ukončili zbieranie staníc, validujeme: min 2 stanice.
        if (wasCollecting && VehicleSystem.instance != null)
        {
            var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
            if (vd != null && vd.stations.Count < MIN_STATIONS_FOR_VALID_ROUTE)
            {
                Debug.LogWarning($"[DepotRoadConstructionMenuUI] Trasa zahodená – počet staníc {vd.stations.Count} < {MIN_STATIONS_FOR_VALID_ROUTE}.");
                GameManager.instance.RequestClearStationsForRoadDepot(t.x, t.y);
            }
        }

        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();
    }

    /// <summary>
    /// DCDeleteAllRoutesButton handler – analógia k rail variantu.
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
            Debug.LogWarning("[DepotRoadConstructionMenuUI] GameWarningMenuUI nie je v scéne – " +
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
    /// SPRÁVANIE PODĽA STAVU VOZIDLA – identické s rail variantom:
    ///
    /// • Vozidlo "Stopped" (vrátane prípadu, keď ešte neexistuje alebo
    ///   nemá žiadne stanice):
    ///   Vždy vymažeme všetky definované stanice. Ak práve prebieha
    ///   "Define Route" režim pre toto depo, ten sa zároveň ukončí
    ///   (zariadi RequestClearStationsForRoadDepot v GameManager).
    ///
    /// • Vozidlo "Running":
    ///   1) Vozidlo okamžite zastavíme (ekvivalent kliku na DCStopVehicleButton).
    ///   2) Až potom vymažeme stanice.
    /// </summary>
    private void DeleteAllRoutesForDepot(Vector2Int t)
    {
        if (GameManager.instance == null) return;

        if (VehicleSystem.instance != null)
        {
            var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
            if (vd != null && vd.isRunning)
            {
                Debug.Log($"[DepotRoadConstructionMenuUI] Vozidlo v depe [{t.x},{t.y}] beží – pred mazaním staníc ho zastavujem.");
                GameManager.instance.RequestStopVehicleForRoadDepot(t.x, t.y);
            }
        }

        GameManager.instance.RequestClearStationsForRoadDepot(t.x, t.y);

        RefreshStatusAndRoute();
        UpdateDefineRouteButtonLabel();
        RefreshDefinedRoutesDropdown();
        RefreshButtonStates();
    }

    /// <summary>
    /// Okno, z ktorého sa varovanie vyvoláva. Keď sa skryje, varovanie sa zruší.
    /// </summary>
    private GameObject WarningOwner =>
        depotRoadConstructionMenuUIPanel != null ? depotRoadConstructionMenuUIPanel : gameObject;

    private void OnVehicleTypeDropdownChanged(int newIndex)
    {
        if (!currentDepotTile.HasValue) return;
        depotConfigs[currentDepotTile.Value].vehicleTypeIndex = newIndex;
        // Uložíme len vybraný index. Dátová štruktúra vozidla sa vytvorí až
        // pri kliknutí na DCCreateVehicleButton – vtedy sa podľa tohto
        // indexu vyberie VehicleSpec z VehicleCatalog. Zmena dropdownu
        // neovplyvní už existujúce vozidlo v scéne.
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

    private void UpdateDepotNameCaption(int dx, int dz)
    {
        if (depotNameTextCaption == null) return;
        depotNameTextCaption.text = $"Depot ({dx},{dz})";
    }

    private void UpdateDefineRouteButtonLabel()
    {
        if (dcDefineRouteButtonLabel == null) return;

        bool active = false;
        if (currentDepotTile.HasValue && GameManager.instance != null)
        {
            var t = currentDepotTile.Value;
            active = GameManager.instance.IsCollectingStationsForRoadDepot(t.x, t.y);
        }

        string target = active ? DEFINE_ROUTE_LABEL_ACTIVE : DEFINE_ROUTE_LABEL_IDLE;
        if (dcDefineRouteButtonLabel.text != target)
            dcDefineRouteButtonLabel.text = target;
    }

    /// <summary>
    /// Obnoví StatusTypeTextCaption a CurrentRouteTextCaption pre vozidlo
    /// aktuálne otvoreného depa.
    ///
    /// StatusTypeTextCaption má tri možné hodnoty:
    ///   • "NO VEHICLE" – vozidlo neexistuje (ešte nebolo vytvorené, alebo
    ///                    bolo zmazané cez DCRemoveVehicleButton).
    ///   • "Running"    – vozidlo existuje a beží.
    ///   • "Stopped"    – vozidlo existuje a je zastavené.
    /// </summary>
    private void RefreshStatusAndRoute()
    {
        if (!currentDepotTile.HasValue)
        {
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = "-";
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        var t = currentDepotTile.Value;

        if (VehicleSystem.instance == null)
        {
            // VehicleSystem nie je k dispozícii – nevieme o žiadnom vozidle.
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = NO_VEHICLE_LABEL;
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
        if (vd == null)
        {
            // Vozidlo v tomto depe neexistuje – buď ešte nebolo vytvorené
            // (NotCreated), alebo bolo zmazané (Removed). V oboch prípadoch
            // StatusTypeTextCaption zobrazuje "NO VEHICLE".
            if (statusTypeTextCaption != null) statusTypeTextCaption.text = NO_VEHICLE_LABEL;
            if (currentRouteTextCaption != null) currentRouteTextCaption.text = "0";
            return;
        }

        // Vozidlo existuje → Running / Stopped podľa isRunning.
        if (statusTypeTextCaption != null)
            statusTypeTextCaption.text = vd.isRunning ? "Running" : "Stopped";

        if (currentRouteTextCaption != null)
            currentRouteTextCaption.text = vd.stations.Count.ToString();
    }

    /// <summary>
    /// Naplní DefinedRoutesDropdown podľa vd.stations pre vozidlo aktuálne
    /// otvoreného depa. Formát zhodný s rail variantom:
    ///   • "No Stations" placeholder ak nie sú žiadne stanice
    ///   • "Station N - (x,z)" pre každú stanicu (1-based)
    /// </summary>
    private void RefreshDefinedRoutesDropdown()
    {
        if (definedRoutesDropdown == null) return;

        var options = new List<string>();

        List<Vector2Int> stations = null;
        if (currentDepotTile.HasValue && VehicleSystem.instance != null)
        {
            var t = currentDepotTile.Value;
            var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
            if (vd != null) stations = vd.stations;
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
        definedRoutesDropdown.SetValueWithoutNotify(0);
        definedRoutesDropdown.RefreshShownValue();
    }

    // =====================================================================
    // ENABLE / DISABLE TLAČIDIEL PODĽA STAVU DEPA
    //
    // Analógia k DepotRailConstructionMenuUI.RefreshButtonStates – stav je
    // odvoditeľný z: existencie vozidla pre depo (VehicleSystem.GetVehicle
    // != null; 1 depo = 1 vozidlo), či je definovaná trasa (vd.stations.Count
    // > 0) a či práve prebieha "Define Route" zbieranie (IsCollectingStationsForRoadDepot).
    //
    //   A) "Creation" prvky – povolené IBA keď vozidlo NEEXISTUJE:
    //        DCCreateVehicleButton, VehicleTypeDropdown.
    //      (ROAD nemá Wagon Count ani Wagon Type – tie tu nie sú.)
    //
    //   B) "Prevádzkové" prvky – povolené IBA keď vozidlo EXISTUJE:
    //        DCStartVehicleButton, DCRemoveVehicleButton, DCStopVehicleButton,
    //        DCSendToDepotButton, DefinedRoutesDropdown.
    //
    //   C) "Routovacie" prvky – vzájomne sa vylučujúca dvojica (len keď
    //      vozidlo existuje):
    //        DCDefineRouteButton    : povolené keď práve traceujeme ("End
    //                                 Tracing") ALEBO trasa ešte NIE JE definovaná.
    //        DCDeleteAllRoutesButton: povolené keď NEtraceujeme a trasa JE definovaná.
    // </summary>
    private void RefreshButtonStates()
    {
        bool vehicleExists = false;
        bool hasRoute = false;
        bool isTracing = false;

        if (currentDepotTile.HasValue)
        {
            var t = currentDepotTile.Value;

            if (VehicleSystem.instance != null)
            {
                var vd = VehicleSystem.instance.GetVehicle(t.x, t.y);
                if (vd != null)
                {
                    vehicleExists = true;
                    hasRoute = vd.stations != null && vd.stations.Count > 0;
                }
            }

            if (GameManager.instance != null)
                isTracing = GameManager.instance.IsCollectingStationsForRoadDepot(t.x, t.y);
        }

        // A) Creation prvky – povolené len keď vozidlo NEEXISTUJE.
        SetInteractable(dcCreateVehicleButton, !vehicleExists);
        SetInteractable(vehicleTypeDropdown, !vehicleExists);

        // B) Prevádzkové prvky – povolené len keď vozidlo EXISTUJE.
        SetInteractable(dcStartVehicleButton, vehicleExists);
        SetInteractable(dcRemoveVehicleButton, vehicleExists);
        SetInteractable(dcStopVehicleButton, vehicleExists);
        SetInteractable(dcSendToDepotButton, vehicleExists);
        SetInteractable(definedRoutesDropdown, vehicleExists);

        // C) Define Route vs Delete All Routes (len pri existujúcom vozidle).
        SetInteractable(dcDefineRouteButton, vehicleExists && (isTracing || !hasRoute));
        SetInteractable(dcDeleteAllRoutesButton, vehicleExists && !isTracing && hasRoute);
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