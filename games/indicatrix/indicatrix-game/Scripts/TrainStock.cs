using System;
using System.Collections.Generic;

/// <summary>
/// TrainStock.cs
/// ─────────────────────────────────────────────────────────────────────────
/// VOZOVÝ PARK – čisté dátové štruktúry (žiadne MonoBehaviour, žiadne Unity API).
///
/// Tento súbor je zámerne oddelený od TrainSystem.cs:
///   • TrainSystem.cs = pohybová logika, A* pathfinding, vizuál (kvádre).
///   • TrainStock.cs  = "čo je vlak / vagón" – iba dáta a katalógy.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ROZDELENIE: "Spec" (predloha) vs. "Instance" (inštancia v hre)
///
///   *Spec     – nemenná predloha typu (katalógová karta). Spoločná pre všetky
///               vlaky/vagóny toho istého typu. Napr. TrainSpec "Iron Dragon"
///               má Cost, Speed, Power... – tieto hodnoty sú rovnaké pre každý
///               vyrobený kus.
///
///   *Instance – konkrétny kus v hre, vytvorený v depe. Drží referenciu na
///               svoj Spec + premenlivý stav daného kusu (napr. Age vlaku,
///               CurrentCapacity vagónu – tie sa líšia kus od kusu a menia
///               sa počas hry).
///
/// Vďaka tomu sa nemenné parametre (Cost, Speed...) NEDUPLIKUJÚ do každého
/// vlaku – sú raz v katalógu. UI okno s detailmi vlaku si ich len prečíta
/// cez Instance.Spec.
/// ─────────────────────────────────────────────────────────────────────────
///
/// POZNÁMKA K TYPOM (revízia):
///   Polia Weight / Power / YearOfManufacture (TrainSpec) a Weight (WagonSpec)
///   sú modelované ako int (číselné hodnoty), nie ako string s jednotkou.
///   Pôvodne to boli stringy ("55t", "720hp", "1984", "5t"), ale nikde mimo
///   tohto súboru sa nečítali a zadané dáta vlakov/vagónov sú číselné. Číselný
///   typ je tak vhodnejší (dá sa s ním počítať – hmotnosť súpravy, výkon a pod.)
///   a presne zodpovedá zadaným parametrom.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>

namespace Game.TrainStock
{
    // =========================================================================
    // TYP VAGÓNU
    //
    // Zadanie hovorí o "hash table (string, int)" = [textové označenie, číselné
    // označenie]. To je ale len JEDNA dvojica hodnôt pre jeden typ vagónu –
    // nie kolekcia. Použiť Dictionary/Hashtable na uloženie jednej dvojice je
    // zbytočné (alokácia, žiadna typová bezpečnosť, neprehľadné).
    //
    // Preto je typ vagónu modelovaný ako malý nemenný struct WagonType
    // (Name + Id). "Hash table" charakter – teda vyhľadanie typu podľa názvu
    // alebo podľa čísla – zabezpečuje statický register WagonCatalog, ktorý
    // skutočné slovníky (ByName, ById) drží centrálne na jednom mieste.
    //
    // Ak by si predsa len chcel priamo slovník na konkrétnom vagóne, dá sa
    // získať jednoriadkovo: WagonType.AsPair() vráti KeyValuePair<string,int>.
    //
    // DÔLEŽITÉ – ZHODA ID S ResourceType (FactorySystem.cs):
    //   Číselné Id typu vagónu je ZÁMERNE rovnaké ako (int)ResourceType:
    //     "Coal Truck"        #1  → ResourceType.Coal                = 1
    //     "Wood Truck"        #2  → ResourceType.Wood                = 2
    //     "Iron Ore Truck"    #3  → ResourceType.IronOre             = 3
    //     ...
    //     "Electronics Truck" #16 → ResourceType.ElectronicsProducts = 16
    //   Vďaka tomu TrainTradeSystem.WagonResource() namapuje vagón na surovinu
    //   bez extra poľa (Id == (int)ResourceType). Pri pridávaní nového vagónu
    //   teda Id MUSÍ zodpovedať príslušnej hodnote v enum ResourceType.
    // =========================================================================

