using UnityEngine;

/// <summary>
/// VehicleModelLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 3D MODELOV cestných vozidiel (voliteľná).
/// Cestný ekvivalent TrainModelLibrary – plne analogický.
///
/// Tento komponent drží VOLITEĽNÉ prefab-y skutočných 3D modelov pre každý
/// z 16 typov vozidiel. Prefab-y sa do jednotlivých slotov priradia v Unity
/// Inspectore jednoduchým pretiahnutím myšou (drag-and-drop).
///
/// ─────────────────────────────────────────────────────────────────────────
/// LOGIKA NAHRADENIA (presne podľa zadania):
///
///   • Ak je pre daný typ vozidla priradený prefab (slot != None / != null):
///         → VehicleSystem.CreateVehiclePart vytvorí inštanciu tohto modelu
///           NAMIESTO pôvodného kvádra.
///
///   • Ak prefab priradený NIE JE (slot ostane prázdny = null):
///         → vytvorí sa pôvodný MAGENTA kváder, presne ako doteraz.
///           Nič sa nemení.
///
/// Voľba je per-typ a úplne nezávislá: môžeš mať model len pre niektoré typy
/// vozidiel, zvyšok ostane kvádrami.
///
/// ─────────────────────────────────────────────────────────────────────────
/// POUŽITIE V EDITORE:
///   1. Vytvor prázdny GameObject (napr. "VehicleModelLibrary") a pridaj naň
///      tento komponent. (Pokojne ho daj na ten istý objekt ako VehicleSystem.)
///   2. Do požadovaných slotov pretiahni myšou prefab-y 3D modelov.
///   3. (Voliteľné) Na komponente VehicleSystem prirad tento komponent do poľa
///      "Model Library". Ak ho nepriradíš, VehicleSystem si knižnicu dohľadá
///      automaticky (cez statickú inštanciu alebo FindObjectOfType).
///
/// ORIENTÁCIA MODELU:
///   Pohybová logika natáča telo vozidla tak, že jeho lokálna os +Z smeruje
///   v smere jazdy (rovnako ako dlhšia os pôvodného kvádra). Prefab by mal
///   byť teda "tvárou" otočený na +Z. Ak model mieri inou osou, vnor ho pod
///   prázdny rodičovský objekt natočený správne a ako prefab prirad rodiča.
///
/// MIERKA MODELU:
///   Model si nesie vlastnú mierku (autorom zvolenú). VehicleSystem na prefab
///   NEAPLIKUJE rozmery kvádra (VEHICLE_SCALE), takže model zachová svoju
///   veľkosť. Prefab teda priprav v mierke zodpovedajúcej jednej dlaždici.
///
/// PORADIE SLOTOV:
///   Poradie je ZHODNÉ s VehicleCatalog.All (VehicleStock.cs):
///     index 0 = Coal Truck (Id 1) ... index 15 = Electronics Truck (Id 16).
///   VehicleSystem zistí index zvoleného typu z katalógu a podľa neho vyberie
///   príslušný slot, takže mapovanie je vždy konzistentné.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class VehicleModelLibrary : MonoBehaviour
{
    /// <summary>
    /// Statická inštancia pre pohodlné dohľadanie z VehicleSystem bez nutnosti
    /// priraďovať referenciu ručne. Prvá vytvorená inštancia "vyhrá".
    /// </summary>
    public static VehicleModelLibrary instance;

    // =========================================================================
    // VOZIDLÁ (16 typov) – poradie ZHODNÉ s VehicleCatalog.All (VehicleStock.cs)
    //   index 0 = Coal Truck (Id 1) ... index 15 = Electronics Truck (Id 16)
    // Každý slot je voliteľný (môže ostať prázdny = kváder).
    // =========================================================================

    [Header("Vozidlá (voliteľné – prázdne = kváder)")]
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
    // INDEXOVANÉ POLE – zostavené z pomenovaných polí vyššie.
    // Vďaka nemu je vyhľadanie podľa indexu (z dropdownu / katalógu) O(1)
    // a poradie je 1:1 s VehicleCatalog.
    // =========================================================================

    private GameObject[] _vehiclePrefabs;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Debug.LogWarning("[VehicleModelLibrary] V scéne je viac inštancií – " +
                             "ponechávam prvú. Tento komponent sa ignoruje pri auto-dohľadaní.");

        BuildArray();
    }

    /// <summary>
    /// Naplní indexované pole z pomenovaných serializovaných polí.
    /// Volá sa v Awake; je idempotentné a bezpečné volať aj opakovane.
    /// </summary>
    private void BuildArray()
    {
        _vehiclePrefabs = new[]
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
    /// Vráti prefab 3D modelu vozidla pre zadaný index typu (0..15), alebo
    /// null ak nie je priradený / index je mimo rozsahu. null = ponechať kváder.
    /// </summary>
    public GameObject GetVehiclePrefab(int vehicleTypeIndex)
    {
        if (_vehiclePrefabs == null) BuildArray();
        if (vehicleTypeIndex < 0 || vehicleTypeIndex >= _vehiclePrefabs.Length) return null;
        return _vehiclePrefabs[vehicleTypeIndex];
    }
}
