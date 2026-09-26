using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// FactorySystem
/// ------------------------------------------------------------------
/// Dátová vrstva pre továrne. Doteraz boli továrne reprezentované iba ako
/// skupina tilov na mape (IndicatrixAPI.SetTile zapíše tileID 4/5 + stateID
/// = FactoryConstructionMode do footprintu). To stačí na VYKRESLENIE továrne,
/// ale nenesie to žiadne "ekonomické" informácie – koľko suroviny továreň
/// prijíma, vydáva, aké má kapacity.
///
/// Tento súbor dopĺňa práve tieto informácie a NEZASAHUJE do tile enginu:
///   - ResourceType      – evidenčné čísla surovín (0/1/2, rozšíriteľné).
///   - ResourceSlot      – jeden riadok [typ, aktuálne množstvo, max množstvo].
///   - FactoryDefinition – nemenná ŠABLÓNA jedného typu továrne
///                         (Name + Load/UnLoad sloty + odkaz na footprint).
///   - FactoryDatabase   – 4 fixné definície, indexované cez
///                         GameManager.FactoryConstructionMode (rovnaký vzor
///                         ako IndicatrixAPI.GetFactoryFootprint).
///   - FactoryInstance   – konkrétna POLOŽENÁ továreň na mape: vlastná kópia
///                         slotov (množstvá sa počas hry menia) + pozícia.
///
/// PREČO List<ResourceSlot> A NIE Hashtable / Dictionary:
///   Zadanie hovorí, že továren môže prijať/vydať 0, 1 alebo VIAC surovín
///   a uvádza zápis "Load: [2,0,500], [3,0,750]". Dictionary<ResourceType,…>
///   by znemožnil dva sloty s rovnakou surovinou a pridáva réžiu hashovania
///   bez úžitku (slotov je málo, 0–niekoľko). List<ResourceSlot> presne
///   kopíruje zápis zo zadania, podporuje 0/1/N slotov a je triviálne
///   serializovateľný pre Unity Inspector.
/// ------------------------------------------------------------------
/// </summary>

// ------------------------------------------------------------------
// SUROVINY
// ------------------------------------------------------------------

/// <summary>
/// Typ suroviny. Hodnota enumu = "evidenčné číslo" zo zadania, takže sa dá
/// priamo (int)ResourceType použiť ako ID a opačne (ResourceType)id naspäť.
///
/// Tabuľka surovín (zadanie):
///   0  = None                 (žiadna surovina)
///   1  = Coal                 (uhlie)
///   2  = Wood                 (drevo)
///   3  = IronOre              (železná ruda)
///   4  = Gold                 (zlatá ruda)
///   5  = Silver               (strieborná ruda)
///   6  = Livestock            (dobytok)
///   7  = Grain                (pšenica)
///   8  = Oil                  (ropa)
///   9  = Boards               (dosky)
///   10 = Plastic              (plast)
///   11 = Meat                 (mäso)
///   12 = Flour                (múka)
///   13 = Metals               (kovy)
///   14 = Glass                (sklo)
///   15 = Furniture            (nábytok)
///   16 = ElectronicsProducts  (elektronické produkty)
///
/// Rozšírenie: budúcu surovinu (ďalšie evidenčné číslo) stačí dopísať sem.
/// </summary>
public enum ResourceType
{
    None = 0,    // žiadna surovina
    Coal = 1,    // uhlie
    Wood = 2,    // drevo
    IronOre = 3,    // železná ruda
    Gold = 4,    // zlatá ruda
    Silver = 5,    // strieborná ruda
    Livestock = 6,    // dobytok
    Grain = 7,    // pšenica
    Oil = 8,    // ropa
    Boards = 9,    // dosky
    Plastic = 10,   // plast
    Meat = 11,   // mäso
    Flour = 12,   // múka
    Metals = 13,   // kovy
    Glass = 14,   // sklo
    Furniture = 15,   // nábytok
    ElectronicsProducts = 16    // elektronické produkty
}

/// <summary>
/// ResourceSlot
/// ------------------------------------------------------------------
/// Jeden riadok zo zadania: [typ suroviny, aktuálne množstvo, maximálne
/// množstvo] – t.j. [Type, Amount, Capacity].
///
/// Používa sa rovnako v zozname Load (príjem) aj UnLoad (výdaj). Jedna
/// továreň môže mať takýchto slotov 0, 1 alebo viac (pozri FactoryDefinition).
///
/// POZN.: Trieda (nie struct) je zvolená zámerne – FactoryInstance si musí
/// drža? MENITEĽNÉ množstvá (Amount sa počas hry mení nakladaním/vykladaním
/// vlakov). Reference type uľahčuje úpravu cez List bez index-prepisovania.
/// </summary>
[System.Serializable]
public class ResourceSlot
{
    /// <summary>Typ suroviny tohto slotu (Coal / Wood / …).</summary>
    public ResourceType type;

    /// <summary>Aktuálne množstvo suroviny v slote (0 .. capacity).</summary>
    public int amount;

    /// <summary>Maximálne množstvo suroviny, ktoré sa do slotu zmestí.</summary>
    public int capacity;

    public ResourceSlot(ResourceType type, int amount, int capacity)
    {
        this.type = type;
        this.amount = amount;
        this.capacity = capacity;
    }

    /// <summary>Hlboká kópia slotu – pri vytváraní FactoryInstance z definície,
    /// aby inštancia nezdielala menené množstvá so šablónou.</summary>
    public ResourceSlot Clone()
    {
        return new ResourceSlot(type, amount, capacity);
    }

    // Pomocné dotazy / operácie ---------------------------------------------------------------------------

    /// <summary>Voľné miesto v slote (koľko ešte možno pridať).</summary>
    public int FreeSpace => Mathf.Max(0, capacity - amount);

    public bool IsFull => amount >= capacity;
    public bool IsEmpty => amount <= 0;

