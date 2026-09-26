using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MusicTracklistToggleUI  (scéna MainMenu)
/// ─────────────────────────────────────────────────────────────────────────
/// Obsluhuje prepínač hudby v SettingsPanel → ToggleGroup:
///
///   • TracklistA = v hre hrá PÔVODNÁ hudba – jeden mp3 klip v loope.
///   • TracklistB = v hre hrá 10 mp3 skladieb v náhodnom poradí (shuffle).
///
/// Skript NIČ neprehráva. Iba uloží voľbu do <see cref="MusicSettings"/>,
/// odkiaľ si ju GameManager prečíta pri štarte hernej scény a podľa nej
/// spustí príslušnú hudbu. Hudba v Main Menu (MenuAudioManager) ostáva
/// bez zmeny – tá s týmto prepínačom nemá nič spoločné.
///
/// NASTAVENIE V UNITY (jednorazovo):
///   1. Skript pridaj na objekt "SettingsPanel" (alebo na "ToggleGroup",
///      funguje to rovnako – dôležité sú len referencie nižšie).
///   2. Do polí "Tracklist A Toggle" / "Tracklist B Toggle" natiahni objekty
///      TracklistA a TracklistB (musia mať komponent Toggle).
///   3. Na objekte "ToggleGroup" musí byť komponent Toggle Group a mať
///      VYPNUTÉ "Allow Switch Off" (aby bola vždy zvolená práve jedna možnosť).
///   4. Na oboch Toggle komponentoch musí byť v poli "Group" nastavený
///      ten istý ToggleGroup.
///
/// Poznámka: OnClick zvuk sa cez MenuAudioManager automaticky napája len na
/// komponenty Button, nie na Toggle – preto si klik zvuk pri prepnutí volá
/// tento skript sám (dá sa vypnúť voľbou "Play Click Sound").
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class MusicTracklistToggleUI : MonoBehaviour
{
    [Header("Toggles (SettingsPanel › ToggleGroup)")]
    [Tooltip("Toggle 'TracklistA' = pôvodná hudba, jeden mp3 klip v nekonečnej slučke.")]
    [SerializeField] private Toggle tracklistAToggle;

    [Tooltip("Toggle 'TracklistB' = 10 mp3 skladieb prehrávaných náhodne (shuffle).")]
    [SerializeField] private Toggle tracklistBToggle;

    [Header("Správanie")]
    [Tooltip("Zapnuté = pri prepnutí sa prehrá klik zvuk z MenuAudioManager (Toggle sa naň sám nenapája).")]
    [SerializeField] private bool playClickSound = true;

    [Tooltip("Zapnuté = do konzoly sa vypíše, ktorá alternatíva je práve zvolená (ladenie).")]
    [SerializeField] private bool logSelection = true;

    // Kým sa toggly nastavujú podľa uloženej hodnoty, nesmieme reagovať
    // na ich onValueChanged (inak by sme hneď zapisovali späť do nastavení).
    private bool isApplyingSavedValue;

    private void Awake()
    {
        if (tracklistAToggle != null)
            tracklistAToggle.onValueChanged.AddListener(OnTracklistAChanged);

        if (tracklistBToggle != null)
            tracklistBToggle.onValueChanged.AddListener(OnTracklistBChanged);
    }

    private void OnEnable()
    {
        // Panel sa práve otvoril → zobraz aktuálne uložený stav.
        ApplySavedValueToToggles();
    }

    private void OnDestroy()
    {
        if (tracklistAToggle != null)
            tracklistAToggle.onValueChanged.RemoveListener(OnTracklistAChanged);

        if (tracklistBToggle != null)
            tracklistBToggle.onValueChanged.RemoveListener(OnTracklistBChanged);
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// Nastaví polohu prepínača podľa hodnoty uloženej v MusicSettings,
    /// bez vyvolania onValueChanged (SetIsOnWithoutNotify).
    /// </summary>
    private void ApplySavedValueToToggles()
    {
        bool useShuffle = MusicSettings.IsShuffleSelected;

        isApplyingSavedValue = true;

        if (tracklistAToggle != null) tracklistAToggle.SetIsOnWithoutNotify(!useShuffle);
        if (tracklistBToggle != null) tracklistBToggle.SetIsOnWithoutNotify(useShuffle);

        isApplyingSavedValue = false;
    }

    private void OnTracklistAChanged(bool isOn)
    {
        if (isApplyingSavedValue || !isOn) return;   // reagujeme len na zapnutie
        Select(MusicTracklist.TracklistA);
    }

    private void OnTracklistBChanged(bool isOn)
    {
        if (isApplyingSavedValue || !isOn) return;   // reagujeme len na zapnutie
        Select(MusicTracklist.TracklistB);
    }

    /// <summary>Uloží voľbu. Verejné, aby sa dala napojiť aj priamo z Inspectora.</summary>
    public void Select(MusicTracklist tracklist)
    {
        MusicSettings.SelectedTracklist = tracklist;

        if (playClickSound) MenuAudioManager.Instance?.PlayClick();

        if (logSelection)
        {
            Debug.Log(tracklist == MusicTracklist.TracklistB
                ? "[MainMenu] Hudba v hre: TracklistB – 10 skladieb náhodne (shuffle)."
                : "[MainMenu] Hudba v hre: TracklistA – jeden klip v nekonečnej slučke.");
        }
    }

    /// <summary>Pomocné metódy pre napojenie priamo z Inspectora (Toggle → On Value Changed).</summary>
    public void SelectTracklistA() => Select(MusicTracklist.TracklistA);

    /// <inheritdoc cref="SelectTracklistA"/>
    public void SelectTracklistB() => Select(MusicTracklist.TracklistB);
}
