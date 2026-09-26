using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class GameMenuUI : MonoBehaviour
{
    [Header("Rail Construction Menu")]
    [SerializeField] private Button railConstructionMenuButton;
    [SerializeField] private GameObject railConstructionMenuUIPanel;

    [Header("Road Construction Menu")]
    [SerializeField] private Button roadConstructionMenuButton;
    [SerializeField] private GameObject roadConstructionMenuUIPanel;

    [Header("Factory Construction Menu")]
    [SerializeField] private Button factoryConstructionMenuButton;
    [SerializeField] private GameObject factoryConstructionMenuUIPanel;

    [Header("In-Game Menu (Pause / System Menu)")]
    [Tooltip("Toggle tlačidlo 'GameMenuUIButton' – otvára/zatvára " +
             "okno InGameMenu ('GameMenuUIPanel - Window') s tlačidlami " +
             "BackToMainMenu / ExitGame / LoadGame / SaveGame.")]
    [SerializeField] private Button gameMenuButton;

    [Tooltip("Root okna InGameMenu ('GameMenuUIPanel - Window'), ktoré sa " +
             "toggluje. Pri štarte sa skryje.")]
    [SerializeField] private GameObject gameMenuUIPanel;

    [Header("Staff Management Menu")]
    [Tooltip("Toggle tlačidlo 'StaffManagementUIButton' – otvára/zatvára " +
             "okno StaffManagementMenuUI (nábor a správa personálu).")]
    [SerializeField] private Button staffManagementMenuButton;

    [Tooltip("Root okna StaffManagementMenuUI ('StaffManagementMenuUIPanel - " +
             "Window'), ktoré sa toggluje. Pri štarte sa skryje.")]
    [SerializeField] private GameObject staffManagementMenuUIPanel;

    [Header("Status Stations Menu")]
    [Tooltip("Toggle tlačidlo 'StatusStationsUIButton' – otvára/zatvára " +
             "informačné okno StatusStationsMenu so zoznamom dep.")]
    [SerializeField] private Button statusStationsMenuButton;

    [Tooltip("Referencia na StatusStationsMenuUI okno. Ak ostane prázdna, " +
             "nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private StatusStationsMenuUI statusStationsMenuUI;

    [Header("Status Trains Menu")]
    [Tooltip("Toggle tlačidlo 'StatusTrainsUIButton' – otvára/zatvára " +
             "informačné okno StatusTrainsMenu so zoznamom všetkých vlakov.")]
    [SerializeField] private Button statusTrainsMenuButton;

    [Tooltip("Referencia na StatusTrainsMenuUI okno. Ak ostane prázdna, " +
             "nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private StatusTrainsMenuUI statusTrainsMenuUI;

    [Header("Status Vehicles Menu")]
    [Tooltip("Toggle tlačidlo 'StatusVehiclesUIButton' – otvára/zatvára " +
             "informačné okno StatusVehiclesMenu so zoznamom všetkých vozidiel.")]
    [SerializeField] private Button statusVehiclesMenuButton;

    [Tooltip("Referencia na StatusVehiclesMenuUI okno. Ak ostane prázdna, " +
             "nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private StatusVehiclesMenuUI statusVehiclesMenuUI;

    [Header("Status Map Menu")]
    [Tooltip("Toggle tlačidlo 'StatusMapUIButton' – otvára/zatvára " +
             "informačné okno StatusMapMenu s prehľadovou mapou.")]
    [SerializeField] private Button statusMapMenuButton;

    [Tooltip("Referencia na StatusMapMenuUI okno. Ak ostane prázdna, " +
             "nájde sa automaticky (aj keď je panel neaktívny).")]
    [SerializeField] private StatusMapMenuUI statusMapMenuUI;

    [Header("Camera Zoom")]
    [Tooltip("Tlačidlo '+' – priblíženie kamery o jednu úroveň.")]
    [SerializeField] private Button cameraZoomPlusButton;

    [Tooltip("Tlačidlo '-' – oddialenie kamery o jednu úroveň.")]
    [SerializeField] private Button cameraZoomMinusButton;

    [Tooltip("Referencia na izometrickú kameru. Ak ostane prázdna, nájde sa automaticky.")]
    [SerializeField] private IsometricCamera isometricCamera;

    [Header("Night Mode")]
    [Tooltip("Tlačidlo 'NightToggleUIButton' – prepína deň / noc (NightModeManager).")]
    [SerializeField] private Button nightToggleButton;

    [Tooltip("Referencia na NightModeManager. Ak ostane prázdna, nájde sa automaticky.")]
    [SerializeField] private NightModeManager nightModeManager;

    [Tooltip("Child 'Icon Image' na tlačidle NightToggleUIButton – tomuto Image sa " +
             "mení sprite podľa stavu deň/noc.")]
    [SerializeField] private Image nightToggleIconImage;

    [Tooltip("Ikonka v stave NORMAL (deň – nočný režim vypnutý).")]
    [SerializeField] private Sprite nightToggleIconNormal;

    [Tooltip("Ikonka v stave PRESSED (noc – nočný režim zapnutý).")]
    [SerializeField] private Sprite nightToggleIconPressed;

    // =====================================================================
    // GAME TIME / CASH CAPTIONS
    //
    // Dva čisto zobrazovacie (read-only) UI prvky v GameMenuPanel:
    //   - GameTimeTextCaption        → herný čas "January, 1950"
    //   - GameCashAccountTextCaption → stav konta "100.000 CR"
    //
    // GameMenuUI je tu len VIEW – samotný model (čas a peniaze) drží
    // GameClock, resp. GameEconomy (samostatné singletony). View počúva na
    // ich eventy a podľa nich prekresľuje text. Referencie na oba systémy sa
    // dajú nechať prázdne – nájdu sa automaticky v Start().
    //
    // POZNÁMKA k typu: captiony sú riešené ako TextMeshPro (TMP_Text). Ak by
    // ste v projekte používali staré UnityEngine.UI.Text, stačí zmeniť typ
    // týchto dvoch polí na 'Text' (logika nižšie ostáva rovnaká – obe majú
    // vlastnosť .text).
    // =====================================================================

    [Header("Game Time / Cash Captions")]
    [Tooltip("UI prvok 'GameTimeTextCaption' – zobrazuje herný čas (Month, Year).")]
    [SerializeField] private TMP_Text gameTimeTextCaption;

    [Tooltip("UI prvok 'GameCashAccountTextCaption' – zobrazuje stav konta (napr. 100.000 CR).")]
    [SerializeField] private TMP_Text gameCashAccountTextCaption;

    [Tooltip("Referencia na GameClock. Ak ostane prázdna, nájde sa automaticky.")]
    [SerializeField] private GameClock gameClock;

    [Tooltip("Referencia na GameEconomy. Ak ostane prázdna, nájde sa automaticky.")]
    [SerializeField] private GameEconomy gameEconomy;


    void Awake()
    {
        railConstructionMenuUIPanel.SetActive(false);
        roadConstructionMenuUIPanel.SetActive(false);
        factoryConstructionMenuUIPanel.SetActive(false);

        if (staffManagementMenuUIPanel != null)
            staffManagementMenuUIPanel.SetActive(false);

        if (gameMenuUIPanel != null)
            gameMenuUIPanel.SetActive(false);
    }


    void Start()
    {
        railConstructionMenuButton.onClick.AddListener(RailConstructionMenuButtonClick);

        if (roadConstructionMenuButton != null)
            roadConstructionMenuButton.onClick.AddListener(RoadConstructionMenuButtonClick);

        if (factoryConstructionMenuButton != null)
            factoryConstructionMenuButton.onClick.AddListener(FactoryConstructionMenuButtonClick);

        if (gameMenuButton != null)
            gameMenuButton.onClick.AddListener(GameMenuButtonClick);

        if (staffManagementMenuButton != null)
            staffManagementMenuButton.onClick.AddListener(StaffManagementMenuButtonClick);

        // -----------------------------------------------------------------
        // STATUS STATIONS MENU TLAČIDLO
        // -----------------------------------------------------------------

        // Fallback – ak referencia na okno nie je priradená v Inspectore,
        // nájdeme StatusStationsMenuUI v scéne (aj keď je panel neaktívny).
        if (statusStationsMenuUI == null)
            statusStationsMenuUI = Object.FindFirstObjectByType<StatusStationsMenuUI>(FindObjectsInactive.Include);

        if (statusStationsMenuButton != null)
            statusStationsMenuButton.onClick.AddListener(StatusStationsMenuButtonClick);

        // -----------------------------------------------------------------
        // STATUS TRAINS MENU TLAČIDLO
        // -----------------------------------------------------------------

        // Fallback – ak referencia na okno nie je priradená v Inspectore,
        // nájdeme StatusTrainsMenuUI v scéne (aj keď je panel neaktívny).
        if (statusTrainsMenuUI == null)
            statusTrainsMenuUI = Object.FindFirstObjectByType<StatusTrainsMenuUI>(FindObjectsInactive.Include);

        if (statusTrainsMenuButton != null)
            statusTrainsMenuButton.onClick.AddListener(StatusTrainsMenuButtonClick);

        // -----------------------------------------------------------------
        // STATUS VEHICLES MENU TLAČIDLO
        // -----------------------------------------------------------------

        // Fallback – ak referencia na okno nie je priradená v Inspectore,
        // nájdeme StatusVehiclesMenuUI v scéne (aj keď je panel neaktívny).
        if (statusVehiclesMenuUI == null)
            statusVehiclesMenuUI = Object.FindFirstObjectByType<StatusVehiclesMenuUI>(FindObjectsInactive.Include);

        if (statusVehiclesMenuButton != null)
            statusVehiclesMenuButton.onClick.AddListener(StatusVehiclesMenuButtonClick);

        // -----------------------------------------------------------------
        // STATUS MAP MENU TLAČIDLO
        // -----------------------------------------------------------------

        // Fallback – ak referencia na okno nie je priradená v Inspectore,
        // nájdeme StatusMapMenuUI v scéne (aj keď je panel neaktívny).
        if (statusMapMenuUI == null)
            statusMapMenuUI = Object.FindFirstObjectByType<StatusMapMenuUI>(FindObjectsInactive.Include);

        if (statusMapMenuButton != null)
            statusMapMenuButton.onClick.AddListener(StatusMapMenuButtonClick);

        // -----------------------------------------------------------------
        // CAMERA ZOOM TLAČIDLÁ
        // -----------------------------------------------------------------

        // Fallback – ak referencia nie je priradená v Inspectore, nájdeme
        // kameru v scéne.
        if (isometricCamera == null)
            isometricCamera = Object.FindFirstObjectByType<IsometricCamera>();

        if (cameraZoomPlusButton != null)
            cameraZoomPlusButton.onClick.AddListener(CameraZoomPlusButtonClick);

        if (cameraZoomMinusButton != null)
            cameraZoomMinusButton.onClick.AddListener(CameraZoomMinusButtonClick);

        // Nastavíme počiatočný enabled stav tlačidiel podľa štartovacej
        // zoom úrovne (default near → "+" disabled, "-" enabled).
        RefreshZoomButtons();

        // -----------------------------------------------------------------
        // NIGHT MODE TLAČIDLO (NightToggleUIButton)
        // -----------------------------------------------------------------

        // Fallback – ak referencia nie je priradená v Inspectore, nájdeme
        // NightModeManager v scéne.
        if (nightModeManager == null)
            nightModeManager = Object.FindFirstObjectByType<NightModeManager>();

        if (nightToggleButton != null)
            nightToggleButton.onClick.AddListener(NightToggleButtonClick);

        // Ikonka sa riadi STAVOM nočného režimu (nie myšou) – prihlásime sa na
        // zmenu a hneď nastavíme počiatočný stav (napr. ak hra začína v noci).
        if (nightModeManager != null)
        {
            nightModeManager.NightModeChanged += RefreshNightToggleIcon;
            RefreshNightToggleIcon(nightModeManager.IsNight);
        }
        else
        {
            RefreshNightToggleIcon(false);
        }

        // -----------------------------------------------------------------
        // GAME TIME / CASH – NAPOJENIE NA MODEL
        // -----------------------------------------------------------------

        // Fallback – ak referencie nie sú priradené v Inspectore, nájdeme
        // GameClock a GameEconomy v scéne.
        if (gameClock == null)
            gameClock = Object.FindFirstObjectByType<GameClock>();

        if (gameEconomy == null)
            gameEconomy = Object.FindFirstObjectByType<GameEconomy>();

        // Prihlásime sa na zmeny a HNEĎ prekreslíme aktuálny stav. Okamžité
        // prekreslenie pokrýva aj prípad, keď GameClock/GameEconomy stihli
        // vyvolať svoj štartový event ešte predtým, než sme sa prihlásili
        // (poradie Start() medzi skriptami nie je garantované).
        if (gameClock != null)
        {
            gameClock.OnDateChanged += RefreshGameTimeCaption;
            RefreshGameTimeCaption();
        }

        if (gameEconomy != null)
        {
            gameEconomy.OnBalanceChanged += RefreshCashCaption;
            RefreshCashCaption();
        }
    }

    void OnDestroy()
    {
        // Odhlásenie eventov – ochrana pred volaním na zničenom objekte.
        if (gameClock != null)
            gameClock.OnDateChanged -= RefreshGameTimeCaption;

        if (gameEconomy != null)
            gameEconomy.OnBalanceChanged -= RefreshCashCaption;

        if (nightModeManager != null)
            nightModeManager.NightModeChanged -= RefreshNightToggleIcon;
    }

    void RailConstructionMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        bool newState = !railConstructionMenuUIPanel.activeSelf;
        railConstructionMenuUIPanel.SetActive(newState);

        if (!newState)
        {
            // Pri zavretí RAIL menu zrušíme RAIL režim
            GameManager.instance.SetTerrainMode(GameManager.RailConstructionMode.None);
        }
        else
        {
            // Pri otvorení RAIL menu zrušíme prípadne aktívny ROAD aj FACTORY
            // režim, aby sa neprekrývali snap indikátory rôznych systémov.
            GameManager.instance.SetTerrainMode(GameManager.RoadConstructionMode.None);
            GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.None);
        }
    }

    void RoadConstructionMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (roadConstructionMenuUIPanel == null) return;

        bool newState = !roadConstructionMenuUIPanel.activeSelf;
        roadConstructionMenuUIPanel.SetActive(newState);

        if (!newState)
        {
            // Pri zavretí ROAD menu zrušíme ROAD režim
            GameManager.instance.SetTerrainMode(GameManager.RoadConstructionMode.None);
        }
        else
        {
            // Pri otvorení ROAD menu zrušíme prípadne aktívny RAIL aj FACTORY režim
            GameManager.instance.SetTerrainMode(GameManager.RailConstructionMode.None);
            GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.None);
        }
    }

    void FactoryConstructionMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (factoryConstructionMenuUIPanel == null) return;

        bool newState = !factoryConstructionMenuUIPanel.activeSelf;
        factoryConstructionMenuUIPanel.SetActive(newState);

        if (!newState)
        {
            // Pri zavretí FACTORY menu zrušíme FACTORY režim
            GameManager.instance.SetTerrainMode(GameManager.FactoryConstructionMode.None);
        }
        else
        {
            // Pri otvorení FACTORY menu zrušíme prípadne aktívny RAIL aj ROAD režim
            GameManager.instance.SetTerrainMode(GameManager.RailConstructionMode.None);
            GameManager.instance.SetTerrainMode(GameManager.RoadConstructionMode.None);
        }
    }

    // =====================================================================
    // STAFF MANAGEMENT MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // StaffManagementMenu je INFORMAČNÉ / MANAŽÉRSKE okno (nábor a správa
    // personálu). Nespúšťa žiadny konštrukčný režim, preto sa tu – na rozdiel
    // od construction menu tlačidiel vyššie – NEVOLÁ GameManager.SetTerrainMode().
    //
    // Vnútorné prepínanie panelov (HireStaff / StaffManagement / SM* panely)
    // rieši samostatný komponent StaffManagementMenuUI umiestnený na okne.
    // GameMenuUI tu len toggluje viditeľnosť celého okna; StaffManagementMenuUI
    // si pri každom otvorení (OnEnable) sám zresetuje stav na default.
    // =====================================================================

    void StaffManagementMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (staffManagementMenuUIPanel == null) return;

        bool newState = !staffManagementMenuUIPanel.activeSelf;
        staffManagementMenuUIPanel.SetActive(newState);
    }

    // =====================================================================
    // IN-GAME MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // InGameMenu ('GameMenuUIPanel - Window') je OVLÁDACIE / pauzové okno
    // (BackToMainMenu / ExitGame / LoadGame / SaveGame). Nespúšťa žiadny
    // konštrukčný režim, preto sa tu – na rozdiel od construction menu
    // tlačidiel vyššie – NEVOLÁ GameManager.SetTerrainMode(None). Inak by sa
    // otvorením pauzového menu nechcene zrušil rozrobený RAIL/ROAD/FACTORY
    // režim hráča.
    //
    // Obsluhu obsahových tlačidiel a Close (X) rieši samostatne InGameMenuUI,
    // resp. InGameMenuUIwindow umiestnené na okne. GameMenuUI tu len prepína
    // viditeľnosť celého okna.
    // =====================================================================

    void GameMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (gameMenuUIPanel == null) return;

        bool newState = !gameMenuUIPanel.activeSelf;
        gameMenuUIPanel.SetActive(newState);
    }

    // =====================================================================
    // STATUS STATIONS MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // StatusStationsMenu je čisto INFORMAČNÉ okno (zoznam vlakových a
    // vozidlových dep). Nespúšťa žiadny konštrukčný režim, preto sa tu –
    // na rozdiel od construction menu tlačidiel vyššie – nevolá
    // GameManager.SetTerrainMode(None).
    //
    // Samotné zobrazenie/skrytie panelu rieši StatusStationsMenuUI (drží
    // si referenciu na svoj root panel, rovnako ako StatusFactoryMenuUI).
    // GameMenuUI tu len prepne stav cez verejné API okna.
    // =====================================================================

    void StatusStationsMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (statusStationsMenuUI == null) return;

        statusStationsMenuUI.ToggleWindow();
    }

    // =====================================================================
    // STATUS TRAINS MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // StatusTrainsMenu je čisto INFORMAČNÉ okno (zoznam všetkých vlakov v
    // hre). Rovnako ako StatusStationsMenu nespúšťa žiadny konštrukčný
    // režim, preto sa tu nevolá GameManager.SetTerrainMode(None).
    //
    // Zobrazenie/skrytie panelu a naplnenie TrainsTextCaption rieši
    // StatusTrainsMenuUI – GameMenuUI tu len prepne stav cez verejné API
    // okna (ToggleWindow), ktoré si pri otvorení samo načíta aktuálny
    // zoznam vlakov z TrainSystem.
    // =====================================================================

    void StatusTrainsMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (statusTrainsMenuUI == null) return;

        statusTrainsMenuUI.ToggleWindow();
    }

    // =====================================================================
    // STATUS VEHICLES MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // StatusVehiclesMenu je čisto INFORMAČNÉ okno (zoznam všetkých vozidiel
    // v hre). Rovnako ako StatusStationsMenu nespúšťa žiadny konštrukčný
    // režim, preto sa tu nevolá GameManager.SetTerrainMode(None).
    //
    // Zobrazenie/skrytie panelu a naplnenie VehiclesTextCaption rieši
    // StatusVehiclesMenuUI – GameMenuUI tu len prepne stav cez verejné API
    // okna (ToggleWindow), ktoré si pri otvorení samo načíta aktuálny
    // zoznam vozidiel z VehicleSystem.
    // =====================================================================

    void StatusVehiclesMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (statusVehiclesMenuUI == null) return;

        statusVehiclesMenuUI.ToggleWindow();
    }

    // =====================================================================
    // STATUS MAP MENU – OBSLUHA TOGGLE TLAČIDLA
    //
    // StatusMapMenu je čisto INFORMAČNÉ okno (prehľadová mapa terénu,
    // železničnej siete, cestnej siete a priemyslu). Rovnako ako ostatné
    // Status okná nespúšťa žiadny konštrukčný režim, preto sa tu nevolá
    // GameManager.SetTerrainMode(None).
    //
    // Zobrazenie/skrytie panelu rieši StatusMapMenuUI – GameMenuUI tu len
    // prepne stav cez verejné API okna (ToggleWindow).
    // =====================================================================

    void StatusMapMenuButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (statusMapMenuUI == null) return;

        statusMapMenuUI.ToggleWindow();
    }

    // =====================================================================
    // CAMERA ZOOM – OBSLUHA TLAČIDIEL "+" A "-"
    //
    // Zoom má 3 pevné úrovne (near=4, mid=8, far=12 – pozri IsometricCamera).
    // Po každom kliku sa aktualizuje enabled stav oboch tlačidiel:
    //   - "+" je disabled na najbližšej úrovni (nedá sa priblížiť bližšie),
    //   - "-" je disabled na najvzdialenejšej úrovni.
    // =====================================================================

    void CameraZoomPlusButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (isometricCamera == null) return;

        isometricCamera.ZoomIn();
        RefreshZoomButtons();
    }

    void CameraZoomMinusButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (isometricCamera == null) return;

        isometricCamera.ZoomOut();
        RefreshZoomButtons();
    }

    // =====================================================================
    // NIGHT MODE – prepínanie deň / noc
    //
    // Nahrádza pôvodnú klávesu "N". Samotné prepnutie (stlmenie svetiel,
    // rozsvietenie okien, plynulý prechod) robí NightModeManager.
    // =====================================================================

    void NightToggleButtonClick()
    {
        GameManager.instance?.PlaySfxUIclick();

        if (nightModeManager == null)
            nightModeManager = NightModeManager.instance != null
                ? NightModeManager.instance
                : Object.FindFirstObjectByType<NightModeManager>();

        if (nightModeManager == null)
        {
            Debug.LogWarning("[GameMenuUI] NightModeManager sa v scéne nenašiel – " +
                             "prepnutie deň/noc nie je možné.");
            return;
        }

        nightModeManager.Toggle();

        // Ikonku prepína event NightModeChanged; toto je poistka pre prípad,
        // že by sa manager dohľadal až teraz (bez prihlásenia na event).
        RefreshNightToggleIcon(nightModeManager.IsNight);
    }

    /// <summary>
    /// Nastaví ikonku tlačidla NightToggleUIButton podľa stavu:
    ///   noc (zapnuté) → Pressed ikonka, deň (vypnuté) → Normal ikonka.
    /// Mení sa len sprite child Image ('Icon Image'); sprity pozadia
    /// tlačidla (Normal/Highlighted/Pressed v Button komponente) ostávajú.
    /// </summary>
    void RefreshNightToggleIcon(bool isNight)
    {
        if (nightToggleIconImage == null) return;

        Sprite s = isNight ? nightToggleIconPressed : nightToggleIconNormal;
        if (s != null)
            nightToggleIconImage.sprite = s;
    }

    /// <summary>
    /// Zosynchronizuje enabled stav zoom tlačidiel s aktuálnou zoom úrovňou
    /// kamery. Volá sa pri štarte aj po každom kliku.
    /// </summary>
    void RefreshZoomButtons()
    {
        if (isometricCamera == null) return;

        if (cameraZoomPlusButton != null)
            cameraZoomPlusButton.interactable = isometricCamera.CanZoomIn();

        if (cameraZoomMinusButton != null)
            cameraZoomMinusButton.interactable = isometricCamera.CanZoomOut();
    }

    // =====================================================================
    // GAME TIME / CASH – PREKRESĽOVANIE CAPTIONOV
    //
    // Tieto handlery sú napojené na eventy GameClock.OnDateChanged a
    // GameEconomy.OnBalanceChanged. Sú zámerne "hlúpe" – iba prečítajú
    // naformátovaný reťazec z modelu a zapíšu ho do textu. Žiadnu logiku
    // času ani peňazí GameMenuUI nedrží.
    // =====================================================================

    void RefreshGameTimeCaption()
    {
        if (gameTimeTextCaption == null || gameClock == null) return;
        gameTimeTextCaption.text = gameClock.GetFormattedDate();
    }

    void RefreshCashCaption()
    {
        if (gameCashAccountTextCaption == null || gameEconomy == null) return;
        gameCashAccountTextCaption.text = gameEconomy.GetFormattedBalance();
    }
}