    /// <summary>
    /// Pridá do slotu maximálne <paramref name="requested"/> jednotiek,
    /// orezané kapacitou. Vráti, koľko sa SKUTOČNE pridaťo.
    /// (Použiteľné napr. keď vlak/továreň "naplní" UnLoad sklad.)
    /// </summary>
    public int Add(int requested)
    {
        int added = Mathf.Clamp(requested, 0, FreeSpace);
        amount += added;
        return added;
    }

    /// <summary>
    /// Odoberie zo slotu maximálne <paramref name="requested"/> jednotiek,
    /// orezané dostupným množstvom. Vráti, koľko sa SKUTOČNE odobralo.
    /// (Použiteľné napr. keď vlak "naloží" zo skladu továrne.)
    /// </summary>
    public int Remove(int requested)
    {
        int removed = Mathf.Clamp(requested, 0, amount);
        amount -= removed;
        return removed;
    }

    public override string ToString() => $"[{(int)type} {type}, {amount}/{capacity}]";
}

// ------------------------------------------------------------------
// DEFINÍCIA TOVÁRNE (nemenná šablóna)
// ------------------------------------------------------------------

/// <summary>
/// FactoryDefinition
/// ------------------------------------------------------------------
/// Nemenná ŠABLÓNA jedného typu továrne – "vzor", podľa ktorého sa pri
/// položení vytvorí konkrétna FactoryInstance.
///
/// Obsahuje:
///   - Name   : názov továrne (string), napr. "Coal Mine".
///   - Mode   : ku ktorému FactoryConstructionMode definícia patrí
///              (prepojenie na GameManager / IndicatrixAPI / UI).
///   - TileID : 4 = Factory, 5 = Processing – zhodné s tým, čo posiela
///              GameManager do IndicatrixAPI.SetTile.
///   - Load   : zoznam slotov, ktoré továreň PRIJÍMA (0, 1 alebo N).
///   - UnLoad : zoznam slotov, ktoré továreň VYDÁVA (0, 1 alebo N).
///   - MapColor : farba, ktorou sa táto továreň kreslí do STATUS MAPY
///              (StatusMapMenuUI → IndustryView). Je to ČISTO mapová,
///              vnútorná reprezentácia – herná textúra továrne ani jej
///              vzhľad v hre sa NEMENÍ. Každý typ továrne má vlastnú,
///              odlišnú farbu, aby boli na prehľadovej mape rozlíšiteľné.
///
/// "Load: 0" zo zadania = prázdny zoznam (Count == 0).
/// "Load: [1,0,300]"    = zoznam s jedným ResourceSlot(Coal, 0, 300).
/// "Load: [2,0,500],[3,0,750]" = zoznam s dvoma slotmi (budúce rozšírenie).
///
/// FOOTPRINT zostáva definovaný v IndicatrixAPI.GetFactoryFootprint(Mode) –
/// FactorySystem ho ZÁMERNE neduplikuje, len naň cez Mode odkazuje, aby
/// veľkosti továrne mali aj naňalej jediný zdroj pravdy.
/// </summary>
[System.Serializable]
public class FactoryDefinition
{
    /// <summary>
    /// FIXNÉ číselné ID typu továrne. Hodnota je pridelená v PORADÍ, v akom
    /// továrne nasledujú v FactoryDatabase (1. Coal Mine = 0, 2. Forest = 1,
    /// … 16. Glass Factory = 15) a počas hry sa NEMENÍ. Slúži ako stabilný
    /// kľúč typu (napr. pre uloženie hry, štatistiky, UI). Každá položená
    /// FactoryInstance si toto ID prevezme do svojho rovnomenného poľa.
    /// </summary>
    public int ID;

    public string Name;
    public GameManager.FactoryConstructionMode Mode;
    public int TileID;                       // 4 = Factory, 5 = Processing

    public List<ResourceSlot> Load;          // príjem surovín  (0..N slotov)
    public List<ResourceSlot> UnLoad;        // výdaj surovín   (0..N slotov)

    /// <summary>
    /// Farba pre vykreslenie tejto továrne do STATUS MAPY (IndustryView).
    /// LEN pre mapový účel – nemení hernú textúru ani vzhľad v hre.
    /// </summary>
    public Color MapColor;

    /// <summary>
    /// Čas výstavby tejto továrne v SEKUNDÁCH (definované per-typ v kóde).
    /// Po položení beží odpočet a kým progres &lt; 100 %, je továreň "vo
    /// výstavbe" – zablokovaná pre zmenu množstiev/kapacít aj pre demoláciu.
    /// Hodnota &lt;= 0 znamená, že továreň je hotová okamžite (bez výstavby).
    /// </summary>
    public float BuildingTime;

    /// <summary>
    /// Cena (náklad) na postavenie tejto továrne v kreditoch (CR). Odpočíta sa
    /// z konta (GameEconomy.Balance) HNEĎ pri položení továrne – hráč nemusí
    /// čakať na dokončenie výstavby (100 %). Pri demolácii sa vráti 50 % z tejto
    /// sumy (rovnaká politika ako pri RAIL/ROAD – pozri ConstructionCosts).
    ///
    /// Centrálne sa cena číta cez ConstructionCosts.FactoryBuildCost(mode),
    /// ktorá ju berie práve odtiaľto – hodnota Cost je teda jediný zdroj pravdy.
    /// Hodnota &lt;= 0 znamená "zadarmo".
    /// </summary>
    public int Cost;

    public FactoryDefinition(
        int id,
        string name,
        GameManager.FactoryConstructionMode mode,
        int tileID,
        List<ResourceSlot> load,
        List<ResourceSlot> unload,
        Color mapColor,
        float buildingTime,
        int cost)
    {
        ID = id;
        Name = name;
        Mode = mode;
        TileID = tileID;
        Load = load ?? new List<ResourceSlot>();
        UnLoad = unload ?? new List<ResourceSlot>();
        MapColor = mapColor;
        BuildingTime = buildingTime;
        Cost = cost;
    }

