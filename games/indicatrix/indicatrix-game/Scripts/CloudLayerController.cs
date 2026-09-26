using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VRSTVA MRAKOV A HMLY VIAZANÁ NA ZOOM ÚROVEŇ
/// =====================================================================
///
/// ČO ROBÍ:
///  - Nad terénom drží vodorovnú vrstvu časticových emitorov (prefaby z
///    assetu "Fog Particles" alebo akékoľvek iné ParticleSystem prefaby).
///  - Vrstva je ZAPNUTÁ len na jednej zoom úrovni (predvolene index 2 =
///    najviac oddialený pohľad). Na ostatných úrovniach je kompletne
///    deaktivovaná – nulová záťaž na CPU/GPU, žiadne simulované častice.
///  - Prechod medzi stavmi je plynulý (fade in/out), nie skokový.
///  - Emitory sledujú kameru, takže vrstva vždy pokrýva celý záber, ale
///    častice sa simulujú vo SVETOVOM priestore → pri posúvaní kamery je
///    vidieť správnu parallaxu (mraky "stoja" nad mapou, nelepia sa na
///    obrazovku).
///  - Voliteľne vie na najvyššej úrovni pridať aj jemnú globálnu hmlu
///    (RenderSettings.fog) pre zjednotenie s časticami.
///
/// AKO TO ZAPADÁ DO HRY:
///  - Číta stav zo skriptu <see cref="IsometricCamera"/> (udalosť
///    OnZoomLevelChanged + vlastnosť CurrentZoomIndex). Nič iné v hre
///    nemodifikuje.
///  - Nevytvára žiadne collidery → neovplyvňuje Physics.Raycast v
///    GameManager.Update(), ktorý vyberá dlaždice pod myšou.
///
/// UMIESTNENIE:
///  - Prázdny GameObject v scéne (napr. "CloudLayer"), pozícia ľubovoľná –
///    skript si ju riadi sám. Rotácia musí ostať (0,0,0).
///
/// Autor poznámky: pridané ako samostatný modul, existujúce systémy hry
/// (GameManager, IndicatrixAPI, TerrainManager) ostávajú nedotknuté.
/// </summary>
[DisallowMultipleComponent]
public class CloudLayerController : MonoBehaviour
{
    // =====================================================================
    // REFERENCIE
    // =====================================================================

    [Header("Referencie")]
    [Tooltip("Izometrická kamera. Ak ostane prázdne, nájde sa automaticky.")]
    [SerializeField] private IsometricCamera isoCamera;

    [Tooltip("Prefaby mrakov/hmly (ParticleSystem). Napr. prefaby z assetu " +
             "Fog Particles. Ak je ich viac, striedajú sa v mriežke – vrstva " +
             "potom vyzerá menej pravidelne.")]
    [SerializeField] private ParticleSystem[] cloudPrefabs;

    // =====================================================================
    // KEDY SÚ MRAKY VIDITEĽNÉ
    // =====================================================================

    [Header("Viditeľnosť podľa zoomu")]
    [Tooltip("Index zoom úrovne, na ktorej sa mraky zobrazia. " +
             "0 = najbližšie, 1 = stredná, 2 = najviac oddialené (predvolené).")]
    [SerializeField] private int visibleAtZoomIndex = 2;

    // =====================================================================
    // UMIESTNENIE VRSTVY
    // =====================================================================

    [Header("Výška vrstvy")]
    [Tooltip("Výška vrstvy mrakov v svetových jednotkách (os Y). " +
             "Musí byť nad terénom a POD kamerou.")]
    [SerializeField] private float cloudHeight = 12.0f;

    [Tooltip("Minimálny odstup vrstvy pod kamerou. Ak by cloudHeight bola " +
             "vyššie, automaticky sa zníži (inak by mraky boli za kamerou).")]
    [SerializeField] private float minCameraClearance = 4.0f;

    [Tooltip("Automaticky posunie near clip plane kamery tak, aby vrstva " +
             "mrakov nebola orezaná. Pri ortografickej kamere je bezpečné " +
             "aj záporné near.")]
    [SerializeField] private bool autoFixCameraNearPlane = true;

    // =====================================================================
    // POKRYTIE ZÁBERU
    // =====================================================================

