using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StaffManagementUIwindow
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionUIwindow. Komponent umiestnený na samotnom
/// okne StaffManagementMenuUI (root "StaffManagementMenuUIPanel - Window").
/// Zabezpečuje:
///   1. Obsluhu voliteľného Close (X) tlačidla.
///   2. Pozíciovanie okna – dedí z UIWindowBase (automatické vycentrovanie pri
///      otvorení + verejné metódy SetWindowPosition(...)). Žiadna Inspector
///      referencia na to netreba.
///
/// ROZDIEL oproti FactoryConstructionUIwindow:
///   StaffManagementMenuUI NIE JE konštrukčné menu – nespúšťa žiadny
///   FactoryConstructionMode / Rail / Road režim. Preto sa tu NEVOLÁ
///   GameManager.SetTerrainMode(None) ani pri Close, ani v OnDisable. Žiadny
///   "zaseknutý" SnapAreaFace indikátor pri tomto okne nevzniká.
///
///   Reset vnútorného stavu okna (návrat do default stavu č. 1) zabezpečuje
///   samotný StaffManagementMenuUI vo svojom OnEnable pri každom otvorení,
///   takže Close stačí len skryť panel.
///
/// POZN.: Drag-and-drop pohyb okna NIE JE súčasťou tohto skriptu – rieši ho
/// samostatný WindowDragHandle pripojený na Title GameObject
/// ("StaffManagementMenuUI - Title"), rovnako ako pri ostatných oknách.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StaffManagementUIwindow : UIWindowBase
{
    [Header("Optional Close (X) Button")]
    [Tooltip("Voliteľné tlačidlo X na zatvorenie okna. Ak je priradené, " +
             "kliknutie skryje panel okna.")]
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
        // Žiadny SetTerrainMode – Staff okno nie je konštrukčný režim.
        // Stačí skryť panel (vnútorný stav sa pri ďalšom otvorení resetuje
        // v StaffManagementMenuUI.OnEnable).
        GameObject target = panelToHide != null ? panelToHide : gameObject;
        target.SetActive(false);
    }
}