    /// <summary>Footprint (ve?kos? + anchor) tejto továrne – jediný zdroj
    /// pravdy zostáva v IndicatrixAPI.</summary>
    public IndicatrixAPI.FactoryFootprint GetFootprint()
    {
        return IndicatrixAPI.GetFactoryFootprint(Mode);
    }

    /// <summary>True, ak továreň nejakú surovinu prijíma.</summary>
    public bool AcceptsResources => Load.Count > 0;

    /// <summary>True, ak továreň nejakú surovinu vydáva.</summary>
    public bool ProducesResources => UnLoad.Count > 0;
}

// ------------------------------------------------------------------
// DATABÁZA FIXNÝCH TOVÁRNÍ
// ------------------------------------------------------------------

/// <summary>
/// FactoryDatabase
/// ------------------------------------------------------------------
/// Statická databáza definícií. Drží fixné FactoryDefinition a sprístupní
/// ich cez GameManager.FactoryConstructionMode – presne ten istý vzor ako
/// IndicatrixAPI.GetFactoryFootprint(mode).
///
/// Každý slot je [type, amount, capacity] = [evidenčné číslo suroviny,
/// aktuálne množstvo, maximálna kapacita]. amount je počiatočná hodnota pri
/// položení a počas hry sa mení (FactoryInstance má vlastnú kópiu slotov).
///
/// FIXNÉ HODNOTY (podľa zadania):
///
///   Factory (tileID 4) – ťažba:
///     1.  Coal Mine     – Load: 0   | UnLoad: [1,0,1000]   (uhlie 0/1000)
///     2.  Forest        – Load: 0   | UnLoad: [2,0,1000]   (drevo 0/1000)
///     3.  Iron Ore Mine – Load: 0   | UnLoad: [3,0,1000]   (žel. ruda 0/1000)
///     4.  Gold Mine     – Load: 0   | UnLoad: [4,0,1000]   (zlatá ruda 0/1000)
///     5.  Silver Mine   – Load: 0   | UnLoad: [5,0,1000]   (strieb. ruda 0/1000)
///     6.  Farm          – Load: 0   | UnLoad: [6,0,3000] (dobytok 0/3000),
///                                              [7,0,1000] (pšenica 0/1000)
///     7.  Oil Wells     – Load: 0   | UnLoad: [8,0,10000]  (ropa 0/10000)
///
///   Processing (tileID 5) – spracovanie:
///     8.  Power Station       – Load: [1,0,5000] (uhlie 0/5000) | UnLoad: 0
///     9.  Sawmill             – Load: [2,0,3000] (drevo 0/3000) |
///                               UnLoad:[9,0,1000] (dosky 0/1000)
///     10. Oil Refinery        – Load: [8,0,8000] (ropa 0/8000)  |
///                               UnLoad:[10,0,2500] (plast 0/2500)
///     11. Electronics Factory – Load: [10,0,1000] (plast 0/1000),
///                                      [14,0,1000] (sklo 0/1000),
///                                      [13,0,1000] (kovy 0/1000) |
///                               UnLoad:[16,0,2500] (elektronika 0/2500)
///     12. Furniture Factory   – Load: [10,0,1000] (plast 0/1000),
///                                      [14,0,1000] (sklo 0/1000),
///                                      [13,0,1000] (kovy 0/1000),
///                                      [9,0,1000]  (dosky 0/1000) |
///                               UnLoad:[15,0,5000] (nábytok 0/5000)
///     13. Slaughterhouse      – Load: [6,0,750] (dobytok 0/750) |
///                               UnLoad:[11,0,1000] (mäso 0/1000)
///     14. Grain Factory       – Load: [7,0,1500] (pšenica 0/1500) |
///                               UnLoad:[12,0,2000] (múka 0/2000)
///     15. Smelter             – Load: [3,0,9000] (žel. ruda 0/9000),
///                                      [4,0,9000] (zlatá ruda 0/9000),
///                                      [5,0,9000] (strieb. ruda 0/9000) |
///                               UnLoad:[13,0,12000] (kovy 0/12000)
///     16. Glass Factory       – Load: [13,0,3000] (kovy 0/3000) |
///                               UnLoad:[14,0,1000] (sklo 0/1000)
///
/// Pridanie ďalšej továrne = pridať FactoryConstructionMode do GameManager,
/// footprint do IndicatrixAPI.GetFactoryFootprint a jeden záznam sem.
/// </summary>
public static class FactoryDatabase
{
    private static readonly Dictionary<GameManager.FactoryConstructionMode, FactoryDefinition> definitions
        = new Dictionary<GameManager.FactoryConstructionMode, FactoryDefinition>
    {
        // =====================================================================
        // FACTORY (tileID 4) – ťažba surovín (Load: 0)
        // =====================================================================

        // 1. Coal Mine ---------------------------------------------------------
        // Nič neprijíma, vydáva uhlie 0/10000.
        {
            GameManager.FactoryConstructionMode.CoalMine,
            new FactoryDefinition(
                id:     0,
                name:   "Coal Mine",
                mode:   GameManager.FactoryConstructionMode.CoalMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Coal, 0, 10000)          // UnLoad: [1,0,10000]
                },
                mapColor: new Color(0.15f, 0.15f, 0.15f),
                buildingTime: 12f,
                cost:         500)
        },

        // 2. Forest ------------------------------------------------------------
        // Nič neprijíma, vydáva drevo 0/10000.
        {
            GameManager.FactoryConstructionMode.Forest,
            new FactoryDefinition(
                id:     1,
                name:   "Forest",
                mode:   GameManager.FactoryConstructionMode.Forest,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Wood, 0, 10000)          // UnLoad: [2,0,10000]
                },
                mapColor: new Color(0.13f, 0.55f, 0.13f),
                buildingTime: 10f,
                cost:         500)
        },

        // 3. Iron Ore Mine -----------------------------------------------------
        // Nič neprijíma, vydáva železnú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.IronOreMine,
            new FactoryDefinition(
                id:     2,
                name:   "Iron Ore Mine",
                mode:   GameManager.FactoryConstructionMode.IronOreMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.IronOre, 0, 10000)       // UnLoad: [3,0,10000]
                },
                mapColor: new Color(0.55f, 0.27f, 0.07f),
                buildingTime: 14f,
                cost:         500)
        },

        // 4. Gold Mine ---------------------------------------------------------
        // Nič neprijíma, vydáva zlatú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.GoldMine,
            new FactoryDefinition(
                id:     3,
                name:   "Gold Mine",
                mode:   GameManager.FactoryConstructionMode.GoldMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Gold, 0, 10000)          // UnLoad: [4,0,10000]
                },
                mapColor: new Color(1.00f, 0.84f, 0.00f),
                buildingTime: 18f,
                cost:         500)
        },

        // 5. Silver Mine -------------------------------------------------------
        // Nič neprijíma, vydáva striebornú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.SilverMine,
            new FactoryDefinition(
                id:     4,
                name:   "Silver Mine",
                mode:   GameManager.FactoryConstructionMode.SilverMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Silver, 0, 10000)        // UnLoad: [5,0,10000]
                },
                mapColor: new Color(0.75f, 0.75f, 0.80f),
                buildingTime: 16f,
                cost:         500)
        },

        // 6. Farm --------------------------------------------------------------
        // Nič neprijíma, vydáva dobytok 0/9000 a pšenicu 0/12000.
        {
            GameManager.FactoryConstructionMode.Farm,
            new FactoryDefinition(
                id:     5,
                name:   "Farm",
                mode:   GameManager.FactoryConstructionMode.Farm,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Livestock, 0, 9000),    // UnLoad: [6,0,9000]
                    new ResourceSlot(ResourceType.Grain,     0, 12000)     // UnLoad: [7,0,12000]
                },
                mapColor: new Color(0.80f, 0.75f, 0.30f),
                buildingTime: 10f,
                cost:         500)
        },

        // 7. Oil Wells ---------------------------------------------------------
        // Nič neprijíma, vydáva ropu 0/15000.
        {
            GameManager.FactoryConstructionMode.OilWells,
            new FactoryDefinition(
                id:     6,
                name:   "Oil Wells",
                mode:   GameManager.FactoryConstructionMode.OilWells,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Oil, 0, 15000)          // UnLoad: [8,0,15000]
                },
                mapColor: new Color(0.20f, 0.10f, 0.25f),
                buildingTime: 20f,
                cost:         500)
        },

        // =====================================================================
        // PROCESSING (tileID 5) – spracovanie surovín
        // =====================================================================

        // 8. Power Station -----------------------------------------------------
        // Prijíma uhlie 0/17000, nič nevydáva.
        {
            GameManager.FactoryConstructionMode.PowerStation,
            new FactoryDefinition(
                id:     7,
                name:   "Power Station",
                mode:   GameManager.FactoryConstructionMode.PowerStation,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Coal, 0, 17000)          // Load: [1,0,17000]
                },
                unload: new List<ResourceSlot>(),                         // UnLoad: 0
                mapColor: new Color(0.90f, 0.15f, 0.15f),
                buildingTime: 25f,
                cost:         300)
        },

        // 9. Sawmill -----------------------------------------------------------
        // Prijíma drevo 0/7500, vydáva dosky 0/8500.
        {
            GameManager.FactoryConstructionMode.SawMill,
            new FactoryDefinition(
                id:     8,
                name:   "Sawmill",
                mode:   GameManager.FactoryConstructionMode.SawMill,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Wood, 0, 7500)          // Load: [2,0,7500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Boards, 0, 8500)        // UnLoad: [9,0,8500]
                },
                mapColor: new Color(0.70f, 0.50f, 0.25f),
                buildingTime: 15f,
                cost:         300)
        },

        // 10. Oil Refinery -----------------------------------------------------
        // Prijíma ropu 0/12000, vydáva plast 0/7500.
        {
            GameManager.FactoryConstructionMode.OilRefinery,
            new FactoryDefinition(
                id:     9,
                name:   "Oil Refinery",
                mode:   GameManager.FactoryConstructionMode.OilRefinery,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Oil, 0, 12000)           // Load: [8,0,12000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 7500)       // UnLoad: [10,0,7500]
                },
                mapColor: new Color(0.90f, 0.20f, 0.70f),
                buildingTime: 28f,
                cost:         300)
        },

        // 11. Electronics Factory ----------------------------------------------
        // Prijíma plast/sklo/kovy (každé 0/9500), vydáva elektroniku 0/9500.
        {
            GameManager.FactoryConstructionMode.ElectronicsFactory,
            new FactoryDefinition(
                id:     10,
                name:   "Electronics Factory",
                mode:   GameManager.FactoryConstructionMode.ElectronicsFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 9500),      // Load: [10,0,9500]
                    new ResourceSlot(ResourceType.Glass,   0, 9500),      // Load: [14,0,9500]
                    new ResourceSlot(ResourceType.Metals,  0, 9500)       // Load: [13,0,9500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.ElectronicsProducts, 0, 12500) // UnLoad: [16,0,12500]
                },
                mapColor: new Color(0.10f, 0.75f, 0.85f),
                buildingTime: 35f,
                cost:         300)
        },

        // 12. Furniture Factory ------------------------------------------------
        // Prijíma plast/sklo/kovy/dosky (každé 0/1000), vydáva nábytok 0/18000.
        {
            GameManager.FactoryConstructionMode.FurnitureFactory,
            new FactoryDefinition(
                id:     11,
                name:   "Furniture Factory",
                mode:   GameManager.FactoryConstructionMode.FurnitureFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 12000),      // Load: [10,0,12000]
                    new ResourceSlot(ResourceType.Glass,   0, 12000),      // Load: [14,0,12000]
                    new ResourceSlot(ResourceType.Metals,  0, 12000),      // Load: [13,0,12000]
                    new ResourceSlot(ResourceType.Boards,  0, 12000)       // Load: [9,0,12000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Furniture, 0, 18000)     // UnLoad: [15,0,18000]
                },
                mapColor: new Color(0.60f, 0.35f, 0.75f),
                buildingTime: 32f,
                cost:         300)
        },

        // 13. Slaughterhouse ---------------------------------------------------
        // Prijíma dobytok 0/7750, vydáva mäso 0/11000.
        {
            GameManager.FactoryConstructionMode.Slaughterhouse,
            new FactoryDefinition(
                id:     12,
                name:   "Slaughterhouse",
                mode:   GameManager.FactoryConstructionMode.Slaughterhouse,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Livestock, 0, 7750)      // Load: [6,0,7750]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Meat, 0, 11000)          // UnLoad: [11,0,11000]
                },
                mapColor: new Color(0.80f, 0.30f, 0.40f),
                buildingTime: 18f,
                cost:         300)
        },

        // 14. Grain Factory ----------------------------------------------------
        // Prijíma pšenicu 0/7500, vydáva múku 0/9000.
        {
            GameManager.FactoryConstructionMode.GrainFactory,
            new FactoryDefinition(
                id:     13,
                name:   "Grain Factory",
                mode:   GameManager.FactoryConstructionMode.GrainFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Grain, 0, 7500)         // Load: [7,0,7500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Flour, 0, 9000)         // UnLoad: [12,0,9000]
                },
                mapColor: new Color(0.95f, 0.90f, 0.70f),
                buildingTime: 16f,
                cost:         300)
        },

        // 15. Smelter ----------------------------------------------------------
        // Prijíma železnú rudu 0/9000, zlatú rudu 0/9000 a striebornú rudu
        // 0/9000; vydáva kovy 0/12000.
        //
        // Taviareň je JEDINÝ odberateľ rúd v hre. Bez slotov Gold a Silver by
        // Gold Mine a Silver Mine nemali kam dodávať a obe budovy by boli
        // nepredajné (výnos vzniká výhradne vyložením do Load slotu – pozri
        // TrainTradeSystem.Execute / VehicleTradeSystem.Execute).
        {
            GameManager.FactoryConstructionMode.Smelter,
            new FactoryDefinition(
                id:     14,
                name:   "Smelter",
                mode:   GameManager.FactoryConstructionMode.Smelter,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.IronOre, 0, 9000),      // Load: [3,0,9000]
                    new ResourceSlot(ResourceType.Gold,    0, 9000),      // Load: [4,0,9000]
                    new ResourceSlot(ResourceType.Silver,  0, 9000)       // Load: [5,0,9000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Metals, 0, 12000)        // UnLoad: [13,0,12000]
                },
                mapColor: new Color(1.00f, 0.45f, 0.00f),
                buildingTime: 26f,
                cost:         300)
        },

        // 16. Glass Factory ----------------------------------------------------
        // Prijíma kovy 0/13000, vydáva sklo 0/12000.
        {
            GameManager.FactoryConstructionMode.GlassFactory,
            new FactoryDefinition(
                id:     15,
                name:   "Glass Factory",
                mode:   GameManager.FactoryConstructionMode.GlassFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Metals, 0, 13000)        // Load: [13,0,13000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Glass, 0, 12000)         // UnLoad: [14,0,12000]
                },
                mapColor: new Color(0.55f, 0.80f, 0.90f),
                buildingTime: 22f,
                cost:         300)
        },
    };

    /// <summary>
    /// Vráti definíciu (šablónu) pre daný typ továrne, alebo null pre
    /// FactoryConstructionMode.None / neznámy typ.
    /// </summary>
    public static FactoryDefinition GetDefinition(GameManager.FactoryConstructionMode mode)
    {
        return definitions.TryGetValue(mode, out var def) ? def : null;
    }

    /// <summary>Všetky definície (napr. pre naplnenie UI alebo encyklopédiu).</summary>
    public static IEnumerable<FactoryDefinition> AllDefinitions => definitions.Values;
}

