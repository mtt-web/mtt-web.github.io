using System;
using System.Globalization;
using UnityEngine;


/// <summary>
/// Peňažné konto hry (herný kredit). Samostatný singleton – rovnaký vzor ako
/// GameClock / TrainSystem / VehicleSystem. Dostupný kdekoľvek cez
/// <see cref="GameEconomy.instance"/>, takže ktorýkoľvek systém (napr.
/// konštrukcia v GameManager-i) vie kedykoľvek pripočítať príjem alebo
/// odpočítať náklady.
///
/// Mena: "Credits" so skratkou "CR", napr. "100.000 CR".
///   - hodnota je interne <see cref="long"/> (celé číslo SO znamienkom – konto
///     môže ísť aj do ZÁPORNEJ hodnoty cez DeductAllowNegative, napr. -3.425 CR),
///   - tisícky sa pri zobrazení oddeľujú BODKOU ("100.000"),
///   - v programe stále vystupuje ako číslo (uint), formátuje sa len pri výpise.
///
/// API:
///   AddCredits(amount)        – pripočíta príjem,
///   TrySpendCredits(amount)   – bezpečne odpočíta náklady (false ak nie je dosť),
///   CanAfford(amount)         – kontrola bez zmeny stavu,
///   SetBalance(amount)        – priame nastavenie (napr. load hry).
///
/// View (GameMenuUI) počúva na <see cref="OnBalanceChanged"/> a prekresľuje
/// caption – ekonomika sama o UI nič nevie.
/// </summary>
public class GameEconomy : MonoBehaviour
{
    public static GameEconomy instance;

    [Header("Štartovací stav konta")]
    [Tooltip("Počiatočná suma kreditov na začiatku hry (default 100000 = 100.000 CR).")]
    [SerializeField] private uint startingBalance = 100000;

    [Header("Mena")]
    [Tooltip("Skratka meny zobrazená za sumou (default 'CR').")]
    [SerializeField] private string currencySuffix = "CR";

    /// <summary>
    /// Aktuálny stav konta. Typ <see cref="long"/> (so znamienkom) – konto smie
    /// klesnúť aj do zápornej hodnoty pri pravidelných mesačných nákladoch
    /// (pozri <see cref="DeductAllowNegative"/>).
    /// </summary>
    public long Balance { get; private set; }

    /// <summary>Vyvolá sa pri štarte a pri každej zmene stavu konta.</summary>
    public event Action OnBalanceChanged;

    // Kultúra, ktorá tisícky oddeľuje bodkou – aby formátovanie nezáviselo od
    // nastavenia OS / lokalizácie počítača hráča.
    private static readonly CultureInfo DotGroupingCulture = CreateDotGroupingCulture();

    void Awake()
    {
        instance = this;
        Balance = startingBalance;
    }

    void Start()
    {
        // Okamžité zobrazenie po spustení hry.
        OnBalanceChanged?.Invoke();
    }

    // -----------------------------------------------------------------
    // VEREJNÉ API – zmena stavu konta
    // -----------------------------------------------------------------

    /// <summary>Pripočíta príjem na konto.</summary>
    public void AddCredits(uint amount)
    {
        if (amount == 0u) return;
        Balance += amount;
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Odpočíta náklady z konta. Ak na ne nie je dosť kreditov, NIČ sa nezmení
    /// a vráti false (volajúci tak vie napr. zablokovať stavbu).
    /// </summary>
    public bool TrySpendCredits(uint amount)
    {
        if (amount > Balance) return false;
        Balance -= amount;
        OnBalanceChanged?.Invoke();
        return true;
    }

    /// <summary>Kontrola, či je na konte aspoň daná suma (bez zmeny stavu).</summary>
    public bool CanAfford(uint amount) => amount <= Balance;

    /// <summary>Priame nastavenie stavu konta (napr. pri načítaní uloženej hry).</summary>
    public void SetBalance(uint amount)
    {
        Balance = amount;
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Priame nastavenie stavu konta vrátane ZÁPORNEJ hodnoty (load uloženej
    /// hry). Doplnok k SetBalance(uint), ktorý mínus nezvládne.
    /// </summary>
    public void SetBalanceSigned(long amount)
    {
        Balance = amount;
        OnBalanceChanged?.Invoke();
    }

    /// <summary>
    /// Strhne náklady z konta BEZ kontroly krytia – konto SMIE ísť do zápornej
    /// hodnoty (napr. zo -3.125 CR strhnutie 300 CR → -3.425 CR). Používa sa pre
    /// pravidelné MESAČNÉ odpočty (prevádzkové náklady vlakov/vozidiel a mzdy
    /// zamestnancov tovární), kde sa podľa zadania nedostatok financií nerieši –
    /// suma sa strhne vždy.
    ///
    /// Kladné <paramref name="amount"/> konto ZNIŽUJE. (Záporné by ho zvýšilo,
    /// ale pre príjmy radšej použi <see cref="AddCredits"/>.)
    /// </summary>
    public void DeductAllowNegative(long amount)
    {
        if (amount == 0L) return;
        Balance -= amount;
        OnBalanceChanged?.Invoke();
    }

    // -----------------------------------------------------------------
    // FORMÁTOVANIE PRE UI
    // -----------------------------------------------------------------

    /// <summary>
    /// Naformátovaný stav konta, napr. "100.000 CR" (tisícky oddelené bodkou).
    /// </summary>
    public string GetFormattedBalance()
    {
        return Balance.ToString("#,0", DotGroupingCulture) + " " + currencySuffix;
    }

    private static CultureInfo CreateDotGroupingCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.NumberGroupSizes = new[] { 3 };
        return culture;
    }
}