    /// <summary>
    /// Typ vagónu – nemenná dvojica [textové označenie, číselné označenie].
    /// Napr. ("Coal Truck", 1), ("Wood Truck", 2).
    /// </summary>
    [Serializable]
    public readonly struct WagonType : IEquatable<WagonType>
    {
        /// <summary>Textové označenie typu vagónu, napr. "Coal Truck".</summary>
        public readonly string Name;

        /// <summary>Číselné označenie typu vagónu (== (int)ResourceType), napr. 1.</summary>
        public readonly int Id;

        public WagonType(string name, int id)
        {
            Name = name;
            Id = id;
        }

        /// <summary>
        /// Vráti typ vagónu ako dvojicu kľúč–hodnota (string, int).
        /// Pohodlné, ak niektorá časť kódu očakáva "hash table" reprezentáciu.
        /// </summary>
        public KeyValuePair<string, int> AsPair() => new KeyValuePair<string, int>(Name, Id);

        public bool Equals(WagonType other) => Id == other.Id && Name == other.Name;
        public override bool Equals(object obj) => obj is WagonType w && Equals(w);
        public override int GetHashCode() => Id;
        public override string ToString() => $"{Name} (#{Id})";
    }

    // =========================================================================
    // PREDLOHA VAGÓNU – WagonSpec
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vagónu.
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class WagonSpec
    {
        /// <summary>
        /// Názov vagónu, napr. "Coal Truck".
        /// Pozn.: názov sa dá odvodiť aj z Type.Name. Necháme ho ako samostatné
        /// pole, lebo UI prvok WagonTypeDropdown pracuje s názvami priamo a je
        /// pohodlnejšie mať jeden zdroj pre zobrazenie. Drží sa konzistentne
        /// s Type.Name (viď WagonCatalog, kde sa oba nastavujú spolu).
        /// </summary>
        public string Name;

        /// <summary>Typ vagónu – [textové označenie, číselné označenie].</summary>
        public WagonType Type;

        /// <summary>Cena vagónu.</summary>
        public int Cost;

        /// <summary>Hmotnosť vagónu (číselne, napr. 5).</summary>
        public int Weight;

        /// <summary>Maximálna kapacita vagónu.</summary>
        public int MaximumCapacity;

        public WagonSpec(string name, WagonType type, int cost, int weight, int maximumCapacity)
        {
            Name = name;
            Type = type;
            Cost = cost;
            Weight = weight;
            MaximumCapacity = maximumCapacity;
        }
    }

    // =========================================================================
    // INŠTANCIA VAGÓNU – WagonInstance
    // =========================================================================

    /// <summary>
    /// Konkrétny vagón existujúci v hre. Drží referenciu na svoj WagonSpec
    /// (nemenné parametre) + premenlivý stav (aktuálna kapacita).
    /// </summary>
    [Serializable]
    public sealed class WagonInstance
    {
        /// <summary>Predloha typu tohto vagónu (nemenné parametre).</summary>
        public readonly WagonSpec Spec;

        /// <summary>
        /// Aktuálna kapacita (naložený náklad). Pri vytvorení 0.
        /// Mení sa počas hry pri nakladaní/vykladaní (viď TrainTradeSystem).
        /// </summary>
        public int CurrentCapacity;

        public WagonInstance(WagonSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            CurrentCapacity = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public WagonType Type => Spec.Type;
        public int Cost => Spec.Cost;
        public int Weight => Spec.Weight;
        public int MaximumCapacity => Spec.MaximumCapacity;
    }

