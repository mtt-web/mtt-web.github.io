using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMMinerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMMinerPanel".
/// (V hre je 16 podobných panelov; tu je naimplementovaný iba Miner ako vzor.)
///
/// Komponent sa pripája na GameObject "SMMinerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMMinerPanel
///     ├─ MinerImage
///     ├─ MinerText
///     ├─ MinerHireButton          ← obsluhuje tento skript
///     └─ MinerToggle
///          ├─ MinerLowSalaryToggle      (LevelSalary = 0)
///          ├─ MinerMediumSalaryToggle   (LevelSalary = 1)
///          └─ MinerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na MinerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 0     (fixná hodnota – typ "Miner")
///        • EmployeeSalary = 100   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "MinerToggle"), takže
/// je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMMinerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "MINER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "Miner" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int MinerID = 0;

    /// <summary>Fixná mzda zamestnanca pre "Miner".</summary>
    private const int MinerEmployeeSalary = 100;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("MinerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button minerHireButton;

    [Header("Salary Level Toggles (MinerToggle)")]
    [Tooltip("MinerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle minerLowSalaryToggle;

    [Tooltip("MinerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle minerMediumSalaryToggle;

    [Tooltip("MinerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle minerHighSalaryToggle;

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
        if (minerHireButton != null)
            minerHireButton.onClick.AddListener(OnMinerHireClick);
        else
            Debug.LogWarning("[SMMinerPanelUI] MinerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na MinerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnMinerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(MinerID, MinerEmployeeSalary, levelSalary);
        Debug.Log($"[SMMinerPanelUI] Hire Miner → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMMinerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (minerHighSalaryToggle != null && minerHighSalaryToggle.isOn) return 2;
        if (minerMediumSalaryToggle != null && minerMediumSalaryToggle.isOn) return 1;
        if (minerLowSalaryToggle != null && minerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMMinerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMMinerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
