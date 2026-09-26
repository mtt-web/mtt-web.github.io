using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusStationUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusStationMenuUI (root paneli
/// okna). Analógia k StatusVehiclesUIwindow / StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "RCCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa cez StatusStationMenuUI
///      panel skryje a vynuluje currentStation.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri prvom otvorení a verejné, z kódu
///      volateľné metódy SetWindowPosition(...). Žiadna Inspector
///      referencia netreba.
///
/// ROZDIEL OPROTI StatusVehiclesUIwindow:
///   Funkčne IDENTICKÝ – mení sa len referencovaný menu komponent
///   (StatusStationMenuUI namiesto StatusVehiclesMenuUI). Status station
///   okno na rozdiel od status vehicles okna VIE byť viazané na konkrétnu
///   entitu (StationInstance), ale to čistenie obsluhuje samotné
///   StatusStationMenuUI.CloseWindow() (vynulovanie currentStation), nie
///   tento window komponent.
///
/// ROZDIEL OPROTI construction oknám:
///   Status okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim,
///   takže pri zatváraní NIE JE čo rušiť cez GameManager.SetTerrainMode(None).
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject ("StatusStationMenuUI - Title") a drag-uje root
/// panel. Týmto sa dosahuje správanie "okno sa ťahá len za titulok".
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusStationUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"RCCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusStationMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusStationMenuUI statusStationMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť
        // na rovnakom GameObjecte – StatusStationMenuUI aj StatusStationUIwindow
        // sedia spolu na root paneli okna (rovnaký vzor ako Vehicles / Factory dvojica).
        if (statusStationMenuUI == null)
            statusStationMenuUI = GetComponent<StatusStationMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusStationMenuUI.CloseWindow() –
        // tým sa vynuluje aj currentStation.
        if (statusStationMenuUI != null)
        {
            statusStationMenuUI.CloseWindow();
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
    /// idempotentné a neškodí. CloseWindow() pri tom vynuluje currentStation,
    /// takže prípadné neskoršie ToggleWindow nezobrazí "starú" stanicu.
    /// </summary>
    void OnDisable()
    {
        if (statusStationMenuUI != null)
            statusStationMenuUI.CloseWindow();
    }
}
