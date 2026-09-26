using UnityEngine;

/// <summary>
/// CrossingSpriteSettings
/// ─────────────────────────────────────────────────────────────────────────
/// Nastavenia vykreslenia JEDNÉHO spritu mosta / tunela. V Inspectore sedí
/// v CrossingSpriteLibrary hneď pri slote s obrázkom, takže každý sprite
/// (nástup mosta, stred mosta, výstup mosta, portál tunela …) sa ladí
/// samostatne – presne ako FactorySpriteSettings pri továrňach.
///
/// OTOČENIE V 3 OSIACH (Pitch / Yaw / Roll po 90°):
///   Nikdy dopredu nevieme, ako bude obrázok v scéne natočený (ako ho grafik
///   nakreslil, ako je nastavený import). Preto má KAŽDÝ sprite fixné
///   pootočenie okolo všetkých troch osí – rovnaký princíp ako TileYaw
///   pri RAIL/ROAD prefaboch v TileModelLibrary.
///     • Pitch – okolo osi X (rovina Z-Y)
///     • Yaw   – okolo osi Y (rovina X-Z)
///     • Roll  – okolo osi Z (rovina X-Y)
///
///   V režime Billboard sa pootočenie aplikuje v priestore KAMERY (napr.
///   Roll 90 = obrázok otočený o 90° na obrazovke). V režime WorldOriented
///   sa aplikuje vo SVETOVÝCH osiach na základnú orientáciu podľa smeru
///   mosta / normály portálu tunela.
///
/// ZRKADLENIE (flipX / flipY):
///   Izometrický obrázok nástupu a výstupu mosta býva zrkadlovým obrazom.
///   Stačí teda jeden obrázok a pri druhom konci zapnúť flipX.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[System.Serializable]
public class CrossingSpriteSettings
{
    /// <summary>Spôsob umiestnenia spritu do scény.</summary>
    public enum RenderMode
    {
        /// <summary>
        /// Sprite je vždy natočený na kameru (rovnako ako sprity tovární).
        /// Šírka = šírka izometrického kosoštvorca segmentu na obrazovke.
        /// ODPORÚČANÉ pre izometrickú 2D grafiku.
        /// </summary>
        Billboard = 0,

        /// <summary>
        /// Sprite je pevne umiestnený vo svete: pri moste leží jeho rovina
        /// pozdĺž osi mosta, pri tuneli je kolmá na smer tunela (normála
        /// portálu smeruje von z kopca). Šírka = dĺžka segmentu vo svete.
        /// </summary>
        WorldOriented = 1
    }

    [Tooltip("Len MOSTY: Billboard = natočený na kameru (ako továrne). " +
             "WorldOriented = pevne vo svete podľa smeru mosta. " +
             "Portál TUNELA toto ignoruje – vždy leží na povrchu dlaždice.")]
    public RenderMode renderMode = RenderMode.Billboard;

    [Tooltip("Fixné pootočenie okolo osi X (rovina Z-Y).")]
    public TileModelLibrary.TileYaw pitch = TileModelLibrary.TileYaw.Deg0;

    [Tooltip("Fixné pootočenie okolo osi Y (rovina X-Z).")]
    public TileModelLibrary.TileYaw yaw = TileModelLibrary.TileYaw.Deg0;

    [Tooltip("Fixné pootočenie okolo osi Z (rovina X-Y).")]
    public TileModelLibrary.TileYaw roll = TileModelLibrary.TileYaw.Deg0;

    [Tooltip("Zrkadlenie obrázka vodorovne (napr. výstup mosta = zrkadlený nástup).")]
    public bool flipX = false;

    [Tooltip("Zrkadlenie obrázka zvisle.")]
    public bool flipY = false;

    [Tooltip("Násobok šírky spritu. 1 = presne šírka segmentu (1 tile, pri krátkom " +
             "moste celá dĺžka). NULA = neviditeľný sprite (automaticky sa opraví na 1).")]
    public float widthMultiplier = 1f;

    [Tooltip("Zvislý posun spritu v TILE jednotkách (+ = hore, − = dole).")]
    public float verticalOffset = 0f;