    // =========================================================================
    // PREDLOHA VLAKU / LOKOMOTÍVY – TrainSpec
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vlaku (lokomotívy).
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class TrainSpec
    {
        /// <summary>Názov vlaku, napr. "Iron Dragon".</summary>
        public string Name;

        /// <summary>Cena vlaku.</summary>
        public int Cost;

        /// <summary>Prevádzkové náklady.</summary>
        public int OperatingCosts;

        /// <summary>Rýchlosť vlaku.</summary>
        public int Speed;

        /// <summary>Hmotnosť vlaku (číselne, napr. 150).</summary>
        public int Weight;

        /// <summary>Výkon vlaku (číselne, napr. 60).</summary>
        public int Power;

        /// <summary>Životnosť vlaku.</summary>
        public int ServiceLife;

        /// <summary>Servisný interval.</summary>
        public int ServicingInterval;

        /// <summary>Rok výroby (číselne, napr. 1930).</summary>
        public int YearOfManufacture;

        public TrainSpec(string name, int cost, int operatingCosts, int speed,
                         int weight, int power, int serviceLife,
                         int servicingInterval, int yearOfManufacture)
        {
            Name = name;
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
    // INŠTANCIA VLAKU – TrainInstance
    //
    // Pozn.: TrainInstance je čisto dátový popis vlakovej súpravy
    // (lokomotíva + vagóny). Pohybový/pathfinding stav (PathData, stanice,
    // isRunning...) zostáva v TrainSystem.TrainData. TrainData drží referenciu
    // na TrainInstance – viď integráciu nižšie v poznámke pre TrainSystem.cs.
    // =========================================================================

    /// <summary>
    /// Konkrétny vlak existujúci v hre. Drží referenciu na svoj TrainSpec
    /// (nemenné parametre) + premenlivý stav (vek) + zoznam vagónov.
    /// </summary>
    [Serializable]
    public sealed class TrainInstance
    {
        /// <summary>Predloha typu tohto vlaku (nemenné parametre).</summary>
        public readonly TrainSpec Spec;

        /// <summary>
        /// Aktuálny vek vlaku. Pri vytvorení 0.
        /// Mení sa počas hry (pribúdaním herného času).
        /// </summary>
        public int Age;

        /// <summary>Vagóny pripojené k tomuto vlaku (poradie = poradie v súprave).</summary>
        public readonly List<WagonInstance> Wagons = new List<WagonInstance>();

        public TrainInstance(TrainSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Age = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public int Cost => Spec.Cost;
        public int OperatingCosts => Spec.OperatingCosts;
        public int Speed => Spec.Speed;
        public int Weight => Spec.Weight;
        public int Power => Spec.Power;
        public int ServiceLife => Spec.ServiceLife;
        public int ServicingInterval => Spec.ServicingInterval;
        public int YearOfManufacture => Spec.YearOfManufacture;
    }

    // =========================================================================
    // KATALÓG VLAKOV – TrainCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vlakov. Slúži ako zdroj
    // pre TrainTypeDropdown (názvy) aj pre TrainSystem (vytvorenie inštancie).
    // Poradie v zozname = poradie položiek v TrainTypeDropdown (index 1:1).
    // =========================================================================

    public static class TrainCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vlakov, v poradí zhodnom s TrainTypeDropdown.
        /// Tri lokomotívy: "Iron Dragon", "Desert Runner", "Thunderbolt".
        /// </summary>
        public static readonly IReadOnlyList<TrainSpec> All = new List<TrainSpec>
        {
            // Vlak 1
            new TrainSpec(
                name:              "Iron Dragon",
                cost:              100,
                operatingCosts:    5,
                speed:             60,
                weight:            150,
                power:             60,
                serviceLife:       8,
                servicingInterval: 1500,
                yearOfManufacture: 1930
            ),
            // Vlak 2
            new TrainSpec(
                name:              "Desert Runner",
                cost:              150,
                operatingCosts:    7,
                speed:             100,
                weight:            200,
                power:             100,
                serviceLife:       15,
                servicingInterval: 2000,
                yearOfManufacture: 1970
            ),
            // Vlak 3
            new TrainSpec(
                name:              "Thunderbolt",
                cost:              200,
                operatingCosts:    10,
                speed:             150,
                weight:            200,
                power:             150,
                serviceLife:       20,
                servicingInterval: 2500,
                yearOfManufacture: 2000
            ),
        };

        /// <summary>Názvy typov vlakov – priamo použiteľné pre TrainTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti TrainSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static TrainSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti TrainSpec podľa názvu, alebo null.</summary>
        public static TrainSpec ByName(string name)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name) return All[i];
            return null;
        }
    }

