using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// NightModeManager
/// ─────────────────────────────────────────────────────────────────────────
/// DEŇ / NOC s plynulým prechodom. Prepína sa tlačidlom 'NightToggleUIButton'
/// v GameMenuUI (volá <see cref="Toggle"/>), prípadne z kódu cez SetNight().
///
/// V noci:
///   • smerové svetlá (slnko) sa stlmia na slabé modré „mesačné“ svetlo,
///     pri tme 100 % sa úplne vypnú,
///   • ambientné svetlo, odrazy (reflections), hmla, pozadie kamery a
///     skybox sa stmavia podľa nastavenia Darkness,
///   • globálna hodnota shaderov _CityNightAmount ide 0 → 1, podľa nej sa
///     rozsvietia okná budov (RetroCity/BuildingNightWindows).
///
/// DEŇ sa pri návrate obnoví PRESNE na pôvodné hodnoty (odfotia sa v momente,
/// keď začne stmievanie). Počas dňa komponent do scény nič nezapisuje.
///
/// Použitie: pridaj na ľubovoľný GameObject v scéne. Všetko ostatné je
/// voliteľné ladenie v Inspectore.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[DisallowMultipleComponent]
public class NightModeManager : MonoBehaviour
{
    public static NightModeManager instance;

    [Header("Ovládanie")]
    [Tooltip("Hra začne v noci.")]
    [SerializeField] private bool startAtNight = false;

    [Header("Tma")]
    [Tooltip("Ako tmavá je noc. 0 = ako cez deň, 1 = úplná tma (svietia len okná).")]
    [Range(0f, 1f)][SerializeField] private float darkness = 0.85f;
    [Tooltip("Dĺžka prechodu deň ↔ noc v sekundách (reálny čas, beží aj pri pauze).")]
    [Range(0.01f, 10f)][SerializeField] private float transitionSeconds = 1.5f;

    [Header("Svetlá")]
    [Tooltip("Stlmiť všetky smerové svetlá (slnko) v scéne.")]
    [SerializeField] private bool controlDirectionalLights = true;
    [Tooltip("Ďalšie svetlá, ktoré sa majú v noci stlmiť / vypnúť (bodové, spot...).")]
    [SerializeField] private List<Light> additionalLights = new List<Light>();
    [Tooltip("Farba svetla v noci (mesiac).")]
    [SerializeField] private Color moonLightColor = new Color(0.55f, 0.65f, 1.0f, 1f);
    [Tooltip("Sila mesačného svetla voči dennému slnku (násobí sa ešte (1 - Darkness)). " +
             "0 = svetlá sa v noci úplne vypnú.")]
    [Range(0f, 1f)][SerializeField] private float moonLightStrength = 0.35f;

    [Header("Okolie")]
    [Tooltip("Farebný nádych ambientného svetla v noci.")]
    [SerializeField] private Color nightAmbientTint = new Color(0.55f, 0.65f, 1.0f, 1f);
    [SerializeField] private bool darkenReflections = true;
    [SerializeField] private bool darkenFog = true;
    [SerializeField] private bool darkenCameraBackground = true;
    [SerializeField] private bool darkenSkybox = true;

    /// <summary>Volá sa pri prepnutí (true = noc).</summary>
    public event System.Action<bool> NightModeChanged;

    /// <summary>Cieľový stav (true = noc, aj keď prechod ešte beží).</summary>
    public bool IsNight => targetNight;

    /// <summary>Aktuálna „nočnosť“ 0..1 (vyhladená).</summary>
    public float NightAmount => Smooth(progress);

    public float Darkness
    {
        get => darkness;
        set => darkness = Mathf.Clamp01(value);
    }

    static readonly int ID_NightAmount = Shader.PropertyToID("_CityNightAmount");
    static readonly int ID_Exposure = Shader.PropertyToID("_Exposure");

    private bool targetNight;
    private float progress;        // lineárne 0..1
    private bool snapshotValid;    // denný stav je odfotený a scéna je „naša“

    // ── Denný snapshot ──────────────────────────────────────────────────
    private struct LightState
    {
        public Light light;
        public float intensity;
        public Color color;
        public bool enabled;
    }

