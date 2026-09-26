using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// StaffManagementListView
/// ─────────────────────────────────────────────────────────────────────────
/// Výpis VŠETKÝCH aktívnych tovární do scrollovacieho zoznamu "ListScroll"
/// v paneli "StaffManagementTogglePanelUI".
///
/// MODEL ZOBRAZENIA:
///   ListScroll je štandardný uGUI Scroll View (ScrollRect) so štruktúrou
///   Viewport → Content. Každý JEDEN riadok výpisu = nový GameObject s
///   TMP_Text, ktorý sa naklonuje z prefabu a priradí pod "Content". Vďaka
///   tomu ListScroll obsah prirodzene scrolluje.
///
///   Pri každom otvorení panela (OnEnable) sa staré riadky zmažú a vytvoria
///   sa nanovo z aktuálneho stavu hry.
///
/// "Aktívna továreň" = existuje jej FactoryInstance na tile mape
/// (FactoryRegistry.All) A má OccupancyFlag == true.
///
/// FORMÁT (zo zadania):
///   Hlavička (1. riadok): "ID | Factory Name | Employee | Salary | Map Color | Factory Price"
///   Dátový riadok:        "1. Coal Mine | Miner | Low | {štvorček s farbou} | 5000 CR"
///     - "ID" v riadku = PORADOVÉ číslo vo výpise (1, 2, 3 …), nie ID továrne.
///     - "Map Color"   = farebný štvorček z Definition.MapColor cez rich-text
///                       <color=#RRGGBB>■</color> (škáluje sa s veľkosťou fontu).
///     - "Factory Price" = Definition.Cost + " CR".
///
/// ─────────────────────────────────────────────────────────────────────────
/// NASTAVENIE V UNITY (jednorazovo):
///   1. Tento komponent priraď na GameObject "StaffManagementTogglePanelUI"
///      (alebo na samotný ListScroll – kde sa ti hodí).
///   2. Do poľa "Content" priraď child "Content" z ListScroll.
///   3. Na "Content" pridaj:
///        • Vertical Layout Group  (Control Child Size → Width = ON,
///          Child Force Expand → podľa chuti)
///        • Content Size Fitter    (Vertical Fit = Preferred Size)
///      Týmto sa riadky ukladajú pod seba a Content rastie → ScrollRect scrolluje.
///   4. Do poľa "Row Prefab" priraď prefab s komponentom TMP_Text (jeden
///      riadok – nastav font, veľkosť, zarovnanie podľa seba). Šírku riadku
///      riadi Vertical Layout Group, takže ju v prefabe riešiť netreba.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StaffManagementListView : MonoBehaviour
{
    [Header("ListScroll → Content (rodič riadkov)")]
    [Tooltip("RectTransform child \"Content\" z ListScroll (ScrollRect). " +
             "Sem sa vkladajú jednotlivé TMP_Text riadky.")]
    [SerializeField] private RectTransform content;

    [Header("Prefab jedného riadku")]
    [Tooltip("Prefab s komponentom TMP_Text – jeden riadok výpisu. " +
             "Naklonuje sa pre hlavičku aj pre každú továreň.")]
    [SerializeField] private TMP_Text rowPrefab;

    [Header("Vzhľad")]
    [Tooltip("Znak farebného štvorčeka. \"■\" (U+25A0) = menší štvorec, " +
             "\"█\" (U+2588) = plná výška fontu.")]
    [SerializeField] private string squareGlyph = "■";

    [Tooltip("Zvýrazniť riadok hlavičky tučným (rich-text <b>).")]
    [SerializeField] private bool boldHeader = true;

    [Tooltip("Riadok zobrazený, ak nie je žiadna aktívna továreň.")]
    [SerializeField] private string emptyMessage = "» no active factories with employees «";

    // Hlavička tabuľky presne podľa zadania.
    private const string Header =
        "ID | Factory Name | Employee | Salary | Map Color | Factory Price";

    /// <summary>
    /// Názvy zamestnancov indexované podľa FIXNÉHO ID typu továrne
    /// (FactoryDefinition.ID / FactoryInstance.ID), 0..15. Poradie zodpovedá
    /// FactoryDatabase aj zadaniu.
    /// </summary>
    private static readonly string[] EmployeeNamesByFactoryID =
    {
        "Miner",                   // 0  Coal Mine
        "Woodcutter",              // 1  Forest
        "Iron Miner",              // 2  Iron Ore Mine
        "Gold Miner",              // 3  Gold Mine
        "Silver Miner",            // 4  Silver Mine
        "Farmer",                  // 5  Farm
        "Oil Driller",             // 6  Oil Wells
        "Power Plant Operator",    // 7  Power Station
        "Sawyer",                  // 8  Sawmill
        "Refinery Operator",       // 9  Oil Refinery
        "Electronics Technician",  // 10 Electronics Factory
        "Carpenter",               // 11 Furniture Factory
        "Butcher",                 // 12 Slaughterhouse
        "Miller",                  // 13 Grain Factory
        "Metallurgist",            // 14 Smelter
        "Glassmaker",              // 15 Glass Factory
    };

    // Riadky, ktoré sme vytvorili (aby sme ich vedeli pri refreshi zmazať).
    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    // Pri každom otvorení panela (toggle button) sa zoznam prepočíta nanovo.
    private void OnEnable()
    {
        Refresh();
    }

    /// <summary>
    /// Zmaže staré riadky a vytvorí nové z aktuálneho stavu hry.
    /// Verejná, aby sa dala zavolať aj ručne (napr. po zmene obsadenosti).
    /// </summary>
    public void Refresh()
    {
        if (content == null || rowPrefab == null)
        {
            Debug.LogWarning("[StaffManagementListView] Nie je priradený Content " +
                             "alebo Row Prefab – výpis sa nevykreslí.");
            return;
        }

        ClearRows();

        // 1) Hlavička
        AddRow(boldHeader ? $"<b>{Header}</b>" : Header);

        // 2) Dátové riadky – len obsadené (OccupancyFlag) položené továrne.
        int order = 0;
        foreach (FactoryInstance inst in FactoryRegistry.All)
        {
            if (inst == null || inst.Definition == null) continue;
            if (!inst.OccupancyFlag) continue;

            order++;
            AddRow(FormatRow(order, inst));
        }

        // 3) Ak nič, doplň informačný riadok.
        if (order == 0)
            AddRow(emptyMessage);
    }

    // ─────────────────────────────────────────────────────────────────────
    // RIADKY
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Zmaže všetky predtým vytvorené riadky.</summary>
    private void ClearRows()
    {
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null)
                Destroy(spawnedRows[i]);
        }
        spawnedRows.Clear();
    }

    /// <summary>Naklonuje rowPrefab pod Content a nastaví mu text.</summary>
    private void AddRow(string text)
    {
        TMP_Text row = Instantiate(rowPrefab, content);
        row.gameObject.SetActive(true);
        row.richText = true;          // pre istotu – farebný štvorček a <b>
        row.text = text;

        spawnedRows.Add(row.gameObject);
    }

    /// <summary>Naformátuje jeden dátový riadok podľa vzoru zo zadania.</summary>
    private string FormatRow(int order, FactoryInstance inst)
    {
        FactoryDefinition def = inst.Definition;

        string factoryName = def.Name;
        string employee = GetEmployeeName(inst.ID);
        string salary = SalaryLevelToText(inst.LevelSalary);
        string colorSquare = ColorSquare(def.MapColor);
        int price = def.Cost;

        // "1. Coal Mine | Miner | Low | {štvorček} | 5000 CR"
        return $"{order}. {factoryName} | {employee} | {salary} | {colorSquare} | {price} CR";
    }

    // ─────────────────────────────────────────────────────────────────────
    // HELPERY
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Farebný štvorček z Unity Color cez rich-text.</summary>
    private string ColorSquare(Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGB(color); // "RRGGBB"
        return $"<color=#{hex}>{squareGlyph}</color>";
    }

    /// <summary>Názov zamestnanca podľa ID typu továrne (s ochranou rozsahu).</summary>
    private static string GetEmployeeName(int factoryID)
    {
        if (factoryID >= 0 && factoryID < EmployeeNamesByFactoryID.Length)
            return EmployeeNamesByFactoryID[factoryID];
        return "-";
    }

    /// <summary>
    /// Prevod mzdového levelu (FactoryInstance.LevelSalary) na text.
    /// Zhoduje sa s náborovými panelmi: 0 = Low, 1 = Medium, 2 = High.
    /// </summary>
    private static string SalaryLevelToText(short level)
    {
        switch (level)
        {
            case 0: return "Low";
            case 1: return "Medium";
            case 2: return "High";
            default: return level < 0 ? "Low" : "High";
        }
    }
}