    [Tooltip("Len WorldOriented (mosty – Start, Middle, End, Short): posun spritu " +
             "vo SVETOVÝCH osiach v TILE jednotkách. x = +X (východ), y = hore, " +
             "z = +Z (sever). Pripočíta sa po umiestnení (aj k Vertical Offset). " +
             "V režime Billboard a pri portáloch tunelov sa nepoužíva.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("Len pre Billboard: kam sa v hĺbke položí rovina spritu. " +
             "Most: odporúčané Far (vlak na moste sa kreslí pred mostom). " +
             "Portál tunela: odporúčané Near (vlak zajde ZA portál).")]
    public FactorySpriteBillboard.DepthAnchor depthAnchor = FactorySpriteBillboard.DepthAnchor.Near;

    [Tooltip("Dodatočný posun roviny spritu v hĺbke (svetové jednotky). " +
             "+ = ďalej od kamery, − = bližšie ku kamere.")]
    public float depthBias = 0f;

    [Tooltip("Doladenie poradia kreslenia (pripočíta sa k spoločnému sortingOrder).")]
    public int sortingOrderOffset = 0;

    /// <summary>Euler uhly (x = pitch, y = yaw, z = roll) v stupňoch.</summary>
    public Vector3 EulerDegrees => new Vector3((int)pitch, (int)yaw, (int)roll);

    /// <summary>Opraví nezmyselné hodnoty (nuly po pridaní nového poľa).</summary>
    public string Normalize()
    {
        string fixes = "";
        if (widthMultiplier <= 0.0001f)
        {
            fixes += $" widthMultiplier {widthMultiplier}→1;";
            widthMultiplier = 1f;
        }
        return fixes;
    }

    /// <summary>Predvolené nastavenia pre most (Billboard, rovina v zadnom rohu).</summary>
    public static CrossingSpriteSettings DefaultBridge()
        => new CrossingSpriteSettings { depthAnchor = FactorySpriteBillboard.DepthAnchor.Far };

    /// <summary>Predvolené nastavenia pre portál tunela (Billboard, rovina v prednom rohu).</summary>
    public static CrossingSpriteSettings DefaultTunnel()
        => new CrossingSpriteSettings { depthAnchor = FactorySpriteBillboard.DepthAnchor.Near };
}

