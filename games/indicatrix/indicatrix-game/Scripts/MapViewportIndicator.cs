using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// MapViewportIndicator
/// ─────────────────────────────────────────────────────────────────────────
/// Viewport rámik ("minimap indicator") vykreslený NAD mapovým podkladom
/// v okne StatusMapMenuUI. Ukazuje, na ktorú časť hernej mapy sa práve
/// pozerá izometrická kamera, a umožňuje hráčovi túto časť presúvať
/// drag-and-drop ťahom myši (čím sa pohne kamera v hre).
///
/// UMIESTNENIE V HIERARCHII:
///   Tento komponent je na samostatnom UI GameObjecte, ktorý je DIEŤAŤOM
///   MapRawImage (alebo súrodencom kresleným nad ním). Jeho RectTransform
///   musí presne prekrývať MapRawImage – preto sa odporúča roztiahnuť ho
///   na plný rect rodiča (anchors 0,0 – 1,1, offsety 0).
///
/// AKO TO FUNGUJE:
///   1) ZÁBER KAMERY → MAPA
///      Premietnu sa 4 rohy kamerového viewportu (0,0)(1,0)(1,1)(0,1) cez
///      ViewportPointToRay na rovinu mapy (y = projectionPlaneY). Tým
///      vzniknú 4 svetové body – pretože kamera je izometrická (otočená),
///      tvoria na mape KOSOŠTVOREC, nie obdĺžnik.
///   2) MAPA → UI
///      Svetové X/Z sa normalizujú na [0,1] podľa rozmeru tile mapy
///      (terrainWidth) a prepočítajú na lokálne pozície v rámci rectu
///      MapRawImage. Týmito 4 bodmi sa vykreslí rámik (4 hrany).
///   3) DRAG
///      Pri ťahaní sa posun myši (v UI priestore) prevedie späť na posun
///      v tile priestore a zavolá IsometricCamera.SetCameraWorldPositionXZ,
///      čím sa kamera v hre presunie. Rámik sa pri ďalšom Update prekreslí
///      už na novej pozícii.
///
/// VYKRESĽOVANIE:
///   Rámik je tvorený 4 hranami. Každá hrana je samostatný UI Image, ktorý
///   sa za behu naťahuje/otáča medzi dvoma rohmi. Tieto Image objekty si
///   komponent vytvára sám v Awake() – v scéne ich netreba pripravovať.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MapViewportIndicator : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
{
    // =====================================================================
    // STATICKÝ STAV PRE INTEROP S HERNOU KAMEROU
    // =====================================================================

    /// <summary>
    /// True, ak používateľ práve drží pravé tlačidlo myši a začal ho držať
    /// nad minimapou (panning mapového podkladu). IsometricCamera túto
    /// hodnotu číta, aby v takom prípade NEhýbala hernou kamerou pravým
    /// tlačidlom (predišlo by sa konfliktu dvoch súbežných pan operácií).
    ///
    /// Flag je statický zámerne – v scéne býva typicky len jedna minimapa
    /// a jedna izo kamera; statický prístup je jednoduchší ako udržiavať
    /// vzájomné referencie. Ak by v budúcnosti vzniklo viac minimáp,
    /// stačí premeniť na inštančný a sprístupniť cez singleton/registry.
    /// </summary>
    public static bool IsRightDraggingMinimap { get; private set; }

    // =====================================================================
    // SCÉNICKÉ REFERENCIE
    // =====================================================================

    [Header("Referencie")]
    [Tooltip("RectTransform mapového podkladu (MapRawImage). Rámik sa kreslí " +
             "v jeho súradniciach. Ak nie je priradený, použije sa rodič " +
             "tohto GameObjectu.")]
    [SerializeField] private RectTransform mapRect;

    [Tooltip("RawImage mapového podkladu (MapRawImage). Slúži na vizuálny zoom " +
             "a pan minimapy cez uvRect (vystrihne sa časť textúry podľa " +
             "aktuálneho zoomu a panu). Ak nie je priradený, komponent ho " +
             "skúsi nájsť na rovnakom GameObjecte ako mapRect.")]
    [SerializeField] private RawImage mapRawImage;

    [Tooltip("Izometrická kamera hry. Ak nie je priradená, komponent ju " +
             "skúsi nájsť cez FindFirstObjectByType pri Start().")]
    [SerializeField] private IsometricCamera isometricCamera;

    [Header("Vzhľad rámika")]
    [Tooltip("Farba hrán viewport rámika.")]
    [SerializeField] private Color frameColor = new Color(1f, 1f, 1f, 0.95f);

    [Tooltip("Hrúbka hrán rámika v pixeloch.")]
    [SerializeField] private float frameThickness = 3f;

    [Header("Geometria")]
    [Tooltip("Y-súradnica roviny, na ktorú sa premietajú rohy kamerového " +
             "záberu. Má zodpovedať výškovej úrovni mapy – TerrainManager " +
             "stavia terén na baseHeight = 2.75.")]
    [SerializeField] private float projectionPlaneY = 2.75f;

    [Header("Zoom a pan minimapy")]
    [Tooltip("Minimálna úroveň priblíženia minimapy. 1.0 = celá mapa vidieť " +
             "(žiadny zoom). Hodnoty menšie ako 1 by zobrazili mapu menšiu, " +
             "než je RawImage – tomu sa vyhneme.")]
    [SerializeField] private float minMapZoom = 1f;

    [Tooltip("Maximálna úroveň priblíženia minimapy. 4 = vidieť 1/4 šírky " +
             "mapy v každom smere (16-násobné priblíženie plochy).")]
    [SerializeField] private float maxMapZoom = 6f;

    [Tooltip("Koľko zmení jedno tiknutie kolieska myši zoom (multiplikatívne). " +
             "1.2 znamená +20 % zoom na jedno tiknutie nahor.")]
    [SerializeField] private float zoomStep = 1.2f;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    // 4 hrany rámika (vytvorené v Awake). Index: 0=dole, 1=vpravo, 2=hore, 3=vľavo
    // – ale keďže kreslíme kosoštvorec, sú to jednoducho 4 strany medzi
    // po sebe idúcimi rohmi.
    private RectTransform[] edges = new RectTransform[4];

    private RectTransform selfRect;

    // ── Drag stav ────────────────────────────────────────────────────────
    // Rozlišujeme dva nezávislé typy dragu:
    //   • ľavý  → presun hernej kamery (klikneš inde na minimape, kamera
    //             tam skočí); pôvodné správanie.
    //   • pravý → pan mapového podkladu (posun obsahu minimapy).
    // Naraz môže byť aktívny len jeden – ten, ktorý začal v OnPointerDown.
    private bool draggingCamera = false;   // ľavé tlačidlo
    private bool draggingPan = false;      // pravé tlačidlo
    private Vector2 panDragLastLocal;      // posledná lokálna poloha kurzora počas pravého dragu

    // ── Zoom a pan minimapy ──────────────────────────────────────────────
    // mapZoom = 1.0 znamená "vidieť celú mapu"; 2.0 = vidieť polovicu šírky
    // v každom smere (4× zväčšenie plochy), atď.
    private float mapZoom = 1f;

    // Stred zobrazeného výrezu v normalizovaných súradniciach mapy [0,1].
    // (0.5, 0.5) = stred celej mapy. Pri zoom = 1 musí byť vždy (0.5, 0.5)
    // (inak by zoom-out odkryl okraje mimo mapy) – zabezpečí ClampPan().
    private Vector2 panCenterNormalized = new Vector2(0.5f, 0.5f);

    // Posledné vypočítané rohy rámika v lokálnych súradniciach mapRect
    // (poradie proti smeru hod. ručičiek). Držíme ich, aby drag vedel
    // pracovať so stredom rámika.
    private readonly Vector2[] cornersUI = new Vector2[4];

    // True ak sa v poslednom Update podarilo rámik korektne vypočítať.
    private bool frameValid = false;

    // =====================================================================
    // UNITY LIFECYCLE
    // =====================================================================

    void Awake()
    {
        selfRect = GetComponent<RectTransform>();

        // Ak mapRect nie je priradený, skús rodiča.
        if (mapRect == null)
        {
            Transform parent = transform.parent;
            if (parent != null)
                mapRect = parent as RectTransform;
        }

        // Ak mapRawImage nie je priradený, pokús sa ho nájsť na tom istom
        // GameObjecte ako mapRect (typický prípad – MapRawImage má aj
        // RectTransform aj RawImage komponent).
        if (mapRawImage == null && mapRect != null)
            mapRawImage = mapRect.GetComponent<RawImage>();

        // Zabezpeč, aby hrany rámika nikdy nevyšli mimo plochy minimapy.
        // Bez masky by sa po zoom-e časti kosoštvorca renderovali aj von
        // za MapRawImage (cez celé UI okno) – Unity UI Image komponenty
        // nemajú clipping na rodičovský rect.
        EnsureMaskOnIndicator();

        CreateEdges();
    }

    void Start()
    {
        // Lazy dohľadanie kamery, ak nebola priradená v Inspectore.
        if (isometricCamera == null)
            isometricCamera = Object.FindFirstObjectByType<IsometricCamera>();

        if (isometricCamera == null)
            Debug.LogWarning("[MapViewportIndicator] IsometricCamera sa nenašla – " +
                             "viewport rámik nebude fungovať.");
    }

    void OnEnable()
    {
        // Pri každom zobrazení (otvorení okna) resetujeme zoom/pan na
        // východiskový stav (celá mapa, vystredené). Tak hráč po opätovnom
        // otvorení okna vždy vidí celú mapu, bez ohľadu na predošlý stav.
        mapZoom = 1f;
        panCenterNormalized = new Vector2(0.5f, 0.5f);
        ApplyZoomToRawImage();

        // Hneď prekresli rámik, aby sa neukázal na zastaranej pozícii.
        UpdateFrame();
    }

    void OnDisable()
    {
        // Poistka: ak sa okno zavrie počas pravého dragu (napr. cez Close X
        // tlačidlo), nesmie ostať flag visieť na true – inak by herná kamera
        // ignorovala pravé tlačidlo aj po zatvorení minimapy.
        IsRightDraggingMinimap = false;
        draggingCamera = false;
        draggingPan = false;
    }

    void Update()
    {
        // Počas ľavého dragu (presun kamery) pozíciu kamery rieši OnDrag a
        // hneď tam aj prekreslí rámik – Update tu neprepisuje.
        // Počas pravého dragu (pan minimapy) sa síce kamera nehýbe, ale
        // rámik treba prekresliť, lebo sa zmenila uvRect podkladu –
        // UpdateFrame zavoláme aj v tomto prípade.
        if (!draggingCamera)
            UpdateFrame();
    }

    // =====================================================================
    // VYTVORENIE HRÁN RÁMIKA
    // =====================================================================

    /// <summary>
    /// Vytvorí 4 tenké UI Image objekty – hrany rámika. Sú deťmi tohto
    /// GameObjectu, takže sa kreslia v jeho priestore.
    /// </summary>
    private void CreateEdges()
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject go = new GameObject($"ViewportEdge_{i}", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            Image img = go.AddComponent<Image>();
            img.color = frameColor;
            img.raycastTarget = false;   // hrany nesmú "kradnúť" klik dragu

            RectTransform rt = go.GetComponent<RectTransform>();
            // Pivot a anchor do stredu – uľahčuje natáčanie hrany.
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            edges[i] = rt;
        }
    }

    /// <summary>
    /// Zabezpečí, aby hrany rámika kreslené v tomto GameObjecte boli orezané
    /// na jeho rect (= rovnaký rect ako MapRawImage). Bez orezania by sa pri
    /// zoom-e minimapy kosoštvorec mohol vykresliť AJ MIMO plochy mapy
    /// (cez celé okno StatusMapMenuUIPanel a ďalej).
    ///
    /// PREČO RECTMASK2D A NIE MASK:
    ///   • RectMask2D oreže iba podľa obdĺžnikového rectu, je lacný a
    ///     NEVYŽADUJE žiadny zdroj (Image na masking objekte). Presne to,
    ///     čo potrebujeme pre obdĺžnikovú minimapu.
    ///   • Mask vyžaduje Graphic (Image) na tom istom objekte a robí
    ///     stencil-based clipping podľa tvaru – pre tento prípad zbytočne
    ///     drahšie a komplikovanejšie.
    ///
    /// Komponent sa pridáva CEZ KÓD, aby používateľ nemusel nič manuálne
    /// nastavovať v Inspectore. Ak už RectMask2D na GameObjecte je (napr.
    /// niekto ho tam dal ručne), nič sa nepridáva.
    ///
    /// POZN. K HIERARCHII:
    ///   Hrany rámika sú vytvorené ako deti TOHTO GameObjectu (CreateEdges
    ///   volá SetParent(transform, false)). RectMask2D pridaný sem teda
    ///   automaticky orezáva všetky 4 hrany podľa rectu indicatora, ktorý
    ///   je natiahnutý 1:1 na rect MapRawImage – výsledok je orezanie
    ///   presne podľa plochy minimapy.
    /// </summary>
    private void EnsureMaskOnIndicator()
    {
        if (GetComponent<RectMask2D>() == null)
            gameObject.AddComponent<RectMask2D>();
    }

    // =====================================================================
    // VÝPOČET A VYKRESLENIE RÁMIKA
    // =====================================================================

    /// <summary>
    /// Prepočíta záber kamery na kosoštvorec v UI priestore a aktualizuje
    /// polohu 4 hrán. Ak chýbajú referencie alebo dáta, rámik skryje.
    /// </summary>
    private void UpdateFrame()
    {
        frameValid = false;

        if (isometricCamera == null || mapRect == null)
        {
            SetEdgesVisible(false);
            return;
        }

        Camera cam = isometricCamera.CameraComponent;
        TerrainManager tm = TerrainManager.instance;

        if (cam == null || tm == null || tm.terrainWidth <= 0)
        {
            SetEdgesVisible(false);
            return;
        }

        int mapTiles = tm.terrainWidth;

        // --- 1) Premietnutie 4 rohov viewportu kamery na rovinu mapy ----
        // Rohy viewportu: (0,0) ľavý-dolný, (1,0) pravý-dolný,
        //                 (1,1) pravý-horný, (0,1) ľavý-horný.
        Vector2[] viewportCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        for (int i = 0; i < 4; i++)
        {
            if (!ProjectViewportToWorld(cam, viewportCorners[i], out Vector3 worldPt))
            {
                // Roh sa nepodarilo premietnuť (lúč rovnobežný s rovinou) –
                // rámik tentokrát nevykreslíme.
                SetEdgesVisible(false);
                return;
            }

            // --- 2) Svet → normalizované [0,1] → lokálne UI súradnice ---
            // Tile mapa zaberá svetový rozsah [0, mapTiles] v X aj Z.
            //
            // POZOR: nx/nz SCHVÁLNE NECLAMPUJEME do [0,1].
            //
            // Pri najvzdialenejšom zoom-e (alebo keď je kamera blízko kraja
            // mapy) môže viditeľný záber presahovať obvod mapy – jeden alebo
            // viacero rohov záberu potom má worldPt.x/z mimo [0, mapTiles].
            // Keby sme tie hodnoty clampovali, niekoľko rohov by sa "nalepilo"
            // na ten istý bod a rámik by zdegeneroval (vyzerá to ako keby
            // zmizol – v skutočnosti má nulovú/minimálnu plochu).
            //
            // Necháme normalizované hodnoty plynúť bez orezania. Hrany rámika
            // sa potom vykreslia v lokálnych súradniciach, ktoré môžu byť aj
            // mimo rectu MapRawImage – ale o orezanie sa postará RectMask2D
            // pridaný v EnsureMaskOnIndicator(). Výsledok: rámik je vždy
            // správne tvarovaný a viditeľné sú len jeho časti vnútri minimapy.
            float nx = worldPt.x / mapTiles;
            float nz = worldPt.z / mapTiles;

            cornersUI[i] = NormalizedToMapLocal(nx, nz);
        }

        // --- 3) Vykreslenie 4 hrán medzi po sebe idúcimi rohmi ----------
        SetEdgesVisible(true);
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = cornersUI[i];
            Vector2 b = cornersUI[(i + 1) % 4];
            PlaceEdge(edges[i], a, b);
        }

        frameValid = true;
    }

    /// <summary>
    /// Premietne bod viewportu kamery (0..1, 0..1) na svetovú rovinu
    /// y = projectionPlaneY a vráti svetový bod priesečníka.
    ///
    /// Vracia false ak je lúč rovnobežný s rovinou (nemá priesečník).
    ///
    /// POZN. K ORTOGRAFICKEJ KAMERE:
    ///   Pri ortografickej kamere má každý lúč z ViewportPointToRay vlastný
    ///   origin (rozprestrený po near-plane), nie spoločný. Pri šikmom uhle
    ///   pohľadu (30° dole) a väčšom orthographicSize sa môže ľahko stať,
    ///   že "horný" roh viewportu vygeneruje ray, ktorého origin.y leží už
    ///   POD rovinou mapy (projectionPlaneY = 2.75). Pre tento lúč potom
    ///   parameter t vyjde záporný – ale priesečník na rovine je STÁLE
    ///   platný 3D bod (vzorec origin + dir*t funguje rovnako pre záporné
    ///   t v opačnom smere lúča). Preto t záporné NEodmietame, inak by sa
    ///   pri vyššom zoom-e rámik nečakane stratil.
    /// </summary>
    private bool ProjectViewportToWorld(Camera cam, Vector2 viewportPoint,
                                        out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;

        Ray ray = cam.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));

        // Priesečník lúča s vodorovnou rovinou y = projectionPlaneY.
        // Ak je smer lúča v osi Y takmer nulový, lúč je rovnobežný s rovinou
        // – priesečník neexistuje.
        if (Mathf.Abs(ray.direction.y) < 0.0001f)
            return false;

        float t = (projectionPlaneY - ray.origin.y) / ray.direction.y;

        // POZN.: t môže vyjsť záporné (viď komentár v doc-string vyššie). Pre
        // ortografickú kameru je to legitímne – necháme to plynúť.
        worldPoint = ray.origin + ray.direction * t;
        return true;
    }

    /// <summary>
    /// Prevedie normalizovanú pozíciu mapy (nx, nz ∈ [0,1]) na lokálnu
    /// pozíciu v rámci RectTransformu MapRawImage.
    ///
    /// Konvencia: nx = 0 → ľavý okraj mapy, nx = 1 → pravý okraj mapy,
    ///            nz = 0 → spodný okraj mapy, nz = 1 → horný okraj mapy.
    /// (Zhodné s renderom v MapSystem: tile (0,0) je vľavo-dole.)
    ///
    /// ZOOM A PAN MINIMAPY:
    /// Pri zoom != 1 alebo pan != stred sa zobrazuje len výrez mapy. Pozícia
    /// (nx, nz) v rámci CELEJ mapy sa musí prepočítať na pozíciu v rámci
    /// AKTUÁLNE ZOBRAZENÉHO VÝREZU. Body mimo výrezu skončia mimo rectu
    /// MapRawImage – pri kreslení hrán to nevadí (môžu byť aj za okrajom,
    /// neviditeľná časť sa orežu maskou alebo jednoducho skreslia "von" –
    /// pri pevne danom RectTransforme to v UI nevadí, lebo Image renderer
    /// nemá clipping na rect, ale v praxi ostávajú väčšinou v rozsahu).
    ///
    /// Vzorec: kamerový bod v normalizovaných súradniciach mapy → posunieme
    /// tak, aby panCenter padol do stredu výrezu, → vynásobíme zoom-om →
    /// pripočítame 0.5 (stred rectu).
    /// </summary>
    private Vector2 NormalizedToMapLocal(float nx, float nz)
    {
        Rect r = mapRect.rect;

        // Prepočet cez zoom+pan: bod (nx, nz) → bod vo výreze ∈ [0,1]
        // ak je vnútri viditeľnej časti. Pri zoom = 1, pan = (0.5, 0.5)
        // dáva identitu.
        float viewportNx = (nx - panCenterNormalized.x) * mapZoom + 0.5f;
        float viewportNz = (nz - panCenterNormalized.y) * mapZoom + 0.5f;

        // r.xMin/yMin je ľavý-dolný roh v lokálnych súradniciach mapRect.
        float lx = r.xMin + viewportNx * r.width;
        float ly = r.yMin + viewportNz * r.height;
        return new Vector2(lx, ly);
    }

    /// <summary>
    /// Spätný prevod: lokálna pozícia v mapRect → normalizovaná pozícia
    /// v rámci CELEJ mapy [0,1]. Inverzia k <see cref="NormalizedToMapLocal"/>:
    ///   1) lokálne → normalizované v rámci viditeľného výrezu,
    ///   2) výrez → poloha na celej mape (cez panCenter a zoom).
    /// </summary>
    private Vector2 MapLocalToNormalized(Vector2 local)
    {
        Rect r = mapRect.rect;

        // 1) lokálna pozícia → [0,1] v rámci viditeľnej časti rectu.
        float viewportNx = (local.x - r.xMin) / r.width;
        float viewportNz = (local.y - r.yMin) / r.height;

        // 2) [0,1] vo výreze → [0,1] na celej mape (inverzia vzorca z NormalizedToMapLocal).
        float nx = (viewportNx - 0.5f) / mapZoom + panCenterNormalized.x;
        float nz = (viewportNz - 0.5f) / mapZoom + panCenterNormalized.y;
        return new Vector2(nx, nz);
    }

    /// <summary>
    /// Umiestni jednu hranu rámika tak, aby spájala body a–b (v lokálnych
    /// súradniciach mapRect). Hrana je tenký Image, ktorý sa naškáluje na
    /// dĺžku úsečky a natočí do jej smeru.
    ///
    /// POZN.: hrany sú deti TOHTO GameObjectu. Keďže tento GameObject
    /// prekrýva mapRect 1:1 (rovnaký rect), lokálne súradnice mapRect sú
    /// použiteľné aj tu. Ak by RectTransformy neboli zhodné, bolo by treba
    /// prevod – preto sa odporúča roztiahnuť indicator na plný rect mapy.
    /// </summary>
    private void PlaceEdge(RectTransform edge, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;

        // Stred hrany.
        edge.anchoredPosition = (a + b) * 0.5f;

        // Dĺžka × hrúbka.
        edge.sizeDelta = new Vector2(length, frameThickness);

        // Natočenie do smeru úsečky.
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        edge.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>Zobrazí/skryje všetky 4 hrany rámika.</summary>
    private void SetEdgesVisible(bool visible)
    {
        for (int i = 0; i < 4; i++)
            if (edges[i] != null)
                edges[i].gameObject.SetActive(visible);
    }

    // =====================================================================
    // DRAG AND DROP + SCROLL ZOOM
    // =====================================================================
    //
    // Tri nezávislé spôsoby interakcie s minimapou:
    //
    //   ĽAVÉ TLAČIDLO  → drag = presun hernej kamery. Kliknutím v minimape
    //                    sa stred kamerového záberu posunie na pozíciu
    //                    kurzora (a po ťahu nasleduje). Pôvodné správanie.
    //
    //   PRAVÉ TLAČIDLO → drag = pan mapového PODKLADU (posun obsahu minimapy
    //                    v rámci jej RawImage). Herná kamera sa NEhýbe.
    //                    Súbežne sa nastaví IsRightDraggingMinimap = true,
    //                    aby IsometricCamera ignorovala pravé tlačidlo,
    //                    kým ťah pokračuje.
    //
    //   KOLIESKO MYŠI  → zoom mapového podkladu (priblíženie/oddialenie
    //                    obsahu minimapy okolo aktuálnej polohy kurzora).
    //
    // POZN.: raycastTarget musí byť zapnutý na komponente, ktorý drží tento
    // skript (alebo na priehľadnom Image na ňom) – inak by drag/scroll
    // handlery nedostali eventy. Hrany rámika majú raycastTarget = false
    // zámerne, aby celú plochu obsluhoval tento komponent.

    public void OnPointerDown(PointerEventData eventData)
    {
        // Rozlíšime tlačidlo. PointerEventData.button je enum:
        //   Left   → drag kamery (pôvodné správanie),
        //   Right  → pan podkladu minimapy.
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // Drag začneme len ak je rámik platný (kamera aj mapa pripravené).
            draggingCamera = frameValid;
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // Začiatok pravého dragu – pamätáme si východiskovú lokálnu
            // polohu kurzora, voči nej budeme počítať delta posun pri OnDrag.
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    mapRect, eventData.position, eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                draggingPan = true;
                panDragLastLocal = localPoint;

                // Signál pre IsometricCamera: pravé tlačidlo je teraz "moje".
                IsRightDraggingMinimap = true;
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            HandleCameraDrag(eventData);
        else if (eventData.button == PointerEventData.InputButton.Right)
            HandlePanDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            draggingCamera = false;
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            draggingPan = false;
            IsRightDraggingMinimap = false;
        }
    }

    /// <summary>
    /// ĽAVÉ TLAČIDLO – presun hernej kamery na pozíciu kurzora.
    /// Pôvodná logika (zachovaná aj pri zoom-e minimapy: MapLocalToNormalized
    /// už berie zoom+pan do úvahy, takže kamera ide tam, kam reálne ukazuješ
    /// na priblíženom podklade).
    /// </summary>
    private void HandleCameraDrag(PointerEventData eventData)
    {
        if (!draggingCamera) return;
        if (isometricCamera == null || mapRect == null) return;

        TerrainManager tm = TerrainManager.instance;
        if (tm == null || tm.terrainWidth <= 0) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapRect, eventData.position, eventData.pressEventCamera,
                out Vector2 localPoint))
            return;

        // Lokálna pozícia → normalizované [0,1] na celej mape → tile súradnice.
        // MapLocalToNormalized automaticky odpočíta zoom+pan, takže výsledok
        // je vždy v súradniciach CELEJ mapy.
        Vector2 norm = MapLocalToNormalized(localPoint);
        norm.x = Mathf.Clamp01(norm.x);
        norm.y = Mathf.Clamp01(norm.y);

        float targetWorldX = norm.x * tm.terrainWidth;
        float targetWorldZ = norm.y * tm.terrainWidth;

        // Posun kamery: chceme, aby sa STRED záberu posunul na kurzor.
        if (TryGetViewWorldCenter(out Vector3 currentCenter))
        {
            Vector3 camPos = isometricCamera.GetCameraWorldPosition();
            float offsetX = camPos.x - currentCenter.x;
            float offsetZ = camPos.z - currentCenter.z;

            isometricCamera.SetCameraWorldPositionXZ(
                targetWorldX + offsetX,
                targetWorldZ + offsetZ);
        }
        else
        {
            isometricCamera.SetCameraWorldPositionXZ(targetWorldX, targetWorldZ);
        }

        UpdateFrame();
    }

    /// <summary>
    /// PRAVÉ TLAČIDLO – pan mapového podkladu. Posun kurzora v lokálnych
    /// súradniciach mapRect prevedieme na zmenu panCenterNormalized.
    ///
    /// Smer panu: keď používateľ ťahá DOPRAVA, obsah mapy ide doprava
    /// (= panCenter ide doľava). Tým minimapa funguje ako "uchopiť a
    /// posunúť papier" – štandardné správanie pre drag-pan.
    /// </summary>
    private void HandlePanDrag(PointerEventData eventData)
    {
        if (!draggingPan) return;
        if (mapRect == null || mapRawImage == null) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapRect, eventData.position, eventData.pressEventCamera,
                out Vector2 localPoint))
            return;

        Vector2 deltaLocal = localPoint - panDragLastLocal;
        panDragLastLocal = localPoint;

        Rect r = mapRect.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        // Posun lokálne → posun v normalizovaných súradniciach viewportu.
        // Pri zoom = Z má jedna obrazovková jednotka väčší dosah na celej
        // mape (delíme zoom-om), preto … / (mapZoom * r.size).
        float dNx = deltaLocal.x / (mapZoom * r.width);
        float dNz = deltaLocal.y / (mapZoom * r.height);

        // Ťah doprava → obsah ide doprava → panCenter sa posunie doľava.
        panCenterNormalized.x -= dNx;
        panCenterNormalized.y -= dNz;

        ClampPan();
        ApplyZoomToRawImage();
        // Rámik prekreslíme okamžite – počas pan dragu Update síce kreslí
        // tiež (draggingCamera == false), ale takto je odozva 1:1 s eventom.
        UpdateFrame();
    }

    /// <summary>
    /// SCROLL (koliesko myši) – zoom mapového podkladu okolo pozície kurzora.
    /// Aby sa bod pod kurzorom pri zoom-e neposúval, prepočítame panCenter
    /// tak, aby kurzor zostal na rovnakom mieste na mape pred aj po zmene
    /// zoom-u.
    /// </summary>
    public void OnScroll(PointerEventData eventData)
    {
        if (mapRect == null || mapRawImage == null) return;

        // Pozícia kurzora pred zoom-om v normalizovaných súradniciach mapy.
        // Použijeme aktuálny mapZoom, takže výsledok je "kam na celej mape
        // teraz ukazujem".
        Vector2 cursorNormBefore;
        bool cursorValid = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            mapRect, eventData.position, eventData.pressEventCamera,
            out Vector2 cursorLocal);
        cursorNormBefore = cursorValid ? MapLocalToNormalized(cursorLocal)
                                       : panCenterNormalized;

        // Aplikácia zoom kroku. scrollDelta.y > 0 = koliesko nahor = priblížiť.
        float scrollY = eventData.scrollDelta.y;
        if (Mathf.Approximately(scrollY, 0f)) return;

        float oldZoom = mapZoom;
        if (scrollY > 0f)
            mapZoom *= zoomStep;
        else
            mapZoom /= zoomStep;

        mapZoom = Mathf.Clamp(mapZoom, minMapZoom, maxMapZoom);

        // Ak zoom narazil na limit a nezmenil sa, netreba nič prepočítať.
        if (Mathf.Approximately(oldZoom, mapZoom))
            return;

        // Zoom okolo kurzora: po zmene zoom-u zmen panCenter tak, aby ten istý
        // bod mapy ostal pod kurzorom. Z rovnice
        //   cursorNorm = (viewportNx - 0.5) / zoom + panCenter
        // chceme: cursorNormBefore = (viewportNx - 0.5) / newZoom + newPanCenter
        // pričom viewportNx (lokálna poloha kurzora v rámci rectu) sa nemení.
        // Po roznásobení: newPanCenter = cursorNormBefore - (cursorNormBefore - oldPanCenter) * (oldZoom/newZoom).
        if (cursorValid)
        {
            float ratio = oldZoom / mapZoom;
            panCenterNormalized.x = cursorNormBefore.x - (cursorNormBefore.x - panCenterNormalized.x) * ratio;
            panCenterNormalized.y = cursorNormBefore.y - (cursorNormBefore.y - panCenterNormalized.y) * ratio;
        }

        ClampPan();
        ApplyZoomToRawImage();
        UpdateFrame();
    }

    /// <summary>
    /// Aplikuje aktuálny mapZoom a panCenterNormalized na mapRawImage.uvRect
    /// tak, aby sa zobrazil výrez textúry zodpovedajúci viditeľnej časti mapy.
    ///
    /// uvRect (x, y, w, h) v Unity RawImage znamená: w/h = šírka/výška výrezu
    /// vo zlomku celej textúry, (x, y) = ľavý-dolný roh výrezu vo zlomku.
    /// Pri zoom = 2 a paneli na strede chceme zobraziť strednú polovicu:
    /// w = h = 0.5, x = y = 0.25.
    /// </summary>
    private void ApplyZoomToRawImage()
    {
        if (mapRawImage == null) return;

        float size = 1f / mapZoom;
        float x = panCenterNormalized.x - size * 0.5f;
        float y = panCenterNormalized.y - size * 0.5f;
        mapRawImage.uvRect = new Rect(x, y, size, size);
    }

    /// <summary>
    /// Oreže panCenterNormalized tak, aby výrez nikdy nevyšiel mimo mapy
    /// [0,1]. Pri zoom = 1 sa stred MUSÍ vrátiť do (0.5, 0.5) – inak by
    /// vznikol prázdny pruh za okrajom mapy.
    /// </summary>
    private void ClampPan()
    {
        float halfSize = 0.5f / mapZoom;
        panCenterNormalized.x = Mathf.Clamp(panCenterNormalized.x, halfSize, 1f - halfSize);
        panCenterNormalized.y = Mathf.Clamp(panCenterNormalized.y, halfSize, 1f - halfSize);
    }

    /// <summary>
    /// Vypočíta svetový stred aktuálneho kamerového záberu = priemer 4
    /// rohov viewportu premietnutých na rovinu mapy.
    /// Vracia false ak premietnutie zlyhalo.
    /// </summary>
    private bool TryGetViewWorldCenter(out Vector3 center)
    {
        center = Vector3.zero;

        Camera cam = isometricCamera != null ? isometricCamera.CameraComponent : null;
        if (cam == null) return false;

        Vector2[] vp =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            if (!ProjectViewportToWorld(cam, vp[i], out Vector3 wp))
                return false;
            sum += wp;
        }

        center = sum * 0.25f;
        return true;
    }
}
