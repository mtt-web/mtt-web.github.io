using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// StatusStationsMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie ZOZNAMU všetkých staníc v hre.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusStationsUIWindow (analogicky k dvojici
///     StatusFactoryMenuUI + StatusFactoryUIwindow).
///  1. Okno sa otvorí/zatvorí toggle tlačidlom "StatusStationsUIButton"
///     v GameMenuUI (volá ToggleWindow / OpenWindow / CloseWindow).
///  2. Zobrazuje štyri informačné captiony:
///       • VehicleStationsTextCaption      – zoznam súradníc všetkých
///         vozidlových (Road) staníc, každá na vlastnom riadku.
///       • VehicleStationsCountTextCaption – celkový počet vozidlových staníc.
///       • TrainStationsTextCaption        – zoznam súradníc všetkých
///         vlakových (Rail) staníc, každá na vlastnom riadku.
///       • TrainStationsCountTextCaption   – celkový počet vlakových staníc.
///
/// ČO JE "STANICA":
///   Stanica je dlaždica s tileID == 2 v tile gride (IndicatrixAPI).
///   Železnice a cesty zdieľajú jeden grid, líšia sa len kategóriou:
///     • TileCategory.Rail → vlakové (Train) stanice,
///     • TileCategory.Road → vozidlové (Vehicle) stanice.
///   Stanica nemá vlastné meno – je jednoznačne určená súradnicami [x, z]
///   svojej dlaždice, preto sa do zoznamu vypisujú práve tieto súradnice.
///
/// DÁTOVÝ ZDROJ:
///   • IndicatrixAPI.GetAllStationCoords(TileCategory.Road) – Vehicle stanice.
///   • IndicatrixAPI.GetAllStationCoords(TileCategory.Rail) – Train stanice.
///   • IndicatrixAPI.GetStationCount(...)                   – počty staníc.
///   IndicatrixAPI drží mapu (tileGrid) privátne; navonok poskytuje len
///   read-only zoznam súradníc – tento script ho teda len ČÍTA.
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje (podľa Hierarchy) aj statické "popisky"
///   (VehicleStationsText, TrainStationsText) – STATICKÉ nadpisy nastavené
///   v Inspectore. Tento script ich nemení, preto na ne ani nedrží
///   referenciu. Meniteľné sú len štyri captiony nižšie.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusStationsMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusStationsMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusStationsMenuUIPanel;

    [Header("Vehicle Stations Captions")]
    [Tooltip("VehicleStationsTextCaption – zoznam súradníc všetkých " +
             "vozidlových (Road) staníc, každá na vlastnom riadku.")]
    [SerializeField] private TMP_Text vehicleStationsTextCaption;

    [Tooltip("VehicleStationsCountTextCaption – celkový počet vozidlových staníc.")]
    [SerializeField] private TMP_Text vehicleStationsCountTextCaption;

    [Header("Train Stations Captions")]
    [Tooltip("TrainStationsTextCaption – zoznam súradníc všetkých " +
             "vlakových (Rail) staníc, každá na vlastnom riadku.")]
    [SerializeField] private TMP_Text trainStationsTextCaption;

    [Tooltip("TrainStationsCountTextCaption – celkový počet vlakových staníc.")]
    [SerializeField] private TMP_Text trainStationsCountTextCaption;

    // Text v zozname, keď daná kategória nemá žiadne stanice (Count == 0).
    private const string NO_STATIONS_LABEL = "None";

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusFactoryMenuUI): na rovnakom
        // GameObjecte (root paneli) je pripojený aj sprievodný script
        // StatusStationsUIWindow, ktorý si v svojom Awake() registruje
        // listener na Close (X) tlačidlo. Poradie volania Awake() medzi
        // viacerými skriptami na tom istom GameObjecte je v Unity
        // nedeterministické (Script Execution Order). Ak by tento Awake()
        // zbehol PRV a hneď panel deaktivoval, druhý script by svoj Awake()
        // už nemusel stihnúť – Unity nevolá Awake() na neaktívnych
        // GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania v scéne.
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusStationsUIWindow (close button listener) už
        // zbehol. Okno sa otvorí až po kliku na toggle tlačidlo v GameMenuUI.
        if (statusStationsMenuUIPanel != null)
            statusStationsMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // PUBLIC API – volá GameMenuUI (toggle tlačidlo "StatusStationsUIButton")
    // =====================================================================

    /// <summary>
    /// Prepne viditeľnosť okna. Ak je zavreté → otvorí (a obnoví dáta),
    /// ak je otvorené → zavrie. Volá GameMenuUI pri kliku na toggle button.
    /// </summary>
    public void ToggleWindow()
    {
        bool isOpen = statusStationsMenuUIPanel != null
                      && statusStationsMenuUIPanel.activeSelf;

        if (isOpen)
            CloseWindow();
        else
            OpenWindow();
    }

    /// <summary>
    /// Otvorí okno a naplní všetky štyri captiony aktuálnym zoznamom staníc
    /// z IndicatrixAPI.
    /// </summary>
    public void OpenWindow()
    {
        // Zobraz panel
        if (statusStationsMenuUIPanel != null)
            statusStationsMenuUIPanel.SetActive(true);

        // Naplň captiony aktuálnym zoznamom staníc
        RefreshStationsInfo();

        Debug.Log("[StatusStationsMenuUI] Okno otvorené.");
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusStationsUIWindow pri stlačení
    /// Close (X), GameMenuUI pri toggle, alebo sa môže volať aj zvonku.
    /// </summary>
    public void CloseWindow()
    {
        if (statusStationsMenuUIPanel != null)
            statusStationsMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // NAPLNENIE CAPTIONOV
    // =====================================================================

    /// <summary>
    /// Prepíše všetky štyri captiony podľa aktuálneho stavu mapy:
    ///
    ///   VehicleStationsTextCaption      = zoznam súradníc Road staníc.
    ///   VehicleStationsCountTextCaption = počet Road staníc.
    ///   TrainStationsTextCaption        = zoznam súradníc Rail staníc.
    ///   TrainStationsCountTextCaption   = počet Rail staníc.
    ///
    /// Volá sa pri každom otvorení okna (OpenWindow) – tým je zoznam vždy
    /// aktuálny aj keď medzitým pribudli/ubudli stanice.
    /// </summary>
    // Prázdny zoznam na odviazanie klikov, keď nie sú žiadne stanice / dáta.
    private static readonly List<Vector2Int> EmptyCoords = new List<Vector2Int>();

    private void RefreshStationsInfo()
    {
        // IndicatrixAPI je spoločný zdroj pre obe kategórie staníc.
        if (IndicatrixAPI.instance == null)
        {
            Debug.LogWarning("[StatusStationsMenuUI] IndicatrixAPI.instance == null – " +
                             "stanice sa nedajú načítať.");

            // Aby okno nezobrazovalo staré dáta, captiony aspoň vyprázdnime.
            if (vehicleStationsTextCaption != null)
                vehicleStationsTextCaption.text = NO_STATIONS_LABEL;
            if (vehicleStationsCountTextCaption != null)
                vehicleStationsCountTextCaption.text = "0";
            if (trainStationsTextCaption != null)
                trainStationsTextCaption.text = NO_STATIONS_LABEL;
            if (trainStationsCountTextCaption != null)
                trainStationsCountTextCaption.text = "0";

            // Odviaž kliky (žiadne súradnice).
            BindClickable(vehicleStationsTextCaption, EmptyCoords);
            BindClickable(trainStationsTextCaption, EmptyCoords);
            return;
        }

        // --- VOZIDLOVÉ (ROAD) STANICE -------------------------------------
        List<Vector2Int> vehicleStations =
            IndicatrixAPI.instance.GetAllStationCoords(IndicatrixAPI.TileCategory.Road);

        if (vehicleStationsTextCaption != null)
            vehicleStationsTextCaption.text = FormatStationList(vehicleStations);

        if (vehicleStationsCountTextCaption != null)
            vehicleStationsCountTextCaption.text = vehicleStations.Count.ToString();

        // Súradnice idú do kliku PRIAMO z dát (nie z textu) – index riadku
        // zodpovedá indexu v tomto zozname.
        BindClickable(vehicleStationsTextCaption, vehicleStations);

        // --- VLAKOVÉ (RAIL) STANICE ---------------------------------------
        List<Vector2Int> trainStations =
            IndicatrixAPI.instance.GetAllStationCoords(IndicatrixAPI.TileCategory.Rail);

        if (trainStationsTextCaption != null)
            trainStationsTextCaption.text = FormatStationList(trainStations);

        if (trainStationsCountTextCaption != null)
            trainStationsCountTextCaption.text = trainStations.Count.ToString();

        BindClickable(trainStationsTextCaption, trainStations);
    }

    /// <summary>
    /// Zabezpečí, že caption reaguje na klik (StatusListCameraFocus), a odovzdá
    /// mu zoznam súradníc z dát (index = poradie riadku). Idempotentné.
    /// </summary>
    private static void BindClickable(TMP_Text caption, List<Vector2Int> coords)
    {
        if (caption == null) return;
        var focus = caption.GetComponent<StatusListCameraFocus>();
        if (focus == null) focus = caption.gameObject.AddComponent<StatusListCameraFocus>();
        focus.Bind(caption, coords);
    }

    /// <summary>
    /// Naformátuje zoznam súradníc staníc do textu pre caption – každá
    /// stanica na vlastnom riadku vo formáte "Station [x, z]", napr.:
    ///
    ///     Station [12, 7]
    ///     Station [20, 3]
    ///
    /// FORMÁT:
    ///   • 0 staníc → NO_STATIONS_LABEL ("None").
    ///   • N staníc → N riadkov (každá stanica na vlastnom riadku), čo je
    ///                v TMP_Text čitateľnejšie než jeden dlhý zlúčený riadok.
    /// </summary>
    private static string FormatStationList(List<Vector2Int> stations)
    {
        if (stations == null || stations.Count == 0)
            return NO_STATIONS_LABEL;

        var sb = new StringBuilder();
        for (int i = 0; i < stations.Count; i++)
        {
            if (i > 0) sb.Append('\n');      // každá ďalšia stanica na nový riadok

            // Neviditeľný <link="i"> – index do dátového zoznamu súradníc.
            // Vďaka nemu klik funguje nezávisle od viditeľného textu (ten sa
            // môže zmeniť na reálny názov stanice bez súradníc).
            sb.Append("<link=\"").Append(i).Append("\">");
            sb.Append(FormatStation(stations[i]));
            sb.Append("</link>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naformátuje stanicu do ZOBRAZOVACIEHO názvu. Ak existuje CityManager,
    /// použije sa menný názov podľa mesta (napr. "Copenhagen West"); inak sa
    /// vráti interný/fallback názov "Station [x, z]".
    ///
    /// POZN.: Súradnice pre klik (focus kamery) idú samostatne cez dátový zoznam
    /// (BindClickable) – NIE z tohto textu. Text tak môže byť ľubovoľný názov.
    /// </summary>
    private static string FormatStation(Vector2Int station)
    {
        if (CityManager.instance != null)
            return CityManager.instance.GetStationDisplayName(station.x, station.y);
        return $"Station [{station.x}, {station.y}]";
    }
}