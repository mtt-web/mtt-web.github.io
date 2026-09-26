using System;
using System.Collections.Generic;

/// <summary>
/// VehicleStock.cs
/// ─────────────────────────────────────────────────────────────────────────
/// VOZIDLOVÝ PARK – čisté dátové štruktúry (žiadne MonoBehaviour, žiadne
/// Unity API).
///
/// Tento súbor je zámerne oddelený od VehicleSystem.cs, presne tak ako je
/// TrainStock.cs oddelený od TrainSystem.cs:
///   • VehicleSystem.cs = pohybová logika, A* pathfinding, vizuál (kváder).
///   • VehicleStock.cs  = "čo je vozidlo" – iba dáta a katalógy.
///
/// ─────────────────────────────────────────────────────────────────────────
/// VLAK vs. VOZIDLO – intuitívne porovnanie
///
///   Vlak  = lokomotíva (TrainSpec/TrainInstance) + zoznam vagónov
///           (WagonSpec/WagonInstance). Náklad prevážajú vagóny.
///
///   Vozidlo = JEDEN kváder. Nemá vagóny. Ale samotný kváder má vlastnosť
///             "akoby jedného vagónu" – môže prevážať náklad v nejakom
///             množstve. Preto VehicleSpec spája do jednej karty
///             vlakové parametre (Cost, Speed, Power...) aj vagónové
///             parametre (MaximumCapacity, typ nákladu...).
///
/// ─────────────────────────────────────────────────────────────────────────
/// ROZDELENIE: "Spec" (predloha) vs. "Instance" (inštancia v hre)
///
///   *Spec     – nemenná predloha typu (katalógová karta). Spoločná pre
///               všetky vozidlá toho istého typu. Napr. VehicleSpec
///               "Coal Truck" má Cost, Speed, Power... – tieto hodnoty sú
///               rovnaké pre každý vyrobený kus.
///
///   *Instance – konkrétny kus v hre, vytvorený v depe. Drží referenciu na
///               svoj Spec + premenlivý stav daného kusu (Age,
///               CurrentCapacity – tie sa líšia kus od kusu a menia sa
///               počas hry).
///
/// Vďaka tomu sa nemenné parametre (Cost, Speed...) NEDUPLIKUJÚ do každého
/// vozidla – sú raz v katalógu. UI okno s detailmi vozidla si ich len
/// prečíta cez Instance.Spec.
/// ─────────────────────────────────────────────────────────────────────────
///
/// POZNÁMKA K TYPOM (revízia):
///   Polia Weight a Power (VehicleSpec) sú modelované ako int (číselné
///   hodnoty), nie ako string s jednotkou. Pôvodne to boli stringy
///   ("2t", "300hp"), ale nikde mimo tohto súboru sa nečítali a zadané dáta
///   vozidiel sú číselné. Číselný typ je vhodnejší (dá sa s ním počítať).
///   YearOfManufacture zostáva string ("1984") – rok sa nikde nepočíta a v
///   zadaní je uvedený ako textová hodnota.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>

namespace Game.VehicleStock
{
    // =========================================================================
    // TYP VOZIDLA
    //
    // Zadanie hovorí o "hash table (string, int)" = [typ vozidla, číselné
    // označenie vozidla]. To je ale len JEDNA dvojica hodnôt pre jeden typ
    // vozidla – nie kolekcia. Použiť Dictionary/Hashtable na uloženie jednej
    // dvojice je zbytočné (alokácia, žiadna typová bezpečnosť, neprehľadné).
    //
    // Preto je typ vozidla modelovaný ako malý nemenný struct VehicleType
    // (Name + Id) – presne ako WagonType v TrainStock.cs. "Hash table"
    // charakter – teda vyhľadanie typu podľa názvu alebo podľa čísla –
    // zabezpečuje statický register VehicleCatalog, ktorý skutočné slovníky
    // (ByName, ById) drží centrálne na jednom mieste.
    //
    // Ak by si predsa len chcel priamo slovník na konkrétnom vozidle, dá sa
    // získať jednoriadkovo: VehicleType.AsPair() vráti KeyValuePair<string,int>.
    //
    // DÔLEŽITÉ – ZHODA ID S ResourceType (FactorySystem.cs):
    //   Číselné Id typu vozidla je ZÁMERNE rovnaké ako (int)ResourceType:
    //     "Coal Truck"        #1  → ResourceType.Coal                = 1
    //     "Wood Truck"        #2  → ResourceType.Wood                = 2
    //     "Iron Ore Truck"    #3  → ResourceType.IronOre             = 3
    //     ...
    //     "Electronics Truck" #16 → ResourceType.ElectronicsProducts = 16
    //   Vďaka tomu VehicleTradeSystem.VehicleResource() namapuje vozidlo na
    //   surovinu bez extra poľa (Id == (int)ResourceType). Pri pridávaní
    //   nového vozidla teda Id MUSÍ zodpovedať príslušnej hodnote v enum
    //   ResourceType.
    // =========================================================================

