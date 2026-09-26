using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// StatusStationMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie INFORMÁCIÍ o JEDNEJ konkrétnej stanici – konkrétne
/// zoznam tovární, ktoré daná stanica eviduje vo svojom 9×9 catchmente.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusStationUIwindow (analogicky k dvojici
///     StatusVehiclesMenuUI + StatusVehiclesUIwindow).
///  1. Okno sa otvára z GameManagera pri ľavom kliku na tile stanice (tileID == 2),
///     a to BEZ OHĽADU na to, či ide o RAIL alebo ROAD stanicu. GameManager
///     podľa kategórie tile (Rail / Road) vyhľadá StationInstance buď v
///     RailStationRegistry alebo v RoadStationRegistry, a následne zavolá
///     OpenForStation(stationInstance). Mimo toho je okno skryté.
///  2. Po otvorení sa do jediného UI Textu "FactoriesTextCaption" vypíše
///     zoznam tovární evidovaných pre danú stanicu, každá továreň na vlastnom
///     riadku len svojím názvom:
///         Coal Mine
///         Forest
///         Power Station
///     Ak stanica neeviduje žiadnu továreň, zobrazí sa NO_FACTORIES_LABEL.
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje jeden meniteľný textový prvok – FactoriesTextCaption
///   (v Hierarchy "FactoriesTextCaption", child "StatusStationMenuUI - Content").
///   Iné popisky sú statické nadpisy nastavené v Inspectore – tento script
///   ich nemení, preto na ne ani nedrží referenciu.
///
/// DÁTOVÝ ZDROJ:
///   StationInstance.Factories – zoznam FactoryInstance, ktorý napĺňa
///   RailStationRegistry/RoadStationRegistry pri (re)scane. StationInstance
///   je KATEGÓRIOVO NEUTRÁLNA (rovnaký typ pre Rail aj Road) – preto vystačí
///   jediné UI okno pre obe varianty staníc. Z FactoryInstance sa berie
///   priamy názov továrne cez vlastnosť .Name (skratka cez Definition.Name),
///   napr. "Coal Mine", "Forest", "Power Station", "SawMill".
///
/// ROZDIEL OPROTI StatusVehiclesMenuUI:
///   StatusVehiclesMenuUI je globálne zoznamové okno (všetky vozidlá v hre)
///   – nepotrebuje vedieť, "ktoré vozidlo" zobrazuje, lebo zobrazuje všetky.
///   Naopak StatusStationMenuUI je viazané na JEDNU konkrétnu stanicu, ktorú
///   si pri otvorení (OpenForStation) musí zapamätať. Vďaka tomu sa okno dá
///   aj "obnoviť" (RefreshFactoriesList) bez nového kliku – napr. ak by sa
///   medzitým prerátal RescanFactories pre danú stanicu.
///
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusStationMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusStationMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusStationMenuUIPanel;

    [Header("Station Name Caption")]
    [Tooltip("StatusStationNameTextCaption – UI Text v titulku okna, do ktorého " +
             "sa vypíše názov stanice v tvare \"Station [x, z]\", kde [x, z] sú " +
             "súradnice tile stanice (rovnaké pre Rail aj Road).")]
    [SerializeField] private TMP_Text statusStationNameTextCaption;

    [Header("Factories Info Caption")]
    [Tooltip("FactoriesTextCaption – jediný UI Text, do ktorého sa vypíše " +
             "zoznam tovární evidovaných stanicou (každá továreň na vlastnom riadku).")]
    [SerializeField] private TMP_Text factoriesTextCaption;

    // Text v caption, keď stanica nemá v 9×9 catchmente ani jednu továreň.
    private const string NO_FACTORIES_LABEL = "No factories.";

    // Text v name caption, keď nie je nastavená žiadna stanica (defenzívne).
    private const string NO_STATION_NAME_LABEL = "Station";

    // =====================================================================
    // STAV
    // =====================================================================

    /// <summary>
    /// Aktuálne zobrazená stanica (analógia k currentFactory v StatusFactoryMenuUI).
    /// Nastaví sa v OpenForStation a vynuluje v CloseWindow – pri ďalšom otvorení
    /// musí volajúci znova určiť, ktorú stanicu zobraziť.
    /// </summary>
    private StationInstance currentStation;

    /// <summary>
    /// Súradnice tile aktuálne zobrazenej stanice [x, z]. Nastavia sa spolu
    /// s currentStation v OpenForStation a použijú sa pri vykreslení názvu
    /// stanice ("Station [x, z]") v RefreshStationName. Pri CloseWindow sa
    /// nevynulovávajú zvlášť – stačí, že currentStation == null deaktivuje
    /// vykreslenie názvu.
    /// </summary>
    private int currentTileX;
    private int currentTileZ;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusVehiclesMenuUI / StatusFactoryMenuUI):
        // na rovnakom GameObjecte (root paneli) je pripojený aj sprievodný
        // script StatusStationUIwindow, ktorý si v svojom Awake() registruje
        // listener na Close (X) tlačidlo. Poradie volania Awake() medzi
        // viacerými skriptami na tom istom GameObjecte je v Unity
        // nedeterministické (Script Execution Order). Ak by tento Awake()
        // zbehol PRV a hneď panel deaktivoval, druhý script by svoj Awake()
        // už nemusel stihnúť – Unity nevolá Awake() na neaktívnych
        // GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start().
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusStationUIwindow už zbehol. Okno sa otvorí až
        // po ľavom kliku na tile stanice (Rail/Road) cez OpenForStation().
        if (statusStationMenuUIPanel != null)
            statusStationMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Otvorí okno pre konkrétnu stanicu a zobrazí v ňom názov stanice
    /// ("Station [x, z]") + zoznam jej tovární. Volá GameManager pri ľavom
    /// kliku na tile stanice (tileID == 2) – jedna metóda obslúži aj RAIL aj
    /// ROAD variant, lebo StationInstance je pre obe kategórie rovnaký typ.
    ///
    /// Súradnice [tileX, tileZ] sú indexy tile stanice (z IndicatrixAPI
    /// SnapTileIndex → Vector2Int(x, z)). GameManager ich má priamo na mieste
    /// kliku, preto ich odovzdáva sem – nie je tým pádom potrebné, aby
    /// StationInstance svoje súradnice samo evidovalo.
    ///
    /// Ak station == null, okno sa neotvorí (defenzívne – napr. ak by sa
    /// stanica medzitým zbúrala/odregistrovala).
    /// </summary>
    public void OpenForStation(StationInstance station, int tileX, int tileZ)
    {
        if (station == null)
        {
            Debug.LogWarning("[StatusStationMenuUI] OpenForStation volané s null – ignorujem.");
            return;
        }

        currentStation = station;
        currentTileX = tileX;
        currentTileZ = tileZ;

        if (statusStationMenuUIPanel != null)
            statusStationMenuUIPanel.SetActive(true);

        RefreshStationName();
        RefreshFactoriesList();
    }

    /// <summary>
    /// Spätne kompatibilné preťaženie bez súradníc. Ponechané pre prípadných
    /// starších volajúcich – názov stanice sa v tomto prípade nedá zložiť so
    /// správnymi súradnicami, preto sa použijú naposledy známe (default 0,0).
    /// Preferuj OpenForStation(station, tileX, tileZ).
    /// </summary>
    public void OpenForStation(StationInstance station)
    {
        OpenForStation(station, currentTileX, currentTileZ);
    }

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Užitočné, ak by sa
    /// niekedy v budúcnosti pridalo toggle tlačidlo na ovládacom paneli;
    /// otvorenie cez tile klik primárne používa OpenForStation.
    ///
    /// Pri otvorení vyžaduje, aby už bola nastavená currentStation – inak
    /// caption zobrazí NO_FACTORIES_LABEL (nemáme čo zobraziť).
    /// </summary>
    public void ToggleWindow()
    {
        if (statusStationMenuUIPanel == null) return;

        bool newState = !statusStationMenuUIPanel.activeSelf;
        statusStationMenuUIPanel.SetActive(newState);

        if (newState)
        {
            RefreshStationName();
            RefreshFactoriesList();
        }
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusStationUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    ///
    /// Pri zatvorení vynulujeme aj currentStation – okno už nezobrazuje
    /// žiadnu stanicu, takže prípadné neskoršie ToggleWindow by len zobrazil
    /// prázdny zoznam. Pri ďalšom kliku na tile stanice volajúci znova použije
    /// OpenForStation, čím sa currentStation korektne nastaví.
    /// </summary>
    public void CloseWindow()
    {
        if (statusStationMenuUIPanel != null)
            statusStationMenuUIPanel.SetActive(false);

        currentStation = null;
    }

    // =====================================================================
    // NAPLNENIE CAPTIONU
    // =====================================================================

    /// <summary>
    /// Prepíše StatusStationNameTextCaption názvom aktuálnej stanice v tvare
    /// "Station [x, z]", kde [x, z] sú súradnice tile stanice (currentTileX,
    /// currentTileZ) zhodné pre RAIL aj ROAD variant.
    ///
    /// Príklad výstupu:
    ///     Station [14, 21]
    ///
    /// Ak currentStation nie je nastavená (napr. okno otvorené cez
    /// ToggleWindow bez predchádzajúceho OpenForStation), zobrazí sa len
    /// holé NO_STATION_NAME_LABEL ("Station") bez súradníc.
    /// </summary>
    private void RefreshStationName()
    {
        if (statusStationNameTextCaption == null) return;

        if (currentStation == null)
        {
            statusStationNameTextCaption.text = NO_STATION_NAME_LABEL;
            return;
        }

        statusStationNameTextCaption.text = FormatStationName(currentTileX, currentTileZ);
    }

    /// <summary>
    /// Naformátuje názov stanice do tvaru "Station [x, z]".
    /// </summary>
    private static string FormatStationName(int tileX, int tileZ)
    {
        return $"Station [{tileX}, {tileZ}]";
    }

    /// <summary>
    /// Prepíše FactoriesTextCaption aktuálnym zoznamom tovární pre
    /// currentStation. Každá továreň je na vlastnom riadku len svojím
    /// názvom (FactoryInstance.Name). Ak v zóne nie je žiadna továreň, alebo
    /// currentStation nie je nastavená, zobrazí sa NO_FACTORIES_LABEL.
    /// </summary>
    private void RefreshFactoriesList()
    {
        if (factoriesTextCaption == null) return;

        // Ak nemáme stanicu (napr. okno otvorené cez ToggleWindow bez
        // predchádzajúceho OpenForStation), zobrazíme prázdny zoznam.
        if (currentStation == null)
        {
            factoriesTextCaption.text = NO_FACTORIES_LABEL;
            return;
        }

        factoriesTextCaption.text = FormatFactories(currentStation.Factories);
    }

    /// <summary>
    /// Naformátuje CELÝ zoznam tovární do textu pre caption.
    ///
    /// FORMÁT:
    ///   • 0 tovární → NO_FACTORIES_LABEL ("No factories.").
    ///   • N tovární → N riadkov, každý len názov továrne (FactoryInstance.Name).
    ///
    /// Príklad výstupu:
    ///     Coal Mine
    ///     Forest
    ///     Power Station
    /// </summary>
    private static string FormatFactories(List<FactoryInstance> factories)
    {
        if (factories == null || factories.Count == 0)
            return NO_FACTORIES_LABEL;

        var sb = new StringBuilder();
        for (int i = 0; i < factories.Count; i++)
        {
            if (i > 0) sb.Append('\n');          // každá ďalšia továreň na nový riadok
            sb.Append(FormatFactory(factories[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naformátuje jednu továreň (FactoryInstance) do reťazca pre jeden riadok.
    /// Aktuálne vracia len Name (napr. "Coal Mine"), v súlade so zadaným
    /// formátom v špecifikácii.
    /// </summary>
    private static string FormatFactory(FactoryInstance factory)
    {
        if (factory == null) return "-";
        return factory.Name;
    }
}
