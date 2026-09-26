using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InGameMenuUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionUIwindow. Komponent umiestnený na samotnom
/// root paneli "GameMenuUIPanel - Window" (in-game / pauzové menu).
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla "GMCloseWindowButton" v titulku.
///   2. Bezpečné zatvorenie okna – pri kliknutí na Close sa panel skryje.
///   3. Pozíciovanie okna – dedí z UIWindowBase, ktorá zabezpečuje
///      automatické vycentrovanie pri otvorení a verejné, z kódu volateľné
///      metódy SetWindowPosition(...). Žiadna Inspector referencia netreba.
///
/// ROZDIEL OPROTI FactoryConstructionUIwindow:
///   GameMenu okno je čisto OVLÁDACIE (pauzové) – NESPÚŠŤA žiadny construction
///   / terrain režim. Preto tu zámerne NEVOLÁME ani GameManager.PerformEscapeReset(),
///   ani GameManager.SetTerrainMode(...). Pri Factory okne sa to robí preto,
///   lebo jeho otvorenie spustí FACTORY snapping režim; tu žiadny taký režim
///   neexistuje, a volanie escape resetu by nechcene zrušilo prípadný
///   rozrobený RAIL/ROAD/FACTORY režim hráča, ktorý si len otvoril pauzové menu.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu. Drag rieši
/// samostatný WindowDragHandle pripojený na Title GameObject
/// ("GameMenuUI - Title"), ktorý drag-uje root panel "GameMenuUIPanel - Window".
///
/// POZN.: Obsahové tlačidlá (BackToMainMenu / ExitGame / LoadGame / SaveGame)
/// rieši samostatný komponent InGameMenuUI – rovnako ako pri Factory okne má
/// content tlačidlá na starosti FactoryConstructionMenuUI, nie ...UIwindow.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class InGameMenuUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Close (X) tlačidlo 'GMCloseWindowButton' v titulku. Po stlačení " +
             "sa okno skryje.")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject " +
             "(root 'GameMenuUIPanel - Window').")]
    [SerializeField] private GameObject panelToHide;

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // Iba skryť okno. Žiadny construction/terrain režim sa nezrušuje
        // (viď komentár v hlavičke triedy) – pauzové meno nesmie zasahovať
        // do rozrobeného RAIL/ROAD/FACTORY režimu hráča.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }
}
