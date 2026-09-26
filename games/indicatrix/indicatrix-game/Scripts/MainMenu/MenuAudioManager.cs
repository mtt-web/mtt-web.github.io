using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Jednoduchý zvukový manažér pre Main Menu:
///   • HUDBA sa prehrá hneď od začiatku v nekonečnom loope.
///   • KLIK zvuk sa prehrá pri kliknutí na KAŽDÝ button.
///   • HLASITOSŤ hudby aj efektov riadi Inspector (slider je len v Editore, ale
///     jeho hodnota naplno platí v hre – dá sa meniť aj naživo počas Play mode).
///   • Ak nie je zadaný žiadny zvuk, nič sa nestane – žiadna chyba, len ticho.
///
/// ZDROJ ZVUKU – dve možnosti (pre každý zvuk zvlášť):
///   A) DRAG & DROP: pretiahni AudioClip (mp3/wav/ogg) priamo do poľa v Inspectore.
///      → Najjednoduchšie, funguje všade. Súbor je súčasťou projektu/buildu.
///   B) EXTERNÝ SÚBOR: nechaj AudioClip prázdny a zadaj cestu k mp3 do "...File Path".
///      → Súbor sa načíta z disku za behu (dá sa vymeniť aj po builde).
///      Absolútna cesta (C:/Audio/hudba.mp3) alebo relatívna k StreamingAssets.
///   Ak je vyplnené oboje, prednosť má pretiahnutý AudioClip.
///
/// POUŽITIE:
///   1. Vytvor prázdny GameObject (napr. "MenuAudio"), najlepšie pod Canvas Main Menu,
///      a pridaj naň tento skript.
///   2. Nastav hudbu a klik zvuk cez A) alebo B) a hlasitosti slidermi.
///   3. Ak GameObject nie je pod Canvasom s buttonmi, priraď "Buttons Root" = Canvas.
///
/// Poznámka: v scéne musí byť AudioListener (štandardne je na Main Camera), inak nepočuť nič.
/// </summary>
public class MenuAudioManager : MonoBehaviour
{
    /// <summary>Voliteľný prístup z iných skriptov: MenuAudioManager.Instance?.PlayClick();</summary>
    public static MenuAudioManager Instance { get; private set; }

    [Header("Hudba")]
    [Tooltip("Pretiahni sem AudioClip (mp3/wav/ogg). Ak necháš prázdne, použije sa 'Music File Path'.")]
    [SerializeField] private AudioClip musicClip;
    [Tooltip("Voliteľná externá cesta k mp3 – použije sa len ak nie je pretiahnutý AudioClip. Prázdne = bez hudby.")]
    [SerializeField] private string musicFilePath = "";
    [Range(0f, 1f)]
    [Tooltip("Hlasitosť hudby – nastavuje sa v Inspectore, ale platí v hre (aj naživo).")]
    [SerializeField] private float musicVolume = 1f;
    [SerializeField] private bool loopMusic = true;

    [Header("Klik efekt")]
    [Tooltip("Pretiahni sem AudioClip (mp3/wav/ogg). Ak necháš prázdne, použije sa 'Click Sfx File Path'.")]
    [SerializeField] private AudioClip clickClip;
    [Tooltip("Voliteľná externá cesta k mp3 – použije sa len ak nie je pretiahnutý AudioClip. Prázdne = bez efektu.")]
    [SerializeField] private string clickSfxFilePath = "";
    [Range(0f, 1f)]
    [Tooltip("Hlasitosť efektov – nastavuje sa v Inspectore, ale platí v hre (aj naživo).")]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Buttony")]
    [Tooltip("Koreň, pod ktorým sa nájdu všetky Buttony (aj vypnuté v skrytých paneloch). Prázdne = tento objekt a jeho deti.")]
    [SerializeField] private Transform buttonsRoot;
    [Tooltip("Ak je zapnuté, klik zvuk sa automaticky pridá na každý Button – netreba ručné napájanie.")]
    [SerializeField] private bool autoHookButtons = true;

    [Header("Slidery (SettingsPanel)")]
    [Tooltip("Voliteľné. 'MusicSlider' zo SettingsPanel – natiahni ho sem, aby sa jeho poloha pri štarte nastavila podľa Music Volume vyššie. " +
             "Na samotnom slideri v Inspectore ešte treba pridať OnValueChanged (Float) -> MenuAudioManager.SetMusicVolume.")]
    [SerializeField] private Slider musicSlider;
    [Tooltip("Voliteľné. 'SoundEffectsSlider' zo SettingsPanel – natiahni ho sem, aby sa jeho poloha pri štarte nastavila podľa Sfx Volume vyššie. " +
             "Na samotnom slideri v Inspectore ešte treba pridať OnValueChanged (Float) -> MenuAudioManager.SetSfxVolume.")]
    [SerializeField] private Slider soundEffectsSlider;

