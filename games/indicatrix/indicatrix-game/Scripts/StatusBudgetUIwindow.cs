using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusBudgetUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusBudgetMenuUI (root paneli
/// okna). Analógia k StatusErrorUIwindow / StatusStationUIwindow /
/// StatusVehiclesUIwindow / StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "SBCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri akomkoľvek
///      skrytí panelu (OnDisable), sa cez StatusBudgetMenuUI panel skryje a
///      vynuluje currentReport.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje automatické
///      vycentrovanie pri prvom otvorení a verejné, z kódu volateľné metódy
///      SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// ROZDIEL OPROTI StatusErrorUIwindow:
///   Funkčne IDENTICKÝ – mení sa len referencovaný menu komponent
///   (StatusBudgetMenuUI namiesto StatusErrorMenuUI) a názov Close tlačidla
///   v Hierarchy (SBCloseWindowButton). Okno NIE JE viazané na žiadnu entitu –
///   drží len text výkazu, ktorý si vyčistí samotné
///   StatusBudgetMenuUI.CloseWindow() (vynulovanie currentReport), nie tento
///   window komponent.
///
/// ROZDIEL OPROTI construction oknám:
///   Status okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim,
///   takže pri zatváraní NIE JE čo rušiť cez GameManager.SetTerrainMode(None).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject (titulok okna) a drag-uje root panel.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusBudgetUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"SBCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusBudgetMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusBudgetMenuUI statusBudgetMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť na
        // rovnakom GameObjecte – StatusBudgetMenuUI aj StatusBudgetUIwindow sedia
        // spolu na root paneli okna (rovnaký vzor ako Error / Station / Vehicles
        // / Factory dvojica).
        if (statusBudgetMenuUI == null)
            statusBudgetMenuUI = GetComponent<StatusBudgetMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusBudgetMenuUI.CloseWindow() –
        // tým sa vynuluje aj currentReport.
        if (statusBudgetMenuUI != null)
        {
            statusBudgetMenuUI.CloseWindow();
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
    /// a neškodí. CloseWindow() pri tom vynuluje currentReport, takže prípadné
    /// neskoršie ToggleWindow nezobrazí "starý" výkaz.
    /// </summary>
    void OnDisable()
    {
        if (statusBudgetMenuUI != null)
            statusBudgetMenuUI.CloseWindow();
    }
}