    // =========================================================================
    // KATALÓG VAGÓNOV – WagonCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vagónov. Drží aj
    // "hash table" lookupy (ByName, ById) – to je miesto, kde má slovník
    // skutočný zmysel (kolekcia viacerých typov).
    //
    // Id každého vagónu sa zhoduje s (int)ResourceType (FactorySystem.cs),
    // takže keď vlak dorazí do stanice, TrainTradeSystem.WagonResource()
    // automaticky rozpozná surovinu pre ktorýkoľvek z týchto 16 typov.
    // =========================================================================

    public static class WagonCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vagónov, v poradí zhodnom s WagonTypeDropdown.
        /// 16 typov – Id zodpovedá ResourceType (1 = Coal ... 16 = Electronics).
        /// </summary>
        public static readonly IReadOnlyList<WagonSpec> All = new List<WagonSpec>
        {
            // Vagón 1 – už existoval
            new WagonSpec(
                name:            "Coal Truck",
                type:            new WagonType("Coal Truck", 1),
                cost:            10,
                weight:          5,
                maximumCapacity: 15
            ),
            // Vagón 2 – už existoval
            new WagonSpec(
                name:            "Wood Truck",
                type:            new WagonType("Wood Truck", 2),
                cost:            10,
                weight:          7,
                maximumCapacity: 15
            ),
            // Vagón 3
            new WagonSpec(
                name:            "Iron Ore Truck",
                type:            new WagonType("Iron Ore Truck", 3),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 4
            new WagonSpec(
                name:            "Gold Truck",
                type:            new WagonType("Gold Truck", 4),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 5
            new WagonSpec(
                name:            "Silver Truck",
                type:            new WagonType("Silver Truck", 5),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 6
            new WagonSpec(
                name:            "Livestock Truck",
                type:            new WagonType("Livestock Truck", 6),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 7
            new WagonSpec(
                name:            "Grain Truck",
                type:            new WagonType("Grain Truck", 7),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 8
            new WagonSpec(
                name:            "Oil Truck",
                type:            new WagonType("Oil Truck", 8),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 9
            new WagonSpec(
                name:            "Boards Truck",
                type:            new WagonType("Boards Truck", 9),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 10
            new WagonSpec(
                name:            "Plastic Truck",
                type:            new WagonType("Plastic Truck", 10),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 11
            new WagonSpec(
                name:            "Meat Truck",
                type:            new WagonType("Meat Truck", 11),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 12
            new WagonSpec(
                name:            "Flour Truck",
                type:            new WagonType("Flour Truck", 12),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 13
            new WagonSpec(
                name:            "Metals Truck",
                type:            new WagonType("Metals Truck", 13),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 14
            new WagonSpec(
                name:            "Glass Truck",
                type:            new WagonType("Glass Truck", 14),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 15
            new WagonSpec(
                name:            "Furniture Truck",
                type:            new WagonType("Furniture Truck", 15),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
            // Vagón 16
            new WagonSpec(
                name:            "Electronics Truck",
                type:            new WagonType("Electronics Truck", 16),
                cost:            10,
                weight:          2,
                maximumCapacity: 15
            ),
        };

        // "Hash table" lookupy – tu má slovník zmysel, lebo ide o kolekciu typov.
        private static readonly Dictionary<string, WagonSpec> _byName = BuildByName();
        private static readonly Dictionary<int, WagonSpec> _byId = BuildById();

        private static Dictionary<string, WagonSpec> BuildByName()
        {
            var d = new Dictionary<string, WagonSpec>();
            foreach (var w in All) d[w.Type.Name] = w;
            return d;
        }

        private static Dictionary<int, WagonSpec> BuildById()
        {
            var d = new Dictionary<int, WagonSpec>();
            foreach (var w in All) d[w.Type.Id] = w;
            return d;
        }

        /// <summary>Názvy typov vagónov – priamo použiteľné pre WagonTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti WagonSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static WagonSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti WagonSpec podľa textového označenia ("Coal Truck"), alebo null.</summary>
        public static WagonSpec ByName(string name)
            => name != null && _byName.TryGetValue(name, out var w) ? w : null;

        /// <summary>Vráti WagonSpec podľa číselného označenia (1, 2, ...), alebo null.</summary>
        public static WagonSpec ById(int id)
            => _byId.TryGetValue(id, out var w) ? w : null;
    }
}