    [Header("Pokrytie záberu")]
    [Tooltip("Rozostup emitorov v mriežke (svetové jednotky). Menšia hodnota " +
             "= hustejšie mraky, ale viac častíc.")]
    [SerializeField] private float emitterSpacing = 16.0f;

    [Tooltip("Rezerva pokrytia mimo záber (1.0 = presne záber). " +
             "Zabraňuje viditeľnej hrane vrstvy pri pohybe kamery.")]
    [SerializeField] private float coveragePadding = 1.5f;

    [Tooltip("Poistka proti extrémnemu počtu emitorov. Pri prekročení sa " +
             "rozostup automaticky zväčší.")]
    [SerializeField] private int maxEmitters = 200;

    [Tooltip("Náhodné rozhodenie emitorov okolo ich bunky, aby mriežka " +
             "nebola opticky pravidelná.")]
    [SerializeField] private float positionJitter = 5.0f;

    [Tooltip("Vynúti časticiam World simulation space. Vypni len vtedy, ak " +
             "si prefab rieši priestor simulácie zámerne inak.")]
    [SerializeField] private bool forceWorldSimulationSpace = true;

    [Tooltip("Náhodné rozpätie mierky jednotlivých emitorov (min/max).")]
    [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.35f);

    [Tooltip("Seed pre náhodné rozhodenie – rovnaký seed = rovnaký vzhľad " +
             "vrstvy pri každom spustení hry.")]
    [SerializeField] private int randomSeed = 12345;

    // =====================================================================
    // PRELÍNANIE (FADE)
    // =====================================================================

    [Header("Prelínanie")]
    [Tooltip("Čas nábehu mrakov po prepnutí na najvyššiu zoom úroveň (s).")]
    [SerializeField] private float fadeInDuration = 0.8f;

