using UnityEngine;
using TMPro;

/// <summary>
/// StatusBudgetMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UNIVERZÁLNE UI okno pre zobrazenie ROČNEJ ÚČTOVNEJ UZÁVIERKY (rozpočtu) hráčovi.
///
/// Návrhovo je IDENTICKÉ s dvojicou StatusErrorMenuUI + StatusErrorUIwindow
/// (a analogicky k StatusStationMenuUI / StatusVehiclesMenuUI / StatusFactoryMenuUI).
/// Líši sa len tým, že namiesto textu chyby si pamätá text VÝKAZU (rozpočtu),
/// ktorý sa vypíše do "BudgetTextCaption".
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo (SBCloseWindowButton) –
///     túto obsluhu rieši sprievodný script StatusBudgetUIwindow (analogicky
///     k dvojici StatusErrorMenuUI + StatusErrorUIwindow).
///  1. Okno NEOTVÁRA žiadny tile/klik – otvára sa PROGRAMOVO na začiatku každého
///     herného roka. Spúšťačom je BudgetSystem.CloseYearAndShow(...), ktorý
///     zostaví textový výkaz a zavolá OpenWithReport("..."). Mimo toho je okno
///     skryté.
///  2. Po otvorení sa do jediného UI Textu "BudgetTextCaption" vypíše odovzdaný
///     výkaz (rich text – kladné sumy zelené, záporné červené). Ak by výkaz
///     chýbal (null/prázdny), zobrazí sa DEFAULT_BUDGET_LABEL.
///
/// PREČO JEDNO OKNO:
///   Rovnako ako pri chybovom okne – aby sme neduplikovali UI. Toto JEDNO okno
///   zobrazí uzávierku ľubovoľného roka, mení sa iba text. Text dodá priamo
///   volajúci (BudgetSystem), takže okno samo o ekonomike nič nevie.
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje jeden meniteľný textový prvok – BudgetTextCaption
///   (v Hierarchy "BudgetTextCaption", child "StatusBudgetMenuUI - Content").
///   Aby sa farby (rich text <color=...>) prejavili, musí mať TMP_Text
///   zapnuté "Rich Text" (predvolene true).
///
/// ROZDIEL OPROTI StatusErrorMenuUI:
///   Funkčne IDENTICKÉ – mení sa len názov caption-u (BudgetTextCaption),
///   názov panela a metóda OpenWithReport namiesto OpenWithError. Obe okná sú
///   viazané len na jednoduchý reťazec, ktorý im dodá volajúci z miesta, kde
///   vznikol (StatusErrorMenuUI = miesto chyby, StatusBudgetMenuUI = uzávierka
///   roka v BudgetSystem).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusBudgetMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusBudgetMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusBudgetMenuUIPanel;

    [Header("Budget Info Caption")]
    [Tooltip("BudgetTextCaption – jediný UI Text, do ktorého sa vypíše " +
             "výkaz rozpočtu odovzdaný cez OpenWithReport(...).")]
    [SerializeField] private TMP_Text budgetTextCaption;

    // Text v caption, ak by OpenWithReport bolo zavolané bez výkazu
    // (null/prázdny string), prípadne ak by sa okno otvorilo cez ToggleWindow
    // bez predchádzajúceho OpenWithReport. Defenzívny fallback.
    private const string DEFAULT_BUDGET_LABEL = "Žiadne dáta rozpočtu.";

    // =====================================================================
    // STAV
    // =====================================================================

    /// <summary>
    /// Aktuálne zobrazený výkaz (analógia k currentMessage v StatusErrorMenuUI).
    /// Nastaví sa v OpenWithReport a vynuluje v CloseWindow – pri ďalšom otvorení
    /// musí volajúci znova určiť text.
    /// </summary>
    private string currentReport;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod je rovnaký ako pri StatusErrorMenuUI: na rovnakom GameObjecte
        // (root paneli) je pripojený aj sprievodný script StatusBudgetUIwindow,
        // ktorý si v svojom Awake() registruje listener na Close (X) tlačidlo.
        // Poradie Awake() medzi viacerými skriptami na tom istom GameObjecte je
        // v Unity nedeterministické. Ak by tento Awake() zbehol prv a hneď panel
        // deaktivoval, druhý script by svoj Awake() už nemusel stihnúť (Unity
        // nevolá Awake() na neaktívnych GameObjectoch) a Close by nefungoval.
        //
        // Riešenie: panel skryjeme až v Start().
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusBudgetUIwindow už zbehol. Okno sa otvorí až na
        // začiatku nového herného roka, keď BudgetSystem zavolá OpenWithReport(...).
        if (statusBudgetMenuUIPanel != null)
            statusBudgetMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Otvorí okno a zobrazí v ňom odovzdaný výkaz rozpočtu.
    /// Toto je HLAVNÝ vstupný bod – volá ho BudgetSystem na začiatku nového
    /// herného roka (CloseYearAndShow).
    ///
    /// Jedno a to isté okno obslúži uzávierku ľubovoľného roka – mení sa iba text.
    /// Ak je report null/prázdny, zobrazí sa DEFAULT_BUDGET_LABEL (defenzívne).
    /// </summary>
    public void OpenWithReport(string report)
    {
        currentReport = string.IsNullOrEmpty(report) ? DEFAULT_BUDGET_LABEL : report;

        if (statusBudgetMenuUIPanel != null)
            statusBudgetMenuUIPanel.SetActive(true);

        RefreshBudgetText();
    }

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Užitočné, ak by sa
    /// niekedy pridalo toggle tlačidlo; uzávierka sa primárne zobrazuje cez
    /// OpenWithReport.
    ///
    /// Pri otvorení vyžaduje, aby už bola nastavená currentReport – inak
    /// caption zobrazí DEFAULT_BUDGET_LABEL.
    /// </summary>
    public void ToggleWindow()
    {
        if (statusBudgetMenuUIPanel == null) return;

        bool newState = !statusBudgetMenuUIPanel.activeSelf;
        statusBudgetMenuUIPanel.SetActive(newState);

        if (newState)
            RefreshBudgetText();
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusBudgetUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    ///
    /// Pri zatvorení vynulujeme aj currentReport – okno už nezobrazuje žiadny
    /// výkaz. Pri ďalšej uzávierke volajúci znova použije OpenWithReport.
    /// </summary>
    public void CloseWindow()
    {
        if (statusBudgetMenuUIPanel != null)
            statusBudgetMenuUIPanel.SetActive(false);

        currentReport = null;
    }

    // =====================================================================
    // NAPLNENIE CAPTIONU
    // =====================================================================

    /// <summary>
    /// Prepíše BudgetTextCaption aktuálnym výkazom (currentReport). Ak výkaz nie
    /// je nastavený, zobrazí sa DEFAULT_BUDGET_LABEL.
    /// </summary>
    private void RefreshBudgetText()
    {
        if (budgetTextCaption == null) return;

        budgetTextCaption.text = string.IsNullOrEmpty(currentReport)
            ? DEFAULT_BUDGET_LABEL
            : currentReport;
    }
}
