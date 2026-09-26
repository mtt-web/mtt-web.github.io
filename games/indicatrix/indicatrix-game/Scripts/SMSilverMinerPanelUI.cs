using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMSilverMinerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMSilverMinerPanel".
/// Presný analóg vzoru SMMinerPanelUI (líši sa len fixnými hodnotami a názvami
/// referencií). V hre je 16 podobných panelov; tento je pre typ "SilverMiner".
///
/// Komponent sa pripája na GameObject "SMSilverMinerPanel". Podľa Hierarchy
/// (screenshot) panel obsahuje:
///   SMSilverMinerPanel
///     ├─ SilverMinerImage
///     ├─ SilverMinerText
///     ├─ SilverMinerHireButton          ← obsluhuje tento skript
///     └─ SilverMinerToggle
///          ├─ SilverMinerLowSalaryToggle      (LevelSalary = 0)
///          ├─ SilverMinerMediumSalaryToggle   (LevelSalary = 1)
///          └─ SilverMinerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na SilverMinerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 4     (fixná hodnota – typ "SilverMiner")
///        • EmployeeSalary = 120   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement. Snapping na tile mape
///      (štvorec + face), zohľadnenie footprintu továrne a porovnanie
///      ID / LevelSalary / EmployeeSalary + nastavenie OccupancyFlag prebieha
///      generického v GameManager.HandleStaffToFactoryClick – netreba tam nič
///      upravovať pre tento (ani žiadny ďalší) typ personálu.
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "SilverMinerToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMSilverMinerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "SILVERMINER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "SilverMiner" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int SilverMinerID = 4;

    /// <summary>Fixná mzda zamestnanca pre "SilverMiner".</summary>
    private const int SilverMinerEmployeeSalary = 120;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("SilverMinerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button silverMinerHireButton;

    [Header("Salary Level Toggles (SilverMinerToggle)")]
    [Tooltip("SilverMinerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle silverMinerLowSalaryToggle;

    [Tooltip("SilverMinerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle silverMinerMediumSalaryToggle;

    [Tooltip("SilverMinerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle silverMinerHighSalaryToggle;

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
        if (silverMinerHireButton != null)
            silverMinerHireButton.onClick.AddListener(OnSilverMinerHireClick);
        else
            Debug.LogWarning("[SMSilverMinerPanelUI] SilverMinerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na SilverMinerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnSilverMinerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(SilverMinerID, SilverMinerEmployeeSalary, levelSalary);
        Debug.Log($"[SMSilverMinerPanelUI] Hire SilverMiner → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMSilverMinerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (silverMinerHighSalaryToggle != null && silverMinerHighSalaryToggle.isOn) return 2;
        if (silverMinerMediumSalaryToggle != null && silverMinerMediumSalaryToggle.isOn) return 1;
        if (silverMinerLowSalaryToggle != null && silverMinerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMSilverMinerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMSilverMinerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
