using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StatusMapUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli StatusMapMenuUI (root paneli
/// okna). Analógia k StatusFactoryUIwindow.
///
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla okna (v Hierarchy "RCCloseWindowButton").
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa cez StatusMapMenuUI
///      okno korektne zatvorí.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri prvom otvorení a verejné, z kódu
///      volateľné metódy SetWindowPosition(...). Žiadna Inspector
///      referencia netreba.
///
/// CHARAKTER OKNA:
///   Status Map okno je čisto INFORMAČNÉ – nespúšťa žiadny konštrukčný režim
///   (SnapLineFace / SnapAreaFace), takže pri zatváraní NIE JE čo rušiť cez
///   GameManager.SetTerrainMode(None). Zatvorenie len skryje panel.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je pripojený
/// na Title GameObject ("StatusMapMenuUI - Title") a drag-uje root panel.
/// Týmto sa dosahuje správanie "okno sa ťahá len za titulok".
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusMapUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Tlačidlo X na zatvorenie okna (v Hierarchy \"RCCloseWindowButton\"). " +
             "Ak je priradené, zavretie skryje panel okna.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    [Header("Owning Menu")]
    [Tooltip("StatusMapMenuUI komponent tohto okna. Ak nie je priradený, " +
             "skript ho skúsi nájsť na tomto GameObjecte (GetComponent).")]
    [SerializeField] private StatusMapMenuUI statusMapMenuUI;

    void Awake()
    {
        // Ak referencia na menu nie je priradená v Inspectore, skús ju nájsť
        // na rovnakom GameObjecte – StatusMapMenuUI aj StatusMapUIwindow
        // sedia spolu na root paneli okna (rovnaký vzor ako Factory dvojica).
        if (statusMapMenuUI == null)
            statusMapMenuUI = GetComponent<StatusMapMenuUI>();

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Preferovane zatvor cez StatusMapMenuUI.CloseWindow().
        if (statusMapMenuUI != null)
        {
            statusMapMenuUI.CloseWindow();
            return;
        }

        // Fallback: ak referencia na menu chýba, aspoň skry panel.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez externý script, deaktiváciu rodiča, ...), zavoláme CloseWindow(),
    /// aby stav okna ostal konzistentný.
    ///
    /// POZN.: Voláme CloseWindow() len ako "vyčistenie stavu" – opätovné
    /// SetActive(false) na už neaktívnom paneli je idempotentné a neškodí.
    /// </summary>
    void OnDisable()
    {
        if (statusMapMenuUI != null)
            statusMapMenuUI.CloseWindow();
    }
}
