using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Central controller for the Main Menu scene.
/// Handles panel switching (Main / Load / Credits) and the main button actions.
/// No new windows are opened: panels are simply activated / deactivated.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject loadGamePanel;
    [SerializeField] private GameObject creditsPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Load Game tlačidlo")]
    [Tooltip("Button 'LoadGameButton'. Automaticky sa enable/disable-ne podľa toho, či existuje save súbor (IndicatrixAPI.SaveFilePath).")]
    [SerializeField] private Button loadGameButton;

    [Header("Controllers")]
    [SerializeField] private MainMenuFadeController fadeController; // on MainPanel
    [SerializeField] private IntroSequenceController introController; // optional
    [Tooltip("Falošná načítavacia obrazovka po kliknutí na New Game (voliteľné). Ak je null, hra sa spustí priamo.")]
    [SerializeField] private FakePreloaderController preloaderController; // optional

    [Header("Options")]
    [Tooltip("If enabled, the intro sequence plays first and the main menu appears afterwards.")]
    [SerializeField] private bool playIntroOnStart = true;

    // Príznak prežíva načítanie scén počas behu aplikácie,
    // ale resetuje sa pri reštarte hry. => intro len prvýkrát.
    private static bool introAlreadyPlayed = false;

    /// <summary>Názov hernej scény (Build profiles: index 1 = Scenes/IndicatrixScene).</summary>
    private const string GameSceneName = "IndicatrixScene";

    // =====================================================================
    // KURZOR MYŠI
    //
    // Analogicky ako v GameManager.cs (samostatná scéna Main Menu = vlastné
    // nastavenie kurzora). Statický vlastný kurzor sa nastaví raz v Start()
    // cez Cursor.SetCursor a platí, kým je táto scéna aktívna.
    //
    // POZOR – dôležité pre správne fungovanie:
    //  • Cursor.SetCursor vyžaduje Texture2D, nie Sprite. Sprite si preto
    //    nižšie prevádzame na textúru (ApplyCustomCursor / ExtractCursorTexture).
    //  • Ak je sprite samostatný obrázok (celá textúra = 1 sprite), použije sa
    //    priamo – funguje bez ďalších nastavení.
    //  • Ak je sprite výrez z atlasu, musíme pixely vykopírovať, čo vyžaduje
    //    zapnuté "Read/Write Enabled" na textúre v import settings. Preto sa
    //    ako kurzor odporúča samostatný PNG (Texture Type = Sprite).
    //  • CursorMode.Auto použije rýchlejší hardvérový kurzor, ktorý má však na
    //    niektorých platformách limit veľkosti (napr. 32×32). Pri väčšom sprite,
    //    ktorý sa nezobrazuje, prepni na ForceSoftware.
    // =====================================================================

    [Header("Kurzor myši")]
    [Tooltip("Sprite použitý ako kurzor myši v Main Menu scéne. Natiahni ho sem cez Inspector. " +
             "Ak ostane prázdny, ponechá sa systémový kurzor.")]
    [SerializeField] private Sprite cursorSprite;

    [Tooltip("Hotspot (aktívny bod) kurzora v pixeloch od ĽAVÉHO HORNÉHO rohu obrázka. " +
             "(0,0) = špička kurzora je v ľavom hornom rohu; stred = (šírka/2, výška/2).")]
    [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

    [Tooltip("Auto = hardvérový kurzor (rýchly, ale s limitmi veľkosti podľa platformy). " +
             "ForceSoftware = spoľahlivý pre ľubovoľnú veľkosť sprite.")]
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;


    private void Start()
    {
        // Sub-panely vždy začínajú skryté.
        if (loadGamePanel != null) loadGamePanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        // Nastavenie vlastného statického kurzora pre Main Menu scénu.
        ApplyCustomCursor();

        // LoadGameButton je enabled len ak existuje savegame.dat.
        RefreshLoadButtonState();

        // Intro spusti len ak je povolené, máme controller A ešte nebežalo.
        bool shouldPlayIntro = playIntroOnStart
                               && introController != null
                               && !introAlreadyPlayed;

        if (shouldPlayIntro)
        {
            introAlreadyPlayed = true;   // označ, že už bežalo

            // Skry main panel, prehraj intro, potom cez callback ukáž menu.
            if (mainPanel != null) mainPanel.SetActive(false);
            introController.PlayIntro(ShowMainMenuWithFade);
        }
        else
        {
            // Intro sa už v tomto behu hry odohralo (napr. návrat cez BackToMainMenu).
            // introRoot môže byť v znovu-načítanej scéne uložený ako aktívny, preto
            // ho treba explicitne a potichu vypnúť – inak by "prekukol" navrchu,
            // aj keď sa PlayIntro() vôbec nezavolá.
            if (introController != null) introController.SkipImmediate();

            ShowMainMenuWithFade();
        }
    }

    // =====================================================================
    // KURZOR MYŠI – implementácia
    // =====================================================================

    /// <summary>
    /// Nastaví vlastný kurzor podľa <see cref="cursorSprite"/> natiahnutého
    /// v Inspektore. Volá sa raz v Start(); kurzor potom platí, kým je
    /// aktívna táto (Main Menu) scéna. Ak sprite nie je priradený, ponechá
    /// sa systémový kurzor.
    /// </summary>
    private void ApplyCustomCursor()
    {
        if (cursorSprite == null) return; // žiadny kurzor → systémový ostáva

        Texture2D cursorTexture = ExtractCursorTexture(cursorSprite);
        if (cursorTexture == null) return; // konverzia zlyhala (dôvod už zalogovaný)

        Cursor.SetCursor(cursorTexture, cursorHotspot, cursorMode);
    }

    /// <summary>
    /// Získa <see cref="Texture2D"/> zo sprite pre Cursor.SetCursor.
    ///
    /// Ak sprite pokrýva celú svoju textúru (bežný prípad – samostatný PNG),
    /// vráti textúru priamo (bez potreby Read/Write). Ak je sprite výrezom
    /// z väčšej textúry (atlas), vykopíruje príslušné pixely – to už ale
    /// vyžaduje zapnuté "Read/Write Enabled" na zdrojovej textúre.
    /// </summary>
    private Texture2D ExtractCursorTexture(Sprite sprite)
    {
        Texture2D srcTex = sprite.texture;
        if (srcTex == null)
        {
            Debug.LogError($"[MainMenuController] Kurzor '{sprite.name}' nemá platnú textúru.");
            return null;
        }

        Rect rect = sprite.textureRect;

        // Sprite = celá textúra? Potom ju vieme použiť priamo.
        bool isWholeTexture =
               Mathf.Approximately(rect.x, 0f)
            && Mathf.Approximately(rect.y, 0f)
            && Mathf.Approximately(rect.width, srcTex.width)
            && Mathf.Approximately(rect.height, srcTex.height);

        if (isWholeTexture)
            return srcTex;

        // Výrez z atlasu → skopírujeme pixely daného rectu do novej textúry.
        // GetPixels používa počiatok v ĽAVOM DOLNOM rohu, rovnako ako textureRect,
        // takže výsledok ostane správne orientovaný.
        try
        {
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);

            Texture2D cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
            cropped.SetPixels(srcTex.GetPixels(
                Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), w, h));
            cropped.Apply();
            return cropped;
        }
        catch (UnityException)
        {
            Debug.LogError(
                $"[MainMenuController] Kurzor '{sprite.name}' je výrez z atlasu, ale jeho " +
                $"textúra nemá zapnuté 'Read/Write Enabled'. Zapni ho v import " +
                $"settings textúry, alebo použi samostatný PNG obrázok ako kurzor.");
            return null;
        }
    }

    /// <summary>Shows the main panel and plays the 3s fade-in.</summary>
    public void ShowMainMenuWithFade()
    {
        if (loadGamePanel != null) loadGamePanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (mainPanel != null) mainPanel.SetActive(true);

        if (fadeController != null) fadeController.PlayFadeIn();

        // Znovu prekontroluj save súbor (pre istotu, keby sa medzitým zmenil).
        RefreshLoadButtonState();
    }

    /// <summary>
    /// Zapne/vypne LoadGameButton podľa toho, či existuje save súbor
    /// (IndicatrixAPI.SaveFilePath -> "Assets/SaveGame/savegame.dat"). Save sa
    /// dá vytvoriť len z vnútra hry, takže tu iba čítame, či súbor existuje.
    ///
    /// Ak je na tlačidle aj HoverButton (vlastná grafika namiesto štandardného
    /// Button tint/sprite swap), použije sa jeho SetInteractable() – ten prepne
    /// aj Button.interactable, aj vizuál na "Disabled Sprite" priradený v HoverButton.
    /// Bez HoverButton sa nastaví len Button.interactable (štandardný Unity vizuál).
    /// </summary>
    private void RefreshLoadButtonState()
    {
        if (loadGameButton == null) return;

        bool saveExists = System.IO.File.Exists(IndicatrixAPI.SaveFilePath);

        HoverButton hover = loadGameButton.GetComponent<HoverButton>();
        if (hover != null)
            hover.SetInteractable(saveExists);
        else
            loadGameButton.interactable = saveExists;
    }

    // ---------------------------------------------------------------------
    // Button handlers (hook these into Button -> OnClick in the Inspector)
    // ---------------------------------------------------------------------

    public void OnNewGame()
    {
        Debug.Log("[MainMenu] New Game pressed.");

        // Nová hra → NEnačítavaj save (príznak musí byť zhodený, keby ostal
        // nastavený z predošlého kliknutia na Load Game, ktoré sa nedokončilo).
        IndicatrixAPI.LoadGameOnSceneStart = false;

        // Ak máme fake preloader, najprv ho prehraj (fade in → 5s progres → fade out)
        // a až po jeho dokončení načítaj hernú scénu. Inak spusti hru priamo.
        if (preloaderController != null)
        {
            preloaderController.PlayPreloader(LoadGameScene);
        }
        else
        {
            LoadGameScene();
        }
    }

    /// <summary>Načíta hernú scénu.</summary>
    private void LoadGameScene()
    {
        SceneManager.LoadScene(GameSceneName);
    }

    public void OnLoadGame()
    {
        Debug.Log("[MainMenu] Load Game pressed.");

        // Ak save súbor neexistuje, nemá zmysel prepínať scénu – načítavať nie je čo.
        if (!System.IO.File.Exists(IndicatrixAPI.SaveFilePath))
        {
            Debug.LogWarning("[MainMenu] Save súbor neexistuje: " +
                             IndicatrixAPI.SaveFilePath + " – Load Game ignorovaný.");
            return;
        }

        // Nastav medziscénový príznak a spusti hernú scénu. IndicatrixAPI v nej
        // po pripravení systémov sám zavolá LoadGame() a obnoví uloženú hru.
        IndicatrixAPI.LoadGameOnSceneStart = true;
        SceneManager.LoadScene(GameSceneName);
    }

    public void OnCredits()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(true);
    }

    public void OnSettings()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    /// <summary>Returns from any sub-panel to the main menu (instant, no long fade).</summary>
    public void OnBackToMainMenu()
    {
        if (loadGamePanel != null) loadGamePanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (mainPanel != null) mainPanel.SetActive(true);

        // Make sure the panel is fully visible without replaying the long intro fade.
        if (fadeController != null) fadeController.SetVisibleInstant();
    }

    public void OnExitGame()
    {
        Debug.Log("[MainMenu] Exit Game pressed.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}