using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMRefineryOperatorPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMRefineryOperatorPanel".
/// Analógia k SMMinerPanelUI (vzor), líši sa len fixnými hodnotami a názvami
/// referencií pre typ "RefineryOperator".
///
/// Komponent sa pripája na GameObject "SMRefineryOperatorPanel". Podľa Hierarchy
/// panel obsahuje:
///   SMRefineryOperatorPanel
///     ├─ RefineryOperatorImage
///     ├─ RefineryOperatorText
///     ├─ RefineryOperatorHireButton          ← obsluhuje tento skript
///     └─ RefineryOperatorToggle
///          ├─ RefineryOperatorLowSalaryToggle      (LevelSalary = 0)
///          ├─ RefineryOperatorMediumSalaryToggle   (LevelSalary = 1)
///          └─ RefineryOperatorHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na RefineryOperatorHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 9     (fixná hodnota – typ "RefineryOperator")
///        • EmployeeSalary = 90    (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "RefineryOperatorToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMRefineryOperatorPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "REFINERY OPERATOR"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "RefineryOperator" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int RefineryOperatorID = 9;

    /// <summary>Fixná mzda zamestnanca pre "RefineryOperator".</summary>
    private const int RefineryOperatorEmployeeSalary = 90;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("RefineryOperatorHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button refineryOperatorHireButton;

    [Header("Salary Level Toggles (RefineryOperatorToggle)")]
    [Tooltip("RefineryOperatorLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle refineryOperatorLowSalaryToggle;

    [Tooltip("RefineryOperatorMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle refineryOperatorMediumSalaryToggle;

    [Tooltip("RefineryOperatorHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle refineryOperatorHighSalaryToggle;

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
        if (refineryOperatorHireButton != null)
            refineryOperatorHireButton.onClick.AddListener(OnRefineryOperatorHireClick);
        else
            Debug.LogWarning("[SMRefineryOperatorPanelUI] RefineryOperatorHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na RefineryOperatorHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnRefineryOperatorHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(RefineryOperatorID, RefineryOperatorEmployeeSalary, levelSalary);
        Debug.Log($"[SMRefineryOperatorPanelUI] Hire RefineryOperator → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMRefineryOperatorPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (refineryOperatorHighSalaryToggle != null && refineryOperatorHighSalaryToggle.isOn) return 2;
        if (refineryOperatorMediumSalaryToggle != null && refineryOperatorMediumSalaryToggle.isOn) return 1;
        if (refineryOperatorLowSalaryToggle != null && refineryOperatorLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMRefineryOperatorPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMRefineryOperatorPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
