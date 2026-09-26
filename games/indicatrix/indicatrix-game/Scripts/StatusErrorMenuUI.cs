using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// StatusErrorMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UNIVERZÁLNE UI okno pre zobrazenie CHYBOVEJ hlášky hráčovi.
///
/// Návrhovo je IDENTICKÉ s dvojicou StatusStationMenuUI + StatusStationUIwindow
/// (analogicky aj k StatusVehiclesMenuUI / StatusFactoryMenuUI). Líši sa len
/// tým, že namiesto entity (StationInstance) si pamätá jednoduchý reťazec –
/// text chyby, ktorý sa vypíše do "ErrorTextCaption".
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo (SECloseWindowButton) –
///     túto obsluhu rieši sprievodný script StatusErrorUIwindow (analogicky
///     k dvojici StatusStationMenuUI + StatusStationUIwindow).
///     NAVYŠE: okno sa po AUTO_CLOSE_DELAY sekundách (predvolene 3 s) zavrie
///     aj samo – pozri sekciu AUTO-CLOSE nižšie.
///  1. Okno NEOTVÁRA žiadny konkrétny tile/klik – otvára sa programovo
///     z MIESTA, kde v hre nastala chyba, volaním OpenWithError("...").
///     Príklad (zatiaľ jediný definovaný prípad): hráč sa pokúsi postaviť na
///     už obsadený tile → GameManager namiesto SetTile zavolá
///     OpenWithError(GameErrors.CannotBuildOnOccupiedTile). Mimo toho je okno
///     skryté.
///  2. Po otvorení sa do jediného UI Textu "ErrorTextCaption" vypíše
///     odovzdaná hláška, napr.:
///         It is not possible to build on the given square.
///     Ak by hláška chýbala (null/prázdna), zobrazí sa DEFAULT_ERROR_LABEL.
///
/// PREČO JEDNO OKNO PRE VŠETKY CHYBY:
///   Chýb v hre môže byť časom viac a môžu vznikať na rôznych miestach
///   (tile systém, ekonomika, vozidlá, ...). Aby sme neduplikovali UI, používa
///   sa stále TOTO JEDNO okno, len s INÝM textom. Volajúci (miesto incidentu)
///   určuje text – kanonické znenia sú sústredené v GameErrors.cs, takže sa
///   neopakujú "magic stringy" a všetky definované chyby sú na jednom mieste.
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje jeden meniteľný textový prvok – ErrorTextCaption
///   (v Hierarchy "ErrorTextCaption", child "StatusErrorMenuUI - Content").
///   Iné popisky sú statické nadpisy nastavené v Inspectore – tento script
///   ich nemení, preto na ne ani nedrží referenciu.
///
/// ROZDIEL OPROTI StatusStationMenuUI:
///   StatusStationMenuUI je viazané na konkrétnu entitu (StationInstance) a
///   formátuje zoznam jej tovární. StatusErrorMenuUI je viazané len na
///   jednoduchý reťazec (text chyby) – nepotrebuje žiadny dátový zdroj ani
///   register, lebo text mu dodá priamo volajúci z miesta chyby.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusErrorMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusErrorMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusErrorMenuUIPanel;

    [Header("Error Info Caption")]
    [Tooltip("ErrorTextCaption – jediný UI Text, do ktorého sa vypíše " +
             "text chyby odovzdaný cez OpenWithError(...).")]
    [SerializeField] private TMP_Text errorTextCaption;

    [Header("Auto-Close")]
    [Tooltip("Počet sekúnd, po ktorých sa okno automaticky zavrie. " +
             "Hodnota 0 alebo menšia auto-close úplne vypne (okno potom " +
             "ostane otvorené, kým ho hráč nezavrie cez Close (X)).")]
    [SerializeField] private float autoCloseDelay = 3f;

    // Text v caption, ak by OpenWithError bolo zavolané bez konkrétnej hlášky
    // (null/prázdny string), prípadne ak by sa okno otvorilo cez ToggleWindow
    // bez predchádzajúceho OpenWithError. Defenzívny fallback.
    private const string DEFAULT_ERROR_LABEL = "Unknown error.";

    // =====================================================================
    // STAV
    // =====================================================================

    /// <summary>
    /// Aktuálne zobrazená chybová hláška (analógia k currentStation v
    /// StatusStationMenuUI). Nastaví sa v OpenWithError a vynuluje v
    /// CloseWindow – pri ďalšom otvorení musí volajúci znova určiť text.
    /// </summary>
    private string currentMessage;

    /// <summary>
    /// Bežiaca auto-close coroutine (odpočet do automatického zatvorenia).
    /// Držíme si na ňu referenciu, aby sa dala kedykoľvek zrušiť – napr. keď
    /// hráč zavrie okno skôr cez Close (X), alebo keď v priebehu odpočtu
    /// nastane ĎALŠIA chyba a okno sa otvorí nanovo (vtedy sa odpočet
    /// reštartuje od nuly, aby hráč mal na prečítanie novej hlášky opäť
    /// celé 3 sekundy).
    /// </summary>
    private Coroutine autoCloseRoutine;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusStationMenuUI / StatusVehiclesMenuUI /
        // StatusFactoryMenuUI): na rovnakom GameObjecte (root paneli) je
        // pripojený aj sprievodný script StatusErrorUIwindow, ktorý si v
        // svojom Awake() registruje listener na Close (X) tlačidlo. Poradie
        // volania Awake() medzi viacerými skriptami na tom istom GameObjecte
        // je v Unity nedeterministické (Script Execution Order). Ak by tento
        // Awake() zbehol PRV a hneď panel deaktivoval, druhý script by svoj
        // Awake() už nemusel stihnúť – Unity nevolá Awake() na neaktívnych
        // GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start().
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusErrorUIwindow už zbehol. Okno sa otvorí až keď
        // v hre nastane chyba a niekto zavolá OpenWithError(...).
        if (statusErrorMenuUIPanel != null)
            statusErrorMenuUIPanel.SetActive(false);
    }

    /// <summary>
    /// Zaručí, že okno chyby sa vykreslí NAD všetkými ostatnými UI oknami hry.
    ///  1. transform.SetAsLastSibling() – posunie panel na koniec Hierarchy
    ///     v rámci rodičovského Canvasu → v uGUI sa posledné dieťa kreslí navrchu.
    ///     Toto rieši prípad, keď Error okno zdieľa Canvas so Station/Vehicles/
    ///     Factory oknami.
    ///  2. Ak má panel vlastný Canvas komponent (napr. je to samostatný,
    ///     "overlay" Canvas), navyše mu nastavíme čo najvyšší sortingOrder,
    ///     aby vyhral aj voči iným Canvasom v scéne.
    /// </summary>
    private void BringToFront()
    {
        statusErrorMenuUIPanel.transform.SetAsLastSibling();

        Canvas ownCanvas = statusErrorMenuUIPanel.GetComponent<Canvas>();
        if (ownCanvas != null)
            ownCanvas.sortingOrder = 32000; // dostatočne vysoko nad ostatné okná
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Otvorí okno a zobrazí v ňom odovzdanú chybovú hlášku.
    /// Toto je HLAVNÝ vstupný bod – volá sa z miesta, kde v hre nastala
    /// chyba (napr. GameManager pri pokuse postaviť na obsadený tile).
    ///
    /// Jedno a to isté okno obslúži ľubovoľný typ chyby – mení sa iba text.
    /// Kanonické znenia hľadaj v GameErrors.cs.
    ///
    /// Ak je message null/prázdny, zobrazí sa DEFAULT_ERROR_LABEL (defenzívne).
    ///
    /// Po otvorení sa spustí odpočet auto-close (autoCloseDelay sekúnd), po
    /// ktorom sa okno zavrie samo – rovnakou cestou ako Close (X) tlačidlo.
    /// </summary>
    public void OpenWithError(string message)
    {
        currentMessage = string.IsNullOrEmpty(message) ? DEFAULT_ERROR_LABEL : message;

        if (statusErrorMenuUIPanel != null)
        {
            statusErrorMenuUIPanel.SetActive(true);
            BringToFront();
        }

        RefreshErrorText();

        // Odpočet spúšťame AŽ TERAZ – po SetActive(true). Coroutine sa nedá
        // spustiť na neaktívnom GameObjecte (tento script sedí na root paneli).
        RestartAutoCloseTimer();
    }

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Užitočné, ak by sa
    /// niekedy v budúcnosti pridalo toggle tlačidlo na ovládacom paneli;
    /// chyby sa primárne zobrazujú cez OpenWithError.
    ///
    /// Pri otvorení vyžaduje, aby už bola nastavená currentMessage – inak
    /// caption zobrazí DEFAULT_ERROR_LABEL (nemáme čo zobraziť).
    /// </summary>
    public void ToggleWindow()
    {
        if (statusErrorMenuUIPanel == null) return;

        bool newState = !statusErrorMenuUIPanel.activeSelf;
        statusErrorMenuUIPanel.SetActive(newState);

        if (newState)
        {
            RefreshErrorText();
            RestartAutoCloseTimer();   // aj takto otvorené okno sa zavrie samo
        }
        else
        {
            StopAutoCloseTimer();
        }
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusErrorUIwindow pri stlačení
    /// Close (X), volá sa aj z auto-close odpočtu, môže sa volať aj zvonku.
    ///
    /// Pri zatvorení vynulujeme aj currentMessage – okno už nezobrazuje
    /// žiadnu chybu, takže prípadné neskoršie ToggleWindow by len zobrazil
    /// default. Pri ďalšej chybe volajúci znova použije OpenWithError, čím sa
    /// currentMessage korektne nastaví.
    /// </summary>
    public void CloseWindow()
    {
        // Zrušíme prípadný bežiaci odpočet – ak hráč zavrel okno skôr (Close X),
        // nesmie neskôr "doznieť" starý timer a zavrieť okno, ktoré medzitým
        // mohla otvoriť nová chyba.
        StopAutoCloseTimer();

        if (statusErrorMenuUIPanel != null)
            statusErrorMenuUIPanel.SetActive(false);

        currentMessage = null;
    }

    // =====================================================================
    // AUTO-CLOSE (automatické zatvorenie po autoCloseDelay sekundách)
    // =====================================================================

    /// <summary>
    /// (Re)štartuje odpočet do automatického zatvorenia okna. Ak už nejaký
    /// odpočet bežal, zruší sa a začne odznova – vďaka tomu má hráč pri
    /// KAŽDEJ novej hláške plné 3 sekundy na prečítanie.
    /// </summary>
    private void RestartAutoCloseTimer()
    {
        StopAutoCloseTimer();

        // autoCloseDelay <= 0 → auto-close vypnutý (okno zatvorí len hráč).
        if (autoCloseDelay <= 0f) return;

        // Coroutine sa dá spustiť len na aktívnom a zapnutom komponente.
        if (!isActiveAndEnabled) return;

        autoCloseRoutine = StartCoroutine(AutoCloseRoutine());
    }

    /// <summary>
    /// Zruší bežiaci odpočet (ak nejaký je). Idempotentné – volanie navyše
    /// neškodí.
    /// </summary>
    private void StopAutoCloseTimer()
    {
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }
    }

    /// <summary>
    /// Samotný odpočet. Používa WaitForSecondsRealtime, aby okno zmizlo po
    /// reálnych 3 sekundách aj vtedy, ak by bola hra pozastavená alebo
    /// spomalená cez Time.timeScale.
    /// </summary>
    private IEnumerator AutoCloseRoutine()
    {
        yield return new WaitForSecondsRealtime(autoCloseDelay);

        // Vynulujeme referenciu ešte pred CloseWindow(), aby StopAutoCloseTimer()
        // vnútri CloseWindow() nevolal StopCoroutine na práve dobiehajúcu
        // coroutine (bolo by to neškodné, ale takto je to čisté).
        autoCloseRoutine = null;

        CloseWindow();
    }

    // =====================================================================
    // NAPLNENIE CAPTIONU
    // =====================================================================

    /// <summary>
    /// Prepíše ErrorTextCaption aktuálnou hláškou (currentMessage). Ak hláška
    /// nie je nastavená, zobrazí sa DEFAULT_ERROR_LABEL.
    /// </summary>
    private void RefreshErrorText()
    {
        if (errorTextCaption == null) return;

        errorTextCaption.text = string.IsNullOrEmpty(currentMessage)
            ? DEFAULT_ERROR_LABEL
            : currentMessage;
    }
}
