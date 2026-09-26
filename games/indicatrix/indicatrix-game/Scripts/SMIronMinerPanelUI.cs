using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SMIronMinerPanelUI
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluha JEDNÉHO konkrétneho panelu náboru personálu – "SMIronMinerPanel".
/// Analógia k SMMinerPanelUI (vzor); líši sa iba fixnými hodnotami a názvami
/// prvkov (IronMiner namiesto Miner).
///
/// Komponent sa pripája na GameObject "SMIronMinerPanel". Podľa Hierarchy panel
/// obsahuje:
///   SMIronMinerPanel
///     ├─ IronMinerImage
///     ├─ IronMinerText
///     ├─ IronMinerHireButton          ← obsluhuje tento skript
///     └─ IronminerToggle
///          ├─ IronminerLowSalaryToggle      (LevelSalary = 0)
///          ├─ IronminerMediumSalaryToggle   (LevelSalary = 1)
///          └─ IronminerHighSalaryToggle     (LevelSalary = 2)
///
/// SPRÁVANIE pri kliku na IronMinerHireButton (zo zadania):
///   1. Naplní sa dátová štruktúra StaffManagement:
///        • ID             = 2     (fixná hodnota – typ "IronMiner")
///        • EmployeeSalary = 125   (fixná hodnota)
///        • LevelSalary    = z aktívne zaškrtnutého toggle
///                           (Low = 0, Medium = 1, High = 2)
///   2. Zatvorí sa celé UI okno StaffManagementMenuUI.
///   3. Spustí sa režim prideľovania personálu do továrne
///      (GameManager.EnterStaffToFactoryMode) – odvtedy hráč klikom na
///      ľubovoľnú továreň priradí tento StaffManagement (porovnanie/aplikácia
///      prebehne v GameManager.HandleStaffToFactoryClick).
///
/// POZN. k GameManageru: Staff→Factory režim je v GameManager.cs napísaný
/// GENERICKY – porovnáva factory.ID == pendingStaff.ID (nie napevno zadané
/// ID Miner-a). Preto tento panel ŽIADNU úpravu GameManager.cs nevyžaduje;
/// pre ID = 2 sa zhodne s továrňou, ktorej ID = 2, presne ako Miner pre ID = 0.
///
/// POZN.: Toggle-y bývajú v spoločnom ToggleGroup (rodič "IronminerToggle"),
/// takže je aktívny práve jeden. Ak by nebol zaškrtnutý žiaden, použije sa
/// Low (0).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SMIronMinerPanelUI : MonoBehaviour
{
    // =====================================================================
    // FIXNÉ HODNOTY PRE TYP "IRONMINER"
    // =====================================================================

    /// <summary>Fixné ID typu personálu "IronMiner" (zhoduje sa s ID továrne, ktorej má byť priradený).</summary>
    private const int IronMinerID = 2;

    /// <summary>Fixná mzda zamestnanca pre "IronMiner".</summary>
    private const int IronMinerEmployeeSalary = 125;

    // =====================================================================
    // INSPECTOR REFERENCIE
    // =====================================================================

    [Header("Hire Button")]
    [Tooltip("IronMinerHireButton – po kliku naplní StaffManagement a spustí Staff→Factory režim.")]
    [SerializeField] private Button ironMinerHireButton;

    [Header("Salary Level Toggles (IronminerToggle)")]
    [Tooltip("IronminerLowSalaryToggle – LevelSalary = 0.")]
    [SerializeField] private Toggle ironMinerLowSalaryToggle;

    [Tooltip("IronminerMediumSalaryToggle – LevelSalary = 1.")]
    [SerializeField] private Toggle ironMinerMediumSalaryToggle;

    [Tooltip("IronminerHighSalaryToggle – LevelSalary = 2.")]
    [SerializeField] private Toggle ironMinerHighSalaryToggle;

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
        if (ironMinerHireButton != null)
            ironMinerHireButton.onClick.AddListener(OnIronMinerHireClick);
        else
            Debug.LogWarning("[SMIronMinerPanelUI] IronMinerHireButton nie je priradený v Inspectore.");
    }

    // =====================================================================
    // HIRE
    // =====================================================================

    /// <summary>Klik na IronMinerHireButton – naplní StaffManagement, zatvorí okno a spustí Staff→Factory režim.</summary>
    private void OnIronMinerHireClick()
    {
        // 1) Level z aktívneho toggle (Low=0 / Medium=1 / High=2).
        short levelSalary = ReadSelectedLevelSalary();

        // 2) Naplnenie dátovej štruktúry StaffManagement.
        StaffManagement staff = new StaffManagement(IronMinerID, IronMinerEmployeeSalary, levelSalary);
        Debug.Log($"[SMIronMinerPanelUI] Hire IronMiner → {staff}");

        // 3) Zatvorenie celého okna StaffManagementMenuUI.
        CloseStaffManagementWindow();

        // 4) Spustenie režimu prideľovania personálu do továrne.
        if (GameManager.instance != null)
        {
            GameManager.instance.EnterStaffToFactoryMode(staff);
        }
        else
        {
            Debug.LogError("[SMIronMinerPanelUI] GameManager.instance je null – Staff→Factory režim sa nespustil.");
        }
    }

    /// <summary>
    /// Vráti LevelSalary podľa aktívne zaškrtnutého toggle. Poradie kontroly:
    /// High (2) → Medium (1) → Low (0). Ak nie je zaškrtnutý žiaden, vráti 0.
    /// </summary>
    private short ReadSelectedLevelSalary()
    {
        if (ironMinerHighSalaryToggle != null && ironMinerHighSalaryToggle.isOn) return 2;
        if (ironMinerMediumSalaryToggle != null && ironMinerMediumSalaryToggle.isOn) return 1;
        if (ironMinerLowSalaryToggle != null && ironMinerLowSalaryToggle.isOn) return 0;

        // Žiaden toggle aktívny → bezpečný default (Low).
        Debug.LogWarning("[SMIronMinerPanelUI] Žiaden salary toggle nie je zaškrtnutý – použité LevelSalary = 0 (Low).");
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
            Debug.LogWarning("[SMIronMinerPanelUI] Nepodarilo sa nájsť root okna StaffManagementMenuUI – okno sa nezatvorilo.");
    }
}