// ------------------------------------------------------------------
// KONKRÉTNA POLOŽENÁ TOVÁREN
// ------------------------------------------------------------------

/// <summary>
/// FactoryInstance
/// ------------------------------------------------------------------
/// Konkrétna továreň POLOŽENÁ na mape. Vzniká z FactoryDefinition pri
/// úspešnom IndicatrixAPI.SetTile(...) (pozri integráciu nižšie v komentári).
///
/// ROZDIEL oproti FactoryDefinition:
///   - Definition = nemenná šablóna, zdie?aná všetkými inštanciami toho typu.
///   - Instance   = vlastná KÓPIA Load/UnLoad slotov (množstvá sa počas hry
///                  menia – vlaky nakladajú/vykladajú) + pozícia footprintu
///                  na tile mape.
///
/// Týmto môžu na mape stá? napr. 3 Coal Mine, každá s iným zostatkom uhlia.
/// </summary>
public class FactoryInstance
{
    /// <summary>Odkaz na nemennú šablónu (Name, Mode, TileID, footprint).</summary>
    public FactoryDefinition Definition { get; private set; }

    /// <summary>Vlastná, menite?ná kópia príjmových slotov.</summary>
    public List<ResourceSlot> Load { get; private set; }

    /// <summary>Vlastná, menite?ná kópia výdajových slotov.</summary>
    public List<ResourceSlot> UnLoad { get; private set; }