/// <summary>
/// CrossingSpriteLibrary
/// ─────────────────────────────────────────────────────────────────────────
/// KNIŽNICA 2D SPRITOV pre MOSTY a TUNELY (RAIL). Analógia k TileModelLibrary
/// (sloty tovární): do slotov sa ťahá priamo importovaný obrázok (priehľadné
/// PNG, Texture Type = Sprite, odporúčané Mesh Type = Tight).
///
/// Samostatný komponent (a nie ďalšie polia v TileModelLibrary) zámerne –
/// TileModelLibrary ostáva nedotknutý, žiadna existujúca Inspector referencia
/// sa nerozbije a mosty/tunely sa dajú ladiť na jednom mieste.
///
/// TYPY MOSTOV A / B / C:
///   Každá sieť má 3 typy mostov (hráč si typ vyberá v okne
///   GameRailSelectBridgesMenuUI / GameRoadSelectBridgesMenuUI). Každý typ má
///   vlastnú, úplne rovnakú sadu slotov nižšie. Typ A používa pôvodné polia,
///   takže sprity priradené pred zavedením typov ostávajú zachované.
///   Prázdny slot typu B / C = náhradná geometria (NIE sprite typu A).
///
/// MOST = 3 obrázky na os (+ voliteľný 4.):
///   • Start  – nástup mosta (tile s MENŠOU súradnicou: západ -X / juh -Z)
///   • Middle – stred mosta, OPAKUJE sa pre každý tile medzi hlavami
///   • End    – výstup mosta (tile s VÄČŠOU súradnicou: východ +X / sever +Z)
///   • Short  – VOLITEĽNÝ jeden obrázok pre najkratší most (3 tily), ktorý sa
///              roztiahne cez celú dĺžku. Prázdne = aj 3-tilový most sa
///              zloží zo Start + 1× Middle + End.
///
///   Príklad: most dĺžky 10 = Start + 8× Middle + End.
///
/// TUNEL = 1 obrázok portálu na os + nastavenia pre každý z dvoch portálov.
///   Portál NIE JE billboard: obrázok leží NA POVRCHU šikmej dlaždice portálu
///   a roztiahne sa presne na celý tile – rovnako ako textúra Rail_Tex /
///   Road_Tex alebo sprite RailHorizontal / RailVertical z TileModelLibrary.
///   Pitch / Yaw / Roll sa zadávajú v súradniciach UŽ NAKLONENEJ dlaždice
///   (tunel je vždy na rampe): Pitch 90 položí obrázok na šikmú plochu, Yaw
///   ho otáča v rovine rampy, flipX / flipY zrkadlia. Pri tuneli sa NEPOUŽÍVA
///   Render Mode, Depth Anchor ani Depth Bias; Width Multiplier je násobok
///   roztiahnutia (1 = presne tile) a Vertical Offset posun nad povrch.
///   • Horizontálny tunel (os X): portál na západe (normála -X) a na východe (+X).
///   • Vertikálny tunel   (os Z): portál na juhu  (normála -Z) a na severe (+Z).
///   Každý portál má vlastný blok nastavení (rotácia v 3 osiach, flip …) a
///   voliteľný vlastný obrázok (prázdne = použije sa spoločný obrázok osi).
///
/// Prázdny slot = systém mostov/tunelov vykreslí jednoduchú náhradnú geometriu
/// (sivá mostovka / tmavý portál), takže hra funguje aj bez grafiky.
///
/// SIETE (RAIL / ROAD):
///   Pre železnicu a cesty sa používajú SAMOSTATNÉ komponenty tejto knižnice
///   (iné obrázky). Pole "Network" určuje, ku ktorej sieti knižnica patrí –
///   RailCrossingSystem / RoadCrossingSystem si ju podľa neho dohľadajú, ak
///   nie je priradená priamo v ich Inspectore.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class CrossingSpriteLibrary : MonoBehaviour
{
    public static CrossingSpriteLibrary instance;

    [Header("Sieť")]
    [Tooltip("Ku ktorej sieti patria sprity tejto knižnice (Rail = železnica, Road = cesty).")]
    [SerializeField] private CrossingNetwork network = CrossingNetwork.Rail;

    public CrossingNetwork Network => network;

    private static readonly System.Collections.Generic.List<CrossingSpriteLibrary> libraries =
        new System.Collections.Generic.List<CrossingSpriteLibrary>();

    /// <summary>
    /// Nájde knižnicu pre danú sieť (prvú s rovnakým Network). Null, ak v scéne
    /// žiadna nie je → systém použije náhradnú geometriu.
    /// </summary>
    public static CrossingSpriteLibrary FindFor(CrossingNetwork net)
    {
        for (int i = 0; i < libraries.Count; i++)
            if (libraries[i] != null && libraries[i].network == net) return libraries[i];

        // Knižnica mohla byť ešte neinicializovaná (Awake) – dohľadaj v scéne.
        var all = FindObjectsByType<CrossingSpriteLibrary>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].network == net) return all[i];
        return null;
    }

    /// <summary>Rola segmentu mosta.</summary>
    public enum BridgePart { Start = 0, Middle = 1, End = 2, Short = 3 }

    // =========================================================================
    // MOST TYP A – HORIZONTÁLNY (os X)
    // =========================================================================
    [Header("MOST TYP A – horizontálny (os X, zo západu na východ)")]
    [Tooltip("Nástup mosta – tile na ZÁPADNOM konci (menšie X).")]
    [SerializeField] private Sprite bridgeHorizontalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeHorizontalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeHorizontalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeHorizontalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na VÝCHODNOM konci (väčšie X).")]
    [SerializeField] private Sprite bridgeHorizontalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeHorizontalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeHorizontalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeHorizontalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // MOST – VERTIKÁLNY (os Z)
    // =========================================================================
    [Header("MOST TYP A – vertikálny (os Z, z juhu na sever)")]
    [Tooltip("Nástup mosta – tile na JUŽNOM konci (menšie Z).")]
    [SerializeField] private Sprite bridgeVerticalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeVerticalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeVerticalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeVerticalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na SEVERNOM konci (väčšie Z).")]
    [SerializeField] private Sprite bridgeVerticalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeVerticalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeVerticalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeVerticalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // MOST TYP B – HORIZONTÁLNY (os X)
    // =========================================================================
    [Header("MOST TYP B – horizontálny (os X, zo západu na východ)")]
    [Tooltip("Nástup mosta – tile na ZÁPADNOM konci (menšie X).")]
    [SerializeField] private Sprite bridgeBHorizontalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBHorizontalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeBHorizontalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBHorizontalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na VÝCHODNOM konci (väčšie X).")]
    [SerializeField] private Sprite bridgeBHorizontalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBHorizontalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeBHorizontalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBHorizontalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // MOST TYP B – VERTIKÁLNY (os Z)
    // =========================================================================
    [Header("MOST TYP B – vertikálny (os Z, z juhu na sever)")]
    [Tooltip("Nástup mosta – tile na JUŽNOM konci (menšie Z).")]
    [SerializeField] private Sprite bridgeBVerticalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBVerticalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeBVerticalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBVerticalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na SEVERNOM konci (väčšie Z).")]
    [SerializeField] private Sprite bridgeBVerticalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBVerticalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeBVerticalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeBVerticalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // MOST TYP C – HORIZONTÁLNY (os X)
    // =========================================================================
    [Header("MOST TYP C – horizontálny (os X, zo západu na východ)")]
    [Tooltip("Nástup mosta – tile na ZÁPADNOM konci (menšie X).")]
    [SerializeField] private Sprite bridgeCHorizontalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCHorizontalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeCHorizontalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCHorizontalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na VÝCHODNOM konci (väčšie X).")]
    [SerializeField] private Sprite bridgeCHorizontalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCHorizontalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeCHorizontalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCHorizontalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // MOST TYP C – VERTIKÁLNY (os Z)
    // =========================================================================
    [Header("MOST TYP C – vertikálny (os Z, z juhu na sever)")]
    [Tooltip("Nástup mosta – tile na JUŽNOM konci (menšie Z).")]
    [SerializeField] private Sprite bridgeCVerticalStartSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCVerticalStartSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Stred mosta – opakuje sa pre každý tile medzi hlavami.")]
    [SerializeField] private Sprite bridgeCVerticalMiddleSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCVerticalMiddleSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("Výstup mosta – tile na SEVERNOM konci (väčšie Z).")]
    [SerializeField] private Sprite bridgeCVerticalEndSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCVerticalEndSettings = CrossingSpriteSettings.DefaultBridge();

    [Tooltip("VOLITEĽNÉ: jeden obrázok pre najkratší most (3 tily), roztiahne sa na celú dĺžku.")]
    [SerializeField] private Sprite bridgeCVerticalShortSprite;
    [SerializeField] private CrossingSpriteSettings bridgeCVerticalShortSettings = CrossingSpriteSettings.DefaultBridge();

    // =========================================================================
    // TUNEL – HORIZONTÁLNY (os X)
    // =========================================================================
    [Header("TUNEL – horizontálny (os X)")]
    [Tooltip("Spoločný obrázok portálu pre tunely pozdĺž osi X.")]
    [SerializeField] private Sprite tunnelHorizontalPortalSprite;

    [Tooltip("VOLITEĽNÉ: vlastný obrázok ZÁPADNÉHO portálu (normála -X). Prázdne = spoločný.")]
    [SerializeField] private Sprite tunnelWestPortalOverride;
    [SerializeField] private CrossingSpriteSettings tunnelWestPortalSettings = CrossingSpriteSettings.DefaultTunnel();

    [Tooltip("VOLITEĽNÉ: vlastný obrázok VÝCHODNÉHO portálu (normála +X). Prázdne = spoločný.")]
    [SerializeField] private Sprite tunnelEastPortalOverride;
    [SerializeField] private CrossingSpriteSettings tunnelEastPortalSettings = CrossingSpriteSettings.DefaultTunnel();

    // =========================================================================
    // TUNEL – VERTIKÁLNY (os Z)
    // =========================================================================
    [Header("TUNEL – vertikálny (os Z)")]
    [Tooltip("Spoločný obrázok portálu pre tunely pozdĺž osi Z.")]
    [SerializeField] private Sprite tunnelVerticalPortalSprite;

    [Tooltip("VOLITEĽNÉ: vlastný obrázok JUŽNÉHO portálu (normála -Z). Prázdne = spoločný.")]
    [SerializeField] private Sprite tunnelSouthPortalOverride;
    [SerializeField] private CrossingSpriteSettings tunnelSouthPortalSettings = CrossingSpriteSettings.DefaultTunnel();

    [Tooltip("VOLITEĽNÉ: vlastný obrázok SEVERNÉHO portálu (normála +Z). Prázdne = spoločný.")]
    [SerializeField] private Sprite tunnelNorthPortalOverride;
    [SerializeField] private CrossingSpriteSettings tunnelNorthPortalSettings = CrossingSpriteSettings.DefaultTunnel();

    // =========================================================================

    void Awake()
    {
        if (!libraries.Contains(this)) libraries.Add(this);

        // "instance" = prvá RAIL knižnica (spätná kompatibilita). Viac knižníc
        // je v poriadku, ak patria k rôznym sieťam.
        if (network == CrossingNetwork.Rail && instance == null) instance = this;
    }

    void OnDestroy()
    {
        libraries.Remove(this);
        if (instance == this) instance = null;
    }

    /// <summary>
    /// Vráti sprite a nastavenia segmentu mosta daného TYPU (0 = A, 1 = B,
    /// 2 = C). Sprite môže byť null (slot prázdny → náhradná geometria).
    /// Nastavenia nikdy nie sú null.
    /// </summary>
    public Sprite GetBridgeSprite(int variant, CrossingAxis axis, BridgePart part, out CrossingSpriteSettings settings)
    {
        bool h = axis == CrossingAxis.Horizontal;
        switch (variant)
        {
            case 1: return BridgeSlotB(h, part, out settings);
            case 2: return BridgeSlotC(h, part, out settings);
            default: return BridgeSlotA(h, part, out settings);
        }
    }

    /// <summary>Spätná kompatibilita – typ A.</summary>
    public Sprite GetBridgeSprite(CrossingAxis axis, BridgePart part, out CrossingSpriteSettings settings)
        => GetBridgeSprite(0, axis, part, out settings);

    private Sprite BridgeSlotA(bool h, BridgePart part, out CrossingSpriteSettings settings)
    {
        switch (part)
        {
            case BridgePart.Start:
                settings = h ? bridgeHorizontalStartSettings : bridgeVerticalStartSettings;
                return Checked(h ? bridgeHorizontalStartSprite : bridgeVerticalStartSprite, ref settings, true);
            case BridgePart.End:
                settings = h ? bridgeHorizontalEndSettings : bridgeVerticalEndSettings;
                return Checked(h ? bridgeHorizontalEndSprite : bridgeVerticalEndSprite, ref settings, true);
            case BridgePart.Short:
                settings = h ? bridgeHorizontalShortSettings : bridgeVerticalShortSettings;
                return Checked(h ? bridgeHorizontalShortSprite : bridgeVerticalShortSprite, ref settings, true);
            default:
                settings = h ? bridgeHorizontalMiddleSettings : bridgeVerticalMiddleSettings;
                return Checked(h ? bridgeHorizontalMiddleSprite : bridgeVerticalMiddleSprite, ref settings, true);
        }
    }

    private Sprite BridgeSlotB(bool h, BridgePart part, out CrossingSpriteSettings settings)
    {
        switch (part)
        {
            case BridgePart.Start:
                settings = h ? bridgeBHorizontalStartSettings : bridgeBVerticalStartSettings;
                return Checked(h ? bridgeBHorizontalStartSprite : bridgeBVerticalStartSprite, ref settings, true);
            case BridgePart.End:
                settings = h ? bridgeBHorizontalEndSettings : bridgeBVerticalEndSettings;
                return Checked(h ? bridgeBHorizontalEndSprite : bridgeBVerticalEndSprite, ref settings, true);
            case BridgePart.Short:
                settings = h ? bridgeBHorizontalShortSettings : bridgeBVerticalShortSettings;
                return Checked(h ? bridgeBHorizontalShortSprite : bridgeBVerticalShortSprite, ref settings, true);
            default:
                settings = h ? bridgeBHorizontalMiddleSettings : bridgeBVerticalMiddleSettings;
                return Checked(h ? bridgeBHorizontalMiddleSprite : bridgeBVerticalMiddleSprite, ref settings, true);
        }
    }

    private Sprite BridgeSlotC(bool h, BridgePart part, out CrossingSpriteSettings settings)
    {
        switch (part)
        {
            case BridgePart.Start:
                settings = h ? bridgeCHorizontalStartSettings : bridgeCVerticalStartSettings;
                return Checked(h ? bridgeCHorizontalStartSprite : bridgeCVerticalStartSprite, ref settings, true);
            case BridgePart.End:
                settings = h ? bridgeCHorizontalEndSettings : bridgeCVerticalEndSettings;
                return Checked(h ? bridgeCHorizontalEndSprite : bridgeCVerticalEndSprite, ref settings, true);
            case BridgePart.Short:
                settings = h ? bridgeCHorizontalShortSettings : bridgeCVerticalShortSettings;
                return Checked(h ? bridgeCHorizontalShortSprite : bridgeCVerticalShortSprite, ref settings, true);
            default:
                settings = h ? bridgeCHorizontalMiddleSettings : bridgeCVerticalMiddleSettings;
                return Checked(h ? bridgeCHorizontalMiddleSprite : bridgeCVerticalMiddleSprite, ref settings, true);
        }
    }

    /// <summary>
    /// Vráti sprite a nastavenia portálu tunela.
    /// isEndPortal = false → portál s menšou súradnicou (západ / juh),
    /// isEndPortal = true  → portál s väčšou súradnicou (východ / sever).
    /// </summary>
    public Sprite GetTunnelPortalSprite(CrossingAxis axis, bool isEndPortal, out CrossingSpriteSettings settings)
    {
        if (axis == CrossingAxis.Horizontal)
        {
            settings = isEndPortal ? tunnelEastPortalSettings : tunnelWestPortalSettings;
            Sprite ov = isEndPortal ? tunnelEastPortalOverride : tunnelWestPortalOverride;
            return Checked(ov != null ? ov : tunnelHorizontalPortalSprite, ref settings, false);
        }

        settings = isEndPortal ? tunnelNorthPortalSettings : tunnelSouthPortalSettings;
        Sprite ovV = isEndPortal ? tunnelNorthPortalOverride : tunnelSouthPortalOverride;
        return Checked(ovV != null ? ovV : tunnelVerticalPortalSprite, ref settings, false);
    }

    private static Sprite Checked(Sprite sprite, ref CrossingSpriteSettings settings, bool bridge)
    {
        if (settings == null)
            settings = bridge ? CrossingSpriteSettings.DefaultBridge() : CrossingSpriteSettings.DefaultTunnel();
        return sprite;
    }

    void OnValidate()
    {
        Norm(bridgeHorizontalStartSettings); Norm(bridgeHorizontalMiddleSettings);
        Norm(bridgeHorizontalEndSettings); Norm(bridgeHorizontalShortSettings);
        Norm(bridgeVerticalStartSettings); Norm(bridgeVerticalMiddleSettings);
        Norm(bridgeVerticalEndSettings); Norm(bridgeVerticalShortSettings);
        Norm(bridgeBHorizontalStartSettings); Norm(bridgeBHorizontalMiddleSettings);
        Norm(bridgeBHorizontalEndSettings); Norm(bridgeBHorizontalShortSettings);
        Norm(bridgeBVerticalStartSettings); Norm(bridgeBVerticalMiddleSettings);
        Norm(bridgeBVerticalEndSettings); Norm(bridgeBVerticalShortSettings);
        Norm(bridgeCHorizontalStartSettings); Norm(bridgeCHorizontalMiddleSettings);
        Norm(bridgeCHorizontalEndSettings); Norm(bridgeCHorizontalShortSettings);
        Norm(bridgeCVerticalStartSettings); Norm(bridgeCVerticalMiddleSettings);
        Norm(bridgeCVerticalEndSettings); Norm(bridgeCVerticalShortSettings);
        Norm(tunnelWestPortalSettings); Norm(tunnelEastPortalSettings);
        Norm(tunnelSouthPortalSettings); Norm(tunnelNorthPortalSettings);
    }

    private static void Norm(CrossingSpriteSettings s) { if (s != null) s.Normalize(); }
}
