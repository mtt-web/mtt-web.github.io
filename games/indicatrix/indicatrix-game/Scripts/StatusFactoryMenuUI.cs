using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;   // Button (StaffLayoffButton)
using TMPro;

/// <summary>
/// StatusFactoryMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie INFORMÁCIÍ o konkrétnej položenej továrni.
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusFactoryUIwindow (analogicky k dvojici
///     DepotRailConstructionMenuUI + DepotRailConstructionUIwindow).
///  1. Okno sa otvorí kliknutím na ľubovoľný tile, ktorý patrí do footprintu
///     niektorej továrne (volá GameManager → StatusFactoryMenuUI.OpenForFactory).
///     Mimo toho je okno skryté.
///  2. Zobrazuje tri informačné captiony o aktuálne vybranej továrni:
///       • FactoryNameTextCaption   – názov továrne, napr. "Coal Mine".
///       • FactoryLoadTextCaption   – príjmový (Load) slot vo formáte
///         "surovina, množstvo, max množstvo", napr. "Coal, 0, 300".
///       • FactoryUnLoadTextCaption – výdajový (UnLoad) slot v rovnakom
///         formáte, napr. "Coal, 72, 300".
///
/// POZN. K UI PRVKOM:
///   Okno obsahuje 6 textových prvkov – tri "popisky" (FactoryNameText,
///   FactoryLoadText, FactoryUnLoadText) a tri "captiony"
///   (FactoryNameTextCaption, FactoryLoadTextCaption, FactoryUnLoadTextCaption).
///   Popisky sú STATICKÉ nadpisy nastavené v Inspectore ("Factory Name:",
///   "Factory Load:", "Factory UnLoad:") – tento script ich nemení, preto
///   na ne ani nedrží referenciu. Meniteľné sú len tri captiony nižšie.
///
/// DÁTOVÝ ZDROJ:
///   Obsah captionov pochádza z FactoryInstance (FactorySystem.cs):
///     • Name           → FactoryNameTextCaption
///     • Load[0] slot   → FactoryLoadTextCaption
///     • UnLoad[0] slot  → FactoryUnLoadTextCaption
///   Tile mapa (IndicatrixAPI) nesie len textúru/tileID – ekonomické dáta
///   (názov, sklady, kapacity) žijú vo FactoryInstance / FactoryRegistry.
///
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusFactoryMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusFactoryMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusFactoryMenuUIPanel;

    [Header("Factory Info Captions")]
    [Tooltip("FactoryNameTextCaption – zobrazuje názov vybranej továrne, napr. \"Coal Mine\".")]
    [SerializeField] private TMP_Text factoryNameTextCaption;

    [Tooltip("FactoryLoadTextCaption – príjmový slot vo formáte \"surovina, množstvo, max množstvo\".")]
    [SerializeField] private TMP_Text factoryLoadTextCaption;

    [Tooltip("FactoryUnLoadTextCaption – výdajový slot vo formáte \"surovina, množstvo, max množstvo\".")]
    [SerializeField] private TMP_Text factoryUnLoadTextCaption;

    [Header("Factory Staff")]
    [Tooltip("StaffTextCaption – stav personálu. Pri OccupancyFlag == false zobrazí " +
             "\"no staff\", pri OccupancyFlag == true zobrazí \"ID - LevelSalary\" (napr. \"0 - 1\").")]
    [SerializeField] private TMP_Text staffTextCaption;

    [Tooltip("StaffLayoffButton – prepustenie personálu. Enabled (interactable) len " +
             "pri OccupancyFlag == true. Po kliknutí nastaví EmployeeSalary = 0 a OccupancyFlag = false.")]
    [SerializeField] private Button staffLayoffButton;

    // Text v StaffTextCaption, keď továreň nemá obsadený personál
    // (OccupancyFlag == false).
    private const string NO_STAFF_LABEL = "» no staff «";

    // Text v Load/UnLoad caption, keď továreň daný zoznam slotov nemá
    // (Count == 0). Napr. Coal Mine nič neprijíma → Load caption = "None".
    // Power Station nič nevydáva → UnLoad caption = "None".
    private const string NO_SLOT_LABEL = "None";

    // =====================================================================
    // AKTUÁLNY STAV
    // =====================================================================

    // Továreň, pre ktorú je okno otvorené. null = okno zavreté.
    private FactoryInstance currentFactory = null;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v DepotRailConstructionMenuUI): na rovnakom
        // GameObjecte (root paneli) je pripojený aj sprievodný script
        // StatusFactoryUIwindow, ktorý si v svojom Awake() registruje
        // listener na Close (X) tlačidlo. Poradie volania Awake() medzi
        // viacerými skriptami na tom istom GameObjecte je v Unity
        // nedeterministické (Script Execution Order). Ak by tento Awake()
        // zbehol PRV a hneď panel deaktivoval, druhý script by svoj Awake()
        // už nemusel stihnúť – Unity nevolá Awake() na neaktívnych
        // GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania v scéne.

        // Listener na StaffLayoffButton registrujeme tu v Awake().
        // AddListener funguje aj na (zatiaľ) neaktívnom GameObjecte, takže to
        // nezávisí od toho, či je panel aktívny – a panel v Awake() ani
        // nedeaktivujeme (to robí až Start()). Samotné enable/disable
        // (interactable) tlačidla rieši RefreshFactoryInfo() podľa OccupancyFlag.
        if (staffLayoffButton != null)
            staffLayoffButton.onClick.AddListener(OnStaffLayoffClicked);
    }

    void OnDestroy()
    {
        // Upraceme za sebou – odregistrujeme listener, aby pri zničení okna
        // neostala visieť referencia na túto inštanciu.
        if (staffLayoffButton != null)
            staffLayoffButton.onClick.RemoveListener(OnStaffLayoffClicked);
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusFactoryUIwindow (close button listener) už
        // zbehol. Okno sa otvorí až po kliku na továreň cez OpenForFactory().
        if (statusFactoryMenuUIPanel != null)
            statusFactoryMenuUIPanel.SetActive(false);
    }

    void Update()
    {
        // ŽIVÁ AKTUALIZÁCIA OTVORENÉHO OKNA
        // ─────────────────────────────────────────────────────────────────
        // RefreshFactoryInfo() pôvodne bežal len raz – v momente kliknutia na
        // továreň (OpenForFactory). Sklady (amount), personál či LevelSalary sa
        // však menia aj POČAS toho, ako je okno otvorené (napr. vlak priebežne
        // nakladá/vykladá surovinu). Aby okno neukazovalo "zamrznutú" snímku z
        // okamihu kliknutia, kým je otvorené (currentFactory != null) priebežne
        // prepočítavame captiony z aktuálneho stavu FactoryInstance.
        //
        // POZN. K VÝKONU: RefreshFactoryInfo() pri každom snímku alokuje nové
        // reťazce (StringBuilder vo FormatSlots). Pre jedno informačné okno je
        // to zanedbateľné. Ak by to niekedy prekážalo, dá sa to obmedziť na
        // refresh pri zmene hodnoty alebo na interval (napr. raz za 0.25 s).
        if (currentFactory != null)
            RefreshFactoryInfo();
    }

    // =====================================================================
    // PUBLIC API – volá GameManager pri kliknutí na tile továrne
    // =====================================================================

    /// <summary>
    /// Otvorí okno pre danú továreň a naplní informačné captiony.
    ///
    /// Volá GameManager po tom, čo klik myšou na tile mapu trafil tile
    /// patriaci do footprintu niektorej továrne (FactoryRegistry.GetFactoryAt).
    /// Footprint NETREBA riešiť tu – register mapuje KAŽDÝ tile footprintu
    /// na tú istú FactoryInstance, takže klik na ľubovoľný tile 2×3 / 3×3 /
    /// 2×2 továrne vedie k tomuto volaniu s rovnakou inštanciou.
    /// </summary>
    /// <param name="factory">Inštancia továrne, ktorej info sa má zobraziť.</param>
    public void OpenForFactory(FactoryInstance factory)
    {
        if (factory == null)
        {
            Debug.LogWarning("[StatusFactoryMenuUI] OpenForFactory: factory == null – okno sa neotvorí.");
            return;
        }

        currentFactory = factory;

        // Zobraz panel
        if (statusFactoryMenuUIPanel != null)
            statusFactoryMenuUIPanel.SetActive(true);

        // Naplň captiony dátami z FactoryInstance
        RefreshFactoryInfo();

        Debug.Log($"[StatusFactoryMenuUI] Otvorené pre továreň {currentFactory}.");
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusFactoryUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku (napr. pri prepnutí do iného režimu).
    /// </summary>
    public void CloseWindow()
    {
        if (statusFactoryMenuUIPanel != null)
            statusFactoryMenuUIPanel.SetActive(false);
        currentFactory = null;
    }

    // =====================================================================
    // NAPLNENIE CAPTIONOV
    // =====================================================================

    /// <summary>
    /// Prepíše tri informačné captiony podľa aktuálne vybranej továrne.
    ///
    ///   FactoryNameTextCaption   = názov továrne (Definition.Name).
    ///   FactoryLoadTextCaption   = VŠETKY Load sloty   ako "surovina, mn, max".
    ///   FactoryUnLoadTextCaption = VŠETKY UnLoad sloty ako "surovina, mn, max".
    ///
    /// Ak továreň daný zoznam slotov nemá (Load/UnLoad Count == 0), zobrazí
    /// sa NO_SLOT_LABEL ("None").
    /// </summary>
    private void RefreshFactoryInfo()
    {
        if (currentFactory == null) return;

        // --- Názov továrne -------------------------------------------------
        if (factoryNameTextCaption != null)
            factoryNameTextCaption.text = $"» {currentFactory.Name}";

        // --- Load sloty ----------------------------------------------------
        if (factoryLoadTextCaption != null)
            factoryLoadTextCaption.text = FormatSlots(currentFactory.Load);

        // --- UnLoad sloty --------------------------------------------------
        if (factoryUnLoadTextCaption != null)
            factoryUnLoadTextCaption.text = FormatSlots(currentFactory.UnLoad);

        // --- Personál ------------------------------------------------------
        RefreshStaffInfo();
    }

    /// <summary>
    /// Naplní StaffTextCaption a nastaví enable/disable (interactable) tlačidla
    /// StaffLayoffButton podľa príznaku OccupancyFlag aktuálnej továrne:
    ///
    ///   OccupancyFlag == false → StaffTextCaption = "no staff",
    ///                            StaffLayoffButton = disabled (interactable=false).
    ///   OccupancyFlag == true  → StaffTextCaption = "ID - LevelSalary" (napr. "0 - 1"),
    ///                            StaffLayoffButton = enabled (interactable=true).
    /// </summary>
    private void RefreshStaffInfo()
    {
        if (currentFactory == null) return;

        bool occupied = currentFactory.OccupancyFlag;

        if (staffTextCaption != null)
        {
            if (occupied)
            {
                staffTextCaption.text =
                    $"» {GetEmployeeName(currentFactory.ID)}\n» {GetSalaryLabel(currentFactory.LevelSalary)}";

                staffTextCaption.alignment = TMPro.TextAlignmentOptions.Left;
            }
            else
            {
                staffTextCaption.text = NO_STAFF_LABEL;
                staffTextCaption.alignment = TMPro.TextAlignmentOptions.Center;
            }
        }

        // Tlačidlo je klikateľné len keď je personál obsadený.
        if (staffLayoffButton != null)
            staffLayoffButton.interactable = occupied;
    }

    // =====================================================================
    // OBSLUHA STAFF LAYOFF BUTTONU
    // =====================================================================

    /// <summary>
    /// Obsluha kliknutia na StaffLayoffButton (prepustenie personálu).
    ///
    /// Volá sa len keď je tlačidlo enabled (interactable == true), čo nastáva
    /// výhradne pri OccupancyFlag == true (pozri RefreshStaffInfo). Pre danú
    /// inštanciu továrne nastaví EmployeeSalary = 0 a OccupancyFlag = false,
    /// následne osvieži zobrazenie (caption prejde na "no staff" a tlačidlo
    /// sa deaktivuje), aby okno zodpovedalo novému stavu.
    /// </summary>
    private void OnStaffLayoffClicked()
    {
        if (currentFactory == null) return;

        currentFactory.EmployeeSalary = 0;
        currentFactory.OccupancyFlag = false;

        // Premietni zmenu do UI (StaffTextCaption + interactable tlačidla).
        RefreshStaffInfo();

        Debug.Log($"[StatusFactoryMenuUI] Personál prepustený pre továreň {currentFactory}.");
    }

    // =====================================================================
    // ZÁSTUPNÉ NÁZVY PERSONÁLU (mirror StaffManagementListView)
    // =====================================================================
    //
    // POZN.: Názvy zamestnancov aj prevod levelu mzdy sú v StaffManagementListView
    // deklarované ako PRIVATE (EmployeeNamesByFactoryID, SalaryLevelToText), preto
    // sa na ne odtiaľto nedá priamo odkázať. Podľa zadania ten súbor neprepisujeme,
    // takže si tu držíme vlastnú (zhodnú) kópiu mapovania. Pri zmene jedného
    // zoznamu treba zosúladiť aj druhý.

    /// <summary>
    /// Názvy zamestnancov indexované podľa FIXNÉHO ID typu továrne
    /// (FactoryInstance.ID), 0..15. Poradie zodpovedá FactoryDatabase.
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

    /// <summary>
    /// Názov zamestnanca podľa ID typu továrne (s ochranou rozsahu).
    /// Mimo rozsahu vráti "-".
    /// </summary>
    private static string GetEmployeeName(int factoryID)
    {
        if (factoryID >= 0 && factoryID < EmployeeNamesByFactoryID.Length)
            return EmployeeNamesByFactoryID[factoryID];
        return "-";
    }

    /// <summary>
    /// Zostaví popisok mzdy z LevelSalary: textový level + " Salary".
    /// Level: 0 = Low, 1 = Medium, 2 = High (mimo rozsahu sa orežáva na Low/High).
    /// Napr. 0 → "Low Salary", 1 → "Medium Salary", 2 → "High Salary".
    /// </summary>
    private static string GetSalaryLabel(short level)
    {
        return $"{SalaryLevelToText(level)} Salary";
    }

    /// <summary>
    /// Prevod mzdového levelu na text (zhoduje sa s náborovými panelmi
    /// aj so StaffManagementListView): 0 = Low, 1 = Medium, 2 = High.
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

    /// <summary>
    /// Naformátuje CELÝ zoznam slotov (Load alebo UnLoad) do textu pre caption.
    ///
    /// PREČO ZOZNAM A NIE LEN PRVÝ SLOT:
    ///   FactorySystem podporuje 0, 1 alebo VIAC slotov na továreň (napr.
    ///   budúca továreň s Load "[2,0,500],[3,0,750]"). Caption preto vypíše
    ///   všetky sloty, nie len Load[0] – inak by ďalšie sloty boli skryté.
    ///
    /// FORMÁT:
    ///   • 0 slotov  → NO_SLOT_LABEL ("None").
    ///   • 1 slot    → jeden riadok "surovina, množstvo, max".
    ///   • N slotov  → N riadkov (každý slot na vlastnom riadku), čo je v
    ///                 TMP_Text čitateľnejšie než jeden dlhý zlúčený riadok.
    /// </summary>
    private static string FormatSlots(List<ResourceSlot> slots)
    {
        if (slots == null || slots.Count == 0)
            return NO_SLOT_LABEL;

        var sb = new StringBuilder();
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) sb.Append('\n');     // každý ďalší slot na nový riadok
            sb.Append($"» {FormatSlot(slots[i])}");   // šípka pred každým slotom
        }
        return sb.ToString();
    }

    /// <summary>
    /// Naformátuje jeden ResourceSlot do spojeného reťazca
    /// "surovina, množstvo, maximálne množstvo", napr. "Coal, 72, 300".
    ///
    /// KONVERZIA ČÍSLA TYPU NA TEXT:
    ///   Slot drží surovinu ako ResourceType enum (Coal = 1, Wood = 2, ...).
    ///   Číselná hodnota enumu = "evidenčné číslo" suroviny; jej textový
    ///   ekvivalent je priamo názov člena enumu (slot.type.ToString()).
    ///   Týmto je číslo typu skonvertované na čitateľný text bez ručnej
    ///   switch tabuľky – enum je jediný zdroj pravdy.
    /// </summary>
    private static string FormatSlot(ResourceSlot slot)
    {
        if (slot == null) return NO_SLOT_LABEL;
        // amount aj capacity sa formátujú s tisícovým oddeľovačom (bodka) a
        // jednotkou tona ("t"). Napr. amount=0, capacity=14000 → "0 t, 14.000 t".
        return $"{FormatResourceName(slot.type)}, {FormatTons(slot.amount)}, {FormatTons(slot.capacity)}";
    }

    /// <summary>
    /// Prevedie názov člena ResourceType na čitateľný text – pred každé veľké
    /// písmeno (okrem prvého) vloží medzeru. Napr. "IronOre" → "Iron Ore",
    /// "ElectronicsProducts" → "Electronics Products", "Coal" → "Coal".
    /// </summary>
    private static string FormatResourceName(ResourceType type)
    {
        string name = type.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    // Formát čísel pre množstvá: bodka ako oddeľovač tisícok, 0 desatinných
    // miest. Napr. 0 → "0", 1450 → "1.450", 14000 → "14.000", 17300 → "17.300".
    // CultureInvariant klon, aby výpis nezávisel od lokálneho nastavenia stroja.
    private static readonly System.Globalization.NumberFormatInfo TonsFormat =
        new System.Globalization.NumberFormatInfo
        {
            NumberGroupSeparator = ".",
            NumberGroupSizes = new[] { 3 },
            NumberDecimalDigits = 0,
        };

    /// <summary>
    /// Naformátuje celočíselné množstvo suroviny na text s tisícovým
    /// oddeľovačom (bodka) a pripojenou jednotkou tona, napr. 14000 → "14.000 t".
    /// </summary>
    private static string FormatTons(int value)
    {
        return $"{value.ToString("N0", TonsFormat)} t";
    }
}
