using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RailCrossingSpriteGlobalSettings
/// ─────────────────────────────────────────────────────────────────────────
/// Nastavenia SPOLOČNÉ pre sprity všetkých mostov a tunelov (vrstva, render
/// queue, náhľad). Sedí v Inspectore na komponente RailCrossingSystem.
/// Per-sprite veci (rotácia, flip, mierka, posun) sú v CrossingSpriteLibrary.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[System.Serializable]
public class RailCrossingSpriteGlobalSettings
{
    [Tooltip("Základný Sorting Order spritov mostov a tunelov.")]
    public int sortingOrder = -500;

    [Tooltip("Sorting Layer spritov. Prázdne = \"Default\".")]
    public string sortingLayerName = "Default";

    [Tooltip("Render queue materiálu (3000 = štandardná priehľadná geometria).")]
    public int renderQueue = 3000;

    [Tooltip("Zarovnanie podľa skutočne nakresleného obsahu (vyžaduje Mesh Type = Tight).")]
    public bool useTightBounds = true;

    [Tooltip("Priehľadnosť NÁHĽADU (sprite pod kurzorom pred postavením).")]
    [Range(0.05f, 1f)]
    public float previewAlpha = 0.55f;

    [Tooltip("Farebný nádych NÁHĽADU.")]
    public Color previewTint = new Color(0.65f, 1f, 0.65f, 1f);

    [Tooltip("Skryť textúru koľaje pod spritom HLAVY MOSTA (sprite obsahuje nástup).")]
    public bool hideBridgeHeadTextures = true;

    // POZN.: Sprite PORTÁLU TUNELA nahrádza textúru dlaždice (Rail_Tex /
    // Road_Tex) – leží presne na tile, preto sa textúra pod ním skrýva vždy.

    public string Normalize()
    {
        string fixes = "";
        if (renderQueue <= 0) { fixes += $" renderQueue {renderQueue}→3000;"; renderQueue = 3000; }
        if (string.IsNullOrEmpty(sortingLayerName)) sortingLayerName = "Default";
        if (previewAlpha <= 0.001f) previewAlpha = 0.55f;
        return fixes;
    }
}

