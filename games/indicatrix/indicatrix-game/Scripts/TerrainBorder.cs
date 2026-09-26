using System.Collections;
using UnityEngine;

/// <summary>
/// OKRAJOVÉ STENY TERÉNU ("skirt" / pôdny profil).
///
/// Vygeneruje 4 SAMOSTATNÉ mesh-e (jeden na každú stranu mapy), ktoré zvisle
/// uzatvárajú okraj terénu a tým prekryjú vodnú hladinu pod terénom, aby ju
/// hráč z pohľadu izometrickej kamery nevidel.
///
/// GEOMETRIA JEDNEJ STENY (obdĺžnik pozdĺž celej strany mapy):
///   • HORNÁ hrana presne kopíruje výšky vertexov pôvodného terénu
///     (TerrainManager.coordsF na danom okraji) → žiadna škára medzi
///     terénom a stenou,
///   • SPODNÁ hrana je úplne vodorovná (jedno spoločné Y pre všetky steny),
///   • stena je rozdelená na segmenty po 1 tile → UV je mapované tak, že
///     jedno "opakovanie" textúry = 1 tile na šírku a 4 jednotky na výšku.
///
/// DÔLEŽITÉ:
///   • Ide o ÚPLNE NOVÉ, samostatné mesh-e – pôvodný terén (TerrainElement /
///     TerrainManager) sa nijako nemení a nedotýka sa ho ani jeden riadok kódu.
///   • Steny NEMAJÚ MeshCollider – neovplyvnia raycast pri editácii terénu
///     ani klikanie v GameManageri.
///   • Generuje sa jednorazovo pri štarte (po tom, čo TerrainManager
///     vytvorí coordsF). Ak by si niekedy chcel steny prekresliť po zmene
///     výšok na okraji mapy, stačí zavolať TerrainBorder.instance.Rebuild().
///
/// POUŽITIE:
///   1. V scéne vytvor prázdny GameObject (napr. "TerrainBorder"),
///      pozíciu nechaj na (0,0,0) a pridaj naň tento skript.
///   2. Každej strane priraď v Inspectore Material (alebo len Texture –
///      materiál sa dotvorí automaticky) s textúrou pôdneho profilu.
///
/// POZNÁMKA KU KAMERE: pri Rotation 30/45/0 sa kamera pozerá smerom +X/+Z,
/// takže reálne viditeľné sú len steny WEST (x = 0) a SOUTH (z = 0).
/// Ostatné dve sú "za" mapou – môžeš ich vypnúť (generate = false),
/// predvolene sú zapnuté všetky štyri.
/// </summary>
[DisallowMultipleComponent]
public class TerrainBorder : MonoBehaviour
{
    public static TerrainBorder instance;

    public enum Side { South = 0, North = 1, West = 2, East = 3 }

    [System.Serializable]
    public class SideSettings
    {
        [Tooltip("Má sa táto stena vôbec vygenerovať?")]
        public bool generate = true;

        [Tooltip("Materiál steny (napr. Unlit/Texture alebo Sprites/Default s textúrou pôdneho profilu).")]
        public Material material;

        [Tooltip("Voliteľné – ak necháš Material prázdny, stačí sem dať textúru/sprite a materiál sa vytvorí automaticky.")]
        public Texture texture;
    }

    // =====================================================================
    // NASTAVENIA
    // =====================================================================

    [Header("Steny (v poradí South z=0, North z=max, West x=0, East x=max)")]
    public SideSettings south = new SideSettings();
    public SideSettings north = new SideSettings();
    public SideSettings west = new SideSettings();
    public SideSettings east = new SideSettings();

    [Header("Rozmery")]
    [Tooltip("Výška steny (v jednotkách = tiloch) meraná od najnižšej možnej výšky terénu nadol. 4 = obdĺžnik vysoký 4 tily.")]
    public float wallHeight = 4f;

    [Tooltip("Ak je zapnuté, spodná hrana je na pevnej hodnote 'bottomY' namiesto (MinTerrainHeight - wallHeight).")]
    public bool useCustomBottomY = false;

    [Tooltip("Pevná Y súradnica spodnej (vodorovnej) hrany, ak je useCustomBottomY zapnuté.")]
    public float bottomY = -1.25f;

    [Tooltip("Posun steny smerom VON od mapy. 0 = presne na okraji. Malá hodnota (napr. 0.001) pomôže, ak by vznikali z-fighting švy.")]
    public float outwardOffset = 0f;

