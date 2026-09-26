using System;
using UnityEngine;

/// <summary>
/// Ktorá alternatíva hudby na pozadí sa má v hre prehrávať.
///
///   TracklistA = pôvodné správanie – JEDEN mp3 klip ("Music Clip" v GameManager),
///                ktorý sa prehráva v nekonečnej slučke (loop).
///   TracklistB = pole mp3 klipov ("Shuffle Music Clips" v GameManager, napr.
///                10 skladieb), ktoré sa prehrávajú v náhodnom poradí (shuffle).
///
/// Hodnoty sú explicitne očíslované, lebo sa ukladajú do PlayerPrefs ako int –
/// poradie prvkov sa preto nesmie meniť.
/// </summary>
public enum MusicTracklist
{
    TracklistA = 0,
    TracklistB = 1
}

/// <summary>
/// MusicSettings
/// ─────────────────────────────────────────────────────────────────────────
/// Jediné miesto, kde je uložená voľba hudby na pozadí. Prepínač je LEN
/// v Main Menu (SettingsPanel → ToggleGroup → TracklistA / TracklistB),
/// samotnú hudbu podľa tejto voľby prehráva GameManager v hernej scéne.
///
/// PREČO STATICKÁ TRIEDA + PlayerPrefs:
///   • MainMenu a IndicatrixScene sú dve samostatné scény – obyčajná
///     referencia medzi nimi neprežije SceneManager.LoadScene().
///   • PlayerPrefs navyše prežije aj reštart hry, takže hráč si voľbu
///     nastaví raz a platí aj po ďalšom spustení.
///   • Netreba žiadny DontDestroyOnLoad objekt ani nič v Hierarchy.
///
/// POUŽITIE:
///   čítanie:  MusicTracklist t = MusicSettings.SelectedTracklist;
///   zápis:    MusicSettings.SelectedTracklist = MusicTracklist.TracklistB;
///
/// Súbor stačí umiestniť kdekoľvek v Assets (napr. Assets/Scripts) – je
/// dostupný z oboch scén.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public static class MusicSettings
{
    // Kľúč v PlayerPrefs. Ak ho zmeníš, hráčom sa voľba resetuje na default.
    private const string PrefKey = "Indicatrix.MusicTracklist";

    // Predvolená alternatíva pri úplne prvom spustení hry = pôvodná hudba.
    private const MusicTracklist DefaultTracklist = MusicTracklist.TracklistA;

    // Cache, aby sa PlayerPrefs nečítali pri každom prístupe.
    private static MusicTracklist? cached;

    /// <summary>
    /// Vyvolá sa pri každej zmene voľby (napr. keď hráč klikne na TracklistB
    /// v Main Menu). Voliteľné – hodí sa, ak by si chcel na zmenu reagovať
    /// naživo aj v inej scéne.
    /// </summary>
    public static event Action<MusicTracklist> TracklistChanged;

    /// <summary>Aktuálne zvolená alternatíva hudby na pozadí.</summary>
    public static MusicTracklist SelectedTracklist
    {
        get
        {
            if (cached == null)
                cached = (MusicTracklist)PlayerPrefs.GetInt(PrefKey, (int)DefaultTracklist);

            return cached.Value;
        }
        set
        {
            if (cached != null && cached.Value == value) return; // nič sa nemení

            cached = value;
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();

            TracklistChanged?.Invoke(value);
        }
    }

    /// <summary>Skratka: je zvolená druhá alternatíva (náhodné prehrávanie)?</summary>
    public static bool IsShuffleSelected => SelectedTracklist == MusicTracklist.TracklistB;

    /// <summary>Vráti voľbu na predvolenú hodnotu (TracklistA).</summary>
    public static void ResetToDefault()
    {
        SelectedTracklist = DefaultTracklist;
    }
}