/// <summary>
/// CrossingSpriteVisual
/// ─────────────────────────────────────────────────────────────────────────
/// Vykreslí JEDEN segment mosta (1 tile, alebo celý krátky most) alebo JEDEN
/// portál tunela ako 2D sprite. Vytvára ho RailCrossingSystem.
///
/// Princíp je rovnaký ako FactorySpriteBillboard (mierka podľa priemetu
/// rohov, spodná hrana nakresleného obsahu na najnižší roh, ZWrite OFF),
/// rozšírený o:
///   • fixné pootočenie v 3 osiach (CrossingSpriteSettings.pitch/yaw/roll),
///   • zrkadlenie flipX / flipY,
///   • režim WorldOriented (pevná orientácia vo svete podľa osi mosta):
///       – mierka sa počíta podľa tej osi obrázka, ktorá PO POOTOČENÍ leží
///         pozdĺž mosta → segment má presne 1 tile (krátky most celú dĺžku),
///         nezávisle od Pitch/Yaw/Roll a pomeru strán obrázka,
///       – naprieč mostom obrázok nikdy nepresiahne 1 tile,
///       – positionOffset (x, y, z) posunie sprite vo svetových osiach.
///
/// REŽIM TILE SURFACE (portály TUNELOV) – SetupTileSurface(...):
///   Sprite NIE JE billboard. Leží na povrchu dlaždice presne ako textúra
///   Rail_Tex / Road_Tex alebo sprite RailHorizontal z TileModelLibrary:
///     rotácia = naklonenie(rampa) × Euler(Pitch, Yaw, Roll)
///   Pitch/Yaw/Roll sa teda zadávajú V SÚRADNICIACH UŽ NAKLONENEJ dlaždice
///   (tunel je vždy na rampe): Pitch 90 položí obrázok na šikmú plochu,
///   Yaw ho otáča v rovine rampy. Obrázok sa roztiahne presne na celý tile
///   (po spádnici na šikmú dĺžku, priečne na 1).
///
/// Nemá Collider – klik prejde na terén, most/tunel sa nájde cez
/// RailCrossingSystem (Demolish).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CrossingSpriteVisual : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Sprite sprite;
    private readonly Vector3[] corners = new Vector3[4];
    private Vector3 center;
    private Quaternion worldBaseRotation = Quaternion.identity;
    private float worldWidth = 1f;

    private CrossingSpriteSettings settings;
    private RailCrossingSpriteGlobalSettings global;

    private Vector2 contentSize;
    private Vector2 contentCenter;

    private Camera cachedCam;
    private Quaternion lastCamRotation;
    private bool applied;

    // Režim TILE SURFACE (portál tunela) – umiestni sa raz, nezávisí od kamery.
    private bool tileSurface;

    private static readonly Dictionary<int, Material> sharedMaterials = new Dictionary<int, Material>();

    /// <summary>
    /// Nastaví sprite a geometriu segmentu.
    /// </summary>
    /// <param name="segmentCorners">4 rohy segmentu vo svete (vrátane Y).</param>
    /// <param name="segmentCenter">Stred segmentu vo svete.</param>
    /// <param name="baseRotation">Základná orientácia pre WorldOriented režim.</param>
    /// <param name="widthWorld">Šírka segmentu vo svete pre WorldOriented režim.</param>
    /// <param name="tint">Farba (náhľad = priehľadná).</param>
    public void Setup(Sprite sprite, Vector3[] segmentCorners, Vector3 segmentCenter,
                      Quaternion baseRotation, float widthWorld,
                      CrossingSpriteSettings spriteSettings,
                      RailCrossingSpriteGlobalSettings globalSettings,
                      Color tint)
    {
        this.sprite = sprite;
        settings = spriteSettings ?? new CrossingSpriteSettings();
        global = globalSettings ?? new RailCrossingSpriteGlobalSettings();
        center = segmentCenter;
        worldBaseRotation = baseRotation;
        worldWidth = Mathf.Max(0.01f, widthWorld);

        string fixes = global.Normalize() + settings.Normalize();
        if (!string.IsNullOrEmpty(fixes))
            Debug.LogWarning($"[CrossingSprite] Opravené neplatné hodnoty:{fixes}");

        if (sprite == null) return;

        if (segmentCorners != null)
            for (int i = 0; i < 4 && i < segmentCorners.Length; i++)
                corners[i] = segmentCorners[i];

        ComputeContentBounds();

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = tint;
        spriteRenderer.flipX = settings.flipX;
        spriteRenderer.flipY = settings.flipY;
        spriteRenderer.sortingOrder = global.sortingOrder + settings.sortingOrderOffset;
        spriteRenderer.sortingLayerName = global.sortingLayerName;
        spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        Material mat = ResolveMaterial(global.renderQueue);
        if (mat != null) spriteRenderer.sharedMaterial = mat;

        applied = false;
        Apply();
    }

    /// <summary>
    /// Nastaví sprite ležiaci NA POVRCHU DLAŽDICE (portál tunela) – analógia
    /// k textúre Rail_Tex / sprite RailHorizontal z TileModelLibrary.
    /// </summary>
    /// <param name="tileCorners">
    /// 4 rohy terénu dlaždice v poradí [x,z], [x+1,z], [x+1,z+1], [x,z+1].
    /// </param>
    public void SetupTileSurface(Sprite sprite, Vector3[] tileCorners,
                                 CrossingSpriteSettings spriteSettings,
                                 RailCrossingSpriteGlobalSettings globalSettings,
                                 Color tint)
    {
        this.sprite = sprite;
        settings = spriteSettings ?? new CrossingSpriteSettings();
        global = globalSettings ?? new RailCrossingSpriteGlobalSettings();
        tileSurface = true;

        string fixes = global.Normalize() + settings.Normalize();
        if (!string.IsNullOrEmpty(fixes))
            Debug.LogWarning($"[CrossingSprite] Opravené neplatné hodnoty:{fixes}");

        if (sprite == null || tileCorners == null || tileCorners.Length < 4) return;

        for (int i = 0; i < 4; i++) corners[i] = tileCorners[i];

        ComputeContentBounds();

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = tint;
        spriteRenderer.flipX = settings.flipX;
        spriteRenderer.flipY = settings.flipY;
        spriteRenderer.sortingOrder = global.sortingOrder + settings.sortingOrderOffset;
        spriteRenderer.sortingLayerName = global.sortingLayerName;
        spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        Material mat = ResolveMaterial(global.renderQueue);
        if (mat != null) spriteRenderer.sharedMaterial = mat;

        ApplyTileSurface();
    }

    /// <summary>
    /// Položí sprite na povrch dlaždice.
    ///
    ///   1. Rovina dlaždice zo 4 rohov (rovnaký gradient ako
    ///      IndicatrixAPI.TryGetTileSlope) → normála a naklonenie
    ///      surfaceRot = FromToRotation(up, normála). Lokálne osi X/Z
    ///      naklonenej dlaždice ležia v rovine rampy.
    ///   2. rotácia = surfaceRot × Euler(Pitch, Yaw, Roll) – nastavenia sa
    ///      aplikujú v súradniciach UŽ NAKLONENEJ dlaždice.
    ///   3. Mierka: tá os obrázka, ktorá po pootočení leží pozdĺž X dlaždice,
    ///      sa roztiahne na šikmú dĺžku rampy v X; os pozdĺž Z na dĺžku v Z
    ///      → obrázok pokryje presne celý tile ako textúra.
    ///   4. Stred nakresleného obsahu na stred dlaždice, mierne nad povrch.
    /// </summary>
    private void ApplyTileSurface()
    {
        if (sprite == null || spriteRenderer == null) return;

        float h00 = corners[0].y, h10 = corners[1].y, h11 = corners[2].y, h01 = corners[3].y;
        float dhdx = ((h10 + h11) - (h00 + h01)) * 0.5f;
        float dhdz = ((h01 + h11) - (h00 + h10)) * 0.5f;

        Vector3 normal = new Vector3(-dhdx, 1f, -dhdz).normalized;
        Quaternion surfaceRot = Quaternion.FromToRotation(Vector3.up, normal);
        Quaternion localRot = Quaternion.Euler(settings.EulerDegrees);
        Quaternion rot = surfaceRot * localRot;
        transform.rotation = rot;

        // Šikmé dĺžky dlaždice pozdĺž jej lokálnych osí X a Z.
        float lenX = Mathf.Sqrt(1f + dhdx * dhdx);
        float lenZ = Mathf.Sqrt(1f + dhdz * dhdz);

        // Ktorá os obrázka leží po pootočení pozdĺž X / Z dlaždice.
        float sx = AxisScale(localRot * Vector3.right, contentSize.x, lenX, lenZ);
        float sy = AxisScale(localRot * Vector3.up, contentSize.y, lenX, lenZ);
        if (float.IsNaN(sx)) sx = float.IsNaN(sy) ? 1f : sy;   // os kolmá na povrch → uniformne
        if (float.IsNaN(sy)) sy = sx;

        float mult = settings.widthMultiplier;
        Vector3 scale = new Vector3(sx * mult, sy * mult, (sx + sy) * 0.5f * mult);
        transform.localScale = scale;

        Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        center += normal * (0.012f + settings.verticalOffset);   // nad textúrou (0.01) proti z-fightingu

        transform.position = center - rot * Vector3.Scale((Vector3)contentCenter, scale);
        applied = true;
    }

    /// <summary>
    /// Mierka jednej osi obrázka: ak os (v súradniciach dlaždice) leží pozdĺž X,
    /// roztiahne sa na lenX; pozdĺž Z na lenZ; kolmá na povrch → NaN.
    /// </summary>
    private static float AxisScale(Vector3 axisInTile, float contentExtent, float lenX, float lenZ)
    {
        if (contentExtent < 1e-5f) return float.NaN;
        if (Mathf.Abs(axisInTile.x) > 0.5f) return lenX / contentExtent;
        if (Mathf.Abs(axisInTile.z) > 0.5f) return lenZ / contentExtent;
        return float.NaN;
    }

    void LateUpdate()
    {
        if (sprite == null || settings == null) return;

        // Sprite na povrchu dlaždice nezávisí od kamery – umiestni sa raz.
        if (tileSurface)
        {
            if (!applied) ApplyTileSurface();
            return;
        }

        // WorldOriented nezávisí od kamery – stačí raz.
        if (settings.renderMode == CrossingSpriteSettings.RenderMode.WorldOriented)
        {
            if (!applied) Apply();
            return;
        }

        Camera cam = ResolveCamera();
        if (cam == null) return;
        if (!applied || cam.transform.rotation != lastCamRotation)
            Apply();
    }

    private void ComputeContentBounds()
    {
        contentSize = sprite.bounds.size;
        contentCenter = sprite.bounds.center;

        if (global != null && global.useTightBounds)
        {
            Vector2[] v = sprite.vertices;
            if (v != null && v.Length >= 3)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].x < minX) minX = v[i].x;
                    if (v[i].x > maxX) maxX = v[i].x;
                    if (v[i].y < minY) minY = v[i].y;
                    if (v[i].y > maxY) maxY = v[i].y;
                }
                if (maxX - minX > 1e-5f && maxY - minY > 1e-5f)
                {
                    contentSize = new Vector2(maxX - minX, maxY - minY);
                    contentCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                }
            }
        }

        // Zrkadlenie prevráti obsah okolo pivotu → stred obsahu sa zrkadlí tiež.
        if (settings.flipX) contentCenter.x = -contentCenter.x;
        if (settings.flipY) contentCenter.y = -contentCenter.y;
    }

    private void Apply()
    {
        if (sprite == null || spriteRenderer == null) return;

        bool world = settings.renderMode == CrossingSpriteSettings.RenderMode.WorldOriented;
        Quaternion extra = Quaternion.Euler(settings.EulerDegrees);

        Quaternion rot;
        Camera cam = null;
        if (world)
        {
            // Pootočenie vo svetových osiach na základnú orientáciu.
            rot = extra * worldBaseRotation;
        }
        else
        {
            cam = ResolveCamera();
            if (cam == null) return;
            // Billboard + pootočenie v priestore kamery.
            rot = cam.transform.rotation * extra;
        }

        transform.rotation = rot;

        Vector3 right = rot * Vector3.right;
        Vector3 up = rot * Vector3.up;
        Vector3 fwd = rot * Vector3.forward;

        float minRight = float.MaxValue, maxRight = float.MinValue;
        float minUp = float.MaxValue;
        float minFwd = float.MaxValue, maxFwd = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float r = Vector3.Dot(corners[i], right);
            float u = Vector3.Dot(corners[i], up);
            float f = Vector3.Dot(corners[i], fwd);
            if (r < minRight) minRight = r;
            if (r > maxRight) maxRight = r;
            if (u < minUp) minUp = u;
            if (f < minFwd) minFwd = f;
            if (f > maxFwd) maxFwd = f;
        }

        float scale;
        if (world)
        {
            scale = WorldOrientedScale(right, up) * settings.widthMultiplier;
            if (scale < 1e-6f) return;
        }
        else
        {
            float targetWidth = (maxRight - minRight) * settings.widthMultiplier;
            if (targetWidth < 0.0001f) return;
            scale = contentSize.x > 1e-5f ? targetWidth / contentSize.x : 1f;
        }
        transform.localScale = new Vector3(scale, scale, scale);

        float halfH = contentSize.y * 0.5f * scale;
        float targetUp = minUp + halfH + settings.verticalOffset;

        float targetFwd;
        if (world)
        {
            targetFwd = Vector3.Dot(center, fwd);
        }
        else
        {
            switch (settings.depthAnchor)
            {
                case FactorySpriteBillboard.DepthAnchor.Far: targetFwd = maxFwd; break;
                case FactorySpriteBillboard.DepthAnchor.Center: targetFwd = (minFwd + maxFwd) * 0.5f; break;
                default: targetFwd = minFwd; break;
            }
        }
        targetFwd += settings.depthBias;

        Vector3 contentWorldCenter = center
            + up * (targetUp - Vector3.Dot(center, up))
            + fwd * (targetFwd - Vector3.Dot(center, fwd));

        transform.position = contentWorldCenter - rot * ((Vector3)contentCenter * scale);

        // Ručný posun (x, y, z) – len WorldOriented.
        if (world) transform.position += settings.positionOffset;

        if (cam != null) lastCamRotation = cam.transform.rotation;
        applied = true;
    }

    /// <summary>
    /// Mierka spritu v režime WorldOriented (bez widthMultiplier).
    ///
    /// Os mosta vo svete = worldBaseRotation × X (horizontálny most → X,
    /// vertikálny → Z). Po pootočení (Pitch/Yaw/Roll) môže pozdĺž mosta ležať
    /// šírka ALEBO výška obrázka – preto sa rozmer pozdĺž mosta počíta z
    /// priemetu OBOCH osí obrázka:
    ///   pozdĺž  = |right·os| × šírka + |up·os| × výška
    ///   naprieč = |right·kolmica| × šírka + |up·kolmica| × výška
    /// Mierka = worldWidth / pozdĺž → segment má presne 1 tile (krátky most
    /// celú dĺžku). Ak by obrázok naprieč mostom presiahol 1 tile, mierka sa
    /// zníži tak, aby sa zmestil do 1 tile.
    /// </summary>
    private float WorldOrientedScale(Vector3 right, Vector3 up)
    {
        Vector3 axis = worldBaseRotation * Vector3.right;
        axis.y = 0f;
        axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
        Vector3 across = Vector3.Cross(Vector3.up, axis);

        float along = Mathf.Abs(Vector3.Dot(right, axis)) * contentSize.x
                    + Mathf.Abs(Vector3.Dot(up, axis)) * contentSize.y;
        float acrossSize = Mathf.Abs(Vector3.Dot(right, across)) * contentSize.x
                         + Mathf.Abs(Vector3.Dot(up, across)) * contentSize.y;

        float scale;
        if (along > 1e-4f)
            scale = worldWidth / along;
        else if (acrossSize > 1e-4f)
            scale = 1f / acrossSize;          // obrázok kolmo na os mosta – naprieč 1 tile
        else
            scale = contentSize.x > 1e-5f ? 1f / contentSize.x : 1f;

        // Naprieč mostom nikdy viac ako 1 tile.
        if (acrossSize * scale > 1f + 1e-4f)
            scale = 1f / acrossSize;

        return scale;
    }

    private Camera ResolveCamera()
    {
        if (cachedCam != null) return cachedCam;
        cachedCam = Camera.main;
        if (cachedCam == null) cachedCam = FindFirstObjectByType<Camera>();
        return cachedCam;
    }

    private static Material ResolveMaterial(int renderQueue)
    {
        if (sharedMaterials.TryGetValue(renderQueue, out Material cached) && cached != null)
            return cached;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) return null;

        var mat = new Material(sh) { name = $"CrossingSpriteMat_{renderQueue}", renderQueue = renderQueue };
        sharedMaterials[renderQueue] = mat;
        return mat;
    }
}