    /// <summary>
    /// Typ vozidla – nemenná dvojica [textové označenie, číselné označenie].
    /// Napr. ("Coal Truck", 1), ("Wood Truck", 2).
    /// </summary>
    [Serializable]
    public readonly struct VehicleType : IEquatable<VehicleType>
    {
        /// <summary>Textové označenie typu vozidla, napr. "Coal Truck".</summary>
        public readonly string Name;

        /// <summary>Číselné označenie typu vozidla (== (int)ResourceType), napr. 1.</summary>
        public readonly int Id;

        public VehicleType(string name, int id)
        {
            Name = name;
            Id = id;
        }

        /// <summary>
        /// Vráti typ vozidla ako dvojicu kľúč–hodnota (string, int).
        /// Pohodlné, ak niektorá časť kódu očakáva "hash table" reprezentáciu.
        /// </summary>
        public KeyValuePair<string, int> AsPair() => new KeyValuePair<string, int>(Name, Id);

        public bool Equals(VehicleType other) => Id == other.Id && Name == other.Name;
        public override bool Equals(object obj) => obj is VehicleType v && Equals(v);
        public override int GetHashCode() => Id;
        public override string ToString() => $"{Name} (#{Id})";
    }

    // =========================================================================
    // PREDLOHA VOZIDLA – VehicleSpec
    //
    // Nemenná katalógová karta jedného typu vozidla. Obsahuje VŠETKY
    // nemenné atribúty zo zadania:
    //   Name, Type, MaximumCapacity, Cost, OperatingCosts, Speed, Weight,
    //   Power, ServiceLife, ServicingInterval, YearOfManufacture.
    //
    // Premenlivé atribúty (CurrentCapacity, Age) NEPATRIA sem – tie sú na
    // VehicleInstance, lebo sa líšia kus od kusu a menia sa počas hry.
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vozidla.
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class VehicleSpec
    {
        /// <summary>Názov vozidla, napr. "Coal Truck".</summary>
        public string Name;

        /// <summary>Typ vozidla – [textové označenie, číselné označenie].</summary>
        public VehicleType Type;

        /// <summary>Maximálna kapacita vozidla.</summary>
        public int MaximumCapacity;

        /// <summary>Cena vozidla.</summary>
        public int Cost;

        /// <summary>Prevádzkové náklady.</summary>
        public int OperatingCosts;

        /// <summary>Rýchlosť vozidla.</summary>
        public int Speed;

        /// <summary>Hmotnosť vozidla (číselne, napr. 2).</summary>
        public int Weight;

        /// <summary>Výkon vozidla (číselne, napr. 100).</summary>
        public int Power;

        /// <summary>Životnosť vozidla.</summary>
        public int ServiceLife;

        /// <summary>Servisný interval.</summary>
        public int ServicingInterval;

        /// <summary>Rok výroby, napr. "1984".</summary>
        public string YearOfManufacture;

        public VehicleSpec(string name, VehicleType type, int maximumCapacity,
                           int cost, int operatingCosts, int speed, int weight,
                           int power, int serviceLife, int servicingInterval,
                           string yearOfManufacture)
        {
            Name = name;
            Type = type;
            MaximumCapacity = maximumCapacity;
            Cost = cost;
            OperatingCosts = operatingCosts;
            Speed = speed;
            Weight = weight;
            Power = power;
            ServiceLife = serviceLife;
            ServicingInterval = servicingInterval;
            YearOfManufacture = yearOfManufacture;
        }
    }

    // =========================================================================
    // INŠTANCIA VOZIDLA – VehicleInstance
    //
    // Konkrétny kus vozidla existujúci v hre, vytvorený v depe. Drží
    // referenciu na svoj VehicleSpec (nemenné parametre) + premenlivý stav
    // daného kusu:
    //   • CurrentCapacity – aktuálne naložený náklad (pri vytvorení 0).
    //   • Age             – aktuálny vek vozidla (pri vytvorení 0).
    //
    // Pozn.: VehicleInstance je čisto dátový popis. Pohybový/pathfinding
    // stav (activePath, vehicleDistance, isRunning, stanice...) zostáva v
    // VehicleSystem.VehicleData. VehicleData drží referenciu na
    // VehicleInstance – pozri integráciu vo VehicleSystem.cs.
    // =========================================================================

