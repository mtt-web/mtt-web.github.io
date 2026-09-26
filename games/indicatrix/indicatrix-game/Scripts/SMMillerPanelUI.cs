using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMMillerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMMillerPanel".
/// Analógia k SMMinerPanelUI (vzor), len pre typ "Miller" (Mlynár).
///
/// Komponent sa pripája na GameObject "SMMillerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMMillerPanel
///     ├─ MillerImage
///     ├─ MillerText
///     ├─ MillerHireButton          ← obsluhuje tento skript
///     └─ MillerToggle
///          ├─ MillerLowSalaryToggle      (LevelSalary = 0)
///          ├─ MillerMediumSalaryToggle   (LevelSalary = 1)
///          └─ MillerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na MillerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 13    (fixná hodnota – typ "Miller")
///        • EmployeeSalary = 80    (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick):
///        • ak sa StaffManagement.ID == FactoryInstance.ID → do továrne sa
///          prenesie LevelSalary aj EmployeeSalary a OccupancyFlag = true;
///        • inak → OccupancyFlag = false (ostatné polia bez zmeny).
///      Footprint továrne (2×3 / 3×3 / 2×2) rieši GameManager automaticky cez
///      FactoryRegistry.GetFactoryAt – stačí kliknúť na hociktorý tile továrne.
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "MillerToggle"), takže
/// je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMMillerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "MILLER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "Miller" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int MillerID = 13;

    /// <summary>Fixná mzda zamestnanca pre "Miller".</summary>
    private const int MillerEmployeeSalary = 80;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("MillerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button millerHireButton;

    [Header("Salary Level Toggles (MillerToggle)")]
    [Tooltip("MillerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle millerLowSalaryToggle;

    [Tooltip("MillerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle millerMediumSalaryToggle;

    [Tooltip("MillerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle millerHighSalaryToggle;

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
        if (millerHireButton != null)
            millerHireButton.onClick.AddListener(OnMillerHireClick);
        else
            Debug.LogWarning("[SMMillerPanelUI] MillerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na MillerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnMillerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(MillerID, MillerEmployeeSalary, levelSalary);
        Debug.Log($"[SMMillerPanelUI] Hire Miller → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMMillerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (millerHighSalaryToggle != null && millerHighSalaryToggle.isOn) return 2;
        if (millerMediumSalaryToggle != null && millerMediumSalaryToggle.isOn) return 1;
        if (millerLowSalaryToggle != null && millerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMMillerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMMillerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
