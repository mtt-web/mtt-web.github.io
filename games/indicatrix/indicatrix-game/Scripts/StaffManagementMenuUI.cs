using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StaffManagementMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionMenuUI – obsluhuje kliky tlačidiel v okne
/// StaffManagementMenuUI. Na rozdiel od FACTORY/RAIL/ROAD menu NEspúšťa žiadny
/// konštrukčný režim (GameManager.SetTerrainMode sa tu nevolá) – ide o čisto
/// informačné / manažérske okno, ktoré len prepína viditeľnosť panelov.
///
/// Komponent je umiestnený na okne StaffManagementMenuUI (root "Window",
/// prípadne na jeho "Content"). Pri každom otvorení okna (OnEnable) sa stav
/// resetuje na DEFAULT (bod 1 zo zadania).
///
/// ŠTRUKTÚRA (podľa Hierarchy):
///   StaffManagementMenuUIPanel - Window
///     └─ StaffManagementMenuUI - Content
///          ├─ StaffManagementImage          (default obrázok)
///          ├─ HireStaffToggleButton         (vždy visible=enabled)
///          ├─ StaffManagementToggleButton   (vždy visible=enabled)
///          ├─ HireStaffTogglePanelUI
///          │     ├─ HireStaffImage          (default v rámci tohto panelu)
///          │     ├─ SMMinerButton ... SMGlassmakerButton   (16 buttonov)
///          │     └─ SMMinerPanel  ... SMGlassmakerPanel     (16 panelov)
///          └─ StaffManagementTogglePanelUI
///
/// SPRÁVANIE (zo zadania):
///   1. Po otvorení (default):  StaffManagementImage = ON,
///                              HireStaffTogglePanelUI = OFF,
///                              StaffManagementTogglePanelUI = OFF.
///   2. HireStaffToggleButton:  StaffManagementImage = OFF,
///                              HireStaffTogglePanelUI = ON,
///                              StaffManagementTogglePanelUI = OFF.
///   3. Default v HireStaffTogglePanelUI: HireStaffImage = ON,
///                              všetky SM*Panel = OFF (SM*Button ostávajú ON).
///   4. Klik na SM*Button:      HireStaffImage = OFF, príslušný SM*Panel = ON,
///                              všetky ostatné SM*Panel = OFF (vždy max. 1 ON).
///   5. StaffManagementToggleButton: StaffManagementImage = OFF,
///                              HireStaffTogglePanelUI = OFF,
///                              StaffManagementTogglePanelUI = ON.
///
///   HireStaffToggleButton / StaffManagementToggleButton sa správajú ako
///   TOGGLE – druhý klik na ten istý button vráti okno do stavu č. 1.
///   Klik na druhý toggle button len prepne na jeho panel (vzájomne výlučné).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StaffManagementMenuUI : MonoBehaviour
{
    /// <summary>
    /// Pár "button ↔ panel" pre jeden typ personálu v HireStaffTogglePanelUI.
    /// V Inspectore priraď napr. SMMinerButton + SMMinerPanel atď.
    /// (16 položiek: Miner, Woodcutter, GoldMiner, SilverMiner, IronMiner,
    ///  Farmer, OilDriller, RefineryOperator, ElectronicsTechnician, Carpenter,
    ///  PowerPlantOperator, Sawyer, Butcher, Miller, Metallurgist, Glassmaker).
    /// </summary>
    [System.Serializable]
    public class StaffPanelEntry
    {
        [Tooltip("Tlačidlo SM...Button (napr. SMMinerButton).")]
        public Button button;

        [Tooltip("Panel SM...Panel, ktorý sa zobrazí po kliknutí (napr. SMMinerPanel).")]
        public GameObject panel;
    }

    [Header("Toggle Buttons (vždy visible=enabled)")]
    [Tooltip("HireStaffToggleButton – otvára/zatvára HireStaffTogglePanelUI.")]
    [SerializeField] private Button hireStaffToggleButton;

    [Tooltip("StaffManagementToggleButton – otvára/zatvára StaffManagementTogglePanelUI.")]
    [SerializeField] private Button staffManagementToggleButton;

    [Header("Default obsah okna")]
    [Tooltip("StaffManagementImage – zobrazený v default stave (bod 1).")]
    [SerializeField] private GameObject staffManagementImage;

    [Header("Toggle Panely")]
    [Tooltip("HireStaffTogglePanelUI – panel s tlačidlami na nábor personálu.")]
    [SerializeField] private GameObject hireStaffTogglePanelUI;

    [Tooltip("StaffManagementTogglePanelUI – panel so správou personálu.")]
    [SerializeField] private GameObject staffManagementTogglePanelUI;

    [Header("HireStaffTogglePanelUI – default obsah")]
    [Tooltip("HireStaffImage – default obrázok v HireStaffTogglePanelUI (bod 3).")]
    [SerializeField] private GameObject hireStaffImage;

    [Header("HireStaffTogglePanelUI – staff buttons + panely (16 ks)")]
    [Tooltip("Páry SM...Button ↔ SM...Panel. Priraď všetkých 16 v Inspectore.")]
    [SerializeField] private StaffPanelEntry[] hireStaffEntries;


    // OnEnable beží pri každom (znovu)otvorení okna → vždy začni v stave č. 1.
    void OnEnable()
    {
        ShowDefaultState();
    }

    void Start()
    {
        // Toggle buttons
        if (hireStaffToggleButton != null)
            hireStaffToggleButton.onClick.AddListener(OnHireStaffToggleClick);

        if (staffManagementToggleButton != null)
            staffManagementToggleButton.onClick.AddListener(OnStaffManagementToggleClick);

        // SM...Button → zobraz príslušný SM...Panel (index zachytený do closure)
        if (hireStaffEntries != null)
        {
            for (int i = 0; i < hireStaffEntries.Length; i++)
            {
                StaffPanelEntry entry = hireStaffEntries[i];
                if (entry == null || entry.button == null) continue;

                int index = i; // dôležité: zachytenie kópie indexu pre listener
                entry.button.onClick.AddListener(() => OnStaffButtonClick(index));
            }
        }

        // Poistka – po naviazaní listenerov ešte raz zarovnaj na default.
        ShowDefaultState();
    }

    // ─────────────────────────────────────────────────────────────────────
    // STAVY OKNA
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Bod 1 – default stav okna po otvorení.</summary>
    private void ShowDefaultState()
    {
        SetActiveSafe(staffManagementImage, true);
        SetActiveSafe(hireStaffTogglePanelUI, false);
        SetActiveSafe(staffManagementTogglePanelUI, false);
    }

    // ── HireStaffToggleButton ──────────────────────────────────────────────

    private void OnHireStaffToggleClick()
    {
        bool currentlyOpen = hireStaffTogglePanelUI != null
                          && hireStaffTogglePanelUI.activeSelf;

        if (currentlyOpen)
        {
            // Druhý klik na ten istý toggle → späť do stavu č. 1.
            ShowDefaultState();
        }
        else
        {
            // Bod 2 – otvor HireStaffTogglePanelUI, zvyšok skry.
            SetActiveSafe(staffManagementImage, false);
            SetActiveSafe(staffManagementTogglePanelUI, false);
            SetActiveSafe(hireStaffTogglePanelUI, true);

            // Bod 3 – default obsah HireStaffTogglePanelUI.
            ShowHireStaffDefault();
        }
    }

    /// <summary>Bod 3 – HireStaffImage = ON, všetky SM*Panel = OFF.</summary>
    private void ShowHireStaffDefault()
    {
        SetActiveSafe(hireStaffImage, true);

        if (hireStaffEntries != null)
        {
            foreach (StaffPanelEntry entry in hireStaffEntries)
            {
                if (entry == null) continue;
                SetActiveSafe(entry.panel, false);
            }
        }
        // SM*Button ostávajú visible=enabled – nikdy ich nevypíname.
    }

    /// <summary>Bod 4 – klik na konkrétny SM*Button: ten panel ON, ostatné OFF.</summary>
    private void OnStaffButtonClick(int index)
    {
        SetActiveSafe(hireStaffImage, false);

        if (hireStaffEntries == null) return;

        for (int i = 0; i < hireStaffEntries.Length; i++)
        {
            StaffPanelEntry entry = hireStaffEntries[i];
            if (entry == null) continue;

            // Iba zvolený panel ostane ON; všetky ostatné OFF.
            SetActiveSafe(entry.panel, i == index);
        }
    }

    // ── StaffManagementToggleButton ────────────────────────────────────────

    private void OnStaffManagementToggleClick()
    {
        bool currentlyOpen = staffManagementTogglePanelUI != null
                          && staffManagementTogglePanelUI.activeSelf;

        if (currentlyOpen)
        {
            // Druhý klik na ten istý toggle → späť do stavu č. 1.
            ShowDefaultState();
        }
        else
        {
            // Bod 5 – otvor StaffManagementTogglePanelUI, zvyšok skry.
            SetActiveSafe(staffManagementImage, false);
            SetActiveSafe(hireStaffTogglePanelUI, false);
            SetActiveSafe(staffManagementTogglePanelUI, true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // HELPER
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bezpečné SetActive – ošetrí null referenciu a vyhne sa zbytočnému
    /// volaniu (a tým aj zbytočnému OnEnable/OnDisable), ak je stav rovnaký.
    /// </summary>
    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }
}
