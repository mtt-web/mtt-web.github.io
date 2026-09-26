using System;
using UnityEngine;

/// <summary>
/// Izometrická kamera pre dopravný simulátor (Transport Tycoon štýl).
///
/// FUNKCIONALITA:
///  - Pohyb kamery LEN cez šípky (Hore/Dole/Doľava/Doprava).
///  - Pohyb kamery aj cez myš: podržané PRAVÉ tlačidlo myši + ťah.
///  - Zoom v 3 PEVNÝCH (skokových) úrovniach – ovláda sa cez UI tlačidlá
///    "+" a "-" (GameMenuUI). Koliesko myši zoom NEovláda.
///  - Pohyb je obmedzený na obdĺžnikové hranice tile mapy.
///  - PLYNULÝ pohyb: šípky sa rozbiehajú a dobiehajú jemne (zrýchlenie /
///    spomalenie), ťah myšou je vyhladený a po pustení tlačidla kamera
///    ešte krátko dokĺže (zotrvačnosť). Dá sa vypnúť v Inspectore.
///
/// Uhol pohľadu (Rotation 30/45/0, Orthographic) sa nastavuje na Main Camera
/// v Inspectore – tento skript ho nemení, len ho rešpektuje.
/// </summary>
public class IsometricCamera : MonoBehaviour
{
    // =====================================================================
    // POHYB
    // =====================================================================

    [Header("Pohyb kamery")]
    [Tooltip("Rýchlosť posunu kamery cez šípky (jednotky za sekundu).")]
    [SerializeField] private float panSpeed = 15.0f;

    [Tooltip("Citlivosť posunu kamery pri ťahaní pravým tlačidlom myši.")]
    [SerializeField] private float mousePanSensitivity = 0.5f;

    // =====================================================================
    // PLYNULOSŤ POHYBU
    // =====================================================================

    [Header("Plynulosť pohybu")]
    [Tooltip("Zapnuté = jemný rozbeh/dobeh šípok, vyhladený ťah myšou a zotrvačnosť. " +
             "Vypnuté = pôvodný okamžitý (lineárny) pohyb.")]
    [SerializeField] private bool smoothMovement = true;

    [Tooltip("Šípky: za aký čas (s) sa kamera rozbehne na plnú rýchlosť. " +
             "Väčšie = jemnejší štart.")]
    [Range(0.01f, 1.5f)][SerializeField] private float keyAccelerationTime = 0.35f;

    [Tooltip("Šípky: za aký čas (s) kamera po pustení klávesu zastaví. " +
             "Väčšie = dlhší dobeh.")]
    [Range(0.01f, 1.5f)][SerializeField] private float keyDecelerationTime = 0.25f;

    [Tooltip("Myš: vyhladenie ťahu (s). Kamera nasleduje myš s týmto oneskorením – " +
             "pohyb je mäkší, bez trhania. 0 = bez vyhladenia.")]
    [Range(0f, 0.5f)][SerializeField] private float mouseSmoothTime = 0.08f;

    [Tooltip("Myš: po pustení pravého tlačidla kamera ešte dokĺže v smere ťahu.")]
    [SerializeField] private bool mouseInertia = true;

    [Tooltip("Myš: ako rýchlo zotrvačnosť zanikne (väčšie = kratšie dokĺzanie).")]
    [Range(0.5f, 20f)][SerializeField] private float mouseInertiaDamping = 6f;

    [Tooltip("Myš: maximálna rýchlosť zotrvačnosti (jednotky obrazovky za sekundu).")]
    [SerializeField] private float mouseInertiaMaxSpeed = 60f;

    // =====================================================================
    // ZOOM – 3 PEVNÉ ÚROVNE
    // =====================================================================

    [Header("Zoom úrovne (orthographicSize)")]
    [Tooltip("Úroveň 0 – najbližšie pri tile mape.")]
    [SerializeField] private float zoomLevelNear = 8.0f;

    [Tooltip("Úroveň 1 – stredná vzdialenosť.")]
    [SerializeField] private float zoomLevelMid = 10.0f;

