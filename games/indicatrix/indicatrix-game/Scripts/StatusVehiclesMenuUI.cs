using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using Game.VehicleStock;

/// <summary>
/// StatusVehiclesMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie INFORMÁCIÍ o VŠETKÝCH vozidlách v hre.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusVehiclesUIwindow (analogicky k dvojici
///     StatusFactoryMenuUI + StatusFactoryUIwindow).
///  1. Okno sa otvára/zatvára toggle tlačidlom "StatusVehiclesUIButton"
///     v GameMenuUI, ktoré volá verejné API ToggleWindow(). Mimo toho je
///     okno skryté.
///  2. Pri každom otvorení sa do jediného UI Textu "VehiclesTextCaption"
///     vypíše zoznam všetkých vozidiel, každé vozidlo na vlastnom riadku
///     vo formáte:
///         "ČísloRiadku. názov vozidla, typ suroviny"
///     príklad:
///         "1. Vehicle 1, Coal"
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje jeden meniteľný textový prvok – VehiclesTextCaption
///   (v Hierarchy "VehiclesTextCaption", child "FactoryConstructionMenuUI -
///   Content"). Iné popisky sú statické nadpisy nastavené v Inspectore,
///   tento script ich nemení, preto na ne ani nedrží referenciu.
///
/// DÁTOVÝ ZDROJ:
///   Zoznam vozidiel pochádza z VehicleSystem.GetAllVehicleInstances(),
///   ktorý vráti read-only kópiu zoznamu VehicleInstance (VehicleStock.cs):
///     • Name      → názov vozidla, napr. "Vehicle 1".
///     • Type.Name → typ suroviny (VehicleType.Name), napr. "Coal Truck".
///   Vozidlo na rozdiel od vlaku nemá vagóny – je to JEDEN kváder, takže
///   sa nevypisuje počet vagónov. Platí "1 vehicle depo = 1 vehicle",
///   takže počet VehicleInstance = počet vozidiel v hre.
///
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusVehiclesMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusVehiclesMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusVehiclesMenuUIPanel;

    [Header("Vehicles Info Caption")]
    [Tooltip("VehiclesTextCaption – jediný UI Text, do ktorého sa vypíše " +
             "zoznam všetkých vozidiel (každé vozidlo na vlastnom riadku).")]
    [SerializeField] private TMP_Text vehiclesTextCaption;

    // Text v caption, keď v hre nie je ani jedno vozidlo (zoznam je prázdny).
    private const string NO_VEHICLES_LABEL = "No vehicles.";

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusFactoryMenuUI): na rovnakom GameObjecte
        // (root paneli) je pripojený aj sprievodný script
        // StatusVehiclesUIwindow, ktorý si v svojom Awake() registruje
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
        // sprievodnom StatusVehiclesUIwindow už zbehol. Okno sa otvorí až
        // po kliku na StatusVehiclesUIButton cez ToggleWindow().
        if (statusVehiclesMenuUIPanel != null)
            statusVehiclesMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // PUBLIC API – volá GameMenuUI z toggle tlačidla "StatusVehiclesUIButton"
    // =====================================================================

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Volá GameMenuUI pri
    /// kliknutí na toggle tlačidlo "StatusVehiclesUIButton".
    ///
    /// Pri otvorení sa caption naplní AKTUÁLNYM zoznamom vozidiel – tým je
    /// okno vždy synchronizované so stavom hry v momente otvorenia.
    /// </summary>
    public void ToggleWindow()
    {
        if (statusVehiclesMenuUIPanel == null) return;

        bool newState = !statusVehiclesMenuUIPanel.activeSelf;
        statusVehiclesMenuUIPanel.SetActive(newState);

        // Caption napĺňame len pri otváraní – pri zatváraní netreba.
        if (newState)
            RefreshVehiclesList();
    }

    /// <summary>
    /// Zobrazí okno a naplní zoznam vozidiel. Pohodlné explicitné API, ak by
    /// niektorá časť kódu chcela okno otvoriť priamo (bez toggle).
    /// </summary>
    public void OpenWindow()
    {
        if (statusVehiclesMenuUIPanel != null)
            statusVehiclesMenuUIPanel.SetActive(true);

        RefreshVehiclesList();
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusVehiclesUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    /// </summary>
    public void CloseWindow()
    {
        if (statusVehiclesMenuUIPanel != null)
            statusVehiclesMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // NAPLNENIE CAPTIONU
    // =====================================================================

    /// <summary>
    /// Prepíše VehiclesTextCaption aktuálnym zoznamom všetkých vozidiel v hre.
    ///
    /// Zoznam sa získa z VehicleSystem.GetAllVehicleInstances(). Každé
    /// vozidlo je na vlastnom riadku vo formáte:
    ///     "ČísloRiadku. názov vozidla, typ suroviny"
    /// Ak v hre nie je ani jedno vozidlo, zobrazí sa NO_VEHICLES_LABEL.
    /// </summary>
    private void RefreshVehiclesList()
    {
        if (vehiclesTextCaption == null) return;

        // VehicleSystem je singleton (VehicleSystem.instance). Ak ešte
        // neexistuje (napr. scéna sa len načítava), zobrazíme prázdny zoznam.
        if (VehicleSystem.instance == null)
        {
            vehiclesTextCaption.text = NO_VEHICLES_LABEL;
            return;
        }

        // Súradnice depa (pre focus kamery) PRIAMO spárované s inštanciou cez
        // GetActiveVehicleInfos() – (DepotX, DepotZ, Instance). Žiadny sken
        // gridu ani porovnávanie referencií, takže klik vždy trafí správny tile.
        List<VehicleSystem.ActiveVehicleInfo> infos = VehicleSystem.instance.GetActiveVehicleInfos();

        var coords = new List<Vector2Int>(infos != null ? infos.Count : 0);
        vehiclesTextCaption.text = FormatVehicles(infos, coords);
        BindClickable(vehiclesTextCaption, coords);
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
    /// Naformátuje CELÝ zoznam vozidiel do textu pre caption.
    ///
    /// FORMÁT:
    ///   • 0 vozidiel → NO_VEHICLES_LABEL ("No vehicles.").
    ///   • N vozidiel → N riadkov, každý "ČísloRiadku. názov, surovina".
    ///
    /// Číslo riadku je 1-based (prvé vozidlo = "1.").
    /// </summary>
    private static string FormatVehicles(List<VehicleSystem.ActiveVehicleInfo> infos,
                                         List<Vector2Int> outCoords)
    {
        if (infos == null || infos.Count == 0)
            return NO_VEHICLES_LABEL;

        var sb = new StringBuilder();
        for (int i = 0; i < infos.Count; i++)
        {
            if (i > 0) sb.Append('\n');          // každé ďalšie vozidlo na nový riadok

            // Depo-tile priamo z dát (depo = pozícia, na ktorú klik vycentruje).
            outCoords.Add(new Vector2Int(infos[i].DepotX, infos[i].DepotZ));

            // Neviditeľný <link="i"> obaľuje celý riadok (index do outCoords).
            sb.Append("<link=\"").Append(i).Append("\">");
            sb.Append(i + 1);                    // 1-based číslo riadku
            sb.Append(". ");
            sb.Append(FormatVehicle(infos[i].Instance));
            sb.Append("</link>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naformátuje jedno vozidlo (VehicleInstance) do reťazca
    /// "názov vozidla, typ suroviny", napr. "Vehicle 1, Coal".
    ///
    /// POZN.: Súradnice sa do VIDITEĽNÉHO textu zámerne NEpridávajú – pozícia
    /// pre klik ide cez dátový zoznam (link index). Text tak môže v budúcnosti
    /// niesť reálny názov vozidla bez súradníc.
    ///
    /// TYP SUROVINY:
    ///   Vozidlo nemá vagóny – náklad preváža samotný kváder. Typ suroviny
    ///   preto čítame priamo z typu vozidla (Type.Name), napr. "Coal Truck".
    /// </summary>
    private static string FormatVehicle(VehicleInstance vehicle)
    {
        if (vehicle == null) return "-";
        return $"{vehicle.Name}, {vehicle.Type.Name}";
    }
}