    // Pozícia footprintu na tile mape
    // Lavý-dolný roh a rozmery (už po aplikovaní rotácie), ako ich vypočíta
    // IndicatrixAPI.SetTile. Slúži na spätné mapovanie tile ? továreň.

    public int OriginX { get; private set; }
    public int OriginZ { get; private set; }
    public int Width { get; private set; }
    public int Depth { get; private set; }
    public IndicatrixAPI.FactoryRotation Rotation { get; private set; }

    // =====================================================================
    // DOPLNKOVÉ POLOŽKY TOVÁRNE (ID + ekonomické / stavové príznaky)
    // =====================================================================

    /// <summary>
    /// FIXNÉ číselné ID typu továrne, prevzaté z FactoryDefinition.ID
    /// (1. Coal Mine = 0, 2. Forest = 1, … 16. Glass Factory = 15). Nastaví
    /// sa raz v konštruktore a počas hry sa nemení.
    /// </summary>
    public int ID { get; private set; }

    /// <summary>
    /// Mzda zamestnanca v tejto továrni. DEFAULT pri položení = 0.
    /// </summary>
    public int EmployeeSalary;

    /// <summary>
    /// Mzdový level (short int). DEFAULT pri položení = 0.
    /// </summary>
    public short LevelSalary;

    /// <summary>
    /// Príznak obsadenosti továrne. DEFAULT pri položení = false.
    /// </summary>
    public bool OccupancyFlag;

