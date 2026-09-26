using System;
using System.Globalization;
using System.Text;
using UnityEngine;


/// <summary>
/// BudgetSystem
/// ============================================================================
/// ROČNÁ ÚČTOVNÁ KNIHA hry. Samostatný singleton (rovnaký vzor ako GameClock /
/// GameEconomy / EconomySystem / TrainSystem / VehicleSystem) – stačí ho hodiť
/// na ľubovoľný GameObject v scéne. Zbiera počas CELÉHO herného roka všetky
/// PRÍJMY a VÝDAVKY z konta a na začiatku každého nového roka z nich zostaví
/// "ročnú účtovnú uzávierku" a zobrazí ju hráčovi v okne <see cref="StatusBudgetMenuUI"/>.
///
/// ── ČO SA SLEDUJE (per herný rok) ─────────────────────────────────────────
///   PRÍJMY:
///     • Tržby z prepravy – výnos z úspešných obchodných transakcií vlakov aj
///       vozidiel (RecordRevenue, volané z TrainSystem / VehicleSystem).
///   VÝDAVKY:
///     • Kúpené vlaky    – počet + súčet zaplatenej ceny (RecordTrainPurchase),
///     • Kúpené vozidlá  – počet + súčet ceny (RecordVehiclePurchase),
///     • Kúpené továrne  – počet + súčet ceny (RecordFactoryPurchase),
///     • Prevádzka vlakov   – súčet OperatingCosts všetkých vlakov za rok,
///     • Prevádzka vozidiel – súčet OperatingCosts všetkých vozidiel za rok,
///     • Mzdy zamestnancov  – súčet miezd tovární (EmployeeSalary × LevelSalary).
///   Prevádzkové náklady a mzdy hlási RAZ ZA MESIAC <see cref="EconomySystem"/>
///   cez <see cref="RecordMonthlySettlement"/> – BudgetSystem ich len kumuluje.
///
/// ── KEDY SA OKNO ZOBRAZÍ ───────────────────────────────────────────────────
/// Na ZAČIATKU každého herného roka (po prechode z Decembra do Januára). Spúšťa
/// to <see cref="EconomySystem"/>, ktorý hranicu roka deteguje sám a PO zúčtovaní
/// posledného (decembrového) mesiaca zavolá <see cref="CloseYearAndShow"/>. Tým
/// je zaručené, že v uzávierke je celý rok vrátane decembra. Prvá uzávierka sa
/// teda objaví na začiatku DRUHÉHO herného roka (rekapituluje prvý odohraný rok).
///
/// Samotný BudgetSystem nič neodpočítava z konta – je to len EVIDENCIA. Zmeny
/// konta robia pôvodné systémy (GameEconomy cez build/refund/revenue/mesačné
/// odpočty); tu sa tie isté sumy len zaznamenávajú na reporting.
/// </summary>
public class BudgetSystem : MonoBehaviour
{
    public static BudgetSystem instance;

    // -----------------------------------------------------------------
    // ROČNÁ KNIHA – akumulátor aktuálne prebiehajúceho roka
    // -----------------------------------------------------------------

    /// <summary>
    /// Jeden súhrnný záznam za herný rok. Všetky sumy sú KLADNÉ magnitúdy v CR
    /// (znamienko/farbu rieši až formátovanie výkazu). Príjem aj výdavky sa
    /// držia osobitne, aby sa dal vyčísliť výsledok hospodárenia.
    /// </summary>
    private class YearLedger
    {
        // PRÍJMY
        public long Revenue;            // tržby z prepravy (vlaky + vozidlá)

        // VÝDAVKY – jednorazové nákupy
        public int TrainsBought; public long TrainsCost;
        public int VehiclesBought; public long VehiclesCost;
        public int FactoriesBought; public long FactoriesCost;

        // VÝDAVKY – opakované (mesačné, kumulované za rok)
        public long TrainOperating;     // súčet OperatingCosts vlakov za rok
        public long VehicleOperating;   // súčet OperatingCosts vozidiel za rok
        public long EmployeeSalaries;   // súčet miezd zamestnancov tovární za rok

        /// <summary>Súčet všetkých výdavkov roka (kladná suma).</summary>
        public long TotalExpenses =>
            TrainsCost + VehiclesCost + FactoriesCost +
            TrainOperating + VehicleOperating + EmployeeSalaries;

        /// <summary>
        /// Výsledok hospodárenia = príjmy − výdavky. Kladné = zisk, záporné = strata.
        /// </summary>
        public long NetResult => Revenue - TotalExpenses;

        public void Reset()
        {
            Revenue = 0;
            TrainsBought = 0; TrainsCost = 0;
            VehiclesBought = 0; VehiclesCost = 0;
            FactoriesBought = 0; FactoriesCost = 0;
            TrainOperating = 0;
            VehicleOperating = 0;
            EmployeeSalaries = 0;
        }
    }

    private readonly YearLedger current = new YearLedger();

