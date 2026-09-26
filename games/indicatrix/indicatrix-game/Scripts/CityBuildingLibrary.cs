using UnityEngine;

/// <summary>
/// CityBuildingLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 3D MODELOV / PREFABOV pre BUDOVY MIEST – analógia k
/// <see cref="TileModelLibrary"/>, ale pre mestskú výstavbu (CityManager.cs).
///
/// Drží VOLITEĽNÉ prefab-y skutočných 3D modelov budov. Definovaných je 15
/// budov rozdelených do troch výškových sád (tier) po 5 typoch:
///
///   • Low    (Small Town)  – nízke budovy   (5 typov)
///   • Medium (Medium Town) – stredné budovy (5 typov)
///   • High   (Big Town)    – vysoké budovy  (5 typov)
///
/// ─────────────────────────────────────────────────────────────────────────
/// DVOJFÁZOVÉ NAČÍTANIE (presne podľa zadania) – rieši CityManager:
///
///   1. FÁZA: Ak pre daný typ budovy NIE JE priradený prefab (slot = null),
///            CityManager vygeneruje budovu z PRIMITÍVA (box kopírujúci šírku
///            jedného tile, s výškou podľa tieru, odlíšený farbou typu).
///
///   2. FÁZA: Ak prefab priradený JE (slot != null), použije sa tento prefab
///            a primitíva sa NEGENERUJE.
///
/// Voľba je per-typ a úplne nezávislá: pokojne maj model len pre niektoré
/// budovy, zvyšok ostane na primitívach.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class CityBuildingLibrary : MonoBehaviour
{
    /// <summary>Statická inštancia pre pohodlné dohľadanie z CityManager.</summary>
    public static CityBuildingLibrary instance;

    /// <summary>Počet typov budov v jednej výškovej sade (tier).</summary>
    public const int VariantsPerTier = 5;

    /// <summary>
    /// Výšková sada budovy. Zároveň určuje, z ktorých budov môže mesto čerpať
    /// (Small Town → len Low; Medium Town → Low/Medium; Big Town → Low/Medium/High).
    /// </summary>
    public enum BuildingTier
    {
        Low = 0,    // nízke budovy  – Small Town
        Medium = 1, // stredné       – Medium Town
        High = 2    // vysoké        – Big Town
    }

    // =========================================================================
    // LOW – nízke budovy (Small Town) – 5 typov
    // =========================================================================
    [Header("LOW / SMALL TOWN – nízke budovy (voliteľné, prázdne = primitíva)")]
    [Tooltip("Prefab nízkej budovy typu 1. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject lowBuilding1;
    [Tooltip("Prefab nízkej budovy typu 2. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject lowBuilding2;
    [Tooltip("Prefab nízkej budovy typu 3. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject lowBuilding3;
    [Tooltip("Prefab nízkej budovy typu 4. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject lowBuilding4;
    [Tooltip("Prefab nízkej budovy typu 5. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject lowBuilding5;

    // =========================================================================
    // MEDIUM – stredné budovy (Medium Town) – 5 typov
    // =========================================================================
    [Header("MEDIUM / MEDIUM TOWN – stredné budovy (voliteľné, prázdne = primitíva)")]
    [Tooltip("Prefab strednej budovy typu 1. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject mediumBuilding1;
    [Tooltip("Prefab strednej budovy typu 2. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject mediumBuilding2;
    [Tooltip("Prefab strednej budovy typu 3. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject mediumBuilding3;
    [Tooltip("Prefab strednej budovy typu 4. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject mediumBuilding4;
    [Tooltip("Prefab strednej budovy typu 5. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject mediumBuilding5;

    // =========================================================================
    // HIGH – vysoké budovy (Big Town) – 5 typov
    // =========================================================================
    [Header("HIGH / BIG TOWN – vysoké budovy (voliteľné, prázdne = primitíva)")]
    [Tooltip("Prefab vysokej budovy typu 1. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject highBuilding1;
    [Tooltip("Prefab vysokej budovy typu 2. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject highBuilding2;
    [Tooltip("Prefab vysokej budovy typu 3. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject highBuilding3;
    [Tooltip("Prefab vysokej budovy typu 4. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject highBuilding4;
    [Tooltip("Prefab vysokej budovy typu 5. Prázdne = vygenerovaná primitíva.")]
    [SerializeField] private GameObject highBuilding5;

    // =========================================================================
    // LOOKUP – [tier][variant] → prefab. Zostavené zo serializovaných polí.
    // =========================================================================
    private GameObject[][] _byTier;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
            Debug.LogWarning("[CityBuildingLibrary] V scéne je viac inštancií – " +
                             "ponechávam prvú. Tento komponent sa ignoruje pri auto-dohľadaní.");

        BuildMaps();
    }

    /// <summary>
    /// Naplní lookup z pomenovaných serializovaných slotov. Idempotentné –
    /// bezpečné volať aj opakovane.
    /// </summary>
    private void BuildMaps()
    {
        _byTier = new GameObject[3][];
        _byTier[(int)BuildingTier.Low] = new[]
            { lowBuilding1, lowBuilding2, lowBuilding3, lowBuilding4, lowBuilding5 };
        _byTier[(int)BuildingTier.Medium] = new[]
            { mediumBuilding1, mediumBuilding2, mediumBuilding3, mediumBuilding4, mediumBuilding5 };
        _byTier[(int)BuildingTier.High] = new[]
            { highBuilding1, highBuilding2, highBuilding3, highBuilding4, highBuilding5 };
    }

    /// <summary>
    /// Vráti prefab budovy pre daný tier a index typu (0..4), alebo null ak
    /// preň nie je priradený žiadny prefab (→ CityManager vygeneruje primitívu).
    /// </summary>
    public GameObject GetBuildingPrefab(BuildingTier tier, int variantIndex)
    {
        if (_byTier == null) BuildMaps();

        int t = (int)tier;
        if (t < 0 || t >= _byTier.Length) return null;

        GameObject[] arr = _byTier[t];
        if (arr == null || variantIndex < 0 || variantIndex >= arr.Length) return null;

        return arr[variantIndex];
    }
}