    [Header("UV mapovanie textúry")]
    [Tooltip("Šírka jedného opakovania textúry pozdĺž okraja, v tiloch. 1 = jeden tile = jedna textúra.")]
    public float uvTileWidth = 1f;

    [Tooltip("Výška jedného opakovania textúry, v jednotkách. 4 = textúra pokryje 4 tily na výšku.")]
    public float uvTileHeight = 4f;

    [Tooltip("Ukotvenie textúry k HORNEJ hrane (v = 1 na povrchu terénu). Odporúčané pre pôdny profil – tráva/vrchná vrstva vždy presne lícuje s terénom.")]
    public bool uvAnchorTop = true;

    [Header("Render")]
    [Tooltip("Majú steny vrhať tiene? Pre okrajové steny sa zvyčajne nehodí.")]
    public bool castShadows = false;

    [Tooltip("Majú steny prijímať tiene?")]
    public bool receiveShadows = true;

    // Vygenerované objekty (aby sa dali pri Rebuild() korektne zmazať).
    private GameObject[] wallObjects = new GameObject[4];

    // =====================================================================
    // ŠTART – počká, kým TerrainManager pripraví coordsF, potom postaví steny
    // =====================================================================

    private IEnumerator Start()
    {
        instance = this;

        // TerrainManager napĺňa coordsF vo svojom Start(); poradie skriptov
        // nie je zaručené, preto počkáme, kým sú dáta k dispozícii.
        while (TerrainManager.instance == null
               || TerrainManager.instance.coordsF == null
               || TerrainManager.instance.coordsF.Length == 0)
        {
            yield return null;
        }

        // Ešte jeden frame, aby boli isto postavené aj TerrainElementy.
        yield return null;

        Build();
    }

    // =====================================================================
    // VEREJNÉ API
    // =====================================================================

