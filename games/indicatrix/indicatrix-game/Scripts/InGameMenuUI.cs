using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// InGameMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// Analógia k FactoryConstructionMenuUI – obsluhuje kliky tlačidiel v
/// CONTENT sekcii okna "GameMenuUIPanel - Window" (in-game / pauzové menu).
///
/// POZN. K NÁZVU: trieda sa volá zámerne InGameMenuUI (NIE GameMenuUI),
/// aby nekolidovala s už existujúcou triedou GameMenuUI, ktorá ovláda
/// spodnú lištu "GameMenuPanel". Tento komponent patrí výhradne oknu
/// "GameMenuUIPanel - Window".
///
/// Na rozdiel od construction menu (RAIL/ROAD/FACTORY) toto okno NESPÚŠŤA
/// žiadny konštrukčný / terrain režim – je to čisto ovládacie okno. Preto sa
/// tu NEVOLÁ GameManager.SetTerrainMode(...).
///
/// Okno obsahuje 3 obsahové tlačidlá (v "GameMenuUI - Content"):
///   - GMBackToMainMenuButton  → návrat do hlavného menu (scéna "MainMenu")
///   - GMExitGameButton        → ukončenie hry (Application.Quit)
///   - GMSaveGameButton        → uloženie hry (IndicatrixAPI.SaveGame)
///
/// POZN. K LOAD: Načítanie hry sa z tohto okna ZÁMERNE NEROBÍ. Hra sa vždy
/// načítava z HLAVNÉHO MENU (MainMenuController.OnLoadGame), ktoré prepne do
/// hernej scény a obnoví uloženú hru. In-game menu preto Load tlačidlo nemá.
///
/// Close (X) tlačidlo "GMCloseWindowButton" v titulku NIE JE súčasťou tohto
/// skriptu – rieši ho InGameMenuUIwindow (rovnako ako pri Factory okne má
/// Close na starosti FactoryConstructionUIwindow, nie FactoryConstructionMenuUI).
///
/// POZN.: Drag-and-drop pohyb okna rieši samostatný WindowDragHandle pripojený
/// na "GameMenuUI - Title" (drag-uje root panel "GameMenuUIPanel - Window").
///
/// V Inspectore stačí priradiť konkrétne Button komponenty z CONTENT sekcie
/// na nižšie uvedené SerializeField polia.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class InGameMenuUI : MonoBehaviour
{
    [Header("In-Game Menu – Content tlačidlá")]
    [Tooltip("GMBackToMainMenuButton – návrat do hlavného menu (scéna MainMenu).")]
    [SerializeField] private Button inGameMenuBackToMainMenuButton;

    [Tooltip("GMExitGameButton – ukončenie hry (Application.Quit).")]
    [SerializeField] private Button inGameMenuExitGameButton;

    [Tooltip("GMSaveGameButton – uloženie aktuálnej hry (IndicatrixAPI.SaveGame).")]
    [SerializeField] private Button inGameMenuSaveGameButton;

    [Tooltip("GMSettingsGameButton – nastavenie aktuálnej hry.")]
    [SerializeField] private Button inGameMenuSettingsGameButton;

    [Tooltip("Referencia na SerttingsMenuUI okno. Ak ostane prázdna, " +
         "nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private SettingsMenuUI settingsMenuUI;

    // -------------------------------------------------------------------------
    // Lazy referencia na univerzálne okno StatusErrorMenuUI – rovnaký vzor ako
    // v GameManager. Okno je pri štarte skryté (SetActive(false) v jeho Start()),
    // preto sa hľadá s FindObjectsInactive.Include. Slúži pre OZNÁMENIE o
    // úspešnom uložení (nie je to chyba, len to isté generické okno s iným textom).
    // -------------------------------------------------------------------------
    private StatusErrorMenuUI _statusErrorMenuUI;
    private StatusErrorMenuUI StatusErrorMenuUI
    {
        get
        {
            if (_statusErrorMenuUI == null)
                _statusErrorMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusErrorMenuUI>(FindObjectsInactive.Include);
            return _statusErrorMenuUI;
        }
    }


    void Start()
    {
        if (inGameMenuBackToMainMenuButton != null)
            inGameMenuBackToMainMenuButton.onClick.AddListener(InGameMenuBackToMainMenuButtonClick);

        if (inGameMenuExitGameButton != null)
            inGameMenuExitGameButton.onClick.AddListener(InGameMenuExitGameButtonClick);

        if (inGameMenuSaveGameButton != null)
            inGameMenuSaveGameButton.onClick.AddListener(InGameMenuSaveGameButtonClick);

        if (inGameMenuSettingsGameButton != null)
            inGameMenuSettingsGameButton.onClick.AddListener(InGameMenuSettingsGameButtonClick);

        // -----------------------------------------------------------------
        // SETTINGS MENU TLAČIDLO
        // -----------------------------------------------------------------

        // Fallback – ak referencia na okno nie je priradená v Inspectore,
        // nájdeme StatusTrainsMenuUI v scéne (aj keď je panel neaktívny).
        if (settingsMenuUI == null)
            settingsMenuUI = Object.FindFirstObjectByType<SettingsMenuUI>(FindObjectsInactive.Include);
    }

    // ── Content tlačidlá ──

    private void InGameMenuSettingsGameButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (settingsMenuUI == null) return;

        settingsMenuUI.ToggleWindow();

    }

    private void InGameMenuBackToMainMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        SceneManager.LoadScene("MainMenu");
    }

    private void InGameMenuExitGameButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        // Ukončenie hry. V buildi to vypne aplikáciu; v Editore by
        // Application.Quit() nič neurobil, preto tam zastavíme Play mód.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void InGameMenuSaveGameButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();
        
        // Uloženie aktuálnej hry do "Assets/SaveGame/savegame.dat".
        // Súbor sa VŽDY prepíše (overwrite) – nikdy sa nevytvára druhý súbor
        // a hra sa nepýta na potvrdenie prepísania.
        if (IndicatrixAPI.instance != null)
        {
            IndicatrixAPI.instance.SaveGame();

            // Po úspešnom uložení zobraz hráčovi OZNÁMENIE v univerzálnom okne
            // StatusErrorMenuUI (rovnaká metodika ako chyby v GameManager –
            // len s oznamovacím textom z GameErrors).
            NotifyGameSaved();
        }
        else
        {
            Debug.LogWarning("[InGameMenuUI] IndicatrixAPI.instance je null – " +
                             "hru nemožno uložiť.");
        }
    }

    /// <summary>
    /// Otvorí univerzálne okno StatusErrorMenuUI s oznámením o úspešnom uložení.
    /// Nie je to chyba – okno je generické, použité tu na spätnú väzbu hráčovi.
    /// Rovnaký spôsob vyvolania ako GameManager.ReportError(...).
    /// </summary>
    private void NotifyGameSaved()
    {
        var menu = StatusErrorMenuUI;
        if (menu != null)
        {
            menu.OpenWithError(GameErrors.GameSavedSuccessfully);
        }
        else
        {
            // Okno nie je v scéne – aspoň nech oznámenie neutíchne ticho.
            Debug.LogWarning("[InGameMenuUI] StatusErrorMenuUI nie je v scéne. " +
                             $"Oznámenie: \"{GameErrors.GameSavedSuccessfully}\"");
        }
    }
}