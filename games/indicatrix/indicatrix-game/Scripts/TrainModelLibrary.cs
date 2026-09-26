using UnityEngine;

/// <summary>
/// TrainModelLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 3D MODELOV vlakovej súpravy (voliteľná).
///
/// Tento komponent drží VOLITEĽNÉ prefab-y skutočných 3D modelov pre každý
/// typ lokomotívy (3 typy) a každý typ vagónu (16 typov). Prefab-y sa do
/// jednotlivých slotov priradia v Unity Inspectore jednoduchým pretiahnutím
/// myšou (drag-and-drop).
///
/// ─────────────────────────────────────────────────────────────────────────
/// LOGIKA NAHRADENIA (presne podľa zadania):
///
///   • Ak je pre daný typ priradený prefab (slot != None / != null):
///         → TrainSystem.CreateConsistPart vytvorí inštanciu tohto modelu
///           NAMIESTO pôvodného kvádra.
///
///   • Ak prefab priradený NIE JE (slot ostane prázdny = null):
///         → vytvorí sa pôvodný kváder (CYAN lokomotíva / GREY vagón),
///           presne ako doteraz. Nič sa nemení.
///
/// Voľba je teda per-typ a úplne nezávislá: môžeš mať model len pre jednu
/// lokomotívu a tri vagóny, zvyšok ostane kvádrami.
///
/// ─────────────────────────────────────────────────────────────────────────
/// POUŽITIE V EDITORE:
///   1. Vytvor prázdny GameObject (napr. "TrainModelLibrary") a pridaj naň
///      tento komponent. (Pokojne ho daj na ten istý objekt ako TrainSystem.)
///   2. Do požadovaných slotov pretiahni myšou prefab-y 3D modelov.
///   3. (Voliteľné) Na komponente TrainSystem prirad tento komponent do poľa
///      "Model Library". Ak ho nepriradíš, TrainSystem si knižnicu dohľadá
///      automaticky (cez statickú inštanciu alebo FindObjectOfType).
///
/// ORIENTÁCIA MODELU:
///   Pohybová logika natáča člena súpravy tak, že jeho lokálna os +Z smeruje
///   v smere jazdy (rovnako ako dlhšia os pôvodného kvádra). Prefab by mal
///   byť teda "tvárou" otočený na +Z. Ak model mieri inou osou, vnor ho pod
///   prázdny rodičovský objekt natočený správne a ako prefab prirad rodiča.
///
/// MIERKA MODELU:
///   Model si nesie vlastnú mierku (autorom zvolenú). TrainSystem na prefab
///   NEAPLIKUJE rozmery kvádra (CONSIST_SCALE), takže model zachová svoju
///   veľkosť. Prefab teda priprav v mierke zodpovedajúcej jednej dlaždici.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class TrainModelLibrary : MonoBehaviour
{
    /// <summary>
    /// Statická inštancia pre pohodlné dohľadanie z TrainSystem bez nutnosti
    /// priraďovať referenciu ručne. Prvá vytvorená inštancia "vyhrá".
    /// </summary>
    public static TrainModelLibrary instance;

    // =========================================================================
    // LOKOMOTÍVY (3 typy) – poradie ZHODNÉ s TrainCatalog.All (TrainStock.cs)
    //   index 0 = Iron Dragon, 1 = Desert Runner, 2 = Thunderbolt
    // Každý slot je voliteľný (môže ostať prázdny = kváder).
    // =========================================================================

    [Header("Lokomotívy (voliteľné – prázdne = kváder)")]
    [Tooltip("3D model pre lokomotívu 'Iron Dragon' (index 0). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject ironDragonPrefab;

    [Tooltip("3D model pre lokomotívu 'Desert Runner' (index 1). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject desertRunnerPrefab;

    [Tooltip("3D model pre lokomotívu 'Thunderbolt' (index 2). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject thunderboltPrefab;

    // =========================================================================
    // VAGÓNY (16 typov) – poradie ZHODNÉ s WagonCatalog.All (TrainStock.cs)
    //   index 0 = Coal Truck (Id 1) ... index 15 = Electronics Truck (Id 16)
    // Každý slot je voliteľný (môže ostať prázdny = kváder).
    // =========================================================================

    [Header("Vagóny (voliteľné – prázdne = kváder)")]
    [Tooltip("3D model pre 'Coal Truck' (index 0). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject coalTruckPrefab;

    [Tooltip("3D model pre 'Wood Truck' (index 1). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject woodTruckPrefab;

    [Tooltip("3D model pre 'Iron Ore Truck' (index 2). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject ironOreTruckPrefab;

    [Tooltip("3D model pre 'Gold Truck' (index 3). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject goldTruckPrefab;

    [Tooltip("3D model pre 'Silver Truck' (index 4). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject silverTruckPrefab;

    [Tooltip("3D model pre 'Livestock Truck' (index 5). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject livestockTruckPrefab;

    [Tooltip("3D model pre 'Grain Truck' (index 6). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject grainTruckPrefab;

    [Tooltip("3D model pre 'Oil Truck' (index 7). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject oilTruckPrefab;

    [Tooltip("3D model pre 'Boards Truck' (index 8). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject boardsTruckPrefab;

    [Tooltip("3D model pre 'Plastic Truck' (index 9). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject plasticTruckPrefab;

    [Tooltip("3D model pre 'Meat Truck' (index 10). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject meatTruckPrefab;

    [Tooltip("3D model pre 'Flour Truck' (index 11). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject flourTruckPrefab;

    [Tooltip("3D model pre 'Metals Truck' (index 12). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject metalsTruckPrefab;

    [Tooltip("3D model pre 'Glass Truck' (index 13). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject glassTruckPrefab;

    [Tooltip("3D model pre 'Furniture Truck' (index 14). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject furnitureTruckPrefab;

    [Tooltip("3D model pre 'Electronics Truck' (index 15). Prázdne = pôvodný kváder.")]
    [SerializeField] private GameObject electronicsTruckPrefab;

    // =========================================================================
    // INDEXOVANÉ POLIA – zostavené z pomenovaných polí vyššie.
    // Vďaka nim je vyhľadanie podľa indexu (z dropdownu) O(1) a poradie je
    // 1:1 s TrainCatalog / WagonCatalog.
    // =========================================================================

    private GameObject[] _locomotivePrefabs;
    private GameObject[] _wagonPrefabs;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Debug.LogWarning("[TrainModelLibrary] V scéne je viac inštancií – " +
                             "ponechávam prvú. Tento komponent sa ignoruje pri auto-dohľadaní.");

        BuildArrays();
    }

    /// <summary>
    /// Naplní indexované polia z pomenovaných serializovaných polí.
    /// Volá sa v Awake; je idempotentné a bezpečné volať aj opakovane.
    /// </summary>
    private void BuildArrays()
    {
        _locomotivePrefabs = new[]
        {
            ironDragonPrefab,    // 0
            desertRunnerPrefab,  // 1
            thunderboltPrefab,   // 2
        };

        _wagonPrefabs = new[]
        {
            coalTruckPrefab,        // 0  (Id 1)
            woodTruckPrefab,        // 1  (Id 2)
            ironOreTruckPrefab,     // 2  (Id 3)
            goldTruckPrefab,        // 3  (Id 4)
            silverTruckPrefab,      // 4  (Id 5)
            livestockTruckPrefab,   // 5  (Id 6)
            grainTruckPrefab,       // 6  (Id 7)
            oilTruckPrefab,         // 7  (Id 8)
            boardsTruckPrefab,      // 8  (Id 9)
            plasticTruckPrefab,     // 9  (Id 10)
            meatTruckPrefab,        // 10 (Id 11)
            flourTruckPrefab,       // 11 (Id 12)
            metalsTruckPrefab,      // 12 (Id 13)
            glassTruckPrefab,       // 13 (Id 14)
            furnitureTruckPrefab,   // 14 (Id 15)
            electronicsTruckPrefab, // 15 (Id 16)
        };
    }

    /// <summary>
    /// Vráti prefab 3D modelu lokomotívy pre zadaný index typu (0..2), alebo
    /// null ak nie je priradený / index je mimo rozsahu. null = ponechať kváder.
    /// </summary>
    public GameObject GetLocomotivePrefab(int trainTypeIndex)
    {
        if (_locomotivePrefabs == null) BuildArrays();
        if (trainTypeIndex < 0 || trainTypeIndex >= _locomotivePrefabs.Length) return null;
        return _locomotivePrefabs[trainTypeIndex];
    }

    /// <summary>
    /// Vráti prefab 3D modelu vagónu pre zadaný index typu (0..15), alebo
    /// null ak nie je priradený / index je mimo rozsahu. null = ponechať kváder.
    /// </summary>
    public GameObject GetWagonPrefab(int wagonTypeIndex)
    {
        if (_wagonPrefabs == null) BuildArrays();
        if (wagonTypeIndex < 0 || wagonTypeIndex >= _wagonPrefabs.Length) return null;
        return _wagonPrefabs[wagonTypeIndex];
    }
}
