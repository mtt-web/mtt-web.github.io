using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameRailSelectBridgesUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k GameWarningUIwindow. Komponent na root paneli
/// "GameRailSelectBridgesUIPanel - Window" (výber typu ŽELEZNIČNÉHO mosta).
/// Zabezpečuje:
///   1. Obsluhu Close (X) tlačidla "GRCloseWindowButton" v titulku.
///   2. Zatvorenie okna = klávesa ESC: panel sa skryje, most sa NEPOSTAVÍ
///      (GameRailSelectBridgesMenuUI výber zruší v OnDisable) a zavolá sa
///      GameManager.PerformEscapeReset() – zruší sa výstavbový režim.
///   3. Pozíciovanie okna – dedí z UIWindowBase (vycentrovanie pri otvorení,
///      SetWindowPosition(...)).
///
/// ROZDIEL OPROTI GameWarningUIwindow:
///   Okno je súčasťou výstavby mosta, preto X NEROBÍ len skrytie, ale presne
///   to isté ako ESC (zadanie).
///
/// POZN.: Drag-and-drop rieši samostatný WindowDragHandle na
/// "GameRailSelectBridgesUI - Title". Obsahové tlačidlá rieši
/// GameRailSelectBridgesMenuUI.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class GameRailSelectBridgesUIwindow : UIWindowBase
{
    [Header("Close (X) Button")]
    [Tooltip("Close (X) tlačidlo 'GRCloseWindowButton' v titulku. Po stlačení " +
             "sa okno skryje, most sa nepostaví a zruší sa výstavbový režim (ESC).")]
    [SerializeField] private Button closeButton;

    [Tooltip("GameObject panelu, ktorý sa má pri stlačení Close skryť. " +
             "Ak nie je priradený, použije sa tento gameObject " +
             "(root 'GameRailSelectBridgesUIPanel - Window').")]
    [SerializeField] private GameObject panelToHide;

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        // 1) Skryť okno – výber typu mosta sa zruší v GameRailSelectBridgesMenuUI.OnDisable.
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);

        // 2) Rovnaká akcia ako klávesa ESC – zruší výstavbový režim.
        if (GameManager.instance != null)
            GameManager.instance.PerformEscapeReset();
    }
}
