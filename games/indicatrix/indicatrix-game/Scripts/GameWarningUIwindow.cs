using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameWarningUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k InGameMenuUIwindow. Komponent umiestnený na samotnom root
/// paneli "GameWarningUIPanel - Window" (univerzálne varovné okno No / Yes).
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla "GWCloseWindowButton" v titulku.
///   2. Zatvorenie okna – pri kliknutí na Close sa panel skryje. Správa sa
///      rovnako ako GWNoGameButton: potvrdzovaná akcia sa NEvykoná
///      (GameWarningMenuUI ju pri skrytí okna zahodí v OnDisable).
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// ROZDIEL OPROTI Construction oknám:
///   Varovné okno NESPÚŠŤA žiadny construction / terrain režim. Preto tu
///   zámerne NEVOLÁME GameManager.PerformEscapeReset() ani SetTerrainMode(...)
///   – rovnako ako InGameMenuUIwindow.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag rieši
/// samostatný WindowDragHandle pripojený na Title GameObject
/// ("GameWarningUI - Title"), ktorý drag-uje root panel "GameWarningUIPanel - Window".
///
/// POZN.: Obsahové tlačidlá (GWNoGameButton / GWYesGameButton) a text otázky
/// rieši samostatný komponent GameWarningMenuUI.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameWarningUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Close (X) tlačidlo 'GWCloseWindowButton' v titulku. Po stlačení " +
             "sa okno skryje bez vykonania akcie (rovnako ako No).")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject " +
             "(root 'GameWarningUIPanel - Window').")]
    [SerializeField] private GameObject panelToHide;

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Iba skryť okno – potvrdzovaná akcia sa zahodí v GameWarningMenuUI.OnDisable.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }
}