    // -----------------------------------------------------------------
    // FARBY VÝKAZU (rich text TMP) – kladné zelenou, záporné červenou
    // -----------------------------------------------------------------
    private const string ColorPositive = "#2ECC71";   // zelená (+)
    private const string ColorNegative = "#E74C3C";   // červená (−)

    // Kultúra s BODKOVÝM oddeľovačom tisícok (napr. "5.478"), nezávislá od
    // lokalizácie OS – rovnaký princíp ako v GameEconomy.
    private static readonly CultureInfo DotGroupingCulture = CreateDotGroupingCulture();

    // Lazy referencia na okno uzávierky (môže byť v scéne neaktívne – pri štarte
    // sa skrýva v Start()), rovnako ako GameManager hľadá StatusErrorMenuUI.
    private StatusBudgetMenuUI _budgetMenu;
    private StatusBudgetMenuUI BudgetMenu
    {
        get
        {
            if (_budgetMenu == null)
                _budgetMenu = UnityEngine.Object.FindFirstObjectByType<StatusBudgetMenuUI>(FindObjectsInactive.Include);
            return _budgetMenu;
        }
    }

    void Awake()
    {
        instance = this;
        current.Reset();
    }

    // =====================================================================
    // READ-ONLY PRÍSTUP PRE HUD (GameMenuHUDPanel) – priebežný stav roka
    // =====================================================================

    /// <summary>Tržby aktuálneho (ešte neuzavretého) roka.</summary>
    public long YearRevenue => current.Revenue;

    /// <summary>Súčet výdavkov aktuálneho roka (kladná suma).</summary>
    public long YearExpenses => current.TotalExpenses;

    /// <summary>Výsledok hospodárenia aktuálneho roka (príjmy − výdavky).</summary>
    public long YearNetResult => current.NetResult;

    // =====================================================================
    // VEREJNÉ API – ZÁZNAM PRÍJMOV A VÝDAVKOV
    //
    // Volá sa z miest, kde reálne dochádza k zmene konta (aby evidencia sedela
    // s tým, čo sa naozaj minulo / zarobilo). Všetky metódy sú odolné voči
    // nulovým/záporným vstupom.
    // =====================================================================

    /// <summary>Tržba z úspešnej obchodnej transakcie (vlak alebo vozidlo).</summary>
    public void RecordRevenue(long amount)
    {
        if (amount <= 0) return;
        current.Revenue += amount;
    }

    /// <summary>Nákup jedného vlaku za danú cenu.</summary>
    public void RecordTrainPurchase(long cost)
    {
        current.TrainsBought++;
        if (cost > 0) current.TrainsCost += cost;
    }

    /// <summary>Nákup jedného vozidla za danú cenu.</summary>
    public void RecordVehiclePurchase(long cost)
    {
        current.VehiclesBought++;
        if (cost > 0) current.VehiclesCost += cost;
    }

    /// <summary>Nákup (postavenie) jednej továrne za danú cenu.</summary>
    public void RecordFactoryPurchase(long cost)
    {
        current.FactoriesBought++;
        if (cost > 0) current.FactoriesCost += cost;
    }

    /// <summary>
    /// Mesačné zúčtovanie z <see cref="EconomySystem"/>: súčty prevádzkových
    /// nákladov flotily a miezd zamestnancov za PRÁVE UPLYNULÝ mesiac. Kumuluje
    /// sa do ročnej knihy. (Volá sa raz za herný mesiac, na hranici mesiaca.)
    /// </summary>
    public void RecordMonthlySettlement(long employeeSalaries, long trainOperating, long vehicleOperating)
    {
        if (employeeSalaries > 0) current.EmployeeSalaries += employeeSalaries;
        if (trainOperating > 0) current.TrainOperating += trainOperating;
        if (vehicleOperating > 0) current.VehicleOperating += vehicleOperating;
    }

    // =====================================================================
    // ROČNÁ UZÁVIERKA – zostav výkaz, zobraz okno a vynuluj knihu
    // =====================================================================

    /// <summary>
    /// Uzavrie práve skončený herný rok: z nazbieraných dát zostaví textový
    /// výkaz, otvorí okno <see cref="StatusBudgetMenuUI"/> a knihu vynuluje pre
    /// nový rok. Volá <see cref="EconomySystem"/> na hranici roka (PO zúčtovaní
    /// decembra), <paramref name="closedYear"/> je rok, ktorý sa uzatvára.
    /// </summary>
    public void CloseYearAndShow(int closedYear)
    {
        string report = BuildReport(closedYear);

        var menu = BudgetMenu;
        if (menu != null)
        {
            GameManager.instance?.PlaySfxAnnualReport();
            menu.OpenWithReport(report);
        }
        else
            Debug.LogWarning("[BudgetSystem] StatusBudgetMenuUI nie je v scéne – " +
                             $"ročná uzávierka {closedYear} sa nezobrazila.");

        current.Reset();   // nový rok začína s čistou knihou
    }

