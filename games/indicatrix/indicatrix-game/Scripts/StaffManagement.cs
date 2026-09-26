/// <summary>
/// StaffManagement
/// ------------------------------------------------------------------
/// Samostatná dátová štruktúra pre evidenciu personálu / mzdových údajov.
///
/// Drží tri položky:
///   - ID             (int)   – číselný identifikátor záznamu.
///   - EmployeeSalary (int)   – mzda zamestnanca.
///   - LevelSalary    (short) – mzdový level ("short int").
///
/// NA ROZDIEL od FactoryInstance, kde je ID FIXNÉ (typ továrne) a kde sa
/// niektoré hodnoty editujú až po dokončení výstavby, tu sú VŠETKY TRI
/// položky verejné a MENITEĽNÉ KEDYKOĽVEK počas hry. Zámerne neexistuje
/// žiadny "construction guard", read-only getter ani try-setter – hodnoty
/// sa prepisujú priamym priradením:
///
///     var staff = new StaffManagement(0, 250, 3);
///     staff.ID            = 7;     // kedykoľvek
///     staff.EmployeeSalary = 400;  // kedykoľvek
///     staff.LevelSalary    = 5;    // kedykoľvek
///
/// Trieda nemá žiadnu väzbu na tile engine ani na ostatné systémy – je to
/// čistá dátová štruktúra. Ak ju budeš chcieť napojiť na konkrétnu továreň,
/// stačí ju použiť ako pole v inej triede alebo držať v zozname/registri.
/// ------------------------------------------------------------------
/// </summary>
[System.Serializable]
public class StaffManagement
{
    /// <summary>Číselné ID záznamu personálu. Meniteľné kedykoľvek počas hry.</summary>
    public int ID;

    /// <summary>Mzda zamestnanca. Meniteľná kedykoľvek počas hry.</summary>
    public int EmployeeSalary;

    /// <summary>Mzdový level (short int). Meniteľný kedykoľvek počas hry.</summary>
    public short LevelSalary;

    /// <summary>
    /// Bezparametrický konštruktor – ponechá default hodnoty (ID = 0,
    /// EmployeeSalary = 0, LevelSalary = 0). Vhodný napr. pri serializácii
    /// alebo keď sa hodnoty doplnia neskôr priamym priradením.
    /// </summary>
    public StaffManagement()
    {
    }

    /// <summary>
    /// Konštruktor s počiatočnými hodnotami. Aj po vytvorení sa dajú všetky
    /// polia ľubovoľne prepisovať.
    /// </summary>
    public StaffManagement(int id, int employeeSalary, short levelSalary)
    {
        ID = id;
        EmployeeSalary = employeeSalary;
        LevelSalary = levelSalary;
    }

    /// <summary>Vytvorí samostatnú kópiu tohto záznamu (hodnotová kópia).</summary>
    public StaffManagement Clone()
    {
        return new StaffManagement(ID, EmployeeSalary, LevelSalary);
    }

    public override string ToString()
    {
        return $"StaffManagement(ID={ID}, EmployeeSalary={EmployeeSalary}, LevelSalary={LevelSalary})";
    }
}
