using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMFarmerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMFarmerPanel".
/// Vytvorené analogicky k SMMinerPanelUI (vzor pre "SMMinerPanel").
///
/// Komponent sa pripája na GameObject "SMFarmerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMFarmerPanel
///     ├─ FarmerImage
///     ├─ FarmerText
///     ├─ FarmerHireButton          ← obsluhuje tento skript
///     └─ FarmerToggle
///          ├─ FarmerLowSalaryToggle      (LevelSalary = 0)
///          ├─ FarmerMediumSalaryToggle   (LevelSalary = 1)
///          └─ FarmerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na FarmerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 5     (fixná hodnota – typ "Farmer")
///        • EmployeeSalary = 200   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "FarmerToggle"), takže
/// je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMFarmerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "FARMER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "Farmer" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int FarmerID = 5;

    /// <summary>Fixná mzda zamestnanca pre "Farmer".</summary>
    private const int FarmerEmployeeSalary = 200;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("FarmerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button farmerHireButton;

    [Header("Salary Level Toggles (FarmerToggle)")]
    [Tooltip("FarmerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle farmerLowSalaryToggle;

    [Tooltip("FarmerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle farmerMediumSalaryToggle;

    [Tooltip("FarmerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle farmerHighSalaryToggle;

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
        if (farmerHireButton != null)
            farmerHireButton.onClick.AddListener(OnFarmerHireClick);
        else
            Debug.LogWarning("[SMFarmerPanelUI] FarmerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na FarmerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnFarmerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(FarmerID, FarmerEmployeeSalary, levelSalary);
        Debug.Log($"[SMFarmerPanelUI] Hire Farmer → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMFarmerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (farmerHighSalaryToggle != null && farmerHighSalaryToggle.isOn) return 2;
        if (farmerMediumSalaryToggle != null && farmerMediumSalaryToggle.isOn) return 1;
        if (farmerLowSalaryToggle != null && farmerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMFarmerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMFarmerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