    public FactoryInstance(
        FactoryDefinition definition,
        int originX, int originZ,
        int width, int depth,
        IndicatrixAPI.FactoryRotation rotation)
    {
        Definition = definition;
        OriginX = originX;
        OriginZ = originZ;
        Width = width;
        Depth = depth;
        Rotation = rotation;

        // FIXNÉ ID typu prevezmeme z definície (pridelené v poradí databázy).
        ID = definition != null ? definition.ID : -1;

        // Hlboká kópia slotov – inštancia NESMIE meniť šablónu.
        Load = CloneSlots(definition.Load);
        UnLoad = CloneSlots(definition.UnLoad);

        // DEFAULTNÉ hodnoty pri položení továrne na tile map:
        // EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
        // Týmto je default zaručený pre KAŽDÚ položenú továreň bez ohľadu na
        // to, ktorou cestou inštancia vznikne.
        ApplyPlacementDefaults();

        // Po položení začína fáza výstavby (ak má definícia kladný BuildingTime).
        InitConstruction();
    }

    /// <summary>
    /// Nastaví počiatočné (default) hodnoty továrne platné v okamihu položenia
    /// na tile map: EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
    ///
    /// Jediné miesto, kde sú tieto defaulty definované. Volá ho konštruktor
    /// (záruka pre každú inštanciu) a explicitne aj GameManager priamo na mieste
    /// položenia (po FactoryRegistry.Register), aby bola požiadavka "vždy pri
    /// položení sa nastavia tieto položky" viditeľne splnená aj v ovládacom kóde.
    /// Operácia je idempotentná, takže dvojité zavolanie nič nepokazí.
    /// </summary>
    public void ApplyPlacementDefaults()
    {
        EmployeeSalary = 0;
        LevelSalary = 0;
        OccupancyFlag = false;
    }

    private static List<ResourceSlot> CloneSlots(List<ResourceSlot> source)
    {
        var copy = new List<ResourceSlot>(source.Count);
        foreach (var s in source)
            copy.Add(s.Clone());
        return copy;
    }

    // =====================================================================
    // STAV VÝSTAVBY (construction progress)
    // =====================================================================
    // Po položení továreň prejde fázou výstavby trvajúcou Definition.BuildingTime
    // sekúnd. Počas nej je IsUnderConstruction == true a továreň je zablokovaná
    // pre zmenu množstiev/kapacít (CanEditResources == false) aj pre demoláciu
    // (CanDemolish == false). Progres poháňa FactoryConstructionManager, ktorý
    // každý snímok volá AdvanceConstruction(Time.deltaTime).

    /// <summary>Koľko sekúnd výstavby už ubehlo (0 .. BuildTime).</summary>
    public float BuildElapsed { get; private set; }

    /// <summary>True, kým továreň nedosiahne 100 % výstavby.</summary>
    public bool IsUnderConstruction { get; private set; }

    /// <summary>True, ak je výstavba dokončená (opak IsUnderConstruction).</summary>
    public bool IsComplete => !IsUnderConstruction;

    /// <summary>Celkový čas výstavby v sekundách (z definície).</summary>
    public float BuildTime => Definition != null ? Definition.BuildingTime : 0f;

    /// <summary>Progres výstavby v rozsahu 0..1.</summary>
    public float BuildProgress01 =>
        BuildTime <= 0f ? 1f : Mathf.Clamp01(BuildElapsed / BuildTime);

    /// <summary>Progres výstavby v celých percentách 0..100 (na label).</summary>
    public int BuildPercent => Mathf.Clamp(Mathf.FloorToInt(BuildProgress01 * 100f), 0, 100);

    /// <summary>Zmena množstiev/kapacít je povolená až po dokončení výstavby.</summary>
    public bool CanEditResources => IsComplete;

    /// <summary>Demolácia továrne je povolená až po dokončení výstavby.</summary>
    public bool CanDemolish => IsComplete;

    /// <summary>
    /// Inicializuje stav výstavby. Volá konštruktor: ak má definícia kladný
    /// BuildingTime, továreň začína "vo výstavbe"; inak je hotová okamžite.
    /// </summary>
    private void InitConstruction()
    {
        BuildElapsed = 0f;
        IsUnderConstruction = (Definition != null && Definition.BuildingTime > 0f);
    }

    /// <summary>
    /// Posunie výstavbu o <paramref name="deltaSeconds"/>. Po dosiahnutí
    /// celkového času sa továreň označí ako dokončená (IsUnderConstruction =
    /// false) a sprístupní sa pre zmeny aj demoláciu.
    /// </summary>
    public void AdvanceConstruction(float deltaSeconds)
    {
        if (!IsUnderConstruction) return;

        BuildElapsed += deltaSeconds;
        if (BuildElapsed >= BuildTime)
        {
            BuildElapsed = BuildTime;
            IsUnderConstruction = false;
        }
    }

    /// <summary>Okamžite dokončí výstavbu (napr. cheat / load uloženej hry).</summary>
    public void ForceCompleteConstruction()
    {
        BuildElapsed = BuildTime;
        IsUnderConstruction = false;
    }

