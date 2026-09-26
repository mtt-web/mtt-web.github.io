using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using Game.TrainStock;

/// <summary>
/// StatusTrainsMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie INFORMÁCIÍ o VŠETKÝCH vlakoch v hre.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusTrainsUIwindow (analogicky k dvojici
///     StatusFactoryMenuUI + StatusFactoryUIwindow).
///  1. Okno sa otvára/zatvára toggle tlačidlom "StatusTrainsUIButton"
///     v GameMenuUI, ktoré volá verejné API ToggleWindow(). Mimo toho je
///     okno skryté.
///  2. Pri každom otvorení sa do jediného UI Textu "TrainsTextCaption"
///     vypíše zoznam všetkých vlakov, každý vlak na vlastnom riadku
///     vo formáte:
///         "ČísloRiadku. názov vlaku: počet vagónov wagons, typ suroviny"
///     príklad:
///         "1. Train 1: 5 wagons, Coal"
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje jeden meniteľný textový prvok – TrainsTextCaption
///   (v Hierarchy "TrainsTextCaption", child "FactoryConstructionMenuUI -
///   Content"). Iné popisky sú statické nadpisy nastavené v Inspectore,
///   tento script ich nemení, preto na ne ani nedrží referenciu.
///
/// DÁTOVÝ ZDROJ:
///   Zoznam vlakov pochádza z TrainSystem.GetAllTrainConsists(), ktorý vráti
///   read-only kópiu zoznamu TrainInstance (TrainStock.cs):
///     • Name           → názov vlaku, napr. "Train 1".
///     • Wagons.Count   → počet vagónov v súprave.
///     • Wagons[0].Type → typ suroviny (WagonType.Name), napr. "Coal Truck".
///   Platí "1 train depo = 1 train", takže počet TrainInstance = počet
///   vlakov v hre.
///
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusTrainsMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusTrainsMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusTrainsMenuUIPanel;

    [Header("Trains Info Caption")]
    [Tooltip("TrainsTextCaption – jediný UI Text, do ktorého sa vypíše " +
             "zoznam všetkých vlakov (každý vlak na vlastnom riadku).")]
    [SerializeField] private TMP_Text trainsTextCaption;

    // Text v caption, keď v hre nie je ani jeden vlak (zoznam je prázdny).
    private const string NO_TRAINS_LABEL = "No trains.";

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusFactoryMenuUI): na rovnakom GameObjecte
        // (root paneli) je pripojený aj sprievodný script StatusTrainsUIwindow,
        // ktorý si v svojom Awake() registruje listener na Close (X) tlačidlo.
        // Poradie volania Awake() medzi viacerými skriptami na tom istom
        // GameObjecte je v Unity nedeterministické (Script Execution Order).
        // Ak by tento Awake() zbehol PRV a hneď panel deaktivoval, druhý
        // script by svoj Awake() už nemusel stihnúť – Unity nevolá Awake()
        // na neaktívnych GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania v scéne.
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusTrainsUIwindow (close button listener) už zbehol.
        // Okno sa otvorí až po kliku na StatusTrainsUIButton cez ToggleWindow().
        if (statusTrainsMenuUIPanel != null)
            statusTrainsMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // PUBLIC API – volá GameMenuUI z toggle tlačidla "StatusTrainsUIButton"
    // =====================================================================

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Volá GameMenuUI pri
    /// kliknutí na toggle tlačidlo "StatusTrainsUIButton".
    ///
    /// Pri otvorení sa caption naplní AKTUÁLNYM zoznamom vlakov – tým je
    /// okno vždy synchronizované so stavom hry v momente otvorenia.
    /// </summary>
    public void ToggleWindow()
    {
        if (statusTrainsMenuUIPanel == null) return;

        bool newState = !statusTrainsMenuUIPanel.activeSelf;
        statusTrainsMenuUIPanel.SetActive(newState);

        // Caption napĺňame len pri otváraní – pri zatváraní netreba.
        if (newState)
            RefreshTrainsList();
    }

    /// <summary>
    /// Zobrazí okno a naplní zoznam vlakov. Pohodlné explicitné API, ak by
    /// niektorá časť kódu chcela okno otvoriť priamo (bez toggle).
    /// </summary>
    public void OpenWindow()
    {
        if (statusTrainsMenuUIPanel != null)
            statusTrainsMenuUIPanel.SetActive(true);

        RefreshTrainsList();
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusTrainsUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    /// </summary>
    public void CloseWindow()
    {
        if (statusTrainsMenuUIPanel != null)
            statusTrainsMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // NAPLNENIE CAPTIONU
    // =====================================================================

    /// <summary>
    /// Prepíše TrainsTextCaption aktuálnym zoznamom všetkých vlakov v hre.
    ///
    /// Zoznam sa získa z TrainSystem.GetAllTrainConsists(). Každý vlak je
    /// na vlastnom riadku vo formáte:
    ///     "ČísloRiadku. názov vlaku: počet vagónov wagons, typ suroviny"
    /// Ak v hre nie je ani jeden vlak, zobrazí sa NO_TRAINS_LABEL.
    /// </summary>
    private void RefreshTrainsList()
    {
        if (trainsTextCaption == null) return;

        // TrainSystem je singleton (TrainSystem.instance). Ak ešte neexistuje
        // (napr. scéna sa len načítava), zobrazíme prázdny zoznam.
        if (TrainSystem.instance == null)
        {
            trainsTextCaption.text = NO_TRAINS_LABEL;
            return;
        }

        // Súradnice depa (pre focus kamery) PRIAMO spárované s inštanciou cez
        // GetActiveTrainInfos() – (DepotX, DepotZ, Consist). Žiadny sken gridu
        // ani porovnávanie referencií, takže klik vždy trafí správny tile.
        List<TrainSystem.ActiveTrainInfo> infos = TrainSystem.instance.GetActiveTrainInfos();

        var coords = new List<Vector2Int>(infos != null ? infos.Count : 0);
        trainsTextCaption.text = FormatTrains(infos, coords);
        BindClickable(trainsTextCaption, coords);
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
    /// Naformátuje CELÝ zoznam vlakov do textu pre caption.
    ///
    /// FORMÁT:
    ///   • 0 vlakov  → NO_TRAINS_LABEL ("No trains.").
    ///   • N vlakov  → N riadkov, každý "ČísloRiadku. názov: počet wagons, surovina".
    ///
    /// Číslo riadku je 1-based (prvý vlak = "1.").
    /// </summary>
    private static string FormatTrains(List<TrainSystem.ActiveTrainInfo> infos,
                                       List<Vector2Int> outCoords)
    {
        if (infos == null || infos.Count == 0)
            return NO_TRAINS_LABEL;

        var sb = new StringBuilder();
        for (int i = 0; i < infos.Count; i++)
        {
            if (i > 0) sb.Append('\n');          // každý ďalší vlak na nový riadok

            // Depo-tile priamo z dát (depo = pozícia, na ktorú klik vycentruje).
            outCoords.Add(new Vector2Int(infos[i].DepotX, infos[i].DepotZ));

            // Neviditeľný <link="i"> obaľuje celý riadok (index do outCoords).
            sb.Append("<link=\"").Append(i).Append("\">");
            sb.Append(i + 1);                    // 1-based číslo riadku
            sb.Append(". ");
            sb.Append(FormatTrain(infos[i].Consist));
            sb.Append("</link>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naformátuje jeden vlak (TrainInstance) do reťazca
    /// "názov vlaku: počet vagónov wagons, typ suroviny",
    /// napr. "Train 1: 5 wagons, Coal".
    ///
    /// POZN.: Súradnice sa do VIDITEĽNÉHO textu zámerne NEpridávajú – pozícia
    /// pre klik ide cez dátový zoznam (link index). Text tak môže v budúcnosti
    /// niesť reálny názov vlaku bez súradníc.
    ///
    /// TYP SUROVINY:
    ///   Súprava môže mať viacero vagónov; všetky vagóny vytvorené v jednom
    ///   depe sú rovnakého typu (pozri TrainSystem.CreateTrain). Typ suroviny
    ///   preto čítame z prvého vagónu (Wagons[0].Type.Name). Ak vlak nemá
    ///   žiadne vagóny, surovina sa vypíše ako "-".
    /// </summary>
    private static string FormatTrain(TrainInstance train)
    {
        if (train == null) return "-";

        int wagonCount = train.Wagons != null ? train.Wagons.Count : 0;

        string resource = "-";
        if (wagonCount > 0 && train.Wagons[0] != null)
            resource = train.Wagons[0].Type.Name;

        return $"{train.Name}: {wagonCount} wagons, {resource}";
    }
}