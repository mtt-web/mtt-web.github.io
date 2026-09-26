using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMMetallurgistPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMMetallurgistPanel".
/// Vytvorené analogicky k vzoru SMMinerPanelUI (typ "Miner", ID = 0).
///
/// Komponent sa pripája na GameObject "SMMetallurgistPanel". Podľa Hierarchy
/// panel obsahuje:
///   SMMetallurgistPanel
///     ├─ MetallurgistImage
///     ├─ MetallurgistText
///     ├─ MetallurgistHireButton          ← obsluhuje tento skript
///     └─ MetallurgistToggle
///          ├─ MetallurgistLowSalaryToggle      (LevelSalary = 0)
///          ├─ MetallurgistMediumSalaryToggle   (LevelSalary = 1)
///          └─ MetallurgistHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na MetallurgistHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 14    (fixná hodnota – typ "Metallurgist")
///        • EmployeeSalary = 120   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick: pri zhode
///      ID == 14 sa do továrne prenesie LevelSalary + EmployeeSalary
///      a OccupancyFlag = true, inak OccupancyFlag = false).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "MetallurgistToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0). GameManager režim je generický – nepotrebuje žiadnu úpravu pre
/// tento typ personálu; rozlišovanie rieši výhradne fixné ID (14) tu.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMMetallurgistPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "METALLURGIST"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "Metallurgist" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int MetallurgistID = 14;

    /// <summary>Fixná mzda zamestnanca pre "Metallurgist".</summary>
    private const int MetallurgistEmployeeSalary = 120;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("MetallurgistHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button metallurgistHireButton;

    [Header("Salary Level Toggles (MetallurgistToggle)")]
    [Tooltip("MetallurgistLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle metallurgistLowSalaryToggle;

    [Tooltip("MetallurgistMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle metallurgistMediumSalaryToggle;

    [Tooltip("MetallurgistHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle metallurgistHighSalaryToggle;

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
        if (metallurgistHireButton != null)
            metallurgistHireButton.onClick.AddListener(OnMetallurgistHireClick);
        else
            Debug.LogWarning("[SMMetallurgistPanelUI] MetallurgistHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na MetallurgistHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnMetallurgistHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(MetallurgistID, MetallurgistEmployeeSalary, levelSalary);
        Debug.Log($"[SMMetallurgistPanelUI] Hire Metallurgist → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMMetallurgistPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (metallurgistHighSalaryToggle != null && metallurgistHighSalaryToggle.isOn) return 2;
        if (metallurgistMediumSalaryToggle != null && metallurgistMediumSalaryToggle.isOn) return 1;
        if (metallurgistLowSalaryToggle != null && metallurgistLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMMetallurgistPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMMetallurgistPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