    /// <summary>
    /// Bezpečné nastavenie množstva v slote – funguje LEN po dokončení
    /// výstavby (počas výstavby je zmena zablokovaná a vráti false).
    /// </summary>
    public bool TrySetSlotAmount(ResourceSlot slot, int newAmount)
    {
        if (!CanEditResources || slot == null) return false;
        slot.amount = Mathf.Clamp(newAmount, 0, slot.capacity);
        return true;
    }

    /// <summary>
    /// Bezpečné nastavenie kapacity slotu – funguje LEN po dokončení výstavby.
    /// Ak nová kapacita klesne pod aktuálne množstvo, množstvo sa oreže.
    /// </summary>
    public bool TrySetSlotCapacity(ResourceSlot slot, int newCapacity)
    {
        if (!CanEditResources || slot == null) return false;
        slot.capacity = Mathf.Max(0, newCapacity);
        if (slot.amount > slot.capacity) slot.amount = slot.capacity;
        return true;
    }

    // Pomocné prístupy k slotom

    /// <summary>Názov továrne (skratka cez Definition).</summary>
    public string Name => Definition.Name;

    /// <summary>Prvý príjmový slot pre danú surovinu, alebo null.</summary>
    public ResourceSlot GetLoadSlot(ResourceType type)
    {
        foreach (var s in Load)
            if (s.type == type) return s;
        return null;
    }

    /// <summary>Prvý výdajový slot pre danú surovinu, alebo null.</summary>
    public ResourceSlot GetUnLoadSlot(ResourceType type)
    {
        foreach (var s in UnLoad)
            if (s.type == type) return s;
        return null;
    }

    /// <summary>True, ak tile [x,z] patrí do footprintu tejto továrne.</summary>
    public bool ContainsTile(int x, int z)
    {
        return x >= OriginX && x < OriginX + Width
            && z >= OriginZ && z < OriginZ + Depth;
    }

    public override string ToString()
    {
        return $"FactoryInstance '{Name}' @[{OriginX},{OriginZ}] {Width}x{Depth} (rot {Rotation})";
    }
}

// ------------------------------------------------------------------
// REGISTER POLOŽENÝCH TOVÁRNÍ
// ------------------------------------------------------------------

/// <summary>
/// FactoryRegistry
/// ------------------------------------------------------------------
/// Centrálna evidencia VŠETKÝCH položených FactoryInstance v hre. Tile mapa
/// (IndicatrixAPI) naňalej drží len textúru/tileID/stateID – nevie nič o
/// kapacitách ani o tom, "ktoré tily tvoria jednu továreň". Tento register
/// dopĺňa práve toto: jeden zoznam inštancií + rýchle spätné mapovanie
/// tile [x,z] ? FactoryInstance.
///
/// PREČO STATIC:
///   Register je herne jedinečný (jedna mapa = jedna sada tovární), rovnako
///   ako FactoryDatabase. Static prístup je konzistentný a netreba riešiť
///   serializáciu MonoBehaviour referencie. Ak by si v budúcnosti chcel mať
///   register ako MonoBehaviour singleton (kvôli Inspectoru), stačí obaliť
///   tieto metódy do inštančnej triedy – API zostane rovnaké.
///
/// SPÄTNÉ MAPOVANIE tile ? továreň:
///   Dictionary<long, FactoryInstance> kde kľúč = TileKey(x,z). Pri položení
///   sa zaregistrujú VŠETKY tily footprintu, takže klik na ľubovoľný tile
///   továrne (napr. pre info okno alebo demolish) nájde inštanciu v O(1).
/// </summary>
public static class FactoryRegistry
{
    /// <summary>Všetky položené továrne (poradie = poradie položenia).</summary>
    private static readonly List<FactoryInstance> instances = new List<FactoryInstance>();

    /// <summary>Spätné mapovanie: každý tile footprintu ? jeho FactoryInstance.</summary>
    private static readonly Dictionary<long, FactoryInstance> tileToFactory
        = new Dictionary<long, FactoryInstance>();

    /// <summary>Stabilný kľúč pre dvojicu tile súradníc (x, z).</summary>
    private static long TileKey(int x, int z) => ((long)x << 32) | (uint)z;

    // =====================================================================
    // AUTOMATICKÝ RESET PRI NOVEJ SCÉNE  (MainMenu → New Game)
    // ─────────────────────────────────────────────────────────────────────
    // PROBLÉM, KTORÝ TO RIEŠI:
    //   Register je STATIC, takže jeho obsah NEZANIKÁ pri prechode scén –
    //   prežíva rovnako ako IndicatrixAPI.LoadGameOnSceneStart. Keď sa hráč
    //   vrátil z hry do MainMenu a spustil "New Game", v registri ostali
    //   VŠETKY továrne z predchádzajúceho sedenia, hoci tile mapa bola nová
    //   a prázdna. Prejavilo sa to napr. v StaffManagementListView (výpis
    //   "duchov" na prázdnej mape) a pri Save (WriteFactories zapísal aj
    //   neexistujúce továrne).
    //
    //   Doteraz sa Clear() volal JEDINE z IndicatrixAPI.ReadFactories(), t.j.
    //   len pri Load Game. Cesta "New Game" register nikdy nevyčistila.
    //
    // RIEŠENIE:
    //   Register sa čistí SÁM pri každom načítaní scény (Single mode), takže
    //   žiadny volajúci si na to nemusí pamätať – ani New Game, ani Load,
    //   ani prípadné budúce scény. Poradie je bezpečné: sceneLoaded sa vyvolá
    //   PO Awake() objektov novej scény, ale ešte PRED ich Start(), a všetky
    //   registrácie tovární prebiehajú až v Start() alebo neskôr:
    //     • ručné položenie – GameManager.Update (klik hráča),
    //     • načítanie hry   – IndicatrixAPI.LoadGameWhenReady (korutina zo Start).
    //   Vyčistený register teda nikdy nezmaže továrne novej scény.
    //
    // POZN. K EDITORU:
    //   RuntimeInitializeOnLoadMethod(SubsystemRegistration) beží pred prvou
    //   scénou pri každom vstupe do Play Mode. Tým je register korektný aj pri
    //   vypnutom Domain Reload ("Enter Play Mode Options"), keď by statické
    //   polia inak prežili aj medzi spusteniami hry v editore.
    // =====================================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewPlaySession()
    {
        ClearData();

        // Odhlásiť + prihlásiť = idempotentné (nikdy nevznikne dvojitý odber).
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Additívne načítanie scény nie je zmena mapy – register nechávame tak.
        if (mode != LoadSceneMode.Single) return;
        if (instances.Count == 0) return;

        int stale = instances.Count;
        ClearData();

        Debug.Log($"[FactoryRegistry] Načítaná scéna '{scene.name}' – zahodených " +
                  $"{stale} tovární z predchádzajúceho sedenia (nový register je prázdny).");
    }

