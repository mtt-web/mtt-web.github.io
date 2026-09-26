using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMGlassmakerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMGlassmakerPanel".
/// Analógia k SMMinerPanelUI – líši sa iba FIXNÝM ID typu (Glassmaker = 15)
/// a názvami referencovaných UI prvkov (prefix "Glassmaker").
///
/// Komponent sa pripája na GameObject "SMGlassmakerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMGlassmakerPanel
///     ├─ GlassmakerImage
///     ├─ GlassmakerText
///     ├─ GlassmakerHireButton          ← obsluhuje tento skript
///     └─ GlassmakerToggle
///          ├─ GlassmakerLowSalaryToggle      (LevelSalary = 0)
///          ├─ GlassmakerMediumSalaryToggle   (LevelSalary = 1)
///          └─ GlassmakerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na GlassmakerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 15    (fixná hodnota – typ "Glassmaker")
///        • EmployeeSalary = 100   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "GlassmakerToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMGlassmakerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "GLASSMAKER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "Glassmaker" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int GlassmakerID = 15;

    /// <summary>Fixná mzda zamestnanca pre "Glassmaker".</summary>
    private const int GlassmakerEmployeeSalary = 100;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("GlassmakerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button glassmakerHireButton;

    [Header("Salary Level Toggles (GlassmakerToggle)")]
    [Tooltip("GlassmakerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle glassmakerLowSalaryToggle;

    [Tooltip("GlassmakerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle glassmakerMediumSalaryToggle;

    [Tooltip("GlassmakerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle glassmakerHighSalaryToggle;

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
        if (glassmakerHireButton != null)
            glassmakerHireButton.onClick.AddListener(OnGlassmakerHireClick);
        else
            Debug.LogWarning("[SMGlassmakerPanelUI] GlassmakerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na GlassmakerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnGlassmakerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(GlassmakerID, GlassmakerEmployeeSalary, levelSalary);
        Debug.Log($"[SMGlassmakerPanelUI] Hire Glassmaker → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMGlassmakerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (glassmakerHighSalaryToggle != null && glassmakerHighSalaryToggle.isOn) return 2;
        if (glassmakerMediumSalaryToggle != null && glassmakerMediumSalaryToggle.isOn) return 1;
        if (glassmakerLowSalaryToggle != null && glassmakerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMGlassmakerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMGlassmakerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
