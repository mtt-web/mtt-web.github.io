using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StationLabelManager
/// ─────────────────────────────────────────────────────────────────────────
/// Spravuje TRVALÉ plávajúce popisy (labely) staníc – RAIL aj ROAD. Keď hráč
/// postaví stanicu (StationHorizontal / StationVertical), nad ňou sa zobrazí
/// nápis s jej interným názvom, napr. "Station [17, 23]". Label existuje, kým
/// existuje stanica; pri demolácii stanice sa odstráni.
///
/// VZŤAH K FactoryConstructionManager.cs:
///   Vizuálne je label rovnaký typ plávajúceho 3D nápisu ako pri továrňach –
///   billboard ku kamere s čiernym pozadím. Samotné vykreslenie rieši komponent
///   CityLabel (univerzálny plávajúci label, billboard pre ortho kameru), takže
///   sa logika nedotuje. Na rozdiel od FactoryConstructionManager-u (ktorý label
///   po 100 % výstavby zruší) je tento label TRVALÝ.
///
/// SINGLETON s auto-vytvorením (rovnaký vzor ako FactoryConstructionManager):
///   Netreba ho ručne pridávať do scény – pri prvom použití cez Instance sa
///   vytvorí sám. Ak ho do scény pridáš ručne, môžeš ladiť vzhľad v Inspectore.
///
/// HOOKY V GameManager.cs:
///   • po SetTile stanice (tileID 2)  → CreateLabel(x, z),
///   • v Demolish vetve, ak bola zmazaná stanica (tileID 2) → RemoveLabel(x, z).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StationLabelManager : MonoBehaviour
{
    // =====================================================================
    // SINGLETON
    // =====================================================================

    private static StationLabelManager _instance;

    public static StationLabelManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<StationLabelManager>();
                if (_instance == null)
                {
                    var go = new GameObject("StationLabelManager");
                    _instance = go.AddComponent<StationLabelManager>();
                }
            }
            return _instance;
        }
    }

    // =====================================================================
    // NASTAVENIA (laditeľné v Inspectore, ak komponent pridáš ručne)
    // =====================================================================

    [Header("Label – pozícia a vzhľad")]
    [Tooltip("Výška nápisu nad povrchom tile stanice (svetové jednotky). Ručne nastaviteľná.")]
    [SerializeField] private float labelHeight = 2.0f;

    [Tooltip("Veľkosť písma popisku stanice.")]
    [SerializeField] private float fontSize = 3.0f;

    [Tooltip("Uniformná mierka celého labelu (jemné doladenie veľkosti).")]
    [SerializeField] private float labelScale = 1.0f;

    [Tooltip("Farba textu.")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("Farba pozadia (čierny box za textom pre čitateľnosť).")]
    [SerializeField] private Color backgroundColor = Color.black;

    [Tooltip("Farba obrysu textu.")]
    [SerializeField] private Color outlineColor = Color.black;

    [Tooltip("Okraj pozadia okolo textu (X = vodorovne, Y = zvisle).")]
    [SerializeField] private Vector2 backgroundPadding = new Vector2(1.5f, 0.5f);

    [Tooltip("Predpona názvu stanice (za ňou nasleduje [x, z]).")]
    [SerializeField] private string labelPrefix = "Station ";

    // =====================================================================
    // INTERNÝ STAV – labely podľa tile [x,z]
    // =====================================================================

    private readonly Dictionary<long, GameObject> labels = new Dictionary<long, GameObject>();

    private static long TileKey(int x, int z) => ((long)x << 32) | (uint)z;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Vytvorí trvalý popisok stanice nad tile [x,z]. Zobrazený NÁZOV je menný
    /// názov podľa mesta (napr. "Copenhagen West") z CityManager-a; ak stanica
    /// nepatrí žiadnemu mestu, použije sa interný/fallback názov "Station [x, z]".
    /// Ak pre tento tile label už existuje, nič sa nestane (idempotentné).
    /// </summary>
    public void CreateLabel(int x, int z)
    {
        long key = TileKey(x, z);
        if (labels.ContainsKey(key)) return;

        float surfaceY = SampleTileSurfaceY(x, z);
        string text = ResolveStationName(x, z);

        var go = new GameObject($"StationLabel_{x}_{z}");
        go.transform.SetParent(transform, false);

        var label = go.AddComponent<CityLabel>();
        label.Initialize(
            text,
            new Vector3(x + 0.5f, surfaceY + labelHeight, z + 0.5f),
            fontSize, labelScale,
            textColor, backgroundColor, outlineColor,
            backgroundPadding);

        labels[key] = go;
    }

    /// <summary>
    /// Menný názov stanice z mesta (CityManager.GetStationDisplayName). Ak
    /// CityManager nie je v scéne, fallback na "Station [x, z]" s predponou.
    /// </summary>
    private string ResolveStationName(int x, int z)
    {
        if (CityManager.instance != null)
            return CityManager.instance.GetStationDisplayName(x, z);
        return $"{labelPrefix}[{x}, {z}]";
    }

    /// <summary>
    /// Odstráni popisok stanice na tile [x,z], ak existuje. Volá sa pri
    /// demolácii stanice. Ak label neexistuje, nič sa nestane.
    /// </summary>
    public void RemoveLabel(int x, int z)
    {
        long key = TileKey(x, z);
        if (labels.TryGetValue(key, out GameObject go))
        {
            if (go != null) Destroy(go);
            labels.Remove(key);
        }
    }

    /// <summary>Odstráni VŠETKY popisy staníc (napr. pri načítaní novej mapy).</summary>
    public void ClearAll()
    {
        foreach (var kv in labels)
            if (kv.Value != null) Destroy(kv.Value);
        labels.Clear();
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    /// <summary>
    /// Priemerná výška terénu cez 4 rohové vertexy tile [x,z]. Stanice sa
    /// stavajú len na rovine (4 rovnaké Y), priemer je teda totožný s rohom,
    /// ale počíta sa robustne pre prípad budúcich zmien.
    /// </summary>
    private float SampleTileSurfaceY(int x, int z)
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null || tm.terrainWidth <= 0)
            return 0f;

        int w = tm.terrainWidth + 1;

        float Y(int vx, int vz)
        {
            vx = Mathf.Clamp(vx, 0, w - 1);
            vz = Mathf.Clamp(vz, 0, w - 1);
            return tm.coordsF[vz * w + vx].y;
        }

        return (Y(x, z) + Y(x, z + 1) + Y(x + 1, z + 1) + Y(x + 1, z)) * 0.25f;
    }
}