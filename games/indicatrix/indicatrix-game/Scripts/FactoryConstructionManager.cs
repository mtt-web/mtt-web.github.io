using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FactoryConstructionManager
/// ─────────────────────────────────────────────────────────────────────────
/// Nad stredom práve postavenej továrne zobrazí plávajúci 3D label
/// "Building process: NN%", poháňa časovač výstavby (FactoryInstance), a po
/// dosiahnutí 100 % nechá label ešte 2 s a potom ho odstráni.
///
/// VYKRESLENIE LABELU:
///   Rovnako ako CityManager (mestá) a StationLabelManager (stanice), aj tento
///   manager deleguje samotné vykreslenie na univerzálny komponent CityLabel
///   (3D TextMeshPro + čierne pozadie + billboard pre ortho kameru).
///   CityLabel sa stará aj o to, aby label nebol prekrytý vysokými budovami/
///   mrakodrapmi – beží na zdieľanej overlay vrstve ("CityLabelOverlay") a
///   vykresľuje sa cez zdieľanú overlay kameru, ktorá kreslí AŽ PO hlavnej
///   kamere (viď CityLabel.cs pre detaily). Táto overlay kamera/vrstva je
///   STATICKÁ a zdieľaná so všetkými ostatnými labelmi v hre – vytvorí sa
///   len raz, bez ohľadu na to, kto (mesto/stanica/továreň) ju prvý vyvolá.
///
/// DÔLEŽITÉ – VÝŠKA LABELU vs. ZOOM:
///   Pri ortho kamere s malým orthographicSize (napr. 4 = najbližší zoom) je
///   na obrazovke vidno len ~8 svetových jednotiek na výšku. Ak je labelHeight
///   priveľký (napr. 5), label sa premietne NAD horný okraj obrazovky a nevidno
///   ho, hoci existuje. Diagnostika nižšie vypíše viewport pozíciu – ak je y &gt; 1,
///   zníž labelHeight (Inspector na tomto komponente).
///
///   POZN.: Po nahradení tohto skriptu si Unity PONECHÁ hodnoty zo starého
///   komponentu v Inspectore (serializované). Ak tam máš labelHeight = 5,
///   zmena defaultu v kóde sa NEPREJAVÍ – uprav hodnotu priamo v Inspectore
///   (alebo Reset komponentu).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class FactoryConstructionManager : MonoBehaviour
{
    // =====================================================================
    // SINGLETON
    // =====================================================================

    private static FactoryConstructionManager _instance;

    public static FactoryConstructionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<FactoryConstructionManager>();
                if (_instance == null)
                {
                    var go = new GameObject("FactoryConstructionManager");
                    _instance = go.AddComponent<FactoryConstructionManager>();
                }
            }
            return _instance;
        }
    }

    // =====================================================================
    // NASTAVENIA (laditeľné v Inspectore)
    // =====================================================================

    [Header("Label – pozícia a vzhľad")]
    [Tooltip("Výška labelu nad stredom továrne (svetové jednotky). Pri malom " +
             "orthographicSize drž nízko (napr. 2), inak label ujde nad obraz.")]
    [SerializeField] private float labelHeight = 1.0f;

    [Tooltip("Veľkosť písma 3D TextMeshPro labelu (uprav podľa mierky hry).")]
    [SerializeField] private float fontSize = 3.0f;

    [Tooltip("Uniformná mierka celého labelu (jemné doladenie veľkosti).")]
    [SerializeField] private float labelScale = 1.0f;

    [Tooltip("Farba textu.")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("Farba POZADIA labelu (čierny box za textom pre čitateľnosť).")]
    [SerializeField] private Color backgroundColor = Color.black;

    [Tooltip("Okraj pozadia okolo textu (X = vodorovne, Y = zvisle) v jednotkách textu.")]
    [SerializeField] private Vector2 backgroundPadding = new Vector2(1.5f, 0.5f);

    [Tooltip("Farba obrysu textu (kontrast).")]
    [SerializeField] private Color outlineColor = Color.black;

    [Tooltip("Koľko sekúnd ostane label po 100 % viditeľný, než zmizne (zadanie: 2 s).")]
    [SerializeField] private float lingerAfterComplete = 2f;

    [Tooltip("Predpona pred percentami.")]
    [SerializeField] private string labelPrefix = "Building process: ";

    [Tooltip("Vypisovať diagnostiku do Console.")]
    [SerializeField] private bool verboseLog = true;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    private class ActiveConstruction
    {
        public FactoryInstance factory;
        public GameObject labelGO;
        public CityLabel cityLabel;
        public bool completed;
        public float lingerTimer;
    }

    private readonly List<ActiveConstruction> active = new List<ActiveConstruction>();

    private Camera cachedCam;
    private bool warnedNoCamera;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveConstruction ac = active[i];
            if (ac.factory == null) { DestroyEntry(i); continue; }

            if (!ac.completed)
            {
                ac.factory.AdvanceConstruction(dt);
                UpdateLabelText(ac);

                if (ac.factory.IsComplete)
                {
                    ac.completed = true;
                    ac.lingerTimer = lingerAfterComplete;
                    UpdateLabelText(ac);
                }
            }
            else
            {
                ac.lingerTimer -= dt;
                if (ac.lingerTimer <= 0f) { DestroyEntry(i); continue; }
            }

            // Billboard (natočenie do roviny obrazovky) rieši CityLabel sám
            // vo svojom vlastnom Update – tu už netreba nič robiť.
        }
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    public void BeginConstruction(FactoryInstance factory)
    {
        if (factory == null)
        {
            if (verboseLog) Debug.LogWarning("[FCM] BeginConstruction: factory == null.");
            return;
        }

        if (verboseLog)
            Debug.Log($"[FCM] BeginConstruction '{factory.Name}' " +
                      $"buildTime={factory.BuildTime}s underConstruction={factory.IsUnderConstruction}");

        if (!factory.IsUnderConstruction)
        {
            if (verboseLog)
                Debug.Log($"[FCM] '{factory.Name}' nemá kladný BuildingTime – label sa nevytvára.");
            return;
        }

        for (int i = 0; i < active.Count; i++)
            if (active[i].factory == factory) return;

        var ac = new ActiveConstruction { factory = factory, completed = false, lingerTimer = 0f };

        Vector3 pos = ComputeCenterTop(factory);
        string initialText = labelPrefix + factory.BuildPercent + "%";

        ac.labelGO = new GameObject("FactoryBuildLabel");
        ac.labelGO.transform.SetParent(transform, false);

        ac.cityLabel = ac.labelGO.AddComponent<CityLabel>();
        ac.cityLabel.Initialize(initialText, pos, fontSize, labelScale,
                                 textColor, backgroundColor, outlineColor,
                                 backgroundPadding);

        active.Add(ac);

        if (verboseLog)
            LogDiagnostics(ac, ResolveCamera());
    }

    public void ClearAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            DestroyEntry(i);
    }

    // =====================================================================
    // DIAGNOSTIKA
    // =====================================================================

    private void LogDiagnostics(ActiveConstruction ac, Camera cam)
    {
        Vector3 sz = ac.cityLabel != null ? ac.cityLabel.GetTextSize() : Vector3.zero;
        Vector3 pos = ac.labelGO.transform.position;

        string camInfo = "kamera = NULL (label sa nebude natáčať)";
        string viewInfo = "";
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(pos);
            bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            camInfo = $"kamera='{cam.name}' ortho={cam.orthographic} size={cam.orthographicSize} " +
                      $"cullingMask=0x{cam.cullingMask:X} labelLayer={ac.labelGO.layer}";
            viewInfo = $" viewport=({vp.x:F2},{vp.y:F2},{vp.z:F2}) onScreen={onScreen}" +
                       (onScreen ? "" : "  ← MIMO OBRAZU! Zníž labelHeight alebo oddiaľ kameru.");
        }

        Debug.Log($"[FCM] '{ac.factory.Name}' label pos={pos} veľkosť≈{sz}; {camInfo}.{viewInfo}");
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    private Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;

        cachedCam = Camera.main;
        if (cachedCam == null) cachedCam = FindFirstObjectByType<Camera>();

        if (cachedCam == null && !warnedNoCamera)
        {
            warnedNoCamera = true;
            Debug.LogWarning("[FCM] Nenašla sa žiadna kamera – billboard nebude fungovať.");
        }
        return cachedCam;
    }

    private void UpdateLabelText(ActiveConstruction ac)
    {
        if (ac.cityLabel == null || ac.factory == null) return;
        ac.cityLabel.SetText(labelPrefix + ac.factory.BuildPercent + "%");
    }

    private Vector3 ComputeCenterTop(FactoryInstance f)
    {
        float cx = f.OriginX + f.Width * 0.5f;
        float cz = f.OriginZ + f.Depth * 0.5f;
        float h = SampleFootprintHeight(f);
        return new Vector3(cx, h + labelHeight, cz);
    }

    private float SampleFootprintHeight(FactoryInstance f)
    {
        TerrainManager tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null || tm.terrainWidth <= 0)
            return 0f;

        int vWidth = tm.terrainWidth + 1;
        float sum = 0f;
        int n = 0;

        int[] xs = { f.OriginX, f.OriginX + f.Width };
        int[] zs = { f.OriginZ, f.OriginZ + f.Depth };

        foreach (int xRaw in xs)
            foreach (int zRaw in zs)
            {
                int x = Mathf.Clamp(xRaw, 0, tm.terrainWidth);
                int z = Mathf.Clamp(zRaw, 0, tm.terrainWidth);
                int idx = z * vWidth + x;
                if (idx >= 0 && idx < tm.coordsF.Length) { sum += tm.coordsF[idx].y; n++; }
            }

        return n > 0 ? sum / n : 0f;
    }

    private void DestroyEntry(int index)
    {
        ActiveConstruction ac = active[index];
        if (ac.labelGO != null) Destroy(ac.labelGO);
        active.RemoveAt(index);
    }
}