    [Tooltip("Čas zmiznutia mrakov po odchode z najvyššej úrovne (s).")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    [Tooltip("Maximálna nepriehľadnosť vrstvy (1 = pôvodná hodnota z prefabu).")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1.0f;

    [Tooltip("Koľko sekúnd simulácie sa 'predohreje' pri zapnutí, aby vrstva " +
             "nábehla už plná častíc a nebolo vidieť, ako sa napĺňa.")]
    [SerializeField] private float prewarmSeconds = 8.0f;

    // =====================================================================
    // VIETOR
    // =====================================================================

    [Header("Rýchlosť pohybu")]
    [Tooltip("Globálny násobič rýchlosti simulácie. Prenásobí simulationSpeed " +
             "KAŽDÉHO časticového systému vo vrstve, takže spomalí naraz úplne " +
             "všetko, čo prefab robí – rýchlosť častíc, rotáciu, animáciu " +
             "textúry aj mieru vzniku. Hustota vrstvy sa NEZMENÍ: častice síce " +
             "vznikajú pomalšie, ale zároveň úmerne dlhšie žijú.\n\n" +
             "1 = pôvodná rýchlosť z prefabu, 0.2 = päťkrát pomalšie.\n" +
             "Pre Transport Tycoon mierku (1 dlaždica = 1 jednotka) býva " +
             "rozumné 0.1 – 0.3.")]
    [SerializeField, Range(0.01f, 2f)] private float cloudSimulationSpeed = 0.2f;

    [Header("Vietor (voliteľné)")]
    [Tooltip("Pridá časticiam konštantnú rýchlosť – mraky pomaly plávajú. " +
             "Vypni, ak si pohyb riešiš priamo v prefabe.")]
    [SerializeField] private bool applyWind = true;

    [Tooltip("Rýchlosť vetra v rovine XZ (svetové jednotky za sekundu). " +
             "Pozor na mierku: 1.0 znamená, že mrak preletí jednu dlaždicu " +
             "za sekundu – to je na mapu veľa. Reálne hodnoty sú stotiny.")]
    [SerializeField] private Vector2 windVelocity = new Vector2(0.06f, 0.02f);

    // =====================================================================
    // GLOBÁLNA HMLA (voliteľné)
    // =====================================================================

    [Header("Globálna hmla (voliteľné)")]
    [Tooltip("Na najvyššej zoom úrovni zapne aj RenderSettings hmlu. " +
             "Nechaj vypnuté, ak ti stačia len častice.")]
    [SerializeField] private bool driveRenderSettingsFog = false;

    [SerializeField] private Color fogColor = new Color(0.78f, 0.82f, 0.88f, 1f);

    [Tooltip("Cieľová hustota hmly (Exponential) na najvyššej úrovni.")]
    [SerializeField] private float fogDensity = 0.010f;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    private readonly List<EmitterEntry> emitters = new List<EmitterEntry>();

    private Camera cam;
    private Transform camT;

    private float currentAlpha;      // aktuálna nepriehľadnosť vrstvy 0..1
    private float targetAlpha;       // cieľová nepriehľadnosť 0..1
    private bool fieldActive;        // sú emitory aktívne (SetActive)?
    private bool alphaDirty = true;  // treba prepísať MaterialPropertyBlock?
    private bool motionDirty = true; // treba prepísať rýchlosť simulácie a vietor?

    private float effectiveSpacing = 16.0f;   // rozostup po korekcii na maxEmitters
    private int builtForScreenW = -1;
    private int builtForScreenH = -1;

    // Pôvodné nastavenia hmly – aby sme scénu vrátili do východzieho stavu.
    private bool origFogEnabled;
    private Color origFogColor;
    private float origFogDensity;
    private FogMode origFogMode;
    private bool fogStateCaptured;

    /// <summary>Sú mraky aktuálne (aspoň čiastočne) viditeľné?</summary>
    public bool AreCloudsVisible => currentAlpha > 0.001f;

    /// <summary>Aktuálna nepriehľadnosť vrstvy (0..1) – pre ladenie/UI.</summary>
    public float CurrentAlpha => currentAlpha;

    // Názvy farebných vlastností, ktoré vieme tónovať. Pokrývajú Built-in aj
    // URP particle shadery aj typické custom shadery z Asset Store balíkov.
    private static readonly string[] ColorPropertyNames =
    {
        "_BaseColor",   // URP / Shader Graph
        "_Color",       // Built-in Standard / Particles Standard Unlit
        "_TintColor"    // staršie Particles/Additive, Particles/Alpha Blended
    };

    /// <summary>
    /// Jeden emitor v mriežke + údaje potrebné na tónovanie jeho materiálov.
    /// </summary>
    private class EmitterEntry
    {
        public GameObject go;
        public ParticleSystem[] systems;
        public ParticleSystemRenderer[] renderers;
        public MaterialPropertyBlock[] blocks;
        public List<KeyValuePair<int, Color>>[] baseColors; // shaderPropId → pôvodná farba
        public Color[] baseStartColors;                     // fallback cez main.startColor
        public float[] baseSimSpeeds;                       // pôvodné simulationSpeed z prefabu
    }

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    private void Awake()
    {
        // Rotácia kontajnera musí byť nulová – deti sa umiestňujú cez
        // localPosition v svetových osiach.
        transform.rotation = Quaternion.identity;

        if (isoCamera == null)
            isoCamera = FindFirstObjectByType<IsometricCamera>();

        if (isoCamera != null)
            cam = isoCamera.CameraComponent;

        if (cam == null) cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();

        camT = (cam != null) ? cam.transform : null;
    }

    private void OnEnable()
    {
        if (isoCamera != null)
            isoCamera.OnZoomLevelChanged += HandleZoomLevelChanged;
    }

    private void OnDisable()
    {
        if (isoCamera != null)
            isoCamera.OnZoomLevelChanged -= HandleZoomLevelChanged;

        RestoreFogState();
    }

    private void Start()
    {
        if (cam == null)
        {
            Debug.LogWarning("[CloudLayerController] Nenašla sa kamera – vrstva mrakov je vypnutá.");
            enabled = false;
            return;
        }

        if (cloudPrefabs == null || cloudPrefabs.Length == 0)
        {
            Debug.LogWarning("[CloudLayerController] Nie sú priradené žiadne cloudPrefabs – vrstva mrakov je vypnutá.");
            enabled = false;
            return;
        }

        ClampCloudHeightUnderCamera();
        FixCameraNearPlaneIfNeeded();
        CaptureFogState();

        BuildEmitterGrid();

        // Počiatočná synchronizácia – ak IsometricCamera.Start() už prebehol,
        // udalosť sme mohli zmeškať, preto stav načítame priamo.
        int idx = (isoCamera != null) ? isoCamera.CurrentZoomIndex : -1;
        bool visible = (idx == visibleAtZoomIndex);

        targetAlpha = visible ? maxAlpha : 0f;
        currentAlpha = targetAlpha;      // bez fade pri štarte hry
        SetFieldActive(visible);
        alphaDirty = true;
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        UpdateAlpha();

        if (fieldActive)
        {
            RebuildIfViewChanged();
            FollowCamera();
        }

        if (motionDirty)
        {
            ApplyMotionSettings();
            motionDirty = false;
        }

        if (alphaDirty)
        {
            ApplyAlphaToMaterials();
            alphaDirty = false;
        }

        if (driveRenderSettingsFog)
            ApplyGlobalFog();
    }

    // =====================================================================
    // REAKCIA NA ZMENU ZOOMU
    // =====================================================================

    /// <summary>
    /// Callback z <see cref="IsometricCamera.OnZoomLevelChanged"/>. Jediné
    /// miesto, kde sa rozhoduje o viditeľnosti vrstvy.
    /// </summary>
    private void HandleZoomLevelChanged(int zoomIndex)
    {
        bool shouldBeVisible = (zoomIndex == visibleAtZoomIndex);
        targetAlpha = shouldBeVisible ? maxAlpha : 0f;

        // Pri nábehu treba emitory najprv aktivovať a "predohriať", aby sa
        // vrstva neobjavovala postupne od nuly častíc.
        if (shouldBeVisible && !fieldActive)
        {
            SetFieldActive(true);
            PrewarmEmitters();
        }
    }

    /// <summary>
    /// Plynulý prechod aktuálnej nepriehľadnosti k cieľovej. Pri dosiahnutí
    /// nuly sa emitory deaktivujú, aby vrstva nestála žiadny výkon.
    /// </summary>
    private void UpdateAlpha()
    {
        if (Mathf.Approximately(currentAlpha, targetAlpha))
            return;

        float duration = (targetAlpha > currentAlpha) ? fadeInDuration : fadeOutDuration;
        float step = (duration > 0.001f)
            ? (Time.unscaledDeltaTime / duration) * maxAlpha
            : maxAlpha;

        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, step);
        alphaDirty = true;

        if (currentAlpha <= 0.001f && fieldActive)
        {
            currentAlpha = 0f;
            SetFieldActive(false);
        }
    }

    // =====================================================================
    // MRIEŽKA EMITOROV
    // =====================================================================

    /// <summary>
    /// Vypočíta potrebné pokrytie záberu a vytvorí mriežku emitorov ako deti
    /// tohto objektu.
    ///
    /// GEOMETRIA (ortografická kamera naklonená o ~30°):
    ///  - šírka záberu na vodorovnej rovine  = 2 * orthoSize * aspect
    ///  - hĺbka záberu na vodorovnej rovine  = 2 * orthoSize / |forward.y|
    ///    (naklonením sa záber po zemi "natiahne" – pri 30° približne 2×)
    /// </summary>
    private void BuildEmitterGrid()
    {
        ClearEmitters();

        float orthoSize = (isoCamera != null)
            ? isoCamera.GetZoomSizeForIndex(visibleAtZoomIndex)
            : cam.orthographicSize;

        float aspect = cam.aspect;

        Vector3 fwd = camT.forward;
        float sinPitch = Mathf.Max(0.05f, Mathf.Abs(fwd.y));

        float halfWidth = orthoSize * aspect * coveragePadding;
        float halfDepth = (orthoSize / sinPitch) * coveragePadding;

        // Poistka proti prehnanému počtu emitorov – radšej zväčšíme rozostup.
        float spacing = Mathf.Max(1f, emitterSpacing);
        for (int guard = 0; guard < 12; guard++)
        {
            int c = Mathf.CeilToInt(halfWidth / spacing);
            int r = Mathf.CeilToInt(halfDepth / spacing);
            if ((2 * c + 1) * (2 * r + 1) <= maxEmitters) break;
            spacing *= 1.25f;
        }

        effectiveSpacing = spacing;

        int cols = Mathf.CeilToInt(halfWidth / spacing);
        int rows = Mathf.CeilToInt(halfDepth / spacing);

        GetScreenAlignedBasis(out Vector3 screenRight, out Vector3 screenUp);

        // Deterministická náhoda – rovnaký vzhľad vrstvy pri každom spustení.
        Random.State prevState = Random.state;
        Random.InitState(randomSeed);

        int prefabCursor = 0;

        for (int i = -cols; i <= cols; i++)
        {
            for (int j = -rows; j <= rows; j++)
            {
                Vector3 offset = screenRight * (i * spacing) + screenUp * (j * spacing);

                offset += screenRight * Random.Range(-positionJitter, positionJitter)
                        + screenUp * Random.Range(-positionJitter, positionJitter);

                ParticleSystem prefab = cloudPrefabs[prefabCursor % cloudPrefabs.Length];
                prefabCursor++;
                if (prefab == null) continue;

                float scale = Random.Range(scaleRange.x, scaleRange.y);
                CreateEmitter(prefab, offset, scale);
            }
        }

        Random.state = prevState;

        builtForScreenW = Screen.width;
        builtForScreenH = Screen.height;

        ApplyMotionSettings();
        motionDirty = false;

        SetFieldActive(fieldActive);
        if (fieldActive) PrewarmEmitters();
        alphaDirty = true;
    }

    /// <summary>
    /// Vytvorí jeden emitor, prepne jeho častice do svetového priestoru
    /// (kvôli parallaxe), nastaví vietor a pripraví dáta pre tónovanie.
    /// </summary>
    private void CreateEmitter(ParticleSystem prefab, Vector3 localOffset, float scale)
    {
        ParticleSystem ps = Instantiate(prefab, transform);
        ps.transform.localPosition = localOffset;
        ps.transform.localRotation = prefab.transform.localRotation;
        ps.transform.localScale = prefab.transform.localScale * scale;

        var entry = new EmitterEntry { go = ps.gameObject };

        entry.systems = ps.GetComponentsInChildren<ParticleSystem>(true);
        entry.baseStartColors = new Color[entry.systems.Length];
        entry.baseSimSpeeds = new float[entry.systems.Length];

        for (int i = 0; i < entry.systems.Length; i++)
        {
            ParticleSystem sub = entry.systems[i];
            var main = sub.main;

            // KĽÚČOVÉ: World simulation space znamená, že častice ostávajú na
            // mieste v svete, aj keď sa emitor posunie za kamerou. Bez toho by
            // celá vrstva "lepila" na obrazovku a nebolo by vidieť parallaxu.
            if (forceWorldSimulationSpace)
                main.simulationSpace = ParticleSystemSimulationSpace.World;

            entry.baseStartColors[i] = (main.startColor.mode == ParticleSystemGradientMode.Color)
                ? main.startColor.color
                : Color.white;

            // Collidery/triggery vypíname – Physics.Raycast v GameManageri
            // vyberá dlaždice pod myšou a mraky mu do toho nesmú zasahovať.
            var col = sub.collision; col.enabled = false;
            var trg = sub.trigger; trg.enabled = false;

            // Pôvodná rýchlosť z prefabu je referenčná hodnota – násobič sa
            // vždy počíta z nej, takže opakované ladenie za behu hodnoty
            // nekumuluje (inak by sa vrstva s každou zmenou spomaľovala).
            entry.baseSimSpeeds[i] = main.simulationSpeed;
        }

        // Samotná rýchlosť a vietor sa nastavujú centrálne v
        // ApplyMotionSettings(), aby sa dali ladiť aj počas Play mode.

        entry.renderers = ps.GetComponentsInChildren<ParticleSystemRenderer>(true);
        entry.blocks = new MaterialPropertyBlock[entry.renderers.Length];
        entry.baseColors = new List<KeyValuePair<int, Color>>[entry.renderers.Length];

        for (int i = 0; i < entry.renderers.Length; i++)
        {
            entry.blocks[i] = new MaterialPropertyBlock();
            entry.baseColors[i] = new List<KeyValuePair<int, Color>>();

            Material mat = entry.renderers[i].sharedMaterial;
            if (mat == null) continue;

            foreach (string name in ColorPropertyNames)
            {
                if (!mat.HasProperty(name)) continue;
                int id = Shader.PropertyToID(name);
                entry.baseColors[i].Add(new KeyValuePair<int, Color>(id, mat.GetColor(id)));
            }
        }

        emitters.Add(entry);
    }

    private void ClearEmitters()
    {
        for (int i = 0; i < emitters.Count; i++)
        {
            if (emitters[i].go != null)
                Destroy(emitters[i].go);
        }
        emitters.Clear();
    }

    /// <summary>
    /// Ak sa zmenilo rozlíšenie okna (a teda pomer strán), mriežka už nemusí
    /// pokrývať celý záber – prestaviame ju.
    /// </summary>
    private void RebuildIfViewChanged()
    {
        if (Screen.width == builtForScreenW && Screen.height == builtForScreenH)
            return;

        BuildEmitterGrid();
    }

    // =====================================================================
    // SLEDOVANIE KAMERY
    // =====================================================================

    /// <summary>
    /// Presunie kontajner emitorov nad stred záberu.
    ///
    /// SNAPOVANIE: pozícia sa zaokrúhľuje na násobky rozostupu mriežky. Vďaka
    /// tomu sa emitory "preskladajú" po celých bunkách a keďže častice žijú vo
    /// svetovom priestore, hráč žiadny skok nevidí – mraky ostávajú nad tým
    /// istým miestom mapy.
    /// </summary>
    private void FollowCamera()
    {
        Vector3 center = GetViewCenterOnCloudPlane();

        GetScreenAlignedBasis(out Vector3 screenRight, out Vector3 screenUp);

        float spacing = Mathf.Max(1f, effectiveSpacing);

        float a = Vector3.Dot(center, screenRight);
        float b = Vector3.Dot(center, screenUp);

        a = Mathf.Round(a / spacing) * spacing;
        b = Mathf.Round(b / spacing) * spacing;

        Vector3 snapped = screenRight * a + screenUp * b;
        snapped.y = cloudHeight;

        transform.position = snapped;
    }

    /// <summary>
    /// Bod, kde stred obrazu pretína vodorovnú rovinu vo výške cloudHeight.
    /// Práve nad ním musí byť stred vrstvy mrakov (nie nad stredom terénu –
    /// pri naklonenej kamere sú to dva rôzne body).
    /// </summary>
    private Vector3 GetViewCenterOnCloudPlane()
    {
        Vector3 fwd = camT.forward;
        if (Mathf.Abs(fwd.y) < 1e-4f) fwd.y = -1f;

        float t = (cloudHeight - camT.position.y) / fwd.y;
        return camT.position + fwd * t;
    }

    /// <summary>
    /// Vodorovné osi zarovnané s obrazovkou – rovnaká logika ako pohyb kamery
    /// v <see cref="IsometricCamera"/>: right kamery a forward kamery, oboje
    /// sploštené do roviny XZ.
    /// </summary>
    private void GetScreenAlignedBasis(out Vector3 screenRight, out Vector3 screenUp)
    {
        screenRight = camT.right;
        screenRight.y = 0f;
        screenRight.Normalize();

        screenUp = camT.forward;
        screenUp.y = 0f;
        screenUp.Normalize();
    }

    // =====================================================================
    // AKTIVÁCIA / TÓNOVANIE
    // =====================================================================

    private void SetFieldActive(bool active)
    {
        fieldActive = active;

        for (int i = 0; i < emitters.Count; i++)
        {
            if (emitters[i].go != null && emitters[i].go.activeSelf != active)
                emitters[i].go.SetActive(active);
        }

        if (active)
            FollowCamera(); // hneď na správnom mieste, ešte pred prvým vykreslením
    }

    /// <summary>
    /// Nastaví rýchlosť simulácie a vietor pre celú vrstvu naraz.
    ///
    /// Volá sa po prestavbe mriežky a vždy, keď sa v Inspectore zmení
    /// niektorý parameter pohybu – vďaka tomu sa dá rýchlosť mrakov doladiť
    /// priamo počas hry a výsledok je vidieť okamžite.
    /// </summary>
    private void ApplyMotionSettings()
    {
        float speed = Mathf.Max(0.01f, cloudSimulationSpeed);

        for (int e = 0; e < emitters.Count; e++)
        {
            EmitterEntry entry = emitters[e];
            if (entry.systems == null) continue;

            for (int i = 0; i < entry.systems.Length; i++)
            {
                ParticleSystem ps = entry.systems[i];
                if (ps == null) continue;

                var main = ps.main;

                // Násobok pôvodnej hodnoty z prefabu – spomalí sa tým naraz
                // úplne celý pohyb, ktorý má prefab v sebe zabudovaný
                // (Start Speed, Velocity/Force over Lifetime, rotácia,
                // Texture Sheet Animation, ...).
                main.simulationSpeed = entry.baseSimSpeeds[i] * speed;

                var vel = ps.velocityOverLifetime;
                if (applyWind)
                {
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.World;

                    // Delíme rýchlosťou simulácie, aby vietor ostal v
                    // SVETOVÝCH jednotkách za reálnu sekundu. Bez toho by
                    // spomalenie simulácie spomalilo aj vietor a obe hodnoty
                    // by sa dali ladiť len spolu.
                    vel.x = new ParticleSystem.MinMaxCurve(windVelocity.x / speed);
                    vel.z = new ParticleSystem.MinMaxCurve(windVelocity.y / speed);
                }
                else
                {
                    vel.enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// "Predohreje" simuláciu, aby vrstva pri zapnutí bola už plná častíc.
    /// Bez toho by hráč po prepnutí zoomu videl, ako sa mraky postupne rodia.
    /// </summary>
    private void PrewarmEmitters()
    {
        if (prewarmSeconds <= 0f) return;

        for (int i = 0; i < emitters.Count; i++)
        {
            var systems = emitters[i].systems;
            if (systems == null || systems.Length == 0) continue;

            // GetComponentsInChildren vracia komponent na samotnom objekte ako
            // prvý → systems[0] je koreňový systém. Simulate/Play s
            // withChildren = true obslúži aj všetky podsystémy naraz; volať to
            // pre každý podsystém zvlášť by simuláciu zdvojilo.
            ParticleSystem root = systems[0];
            if (root == null) continue;

            root.Simulate(prewarmSeconds, true, true);
            root.Play(true);
        }
    }

    /// <summary>
    /// Nastaví nepriehľadnosť celej vrstvy cez MaterialPropertyBlock, takže sa
    /// nevytvárajú inštancie materiálov a nemodifikujú sa assety na disku.
    ///
    /// Ak shader nemá žiadnu známu farebnú vlastnosť, použije sa náhradné
    /// riešenie cez main.startColor (ovplyvní len novo vzniknuté častice –
    /// prechod je o niečo menej presný, ale funkčný).
    /// </summary>
    private void ApplyAlphaToMaterials()
    {
        float a = Mathf.Clamp01(currentAlpha);

        for (int e = 0; e < emitters.Count; e++)
        {
            EmitterEntry entry = emitters[e];
            if (entry.renderers == null) continue;

            bool tintedAnything = false;

            for (int r = 0; r < entry.renderers.Length; r++)
            {
                ParticleSystemRenderer rend = entry.renderers[r];
                if (rend == null) continue;

                var colors = entry.baseColors[r];
                if (colors == null || colors.Count == 0) continue;

                MaterialPropertyBlock mpb = entry.blocks[r];
                rend.GetPropertyBlock(mpb);

                for (int c = 0; c < colors.Count; c++)
                {
                    Color baseCol = colors[c].Value;
                    baseCol.a *= a;
                    mpb.SetColor(colors[c].Key, baseCol);
                }

                rend.SetPropertyBlock(mpb);
                tintedAnything = true;
            }

            if (tintedAnything || entry.systems == null) continue;

            // Fallback – shader bez farebnej vlastnosti.
            for (int s = 0; s < entry.systems.Length; s++)
            {
                ParticleSystem ps = entry.systems[s];
                if (ps == null) continue;

                var main = ps.main;
                if (main.startColor.mode != ParticleSystemGradientMode.Color) continue;

                Color col = entry.baseStartColors[s];
                col.a *= a;
                main.startColor = col;
            }
        }
    }

    // =====================================================================
    // GLOBÁLNA HMLA
    // =====================================================================

    private void CaptureFogState()
    {
        if (fogStateCaptured) return;

        origFogEnabled = RenderSettings.fog;
        origFogColor = RenderSettings.fogColor;
        origFogDensity = RenderSettings.fogDensity;
        origFogMode = RenderSettings.fogMode;
        fogStateCaptured = true;
    }

    private void RestoreFogState()
    {
        if (!fogStateCaptured || !driveRenderSettingsFog) return;

        RenderSettings.fog = origFogEnabled;
        RenderSettings.fogColor = origFogColor;
        RenderSettings.fogDensity = origFogDensity;
        RenderSettings.fogMode = origFogMode;
    }

    /// <summary>
    /// Hustota globálnej hmly kopíruje nepriehľadnosť časticovej vrstvy, takže
    /// oba efekty nabiehajú a miznú synchrónne.
    /// </summary>
    private void ApplyGlobalFog()
    {
        if (currentAlpha <= 0.001f)
        {
            if (RenderSettings.fog && !origFogEnabled)
                RenderSettings.fog = false;
            return;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity * currentAlpha;
    }

    // =====================================================================
    // GEOMETRICKÉ POISTKY
    // =====================================================================

    /// <summary>
    /// Vrstva mrakov musí byť pod kamerou – inak by bola za jej near clip
    /// rovinou a vôbec by sa nevykreslila.
    /// </summary>
    private void ClampCloudHeightUnderCamera()
    {
        if (camT == null) return;

        float maxHeight = camT.position.y - minCameraClearance;
        if (cloudHeight > maxHeight)
        {
            Debug.LogWarning($"[CloudLayerController] cloudHeight ({cloudHeight}) je príliš " +
                             $"vysoko voči kamere (Y={camT.position.y}). Znížené na {maxHeight}.");
            cloudHeight = maxHeight;
        }
    }

    /// <summary>
    /// Overí, že vrstva mrakov leží medzi near a far clip rovinou kamery.
    ///
    /// Unity klampuje nearClipPlane na minimálne 0.01 aj pri nastavení zo
    /// skriptu, preto sa nedá spoľahnúť na záporné hodnoty. Ak by vrstva
    /// nevyšla, treba zdvihnúť kameru alebo znížiť cloudHeight – skript na to
    /// upozorní v konzole namiesto tichého zlyhania.
    /// </summary>
    private void FixCameraNearPlaneIfNeeded()
    {
        if (!autoFixCameraNearPlane || cam == null) return;

        Vector3 center = GetViewCenterOnCloudPlane();
        float distanceAlongForward = Vector3.Dot(center - camT.position, camT.forward);

        // Rezerva na veľké častice, ktoré presahujú nad/pod rovinu vrstvy.
        const float margin = 10f;

        if (distanceAlongForward - margin < cam.nearClipPlane)
        {
            float desired = Mathf.Max(0.01f, distanceAlongForward - margin);

            if (desired < 0.02f)
            {
                Debug.LogWarning("[CloudLayerController] Vrstva mrakov je príliš blízko " +
                                 "kamery a mohla by byť orezaná near clip rovinou. " +
                                 "Zníž cloudHeight alebo zdvihni kameru vyššie.");
            }

            cam.nearClipPlane = desired;
        }

        if (distanceAlongForward + margin > cam.farClipPlane)
            cam.farClipPlane = distanceAlongForward + margin * 10f;
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Dočasné potlačenie vrstvy (napr. pri screenshote alebo v tutoriále).
    /// Nezasahuje do zoom logiky – po opätovnom povolení sa stav dorovná.
    /// </summary>
    public void SetSuppressed(bool suppressed)
    {
        if (suppressed)
        {
            targetAlpha = 0f;
        }
        else if (isoCamera != null)
        {
            HandleZoomLevelChanged(isoCamera.CurrentZoomIndex);
        }
    }

    /// <summary>Kompletné prestavenie mriežky – po zmene parametrov za behu.</summary>
    public void Rebuild()
    {
        if (!Application.isPlaying) return;
        BuildEmitterGrid();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Zmena parametrov v Inspectore počas Play mode sa prejaví okamžite –
    /// bez toho by sa rýchlosť mrakov dala ladiť len opakovaným reštartom.
    /// </summary>
    private void OnValidate()
    {
        motionDirty = true;
        alphaDirty = true;
    }

    private void OnDrawGizmosSelected()
    {
        Camera c = cam;
        if (c == null) c = Camera.main;
        if (c == null) return;

        float size = c.orthographicSize;
        float sinPitch = Mathf.Max(0.05f, Mathf.Abs(c.transform.forward.y));

        Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.6f);
        Gizmos.matrix = Matrix4x4.TRS(
            new Vector3(transform.position.x, cloudHeight, transform.position.z),
            Quaternion.Euler(0f, c.transform.eulerAngles.y, 0f),
            Vector3.one);

        Gizmos.DrawWireCube(
            Vector3.zero,
            new Vector3(2f * size * c.aspect * coveragePadding,
                        0.1f,
                        2f * (size / sinPitch) * coveragePadding));
    }
#endif
}
