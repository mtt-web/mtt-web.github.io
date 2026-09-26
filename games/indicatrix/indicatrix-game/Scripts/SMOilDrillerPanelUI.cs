using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMOilDrillerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMOilDrillerPanel".
/// (V hre je 16 podobných panelov; tento je naimplementovaný analogicky k
/// vzorovému "SMMinerPanel" / SMMinerPanelUI.)
///
/// Komponent sa pripája na GameObject "SMOilDrillerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMOilDrillerPanel
///     ├─ OilDrillerImage
///     ├─ OilDrillerText
///     ├─ OilDrillerHireButton          ← obsluhuje tento skript
///     └─ OilDrillerToggle
///          ├─ OilDrillerLowSalaryToggle      (LevelSalary = 0)
///          ├─ OilDrillerMediumSalaryToggle   (LevelSalary = 1)
///          └─ OilDrillerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na OilDrillerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 6     (fixná hodnota – typ "OilDriller")
///        • EmployeeSalary = 175   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick). Footprint továrne
///      je zohľadnený automaticky (FactoryRegistry.GetFactoryAt mapuje každý
///      tile footprintu na tú istú FactoryInstance) a snap vizuál je 1×1
///      štvorec + face (IndAPI.SnapMeshFace).
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "OilDrillerToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMOilDrillerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "OILDRILLER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "OilDriller" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int OilDrillerID = 6;

    /// <summary>Fixná mzda zamestnanca pre "OilDriller".</summary>
    private const int OilDrillerEmployeeSalary = 175;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("OilDrillerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button oilDrillerHireButton;

    [Header("Salary Level Toggles (OilDrillerToggle)")]
    [Tooltip("OilDrillerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle oilDrillerLowSalaryToggle;

    [Tooltip("OilDrillerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle oilDrillerMediumSalaryToggle;

    [Tooltip("OilDrillerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle oilDrillerHighSalaryToggle;

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
        if (oilDrillerHireButton != null)
            oilDrillerHireButton.onClick.AddListener(OnOilDrillerHireClick);
        else
            Debug.LogWarning("[SMOilDrillerPanelUI] OilDrillerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na OilDrillerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnOilDrillerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(OilDrillerID, OilDrillerEmployeeSalary, levelSalary);
        Debug.Log($"[SMOilDrillerPanelUI] Hire OilDriller → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMOilDrillerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (oilDrillerHighSalaryToggle != null && oilDrillerHighSalaryToggle.isOn) return 2;
        if (oilDrillerMediumSalaryToggle != null && oilDrillerMediumSalaryToggle.isOn) return 1;
        if (oilDrillerLowSalaryToggle != null && oilDrillerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMOilDrillerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMOilDrillerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
