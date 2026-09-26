using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IntroWindowUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli IntroWindowMenuUI (root paneli
/// okna). Analógia k StatusBudgetUIwindow / StatusErrorUIwindow /
/// StatusStationUIwindow / StatusVehiclesUIwindow / StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "IWCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri akomkoľvek
///      skrytí panelu (OnDisable), sa cez IntroWindowMenuUI panel skryje.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje automatické
///      vycentrovanie pri prvom otvorení a verejné, z kódu volateľné metódy
///      SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// ROZDIEL OPROTI StatusBudgetUIwindow:
///   Funkčne IDENTICKÝ – mení sa len referencovaný menu komponent
///   (IntroWindowMenuUI namiesto StatusBudgetMenuUI) a názov Close tlačidla
///   v Hierarchy (IWCloseWindowButton). Okno NIE JE viazané na žiadnu entitu a
///   ani nedrží žiadny text výkazu – je čisto informačné, takže pri zatváraní
///   nie je čo nulovať (IntroWindowMenuUI.CloseWindow() len skryje panel).
///
/// ROZDIEL OPROTI construction oknám:
///   Intro okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim,
///   takže pri zatváraní NIE JE čo rušiť cez GameManager.SetTerrainMode(None).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v spoločnom skripte WindowDragHandle, ktorý je pripojený na
/// Title GameObject (v Hierarchy "IntroWindowMenuUI - Title") a drag-uje root
/// panel – rovnako ako pri ostatných oknách.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class IntroWindowUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"IWCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("IntroWindowMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private IntroWindowMenuUI introWindowMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť na
        // rovnakom GameObjecte – IntroWindowMenuUI aj IntroWindowUIwindow sedia
        // spolu na root paneli okna (rovnaký vzor ako Budget / Error / Station
        // / Vehicles / Factory dvojica).
        if (introWindowMenuUI == null)
            introWindowMenuUI = GetComponent<IntroWindowMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez IntroWindowMenuUI.CloseWindow().
        if (introWindowMenuUI != null)
        {
            introWindowMenuUI.CloseWindow();
            return;
        }

        // Fallback: ak referencia na menu chýba, aspoň skry panel.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez externý script, deaktiváciu rodiča, ...), zavoláme CloseWindow().
    ///
    /// POZN.: Opätovné SetActive(false) na už neaktívnom paneli je idempotentné
    /// a neškodí.
    /// </summary>
    void OnDisable()
    {
        if (introWindowMenuUI != null)
            introWindowMenuUI.CloseWindow();
    }
}
