using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMWoodcutterPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMWoodcutterPanel".
/// Analógia k SMMinerPanelUI (vzorová implementácia typu "Miner").
///
/// Komponent sa pripája na GameObject "SMWoodcutterPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMWoodcutterPanel
///     ├─ WoodcutterImage
///     ├─ WoodcutterText
///     ├─ WoodcutterHireButton          ← obsluhuje tento skript
///     └─ WoodcutterToggle
///          ├─ WoodcutterLowSalaryToggle      (LevelSalary = 0)
///          ├─ WoodcutterMediumSalaryToggle   (LevelSalary = 1)
///          └─ WoodcutterHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na WoodcutterHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = WoodcutterID          (fixná hodnota – typ "Woodcutter")
///        • EmployeeSalary = 120                   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick – ten je generický,
///      pracuje s ľubovoľnou StaffManagement štruktúrou, takže pre tento panel
///      NETREBA žiadnu úpravu GameManager.cs).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "WoodcutterToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMWoodcutterPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "WOODCUTTER"
    // =====================================================================

    /// <summary>
    /// Fixné ID typu personálu "Woodcutter".
    ///
    /// DÔLEŽITÉ – mapovanie ID musí sedieť s ID továrne, do ktorej má personál
    /// patriť. V GameManager.HandleStaffToFactoryClick sa porovnáva
    /// (StaffManagement.ID == FactoryInstance.ID); zhoda spôsobí priradenie,
    /// nezhoda len OccupancyFlag = false. FactoryInstance.ID si továreň preberá
    /// z FactoryDefinition.ID (fixné, v poradí databázy).
    ///
    /// Vo vzore má Miner ID = 0 (zodpovedá CoalMine – prvej továrni). Woodcutter
    /// patrí k továrni "Forest", ktorej FactoryDefinition.ID = 1 (CoalMine=0,
    /// Forest=1, … v poradí databázy). Preto je ID nastavené na 1.
    /// </summary>
    private const int WoodcutterID = 1;

    /// <summary>Fixná mzda zamestnanca pre "Woodcutter".</summary>
    private const int WoodcutterEmployeeSalary = 120;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("WoodcutterHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button woodcutterHireButton;

    [Header("Salary Level Toggles (WoodcutterToggle)")]
    [Tooltip("WoodcutterLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle woodcutterLowSalaryToggle;

    [Tooltip("WoodcutterMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle woodcutterMediumSalaryToggle;

    [Tooltip("WoodcutterHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle woodcutterHighSalaryToggle;

    [Header("Okno na zatvorenie")]
    [Tooltip("Root GameObject celého okna StaffManagementMenuUI " +
             "(\"StaffManagementMenuUIPanel - Window\"). Po Hire sa SetActive(false). " +
             "Ak ostane nepriradené, skript sa pokúsi nájsť rodičovský StaffManagementUIwindow.")]
    [SerializeField] private GameObject staffManagementWindowRoot;

    // =====================================================================
    // LIFECYCLE
    // =====================================================================

    private void Start()
    {
        if (woodcutterHireButton != null)
            woodcutterHireButton.onClick.AddListener(OnWoodcutterHireClick);
        else
            Debug.LogWarning("[SMWoodcutterPanelUI] WoodcutterHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na WoodcutterHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnWoodcutterHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(WoodcutterID, WoodcutterEmployeeSalary, levelSalary);
        Debug.Log($"[SMWoodcutterPanelUI] Hire Woodcutter → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMWoodcutterPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (woodcutterHighSalaryToggle != null && woodcutterHighSalaryToggle.isOn) return 2;
        if (woodcutterMediumSalaryToggle != null && woodcutterMediumSalaryToggle.isOn) return 1;
        if (woodcutterLowSalaryToggle != null && woodcutterLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMWoodcutterPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
        return 0;
    }

    /// <summary>
    /// Skryje celé okno StaffManagementMenuUI. Vnútorný stav okna sa pri ďalšom
    /// otvorení resetuje v StaffManagementMenuUI.OnEnable, takže stačí SetActive(false).
    /// </summary>
    private void CloseStaffManagementWindow()
    {
        GameObject root = staffManagementWindowRoot;

        // Fallback – ak root nie je priradený, skús nájsť okno cez rodiča.
        if (root == null)
        {
            StaffManagementUIwindow window = GetComponentInParent<StaffManagementUIwindow>(true);
            if (window != null)
                root = window.gameObject;
        }

        if (root != null)
            root.SetActive(false);
        else
            Debug.LogWarning("[SMWoodcutterPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