    // =====================================================================
    // ZOSTAVENIE TEXTU VÝKAZU
    // =====================================================================

    /// <summary>
    /// Zostaví rich-text výkaz do jediného caption-u (BudgetTextCaption). Kladné
    /// sumy sú zelené (+), výdavky a strata červené (−). Hodnoty sú v stĺpci
    /// (TMP <pos=62%>), tisícky oddelené bodkou, mena "CR".
    /// </summary>
    private string BuildReport(int closedYear)
    {
        var sb = new StringBuilder(512);

        // ── Hlavička ──
        // Titulok nesie DÁTUM uzávierky = 1. januára nového roka (rok, do ktorého
        // hra práve vstúpila). Napr. po uzavretí roka 1950 sa zobrazí "1.1.1951".
        // Podriadok upresňuje, ZA KTORÝ rok je výkaz (uzatváraný rok = closedYear).
        int closingYear = closedYear + 1;
        sb.Append($"<size=120%>ANNUAL ACCOUNTS - YEAR 1.1.{closingYear}</size>\n");
        sb.Append($"Income and expenditure statement for the year {closedYear}\n\n");

        // ── PRÍJMY ──
        sb.Append("INCOME\n");
        sb.Append(Line("Transportation revenue", +current.Revenue));
        sb.Append('\n');

        // ── VÝDAVKY ──
        sb.Append("EXPENSES\n");
        sb.Append(Line($"Trains purchased ({current.TrainsBought})", -current.TrainsCost));
        sb.Append(Line($"Purchased vehicles ({current.VehiclesBought})", -current.VehiclesCost));
        sb.Append(Line($"Purchased factories ({current.FactoriesBought})", -current.FactoriesCost));
        sb.Append(Line("Train operations", -current.TrainOperating));
        sb.Append(Line("Vehicle operations", -current.VehicleOperating));
        sb.Append(Line("Employee salaries", -current.EmployeeSalaries));
        sb.Append('\n');

        // ── VÝSLEDOK HOSPODÁRENIA ──
        long net = current.NetResult;
        string resultLabel = net >= 0 ? "OPERATING RESULT (profit)"
                                       : "OPERATING RESULT (loss)";
        sb.Append("");
        sb.Append(Line(resultLabel, net));
        sb.Append("");

        return sb.ToString();
    }

    /// <summary>
    /// Jeden riadok výkazu: "popis" vľavo, farebne ofarbená suma v stĺpci vpravo.
    /// <paramref name="signedAmount"/> nesie ZNAMIENKO príspevku na konto
    /// (príjem kladný, výdavok záporný) – podľa neho sa volí farba aj znak.
    /// </summary>
    private static string Line(string label, long signedAmount)
        => $"{label}<pos=62%>{ColoredAmount(signedAmount)}\n";

    /// <summary>
    /// Naformátuje sumu ako "+5.478 CR" (zelená) alebo "-1.477 CR" (červená).
    /// Nula sa zobrazí neutrálne ako "0 CR" bez znamienka.
    /// </summary>
    private static string ColoredAmount(long signedAmount)
    {
        if (signedAmount == 0)
            return "0 " + CurrencySuffix;

        string color = signedAmount > 0 ? ColorPositive : ColorNegative;
        string sign = signedAmount > 0 ? "+" : "-";
        string magnitude = Math.Abs(signedAmount).ToString("#,0", DotGroupingCulture);

        return $"<color={color}>{sign}{magnitude} {CurrencySuffix}</color>";
    }

    private const string CurrencySuffix = "CR";

    private static CultureInfo CreateDotGroupingCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.NumberGroupSizes = new[] { 3 };
        return culture;
    }

    /// <summary>Zapíše akumulátor prebiehajúceho roka do save streamu.</summary>
    public void WriteSave(System.IO.BinaryWriter bw)
    {
        bw.Write(current.Revenue);
        bw.Write(current.TrainsBought); bw.Write(current.TrainsCost);
        bw.Write(current.VehiclesBought); bw.Write(current.VehiclesCost);
        bw.Write(current.FactoriesBought); bw.Write(current.FactoriesCost);
        bw.Write(current.TrainOperating);
        bw.Write(current.VehicleOperating);
        bw.Write(current.EmployeeSalaries);
    }

    /// <summary>Načíta akumulátor prebiehajúceho roka zo save streamu.</summary>
    public void ReadSave(System.IO.BinaryReader br)
    {
        current.Revenue = br.ReadInt64();
        current.TrainsBought = br.ReadInt32(); current.TrainsCost = br.ReadInt64();
        current.VehiclesBought = br.ReadInt32(); current.VehiclesCost = br.ReadInt64();
        current.FactoriesBought = br.ReadInt32(); current.FactoriesCost = br.ReadInt64();
        current.TrainOperating = br.ReadInt64();
        current.VehicleOperating = br.ReadInt64();
        current.EmployeeSalaries = br.ReadInt64();
    }
}