    /// <summary>
    /// Vyprázdni LEN dátovú časť registra (zoznam + spätné mapovanie), bez
    /// akéhokoľvek zásahu do vizuálu. Používa sa pri prechode scén, kde modely
    /// starých tovární už zanikli spolu s pôvodnou scénou a niet čo ničiť.
    /// </summary>
    private static void ClearData()
    {
        instances.Clear();
        tileToFactory.Clear();
    }

    /// <summary>Všetky aktuálne položené továrne (len na čítanie).</summary>
    public static IReadOnlyList<FactoryInstance> All => instances;

    /// <summary>Počet položených tovární.</summary>
    public static int Count => instances.Count;

    /// <summary>
    /// Zaregistruje novú položenú továreň. Volá sa po úspešnom
    /// IndicatrixAPI.SetTile(...) (placed == true). Origin/width/depth musia
    /// zodpovedať footprintu zapísanému do tile mapy – preto sa berú priamo
    /// z out parametrov SetTile.
    ///
    /// Vráti vytvorenú FactoryInstance (alebo null, ak je definícia neznáma).
    /// </summary>
    public static FactoryInstance Register(
        FactoryDefinition definition,
        int originX, int originZ,
        int width, int depth,
        IndicatrixAPI.FactoryRotation rotation)
    {
        if (definition == null)
        {
            Debug.LogWarning("[FactoryRegistry] Register: definition == null – preskočené.");
            return null;
        }

        var inst = new FactoryInstance(definition, originX, originZ, width, depth, rotation);
        instances.Add(inst);

        // Zaregistruj každý tile footprintu do spätného mapovania.
        for (int x = originX; x < originX + width; x++)
            for (int z = originZ; z < originZ + depth; z++)
                tileToFactory[TileKey(x, z)] = inst;

        Debug.Log($"[FactoryRegistry] Zaregistrovaná {inst}. Spolu tovární: {instances.Count}.");

        // Vizuál: ak má typ továrne priradený prefab v TileModelLibrary, vytvorí
        // sa 1 model na celý footprint (inak ostávajú pôvodné textúry). Pokrýva
        // ručné položenie aj Load (ReadFactories volá rovnakú cestu).
        IndicatrixAPI.instance?.OnFactoryRegistered(inst);

        return inst;
    }

    /// <summary>
    /// Vráti továreň, ktorej footprint obsahuje tile [x,z], alebo null ak
    /// na danom tile žiadna továreň nie je. O(1) cez spätné mapovanie.
    /// </summary>
    public static FactoryInstance GetFactoryAt(int x, int z)
    {
        return tileToFactory.TryGetValue(TileKey(x, z), out var inst) ? inst : null;
    }

    /// <summary>
    /// Odregistruje továreň (napr. pri demolish). Odstráni inštanciu zo
    /// zoznamu aj všetky jej tily zo spätného mapovania.
    ///
    /// POZN.: Samotné vymazanie tilov z tile mapy (IndicatrixAPI) je
    /// samostatná operácia – tento register len prestane továreň evidovať.
    ///
    /// Vráti true, ak sa továreň našla a odstránila.
    /// </summary>
    public static bool Unregister(FactoryInstance inst)
    {
        if (inst == null || !instances.Remove(inst))
            return false;

        for (int x = inst.OriginX; x < inst.OriginX + inst.Width; x++)
            for (int z = inst.OriginZ; z < inst.OriginZ + inst.Depth; z++)
            {
                long key = TileKey(x, z);
                // Odstráň len ak kľúč naozaj patrí tejto inštancii
                // (ochrana pri prípadnom prekryve – nemalo by nastať).
                if (tileToFactory.TryGetValue(key, out var mapped) && mapped == inst)
                    tileToFactory.Remove(key);
            }

        Debug.Log($"[FactoryRegistry] Odregistrovaná {inst}. Spolu tovární: {instances.Count}.");

        // Vizuál: znič prípadný model továrne a uvoľni prekrytie footprintu.
        IndicatrixAPI.instance?.OnFactoryUnregistered(inst);

        return true;
    }

    /// <summary>Odregistruje továreň stojacu na tile [x,z], ak nejaká je.</summary>
    public static bool UnregisterAt(int x, int z)
    {
        return Unregister(GetFactoryAt(x, z));
    }

    /// <summary>
    /// Vymaže celý register V RÁMCI BEŽIACEJ SCÉNY (napr. pred Load Game) –
    /// vrátane zničenia modelov tovární v scéne. Volá sa z
    /// IndicatrixAPI.ReadFactories(). Pri prechode na novú scénu sa register
    /// čistí automaticky sám (HandleSceneLoaded vyššie), volať to netreba.
    /// </summary>
    public static void Clear()
    {
        ClearData();

        // Vizuál: znič všetky modely tovární (napr. pred načítaním novej mapy).
        IndicatrixAPI.instance?.OnAllFactoriesCleared();

        Debug.Log("[FactoryRegistry] Register vyčistený.");
    }
}