    [Tooltip("Úroveň 2 – najvzdialenejšie.")]
    [SerializeField] private float zoomLevelFar = 12.0f;

    [Tooltip("Index zoom úrovne, s ktorou hra začína (0 = najbližšie).")]
    [SerializeField] private int startZoomIndex = 0;

    // =====================================================================
    // HRANICE MAPY
    //
    // Pohyb kamery je obmedzený na obdĺžnik [minX,maxX] × [minZ,maxZ]
    // (svetové súradnice X/Z). Hodnoty nastav v Inspectore podľa skutočnej
    // veľkosti tile mapy – ide o pozíciu PIVOTU kamery (tohto GameObjectu),
    // nie o premietnutý roh záberu. Predvolené hodnoty sú len placeholder.
    // =====================================================================

    [Header("Hranice mapy (svetové X/Z pivotu kamery)")]
    [SerializeField] private float minX = -50.0f;
    [SerializeField] private float maxX = 50.0f;
    [SerializeField] private float minZ = -50.0f;
    [SerializeField] private float maxZ = 50.0f;

    // =====================================================================
    // INTERNÝ STAV
    // =====================================================================

    /// <summary>
    /// Aktuálna hodnota zoomu (orthographicSize) – zodpovedá jednej z troch
    /// úrovní. Verejne čitateľné, aby UI vedelo zobraziť aktuálny stav.
    /// </summary>
    public float CurrentZoom { get; private set; }

    /// <summary>Index aktuálnej zoom úrovne: 0 = near, 1 = mid, 2 = far.</summary>
    public int CurrentZoomIndex { get; private set; }

    /// <summary>Počet zoom úrovní (pre UI – hranice tlačidiel +/-).</summary>
    public int ZoomLevelCount => 3;

    // =====================================================================
    // UDALOSŤ ZMENY ZOOMU
    // ---------------------------------------------------------------------
    // Vyvolá sa VŽDY, keď sa aplikuje zoom úroveň (aj pri prvotnej
    // inicializácii v Start()). Parameter = nový CurrentZoomIndex.
    //
    // Používa ju napr. CloudLayerController, ktorý podľa zoom úrovne
    // zapína/vypína vrstvu mrakov a hmly. Ide o čisto additívne rozšírenie –
    // existujúce správanie kamery sa nemení.
    // =====================================================================

    /// <summary>Vyvolané po každej zmene zoom úrovne. Parameter = index úrovne.</summary>
    public event Action<int> OnZoomLevelChanged;

    /// <summary>Je kamera na NAJVIAC ODDIALENEJ úrovni (index 2 = far)?</summary>
    public bool IsAtFarthestZoom => CurrentZoomIndex >= ZoomLevelCount - 1;

    /// <summary>orthographicSize pre zadaný index úrovne (bez jej aplikovania).</summary>
    public float GetZoomSizeForIndex(int index)
    {
        switch (index)
        {
            case 0: return zoomLevelNear;
            case 1: return zoomLevelMid;
            case 2: return zoomLevelFar;
            default: return zoomLevelNear;
        }
    }

    private Camera cam;

    // Plynulý pohyb – všetko v "obrazovkových" jednotkách (h = doprava,
    // v = hore po obraze), prepočet na svet robí MoveCamera.
    private Vector2 keyVelocity;        // aktuálna rýchlosť zo šípok (j/s)
    private Vector2 keySmoothRef;       // pomocná premenná pre SmoothDamp
    private Vector2 mousePending;       // zvyšok ťahu myšou, ktorý kamera ešte "dobehne"
    private Vector2 mouseVelocity;      // odhad rýchlosti ťahu (pre zotrvačnosť)
    private Vector2 inertiaVelocity;    // zotrvačnosť po pustení tlačidla (j/s)
    private bool wasMouseDragging;

    private void Awake()
    {
        // Skript je umiestnený priamo na Main Camera, preto GetComponent.
        // GetComponentInChildren ponechané ako fallback pre prípad, že by
        // bola kamera dieťaťom rig objektu.
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = GetComponentInChildren<Camera>();
    }