    /// <summary>
    /// Konkrétne vozidlo existujúce v hre. Drží referenciu na svoj
    /// VehicleSpec (nemenné parametre) + premenlivý stav (Age, CurrentCapacity).
    /// </summary>
    [Serializable]
    public sealed class VehicleInstance
    {
        /// <summary>Predloha typu tohto vozidla (nemenné parametre).</summary>
        public readonly VehicleSpec Spec;

        /// <summary>
        /// Aktuálna kapacita (naložený náklad). Pri vytvorení 0.
        /// Mení sa počas hry pri nakladaní/vykladaní.
        /// </summary>
        public int CurrentCapacity;

        /// <summary>
        /// Aktuálny vek vozidla. Pri vytvorení 0.
        /// Mení sa počas hry (pribúdaním herného času).
        /// </summary>
        public int Age;

        public VehicleInstance(VehicleSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            CurrentCapacity = 0;
            Age = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public VehicleType Type => Spec.Type;
        public int MaximumCapacity => Spec.MaximumCapacity;
        public int Cost => Spec.Cost;
        public int OperatingCosts => Spec.OperatingCosts;
        public int Speed => Spec.Speed;
        public int Weight => Spec.Weight;
        public int Power => Spec.Power;
        public int ServiceLife => Spec.ServiceLife;
        public int ServicingInterval => Spec.ServicingInterval;
        public string YearOfManufacture => Spec.YearOfManufacture;
    }

    // =========================================================================
    // KATALÓG VOZIDIEL – VehicleCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vozidiel. Slúži ako
    // zdroj pre VehicleTypeDropdown (názvy) aj pre VehicleSystem (vytvorenie
    // inštancie). Drží aj "hash table" lookupy (ByName, ById) – to je
    // miesto, kde má slovník skutočný zmysel (kolekcia viacerých typov).
    //
    // Id každého vozidla sa zhoduje s (int)ResourceType (FactorySystem.cs),
    // takže keď vozidlo dorazí do stanice, VehicleTradeSystem.VehicleResource()
    // automaticky rozpozná surovinu pre ktorýkoľvek z týchto 16 typov.
    // =========================================================================

    public static class VehicleCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vozidiel, v poradí zhodnom s VehicleTypeDropdown.
        /// 16 typov – Id zodpovedá ResourceType (1 = Coal ... 16 = Electronics).
        ///
        /// Pozn.: Name aj Type.Name sú teraz zhodné (napr. "Coal Truck"),
        /// takže dropdown zobrazuje priamo názvy nákladných vozidiel a
        /// VehicleCatalog.ByName(...) ich vie rozlíšiť (názvy sú unikátne).
        /// </summary>
        public static readonly IReadOnlyList<VehicleSpec> All = new List<VehicleSpec>
        {
            // Vozidlo 1 – upravené
            new VehicleSpec(
                name:              "Coal Truck",
                type:              new VehicleType("Coal Truck", 1),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 2150,
                yearOfManufacture: "1984"
            ),
            // Vozidlo 2 – upravené
            new VehicleSpec(
                name:              "Wood Truck",
                type:              new VehicleType("Wood Truck", 2),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 2300,
                yearOfManufacture: "1984"
            ),
            // Vozidlo 3
            new VehicleSpec(
                name:              "Iron Ore Truck",
                type:              new VehicleType("Iron Ore Truck", 3),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 2450,
                yearOfManufacture: "1997"
            ),
            // Vozidlo 4
            new VehicleSpec(
                name:              "Gold Truck",
                type:              new VehicleType("Gold Truck", 4),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 2600,
                yearOfManufacture: "1995"
            ),
            // Vozidlo 5
            new VehicleSpec(
                name:              "Silver Truck",
                type:              new VehicleType("Silver Truck", 5),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 2750,
                yearOfManufacture: "1992"
            ),
            // Vozidlo 6
            new VehicleSpec(
                name:              "Livestock Truck",
                type:              new VehicleType("Livestock Truck", 6),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             45,
                weight:            2,
                power:             45,
                serviceLife:       10,
                servicingInterval: 2900,
                yearOfManufacture: "1989"
            ),
            // Vozidlo 7
            new VehicleSpec(
                name:              "Grain Truck",
                type:              new VehicleType("Grain Truck", 7),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 3050,
                yearOfManufacture: "1988"
            ),
            // Vozidlo 8
            new VehicleSpec(
                name:              "Oil Truck",
                type:              new VehicleType("Oil Truck", 8),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             45,
                weight:            2,
                power:             45,
                serviceLife:       10,
                servicingInterval: 3200,
                yearOfManufacture: "1977"
            ),
            // Vozidlo 9
            new VehicleSpec(
                name:              "Boards Truck",
                type:              new VehicleType("Boards Truck", 9),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 3350,
                yearOfManufacture: "1977"
            ),
            // Vozidlo 10
            new VehicleSpec(
                name:              "Plastic Truck",
                type:              new VehicleType("Plastic Truck", 10),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 3500,
                yearOfManufacture: "1973"
            ),
            // Vozidlo 11
            new VehicleSpec(
                name:              "Meat Truck",
                type:              new VehicleType("Meat Truck", 11),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 3650,
                yearOfManufacture: "1969"
            ),
            // Vozidlo 12
            new VehicleSpec(
                name:              "Flour Truck",
                type:              new VehicleType("Flour Truck", 12),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 3800,
                yearOfManufacture: "1967"
            ),
            // Vozidlo 13
            new VehicleSpec(
                name:              "Metals Truck",
                type:              new VehicleType("Metals Truck", 13),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             60,
                weight:            2,
                power:             60,
                serviceLife:       10,
                servicingInterval: 3950,
                yearOfManufacture: "1981"
            ),
            // Vozidlo 14
            new VehicleSpec(
                name:              "Glass Truck",
                type:              new VehicleType("Glass Truck", 14),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             45,
                weight:            2,
                power:             45,
                serviceLife:       10,
                servicingInterval: 4100,
                yearOfManufacture: "1981"
            ),
            // Vozidlo 15
            new VehicleSpec(
                name:              "Furniture Truck",
                type:              new VehicleType("Furniture Truck", 15),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 4250,
                yearOfManufacture: "1982"
            ),
            // Vozidlo 16
            new VehicleSpec(
                name:              "Electronics Truck",
                type:              new VehicleType("Electronics Truck", 16),
                maximumCapacity:   20,
                cost:              50,
                operatingCosts:    3,
                speed:             80,
                weight:            2,
                power:             80,
                serviceLife:       10,
                servicingInterval: 4400,
                yearOfManufacture: "1975"
            ),
        };

