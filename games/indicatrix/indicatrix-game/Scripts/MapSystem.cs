using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MapSystem
/// ─────────────────────────────────────────────────────────────────────────
/// Generátor mapového podkladu (prehľadová "status mapa") pre okno
/// StatusMapMenuUI. Renderuje obsah hernej mapy do <see cref="Texture2D"/>,
/// ktorú UI zobrazí v RawImage komponente (MapRawImage).
///
/// ZDROJE DÁT:
///   • TerrainManager.instance.coordsF   – výškové dáta terénu (Y-súradnica
///     každého vrcholu mriežky). Mapa má (terrainWidth × terrainWidth) tilov,
///     vrcholová mriežka má (terrainWidth+1)² bodov.
///   • IndicatrixAPI.instance            – tile grid: tileID 1=trať/cesta,
///     2=stanica, 3=depo, 4=Factory, 5=Processing; kategória rozlišuje
///     Rail / Road / Factory.
///   • Resources/Textures/...            – tie isté textúry, ktoré IndicatrixAPI
///     používa na vykreslenie tilov v hre. MapSystem si ich načíta sám a
///     z každej spočíta PRIEMERNÚ FARBU, takže mapa zobrazí objekty vo
///     farbách reálnych herných textúr (nie vo vymyslenej palete).
///
/// TYPY MÁP (MapViewType):
///   • TerrainView      – celkový terén; výškové prevýšenia ako gradient
///                        zelenej (najnižšie = sýta tmavozelená, najvyššie =
///                        svetlá). VODA (úroveň terénu E &lt; 0, t.j. -1 a nižšie)
///                        sa kreslí explicitnou modrou (WaterColor), nie zelenou.
///                        MESTÁ (tily obsadené budovami, CityManager.IsCityTile)
///                        sa kreslia farbou CityColor. Navrch sa vykreslia všetky
///                        objekty (trate, cesty, stanice, depá, továrne). Trate a
///                        cesty vo farbách svojich textúr; TOVÁRNE rovnako
///                        ako v IndustryView – explicitnou mapovou farbou
///                        FactoryDefinition.MapColor (herná textúra sa nemení).
///                        Farby vody a miest sú zmeniteľné z kódu (WaterColor /
///                        CityColor) – nie cez Inspector.
///   • RailNetworkView  – výšky ignorované, jednoliata zelená; vykreslia sa
///                        len ŽELEZNIČNÉ prvky (Rail trať/stanica/depo).
///   • RoadNetView      – výšky ignorované, jednoliata zelená; vykreslia sa
///                        len CESTNÉ prvky (Road trať/stanica/depo).
///   • IndustryView     – výšky ignorované, jednoliata zelená; vykreslia sa
///                        len TOVÁRNE (Factory / Processing). Na rozdiel od
///                        ostatných view sa farba KAŽDEJ továrne neberie z
///                        hernej textúry, ale z explicitnej mapovej farby
///                        FactoryDefinition.MapColor (každý typ továrne má
///                        vlastnú farbu). Herná textúra ani vzhľad továrne
///                        v hre sa tým NEMENÍ – farba je len pre túto mapu.
///
/// POUŽITIE:
///   Texture2D tex = MapSystem.RenderMap(MapSystem.MapViewType.TerrainView);
///   mapRawImage.texture = tex;
///
/// Trieda je statická – nie je to MonoBehaviour. Drží len jednu vec: cache
/// priemerných farieb textúr (TextureColorCache), aby sa textúry nenačítavali
/// a nepriemerovali pri každom prepnutí mapy nanovo.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public static class MapSystem
{
    // =====================================================================
    // TYP MAPY
    // =====================================================================

    public enum MapViewType
    {
        TerrainView,
        RailNetworkView,
        RoadNetView,
        IndustryView
    }

    // =====================================================================
    // FAREBNÁ PALETA – LEN PODKLAD
    // =====================================================================
    // POZN.: Farby OBJEKTOV (trate, stanice, depá, továrne) sa už NEberú
    // odtiaľto – počítajú sa z reálnych herných textúr (pozri sekciu
    // "FARBY Z HERNÝCH TEXTÚR" nižšie). Tu zostáva len podklad mapy.

    // TerrainView gradient – najnižší terén (sýta tmavozelená) … najvyšší
    // (svetlá zelená). Lineárna interpolácia podľa normalizovanej výšky.
    static readonly Color terrainLow = new Color(0.04f, 0.30f, 0.07f); // sýta tmavozelená
    static readonly Color terrainHigh = new Color(0.78f, 0.95f, 0.70f); // svetlá zelená

    // Jednoliate zelené pozadie pre Rail/Road/Industry view (výšky ignorované).
    static readonly Color flatGreen = new Color(0.16f, 0.45f, 0.18f);

    // -------------------------------------------------------------------------
    // VODA A MESTÁ (TerrainView) – farby zmeniteľné z KÓDU.
    //
    // Zámerne NIE cez Inspector: sú to public static polia, takže sa dajú
    // kedykoľvek prepísať z kódu (napr. MapSystem.WaterColor = ...). Hodnoty sú
    // zatiaľ predbežné (modrá / červená) – keď bude jasné, aké majú byť finálne,
    // stačí zmeniť tieto dve hodnoty na jednom mieste.
    //
    // Color32 použité úmyselne, aby bol zápis v 0–255 (modrá = 0,0,255;
    // červená = 255,0,0) doslovný a čitateľný; implicitne sa skonvertuje na Color.

    /// <summary>Farba VODY na mape (terén s úrovňou E &lt; 0, t.j. pod rovinou).</summary>
    public static Color WaterColor = new Color32(0, 0, 255, 255);   // modrá

    /// <summary>Farba MESTSKÝCH tilov na mape (obsadené budovami).</summary>
    public static Color CityColor = new Color32(255, 0, 0, 255);    // červená

    // Farba okraja mapy (rámik okolo textúry – kozmetické).
    static readonly Color colBorder = new Color(0f, 0f, 0f, 1f);

    // Núdzové (fallback) farby – použijú sa LEN ak sa textúru nepodarí
    // načítať alebo z nej prečítať pixely (napr. nemá zapnuté Read/Write).
    static readonly Color fallbackRail = new Color(0.15f, 0.15f, 0.15f);
    static readonly Color fallbackRoad = new Color(0.35f, 0.35f, 0.38f);
    static readonly Color fallbackStation = new Color(1.00f, 0.85f, 0.10f);
    static readonly Color fallbackDepot = new Color(1.00f, 0.45f, 0.05f);
    static readonly Color fallbackFactory = new Color(0.85f, 0.20f, 0.20f);
    static readonly Color fallbackProcessing = new Color(0.65f, 0.25f, 0.75f);

    // =====================================================================
    // FARBY Z HERNÝCH TEXTÚR
    // =====================================================================
    // IndicatrixAPI vykresľuje tile pomocou textúr načítaných z priečinka
    // Resources/Textures/... . MapSystem si načíta TIE ISTÉ textúry (rovnaké
    // cesty) a z každej spočíta priemernú farbu – tak mapa zobrazí objekty
    // presne vo farbách, aké majú v hre.
    //
    // Cesty MUSIA byť zhodné s tými v IndicatrixAPI.InitializeSnapSystem().

    // Kľúč do cache: kombinácia kategórie a tileID jednoznačne určuje textúru.
    static readonly Dictionary<string, Color> textureColorCache
        = new Dictionary<string, Color>();

    /// <summary>
    /// Vráti priemernú farbu herného tile podľa kategórie a tileID.
    ///
    /// Mapovanie (zhodné s IndicatrixAPI.UpdateTileMap):
    ///   Rail    : 1→Rail_Tex      2→RailStation_Tex   3→RailDepot_Tex
    ///   Road    : 1→Road_Tex      2→RoadStation_Tex   3→RoadDepot_Tex
    ///   Factory : 4→Factory_Tex   5→Processing_Tex
    ///
    /// Výsledok sa cachuje – textúra sa načíta a spriemeruje len raz.
    /// </summary>
    static Color GetTileTextureColor(IndicatrixAPI.TileCategory category, int tileID)
    {
        // Zostavenie cesty k textúre + fallback farby pre daný prvok.
        string resourcePath;
        Color fallback;

        switch (category)
        {
            case IndicatrixAPI.TileCategory.Rail:
                switch (tileID)
                {
                    case 1: resourcePath = "Textures/Rail/Rail_Tex"; fallback = fallbackRail; break;
                    case 2: resourcePath = "Textures/Rail/RailStation_Tex"; fallback = fallbackStation; break;
                    case 3: resourcePath = "Textures/Rail/RailDepot_Tex"; fallback = fallbackDepot; break;
                    default: resourcePath = "Textures/Rail/Rail_Tex"; fallback = fallbackRail; break;
                }
                break;

            case IndicatrixAPI.TileCategory.Road:
                switch (tileID)
                {
                    case 1: resourcePath = "Textures/Road/Road_Tex"; fallback = fallbackRoad; break;
                    case 2: resourcePath = "Textures/Road/RoadStation_Tex"; fallback = fallbackStation; break;
                    case 3: resourcePath = "Textures/Road/RoadDepot_Tex"; fallback = fallbackDepot; break;
                    default: resourcePath = "Textures/Road/Road_Tex"; fallback = fallbackRoad; break;
                }
                break;

            case IndicatrixAPI.TileCategory.Factory:
                switch (tileID)
                {
                    case 4: resourcePath = "Textures/Factory/Factory_Tex"; fallback = fallbackFactory; break;
                    case 5: resourcePath = "Textures/Factory/Processing_Tex"; fallback = fallbackProcessing; break;
                    default: resourcePath = "Textures/Factory/Factory_Tex"; fallback = fallbackFactory; break;
                }
                break;

            default:
                return flatGreen;
        }

        // Cache hit?
        if (textureColorCache.TryGetValue(resourcePath, out Color cached))
            return cached;

        // Cache miss → načítaj textúru a spočítaj jej priemernú farbu.
        Color result = AverageTextureColor(resourcePath, fallback);
        textureColorCache[resourcePath] = result;
        return result;
    }

    /// <summary>
    /// Načíta textúru z Resources a vráti jej priemernú farbu.
    ///
    /// Robustné voči dvom bežným problémom:
    ///   (1) Textúra v Resources neexistuje → Resources.Load vráti null.
    ///   (2) Textúra nemá v import settings zapnuté "Read/Write Enabled"
    ///       → GetPixels() hodí výnimku.
    /// V oboch prípadoch sa vráti zadaná fallback farba a vypíše varovanie.
    ///
    /// Priemeruje sa s preskakovaním (krok 4 px), aby to bolo rýchle aj pre
    /// veľké textúry. Plne priehľadné pixely (alpha ≈ 0) sa do priemeru
    /// nezarátavajú – inak by priehľadné okraje textúry "vyblednutím"
    /// skreslili výslednú farbu.
    /// </summary>
    static Color AverageTextureColor(string resourcePath, Color fallback)
    {
        Texture2D tex = Resources.Load<Texture2D>(resourcePath);
        if (tex == null)
        {
            Debug.LogWarning($"[MapSystem] Textúra '{resourcePath}' sa nenašla v Resources – " +
                             $"použijem náhradnú farbu.");
            return fallback;
        }

        try
        {
            Color[] px = tex.GetPixels();
            if (px == null || px.Length == 0)
                return fallback;

            float r = 0f, g = 0f, b = 0f;
            int count = 0;

            // Krok 4 – vzorkujeme každý 4. pixel (rýchlosť; presnosť stačí).
            int stride = Mathf.Max(1, px.Length / 4096);
            for (int i = 0; i < px.Length; i += stride)
            {
                Color c = px[i];
                if (c.a < 0.05f) continue;   // priehľadný pixel preskoč
                r += c.r; g += c.g; b += c.b;
                count++;
            }

            if (count == 0) return fallback;   // textúra celá priehľadná
            return new Color(r / count, g / count, b / count, 1f);
        }
        catch (UnityException)
        {
            // GetPixels zlyhá ak textúra nemá Read/Write Enabled.
            Debug.LogWarning($"[MapSystem] Textúra '{resourcePath}' nemá zapnuté " +
                             $"'Read/Write Enabled' v import settings – použijem náhradnú farbu. " +
                             $"Pre presnú farbu zapni Read/Write na textúre.");
            return fallback;
        }
    }

    /// <summary>
    /// Vyčistí cache priemerných farieb textúr. Volať netreba bežne – hodí sa
    /// len ak by sa textúry za behu zmenili (napr. pri hot-reloade assetov).
    /// </summary>
    public static void ClearTextureColorCache()
    {
        textureColorCache.Clear();
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Vyrenderuje mapu zadaného typu a vráti hotovú Texture2D.
    ///
    /// pixelsPerTile určuje rozlíšenie – koľko obrazových pixelov pripadá na
    /// jeden herný tile. Väčšia hodnota = ostrejšia mapa, ale väčšia textúra.
    ///
    /// Pri chýbajúcich manažéroch (TerrainManager / IndicatrixAPI ešte
    /// neinicializované) vráti malú jednofarebnú textúru, aby UI nespadlo.
    /// </summary>
    public static Texture2D RenderMap(MapViewType viewType, int pixelsPerTile = 4)
    {
        TerrainManager tm = TerrainManager.instance;
        IndicatrixAPI api = IndicatrixAPI.instance;

        // --- Ochrana pred neinicializovaným stavom ---------------------
        if (tm == null || tm.coordsF == null || tm.terrainWidth <= 0)
        {
            Debug.LogWarning("[MapSystem] TerrainManager nie je pripravený – vraciam prázdnu textúru.");
            return SolidTexture(flatGreen);
        }

        int tilesPerSide = tm.terrainWidth;          // počet tilov v jednej osi
        if (pixelsPerTile < 1) pixelsPerTile = 1;

        int texSize = tilesPerSide * pixelsPerTile;  // mapa je štvorcová

        // Bezpečnostný strop, aby sme nevyrobili obrovskú textúru.
        const int MAX_TEX = 2048;
        if (texSize > MAX_TEX)
        {
            pixelsPerTile = Mathf.Max(1, MAX_TEX / tilesPerSide);
            texSize = tilesPerSide * pixelsPerTile;
        }

        Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;   // ostré tile hrany, žiadne rozmazanie
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[texSize * texSize];

        // --- Predpočet rozsahu výšok (len pre TerrainView gradient) -----
        float minY = 0f, maxY = 1f;
        if (viewType == MapViewType.TerrainView)
            ComputeHeightRange(tm, out minY, out maxY);

        // --- Predpočet pre vodu a mestá (len TerrainView) ---------------
        // waterCeiling: tile s priemernou výškou POD touto hodnotou = voda.
        // cityMgr: zdroj mestských tilov (môže chýbať – vtedy mestá nekreslíme).
        float waterCeiling = LandPlaneHeight();
        CityManager cityMgr = (viewType == MapViewType.TerrainView)
            ? CityManager.instance
            : null;

        // =================================================================
        // HLAVNÝ RENDER LOOP – po jednotlivých tiloch
        // =================================================================
        for (int tz = 0; tz < tilesPerSide; tz++)
        {
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                // 1) Farba podkladu daného tilu
                Color baseColor;
                if (viewType == MapViewType.TerrainView)
                {
                    float h = TileAverageHeight(tm, tx, tz);

                    if (h < waterCeiling)
                    {
                        // VODA: úroveň terénu E < 0 (E = -1 a nižšie). Podklad
                        // prepíšeme explicitnou modrou (WaterColor), aby voda
                        // nesplývala s tmavozeleným spodkom výškového gradientu.
                        baseColor = WaterColor;
                    }
                    else
                    {
                        // Pevnina: výškový gradient zelenej (nízko → vysoko).
                        float t = (maxY - minY) > 0.0001f
                                  ? Mathf.InverseLerp(minY, maxY, h)
                                  : 0f;
                        baseColor = Color.Lerp(terrainLow, terrainHigh, t);
                    }
                }
                else
                {
                    // Rail / Road / Industry → jednoliate zelené pozadie.
                    baseColor = flatGreen;
                }

                Color tileColor = baseColor;

                // 2) MESTÁ (len TerrainView): mestský tile (obsadený budovou)
                //    prekreslíme farbou CityColor. Kreslí sa NAD podklad, ale
                //    POD objekty (trate/cesty/stanice) – tie ostanú viditeľné.
                //    Mestá nikdy nestoja na vode (minBuildLevel = 0), takže sa
                //    s vodnou plochou neprekrývajú.
                if (cityMgr != null && cityMgr.IsCityTile(tx, tz))
                    tileColor = CityColor;

                // 3) Prekreslenie objektom (ak na tile niečo je a patrí
                //    do práve zobrazovaného typu mapy). Farba objektu sa
                //    berie z reálnej hernej textúry. Objekty sú navrchu.
                if (api != null)
                {
                    if (TryGetObjectColor(api, tx, tz, viewType, out Color objColor))
                        tileColor = objColor;
                }

                // 4) Zápis bloku pixelsPerTile × pixelsPerTile pixelov.
                //    POZN.: Pixel (0,0) textúry je vľavo-dole, tile (0,0)
                //    mapy je tiež vľavo-dole (svet +X doprava, +Z hore),
                //    takže osi sú priamo zhodné – netreba zrkadliť.
                int px0 = tx * pixelsPerTile;
                int pz0 = tz * pixelsPerTile;

                for (int dz = 0; dz < pixelsPerTile; dz++)
                {
                    int row = (pz0 + dz) * texSize;
                    for (int dx = 0; dx < pixelsPerTile; dx++)
                    {
                        pixels[row + px0 + dx] = tileColor;
                    }
                }
            }
        }

        // --- Tenký rámik okolo celej mapy (kozmetické) -----------------
        DrawBorder(pixels, texSize);

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Pomocný getter – ľudský názov typu mapy pre TypeOfMapTextCaption.
    /// </summary>
    public static string GetViewLabel(MapViewType viewType)
    {
        switch (viewType)
        {
            case MapViewType.TerrainView: return "Terrain View";
            case MapViewType.RailNetworkView: return "Rail Network View";
            case MapViewType.RoadNetView: return "Road Network View";
            case MapViewType.IndustryView: return "Industry View";
            default: return viewType.ToString();
        }
    }

    // =====================================================================
    // VÝŠKOVÉ DÁTA TERÉNU
    // =====================================================================

    /// <summary>
    /// Priemerná výška tilu (tx, tz) – aritmetický priemer Y zo 4 rohových
    /// vrcholov daného tilu v poli coordsF.
    ///
    /// Vrcholová mriežka má (terrainWidth+1) bodov v každej osi. Index
    /// vrcholu (vx, vz) v coordsF je: vz * (terrainWidth+1) + vx.
    /// Tile (tx, tz) má rohy (tx,tz), (tx+1,tz), (tx,tz+1), (tx+1,tz+1).
    /// </summary>
    static float TileAverageHeight(TerrainManager tm, int tx, int tz)
    {
        int vWidth = tm.terrainWidth + 1;

        int i00 = tz * vWidth + tx;
        int i10 = tz * vWidth + (tx + 1);
        int i01 = (tz + 1) * vWidth + tx;
        int i11 = (tz + 1) * vWidth + (tx + 1);

        Vector3[] c = tm.coordsF;
        return (c[i00].y + c[i10].y + c[i01].y + c[i11].y) * 0.25f;
    }

    /// <summary>
    /// Y-výška ROVINY terénu (úroveň E = 0). Tile je VODA, ak jeho priemerná
    /// výška leží POD touto hodnotou (úroveň E &lt; 0, t.j. E = -1 a nižšie) –
    /// presne podľa definície "voda = úroveň terénu pod 0, teda -1".
    ///
    /// Hodnota je odvodená z verejných konštánt TerrainManager, takže ostane
    /// správna aj keby sa výškové limity terénu v budúcnosti zmenili:
    ///   step           = (MaxTerrainHeight - MinTerrainHeight) / (MaxE - MinE)
    ///   rovina (E = 0) = MinTerrainHeight + step   (E = -1 je najnižšia úroveň)
    /// </summary>
    static float LandPlaneHeight()
    {
        int levelSpan = TerrainManager.MaxElevationLevel - TerrainManager.MinElevationLevel;
        float step = (levelSpan != 0)
            ? (TerrainManager.MaxTerrainHeight - TerrainManager.MinTerrainHeight) / levelSpan
            : 0.25f;

        // E = -1 zodpovedá MinTerrainHeight; rovina E = 0 je o jeden krok vyššie.
        return TerrainManager.MinTerrainHeight + step;
    }

    /// <summary>
    /// Nájde minimálnu a maximálnu výšku (Y) naprieč celou vrcholovou
    /// mriežkou – slúži na normalizáciu gradientu v TerrainView.
    /// Ak je terén úplne plochý, vráti minY == maxY (volajúci to ošetrí).
    /// </summary>
    static void ComputeHeightRange(TerrainManager tm, out float minY, out float maxY)
    {
        Vector3[] c = tm.coordsF;
        minY = float.MaxValue;
        maxY = float.MinValue;

        for (int i = 0; i < c.Length; i++)
        {
            float y = c[i].y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        if (minY == float.MaxValue) { minY = 0f; maxY = 1f; }
    }

    // =====================================================================
    // ČÍTANIE OBJEKTOV Z TILE GRIDU
    // =====================================================================

    /// <summary>
    /// Zistí, či na tile (x, z) leží objekt, ktorý patrí do práve
    /// zobrazovaného typu mapy, a ak áno, vráti jeho farbu (z hernej textúry).
    ///
    /// Filtrovanie podľa MapViewType:
    ///   • TerrainView      → zobrazí VŠETKY objekty: Rail a Road farbou hernej
    ///                          textúry, Factory mapovou farbou MapColor
    ///                          (rovnako ako IndustryView).
    ///   • RailNetworkView  → len kategória Rail (tileID 1,2,3).
    ///   • RoadNetView      → len kategória Road (tileID 1,2,3).
    ///   • IndustryView     → len kategória Factory (tileID 4,5). Farba sa
    ///                          berie z FactoryDefinition.MapColor položenej
    ///                          továrne (FactoryRegistry), NIE z hernej textúry.
    ///
    /// Vracia false ak je tile prázdny alebo nepatrí do daného view.
    /// </summary>
    /// <summary>
    /// Vráti mapovú farbu továrne, ktorá leží na tile [x,z]. Farba sa berie
    /// z FactoryDefinition.MapColor položenej továrne (FactoryRegistry –
    /// O(1) spätné mapovanie tile → FactoryInstance), takže každý typ
    /// továrne má na mape vlastnú farbu nezávislú od hernej textúry.
    ///
    /// Fallback: ak pre Factory tile neexistuje registrovaná FactoryInstance
    /// (napr. po načítaní hry bez re-registrácie), vráti pôvodnú priemernú
    /// farbu hernej textúry, aby mapa aspoň niečo zobrazila.
    ///
    /// Spoločné pre IndustryView aj TerrainView (jediný zdroj logiky farby
    /// továrne na mape).
    /// </summary>
    static Color GetFactoryMapColor(int x, int z, IndicatrixAPI.TileData td)
    {
        FactoryInstance fi = FactoryRegistry.GetFactoryAt(x, z);
        if (fi != null && fi.Definition != null)
            return fi.Definition.MapColor;

        return GetTileTextureColor(IndicatrixAPI.TileCategory.Factory, td.tileID);
    }

    static bool TryGetObjectColor(IndicatrixAPI api, int x, int z,
                                  MapViewType viewType, out Color color)
    {
        color = default;

        IndicatrixAPI.TileData td = api.GetTileByIndexAny(x, z);
        if (td.tileID == 0)
            return false;   // prázdny tile

        switch (viewType)
        {
            // --- Železničná sieť ---------------------------------------
            case MapViewType.RailNetworkView:
                if (td.category != IndicatrixAPI.TileCategory.Rail) return false;
                color = GetTileTextureColor(IndicatrixAPI.TileCategory.Rail, td.tileID);
                return true;

            // --- Cestná sieť -------------------------------------------
            case MapViewType.RoadNetView:
                if (td.category != IndicatrixAPI.TileCategory.Road) return false;
                color = GetTileTextureColor(IndicatrixAPI.TileCategory.Road, td.tileID);
                return true;

            // --- Priemysel ---------------------------------------------
            // Na rozdiel od Rail/Road view sa farba NEberie z hernej textúry,
            // ale z explicitnej mapovej farby konkrétnej položenej továrne
            // (FactoryDefinition.MapColor). Tile [x,z] namapujeme na jeho
            // FactoryInstance cez FactoryRegistry (O(1) spätné mapovanie).
            case MapViewType.IndustryView:
                if (td.category != IndicatrixAPI.TileCategory.Factory) return false;
                color = GetFactoryMapColor(x, z, td);
                return true;

            // --- Celkový terén: zobraz čokoľvek -------------------------
            // Trate a cesty sa kreslia farbou hernej textúry; továrne rovnako
            // ako v IndustryView – explicitnou mapovou farbou MapColor.
            case MapViewType.TerrainView:
            default:
                switch (td.category)
                {
                    case IndicatrixAPI.TileCategory.Rail:
                    case IndicatrixAPI.TileCategory.Road:
                        color = GetTileTextureColor(td.category, td.tileID);
                        return true;
                    case IndicatrixAPI.TileCategory.Factory:
                        color = GetFactoryMapColor(x, z, td);
                        return true;
                    default:
                        return false;
                }
        }
    }

    // =====================================================================
    // POMOCNÉ – KRESLENIE
    // =====================================================================

    /// <summary>Vyrobí malú 4×4 jednofarebnú textúru (fallback).</summary>
    static Texture2D SolidTexture(Color c)
    {
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Nakreslí 1 px čierny rámik po obvode textúry.</summary>
    static void DrawBorder(Color[] pixels, int size)
    {
        for (int i = 0; i < size; i++)
        {
            pixels[i] = colBorder;                       // spodný riadok
            pixels[(size - 1) * size + i] = colBorder;   // horný riadok
            pixels[i * size] = colBorder;                // ľavý stĺpec
            pixels[i * size + (size - 1)] = colBorder;   // pravý stĺpec
        }
    }
}