    private void Start()
    {
        // Inicializácia zoomu na štartovú úroveň – bez tohto by orthographicSize
        // ostal na hodnote z Inspectoru a CurrentZoom by bol 0.
        CurrentZoomIndex = Mathf.Clamp(startZoomIndex, 0, ZoomLevelCount - 1);
        ApplyZoom(CurrentZoomIndex);
    }

    private void Update()
    {
        HandleKeyboardPan();
        HandleMousePan();
        ClampPositionToBounds();
    }

    // =====================================================================
    // POHYB – KLÁVESNICA (LEN ŠÍPKY)
    // =====================================================================

    private void HandleKeyboardPan()
    {
        if (smoothMovement)
        {
            HandleKeyboardPanSmooth();
            return;
        }

        // Čítame priamo šípky – NIE Input.GetAxis("Horizontal"/"Vertical"),
        // pretože tie sú v predvolenom Input Manageri namapované aj na W/A/S/D.
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) v += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) v -= 1f;

        if (h == 0f && v == 0f) return;

        MoveCamera(h, v, panSpeed * Time.deltaTime);
    }

    // =====================================================================
    // POHYB – MYŠ (PRAVÉ TLAČIDLO + ŤAH)
    // =====================================================================

    private void HandleMousePan()
    {
        if (smoothMovement)
        {
            HandleMousePanSmooth();
            return;
        }

        // Pravé tlačidlo myši = button index 1. Pohyb sa vykonáva len kým
        // je tlačidlo držané.
        if (!Input.GetMouseButton(1)) return;

        // BLOKOVANIE PRI MINIMAPE:
        // Ak používateľ stlačil pravé tlačidlo priamo nad oknom Status Map
        // (nad MapViewportIndicator), ten si pravý drag rezervuje pre pan
        // mapového podkladu. V takom prípade NESMIE pravé tlačidlo zároveň
        // hýbať aj hernou kamerou – inak by sa hýbalo oboje naraz.
        //
        // Kontrola sa robí cez statický flag, ktorý MapViewportIndicator
        // nastaví v OnPointerDown (pravé tlačidlo nad minimapou) a vynuluje
        // v OnPointerUp. Ak hráč stlačil pravé tlačidlo MIMO minimapy,
        // flag ostane false a kamera sa hýbe normálne – presne podľa
        // požiadavky používateľa.
        if (MapViewportIndicator.IsRightDraggingMinimap) return;

        // Input.GetAxis("Mouse X"/"Mouse Y") vracia delta pohybu myši za frame.
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (mouseX == 0f && mouseY == 0f) return;

        // Ťahanie "uchopí" mapu – pohyb kamery je v opačnom smere ťahu,
        // aby sa scéna pohybovala s myšou (horizontálne aj vertikálne voči
        // obrazovke). Ak preferuješ pohyb v smere ťahu, odstráň znamienka mínus.
        MoveCamera(-mouseX, -mouseY, mousePanSensitivity);
    }

    // =====================================================================
    // PLYNULÝ POHYB – ŠÍPKY
    // ---------------------------------------------------------------------
    // Namiesto okamžitej plnej rýchlosti má kamera RÝCHLOSŤ, ktorá sa ku
    // cieľovej (šípky × panSpeed) približuje cez SmoothDamp – to dáva mäkký
    // štart (ease-in) aj mäkké zastavenie (ease-out). Čas rozbehu a dobehu
    // je nastaviteľný zvlášť. Beží v reálnom čase (unscaledDeltaTime), takže
    // kamera sa hýbe rovnako aj pri pauze či zrýchlenom čase hry.
    // =====================================================================

    private void HandleKeyboardPanSmooth()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
        if (Input.GetKey(KeyCode.UpArrow)) v += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) v -= 1f;

        Vector2 target = new Vector2(h, v) * panSpeed;
        bool pressing = (h != 0f || v != 0f);

        // Šípky prerušia prípadnú zotrvačnosť po myši.
        if (pressing) inertiaVelocity = Vector2.zero;

        if (!pressing && keyVelocity.sqrMagnitude < 1e-4f)
        {
            keyVelocity = Vector2.zero;
            keySmoothRef = Vector2.zero;
            return;
        }

        float smoothTime = pressing ? keyAccelerationTime : keyDecelerationTime;
        keyVelocity = Vector2.SmoothDamp(keyVelocity, target, ref keySmoothRef,
                                         Mathf.Max(0.01f, smoothTime), Mathf.Infinity, dt);

        MoveCamera(keyVelocity.x, keyVelocity.y, dt);
    }

    // =====================================================================
    // PLYNULÝ POHYB – MYŠ
    // ---------------------------------------------------------------------
    //  • Ťah sa neaplikuje hneď celý: pripočíta sa k "dlhu" (mousePending)
    //    a kamera ho dobieha exponenciálne s časom mouseSmoothTime → mäkký
    //    rozbeh aj zastavenie, žiadne trhanie pri nerovnomernom pohybe myši.
    //    Celková prejdená vzdialenosť ostáva rovnaká ako pri pôvodnom ťahu.
    //  • Počas ťahu sa odhaduje rýchlosť. Po pustení tlačidla ňou kamera
    //    ešte chvíľu dokĺže a plynulo zastaví (zotrvačnosť).
    //  • Nové stlačenie tlačidla, šípky alebo presun z minimapy zotrvačnosť
    //    okamžite zrušia.
    // =====================================================================

    private void HandleMousePanSmooth()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        bool dragging = Input.GetMouseButton(1) && !MapViewportIndicator.IsRightDraggingMinimap;

        if (dragging)
        {
            if (!wasMouseDragging)
            {
                // Nové uchopenie mapy – zastav doznievajúci pohyb.
                inertiaVelocity = Vector2.zero;
                mouseVelocity = Vector2.zero;
            }

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            mousePending += new Vector2(-mouseX, -mouseY) * mousePanSensitivity;
        }
        else if (wasMouseDragging && mouseInertia)
        {
            // Pustenie tlačidla – rýchlosť ťahu prejde do zotrvačnosti.
            inertiaVelocity = Vector2.ClampMagnitude(mouseVelocity, mouseInertiaMaxSpeed);
        }
        else if (MapViewportIndicator.IsRightDraggingMinimap)
        {
            StopMotion();
        }
        wasMouseDragging = dragging;

        // 1) Dobiehanie ťahu (vyhladenie)
        Vector2 step = Vector2.zero;
        if (mousePending.sqrMagnitude > 0f)
        {
            float k = (mouseSmoothTime <= 0.0001f) ? 1f : 1f - Mathf.Exp(-dt / mouseSmoothTime);
            step = mousePending * k;
            mousePending -= step;
            if (mousePending.sqrMagnitude < 1e-8f) mousePending = Vector2.zero;
        }

        // Odhad rýchlosti ťahu (vyhladený, aby jeden trhnutý snímok nerozhodol)
        if (dragging)
            mouseVelocity = Vector2.Lerp(mouseVelocity, step / dt, 1f - Mathf.Exp(-dt / 0.05f));

        // 2) Zotrvačnosť po pustení
        if (!dragging && inertiaVelocity.sqrMagnitude > 0f)
        {
            step += inertiaVelocity * dt;
            inertiaVelocity *= Mathf.Exp(-mouseInertiaDamping * dt);
            if (inertiaVelocity.sqrMagnitude < 0.01f) inertiaVelocity = Vector2.zero;
        }

        if (step.sqrMagnitude > 0f)
            MoveCamera(step.x, step.y, 1f);
    }

    /// <summary>
    /// Okamžite zastaví všetok doznievajúci pohyb (šípky, ťah myšou,
    /// zotrvačnosť). Volá sa pri presune kamery z kódu (minimapa, vycentrovanie
    /// na tile), aby kamera po presune "neodplávala".
    /// </summary>
    public void StopMotion()
    {
        keyVelocity = Vector2.zero;
        keySmoothRef = Vector2.zero;
        mousePending = Vector2.zero;
        mouseVelocity = Vector2.zero;
        inertiaVelocity = Vector2.zero;
    }

    /// <summary>
    /// Spoločná pohybová logika pre klávesnicu aj myš.
    ///
    /// Pohyb je relatívny voči OBRAZOVKE, nie voči osiam tile mapy:
    ///   - horizontal = posun doľava/doprava po obraze,
    ///   - vertical   = posun hore/dole po obraze.
    ///
    /// DÔLEŽITÉ: smery sa odvodzujú z osí KAMERY (cam.transform), nie z
    /// transform tohto skriptu. Ak je IsometricCamera skript na rodičovskom
    /// rig objekte (rotácia 0,0,0), jeho transform.up by bol (0,1,0) a po
    /// sploštení do roviny XZ by vyšiel nulový vektor – preto by pohyb
    /// hore/dole nefungoval. Osi kamery sú vždy správne natočené.
    ///
    ///   - "doprava po obraze" = cam.right sploštené do roviny XZ,
    ///   - "hore po obraze"    = cam.forward sploštené do roviny XZ
    ///     (forward = kam sa kamera pozerá; jeho priemet na mapu je
    ///      presne smer "do diaľky", t.j. zvislo hore po obrazovke).
    /// </summary>
    private void MoveCamera(float horizontal, float vertical, float step)
    {
        Transform camT = (cam != null) ? cam.transform : transform;

        // "Doprava po obraze" – right kamery, sploštené do roviny XZ.
        Vector3 screenRight = camT.right;
        screenRight.y = 0f;
        screenRight.Normalize();

        // "Hore po obraze" – forward kamery, sploštené do roviny XZ.
        // forward je naklonený dole o 30°, ale jeho priemet do roviny mapy
        // je nenulový a udáva smer "do hĺbky scény" = zvisle hore na obraze.
        Vector3 screenUp = camT.forward;
        screenUp.y = 0f;
        screenUp.Normalize();

        Vector3 direction = screenRight * horizontal + screenUp * vertical;

        // ZOOM-KOMPENZÁCIA RÝCHLOSTI POHYBU:
        // Pohyb sa počíta v SVETOVÝCH jednotkách, ktoré sú pri danom kroku
        // konštantné. Koľko PIXELOV na obrazovke však predstavuje jedna svetová
        // jednotka, závisí od orthographicSize (zoomu): pri oddialení (väčší
        // orthographicSize) je rovnaký svetový posun menšou časťou obrazu, takže
        // panning vyzerá pomalšie. Preto pri zoomLevelMid/Far cítiť spomalenie.
        //
        // Aby bol vizuálny (obrazovkový) pocit z pohybu rovnaký vo všetkých
        // zoom úrovniach, škálujeme krok pomerom voči REFERENČNEJ úrovni
        // zoomLevelNear (ktorá je označená ako východisková „správna"):
        //
        //   zoomCompensation = CurrentZoom / zoomLevelNear
        //     - near: 4  / 4  = 1  → bez zmeny (zachová súčasné správne správanie),
        //     - mid:  8  / 4  = 2  → 2× rýchlejší svetový posun,
        //     - far:  12 / 4  = 3  → 3× rýchlejší svetový posun.
        //
        // Matematicky sa orthographicSize v prepočte na pixely/s presne vyruší,
        // takže rýchlosť posunu po obrazovke je vo všetkých úrovniach identická.
        // Platí pre šípky aj myš, lebo oba prechádzajú touto metódou.
        float zoomCompensation = (zoomLevelNear > 0f) ? (CurrentZoom / zoomLevelNear) : 1f;

        transform.position += direction * step * zoomCompensation;
    }

    // =====================================================================
    // ZOOM – PEVNÉ SKOKOVÉ ÚROVNE
    // =====================================================================

    /// <summary>
    /// Priblíženie o jednu úroveň (smerom k near). Volané z UI tlačidla "+".
    /// Vracia true ak sa zoom zmenil.
    /// </summary>
    public bool ZoomIn()
    {
        if (CurrentZoomIndex <= 0) return false;
        CurrentZoomIndex--;
        ApplyZoom(CurrentZoomIndex);
        return true;
    }

    /// <summary>
    /// Oddialenie o jednu úroveň (smerom k far). Volané z UI tlačidla "-".
    /// Vracia true ak sa zoom zmenil.
    /// </summary>
    public bool ZoomOut()
    {
        if (CurrentZoomIndex >= ZoomLevelCount - 1) return false;
        CurrentZoomIndex++;
        ApplyZoom(CurrentZoomIndex);
        return true;
    }

    /// <summary>Dá sa ešte priblížiť? (pre enabled stav tlačidla "+")</summary>
    public bool CanZoomIn() => CurrentZoomIndex > 0;

    /// <summary>Dá sa ešte oddialiť? (pre enabled stav tlačidla "-")</summary>
    public bool CanZoomOut() => CurrentZoomIndex < ZoomLevelCount - 1;

    /// <summary>
    /// Aplikuje zoom úroveň okamžite (skokovo – bez plynulého prechodu).
    /// </summary>
    private void ApplyZoom(int index)
    {
        switch (index)
        {
            case 0: CurrentZoom = zoomLevelNear; break;
            case 1: CurrentZoom = zoomLevelMid; break;
            case 2: CurrentZoom = zoomLevelFar; break;
            default: CurrentZoom = zoomLevelNear; break;
        }

        if (cam != null)
            cam.orthographicSize = CurrentZoom;

        // Notifikácia pre odberateľov (vrstva mrakov, prípadné budúce systémy).
        // Try/catch nie je potrebný – odberatelia sú interné herné skripty.
        OnZoomLevelChanged?.Invoke(CurrentZoomIndex);
    }

    // =====================================================================
    // OBMEDZENIE POHYBU NA HRANICE MAPY
    // =====================================================================

    /// <summary>
    /// Oreže pozíciu kamery do obdĺžnika [minX,maxX] × [minZ,maxZ].
    /// Vďaka tomu kamera "kopíruje" obvod tile mapy a nedá sa odísť mimo nej.
    /// </summary>
    private void ClampPositionToBounds()
    {
        Vector3 p = transform.position;
        p.x = Mathf.Clamp(p.x, minX, maxX);
        p.z = Mathf.Clamp(p.z, minZ, maxZ);
        transform.position = p;
    }

    // =====================================================================
    // VEREJNÉ API PRE MINIMAPU (StatusMapMenuUI / MapViewportIndicator)
    // ─────────────────────────────────────────────────────────────────────
    // Tieto metódy umožňujú minimape (a) zistiť, kam sa kamera pozerá, a
    // (b) presunúť kameru pri drag-and-drop viewport rámika. Nemenia žiadne
    // existujúce správanie – len sprístupňujú už existujúci stav.
    // =====================================================================

    /// <summary>
    /// Skutočný Camera komponent tejto izometrickej kamery. Minimapa ho
    /// potrebuje na premietnutie rohov záberu (frusta) do roviny mapy cez
    /// ViewportPointToRay. Môže byť null, ak sa kamera nenašla.
    /// </summary>
    public Camera CameraComponent => cam;

    /// <summary>
    /// Aktuálna svetová pozícia PIVOTU kamery (X/Z; Y je nepodstatné pre
    /// pohyb po mape). Minimapa ju používa na výpočet umiestnenia viewport
    /// rámika.
    /// </summary>
    public Vector3 GetCameraWorldPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// Nastaví svetovú pozíciu pivotu kamery na zadané X/Z. Y zostáva
    /// nezmenené (výška kamery sa pri pohybe po mape nemení). Pozícia sa
    /// hneď oreže na hranice mapy (ClampPositionToBounds), takže minimapa
    /// nemôže kameru "vytlačiť" mimo dovolený obdĺžnik.
    ///
    /// Volá MapViewportIndicator počas drag-and-drop viewport rámika.
    /// </summary>
    public void SetCameraWorldPositionXZ(float worldX, float worldZ)
    {
        // Presun z kódu (minimapa, vycentrovanie) – zruš doznievajúci pohyb.
        StopMotion();

        Vector3 p = transform.position;
        p.x = worldX;
        p.z = worldZ;
        transform.position = p;

        // Okamžité orezanie na hranice – rovnaká logika ako v Update().
        ClampPositionToBounds();
    }

    /// <summary>
    /// Hranice pohybu pivotu kamery (svetové X/Z), tak ako sú nastavené v
    /// Inspectore. Minimapa ich môže použiť, ak by chcela mapovať pohyb
    /// rámika presne na rozsah, v ktorom sa kamera vie hýbať.
    /// out parametre: minimálne a maximálne X a Z.
    /// </summary>
    public void GetMovementBounds(out float outMinX, out float outMaxX,
                                  out float outMinZ, out float outMaxZ)
    {
        outMinX = minX;
        outMaxX = maxX;
        outMinZ = minZ;
        outMaxZ = maxZ;
    }

    // =====================================================================
    // VYCENTROVANIE NA TILE / SVETOVÝ BOD (pre Status UI okná – klik na riadok)
    // ─────────────────────────────────────────────────────────────────────
    // Premiestni kameru tak, aby zadaný tile (resp. svetový bod) skončil
    // približne v STREDE záberu. Pohyb sa hneď oreže na hranice mapy
    // (SetCameraWorldPositionXZ → ClampPositionToBounds), takže pri okraji
    // mapy bude tile vycentrovaný len "nakoľko sa dá".
    // =====================================================================

    /// <summary>
    /// Vycentruje kameru na STRED tile [tileX, tileZ]. Výška tile sa odčíta
    /// z terénu (TerrainManager), aby pri naklonenej ortho kamere sedel stred
    /// presne (výška ovplyvňuje priemet bodu do stredu obrazu).
    /// </summary>
    public void CenterOnTile(int tileX, int tileZ)
    {
        float surfaceY = SampleTerrainHeight(tileX, tileZ);
        Vector3 worldPoint = new Vector3(tileX + 0.5f, surfaceY, tileZ + 0.5f);
        CenterOnWorldPoint(worldPoint);
    }

    /// <summary>
    /// Vycentruje kameru tak, aby zadaný svetový bod ležal na centrálnej osi
    /// pohľadu (a teda v strede ortografického záberu).
    ///
    /// MATEMATIKA (ortho kamera): bod P je v strede záberu práve vtedy, keď
    /// (P - oko) je rovnobežné s cam.forward. Y oka necháme nezmenené, takže
    /// dopočítame parameter t pozdĺž forward, ktorý dosiahne výšku P, a z neho
    /// X/Z oka. Ak je skript na rodičovskom rig objekte (kamera je dieťa),
    /// zohľadní sa konštantný X/Z offset.
    /// </summary>
    public void CenterOnWorldPoint(Vector3 worldPoint)
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = GetComponentInChildren<Camera>();
        }

        Transform camT = (cam != null) ? cam.transform : transform;

        Vector3 fwd = camT.forward;
        if (Mathf.Abs(fwd.y) < 1e-4f) fwd.y = -1f; // poistka proti deleniu ~0

        float camEyeY = camT.position.y;
        float t = (worldPoint.y - camEyeY) / fwd.y;

        float desiredEyeX = worldPoint.x - t * fwd.x;
        float desiredEyeZ = worldPoint.z - t * fwd.z;

        // Ak transform tohto skriptu nie je priamo kamera, zachovaj X/Z offset
        // medzi rig pivotom a kamerou (pri skripte na kamere je offset 0).
        Vector3 offset = transform.position - camT.position;

        SetCameraWorldPositionXZ(desiredEyeX + offset.x, desiredEyeZ + offset.z);
    }

    /// <summary>
    /// Priemerná výška terénu cez 4 rohové vertexy tile [x,z] z TerrainManager.
    /// Ak terén nie je dostupný, vráti 0.
    /// </summary>
    private float SampleTerrainHeight(int x, int z)
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null || tm.terrainWidth <= 0)
            return 0f;

        int w = tm.terrainWidth + 1;

        float Y(int vx, int vz)
        {
            vx = Mathf.Clamp(vx, 0, w - 1);
            vz = Mathf.Clamp(vz, 0, w - 1);
            return tm.coordsF[vz * w + vx].y;
        }

        return (Y(x, z) + Y(x, z + 1) + Y(x + 1, z + 1) + Y(x + 1, z)) * 0.25f;
    }
}