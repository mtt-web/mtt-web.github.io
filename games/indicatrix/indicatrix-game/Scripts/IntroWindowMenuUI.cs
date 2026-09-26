using UnityEngine;

/// <summary>
/// IntroWindowMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UNIVERZÁLNE UI okno pre ÚVODNÉ / INFORMAČNÉ okno hry (IntroWindowMenuUI).
///
/// Návrhovo je IDENTICKÉ s dvojicou StatusBudgetMenuUI + StatusBudgetUIwindow
/// (a analogicky k StatusErrorMenuUI / StatusStationMenuUI / StatusVehiclesMenuUI
/// / StatusFactoryMenuUI). Líši sa v DVOCH veciach:
///
///   1. Je ČISTO INFORMAČNÉ – nedrží si žiadny meniteľný text ani entitu.
///      Obsah (v Hierarchy "IntroWindowImage", child "IntroWindowMenuUI -
///      Content") je STATICKÝ a nastavený priamo v editore, takže okno nemá
///      žiadny ekvivalent BudgetTextCaption / OpenWithReport(...). Riadi len
///      viditeľnosť svojho root panelu.
///
///   2. Otvára sa AUTOMATICKY pri spustení hry. Toto je PRESNÝ OPAK budget /
///      error okna, ktoré sa v Start() SKRÝVAJÚ. IntroWindowMenuUI sa v Start()
///      naopak OTVORÍ. Všetky ostatné okná zostávajú pri štarte zavreté.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo (v Hierarchy
///     "IWCloseWindowButton") – túto obsluhu rieši sprievodný script
///     IntroWindowUIwindow (analogicky k dvojici StatusBudgetMenuUI +
///     StatusBudgetUIwindow).
///  1. Okno sa NEOTVÁRA žiadnym tile/klikom – otvorí sa PROGRAMOVO raz, pri
///     spustení hry (v Start() cez OpenWindow()). Po zavretí ho hráč už
///     opätovne neotvára (môže sa však znova otvoriť cez OpenWindow /
///     ToggleWindow, ak by sa to niekedy hodilo – napr. tlačidlo "Pomoc").
///
/// PREČO JEDNO OKNO:
///   Rovnako ako pri ostatných status oknách – jeden GameObject, jeden script.
///   Tu navyše okno nedrží žiadny stav, takže je ešte jednoduchšie.
///
/// DÔLEŽITÉ – NASTAVENIE V SCÉNE:
///   Aby auto-open v Start() fungoval, musí byť root panel okna
///   ("IntroWindowMenuUIPanel - Window") v scéne PONECHANÝ AKTÍVNY (zaškrtnutý),
///   úplne rovnako, ako sú aktívne aj ostatné okná (tie sa následne v Start()
///   samy skryjú). Unity volá Awake()/Start() len na aktívnych GameObjectoch –
///   ak by bol panel v scéne neaktívny, nezbehol by ani Awake() sprievodného
///   IntroWindowUIwindow (a teda by sa nezaregistroval Close button), ani tento
///   Start() (a okno by sa neotvorilo).
///
/// ROZDIEL OPROTI StatusBudgetMenuUI:
///   - Bez currentReport / OpenWithReport / RefreshBudgetText (žiadny text).
///   - Start() namiesto SetActive(false) volá OpenWindow() (auto-open).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class IntroWindowMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (IntroWindowMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject introWindowMenuUIPanel;

    // POZN.: Žiadna referencia na obsah nie je potrebná – obsah okna
    // (IntroWindowImage) je statický a nastavený v editore. Okno riadi len
    // viditeľnosť svojho root panelu.

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: stav panelu (otvorené/zatvorené) NENASTAVUJEME tu v Awake().
        //
        // Dôvod je rovnaký ako pri StatusBudgetMenuUI: na rovnakom GameObjecte
        // (root paneli) je pripojený aj sprievodný script IntroWindowUIwindow,
        // ktorý si v svojom Awake() registruje listener na Close (X) tlačidlo.
        // Poradie Awake() medzi viacerými skriptami na tom istom GameObjecte je
        // v Unity nedeterministické – preto akúkoľvek zmenu aktivity panelu
        // odkladáme až do Start(), keď máme istotu, že Awake() na sprievodnom
        // skripte už zbehol.
    }

    void Start()
    {
        // Na rozdiel od ostatných okien (ktoré sa tu SKRÝVAJÚ) sa úvodné okno
        // pri spustení hry naopak OTVORÍ. Po Start() máme istotu, že Awake() na
        // sprievodnom IntroWindowUIwindow už zbehol (Close button je zapojený).
        OpenWindow();
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Otvorí (zobrazí) úvodné okno. Volá ho Start() pri spustení hry; dá sa
    /// volať aj zvonku (napr. z prípadného tlačidla "Pomoc" / "O hre").
    /// </summary>
    public void OpenWindow()
    {
        if (introWindowMenuUIPanel != null)
            introWindowMenuUIPanel.SetActive(true);
    }

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Užitočné, ak by sa
    /// niekedy pridalo toggle tlačidlo.
    /// </summary>
    public void ToggleWindow()
    {
        if (introWindowMenuUIPanel == null) return;

        introWindowMenuUIPanel.SetActive(!introWindowMenuUIPanel.activeSelf);
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný IntroWindowUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    ///
    /// Okno nedrží žiadny stav, takže pri zatvorení nie je čo nulovať – stačí
    /// deaktivovať panel.
    /// </summary>
    public void CloseWindow()
    {
        if (introWindowMenuUIPanel != null)
            introWindowMenuUIPanel.SetActive(false);
    }
}
