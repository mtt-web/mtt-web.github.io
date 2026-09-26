using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusErrorUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusErrorMenuUI (root paneli
/// okna). Analógia k StatusStationUIwindow / StatusVehiclesUIwindow /
/// StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "SECloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa cez StatusErrorMenuUI
///      panel skryje a vynuluje currentMessage.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri prvom otvorení a verejné, z kódu
///      volateľné metódy SetWindowPosition(...). Žiadna Inspector
///      referencia netreba.
///
/// ROZDIEL OPROTI StatusStationUIwindow:
///   Funkčne IDENTICKÝ – mení sa len referencovaný menu komponent
///   (StatusErrorMenuUI namiesto StatusStationMenuUI) a názov Close tlačidla
///   v Hierarchy (SECloseWindowButton). Status error okno NIE JE viazané na
///   žiadnu entitu – drží len text chyby, ktorý si vyčistí samotné
///   StatusErrorMenuUI.CloseWindow() (vynulovanie currentMessage), nie tento
///   window komponent.
///
/// ROZDIEL OPROTI construction oknám:
///   Status okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim,
///   takže pri zatváraní NIE JE čo rušiť cez GameManager.SetTerrainMode(None).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject (titulok okna) a drag-uje root panel. Týmto sa
/// dosahuje správanie "okno sa ťahá len za titulok".
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusErrorUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"SECloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusErrorMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusErrorMenuUI statusErrorMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť
        // na rovnakom GameObjecte – StatusErrorMenuUI aj StatusErrorUIwindow
        // sedia spolu na root paneli okna (rovnaký vzor ako Station / Vehicles
        // / Factory dvojica).
        if (statusErrorMenuUI == null)
            statusErrorMenuUI = GetComponent<StatusErrorMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusErrorMenuUI.CloseWindow() –
        // tým sa vynuluje aj currentMessage.
        if (statusErrorMenuUI != null)
        {
            statusErrorMenuUI.CloseWindow();
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
    /// POZN.: Opätovné SetActive(false) na už neaktívnom paneli je
    /// idempotentné a neškodí. CloseWindow() pri tom vynuluje currentMessage,
    /// takže prípadné neskoršie ToggleWindow nezobrazí "starú" chybu.
    /// </summary>
    void OnDisable()
    {
        if (statusErrorMenuUI != null)
            statusErrorMenuUI.CloseWindow();
    }
}
