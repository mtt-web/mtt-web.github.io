using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SettingsUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionUIwindow / RoadConstructionUIwindow. Komponent
/// umiestnený na samotnom paneli SettingsMenuUIPanel. Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla (SCloseWindowButton).
///   2. Bezpečné zatvorenie okna – skrytie panelu pri kliknutí na Close.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// Na rozdiel od FactoryConstructionUIwindow tu NIE JE potrebné pri zatvorení
/// rušiť žiadny konštrukčný mód cez GameManager (SettingsMenu nie je naviazané
/// na CurrentFactoryConstructionMode/RoadConstructionMode/RailConstructionMode),
/// preto tu chýba ekvivalent GameManager.instance.SetTerrainMode(...) v OnDisable.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag je
/// implementovaný v samostatnom skripte WindowDragHandle, ktorý je
/// pripojený na Title GameObject ("SettingsMenuUI - Title") a drag-uje
/// root panel. Týmto sa dosahuje správanie "okno sa ťahá len za titulok",
/// štandardné pre desktopové UI (rovnako ako pri ostatných oknách).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SettingsUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
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
        // Na rozdiel od Factory/Road/Rail okien tu nie je žiadny konštrukčný
        // mód, ktorý by bolo treba resetovať – stačí panel skryť.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }
}