    /// <summary>
    /// Vygeneruje (alebo prekreslí) všetky štyri okrajové steny.
    /// </summary>
    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        Build();
    }

    private void Build()
    {
        if (TerrainManager.instance == null || TerrainManager.instance.coordsF == null)
        {
            Debug.LogWarning("[TerrainBorder] TerrainManager alebo coordsF nie sú pripravené – steny sa nevygenerovali.");
            return;
        }

        // Objekt držíme v počiatku – mesh používa priamo svetové súradnice
        // terénu (rovnako ako TerrainElement).
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        ClearWalls();

        BuildSide(Side.South, south);
        BuildSide(Side.North, north);
        BuildSide(Side.West, west);
        BuildSide(Side.East, east);
    }

    private void ClearWalls()
    {
        for (int i = 0; i < wallObjects.Length; i++)
        {
            if (wallObjects[i] != null)
            {
                if (Application.isPlaying)
                    Destroy(wallObjects[i]);
                else
                    DestroyImmediate(wallObjects[i]);

                wallObjects[i] = null;
            }
        }
    }

    // =====================================================================
    // GENEROVANIE JEDNEJ STENY
    // =====================================================================

    private void BuildSide(Side side, SideSettings settings)
    {
        if (settings == null || !settings.generate)
            return;

        int W = TerrainManager.instance.terrainWidth;   // 256
        int count = W + 1;                              // počet vertexov pozdĺž hrany (257)

        float baseY = GetBottomY();

        Vector3 outward = OutwardNormal(side);

        Vector3[] verts = new Vector3[count * 2];       // pre každý stĺpec: [0] hore, [1] dole
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[W * 6];                    // 2 trojuholníky na tile

        float invUvWidth = 1f / Mathf.Max(0.0001f, uvTileWidth);
        float invUvHeight = 1f / Mathf.Max(0.0001f, uvTileHeight);

        for (int i = 0; i < count; i++)
        {
            // Grid súradnice vertexu na danej hrane. Smer prechodu je zvolený
            // tak, aby pri vinutí nižšie vyšla normála smerom VON z mapy
            // (pravidlo: outward = up × smer prechodu).
            int gx, gz;
            switch (side)
            {
                case Side.South: gx = i; gz = 0; break;          // z = 0,  smer +X
                case Side.North: gx = W - i; gz = W; break;      // z = W,  smer -X
                case Side.West: gx = 0; gz = W - i; break;       // x = 0,  smer -Z
                default: gx = W; gz = i; break;                  // x = W,  smer +Z (East)
            }

            float topY = TerrainHeight(gx, gz);

            Vector3 top = new Vector3(gx, topY, gz) + outward * outwardOffset;
            Vector3 bottom = new Vector3(top.x, baseY, top.z);

            int v = i * 2;
            verts[v] = top;
            verts[v + 1] = bottom;

            // U rastie pozdĺž hrany – 1 tile = uvTileWidth opakovaní textúry.
            float u = i * invUvWidth;

            // V: buď ukotvené k hornej hrane (odporúčané pre pôdny profil),
            // alebo k spodnej vodorovnej hrane.
            float height = topY - baseY;
            if (uvAnchorTop)
            {
                uvs[v] = new Vector2(u, 1f);
                uvs[v + 1] = new Vector2(u, 1f - height * invUvHeight);
            }
            else
            {
                uvs[v] = new Vector2(u, height * invUvHeight);
                uvs[v + 1] = new Vector2(u, 0f);
            }
        }

        // Trojuholníky – vinutie v smere hodinových ručičiek pri pohľade zvonku.
        for (int i = 0, t = 0; i < W; i++, t += 6)
        {
            int topA = i * 2;
            int botA = i * 2 + 1;
            int topB = (i + 1) * 2;
            int botB = (i + 1) * 2 + 1;

            tris[t] = topA;
            tris[t + 1] = topB;
            tris[t + 2] = botB;

            tris[t + 3] = topA;
            tris[t + 4] = botB;
            tris[t + 5] = botA;
        }

        // ---- GameObject + mesh ----------------------------------------
        GameObject go = new GameObject("TerrainBorder_" + side);
        go.transform.SetParent(this.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        Mesh mesh = new Mesh();
        mesh.name = "BorderMesh_" + side;
        mesh.indexFormat = (verts.Length > 65000)
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ResolveMaterial(settings, side);
        mr.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = receiveShadows;

        // ŽIADNY MeshCollider – stena nesmie zavadzať raycastom terénu.

        wallObjects[(int)side] = go;
    }

    // =====================================================================
    // POMOCNÉ FUNKCIE
    // =====================================================================

    /// <summary>Výška (Y) vertexu terénu na grid súradniciach [x, z].</summary>
    private float TerrainHeight(int x, int z)
    {
        int size = TerrainManager.instance.terrainWidth + 1;
        return TerrainManager.instance.coordsF[z * size + x].y;
    }

    /// <summary>Y súradnica spoločnej vodorovnej spodnej hrany všetkých stien.</summary>
    private float GetBottomY()
    {
        if (useCustomBottomY)
            return bottomY;

        // Najnižšia možná výška terénu (2.75 = zároveň hladina vody) mínus výška steny.
        return TerrainManager.MinTerrainHeight - wallHeight;
    }

    /// <summary>Normála smerujúca VON z mapy pre danú stranu.</summary>
    private Vector3 OutwardNormal(Side side)
    {
        switch (side)
        {
            case Side.South: return new Vector3(0f, 0f, -1f);
            case Side.North: return new Vector3(0f, 0f, 1f);
            case Side.West: return new Vector3(-1f, 0f, 0f);
            default: return new Vector3(1f, 0f, 0f);
        }
    }

    /// <summary>
    /// Vráti materiál pre stenu. Ak je zadaný Material, použije sa (a ak je
    /// vyplnená aj Texture, nastaví sa na jeho inštanciu). Ak je zadaná len
    /// Texture, vytvorí sa jednoduchý materiál automaticky.
    /// </summary>
    private Material ResolveMaterial(SideSettings settings, Side side)
    {
        Material mat = settings.material;

        if (mat != null)
        {
            if (settings.texture != null)
            {
                mat = new Material(mat);            // inštancia, aby sa neprepísal asset
                mat.name = mat.name + "_" + side;
                mat.mainTexture = settings.texture;
            }
            return mat;
        }

        if (settings.texture != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            if (sh != null)
            {
                mat = new Material(sh);
                mat.name = "TerrainBorderMat_" + side;
                mat.mainTexture = settings.texture;

                // Textúra sa má pozdĺž okraja opakovať.
                if (settings.texture != null)
                    settings.texture.wrapMode = TextureWrapMode.Repeat;

                return mat;
            }
        }

        Debug.LogWarning("[TerrainBorder] Strane " + side + " nie je priradený Material ani Texture.");
        return null;
    }
}