    private readonly List<LightState> dayLights = new List<LightState>();
    private AmbientMode dayAmbientMode;
    private Color dayAmbientLight, dayAmbientSky, dayAmbientEquator, dayAmbientGround;
    private SphericalHarmonicsL2 dayAmbientProbe;
    private float dayReflectionIntensity;
    private Color dayFogColor;
    private Camera dayCamera;
    private Color dayCameraBackground;
    private bool dayCameraSolid;
    private Material daySkybox;
    private Material nightSkybox;  // runtime kópia (originálny asset sa nemení)
    private float daySkyboxExposure;

    // =====================================================================
    // ŽIVOTNÝ CYKLUS
    // =====================================================================

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[NightModeManager] V scéne je viac inštancií – ponechávam prvú.");
            enabled = false;
            return;
        }
        instance = this;
        Shader.SetGlobalFloat(ID_NightAmount, 0f);
    }

    private void Start()
    {
        if (startAtNight) SetNight(true, true);
    }

    private void OnDisable()
    {
        if (snapshotValid) RestoreDay();
        progress = 0f;
        targetNight = false;
        Shader.SetGlobalFloat(ID_NightAmount, 0f);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float target = targetNight ? 1f : 0f;
        if (!Mathf.Approximately(progress, target))
        {
            if (!snapshotValid) CaptureDay();
            progress = Mathf.MoveTowards(progress, target,
                                         Time.unscaledDeltaTime / Mathf.Max(0.01f, transitionSeconds));
        }

        if (!snapshotValid) return;

        if (!targetNight && progress <= 0f)
        {
            RestoreDay();
            Shader.SetGlobalFloat(ID_NightAmount, 0f);
            return;
        }

        // V noci aplikujeme každý frame – zmena Darkness v Inspectore sa prejaví hneď.
        ApplyBlend(Smooth(progress));
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    public void Toggle() => SetNight(!targetNight, false);

    public void SetNight(bool night, bool instant)
    {
        if (targetNight != night)
        {
            targetNight = night;
            NightModeChanged?.Invoke(night);
        }

        if (instant)
        {
            if (night && !snapshotValid) CaptureDay();
            progress = night ? 1f : 0f;
        }
    }

    // =====================================================================
    // DEŇ – SNAPSHOT / OBNOVA
    // =====================================================================

    private void CaptureDay()
    {
        dayLights.Clear();

        if (controlDirectionalLights)
        {
            var all = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].type == LightType.Directional)
                    AddLight(all[i]);
        }
        for (int i = 0; i < additionalLights.Count; i++)
            if (additionalLights[i] != null) AddLight(additionalLights[i]);

        dayAmbientMode = RenderSettings.ambientMode;
        dayAmbientLight = RenderSettings.ambientLight;
        dayAmbientSky = RenderSettings.ambientSkyColor;
        dayAmbientEquator = RenderSettings.ambientEquatorColor;
        dayAmbientGround = RenderSettings.ambientGroundColor;
        dayAmbientProbe = RenderSettings.ambientProbe;
        dayReflectionIntensity = RenderSettings.reflectionIntensity;
        dayFogColor = RenderSettings.fogColor;

        dayCamera = Camera.main;
        if (dayCamera != null)
        {
            dayCameraSolid = dayCamera.clearFlags == CameraClearFlags.SolidColor;
            dayCameraBackground = dayCamera.backgroundColor;
        }

        daySkybox = RenderSettings.skybox;
        if (nightSkybox != null) Destroy(nightSkybox);
        nightSkybox = null;
        if (darkenSkybox && daySkybox != null && daySkybox.HasProperty(ID_Exposure))
        {
            daySkyboxExposure = daySkybox.GetFloat(ID_Exposure);
            nightSkybox = new Material(daySkybox) { name = daySkybox.name + " (Night)" };
        }

        snapshotValid = true;
    }

    private void AddLight(Light l)
    {
        for (int i = 0; i < dayLights.Count; i++)
            if (dayLights[i].light == l) return;

        dayLights.Add(new LightState
        {
            light = l,
            intensity = l.intensity,
            color = l.color,
            enabled = l.enabled
        });
    }

    private void RestoreDay()
    {
        for (int i = 0; i < dayLights.Count; i++)
        {
            var s = dayLights[i];
            if (s.light == null) continue;
            s.light.intensity = s.intensity;
            s.light.color = s.color;
            s.light.enabled = s.enabled;
        }

        RenderSettings.ambientMode = dayAmbientMode;
        switch (dayAmbientMode)
        {
            case AmbientMode.Flat:
                RenderSettings.ambientLight = dayAmbientLight;
                break;
            case AmbientMode.Trilight:
                RenderSettings.ambientSkyColor = dayAmbientSky;
                RenderSettings.ambientEquatorColor = dayAmbientEquator;
                RenderSettings.ambientGroundColor = dayAmbientGround;
                break;
            default:
                RenderSettings.ambientProbe = dayAmbientProbe;
                break;
        }

        RenderSettings.reflectionIntensity = dayReflectionIntensity;
        RenderSettings.fogColor = dayFogColor;

        if (dayCamera != null && dayCameraSolid)
            dayCamera.backgroundColor = dayCameraBackground;

        if (nightSkybox != null)
        {
            if (RenderSettings.skybox == nightSkybox) RenderSettings.skybox = daySkybox;
            Destroy(nightSkybox);
            nightSkybox = null;
        }

        dayLights.Clear();
        snapshotValid = false;
    }

    // =====================================================================
    // NOC – PRELÍNANIE
    // =====================================================================

    private void ApplyBlend(float b)
    {
        float k = 1f - darkness;  // celkový jas noci

        // Svetlá → mesiac
        for (int i = 0; i < dayLights.Count; i++)
        {
            var s = dayLights[i];
            if (s.light == null) continue;

            float nightI = s.intensity * k * moonLightStrength;
            float cur = Mathf.Lerp(s.intensity, nightI, b);
            s.light.intensity = cur;
            s.light.color = Color.Lerp(s.color, moonLightColor, b);
            s.light.enabled = s.enabled && cur > 0.0005f;
        }

        // Ambient
        Color tintK = new Color(nightAmbientTint.r * k, nightAmbientTint.g * k, nightAmbientTint.b * k, 1f);
        switch (dayAmbientMode)
        {
            case AmbientMode.Flat:
                RenderSettings.ambientLight = Darken(dayAmbientLight, tintK, b);
                break;
            case AmbientMode.Trilight:
                RenderSettings.ambientSkyColor = Darken(dayAmbientSky, tintK, b);
                RenderSettings.ambientEquatorColor = Darken(dayAmbientEquator, tintK, b);
                RenderSettings.ambientGroundColor = Darken(dayAmbientGround, tintK, b);
                break;
            default:
                // Skybox / iné: priamo škálujeme sférické harmoniky (per kanál = nádych).
                SphericalHarmonicsL2 sh = dayAmbientProbe;
                for (int c = 0; c < 3; c++)
                {
                    float f = Mathf.Lerp(1f, tintK[c], b);
                    for (int j = 0; j < 9; j++) sh[c, j] = dayAmbientProbe[c, j] * f;
                }
                RenderSettings.ambientProbe = sh;
                break;
        }

        if (darkenReflections)
            RenderSettings.reflectionIntensity = Mathf.Lerp(dayReflectionIntensity, dayReflectionIntensity * k, b);

        if (darkenFog)
            RenderSettings.fogColor = Darken(dayFogColor, tintK, b);

        if (darkenCameraBackground && dayCamera != null && dayCameraSolid)
            dayCamera.backgroundColor = Darken(dayCameraBackground, tintK, b);

        if (nightSkybox != null)
        {
            nightSkybox.SetFloat(ID_Exposure, Mathf.Lerp(daySkyboxExposure, daySkyboxExposure * k, b));
            if (RenderSettings.skybox != nightSkybox) RenderSettings.skybox = nightSkybox;
        }

        Shader.SetGlobalFloat(ID_NightAmount, b);
    }

    private static Color Darken(Color day, Color tintK, float b)
    {
        Color night = new Color(day.r * tintK.r, day.g * tintK.g, day.b * tintK.b, day.a);
        return Color.Lerp(day, night, b);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
