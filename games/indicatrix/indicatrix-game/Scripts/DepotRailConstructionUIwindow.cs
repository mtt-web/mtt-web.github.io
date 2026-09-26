using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DepotRailConstructionUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Komponent umiestnený na samotnom paneli DepotRailConstructionMenuUI.
/// Zabezpečuje:
///   1. Obsluhu voliteľného Close (X) tlačidla.
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close, alebo pri
///      akomkoľvek skrytí panelu (OnDisable), sa zruší aktuálny konštrukčný
///      režim cez GameManager.SetTerrainMode(None), aby ne-zostal "zaseknutý"
///      SnapLineFace/SnapVertex indikátor na mape.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je
/// pripojený na Title GameObject a drag-uje root panel. Týmto sa dosahuje
/// správanie "okno sa ťahá len za titulok", štandardné pre desktopové UI.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class DepotRailConstructionUIwindow : UIWindowBase
{
    [Header("Optional Close (X) Button")]
    [Tooltip("Voliteľné tlačidlo X na zatvorenie okna. Ak je priradené, " +
             "zavretie zruší aj aktuálny konštrukčný režim.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject.")]
    [SerializeField] private GameObject panelToHide;

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Vyvolaj rovnakú akciu ako klávesa ESC: zruší aktuálny režim
        // (vrátane Define Route) A SCHOVÁ snap visuals – inak by indikátor
        // ostal "zaseknutý" na tile mape až do stlačenia ESC.
        if (GameManager.instance != null)
            GameManager.instance.PerformEscapeReset();

        // Skryť panel
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }

    /// <summary>
    /// Bezpečnostná poistka: ak sa panel skryje akýmkoľvek spôsobom
    /// (cez GameMenuUI toggle, externý script, deaktiváciu rodiča, ...),
    /// zrušíme aktuálny konštrukčný režim. SetTerrainMode(None) je
    /// idempotentné – ak režim už bol None, nič sa nestane.
    /// </summary>
    void OnDisable()
    {
        // GameManager.instance môže byť null pri ukončovaní hry / zmene scény
        if (GameManager.instance != null)
            GameManager.instance.SetTerrainMode(GameManager.RailConstructionMode.None);
    }
}