    private AudioSource musicSource;
    private AudioSource sfxSource;

    private void Awake()
    {
        Instance = this;

        // AudioSource si vyrobíme za behu – v Inspectore netreba nič ručne pridávať.
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = loopMusic;
        musicSource.volume = musicVolume;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.volume = sfxVolume;

        if (autoHookButtons) HookButtons();
    }

    private void Start()
    {
        // Slidery v SettingsPanel nastav pri štarte na aktuálnu hlasitosť (bez vyvolania
        // ich OnValueChanged), aby ich poloha sedela s Music Volume / Sfx Volume vyššie.
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(musicVolume);
        if (soundEffectsSlider != null) soundEffectsSlider.SetValueWithoutNotify(sfxVolume);

        // HUDBA: ak je pretiahnutý AudioClip, hraj hneď; inak skús načítať z cesty.
        if (musicClip != null)
        {
            OnMusicLoaded(musicClip);
        }
        else
        {
            StartCoroutine(LoadClipRoutine(musicFilePath, OnMusicLoaded));
        }

        // KLIK: ak AudioClip nie je pretiahnutý, skús ho načítať z cesty.
        if (clickClip == null)
        {
            StartCoroutine(LoadClipRoutine(clickSfxFilePath, clip => clickClip = clip));
        }
    }

    private void Update()
    {
        // Hodnota slidera z Inspectora platí v hre – priebežne aj počas Play mode.
        if (musicSource != null) musicSource.volume = musicVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }

    /// <summary>Prehrá klik zvuk (ak je k dispozícii). Volá sa automaticky pri kliknutí na button.</summary>
    public void PlayClick()
    {
        if (clickClip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clickClip, sfxVolume);
    }

    /// <summary>
    /// Nastaví hlasitosť hudby (0-1). Napoj na slider 'MusicSlider' v SettingsPanel
    /// cez Inspector -> Slider -> On Value Changed (Float) -> MenuAudioManager.SetMusicVolume.
    /// </summary>
    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        if (musicSource != null) musicSource.volume = musicVolume;
    }

    /// <summary>
    /// Nastaví hlasitosť zvukových efektov (0-1). Napoj na slider 'SoundEffectsSlider'
    /// v SettingsPanel cez Inspector -> Slider -> On Value Changed (Float) -> MenuAudioManager.SetSfxVolume.
    /// </summary>
    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }

    private void OnMusicLoaded(AudioClip clip)
    {
        if (clip == null || musicSource == null) return;
        musicSource.clip = clip;
        musicSource.loop = loopMusic;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    private void HookButtons()
    {
        Transform root = buttonsRoot != null ? buttonsRoot : transform;
        // 'true' → nájde aj vypnuté buttony (napr. v skrytých paneloch Load / Credits).
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button b in buttons)
        {
            if (b != null) b.onClick.AddListener(PlayClick);
        }
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Absolútna cesta sa použije priamo, relatívna sa hľadá v StreamingAssets.
        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(Application.streamingAssetsPath, path);
    }

    private AudioType GetAudioType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".mp3": return AudioType.MPEG;
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            case ".aif":
            case ".aiff": return AudioType.AIFF;
            default: return AudioType.MPEG;
        }
    }

    private IEnumerator LoadClipRoutine(string path, Action<AudioClip> onDone)
    {
        string full = ResolvePath(path);

        // Prázdna cesta alebo chýbajúci súbor = ticho, žiadna chyba.
        if (string.IsNullOrEmpty(full) || !File.Exists(full))
        {
            if (!string.IsNullOrWhiteSpace(path))
                Debug.Log("[MenuAudio] Súbor sa nenašiel, zvuk preskočený: " + path);
            onDone?.Invoke(null);
            yield break;
        }

        string url = new Uri(full).AbsoluteUri; // korektná file:// URL aj s medzerami
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, GetAudioType(full)))
        {
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                Debug.Log("[MenuAudio] Zvuk sa nepodarilo načítať (" + path + "): " + req.error);
                onDone?.Invoke(null);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            onDone?.Invoke(clip);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}