        // "Hash table" lookupy – tu má slovník zmysel, lebo ide o kolekciu typov.
        // _byName a _byTypeName sú dva pohľady:
        //   _byName     – kľúč = názov vozidla    ("Coal Truck", "Wood Truck", ...)
        //   _byTypeName – kľúč = textové označenie typu ("Coal Truck", "Wood Truck", ...)
        // (Po revízii sú Name a Type.Name zhodné, takže oba slovníky majú
        //  rovnaké kľúče; ponechané sú obidva kvôli spätnej kompatibilite API.)
        private static readonly Dictionary<string, VehicleSpec> _byName = BuildByName();
        private static readonly Dictionary<string, VehicleSpec> _byTypeName = BuildByTypeName();
        private static readonly Dictionary<int, VehicleSpec> _byTypeId = BuildByTypeId();

        private static Dictionary<string, VehicleSpec> BuildByName()
        {
            var d = new Dictionary<string, VehicleSpec>();
            foreach (var v in All) d[v.Name] = v;
            return d;
        }

        private static Dictionary<string, VehicleSpec> BuildByTypeName()
        {
            var d = new Dictionary<string, VehicleSpec>();
            foreach (var v in All) d[v.Type.Name] = v;
            return d;
        }

        private static Dictionary<int, VehicleSpec> BuildByTypeId()
        {
            var d = new Dictionary<int, VehicleSpec>();
            foreach (var v in All) d[v.Type.Id] = v;
            return d;
        }

        /// <summary>Názvy typov vozidiel – priamo použiteľné pre VehicleTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti VehicleSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static VehicleSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti VehicleSpec podľa názvu vozidla ("Coal Truck"), alebo null.</summary>
        public static VehicleSpec ByName(string name)
            => name != null && _byName.TryGetValue(name, out var v) ? v : null;

        /// <summary>Vráti VehicleSpec podľa textového označenia typu ("Coal Truck"), alebo null.</summary>
        public static VehicleSpec ByTypeName(string typeName)
            => typeName != null && _byTypeName.TryGetValue(typeName, out var v) ? v : null;

        /// <summary>Vráti VehicleSpec podľa číselného označenia typu (1, 2, ...), alebo null.</summary>
        public static VehicleSpec ByTypeId(int typeId)
            => _byTypeId.TryGetValue(typeId, out var v) ? v : null;
    }
}
