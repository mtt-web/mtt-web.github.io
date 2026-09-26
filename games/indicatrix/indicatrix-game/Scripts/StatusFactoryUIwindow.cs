using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusFactoryUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusFactoryMenuUI (root paneli
/// okna). Analógia k DepotRailConstructionUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "RCCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa cez StatusFactoryMenuUI
///      vyčistí aktuálne vybraná továreň (currentFactory → null), aby okno
///      pri ďalšom otvorení neukazovalo staré dáta.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// ROZDIEL OPROTI DepotRailConstructionUIwindow:
///   Status okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim
///   (SnapLineFace / SnapAreaFace), takže pri zatváraní NIE JE čo rušiť cez
///   GameManager.SetTerrainMode(None). Klik na továreň prebieha mimo
///   akéhokoľvek režimu (CurrentFactoryConstructionMode == None). Zatvorenie
///   preto len skryje panel a zruší výber továrne.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject ("FactoryConstructionMenuUI - Title") a drag-uje root
/// panel. Týmto sa dosahuje správanie "okno sa ťahá len za titulok".
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusFactoryUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"RCCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel a zruší výber továrne.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusFactoryMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusFactoryMenuUI statusFactoryMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť
        // na rovnakom GameObjecte – StatusFactoryMenuUI aj StatusFactoryUIwindow
        // sedia spolu na root paneli okna (rovnaký vzor ako Depot dvojica).
        if (statusFactoryMenuUI == null)
            statusFactoryMenuUI = GetComponent<StatusFactoryMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusFactoryMenuUI.CloseWindow() – tým sa
        // okrem skrytia panelu zruší aj výber továrne (currentFactory = null).
        if (statusFactoryMenuUI != null)
        {
            statusFactoryMenuUI.CloseWindow();
            return;
        }

        // Fallback: ak referencia na menu chýba, aspoň skry panel.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez externý script, deaktiváciu rodiča, ...), zrušíme výber továrne,
    /// aby okno pri ďalšom otvorení nezobrazovalo staré dáta.
    ///
    /// POZN.: Voláme CloseWindow() len ako "vyčistenie stavu" – opätovné
    /// SetActive(false) na už neaktívnom paneli je idempotentné a neškodí.
    /// </summary>
    void OnDisable()
    {
        if (statusFactoryMenuUI != null)
            statusFactoryMenuUI.CloseWindow();
    }
}
