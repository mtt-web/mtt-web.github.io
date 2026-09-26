using System;
using UnityEngine;


/// <summary>
/// Celoherný herný čas. Beží ako samostatný singleton (rovnaký vzor ako
/// TrainSystem / VehicleSystem / IndicatrixAPI), takže sa naň dá kdekoľvek
/// odkázať cez <see cref="GameClock.instance"/>.
///
/// Model:
///   - dátum drží Year / Month (1..12) / Day (1..počet dní v mesiaci),
///   - štart je definovaný v Inspectore (default Január 1950),
///   - rýchlosť: <see cref="realSecondsPerGameDay"/> reálnych sekúnd = 1 herný deň
///     (default 3 s/deň podľa zadania).
///
/// Zobrazenie ("January, 1950") rieši view (GameMenuUI) – GameClock mu len
/// dáva naformátovaný reťazec cez <see cref="GetFormattedDate"/> a notifikuje
/// ho eventom <see cref="OnDateChanged"/> vždy, keď sa zmení mesiac alebo rok
/// (teda vždy, keď sa zmení viditeľný text). Zmeny dní vnútri mesiaca event
/// nevyvolávajú – zbytočne by prekresľovali rovnaký text.
/// </summary>
public class GameClock : MonoBehaviour
{
    public static GameClock instance;

    [Header("Štart dátumu")]
    [Tooltip("Počiatočný rok hry (default 1950).")]
    [SerializeField] private int startYear = 1950;

    [Tooltip("Počiatočný mesiac hry: 1 = Január ... 12 = December (default 1).")]
    [SerializeField, Range(1, 12)] private int startMonth = 1;

    [Header("Rýchlosť času")]
    [Tooltip("Koľko REÁLNYCH sekúnd trvá jeden HERNÝ deň. Default 3 (3 s = 1 deň).")]
    [SerializeField] private float realSecondsPerGameDay = 3f;

    // -----------------------------------------------------------------
    // VEREJNÝ STAV (read-only zvonku)
    // -----------------------------------------------------------------
    public int Year { get; private set; }
    public int Month { get; private set; }   // 1..12
    public int Day { get; private set; }     // 1..DaysInMonth

    /// <summary>
    /// Celkový počet UPLYNUTÝCH herných dní od štartu hry (monotónne rastie).
    /// Slúži ako jednoduchý "tik" pre systémy, ktoré bežia v hernom čase
    /// (napr. EconomySystem – produkcia tovární a odpočet servisných intervalov).
    /// </summary>
    public long TotalDaysElapsed { get; private set; }

    /// <summary>
    /// Aktuálny herný dátum ako <see cref="DateTime"/> (Year-Month-Day).
    /// Používa sa na výpočty veku dopravných prostriedkov (servisná životnosť
    /// v rokoch cez <c>CurrentDate.AddYears(...)</c>, servisný interval v dňoch).
    /// </summary>
    public DateTime CurrentDate => new DateTime(Year, Month, Day);

    /// <summary>
    /// Vyvolá sa pri štarte hry (okamžité prvé zobrazenie) a následne vždy,
    /// keď sa zmení mesiac alebo rok.
    /// </summary>
    public event Action OnDateChanged;

    /// <summary>
    /// Vyvolá sa práve raz za každý UPLYNUTÝ herný DEŇ (po posune dátumu o deň).
    /// Pri väčšom lagu (viac dní naraz) sa vyvolá pre každý zmeškaný deň.
    /// </summary>
    public event Action OnDayElapsed;

    /// <summary>
    /// Vyvolá sa práve raz za každý UPLYNUTÝ herný MESIAC (pri prechode do nového
    /// mesiaca, prípadne aj roka). Slúži pre mesačné ekonomické odpočty.
    /// </summary>
    public event Action OnMonthElapsed;

    // Anglické názvy mesiacov (zadanie: čas v angličtine).
    private static readonly string[] MonthNames =
    {
        "January", "February", "March",     "April",   "May",      "June",
        "July",    "August",   "September", "October", "November", "December"
    };

    private float dayTimer;

    void Awake()
    {
        instance = this;

        Year = startYear;
        Month = Mathf.Clamp(startMonth, 1, 12);
        Day = 1;
        dayTimer = 0f;
        TotalDaysElapsed = 0;
    }

    void Start()
    {
        // Okamžité zobrazenie po spustení hry. (View si stav vie prečítať aj
        // sám vo svojom Start – tento event je len pre prípadných ďalších
        // poslucháčov a istotu, ak by view nabehol skôr.)
        OnDateChanged?.Invoke();
    }

    void Update()
    {
        if (realSecondsPerGameDay <= 0f) return;   // ochrana pred delením/zaseknutím

        dayTimer += Time.deltaTime;

        bool monthOrYearChanged = false;

        // while (nie if) – ak by hra na chvíľu "zamrzla" a deltaTime bola
        // veľká, dobehneme všetky zmeškané dni naraz.
        while (dayTimer >= realSecondsPerGameDay)
        {
            dayTimer -= realSecondsPerGameDay;

            bool rolledToNewMonth = AdvanceOneDay();

            // Každý uplynutý herný DEŇ – tik pre poslucháčov (produkcia tovární,
            // odpočet servisných intervalov vozidiel atď.).
            TotalDaysElapsed++;
            OnDayElapsed?.Invoke();

            if (rolledToNewMonth)
            {
                monthOrYearChanged = true;

                // Každý uplynutý herný MESIAC – tik pre mesačné ekonomické
                // odpočty (mzdy zamestnancov, prevádzkové náklady vozidiel).
                OnMonthElapsed?.Invoke();
            }
        }

        if (monthOrYearChanged)
            OnDateChanged?.Invoke();
    }

    /// <summary>
    /// Posunie herný čas o jeden deň. Vráti true, ak sa pritom zmenil mesiac
    /// alebo rok (čo je signál pre view, aby prekreslil text).
    /// </summary>
    private bool AdvanceOneDay()
    {
        Day++;

        int daysInMonth = DateTime.DaysInMonth(Year, Month);   // rieši aj priestupné roky
        if (Day > daysInMonth)
        {
            Day = 1;
            Month++;
            if (Month > 12)
            {
                Month = 1;
                Year++;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Naformátovaný herný dátum v tvare "Month, Year", napr. "January, 1950".
    /// </summary>
    public string GetFormattedDate()
    {
        return $"{MonthNames[Month - 1]}, {Year}";
    }

    /// <summary>
    /// Nastaví herný dátum z uloženej hry. Zladí deň/mesiac/rok, vynuluje
    /// medzičas do ďalšieho dňa a notifikuje view (OnDateChanged).
    /// </summary>
    public void LoadDate(int year, int month, int day, long totalDaysElapsed)
    {
        Year = year;
        Month = Mathf.Clamp(month, 1, 12);

        int daysInMonth = System.DateTime.DaysInMonth(Year, Month);
        Day = Mathf.Clamp(day, 1, daysInMonth);

        TotalDaysElapsed = totalDaysElapsed < 0 ? 0 : totalDaysElapsed;
        dayTimer = 0f;

        OnDateChanged?.Invoke();
    }
}
