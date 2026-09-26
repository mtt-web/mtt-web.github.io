using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// StatusMapMenuUI
/// ─────────────────────────────────────────────────────────────────────────
/// UI okno pre zobrazenie STATUS MAPY hry (prehľadová mapa terénu, železničnej
/// siete, cestnej siete a priemyslu).
///
/// FUNKČNOSŤ:
///  0. Okno sa zavrie kliknutím na Close (X) tlačidlo – túto obsluhu rieši
///     sprievodný script StatusMapUIwindow (analogicky k dvojici
///     StatusFactoryMenuUI + StatusFactoryUIwindow).
///  1. Okno sa otvára/zatvára toggle tlačidlom "StatusMapUIButton" v hernom
///     menu – GameMenuUI volá ToggleWindow() (rovnaký vzor ako Status Stations
///     / Trains / Vehicles okná). Je to čisto INFORMAČNÉ okno, nespúšťa žiadny
///     konštrukčný režim.
///  2. Okno obsahuje 4 view tlačidlá (SMTerrainView / SMRailNetworkView /
///     SMRoadNetView / SMIndustryView). Kliknutím na ktorékoľvek z nich sa
///     PREGENERUJE mapový podklad v MapRawImage podľa zvoleného typu a
///     aktualizuje sa popis v TypeOfMapTextCaption.
///  3. Nad mapovým podkladom je viewport rámik (MapViewportIndicator), ktorý
///     ukazuje aktuálny záber hernej kamery a dá sa ťahať myšou – ťahaním sa
///     presúva kamera v hre. Tento script ho len zapne/vypne spolu s oknom.
///
/// RENDEROVANIE:
///   Samotné renderovanie mapy do textúry NEROBÍ tento script – deleguje ho
///   na statickú triedu MapSystem (súbor MapSystem.cs). Tento script len:
///     • drží referencie na UI prvky,
///     • registruje obsluhu tlačidiel,
///     • pri prepnutí zavolá MapSystem.RenderMap(...) a výsledok zobrazí.
///
/// POZN. K TEXTOVÉMU PRVKU:
///   TypeOfMapTextCaption je v scéne TextMeshPro komponent (TextMeshProUGUI –
///   v Add Component menu uvedený ako "Text - TextMeshPro").
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class StatusMapMenuUI : MonoBehaviour
{
    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Root Panel")]
    [Tooltip("Koreňový panel okna (StatusMapMenuUIPanel - Window). " +
             "Tento GameObject sa aktivuje/deaktivuje pri otvorení/zatvorení okna.")]
    [SerializeField] private GameObject statusMapMenuUIPanel;

    [Header("Obsah okna")]
    [Tooltip("RawImage, do ktorého sa zobrazuje vygenerovaný mapový podklad " +
             "(v Hierarchy \"MapRawImage\").")]
    [SerializeField] private RawImage mapRawImage;

    [Tooltip("Popisok aktuálneho typu mapy – TextMeshProUGUI " +
             "(v Hierarchy \"TypeOfMapTextCaption\", komponent " +
             "\"Text - TextMeshPro\").")]
    [SerializeField] private TextMeshProUGUI typeOfMapTextCaption;

    [Tooltip("Viewport rámik nad mapou (MapViewportIndicator). Ukazuje záber " +
             "hernej kamery a umožňuje jej presun ťahom. Voliteľné – ak nie " +
             "je priradený, mapa funguje bez rámika.")]
    [SerializeField] private MapViewportIndicator mapViewportIndicator;

    [Header("View tlačidlá")]
    [Tooltip("SMTerrainViewButton – prepne mapu na TerrainView.")]
    [SerializeField] private Button smTerrainViewButton;

    [Tooltip("SMRailNetworkViewButton – prepne mapu na RailNetworkView.")]
    [SerializeField] private Button smRailNetworkViewButton;

    [Tooltip("SMRoadNetViewButton – prepne mapu na RoadNetView.")]
    [SerializeField] private Button smRoadNetViewButton;

    [Tooltip("SMIndustryViewButton – prepne mapu na IndustryView.")]
    [SerializeField] private Button smIndustryViewButton;

    [Header("Render nastavenia")]
    [Tooltip("Počet obrazových pixelov na jeden herný tile. Vyššia hodnota = " +
             "ostrejšia mapa, ale väčšia textúra. Rozumný rozsah 2–8.")]
    [SerializeField] private int pixelsPerTile = 4;

    [Tooltip("Typ mapy, ktorý sa zobrazí pri prvom otvorení okna.")]
    [SerializeField]
    private MapSystem.MapViewType defaultViewType
        = MapSystem.MapViewType.TerrainView;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    // Aktuálne zobrazený typ mapy.
    private MapSystem.MapViewType currentViewType;

    // Naposledy vygenerovaná textúra – držíme referenciu, aby sme ju pri
    // ďalšom prepnutí mohli korektne zničiť (Texture2D nie je spravovaný
    // garbage collectorom okamžite, treba Destroy, inak pamäť rastie).
    private Texture2D currentMapTexture;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        // POZOR: panel NEDEAKTIVUJEME tu v Awake().
        //
        // Dôvod (rovnaký ako v StatusFactoryMenuUI): na rovnakom GameObjecte
        // (root paneli) je pripojený aj sprievodný script StatusMapUIwindow,
        // ktorý si v svojom Awake() registruje listener na Close (X) tlačidlo.
        // Poradie volania Awake() medzi viacerými skriptami na tom istom
        // GameObjecte je v Unity nedeterministické (Script Execution Order).
        // Ak by tento Awake() zbehol PRV a hneď panel deaktivoval, druhý
        // script by svoj Awake() už nemusel stihnúť – Unity nevolá Awake()
        // na neaktívnych GameObjectoch. Close tlačidlo by potom nefungovalo.
        //
        // Riešenie: panel skryjeme až v Start(), kde sú už zaručene
        // dokončené všetky Awake() volania v scéne.

        // Registrácia obsluhy view tlačidiel. Listener-y stačí pripojiť raz;
        // fungujú aj keď je panel medzitým skrytý (kliknúť sa na ne aj tak
        // nedá, kým je okno zatvorené).
        if (smTerrainViewButton != null)
            smTerrainViewButton.onClick.AddListener(ShowTerrainView);

        if (smRailNetworkViewButton != null)
            smRailNetworkViewButton.onClick.AddListener(ShowRailNetworkView);

        if (smRoadNetViewButton != null)
            smRoadNetViewButton.onClick.AddListener(ShowRoadNetView);

        if (smIndustryViewButton != null)
            smIndustryViewButton.onClick.AddListener(ShowIndustryView);
    }

    void Start()
    {
        // Okno skryjeme až teraz – po Start() máme istotu, že Awake() na
        // sprievodnom StatusMapUIwindow (close button listener) už zbehol.
        // Okno sa otvorí až po kliku na StatusMapUIButton cez ToggleWindow().
        if (statusMapMenuUIPanel != null)
            statusMapMenuUIPanel.SetActive(false);

        currentViewType = defaultViewType;
    }

    void OnDestroy()
    {
        // Uvoľníme poslednú textúru, aby sme nenechali visieť natívnu pamäť.
        if (currentMapTexture != null)
            Destroy(currentMapTexture);
    }

    // =====================================================================
    // PUBLIC API – volá GameMenuUI pri kliknutí na StatusMapUIButton
    // =====================================================================

    /// <summary>
    /// Prepne stav okna – ak je zavreté, otvorí ho; ak je otvorené, zavrie.
    /// Volá GameMenuUI z obsluhy toggle tlačidla "StatusMapUIButton".
    /// </summary>
    public void ToggleWindow()
    {
        if (statusMapMenuUIPanel == null) return;

        if (statusMapMenuUIPanel.activeSelf)
            CloseWindow();
        else
            OpenWindow();
    }

    /// <summary>
    /// Zobrazí okno (aktivuje root panel) a vygeneruje mapový podklad.
    ///
    /// Pri každom otvorení sa mapa pregeneruje nanovo – tým je zaručené,
    /// že odráža aktuálny stav hry (medzitým mohli pribudnúť trate, továrne,
    /// alebo sa zmenil terén).
    /// </summary>
    public void OpenWindow()
    {
        if (statusMapMenuUIPanel != null)
            statusMapMenuUIPanel.SetActive(true);

        // Pri otvorení vždy obnovíme mapu podľa aktuálne zvoleného typu.
        RefreshMap();
    }

    /// <summary>
    /// Skryje okno. Volá ho sprievodný StatusMapUIwindow pri stlačení
    /// Close (X), môže sa volať aj zvonku (napr. pri prepnutí do iného režimu).
    /// </summary>
    public void CloseWindow()
    {
        if (statusMapMenuUIPanel != null)
            statusMapMenuUIPanel.SetActive(false);
    }

    // =====================================================================
    // OBSLUHA VIEW TLAČIDIEL
    // =====================================================================
    // Každé tlačidlo nastaví typ mapy a pregeneruje podklad. Metódy sú
    // verejné, takže ich možno priradiť aj priamo v Inspectore (OnClick),
    // ak by sa neskôr nepoužil AddListener.

    /// <summary>SMTerrainViewButton → prepne mapu na celkový terén.</summary>
    public void ShowTerrainView()
    {
        SetViewType(MapSystem.MapViewType.TerrainView);
    }

    /// <summary>SMRailNetworkViewButton → prepne mapu na železničnú sieť.</summary>
    public void ShowRailNetworkView()
    {
        SetViewType(MapSystem.MapViewType.RailNetworkView);
    }

    /// <summary>SMRoadNetViewButton → prepne mapu na cestnú sieť.</summary>
    public void ShowRoadNetView()
    {
        SetViewType(MapSystem.MapViewType.RoadNetView);
    }

    /// <summary>SMIndustryViewButton → prepne mapu na priemysel.</summary>
    public void ShowIndustryView()
    {
        SetViewType(MapSystem.MapViewType.IndustryView);
    }

    // =====================================================================
    // RENDEROVANIE MAPY
    // =====================================================================

    /// <summary>
    /// Nastaví typ mapy a pregeneruje podklad. Ak je zvolený typ rovnaký ako
    /// aktuálny, mapa sa aj tak pregeneruje (užitočné ako "refresh").
    /// </summary>
    private void SetViewType(MapSystem.MapViewType viewType)
    {
        currentViewType = viewType;
        RefreshMap();
    }

    /// <summary>
    /// Jadro: vygeneruje textúru cez MapSystem a zobrazí ju v MapRawImage,
    /// plus aktualizuje popis v TypeOfMapTextCaption.
    ///
    /// Starú textúru pred priradením novej zničíme (Destroy), aby natívna
    /// pamäť po opakovanom prepínaní nerástla.
    /// </summary>
    private void RefreshMap()
    {
        // 1) Vygenerovanie novej textúry podľa aktuálneho typu mapy.
        Texture2D newTex = MapSystem.RenderMap(currentViewType, pixelsPerTile);

        // 2) Zničenie starej textúry (ak existuje a je iná než nová).
        if (currentMapTexture != null && currentMapTexture != newTex)
            Destroy(currentMapTexture);

        currentMapTexture = newTex;

        // 3) Priradenie do RawImage.
        if (mapRawImage != null)
        {
            mapRawImage.texture = newTex;
            // Aby sa mapa zobrazila vždy v plnej, neprehľadnej farbe.
            mapRawImage.color = Color.white;
        }
        else
        {
            Debug.LogWarning("[StatusMapMenuUI] mapRawImage nie je priradený v Inspectore.");
        }

        // 4) Aktualizácia popisu typu mapy.
        UpdateCaption();
    }

    /// <summary>
    /// Zapíše ľudský názov aktuálneho typu mapy do TypeOfMapTextCaption.
    /// </summary>
    private void UpdateCaption()
    {
        if (typeOfMapTextCaption != null)
            typeOfMapTextCaption.text = MapSystem.GetViewLabel(currentViewType);
    }
}
