using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SettingsMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionMenuUI / RoadConstructionMenuUI – obsluhuje
/// ovládacie prvky panelu SettingsMenuUIPanel.
///
/// Na rozdiel od ROAD/RAIL/FACTORY menu tu nejde o žiadny konštrukčný mód –
/// SettingsMenu neprepína GameManager do žiadneho stavu. Obsahuje iba dva
/// slidery (Content sekcia v Hierarchy):
///
///   - MusicSlider         → hlasitosť hudby
///   - SoundEffectsSlider  → hlasitosť zvukových efektov
///
/// MusicText a SoundEffectsText sú čisto popisné labely bez potreby
/// Inspector referencie (nemenia sa za behu), preto tu nie sú.
///
/// Close (X) tlačidlo (SCloseWindowButton) NIE JE súčasťou tohto skriptu –
/// jeho obsluha je v SettingsUIwindow (analogicky k FactoryConstructionUIwindow),
/// rovnako ako je to oddelené aj pri Factory/Road/Rail oknách.
///
/// HUDBA – DVE ALTERNATÍVY (TracklistA / TracklistB):
///   Hráč si v Main Menu vyberá, či v hre pobeží jeden klip v loope
///   (TracklistA) alebo 10 skladieb náhodne (TracklistB). MusicSlider tu
///   NEPOTREBUJE o tejto voľbe nič vedieť: obe alternatívy prehráva
///   GameManager cez ten istý AudioSource, takže SetMusicVolume() /
///   MusicVolume platia rovnako pre prvú aj druhú alternatívu.
///
/// POZN.: Predpokladá sa existencia GameManager.instance so signatúrou
/// SetMusicVolume(float) / SetSfxMasterVolume(float) a hodnotami
/// MusicVolume / SfxMasterVolume pre inicializáciu sliderov pri Start().
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class SettingsMenuUI : MonoBehaviour
{
    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (SettingsMenuUIPanel - Window). " +
         "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject settingsMenuUIPanel;

    [Header("Content – Audio Slidery")]
    [Tooltip("Slider hlasitosti hudby (SettingsMenuUI - Content › MusicSlider). " +
             "Namapovaný na GameManager.SetMusicVolume() / GameManager.MusicVolume. " +
             "Funguje rovnako pre obe alternatívy hudby (TracklistA aj TracklistB).")]
    [SerializeField] private Slider musicSlider;

    [Tooltip("Slider hlasitosti zvukových efektov (SettingsMenuUI - Content › SoundEffectsSlider). " +
             "Namapovaný na GameManager.SetSfxMasterVolume() / GameManager.SfxMasterVolume.")]
    [SerializeField] private Slider soundEffectsSlider;

    // Listenery sa smú zaregistrovať len raz (inak by sa pri každom otvorení
    // okna pridali znova a hlasitosť by sa nastavovala viackrát za sebou).
    private bool listenersRegistered;


    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusTrainsUIwindow (close button listener) už zbehol.
        // Okno sa otvorí až po kliku na StatusTrainsUIButton cez ToggleWindow().
        if (settingsMenuUIPanel != null)
            settingsMenuUIPanel.SetActive(false);

        InitAudioSliders();
    }

    // =====================================================================
    // AUDIO SLIDERY – namapovanie na GameManager
    // =====================================================================

    /// <summary>
    /// Nastaví slidery na aktuálnu hlasitosť z GameManager (musicVolume /
    /// sfxMasterVolume) a zaregistruje listenery, ktoré pri zmene hodnoty
    /// rovno volajú GameManager.SetMusicVolume() / SetSfxMasterVolume().
    ///
    /// Ak GameManager ešte nie je pripravený (napr. iné poradie Start()),
    /// registrácia sa ticho preskočí a zopakuje sa pri otvorení okna.
    /// </summary>
    private void InitAudioSliders()
    {
        if (GameManager.instance == null) return;

        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(GameManager.instance.MusicVolume);

            if (!listenersRegistered)
                musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }

        if (soundEffectsSlider != null)
        {
            soundEffectsSlider.SetValueWithoutNotify(GameManager.instance.SfxMasterVolume);

            if (!listenersRegistered)
                soundEffectsSlider.onValueChanged.AddListener(OnSoundEffectsSliderChanged);
        }

        listenersRegistered = true;
    }

    /// <summary>
    /// Zosynchronizuje polohu sliderov s aktuálnymi hodnotami v GameManager.
    /// Volá sa pri každom otvorení okna, aby slider vždy ukazoval reálny stav
    /// (a aby sa napojenie dokončilo aj vtedy, ak GameManager v Start() ešte
    /// nebol pripravený).
    /// </summary>
    private void RefreshAudioSliders()
    {
        InitAudioSliders();
    }

    /// <summary>
    /// Volané pri posune MusicSlider – nastaví hlasitosť hudby v GameManager.
    /// Platí pre obe alternatívy hudby: GameManager prehráva TracklistA aj
    /// TracklistB cez ten istý AudioSource, takže sa tu nič nevetví.
    /// </summary>
    private void OnMusicSliderChanged(float value)
    {
        GameManager.instance?.SetMusicVolume(value);
    }

    /// <summary>Volané pri posune SoundEffectsSlider – nastaví spoločnú hlasitosť SFX v GameManager.</summary>
    private void OnSoundEffectsSliderChanged(float value)
    {
        GameManager.instance?.SetSfxMasterVolume(value);
    }

    // =====================================================================
    // PUBLIC API – volá InGameMenuUI z toggle tlačidla "SettingsUIwindow"
    // =====================================================================

    /// <summary>
    /// Prepne viditeľnosť okna (otvorené ↔ zatvorené). Volá GameMenuUI pri
    /// kliknutí na toggle tlačidlo "StatusTrainsUIButton".
    ///
    /// Pri otvorení sa slidery zosynchronizujú s aktuálnymi hodnotami
    /// hlasitosti – okno je tak vždy v súlade so stavom hry.
    /// </summary>
    public void ToggleWindow()
    {
        if (settingsMenuUIPanel == null) return;

        bool newState = !settingsMenuUIPanel.activeSelf;
        settingsMenuUIPanel.SetActive(newState);

        if (newState) RefreshAudioSliders();
    }

    /// <summary>
    /// Zobrazí okno. Pohodlné explicitné API, ak by niektorá časť kódu
    /// chcela okno otvoriť priamo (bez toggle).
    /// </summary>
    public void OpenWindow()
    {
        if (settingsMenuUIPanel == null) return;

        settingsMenuUIPanel.SetActive(true);
        RefreshAudioSliders();
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný SettingsUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku.
    /// </summary>
    public void CloseWindow()
    {
        if (settingsMenuUIPanel != null)
            settingsMenuUIPanel.SetActive(false);
    }
}
