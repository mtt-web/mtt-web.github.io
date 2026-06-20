---

layout: single # splash, single

title: Moja hra

permalink: "/games/indicatrix-blog/"

author_profile: true

toc: true

classes: wide

---

\## Game: Indicatrix (Working title)

The game Indicatrix (working title) is in the development stage. The programming language is C# and the game engine is Unity. I use AI tools to create the program code, but I wrote the game architecture completely myself. I don't have a specific goal for the game yet, for now it is a game that appears to be a train and vehicle transport simulator. The exact specification of the game will develop over time. I program it myself, I revise and check the entire code on my own. I am not working on the graphics side of the game yet. I will probably use some commercial purchased graphic materials from publicly available sources.

### TerrainManager.cs

```csharp
using NUnit.Framework.Constraints;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


public class TerrainManager : MonoBehaviour
{
    public static TerrainManager instance;
    public int terrainWidth, elementWidth;
    public TerrainElement terrainPrefab;
    [HideInInspector]
    public Vector3[] coordsF;
    public Vector3[] coordsI;
    [HideInInspector]
    public TerrainElement[] terrainElements;


    private const float baseHeight = 2.75f;
    private const float step = 0.25f;

    // =====================================================================
    // GLOBÁLNE VÝŠKOVÉ LIMITY TERÉNU
    //
    // Užívateľská "úroveň prevýšenia" E:
    //   E = -1 → Y = 2.75f (= baseHeight, najnižšia, "fiktívna" úroveň pod rovinou)
    //   E =  0 → Y = 3.00f (rovina terénu)
    //   E = 10 → Y = 5.50f (najvyššia povolená úroveň)
    // Prepočet výšky: Y = baseHeight + (E + 1) * step.
    //
    // Celá mapa (všetky vertexy) musí ostať v rozsahu [MinTerrainHeight,
    // MaxTerrainHeight]. Limity sú odvodené z úrovní, takže stačí zmeniť
    // Min/MaxElevationLevel a výšky sa prepočítajú.
    // =====================================================================

    public const int MinElevationLevel = -1; // Y = 2.75f
    public const int MaxElevationLevel = 10; // Y = 5.50f

    public const float MinTerrainHeight = baseHeight + (MinElevationLevel + 1) * step; // 2.75f
    public const float MaxTerrainHeight = baseHeight + (MaxElevationLevel + 1) * step; // 5.50f


    // =====================================================================
    // PARAMETRE GENEROVANIA TERÉNU (laditeľné v Inspectore)
    //
    // Hodnoty seaFloorLevel / maxLandLevel sú v užívateľskej úrovni E
    // (rovnaká škála ako vyššie: E=-1 = hladina vody, E=0 = rovina).
    // Vnútorne sa prepočítavajú na "coordsI" úroveň = E + 1.
    // =====================================================================

    [Header("Generovanie terénu")]
    [Tooltip("0 = pri každom spustení iný náhodný terén; iná hodnota = opakovateľný (rovnaký) terén.")]
    public int terrainSeed = 0;

    [Tooltip("Mierka šumu. Menšie číslo = väčšie a plynulejšie útvary (viac rovín); väčšie = členitejší terén.")]
    public float noiseScale = 0.03f;

    [Tooltip("Počet vrstiev šumu (detail). Viac = drobnejšie nerovnosti navrch.")]
    [Range(1, 8)]
    public int octaves = 4;

    [Tooltip("Útlm amplitúdy medzi vrstvami šumu (0..1). Nižšie = hladší terén.")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Tooltip("Nárast frekvencie medzi vrstvami šumu (typicky 2).")]
    public float lacunarity = 2f;

    [Tooltip("Podiel mapy (0..1), ktorý spadne na vodu/pobrežie. Vyššie = viac vody.")]
    [Range(0f, 0.9f)]
    public float seaThreshold = 0.30f;

    [Tooltip("Najnižšia úroveň E dna pod vodou. Pod -1 = terén klesne pod hladinu a voda je viditeľná.")]
    public int seaFloorLevel = -2;

    [Tooltip("Najvyššia úroveň E pevniny (kopce). Max 10.")]
    public int maxLandLevel = 8;

    [Tooltip("Sploštenie nížin (mocnina). >1 = viac rovín a menej kopcov, =1 = lineárne.")]
    public float flatBias = 1.5f;


    /// <summary>
    /// Vráti true, ak by ďalší krok úpravy terénu na vrchole s aktuálnou výškou
    /// snapY prekročil povolený rozsah:
    ///   • levelUp == true  → nová výška (snapY + step) by presiahla MaxTerrainHeight,
    ///   • levelUp == false → nová výška (snapY - step) by klesla pod MinTerrainHeight.
    ///
    /// Stačí kontrolovať klikaný vrchol: pri LevelUp je práve on najvyšším a pri
    /// LevelDown najnižším bodom zmeny – kaskáda (TerrainCollapse) susedov len
    /// doťahuje smerom k nemu, takže limit neprekročí nič, čo neprekročí on.
    /// </summary>
    public bool WouldExceedElevationLimit(float snapY, bool levelUp)
    {
        const float EPS = 0.0001f;

        if (levelUp)
            return snapY + step > MaxTerrainHeight + EPS;
        else
            return snapY - step < MinTerrainHeight - EPS;
    }


    void Start()
    {
        instance = this;

        coordsF = CreateMap(3.00f);
        coordsI = CreateMap(0);

        // Náhodný terén ešte PRED postavením mesh-ov, aby sa už prvé
        // vykreslenie elementov postavilo z vygenerovaných výšok.
        GenerateRandomTerrain();

        CreateTerrainElements();
    }


    // =====================================================================
    // GENERÁTOR NÁHODNÉHO TERÉNU
    //
    // Postup (inšpirované OpenTTD/TTD – plynulé útvary, veľa rovín):
    //   1) fraktálny Perlin šum (fBm) → plynulé výškové pole 0..1
    //   2) mapovanie šumu na úroveň E s "hladinou" a sploštením nížin
    //   3) EnforceMaxSlope – dotiahnutie na pravidlo "susedia max o 1 úroveň"
    //      (rovnaké pravidlo ako TerrainCollapse v editore) → vznikajú
    //      prirodzené svahy a rovinné plató
    //   4) prepočet coordsI → coordsF (reálne Y výšky pre mesh)
    //
    // Vodná vrstva (child[0] v TerrainElement) sa NEMENÍ – ostáva na Y=2.75.
    // =====================================================================
    public void GenerateRandomTerrain()
    {
        if (coordsI == null || coordsF == null)
            return;

        int size = terrainWidth + 1;

        // Seed: 0 = nový náhodný pri každom spustení.
        if (terrainSeed == 0)
            UnityEngine.Random.InitState(Environment.TickCount);
        else
            UnityEngine.Random.InitState(terrainSeed);

        // Náhodný posun do šumového poľa → iná mapa pri každom behu.
        float offsetX = UnityEngine.Random.Range(-100000f, 100000f);
        float offsetZ = UnityEngine.Random.Range(-100000f, 100000f);

        // Bezpečné orezanie limitov (v úrovni E).
        int maxLandE = Mathf.Clamp(maxLandLevel, MinElevationLevel, MaxElevationLevel);
        int seaFloorE = Mathf.Clamp(seaFloorLevel, -5, maxLandE);

        for (int z = 0; z <= terrainWidth; z++)
        {
            for (int x = 0; x <= terrainWidth; x++)
            {
                int idx = z * size + x;

                // 1) fBm Perlin – súčet niekoľkých vrstiev šumu.
                float amp = 1f, freq = 1f, sum = 0f, ampSum = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    float sx = offsetX + x * noiseScale * freq;
                    float sz = offsetZ + z * noiseScale * freq;
                    sum += amp * Mathf.PerlinNoise(sx, sz);
                    ampSum += amp;
                    amp *= persistence;
                    freq *= lacunarity;
                }
                float n = (ampSum > 0f) ? Mathf.Clamp01(sum / ampSum) : 0f;

                // 2) Mapovanie šumu → úroveň E.
                float e;
                if (n < seaThreshold)
                {
                    // Voda/pobrežie: od dna (seaFloorE) po hladinu (E=-1).
                    float t = (seaThreshold > 0f) ? n / seaThreshold : 0f;
                    e = Mathf.Lerp(seaFloorE, MinElevationLevel, t);
                }
                else
                {
                    // Pevnina: od roviny (E=0) nahor, so sploštením nížin.
                    float t = (n - seaThreshold) / (1f - seaThreshold);
                    t = Mathf.Pow(t, Mathf.Max(0.01f, flatBias));
                    e = Mathf.Lerp(0f, maxLandE, t);
                }

                int eRounded = Mathf.Clamp(Mathf.RoundToInt(e), seaFloorE, maxLandE);

                // coordsI úroveň = E + 1 (rovnaký vzťah ako ConvertCoordsFI).
                coordsI[idx].x = x;
                coordsI[idx].z = z;
                coordsI[idx].y = eRounded + 1;
            }
        }

        // 3) Dotiahnutie na max sklon 1 úrovne medzi susedmi (TT pravidlo).
        EnforceMaxSlope(coordsI);

        // 4) Prepočet na reálne Y výšky používané mesh-om.
        ConvertCoordsFI(false);
    }


    /// <summary>
    /// Zabezpečí, že žiadne dva susedné vrcholy sa nelíšia o viac než 1 úroveň
    /// (rovnaké pravidlo ako TerrainCollapse). Pracuje "iba znižovaním":
    /// každý vrchol môže byť nanajvýš o 1 nad svojím NAJNIŽŠÍM susedom, inak sa
    /// zníži. Tým sa zachovajú nížiny/voda a zo strmých vrcholov vzniknú plynulé
    /// svahy a rovinné plató (presne TT/OpenTTD vzhľad). Postup je monotónne
    /// klesajúci a celočíselný, takže vždy skonverguje.
    /// </summary>
    private void EnforceMaxSlope(Vector3[] ci)
    {
        int size = terrainWidth + 1;

        bool changed = true;
        while (changed)
        {
            changed = false;

            for (int z = 0; z <= terrainWidth; z++)
            {
                for (int x = 0; x <= terrainWidth; x++)
                {
                    int idx = z * size + x;
                    float h = ci[idx].y;

                    float minN = float.MaxValue;
                    if (x > 0) minN = Mathf.Min(minN, ci[idx - 1].y);
                    if (x < terrainWidth) minN = Mathf.Min(minN, ci[idx + 1].y);
                    if (z > 0) minN = Mathf.Min(minN, ci[idx - size].y);
                    if (z < terrainWidth) minN = Mathf.Min(minN, ci[idx + size].y);

                    if (h > minN + 1f)
                    {
                        ci[idx].y = minN + 1f;
                        changed = true;
                    }
                }
            }
        }
    }



    public void TerrainCollapse(int startIndex)
    {
        // Reálny beh nad živým coordsI (pôvodné správanie zachované).
        TerrainCollapse(startIndex, coordsI);
    }

    /// <summary>
    /// Jadro collapse algoritmu parametrizované poľom integer-výšok (ci).
    /// Volá sa buď nad reálnym coordsI, alebo nad pracovnou KÓPIOU pri
    /// simulácii (PredictVertexLevelChanges) – vďaka tomu sa logika
    /// nedupluje a simulácia presne zodpovedá reálnemu výsledku.
    /// </summary>
    private void TerrainCollapse(int startIndex, Vector3[] ci)
    {
        int size = terrainWidth + 1;

        Queue<int> queue = new Queue<int>();
        HashSet<int> visited = new HashSet<int>();

        queue.Enqueue(startIndex);
        visited.Add(startIndex);

        while (queue.Count > 0)
        {
            int currentIndex = queue.Dequeue();

            Vector3 current = ci[currentIndex];
            int cx = (int)current.x;
            int cz = (int)current.z;
            float currentHeight = current.y;

            Vector2Int[] directions =
            {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

            foreach (var dir in directions)
            {
                int nx = cx + dir.x;
                int nz = cz + dir.y;

                if (nx < 0 || nz < 0 || nx > terrainWidth || nz > terrainWidth)
                    continue;

                int neighborIndex = nz * size + nx;

                float neighborHeight = ci[neighborIndex].y;
                float delta = Mathf.Abs(currentHeight - neighborHeight);

                if (delta > 1)
                {
                    if (neighborHeight < currentHeight)
                        ci[neighborIndex].y = currentHeight - 1;
                    else
                        ci[neighborIndex].y = currentHeight + 1;

                    if (!visited.Contains(neighborIndex))
                    {
                        queue.Enqueue(neighborIndex);
                        visited.Add(neighborIndex);
                    }
                }
            }
        }
    }




    public void TerrainVertexLevel(float snapX, float snapY, float snapZ, bool levelUp)
    {
        int coordClick = 0;

        int size = terrainWidth + 1;

        int centerX = Mathf.RoundToInt(snapX);
        int centerZ = Mathf.RoundToInt(snapZ);

        int minX = Mathf.Max(0, centerX - 10);
        int maxX = Mathf.Min(terrainWidth, centerX + 10);

        int minZ = Mathf.Max(0, centerZ - 10);
        int maxZ = Mathf.Min(terrainWidth, centerZ + 10);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * size + x;

                if (coordsF[i].x == snapX && coordsF[i].y == snapY && coordsF[i].z == snapZ)
                {
                    coordsF[i].y += levelUp ? 0.25f : -0.25f;
                    coordClick = i;
                }
            }
        }

        ConvertCoordsFI(true);
        TerrainCollapse(coordClick);
        ConvertCoordsFI(false);

        UpdateRegion(coordClick);
    }


    /// <summary>
    /// SIMULÁCIA (dry-run) operácie TerrainVertexLevel BEZ akejkoľvek zmeny
    /// reálneho terénu. Vráti grid-súradnice [vx,vz] VŠETKÝCH vrcholov, ktorých
    /// výška Y by sa po klikoch (vrátane kaskádového TerrainCollapse) zmenila.
    ///
    /// PREČO: úprava jedného vrcholu môže cez TerrainCollapse posunúť aj mnoho
    /// okolitých vrcholov (aby susedné výškové úrovne nelíšili o viac než 1).
    /// Volajúci (GameManager) tak vie ešte PRED reálnou úpravou skontrolovať,
    /// či by sa niektorý z dotknutých vrcholov dotkol obsadeného tile, a ak áno,
    /// operáciu vôbec nespustiť.
    ///
    /// Celý výpočet beží nad KÓPIAMI coordsF/coordsI – živé dáta ostávajú
    /// nedotknuté a mesh sa neprestavuje.
    /// </summary>
    public List<Vector2Int> PredictVertexLevelChanges(float snapX, float snapY, float snapZ, bool levelUp)
    {
        var changed = new List<Vector2Int>();

        if (coordsF == null || coordsI == null)
            return changed;

        int size = terrainWidth + 1;

        // Pracovné kópie – reálny terén nemodifikujeme.
        Vector3[] simF = (Vector3[])coordsF.Clone();
        Vector3[] simI = (Vector3[])coordsI.Clone();

        // 1) Rovnaké vyhľadanie klikaného vrcholu ako v TerrainVertexLevel.
        int coordClick = 0;

        int centerX = Mathf.RoundToInt(snapX);
        int centerZ = Mathf.RoundToInt(snapZ);

        int minX = Mathf.Max(0, centerX - 10);
        int maxX = Mathf.Min(terrainWidth, centerX + 10);

        int minZ = Mathf.Max(0, centerZ - 10);
        int maxZ = Mathf.Min(terrainWidth, centerZ + 10);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int i = z * size + x;

                if (simF[i].x == snapX && simF[i].y == snapY && simF[i].z == snapZ)
                {
                    simF[i].y += levelUp ? 0.25f : -0.25f;
                    coordClick = i;
                }
            }
        }

        // 2) F→I, kaskádový collapse, I→F – všetko nad kópiami.
        ConvertCoordsFI(true, simF, simI);
        TerrainCollapse(coordClick, simI);
        ConvertCoordsFI(false, simF, simI);

        // 3) Pozbierame vrcholy, ktorých Y sa oproti živému stavu zmenilo.
        for (int i = 0; i < coordsF.Length; i++)
        {
            if (!Mathf.Approximately(simF[i].y, coordsF[i].y))
                changed.Add(new Vector2Int(i % size, i / size));
        }

        return changed;
    }





    private Vector3[] CreateMap(float initialHeight)
    {
        int size = (terrainWidth + 1) * (terrainWidth + 1);
        Vector3[] map = new Vector3[size];

        int i = 0;
        for (int z = 0; z <= terrainWidth; z++)
        {
            for (int x = 0; x <= terrainWidth; x++)
            {
                map[i++] = new Vector3(x, initialHeight, z);
            }
        }

        return map;
    }




    public void ConvertCoordsFI(bool floatToInt)
    {
        // Reálna konverzia nad živými poliami (pôvodné správanie).
        ConvertCoordsFI(floatToInt, coordsF, coordsI);
    }

    /// <summary>
    /// Konverzia float↔int výšok parametrizovaná poľami – aby ju vedela
    /// použiť aj simulácia (PredictVertexLevelChanges) nad kópiami.
    /// </summary>
    private void ConvertCoordsFI(bool floatToInt, Vector3[] f, Vector3[] iArr)
    {
        for (int i = 0; i < f.Length; i++)
        {
            if (floatToInt)
            {
                iArr[i].x = f[i].x;
                iArr[i].z = f[i].z;

                iArr[i].y = Mathf.RoundToInt((f[i].y - baseHeight) / step);
            }
            else
            {
                f[i].x = iArr[i].x;
                f[i].z = iArr[i].z;

                f[i].y = baseHeight + iArr[i].y * step;
            }
        }
    }




    private void CreateTerrainElements()
    {
        int tilesPerSide = terrainWidth / elementWidth;
        terrainElements = new TerrainElement[tilesPerSide * tilesPerSide];

        for (int i = 0, z = 0; z < tilesPerSide; z++)
        {
            for (int x = 0; x < tilesPerSide; x++, i++)
            {
                TerrainElement elementInstance = Instantiate(terrainPrefab, this.transform);
                elementInstance.Initialize(x, z);
                terrainElements[i] = elementInstance;
            }
        }
    }



    private void UpdateRegion(int centerIndex)
    {
        Vector3 center = coordsI[centerIndex];

        int minX = Mathf.Max(0, (int)center.x - 10);
        int maxX = Mathf.Min(terrainWidth, (int)center.x + 10);

        int minZ = Mathf.Max(0, (int)center.z - 10);
        int maxZ = Mathf.Min(terrainWidth, (int)center.z + 10);

        int elementSize = elementWidth;
        int tilesPerSide = terrainWidth / elementSize;

        HashSet<int> elementsToUpdate = new HashSet<int>();

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int elementX = x / elementSize;
                int elementZ = z / elementSize;

                int elementIndex = elementZ * tilesPerSide + elementX;

                elementsToUpdate.Add(elementIndex);
            }
        }

        foreach (int i in elementsToUpdate)
        {
            terrainElements[i].Rebuild();
        }
    }

}
```


### TerrainElement.cs

```csharp
using System;
using Unity.VisualScripting;
using UnityEngine;


[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TerrainElement : MonoBehaviour
{
    private Mesh mesh;
    private MeshFilter meshFilter;
    private Vector3[] verts;
    private Color[] colors;
    private int[] tris;
    private Vector2[] uvs;

    private int indexX;
    private int indexZ;



    private bool TriangulationCheck(Vector3 coord0, Vector3 coord1)
    {
        if (coord0.y == coord1.y)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private Color VertexColor(float height)
    {
        if (height < 1.5) //1.5
        {
            return Color.black;
        }
        else if (height < 3.5) //3.5
        {
            return Color.red;
        }
        else
        {
            return Color.green;
        }
    }



    public void Initialize(int index_x, int index_z)
    {
        indexX = index_x;
        indexZ = index_z;

        this.name = index_x + "_" + index_z;

        meshFilter = GetComponent<MeshFilter>();
        mesh = new Mesh();
        meshFilter.mesh = mesh;

        BuildMesh();
    }


    public void BuildMesh()
    {
        //getting width of element and terrain
        int width = TerrainManager.instance.elementWidth;
        int tWidth = TerrainManager.instance.terrainWidth;

        //setting the water position
        this.transform.GetChild(0).transform.position = new Vector3(indexX * width + width / 2, 2.75f, indexZ * width + width / 2);

        verts = new Vector3[width * width * 6];
        colors = new Color[verts.Length];
        uvs = new Vector2[verts.Length];
        tris = new int[verts.Length];

        //pivot point inside of the coord array
        int origin = indexX * width + indexZ * width * (tWidth + 1);

        for (int i = 0, z = 0; z < width; z++)
        {
            //outer loop for z-axis
            for (int x = 0; x < width; x++, i += 6)
            {
                //setting verts                    
                verts[i] = TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z)];
                verts[i + 1] = TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z)];
                verts[i + 2] = TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z + 1)];
                verts[i + 3] = TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z + 1)];

                colors[i] = VertexColor(TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z)].y);
                colors[i + 1] = VertexColor(TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z)].y);
                colors[i + 2] = VertexColor(TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z + 1)].y);
                colors[i + 3] = VertexColor(TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z + 1)].y);



                if (TriangulationCheck(TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z)], TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z + 1)]))
                {
                    //setting extra vertices
                    verts[i + 4] = TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z)];
                    verts[i + 5] = TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z + 1)];

                    //setting vertex colors
                    colors[i + 4] = VertexColor(TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z)].y);
                    colors[i + 5] = VertexColor(TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z + 1)].y);

                    //setting tris
                    tris[i] = i;
                    tris[i + 1] = i + 1;
                    tris[i + 2] = i + 2;
                    tris[i + 3] = i + 4;
                    tris[i + 4] = i + 5;
                    tris[i + 5] = i + 3;
                    //setting uvs
                    uvs[i] = new Vector2(0, 0);
                    uvs[i + 1] = new Vector2(0, 1);
                    uvs[i + 2] = new Vector2(1, 1);
                    uvs[i + 3] = new Vector2(1, 0);
                    uvs[i + 4] = new Vector2(0, 0);
                    uvs[i + 5] = new Vector2(1, 1);
                }

                else
                {
                    //setting extra vertices
                    verts[i + 4] = TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z + 1)];
                    verts[i + 5] = TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z)];

                    //setting vertex colors
                    colors[i + 4] = VertexColor(TerrainManager.instance.coordsF[origin + (x * (tWidth + 1) + z + 1)].y);
                    colors[i + 5] = VertexColor(TerrainManager.instance.coordsF[origin + ((x + 1) * (tWidth + 1) + z)].y);

                    //setting tris
                    tris[i] = i;
                    tris[i + 1] = i + 1;
                    tris[i + 2] = i + 3;
                    tris[i + 3] = i + 4;
                    tris[i + 4] = i + 5;
                    tris[i + 5] = i + 2;
                    //setting uvs
                    uvs[i] = new Vector2(0, 0);
                    uvs[i + 1] = new Vector2(0, 1);
                    uvs[i + 2] = new Vector2(1, 1);
                    uvs[i + 3] = new Vector2(1, 0);
                    uvs[i + 4] = new Vector2(1, 0);
                    uvs[i + 5] = new Vector2(0, 1);
                }


            }
        }

        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();

        this.AddComponent<MeshCollider>();
    }


    public void Rebuild()
    {
        mesh.Clear();
        BuildMesh();
    }



}
```


### FactorySystem.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FactorySystem
/// ------------------------------------------------------------------
/// Dátová vrstva pre továrne. Doteraz boli továrne reprezentované iba ako
/// skupina tilov na mape (IndicatrixAPI.SetTile zapíše tileID 4/5 + stateID
/// = FactoryConstructionMode do footprintu). To stačí na VYKRESLENIE továrne,
/// ale nenesie to žiadne "ekonomické" informácie – koľko suroviny továreň
/// prijíma, vydáva, aké má kapacity.
///
/// Tento súbor dopĺňa práve tieto informácie a NEZASAHUJE do tile enginu:
///   - ResourceType      – evidenčné čísla surovín (0/1/2, rozšíriteľné).
///   - ResourceSlot      – jeden riadok [typ, aktuálne množstvo, max množstvo].
///   - FactoryDefinition – nemenná ŠABLÓNA jedného typu továrne
///                         (Name + Load/UnLoad sloty + odkaz na footprint).
///   - FactoryDatabase   – 4 fixné definície, indexované cez
///                         GameManager.FactoryConstructionMode (rovnaký vzor
///                         ako IndicatrixAPI.GetFactoryFootprint).
///   - FactoryInstance   – konkrétna POLOŽENÁ továreň na mape: vlastná kópia
///                         slotov (množstvá sa počas hry menia) + pozícia.
///
/// PREČO List<ResourceSlot> A NIE Hashtable / Dictionary:
///   Zadanie hovorí, že továren môže prijať/vydať 0, 1 alebo VIAC surovín
///   a uvádza zápis "Load: [2,0,500], [3,0,750]". Dictionary<ResourceType,…>
///   by znemožnil dva sloty s rovnakou surovinou a pridáva réžiu hashovania
///   bez úžitku (slotov je málo, 0–niekoľko). List<ResourceSlot> presne
///   kopíruje zápis zo zadania, podporuje 0/1/N slotov a je triviálne
///   serializovateľný pre Unity Inspector.
/// ------------------------------------------------------------------
/// </summary>

// ------------------------------------------------------------------
// SUROVINY
// ------------------------------------------------------------------

/// <summary>
/// Typ suroviny. Hodnota enumu = "evidenčné číslo" zo zadania, takže sa dá
/// priamo (int)ResourceType použiť ako ID a opačne (ResourceType)id naspäť.
///
/// Tabuľka surovín (zadanie):
///   0  = None                 (žiadna surovina)
///   1  = Coal                 (uhlie)
///   2  = Wood                 (drevo)
///   3  = IronOre              (železná ruda)
///   4  = Gold                 (zlatá ruda)
///   5  = Silver               (strieborná ruda)
///   6  = Livestock            (dobytok)
///   7  = Grain                (pšenica)
///   8  = Oil                  (ropa)
///   9  = Boards               (dosky)
///   10 = Plastic              (plast)
///   11 = Meat                 (mäso)
///   12 = Flour                (múka)
///   13 = Metals               (kovy)
///   14 = Glass                (sklo)
///   15 = Furniture            (nábytok)
///   16 = ElectronicsProducts  (elektronické produkty)
///
/// Rozšírenie: budúcu surovinu (ďalšie evidenčné číslo) stačí dopísať sem.
/// </summary>
public enum ResourceType
{
    None = 0,    // žiadna surovina
    Coal = 1,    // uhlie
    Wood = 2,    // drevo
    IronOre = 3,    // železná ruda
    Gold = 4,    // zlatá ruda
    Silver = 5,    // strieborná ruda
    Livestock = 6,    // dobytok
    Grain = 7,    // pšenica
    Oil = 8,    // ropa
    Boards = 9,    // dosky
    Plastic = 10,   // plast
    Meat = 11,   // mäso
    Flour = 12,   // múka
    Metals = 13,   // kovy
    Glass = 14,   // sklo
    Furniture = 15,   // nábytok
    ElectronicsProducts = 16    // elektronické produkty
}

/// <summary>
/// ResourceSlot
/// ------------------------------------------------------------------
/// Jeden riadok zo zadania: [typ suroviny, aktuálne množstvo, maximálne
/// množstvo] – t.j. [Type, Amount, Capacity].
///
/// Používa sa rovnako v zozname Load (príjem) aj UnLoad (výdaj). Jedna
/// továreň môže mať takýchto slotov 0, 1 alebo viac (pozri FactoryDefinition).
///
/// POZN.: Trieda (nie struct) je zvolená zámerne – FactoryInstance si musí
/// drža? MENITEĽNÉ množstvá (Amount sa počas hry mení nakladaním/vykladaním
/// vlakov). Reference type uľahčuje úpravu cez List bez index-prepisovania.
/// </summary>
[System.Serializable]
public class ResourceSlot
{
    /// <summary>Typ suroviny tohto slotu (Coal / Wood / …).</summary>
    public ResourceType type;

    /// <summary>Aktuálne množstvo suroviny v slote (0 .. capacity).</summary>
    public int amount;

    /// <summary>Maximálne množstvo suroviny, ktoré sa do slotu zmestí.</summary>
    public int capacity;

    public ResourceSlot(ResourceType type, int amount, int capacity)
    {
        this.type = type;
        this.amount = amount;
        this.capacity = capacity;
    }

    /// <summary>Hlboká kópia slotu – pri vytváraní FactoryInstance z definície,
    /// aby inštancia nezdielala menené množstvá so šablónou.</summary>
    public ResourceSlot Clone()
    {
        return new ResourceSlot(type, amount, capacity);
    }

    // Pomocné dotazy / operácie ---------------------------------------------------------------------------

    /// <summary>Voľné miesto v slote (koľko ešte možno pridať).</summary>
    public int FreeSpace => Mathf.Max(0, capacity - amount);

    public bool IsFull => amount >= capacity;
    public bool IsEmpty => amount <= 0;

    /// <summary>
    /// Pridá do slotu maximálne <paramref name="requested"/> jednotiek,
    /// orezané kapacitou. Vráti, koľko sa SKUTOČNE pridaťo.
    /// (Použiteľné napr. keď vlak/továreň "naplní" UnLoad sklad.)
    /// </summary>
    public int Add(int requested)
    {
        int added = Mathf.Clamp(requested, 0, FreeSpace);
        amount += added;
        return added;
    }

    /// <summary>
    /// Odoberie zo slotu maximálne <paramref name="requested"/> jednotiek,
    /// orezané dostupným množstvom. Vráti, koľko sa SKUTOČNE odobralo.
    /// (Použiteľné napr. keď vlak "naloží" zo skladu továrne.)
    /// </summary>
    public int Remove(int requested)
    {
        int removed = Mathf.Clamp(requested, 0, amount);
        amount -= removed;
        return removed;
    }

    public override string ToString() => $"[{(int)type} {type}, {amount}/{capacity}]";
}

// ------------------------------------------------------------------
// DEFINÍCIA TOVÁRNE (nemenná šablóna)
// ------------------------------------------------------------------

/// <summary>
/// FactoryDefinition
/// ------------------------------------------------------------------
/// Nemenná ŠABLÓNA jedného typu továrne – "vzor", podľa ktorého sa pri
/// položení vytvorí konkrétna FactoryInstance.
///
/// Obsahuje:
///   - Name   : názov továrne (string), napr. "Coal Mine".
///   - Mode   : ku ktorému FactoryConstructionMode definícia patrí
///              (prepojenie na GameManager / IndicatrixAPI / UI).
///   - TileID : 4 = Factory, 5 = Processing – zhodné s tým, čo posiela
///              GameManager do IndicatrixAPI.SetTile.
///   - Load   : zoznam slotov, ktoré továreň PRIJÍMA (0, 1 alebo N).
///   - UnLoad : zoznam slotov, ktoré továreň VYDÁVA (0, 1 alebo N).
///   - MapColor : farba, ktorou sa táto továreň kreslí do STATUS MAPY
///              (StatusMapMenuUI → IndustryView). Je to ČISTO mapová,
///              vnútorná reprezentácia – herná textúra továrne ani jej
///              vzhľad v hre sa NEMENÍ. Každý typ továrne má vlastnú,
///              odlišnú farbu, aby boli na prehľadovej mape rozlíšiteľné.
///
/// "Load: 0" zo zadania = prázdny zoznam (Count == 0).
/// "Load: [1,0,300]"    = zoznam s jedným ResourceSlot(Coal, 0, 300).
/// "Load: [2,0,500],[3,0,750]" = zoznam s dvoma slotmi (budúce rozšírenie).
///
/// FOOTPRINT zostáva definovaný v IndicatrixAPI.GetFactoryFootprint(Mode) –
/// FactorySystem ho ZÁMERNE neduplikuje, len naň cez Mode odkazuje, aby
/// veľkosti továrne mali aj naňalej jediný zdroj pravdy.
/// </summary>
[System.Serializable]
public class FactoryDefinition
{
    /// <summary>
    /// FIXNÉ číselné ID typu továrne. Hodnota je pridelená v PORADÍ, v akom
    /// továrne nasledujú v FactoryDatabase (1. Coal Mine = 0, 2. Forest = 1,
    /// … 16. Glass Factory = 15) a počas hry sa NEMENÍ. Slúži ako stabilný
    /// kľúč typu (napr. pre uloženie hry, štatistiky, UI). Každá položená
    /// FactoryInstance si toto ID prevezme do svojho rovnomenného poľa.
    /// </summary>
    public int ID;

    public string Name;
    public GameManager.FactoryConstructionMode Mode;
    public int TileID;                       // 4 = Factory, 5 = Processing

    public List<ResourceSlot> Load;          // príjem surovín  (0..N slotov)
    public List<ResourceSlot> UnLoad;        // výdaj surovín   (0..N slotov)

    /// <summary>
    /// Farba pre vykreslenie tejto továrne do STATUS MAPY (IndustryView).
    /// LEN pre mapový účel – nemení hernú textúru ani vzhľad v hre.
    /// </summary>
    public Color MapColor;

    /// <summary>
    /// Čas výstavby tejto továrne v SEKUNDÁCH (definované per-typ v kóde).
    /// Po položení beží odpočet a kým progres &lt; 100 %, je továreň "vo
    /// výstavbe" – zablokovaná pre zmenu množstiev/kapacít aj pre demoláciu.
    /// Hodnota &lt;= 0 znamená, že továreň je hotová okamžite (bez výstavby).
    /// </summary>
    public float BuildingTime;

    /// <summary>
    /// Cena (náklad) na postavenie tejto továrne v kreditoch (CR). Odpočíta sa
    /// z konta (GameEconomy.Balance) HNEĎ pri položení továrne – hráč nemusí
    /// čakať na dokončenie výstavby (100 %). Pri demolácii sa vráti 50 % z tejto
    /// sumy (rovnaká politika ako pri RAIL/ROAD – pozri ConstructionCosts).
    ///
    /// Centrálne sa cena číta cez ConstructionCosts.FactoryBuildCost(mode),
    /// ktorá ju berie práve odtiaľto – hodnota Cost je teda jediný zdroj pravdy.
    /// Hodnota &lt;= 0 znamená "zadarmo".
    /// </summary>
    public int Cost;

    public FactoryDefinition(
        int id,
        string name,
        GameManager.FactoryConstructionMode mode,
        int tileID,
        List<ResourceSlot> load,
        List<ResourceSlot> unload,
        Color mapColor,
        float buildingTime,
        int cost)
    {
        ID = id;
        Name = name;
        Mode = mode;
        TileID = tileID;
        Load = load ?? new List<ResourceSlot>();
        UnLoad = unload ?? new List<ResourceSlot>();
        MapColor = mapColor;
        BuildingTime = buildingTime;
        Cost = cost;
    }

    /// <summary>Footprint (ve?kos? + anchor) tejto továrne – jediný zdroj
    /// pravdy zostáva v IndicatrixAPI.</summary>
    public IndicatrixAPI.FactoryFootprint GetFootprint()
    {
        return IndicatrixAPI.GetFactoryFootprint(Mode);
    }

    /// <summary>True, ak továreň nejakú surovinu prijíma.</summary>
    public bool AcceptsResources => Load.Count > 0;

    /// <summary>True, ak továreň nejakú surovinu vydáva.</summary>
    public bool ProducesResources => UnLoad.Count > 0;
}

// ------------------------------------------------------------------
// DATABÁZA FIXNÝCH TOVÁRNÍ
// ------------------------------------------------------------------

/// <summary>
/// FactoryDatabase
/// ------------------------------------------------------------------
/// Statická databáza definícií. Drží fixné FactoryDefinition a sprístupní
/// ich cez GameManager.FactoryConstructionMode – presne ten istý vzor ako
/// IndicatrixAPI.GetFactoryFootprint(mode).
///
/// Každý slot je [type, amount, capacity] = [evidenčné číslo suroviny,
/// aktuálne množstvo, maximálna kapacita]. amount je počiatočná hodnota pri
/// položení a počas hry sa mení (FactoryInstance má vlastnú kópiu slotov).
///
/// FIXNÉ HODNOTY (podľa zadania):
///
///   Factory (tileID 4) – ťažba:
///     1.  Coal Mine     – Load: 0   | UnLoad: [1,0,1000]   (uhlie 0/1000)
///     2.  Forest        – Load: 0   | UnLoad: [2,0,1000]   (drevo 0/1000)
///     3.  Iron Ore Mine – Load: 0   | UnLoad: [3,0,1000]   (žel. ruda 0/1000)
///     4.  Gold Mine     – Load: 0   | UnLoad: [4,0,1000]   (zlatá ruda 0/1000)
///     5.  Silver Mine   – Load: 0   | UnLoad: [5,0,1000]   (strieb. ruda 0/1000)
///     6.  Farm          – Load: 0   | UnLoad: [6,0,3000] (dobytok 0/3000),
///                                              [7,0,1000] (pšenica 0/1000)
///     7.  Oil Wells     – Load: 0   | UnLoad: [8,0,10000]  (ropa 0/10000)
///
///   Processing (tileID 5) – spracovanie:
///     8.  Power Station       – Load: [1,0,5000] (uhlie 0/5000) | UnLoad: 0
///     9.  Sawmill             – Load: [2,0,3000] (drevo 0/3000) |
///                               UnLoad:[9,0,1000] (dosky 0/1000)
///     10. Oil Refinery        – Load: [8,0,8000] (ropa 0/8000)  |
///                               UnLoad:[10,0,2500] (plast 0/2500)
///     11. Electronics Factory – Load: [10,0,1000] (plast 0/1000),
///                                      [14,0,1000] (sklo 0/1000),
///                                      [13,0,1000] (kovy 0/1000) |
///                               UnLoad:[16,0,2500] (elektronika 0/2500)
///     12. Furniture Factory   – Load: [10,0,1000] (plast 0/1000),
///                                      [14,0,1000] (sklo 0/1000),
///                                      [13,0,1000] (kovy 0/1000),
///                                      [9,0,1000]  (dosky 0/1000) |
///                               UnLoad:[15,0,5000] (nábytok 0/5000)
///     13. Slaughterhouse      – Load: [6,0,750] (dobytok 0/750) |
///                               UnLoad:[11,0,1000] (mäso 0/1000)
///     14. Grain Factory       – Load: [7,0,1500] (pšenica 0/1500) |
///                               UnLoad:[12,0,2000] (múka 0/2000)
///     15. Smelter             – Load: [3,0,3000] (žel. ruda 0/3000) |
///                               UnLoad:[13,0,5000] (kovy 0/5000)
///     16. Glass Factory       – Load: [13,0,3000] (kovy 0/3000) |
///                               UnLoad:[14,0,1000] (sklo 0/1000)
///
/// Pridanie ďalšej továrne = pridať FactoryConstructionMode do GameManager,
/// footprint do IndicatrixAPI.GetFactoryFootprint a jeden záznam sem.
/// </summary>
public static class FactoryDatabase
{
    private static readonly Dictionary<GameManager.FactoryConstructionMode, FactoryDefinition> definitions
        = new Dictionary<GameManager.FactoryConstructionMode, FactoryDefinition>
    {
        // =====================================================================
        // FACTORY (tileID 4) – ťažba surovín (Load: 0)
        // =====================================================================

        // 1. Coal Mine ---------------------------------------------------------
        // Nič neprijíma, vydáva uhlie 0/10000.
        {
            GameManager.FactoryConstructionMode.CoalMine,
            new FactoryDefinition(
                id:     0,
                name:   "Coal Mine",
                mode:   GameManager.FactoryConstructionMode.CoalMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Coal, 0, 10000)          // UnLoad: [1,0,10000]
                },
                mapColor: new Color(0.15f, 0.15f, 0.15f),
                buildingTime: 12f,
                cost:         5000)
        },

        // 2. Forest ------------------------------------------------------------
        // Nič neprijíma, vydáva drevo 0/10000.
        {
            GameManager.FactoryConstructionMode.Forest,
            new FactoryDefinition(
                id:     1,
                name:   "Forest",
                mode:   GameManager.FactoryConstructionMode.Forest,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Wood, 0, 10000)          // UnLoad: [2,0,10000]
                },
                mapColor: new Color(0.13f, 0.55f, 0.13f),
                buildingTime: 10f,
                cost:         3500)
        },

        // 3. Iron Ore Mine -----------------------------------------------------
        // Nič neprijíma, vydáva železnú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.IronOreMine,
            new FactoryDefinition(
                id:     2,
                name:   "Iron Ore Mine",
                mode:   GameManager.FactoryConstructionMode.IronOreMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.IronOre, 0, 10000)       // UnLoad: [3,0,10000]
                },
                mapColor: new Color(0.55f, 0.27f, 0.07f),
                buildingTime: 14f,
                cost:         5500)
        },

        // 4. Gold Mine ---------------------------------------------------------
        // Nič neprijíma, vydáva zlatú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.GoldMine,
            new FactoryDefinition(
                id:     3,
                name:   "Gold Mine",
                mode:   GameManager.FactoryConstructionMode.GoldMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Gold, 0, 10000)          // UnLoad: [4,0,10000]
                },
                mapColor: new Color(1.00f, 0.84f, 0.00f),
                buildingTime: 18f,
                cost:         9000)
        },

        // 5. Silver Mine -------------------------------------------------------
        // Nič neprijíma, vydáva striebornú rudu 0/10000.
        {
            GameManager.FactoryConstructionMode.SilverMine,
            new FactoryDefinition(
                id:     4,
                name:   "Silver Mine",
                mode:   GameManager.FactoryConstructionMode.SilverMine,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Silver, 0, 10000)        // UnLoad: [5,0,10000]
                },
                mapColor: new Color(0.75f, 0.75f, 0.80f),
                buildingTime: 16f,
                cost:         8000)
        },

        // 6. Farm --------------------------------------------------------------
        // Nič neprijíma, vydáva dobytok 0/9000 a pšenicu 0/12000.
        {
            GameManager.FactoryConstructionMode.Farm,
            new FactoryDefinition(
                id:     5,
                name:   "Farm",
                mode:   GameManager.FactoryConstructionMode.Farm,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Livestock, 0, 9000),    // UnLoad: [6,0,9000]
                    new ResourceSlot(ResourceType.Grain,     0, 12000)     // UnLoad: [7,0,12000]
                },
                mapColor: new Color(0.80f, 0.75f, 0.30f),
                buildingTime: 10f,
                cost:         4000)
        },

        // 7. Oil Wells ---------------------------------------------------------
        // Nič neprijíma, vydáva ropu 0/15000.
        {
            GameManager.FactoryConstructionMode.OilWells,
            new FactoryDefinition(
                id:     6,
                name:   "Oil Wells",
                mode:   GameManager.FactoryConstructionMode.OilWells,
                tileID: 4,
                load:   new List<ResourceSlot>(),                         // Load: 0
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Oil, 0, 15000)          // UnLoad: [8,0,15000]
                },
                mapColor: new Color(0.20f, 0.10f, 0.25f),
                buildingTime: 20f,
                cost:         12000)
        },

        // =====================================================================
        // PROCESSING (tileID 5) – spracovanie surovín
        // =====================================================================

        // 8. Power Station -----------------------------------------------------
        // Prijíma uhlie 0/17000, nič nevydáva.
        {
            GameManager.FactoryConstructionMode.PowerStation,
            new FactoryDefinition(
                id:     7,
                name:   "Power Station",
                mode:   GameManager.FactoryConstructionMode.PowerStation,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Coal, 0, 17000)          // Load: [1,0,17000]
                },
                unload: new List<ResourceSlot>(),                         // UnLoad: 0
                mapColor: new Color(0.90f, 0.15f, 0.15f),
                buildingTime: 25f,
                cost:         8000)
        },

        // 9. Sawmill -----------------------------------------------------------
        // Prijíma drevo 0/7500, vydáva dosky 0/8500.
        {
            GameManager.FactoryConstructionMode.SawMill,
            new FactoryDefinition(
                id:     8,
                name:   "Sawmill",
                mode:   GameManager.FactoryConstructionMode.SawMill,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Wood, 0, 7500)          // Load: [2,0,7500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Boards, 0, 8500)        // UnLoad: [9,0,8500]
                },
                mapColor: new Color(0.70f, 0.50f, 0.25f),
                buildingTime: 15f,
                cost:         4500)
        },

        // 10. Oil Refinery -----------------------------------------------------
        // Prijíma ropu 0/12000, vydáva plast 0/7500.
        {
            GameManager.FactoryConstructionMode.OilRefinery,
            new FactoryDefinition(
                id:     9,
                name:   "Oil Refinery",
                mode:   GameManager.FactoryConstructionMode.OilRefinery,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Oil, 0, 12000)           // Load: [8,0,12000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 7500)       // UnLoad: [10,0,7500]
                },
                mapColor: new Color(0.90f, 0.20f, 0.70f),
                buildingTime: 28f,
                cost:         11000)
        },

        // 11. Electronics Factory ----------------------------------------------
        // Prijíma plast/sklo/kovy (každé 0/9500), vydáva elektroniku 0/9500.
        {
            GameManager.FactoryConstructionMode.ElectronicsFactory,
            new FactoryDefinition(
                id:     10,
                name:   "Electronics Factory",
                mode:   GameManager.FactoryConstructionMode.ElectronicsFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 9500),      // Load: [10,0,9500]
                    new ResourceSlot(ResourceType.Glass,   0, 9500),      // Load: [14,0,9500]
                    new ResourceSlot(ResourceType.Metals,  0, 9500)       // Load: [13,0,9500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.ElectronicsProducts, 0, 12500) // UnLoad: [16,0,12500]
                },
                mapColor: new Color(0.10f, 0.75f, 0.85f),
                buildingTime: 35f,
                cost:         15000)
        },

        // 12. Furniture Factory ------------------------------------------------
        // Prijíma plast/sklo/kovy/dosky (každé 0/1000), vydáva nábytok 0/18000.
        {
            GameManager.FactoryConstructionMode.FurnitureFactory,
            new FactoryDefinition(
                id:     11,
                name:   "Furniture Factory",
                mode:   GameManager.FactoryConstructionMode.FurnitureFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Plastic, 0, 12000),      // Load: [10,0,12000]
                    new ResourceSlot(ResourceType.Glass,   0, 12000),      // Load: [14,0,12000]
                    new ResourceSlot(ResourceType.Metals,  0, 12000),      // Load: [13,0,12000]
                    new ResourceSlot(ResourceType.Boards,  0, 12000)       // Load: [9,0,12000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Furniture, 0, 18000)     // UnLoad: [15,0,18000]
                },
                mapColor: new Color(0.60f, 0.35f, 0.75f),
                buildingTime: 32f,
                cost:         13000)
        },

        // 13. Slaughterhouse ---------------------------------------------------
        // Prijíma dobytok 0/7750, vydáva mäso 0/11000.
        {
            GameManager.FactoryConstructionMode.Slaughterhouse,
            new FactoryDefinition(
                id:     12,
                name:   "Slaughterhouse",
                mode:   GameManager.FactoryConstructionMode.Slaughterhouse,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Livestock, 0, 7750)      // Load: [6,0,7750]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Meat, 0, 11000)          // UnLoad: [11,0,11000]
                },
                mapColor: new Color(0.80f, 0.30f, 0.40f),
                buildingTime: 18f,
                cost:         5000)
        },

        // 14. Grain Factory ----------------------------------------------------
        // Prijíma pšenicu 0/7500, vydáva múku 0/9000.
        {
            GameManager.FactoryConstructionMode.GrainFactory,
            new FactoryDefinition(
                id:     13,
                name:   "Grain Factory",
                mode:   GameManager.FactoryConstructionMode.GrainFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Grain, 0, 7500)         // Load: [7,0,7500]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Flour, 0, 9000)         // UnLoad: [12,0,9000]
                },
                mapColor: new Color(0.95f, 0.90f, 0.70f),
                buildingTime: 16f,
                cost:         6000)
        },

        // 15. Smelter ----------------------------------------------------------
        // Prijíma železnú rudu 0/9000, vydáva kovy 0/12000.
        {
            GameManager.FactoryConstructionMode.Smelter,
            new FactoryDefinition(
                id:     14,
                name:   "Smelter",
                mode:   GameManager.FactoryConstructionMode.Smelter,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.IronOre, 0, 9000)       // Load: [3,0,9000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Metals, 0, 12000)        // UnLoad: [13,0,12000]
                },
                mapColor: new Color(1.00f, 0.45f, 0.00f),
                buildingTime: 26f,
                cost:         9500)
        },

        // 16. Glass Factory ----------------------------------------------------
        // Prijíma kovy 0/13000, vydáva sklo 0/12000.
        {
            GameManager.FactoryConstructionMode.GlassFactory,
            new FactoryDefinition(
                id:     15,
                name:   "Glass Factory",
                mode:   GameManager.FactoryConstructionMode.GlassFactory,
                tileID: 5,
                load:   new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Metals, 0, 13000)        // Load: [13,0,13000]
                },
                unload: new List<ResourceSlot>
                {
                    new ResourceSlot(ResourceType.Glass, 0, 12000)         // UnLoad: [14,0,12000]
                },
                mapColor: new Color(0.55f, 0.80f, 0.90f),
                buildingTime: 22f,
                cost:         7000)
        },
    };

    /// <summary>
    /// Vráti definíciu (šablónu) pre daný typ továrne, alebo null pre
    /// FactoryConstructionMode.None / neznámy typ.
    /// </summary>
    public static FactoryDefinition GetDefinition(GameManager.FactoryConstructionMode mode)
    {
        return definitions.TryGetValue(mode, out var def) ? def : null;
    }

    /// <summary>Všetky definície (napr. pre naplnenie UI alebo encyklopédiu).</summary>
    public static IEnumerable<FactoryDefinition> AllDefinitions => definitions.Values;
}

// ------------------------------------------------------------------
// KONKRÉTNA POLOŽENÁ TOVÁREN
// ------------------------------------------------------------------

/// <summary>
/// FactoryInstance
/// ------------------------------------------------------------------
/// Konkrétna továreň POLOŽENÁ na mape. Vzniká z FactoryDefinition pri
/// úspešnom IndicatrixAPI.SetTile(...) (pozri integráciu nižšie v komentári).
///
/// ROZDIEL oproti FactoryDefinition:
///   - Definition = nemenná šablóna, zdie?aná všetkými inštanciami toho typu.
///   - Instance   = vlastná KÓPIA Load/UnLoad slotov (množstvá sa počas hry
///                  menia – vlaky nakladajú/vykladajú) + pozícia footprintu
///                  na tile mape.
///
/// Týmto môžu na mape stá? napr. 3 Coal Mine, každá s iným zostatkom uhlia.
/// </summary>
public class FactoryInstance
{
    /// <summary>Odkaz na nemennú šablónu (Name, Mode, TileID, footprint).</summary>
    public FactoryDefinition Definition { get; private set; }

    /// <summary>Vlastná, menite?ná kópia príjmových slotov.</summary>
    public List<ResourceSlot> Load { get; private set; }

    /// <summary>Vlastná, menite?ná kópia výdajových slotov.</summary>
    public List<ResourceSlot> UnLoad { get; private set; }

    // Pozícia footprintu na tile mape
    // Lavý-dolný roh a rozmery (už po aplikovaní rotácie), ako ich vypočíta
    // IndicatrixAPI.SetTile. Slúži na spätné mapovanie tile ? továreň.

    public int OriginX { get; private set; }
    public int OriginZ { get; private set; }
    public int Width { get; private set; }
    public int Depth { get; private set; }
    public IndicatrixAPI.FactoryRotation Rotation { get; private set; }

    // =====================================================================
    // DOPLNKOVÉ POLOŽKY TOVÁRNE (ID + ekonomické / stavové príznaky)
    // =====================================================================

    /// <summary>
    /// FIXNÉ číselné ID typu továrne, prevzaté z FactoryDefinition.ID
    /// (1. Coal Mine = 0, 2. Forest = 1, … 16. Glass Factory = 15). Nastaví
    /// sa raz v konštruktore a počas hry sa nemení.
    /// </summary>
    public int ID { get; private set; }

    /// <summary>
    /// Mzda zamestnanca v tejto továrni. DEFAULT pri položení = 0.
    /// </summary>
    public int EmployeeSalary;

    /// <summary>
    /// Mzdový level (short int). DEFAULT pri položení = 0.
    /// </summary>
    public short LevelSalary;

    /// <summary>
    /// Príznak obsadenosti továrne. DEFAULT pri položení = false.
    /// </summary>
    public bool OccupancyFlag;

    public FactoryInstance(
        FactoryDefinition definition,
        int originX, int originZ,
        int width, int depth,
        IndicatrixAPI.FactoryRotation rotation)
    {
        Definition = definition;
        OriginX = originX;
        OriginZ = originZ;
        Width = width;
        Depth = depth;
        Rotation = rotation;

        // FIXNÉ ID typu prevezmeme z definície (pridelené v poradí databázy).
        ID = definition != null ? definition.ID : -1;

        // Hlboká kópia slotov – inštancia NESMIE meniť šablónu.
        Load = CloneSlots(definition.Load);
        UnLoad = CloneSlots(definition.UnLoad);

        // DEFAULTNÉ hodnoty pri položení továrne na tile map:
        // EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
        // Týmto je default zaručený pre KAŽDÚ položenú továreň bez ohľadu na
        // to, ktorou cestou inštancia vznikne.
        ApplyPlacementDefaults();

        // Po položení začína fáza výstavby (ak má definícia kladný BuildingTime).
        InitConstruction();
    }

    /// <summary>
    /// Nastaví počiatočné (default) hodnoty továrne platné v okamihu položenia
    /// na tile map: EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
    ///
    /// Jediné miesto, kde sú tieto defaulty definované. Volá ho konštruktor
    /// (záruka pre každú inštanciu) a explicitne aj GameManager priamo na mieste
    /// položenia (po FactoryRegistry.Register), aby bola požiadavka "vždy pri
    /// položení sa nastavia tieto položky" viditeľne splnená aj v ovládacom kóde.
    /// Operácia je idempotentná, takže dvojité zavolanie nič nepokazí.
    /// </summary>
    public void ApplyPlacementDefaults()
    {
        EmployeeSalary = 0;
        LevelSalary = 0;
        OccupancyFlag = false;
    }

    private static List<ResourceSlot> CloneSlots(List<ResourceSlot> source)
    {
        var copy = new List<ResourceSlot>(source.Count);
        foreach (var s in source)
            copy.Add(s.Clone());
        return copy;
    }

    // =====================================================================
    // STAV VÝSTAVBY (construction progress)
    // =====================================================================
    // Po položení továreň prejde fázou výstavby trvajúcou Definition.BuildingTime
    // sekúnd. Počas nej je IsUnderConstruction == true a továreň je zablokovaná
    // pre zmenu množstiev/kapacít (CanEditResources == false) aj pre demoláciu
    // (CanDemolish == false). Progres poháňa FactoryConstructionManager, ktorý
    // každý snímok volá AdvanceConstruction(Time.deltaTime).

    /// <summary>Koľko sekúnd výstavby už ubehlo (0 .. BuildTime).</summary>
    public float BuildElapsed { get; private set; }

    /// <summary>True, kým továreň nedosiahne 100 % výstavby.</summary>
    public bool IsUnderConstruction { get; private set; }

    /// <summary>True, ak je výstavba dokončená (opak IsUnderConstruction).</summary>
    public bool IsComplete => !IsUnderConstruction;

    /// <summary>Celkový čas výstavby v sekundách (z definície).</summary>
    public float BuildTime => Definition != null ? Definition.BuildingTime : 0f;

    /// <summary>Progres výstavby v rozsahu 0..1.</summary>
    public float BuildProgress01 =>
        BuildTime <= 0f ? 1f : Mathf.Clamp01(BuildElapsed / BuildTime);

    /// <summary>Progres výstavby v celých percentách 0..100 (na label).</summary>
    public int BuildPercent => Mathf.Clamp(Mathf.FloorToInt(BuildProgress01 * 100f), 0, 100);

    /// <summary>Zmena množstiev/kapacít je povolená až po dokončení výstavby.</summary>
    public bool CanEditResources => IsComplete;

    /// <summary>Demolácia továrne je povolená až po dokončení výstavby.</summary>
    public bool CanDemolish => IsComplete;

    /// <summary>
    /// Inicializuje stav výstavby. Volá konštruktor: ak má definícia kladný
    /// BuildingTime, továreň začína "vo výstavbe"; inak je hotová okamžite.
    /// </summary>
    private void InitConstruction()
    {
        BuildElapsed = 0f;
        IsUnderConstruction = (Definition != null && Definition.BuildingTime > 0f);
    }

    /// <summary>
    /// Posunie výstavbu o <paramref name="deltaSeconds"/>. Po dosiahnutí
    /// celkového času sa továreň označí ako dokončená (IsUnderConstruction =
    /// false) a sprístupní sa pre zmeny aj demoláciu.
    /// </summary>
    public void AdvanceConstruction(float deltaSeconds)
    {
        if (!IsUnderConstruction) return;

        BuildElapsed += deltaSeconds;
        if (BuildElapsed >= BuildTime)
        {
            BuildElapsed = BuildTime;
            IsUnderConstruction = false;
        }
    }

    /// <summary>Okamžite dokončí výstavbu (napr. cheat / load uloženej hry).</summary>
    public void ForceCompleteConstruction()
    {
        BuildElapsed = BuildTime;
        IsUnderConstruction = false;
    }

    /// <summary>
    /// Bezpečné nastavenie množstva v slote – funguje LEN po dokončení
    /// výstavby (počas výstavby je zmena zablokovaná a vráti false).
    /// </summary>
    public bool TrySetSlotAmount(ResourceSlot slot, int newAmount)
    {
        if (!CanEditResources || slot == null) return false;
        slot.amount = Mathf.Clamp(newAmount, 0, slot.capacity);
        return true;
    }

    /// <summary>
    /// Bezpečné nastavenie kapacity slotu – funguje LEN po dokončení výstavby.
    /// Ak nová kapacita klesne pod aktuálne množstvo, množstvo sa oreže.
    /// </summary>
    public bool TrySetSlotCapacity(ResourceSlot slot, int newCapacity)
    {
        if (!CanEditResources || slot == null) return false;
        slot.capacity = Mathf.Max(0, newCapacity);
        if (slot.amount > slot.capacity) slot.amount = slot.capacity;
        return true;
    }

    // Pomocné prístupy k slotom

    /// <summary>Názov továrne (skratka cez Definition).</summary>
    public string Name => Definition.Name;

    /// <summary>Prvý príjmový slot pre danú surovinu, alebo null.</summary>
    public ResourceSlot GetLoadSlot(ResourceType type)
    {
        foreach (var s in Load)
            if (s.type == type) return s;
        return null;
    }

    /// <summary>Prvý výdajový slot pre danú surovinu, alebo null.</summary>
    public ResourceSlot GetUnLoadSlot(ResourceType type)
    {
        foreach (var s in UnLoad)
            if (s.type == type) return s;
        return null;
    }

    /// <summary>True, ak tile [x,z] patrí do footprintu tejto továrne.</summary>
    public bool ContainsTile(int x, int z)
    {
        return x >= OriginX && x < OriginX + Width
            && z >= OriginZ && z < OriginZ + Depth;
    }

    public override string ToString()
    {
        return $"FactoryInstance '{Name}' @[{OriginX},{OriginZ}] {Width}x{Depth} (rot {Rotation})";
    }
}

// ------------------------------------------------------------------
// REGISTER POLOŽENÝCH TOVÁRNÍ
// ------------------------------------------------------------------

/// <summary>
/// FactoryRegistry
/// ------------------------------------------------------------------
/// Centrálna evidencia VŠETKÝCH položených FactoryInstance v hre. Tile mapa
/// (IndicatrixAPI) naňalej drží len textúru/tileID/stateID – nevie nič o
/// kapacitách ani o tom, "ktoré tily tvoria jednu továreň". Tento register
/// dopĺňa práve toto: jeden zoznam inštancií + rýchle spätné mapovanie
/// tile [x,z] ? FactoryInstance.
///
/// PREČO STATIC:
///   Register je herne jedinečný (jedna mapa = jedna sada tovární), rovnako
///   ako FactoryDatabase. Static prístup je konzistentný a netreba riešiť
///   serializáciu MonoBehaviour referencie. Ak by si v budúcnosti chcel mať
///   register ako MonoBehaviour singleton (kvôli Inspectoru), stačí obaliť
///   tieto metódy do inštančnej triedy – API zostane rovnaké.
///
/// SPÄTNÉ MAPOVANIE tile ? továreň:
///   Dictionary<long, FactoryInstance> kde kľúč = TileKey(x,z). Pri položení
///   sa zaregistrujú VŠETKY tily footprintu, takže klik na ľubovoľný tile
///   továrne (napr. pre info okno alebo demolish) nájde inštanciu v O(1).
/// </summary>
public static class FactoryRegistry
{
    /// <summary>Všetky položené továrne (poradie = poradie položenia).</summary>
    private static readonly List<FactoryInstance> instances = new List<FactoryInstance>();

    /// <summary>Spätné mapovanie: každý tile footprintu ? jeho FactoryInstance.</summary>
    private static readonly Dictionary<long, FactoryInstance> tileToFactory
        = new Dictionary<long, FactoryInstance>();

    /// <summary>Stabilný kľúč pre dvojicu tile súradníc (x, z).</summary>
    private static long TileKey(int x, int z) => ((long)x << 32) | (uint)z;

    /// <summary>Všetky aktuálne položené továrne (len na čítanie).</summary>
    public static IReadOnlyList<FactoryInstance> All => instances;

    /// <summary>Počet položených tovární.</summary>
    public static int Count => instances.Count;

    /// <summary>
    /// Zaregistruje novú položenú továreň. Volá sa po úspešnom
    /// IndicatrixAPI.SetTile(...) (placed == true). Origin/width/depth musia
    /// zodpovedať footprintu zapísanému do tile mapy – preto sa berú priamo
    /// z out parametrov SetTile.
    ///
    /// Vráti vytvorenú FactoryInstance (alebo null, ak je definícia neznáma).
    /// </summary>
    public static FactoryInstance Register(
        FactoryDefinition definition,
        int originX, int originZ,
        int width, int depth,
        IndicatrixAPI.FactoryRotation rotation)
    {
        if (definition == null)
        {
            Debug.LogWarning("[FactoryRegistry] Register: definition == null – preskočené.");
            return null;
        }

        var inst = new FactoryInstance(definition, originX, originZ, width, depth, rotation);
        instances.Add(inst);

        // Zaregistruj každý tile footprintu do spätného mapovania.
        for (int x = originX; x < originX + width; x++)
            for (int z = originZ; z < originZ + depth; z++)
                tileToFactory[TileKey(x, z)] = inst;

        Debug.Log($"[FactoryRegistry] Zaregistrovaná {inst}. Spolu tovární: {instances.Count}.");
        return inst;
    }

    /// <summary>
    /// Vráti továreň, ktorej footprint obsahuje tile [x,z], alebo null ak
    /// na danom tile žiadna továreň nie je. O(1) cez spätné mapovanie.
    /// </summary>
    public static FactoryInstance GetFactoryAt(int x, int z)
    {
        return tileToFactory.TryGetValue(TileKey(x, z), out var inst) ? inst : null;
    }

    /// <summary>
    /// Odregistruje továreň (napr. pri demolish). Odstráni inštanciu zo
    /// zoznamu aj všetky jej tily zo spätného mapovania.
    ///
    /// POZN.: Samotné vymazanie tilov z tile mapy (IndicatrixAPI) je
    /// samostatná operácia – tento register len prestane továreň evidovať.
    ///
    /// Vráti true, ak sa továreň našla a odstránila.
    /// </summary>
    public static bool Unregister(FactoryInstance inst)
    {
        if (inst == null || !instances.Remove(inst))
            return false;

        for (int x = inst.OriginX; x < inst.OriginX + inst.Width; x++)
            for (int z = inst.OriginZ; z < inst.OriginZ + inst.Depth; z++)
            {
                long key = TileKey(x, z);
                // Odstráň len ak kľúč naozaj patrí tejto inštancii
                // (ochrana pri prípadnom prekryve – nemalo by nastať).
                if (tileToFactory.TryGetValue(key, out var mapped) && mapped == inst)
                    tileToFactory.Remove(key);
            }

        Debug.Log($"[FactoryRegistry] Odregistrovaná {inst}. Spolu tovární: {instances.Count}.");
        return true;
    }

    /// <summary>Odregistruje továreň stojacu na tile [x,z], ak nejaká je.</summary>
    public static bool UnregisterAt(int x, int z)
    {
        return Unregister(GetFactoryAt(x, z));
    }

    /// <summary>Vymaže celý register (napr. pri naňítaní novej mapy).</summary>
    public static void Clear()
    {
        instances.Clear();
        tileToFactory.Clear();
        Debug.Log("[FactoryRegistry] Register vyčistený.");
    }
}
```


### GameManager.cs

```csharp
﻿using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.HID;
using UnityEngine.Splines;


public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    // =====================================================================
    // REŽIMY KONŠTRUKCIE (pôvodné)
    // =====================================================================

    public enum RailConstructionMode
    {
        None,
        LevelUp,
        LevelDown,
        Demolish,
        RailHorizontal,
        RailVertical,
        RailCrossroad,
        RailCurveRightBottom,
        RailCurveLeftBottom,
        RailCurveRightTop,
        RailCurveLeftTop,
        StationHorizontal,
        StationVertical,
        DepotHorizontalBottom,
        DepotVerticalBottom,
        DepotHorizontalTop,
        DepotVerticalTop,
        RailSwitchHorizontalBottom,
        RailSwitchHorizontalTop,
        RailSwitchVerticalBottom,
        RailSwitchVerticalTop
    }

    public RailConstructionMode CurrentRailConstructionMode { get; private set; }

    public void SetTerrainMode(RailConstructionMode RCmode)
    {
        CurrentRailConstructionMode = RCmode;
        // Pri prepnutí do iného režimu ukončíme prípadné čakanie na vstup vlaku
        if (RCmode != RailConstructionMode.None)
        {
            // Aktivácia RAIL režimu vyzbrojí aj ROAD a FACTORY režim (mutex)
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRoadConstructionMode = RoadConstructionMode.None;
            CurrentFactoryConstructionMode = FactoryConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // ROAD CONSTRUCTION MODE (analógia k RailConstructionMode)
    //
    // Cesty a železnice sú dva oddelené systémy – obe používajú rovnaký
    // tile grid v IndicatrixAPI, ale s rôznou TileCategory (Rail / Road),
    // čo umožňuje aby SetTile pre ROAD nezasiahol existujúci RAIL graf
    // a naopak. V jednom momente môže byť aktívny BUĎ rail BUĎ road režim
    // (mutex), takže OnMovement / OnClick prebehne čistú vetvu podľa toho,
    // ktorý režim je nastavený.
    // =====================================================================

    public enum RoadConstructionMode
    {
        None,
        LevelUp,
        LevelDown,
        Demolish,
        RoadHorizontal,
        RoadVertical,
        RoadCrossroad,
        RoadCurveRightBottom,
        RoadCurveLeftBottom,
        RoadCurveRightTop,
        RoadCurveLeftTop,
        StationHorizontal,
        StationVertical,
        DepotHorizontalBottom,
        DepotVerticalBottom,
        DepotHorizontalTop,
        DepotVerticalTop,
        RoadSwitchHorizontalBottom,
        RoadSwitchHorizontalTop,
        RoadSwitchVerticalBottom,
        RoadSwitchVerticalTop
    }

    public RoadConstructionMode CurrentRoadConstructionMode { get; private set; }

    /// <summary>
    /// Preťaženie SetTerrainMode pre ROAD systém. Analogicky k RAIL verzii.
    /// Mutex: aktivácia ROAD režimu zruší RAIL režim (a opačne).
    /// </summary>
    public void SetTerrainMode(RoadConstructionMode RCmode)
    {
        CurrentRoadConstructionMode = RCmode;
        if (RCmode != RoadConstructionMode.None)
        {
            // Aktivácia ROAD režimu zruší RAIL režim aj vlakový vstup
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRailConstructionMode = RailConstructionMode.None;
            CurrentFactoryConstructionMode = FactoryConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // FACTORY CONSTRUCTION MODE (analógia k RAIL/ROAD ConstructionMode)
    //
    // Továrne sú tretí, samostatný systém. Na rozdiel od RAIL/ROAD NIE SÚ
    // dopravným grafom – sú to staticky umiestnené viac-tile objekty
    // (footprint 2×3, 3×3, 2×2 ...). Zdieľajú ten istý tile grid v
    // IndicatrixAPI, ale s TileCategory.Factory, takže TrainSystem ani
    // VehicleSystem ich nevidia (GetTileByIndex filtruje na Rail/Road).
    //
    // tileID konvencia:
    //   4 = Factory     (CoalMine, Forest, IronOreMine, GoldMine, SilverMine,
    //                    Farm, OilWells)
    //   5 = Processing  (PowerStation, SawMill, OilRefinery, ElectronicsFactory,
    //                    FurnitureFactory, Slaughterhouse, GrainFactory, Smelter,
    //                    GlassFactory)
    // Konkrétny typ továrne rozlišuje stateID = (int)FactoryConstructionMode.
    //
    // V jednom momente je aktívny BUĎ rail, BUĎ road, BUĎ factory režim
    // (mutex) – rovnako ako medzi RAIL a ROAD.
    // =====================================================================

    public enum FactoryConstructionMode
    {
        None,

        // ── Factory (tileID 4) – ťažba surovín ──
        CoalMine,            // tileID 4 (Factory),    footprint 2×3
        Forest,              // tileID 4 (Factory),    footprint 3×3
        IronOreMine,         // tileID 4 (Factory),    footprint 3×3
        GoldMine,            // tileID 4 (Factory),    footprint 3×3
        SilverMine,          // tileID 4 (Factory),    footprint 3×3
        Farm,                // tileID 4 (Factory),    footprint 3×3
        OilWells,            // tileID 4 (Factory),    footprint 3×3

        // ── Processing (tileID 5) – spracovanie surovín ──
        PowerStation,        // tileID 5 (Processing), footprint 2×2
        SawMill,             // tileID 5 (Processing), footprint 2×2
        OilRefinery,         // tileID 5 (Processing), footprint 2×2
        ElectronicsFactory,  // tileID 5 (Processing), footprint 3×3
        FurnitureFactory,    // tileID 5 (Processing), footprint 3×3
        Slaughterhouse,      // tileID 5 (Processing), footprint 2×2
        GrainFactory,        // tileID 5 (Processing), footprint 2×3
        Smelter,             // tileID 5 (Processing), footprint 2×3
        GlassFactory         // tileID 5 (Processing), footprint 2×2
    }

    public FactoryConstructionMode CurrentFactoryConstructionMode { get; private set; }

    /// <summary>
    /// Aktuálna rotácia, s ktorou sa bude umiestňovať továreň.
    ///
    /// DEFAULT: FactoryRotation.Deg0 (bez otočenia – natívna orientácia
    /// definovaná v IndicatrixAPI.GetFactoryFootprint).
    ///
    /// Hodnotu mení hráč napr. klávesou (pozri RotateFactory nižšie) alebo
    /// UI tlačidlom. SetTile aj SnapAreaFace ju čítajú, takže náhľad
    /// footprintu aj výsledné umiestnenie zostávajú synchronizované.
    /// </summary>
    public IndicatrixAPI.FactoryRotation CurrentFactoryRotation { get; private set; }
        = IndicatrixAPI.FactoryRotation.Deg0;

    /// <summary>
    /// Posunie rotáciu továrne o 90° (cyklicky Deg0→Deg90→Deg180→Deg270→Deg0).
    /// Vhodné napojiť na klávesu (napr. R) alebo na UI tlačidlo "Rotate".
    /// </summary>
    public void RotateFactory()
    {
        int next = ((int)CurrentFactoryRotation + 1) % 4;
        CurrentFactoryRotation = (IndicatrixAPI.FactoryRotation)next;
        Debug.Log($"[GameManager] Rotácia továrne nastavená na {CurrentFactoryRotation}.");
    }

    /// <summary>
    /// Priame nastavenie rotácie továrne na konkrétnu hodnotu.
    /// </summary>
    public void SetFactoryRotation(IndicatrixAPI.FactoryRotation rotation)
    {
        CurrentFactoryRotation = rotation;
        Debug.Log($"[GameManager] Rotácia továrne nastavená na {CurrentFactoryRotation}.");
    }

    /// <summary>
    /// Preťaženie SetTerrainMode pre FACTORY systém. Analogicky k RAIL/ROAD.
    /// Mutex: aktivácia FACTORY režimu zruší RAIL aj ROAD režim.
    /// </summary>
    public void SetTerrainMode(FactoryConstructionMode FCmode)
    {
        CurrentFactoryConstructionMode = FCmode;
        if (FCmode != FactoryConstructionMode.None)
        {
            // Pri výbere novej továrne resetujeme rotáciu na Deg0, aby
            // nezostala "zaseknutá" z predchádzajúceho výberu. Hráč si ju
            // vie kedykoľvek znovu otočiť klávesou R.
            CurrentFactoryRotation = IndicatrixAPI.FactoryRotation.Deg0;

            // Aktivácia FACTORY režimu zruší RAIL aj ROAD režim aj vstupy
            // a tiež režim prideľovania personálu (Staff→Factory).
            IsStaffToFactoryMode = false;
            pendingStaff = null;
            CurrentRailConstructionMode = RailConstructionMode.None;
            CurrentRoadConstructionMode = RoadConstructionMode.None;
            trainInputMode = TrainInputMode.None;
            pendingDepotTile = null;
            collectingStations = false;
            pendingRoadDepotTile = null;
            collectingRoadStations = false;
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // STAFF → FACTORY ASSIGNMENT MODE (ConstructionModeStaffToFactory)
    //
    // Štvrtý "konštrukčný" režim – NIE je to ale stavba na tile mape, ale
    // PRIDELENIE personálu (StaffManagement) do už existujúcej továrne.
    // Spúšťa ho UI okno StaffManagementMenuUI po kliknutí na *HireButton
    // (napr. MinerHireButton) – odovzdá naplnenú StaffManagement štruktúru.
    //
    // SPRÁVANIE (analogické k FACTORY/RAIL/ROAD vetvám v Update):
    //   • OnMovement: IndAPI.SnapMeshFace(...) – zvýrazní 1×1 ŠTVOREC + FACE
    //     pod kurzorom (rovnaký vizuál ako pri kliku na hotovú továreň).
    //   • OnClick:    hráč klikne na ĽUBOVOĽNÚ továreň. Keďže
    //     FactoryRegistry.GetFactoryAt(...) mapuje KAŽDÝ tile footprintu na tú
    //     istú FactoryInstance, footprint (2×3 / 3×3 / 2×2) je zohľadnený
    //     automaticky – stačí kliknúť na hociktorý tile továrne.
    //
    // POROVNANIE pri kliku (StaffManagement vs FactoryInstance):
    //   • ak sa StaffManagement.ID == FactoryInstance.ID →
    //         továreň.LevelSalary    = staff.LevelSalary;
    //         továreň.EmployeeSalary = staff.EmployeeSalary;
    //         továreň.OccupancyFlag  = true;
    //   • inak →
    //         továreň.OccupancyFlag  = false;  (ostatné polia bez zmeny)
    //
    // Mutex: aktivácia tohto režimu zruší RAIL/ROAD/FACTORY režim aj vstupy
    // (a opačne – aktivácia ktoréhokoľvek konštrukčného režimu zruší tento).
    // =====================================================================

    /// <summary>True, kým je aktívny režim prideľovania personálu do továrne.</summary>
    public bool IsStaffToFactoryMode { get; private set; }

    /// <summary>
    /// Práve "nesená" dávka personálu, ktorú UI naplnilo pri kliku na
    /// *HireButton. Po kliknutí na továreň sa porovná/prenesie do nej.
    /// </summary>
    StaffManagement pendingStaff;

    /// <summary>
    /// Spustí režim prideľovania personálu do továrne. Volá ho StaffManagement
    /// UI (napr. SMMinerPanelUI) po naplnení StaffManagement a zatvorení okna.
    /// Mutex – zhodí všetky ostatné režimy (rovnako ako SetTerrainMode).
    /// </summary>
    public void EnterStaffToFactoryMode(StaffManagement staff)
    {
        if (staff == null)
        {
            Debug.LogWarning("[GameManager] EnterStaffToFactoryMode: staff == null – ignorované.");
            return;
        }

        pendingStaff = staff;
        IsStaffToFactoryMode = true;

        // Mutex – zruš RAIL/ROAD/FACTORY režim aj vlakový/cestný vstup.
        CurrentRailConstructionMode = RailConstructionMode.None;
        CurrentRoadConstructionMode = RoadConstructionMode.None;
        CurrentFactoryConstructionMode = FactoryConstructionMode.None;
        trainInputMode = TrainInputMode.None;
        pendingDepotTile = null;
        collectingStations = false;
        pendingRoadDepotTile = null;
        collectingRoadStations = false;
        IndAPI?.HideAllSnapVisuals();

        Debug.Log($"[GameManager] Staff→Factory režim AKTÍVNY. Klikni na továreň. {staff}");
    }

    /// <summary>
    /// Ukončí režim prideľovania personálu (po vykonaní akcie alebo cez ESC)
    /// a skryje snap vizuál.
    /// </summary>
    public void ExitStaffToFactoryMode()
    {
        IsStaffToFactoryMode = false;
        pendingStaff = null;
        IndAPI?.HideAllSnapVisuals();
    }

    // =====================================================================
    // STAV VSTUPU VLAKU
    //
    // POZNÁMKA: Klávesové skratky Q, W, S, P, R, T boli ODSTRÁNENÉ.
    // Funkcionalita bola presunutá do DepotRailConstructionMenuUI, ktoré sa
    // otvára kliknutím na Depot tile.
    //
    // Z pôvodných režimov ostal LEN AddStations (W) – pretože vyžaduje
    // viacero klikov na tile mapu (na stanice). Ostatné akcie sa volajú
    // priamo cez Request*ForDepot(...) metódy bez čakania na klik na tile.
    // =====================================================================

    enum TrainInputMode
    {
        None,
        AddStations,        // Čakáme na klik na RAIL Station(e) pre vopred vybrané RAIL depo
        AddStationsRoad     // Čakáme na klik na ROAD Station(e) pre vopred vybrané ROAD depo
    }

    TrainInputMode trainInputMode = TrainInputMode.None;

    // Pre režim AddStations – zapamätáme depot, potom čakáme na kliky na stanice
    Vector2Int? pendingDepotTile = null;
    bool collectingStations = false;

    // Pre režim AddStationsRoad – zapamätáme ROAD depot, potom čakáme na
    // kliky na ROAD stanice (autobus./nákl. zastávky).
    Vector2Int? pendingRoadDepotTile = null;
    bool collectingRoadStations = false;

    // Spätná väzba pre DepotRailConstructionMenuUI – aby vedelo, či práve
    // beží "Define Route" režim pre dané depo
    public bool IsCollectingStationsForDepot(int dx, int dz)
    {
        return trainInputMode == TrainInputMode.AddStations
               && collectingStations
               && pendingDepotTile.HasValue
               && pendingDepotTile.Value.x == dx
               && pendingDepotTile.Value.y == dz;
    }

    /// <summary>
    /// ROAD ekvivalent IsCollectingStationsForDepot – spätná väzba pre
    /// DepotRoadConstructionMenuUI.
    /// </summary>
    public bool IsCollectingStationsForRoadDepot(int dx, int dz)
    {
        return trainInputMode == TrainInputMode.AddStationsRoad
               && collectingRoadStations
               && pendingRoadDepotTile.HasValue
               && pendingRoadDepotTile.Value.x == dx
               && pendingRoadDepotTile.Value.y == dz;
    }

    Camera cam;

    // =====================================================================
    // NULL-SAFE LAZY PROPERTIES
    // Chránia pred NullReferenceException ak Awake/Start prebehli v zlom poradí
    // =====================================================================

    static TrainSystem TrainSys
    {
        get
        {
            if (TrainSystem.instance == null)
                TrainSystem.instance = UnityEngine.Object.FindFirstObjectByType<TrainSystem>();
            return TrainSystem.instance;
        }
    }

    /// <summary>
    /// Lazy referencia na VehicleSystem (cestný ekvivalent TrainSystem).
    /// VehicleSystem je úplne autonómny – obsluhuje len ROAD dlaždice
    /// (skrz GetTileByIndexAny v IndicatrixAPI s filtrom kategórie).
    /// </summary>
    static VehicleSystem VehicleSys
    {
        get
        {
            if (VehicleSystem.instance == null)
                VehicleSystem.instance = UnityEngine.Object.FindFirstObjectByType<VehicleSystem>();
            return VehicleSystem.instance;
        }
    }

    static IndicatrixAPI IndAPI
    {
        get
        {
            if (IndicatrixAPI.instance == null)
                IndicatrixAPI.instance = UnityEngine.Object.FindFirstObjectByType<IndicatrixAPI>();
            return IndicatrixAPI.instance;
        }
    }

    // Lazy referencia na DepotRailConstructionMenuUI (môže byť v scéne neaktívne)
    DepotRailConstructionMenuUI _depotRailMenuUI;
    DepotRailConstructionMenuUI DepotRailMenuUI
    {
        get
        {
            if (_depotRailMenuUI == null)
                _depotRailMenuUI = UnityEngine.Object.FindFirstObjectByType<DepotRailConstructionMenuUI>(FindObjectsInactive.Include);
            return _depotRailMenuUI;
        }
    }

    // Lazy referencia na DepotRoadConstructionMenuUI (môže byť v scéne neaktívne)
    DepotRoadConstructionMenuUI _depotRoadMenuUI;
    DepotRoadConstructionMenuUI DepotRoadMenuUI
    {
        get
        {
            if (_depotRoadMenuUI == null)
                _depotRoadMenuUI = UnityEngine.Object.FindFirstObjectByType<DepotRoadConstructionMenuUI>(FindObjectsInactive.Include);
            return _depotRoadMenuUI;
        }
    }

    // Lazy referencia na StatusFactoryMenuUI (môže byť v scéne neaktívne).
    // Okno je pri štarte skryté (SetActive(false) v jeho Start()), preto sa
    // hľadá s FindObjectsInactive.Include – rovnako ako Depot menu okná.
    StatusFactoryMenuUI _statusFactoryMenuUI;
    StatusFactoryMenuUI StatusFactoryMenuUI
    {
        get
        {
            if (_statusFactoryMenuUI == null)
                _statusFactoryMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusFactoryMenuUI>(FindObjectsInactive.Include);
            return _statusFactoryMenuUI;
        }
    }

    // Lazy referencia na StatusStationMenuUI (môže byť v scéne neaktívne).
    // Okno je pri štarte skryté (SetActive(false) v jeho Start()), preto sa
    // hľadá s FindObjectsInactive.Include – rovnako ako StatusFactoryMenuUI
    // a Depot menu okná.
    StatusStationMenuUI _statusStationMenuUI;
    StatusStationMenuUI StatusStationMenuUI
    {
        get
        {
            if (_statusStationMenuUI == null)
                _statusStationMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusStationMenuUI>(FindObjectsInactive.Include);
            return _statusStationMenuUI;
        }
    }

    // Lazy referencia na StatusErrorMenuUI (môže byť v scéne neaktívne).
    // Univerzálne chybové okno – pri štarte skryté (SetActive(false) v jeho
    // Start()), preto sa hľadá s FindObjectsInactive.Include, rovnako ako
    // ostatné Status / Depot menu okná.
    StatusErrorMenuUI _statusErrorMenuUI;
    StatusErrorMenuUI StatusErrorMenuUI
    {
        get
        {
            if (_statusErrorMenuUI == null)
                _statusErrorMenuUI = UnityEngine.Object.FindFirstObjectByType<StatusErrorMenuUI>(FindObjectsInactive.Include);
            return _statusErrorMenuUI;
        }
    }

    // =====================================================================
    // CHYBOVÉ HLÁSENIA (univerzálne okno StatusErrorMenuUI)
    //
    // Chyby sa VYVOLÁVAJÚ na mieste incidentu (tu v OnClick build vetvách,
    // v budúcnosti hocikde inde). Vždy sa otvorí TO ISTÉ okno, len s INÝM
    // textom – kanonické znenia sú v GameErrors.cs.
    // =====================================================================

    /// <summary>
    /// Zobrazí hráčovi chybovú hlášku v univerzálnom okne StatusErrorMenuUI.
    /// Jediné miesto, cez ktoré GameManager otvára chybové okno – volajúci
    /// dodá text (najlepšie konštantu z GameErrors).
    /// </summary>
    void ReportError(string message)
    {
        var errMenu = StatusErrorMenuUI;
        if (errMenu != null)
        {
            errMenu.OpenWithError(message);
        }
        else
        {
            // Okno nie je v scéne – aspoň nech sa chyba neutopí ticho.
            Debug.LogWarning($"[GameManager] StatusErrorMenuUI nie je v scéne. " +
                             $"Chyba: \"{message}\"");
        }
    }

    // =====================================================================
    // EKONOMIKA KONŠTRUKCIE – spoplatnenie stavby a refund za demoláciu
    //
    // Cenník je centralizovaný v ConstructionCosts.cs; tu sú len tenké
    // obaly, ktoré cenu odpočítajú/pripočítajú cez GameEconomy a v prípade
    // nedostatku kreditov stavbu zablokujú (ReportError + return false).
    //
    // Ak GameEconomy.instance nie je v scéne, spoplatnenie sa preskočí
    // (hra ostane funkčná aj bez peňažného systému).
    // =====================================================================

    /// <summary>
    /// Pokus o zaplatenie RAIL stavby. true = zaplatené (alebo zadarmo / bez
    /// ekonomiky), false = nedostatok kreditov → volajúci NESMIE stavať.
    /// </summary>
    bool TryChargeRailBuild(RailConstructionMode mode)
        => TryChargeBuild(ConstructionCosts.RailBuildCost(mode));

    /// <summary>
    /// Pokus o zaplatenie ROAD stavby. true = zaplatené, false = nedostatok.
    /// </summary>
    bool TryChargeRoadBuild(RoadConstructionMode mode)
        => TryChargeBuild(ConstructionCosts.RoadBuildCost(mode));

    /// <summary>
    /// Pokus o zaplatenie postavenia TOVÁRNE. true = zaplatené (alebo zadarmo /
    /// bez ekonomiky), false = nedostatok kreditov → továreň sa NESMIE položiť.
    /// Cena sa berie z ConstructionCosts.FactoryBuildCost (= FactoryDefinition.Cost).
    /// </summary>
    bool TryChargeFactoryBuild(FactoryConstructionMode mode)
    {
        uint cost = ConstructionCosts.FactoryBuildCost(mode);
        bool charged = TryChargeBuild(cost);

        // EVIDENCIA pre ročnú uzávierku: cena továrne sa odpočítava HNEĎ pri
        // položení (geometria je už overená vyššie, SetTile nezlyhá), takže
        // úspešné spoplatnenie = postavená továreň. Zaznamenáme len reálne
        // platený nákup (cost > 0).
        if (charged && cost > 0u)
            BudgetSystem.instance?.RecordFactoryPurchase(cost);

        return charged;
    }

    /// <summary>Spoločné jadro – odpočíta cenu, ak je dosť kreditov.</summary>
    bool TryChargeBuild(uint cost)
    {
        if (cost == 0u) return true;                    // úkon nič nestojí
        if (GameEconomy.instance == null) return true;  // ekonomika nie je v scéne

        if (!GameEconomy.instance.TrySpendCredits(cost))
        {
            ReportError($"Nedostatok kreditov – tento úkon stojí {cost} CR.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Pripočíta na konto refund za zdemolovaný tile (50 % zo základnej ceny).
    /// tileInfo treba prečítať PRED vymazaním dlaždice (drží stateID + category).
    /// </summary>
    void RefundDemolishedTile(IndicatrixAPI.TileData tileInfo)
    {
        if (GameEconomy.instance == null) return;

        uint refund = ConstructionCosts.DemolishRefund(tileInfo);
        if (refund > 0u)
            GameEconomy.instance.AddCredits(refund);
    }

    /// <summary>
    /// Vráti true, ak je daný RAIL režim PLACEMENT (kladie tile cez SetTile s
    /// tileID != 0 – koľaje, stanice, depá, výhybky). Pre None, LevelUp,
    /// LevelDown (úprava terénu, nevolá SetTile) a Demolish (SetTile = 0)
    /// vracia false – tieto sa proti obsadenosti NEkontrolujú.
    /// </summary>
    static bool IsRailPlacementMode(RailConstructionMode mode)
    {
        switch (mode)
        {
            case RailConstructionMode.None:
            case RailConstructionMode.LevelUp:
            case RailConstructionMode.LevelDown:
            case RailConstructionMode.Demolish:
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// ROAD ekvivalent IsRailPlacementMode.
    /// </summary>
    static bool IsRoadPlacementMode(RoadConstructionMode mode)
    {
        switch (mode)
        {
            case RoadConstructionMode.None:
            case RoadConstructionMode.LevelUp:
            case RoadConstructionMode.LevelDown:
            case RoadConstructionMode.Demolish:
                return false;
            default:
                return true;
        }
    }

    // =====================================================================
    // OCHRANNÁ ZÓNA STANÍC (stanica nesmie vzniknúť pri inej stanici)
    //
    // Okolo KAŽDEJ stanice (tileID == 2) je graficky neviditeľná ochranná
    // zóna. Do tejto zóny nie je možné postaviť ďalšiu stanicu. Pravidlo je
    // kategória-agnostické: RAIL stanica blokuje aj ROAD stanicu a naopak
    // (kontroluje sa len tileID == 2, nie category).
    //
    // VEĽKOSŤ ZÓNY (STATION_EXCLUSION_RADIUS) je Chebyshev polomer okolo
    // stanice:
    //   1 → región 3×3 vycentrovaný na stanici (stanica + 8 susedov).
    //       Novú stanicu možno postaviť až vo vzdialenosti ≥ 2 tile, t.j.
    //       medzi dvoma stanicami ostane minimálne 1 voľný tile.
    //   2 → región 5×5 (väčší odstup – medzi stanicami min. 2 voľné tile).
    // Stačí zmeniť toto jediné číslo, zvyšok logiky sa prispôsobí.
    // =====================================================================

    const int STATION_EXCLUSION_RADIUS = 2;

    /// <summary>
    /// Vráti true, ak je daný RAIL režim umiestnením STANICE
    /// (StationHorizontal / StationVertical). Len pre tieto režimy sa
    /// kontroluje ochranná zóna okolo iných staníc.
    /// </summary>
    static bool IsRailStationMode(RailConstructionMode mode)
        => mode == RailConstructionMode.StationHorizontal
        || mode == RailConstructionMode.StationVertical;

    /// <summary>
    /// ROAD ekvivalent IsRailStationMode.
    /// </summary>
    static bool IsRoadStationMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.StationHorizontal
        || mode == RoadConstructionMode.StationVertical;

    /// <summary>
    /// Vráti true, ak je daný RAIL režim umiestnením DEPA (Depot* varianty).
    /// Len pre tieto režimy sa kontroluje vodorovnosť terénu (situácia č.2).
    /// </summary>
    static bool IsRailDepotMode(RailConstructionMode mode)
        => mode == RailConstructionMode.DepotHorizontalBottom
        || mode == RailConstructionMode.DepotVerticalBottom
        || mode == RailConstructionMode.DepotHorizontalTop
        || mode == RailConstructionMode.DepotVerticalTop;

    /// <summary>
    /// ROAD ekvivalent IsRailDepotMode.
    /// </summary>
    static bool IsRoadDepotMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.DepotHorizontalBottom
        || mode == RoadConstructionMode.DepotVerticalBottom
        || mode == RoadConstructionMode.DepotHorizontalTop
        || mode == RoadConstructionMode.DepotVerticalTop;

    // =====================================================================
    // KLASIFIKÁCIA KOĽAJOVÝCH/CESTNÝCH DIELOV PODĽA POVOLENÉHO TERÉNU
    //
    //   • ROVNÉ diely (Horizontal/Vertical) – smú na rovinu AJ na rampu.
    //   • OSTATNÉ track diely (crossroad, curves, switches) – len na rovinu.
    //
    // Stanice a depá majú vlastné guardy (vlastné hlášky), preto sú z
    // "flat-only track" skupiny vylúčené.
    // =====================================================================

    /// <summary>
    /// Vráti true, ak je RAIL režim ROVNÝ diel (RailHorizontal/RailVertical).
    /// Tieto sa smú stavať na rovine aj na šikmom tile (rampe).
    /// </summary>
    static bool IsRailStraightMode(RailConstructionMode mode)
        => mode == RailConstructionMode.RailHorizontal
        || mode == RailConstructionMode.RailVertical;

    /// <summary>
    /// ROAD ekvivalent IsRailStraightMode.
    /// </summary>
    static bool IsRoadStraightMode(RoadConstructionMode mode)
        => mode == RoadConstructionMode.RoadHorizontal
        || mode == RoadConstructionMode.RoadVertical;

    /// <summary>
    /// Vráti true pre RAIL track diel, ktorý sa smie stavať LEN na rovine –
    /// t.j. placement diel, ktorý NIE JE rovný, ani stanica, ani depo
    /// (zostávajú: crossroad, 4 curves, 4 switches).
    /// </summary>
    static bool IsRailFlatOnlyTrackMode(RailConstructionMode mode)
        => IsRailPlacementMode(mode)
        && !IsRailStraightMode(mode)
        && !IsRailStationMode(mode)
        && !IsRailDepotMode(mode);

    /// <summary>
    /// ROAD ekvivalent IsRailFlatOnlyTrackMode (RoadCrossroad, curves, switches).
    /// </summary>
    static bool IsRoadFlatOnlyTrackMode(RoadConstructionMode mode)
        => IsRoadPlacementMode(mode)
        && !IsRoadStraightMode(mode)
        && !IsRoadStationMode(mode)
        && !IsRoadDepotMode(mode);

    /// <summary>
    /// Vráti true, ak sa v okolí cieľového tile [cx,cz] (Chebyshev polomer
    /// STATION_EXCLUSION_RADIUS) už nachádza ĽUBOVOĽNÁ stanica (tileID == 2,
    /// RAIL aj ROAD). Slúži ako guard pred postavením novej stanice.
    ///
    /// Okrajové tiles mimo mriežky rieši GetTileByIndexAny – pre indexy mimo
    /// 0..GRID_SIZE-1 vracia prázdnu dlaždicu (tileID 0), takže sa nezarátajú.
    /// </summary>
    bool IsStationNearby(int cx, int cz)
    {
        if (IndAPI == null) return false;

        for (int dx = -STATION_EXCLUSION_RADIUS; dx <= STATION_EXCLUSION_RADIUS; dx++)
        {
            for (int dz = -STATION_EXCLUSION_RADIUS; dz <= STATION_EXCLUSION_RADIUS; dz++)
            {
                var t = IndAPI.GetTileByIndexAny(cx + dx, cz + dz);
                if (t.tileID == 2) // stanica (RAIL alebo ROAD)
                    return true;
            }
        }
        return false;
    }

    // =====================================================================
    // OCHRANNÁ ZÓNA TOVÁRNÍ (továreň nesmie vzniknúť pri inej továrni)
    //
    // Plná analógia k STATION_EXCLUSION_RADIUS / IsStationNearby, ale pre
    // viac-tile objekty (footprint). Okolo KAŽDEJ továrne (TileCategory.
    // Factory, tileID 4/5) je neviditeľná ochranná zóna. Do tejto zóny nie
    // je možné položiť ďalšiu továreň.
    //
    // FACTORY_EXCLUSION_RADIUS je Chebyshev polomer MERANÝ OD OKRAJA
    // footprintu plánovanej továrne (nie od stredu) – t.j. medzi dvoma
    // továrňami ostane minimálne toľko voľných tilov. Stačí zmeniť toto
    // jediné číslo, zvyšok logiky sa prispôsobí.
    // =====================================================================

    const int FACTORY_EXCLUSION_RADIUS = 3;

    /// <summary>
    /// Vráti true, ak sa v okolí PLÁNOVANÉHO footprintu továrne (ľavý-dolný
    /// roh [originX,originZ], rozmery width×depth) – rozšíreného o Chebyshev
    /// polomer FACTORY_EXCLUSION_RADIUS na všetky strany – už nachádza tile
    /// patriaci inej továrni (TileCategory.Factory). Slúži ako guard pred
    /// položením novej továrne (analógia k IsStationNearby).
    ///
    /// Tily samotného plánovaného footprintu sa preskakujú; okrajové indexy
    /// mimo mriežky rieši GetTileByIndexAny (vráti prázdnu dlaždicu).
    /// </summary>
    bool IsFactoryNearby(int originX, int originZ, int width, int depth)
    {
        if (IndAPI == null) return false;

        int minX = originX - FACTORY_EXCLUSION_RADIUS;
        int minZ = originZ - FACTORY_EXCLUSION_RADIUS;
        int maxX = originX + width - 1 + FACTORY_EXCLUSION_RADIUS;
        int maxZ = originZ + depth - 1 + FACTORY_EXCLUSION_RADIUS;

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // Vnútro plánovaného footprintu nie je "iná" továreň – preskoč.
                if (x >= originX && x < originX + width &&
                    z >= originZ && z < originZ + depth)
                    continue;

                var t = IndAPI.GetTileByIndexAny(x, z);
                if (t.category == IndicatrixAPI.TileCategory.Factory)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Zdemoluje CELÚ továreň, ktorej footprint obsahuje kliknutý tile – nie
    /// len jeden tile. Vyčistí všetky tily footprintu z tile mapy
    /// (IndicatrixAPI.ClearTileByIndex) a odregistruje FactoryInstance z
    /// FactoryRegistry. Volá sa z RAIL aj ROAD Demolish vetvy potom, ako sa
    /// zistí, že kliknutý tile patrí kategórii Factory a továreň NIE JE vo
    /// výstavbe.
    ///
    /// REFUND: pripočíta 50 % z ceny továrne (ConstructionCosts.FactoryBuildCost
    /// = FactoryDefinition.Cost) – rovnaká politika ako pri RAIL/ROAD demolácii.
    /// Pri stavbe sa odpočítal plný Cost, pri demolácii sa vráti polovica
    /// (zaokrúhlené nadol, vždy z kladného základu). Refund je RAZ za celý
    /// objekt (nie per-tile).
    /// </summary>
    void DemolishFactory(FactoryInstance factory)
    {
        if (factory == null || IndAPI == null) return;

        for (int x = factory.OriginX; x < factory.OriginX + factory.Width; x++)
            for (int z = factory.OriginZ; z < factory.OriginZ + factory.Depth; z++)
                IndAPI.ClearTileByIndex(x, z);

        FactoryRegistry.Unregister(factory);

        // Refund 50 % z ceny továrne (raz za celý objekt).
        if (GameEconomy.instance != null && factory.Definition != null)
        {
            uint refund = ConstructionCosts.HalfRefund(
                ConstructionCosts.FactoryBuildCost(factory.Definition.Mode));
            if (refund > 0u) GameEconomy.instance.AddCredits(refund);
        }

        Debug.Log($"[GameManager] Továreň '{factory.Name}' zdemolovaná celá " +
                  $"({factory.Width}x{factory.Depth} tilov) z rohu " +
                  $"[{factory.OriginX},{factory.OriginZ}].");
    }

    /// <summary>
    /// Vráti true, ak by úprava terénu (LevelUp/LevelDown) klikom na vrchol
    /// snapPoint zmenila výšku aspoň jedného vrcholu patriaceho OBSADENÉMU
    /// tile (tileID != 0).
    ///
    /// Nestačí kontrolovať len klikaný vrchol: TerrainVertexLevel cez
    /// TerrainCollapse kaskádovo posúva aj okolité vrcholy. Preto si necháme
    /// od TerrainManager-a SIMULOVAŤ (dry-run) celú operáciu a vrátiť zoznam
    /// VŠETKÝCH vrcholov, ktoré by sa zmenili. Každý z nich potom overíme cez
    /// IndAPI.IsVertexOnOccupiedTile (vrchol je rohom až 4 tilov).
    ///
    /// Simulácia nič reálne nemení – terén sa upraví až keď táto metóda vráti
    /// false (žiadny dotknutý vrchol nepatrí obsadenému tile).
    /// </summary>
    bool WouldTerrainEditHitOccupied(Vector3 snapPoint, bool levelUp)
    {
        if (TerrainManager.instance == null || IndAPI == null) return false;

        var affected = TerrainManager.instance.PredictVertexLevelChanges(
            snapPoint.x, snapPoint.y, snapPoint.z, levelUp);

        for (int i = 0; i < affected.Count; i++)
        {
            if (IndAPI.IsVertexOnOccupiedTile(affected[i].x, affected[i].y))
                return true;
        }
        return false;
    }

    // =====================================================================
    // INICIALIZÁCIA
    // =====================================================================

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        cam = Camera.main;
        if (cam == null)
            cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
    }

    // =====================================================================
    // UPDATE – KLÁVESOVÉ SKRATKY + KLIKANIE + POHYB MYŠI
    // =====================================================================

    private void Update()
    {
        HandleEscapeShortcut();

        if (cam == null) return; // bezpečnosť pred null kamerou
        if (IndAPI == null) return; // bezpečnosť pred neinicializovaným IndicatrixAPI

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100000))
        {
            // =========================================================
            // RAIL CONSTRUCTION MENU UI (pôvodná funkcionalita)
            // =========================================================

            if (CurrentRailConstructionMode != RailConstructionMode.None
                && trainInputMode == TrainInputMode.None)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    switch (CurrentRailConstructionMode)
                    {
                        case RailConstructionMode.LevelUp:
                        case RailConstructionMode.LevelDown:
                            IndAPI.SnapVertex(hit.point);
                            break;

                        case RailConstructionMode.Demolish:
                        case RailConstructionMode.StationHorizontal:
                        case RailConstructionMode.StationVertical:
                        case RailConstructionMode.RailHorizontal:
                        case RailConstructionMode.RailVertical:
                        case RailConstructionMode.RailCrossroad:
                        case RailConstructionMode.RailCurveRightBottom:
                        case RailConstructionMode.RailCurveLeftBottom:
                        case RailConstructionMode.RailCurveRightTop:
                        case RailConstructionMode.RailCurveLeftTop:
                        case RailConstructionMode.DepotHorizontalBottom:
                        case RailConstructionMode.DepotVerticalBottom:
                        case RailConstructionMode.DepotHorizontalTop:
                        case RailConstructionMode.DepotVerticalTop:
                        case RailConstructionMode.RailSwitchHorizontalBottom:
                        case RailConstructionMode.RailSwitchHorizontalTop:
                        case RailConstructionMode.RailSwitchVerticalBottom:
                        case RailConstructionMode.RailSwitchVerticalTop:
                            IndAPI.SnapLineFace(hit.point);
                            break;
                    }
                }

                // ON CLICK
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // [ERROR GUARD] Stavba (placement) na UŽ OBSADENOM tile
                        // je zakázaná. Ak je aktuálny režim placement (kladie
                        // SetTile s tileID != 0) a cieľový tile už nie je
                        // prázdny, NIČ sa nepostaví a hráčovi sa zobrazí chyba.
                        // Demolish (SetTile = 0) a Level Up/Down sem nespadajú –
                        // IsRailPlacementMode ich vyradí.
                        if (IsRailPlacementMode(CurrentRailConstructionMode))
                        {
                            Vector2Int targetIdx = IndAPI.SnapTileIndex(hit.point);
                            var targetTile = IndAPI.GetTileByIndexAny(targetIdx.x, targetIdx.y);

                            // [ERROR GUARD] Na TOVÁREŇ (TileCategory.Factory) sa
                            // nesmie postaviť nič z RAIL výstavbových módov.
                            // Špecifickejšia hláška ako generická "occupied tile"
                            // nižšie – preto sa kontroluje skôr.
                            if (targetTile.category == IndicatrixAPI.TileCategory.Factory)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            if (targetTile.tileID != 0)
                            {
                                ReportError(GameErrors.CannotBuildOnOccupiedTile);
                                IndAPI.HideAllSnapVisuals();
                                return; // mutex režimy – ostatné vetvy sú neaktívne, návrat je bezpečný
                            }

                            // [ERROR GUARD] Stanica nesmie vzniknúť v ochrannej
                            // zóne (3×3) inej stanice. Kontrola je kategória-
                            // agnostická – blokuje RAIL aj ROAD stanicu v okolí.
                            // Len pre režimy umiestnenia stanice (tileID 2).
                            if (IsRailStationMode(CurrentRailConstructionMode)
                                && IsStationNearby(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationNearStation);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica sa smie stavať len na
                            // VODOROVNOM tile (4 vertexy face s rovnakým Y).
                            if (IsRailStationMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Depo sa rovnako smie stavať len na
                            // vodorovnom tile.
                            if (IsRailDepotMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildDepotOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] ROVNÝ koľajový diel (Horizontal/
                            // Vertical) sa smie stavať na rovine ALEBO na rampe.
                            if (IsRailStraightMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y)
                                && !IndAPI.IsFaceRamp(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Ostatné koľajové diely (crossroad,
                            // curves, switches) sa smú stavať len na rovine.
                            if (IsRailFlatOnlyTrackMode(CurrentRailConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov. Cena sa odpočíta
                            // hneď tu (placement vetvy v switch už nezlyhajú),
                            // takže odpočet aj postavenie sú atomické.
                            if (!TryChargeRailBuild(CurrentRailConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }
                        }

                        switch (CurrentRailConstructionMode)
                        {
                            case RailConstructionMode.LevelUp:
                                {
                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Nepresiahnuť maximálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, true))
                                    {
                                        ReportError(GameErrors.MaxTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Úprava terénu nesmie zmeniť
                                    // žiadny vrchol obsadeného tile – a to ani
                                    // cez kaskádu (TerrainCollapse), preto vopred
                                    // simulujeme celú operáciu.
                                    if (WouldTerrainEditHitOccupied(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRailBuild(RailConstructionMode.LevelUp))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    TerrainManager.instance.TerrainVertexLevel(snapPoint.x, snapPoint.y, snapPoint.z, true);
                                }
                                break;

                            case RailConstructionMode.LevelDown:
                                {
                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Nepodliezť minimálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, false))
                                    {
                                        ReportError(GameErrors.MinTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (WouldTerrainEditHitOccupied(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRailBuild(RailConstructionMode.LevelDown))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    TerrainManager.instance.TerrainVertexLevel(snapPoint.x, snapPoint.y, snapPoint.z, false);
                                }
                                break;

                            case RailConstructionMode.Demolish:
                                {
                                    Vector2Int tileIdx = IndAPI.SnapTileIndex(hit.point);
                                    IndicatrixAPI.TileData tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);

                                    // RAIL Demolish neničí ROAD tiles – tie sa
                                    // mažú cez ROAD menu (Demolish v ROAD režime).
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                                    {
                                        Debug.LogWarning($"[GameManager] RAIL Demolish ignorovaný – tile [{tileIdx.x},{tileIdx.y}] patrí ROAD systému. Použite ROAD menu.");
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // TOVÁREŇ: demolish zmaže CELÚ továreň (celý
                                    // footprint), nie len kliknutý tile. Klik na
                                    // ľubovoľný tile footprintu nájde inštanciu cez
                                    // FactoryRegistry. Vo výstavbe sa demolovať
                                    // nesmie – až po dosiahnutí 100 % výstavby.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                                    {
                                        FactoryInstance fInst = FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
                                        if (fInst != null)
                                        {
                                            if (fInst.IsUnderConstruction)
                                            {
                                                Debug.LogWarning($"[GameManager] Demolish zamietnutý – továreň '{fInst.Name}' " +
                                                                 $"je vo výstavbe ({fInst.BuildPercent}%). Demoláciu skúste po dokončení.");
                                                IndAPI.HideAllSnapVisuals();
                                                break;
                                            }

                                            DemolishFactory(fInst);
                                            IndAPI.SnapMeshFace(hit.point);
                                            TrainSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // Depo nie je možné zmazať ak existuje vlak prináležiaci tomuto depu.
                                    // Depo je možné zmazať až po zmazaní vlaku cez DCRemoveTrainButton.
                                    if (tileInfo.tileID == 3)
                                    {
                                        var existingTrain = TrainSys?.GetTrain(tileIdx.x, tileIdx.y);
                                        if (existingTrain != null)
                                        {
                                            Debug.LogWarning($"[GameManager] Depo [{tileIdx.x},{tileIdx.y}] nie je možné zmazať – vlak stále existuje. Najprv vráťte vlak do depa a potom ho vymažte cez UI.");
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }
                                    }

                                    // Demolish – vymažeme dlaždicu
                                    IndAPI.SetTile(hit.point, 0, RailConstructionMode.None);
                                    IndAPI.SnapMeshFace(hit.point);
                                    TrainSys?.OnMapChanged();

                                    // Refund 50 % zo základnej ceny zdemolovaného
                                    // prvku (tileInfo bol prečítaný pred mazaním).
                                    RefundDemolishedTile(tileInfo);
                                }
                                break;

                            case RailConstructionMode.RailHorizontal:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailVertical:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCrossroad:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCrossroad);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RailConstructionMode.RailCurveLeftBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveLeftBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveRightBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveRightBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveLeftTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveLeftTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailCurveRightTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailCurveRightTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchHorizontalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchHorizontalTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchVerticalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.RailSwitchVerticalTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RailConstructionMode.RailSwitchVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RailConstructionMode.StationHorizontal:
                                {
                                    IndAPI.SetTile(hit.point, 2, RailConstructionMode.StationHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.StationVertical:
                                {
                                    IndAPI.SetTile(hit.point, 2, RailConstructionMode.StationVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RailConstructionMode.DepotHorizontalTop:
                                {
                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotHorizontalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotVerticalTop:
                                {
                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RailConstructionMode.DepotVerticalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 3, RailConstructionMode.DepotVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                        }
                    }
                }
            }

            // =========================================================
            // ROAD CONSTRUCTION MENU UI (analógia k RAIL vetve)
            //
            // Spracovanie OnMovement a OnClick pre cestný systém. Volá
            // IndAPI.SetTile preťaženie pre RoadConstructionMode, ktoré
            // ukladá tile s TileCategory.Road (a tým je oddelené od RAIL
            // grafu, takže TrainSystem cestné tiles nevidí).
            // =========================================================

            if (CurrentRoadConstructionMode != RoadConstructionMode.None
                && trainInputMode == TrainInputMode.None)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    switch (CurrentRoadConstructionMode)
                    {
                        case RoadConstructionMode.LevelUp:
                        case RoadConstructionMode.LevelDown:
                            IndAPI.SnapVertex(hit.point);
                            break;

                        case RoadConstructionMode.Demolish:
                        case RoadConstructionMode.StationHorizontal:
                        case RoadConstructionMode.StationVertical:
                        case RoadConstructionMode.RoadHorizontal:
                        case RoadConstructionMode.RoadVertical:
                        case RoadConstructionMode.RoadCrossroad:
                        case RoadConstructionMode.RoadCurveRightBottom:
                        case RoadConstructionMode.RoadCurveLeftBottom:
                        case RoadConstructionMode.RoadCurveRightTop:
                        case RoadConstructionMode.RoadCurveLeftTop:
                        case RoadConstructionMode.DepotHorizontalBottom:
                        case RoadConstructionMode.DepotVerticalBottom:
                        case RoadConstructionMode.DepotHorizontalTop:
                        case RoadConstructionMode.DepotVerticalTop:
                        case RoadConstructionMode.RoadSwitchHorizontalBottom:
                        case RoadConstructionMode.RoadSwitchHorizontalTop:
                        case RoadConstructionMode.RoadSwitchVerticalBottom:
                        case RoadConstructionMode.RoadSwitchVerticalTop:
                            IndAPI.SnapLineFace(hit.point);
                            break;
                    }
                }

                // ON CLICK
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // [ERROR GUARD] Rovnako ako pri RAIL: stavba (placement)
                        // na už obsadenom tile je zakázaná. Kontrola je
                        // kategória-agnostická (GetTileByIndexAny) – obsadený
                        // RAIL aj ROAD tile rovnako blokuje prepísanie.
                        if (IsRoadPlacementMode(CurrentRoadConstructionMode))
                        {
                            Vector2Int targetIdx = IndAPI.SnapTileIndex(hit.point);
                            var targetTile = IndAPI.GetTileByIndexAny(targetIdx.x, targetIdx.y);

                            // [ERROR GUARD] Na TOVÁREŇ (TileCategory.Factory) sa
                            // nesmie postaviť nič z ROAD výstavbových módov.
                            if (targetTile.category == IndicatrixAPI.TileCategory.Factory)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            if (targetTile.tileID != 0)
                            {
                                ReportError(GameErrors.CannotBuildOnOccupiedTile);
                                IndAPI.HideAllSnapVisuals();
                                return; // mutex režimy – ostatné vetvy sú neaktívne, návrat je bezpečný
                            }

                            // [ERROR GUARD] Stanica nesmie vzniknúť v ochrannej
                            // zóne (3×3) inej stanice. Rovnako ako pri RAIL je
                            // kontrola kategória-agnostická (blokuje RAIL aj ROAD
                            // stanicu v okolí). Len pre režimy stanice (tileID 2).
                            if (IsRoadStationMode(CurrentRoadConstructionMode)
                                && IsStationNearby(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationNearStation);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Stanica sa smie stavať len na
                            // VODOROVNOM tile (4 vertexy face s rovnakým Y).
                            if (IsRoadStationMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildStationOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Depo sa rovnako smie stavať len na
                            // vodorovnom tile.
                            if (IsRoadDepotMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildDepotOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] ROVNÝ cestný diel (Horizontal/
                            // Vertical) sa smie stavať na rovine ALEBO na rampe.
                            if (IsRoadStraightMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y)
                                && !IndAPI.IsFaceRamp(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Ostatné cestné diely (crossroad,
                            // curves, switches) sa smú stavať len na rovine.
                            if (IsRoadFlatOnlyTrackMode(CurrentRoadConstructionMode)
                                && !IndAPI.IsFaceFlat(targetIdx.x, targetIdx.y))
                            {
                                ReportError(GameErrors.CannotBuildTrackOnTerrain);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov (analógia k RAIL).
                            if (!TryChargeRoadBuild(CurrentRoadConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }
                        }

                        switch (CurrentRoadConstructionMode)
                        {
                            case RoadConstructionMode.LevelUp:
                                {
                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Nepresiahnuť maximálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, true))
                                    {
                                        ReportError(GameErrors.MaxTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // [ERROR GUARD] Rovnako ako pri RAIL –
                                    // simulujeme celú operáciu vrátane kaskády.
                                    if (WouldTerrainEditHitOccupied(snapPoint, true))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRoadBuild(RoadConstructionMode.LevelUp))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    TerrainManager.instance.TerrainVertexLevel(snapPoint.x, snapPoint.y, snapPoint.z, true);
                                }
                                break;

                            case RoadConstructionMode.LevelDown:
                                {
                                    Vector3 snapPoint = IndAPI.SnapVertex(hit.point);

                                    // [ERROR GUARD] Nepodliezť minimálnu úroveň
                                    // prevýšenia terénu.
                                    if (TerrainManager.instance.WouldExceedElevationLimit(snapPoint.y, false))
                                    {
                                        ReportError(GameErrors.MinTerrainElevationReached);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (WouldTerrainEditHitOccupied(snapPoint, false))
                                    {
                                        ReportError(GameErrors.CannotEditTerrainOccupied);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    if (!TryChargeRoadBuild(RoadConstructionMode.LevelDown))
                                    {
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    TerrainManager.instance.TerrainVertexLevel(snapPoint.x, snapPoint.y, snapPoint.z, false);
                                }
                                break;

                            case RoadConstructionMode.Demolish:
                                {
                                    // Demolish v ROAD režime zmaže len cestnú dlaždicu
                                    // (rail dlaždice cez ROAD Demolish nemusí byť cieľom,
                                    // ale IndAPI.SetTile pre ROAD prepíše len Road kategóriu).
                                    // Ak by sme chceli aby ROAD Demolish nezasiahol RAIL tile,
                                    // pridáme kontrolu kategórie:
                                    Vector2Int rdTileIdx = IndAPI.SnapTileIndex(hit.point);
                                    var tileInfo = IndAPI.GetTileByIndexAny(rdTileIdx.x, rdTileIdx.y);

                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                                    {
                                        Debug.LogWarning("[GameManager] ROAD Demolish ignorovaný – tile patrí RAIL systému. Pre demoláciu RAIL prvkov použite RAIL menu.");
                                        ReportError(GameErrors.CannotDemolishWrongSystem);
                                        IndAPI.HideAllSnapVisuals();
                                        break;
                                    }

                                    // TOVÁREŇ: demolish zmaže CELÚ továreň (celý
                                    // footprint), nie len kliknutý tile – rovnako
                                    // ako v RAIL Demolish vetve. Vo výstavbe sa
                                    // demolovať nesmie.
                                    if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                                    {
                                        FactoryInstance fInst = FactoryRegistry.GetFactoryAt(rdTileIdx.x, rdTileIdx.y);
                                        if (fInst != null)
                                        {
                                            if (fInst.IsUnderConstruction)
                                            {
                                                Debug.LogWarning($"[GameManager] Demolish zamietnutý – továreň '{fInst.Name}' " +
                                                                 $"je vo výstavbe ({fInst.BuildPercent}%). Demoláciu skúste po dokončení.");
                                                IndAPI.HideAllSnapVisuals();
                                                break;
                                            }

                                            DemolishFactory(fInst);
                                            IndAPI.SnapMeshFace(hit.point);
                                            VehicleSys?.OnMapChanged();
                                            break;
                                        }
                                    }

                                    // ROAD Depo nie je možné zmazať ak existuje vozidlo prináležiace
                                    // tomuto depu. Vozidlo treba najprv odstrániť cez UI
                                    // (DCRemoveVehicleButton v DepotRoadConstructionMenuUI).
                                    // Analogické správanie s RAIL Demolish vyššie.
                                    if (tileInfo.tileID == 3 && tileInfo.category == IndicatrixAPI.TileCategory.Road)
                                    {
                                        var existingVehicle = VehicleSys?.GetVehicle(rdTileIdx.x, rdTileIdx.y);
                                        if (existingVehicle != null)
                                        {
                                            Debug.LogWarning($"[GameManager] ROAD Depo [{rdTileIdx.x},{rdTileIdx.y}] nie je možné zmazať – vozidlo stále existuje. Najprv vráťte vozidlo do depa a potom ho vymažte cez UI.");
                                            IndAPI.HideAllSnapVisuals();
                                            break;
                                        }
                                    }

                                    IndAPI.SetTile(hit.point, 0, RoadConstructionMode.None);
                                    IndAPI.SnapMeshFace(hit.point);

                                    // Refund 50 % zo základnej ceny zdemolovaného
                                    // prvku (tileInfo bol prečítaný pred mazaním).
                                    RefundDemolishedTile(tileInfo);
                                }
                                break;

                            case RoadConstructionMode.RoadHorizontal:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadVertical:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCrossroad:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCrossroad);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.RoadCurveLeftBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveLeftBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveRightBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveRightBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveLeftTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveLeftTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadCurveRightTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadCurveRightTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.RoadSwitchHorizontalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchHorizontalTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchVerticalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.RoadSwitchVerticalTop:
                                {
                                    IndAPI.SetTile(hit.point, 1, RoadConstructionMode.RoadSwitchVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.StationHorizontal:
                                {
                                    IndAPI.SetTile(hit.point, 2, RoadConstructionMode.StationHorizontal);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.StationVertical:
                                {
                                    IndAPI.SetTile(hit.point, 2, RoadConstructionMode.StationVertical);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;

                            case RoadConstructionMode.DepotHorizontalTop:
                                {
                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotHorizontalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotHorizontalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotHorizontalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotVerticalTop:
                                {
                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotVerticalTop);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                            case RoadConstructionMode.DepotVerticalBottom:
                                {
                                    IndAPI.SetTile(hit.point, 3, RoadConstructionMode.DepotVerticalBottom);
                                    IndAPI.SnapMeshFace(hit.point);
                                }
                                break;
                        }

                        // Notifikácia pre VehicleSystem – akákoľvek modifikácia
                        // ROAD grafu (tile placement, Demolish) vyvolá debounce-d
                        // reroute pre bežiace vozidlá. LevelUp/LevelDown sem
                        // tiež spadnú (úprava terénu mení Y waypointov, ale
                        // grafovo nič – debounce timer to ošetrí, žiadne
                        // zbytočné prepočty pri rovine).
                        VehicleSys?.OnMapChanged();
                    }
                }
            }

            // =========================================================
            // FACTORY CONSTRUCTION MENU UI (analógia k RAIL/ROAD vetve)
            //
            // Spracovanie OnMovement a OnClick pre továrenský systém.
            //
            // ODLIŠNOSTI oproti RAIL/ROAD:
            //   - Továreň je VIAC-TILE objekt (footprint 2×3, 3×3, 2×2).
            //   - OnMovement používa IndAPI.SnapAreaFace(...) – zvýrazní
            //     celý obdĺžnik footprintu, nie len 1×1 tile.
            //   - OnClick volá multi-tile IndAPI.SetTile(...) FACTORY
            //     preťaženie, ktoré zapíše textúru do všetkých N tilov a
            //     vráti bool (úspech/zlyhanie kvôli kolízii alebo okraju).
            //   - tileID: 4 = Factory (CoalMine, Forest, IronOreMine, GoldMine,
            //                            SilverMine, Farm, OilWells),
            //             5 = Processing (PowerStation, SawMill, OilRefinery,
            //                            ElectronicsFactory, FurnitureFactory,
            //                            Slaughterhouse, GrainFactory, Smelter,
            //                            GlassFactory).
            // =========================================================

            if (CurrentFactoryConstructionMode != FactoryConstructionMode.None
                && trainInputMode == TrainInputMode.None)
            {
                // ROTÁCIA – klávesa R otočí továreň o 90°. Náhľad sa
                // prekreslí hneď nižšie v ON MOVEMENT vetve.
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RotateFactory();
                }

                // ON MOVEMENT – náhľad footprintu pod kurzorom (s rotáciou)
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapAreaFace(hit.point, CurrentFactoryConstructionMode,
                                        CurrentFactoryRotation);
                }

                // ON CLICK – umiestnenie továrne (s rotáciou)
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        // =================================================
                        // [ERROR GUARD] – PRED-VALIDÁCIA UMIESTNENIA TOVÁRNE
                        // Footprintovú matematiku počíta IndicatrixAPI (jediný
                        // zdroj pravdy). Najprv zistíme rozmery a roh footprintu
                        // pod kurzorom, potom overíme tri situácie a každú
                        // ohlásime VLASTNOU hláškou (SetTile vracia len bool a
                        // nevie POVEDAŤ, prečo by zlyhal).
                        // =================================================
                        if (IndAPI.TryGetFactoryFootprintBounds(
                                hit.point, CurrentFactoryConstructionMode, CurrentFactoryRotation,
                                out int fpOriginX, out int fpOriginZ, out int fpWidth, out int fpDepth))
                        {
                            // Klasifikácia obsadenosti footprintu:
                            //   • aspoň jeden tile je TOVÁREŇ   → továreň na továreň (situácia 2)
                            //   • aspoň jeden tile je RAIL/ROAD → továreň na inú stavbu (situácia 4)
                            bool footprintHasFactory = false;
                            bool footprintHasOther = false;

                            for (int x = fpOriginX; x < fpOriginX + fpWidth; x++)
                            {
                                for (int z = fpOriginZ; z < fpOriginZ + fpDepth; z++)
                                {
                                    var ft = IndAPI.GetTileByIndexAny(x, z);
                                    if (ft.tileID == 0) continue;

                                    if (ft.category == IndicatrixAPI.TileCategory.Factory)
                                        footprintHasFactory = true;
                                    else
                                        footprintHasOther = true;
                                }
                            }

                            // (2) Továreň NEMOŽNO postaviť na už existujúcu továreň.
                            if (footprintHasFactory)
                            {
                                ReportError(GameErrors.CannotBuildFactoryOnFactory);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // (4) Továreň NEMOŽNO postaviť na koľaj/cestu/stanicu/depo.
                            if (footprintHasOther)
                            {
                                ReportError(GameErrors.CannotBuildOnExisting);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // (3) Továreň NEMOŽNO postaviť v ochrannej zóne inej továrne.
                            if (IsFactoryNearby(fpOriginX, fpOriginZ, fpWidth, fpDepth))
                            {
                                ReportError(GameErrors.CannotBuildFactoryNearFactory);
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }

                            // [ERROR GUARD] Dostatok kreditov. Cena továrne
                            // (ConstructionCosts.FactoryBuildCost) sa odpočíta HNEĎ
                            // pri položení – hráč nečaká na 100 % výstavby. Geometria
                            // je už overená vyššie, takže nasledujúci SetTile už
                            // nezlyhá → odpočet a postavenie sú atomické.
                            if (!TryChargeFactoryBuild(CurrentFactoryConstructionMode))
                            {
                                IndAPI.HideAllSnapVisuals();
                                return;
                            }
                        }

                        bool placed = false;

                        // Footprint položenej továrne – vyplní ho rozšírené
                        // SetTile preťaženie cez out parametre. Slúži na
                        // následnú registráciu FactoryInstance.
                        int fOriginX = 0, fOriginZ = 0, fWidth = 0, fDepth = 0;

                        switch (CurrentFactoryConstructionMode)
                        {
                            // ── tileID = 4 → Factory (ťažba surovín) ──
                            case FactoryConstructionMode.CoalMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.CoalMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Forest:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.Forest,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.IronOreMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.IronOreMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GoldMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.GoldMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.SilverMine:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.SilverMine,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Farm:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.Farm,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.OilWells:
                                placed = IndAPI.SetTile(hit.point, 4, FactoryConstructionMode.OilWells,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;

                            // ── tileID = 5 → Processing (spracovanie surovín) ──
                            case FactoryConstructionMode.PowerStation:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.PowerStation,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.SawMill:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.SawMill,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.OilRefinery:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.OilRefinery,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.ElectronicsFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.ElectronicsFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.FurnitureFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.FurnitureFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Slaughterhouse:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.Slaughterhouse,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GrainFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.GrainFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.Smelter:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.Smelter,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                            case FactoryConstructionMode.GlassFactory:
                                placed = IndAPI.SetTile(hit.point, 5, FactoryConstructionMode.GlassFactory,
                                                        out fOriginX, out fOriginZ, out fWidth, out fDepth,
                                                        CurrentFactoryRotation);
                                break;
                        }

                        // Po úspešnom položení:
                        //  1) zaevidujeme FactoryInstance vo FactoryRegistry –
                        //     vznikne dátový objekt továrne (Name, Load/UnLoad
                        //     sklady, pozícia footprintu). Tile mapa drží len
                        //     textúru; ekonomické dáta žijú tu.
                        //  2) obnovíme náhľad footprintu na pozícii kurzora.
                        if (placed)
                        {
                            FactoryDefinition def =
                                FactoryDatabase.GetDefinition(CurrentFactoryConstructionMode);

                            FactoryInstance newFactory =
                                FactoryRegistry.Register(def, fOriginX, fOriginZ,
                                                         fWidth, fDepth, CurrentFactoryRotation);

                            // DEFAULTNÉ hodnoty pri položení továrne na tile map:
                            // EmployeeSalary = 0, LevelSalary = 0, OccupancyFlag = false.
                            // Konštruktor FactoryInstance ich síce už nastavuje, no
                            // explicitne ich tu (priamo na mieste položenia) zaručíme aj
                            // z ovládacieho kódu, takže pri KAŽDOM položení sú garantované.
                            // ID typu (fixné, v poradí databázy) si inštancia prevzala
                            // z FactoryDefinition.ID už v konštruktore.
                            if (newFactory != null)
                                newFactory.ApplyPlacementDefaults();

                            // Spustíme fázu výstavby: nad stredom továrne sa v
                            // preddefinovanej výške Y zobrazí plávajúci label
                            // "Building process: NN%" a beží časovač s dĺžkou
                            // def.BuildingTime. Kým progres nedosiahne 100 %, je
                            // továreň zablokovaná pre zmeny hodnôt aj demoláciu;
                            // po 100 % label ešte 2 s zostane a potom zmizne.
                            if (newFactory != null)
                                FactoryConstructionManager.Instance.BeginConstruction(newFactory);

                            IndAPI.SnapAreaFace(hit.point, CurrentFactoryConstructionMode,
                                                CurrentFactoryRotation);
                        }
                        else
                        {
                            // FACTORY SetTile vráti false, ak je čo i len jeden
                            // tile footprintu obsadený alebo mimo gridu – t.j.
                            // "nedá sa tu stavať". Ohlásime tú istú chybu ako pri
                            // RAIL/ROAD (jedno univerzálne okno, rovnaký text).
                            ReportError(GameErrors.CannotBuildOnOccupiedTile);
                            IndAPI.HideAllSnapVisuals();
                        }
                    }
                }
            }

            // =========================================================
            // STAFF → FACTORY ASSIGNMENT (ConstructionModeStaffToFactory)
            //
            // Analógia k FACTORY vetve vyššie, ale namiesto stavania:
            //   • OnMovement – SnapMeshFace(...) zvýrazní 1×1 ŠTVOREC + FACE.
            //   • OnClick    – klik na ľubovoľnú továreň (footprint rieši
            //                  FactoryRegistry.GetFactoryAt) → priradenie /
            //                  porovnanie StaffManagement vs FactoryInstance.
            // =========================================================
            if (IsStaffToFactoryMode)
            {
                // ON MOVEMENT – štvorec + face pod kurzorom.
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapMeshFace(hit.point);
                }

                // ON CLICK – klik na továreň.
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleStaffToFactoryClick(hit.point);
                    }
                }
            }

            // =========================================================
            // VLAKOVÝ VSTUP – LEN AddStations (po stlačení DCDefineRouteButton)
            // =========================================================

            if (trainInputMode == TrainInputMode.AddStations)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapLineFace(hit.point);
                }

                // ON CLICK – akceptujeme len kliky na stanice (depo už máme zapamätané)
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleStationClick(hit.point);
                    }
                }
            }
            // =========================================================
            // CESTNÝ VSTUP – AddStationsRoad (po stlačení DCDefineRouteButton
            // v DepotRoadConstructionMenuUI). Analogická vetva k vlakovej
            // vyššie – akceptuje IBA kliky na ROAD Station tiles.
            // =========================================================
            else if (trainInputMode == TrainInputMode.AddStationsRoad)
            {
                // ON MOVEMENT
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    IndAPI.SnapLineFace(hit.point);
                }

                // ON CLICK – akceptujeme len kliky na ROAD stanice
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        HandleRoadStationClick(hit.point);
                    }
                }
            }
            else if (CurrentRailConstructionMode == RailConstructionMode.None
                     && CurrentRoadConstructionMode == RoadConstructionMode.None
                     && CurrentFactoryConstructionMode == FactoryConstructionMode.None
                     && !IsStaffToFactoryMode
                     && trainInputMode == TrainInputMode.None)
            {
                // =====================================================
                // OTVORENIE DEPOT / FACTORY MENU pri kliku na tile (mimo
                // akéhokoľvek režimu konštrukcie / vlakového či cestného
                // vstupu).
                //   • Klik na DEPOT tile (tileID 3) → podľa kategórie depa
                //     sa otvorí buď RAIL alebo ROAD variant Depot menu.
                //   • Klik na ľubovoľný tile FOOTPRINTU továrne → otvorí sa
                //     StatusFactoryMenuUI s informáciami o danej továrni.
                // =====================================================
                if (Input.GetMouseButtonDown(0))
                {
                    if (!EventSystem.current.IsPointerOverGameObject())
                    {
                        Vector2Int tileIdx = IndAPI.SnapTileIndex(hit.point);
                        var tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);
                        if (tileInfo.tileID == 3)
                        {
                            if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                            {
                                DepotRailMenuUI?.OpenForDepot(tileIdx.x, tileIdx.y);
                            }
                            else if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                            {
                                DepotRoadMenuUI?.OpenForDepot(tileIdx.x, tileIdx.y);
                            }
                        }



                        else if (tileInfo.tileID == 2)
                        {
                            // [DIAG] Klik na stanicu – vetva trafená.
                            Debug.Log($"[DIAG][GameManager] Klik na tile [{tileIdx.x},{tileIdx.y}] " +
                                      $"tileID=2, category={tileInfo.category}.");

                            StationInstance station = null;
                            if (tileInfo.category == IndicatrixAPI.TileCategory.Rail)
                            {
                                // Lacný re-scan – istota, že register vidí všetky
                                // stanice z tile mapy + má aktuálne továrne v zóne.
                                RailStationRegistry.RescanAll();
                                Debug.Log($"[DIAG][GameManager] RailStationRegistry po Rescane: " +
                                          $"Count={RailStationRegistry.Count}");
                                station = RailStationRegistry.GetStationAt(tileIdx.x, tileIdx.y);
                            }
                            else if (tileInfo.category == IndicatrixAPI.TileCategory.Road)
                            {
                                RoadStationRegistry.RescanAll();
                                Debug.Log($"[DIAG][GameManager] RoadStationRegistry po Rescane: " +
                                          $"Count={RoadStationRegistry.Count}");
                                station = RoadStationRegistry.GetStationAt(tileIdx.x, tileIdx.y);
                            }
                            else
                            {
                                Debug.LogWarning($"[DIAG][GameManager] tileID==2 ale category={tileInfo.category} " +
                                                 $"– neznáma kategória stanice, ignorujem.");
                            }

                            if (station != null)
                            {
                                Debug.Log($"[DIAG][GameManager] StationInstance OK: {station}. " +
                                          $"Tovární v zóne: {station.Factories.Count}.");

                                var menuRef = StatusStationMenuUI;
                                if (menuRef != null)
                                {
                                    // Odovzdáme aj súradnice tile [x, z] stanice,
                                    // aby okno mohlo zobraziť názov "Station [x, z]".
                                    // tileIdx pochádza z IndAPI.SnapTileIndex →
                                    // Vector2Int(x, z), platí pre RAIL aj ROAD.
                                    menuRef.OpenForStation(station, tileIdx.x, tileIdx.y);
                                }
                                else
                                {
                                    Debug.LogError("[DIAG][GameManager] StatusStationMenuUI ref je NULL – " +
                                                   "komponent nie je v scéne!");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[DIAG][GameManager] StationInstance pre [{tileIdx.x},{tileIdx.y}] " +
                                                 $"je null aj po Rescane. Skontroluj: IndicatrixAPI.GetTileByIndexAny " +
                                                 $"vrátil tileID={tileInfo.tileID}, category={tileInfo.category}, " +
                                                 $"ale register túto stanicu nevidí.");
                            }
                        }






                        else if (tileInfo.category == IndicatrixAPI.TileCategory.Factory)
                        {
                            // Továreň pozostáva z viac tilov (footprint 2×3,
                            // 3×3, 2×2 ...). FactoryRegistry pri položení
                            // zaregistroval KAŽDÝ tile footprintu, takže klik
                            // na ľubovoľný z nich nájde tú istú FactoryInstance
                            // v O(1). Footprint preto netreba mapovať ručne.
                            FactoryInstance factory =
                                FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
                            if (factory != null)
                            {
                                // Počas výstavby (progres < 100 %) je továreň
                                // zablokovaná – status okno neotvárame, aby sa
                                // nedali meniť množstvá (amount) ani kapacity.
                                if (factory.IsUnderConstruction)
                                {
                                    Debug.Log($"[GameManager] Továreň '{factory.Name}' je vo výstavbe " +
                                              $"({factory.BuildPercent}%) – zablokovaná, skúste po dokončení.");
                                }
                                else
                                {
                                    StatusFactoryMenuUI?.OpenForFactory(factory);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // =====================================================================
    // KLÁVESOVÉ SKRATKY – už len ESC
    // =====================================================================

    void HandleEscapeShortcut()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // Celá reset logika je vyčlenená do verejnej metódy PerformEscapeReset(),
        // aby ju bolo možné vyvolať aj programovo (napr. po kliknutí na
        // CloseWindowButton ktoréhokoľvek konštrukčného okna) – nielen klávesou ESC.
        PerformEscapeReset();
    }

    /// <summary>
    /// Vykoná presne tú istú akciu ako stlačenie klávesy ESC: zruší aktuálny
    /// Staff→Factory / Define Route / RAIL / ROAD / FACTORY režim a schová
    /// všetky snap visuals (SnapLineFace, SnapVertex, SnapAreaFace, ...).
    ///
    /// Metóda je verejná a bezpečne volateľná z UI (napr. z OnCloseButtonClick
    /// jednotlivých *UIwindow skriptov), aby sa po zatvorení okna nezasekol
    /// snapping indikátor na tile mape. Je idempotentná – ak nie je aktívny
    /// žiadny režim, len defenzívne schová visuals.
    /// </summary>
    public void PerformEscapeReset()
    {
        bool didSomething = false;

        // ESC – Zrušenie režimu prideľovania personálu (Staff→Factory).
        if (IsStaffToFactoryMode)
        {
            ExitStaffToFactoryMode();
            Debug.Log("[GameManager] Staff→Factory režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho vlakového ALEBO cestného režimu (Define Route)
        if (trainInputMode != TrainInputMode.None)
        {
            // Pre korektné notifikácie zachováme info o zrušenom režime
            // pred resetom – aby sme vedeli, ktoré UI okno notifikovať.
            bool wasRail = (trainInputMode == TrainInputMode.AddStations);
            bool wasRoad = (trainInputMode == TrainInputMode.AddStationsRoad);

            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI?.HideAllSnapVisuals();

            if (wasRail)
            {
                DepotRailMenuUI?.OnRouteDefineCancelled();
                Debug.Log("[GameManager] Vlakový režim (Define Route) zrušený cez ESC.");
            }
            else if (wasRoad)
            {
                DepotRoadMenuUI?.OnRouteDefineCancelled();
                Debug.Log("[GameManager] Cestný režim (Define Route) zrušený cez ESC.");
            }
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho režimu konštrukcie koľají
        // (Rail/Station/Depot/LevelUp/Demolish ...).
        // Toto rieši aj prípad, keď hráč zatvorí RailConstructionMenuUI okno
        // (cez X tlačidlo alebo iným spôsobom) bez toho, aby sa explicitne
        // resetoval režim – zaseknutý SnapLineFace indikátor zmizne.
        if (CurrentRailConstructionMode != RailConstructionMode.None)
        {
            SetTerrainMode(RailConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] RAIL konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho ROAD režimu konštrukcie
        // Analogicky k RAIL bloku vyššie.
        if (CurrentRoadConstructionMode != RoadConstructionMode.None)
        {
            SetTerrainMode(RoadConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        // ESC – Zrušenie aktuálneho FACTORY režimu konštrukcie
        // Analogicky k RAIL/ROAD blokom vyššie. Rieši aj prípad, keď hráč
        // zatvorí FactoryConstructionMenuUI okno bez explicitného resetu –
        // zaseknutý SnapAreaFace indikátor footprintu zmizne.
        if (CurrentFactoryConstructionMode != FactoryConstructionMode.None)
        {
            SetTerrainMode(FactoryConstructionMode.None);
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] FACTORY konštrukčný režim zrušený cez ESC.");
            didSomething = true;
        }

        if (!didSomething)
        {
            // Defenzívne – aj keby niečo ostalo zaseknuté, schováme visuals
            IndAPI?.HideAllSnapVisuals();
        }
    }

    // =====================================================================
    // KLIK NA TOVÁREŇ PRI Staff→Factory PRIDEĽOVANÍ PERSONÁLU
    //
    // Volá sa z Update() v režime IsStaffToFactoryMode po kliku ĽAVÝM
    // tlačidlom mimo UI. Vyhodnotí, či sa kliklo na továreň, a podľa zhody
    // ID prenesie hodnoty z pendingStaff (StaffManagement) do FactoryInstance.
    // =====================================================================
    void HandleStaffToFactoryClick(Vector3 hitPoint)
    {
        if (IndAPI == null) return;

        // Bezpečnostná poistka – bez naplnenej dávky personálu nie je čo robiť.
        if (pendingStaff == null)
        {
            Debug.LogWarning("[GameManager] Staff→Factory: pendingStaff == null – režim ukončený.");
            ExitStaffToFactoryMode();
            return;
        }

        // Tile pod kurzorom (kategória-agnosticky – chceme vidieť aj továreň).
        Vector2Int tileIdx = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileInfo = IndAPI.GetTileByIndexAny(tileIdx.x, tileIdx.y);

        // Klik mimo továrne – ostávame v režime (môže kliknúť znova).
        if (tileInfo.category != IndicatrixAPI.TileCategory.Factory)
        {
            Debug.Log($"[GameManager] Staff→Factory: tile [{tileIdx.x},{tileIdx.y}] nie je továreň – ignorujem.");
            ReportError(GameErrors.StaffTargetNotAFactory);
            return;
        }

        // Footprint (2×3 / 3×3 / 2×2) je zohľadnený: GetFactoryAt mapuje
        // ktorýkoľvek tile footprintu na tú istú FactoryInstance.
        FactoryInstance factory = FactoryRegistry.GetFactoryAt(tileIdx.x, tileIdx.y);
        if (factory == null)
        {
            Debug.LogWarning($"[GameManager] Staff→Factory: na tile [{tileIdx.x},{tileIdx.y}] " +
                             $"je kategória Factory, ale FactoryRegistry továreň nevidí.");
            return;
        }

        // Vo výstavbe (progres < 100 %) – továreň je zablokovaná pre zmeny.
        if (factory.IsUnderConstruction)
        {
            Debug.Log($"[GameManager] Továreň '{factory.Name}' je vo výstavbe " +
                      $"({factory.BuildPercent}%) – personál nemožno priradiť. Skúste po dokončení.");
            return;
        }

        // ── POROVNANIE ID (StaffManagement vs FactoryInstance) ──
        if (factory.ID == pendingStaff.ID)
        {
            // Zhoda ID → prenes hodnoty a označ továreň ako obsadenú.
            factory.LevelSalary = pendingStaff.LevelSalary;
            factory.EmployeeSalary = pendingStaff.EmployeeSalary;
            factory.OccupancyFlag = true;

            Debug.Log($"[GameManager] Staff PRIRADENÝ do '{factory.Name}' " +
                      $"(ID={factory.ID}): LevelSalary={factory.LevelSalary}, " +
                      $"EmployeeSalary={factory.EmployeeSalary}, OccupancyFlag=true.");
        }
        else
        {
            // Nezhoda ID → len OccupancyFlag = false, ostatné polia bez zmeny.
            factory.OccupancyFlag = false;

            Debug.Log($"[GameManager] Staff ID={pendingStaff.ID} ≠ továreň '{factory.Name}' " +
                      $"ID={factory.ID} → OccupancyFlag=false (ostatné hodnoty nezmenené).");
        }

        // Jedno priradenie = jeden klik na továreň. Po vykonaní akcie režim
        // ukončíme a skryjeme snap vizuál.
        ExitStaffToFactoryMode();
    }

    // =====================================================================
    // KLIK NA STANICU PRI Define Route (analogicky pôvodnej W-fáze)
    // =====================================================================

    void HandleStationClick(Vector3 hitPoint)
    {
        if (IndAPI == null || TrainSys == null) return;
        if (!pendingDepotTile.HasValue) return;

        Vector2Int tile = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileData = IndAPI.GetTileByIndexAny(tile.x, tile.y);

        // Stanica musí byť RAIL kategórie – ROAD stanice (autobusové
        // zastávky) pre vlaky neplatia.
        if (tileData.tileID == 2 && tileData.category == IndicatrixAPI.TileCategory.Rail)
        {
            var depTile = pendingDepotTile.Value;
            bool ok = TrainSys.AddStation(depTile.x, depTile.y, tile.x, tile.y);
            if (ok)
            {
                Debug.Log($"[GameManager] Stanica [{tile.x},{tile.y}] pridaná do zoznamu.");
                DepotRailMenuUI?.OnStationAdded(depTile.x, depTile.y);
            }
            else
            {
                Debug.LogWarning($"[GameManager] Stanica [{tile.x},{tile.y}] nebola pridaná.");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Kliknite na políčko RAIL Station! Pre ukončenie znovu stlačte tlačidlo Define Route alebo ESC.");
        }
    }

    /// <summary>
    /// ROAD analógia HandleStationClick. Akceptuje iba kliky na ROAD Station
    /// dlaždice (autobus./nákl. zastávky kategórie Road). RAIL stanice
    /// pre vozidlá neplatia.
    /// </summary>
    void HandleRoadStationClick(Vector3 hitPoint)
    {
        if (IndAPI == null || VehicleSys == null) return;
        if (!pendingRoadDepotTile.HasValue) return;

        Vector2Int tile = IndAPI.SnapTileIndex(hitPoint);
        IndicatrixAPI.TileData tileData = IndAPI.GetTileByIndexAny(tile.x, tile.y);

        if (tileData.tileID == 2 && tileData.category == IndicatrixAPI.TileCategory.Road)
        {
            var depTile = pendingRoadDepotTile.Value;
            bool ok = VehicleSys.AddStation(depTile.x, depTile.y, tile.x, tile.y);
            if (ok)
            {
                Debug.Log($"[GameManager] ROAD Stanica [{tile.x},{tile.y}] pridaná do zoznamu vozidla.");
                DepotRoadMenuUI?.OnStationAdded(depTile.x, depTile.y);
            }
            else
            {
                Debug.LogWarning($"[GameManager] ROAD Stanica [{tile.x},{tile.y}] nebola pridaná.");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] Kliknite na políčko ROAD Station! Pre ukončenie znovu stlačte tlačidlo Define Route alebo ESC.");
        }
    }

    // =====================================================================
    // PUBLIC API PRE DepotRailConstructionMenuUI
    //
    // Tieto metódy nahrádzajú pôvodné klávesové skratky Q, S, P, R, T.
    // UI volá konkrétnu metódu s identifikáciou depa (dx, dz), nad ktorým
    // bolo otvorené – takže nie je potrebný žiadny ďalší klik na tile mapu.
    // =====================================================================

    /// <summary>
    /// Q-ekvivalent: vytvorenie vlaku v zadanom depe.
    ///
    /// trainTypeIndex / wagonTypeIndex sú voľby z TrainTypeDropdown /
    /// WagonTypeDropdown – posúvajú sa do TrainSystem.CreateTrain, kde sa
    /// podľa nich zostaví dátová štruktúra súpravy (TrainStock.cs).
    /// </summary>
    public bool RequestCreateTrainForDepot(int dx, int dz, int wagonCount,
                                           int trainTypeIndex, int wagonTypeIndex)
    {
        if (TrainSys == null)
        {
            Debug.LogError("[GameManager] TrainSystem.instance je null!");
            return false;
        }
        if (IndAPI == null) return false;

        var tileInfo = IndAPI.GetTileByIndexAny(dx, dz);
        if (tileInfo.tileID != 3 || tileInfo.category != IndicatrixAPI.TileCategory.Rail)
        {
            Debug.LogWarning($"[GameManager] Tile [{dx},{dz}] nie je RAIL Depot.");
            return false;
        }

        // Aplikujeme aktuálne nastavený počet vagónov pre vlak
        int clamped = Mathf.Clamp(wagonCount, 1, 10);
        TrainSys.wagonCount = clamped;

        // EKONOMIKA: cena vlaku = Cost lokomotívy + clamped * Cost vagónu.
        // Odpočítame PRED vytvorením; ak vytvorenie zlyhá, sumu vrátime.
        uint trainCost = ConstructionCosts.TrainBuildCost(trainTypeIndex, wagonTypeIndex, clamped);
        if (GameEconomy.instance != null && !GameEconomy.instance.TrySpendCredits(trainCost))
        {
            ReportError($"Nedostatok kreditov – vlak stojí {trainCost} CR.");
            return false;
        }

        bool ok = TrainSys.CreateTrain(dx, dz, trainTypeIndex, wagonTypeIndex);
        if (ok)
        {
            Debug.Log($"[GameManager] Vlak vytvorený v depe [{dx},{dz}] s {clamped} vagónmi (cez UI).");

            // EVIDENCIA pre ročnú uzávierku – počet + reálne zaplatená cena vlaku.
            BudgetSystem.instance?.RecordTrainPurchase(trainCost);
        }
        else
        {
            // Vytvorenie sa nepodarilo (vlak už existuje / tile nie je Depot) –
            // vrátime odpočítanú sumu späť.
            if (GameEconomy.instance != null && trainCost > 0u)
                GameEconomy.instance.AddCredits(trainCost);
            Debug.LogWarning($"[GameManager] Vlak pre depo [{dx},{dz}] už existuje alebo tile nie je Depot.");
        }
        return ok;
    }

    /// <summary>
    /// Spätne kompatibilný preťažený podpis – vytvorí vlak s prvým typom
    /// vlaku aj vagónu (index 0). Pre volania bez voľby typu.
    /// </summary>
    public bool RequestCreateTrainForDepot(int dx, int dz, int wagonCount)
    {
        return RequestCreateTrainForDepot(dx, dz, wagonCount, 0, 0);
    }

    /// <summary>
    /// S-ekvivalent: spustenie vlaku v zadanom depe.
    /// </summary>
    public bool RequestStartTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;
        bool ok = TrainSys.StartTrain(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal spustiť (chýba vlak alebo stanice).");
        return ok;
    }

    /// <summary>
    /// P-ekvivalent: zastavenie vlaku.
    /// </summary>
    public bool RequestStopTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;
        bool ok = TrainSys.StopTrain(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal zastaviť.");
        return ok;
    }

    /// <summary>
    /// R-ekvivalent: návrat vlaku do depa.
    /// </summary>
    public TrainSystem.ReturnToDepotResult RequestSendToDepotForDepot(int dx, int dz)
    {
        if (TrainSys == null) return TrainSystem.ReturnToDepotResult.Error;
        var result = TrainSys.ReturnToDepot(dx, dz);
        if (result == TrainSystem.ReturnToDepotResult.StoppedAwaitingSecondR)
            Debug.Log($"[GameManager] Vlak z depa [{dx},{dz}] zastavil – žiadne dostupné stanice. Kliknite znovu Send To Depot pre priamy návrat.");
        else if (result == TrainSystem.ReturnToDepotResult.Error)
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] sa nedal vrátiť do depa (chyba).");
        return result;
    }

    /// <summary>
    /// T-ekvivalent: odstránenie vlaku (musí byť v depe a zastavený).
    /// </summary>
    public bool RequestRemoveTrainForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;

        // EKONOMIKA: cenu súpravy (lokomotíva + všetky vagóny) spočítame PRED
        // odstránením, kým ešte existuje. Refund (50 %) pripočítame až po
        // úspešnom odstránení.
        int consistCost = 0;
        var td = TrainSys.GetTrain(dx, dz);
        if (td != null && td.consist != null)
        {
            consistCost = td.consist.Cost;
            foreach (var w in td.consist.Wagons)
                consistCost += w.Cost;
        }

        bool ok = TrainSys.RemoveTrain(dx, dz);
        if (ok)
        {
            if (GameEconomy.instance != null)
            {
                uint refund = ConstructionCosts.HalfRefund(consistCost);
                if (refund > 0u) GameEconomy.instance.AddCredits(refund);
            }
        }
        else
            Debug.LogWarning($"[GameManager] Vlak z depa [{dx},{dz}] nemôže byť odstránený (nie je v depe alebo beží).");
        return ok;
    }

    /// <summary>
    /// Vymaže všetky definované stanice pre vlak v zadanom depe a tiež zruší
    /// prípadne prebiehajúci "Define Route" režim pre toto depo (aby stanice
    /// nepribúdali do už-vyprázdneného zoznamu).
    ///
    /// POZNÁMKA: Táto metóda NEZASTAVÍ vlak. Volajúci (UI) je zodpovedný za
    /// to, aby vlak pred volaním zastavil cez RequestStopTrainForDepot v
    /// prípade, že vlak beží – inak by sa pohybová logika počas frame
    /// dostala k prázdnemu zoznamu staníc s nedefinovaným indexom.
    ///
    /// Vracia true ak boli stanice úspešne vyčistené (vlak existuje),
    /// false ak vlak v depe neexistuje.
    /// </summary>
    public bool RequestClearStationsForDepot(int dx, int dz)
    {
        if (TrainSys == null) return false;

        var td = TrainSys.GetTrain(dx, dz);
        if (td == null)
        {
            Debug.LogWarning($"[GameManager] V depe [{dx},{dz}] nie je vlak – nie je čo mazať.");
            return false;
        }

        // Ak práve zbierame stanice pre toto depo, najprv ukončíme zbieranie
        // (aby ďalšie kliky na mapu už nepridávali do vyčisteného zoznamu).
        if (trainInputMode == TrainInputMode.AddStations
            && collectingStations
            && pendingDepotTile.HasValue
            && pendingDepotTile.Value.x == dx
            && pendingDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] Define Route režim ukončený kvôli Delete All Routes.");
        }

        td.stations.Clear();
        td.currentStationIndex = 0;
        Debug.Log($"[GameManager] Stanice pre vlak v depe [{dx},{dz}] vymazané.");
        return true;
    }

    /// <summary>
    /// W-ekvivalent: prepnutie do/zo režimu definovania trasy (klikania na stanice).
    /// Volá sa pri stlačení DCDefineRouteButton.
    /// Vracia true ak práve začalo "collecting stations", false ak ho ukončilo.
    /// </summary>
    public bool RequestToggleDefineRouteForDepot(int dx, int dz)
    {
        if (TrainSys == null || IndAPI == null) return false;

        // Ak práve zbierame stanice pre rovnaké depo → ukončenie
        if (trainInputMode == TrainInputMode.AddStations
            && collectingStations
            && pendingDepotTile.HasValue
            && pendingDepotTile.Value.x == dx
            && pendingDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingStations = false;
            pendingDepotTile = null;
            IndAPI.HideAllSnapVisuals();
            Debug.Log("[GameManager] Define Route: Zadávanie staníc UKONČENÉ.");
            return false;
        }

        // Začiatok – overíme že v depe je vlak
        var td = TrainSys.GetTrain(dx, dz);
        if (td == null)
        {
            Debug.LogWarning($"[GameManager] V depe [{dx},{dz}] nie je vlak. Najprv vytvorte vlak.");
            return false;
        }

        // Aktivujeme režim AddStations rovno s vybraným depom (nepotrebujeme klik na depot)
        CurrentRailConstructionMode = RailConstructionMode.None;
        IndAPI.HideAllSnapVisuals();
        trainInputMode = TrainInputMode.AddStations;
        pendingDepotTile = new Vector2Int(dx, dz);
        collectingStations = true;
        td.stations.Clear();
        Debug.Log($"[GameManager] Define Route: Depo [{dx},{dz}] vybrané. Klikajte na Stanice. Ukončite opätovným stlačením Define Route alebo ESC.");
        return true;
    }

    // =====================================================================
    // PUBLIC API PRE DepotRoadConstructionMenuUI
    //
    // ROAD ekvivalent vyššie uvedeného API. Každá metóda volá VehicleSystem
    // namiesto TrainSystem a operuje nad pendingRoadDepotTile / collectingRoadStations
    // namiesto pendingDepotTile / collectingStations. Logika je inak zhodná.
    // =====================================================================

    /// <summary>
    /// Q-ekvivalent: vytvorenie vozidla v zadanom ROAD depe.
    ///
    /// Parameter vehicleTypeName (napr. "Vehicle 1", "Vehicle 2") je názov
    /// typu z VehicleCatalog (VehicleStock.cs). VehicleSystem podľa neho
    /// vyhľadá VehicleSpec a vytvorí dátovú štruktúru vozidla.
    /// </summary>
    public bool RequestCreateVehicleForRoadDepot(int dx, int dz, string vehicleTypeName)
    {
        if (VehicleSys == null)
        {
            Debug.LogError("[GameManager] VehicleSystem.instance je null!");
            return false;
        }
        if (IndAPI == null) return false;

        var tileInfo = IndAPI.GetTileByIndexAny(dx, dz);
        if (tileInfo.tileID != 3 || tileInfo.category != IndicatrixAPI.TileCategory.Road)
        {
            Debug.LogWarning($"[GameManager] Tile [{dx},{dz}] nie je ROAD Depot.");
            return false;
        }

        // EKONOMIKA: cena vozidla = Cost vozidla. Odpočítame PRED vytvorením;
        // pri zlyhaní sumu vrátime.
        uint vehicleCost = ConstructionCosts.VehicleBuildCost(vehicleTypeName);
        if (GameEconomy.instance != null && !GameEconomy.instance.TrySpendCredits(vehicleCost))
        {
            ReportError($"Nedostatok kreditov – vozidlo stojí {vehicleCost} CR.");
            return false;
        }

        bool ok = VehicleSys.CreateVehicle(dx, dz, vehicleTypeName);
        if (ok)
        {
            Debug.Log($"[GameManager] Vozidlo '{vehicleTypeName}' vytvorené v ROAD depe [{dx},{dz}] (cez UI).");

            // EVIDENCIA pre ročnú uzávierku – počet + reálne zaplatená cena vozidla.
            BudgetSystem.instance?.RecordVehiclePurchase(vehicleCost);
        }
        else
        {
            if (GameEconomy.instance != null && vehicleCost > 0u)
                GameEconomy.instance.AddCredits(vehicleCost);
            Debug.LogWarning($"[GameManager] Vozidlo pre ROAD depo [{dx},{dz}] už existuje alebo tile nie je ROAD Depot.");
        }
        return ok;
    }

    /// <summary>
    /// S-ekvivalent: spustenie vozidla v zadanom ROAD depe.
    /// </summary>
    public bool RequestStartVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;
        bool ok = VehicleSys.StartVehicle(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo spustiť (chýba vozidlo alebo stanice).");
        return ok;
    }

    /// <summary>
    /// P-ekvivalent: zastavenie vozidla.
    /// </summary>
    public bool RequestStopVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;
        bool ok = VehicleSys.StopVehicle(dx, dz);
        if (!ok)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo zastaviť.");
        return ok;
    }

    /// <summary>
    /// R-ekvivalent: návrat vozidla do depa.
    /// </summary>
    public VehicleSystem.ReturnToDepotResult RequestSendVehicleToDepotForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return VehicleSystem.ReturnToDepotResult.Error;
        var result = VehicleSys.ReturnToDepot(dx, dz);
        if (result == VehicleSystem.ReturnToDepotResult.StoppedAwaitingSecondR)
            Debug.Log($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] zastavilo – žiadne dostupné stanice. Kliknite znovu Send To Depot pre priamy návrat.");
        else if (result == VehicleSystem.ReturnToDepotResult.Error)
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] sa nedalo vrátiť do depa (chyba).");
        return result;
    }

    /// <summary>
    /// T-ekvivalent: odstránenie vozidla (musí byť v depe a zastavené).
    /// </summary>
    public bool RequestRemoveVehicleForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;

        // EKONOMIKA: cenu vozidla spočítame PRED odstránením, refund (50 %)
        // pripočítame po úspešnom odstránení.
        int vehicleCost = 0;
        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd != null && vd.vehicleInstance != null)
            vehicleCost = vd.vehicleInstance.Cost;

        bool ok = VehicleSys.RemoveVehicle(dx, dz);
        if (ok)
        {
            if (GameEconomy.instance != null)
            {
                uint refund = ConstructionCosts.HalfRefund(vehicleCost);
                if (refund > 0u) GameEconomy.instance.AddCredits(refund);
            }
        }
        else
            Debug.LogWarning($"[GameManager] Vozidlo z ROAD depa [{dx},{dz}] nemôže byť odstránené (nie je v depe alebo beží).");
        return ok;
    }

    /// <summary>
    /// Vymaže všetky definované stanice pre vozidlo v zadanom ROAD depe a
    /// tiež zruší prípadne prebiehajúci "Define Route" režim pre toto depo
    /// (aby stanice nepribúdali do už-vyprázdneného zoznamu).
    ///
    /// POZNÁMKA: Táto metóda NEZASTAVÍ vozidlo. Volajúci (UI) je zodpovedný
    /// za to, aby vozidlo pred volaním zastavil cez RequestStopVehicleForRoadDepot
    /// v prípade, že vozidlo beží.
    /// </summary>
    public bool RequestClearStationsForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null) return false;

        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd == null)
        {
            Debug.LogWarning($"[GameManager] V ROAD depe [{dx},{dz}] nie je vozidlo – nie je čo mazať.");
            return false;
        }

        if (trainInputMode == TrainInputMode.AddStationsRoad
            && collectingRoadStations
            && pendingRoadDepotTile.HasValue
            && pendingRoadDepotTile.Value.x == dx
            && pendingRoadDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI?.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD Define Route režim ukončený kvôli Delete All Routes.");
        }

        vd.stations.Clear();
        vd.currentStationIndex = 0;
        Debug.Log($"[GameManager] Stanice pre vozidlo v ROAD depe [{dx},{dz}] vymazané.");
        return true;
    }

    /// <summary>
    /// W-ekvivalent: prepnutie do/zo režimu definovania trasy pre ROAD vozidlo.
    /// Volá sa pri stlačení DCDefineRouteButton v DepotRoadConstructionMenuUI.
    /// Vracia true ak práve začalo "collecting road stations", false ak ho ukončilo.
    /// </summary>
    public bool RequestToggleDefineRouteForRoadDepot(int dx, int dz)
    {
        if (VehicleSys == null || IndAPI == null) return false;

        // Ak práve zbierame stanice pre rovnaké depo → ukončenie
        if (trainInputMode == TrainInputMode.AddStationsRoad
            && collectingRoadStations
            && pendingRoadDepotTile.HasValue
            && pendingRoadDepotTile.Value.x == dx
            && pendingRoadDepotTile.Value.y == dz)
        {
            trainInputMode = TrainInputMode.None;
            collectingRoadStations = false;
            pendingRoadDepotTile = null;
            IndAPI.HideAllSnapVisuals();
            Debug.Log("[GameManager] ROAD Define Route: Zadávanie staníc UKONČENÉ.");
            return false;
        }

        // Začiatok – overíme že v depe je vozidlo
        var vd = VehicleSys.GetVehicle(dx, dz);
        if (vd == null)
        {
            Debug.LogWarning($"[GameManager] V ROAD depe [{dx},{dz}] nie je vozidlo. Najprv vytvorte vozidlo.");
            return false;
        }

        // Aktivujeme režim AddStationsRoad rovno s vybraným depom
        CurrentRoadConstructionMode = RoadConstructionMode.None;
        IndAPI.HideAllSnapVisuals();
        trainInputMode = TrainInputMode.AddStationsRoad;
        pendingRoadDepotTile = new Vector2Int(dx, dz);
        collectingRoadStations = true;
        vd.stations.Clear();
        Debug.Log($"[GameManager] ROAD Define Route: Depo [{dx},{dz}] vybrané. Klikajte na ROAD Stanice. Ukončite opätovným stlačením Define Route alebo ESC.");
        return true;
    }
}
```


### IndicatrixAPI.cs

```csharp
﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem.HID;
using UnityEngine.Splines;


public class IndicatrixAPI : MonoBehaviour
{
    public static IndicatrixAPI instance;

    // =========================
    // DIRECTION MASK
    // =========================

    /// <summary>
    /// Smery, ktorými môže koľaj/dlaždica prepojiť susedov.
    /// Konvencia (zhodná s A* DIRECTIONS v TrainSystem):
    ///   Right  = +X (sused na východ)
    ///   Left   = -X (sused na západ)
    ///   Top    = +Z (sused na sever)
    ///   Bottom = -Z (sused na juh)
    /// </summary>
    [Flags]
    public enum DirectionMask
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    /// <summary>
    /// Vráti opačný smer ku zadanému jednoduchému smeru.
    /// Pre kombinovaný mask vracia DirectionMask.None.
    /// </summary>
    public static DirectionMask Opposite(DirectionMask d)
    {
        switch (d)
        {
            case DirectionMask.Left: return DirectionMask.Right;
            case DirectionMask.Right: return DirectionMask.Left;
            case DirectionMask.Top: return DirectionMask.Bottom;
            case DirectionMask.Bottom: return DirectionMask.Top;
            default: return DirectionMask.None;
        }
    }

    // =========================
    // TILE CATEGORY (RAIL vs ROAD)
    // =========================

    /// <summary>
    /// Kategória dlaždice – odlišuje železničné a cestné prvky pri rovnakom
    /// tileID. Železnice a cesty zdieľajú jeden tile grid, ale TrainSystem
    /// si vyberá iba kategóriu Rail a (budúci) RoadSystem iba kategóriu Road.
    /// </summary>
    public enum TileCategory
    {
        None = 0,
        Rail = 1,
        Road = 2,
        Factory = 3   // Spoločná kategória pre Factory (tileID 4) aj Processing (tileID 5)
    }

    /// <summary>
    /// Mapuje RailConstructionMode (uložený ako stateID) na DirectionMask.
    /// Pre režimy, ktoré nie sú koľajové (None, LevelUp, ...) vracia None.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.RailConstructionMode mode)
    {
        switch (mode)
        {
            // Priame koľaje
            case GameManager.RailConstructionMode.RailHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.RailVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Križovatka
            case GameManager.RailConstructionMode.RailCrossroad:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top | DirectionMask.Bottom;

            // Krivky (uhlové, ostré – bez vyhladenia)
            case GameManager.RailConstructionMode.RailCurveRightBottom:
                return DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailCurveLeftBottom:
                return DirectionMask.Left | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailCurveRightTop:
                return DirectionMask.Right | DirectionMask.Top;
            case GameManager.RailConstructionMode.RailCurveLeftTop:
                return DirectionMask.Left | DirectionMask.Top;

            // Stanice
            case GameManager.RailConstructionMode.StationHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.StationVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Depá
            case GameManager.RailConstructionMode.DepotHorizontalBottom:
            case GameManager.RailConstructionMode.DepotHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RailConstructionMode.DepotVerticalBottom:
            case GameManager.RailConstructionMode.DepotVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Výhybky (3-cestné, plne obojsmerné).
            // Každá výhybka má 1 priamy smer (Left↔Right alebo Top↔Bottom)
            // a 1 odbočný smer (kolmý). Všetky 3 spojenia môžu prechádzať
            // medzi sebou navzájom (žiadne asymetrické turnout).
            case GameManager.RailConstructionMode.RailSwitchHorizontalBottom:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RailConstructionMode.RailSwitchHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top;
            case GameManager.RailConstructionMode.RailSwitchVerticalBottom:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Right;
            case GameManager.RailConstructionMode.RailSwitchVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Left;

            default:
                return DirectionMask.None;
        }
    }

    /// <summary>
    /// Mapuje RoadConstructionMode (uložený ako stateID) na DirectionMask.
    /// Úplne analogické k RAIL verzii vyššie.
    /// Pre režimy, ktoré nie sú cestné (None, LevelUp, ...) vracia None.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.RoadConstructionMode mode)
    {
        switch (mode)
        {
            // Priame cesty
            case GameManager.RoadConstructionMode.RoadHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.RoadVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Križovatka
            case GameManager.RoadConstructionMode.RoadCrossroad:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top | DirectionMask.Bottom;

            // Krivky
            case GameManager.RoadConstructionMode.RoadCurveRightBottom:
                return DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadCurveLeftBottom:
                return DirectionMask.Left | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadCurveRightTop:
                return DirectionMask.Right | DirectionMask.Top;
            case GameManager.RoadConstructionMode.RoadCurveLeftTop:
                return DirectionMask.Left | DirectionMask.Top;

            // Stanice (autobusové / nákladné zastávky)
            case GameManager.RoadConstructionMode.StationHorizontal:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.StationVertical:
                return DirectionMask.Top | DirectionMask.Bottom;

            // Depá (garáže)
            case GameManager.RoadConstructionMode.DepotHorizontalBottom:
            case GameManager.RoadConstructionMode.DepotHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right;
            case GameManager.RoadConstructionMode.DepotVerticalBottom:
            case GameManager.RoadConstructionMode.DepotVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom;

            // T-križovatky / Y-rozdvojenia (3-cestné, plne obojsmerné)
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalBottom:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Bottom;
            case GameManager.RoadConstructionMode.RoadSwitchHorizontalTop:
                return DirectionMask.Left | DirectionMask.Right | DirectionMask.Top;
            case GameManager.RoadConstructionMode.RoadSwitchVerticalBottom:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Right;
            case GameManager.RoadConstructionMode.RoadSwitchVerticalTop:
                return DirectionMask.Top | DirectionMask.Bottom | DirectionMask.Left;

            default:
                return DirectionMask.None;
        }
    }

    /// <summary>
    /// Mapuje FactoryConstructionMode na DirectionMask.
    ///
    /// POZN.: Továrne (Factory / Processing) NIE SÚ dopravný graf – nemajú
    /// koľajové/cestné prepojenia so susedmi. Sú to staticky umiestnené
    /// viac-tile objekty (footprint). Preto vždy vraciame DirectionMask.None.
    /// Toto preťaženie existuje hlavne kvôli konzistentnosti API (Load,
    /// SetTile, TileData konštruktor) – rovnako ako pri RAIL/ROAD.
    /// </summary>
    public static DirectionMask GetConnectionsForMode(GameManager.FactoryConstructionMode mode)
    {
        // Továrne nemajú smerové spojenia.
        return DirectionMask.None;
    }

    // =========================
    // TILE ENGINE
    // =========================

    [System.Serializable]
    public struct TileData
    {
        public int tileID;
        public int stateID;          // = (int)RailConstructionMode ALEBO (int)RoadConstructionMode
        public DirectionMask connections;
        public TileCategory category; // RAIL / ROAD / None

        public TileData(int t, int s, DirectionMask c, TileCategory cat)
        {
            tileID = t;
            stateID = s;
            connections = c;
            category = cat;
        }

        // Pôvodný konštruktor (3 parametre) – pre spätnú kompatibilitu predpokladá
        // RAIL kategóriu (pretože pred zavedením ROAD bolo všetko rail).
        public TileData(int t, int s, DirectionMask c)
        {
            tileID = t;
            stateID = s;
            connections = c;
            category = (t == 0) ? TileCategory.None : TileCategory.Rail;
        }

        // Spätne kompatibilný konštruktor – connections sa odvodí zo stateID.
        // Predpokladá RAIL kategóriu (zachovanie pôvodného správania).
        public TileData(int t, int s)
        {
            tileID = t;
            stateID = s;
            connections = GetConnectionsForMode((GameManager.RailConstructionMode)s);
            category = (t == 0) ? TileCategory.None : TileCategory.Rail;
        }
    }

    // =========================
    // FACTORY FOOTPRINT
    // =========================

    /// <summary>
    /// FactoryFootprint
    /// ─────────────────────────────────────────────────────────────────────
    /// Popisuje veľkosť (footprint) jednej továrne v tiloch a pozíciu
    /// "kurzorového" (anchor) tilu v rámci tohto obdĺžnika.
    ///
    /// width  = počet tilov v smere X
    /// depth  = počet tilov v smere Z
    /// anchorX, anchorZ = lokálny index tilu (0..width-1, 0..depth-1), nad
    ///                    ktorým je snap kurzor myši. Zvyšok footprintu sa
    ///                    odvodzuje relatívne od tohto tilu.
    ///
    /// PREČO ANCHOR:
    ///   Pre nepárny rozmer (napr. 3×3) existuje presný stredový tile.
    ///   Pre párny rozmer (napr. 2×3 alebo 2×2) geometrický stred padne na
    ///   hranu medzi tilmi – stredový tile neexistuje. Anchor preto definuje
    ///   fixný, predvídateľný tile, ktorý hráč "drží" pod myšou, takže
    ///   umiestnenie je konzistentné pri každej veľkosti.
    ///
    /// VÝPOČET ROHOV footprintu z anchor tilu (ax, az):
    ///   minX = ax - anchorX
    ///   minZ = az - anchorZ
    ///   maxX = minX + width  - 1
    ///   maxZ = minZ + depth  - 1
    /// </summary>
    /// <summary>
    /// FactoryRotation
    /// ─────────────────────────────────────────────────────────────────────
    /// Otočenie továrne na tile mape v krokoch po 90°.
    ///
    /// PREČO ENUM A NIE bool:
    ///   Bool (true/false) by stačil len na prepnutie 2×3 ↔ 3×2. Ale pri
    ///   nesymetrickom footprinte (2×3) chýba aj otočenie o 180° a 270°,
    ///   ktoré menia, KTORÝM smerom je továreň "otočená" (kde má vstup,
    ///   ktorý roh je anchor). Enum so 4 hodnotami pokrýva všetky prípady
    ///   a je ľahko rozšíriteľný.
    ///
    /// VPLYV NA FOOTPRINT:
    ///   - Deg0   / Deg180 → rozmery zostávajú width×depth.
    ///   - Deg90  / Deg270 → rozmery sa PREHODIA na depth×width
    ///     (to je presne tvoja predstava "rotation XZ / ZX").
    ///   Anchor (kurzorový tile) sa transformuje spolu s obdĺžnikom, takže
    ///   kurzor vždy "drží" ten istý logický tile továrne.
    /// </summary>
    public enum FactoryRotation
    {
        Deg0 = 0,   // bez rotácie (natívna orientácia z GetFactoryFootprint)
        Deg90 = 1,   // 90°  proti smeru hod. ručičiek – prehodí width↔depth
        Deg180 = 2,   // 180°
        Deg270 = 3    // 270° proti smeru hod. ručičiek – prehodí width↔depth
    }

    [System.Serializable]
    public struct FactoryFootprint
    {
        public int width;
        public int depth;
        public int anchorX;
        public int anchorZ;

        public FactoryFootprint(int width, int depth, int anchorX, int anchorZ)
        {
            this.width = width;
            this.depth = depth;
            this.anchorX = anchorX;
            this.anchorZ = anchorZ;
        }

        public int TileCount => width * depth;

        /// <summary>
        /// Vráti NOVÝ footprint otočený o zadaný uhol.
        ///
        /// Transformácia lokálnej súradnice (lx, lz) v rámci obdĺžnika
        /// width×depth do otočeného obdĺžnika:
        ///   Deg0:   (lx, lz)                  rozmer width×depth
        ///   Deg90:  (lz, width-1-lx)          rozmer depth×width
        ///   Deg180: (width-1-lx, depth-1-lz)  rozmer width×depth
        ///   Deg270: (depth-1-lz, lx)          rozmer depth×width
        /// Tá istá transformácia sa aplikuje na anchor, aby kurzor zostal
        /// nad rovnakým logickým tilom továrne.
        /// </summary>
        public FactoryFootprint Rotated(FactoryRotation rot)
        {
            switch (rot)
            {
                case FactoryRotation.Deg90:
                    // rozmer depth×width, anchor (anchorZ, width-1-anchorX)
                    return new FactoryFootprint(depth, width,
                                                anchorZ, width - 1 - anchorX);

                case FactoryRotation.Deg180:
                    // rozmer width×depth, anchor zrkadlený v oboch osiach
                    return new FactoryFootprint(width, depth,
                                                width - 1 - anchorX, depth - 1 - anchorZ);

                case FactoryRotation.Deg270:
                    // rozmer depth×width, anchor (depth-1-anchorZ, anchorX)
                    return new FactoryFootprint(depth, width,
                                                depth - 1 - anchorZ, anchorX);

                case FactoryRotation.Deg0:
                default:
                    return this;
            }
        }
    }

    /// <summary>
    /// Vráti footprint (veľkosť + anchor) pre zadaný typ továrne.
    ///
    /// VEĽKOSTI (podľa zadania):
    ///   Factory (tileID 4):
    ///     - Coal Mine       2×3
    ///     - Forest          3×3
    ///     - Iron Ore Mine   3×3
    ///     - Gold Mine       3×3
    ///     - Silver Mine     3×3
    ///     - Farm            3×3
    ///     - Oil Wells       3×3
    ///   Processing (tileID 5):
    ///     - Power Station       2×2
    ///     - Sawmill             2×2
    ///     - Oil Refinery        2×2
    ///     - Electronics Factory 3×3
    ///     - Furniture Factory   3×3
    ///     - Slaughterhouse      2×2
    ///     - Grain Factory       2×3
    ///     - Smelter             2×3
    ///     - Glass Factory       2×2
    ///
    /// ANCHOR: zvolený tak, aby kurzor bol čo najbližšie stredu obdĺžnika.
    ///   - Pre nepárny rozmer → presný stred (3 → index 1).
    ///   - Pre párny rozmer → "ľavý/dolný zo stredovej dvojice"
    ///     (2 → index 0), čím je footprint deterministický.
    ///
    /// Tieto hodnoty sú jediné miesto, kde sa veľkosti definujú – ide o
    /// "nastaviteľný parameter". Pre zmenu veľkosti továrne stačí upraviť
    /// príslušný riadok tu.
    /// </summary>
    public static FactoryFootprint GetFactoryFootprint(GameManager.FactoryConstructionMode mode)
    {
        switch (mode)
        {
            // ── Factory (tileID 4) ──

            // Coal Mine 2×3  → anchor (0,1)
            case GameManager.FactoryConstructionMode.CoalMine:
                return new FactoryFootprint(2, 3, 0, 1);

            // Forest 3×3 → anchor (1,1) = presný stred
            case GameManager.FactoryConstructionMode.Forest:
                return new FactoryFootprint(3, 3, 1, 1);

            // Iron Ore Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.IronOreMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Gold Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.GoldMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Silver Mine 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.SilverMine:
                return new FactoryFootprint(3, 3, 1, 1);

            // Farm 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.Farm:
                return new FactoryFootprint(3, 3, 1, 1);

            // Oil Wells 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.OilWells:
                return new FactoryFootprint(3, 3, 1, 1);

            // ── Processing (tileID 5) ──

            // Power Station 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.PowerStation:
                return new FactoryFootprint(2, 2, 0, 0);

            // Sawmill 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.SawMill:
                return new FactoryFootprint(2, 2, 0, 0);

            // Oil Refinery 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.OilRefinery:
                return new FactoryFootprint(2, 2, 0, 0);

            // Electronics Factory 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.ElectronicsFactory:
                return new FactoryFootprint(3, 3, 1, 1);

            // Furniture Factory 3×3 → anchor (1,1)
            case GameManager.FactoryConstructionMode.FurnitureFactory:
                return new FactoryFootprint(3, 3, 1, 1);

            // Slaughterhouse 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.Slaughterhouse:
                return new FactoryFootprint(2, 2, 0, 0);

            // Grain Factory 2×3 → anchor (0,1)
            case GameManager.FactoryConstructionMode.GrainFactory:
                return new FactoryFootprint(2, 3, 0, 1);

            // Smelter 2×3 → anchor (0,1)
            case GameManager.FactoryConstructionMode.Smelter:
                return new FactoryFootprint(2, 3, 0, 1);

            // Glass Factory 2×2 → anchor (0,0)
            case GameManager.FactoryConstructionMode.GlassFactory:
                return new FactoryFootprint(2, 2, 0, 0);

            // None / neznáme → 1×1 (degeneruje na klasický single-tile)
            default:
                return new FactoryFootprint(1, 1, 0, 0);
        }
    }

    /// <summary>
    /// Vráti footprint pre daný typ továrne UŽ OTOČENÝ o zadaný uhol.
    /// Pohodlné preťaženie – spojí GetFactoryFootprint(mode) a .Rotated(rot).
    /// </summary>
    public static FactoryFootprint GetFactoryFootprint(
        GameManager.FactoryConstructionMode mode, FactoryRotation rotation)
    {
        return GetFactoryFootprint(mode).Rotated(rotation);
    }

    const int GRID_SIZE = 256;
    TileData[,] tileGrid;

    // =========================
    // SNAP SYSTEM
    // =========================

    GameObject snapSphere;
    GameObject lineFace01, lineFace02, lineFace03, lineFace04;

    LineRenderer lineRenderer01, lineRenderer02, lineRenderer03, lineRenderer04;

    Material lineFaceMat, snapFaceMatTex;

    //Textúry pre RAIL
    Texture2D snapFaceTex_Rail;     //ID = 1
    Texture2D snapFaceTex_RailStation;  //ID = 2
    Texture2D snapFaceTex_RailDepot;    //ID = 3

    //Textúry pre ROAD
    Texture2D snapFaceTex_Road;     //ID = 1
    Texture2D snapFaceTex_RoadStation;  //ID = 2
    Texture2D snapFaceTex_RoadDepot;    //ID = 3

    //Textúry pre FACTORY
    Texture2D snapFaceTex_Factory;     //ID = 4
    Texture2D snapFaceTex_Processing;  //ID = 5

    Mesh meshFace;

    GameObject[,] tileMap;

    // =========================
    // INITIALIZATION
    // =========================

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        InitializeTileEngine();
        InitializeSnapSystem();
    }

    void InitializeTileEngine()
    {
        tileGrid = new TileData[GRID_SIZE, GRID_SIZE];
        tileMap = new GameObject[GRID_SIZE, GRID_SIZE];

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                tileGrid[x, z] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
                tileMap[x, z] = null;
            }
        }
    }

    void InitializeSnapSystem()
    {
        snapSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(snapSphere.GetComponent<SphereCollider>());
        snapSphere.transform.localScale = Vector3.one * 0.15f;
        snapSphere.GetComponent<Renderer>().material.color = new Color(1, 1, 1, 0);

        lineFaceMat = Resources.Load<Material>("Materials/LineMat");
        snapFaceMatTex = Resources.Load<Material>("Materials/FaceMatTex");

        //Načítanie textúr pre RAIL
        snapFaceTex_Rail = Resources.Load<Texture2D>("Textures/Rail/Rail_Tex");
        snapFaceTex_RailStation = Resources.Load<Texture2D>("Textures/Rail/RailStation_Tex");
        snapFaceTex_RailDepot = Resources.Load<Texture2D>("Textures/Rail/RailDepot_Tex");

        //Načítanie textúr pre ROAD
        snapFaceTex_Road = Resources.Load<Texture2D>("Textures/Road/Road_Tex");
        snapFaceTex_RoadStation = Resources.Load<Texture2D>("Textures/Road/RoadStation_Tex");
        snapFaceTex_RoadDepot = Resources.Load<Texture2D>("Textures/Road/RoadDepot_Tex");

        //Načítanie textúr pre FACTORY
        snapFaceTex_Factory = Resources.Load<Texture2D>("Textures/Factory/Factory_Tex");
        snapFaceTex_Processing = Resources.Load<Texture2D>("Textures/Factory/Processing_Tex");

        meshFace = new Mesh();

        CreateLineRenderer(ref lineFace01, ref lineRenderer01, "LineFace01");
        CreateLineRenderer(ref lineFace02, ref lineRenderer02, "LineFace02");
        CreateLineRenderer(ref lineFace03, ref lineRenderer03, "LineFace03");
        CreateLineRenderer(ref lineFace04, ref lineRenderer04, "LineFace04");

        HideSnapSphere();
        HideSnapLines();
    }

    void HideSnapSphere()
    {
        if (snapSphere != null)
            snapSphere.SetActive(false);
    }

    void ShowSnapSphere()
    {
        if (snapSphere != null)
            snapSphere.SetActive(true);
    }

    void HideSnapLines()
    {
        if (lineFace01 != null) lineFace01.SetActive(false);
        if (lineFace02 != null) lineFace02.SetActive(false);
        if (lineFace03 != null) lineFace03.SetActive(false);
        if (lineFace04 != null) lineFace04.SetActive(false);
    }

    void ShowSnapLines()
    {
        if (lineFace01 != null) lineFace01.SetActive(true);
        if (lineFace02 != null) lineFace02.SetActive(true);
        if (lineFace03 != null) lineFace03.SetActive(true);
        if (lineFace04 != null) lineFace04.SetActive(true);
    }

    /// <summary>
    /// Skryje všetky snap vizuály (snap sphere aj línie). Volá sa pri prepnutí do vlakových režimov.
    /// </summary>
    public void HideAllSnapVisuals()
    {
        HideSnapSphere();
        HideSnapLines();
    }

    void CreateLineRenderer(ref GameObject obj, ref LineRenderer lr, string name)
    {
        obj = new GameObject(name);
        lr = obj.AddComponent<LineRenderer>();
        lr.material = lineFaceMat;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;
        lr.positionCount = 2;
    }

    // =========================
    // TILE ENGINE CORE
    // =========================

    public Vector2Int SnapTileIndex(Vector3 hitPoint)
    {
        int x = Mathf.FloorToInt(hitPoint.x);
        int z = Mathf.FloorToInt(hitPoint.z);

        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Pôvodná signatúra – stateID 0 znamená "žiadny špecifický režim" (Demolish / clear).
    /// Connections sa nastaví na None. Predpokladá RAIL kategóriu (spätná kompatibilita).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, int stateID)
    {
        SetTile(hitPoint, tileID, (GameManager.RailConstructionMode)stateID);
    }

    /// <summary>
    /// RAIL verzia – ukladá tileID + RailConstructionMode (ako stateID),
    /// connections sa odvodí cez GetConnectionsForMode, category = Rail
    /// (alebo None ak tileID == 0, t.j. Demolish/clear).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, GameManager.RailConstructionMode mode)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index)) return;

        DirectionMask conns = (tileID == 0) ? DirectionMask.None : GetConnectionsForMode(mode);
        TileCategory cat = (tileID == 0) ? TileCategory.None : TileCategory.Rail;

        tileGrid[index.x, index.y] = new TileData(tileID, (int)mode, conns, cat);
        UpdateTileMap(index.x, index.y);
    }

    /// <summary>
    /// ROAD verzia – ukladá tileID + RoadConstructionMode (ako stateID),
    /// connections sa odvodí cez GetConnectionsForMode preťaženie pre Road,
    /// category = Road (alebo None ak tileID == 0).
    ///
    /// POZN.: stateID pre RAIL a ROAD pochádza z odlišných enum-ov, preto
    /// raw int by mohol kolidovať. Kategória je preto určujúca pri spätnej
    /// rekonštrukcii (pri Load alebo iných operáciách, ktoré poznajú len
    /// raw stateID).
    /// </summary>
    public void SetTile(Vector3 hitPoint, int tileID, GameManager.RoadConstructionMode mode)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index)) return;

        DirectionMask conns = (tileID == 0) ? DirectionMask.None : GetConnectionsForMode(mode);
        TileCategory cat = (tileID == 0) ? TileCategory.None : TileCategory.Road;

        tileGrid[index.x, index.y] = new TileData(tileID, (int)mode, conns, cat);
        UpdateTileMap(index.x, index.y);
    }

    /// <summary>
    /// FACTORY verzia – multi-tile umiestnenie továrne.
    ///
    /// Na rozdiel od RAIL/ROAD (vždy 1×1) zaberá továreň obdĺžnik N×M tilov
    /// podľa <see cref="GetFactoryFootprint"/>. Hit point určuje "anchor"
    /// tile (kurzor myši); zvyšok footprintu sa dopočíta okolo neho.
    ///
    /// Do KAŽDÉHO tilu footprintu sa zapíše tá istá TileData:
    ///   tileID   = 4 (Factory) alebo 5 (Processing)
    ///   stateID  = (int)FactoryConstructionMode (rozlíši CoalMine/Forest/...)
    ///   category = TileCategory.Factory
    ///   connections = None (továrne nie sú dopravný graf)
    ///
    /// Vďaka tomu sa textúra (Factory_Tex / Processing_Tex) vykreslí na
    /// všetkých tiloch footprintu (napr. 2×3 = 6 tilov).
    ///
    /// VALIDÁCIA: celý footprint musí byť (a) vnútri gridu a (b) prázdny
    /// (samé tileID == 0). Ak čokoľvek prekáža, NIČ sa nepoloží a metóda
    /// vráti false – továreň sa nepoloží "čiastočne".
    ///
    /// stateID == 0 (FactoryConstructionMode.None) sa berie ako "clear" –
    /// vymaže 1×1 tile pod kurzorom (rovnaká sémantika ako Demolish).
    ///
    /// PARAMETER rotation:
    ///   Otočenie továrne o 0/90/180/270°. Pri 90°/270° sa footprint
    ///   prehodí (width↔depth). Anchor sa transformuje spolu s obdĺžnikom,
    ///   takže kurzor drží stále ten istý logický tile. Default = Deg0
    ///   (bez rotácie) – staré volania bez tohto parametra fungujú ďalej.
    /// </summary>
    public bool SetTile(Vector3 hitPoint, int tileID, GameManager.FactoryConstructionMode mode,
                        FactoryRotation rotation = FactoryRotation.Deg0)
    {
        // Deleguje na preťaženie s out parametrami; výsledný footprint sa
        // tu zahodí (volajúci ho nepotrebuje).
        return SetTile(hitPoint, tileID, mode, out _, out _, out _, out _, rotation);
    }

    /// <summary>
    /// FACTORY verzia s VÝSTUPOM footprintu – funkčne identická s preťažením
    /// vyššie, navyše ale cez out parametre oznámi, KAM presne sa továreň
    /// položila (ľavý-dolný roh + rozmery už po rotácii).
    ///
    /// Vďaka tomu vie volajúci (GameManager) zaevidovať FactoryInstance bez
    /// toho, aby duplikoval výpočet footprintu – jediný zdroj pravdy pre
    /// pozíciu zostáva tu.
    ///
    /// Pri neúspechu (mimo gridu / kolízia) sú out parametre nastavené na 0
    /// a metóda vráti false. Pri None/clear vetve out parametre popisujú
    /// vymazaný 1×1 tile (origin = anchor, width = depth = 1).
    /// </summary>
    public bool SetTile(Vector3 hitPoint, int tileID, GameManager.FactoryConstructionMode mode,
                        out int originX, out int originZ, out int width, out int depth,
                        FactoryRotation rotation = FactoryRotation.Deg0)
    {
        originX = 0; originZ = 0; width = 0; depth = 0;

        Vector2Int anchor = SnapTileIndex(hitPoint);
        if (!IsValidIndex(anchor)) return false;

        // None / clear → vymaž 1 tile pod kurzorom.
        if (tileID == 0 || mode == GameManager.FactoryConstructionMode.None)
        {
            tileGrid[anchor.x, anchor.y] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
            UpdateTileMap(anchor.x, anchor.y);
            originX = anchor.x; originZ = anchor.y; width = 1; depth = 1;
            return true;
        }

        // Footprint UŽ otočený podľa rotation.
        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        // Ľavý-dolný roh footprintu odvodený z anchor tilu.
        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width - 1;
        int maxZ = minZ + fp.depth - 1;

        // (a) celý footprint musí byť vnútri gridu
        if (minX < 0 || minZ < 0 || maxX >= GRID_SIZE || maxZ >= GRID_SIZE)
        {
            Debug.LogWarning($"[IndicatrixAPI] Factory '{mode}' ({fp.width}x{fp.depth}) " +
                             $"presahuje okraj mapy – neumiestnené.");
            return false;
        }

        // (b) celý footprint musí byť prázdny
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (tileGrid[x, z].tileID != 0)
                {
                    Debug.LogWarning($"[IndicatrixAPI] Factory '{mode}' – tile [{x},{z}] " +
                                     $"je obsadený. Footprint musí byť celý prázdny – neumiestnené.");
                    return false;
                }
            }
        }

        // Zápis tej istej TileData do všetkých tilov footprintu.
        DirectionMask conns = GetConnectionsForMode(mode); // None
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                tileGrid[x, z] = new TileData(tileID, (int)mode, conns, TileCategory.Factory);
                UpdateTileMap(x, z);
            }
        }

        // Výstup položeného footprintu (po rotácii).
        originX = minX; originZ = minZ; width = fp.width; depth = fp.depth;

        Debug.Log($"[IndicatrixAPI] Factory '{mode}' (rot {rotation}) umiestnená na footprint " +
                  $"[{minX},{minZ}]–[{maxX},{maxZ}] ({fp.TileCount} tilov).");
        return true;
    }

    /// <summary>
    /// Vymaže JEDEN tile na danom indexe [x,z] (nastaví prázdnu TileData,
    /// tileID 0, category None) a prekreslí jeho mesh cez UpdateTileMap.
    ///
    /// Na rozdiel od SetTile(Vector3 ...) pracuje priamo s tile indexom, takže
    /// volajúci (napr. GameManager.DemolishFactory) môže v cykle vyčistiť celý
    /// footprint viac-tile objektu (továrne) bez prepočtu svetových súradníc.
    /// Index mimo mriežky sa bezpečne ignoruje.
    /// </summary>
    public void ClearTileByIndex(int x, int z)
    {
        if (!IsValidIndex(new Vector2Int(x, z))) return;

        tileGrid[x, z] = new TileData(0, 0, DirectionMask.None, TileCategory.None);
        UpdateTileMap(x, z);
    }

    /// <summary>
    /// Vypočíta (BEZ akéhokoľvek zápisu do mapy) obdĺžnik footprintu, ktorý by
    /// zabrala továreň typu <paramref name="mode"/> položená pod kurzorom
    /// <paramref name="hitPoint"/> s rotáciou <paramref name="rotation"/>.
    ///
    /// Slúži na PRED-validáciu v GameManager-i (kontrola obsadenosti footprintu
    /// a ochrannej zóny okolo iných tovární) – footprintová matematika tak
    /// zostáva na jedinom mieste (zhodná s tým, čo neskôr spraví SetTile).
    ///
    /// Vráti true, ak je celý footprint vnútri gridu; out parametre potom držia
    /// ľavý-dolný roh a rozmery (už po rotácii). Pri footprinte mimo mapy vráti
    /// false a out parametre sú 0.
    /// </summary>
    public bool TryGetFactoryFootprintBounds(
        Vector3 hitPoint, GameManager.FactoryConstructionMode mode, FactoryRotation rotation,
        out int originX, out int originZ, out int width, out int depth)
    {
        originX = 0; originZ = 0; width = 0; depth = 0;

        Vector2Int anchor = SnapTileIndex(hitPoint);
        if (!IsValidIndex(anchor)) return false;
        if (mode == GameManager.FactoryConstructionMode.None) return false;

        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width - 1;
        int maxZ = minZ + fp.depth - 1;

        if (minX < 0 || minZ < 0 || maxX >= GRID_SIZE || maxZ >= GRID_SIZE)
            return false;

        originX = minX; originZ = minZ; width = fp.width; depth = fp.depth;
        return true;
    }

    /// <summary>
    /// Vráti tile dáta pre daný hit point.
    ///
    /// KRITICKÉ: Táto metóda RETURNS ONLY RAIL tiles. Ak je na pozícii ROAD
    /// tile, vráti prázdnu TileData (tileID=0). Týmto sa zabezpečuje, že
    /// existujúci kód (predovšetkým TrainSystem.cs a jeho A* algoritmus)
    /// vidí len RAIL graf a nemusí byť modifikovaný kvôli ROAD systému.
    ///
    /// Pre prístup ku všetkým tile dátam (bez ohľadu na kategóriu) použite
    /// <see cref="GetTileAny"/>.
    /// </summary>
    public TileData GetTile(Vector3 hitPoint)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index))
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        var td = tileGrid[index.x, index.y];
        if (td.category == TileCategory.Road)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return td;
    }

    /// <summary>
    /// Vráti tile dáta pre daný hit point BEZ filtra kategórie. Použije sa
    /// keď volajúci potrebuje vidieť všetky tile dáta (napr. GameManager
    /// pri Demolish overuje kategóriu, alebo budúci RoadSystem).
    /// </summary>
    public TileData GetTileAny(Vector3 hitPoint)
    {
        Vector2Int index = SnapTileIndex(hitPoint);

        if (!IsValidIndex(index))
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return tileGrid[index.x, index.y];
    }

    /// <summary>
    /// Priamy prístup k tileGrid cez tile indexy (pre TrainSystem a A*).
    ///
    /// KRITICKÉ: Vracia len RAIL tiles (rovnaká logika ako GetTile vyššie).
    /// ROAD tiles sa javia ako prázdne (tileID=0). Týmto je TrainSystem.cs
    /// úplne izolovaný od ROAD systému.
    /// </summary>
    public TileData GetTileByIndex(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        var td = tileGrid[x, z];
        if (td.category == TileCategory.Road)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return td;
    }

    /// <summary>
    /// Priamy prístup k tileGrid cez tile indexy BEZ filtra kategórie.
    /// Použije sa keď volajúci potrebuje vidieť všetky tile dáta vrátane ROAD
    /// (napr. GameManager kontroluje kategóriu pred Demolish, alebo budúci
    /// RoadSystem.cs si bude takto čítať road graf).
    /// </summary>
    public TileData GetTileByIndexAny(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE)
            return new TileData(0, 0, DirectionMask.None, TileCategory.None);

        return tileGrid[x, z];
    }

    // =========================
    // TERÉNNE DOTAZY (rovinatosť face, vrchol vs. obsadené tiles)
    //
    // Pomocné read-only metódy pre GameManager error-guardy:
    //   • IsFaceFlat            – je tile vodorovný (4 vertexy rovnaké Y)?
    //   • IsVertexOnOccupiedTile – patrí vrchol niektorému obsadenému tile?
    // =========================

    /// <summary>
    /// Vráti true, ak je face tile [tileX, tileZ] VODOROVNÝ – t.j. všetky
    /// 4 rohové vertexy majú rovnaké Y. Používa FunctionalValueY (číta výšky
    /// vertexov z TerrainManager.coordsF), rovnako ako SnapMeshFace.
    ///
    /// Rohy tile [x,z]: (x,z), (x,z+1), (x+1,z+1), (x+1,z).
    ///
    /// Slúži ako guard pre stavbu staníc/dep – tie sa smú stavať len na rovine.
    /// Ak TerrainManager ešte nie je inicializovaný, vracia true (fail-open –
    /// guard vtedy nebráni stavbe; reálne sa volá až počas hry, keď terén beží).
    /// </summary>
    public bool IsFaceFlat(int tileX, int tileZ)
    {
        if (TerrainManager.instance == null) return true;

        float y0 = FunctionalValueY(tileX, tileZ);
        float y1 = FunctionalValueY(tileX, tileZ + 1);
        float y2 = FunctionalValueY(tileX + 1, tileZ + 1);
        float y3 = FunctionalValueY(tileX + 1, tileZ);

        const float EPS = 0.0001f; // tolerancia na float nepresnosti
        return Mathf.Abs(y0 - y1) < EPS
            && Mathf.Abs(y0 - y2) < EPS
            && Mathf.Abs(y0 - y3) < EPS;
    }

    /// <summary>
    /// Vráti true, ak je face tile [tileX, tileZ] ŠIKMÝ – čistá RAMPA: dve
    /// susedné vertexy (jedna hrana) sú na jednej Y a protiľahlá hrana (druhé
    /// dva susedné vertexy) je na inej Y. Sklon teda vedie buď pozdĺž osi X,
    /// alebo pozdĺž osi Z.
    ///
    /// Rohy tile [x,z] (poradie ako GetFaceVertices):
    ///   y0=(x,z), y1=(x,z+1), y2=(x+1,z+1), y3=(x+1,z).
    ///
    /// NIE JE rampa: rovina (vylúčená podmienkou „hrany v rôznej výške“) ani
    /// skrútený tile (napr. jeden roh hore) – tam neplatí ani jeden zo vzorov.
    ///
    /// Slúži ako guard pre rovné koľaje/cesty, ktoré sa smú stavať aj na rampe.
    /// </summary>
    public bool IsFaceRamp(int tileX, int tileZ)
    {
        if (TerrainManager.instance == null) return false;

        float y0 = FunctionalValueY(tileX, tileZ);     // (x,   z)
        float y1 = FunctionalValueY(tileX, tileZ + 1); // (x,   z+1)
        float y2 = FunctionalValueY(tileX + 1, tileZ + 1); // (x+1, z+1)
        float y3 = FunctionalValueY(tileX + 1, tileZ);     // (x+1, z)

        const float EPS = 0.0001f;

        // Sklon pozdĺž Z: hrana pri z je rovná (y0==y3), hrana pri z+1 je rovná
        // (y1==y2), a navzájom sú v rôznej výške.
        bool rampAlongZ = Mathf.Abs(y0 - y3) < EPS
                       && Mathf.Abs(y1 - y2) < EPS
                       && Mathf.Abs(y0 - y1) >= EPS;

        // Sklon pozdĺž X: hrana pri x je rovná (y0==y1), hrana pri x+1 je rovná
        // (y3==y2), a navzájom sú v rôznej výške.
        bool rampAlongX = Mathf.Abs(y0 - y1) < EPS
                       && Mathf.Abs(y3 - y2) < EPS
                       && Mathf.Abs(y0 - y3) >= EPS;

        return rampAlongZ || rampAlongX;
    }

    /// <summary>
    /// Vráti true, ak vrchol na grid pozícii [vertX, vertZ] je rohom aspoň
    /// jedného OBSADENÉHO tile (tileID != 0).
    ///
    /// Jeden vrchol je zdieľaný až 4 susednými tilmi:
    ///   (vertX-1, vertZ-1), (vertX, vertZ-1), (vertX-1, vertZ), (vertX, vertZ).
    /// GetTileByIndexAny ošetrí indexy mimo mriežky (vráti tileID 0).
    ///
    /// Slúži ako guard pre úpravu terénu (LevelUp/LevelDown): ak by sa
    /// upravovaný vrchol dotýkal obsadeného tile, operácia sa zablokuje –
    /// a to aj vtedy, keď hráč klikol „vedľa“ obsadeného tile, lebo vrchol
    /// patrí aj jeho face.
    /// </summary>
    public bool IsVertexOnOccupiedTile(int vertX, int vertZ)
    {
        for (int tx = vertX - 1; tx <= vertX; tx++)
            for (int tz = vertZ - 1; tz <= vertZ; tz++)
                if (GetTileByIndexAny(tx, tz).tileID != 0)
                    return true;

        return false;
    }

    /// <summary>
    /// Vráti súradnice [x, z] VŠETKÝCH staníc danej kategórie na mape.
    ///
    /// Stanica je dlaždica s tileID == 2. Železnice a cesty zdieľajú jeden
    /// tile grid, líšia sa len kategóriou (TileCategory) – preto sa pri
    /// vyhľadávaní filtruje aj podľa category:
    ///   • TileCategory.Rail → vlakové (Train) stanice,
    ///   • TileCategory.Road → vozidlové (Vehicle) stanice.
    ///
    /// Metóda jednorázovo prejde celý tileGrid (GRID_SIZE × GRID_SIZE) a
    /// vyzbiera zhodné dlaždice. tileGrid ostáva privátny – navonok dávame
    /// len read-only zoznam súradníc, takže volajúci nemôže poškodiť mapu.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI).
    /// </summary>
    /// <param name="category">Rail = Train stanice, Road = Vehicle stanice.</param>
    public List<Vector2Int> GetAllStationCoords(TileCategory category)
    {
        var result = new List<Vector2Int>();

        // tileGrid ešte nemusí byť inicializovaný (InitializeTileEngine sa
        // volá v Start()) – vtedy vrátime prázdny zoznam.
        if (tileGrid == null)
            return result;

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                TileData td = tileGrid[x, z];
                if (td.tileID == 2 && td.category == category)
                    result.Add(new Vector2Int(x, z));
            }
        }

        return result;
    }

    /// <summary>
    /// Celkový počet staníc danej kategórie na mape. Pohodlný getter –
    /// vnútorne využíva GetAllStationCoords (počet = Count zoznamu).
    /// </summary>
    /// <param name="category">Rail = Train stanice, Road = Vehicle stanice.</param>
    public int GetStationCount(TileCategory category)
    {
        return GetAllStationCoords(category).Count;
    }

    bool IsValidIndex(Vector2Int index)
    {
        return index.x >= 0 && index.x < GRID_SIZE &&
               index.y >= 0 && index.y < GRID_SIZE;
    }

    void UpdateTileMap(int x, int z)
    {
        TileData data = tileGrid[x, z];

        if (tileMap[x, z] != null)
        {
            Destroy(tileMap[x, z]);
            tileMap[x, z] = null;
        }

        if (data.tileID == 0)
            return;

        GameObject go = new GameObject($"Tile_{x}_{z}");
        tileMap[x, z] = go;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        Mesh mesh = CreateTileMesh(x, z);
        mf.mesh = mesh;

        Material mat = new Material(snapFaceMatTex);

        // Výber textúry podľa kategórie (RAIL vs ROAD vs FACTORY) a tileID
        // (1 = trať/cesta, 2 = stanica, 3 = depo, 4 = továreň, 5 = spracovateľská)
        if (data.category == TileCategory.Road)
        {
            if (data.tileID == 1)
                mat.mainTexture = snapFaceTex_Road;
            else if (data.tileID == 2)
                mat.mainTexture = snapFaceTex_RoadStation;
            else if (data.tileID == 3)
                mat.mainTexture = snapFaceTex_RoadDepot;
        }
        else if (data.category == TileCategory.Factory)
        {
            if (data.tileID == 4)
                mat.mainTexture = snapFaceTex_Factory;
            else if (data.tileID == 5)
                mat.mainTexture = snapFaceTex_Processing;
        }
        else // Rail (alebo nezadefinované – default RAIL pre spätnú kompatibilitu)
        {
            if (data.tileID == 1)
                mat.mainTexture = snapFaceTex_Rail;
            else if (data.tileID == 2)
                mat.mainTexture = snapFaceTex_RailStation;
            else if (data.tileID == 3)
                mat.mainTexture = snapFaceTex_RailDepot;
        }

        mr.material = mat;
    }



    Mesh CreateTileMesh(int x, int z)
    {
        Vector3[] verts = new Vector3[4];

        float dy = 0.01f;

        verts[0] = new Vector3(x, FunctionalValueY(x, z) + dy, z);
        verts[1] = new Vector3(x, FunctionalValueY(x, z + 1) + dy, z + 1);
        verts[2] = new Vector3(x + 1, FunctionalValueY(x + 1, z + 1) + dy, z + 1);
        verts[3] = new Vector3(x + 1, FunctionalValueY(x + 1, z) + dy, z);

        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
        mesh.uv = new Vector2[]
        {
        new Vector2(0,0),
        new Vector2(1,0),
        new Vector2(1,1),
        new Vector2(0,1)
        };

        mesh.RecalculateNormals();
        return mesh;
    }




    // =========================
    // LOAD / SAVE
    // =========================
    //
    // KOMPLETNÁ uložená hra (binárne, VERZIOVANÉ). Jeden súbor SaveData.dat
    // drží všetko, čo sa NEDÁ deterministicky znovu vyrobiť. Veci, ktoré sa
    // dajú dopočítať z uloženého stavu, sa zámerne NEukladajú a po načítaní sa
    // vygenerujú:
    //
    //   ── UKLADÁ SA (zdroj pravdy, hráč to ovplyvnil) ─────────────────────
    //     • terén  – reálne Y výšky všetkých vrcholov (coordsF), lebo hráč ich
    //                upravuje (TerrainVertexLevel). Náhodný seed by neobnovil
    //                ručné úpravy, preto ukladáme priamo výšky.
    //     • tile grid – tileID + stateID + category každého tilu (256×256).
    //     • konto (GameEconomy.Balance, vrátane mínusu),
    //     • herný čas (GameClock: Year/Month/Day/TotalDaysElapsed),
    //     • cenový násobiteľ (ResourcePricing.Multiplier),
    //     • ročná účtovná kniha (BudgetSystem – akumulátor prebiehajúceho roka),
    //     • TOVÁRNE (FactoryRegistry) – typ, footprint, rotácia + PREMENLIVÝ
    //       stav: množstvá/kapacity slotov, mzda, mzdový level, occupancy flag,
    //       progres výstavby,
    //     • VLAKY (TrainSystem) a VOZIDLÁ (VehicleSystem) viazané na depá –
    //       typ súpravy/vozidla, vek, náklad, priradené stanice, beh/stop.
    //
    //   ── NEUKLADÁ SA (dopočíta sa pri LoadGame) ──────────────────────────
    //     • mesh terénu (prekreslí sa z výšok),
    //     • textúry/GameObjekty tilov (UpdateTileMap z tile gridu),
    //     • priradenie tovární staniciam – 9×9 catchment (RescanAll),
    //     • konkrétne trasy/sub-tile pozícia vlakov a vozidiel (A* sa prepočíta;
    //       po načítaní stoja v depe a ak bežali, znovu sa rozbehnú).
    //
    // Poradie blokov je ZHODNÉ pri zápise aj čítaní. Tile engine je orchestrátor:
    // terén/tiles/továrne/skalárne stavy rieši sám, serializáciu vlakov,
    // vozidiel a knihy delegujе na príslušné systémy (každý si vlastný stav
    // serializuje sám – pozri WriteSave/ReadSave v TrainSystem/VehicleSystem/
    // BudgetSystem v companion patchi).

    private const int SAVE_MAGIC = 0x494E4458;   // "INDX"
    private const int SAVE_VERSION = 2;

    public void SaveGame()
    {
        string path = Application.persistentDataPath + "/SaveData.dat";

        using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
        {
            // ── Hlavička ──
            bw.Write(SAVE_MAGIC);
            bw.Write(SAVE_VERSION);

            // ── 1) TERÉN (reálne Y výšky vrcholov) ──
            WriteTerrain(bw);

            // ── 2) TILE GRID ──
            for (int x = 0; x < GRID_SIZE; x++)
            {
                for (int z = 0; z < GRID_SIZE; z++)
                {
                    bw.Write(tileGrid[x, z].tileID);
                    bw.Write(tileGrid[x, z].stateID);
                    bw.Write((int)tileGrid[x, z].category);
                    // connections sa neukladá – odvodí sa pri Load zo stateID a category.
                }
            }

            // ── 3) KONTO / ČAS / CENOVÝ NÁSOBITEĽ ──
            bw.Write(GameEconomy.instance != null ? GameEconomy.instance.Balance : 0L);

            var clock = GameClock.instance;
            bw.Write(clock != null ? clock.Year : 1950);
            bw.Write(clock != null ? clock.Month : 1);
            bw.Write(clock != null ? clock.Day : 1);
            bw.Write(clock != null ? clock.TotalDaysElapsed : 0L);

            bw.Write(ResourcePricing.Multiplier);

            // ── 4) ROČNÁ ÚČTOVNÁ KNIHA ──
            if (BudgetSystem.instance != null) BudgetSystem.instance.WriteSave(bw);
            else WriteEmptyBudget(bw);

            // ── 5) TOVÁRNE ──
            WriteFactories(bw);

            // ── 6) VLAKY a VOZIDLÁ ──
            if (TrainSystem.instance != null) TrainSystem.instance.WriteSave(bw);
            else bw.Write(0);
            if (VehicleSystem.instance != null) VehicleSystem.instance.WriteSave(bw);
            else bw.Write(0);
        }

        Debug.Log("[IndicatrixAPI] Hra uložená do " + path);
    }

    public void LoadGame()
    {
        string path = Application.persistentDataPath + "/SaveData.dat";
        if (!File.Exists(path)) return;

        using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open)))
        {
            // ── Hlavička ──
            int magic = br.ReadInt32();
            int version = br.ReadInt32();
            if (magic != SAVE_MAGIC)
            {
                Debug.LogError("[IndicatrixAPI] SaveData.dat má neznámy formát (magic) – " +
                               "načítanie zrušené.");
                return;
            }
            if (version != SAVE_VERSION)
            {
                Debug.LogWarning($"[IndicatrixAPI] SaveData.dat má verziu {version} (očakávaná " +
                                 $"{SAVE_VERSION}). Pokúsim sa načítať, no formát sa mohol zmeniť.");
            }

            // ── 1) TERÉN ──
            ReadTerrain(br);

            // ── 2) TILE GRID (+ prekreslenie meshu každého tilu) ──
            for (int x = 0; x < GRID_SIZE; x++)
            {
                for (int z = 0; z < GRID_SIZE; z++)
                {
                    int tileID = br.ReadInt32();
                    int stateID = br.ReadInt32();
                    TileCategory category = (TileCategory)br.ReadInt32();

                    DirectionMask conns = RebuildConnections(tileID, stateID, ref category);
                    tileGrid[x, z] = new TileData(tileID, stateID, conns, category);
                    UpdateTileMap(x, z);
                }
            }

            // ── 3) KONTO / ČAS / NÁSOBITEĽ ──
            long balance = br.ReadInt64();
            int year = br.ReadInt32();
            int month = br.ReadInt32();
            int day = br.ReadInt32();
            long totalDays = br.ReadInt64();
            int multiplier = br.ReadInt32();

            if (GameEconomy.instance != null) GameEconomy.instance.SetBalanceSigned(balance);
            if (GameClock.instance != null) GameClock.instance.LoadDate(year, month, day, totalDays);
            ResourcePricing.Multiplier = multiplier < 1 ? 1 : multiplier;

            // EconomySystem si pri štarte zapamätal lastMonth/lastYear z PÔVODNÉHO
            // (defaultného) dátumu. Po prepísaní hodín ich treba zladiť, inak by
            // najbližší denný tik mohol omylom zúčtovať "nový mesiac/rok" navyše.
            if (EconomySystem.instance != null) EconomySystem.instance.ResyncToClock();

            // ── 4) ROČNÁ KNIHA ──
            if (BudgetSystem.instance != null) BudgetSystem.instance.ReadSave(br);
            else SkipBudget(br);

            // ── 5) TOVÁRNE (vyprázdni register, potom obnov) ──
            ReadFactories(br);

            // ── 6) PRIRADENIE TOVÁRNÍ STANICIAM – dopočítané, NEukladá sa ──
            //   Tile grid + továrne sú už načítané, takže scan má z čoho zbierať.
            RailStationRegistry.RescanAll();
            RoadStationRegistry.RescanAll();

            // ── 7) VLAKY a VOZIDLÁ ──
            if (TrainSystem.instance != null) TrainSystem.instance.ReadSave(br);
            else SkipCollection(br);
            if (VehicleSystem.instance != null) VehicleSystem.instance.ReadSave(br);
            else SkipCollection(br);
        }

        Debug.Log("[IndicatrixAPI] Hra načítaná z " + path);
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TERÉN
    // ------------------------------------------------------------------
    //
    // Ukladajú sa reálne Y výšky vrcholov (coordsF). x,z sú deterministicky
    // dané indexom (CreateMap), preto ich netreba ukladať. Po načítaní výšok
    // sa dopočíta coordsI (ConvertCoordsFI(true)) a prekreslia sa meshe
    // elementov terénu.

    private void WriteTerrain(BinaryWriter bw)
    {
        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null)
        {
            bw.Write(0);
            return;
        }

        bw.Write(tm.coordsF.Length);
        for (int i = 0; i < tm.coordsF.Length; i++)
            bw.Write(tm.coordsF[i].y);
    }

    private void ReadTerrain(BinaryReader br)
    {
        int count = br.ReadInt32();

        var tm = TerrainManager.instance;
        if (tm == null || tm.coordsF == null)
        {
            // Preskoč dáta (4 bajty na float), nech ostane stream zarovnaný.
            for (int i = 0; i < count; i++) br.ReadSingle();
            Debug.LogWarning("[IndicatrixAPI] TerrainManager nie je pripravený – výšky preskočené.");
            return;
        }

        if (count != tm.coordsF.Length)
        {
            Debug.LogWarning($"[IndicatrixAPI] Veľkosť terénu v save ({count}) sa nezhoduje s " +
                             $"aktuálnou ({tm.coordsF.Length}). Načítam min. spoločnú časť.");
        }

        int n = Mathf.Min(count, tm.coordsF.Length);
        for (int i = 0; i < n; i++)
        {
            float y = br.ReadSingle();
            tm.coordsF[i].y = y;
        }
        // Zvyšok (ak bol save väčší) dočítaj, nech je stream zarovnaný.
        for (int i = n; i < count; i++) br.ReadSingle();

        // Dopočítaj celočíselné úrovne z výšok a prekresli meshe.
        tm.ConvertCoordsFI(true);
        if (tm.terrainElements != null)
        {
            foreach (var e in tm.terrainElements)
                if (e != null) e.Rebuild();
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TILE CONNECTIONS (odvodenie pri Load, ako v pôvodnej verzii)
    // ------------------------------------------------------------------
    private DirectionMask RebuildConnections(int tileID, int stateID, ref TileCategory category)
    {
        if (tileID == 0)
            return DirectionMask.None;

        if (category == TileCategory.Road)
            return GetConnectionsForMode((GameManager.RoadConstructionMode)stateID);

        if (category == TileCategory.Factory)
            return GetConnectionsForMode((GameManager.FactoryConstructionMode)stateID); // None

        // Rail (alebo None pri starších záznamoch – dorovnáme na Rail).
        if (category == TileCategory.None) category = TileCategory.Rail;
        return GetConnectionsForMode((GameManager.RailConstructionMode)stateID);
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – TOVÁRNE
    // ------------------------------------------------------------------
    //
    // Ukladá sa typ (FactoryConstructionMode), footprint (origin + rozmery),
    // rotácia a PREMENLIVÝ stav. Pri Load sa register vyprázdni a každá továreň
    // sa znovu zaregistruje cez FactoryRegistry.Register (rovnaká cesta ako pri
    // ručnom položení), následne sa obnovia premenlivé hodnoty.

    private void WriteFactories(BinaryWriter bw)
    {
        var all = FactoryRegistry.All;
        bw.Write(all.Count);

        foreach (var f in all)
        {
            bw.Write((int)f.Definition.Mode);
            bw.Write(f.OriginX);
            bw.Write(f.OriginZ);
            bw.Write(f.Width);
            bw.Write(f.Depth);
            bw.Write((int)f.Rotation);

            // Premenlivý stav.
            bw.Write(f.EmployeeSalary);
            bw.Write(f.LevelSalary);
            bw.Write(f.OccupancyFlag);
            bw.Write(f.BuildElapsed);     // progres výstavby (sekundy)

            // Sloty (amount + capacity – kapacitu hráč mohol editovať).
            WriteSlots(bw, f.Load);
            WriteSlots(bw, f.UnLoad);
        }
    }

    private void ReadFactories(BinaryReader br)
    {
        FactoryRegistry.Clear();

        int count = br.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            var mode = (GameManager.FactoryConstructionMode)br.ReadInt32();
            int originX = br.ReadInt32();
            int originZ = br.ReadInt32();
            int width = br.ReadInt32();
            int depth = br.ReadInt32();
            var rotation = (FactoryRotation)br.ReadInt32();

            int employeeSalary = br.ReadInt32();
            short levelSalary = br.ReadInt16();
            bool occupancy = br.ReadBoolean();
            float buildElapsed = br.ReadSingle();

            // Sloty zo save (do dočasných zoznamov, aplikujeme po Register).
            var savedLoad = ReadSlots(br);
            var savedUnLoad = ReadSlots(br);

            FactoryDefinition def = FactoryDatabase.GetDefinition(mode);
            if (def == null)
            {
                Debug.LogWarning($"[IndicatrixAPI] Neznáma továreň '{mode}' v save – preskočená.");
                continue;
            }

            var inst = FactoryRegistry.Register(def, originX, originZ, width, depth, rotation);
            if (inst == null) continue;

            inst.EmployeeSalary = employeeSalary;
            inst.LevelSalary = levelSalary;
            inst.OccupancyFlag = occupancy;

            // Obnov množstvá/kapacity slotov (poradie je deterministické z definície).
            ApplySavedSlots(inst.Load, savedLoad);
            ApplySavedSlots(inst.UnLoad, savedUnLoad);

            // Obnov progres výstavby. Register cez konštruktor začína na 0 a
            // (ak má def BuildingTime>0) v stave "vo výstavbe". AdvanceConstruction
            // dotiahne elapsed na uloženú hodnotu; pri dokončení sa flag zhodí sám.
            inst.AdvanceConstruction(buildElapsed);
        }
    }

    private static void WriteSlots(BinaryWriter bw, System.Collections.Generic.List<ResourceSlot> slots)
    {
        bw.Write(slots.Count);
        foreach (var s in slots)
        {
            bw.Write((int)s.type);
            bw.Write(s.amount);
            bw.Write(s.capacity);
        }
    }

    private struct SavedSlot { public int type; public int amount; public int capacity; }

    private static System.Collections.Generic.List<SavedSlot> ReadSlots(BinaryReader br)
    {
        int n = br.ReadInt32();
        var list = new System.Collections.Generic.List<SavedSlot>(n);
        for (int i = 0; i < n; i++)
            list.Add(new SavedSlot { type = br.ReadInt32(), amount = br.ReadInt32(), capacity = br.ReadInt32() });
        return list;
    }

    private static void ApplySavedSlots(System.Collections.Generic.List<ResourceSlot> target,
                                        System.Collections.Generic.List<SavedSlot> saved)
    {
        // Spárуj podľa typu (robustnejšie než podľa indexu, keby sa definícia
        // časom rozšírila). amount aj capacity prepíšeme zo save.
        foreach (var sv in saved)
        {
            var rt = (ResourceType)sv.type;
            foreach (var slot in target)
            {
                if (slot.type == rt)
                {
                    slot.capacity = Mathf.Max(0, sv.capacity);
                    slot.amount = Mathf.Clamp(sv.amount, 0, slot.capacity);
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // POMOCNÉ – PRÁZDNE / PRESKOČENIE BLOKOV (keď systém v scéne chýba)
    // ------------------------------------------------------------------

    private void WriteEmptyBudget(BinaryWriter bw)
    {
        // 10 položiek knihy (pozri BudgetSystem.WriteSave) – samé nuly.
        bw.Write(0L);  // Revenue
        bw.Write(0); bw.Write(0L);  // TrainsBought, TrainsCost
        bw.Write(0); bw.Write(0L);  // VehiclesBought, VehiclesCost
        bw.Write(0); bw.Write(0L);  // FactoriesBought, FactoriesCost
        bw.Write(0L);  // TrainOperating
        bw.Write(0L);  // VehicleOperating
        bw.Write(0L);  // EmployeeSalaries
    }

    private void SkipBudget(BinaryReader br)
    {
        br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt32(); br.ReadInt64();
        br.ReadInt64();
        br.ReadInt64();
        br.ReadInt64();
    }

    // Vlaky/vozidlá serializujú prvý int = count, potom count záznamov. Ak
    // systém v scéne nie je, nevieme bezpečne preskočiť premenlivú dĺžku –
    // ošetríme len count==0 (čo zapisuje SaveGame, keď systém chýbal).
    private void SkipCollection(BinaryReader br)
    {
        int count = br.ReadInt32();
        if (count != 0)
            Debug.LogWarning("[IndicatrixAPI] Save obsahuje vlaky/vozidlá, ale systém v scéne " +
                             "chýba – ich blok sa nedá preskočiť. Stream je odteraz nezarovnaný.");
    }

    // =========================
    // ORIGINAL SNAP FUNCTIONS
    // =========================

    public Vector3 SnapVertex(Vector3 hitPoint)
    {
        HideSnapLines();
        ShowSnapSphere();

        int snapX = 0, snapZ = 0;
        float snapY = 0;

        int xx = (int)hitPoint.x; float x = hitPoint.x - xx;
        int zz = (int)hitPoint.z; float z = hitPoint.z - zz;

        if (x >= 0.0f && x <= 0.5f) { snapX = xx; }
        else snapX = xx + 1;

        if (z >= 0.0f && z <= 0.5f) { snapZ = zz; }
        else snapZ = zz + 1;

        snapY = FunctionalValueY(snapX, snapZ);

        snapSphere.transform.position = new Vector3(snapX, snapY, snapZ);

        return new Vector3(snapX, snapY, snapZ);
    }

    public Vector3 SnapLineFace(Vector3 hitPoint)
    {
        HideSnapSphere();
        ShowSnapLines();

        Vector3[] verts = GetFaceVertices(hitPoint);

        lineRenderer01.SetPosition(0, verts[0]);
        lineRenderer01.SetPosition(1, verts[1]);

        lineRenderer02.SetPosition(0, verts[1]);
        lineRenderer02.SetPosition(1, verts[2]);

        lineRenderer03.SetPosition(0, verts[2]);
        lineRenderer03.SetPosition(1, verts[3]);

        lineRenderer04.SetPosition(0, verts[3]);
        lineRenderer04.SetPosition(1, verts[0]);

        return GetCenterFace(hitPoint);
    }

    public Vector3 SnapMeshFace(Vector3 hitPoint)
    {
        return GetCenterFace(hitPoint);
    }

    /// <summary>
    /// SnapAreaFace – snap vizuál pre VIAC-TILE footprint (továrne).
    ///
    /// Analógia k SnapLineFace, ale namiesto jednej 1×1 plochy zvýrazní
    /// obvod celého obdĺžnika N×M tilov, do ktorého sa továreň umiestni.
    /// Hit point určuje anchor tile; obdĺžnik sa dopočíta cez FactoryFootprint
    /// rovnako ako v SetTile (FACTORY verzia), takže náhľad presne zodpovedá
    /// tomu, čo sa po kliknutí položí.
    ///
    /// Používa tie isté 4 line renderery ako SnapLineFace – vykreslí nimi
    /// 4 strany veľkého obdĺžnika namiesto malého tilu.
    ///
    /// PARAMETER rotation: footprint sa zvýrazní UŽ otočený, takže náhľad
    /// presne zodpovedá tomu, čo SetTile s rovnakou rotáciou položí.
    /// Default = Deg0.
    /// </summary>
    public Vector3 SnapAreaFace(Vector3 hitPoint, GameManager.FactoryConstructionMode mode,
                                FactoryRotation rotation = FactoryRotation.Deg0)
    {
        HideSnapSphere();
        ShowSnapLines();

        Vector2Int anchor = SnapTileIndex(hitPoint);
        FactoryFootprint fp = GetFactoryFootprint(mode, rotation);

        // Ľavý-dolný roh footprintu (rovnaký výpočet ako v SetTile).
        int minX = anchor.x - fp.anchorX;
        int minZ = anchor.y - fp.anchorZ;
        int maxX = minX + fp.width;   // +width  → pravá hrana posledného tilu
        int maxZ = minZ + fp.depth;   // +depth  → horná hrana posledného tilu

        float dy = 0.02f;

        // 4 rohy veľkého obdĺžnika (proti smeru hod. ručičiek).
        Vector3 c0 = new Vector3(minX, FunctionalValueY(minX, minZ) + dy, minZ);
        Vector3 c1 = new Vector3(minX, FunctionalValueY(minX, maxZ) + dy, maxZ);
        Vector3 c2 = new Vector3(maxX, FunctionalValueY(maxX, maxZ) + dy, maxZ);
        Vector3 c3 = new Vector3(maxX, FunctionalValueY(maxX, minZ) + dy, minZ);

        lineRenderer01.SetPosition(0, c0);
        lineRenderer01.SetPosition(1, c1);

        lineRenderer02.SetPosition(0, c1);
        lineRenderer02.SetPosition(1, c2);

        lineRenderer03.SetPosition(0, c2);
        lineRenderer03.SetPosition(1, c3);

        lineRenderer04.SetPosition(0, c3);
        lineRenderer04.SetPosition(1, c0);

        // Geometrický stred footprintu (užitočné pre prípadné umiestnenie
        // 3D modelu továrne neskôr).
        float cx = (minX + maxX) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        return new Vector3(cx, FunctionalValueY(anchor.x, anchor.y), cz);
    }

    Vector3[] GetFaceVertices(Vector3 hitPoint)
    {
        int xx = Mathf.FloorToInt(hitPoint.x);
        int zz = Mathf.FloorToInt(hitPoint.z);

        float dy = 0.01f;

        Vector3[] verts = new Vector3[4];

        verts[0] = new Vector3(xx, FunctionalValueY(xx, zz) + dy, zz);
        verts[1] = new Vector3(xx, FunctionalValueY(xx, zz + 1) + dy, zz + 1);
        verts[2] = new Vector3(xx + 1, FunctionalValueY(xx + 1, zz + 1) + dy, zz + 1);
        verts[3] = new Vector3(xx + 1, FunctionalValueY(xx + 1, zz) + dy, zz);

        return verts;
    }

    public Vector3 GetCenterFace(Vector3 hitPoint)
    {
        int xx = Mathf.FloorToInt(hitPoint.x);
        int zz = Mathf.FloorToInt(hitPoint.z);

        float dx = xx + 0.5f;
        float dz = zz + 0.5f;
        float dy = FunctionalValueY(xx, zz);

        return new Vector3(dx, dy, dz);
    }

    float FunctionalValueY(int snapX, int snapZ)
    {
        int width = TerrainManager.instance.terrainWidth + 1;

        int index = snapZ * width + snapX;

        return TerrainManager.instance.coordsF[index].y;
    }


}
```


### TrainStock.cs

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// TrainStock.cs
/// ─────────────────────────────────────────────────────────────────────────
/// VOZOVÝ PARK – čisté dátové štruktúry (žiadne MonoBehaviour, žiadne Unity API).
///
/// Tento súbor je zámerne oddelený od TrainSystem.cs:
///   • TrainSystem.cs = pohybová logika, A* pathfinding, vizuál (kvádre).
///   • TrainStock.cs  = "čo je vlak / vagón" – iba dáta a katalógy.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ROZDELENIE: "Spec" (predloha) vs. "Instance" (inštancia v hre)
///
///   *Spec     – nemenná predloha typu (katalógová karta). Spoločná pre všetky
///               vlaky/vagóny toho istého typu. Napr. TrainSpec "Iron Dragon"
///               má Cost, Speed, Power... – tieto hodnoty sú rovnaké pre každý
///               vyrobený kus.
///
///   *Instance – konkrétny kus v hre, vytvorený v depe. Drží referenciu na
///               svoj Spec + premenlivý stav daného kusu (napr. Age vlaku,
///               CurrentCapacity vagónu – tie sa líšia kus od kusu a menia
///               sa počas hry).
///
/// Vďaka tomu sa nemenné parametre (Cost, Speed...) NEDUPLIKUJÚ do každého
/// vlaku – sú raz v katalógu. UI okno s detailmi vlaku si ich len prečíta
/// cez Instance.Spec.
/// ─────────────────────────────────────────────────────────────────────────
///
/// POZNÁMKA K TYPOM (revízia):
///   Polia Weight / Power / YearOfManufacture (TrainSpec) a Weight (WagonSpec)
///   sú modelované ako int (číselné hodnoty), nie ako string s jednotkou.
///   Pôvodne to boli stringy ("55t", "720hp", "1984", "5t"), ale nikde mimo
///   tohto súboru sa nečítali a zadané dáta vlakov/vagónov sú číselné. Číselný
///   typ je tak vhodnejší (dá sa s ním počítať – hmotnosť súpravy, výkon a pod.)
///   a presne zodpovedá zadaným parametrom.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>

namespace Game.TrainStock
{
    // =========================================================================
    // TYP VAGÓNU
    //
    // Zadanie hovorí o "hash table (string, int)" = [textové označenie, číselné
    // označenie]. To je ale len JEDNA dvojica hodnôt pre jeden typ vagónu –
    // nie kolekcia. Použiť Dictionary/Hashtable na uloženie jednej dvojice je
    // zbytočné (alokácia, žiadna typová bezpečnosť, neprehľadné).
    //
    // Preto je typ vagónu modelovaný ako malý nemenný struct WagonType
    // (Name + Id). "Hash table" charakter – teda vyhľadanie typu podľa názvu
    // alebo podľa čísla – zabezpečuje statický register WagonCatalog, ktorý
    // skutočné slovníky (ByName, ById) drží centrálne na jednom mieste.
    //
    // Ak by si predsa len chcel priamo slovník na konkrétnom vagóne, dá sa
    // získať jednoriadkovo: WagonType.AsPair() vráti KeyValuePair<string,int>.
    //
    // DÔLEŽITÉ – ZHODA ID S ResourceType (FactorySystem.cs):
    //   Číselné Id typu vagónu je ZÁMERNE rovnaké ako (int)ResourceType:
    //     "Coal Truck"        #1  → ResourceType.Coal                = 1
    //     "Wood Truck"        #2  → ResourceType.Wood                = 2
    //     "Iron Ore Truck"    #3  → ResourceType.IronOre             = 3
    //     ...
    //     "Electronics Truck" #16 → ResourceType.ElectronicsProducts = 16
    //   Vďaka tomu TrainTradeSystem.WagonResource() namapuje vagón na surovinu
    //   bez extra poľa (Id == (int)ResourceType). Pri pridávaní nového vagónu
    //   teda Id MUSÍ zodpovedať príslušnej hodnote v enum ResourceType.
    // =========================================================================

    /// <summary>
    /// Typ vagónu – nemenná dvojica [textové označenie, číselné označenie].
    /// Napr. ("Coal Truck", 1), ("Wood Truck", 2).
    /// </summary>
    [Serializable]
    public readonly struct WagonType : IEquatable<WagonType>
    {
        /// <summary>Textové označenie typu vagónu, napr. "Coal Truck".</summary>
        public readonly string Name;

        /// <summary>Číselné označenie typu vagónu (== (int)ResourceType), napr. 1.</summary>
        public readonly int Id;

        public WagonType(string name, int id)
        {
            Name = name;
            Id = id;
        }

        /// <summary>
        /// Vráti typ vagónu ako dvojicu kľúč–hodnota (string, int).
        /// Pohodlné, ak niektorá časť kódu očakáva "hash table" reprezentáciu.
        /// </summary>
        public KeyValuePair<string, int> AsPair() => new KeyValuePair<string, int>(Name, Id);

        public bool Equals(WagonType other) => Id == other.Id && Name == other.Name;
        public override bool Equals(object obj) => obj is WagonType w && Equals(w);
        public override int GetHashCode() => Id;
        public override string ToString() => $"{Name} (#{Id})";
    }

    // =========================================================================
    // PREDLOHA VAGÓNU – WagonSpec
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vagónu.
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class WagonSpec
    {
        /// <summary>
        /// Názov vagónu, napr. "Coal Truck".
        /// Pozn.: názov sa dá odvodiť aj z Type.Name. Necháme ho ako samostatné
        /// pole, lebo UI prvok WagonTypeDropdown pracuje s názvami priamo a je
        /// pohodlnejšie mať jeden zdroj pre zobrazenie. Drží sa konzistentne
        /// s Type.Name (viď WagonCatalog, kde sa oba nastavujú spolu).
        /// </summary>
        public string Name;

        /// <summary>Typ vagónu – [textové označenie, číselné označenie].</summary>
        public WagonType Type;

        /// <summary>Cena vagónu.</summary>
        public int Cost;

        /// <summary>Hmotnosť vagónu (číselne, napr. 5).</summary>
        public int Weight;

        /// <summary>Maximálna kapacita vagónu.</summary>
        public int MaximumCapacity;

        public WagonSpec(string name, WagonType type, int cost, int weight, int maximumCapacity)
        {
            Name = name;
            Type = type;
            Cost = cost;
            Weight = weight;
            MaximumCapacity = maximumCapacity;
        }
    }

    // =========================================================================
    // INŠTANCIA VAGÓNU – WagonInstance
    // =========================================================================

    /// <summary>
    /// Konkrétny vagón existujúci v hre. Drží referenciu na svoj WagonSpec
    /// (nemenné parametre) + premenlivý stav (aktuálna kapacita).
    /// </summary>
    [Serializable]
    public sealed class WagonInstance
    {
        /// <summary>Predloha typu tohto vagónu (nemenné parametre).</summary>
        public readonly WagonSpec Spec;

        /// <summary>
        /// Aktuálna kapacita (naložený náklad). Pri vytvorení 0.
        /// Mení sa počas hry pri nakladaní/vykladaní (viď TrainTradeSystem).
        /// </summary>
        public int CurrentCapacity;

        public WagonInstance(WagonSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            CurrentCapacity = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public WagonType Type => Spec.Type;
        public int Cost => Spec.Cost;
        public int Weight => Spec.Weight;
        public int MaximumCapacity => Spec.MaximumCapacity;
    }

    // =========================================================================
    // PREDLOHA VLAKU / LOKOMOTÍVY – TrainSpec
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vlaku (lokomotívy).
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class TrainSpec
    {
        /// <summary>Názov vlaku, napr. "Iron Dragon".</summary>
        public string Name;

        /// <summary>Cena vlaku.</summary>
        public int Cost;

        /// <summary>Prevádzkové náklady.</summary>
        public int OperatingCosts;

        /// <summary>Rýchlosť vlaku.</summary>
        public int Speed;

        /// <summary>Hmotnosť vlaku (číselne, napr. 150).</summary>
        public int Weight;

        /// <summary>Výkon vlaku (číselne, napr. 60).</summary>
        public int Power;

        /// <summary>Životnosť vlaku.</summary>
        public int ServiceLife;

        /// <summary>Servisný interval.</summary>
        public int ServicingInterval;

        /// <summary>Rok výroby (číselne, napr. 1930).</summary>
        public int YearOfManufacture;

        public TrainSpec(string name, int cost, int operatingCosts, int speed,
                         int weight, int power, int serviceLife,
                         int servicingInterval, int yearOfManufacture)
        {
            Name = name;
            Cost = cost;
            OperatingCosts = operatingCosts;
            Speed = speed;
            Weight = weight;
            Power = power;
            ServiceLife = serviceLife;
            ServicingInterval = servicingInterval;
            YearOfManufacture = yearOfManufacture;
        }
    }

    // =========================================================================
    // INŠTANCIA VLAKU – TrainInstance
    //
    // Pozn.: TrainInstance je čisto dátový popis vlakovej súpravy
    // (lokomotíva + vagóny). Pohybový/pathfinding stav (PathData, stanice,
    // isRunning...) zostáva v TrainSystem.TrainData. TrainData drží referenciu
    // na TrainInstance – viď integráciu nižšie v poznámke pre TrainSystem.cs.
    // =========================================================================

    /// <summary>
    /// Konkrétny vlak existujúci v hre. Drží referenciu na svoj TrainSpec
    /// (nemenné parametre) + premenlivý stav (vek) + zoznam vagónov.
    /// </summary>
    [Serializable]
    public sealed class TrainInstance
    {
        /// <summary>Predloha typu tohto vlaku (nemenné parametre).</summary>
        public readonly TrainSpec Spec;

        /// <summary>
        /// Aktuálny vek vlaku. Pri vytvorení 0.
        /// Mení sa počas hry (pribúdaním herného času).
        /// </summary>
        public int Age;

        /// <summary>Vagóny pripojené k tomuto vlaku (poradie = poradie v súprave).</summary>
        public readonly List<WagonInstance> Wagons = new List<WagonInstance>();

        public TrainInstance(TrainSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Age = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public int Cost => Spec.Cost;
        public int OperatingCosts => Spec.OperatingCosts;
        public int Speed => Spec.Speed;
        public int Weight => Spec.Weight;
        public int Power => Spec.Power;
        public int ServiceLife => Spec.ServiceLife;
        public int ServicingInterval => Spec.ServicingInterval;
        public int YearOfManufacture => Spec.YearOfManufacture;
    }

    // =========================================================================
    // KATALÓG VLAKOV – TrainCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vlakov. Slúži ako zdroj
    // pre TrainTypeDropdown (názvy) aj pre TrainSystem (vytvorenie inštancie).
    // Poradie v zozname = poradie položiek v TrainTypeDropdown (index 1:1).
    // =========================================================================

    public static class TrainCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vlakov, v poradí zhodnom s TrainTypeDropdown.
        /// Tri lokomotívy: "Iron Dragon", "Desert Runner", "Thunderbolt".
        /// </summary>
        public static readonly IReadOnlyList<TrainSpec> All = new List<TrainSpec>
        {
            // Vlak 1
            new TrainSpec(
                name:              "Iron Dragon",
                cost:              15000,
                operatingCosts:    50,
                speed:             60,
                weight:            150,
                power:             60,
                serviceLife:       8,
                servicingInterval: 150,
                yearOfManufacture: 1930
            ),
            // Vlak 2
            new TrainSpec(
                name:              "Desert Runner",
                cost:              23000,
                operatingCosts:    70,
                speed:             100,
                weight:            200,
                power:             90,
                serviceLife:       15,
                servicingInterval: 200,
                yearOfManufacture: 1970
            ),
            // Vlak 3
            new TrainSpec(
                name:              "Thunderbolt",
                cost:              33000,
                operatingCosts:    80,
                speed:             150,
                weight:            200,
                power:             80,
                serviceLife:       20,
                servicingInterval: 180,
                yearOfManufacture: 2000
            ),
        };

        /// <summary>Názvy typov vlakov – priamo použiteľné pre TrainTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti TrainSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static TrainSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti TrainSpec podľa názvu, alebo null.</summary>
        public static TrainSpec ByName(string name)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name) return All[i];
            return null;
        }
    }

    // =========================================================================
    // KATALÓG VAGÓNOV – WagonCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vagónov. Drží aj
    // "hash table" lookupy (ByName, ById) – to je miesto, kde má slovník
    // skutočný zmysel (kolekcia viacerých typov).
    //
    // Id každého vagónu sa zhoduje s (int)ResourceType (FactorySystem.cs),
    // takže keď vlak dorazí do stanice, TrainTradeSystem.WagonResource()
    // automaticky rozpozná surovinu pre ktorýkoľvek z týchto 16 typov.
    // =========================================================================

    public static class WagonCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vagónov, v poradí zhodnom s WagonTypeDropdown.
        /// 16 typov – Id zodpovedá ResourceType (1 = Coal ... 16 = Electronics).
        /// </summary>
        public static readonly IReadOnlyList<WagonSpec> All = new List<WagonSpec>
        {
            // Vagón 1 – už existoval
            new WagonSpec(
                name:            "Coal Truck",
                type:            new WagonType("Coal Truck", 1),
                cost:            35,
                weight:          5,
                maximumCapacity: 5
            ),
            // Vagón 2 – už existoval
            new WagonSpec(
                name:            "Wood Truck",
                type:            new WagonType("Wood Truck", 2),
                cost:            45,
                weight:          7,
                maximumCapacity: 5
            ),
            // Vagón 3
            new WagonSpec(
                name:            "Iron Ore Truck",
                type:            new WagonType("Iron Ore Truck", 3),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 4
            new WagonSpec(
                name:            "Gold Truck",
                type:            new WagonType("Gold Truck", 4),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 5
            new WagonSpec(
                name:            "Silver Truck",
                type:            new WagonType("Silver Truck", 5),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 6
            new WagonSpec(
                name:            "Livestock Truck",
                type:            new WagonType("Livestock Truck", 6),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 7
            new WagonSpec(
                name:            "Grain Truck",
                type:            new WagonType("Grain Truck", 7),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 8
            new WagonSpec(
                name:            "Oil Truck",
                type:            new WagonType("Oil Truck", 8),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 9
            new WagonSpec(
                name:            "Boards Truck",
                type:            new WagonType("Boards Truck", 9),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 10
            new WagonSpec(
                name:            "Plastic Truck",
                type:            new WagonType("Plastic Truck", 10),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 11
            new WagonSpec(
                name:            "Meat Truck",
                type:            new WagonType("Meat Truck", 11),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 12
            new WagonSpec(
                name:            "Flour Truck",
                type:            new WagonType("Flour Truck", 12),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 13
            new WagonSpec(
                name:            "Metals Truck",
                type:            new WagonType("Metals Truck", 13),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 14
            new WagonSpec(
                name:            "Glass Truck",
                type:            new WagonType("Glass Truck", 14),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 15
            new WagonSpec(
                name:            "Furniture Truck",
                type:            new WagonType("Furniture Truck", 15),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
            // Vagón 16
            new WagonSpec(
                name:            "Electronics Truck",
                type:            new WagonType("Electronics Truck", 16),
                cost:            1000,
                weight:          2,
                maximumCapacity: 5
            ),
        };

        // "Hash table" lookupy – tu má slovník zmysel, lebo ide o kolekciu typov.
        private static readonly Dictionary<string, WagonSpec> _byName = BuildByName();
        private static readonly Dictionary<int, WagonSpec> _byId = BuildById();

        private static Dictionary<string, WagonSpec> BuildByName()
        {
            var d = new Dictionary<string, WagonSpec>();
            foreach (var w in All) d[w.Type.Name] = w;
            return d;
        }

        private static Dictionary<int, WagonSpec> BuildById()
        {
            var d = new Dictionary<int, WagonSpec>();
            foreach (var w in All) d[w.Type.Id] = w;
            return d;
        }

        /// <summary>Názvy typov vagónov – priamo použiteľné pre WagonTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti WagonSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static WagonSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti WagonSpec podľa textového označenia ("Coal Truck"), alebo null.</summary>
        public static WagonSpec ByName(string name)
            => name != null && _byName.TryGetValue(name, out var w) ? w : null;

        /// <summary>Vráti WagonSpec podľa číselného označenia (1, 2, ...), alebo null.</summary>
        public static WagonSpec ById(int id)
            => _byId.TryGetValue(id, out var w) ? w : null;
    }
}
```


### TrainSystem.cs

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.TrainStock;

/// <summary>
/// TrainSystem.cs
///
/// PARALELIZMUS a DEBOUNCE:
/// ─────────────────────────────────────────────────────────────────────────
/// ✔ A* pathfinding beží na Thread Pool cez Task.Run (AStarPathThreaded).
///   Pracuje s int[] snapshotom tileGrid – žiadne Unity API z vlákna.
///   Výsledky sa prenášajú cez ConcurrentQueue, aplikujú sa v Update().
///
/// ✔ OnMapChanged() – DEBOUNCE (riešenie trhnutia vlakov):
///   Každé volanie len nastaví flag + resetuje timer (_mapChangedDebounceTimer).
///   Žiadna práca na hlavnom vlákne pri kliku (žiadny SnapshotTileGrid).
///   Update() odpočítava timer; až po uplynutí DEBOUNCE_DELAY od posledného
///   kliku spustí SnapshotTileGrid() + Task.Run A* pre bežiace vlaky.
///   Vlaky sa hýbu ďalej po starej ceste počas čakania na debounce –
///   žiadne zastavenie, žiadne trhnutie.
///
///   Prepočítavajú sa len vlaky, ktorých aktuálna cesta mohla byť dotknutá
///   (isRunning && !isComputingPath). Vlaky, ktoré práve čakajú na stanici
///   alebo pri prerušení, sa neprepočítavajú zbytočne.
///
/// ✘ UpdateTrain / pohyb – hlavné vlákno (Unity API, thread-unsafe).
/// ✘ CreateTrain / RemoveTrain – hlavné vlákno (GameObject API).
/// ─────────────────────────────────────────────────────────────────────────
///
/// VLAKOVÁ SÚPRAVA – „Distance-Based Path System":
/// ─────────────────────────────────────────────────────────────────────────
/// • Trasa je reprezentovaná ako PathData (List<Vector3> bodov + predpočítané
///   kumulatívne dĺžky segmentov). Zdroj: A* ako List<Vector2Int> → konvertovaný
///   na waypointy cez BuildWaypointPath (centrá + hrany + krivkové rohy).
/// • Lokomotíva má skalárnu hodnotu locomotiveDistance (vzdialenosť pozdĺž cesty).
///   Každý frame: locomotiveDistance += speed * deltaTime.
/// • Vagón i má offsetDist = i * wagonSpacing za lokomotívou.
///   wagonDistance = locomotiveDistance - (i * wagonSpacing), min 0.
/// • Pozícia = PathData.GetPositionAtDistance(d)   → Vector3.Lerp na segmente.
/// • Rotácia = PathData.GetDirectionAtDistance(d)  → Quaternion.LookRotation.
/// • ŽIADNA história, ŽIADNE oneskorenie, ŽIADNE posHistory / historyAccum.
/// • Deterministické: rovnaký vstup → rovnaký výstup každý frame.
///
/// WAYPOINT-BASED MOVEMENT (NOVÉ):
///   Trasa už nie je center→center. Pre každý prechod A→B sa generuje:
///     A.center → A.edge(smer A→B) → B.edge(smer B→A) → B.center
///   Pri krivkovej dlaždici sa center NEPOUŽÍVA – generuje sa:
///     B.entryEdge → B.exitEdge  (priama diagonála cez vnútro dlaždice, ~45°)
///   Pri výhybkovej dlaždici (RailSwitch*) sa správanie líši podľa toho,
///   či ide o priamy prechod (proti-smery) alebo o odbočku (kolmé smery):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge (ako priama dlaždica)
///     • ODBOČKA: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge
///       (3 priame segmenty, stredný diagonálny ~45°; vizuálne železničný
///        turnout, BEZ bezier kriviek, BEZ splajnov, BEZ smoothingu.)
///   Pri križovatkovej dlaždici (RailCrossroad, 4 spojenia) platí to isté:
///     • PRIAMY (Top↔Bottom, Left↔Right): edge → center → edge (90°)
///     • ROHOVÝ (napr. Top↔Right): edge → innerEntry → innerExit → edge
///       (45° turnout, identický s odbočkou výhybky)
///   Krivka, odbočka výhybky aj rohový prechod križovatky používajú
///   VÝHRADNE priame čiarové segmenty spojené lineárnym Lerp-om medzi
///   waypointmi.
///
/// PREJAZD CEZ SVAH (LevelUp / LevelDown):
///   Terén je výškový raster v ROHOCH dlaždíc – jedna dlaždica je
///   naklonený quad medzi 4 rohovými vertexmi s rôznymi Y. Y-súradnica
///   každého waypointu sa získava biliniárnou interpoláciou výšok týchto
///   4 rohov (GetTileSurfaceY), takže každý bod na trase leží presne na
///   šikmej ploche dlaždice. Vlak na svahu ide pod skutočným uhlom
///   stúpania – uhol je daný geometriou terénu, nie pevnou konštantou.
///   Hladkosť na hrane medzi dlaždicami je zaručená automaticky:
///   A.right-edge a B.left-edge zdieľajú tie isté dva rohové vertexy,
///   takže ich interpolovaná Y vychádza identická → žiadny schod, žiadny
///   90° lom medzi dlaždicami. Logika svahu je transparentná pre všetky
///   typy dlaždíc – priame, krivky, výhybky, križovatky.
///
/// OBRAT SMERU NA STANICI:
///   path.Reverse()  +  locomotiveDistance = totalLength - locomotiveDistance
///   → okamžitý, presný, bez usadzovania.
///
/// PARAMETRIZÁCIA:
///   wagonCount   – počet vagónov (1 až 10, predvolene 5).
///   wagonSpacing – rozostup členov súpravy pozdĺž trajektórie (predvolene 0.85).
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class TrainSystem : MonoBehaviour
{
    public static TrainSystem instance;

    // =====================================================================
    // KONFIGURÁCIA VLAKOVEJ SÚPRAVY
    // =====================================================================

    [Range(1, 10)]
    public int wagonCount = 5;

    public float wagonSpacing = 0.85f;

    // =====================================================================
    // POMOCNÁ TRIEDA – PathData
    // Ukladá trasu ako Vector3 body + predpočítané kumulatívne vzdialenosti.
    // =====================================================================

    public class PathData
    {
        /// <summary>Body trasy (waypointy: centrá + hrany + krivkové rohy). Nemeniť po inicializácii.</summary>
        public readonly List<Vector3> points;

        /// <summary>
        /// cumulativeLengths[i] = vzdialenosť od points[0] po points[i].
        /// cumulativeLengths[0] == 0 vždy.
        /// </summary>
        public readonly float[] cumulativeLengths;

        /// <summary>Celková dĺžka trasy = cumulativeLengths[Count-1].</summary>
        public readonly float totalLength;

        public int Count => points.Count;

        public PathData(List<Vector3> pts)
        {
            points = pts;
            cumulativeLengths = new float[pts.Count];
            cumulativeLengths[0] = 0f;
            for (int i = 1; i < pts.Count; i++)
                cumulativeLengths[i] = cumulativeLengths[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            totalLength = pts.Count > 0 ? cumulativeLengths[pts.Count - 1] : 0f;
        }

        /// <summary>
        /// Vráti pozíciu na trase pre zadanú vzdialenosť od začiatku.
        /// Vzdialenosť je upnutá na [0, totalLength].
        /// Interpolácia je VÝHRADNE lineárna (Vector3.Lerp) medzi dvoma susednými bodmi.
        /// </summary>
        public Vector3 GetPositionAtDistance(float dist)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];

            dist = Mathf.Clamp(dist, 0f, totalLength);

            // Binárne vyhľadávanie segmentu
            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            float segStart = cumulativeLengths[lo];
            float segEnd = cumulativeLengths[hi];
            float segLen = segEnd - segStart;

            if (segLen < 1e-6f) return points[hi];

            float localT = (dist - segStart) / segLen;
            return Vector3.Lerp(points[lo], points[hi], localT);
        }

        /// <summary>
        /// Vráti normalizovaný smer pohybu na zadanej vzdialenosti.
        /// Smer = (nextPoint - currentPoint).normalized pre segment, v ktorom leží dist.
        /// NIKDY neakumuluje rotácie, NIKDY nepoužíva dáta z predchádzajúceho snímku.
        /// </summary>
        public Vector3 GetDirectionAtDistance(float dist)
        {
            if (points.Count < 2) return Vector3.forward;

            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            Vector3 dir = points[hi] - points[lo];
            return dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.forward;
        }

        /// <summary>
        /// Vráti obrátenú kópiu PathData (pre obrat smeru na stanici).
        /// Pôvodný objekt zostáva nezmenený.
        /// </summary>
        public PathData Reversed()
        {
            var rev = new List<Vector3>(points);
            rev.Reverse();
            return new PathData(rev);
        }
    }

    // =====================================================================
    // DÁTOVÉ ŠTRUKTÚRY
    // =====================================================================

    public class TrainData
    {
        public int depotX, depotZ;

        /// <summary>
        /// Dátový popis vlakovej súpravy – typ vlaku + jeho atribúty + vagóny
        /// a ich atribúty (Name, Cost, Speed, kapacity vagónov atď.).
        /// Definované v TrainStock.cs. Pohybový/pathfinding stav zostáva
        /// v poliach TrainData nižšie; toto je oddelená "obsahová" časť.
        ///
        /// Naplní sa pri CreateTrain podľa voľby z TrainTypeDropdown /
        /// WagonTypeDropdown. UI okno s detailmi vlaku si odtiaľto len číta.
        /// </summary>
        public TrainInstance consist;

        /// <summary>Skrytá kocka (Renderer off) – pohybová logika (pozícia hlavy).</summary>
        public GameObject trainObject;

        /// <summary>Vizuálna lokomotíva (CYAN kváder).</summary>
        public GameObject locomotive;

        /// <summary>Vizuálne vagóny (GREY kvádre). Count = wagonCount pri vytvorení.</summary>
        public List<GameObject> wagons = new List<GameObject>();

        // ------------------------------------------------------------------
        // DISTANCE-BASED PATH STATE
        // ------------------------------------------------------------------

        /// <summary>
        /// Aktuálna trasa ako PathData (waypointy + kumulatívne dĺžky).
        /// null = žiadna aktívna trasa.
        /// </summary>
        public PathData activePath;

        /// <summary>
        /// Vzdialenosť lokomotívy od začiatku activePath.
        /// Každý frame: locomotiveDistance += trainSpeed * Time.deltaTime.
        /// </summary>
        public float locomotiveDistance;

        /// <summary>Rýchlosť pohybu vlaku (jednotky/sekunda). Odvodená z MOVE_TIME.</summary>
        public float trainSpeed;

        // ------------------------------------------------------------------
        // Pôvodné polia TrainData (nezmenené)
        // ------------------------------------------------------------------
        public List<Vector2Int> stations;
        public int currentStationIndex;
        public bool isRunning;
        public bool isWaiting;
        public bool isReturningToDepot;
        public bool isAtDepot;
        public bool reverseDirection;
        public List<Vector2Int> currentPath;
        public int pathIndex;
        public Vector2Int currentTile;
        public float moveTimer;
        public float waitTimer;
        public bool isComputingPath;
        public List<Vector2Int> pendingPath;
        public bool isGoingToDepotViaStation;
        public bool isStoppedAwaitingDepotReturn;
        public bool pendingReturnToDepot;

        // ------------------------------------------------------------------
        // OBCHOD NA STANICI (TradeSystem)
        // ------------------------------------------------------------------
        /// <summary>
        /// True, ak v AKTUÁLNEJ zastávke na stanici už prebehol pokus o
        /// transakciu (výmenu tovaru). Bráni tomu, aby sa obchod spustil
        /// každý frame – spustí sa práve raz, po 2 s čakania. Resetuje sa
        /// pri každom novom príchode na stanicu (OnPathComplete).
        /// </summary>
        public bool tradeDoneAtStation;

        /// <summary>Index v currentPath kde activePath zacina. Pouziva sa v UpdateCurrentTile.</summary>
        public int pathIndexAtPathStart;

        /// <summary>
        /// Mapovanie waypoint-index → currentPath-index.
        /// Pre activePath.points[i] hovorí, ku ktorej dlaždici v currentPath waypoint patrí.
        /// Používa sa v UpdateCurrentTile pri waypoint-based pohybe.
        /// </summary>
        public int[] waypointToTileIdx;

        public TrainData(int dx, int dz)
        {
            depotX = dx; depotZ = dz;
            stations = new List<Vector2Int>();
            currentStationIndex = 0;
            isRunning = false; isWaiting = false;
            isReturningToDepot = false; isAtDepot = true;
            reverseDirection = false;
            currentPath = new List<Vector2Int>();
            pathIndex = 0;
            currentTile = new Vector2Int(dx, dz);
            moveTimer = 0f; waitTimer = 0f;
            isComputingPath = false;
            pendingPath = null;
            isGoingToDepotViaStation = false;
            isStoppedAwaitingDepotReturn = false;
            pendingReturnToDepot = false;
            tradeDoneAtStation = false;

            activePath = null;
            locomotiveDistance = 0f;
            trainSpeed = 0f;
            pathIndexAtPathStart = 0;
            waypointToTileIdx = null;

            consist = null;
        }
    }

    // =====================================================================
    // VÝSLEDKOVÁ FRONTA
    // =====================================================================

    enum PathDestination { Station, Depot, ReturnViaStation }

    struct PathResult
    {
        public int depotKey;
        public List<Vector2Int> path;
        public bool isReturnToDepot;
        public PathDestination destination;
    }

    public enum ReturnToDepotResult { Dispatched, StoppedAwaitingSecondR, Error }

    readonly System.Collections.Concurrent.ConcurrentQueue<PathResult> _pendingPathResults
        = new System.Collections.Concurrent.ConcurrentQueue<PathResult>();

    // =====================================================================
    // DEBOUNCE
    // =====================================================================

    const float DEBOUNCE_DELAY = 0.4f;
    bool _mapChangePending = false;
    float _mapChangedDebounceTimer = 0f;

    // =====================================================================
    // SNAPSHOT TILE GRIDU
    // =====================================================================

    const int GRID_SIZE = 256;

    /// <summary>
    /// Snapshot tileGrid. Obsahuje DVA paralelné polia:
    ///   tileIDs[i]     – tileID dlaždice (0 = empty, 1 = rail, 2 = station, 3 = depot)
    ///   connections[i] – DirectionMask dlaždice (uložený ako int pre vlákno-bezpečné použitie)
    /// Indexovanie: i = z * GRID_SIZE + x
    /// </summary>
    struct TileGridSnapshot
    {
        public int[] tileIDs;
        public int[] connections;
    }

    TileGridSnapshot SnapshotTileGrid()
    {
        var snap = new TileGridSnapshot
        {
            tileIDs = new int[GRID_SIZE * GRID_SIZE],
            connections = new int[GRID_SIZE * GRID_SIZE]
        };

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                var td = IndicatrixAPI.instance.GetTileByIndex(x, z);
                int idx = z * GRID_SIZE + x;
                snap.tileIDs[idx] = td.tileID;
                snap.connections[idx] = (int)td.connections;
            }
        }
        return snap;
    }

    // =====================================================================
    // KONŠTANTY A STAV
    // =====================================================================

    Dictionary<int, TrainData> trains = new Dictionary<int, TrainData>();

    /// <summary>Čas prechodu jednej dlaždice v sekundách (1.0 = 1 s/dlaždica).</summary>
    const float MOVE_TIME = 2.0f;
    const float STATION_WAIT = 10.0f;
    const float BREAK_WAIT = 1.0f;

    /// <summary>
    /// Po koľkých sekundách čakania na cieľovej stanici vlak realizuje
    /// výmenu tovaru (TradeSystem). Zadanie: štandardné čakanie je 10 s
    /// (STATION_WAIT), transakcia prebehne po 2 s od príchodu.
    ///
    /// Trigger je naviazaný na UPLYNULÝ čas: spustí sa, keď
    /// (STATION_WAIT - waitTimer) >= TRADE_DELAY, t.j. po 2 s čakania.
    /// </summary>
    const float TRADE_DELAY = 2.0f;

    /// <summary>Rozmery kvádra lokomotívy aj vagónov.</summary>
    static readonly Vector3 CONSIST_SCALE = new Vector3(0.3f, 0.3f, 0.7f);

    void Awake()
    {
        instance = this;
        wagonCount = Mathf.Clamp(wagonCount, 1, 10);
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    int DepotKey(int x, int z) => x * 10000 + z;

    Vector3 TileCenter(Vector2Int tile)
    {
        // Stred dlaždice: (u, v) = (0.5, 0.5) v lokálnych súradniciach.
        // Y sa získa biliniárnou interpoláciou výšok 4 rohov dlaždice
        // → priemer výšok všetkých 4 rohov (na rovine konštanta, na svahu
        // sa správne škáluje).
        float y = GetTileSurfaceY(tile, 0.5f, 0.5f) + TRACK_OFFSET_Y;
        return new Vector3(tile.x + 0.5f, y, tile.y + 0.5f);
    }

    float GetTerrainY(int x, int z)
    {
        try
        {
            int width = TerrainManager.instance.terrainWidth + 1;
            int index = z * width + x;
            if (index >= 0 && index < TerrainManager.instance.coordsF.Length)
                return TerrainManager.instance.coordsF[index].y;
        }
        catch { }
        return 0f;
    }

    /// <summary>Vertikálny offset koľaje nad povrchom terénu (track height).</summary>
    const float TRACK_OFFSET_Y = 0.3f;

    /// <summary>
    /// Biliniárna interpolácia výšky terénu vnútri jednej dlaždice (tile)
    /// medzi 4 rohovými vertexmi.
    ///
    /// Lokálne súradnice (u, v) ∈ [0, 1] × [0, 1]:
    ///   (u=0, v=0) = ľavý-dolný roh dlaždice  → vertex (tile.x,   tile.y)
    ///   (u=1, v=0) = pravý-dolný roh dlaždice → vertex (tile.x+1, tile.y)
    ///   (u=0, v=1) = ľavý-horný roh dlaždice  → vertex (tile.x,   tile.y+1)
    ///   (u=1, v=1) = pravý-horný roh dlaždice → vertex (tile.x+1, tile.y+1)
    /// kde u zodpovedá X-osi (Left→Right), v zodpovedá Z-osi (Bottom→Top).
    ///
    /// Vzorec biliniárnej interpolácie:
    ///   y = (1-u)(1-v) * h00 + u*(1-v) * h10 + (1-u)*v * h01 + u*v * h11
    ///
    /// PREČO TOTO POTREBUJEME (kopce, LevelUp / LevelDown):
    ///   Aktuálny terénny model ukladá výšku v ROHOCH dlaždice. Jedna
    ///   dlaždica je naklonený quad medzi 4 rohmi s rôznymi Y. Ak by sme
    ///   pre celý waypoint v rámci dlaždice používali iba výšku jedného
    ///   rohu (napr. ľavého-dolného), všetky waypointy v dlaždici by mali
    ///   rovnaké Y → vlak ide vodorovne a na hrane medzi dlaždicami
    ///   "vyskočí" na novú výšku → ostrý 90° schod namiesto 45° svahu.
    ///
    ///   Biliniárnou interpoláciou dostáva každý waypoint správnu výšku
    ///   podľa svojej polohy (u, v) na šikmej ploche, takže pohyb cez
    ///   svah je plynulý a uhol stúpania presne zodpovedá skutočnému
    ///   prevýšeniu medzi rohmi terénu.
    ///
    /// HLADKOSŤ NA HRANÁCH MEDZI DLAŽDICAMI:
    ///   Pre dve susediace dlaždice A a B (B = A + Right) platí:
    ///     A.exitEdge(Right)  → použije priemer výšok dvoch pravých rohov A
    ///     B.entryEdge(Left)  → použije priemer výšok dvoch ľavých rohov B
    ///   Ľavé rohy B sú TOTOŽNÉ vertexy ako pravé rohy A → výška vychádza
    ///   identická. Prechod cez hranu je teda C0-spojitý automaticky.
    ///   Žiadna explicitná konštanta uhla ani manuálne dorovnávanie nie je
    ///   potrebné – uhol svahu je daný geometriou terénu.
    ///
    /// Funkcia je deterministická a thread-safe pokiaľ ide o vstupy
    /// (číta len TerrainManager.instance.coordsF, ktoré sa nemení mimo
    /// hlavného vlákna v rámci jedného frame-u).
    /// </summary>
    float GetTileSurfaceY(Vector2Int tile, float u, float v)
    {
        float h00 = GetTerrainY(tile.x, tile.y);     // ľavý-dolný roh
        float h10 = GetTerrainY(tile.x + 1, tile.y);     // pravý-dolný roh
        float h01 = GetTerrainY(tile.x, tile.y + 1); // ľavý-horný roh
        float h11 = GetTerrainY(tile.x + 1, tile.y + 1); // pravý-horný roh

        float omu = 1f - u;
        float omv = 1f - v;

        return omu * omv * h00
             + u * omv * h10
             + omu * v * h01
             + u * v * h11;
    }

    int GetTileID(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE) return -1;
        return IndicatrixAPI.instance.GetTileByIndex(x, z).tileID;
    }

    bool IsPassable(int x, int z)
    {
        int id = GetTileID(x, z);
        return id == 1 || id == 2 || id == 3;
    }

    // =====================================================================
    // DIRECTION HELPERS – mapovanie Vector2Int <-> DirectionMask
    // =====================================================================

    /// <summary>
    /// Konvertuje 4-smerový vektor (zo step v A*) na DirectionMask.
    /// (+1, 0)  → Right
    /// (-1, 0)  → Left
    /// ( 0,+1)  → Top
    /// ( 0,-1)  → Bottom
    /// </summary>
    static IndicatrixAPI.DirectionMask DirFromStep(Vector2Int step)
    {
        if (step.x == 1 && step.y == 0) return IndicatrixAPI.DirectionMask.Right;
        if (step.x == -1 && step.y == 0) return IndicatrixAPI.DirectionMask.Left;
        if (step.x == 0 && step.y == 1) return IndicatrixAPI.DirectionMask.Top;
        if (step.x == 0 && step.y == -1) return IndicatrixAPI.DirectionMask.Bottom;
        return IndicatrixAPI.DirectionMask.None;
    }

    /// <summary>
    /// Vráti smer pohybu z dlaždice 'from' na dlaždicu 'to' (musia byť susedia).
    /// </summary>
    static IndicatrixAPI.DirectionMask GetDirection(Vector2Int from, Vector2Int to)
    {
        return DirFromStep(to - from);
    }

    /// <summary>
    /// Pre danú dlaždicu (x, z) a smer 'dir' vráti svetový bod uprostred danej hrany dlaždice.
    /// Hrana je medzi vnútrom dlaždice a susedom v smere 'dir'.
    ///   Right  → x = tile.x + 1.0, z = tile.z + 0.5
    ///   Left   → x = tile.x + 0.0, z = tile.z + 0.5
    ///   Top    → x = tile.x + 0.5, z = tile.z + 1.0
    ///   Bottom → x = tile.x + 0.5, z = tile.z + 0.0
    /// Y: biliniárna interpolácia výšky šikmej plochy dlaždice v polohe
    ///    stredu danej hrany. Pre rovinu vychádza priemer 4 rohov; pre svah
    ///    (LevelUp/LevelDown) vychádza presne stred danej hrany dlaždice,
    ///    takže vlak na svahu ide pod skutočným uhlom stúpania – nie po
    ///    schodoch s 90° lomami.
    /// Hladkosť na hrane medzi susednými dlaždicami je zaručená
    /// automaticky: A.right-edge a B.left-edge zdieľajú tie isté dva
    /// rohové vertexy → ich priemer je totožný.
    /// </summary>
    Vector3 EdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f, GetTileSurfaceY(tile, 1.0f, 0.5f) + TRACK_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f, GetTileSurfaceY(tile, 0.0f, 0.5f) + TRACK_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 1.0f) + TRACK_OFFSET_Y, fz + 1.0f);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 0.0f) + TRACK_OFFSET_Y, fz + 0.0f);
            default: return TileCenter(tile);
        }
    }

    /// <summary>
    /// Detekcia, či je daná dlaždica krivkou.
    /// Krivka = presne 2 nastavené smery, ktoré NIE SÚ navzájom opačné
    /// (t.j. nie Left+Right ani Top+Bottom).
    /// </summary>
    static bool IsCurveTile(IndicatrixAPI.DirectionMask conns)
    {
        // Počet bitov
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        if (bits != 2) return false;

        bool horiz = (conns & (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right))
                       == (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right);
        bool vert = (conns & (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom))
                       == (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom);

        return !horiz && !vert;
    }

    /// <summary>
    /// Detekcia, či je daná dlaždica výhybkou (rail switch / turnout).
    /// Výhybka má presne 3 nastavené smery:
    ///   • RailSwitchHorizontalBottom: Left + Right + Bottom
    ///   • RailSwitchHorizontalTop:    Left + Right + Top
    ///   • RailSwitchVerticalBottom:   Top + Bottom + Right
    ///   • RailSwitchVerticalTop:      Top + Bottom + Left
    ///
    /// Výhybka je plne obojsmerná – ktorékoľvek dva z troch smerov sa
    /// môžu navzájom prepojiť. Pohyb cez výhybku má dva režimy:
    ///   1. Priamy prechod (proti-smery, napr. Left↔Right) – správa sa ako
    ///      klasická priama koľaj: edge → center → edge.
    ///   2. Odbočka (kolmé smery, napr. Left↔Bottom) – generuje sa diagonálny
    ///      vnútorný layout (~45° turnout), nie ostrý 90° roh ako pri krivke.
    ///
    /// Všetky 4 križovatkové smery (crossroad) majú 4 bity, takže sú
    /// odlíšené: switch == 3 bity, crossroad == 4 bity.
    /// </summary>
    static bool IsSwitchTile(IndicatrixAPI.DirectionMask conns)
    {
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        return bits == 3;
    }

    /// <summary>
    /// Detekcia, či je daná dlaždica plnou križovatkou (RailCrossroad).
    /// Križovatka má všetky 4 smery (Left + Right + Top + Bottom) –
    /// jediný typ s 4 bitmi v DirectionMask.
    ///
    /// Pohyb cez križovatku má dva režimy podľa entry/exit smerov:
    ///   1. Priamy prechod (proti-smery, 90°):
    ///         Top ↔ Bottom    (vertikálny prejazd)
    ///         Left ↔ Right    (horizontálny prejazd)
    ///      → edge → center → edge (klasická priama)
    ///   2. Rohový prechod (kolmé smery, 45°):
    ///         Top ↔ Right, Top ↔ Left
    ///         Bottom ↔ Right, Bottom ↔ Left
    ///      → edge → innerEntry → innerExit → edge (45° turnout layout,
    ///        identický s odbočkou výhybky)
    /// </summary>
    static bool IsCrossroadTile(IndicatrixAPI.DirectionMask conns)
    {
        const IndicatrixAPI.DirectionMask ALL =
            IndicatrixAPI.DirectionMask.Left
          | IndicatrixAPI.DirectionMask.Right
          | IndicatrixAPI.DirectionMask.Top
          | IndicatrixAPI.DirectionMask.Bottom;
        return (conns & ALL) == ALL;
    }

    /// <summary>
    /// Vráti true, ak sú dva smery navzájom kolmé (jeden horizontálny + jeden
    /// vertikálny). Vracia false ak sú totožné, alebo proti-smery, alebo None.
    /// Používa sa na odlíšenie odbočky vs priameho prechodu cez výhybku.
    /// </summary>
    static bool IsPerpendicular(IndicatrixAPI.DirectionMask a, IndicatrixAPI.DirectionMask b)
    {
        bool aHoriz = a == IndicatrixAPI.DirectionMask.Left || a == IndicatrixAPI.DirectionMask.Right;
        bool aVert = a == IndicatrixAPI.DirectionMask.Top || a == IndicatrixAPI.DirectionMask.Bottom;
        bool bHoriz = b == IndicatrixAPI.DirectionMask.Left || b == IndicatrixAPI.DirectionMask.Right;
        bool bVert = b == IndicatrixAPI.DirectionMask.Top || b == IndicatrixAPI.DirectionMask.Bottom;
        return (aHoriz && bVert) || (aVert && bHoriz);
    }

    /// <summary>
    /// Vnútorný bod výhybky pre 45° turnout geometriu.
    ///
    /// Pre danú dlaždicu, smer (z perspektívy hrany dlaždice, t.j. v ktorej
    /// hrane sa nachádzame) a parameter t ∈ (0, 0.5) vráti bod ležiaci na osi
    /// danej hrany, ale posunutý dovnútra dlaždice o vzdialenosť t.
    ///
    /// Príklady (tile na (0,0), t = 0.25):
    ///   dir = Left   → edge je (0.0, y, 0.5), inner = (0.25, y, 0.5)
    ///   dir = Right  → edge je (1.0, y, 0.5), inner = (0.75, y, 0.5)
    ///   dir = Top    → edge je (0.5, y, 1.0), inner = (0.5,  y, 0.75)
    ///   dir = Bottom → edge je (0.5, y, 0.0), inner = (0.5,  y, 0.25)
    ///
    /// Použitie: pre odbočkový prechod cez výhybku skladáme cestu ako
    ///   entryEdge → innerEntry(t) → innerExit(t) → exitEdge
    /// Stredný segment (innerEntry → innerExit) je pri t = 0.25 a kolmých
    /// smeroch presne 45° diagonálny – vizuálne pripomína skutočný turnout
    /// koľajnice, bez ostrého 90° lomu cez stred dlaždice.
    /// </summary>
    Vector3 SwitchInnerEdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir, float t)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f - t,
                    GetTileSurfaceY(tile, 1.0f - t, 0.5f) + TRACK_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f + t,
                    GetTileSurfaceY(tile, 0.0f + t, 0.5f) + TRACK_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 1.0f - t) + TRACK_OFFSET_Y,
                    fz + 1.0f - t);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 0.0f + t) + TRACK_OFFSET_Y,
                    fz + 0.0f + t);
            default: return TileCenter(tile);
        }
    }

    // =====================================================================
    // CAN MOVE – validácia smeru pre A* (thread-safe – pracuje so snapshotom)
    // =====================================================================

    /// <summary>
    /// Validácia A* prechodu z 'fromTile' na 'toTile' v smere 'direction'.
    /// Pravidlá:
    ///   1. toTile musí byť priechodná (rail/station/depot)
    ///   2. fromTile musí mať connection v smere 'direction'
    ///   3. toTile musí mať connection v opačnom smere (Opposite(direction))
    ///   4. Výnimka pre štart: ak je fromTile depo (tileID == 3), neaplikujeme
    ///      pravidlo č. 2 striktne (depo má jeden výstup, ten musí ladiť so smerom);
    ///      táto výnimka NIE JE potrebná – depo má vlastný DirectionMask, takže
    ///      vlak môže opustiť depo iba povoleným smerom. Ak by toto bolo príliš
    ///      reštriktívne, dá sa relaxovať.
    /// </summary>
    static bool CanMove(int[] tileIDs, int[] conns, Vector2Int fromTile, Vector2Int toTile,
                        IndicatrixAPI.DirectionMask direction)
    {
        if (toTile.x < 0 || toTile.x >= GRID_SIZE || toTile.y < 0 || toTile.y >= GRID_SIZE)
            return false;
        if (fromTile.x < 0 || fromTile.x >= GRID_SIZE || fromTile.y < 0 || fromTile.y >= GRID_SIZE)
            return false;

        int toIdx = toTile.y * GRID_SIZE + toTile.x;
        int toID = tileIDs[toIdx];
        if (!(toID == 1 || toID == 2 || toID == 3)) return false;

        int fromIdx = fromTile.y * GRID_SIZE + fromTile.x;
        IndicatrixAPI.DirectionMask fromConn = (IndicatrixAPI.DirectionMask)conns[fromIdx];
        IndicatrixAPI.DirectionMask toConn = (IndicatrixAPI.DirectionMask)conns[toIdx];

        // fromTile musí povoľovať odchod v smere direction
        if ((fromConn & direction) == 0) return false;

        // toTile musí povoľovať vstup z opačného smeru
        IndicatrixAPI.DirectionMask oppositeDir = IndicatrixAPI.Opposite(direction);
        if ((toConn & oppositeDir) == 0) return false;

        return true;
    }

    // =====================================================================
    // BUILD WAYPOINT PATH – konvertuje List<Vector2Int> na waypointy
    // =====================================================================

    /// <summary>
    /// Z tile-cesty vygeneruje waypoint cestu podľa pravidiel:
    ///
    /// Pre každú dlaždicu B medzi predchádzajúcou A a nasledujúcou C:
    ///   • Ak je B priama (rail/station/depot s opačnými smermi):
    ///       waypoint sekvencia: B.entryEdge, B.center, B.exitEdge
    ///   • Ak je B krivka (RailCurveX):
    ///       waypoint sekvencia: B.entryEdge, B.exitEdge
    ///       (center sa NEPOUŽÍVA, vnútorný roh sa NEPOUŽÍVA – PathData
    ///       ich spojí jedným lineárnym Lerp segmentom, ktorý prechádza
    ///       vnútrom dlaždice diagonálne pod ~45°.)
    ///   • Ak je B výhybka (RailSwitch*, 3 spojenia):
    ///       - PRIAMY prechod (proti-smery): B.entryEdge, B.center, B.exitEdge
    ///         (rovnako ako priama dlaždica)
    ///       - ODBOČKA (kolmé smery): B.entryEdge, B.innerEntry,
    ///         B.innerExit, B.exitEdge
    ///         (4 waypointy → 3 priame segmenty, stredný 45° diagonálny;
    ///          vizuálne 45° turnout, NIE ostrý 90° roh.)
    ///   • Ak je B križovatka (RailCrossroad, 4 spojenia):
    ///       - PRIAMY prechod (proti-smery, Top↔Bottom alebo Left↔Right):
    ///         B.entryEdge, B.center, B.exitEdge (90° prejazd cez stred)
    ///       - ROHOVÝ prechod (kolmé smery, napr. Top↔Right):
    ///         B.entryEdge, B.innerEntry, B.innerExit, B.exitEdge
    ///         (45° turnout layout, identický s odbočkou výhybky)
    ///
    /// Štart a koniec:
    ///   • Prvá dlaždica (štart): pridá sa A.center, potom A.exitEdge.
    ///   • Posledná dlaždica (cieľ): pridá sa B.entryEdge, potom B.center.
    ///
    /// Pre jednodlaždičovú cestu (count==1): vráti len [center].
    ///
    /// HRANIČNÉ WAYPOINTY (kritické):
    ///   Tile-prechod A→B vygeneruje DVA waypointy s identickou Vector3
    ///   pozíciou, ktoré sú VŽDY zachované ako dva samostatné body:
    ///     - A.exitEdge(dir)         ← patrí dlaždici A
    ///     - B.entryEdge(opposite)   ← patrí dlaždici B
    ///   Aj keď sú geometricky totožné, logicky reprezentujú prechod cez
    ///   hranu a poradie je dôležité: zachováva smerovosť a umožňuje
    ///   správne priradenie currentTile cez waypointToTileIdx. Krivka,
    ///   výhybka aj križovatka spotrebúvajú SVOJ vstupný edge waypoint pre
    ///   korektné odvodenie exit-smeru. Z toho dôvodu deduplikujeme len
    ///   waypointy z ROVNAKEJ dlaždice (pozri AddWaypoint).
    ///
    /// VÝSTUP:
    ///   waypoints – List<Vector3> waypointov v poradí pohybu
    ///   tileIdxPerWaypoint – pre každý waypoint index do tilePath, ku ktorému patrí
    ///                        (potrebné pre UpdateCurrentTile)
    /// </summary>
    void BuildWaypointPath(List<Vector2Int> tilePath, out List<Vector3> waypoints,
                           out List<int> tileIdxPerWaypoint)
    {
        // Pracujeme cez lokálne premenné, nie cez 'out' parametre. Lokálna
        // funkcia AddWaypoint nemôže zachytávať 'out'/'ref' parametre
        // (CS1628), preto vyplníme lokálne zoznamy a na konci ich priradíme
        // do 'out' parametrov.
        var wp = new List<Vector3>();
        var tIdx = new List<int>();

        if (tilePath == null || tilePath.Count == 0)
        {
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        if (tilePath.Count == 1)
        {
            wp.Add(TileCenter(tilePath[0]));
            tIdx.Add(0);
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        // Pomocný local fn na pridanie waypointu.
        //
        // PRAVIDLO (kritické pre tile-boundary semantiku):
        // Waypointy s rovnakou Vector3 pozíciou sa NESMÚ zlučovať,
        // ak patria DVOM RÔZNYM dlaždiciam. Konkrétne A.exitEdge(dir) a
        // B.entryEdge(opposite(dir)) sú v priestore identické, ale logicky
        // ide o dva po sebe idúce waypointy:
        //   - A.exitEdge patrí dlaždici A (tileIdx = A)
        //   - B.entryEdge patrí dlaždici B (tileIdx = B)
        // Toto poradie zachováva smerovosť cesty a umožňuje korektné
        // priradenie currentTile pri prekročení hranice (UpdateCurrentTile
        // používa waypointToTileIdx mapovanie). Bez toho sa hraničný
        // waypoint krivkovej dlaždice "stratí" a UpdateCurrentTile by
        // hlásil zlú dlaždicu pre frame priamo po prekročení hrany.
        //
        // Deduplikujeme IBA vtedy, keď sú obidva waypointy z TEJ ISTEJ
        // dlaždice a v priestore sa kryjú – vtedy ide o degenerovaný
        // intra-tile prípad bez dopadu na sémantiku.
        void AddWaypoint(Vector3 pt, int tileIdx)
        {
            if (wp.Count > 0)
            {
                Vector3 last = wp[wp.Count - 1];
                int lastTileIdx = tIdx[tIdx.Count - 1];
                bool sameTile = (lastTileIdx == tileIdx);
                bool samePos = (pt - last).sqrMagnitude < 1e-8f;
                if (sameTile && samePos) return; // intra-tile duplikát → preskoč
            }
            wp.Add(pt);
            tIdx.Add(tileIdx);
        }

        for (int i = 0; i < tilePath.Count; i++)
        {
            Vector2Int tile = tilePath[i];
            var conns = IndicatrixAPI.instance.GetTileByIndex(tile.x, tile.y).connections;

            bool isFirst = (i == 0);
            bool isLast = (i == tilePath.Count - 1);

            // Smer odchodu z aktuálnej dlaždice (do nasledujúcej)
            IndicatrixAPI.DirectionMask outDir = IndicatrixAPI.DirectionMask.None;
            if (!isLast) outDir = GetDirection(tile, tilePath[i + 1]);

            // Smer príchodu do aktuálnej dlaždice (z predchádzajúcej)
            IndicatrixAPI.DirectionMask inDir = IndicatrixAPI.DirectionMask.None;
            if (!isFirst) inDir = IndicatrixAPI.Opposite(GetDirection(tilePath[i - 1], tile));

            // ────────────────────────────────────────────────────────────────
            // KRIVKA: entryEdge → exitEdge  (BEZ centra, BEZ rohového bodu)
            //
            // PathData spojí dva po sebe idúce waypointy lineárnym Lerp-om.
            // Keďže entryEdge je stred jednej hrany dlaždice a exitEdge je
            // stred susediacej (kolmnej) hrany, priamka medzi nimi prechádza
            // vnútrom dlaždice diagonálne — vizuálne pod ~45°. To je presne
            // požadovaný "ostrý" angular pohyb cez krivku.
            //
            // Predtým sme sem vkladali aj rohový bod (vrchol bunky), čo
            // produkovalo dva segmenty s ostrým 90° lomom v rohu — pohyb
            // skákal cez vrchol dlaždice von z dráhy. Rohový bod sa preto
            // už NEpridáva.
            //
            // Funkcia CurveCornerPoint zostáva v zdrojáku pre prípadné
            // budúce vizuálne pomocníky, ale BuildWaypointPath ju NEvolá.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsCurveTile(conns))
            {
                // entryEdge je geometricky totožný bod ako A.exitEdge predchádzajúcej
                // dlaždice, ale logicky patrí TEJTO krivkovej dlaždici –
                // AddWaypoint ho zachová ako samostatný waypoint, pretože tileIdx
                // je iný od predchádzajúceho waypointu.
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // VÝHYBKA (RAIL SWITCH) – 3 spojenia, plne obojsmerná.
            //
            // Vnútorná logika výhybky závisí od toho, či ide o priamy prechod
            // alebo o odbočku:
            //
            //   1. PRIAMY PRECHOD (proti-smery, napr. Left↔Right na
            //      RailSwitchHorizontalBottom alebo Top↔Bottom na
            //      RailSwitchVerticalBottom):
            //         entryEdge → center → exitEdge
            //      Identické správanie ako klasická priama koľaj. Stred
            //      dlaždice je súčasťou cesty.
            //
            //   2. ODBOČKA (kolmé smery, napr. Left↔Bottom alebo Right↔Top):
            //         entryEdge → innerEntry(t) → innerExit(t) → exitEdge
            //      kde t = SWITCH_TURNOUT_T (= 0.25). Vizuálne to vytvorí
            //      pohyb, ktorý:
            //         • vstúpi do dlaždice po hrane vstupu,
            //         • prejde krátky úsek pozdĺž osi vstupnej hrany,
            //         • diagonálne (45°) sa stočí dovnútra,
            //         • prejde krátky úsek pozdĺž osi výstupnej hrany,
            //         • opustí dlaždicu cez výstupnú hranu.
            //      Pohyb používa IBA priame čiarové segmenty (žiadne
            //      bezier krivky, žiadne splajny, žiadna interpolácia okrem
            //      lineárneho Lerp medzi dvoma susednými waypointmi).
            //      Vizuálne to pripomína skutočný železničný výhybkový
            //      úsek (turnout) namiesto ostrého 90° rohu cez stred.
            //
            // Tile-prechodové pravidlá zostávajú nezmenené:
            //   A.center → A.edge → B.edge   (medzi-tile)
            // entryEdge a exitEdge sú ŠTANDARDNÉ hranové waypointy zhodné
            // s ostatnými tile-typmi → tile-boundary semantika je zachovaná.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsSwitchTile(conns))
            {
                // ── ODBOČKA: entry a exit sú kolmé smery ─────────────────
                if (IsPerpendicular(inDir, outDir))
                {
                    const float SWITCH_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                // ── PRIAMY PRECHOD: entry a exit sú proti-smery ─────────
                // (napr. Left ↔ Right na horizontálnej výhybke)
                // → správa sa ako klasická priama dlaždica: edge → center → edge
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // KRIŽOVATKA (RAIL CROSSROAD) – 4 spojenia (všetky 4 smery).
            //
            // Plná 4-cestná križovatka. Pohyb cez ňu závisí od entry/exit
            // smerov rovnako ako pri výhybke, len s jedným spojením naviac:
            //
            //   1. PRIAMY PRECHOD – 90° osový prejazd (proti-smery):
            //         • Top    ↔ Bottom   (vertikálny prejazd)
            //         • Left   ↔ Right    (horizontálny prejazd)
            //      → entryEdge → center → exitEdge
            //         (rovnako ako klasická priama dlaždica – cez stred)
            //
            //   2. ROHOVÝ PRECHOD – 45° turnout (kolmé smery):
            //         • Top    ↔ Right     • Top    ↔ Left
            //         • Bottom ↔ Right     • Bottom ↔ Left
            //      → entryEdge → innerEntry(t) → innerExit(t) → exitEdge
            //         (presne ten istý layout ako odbočka výhybky –
            //          stredný segment je 45° diagonálny, vstupný a výstupný
            //          segment idú pozdĺž osí príslušných hrán)
            //
            // Princíp je identický s odbočkou výhybky:
            //   • IBA priame čiarové segmenty (žiadne bezier krivky,
            //     žiadne splajny, žiadny smoothing).
            //   • Stred dlaždice sa NEPOUŽÍVA pre rohové prechody –
            //     pohyb sa stočí pred dosiahnutím stredu.
            //   • Tile-prechodové pravidlá zostávajú nezmenené:
            //     entryEdge a exitEdge sú štandardné hranové waypointy
            //     zhodné s ostatnými typmi.
            //
            // POZN: Crossroad detekcia musí prísť AŽ ZA switch detekciou,
            // pretože ako oddelenie používame počet bitov:
            //   curve = 2, switch = 3, crossroad = 4.
            // ────────────────────────────────────────────────────────────────
            if (!isFirst && !isLast && IsCrossroadTile(conns))
            {
                // ── ROHOVÝ PRECHOD: entry a exit sú kolmé smery ──────────
                if (IsPerpendicular(inDir, outDir))
                {
                    const float CROSSROAD_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                // ── PRIAMY PRECHOD: entry a exit sú proti-smery ─────────
                // Top↔Bottom alebo Left↔Right → klasický prejazd cez stred
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ────────────────────────────────────────────────────────────────
            // PRIAMA / KONCOVÁ DLAŽDICA: edge → center → edge
            // ────────────────────────────────────────────────────────────────

            if (isFirst)
            {
                // Štart – začneme v centre
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
            else if (isLast)
            {
                // Cieľ – vstúpime cez hranu a skončíme v centre
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
            }
            else
            {
                // Stredná priama dlaždica: entryEdge → center → exitEdge
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
        }

        // Priradíme lokálne zoznamy do 'out' parametrov.
        waypoints = wp;
        tileIdxPerWaypoint = tIdx;
    }

    /// <summary>
    /// Vráti vrchol bunky pre krivku (vrchol, kde by sa dve hrany krivky
    /// stretli pri 90° lome). NEPOUŽÍVA sa v BuildWaypointPath – pohyb cez
    /// krivku je priamy Lerp medzi entryEdge a exitEdge (45° diagonála).
    /// Funkcia ostáva pre prípadné budúce vizuálne pomôcky / debug overlay.
    ///
    /// Pre RailCurveRightBottom (Right + Bottom):
    ///   Right hrana je x = tile.x+1, Bottom hrana je z = tile.z
    ///   → vrchol bunky = (tile.x+1, y, tile.z)
    /// </summary>
    Vector3 CurveCornerPoint(Vector2Int tile, IndicatrixAPI.DirectionMask conns)
    {
        float fx = tile.x, fz = tile.y;

        bool right = (conns & IndicatrixAPI.DirectionMask.Right) != 0;
        bool left = (conns & IndicatrixAPI.DirectionMask.Left) != 0;
        bool top = (conns & IndicatrixAPI.DirectionMask.Top) != 0;
        bool bottom = (conns & IndicatrixAPI.DirectionMask.Bottom) != 0;

        // X súradnica rohu: ak Right → fx+1, ak Left → fx
        float cx = right ? fx + 1.0f : fx + 0.0f;
        // Z súradnica rohu: ak Top → fz+1, ak Bottom → fz
        float cz = top ? fz + 1.0f : fz + 0.0f;

        // Y v rohu = priamo výška vertexu terénu v tom rohu (biliniárna
        // interpolácia v rohu sa zredukuje na hodnotu samotného rohového
        // vertexu). Korektné aj na svahu.
        float u = right ? 1f : 0f;
        float v = top ? 1f : 0f;
        float y = GetTileSurfaceY(tile, u, v) + TRACK_OFFSET_Y;

        return new Vector3(cx, y, cz);
    }

    // =====================================================================
    // (Pôvodný BuildPathData zachovaný pre spätnú kompatibilitu, ale už
    //  nepoužívaný v hlavnej ceste – ApplyNewPath používa BuildWaypointPath.)
    // =====================================================================

    PathData BuildPathData(List<Vector2Int> tilePath)
    {
        if (tilePath == null || tilePath.Count == 0) return null;
        BuildWaypointPath(tilePath, out var pts, out _);
        return pts.Count > 0 ? new PathData(pts) : null;
    }

    // =====================================================================
    // VIZUÁLNA SÚPRAVA – pomocná metóda vytvorenia
    // =====================================================================

    GameObject CreateConsistPart(string name, Color color, Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.localScale = CONSIST_SCALE;
        go.transform.position = position;
        Destroy(go.GetComponent<BoxCollider>());
        var rend = go.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = color;
        go.SetActive(false);
        return go;
    }

    // =====================================================================
    // SPRÁVA VLAKOV
    // =====================================================================

    /// <summary>
    /// Vytvorí vlak v depe [dx,dz].
    ///
    /// trainTypeIndex / wagonTypeIndex sú indexy zvolené v TrainTypeDropdown /
    /// WagonTypeDropdown (viď DepotRailConstructionMenuUI). Podľa nich sa
    /// z katalógov (TrainCatalog / WagonCatalog v TrainStock.cs) zostaví
    /// dátová štruktúra súpravy (TrainInstance) a uloží do td.consist.
    ///
    /// Všetky vagóny v súprave sú zatiaľ rovnakého zvoleného typu; ich počet
    /// je daný wagonCount. UI sa nemení – mapovanie atribútov na konkrétne UI
    /// prvky nie je potrebné, detailné okno si ich prečíta z td.consist neskôr.
    /// </summary>
    public bool CreateTrain(int dx, int dz, int trainTypeIndex, int wagonTypeIndex)
    {
        int key = DepotKey(dx, dz);
        if (trains.ContainsKey(key)) return false;
        if (GetTileID(dx, dz) != 3) return false;

        int count = Mathf.Clamp(wagonCount, 1, 10);
        TrainData td = new TrainData(dx, dz);
        Vector3 depotPos = TileCenter(new Vector2Int(dx, dz));

        // -----------------------------------------------------------------
        // DÁTOVÁ ŠTRUKTÚRA SÚPRAVY (TrainStock.cs)
        // -----------------------------------------------------------------
        TrainSpec trainSpec = TrainCatalog.ByIndex(trainTypeIndex);
        WagonSpec wagonSpec = WagonCatalog.ByIndex(wagonTypeIndex);

        if (trainSpec == null)
        {
            Debug.LogWarning($"[TrainSystem] Neznámy index typu vlaku ({trainTypeIndex}) – použijem prvý z katalógu.");
            trainSpec = TrainCatalog.ByIndex(0);
        }
        if (wagonSpec == null)
        {
            Debug.LogWarning($"[TrainSystem] Neznámy index typu vagónu ({wagonTypeIndex}) – použijem prvý z katalógu.");
            wagonSpec = WagonCatalog.ByIndex(0);
        }

        td.consist = new TrainInstance(trainSpec);
        for (int i = 0; i < count; i++)
            td.consist.Wagons.Add(new WagonInstance(wagonSpec));

        // Skrytá kocka – pohybová logika
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = CONSIST_SCALE;
        cube.transform.position = depotPos;
        cube.name = $"TrainHead_{dx}_{dz}";
        Destroy(cube.GetComponent<BoxCollider>());
        cube.GetComponent<Renderer>().enabled = false;
        td.trainObject = cube;

        // Lokomotíva – CYAN
        td.locomotive = CreateConsistPart($"Loco_{dx}_{dz}", new Color(0f, 1f, 1f), depotPos);

        // Vagóny – GREY
        for (int i = 0; i < count; i++)
        {
            var wagon = CreateConsistPart($"Wagon_{dx}_{dz}_{i}",
                new Color(0.502f, 0.502f, 0.502f), depotPos);
            td.wagons.Add(wagon);
        }

        trains[key] = td;
        Debug.Log($"[TrainSystem] Vlak '{td.consist.Name}' vytvorený v depe [{dx},{dz}] – "
                + $"1 lokomotíva + {count}× '{wagonSpec.Name}' (spacing={wagonSpacing}).");
        return true;
    }

    /// <summary>
    /// Spätne kompatibilný preťažený podpis – vytvorí vlak s prvým typom
    /// vlaku aj vagónu z katalógu (index 0). Volá sa z miest, kde voľba
    /// typu nie je k dispozícii (napr. klávesová skratka Q bez UI kontextu).
    /// </summary>
    public bool CreateTrain(int dx, int dz)
    {
        return CreateTrain(dx, dz, 0, 0);
    }

    public TrainData GetTrain(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return trains.TryGetValue(key, out var td) ? td : null;
    }

    /// <summary>
    /// Vráti súradnice [depotX, depotZ] VŠETKÝCH aktuálne existujúcich
    /// vlakových dep. Keďže platí "1 train depo = 1 train", počet prvkov
    /// zoznamu = počet vlakov v hre.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI). Vnútorný slovník
    /// `trains` ostáva privátny – navonok dávame len read-only kópiu
    /// kľúčových údajov, takže volajúci nemôže poškodiť interný stav.
    /// </summary>
    public List<Vector2Int> GetAllDepotCoords()
    {
        var result = new List<Vector2Int>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            result.Add(new Vector2Int(td.depotX, td.depotZ));
        }
        return result;
    }

    /// <summary>
    /// Celkový počet vlakových dep (= počet vlakov). Pohodlný getter, aby
    /// UI nemuselo kvôli počtu vytvárať celý zoznam cez GetAllDepotCoords().
    /// </summary>
    public int GetDepotCount()
    {
        return trains.Count;
    }

    /// <summary>
    /// Vráti dátové štruktúry (TrainInstance) VŠETKÝCH aktuálne existujúcich
    /// vlakov. Keďže platí "1 train depo = 1 train", počet prvkov zoznamu =
    /// počet vlakov v hre.
    ///
    /// Slúži pre informačné UI (StatusTrainsMenuUI), ktoré z každej
    /// TrainInstance prečíta názov vlaku (Name), počet vagónov
    /// (Wagons.Count) a typ suroviny (Wagons[0].Type.Name).
    ///
    /// Vnútorný slovník `trains` ostáva privátny – navonok dávame len
    /// read-only kópiu zoznamu referencií na TrainInstance. Volajúci tak
    /// nemôže pridať/odobrať vlak (zmeniť `trains`), čítať atribúty
    /// jednotlivých vlakov ale môže. Analógia k GetAllDepotCoords().
    /// </summary>
    public List<TrainInstance> GetAllTrainConsists()
    {
        var result = new List<TrainInstance>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td != null && td.consist != null)
                result.Add(td.consist);
        }
        return result;
    }

    /// <summary>
    /// EKONOMIKA (read-only snapshot): pre KAŽDÝ existujúci vlak spáruje jeho
    /// DEPO súradnice [depotX, depotZ] (= kľúč pre <see cref="ReturnToDepot"/>)
    /// s jeho dátovou inštanciou <see cref="TrainInstance"/> (OperatingCosts,
    /// ServiceLife, ServicingInterval). Interný slovník `trains` ostáva privátny.
    ///
    /// Slúži pre EconomySystem: mesačné prevádzkové náklady + automatické
    /// poslanie vlaku do depa po uplynutí servisného intervalu alebo životnosti
    /// (EconomySystem zavolá <see cref="ReturnToDepot"/> s vrátenými súradnicami).
    /// </summary>
    public List<ActiveTrainInfo> GetActiveTrainInfos()
    {
        var result = new List<ActiveTrainInfo>(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td != null && td.consist != null)
                result.Add(new ActiveTrainInfo(td.depotX, td.depotZ, td.consist));
        }
        return result;
    }

    /// <summary>Read-only dvojica [depo súradnice + dátová inštancia vlaku] pre EconomySystem.</summary>
    public readonly struct ActiveTrainInfo
    {
        public readonly int DepotX;
        public readonly int DepotZ;
        public readonly TrainInstance Consist;

        public ActiveTrainInfo(int depotX, int depotZ, TrainInstance consist)
        {
            DepotX = depotX;
            DepotZ = depotZ;
            Consist = consist;
        }
    }

    public bool AddStation(int depotX, int depotZ, int stX, int stZ)
    {
        TrainData td = GetTrain(depotX, depotZ);
        if (td == null) return false;
        if (GetTileID(stX, stZ) != 2) return false;

        Vector2Int st = new Vector2Int(stX, stZ);
        if (!td.stations.Contains(st))
        {
            td.stations.Add(st);
            Debug.Log($"[TrainSystem] Stanica [{stX},{stZ}] pridaná pre vlak v depe [{depotX},{depotZ}]. Celkom: {td.stations.Count}");
        }
        return true;
    }

    public bool StartTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;
        if (td.stations.Count < 2)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] nemá aspoň 2 stanice!");
            return false;
        }

        bool isResume = !td.isAtDepot && td.currentPath != null && td.currentPath.Count > 0;

        td.isRunning = true;
        td.isWaiting = false;
        td.isReturningToDepot = false;

        if (isResume)
        {
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] OBNOVENÝ (resume).");
        }
        else
        {
            td.isAtDepot = false;
            td.currentStationIndex = 0;
            td.reverseDirection = false;
            td.moveTimer = 0f;
            td.waitTimer = 0f;
            td.pendingPath = null;
            td.pendingReturnToDepot = false;

            // Reset path state
            td.activePath = null;
            td.locomotiveDistance = 0f;

            ComputeNextPathAsync(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] SPUSTENÝ (prvý štart).");
        }

        return true;
    }

    public bool StopTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;
        td.isRunning = false;
        td.isComputingPath = false;
        td.pendingPath = null;
        td.pendingReturnToDepot = false;
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] ZASTAVENÝ.");
        return true;
    }

    public ReturnToDepotResult ReturnToDepot(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return ReturnToDepotResult.Error;

        if (GetTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[TrainSystem] Depo [{dx},{dz}] neexistuje – vlak zastane.");
            td.isRunning = false;
            td.isStoppedAwaitingDepotReturn = false;
            return ReturnToDepotResult.Error;
        }

        if (td.isStoppedAwaitingDepotReturn)
        {
            td.isStoppedAwaitingDepotReturn = false;
            td.pendingReturnToDepot = false;
            td.isRunning = true;
            td.isWaiting = false;
            td.waitTimer = 0f;
            td.isGoingToDepotViaStation = false;
            td.isComputingPath = false;
            td.pendingPath = null;
            DispatchToDepot(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] → depo priamo (R po zastavení bez staníc).");
            return ReturnToDepotResult.Dispatched;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);

        if (td.isWaiting)
        {
            td.pendingReturnToDepot = false;
            td.isGoingToDepotViaStation = true;
            td.isStoppedAwaitingDepotReturn = false;
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] čaká na stanici → po odpočítaní pôjde do depa.");
            return ReturnToDepotResult.Dispatched;
        }

        if (IsDepotOnCurrentPath(td, depotTile))
        {
            td.pendingReturnToDepot = false;
            td.isGoingToDepotViaStation = false;
            td.isStoppedAwaitingDepotReturn = false;
            TrimPathToDepot(td, depotTile);
            td.isReturningToDepot = true;
            // Prebuduj PathData pre skrátenú cestu
            RebuildActivePathFromCurrentPath(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] → depo PRIAMO (depo je na aktuálnej ceste).");
            return ReturnToDepotResult.Dispatched;
        }

        td.pendingReturnToDepot = true;
        td.isGoingToDepotViaStation = false;
        td.isStoppedAwaitingDepotReturn = false;
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}]: depo nie je v smere jazdy → vlak dokončí cestu na plánovanú stanicu a potom pôjde do depa.");
        return ReturnToDepotResult.Dispatched;
    }

    bool IsDepotOnCurrentPath(TrainData td, Vector2Int depotTile)
    {
        if (td.currentPath == null || td.pathIndex >= td.currentPath.Count) return false;
        for (int i = td.pathIndex; i < td.currentPath.Count; i++)
            if (td.currentPath[i] == depotTile) return true;
        return false;
    }

    void TrimPathToDepot(TrainData td, Vector2Int depotTile)
    {
        if (td.currentPath == null) return;
        for (int i = td.pathIndex; i < td.currentPath.Count; i++)
        {
            if (td.currentPath[i] == depotTile)
            {
                td.currentPath = td.currentPath.GetRange(0, i + 1);
                return;
            }
        }
    }

    void DispatchToDepot(TrainData td)
    {
        int key = DepotKey(td.depotX, td.depotZ);
        Vector2Int goal = new Vector2Int(td.depotX, td.depotZ);
        Vector2Int start = td.currentTile;
        var snap = SnapshotTileGrid();

        td.isReturningToDepot = true;
        td.isGoingToDepotViaStation = false;
        td.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = true,
                destination = PathDestination.Depot
            });
        });

        Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] → depo.");
    }

    /// <summary>
    /// Odstráni vlak z hry. Vymazanie je povolené VÝLUČNE vtedy, keď je
    /// vlak fyzicky v depe (currentTile == depot tile) a nie je v pohybe
    /// (isRunning == false). Flag isAtDepot sa NEPOUŽÍVA – v praxi sa
    /// dostal mimo synchronizáciu pri scenári, keď je vlak po stlačení S
    /// zaregistrovaný ako "vyšiel z depa" (isAtDepot = false), ale A*
    /// nenašiel cestu von, takže vlak zostal fyzicky stáť na depote.
    /// Po následnom P (StopTrain) by stary guard zablokoval T (RemoveTrain),
    /// hoci vlak depo nikdy neopustil.
    ///
    /// Nový guard kontroluje:
    ///   1. Vlak v evidencii existuje (td != null).
    ///   2. Vlak nie je v pohybe (!td.isRunning).
    ///   3. Vlak je fyzicky na tile depota (td.currentTile == (dx, dz)).
    ///   4. Tile (dx, dz) je stále depo (GetTileID == 3).
    ///
    /// Vyrieši oba scenáre používateľa:
    ///   • Q (CreateTrain) → T (RemoveTrain) bezprostredne:
    ///     vlak ešte nikdy nevyšiel, currentTile = (dx,dz), isRunning = false
    ///     → povolené.
    ///   • S (StartTrain) → A* nenájde cestu → P (StopTrain) → T:
    ///     vlak depo neopustil, currentTile = (dx,dz), isRunning = false
    ///     → povolené.
    /// V iných stavoch (vlak na trati, hoci aj zastavený) vymazanie
    /// zlyhá, čo je v súlade s požiadavkou.
    /// </summary>
    public bool RemoveTrain(int dx, int dz)
    {
        TrainData td = GetTrain(dx, dz);
        if (td == null) return false;

        if (td.isRunning)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] sa nedá odstrániť – stále beží. Najprv ho zastavte (P).");
            return false;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);
        if (td.currentTile != depotTile)
        {
            Debug.LogWarning($"[TrainSystem] Vlak v depe [{dx},{dz}] sa nedá odstrániť – nie je fyzicky v depe (aktuálne na [{td.currentTile.x},{td.currentTile.y}]).");
            return false;
        }

        if (GetTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[TrainSystem] Tile [{dx},{dz}] už nie je depo – vlak sa nedá odstrániť.");
            return false;
        }

        // Defenzívne: ak by ešte stále prebiehal asynchrónny A* výpočet,
        // jeho výsledok bude zahodený (TryGetValue v ApplyPendingPathResults
        // neuspeje, lebo sme záznam už odstránili z trains).
        td.isComputingPath = false;
        td.pendingPath = null;

        // Uvoľníme VŠETKY štruktúry, ktoré vytvoril CreateTrain – symetricky.
        DestroyTrainStructures(td);

        trains.Remove(DepotKey(dx, dz));
        Debug.Log($"[TrainSystem] Vlak z depa [{dx},{dz}] ODSTRÁNENÝ.");
        return true;
    }

    /// <summary>
    /// Uvoľní všetky štruktúry vlakovej súpravy, ktoré boli vytvorené
    /// v CreateTrain. Táto metóda je presným ZRKADLOM CreateTrain –
    /// pre každú štruktúru vytvorenú pri vytvorení vlaku tu existuje
    /// zodpovedajúce zrušenie:
    ///
    ///   CreateTrain vytvorí          →  DestroyTrainStructures uvoľní
    ///   ─────────────────────────────────────────────────────────────
    ///   td.trainObject (GameObject)   →  Destroy(td.trainObject)
    ///   td.locomotive  (GameObject)   →  Destroy(td.locomotive)
    ///   td.wagons      (GameObjecty)  →  Destroy(každý) + zoznam vyčistený
    ///   td.consist     (TrainInstance →  Wagons vyčistené + referencia null
    ///                  + WagonInstance)
    ///
    /// Týmto je zaručené, že ak sa pri vytvorení vlaku alokuje nejaká
    /// štruktúra, tá istá štruktúra sa pri odstránení vlaku aj korektne
    /// uvoľní – žiadne "visiace" GameObjecty ani mŕtve referencie
    /// v td.wagons po Destroy().
    /// </summary>
    void DestroyTrainStructures(TrainData td)
    {
        if (td == null) return;

        // Skrytá kocka – pohybová logika (zrkadlí: cube → td.trainObject).
        if (td.trainObject != null) Destroy(td.trainObject);
        td.trainObject = null;

        // Vizuálna lokomotíva (zrkadlí: CreateConsistPart → td.locomotive).
        if (td.locomotive != null) Destroy(td.locomotive);
        td.locomotive = null;

        // Vizuálne vagóny (zrkadlí: CreateConsistPart slučka → td.wagons).
        if (td.wagons != null)
        {
            foreach (var w in td.wagons)
                if (w != null) Destroy(w);
            // Vyčistíme zoznam, aby v td.wagons neostali mŕtve referencie
            // na už zničené GameObjecty.
            td.wagons.Clear();
        }

        // Dátová štruktúra súpravy (zrkadlí: new TrainInstance + WagonInstance
        // slučka → td.consist). Uvoľníme vagóny a samotnú referenciu.
        if (td.consist != null)
        {
            td.consist.Wagons?.Clear();
            td.consist = null;
        }

        // Vyčistíme aj pohybový / pathfinding stav, aby objekt TrainData
        // neostal v nekonzistentnom stave, ak naň ešte drží referenciu
        // prebiehajúci asynchrónny výpočet.
        td.activePath = null;
        td.currentPath = null;
        td.pendingPath = null;
    }

    // =====================================================================
    // NOTIFIKÁCIA PRI ZMENE MAPY – DEBOUNCE
    // =====================================================================

    public void OnMapChanged()
    {
        _mapChangePending = true;
        _mapChangedDebounceTimer = DEBOUNCE_DELAY;
    }

    // =====================================================================
    // UPDATE
    // =====================================================================

    void Update()
    {
        if (_mapChangePending)
        {
            _mapChangedDebounceTimer -= Time.deltaTime;
            if (_mapChangedDebounceTimer <= 0f)
            {
                _mapChangePending = false;
                _mapChangedDebounceTimer = 0f;
                TriggerMapChangedReroute();
            }
        }

        ApplyPendingPathResults();

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (td.trainObject == null) continue;
            if (!td.isRunning) continue;
            UpdateTrain(td);
        }
    }

    // =====================================================================
    // POHYB VLAKU – DISTANCE-BASED
    // =====================================================================

    void UpdateTrain(TrainData td)
    {
        // ── Čakanie (stanica / pauza) ─────────────────────────────────────
        if (td.isWaiting)
        {
            td.waitTimer -= Time.deltaTime;

            // ── OBCHOD: po 2 s čakania na cieľovej stanici ────────────────
            // Spustí sa práve raz za zastávku. tradeDoneAtStation je nastavené
            // na false iba pri príchode na cieľovú stanicu (OnPathComplete),
            // takže pri BREAK_WAIT ani pri ceste do depa sa obchod nespustí.
            if (!td.tradeDoneAtStation
                && (STATION_WAIT - td.waitTimer) >= TRADE_DELAY)
            {
                td.tradeDoneAtStation = true;
                TryExecuteTradeAtStation(td);
            }

            if (td.waitTimer <= 0f)
            {
                td.isWaiting = false;
                td.waitTimer = 0f;

                if (td.isReturningToDepot)
                    DispatchToDepot(td);
                else if (td.isGoingToDepotViaStation)
                    DispatchToDepot(td);
                else
                    AdvanceStation(td);
            }
            return;
        }

        // ── Čakáme na výpočet novej trasy ────────────────────────────────
        if (td.activePath == null)
        {
            if (td.isComputingPath) return;
            // Ak niet aktívnej trasy a nie sme počítajú, skúsime spustiť prepočet
            OnPathComplete(td);
            return;
        }

        // ── Pohyb lokomotívy pozdĺž trasy ────────────────────────────────
        td.locomotiveDistance += td.trainSpeed * Time.deltaTime;

        // Aktualizujeme currentTile podľa najbližšieho bodu na trase
        UpdateCurrentTile(td);

        // Pohyb vizuálnej hlavy (skrytá kocka)
        Vector3 headPos = td.activePath.GetPositionAtDistance(td.locomotiveDistance);
        td.trainObject.transform.position = headPos;

        // ── Aktualizácia vizuálnej súpravy ───────────────────────────────
        UpdateConsistVisuals(td);

        // ── Detekcia konca trasy ──────────────────────────────────────────
        if (td.locomotiveDistance >= td.activePath.totalLength)
        {
            // Lokomotíva dorazila na koniec – zafixujeme na posledný bod
            td.locomotiveDistance = td.activePath.totalLength;
            td.currentTile = td.currentPath != null && td.currentPath.Count > 0
                ? td.currentPath[td.currentPath.Count - 1]
                : td.currentTile;

            // Aplicujeme pendingPath ak existuje
            if (td.pendingPath != null)
            {
                List<Vector2Int> pending = td.pendingPath;
                td.pendingPath = null;
                ApplyNewPath(td, pending);
                return;
            }

            td.activePath = null;
            OnPathComplete(td);
        }
    }

    /// <summary>
    /// Udržiava currentTile synchronizované s aktuálnou pozíciou lokomotívy na trase.
    /// Použije waypointToTileIdx mapovanie pre presné určenie aktuálnej dlaždice
    /// pri waypoint-based pohybe.
    /// </summary>
    void UpdateCurrentTile(TrainData td)
    {
        if (td.currentPath == null || td.activePath == null) return;
        if (td.waypointToTileIdx == null || td.waypointToTileIdx.Length == 0) return;

        // Nájdeme index waypointu, kde sa lokomotíva práve nachádza
        float[] cumLens = td.activePath.cumulativeLengths;
        int lastPassedIdx = 0;
        for (int i = 0; i < cumLens.Length; i++)
        {
            if (cumLens[i] <= td.locomotiveDistance)
                lastPassedIdx = i;
            else
                break;
        }

        // Mapuj waypoint-index na sub-path tile-index, potom na currentPath-index
        int subPathTileIdx = td.waypointToTileIdx[lastPassedIdx];
        int tileIdx = td.pathIndexAtPathStart + subPathTileIdx;
        if (tileIdx >= 0 && tileIdx < td.currentPath.Count)
            td.currentTile = td.currentPath[tileIdx];
    }

    // =====================================================================
    // VIZUÁLNA SÚPRAVA – DISTANCE-BASED
    // =====================================================================

    /// <summary>
    /// Aktualizuje pozíciu, rotáciu a viditeľnosť každého člena súpravy.
    ///
    /// Člen i:
    ///   offsetDist = i * wagonSpacing
    ///   memberDist = locomotiveDistance - offsetDist  (min 0)
    ///   pos = activePath.GetPositionAtDistance(memberDist)
    ///   rot = Quaternion.LookRotation(activePath.GetDirectionAtDistance(memberDist))
    ///
    /// Zobrazenie: člen i sa zobrazí keď locomotiveDistance >= i * wagonSpacing.
    /// Skrývanie pri návrate do depa: keď memberDist >= totalLength - DEPOT_HIDE_MARGIN.
    /// </summary>
    void UpdateConsistVisuals(TrainData td)
    {
        if (td.activePath == null) return;

        const float DEPOT_HIDE_RADIUS = 0.55f;
        bool isReturning = td.isReturningToDepot;
        Vector3 depotCenter = TileCenter(new Vector2Int(td.depotX, td.depotZ));
        int totalMembers = 1 + td.wagons.Count;

        for (int i = 0; i < totalMembers; i++)
        {
            float offsetDist = i * wagonSpacing;
            GameObject member = (i == 0) ? td.locomotive : td.wagons[i - 1];
            if (member == null) continue;

            // ── Kritérium zobrazenia ───────────────────────────────────────
            bool shouldBeVisible = td.locomotiveDistance >= offsetDist;

            float memberDist = Mathf.Max(0f, td.locomotiveDistance - offsetDist);

            // ── Skrývanie pri návrate do depa ─────────────────────────────
            if (isReturning && shouldBeVisible)
            {
                Vector3 memberPos = td.activePath.GetPositionAtDistance(memberDist);
                if (Vector3.Distance(memberPos, depotCenter) < DEPOT_HIDE_RADIUS)
                    shouldBeVisible = false;
            }

            if (member.activeSelf != shouldBeVisible)
                member.SetActive(shouldBeVisible);

            if (!shouldBeVisible) continue;

            // ── Pozícia ───────────────────────────────────────────────────
            Vector3 pos = td.activePath.GetPositionAtDistance(memberDist);

            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
            {
                Debug.LogWarning($"[TrainSystem] NaN pozícia pre člen {i} – preskočené.");
                continue;
            }

            // ── Rotácia – priamo zo smeru segmentu, bez akumulácie ────────
            Vector3 dir = td.activePath.GetDirectionAtDistance(memberDist);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            member.transform.position = pos;
            member.transform.rotation = rot;
        }
    }

    // =====================================================================
    // DOKONČENIE CESTY
    // =====================================================================

    void OnPathComplete(TrainData td)
    {
        if (td.isReturningToDepot)
        {
            td.isRunning = false;
            td.isAtDepot = true;
            td.isReturningToDepot = false;
            td.isGoingToDepotViaStation = false;
            td.currentTile = new Vector2Int(td.depotX, td.depotZ);
            td.trainObject.transform.position = TileCenter(td.currentTile);
            td.activePath = null;
            td.locomotiveDistance = 0f;

            HideAllConsist(td);
            Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] dorazil do DEPA – súprava skrytá.");
            return;
        }

        if (td.isGoingToDepotViaStation)
        {
            bool onStation = td.stations.Contains(td.currentTile)
                             && GetTileID(td.currentTile.x, td.currentTile.y) == 2;
            if (onStation)
            {
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{td.currentTile.x},{td.currentTile.y}] pred depom – čaká {STATION_WAIT}s.");
                td.trainObject.transform.position = TileCenter(td.currentTile);
                td.isWaiting = true;
                td.waitTimer = STATION_WAIT;
                // Zastávka pred návratom do depa – obchod sa nerealizuje.
                td.tradeDoneAtStation = true;
            }
            else
            {
                Debug.LogWarning($"[TrainSystem] Vlak nedosiahol stanicu pred depom. Zastane.");
                td.isRunning = false;
                td.isGoingToDepotViaStation = false;
            }
            return;
        }

        Vector2Int targetStation = td.stations[td.currentStationIndex];
        if (td.currentTile == targetStation)
        {
            if (td.pendingReturnToDepot)
            {
                td.pendingReturnToDepot = false;
                td.isGoingToDepotViaStation = true;
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{targetStation.x},{targetStation.y}] (pending návrat do depa) – čaká {STATION_WAIT}s, potom depo.");
                // Posledná zastávka pred depom – obchod sa nerealizuje.
                td.tradeDoneAtStation = true;
            }
            else
            {
                Debug.Log($"[TrainSystem] Vlak dorazil na stanicu [{targetStation.x},{targetStation.y}] – čaká {STATION_WAIT}s.");
                // Riadna zastávka na cieľovej stanici – po 2 s prebehne obchod.
                td.tradeDoneAtStation = false;
            }
            td.trainObject.transform.position = TileCenter(td.currentTile);
            td.isWaiting = true;
            td.waitTimer = STATION_WAIT;
        }
        else
        {
            Debug.LogWarning($"[TrainSystem] Vlak nedosiahol stanicu [{targetStation.x},{targetStation.y}]. Čaká {BREAK_WAIT}s.");
            td.isWaiting = true;
            td.waitTimer = BREAK_WAIT;
            // Núdzové prerušenie – nie je to zastávka na stanici, žiadny obchod.
            td.tradeDoneAtStation = true;
        }
    }

    // =====================================================================
    // OBCHOD NA STANICI – TRADE (TradeSystem)
    // =====================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi súpravou vlaku a továrňami stanice, na
    /// ktorej vlak práve čaká. Volá sa z UpdateTrain po 2 s čakania
    /// (TRADE_DELAY), práve raz za zastávku.
    ///
    /// Postup:
    ///   1) Zisti tile, na ktorom vlak stojí (cieľová stanica).
    ///   2) Nájdi StationInstance pre tento tile v StationRegistry.
    ///      Ak ešte nie je zaregistrovaná (napr. stanica pribudla bez
    ///      následného scanu), dorovná sa cez RescanAll.
    ///   3) Odovzdaj súpravu + stanicu do TradeSystem.Execute.
    ///   4) Podľa výsledku vypíš Debug.Log (úspech / neúspech).
    ///
    /// Výpisy presne zodpovedajú zadaniu:
    ///   úspech  → "Predaj prebehol úspešne, cena {N} euro"
    ///   neúspech→ "Obchod neprebehol."
    /// </summary>
    void TryExecuteTradeAtStation(TrainData td)
    {
        // Bezpečnostné kontroly – bez dátovej súpravy nemá obchod zmysel.
        if (td == null || td.consist == null)
        {
            Debug.Log("Obchod neprebehol.");
            return;
        }

        Vector2Int stationTile = td.currentTile;

        // Nájdi stanicu v registri. Ak chýba, skús dorovnať register.
        StationInstance station = RailStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        if (station == null)
        {
            RailStationRegistry.RescanAll();
            station = RailStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        }

        if (station == null)
        {
            Debug.Log("Obchod neprebehol.");
            Debug.LogWarning($"[TrainSystem] Pre tile [{stationTile.x},{stationTile.y}] " +
                             $"sa nenašla StationInstance – obchod sa neuskutočnil.");
            return;
        }

        // Vykonaj samotnú transakciu (čisto dátová operácia).
        TrainTradeSystem.TradeResult result = TrainTradeSystem.Execute(td.consist, station);

        if (result.Success)
        {
            // Pripíš zárobok na herné konto – preprava tovaru je jediný zdroj
            // príjmu, ktorý drží konto v pluse (a umožní reset cenového
            // násobiteľa po prepadnutí do mínusu).
            if (result.Revenue > 0)
            {
                if (GameEconomy.instance != null)
                    GameEconomy.instance.AddCredits((uint)result.Revenue);

                // EVIDENCIA pre ročnú uzávierku – tržba z úspešnej prepravy
                // (jediný zdroj príjmu hry, rovnako pre vlaky aj vozidlá).
                BudgetSystem.instance?.RecordRevenue(result.Revenue);
            }

            Debug.Log($"Obchodná transakcia bola vykonaná úspešne, suma {result.Revenue} CR.");
            Debug.Log($"[TrainSystem] Obchod na stanici [{stationTile.x},{stationTile.y}]: " +
                      $"naložené {result.LoadedUnits}, vyložené {result.UnloadedUnits}, " +
                      $"zárobok {result.Revenue} CR.");

            // Plávajúci cenový label nad vlakom na 3 s. Volá sa presne tu –
            // teda po 2 s od príchodu na stanicu (keď prebehne transakcia).
            if (td.locomotive != null)
                SalePriceLabelManager.Instance.ShowPrice(
                    td.locomotive.transform.position, result.Revenue);
        }
        else
        {
            Debug.Log("Obchod neprebehol.");
            Debug.Log($"[TrainSystem] Obchod na stanici [{stationTile.x},{stationTile.y}] " +
                      $"neprebehol – dôvod: {result.Reason}.");
        }
    }

    void HideAllConsist(TrainData td)
    {
        if (td.locomotive != null) td.locomotive.SetActive(false);
        foreach (var w in td.wagons)
            if (w != null) w.SetActive(false);
    }

    // =====================================================================
    // ADVANCE STATION – OBRAT SMERU
    // =====================================================================

    void AdvanceStation(TrainData td)
    {
        if (!td.reverseDirection)
        {
            td.currentStationIndex++;
            if (td.currentStationIndex >= td.stations.Count)
            {
                td.reverseDirection = true;
                td.currentStationIndex = td.stations.Count - 2;
                if (td.currentStationIndex < 0) td.currentStationIndex = 0;
            }
        }
        else
        {
            td.currentStationIndex--;
            if (td.currentStationIndex < 0)
            {
                td.reverseDirection = false;
                td.currentStationIndex = 1;
                if (td.currentStationIndex >= td.stations.Count)
                    td.currentStationIndex = 0;
            }
        }

        ComputeNextPathAsync(td);
    }

    // =====================================================================
    // ASYNC A* WRAPPER
    // =====================================================================

    void ComputeNextPathAsync(TrainData td)
    {
        if (td.stations.Count == 0) return;
        if (td.isComputingPath) return;

        Vector2Int goal = td.stations[td.currentStationIndex];
        Vector2Int start = td.currentTile;
        int key = DepotKey(td.depotX, td.depotZ);
        var snap = SnapshotTileGrid();

        td.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = false
            });
        });
    }

    // =====================================================================
    // TRIGGER MAP CHANGED REROUTE
    // =====================================================================

    void TriggerMapChangedReroute()
    {
        // Mapa sa zmenila (pribudli/ubudli koľaje, stanice alebo továrne) –
        // prepočítaj 9×9 zóny staníc a ich evidenciu tovární. Lacná operácia.
        RailStationRegistry.RescanAll();

        var snap = SnapshotTileGrid();

        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            if (!td.isRunning) continue;
            if (td.isComputingPath) continue;

            int key = kvp.Key;

            if (td.isReturningToDepot)
            {
                Vector2Int start = td.currentTile;
                Vector2Int goal = new Vector2Int(td.depotX, td.depotZ);
                td.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = true,
                        destination = PathDestination.Depot
                    });
                });
            }
            else if (td.isGoingToDepotViaStation)
            {
                Vector2Int? nearest = FindNearestStation(td);
                if (!nearest.HasValue) continue;
                Vector2Int start = td.currentTile;
                Vector2Int goal = nearest.Value;
                td.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.ReturnViaStation
                    });
                });
            }
            else
            {
                if (td.stations.Count == 0) continue;
                Vector2Int start = td.currentTile;
                Vector2Int goal = td.stations[td.currentStationIndex];
                td.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.Station
                    });
                });
            }
        }
    }

    // =====================================================================
    // POMOCNÁ – Nearest Station
    // =====================================================================

    Vector2Int? FindNearestStation(TrainData td)
    {
        Vector2Int? nearest = null;
        int bestDist = int.MaxValue;
        foreach (var st in td.stations)
        {
            if (GetTileID(st.x, st.y) != 2) continue;
            int dist = Math.Abs(td.currentTile.x - st.x) + Math.Abs(td.currentTile.y - st.y);
            if (dist < bestDist) { bestDist = dist; nearest = st; }
        }
        return nearest;
    }

    // =====================================================================
    // APLIKÁCIA VÝSLEDKOV A*
    // =====================================================================

    void ApplyPendingPathResults()
    {
        while (_pendingPathResults.TryDequeue(out PathResult result))
        {
            if (!trains.TryGetValue(result.depotKey, out TrainData td)) continue;

            td.isComputingPath = false;
            bool hasPath = result.path != null && result.path.Count > 0;

            if (result.destination == PathDestination.Depot)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    td.isRunning = false; td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] nemôže nájsť cestu do depa, čaká.");
                }
                continue;
            }

            if (result.destination == PathDestination.ReturnViaStation)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    Debug.LogWarning($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}] nemôže nájsť cestu na stanicu. Zastane.");
                    td.isRunning = false; td.isWaiting = false; td.isGoingToDepotViaStation = false;
                }
                continue;
            }

            if (result.isReturnToDepot)
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    td.isRunning = false; td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[TrainSystem] Vlak nemôže nájsť cestu do depa, čaká.");
                }
            }
            else
            {
                if (hasPath) { ApplyNewPath(td, result.path); td.isWaiting = false; }
                else
                {
                    bool anyStationExists = false;
                    foreach (var st in td.stations)
                        if (GetTileID(st.x, st.y) == 2) { anyStationExists = true; break; }

                    if (!anyStationExists)
                    {
                        Debug.Log($"[TrainSystem] Vlak z depa [{td.depotX},{td.depotZ}]: žiadne dostupné stanice – vlak zastane. Stlačte R pre návrat do depa.");
                        td.isRunning = false; td.isWaiting = false;
                        td.isStoppedAwaitingDepotReturn = true;
                        td.currentPath = new List<Vector2Int>(); td.pathIndex = 0;
                        td.activePath = null;

                        // Synchronizácia flag-u isAtDepot s reálnou polohou.
                        // StartTrain (ne-resume) ho už nastavil na false, ale
                        // vlak fyzicky depo neopustil → vraciame flag späť na
                        // true, aby zodpovedal skutočnosti.
                        if (td.currentTile.x == td.depotX && td.currentTile.y == td.depotZ)
                            td.isAtDepot = true;
                    }
                    else
                    {
                        if (td.stations.Count > 0)
                        {
                            var goal = td.stations[td.currentStationIndex];
                            Debug.LogWarning($"[TrainSystem] A* nenašiel cestu do [{goal.x},{goal.y}]. Čakám {BREAK_WAIT}s.");
                        }
                        td.isWaiting = true; td.waitTimer = BREAK_WAIT;
                        td.currentPath = new List<Vector2Int>(); td.pathIndex = 0;
                        td.activePath = null;

                        // Synchronizácia flag-u isAtDepot s reálnou polohou.
                        // Vlak po neúspešnom A* zostáva fyzicky na depe –
                        // flag musí zodpovedať tomuto stavu, aby StopTrain (P)
                        // a následný RemoveTrain (T) fungovali korektne.
                        if (td.currentTile.x == td.depotX && td.currentTile.y == td.depotZ)
                            td.isAtDepot = true;
                    }
                }
            }
        }
    }

    // =====================================================================
    // APPLY NEW PATH – konvertuje List<Vector2Int> na PathData (waypoint-based)
    // =====================================================================

    void ApplyNewPath(TrainData td, List<Vector2Int> newPath)
    {
        bool isMoving = td.activePath != null && td.locomotiveDistance < td.activePath.totalLength;

        // Ak vlak práve prechádza medzi dvoma dlaždicami, aplikujeme nový
        // path od dlaždice, na ktorej sa aktuálne nachádza
        Vector2Int startTile = td.currentTile;
        int startIdxInNew = -1;
        for (int i = 0; i < newPath.Count; i++)
        {
            if (newPath[i] == startTile) { startIdxInNew = i; break; }
        }

        if (startIdxInNew < 0)
        {
            // Vlak nie je na novej ceste – odložíme na neskôr alebo aplikujeme od začiatku
            if (isMoving)
            {
                td.pendingPath = newPath;
                return;
            }
            startIdxInNew = 0;
        }

        // Subpath od aktuálneho tielu po koniec
        List<Vector2Int> subPath = newPath.GetRange(startIdxInNew, newPath.Count - startIdxInNew);

        td.currentPath = newPath;
        td.pathIndex = startIdxInNew;
        td.pathIndexAtPathStart = startIdxInNew;
        td.moveTimer = 0f;

        // Zostav PathData – WAYPOINT-BASED (centrá + hrany + krivkové rohy)
        BuildWaypointPath(subPath, out var pts, out var tileIdxList);
        var newPathData = new PathData(pts);

        // Rýchlosť: 1 dlaždica / MOVE_TIME s.
        // Pri waypoint-based ceste obsahuje newPathData viac segmentov ako len
        // počet dlaždíc – upravíme rýchlosť tak, aby vlak prešiel celú cestu
        // za rovnaký čas ako pri pôvodnom center-only systéme.
        float expectedTime = (subPath.Count - 1) * MOVE_TIME;
        td.trainSpeed = (newPathData.totalLength > 0f && expectedTime > 0f)
            ? newPathData.totalLength / expectedTime
            : 1f / MOVE_TIME;

        td.locomotiveDistance = 0f;
        td.activePath = newPathData;
        td.waypointToTileIdx = tileIdxList.ToArray();
    }

    /// <summary>
    /// Prebuduje activePath z currentPath (po TrimPathToDepot).
    /// </summary>
    void RebuildActivePathFromCurrentPath(TrainData td)
    {
        if (td.currentPath == null || td.currentPath.Count == 0)
        {
            td.activePath = null;
            return;
        }

        int startIdx = td.pathIndex;
        if (startIdx >= td.currentPath.Count) { td.activePath = null; return; }

        List<Vector2Int> sub = td.currentPath.GetRange(startIdx, td.currentPath.Count - startIdx);

        BuildWaypointPath(sub, out var pts, out var tileIdxList);
        var pd = new PathData(pts);

        td.pathIndexAtPathStart = startIdx;
        float expectedTime = (sub.Count - 1) * MOVE_TIME;
        td.trainSpeed = (pd.totalLength > 0f && expectedTime > 0f)
            ? pd.totalLength / expectedTime
            : 1f / MOVE_TIME;
        td.locomotiveDistance = 0f;
        td.activePath = pd;
        td.waypointToTileIdx = tileIdxList.ToArray();
    }

    // =====================================================================
    // A* PATHFINDING – THREAD-SAFE (modifikované – CanMove namiesto IsPassable)
    // =====================================================================

    class AStarNode
    {
        public Vector2Int pos;
        public AStarNode parent;
        public float g, h;
        public float f => g + h;

        public AStarNode(Vector2Int p, AStarNode par, float g, float h)
        {
            pos = p; parent = par; this.g = g; this.h = h;
        }
    }

    static readonly Vector2Int[] DIRECTIONS =
    {
        new Vector2Int( 1, 0),  // Right
        new Vector2Int(-1, 0),  // Left
        new Vector2Int( 0, 1),  // Top
        new Vector2Int( 0,-1)   // Bottom
    };

    static List<Vector2Int> AStarPathThreaded(TileGridSnapshot snap, Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return new List<Vector2Int>();

        var open = new List<AStarNode>();
        var closed = new HashSet<Vector2Int>();
        var nodeMap = new Dictionary<Vector2Int, AStarNode>();

        open.Add(new AStarNode(start, null, 0f, Heuristic(start, goal)));
        nodeMap[start] = open[0];

        int iterations = 0;
        const int MAX_ITER = 10000;

        while (open.Count > 0 && iterations < MAX_ITER)
        {
            iterations++;
            int bestIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bestIdx].f) bestIdx = i;

            AStarNode current = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (current.pos == goal)
                return ReconstructPath(current);

            closed.Add(current.pos);

            foreach (var dir in DIRECTIONS)
            {
                Vector2Int neighbor = current.pos + dir;
                if (closed.Contains(neighbor)) continue;

                IndicatrixAPI.DirectionMask dirMask = DirFromStep(dir);
                if (!CanMove(snap.tileIDs, snap.connections, current.pos, neighbor, dirMask))
                    continue;

                float newG = current.g + 1f;
                if (nodeMap.TryGetValue(neighbor, out AStarNode existing))
                {
                    if (newG < existing.g) { existing.g = newG; existing.parent = current; }
                }
                else
                {
                    var node = new AStarNode(neighbor, current, newG, Heuristic(neighbor, goal));
                    open.Add(node);
                    nodeMap[neighbor] = node;
                }
            }
        }

        return null;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
        => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    static List<Vector2Int> ReconstructPath(AStarNode node)
    {
        var path = new List<Vector2Int>();
        var cur = node;
        while (cur.parent != null) { path.Add(cur.pos); cur = cur.parent; }
        path.Reverse();
        return path;
    }

    public void WriteSave(System.IO.BinaryWriter bw)
    {
        bw.Write(trains.Count);
        foreach (var kvp in trains)
        {
            TrainData td = kvp.Value;
            var consist = td.consist;

            bw.Write(td.depotX);
            bw.Write(td.depotZ);

            // Typy (indexy v katalógu – mapovanie cez Name, ktoré je unikátne).
            bw.Write(TrainTypeIndexOf(consist));
            bw.Write(WagonTypeIndexOf(consist));

            // Počet vagónov + vek.
            int wc = consist != null ? consist.Wagons.Count : 0;
            bw.Write(wc);
            bw.Write(consist != null ? consist.Age : 0);

            // Náklad jednotlivých vagónov.
            for (int i = 0; i < wc; i++)
                bw.Write(consist.Wagons[i].CurrentCapacity);

            // Priradené stanice.
            bw.Write(td.stations != null ? td.stations.Count : 0);
            if (td.stations != null)
                foreach (var s in td.stations) { bw.Write(s.x); bw.Write(s.y); }

            // Beh/stop.
            bw.Write(td.isRunning);
        }
    }

    public void ReadSave(System.IO.BinaryReader br)
    {
        int count = br.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            int dx = br.ReadInt32();
            int dz = br.ReadInt32();
            int trainTypeIndex = br.ReadInt32();
            int wagonTypeIndex = br.ReadInt32();
            int wagonCnt = br.ReadInt32();
            int age = br.ReadInt32();

            int[] cargo = new int[wagonCnt];
            for (int i = 0; i < wagonCnt; i++) cargo[i] = br.ReadInt32();

            int stationCnt = br.ReadInt32();
            var stations = new System.Collections.Generic.List<Vector2Int>(stationCnt);
            for (int i = 0; i < stationCnt; i++)
                stations.Add(new Vector2Int(br.ReadInt32(), br.ReadInt32()));

            bool wasRunning = br.ReadBoolean();

            // CreateTrain použije pole `wagonCount` ako počet vagónov –
            // nastavíme ho na uloženú hodnotu (CreateTrain si ho ešte oreže 1..10).
            wagonCount = Mathf.Clamp(wagonCnt, 1, 10);
            if (!CreateTrain(dx, dz, trainTypeIndex, wagonTypeIndex))
                continue;

            TrainData td = GetTrain(dx, dz);
            if (td == null) continue;

            if (td.consist != null)
            {
                td.consist.Age = age;
                int n = Mathf.Min(cargo.Length, td.consist.Wagons.Count);
                for (int i = 0; i < n; i++)
                    td.consist.Wagons[i].CurrentCapacity = cargo[i];
            }

            foreach (var s in stations)
                AddStation(dx, dz, s.x, s.y);

            if (wasRunning)
                StartTrain(dx, dz);
        }
    }

    /// <summary>Index typu vlaku v TrainCatalog (podľa unikátneho Name); 0 ak sa nenájde.</summary>
    private int TrainTypeIndexOf(Game.TrainStock.TrainInstance consist)
    {
        if (consist == null) return 0;
        var all = Game.TrainStock.TrainCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Name == consist.Spec.Name) return i;
        return 0;
    }

    /// <summary>Index typu vagónu v WagonCatalog (podľa Name 1. vagónu); 0 ak sa nenájde.</summary>
    private int WagonTypeIndexOf(Game.TrainStock.TrainInstance consist)
    {
        if (consist == null || consist.Wagons.Count == 0) return 0;
        string name = consist.Wagons[0].Spec.Name;
        var all = Game.TrainStock.WagonCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Name == name) return i;
        return 0;
    }

}
```


### TrainTradeSystem.cs

```csharp
using System.Collections.Generic;
using UnityEngine;
using Game.TrainStock;

/// <summary>
/// TrainTradeSystem.cs
/// ------------------------------------------------------------------
/// TRANSAKČNÝ ALGORITMUS – výmena tovaru medzi vlakom a továrňami stanice.
///
/// Tento súbor implementuje to, čo zadanie nazýva "ALGORITMUS TRANSAKCIE
/// tovaru cez stanicu". Volá sa z TrainSystem-u v momente, keď vlak čaká
/// na stanici (po 2 s z 10 s čakania – časovanie rieši TrainSystem).
///
/// ------------------------------------------------------------------
/// AKO TO FUNGUJE – KONCEPT
///
/// Vlak má vagóny VŽDY len jedného typu (napr. "Coal Truck", #1). Typ
/// vagónu nesie ResourceType, s ktorým vie pracovať:
///     WagonType.Id == (int)ResourceType
///     ("Coal Truck" #1 → ResourceType.Coal,  "Wood Truck" #2 → Wood)
/// Vďaka tomu netreba do TrainStock.cs pridávať nové pole – mapovanie
/// je 1:1 cez číselné ID (pozri WagonResource() nižšie).
///
/// Stanica eviduje 0..N tovární vo svojej 9×9 zóne (StationInstance.Factories).
/// Každá továreň má pre danú surovinu buď VÝDAJOVÝ slot (UnLoad – surovinu
/// produkuje, napr. Coal Mine) alebo PRÍJMOVÝ slot (Load – surovinu
/// spotrebúva, napr. Power Station).
///
/// Keď vlak zastane na stanici, pre svoj typ suroviny urobí:
///
///   KROK 1 – VYLOŽENIE (Unload):
///     Ak je v zóne stanice továreň, ktorá danú surovinu PRIJÍMA (Load slot),
///     a vo vagónoch niečo je → vyprázdni vagóny do tej továrne (Load slot
///     amount sa zvýši, vagóny idú na 0).
///
///   KROK 2 – NALOŽENIE (Load):
///     Ak je v zóne stanice továreň, ktorá danú surovinu VYDÁVA (UnLoad slot),
///     a vo vagónoch je voľné miesto → naloží z tej továrne do vagónov
///     (UnLoad slot amount sa zníži, vagóny sa naplnia "alikvotne":
///     vagón po vagóne do max kapacity – pozri FillWagons()).
///
/// Obchod sa pokladá za ÚSPEŠNÝ ("predaj prebehol") IBA vtedy, keď sa pri
/// zastávke SKUTOČNE VYLOŽILA aspoň 1 jednotka tovaru do príjmovej továrne.
/// Samotné naloženie surovín do vagónov NEROBÍ obchod úspešným a negeneruje
/// žiadny výnos – je to len presun, príprava na predaj na ďalšej stanici.
///
/// BONUS zo zadania (vlak príde viackrát do výdajovej stanice a vo vagónoch
/// už niečo má): rieši sa automaticky. KROK 1 sa vykoná len ak je v zóne
/// PRÍJMOVÁ továreň – pri čisto výdajovej stanici sa teda nevyloží a KROK 2
/// len pripočíta ďalšiu surovinu k tomu, čo už vo vagónoch je (FillWagons
/// dopĺňa do voľného miesta, existujúci náklad nemaže).
///
/// ------------------------------------------------------------------
/// CENA / VÝNOS
///
/// Po úspešnom obchode (vyloží sa aspoň 1 jednotka do príjmovej továrne) sa
/// vyčísli zárobok cez centrálny cenník <see cref="ResourcePricing"/>:
/// cena je definovaná ZA PLNÝ VAGÓN podľa suroviny (napr. Coal 50, Oil 70,
/// Furniture 75) a ak sa predal len zlomok plného vagónu, počíta sa ALIKVOTNE.
/// Cena sa navyše násobí dynamickým násobiteľom konta (pri zápornom konte
/// rastie – pozri ResourcePricing / EconomySystem). Výslednú sumu pripíše na
/// konto volajúci (TrainSystem) a vypíše log so sumou v CR.
/// ------------------------------------------------------------------
/// </summary>
public static class TrainTradeSystem
{
    // Cena predaja sa už NEráta paušálne za jednotku. Kompletný cenník (cena
    // ZA PLNÝ VAGÓN podľa suroviny + alikvotná časť pri neúplnom vagóne +
    // dynamický násobiteľ pri zápornom konte) je centralizovaný v
    // ResourcePricing.RailSaleRevenue(...). Pozri krok 4 v Execute(...).

    // ==================================================================
    // VÝSLEDOK TRANSAKCIE
    // ==================================================================

    /// <summary>
    /// Výsledok jednej zastávky vlaku na stanici. TrainSystem si z neho
    /// prečíta Success + Revenue a vypíše príslušný Debug.Log.
    /// </summary>
    public struct TradeResult
    {
        /// <summary>True, ak sa presunula aspoň 1 jednotka tovaru.</summary>
        public bool Success;

        /// <summary>Koľko jednotiek sa naložilo z továrne do vagónov.</summary>
        public int LoadedUnits;

        /// <summary>Koľko jednotiek sa vyložilo z vagónov do továrne.</summary>
        public int UnloadedUnits;

        /// <summary>Celkový výnos z obchodu (euro).</summary>
        public int Revenue;

        /// <summary>Stručný textový dôvod (najmä pri neúspechu) – na debug.</summary>
        public string Reason;

        /// <summary>Celkový počet presunutých jednotiek (load + unload).</summary>
        public int MovedUnits => LoadedUnits + UnloadedUnits;
    }

    // ==================================================================
    // HLAVNÝ VSTUP – vykonaj transakciu pre vlak na stanici
    // ==================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi súpravou <paramref name="consist"/> a
    /// továrňami v zóne stanice <paramref name="station"/>.
    ///
    /// Postup:
    ///   0) Validácia (consist má vagóny, stanica eviduje aspoň 1 továreň).
    ///   1) Zisti surovinu, s ktorou vlak pracuje (z typu vagónov).
    ///   2) UNLOAD – ak je v zóne príjmová továreň, vyprázdni vagóny do nej.
    ///   3) LOAD   – ak je v zóne výdajová továreň, naplň vagóny z nej.
    ///   4) Vyhodnoť úspech a vyčísli cenu.
    ///
    /// Metóda je čisto dátová (žiadne Unity GameObject API), takže sa dá
    /// volať z hlavného vlákna v Update slučke TrainSystem-u bez rizika.
    /// </summary>
    public static TradeResult Execute(TrainInstance consist, StationInstance station)
    {
        var result = new TradeResult
        {
            Success = false,
            LoadedUnits = 0,
            UnloadedUnits = 0,
            Revenue = 0,
            Reason = ""
        };

        // ---- 0) VALIDÁCIA -------------------------------------------------
        if (consist == null)
        {
            result.Reason = "vlak nemá dátovú súpravu (consist == null)";
            return result;
        }
        if (consist.Wagons == null || consist.Wagons.Count == 0)
        {
            result.Reason = "vlak nemá žiadne vagóny";
            return result;
        }
        if (station == null)
        {
            result.Reason = "neznáma stanica";
            return result;
        }
        if (!station.HasFactories)
        {
            result.Reason = "stanica neeviduje žiadnu továreň v 9×9 zóne";
            return result;
        }

        // ---- 1) SUROVINA VLAKU -------------------------------------------
        // Všetky vagóny sú rovnakého typu – stačí pozrieť prvý.
        ResourceType wagonResource = WagonResource(consist.Wagons[0]);
        if (wagonResource == ResourceType.None)
        {
            result.Reason = $"typ vagónu '{consist.Wagons[0].Type}' nemá priradenú surovinu";
            return result;
        }

        // ---- 2) UNLOAD (vyloženie do príjmovej továrne) -------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // PRIJÍMA. Pri čisto výdajovej stanici sa tento krok preskočí –
        // tým je ošetrený "bonus": vlak môže prejsť cez výdajovú stanicu
        // a náklad mu zostane vo vagónoch.
        FactoryInstance consumer = station.FindConsumer(wagonResource);
        if (consumer != null)
        {
            ResourceSlot loadSlot = consumer.GetLoadSlot(wagonResource);
            result.UnloadedUnits = UnloadWagons(consist, loadSlot);
            if (result.UnloadedUnits > 0)
                Debug.Log($"[TradeSystem] Vyložené {result.UnloadedUnits}× {wagonResource} " +
                          $"do '{consumer.Name}' (slot {loadSlot}).");
        }

        // ---- 3) LOAD (naloženie z výdajovej továrne) ----------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // VYDÁVA. FillWagons dopĺňa do VOĽNÉHO miesta, takže existujúci
        // náklad vo vagónoch (z inej stanice) zostáva a nová surovina sa
        // k nemu pripočíta.
        FactoryInstance supplier = station.FindSupplier(wagonResource);
        if (supplier != null)
        {
            ResourceSlot unloadSlot = supplier.GetUnLoadSlot(wagonResource);
            result.LoadedUnits = FillWagons(consist, unloadSlot);
            if (result.LoadedUnits > 0)
                Debug.Log($"[TradeSystem] Naložené {result.LoadedUnits}× {wagonResource} " +
                          $"z '{supplier.Name}' (slot {unloadSlot}).");
        }

        // ---- 4) VYHODNOTENIE ---------------------------------------------
        // DÔLEŽITÉ: peniaze plynú IBA z VYLOŽENIA (predaja tovaru továrni).
        // Naloženie surovín z výdajovej továrne do vagónov je len presun –
        // NEGENERUJE žiadny výnos a samo o sebe nerobí obchod úspešným.
        //
        // Obchod sa teda pokladá za úspešný ("Predaj prebehol úspešne")
        // práve vtedy, keď sa do nejakej príjmovej továrne SKUTOČNE vyložila
        // aspoň 1 jednotka tovaru. Cena = počet VYLOŽENÝCH jednotiek.
        result.Success = result.UnloadedUnits > 0;

        if (result.Success)
        {
            // Cena ZA PLNÝ VAGÓN podľa suroviny, alikvotne podľa reálne
            // predaného (vyloženého) množstva, prenásobená aktuálnym cenovým
            // násobiteľom konta. Všetky vagóny sú rovnakého typu → rovnaká
            // MaximumCapacity, ktorá je menovateľom alikvotného prepočtu.
            int wagonCapacity = FirstWagonCapacity(consist);
            result.Revenue = ResourcePricing.RailSaleRevenue(
                wagonResource, result.UnloadedUnits, wagonCapacity);
        }
        else
        {
            // Žiadny PREDAJ (vyloženie) – upresni dôvod pre debug.
            // Pozn.: aj keď sa niečo naložilo (LoadedUnits > 0), obchod sa
            // bez vyloženia neráta ako úspešný – naloženie je len príprava
            // na predaj na nasledujúcej stanici.
            if (consumer == null)
            {
                if (result.LoadedUnits > 0)
                    result.Reason = $"naložené {result.LoadedUnits}× {wagonResource}, " +
                                    $"ale stanica nemá príjmovú továreň – predaj sa nekoná";
                else if (supplier == null)
                    result.Reason = $"v zóne stanice nie je továreň pre surovinu {wagonResource}";
                else
                    result.Reason = "stanica nemá príjmovú továreň – nie je kam predať";
            }
            else
            {
                result.Reason = "vagóny prázdne alebo príjmová továreň plná – nič sa nevyložilo";
            }
        }

        return result;
    }

    // ==================================================================
    // POMOCNÉ – kapacita jedného vagónu (menovateľ alikvotnej ceny)
    // ==================================================================

    /// <summary>
    /// Maximálna kapacita JEDNÉHO vagónu súpravy. Všetky vagóny vlaku sú
    /// rovnakého typu (rovnaká MaximumCapacity), takže stačí prvý platný.
    /// Slúži ako menovateľ pri výpočte ceny "za plný vagón" (alikvotne).
    /// Vráti 0, ak súprava nemá platný vagón (vtedy je zárobok 0 CR).
    /// </summary>
    private static int FirstWagonCapacity(TrainInstance consist)
    {
        if (consist == null || consist.Wagons == null) return 0;
        for (int i = 0; i < consist.Wagons.Count; i++)
        {
            var w = consist.Wagons[i];
            if (w != null && w.MaximumCapacity > 0) return w.MaximumCapacity;
        }
        return 0;
    }

    // ==================================================================
    // POMOCNÉ – mapovanie typu vagónu na surovinu
    // ==================================================================

    /// <summary>
    /// Vráti surovinu, s ktorou vie daný vagón pracovať.
    ///
    /// Mapovanie je cez ČÍSELNÉ ID: WagonType.Id == (int)ResourceType.
    /// Katalógy sú zámerne zladené:
    ///     "Coal Truck" má Id 1  →  ResourceType.Coal == 1
    ///     "Wood Truck" má Id 2  →  ResourceType.Wood == 2
    /// Vďaka tomu sa do TrainStock.cs nemusí pridávať nové pole.
    ///
    /// Ak by v budúcnosti ID nesedeli, stačí toto mapovanie nahradiť
    /// explicitnou tabuľkou (Dictionary&lt;int, ResourceType&gt;).
    /// </summary>
    public static ResourceType WagonResource(WagonInstance wagon)
    {
        if (wagon == null) return ResourceType.None;

        int id = wagon.Type.Id;
        if (System.Enum.IsDefined(typeof(ResourceType), id))
            return (ResourceType)id;

        return ResourceType.None;
    }

    // ==================================================================
    // POMOCNÉ – LOAD: naplnenie vagónov z výdajového slotu továrne
    // ==================================================================

    /// <summary>
    /// "Alikvotné" naplnenie vagónov surovinou zo skladu továrne
    /// (<paramref name="source"/> = UnLoad slot).
    ///
    /// ALGORITMUS (presne podľa príkladu zo zadania):
    ///   Ide vagón po vagóne. Každý vagón sa naplní do svojej maximálnej
    ///   kapacity, pokým je v továrni surovina k dispozícii. Posledný
    ///   čiastočne naplnený vagón dostane zvyšok.
    ///
    ///   Príklad zo zadania: 8 vagónov × max 30 = kapacita 240, v továrni
    ///   je 173 → naplní sa 5 plných vagónov (5×30 = 150) + 6. vagón
    ///   dostane zvyšok 23. Vagóny 7 a 8 zostanú prázdne. Z továrne sa
    ///   odpočíta presne 173 (resp. 150+23 = 173).
    ///
    /// Rešpektuje aj BONUS: ak vo vagóne už nejaká surovina je (z inej
    /// stanice), dopĺňa sa len jeho VOĽNÉ miesto – existujúce množstvo
    /// sa nemaže.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE naložilo (a teda aj odpočítalo
    /// z továrne).
    /// </summary>
    public static int FillWagons(TrainInstance consist, ResourceSlot source)
    {
        if (consist == null || source == null) return 0;
        if (source.IsEmpty) return 0;

        int totalLoaded = 0;

        foreach (var wagon in consist.Wagons)
        {
            if (wagon == null) continue;

            // Voľné miesto v tomto vagóne.
            int free = wagon.MaximumCapacity - wagon.CurrentCapacity;
            if (free <= 0) continue;            // vagón je plný – ďalší

            // Koľko ešte zostáva v továrni.
            int available = source.amount - totalLoaded;
            if (available <= 0) break;          // továreň vyčerpaná – koniec

            // Do vagónu ide min(voľné miesto, dostupné v továrni).
            int toLoad = Mathf.Min(free, available);
            wagon.CurrentCapacity += toLoad;
            totalLoaded += toLoad;
        }

        // Odpočítaj naloženú surovinu zo skladu továrne.
        // ResourceSlot.Remove je orezané dostupným množstvom (bezpečné).
        if (totalLoaded > 0)
            source.Remove(totalLoaded);

        return totalLoaded;
    }

    // ==================================================================
    // POMOCNÉ – UNLOAD: vyprázdnenie vagónov do príjmového slotu továrne
    // ==================================================================

    /// <summary>
    /// Vyprázdnenie vagónov do skladu továrne (<paramref name="target"/>
    /// = Load slot).
    ///
    /// ALGORITMUS:
    ///   Ide vagón po vagóne. Z každého vagónu sa presunie jeho náklad do
    ///   príjmového slotu továrne. Slot má kapacitu – ak by sa nezmestilo
    ///   všetko, presunie sa len toľko, koľko je v slote voľné, a zvyšok
    ///   ZOSTANE vo vagóne (vlak ho odvezie ďalej).
    ///
    ///   V základnom scenári zadania ("všetky vagóny vyprázdni") má
    ///   príjmová továreň dosť kapacity (Power Station Load 0/300), takže
    ///   sa vagóny vyprázdnia úplne. Orezanie kapacitou je poistka pre
    ///   prípad plnej továrne.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE vyložilo (a pripočítalo k továrni).
    /// </summary>
    public static int UnloadWagons(TrainInstance consist, ResourceSlot target)
    {
        if (consist == null || target == null) return 0;

        int totalUnloaded = 0;

        foreach (var wagon in consist.Wagons)
        {
            if (wagon == null) continue;
            if (wagon.CurrentCapacity <= 0) continue;   // prázdny vagón

            // Voľné miesto v príjmovom slote továrne (po doterajšom vykladaní).
            int free = target.capacity - (target.amount + totalUnloaded);
            if (free <= 0) break;                       // továreň plná – koniec

            // Z vagónu presunieme min(jeho náklad, voľné miesto v továrni).
            int toUnload = Mathf.Min(wagon.CurrentCapacity, free);
            wagon.CurrentCapacity -= toUnload;
            totalUnloaded += toUnload;
        }

        // Pripočítaj vyloženú surovinu do skladu továrne.
        // ResourceSlot.Add je orezané kapacitou (bezpečné).
        if (totalUnloaded > 0)
            target.Add(totalUnloaded);

        return totalUnloaded;
    }
}
```


### VehicleStock.cs

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// VehicleStock.cs
/// ─────────────────────────────────────────────────────────────────────────
/// VOZIDLOVÝ PARK – čisté dátové štruktúry (žiadne MonoBehaviour, žiadne
/// Unity API).
///
/// Tento súbor je zámerne oddelený od VehicleSystem.cs, presne tak ako je
/// TrainStock.cs oddelený od TrainSystem.cs:
///   • VehicleSystem.cs = pohybová logika, A* pathfinding, vizuál (kváder).
///   • VehicleStock.cs  = "čo je vozidlo" – iba dáta a katalógy.
///
/// ─────────────────────────────────────────────────────────────────────────
/// VLAK vs. VOZIDLO – intuitívne porovnanie
///
///   Vlak  = lokomotíva (TrainSpec/TrainInstance) + zoznam vagónov
///           (WagonSpec/WagonInstance). Náklad prevážajú vagóny.
///
///   Vozidlo = JEDEN kváder. Nemá vagóny. Ale samotný kváder má vlastnosť
///             "akoby jedného vagónu" – môže prevážať náklad v nejakom
///             množstve. Preto VehicleSpec spája do jednej karty
///             vlakové parametre (Cost, Speed, Power...) aj vagónové
///             parametre (MaximumCapacity, typ nákladu...).
///
/// ─────────────────────────────────────────────────────────────────────────
/// ROZDELENIE: "Spec" (predloha) vs. "Instance" (inštancia v hre)
///
///   *Spec     – nemenná predloha typu (katalógová karta). Spoločná pre
///               všetky vozidlá toho istého typu. Napr. VehicleSpec
///               "Coal Truck" má Cost, Speed, Power... – tieto hodnoty sú
///               rovnaké pre každý vyrobený kus.
///
///   *Instance – konkrétny kus v hre, vytvorený v depe. Drží referenciu na
///               svoj Spec + premenlivý stav daného kusu (Age,
///               CurrentCapacity – tie sa líšia kus od kusu a menia sa
///               počas hry).
///
/// Vďaka tomu sa nemenné parametre (Cost, Speed...) NEDUPLIKUJÚ do každého
/// vozidla – sú raz v katalógu. UI okno s detailmi vozidla si ich len
/// prečíta cez Instance.Spec.
/// ─────────────────────────────────────────────────────────────────────────
///
/// POZNÁMKA K TYPOM (revízia):
///   Polia Weight a Power (VehicleSpec) sú modelované ako int (číselné
///   hodnoty), nie ako string s jednotkou. Pôvodne to boli stringy
///   ("2t", "300hp"), ale nikde mimo tohto súboru sa nečítali a zadané dáta
///   vozidiel sú číselné. Číselný typ je vhodnejší (dá sa s ním počítať).
///   YearOfManufacture zostáva string ("1984") – rok sa nikde nepočíta a v
///   zadaní je uvedený ako textová hodnota.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>

namespace Game.VehicleStock
{
    // =========================================================================
    // TYP VOZIDLA
    //
    // Zadanie hovorí o "hash table (string, int)" = [typ vozidla, číselné
    // označenie vozidla]. To je ale len JEDNA dvojica hodnôt pre jeden typ
    // vozidla – nie kolekcia. Použiť Dictionary/Hashtable na uloženie jednej
    // dvojice je zbytočné (alokácia, žiadna typová bezpečnosť, neprehľadné).
    //
    // Preto je typ vozidla modelovaný ako malý nemenný struct VehicleType
    // (Name + Id) – presne ako WagonType v TrainStock.cs. "Hash table"
    // charakter – teda vyhľadanie typu podľa názvu alebo podľa čísla –
    // zabezpečuje statický register VehicleCatalog, ktorý skutočné slovníky
    // (ByName, ById) drží centrálne na jednom mieste.
    //
    // Ak by si predsa len chcel priamo slovník na konkrétnom vozidle, dá sa
    // získať jednoriadkovo: VehicleType.AsPair() vráti KeyValuePair<string,int>.
    //
    // DÔLEŽITÉ – ZHODA ID S ResourceType (FactorySystem.cs):
    //   Číselné Id typu vozidla je ZÁMERNE rovnaké ako (int)ResourceType:
    //     "Coal Truck"        #1  → ResourceType.Coal                = 1
    //     "Wood Truck"        #2  → ResourceType.Wood                = 2
    //     "Iron Ore Truck"    #3  → ResourceType.IronOre             = 3
    //     ...
    //     "Electronics Truck" #16 → ResourceType.ElectronicsProducts = 16
    //   Vďaka tomu VehicleTradeSystem.VehicleResource() namapuje vozidlo na
    //   surovinu bez extra poľa (Id == (int)ResourceType). Pri pridávaní
    //   nového vozidla teda Id MUSÍ zodpovedať príslušnej hodnote v enum
    //   ResourceType.
    // =========================================================================

    /// <summary>
    /// Typ vozidla – nemenná dvojica [textové označenie, číselné označenie].
    /// Napr. ("Coal Truck", 1), ("Wood Truck", 2).
    /// </summary>
    [Serializable]
    public readonly struct VehicleType : IEquatable<VehicleType>
    {
        /// <summary>Textové označenie typu vozidla, napr. "Coal Truck".</summary>
        public readonly string Name;

        /// <summary>Číselné označenie typu vozidla (== (int)ResourceType), napr. 1.</summary>
        public readonly int Id;

        public VehicleType(string name, int id)
        {
            Name = name;
            Id = id;
        }

        /// <summary>
        /// Vráti typ vozidla ako dvojicu kľúč–hodnota (string, int).
        /// Pohodlné, ak niektorá časť kódu očakáva "hash table" reprezentáciu.
        /// </summary>
        public KeyValuePair<string, int> AsPair() => new KeyValuePair<string, int>(Name, Id);

        public bool Equals(VehicleType other) => Id == other.Id && Name == other.Name;
        public override bool Equals(object obj) => obj is VehicleType v && Equals(v);
        public override int GetHashCode() => Id;
        public override string ToString() => $"{Name} (#{Id})";
    }

    // =========================================================================
    // PREDLOHA VOZIDLA – VehicleSpec
    //
    // Nemenná katalógová karta jedného typu vozidla. Obsahuje VŠETKY
    // nemenné atribúty zo zadania:
    //   Name, Type, MaximumCapacity, Cost, OperatingCosts, Speed, Weight,
    //   Power, ServiceLife, ServicingInterval, YearOfManufacture.
    //
    // Premenlivé atribúty (CurrentCapacity, Age) NEPATRIA sem – tie sú na
    // VehicleInstance, lebo sa líšia kus od kusu a menia sa počas hry.
    // =========================================================================

    /// <summary>
    /// Predloha (katalógová karta) jedného typu vozidla.
    /// Nemenné parametre spoločné pre všetky kusy daného typu.
    /// </summary>
    [Serializable]
    public sealed class VehicleSpec
    {
        /// <summary>Názov vozidla, napr. "Coal Truck".</summary>
        public string Name;

        /// <summary>Typ vozidla – [textové označenie, číselné označenie].</summary>
        public VehicleType Type;

        /// <summary>Maximálna kapacita vozidla.</summary>
        public int MaximumCapacity;

        /// <summary>Cena vozidla.</summary>
        public int Cost;

        /// <summary>Prevádzkové náklady.</summary>
        public int OperatingCosts;

        /// <summary>Rýchlosť vozidla.</summary>
        public int Speed;

        /// <summary>Hmotnosť vozidla (číselne, napr. 2).</summary>
        public int Weight;

        /// <summary>Výkon vozidla (číselne, napr. 100).</summary>
        public int Power;

        /// <summary>Životnosť vozidla.</summary>
        public int ServiceLife;

        /// <summary>Servisný interval.</summary>
        public int ServicingInterval;

        /// <summary>Rok výroby, napr. "1984".</summary>
        public string YearOfManufacture;

        public VehicleSpec(string name, VehicleType type, int maximumCapacity,
                           int cost, int operatingCosts, int speed, int weight,
                           int power, int serviceLife, int servicingInterval,
                           string yearOfManufacture)
        {
            Name = name;
            Type = type;
            MaximumCapacity = maximumCapacity;
            Cost = cost;
            OperatingCosts = operatingCosts;
            Speed = speed;
            Weight = weight;
            Power = power;
            ServiceLife = serviceLife;
            ServicingInterval = servicingInterval;
            YearOfManufacture = yearOfManufacture;
        }
    }

    // =========================================================================
    // INŠTANCIA VOZIDLA – VehicleInstance
    //
    // Konkrétny kus vozidla existujúci v hre, vytvorený v depe. Drží
    // referenciu na svoj VehicleSpec (nemenné parametre) + premenlivý stav
    // daného kusu:
    //   • CurrentCapacity – aktuálne naložený náklad (pri vytvorení 0).
    //   • Age             – aktuálny vek vozidla (pri vytvorení 0).
    //
    // Pozn.: VehicleInstance je čisto dátový popis. Pohybový/pathfinding
    // stav (activePath, vehicleDistance, isRunning, stanice...) zostáva v
    // VehicleSystem.VehicleData. VehicleData drží referenciu na
    // VehicleInstance – pozri integráciu vo VehicleSystem.cs.
    // =========================================================================

    /// <summary>
    /// Konkrétne vozidlo existujúce v hre. Drží referenciu na svoj
    /// VehicleSpec (nemenné parametre) + premenlivý stav (Age, CurrentCapacity).
    /// </summary>
    [Serializable]
    public sealed class VehicleInstance
    {
        /// <summary>Predloha typu tohto vozidla (nemenné parametre).</summary>
        public readonly VehicleSpec Spec;

        /// <summary>
        /// Aktuálna kapacita (naložený náklad). Pri vytvorení 0.
        /// Mení sa počas hry pri nakladaní/vykladaní.
        /// </summary>
        public int CurrentCapacity;

        /// <summary>
        /// Aktuálny vek vozidla. Pri vytvorení 0.
        /// Mení sa počas hry (pribúdaním herného času).
        /// </summary>
        public int Age;

        public VehicleInstance(VehicleSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            CurrentCapacity = 0;
            Age = 0;
        }

        // ----- Pohodlné skratky na nemenné parametre (čítané z UI okna) -----
        public string Name => Spec.Name;
        public VehicleType Type => Spec.Type;
        public int MaximumCapacity => Spec.MaximumCapacity;
        public int Cost => Spec.Cost;
        public int OperatingCosts => Spec.OperatingCosts;
        public int Speed => Spec.Speed;
        public int Weight => Spec.Weight;
        public int Power => Spec.Power;
        public int ServiceLife => Spec.ServiceLife;
        public int ServicingInterval => Spec.ServicingInterval;
        public string YearOfManufacture => Spec.YearOfManufacture;
    }

    // =========================================================================
    // KATALÓG VOZIDIEL – VehicleCatalog
    //
    // Centrálne miesto, kde sú definované všetky typy vozidiel. Slúži ako
    // zdroj pre VehicleTypeDropdown (názvy) aj pre VehicleSystem (vytvorenie
    // inštancie). Drží aj "hash table" lookupy (ByName, ById) – to je
    // miesto, kde má slovník skutočný zmysel (kolekcia viacerých typov).
    //
    // Id každého vozidla sa zhoduje s (int)ResourceType (FactorySystem.cs),
    // takže keď vozidlo dorazí do stanice, VehicleTradeSystem.VehicleResource()
    // automaticky rozpozná surovinu pre ktorýkoľvek z týchto 16 typov.
    // =========================================================================

    public static class VehicleCatalog
    {
        /// <summary>
        /// Všetky dostupné typy vozidiel, v poradí zhodnom s VehicleTypeDropdown.
        /// 16 typov – Id zodpovedá ResourceType (1 = Coal ... 16 = Electronics).
        ///
        /// Pozn.: Name aj Type.Name sú teraz zhodné (napr. "Coal Truck"),
        /// takže dropdown zobrazuje priamo názvy nákladných vozidiel a
        /// VehicleCatalog.ByName(...) ich vie rozlíšiť (názvy sú unikátne).
        /// </summary>
        public static readonly IReadOnlyList<VehicleSpec> All = new List<VehicleSpec>
        {
            // Vozidlo 1 – upravené
            new VehicleSpec(
                name:              "Coal Truck",
                type:              new VehicleType("Coal Truck", 1),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1984"
            ),
            // Vozidlo 2 – upravené
            new VehicleSpec(
                name:              "Wood Truck",
                type:              new VehicleType("Wood Truck", 2),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1984"
            ),
            // Vozidlo 3
            new VehicleSpec(
                name:              "Iron Ore Truck",
                type:              new VehicleType("Iron Ore Truck", 3),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1997"
            ),
            // Vozidlo 4
            new VehicleSpec(
                name:              "Gold Truck",
                type:              new VehicleType("Gold Truck", 4),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1995"
            ),
            // Vozidlo 5
            new VehicleSpec(
                name:              "Silver Truck",
                type:              new VehicleType("Silver Truck", 5),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1992"
            ),
            // Vozidlo 6
            new VehicleSpec(
                name:              "Livestock Truck",
                type:              new VehicleType("Livestock Truck", 6),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1989"
            ),
            // Vozidlo 7
            new VehicleSpec(
                name:              "Grain Truck",
                type:              new VehicleType("Grain Truck", 7),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1988"
            ),
            // Vozidlo 8
            new VehicleSpec(
                name:              "Oil Truck",
                type:              new VehicleType("Oil Truck", 8),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1977"
            ),
            // Vozidlo 9
            new VehicleSpec(
                name:              "Boards Truck",
                type:              new VehicleType("Boards Truck", 9),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1977"
            ),
            // Vozidlo 10
            new VehicleSpec(
                name:              "Plastic Truck",
                type:              new VehicleType("Plastic Truck", 10),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1973"
            ),
            // Vozidlo 11
            new VehicleSpec(
                name:              "Meat Truck",
                type:              new VehicleType("Meat Truck", 11),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1969"
            ),
            // Vozidlo 12
            new VehicleSpec(
                name:              "Flour Truck",
                type:              new VehicleType("Flour Truck", 12),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1967"
            ),
            // Vozidlo 13
            new VehicleSpec(
                name:              "Metals Truck",
                type:              new VehicleType("Metals Truck", 13),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1981"
            ),
            // Vozidlo 14
            new VehicleSpec(
                name:              "Glass Truck",
                type:              new VehicleType("Glass Truck", 14),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1981"
            ),
            // Vozidlo 15
            new VehicleSpec(
                name:              "Furniture Truck",
                type:              new VehicleType("Furniture Truck", 15),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1982"
            ),
            // Vozidlo 16
            new VehicleSpec(
                name:              "Electronics Truck",
                type:              new VehicleType("Electronics Truck", 16),
                maximumCapacity:   5,
                cost:              500,
                operatingCosts:    75,
                speed:             60,
                weight:            2,
                power:             100,
                serviceLife:       10,
                servicingInterval: 500,
                yearOfManufacture: "1975"
            ),
        };

        // "Hash table" lookupy – tu má slovník zmysel, lebo ide o kolekciu typov.
        // _byName a _byTypeName sú dva pohľady:
        //   _byName     – kľúč = názov vozidla    ("Coal Truck", "Wood Truck", ...)
        //   _byTypeName – kľúč = textové označenie typu ("Coal Truck", "Wood Truck", ...)
        // (Po revízii sú Name a Type.Name zhodné, takže oba slovníky majú
        //  rovnaké kľúče; ponechané sú obidva kvôli spätnej kompatibilite API.)
        private static readonly Dictionary<string, VehicleSpec> _byName = BuildByName();
        private static readonly Dictionary<string, VehicleSpec> _byTypeName = BuildByTypeName();
        private static readonly Dictionary<int, VehicleSpec> _byTypeId = BuildByTypeId();

        private static Dictionary<string, VehicleSpec> BuildByName()
        {
            var d = new Dictionary<string, VehicleSpec>();
            foreach (var v in All) d[v.Name] = v;
            return d;
        }

        private static Dictionary<string, VehicleSpec> BuildByTypeName()
        {
            var d = new Dictionary<string, VehicleSpec>();
            foreach (var v in All) d[v.Type.Name] = v;
            return d;
        }

        private static Dictionary<int, VehicleSpec> BuildByTypeId()
        {
            var d = new Dictionary<int, VehicleSpec>();
            foreach (var v in All) d[v.Type.Id] = v;
            return d;
        }

        /// <summary>Názvy typov vozidiel – priamo použiteľné pre VehicleTypeDropdown.</summary>
        public static string[] Names()
        {
            var names = new string[All.Count];
            for (int i = 0; i < All.Count; i++) names[i] = All[i].Name;
            return names;
        }

        /// <summary>Vráti VehicleSpec podľa indexu v dropdowne (bezpečné voči rozsahu).</summary>
        public static VehicleSpec ByIndex(int index)
        {
            if (index < 0 || index >= All.Count) return null;
            return All[index];
        }

        /// <summary>Vráti VehicleSpec podľa názvu vozidla ("Coal Truck"), alebo null.</summary>
        public static VehicleSpec ByName(string name)
            => name != null && _byName.TryGetValue(name, out var v) ? v : null;

        /// <summary>Vráti VehicleSpec podľa textového označenia typu ("Coal Truck"), alebo null.</summary>
        public static VehicleSpec ByTypeName(string typeName)
            => typeName != null && _byTypeName.TryGetValue(typeName, out var v) ? v : null;

        /// <summary>Vráti VehicleSpec podľa číselného označenia typu (1, 2, ...), alebo null.</summary>
        public static VehicleSpec ByTypeId(int typeId)
            => _byTypeId.TryGetValue(typeId, out var v) ? v : null;
    }
}
```


### VehicleSystem.cs

```csharp
﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Game.VehicleStock;

/// <summary>
/// VehicleSystem.cs
///
/// CESTNÝ EKVIVALENT TrainSystem.cs – plne analogický systém pre ROAD
/// dopravu. Funguje úplne autonómne a nezávisle od TrainSystem.
///
/// PARALELIZMUS a DEBOUNCE:
/// ─────────────────────────────────────────────────────────────────────────
/// ✔ A* pathfinding beží na Thread Pool cez Task.Run (AStarPathThreaded).
///   Pracuje s int[] snapshotom road grafu – žiadne Unity API z vlákna.
///   Výsledky sa prenášajú cez ConcurrentQueue, aplikujú sa v Update().
///
/// ✔ OnMapChanged() – DEBOUNCE:
///   Každé volanie len nastaví flag + resetuje timer.
///   Update() odpočítava timer; až po uplynutí DEBOUNCE_DELAY od posledného
///   kliku spustí RoadSnapshot + Task.Run A* pre bežiace vozidlá.
///
/// ✘ UpdateVehicle / pohyb – hlavné vlákno (Unity API, thread-unsafe).
/// ✘ CreateVehicle / RemoveVehicle – hlavné vlákno (GameObject API).
/// ─────────────────────────────────────────────────────────────────────────
///
/// CESTNÉ VOZIDLO – "Distance-Based Path System":
/// ─────────────────────────────────────────────────────────────────────────
/// Najväčší rozdiel oproti TrainSystem: vozidlo je JEDINÝ kváder
/// (žiadne vagóny). Žiadny wagonCount, žiadny wagonSpacing.
///
/// • Trasa = PathData (List<Vector3> bodov + predpočítané kumulatívne dĺžky).
///   Zdroj: A* ako List<Vector2Int> → konvertovaný na waypointy cez
///   BuildWaypointPath (centrá + hrany + krivkové rohy).
/// • Vozidlo má skalárnu hodnotu vehicleDistance (vzdialenosť pozdĺž cesty).
///   Každý frame: vehicleDistance += speed * deltaTime.
/// • Pozícia = PathData.GetPositionAtDistance(d)   → Vector3.Lerp na segmente.
/// • Rotácia = PathData.GetDirectionAtDistance(d)  → Quaternion.LookRotation.
/// • Deterministické: rovnaký vstup → rovnaký výstup každý frame.
///
/// WAYPOINT-BASED MOVEMENT (zhodné s TrainSystem):
///   Trasa nie je center→center. Pre každý prechod A→B sa generuje:
///     A.center → A.edge(smer A→B) → B.edge(smer B→A) → B.center
///   Krivka:    B.entryEdge → B.exitEdge  (priama diagonála ~45°)
///   T-križovatka (RoadSwitch):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge
///     • ODBOČKA: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge
///   Križovatka (RoadCrossroad):
///     • PRIAMY: B.entryEdge → B.center → B.exitEdge (90°)
///     • ROHOVÝ: B.entryEdge → B.innerEntry → B.innerExit → B.exitEdge (45°)
///   Krivka, odbočka aj rohový prechod používajú VÝHRADNE priame čiarové
///   segmenty spojené lineárnym Lerp-om.
///
/// PREJAZD CEZ SVAH (LevelUp / LevelDown):
///   Identická logika ako v TrainSystem – biliniárna interpolácia výšky
///   povrchu vo waypointoch + hladké zdieľanie rohových vertexov medzi
///   susednými dlaždicami.
///
/// OBRAT SMERU NA STANICI:
///   path.Reverse() + vehicleDistance = totalLength - vehicleDistance
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public class VehicleSystem : MonoBehaviour
{
    public static VehicleSystem instance;

    // =====================================================================
    // POMOCNÁ TRIEDA – PathData
    // Ukladá trasu ako Vector3 body + predpočítané kumulatívne vzdialenosti.
    // (Štruktúra zhodná s TrainSystem.PathData – nezávislá kópia, aby
    // VehicleSystem nezávisel typovo od TrainSystem.)
    // =====================================================================

    public class PathData
    {
        public readonly List<Vector3> points;
        public readonly float[] cumulativeLengths;
        public readonly float totalLength;

        public int Count => points.Count;

        public PathData(List<Vector3> pts)
        {
            points = pts;
            cumulativeLengths = new float[pts.Count];
            cumulativeLengths[0] = 0f;
            for (int i = 1; i < pts.Count; i++)
                cumulativeLengths[i] = cumulativeLengths[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            totalLength = pts.Count > 0 ? cumulativeLengths[pts.Count - 1] : 0f;
        }

        /// <summary>
        /// Vráti pozíciu na trase pre zadanú vzdialenosť od začiatku.
        /// Vzdialenosť je upnutá na [0, totalLength].
        /// </summary>
        public Vector3 GetPositionAtDistance(float dist)
        {
            if (points.Count == 0) return Vector3.zero;
            if (points.Count == 1) return points[0];

            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            float segStart = cumulativeLengths[lo];
            float segEnd = cumulativeLengths[hi];
            float segLen = segEnd - segStart;

            if (segLen < 1e-6f) return points[hi];

            float localT = (dist - segStart) / segLen;
            return Vector3.Lerp(points[lo], points[hi], localT);
        }

        /// <summary>
        /// Vráti normalizovaný smer pohybu na zadanej vzdialenosti.
        /// </summary>
        public Vector3 GetDirectionAtDistance(float dist)
        {
            if (points.Count < 2) return Vector3.forward;

            dist = Mathf.Clamp(dist, 0f, totalLength);

            int lo = 0, hi = points.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeLengths[mid] <= dist) lo = mid;
                else hi = mid;
            }

            Vector3 dir = points[hi] - points[lo];
            return dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.forward;
        }

        public PathData Reversed()
        {
            var rev = new List<Vector3>(points);
            rev.Reverse();
            return new PathData(rev);
        }
    }

    // =====================================================================
    // DÁTOVÉ ŠTRUKTÚRY
    // =====================================================================

    /// <summary>
    /// Stav životného cyklu vozidla v danom depe – z pohľadu UI.
    ///
    /// Analógia k požadovanému stavu pre vlak. Slúži na to, aby UI okno
    /// (DepotRoadConstructionMenuUI) vedelo rozlíšiť tri situácie a podľa
    /// nich nastaviť StatusTypeTextCaption:
    ///
    ///   • NotCreated – vozidlo v tomto depe ešte nikdy nebolo vytvorené
    ///                  (pred prvým kliknutím na DCCreateVehicleButton).
    ///   • Created    – vozidlo existuje (po DCCreateVehicleButton).
    ///                  StatusTypeTextCaption potom zobrazuje "Running"
    ///                  alebo "Stopped" podľa isRunning.
    ///   • Removed    – vozidlo existovalo, ale bolo zmazané cez
    ///                  DCRemoveVehicleButton.
    ///
    /// Pre StatusTypeTextCaption sa NotCreated aj Removed prejavia rovnako –
    /// textom "NO VEHICLE". Stav je rozlíšený samostatne, lebo neskoršie
    /// UI (detail vozidla) môže obidve situácie odlíšiť, ak bude treba.
    /// </summary>
    public enum VehicleLifecycleState
    {
        NotCreated,
        Created,
        Removed
    }

    public class VehicleData
    {
        public int depotX, depotZ;

        /// <summary>Skrytá kocka (Renderer off) – pohybová logika.</summary>
        public GameObject vehicleObject;

        /// <summary>Vizuálne vozidlo (MAGENTA kváder). 1 kus, žiadne vagóny.</summary>
        public GameObject vehicleBody;

        // ------------------------------------------------------------------
        // DÁTOVÁ ŠTRUKTÚRA VOZIDLA (VehicleStock.cs)
        //
        // VehicleData = pohybový/herný stav (cesta, rýchlosť, stanice...).
        // VehicleInstance = "čo je vozidlo" – nemenné parametre cez Spec
        // (Cost, Speed, Power...) + premenlivý stav kusu (Age,
        // CurrentCapacity). Obidve veci sú zámerne oddelené, presne tak ako
        // TrainData (TrainSystem) vs. TrainInstance (TrainStock).
        //
        // Inštancia vznikne v CreateVehicle a uvoľní sa v RemoveVehicle –
        // viď tieto metódy. UI okno s detailmi vozidla bude tieto údaje
        // len ČÍTAŤ cez vd.vehicleInstance.
        // ------------------------------------------------------------------

        /// <summary>
        /// Dátová štruktúra vozidla (predloha + premenlivý stav).
        /// Nastavená v CreateVehicle, vynulovaná v RemoveVehicle.
        /// </summary>
        public VehicleInstance vehicleInstance;

        // ------------------------------------------------------------------
        // DISTANCE-BASED PATH STATE
        // ------------------------------------------------------------------

        public PathData activePath;
        public float vehicleDistance;
        public float vehicleSpeed;

        // ------------------------------------------------------------------
        // Stavové polia (zhodné s TrainData, ale bez vagónov)
        // ------------------------------------------------------------------
        public List<Vector2Int> stations;
        public int currentStationIndex;
        public bool isRunning;
        public bool isWaiting;
        public bool isReturningToDepot;
        public bool isAtDepot;
        public bool reverseDirection;
        public List<Vector2Int> currentPath;
        public int pathIndex;
        public Vector2Int currentTile;
        public float moveTimer;
        public float waitTimer;
        public bool isComputingPath;
        public List<Vector2Int> pendingPath;
        public bool isGoingToDepotViaStation;
        public bool isStoppedAwaitingDepotReturn;
        public bool pendingReturnToDepot;

        // ------------------------------------------------------------------
        // OBCHOD NA STANICI (VehicleTradeSystem)
        // ------------------------------------------------------------------
        /// <summary>
        /// True, ak v AKTUÁLNEJ zastávke na stanici už prebehol pokus o
        /// transakciu (výmenu tovaru). Bráni tomu, aby sa obchod spustil
        /// každý frame – spustí sa práve raz, po 2 s čakania. Resetuje sa
        /// pri každom novom príchode na cieľovú stanicu (OnPathComplete).
        ///
        /// Analógia k TrainData.tradeDoneAtStation.
        /// </summary>
        public bool tradeDoneAtStation;

        public int pathIndexAtPathStart;
        public int[] waypointToTileIdx;

        public VehicleData(int dx, int dz)
        {
            depotX = dx; depotZ = dz;
            stations = new List<Vector2Int>();
            currentStationIndex = 0;
            isRunning = false; isWaiting = false;
            isReturningToDepot = false; isAtDepot = true;
            reverseDirection = false;
            currentPath = new List<Vector2Int>();
            pathIndex = 0;
            currentTile = new Vector2Int(dx, dz);
            moveTimer = 0f; waitTimer = 0f;
            isComputingPath = false;
            pendingPath = null;
            isGoingToDepotViaStation = false;
            isStoppedAwaitingDepotReturn = false;
            pendingReturnToDepot = false;
            tradeDoneAtStation = false;

            activePath = null;
            vehicleDistance = 0f;
            vehicleSpeed = 0f;
            pathIndexAtPathStart = 0;
            waypointToTileIdx = null;
        }
    }

    // =====================================================================
    // VÝSLEDKOVÁ FRONTA
    // =====================================================================

    enum PathDestination { Station, Depot, ReturnViaStation }

    struct PathResult
    {
        public int depotKey;
        public List<Vector2Int> path;
        public bool isReturnToDepot;
        public PathDestination destination;
    }

    public enum ReturnToDepotResult { Dispatched, StoppedAwaitingSecondR, Error }

    readonly System.Collections.Concurrent.ConcurrentQueue<PathResult> _pendingPathResults
        = new System.Collections.Concurrent.ConcurrentQueue<PathResult>();

    // =====================================================================
    // DEBOUNCE
    // =====================================================================

    const float DEBOUNCE_DELAY = 0.4f;
    bool _mapChangePending = false;
    float _mapChangedDebounceTimer = 0f;

    // =====================================================================
    // SNAPSHOT ROAD GRIDU
    // =====================================================================

    const int GRID_SIZE = 256;

    /// <summary>
    /// Snapshot road časti tileGrid. Pre tiles, ktoré nie sú ROAD kategórie,
    /// uloží tileID=0 → A* ich pokladá za nepriechodné. Tým je VehicleSystem
    /// úplne izolovaný od RAIL grafu, hoci obidva systémy zdieľajú jeden
    /// tile grid v IndicatrixAPI.
    /// </summary>
    struct TileGridSnapshot
    {
        public int[] tileIDs;
        public int[] connections;
    }

    TileGridSnapshot SnapshotTileGrid()
    {
        var snap = new TileGridSnapshot
        {
            tileIDs = new int[GRID_SIZE * GRID_SIZE],
            connections = new int[GRID_SIZE * GRID_SIZE]
        };

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int z = 0; z < GRID_SIZE; z++)
            {
                // GetTileByIndexAny vidí všetky tiles bez ohľadu na kategóriu;
                // následne filtrujeme len ROAD do snapshotu.
                var td = IndicatrixAPI.instance.GetTileByIndexAny(x, z);
                int idx = z * GRID_SIZE + x;
                if (td.category == IndicatrixAPI.TileCategory.Road)
                {
                    snap.tileIDs[idx] = td.tileID;
                    snap.connections[idx] = (int)td.connections;
                }
                else
                {
                    // Ne-ROAD tiles sa do road snapshotu nezapočítavajú.
                    snap.tileIDs[idx] = 0;
                    snap.connections[idx] = 0;
                }
            }
        }
        return snap;
    }

    // =====================================================================
    // KONŠTANTY A STAV
    // =====================================================================

    Dictionary<int, VehicleData> vehicles = new Dictionary<int, VehicleData>();

    /// <summary>
    /// Stav životného cyklu vozidla per depo.
    ///
    /// Prečo samostatný slovník a nie pole na VehicleData:
    /// VehicleData v `vehicles` existuje IBA kým vozidlo existuje – po
    /// RemoveVehicle sa záznam z `vehicles` odstráni. Stav "Removed" sa
    /// teda nemá kam uložiť. Tento slovník prežíva odstránenie vozidla a
    /// pamätá si, či vozidlo v danom depe ešte nebolo vytvorené
    /// (depo v slovníku chýba → NotCreated) alebo bolo zmazané (Removed).
    /// </summary>
    Dictionary<int, VehicleLifecycleState> vehicleLifecycle
        = new Dictionary<int, VehicleLifecycleState>();

    /// <summary>
    /// Vráti stav životného cyklu vozidla pre dané depo.
    /// Ak depo nie je v evidencii, vozidlo ešte nikdy nebolo vytvorené.
    /// </summary>
    public VehicleLifecycleState GetVehicleLifecycleState(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return vehicleLifecycle.TryGetValue(key, out var state)
            ? state
            : VehicleLifecycleState.NotCreated;
    }

    /// <summary>Čas prechodu jednej dlaždice v sekundách.</summary>
    const float MOVE_TIME = 2.0f;
    const float STATION_WAIT = 10.0f;
    const float BREAK_WAIT = 1.0f;

    /// <summary>
    /// Oneskorenie od príchodu na cieľovú stanicu, po ktorom sa vykoná
    /// výmena tovaru (VehicleTradeSystem). Zadanie: štandardné čakanie je
    /// 10 s (STATION_WAIT), transakcia prebehne po 2 s od príchodu.
    ///
    /// Spúšťacia podmienka v UpdateVehicle:
    /// (STATION_WAIT - waitTimer) >= TRADE_DELAY, t.j. po 2 s čakania.
    /// </summary>
    const float TRADE_DELAY = 2.0f;

    /// <summary>Rozmery kvádra vozidla.</summary>
    static readonly Vector3 VEHICLE_SCALE = new Vector3(0.3f, 0.3f, 0.7f);

    void Awake()
    {
        instance = this;
    }

    // =====================================================================
    // POMOCNÉ
    // =====================================================================

    int DepotKey(int x, int z) => x * 10000 + z;

    Vector3 TileCenter(Vector2Int tile)
    {
        float y = GetTileSurfaceY(tile, 0.5f, 0.5f) + ROAD_OFFSET_Y;
        return new Vector3(tile.x + 0.5f, y, tile.y + 0.5f);
    }

    float GetTerrainY(int x, int z)
    {
        try
        {
            int width = TerrainManager.instance.terrainWidth + 1;
            int index = z * width + x;
            if (index >= 0 && index < TerrainManager.instance.coordsF.Length)
                return TerrainManager.instance.coordsF[index].y;
        }
        catch { }
        return 0f;
    }

    /// <summary>Vertikálny offset vozovky nad povrchom terénu.</summary>
    const float ROAD_OFFSET_Y = 0.3f;

    /// <summary>
    /// Biliniárna interpolácia výšky terénu vnútri jednej dlaždice medzi
    /// 4 rohovými vertexmi. Logika zhodná s TrainSystem.GetTileSurfaceY.
    ///
    /// PREČO TOTO POTREBUJEME (kopce, LevelUp / LevelDown):
    ///   Bez tejto interpolácie by vozidlo na svahu ostalo vodorovne a
    ///   na hrane medzi dlaždicami "skočilo" na novú výšku → 90° schod.
    ///   S biliniárnou interpoláciou má každý waypoint správnu výšku na
    ///   šikmej ploche dlaždice → pohyb cez svah je plynulý.
    ///
    /// HLADKOSŤ NA HRANÁCH MEDZI DLAŽDICAMI:
    ///   A.right-edge a B.left-edge zdieľajú ten istý pár rohových vertexov,
    ///   takže interpolovaná Y vychádza identická → žiadny schod, žiadny lom.
    /// </summary>
    float GetTileSurfaceY(Vector2Int tile, float u, float v)
    {
        float h00 = GetTerrainY(tile.x, tile.y);
        float h10 = GetTerrainY(tile.x + 1, tile.y);
        float h01 = GetTerrainY(tile.x, tile.y + 1);
        float h11 = GetTerrainY(tile.x + 1, tile.y + 1);

        float omu = 1f - u;
        float omv = 1f - v;

        return omu * omv * h00
             + u * omv * h10
             + omu * v * h01
             + u * v * h11;
    }

    /// <summary>
    /// Vráti tileID pre danú ROAD dlaždicu. Pre tiles, ktoré nie sú ROAD
    /// kategórie (alebo sú mimo gridu), vracia -1 / 0. Slúži ako sentinel
    /// pre VehicleSystem – RAIL tiles sú pre nás "neviditeľné".
    /// </summary>
    int GetRoadTileID(int x, int z)
    {
        if (x < 0 || x >= GRID_SIZE || z < 0 || z >= GRID_SIZE) return -1;
        var td = IndicatrixAPI.instance.GetTileByIndexAny(x, z);
        if (td.category != IndicatrixAPI.TileCategory.Road) return 0;
        return td.tileID;
    }

    bool IsPassable(int x, int z)
    {
        int id = GetRoadTileID(x, z);
        return id == 1 || id == 2 || id == 3;
    }

    // =====================================================================
    // DIRECTION HELPERS – mapovanie Vector2Int <-> DirectionMask
    // =====================================================================

    static IndicatrixAPI.DirectionMask DirFromStep(Vector2Int step)
    {
        if (step.x == 1 && step.y == 0) return IndicatrixAPI.DirectionMask.Right;
        if (step.x == -1 && step.y == 0) return IndicatrixAPI.DirectionMask.Left;
        if (step.x == 0 && step.y == 1) return IndicatrixAPI.DirectionMask.Top;
        if (step.x == 0 && step.y == -1) return IndicatrixAPI.DirectionMask.Bottom;
        return IndicatrixAPI.DirectionMask.None;
    }

    static IndicatrixAPI.DirectionMask GetDirection(Vector2Int from, Vector2Int to)
    {
        return DirFromStep(to - from);
    }

    /// <summary>
    /// Pre danú dlaždicu (x, z) a smer 'dir' vráti svetový bod uprostred
    /// danej hrany dlaždice. Logika zhodná s TrainSystem.EdgePoint, len
    /// s ROAD_OFFSET_Y namiesto TRACK_OFFSET_Y.
    /// </summary>
    Vector3 EdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f, GetTileSurfaceY(tile, 1.0f, 0.5f) + ROAD_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f, GetTileSurfaceY(tile, 0.0f, 0.5f) + ROAD_OFFSET_Y, fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 1.0f) + ROAD_OFFSET_Y, fz + 1.0f);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f, GetTileSurfaceY(tile, 0.5f, 0.0f) + ROAD_OFFSET_Y, fz + 0.0f);
            default: return TileCenter(tile);
        }
    }

    /// <summary>
    /// Krivka (Road) = presne 2 nastavené smery, ktoré NIE SÚ proti-smery.
    /// </summary>
    static bool IsCurveTile(IndicatrixAPI.DirectionMask conns)
    {
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        if (bits != 2) return false;

        bool horiz = (conns & (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right))
                       == (IndicatrixAPI.DirectionMask.Left | IndicatrixAPI.DirectionMask.Right);
        bool vert = (conns & (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom))
                       == (IndicatrixAPI.DirectionMask.Top | IndicatrixAPI.DirectionMask.Bottom);

        return !horiz && !vert;
    }

    /// <summary>
    /// T-križovatka / Y-rozdvojenie (RoadSwitch) – 3 spojenia, plne obojsmerné.
    /// </summary>
    static bool IsSwitchTile(IndicatrixAPI.DirectionMask conns)
    {
        int bits = 0;
        if ((conns & IndicatrixAPI.DirectionMask.Left) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Right) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Top) != 0) bits++;
        if ((conns & IndicatrixAPI.DirectionMask.Bottom) != 0) bits++;
        return bits == 3;
    }

    /// <summary>
    /// Križovatka (RoadCrossroad) – všetky 4 smery.
    /// </summary>
    static bool IsCrossroadTile(IndicatrixAPI.DirectionMask conns)
    {
        const IndicatrixAPI.DirectionMask ALL =
            IndicatrixAPI.DirectionMask.Left
          | IndicatrixAPI.DirectionMask.Right
          | IndicatrixAPI.DirectionMask.Top
          | IndicatrixAPI.DirectionMask.Bottom;
        return (conns & ALL) == ALL;
    }

    /// <summary>
    /// True ak sú dva smery navzájom kolmé (jeden horizontálny + jeden vertikálny).
    /// </summary>
    static bool IsPerpendicular(IndicatrixAPI.DirectionMask a, IndicatrixAPI.DirectionMask b)
    {
        bool aHoriz = a == IndicatrixAPI.DirectionMask.Left || a == IndicatrixAPI.DirectionMask.Right;
        bool aVert = a == IndicatrixAPI.DirectionMask.Top || a == IndicatrixAPI.DirectionMask.Bottom;
        bool bHoriz = b == IndicatrixAPI.DirectionMask.Left || b == IndicatrixAPI.DirectionMask.Right;
        bool bVert = b == IndicatrixAPI.DirectionMask.Top || b == IndicatrixAPI.DirectionMask.Bottom;
        return (aHoriz && bVert) || (aVert && bHoriz);
    }

    /// <summary>
    /// Vnútorný bod pre 45° turnout / rohový prejazd. Zhodné s TrainSystem.
    /// </summary>
    Vector3 SwitchInnerEdgePoint(Vector2Int tile, IndicatrixAPI.DirectionMask dir, float t)
    {
        float fx = tile.x, fz = tile.y;
        switch (dir)
        {
            case IndicatrixAPI.DirectionMask.Right:
                return new Vector3(fx + 1.0f - t,
                    GetTileSurfaceY(tile, 1.0f - t, 0.5f) + ROAD_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Left:
                return new Vector3(fx + 0.0f + t,
                    GetTileSurfaceY(tile, 0.0f + t, 0.5f) + ROAD_OFFSET_Y,
                    fz + 0.5f);
            case IndicatrixAPI.DirectionMask.Top:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 1.0f - t) + ROAD_OFFSET_Y,
                    fz + 1.0f - t);
            case IndicatrixAPI.DirectionMask.Bottom:
                return new Vector3(fx + 0.5f,
                    GetTileSurfaceY(tile, 0.5f, 0.0f + t) + ROAD_OFFSET_Y,
                    fz + 0.0f + t);
            default: return TileCenter(tile);
        }
    }

    // =====================================================================
    // CAN MOVE – validácia smeru pre A* (thread-safe, snapshot)
    // =====================================================================

    /// <summary>
    /// Validácia A* prechodu z 'fromTile' na 'toTile' v smere 'direction'.
    /// Identické pravidlá ako TrainSystem.CanMove – pracuje so snapshot
    /// poľami, ktoré pre VehicleSystem obsahujú IBA ROAD tiles (RAIL tiles
    /// majú tileID=0 v snapshot, takže sú nepriechodné).
    /// </summary>
    static bool CanMove(int[] tileIDs, int[] conns, Vector2Int fromTile, Vector2Int toTile,
                        IndicatrixAPI.DirectionMask direction)
    {
        if (toTile.x < 0 || toTile.x >= GRID_SIZE || toTile.y < 0 || toTile.y >= GRID_SIZE)
            return false;
        if (fromTile.x < 0 || fromTile.x >= GRID_SIZE || fromTile.y < 0 || fromTile.y >= GRID_SIZE)
            return false;

        int toIdx = toTile.y * GRID_SIZE + toTile.x;
        int toID = tileIDs[toIdx];
        if (!(toID == 1 || toID == 2 || toID == 3)) return false;

        int fromIdx = fromTile.y * GRID_SIZE + fromTile.x;
        IndicatrixAPI.DirectionMask fromConn = (IndicatrixAPI.DirectionMask)conns[fromIdx];
        IndicatrixAPI.DirectionMask toConn = (IndicatrixAPI.DirectionMask)conns[toIdx];

        if ((fromConn & direction) == 0) return false;

        IndicatrixAPI.DirectionMask oppositeDir = IndicatrixAPI.Opposite(direction);
        if ((toConn & oppositeDir) == 0) return false;

        return true;
    }

    // =====================================================================
    // BUILD WAYPOINT PATH – konvertuje List<Vector2Int> na waypointy
    // (logika identická s TrainSystem.BuildWaypointPath – pozri tam podrobné
    //  komentáre k pravidlám pre krivky, výhybky, križovatky a tile-boundary
    //  semantike)
    // =====================================================================

    void BuildWaypointPath(List<Vector2Int> tilePath, out List<Vector3> waypoints,
                           out List<int> tileIdxPerWaypoint)
    {
        var wp = new List<Vector3>();
        var tIdx = new List<int>();

        if (tilePath == null || tilePath.Count == 0)
        {
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        if (tilePath.Count == 1)
        {
            wp.Add(TileCenter(tilePath[0]));
            tIdx.Add(0);
            waypoints = wp;
            tileIdxPerWaypoint = tIdx;
            return;
        }

        // Lokálna funkcia – deduplikuje len waypointy z rovnakej dlaždice
        // (intra-tile duplikáty). Hraničné waypointy A.exitEdge a B.entryEdge
        // sa zachovajú ako 2 samostatné waypointy aj keď sa v priestore kryjú.
        void AddWaypoint(Vector3 pt, int tileIdx)
        {
            if (wp.Count > 0)
            {
                Vector3 last = wp[wp.Count - 1];
                int lastTileIdx = tIdx[tIdx.Count - 1];
                bool sameTile = (lastTileIdx == tileIdx);
                bool samePos = (pt - last).sqrMagnitude < 1e-8f;
                if (sameTile && samePos) return;
            }
            wp.Add(pt);
            tIdx.Add(tileIdx);
        }

        for (int i = 0; i < tilePath.Count; i++)
        {
            Vector2Int tile = tilePath[i];
            var conns = IndicatrixAPI.instance.GetTileByIndexAny(tile.x, tile.y).connections;

            bool isFirst = (i == 0);
            bool isLast = (i == tilePath.Count - 1);

            IndicatrixAPI.DirectionMask outDir = IndicatrixAPI.DirectionMask.None;
            if (!isLast) outDir = GetDirection(tile, tilePath[i + 1]);

            IndicatrixAPI.DirectionMask inDir = IndicatrixAPI.DirectionMask.None;
            if (!isFirst) inDir = IndicatrixAPI.Opposite(GetDirection(tilePath[i - 1], tile));

            // ── KRIVKA: entryEdge → exitEdge (priama diagonála ~45°)
            if (!isFirst && !isLast && IsCurveTile(conns))
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── T-KRIŽOVATKA (RoadSwitch) – 3 spojenia
            if (!isFirst && !isLast && IsSwitchTile(conns))
            {
                if (IsPerpendicular(inDir, outDir))
                {
                    const float SWITCH_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, SWITCH_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── KRIŽOVATKA (RoadCrossroad) – 4 spojenia
            if (!isFirst && !isLast && IsCrossroadTile(conns))
            {
                if (IsPerpendicular(inDir, outDir))
                {
                    const float CROSSROAD_TURNOUT_T = 0.25f;
                    AddWaypoint(EdgePoint(tile, inDir), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, inDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(SwitchInnerEdgePoint(tile, outDir, CROSSROAD_TURNOUT_T), i);
                    AddWaypoint(EdgePoint(tile, outDir), i);
                    continue;
                }

                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
                continue;
            }

            // ── PRIAMA / KONCOVÁ DLAŽDICA
            if (isFirst)
            {
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
            else if (isLast)
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
            }
            else
            {
                AddWaypoint(EdgePoint(tile, inDir), i);
                AddWaypoint(TileCenter(tile), i);
                AddWaypoint(EdgePoint(tile, outDir), i);
            }
        }

        waypoints = wp;
        tileIdxPerWaypoint = tIdx;
    }

    // =====================================================================
    // VOZIDLO – pomocná metóda vytvorenia kvádra
    // =====================================================================

    GameObject CreateVehiclePart(string name, Color color, Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.localScale = VEHICLE_SCALE;
        go.transform.position = position;
        Destroy(go.GetComponent<BoxCollider>());
        var rend = go.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = color;
        go.SetActive(false);
        return go;
    }

    // =====================================================================
    // SPRÁVA VOZIDIEL
    // =====================================================================

    /// <summary>
    /// Vytvorí vozidlo v zadanom ROAD depe.
    ///
    /// Parameter vehicleTypeName je názov typu vozidla z VehicleCatalog
    /// (napr. "Vehicle 1", "Vehicle 2"). Slúži na dve veci:
    ///   1. Vyhľadanie VehicleSpec v katalógu → vytvorenie VehicleInstance
    ///      (dátová štruktúra vozidla s atribútmi Cost, Speed, Power...).
    ///   2. Pomenovanie vizuálneho GameObjectu.
    /// Vizuálne ide stále o ten istý magenta kváder – atribúty žijú v
    /// dátovej štruktúre vd.vehicleInstance, nie vo vizuáli.
    /// </summary>
    public bool CreateVehicle(int dx, int dz, string vehicleTypeName = "Vehicle 1")
    {
        int key = DepotKey(dx, dz);
        if (vehicles.ContainsKey(key)) return false;
        if (GetRoadTileID(dx, dz) != 3) return false;

        // Vyhľadáme predlohu vozidla v katalógu. Ak názov nesedí (napr.
        // neznámy typ), spadneme na prvý typ v katalógu, aby vozidlo malo
        // vždy platnú dátovú štruktúru.
        VehicleSpec spec = VehicleCatalog.ByName(vehicleTypeName)
                           ?? VehicleCatalog.ByIndex(0);
        if (spec == null)
        {
            Debug.LogError("[VehicleSystem] VehicleCatalog je prázdny – vozidlo sa nedá vytvoriť.");
            return false;
        }

        VehicleData vd = new VehicleData(dx, dz);
        Vector3 depotPos = TileCenter(new Vector2Int(dx, dz));

        // Dátová štruktúra vozidla – nová inštancia z katalógovej predlohy.
        // Age = 0, CurrentCapacity = 0 (nastaví konštruktor VehicleInstance).
        vd.vehicleInstance = new VehicleInstance(spec);

        // Skrytá kocka – pohybová logika
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = VEHICLE_SCALE;
        cube.transform.position = depotPos;
        cube.name = $"VehicleHead_{dx}_{dz}";
        Destroy(cube.GetComponent<BoxCollider>());
        cube.GetComponent<Renderer>().enabled = false;
        vd.vehicleObject = cube;

        // Vozidlo – MAGENTA, 1 kus, žiadne vagóny
        string bodyName = $"{spec.Name}_{dx}_{dz}";
        vd.vehicleBody = CreateVehiclePart(bodyName, new Color(1f, 0f, 1f), depotPos);

        vehicles[key] = vd;

        // Životný cyklus → Created (StatusTypeTextCaption potom zobrazí
        // Running/Stopped namiesto "NO VEHICLE").
        vehicleLifecycle[key] = VehicleLifecycleState.Created;

        Debug.Log($"[VehicleSystem] Vozidlo '{spec.Name}' ({spec.Type}) vytvorené v depe [{dx},{dz}]. " +
                  $"Kapacita {spec.MaximumCapacity}, cena {spec.Cost}, rýchlosť {spec.Speed}.");
        return true;
    }

    public VehicleData GetVehicle(int dx, int dz)
    {
        int key = DepotKey(dx, dz);
        return vehicles.TryGetValue(key, out var vd) ? vd : null;
    }

    /// <summary>
    /// Vráti súradnice [depotX, depotZ] VŠETKÝCH aktuálne existujúcich
    /// vozidlových dep. Keďže platí "1 vehicle depo = 1 vehicle", počet
    /// prvkov zoznamu = počet vozidiel v hre.
    ///
    /// Slúži pre informačné UI (StatusStationsMenuUI). Vnútorný slovník
    /// `vehicles` ostáva privátny – navonok dávame len read-only kópiu
    /// kľúčových údajov, takže volajúci nemôže poškodiť interný stav.
    /// </summary>
    public List<Vector2Int> GetAllDepotCoords()
    {
        var result = new List<Vector2Int>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            result.Add(new Vector2Int(vd.depotX, vd.depotZ));
        }
        return result;
    }

    /// <summary>
    /// Celkový počet vozidlových dep (= počet vozidiel). Pohodlný getter,
    /// aby UI nemuselo kvôli počtu vytvárať celý zoznam cez
    /// GetAllDepotCoords().
    /// </summary>
    public int GetDepotCount()
    {
        return vehicles.Count;
    }

    /// <summary>
    /// Vráti dátové štruktúry (VehicleInstance) VŠETKÝCH aktuálne
    /// existujúcich vozidiel. Keďže platí "1 vehicle depo = 1 vehicle",
    /// počet prvkov zoznamu = počet vozidiel v hre.
    ///
    /// Slúži pre informačné UI (StatusVehiclesMenuUI), ktoré z každej
    /// VehicleInstance prečíta názov vozidla (Name) a typ suroviny
    /// (Type.Name).
    ///
    /// Vnútorný slovník `vehicles` ostáva privátny – navonok dávame len
    /// read-only kópiu zoznamu referencií na VehicleInstance. Volajúci tak
    /// nemôže pridať/odobrať vozidlo (zmeniť `vehicles`), čítať atribúty
    /// jednotlivých vozidiel ale môže. Analógia k GetAllDepotCoords().
    /// </summary>
    public List<VehicleInstance> GetAllVehicleInstances()
    {
        var result = new List<VehicleInstance>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd != null && vd.vehicleInstance != null)
                result.Add(vd.vehicleInstance);
        }
        return result;
    }

    /// <summary>
    /// EKONOMIKA (read-only snapshot): pre KAŽDÉ existujúce vozidlo spáruje jeho
    /// DEPO súradnice [depotX, depotZ] (= kľúč pre <see cref="ReturnToDepot"/>)
    /// s jeho dátovou inštanciou <see cref="VehicleInstance"/> (OperatingCosts,
    /// ServiceLife, ServicingInterval). Interný slovník `vehicles` ostáva privátny.
    ///
    /// Slúži pre EconomySystem: mesačné prevádzkové náklady + automatické
    /// poslanie vozidla do depa po uplynutí servisného intervalu alebo životnosti.
    /// Analógia k TrainSystem.GetActiveTrainInfos().
    /// </summary>
    public List<ActiveVehicleInfo> GetActiveVehicleInfos()
    {
        var result = new List<ActiveVehicleInfo>(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd != null && vd.vehicleInstance != null)
                result.Add(new ActiveVehicleInfo(vd.depotX, vd.depotZ, vd.vehicleInstance));
        }
        return result;
    }

    /// <summary>Read-only dvojica [depo súradnice + dátová inštancia vozidla] pre EconomySystem.</summary>
    public readonly struct ActiveVehicleInfo
    {
        public readonly int DepotX;
        public readonly int DepotZ;
        public readonly VehicleInstance Instance;

        public ActiveVehicleInfo(int depotX, int depotZ, VehicleInstance instance)
        {
            DepotX = depotX;
            DepotZ = depotZ;
            Instance = instance;
        }
    }

    public bool AddStation(int depotX, int depotZ, int stX, int stZ)
    {
        VehicleData vd = GetVehicle(depotX, depotZ);
        if (vd == null) return false;
        if (GetRoadTileID(stX, stZ) != 2) return false;

        Vector2Int st = new Vector2Int(stX, stZ);
        if (!vd.stations.Contains(st))
        {
            vd.stations.Add(st);
            Debug.Log($"[VehicleSystem] Stanica [{stX},{stZ}] pridaná pre vozidlo v depe [{depotX},{depotZ}]. Celkom: {vd.stations.Count}");
        }
        return true;
    }

    public bool StartVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;
        if (vd.stations.Count < 2)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] nemá aspoň 2 stanice!");
            return false;
        }

        bool isResume = !vd.isAtDepot && vd.currentPath != null && vd.currentPath.Count > 0;

        vd.isRunning = true;
        vd.isWaiting = false;
        vd.isReturningToDepot = false;

        if (isResume)
        {
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] OBNOVENÉ (resume).");
        }
        else
        {
            vd.isAtDepot = false;
            vd.currentStationIndex = 0;
            vd.reverseDirection = false;
            vd.moveTimer = 0f;
            vd.waitTimer = 0f;
            vd.pendingPath = null;
            vd.pendingReturnToDepot = false;

            vd.activePath = null;
            vd.vehicleDistance = 0f;

            ComputeNextPathAsync(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] SPUSTENÉ (prvý štart).");
        }

        return true;
    }

    public bool StopVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;
        vd.isRunning = false;
        vd.isComputingPath = false;
        vd.pendingPath = null;
        vd.pendingReturnToDepot = false;
        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] ZASTAVENÉ.");
        return true;
    }

    public ReturnToDepotResult ReturnToDepot(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return ReturnToDepotResult.Error;

        if (GetRoadTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[VehicleSystem] Depo [{dx},{dz}] neexistuje – vozidlo zastane.");
            vd.isRunning = false;
            vd.isStoppedAwaitingDepotReturn = false;
            return ReturnToDepotResult.Error;
        }

        if (vd.isStoppedAwaitingDepotReturn)
        {
            vd.isStoppedAwaitingDepotReturn = false;
            vd.pendingReturnToDepot = false;
            vd.isRunning = true;
            vd.isWaiting = false;
            vd.waitTimer = 0f;
            vd.isGoingToDepotViaStation = false;
            vd.isComputingPath = false;
            vd.pendingPath = null;
            DispatchToDepot(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] → depo priamo.");
            return ReturnToDepotResult.Dispatched;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);

        if (vd.isWaiting)
        {
            vd.pendingReturnToDepot = false;
            vd.isGoingToDepotViaStation = true;
            vd.isStoppedAwaitingDepotReturn = false;
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] čaká na stanici → po odpočítaní pôjde do depa.");
            return ReturnToDepotResult.Dispatched;
        }

        if (IsDepotOnCurrentPath(vd, depotTile))
        {
            vd.pendingReturnToDepot = false;
            vd.isGoingToDepotViaStation = false;
            vd.isStoppedAwaitingDepotReturn = false;
            TrimPathToDepot(vd, depotTile);
            vd.isReturningToDepot = true;
            RebuildActivePathFromCurrentPath(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] → depo PRIAMO (na aktuálnej ceste).");
            return ReturnToDepotResult.Dispatched;
        }

        vd.pendingReturnToDepot = true;
        vd.isGoingToDepotViaStation = false;
        vd.isStoppedAwaitingDepotReturn = false;
        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}]: depo nie je v smere jazdy → dokončí cestu a potom pôjde do depa.");
        return ReturnToDepotResult.Dispatched;
    }

    bool IsDepotOnCurrentPath(VehicleData vd, Vector2Int depotTile)
    {
        if (vd.currentPath == null || vd.pathIndex >= vd.currentPath.Count) return false;
        for (int i = vd.pathIndex; i < vd.currentPath.Count; i++)
            if (vd.currentPath[i] == depotTile) return true;
        return false;
    }

    void TrimPathToDepot(VehicleData vd, Vector2Int depotTile)
    {
        if (vd.currentPath == null) return;
        for (int i = vd.pathIndex; i < vd.currentPath.Count; i++)
        {
            if (vd.currentPath[i] == depotTile)
            {
                vd.currentPath = vd.currentPath.GetRange(0, i + 1);
                return;
            }
        }
    }

    void DispatchToDepot(VehicleData vd)
    {
        int key = DepotKey(vd.depotX, vd.depotZ);
        Vector2Int goal = new Vector2Int(vd.depotX, vd.depotZ);
        Vector2Int start = vd.currentTile;
        var snap = SnapshotTileGrid();

        vd.isReturningToDepot = true;
        vd.isGoingToDepotViaStation = false;
        vd.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = true,
                destination = PathDestination.Depot
            });
        });

        Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] → depo.");
    }

    /// <summary>
    /// Odstráni vozidlo. Povolené iba ak je vozidlo fyzicky v depe a zastavené.
    /// Logika zhodná s TrainSystem.RemoveTrain (pozri tam podrobný popis
    /// scenárov, ktoré vyžadujú túto guardu).
    ///
    /// ZRKADLOVOSŤ s CreateVehicle:
    /// CreateVehicle vytvorí dátové štruktúry vozidla (VehicleInstance) a
    /// vizuálne GameObjecty. RemoveVehicle musí to isté uvoľniť – teda
    /// okrem zničenia GameObjectov aj uvoľniť vd.vehicleInstance a
    /// nastaviť životný cyklus na Removed.
    /// </summary>
    public bool RemoveVehicle(int dx, int dz)
    {
        VehicleData vd = GetVehicle(dx, dz);
        if (vd == null) return false;

        if (vd.isRunning)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] sa nedá odstrániť – stále beží. Najprv ho zastavte.");
            return false;
        }

        Vector2Int depotTile = new Vector2Int(dx, dz);
        if (vd.currentTile != depotTile)
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo v depe [{dx},{dz}] sa nedá odstrániť – nie je fyzicky v depe (aktuálne na [{vd.currentTile.x},{vd.currentTile.y}]).");
            return false;
        }

        if (GetRoadTileID(dx, dz) != 3)
        {
            Debug.LogWarning($"[VehicleSystem] Tile [{dx},{dz}] už nie je ROAD depo – vozidlo sa nedá odstrániť.");
            return false;
        }

        vd.isComputingPath = false;
        vd.pendingPath = null;

        if (vd.vehicleObject != null) Destroy(vd.vehicleObject);
        if (vd.vehicleBody != null) Destroy(vd.vehicleBody);

        // Uvoľnenie dátovej štruktúry vozidla – zrkadlovo k CreateVehicle,
        // ktorý ju vytvoril. Po tomto bode vozidlo nemá žiadne atribúty.
        vd.vehicleInstance = null;

        int key = DepotKey(dx, dz);
        vehicles.Remove(key);

        // Životný cyklus → Removed (StatusTypeTextCaption potom zobrazí
        // "NO VEHICLE", kým sa vozidlo znovu nevytvorí cez DCCreateVehicleButton).
        vehicleLifecycle[key] = VehicleLifecycleState.Removed;

        Debug.Log($"[VehicleSystem] Vozidlo z depa [{dx},{dz}] ODSTRÁNENÉ (dátová štruktúra uvoľnená).");
        return true;
    }

    // =====================================================================
    // NOTIFIKÁCIA PRI ZMENE MAPY – DEBOUNCE
    // =====================================================================

    public void OnMapChanged()
    {
        _mapChangePending = true;
        _mapChangedDebounceTimer = DEBOUNCE_DELAY;
    }

    // =====================================================================
    // UPDATE
    // =====================================================================

    void Update()
    {
        if (_mapChangePending)
        {
            _mapChangedDebounceTimer -= Time.deltaTime;
            if (_mapChangedDebounceTimer <= 0f)
            {
                _mapChangePending = false;
                _mapChangedDebounceTimer = 0f;
                TriggerMapChangedReroute();
            }
        }

        ApplyPendingPathResults();

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (vd.vehicleObject == null) continue;
            if (!vd.isRunning) continue;
            UpdateVehicle(vd);
        }
    }

    // =====================================================================
    // POHYB VOZIDLA – DISTANCE-BASED
    // =====================================================================

    void UpdateVehicle(VehicleData vd)
    {
        // ── Čakanie (stanica / pauza)
        if (vd.isWaiting)
        {
            vd.waitTimer -= Time.deltaTime;

            // ── OBCHOD: po 2 s čakania na cieľovej stanici ────────────────
            // Spustí sa práve raz za zastávku. tradeDoneAtStation je nastavené
            // na false iba pri príchode na cieľovú stanicu (OnPathComplete),
            // takže pri BREAK_WAIT ani pri ceste do depa sa obchod nespustí.
            if (!vd.tradeDoneAtStation
                && (STATION_WAIT - vd.waitTimer) >= TRADE_DELAY)
            {
                vd.tradeDoneAtStation = true;
                TryExecuteTradeAtStation(vd);
            }

            if (vd.waitTimer <= 0f)
            {
                vd.isWaiting = false;
                vd.waitTimer = 0f;

                if (vd.isReturningToDepot)
                    DispatchToDepot(vd);
                else if (vd.isGoingToDepotViaStation)
                    DispatchToDepot(vd);
                else
                    AdvanceStation(vd);
            }
            return;
        }

        // ── Čakáme na výpočet novej trasy
        if (vd.activePath == null)
        {
            if (vd.isComputingPath) return;
            OnPathComplete(vd);
            return;
        }

        // ── Pohyb vozidla pozdĺž trasy
        vd.vehicleDistance += vd.vehicleSpeed * Time.deltaTime;

        UpdateCurrentTile(vd);

        Vector3 headPos = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
        vd.vehicleObject.transform.position = headPos;

        // ── Vizuálne vozidlo (1 kváder)
        UpdateVehicleVisual(vd);

        // ── Detekcia konca trasy
        if (vd.vehicleDistance >= vd.activePath.totalLength)
        {
            vd.vehicleDistance = vd.activePath.totalLength;
            vd.currentTile = vd.currentPath != null && vd.currentPath.Count > 0
                ? vd.currentPath[vd.currentPath.Count - 1]
                : vd.currentTile;

            if (vd.pendingPath != null)
            {
                List<Vector2Int> pending = vd.pendingPath;
                vd.pendingPath = null;
                ApplyNewPath(vd, pending);
                return;
            }

            vd.activePath = null;
            OnPathComplete(vd);
        }
    }

    void UpdateCurrentTile(VehicleData vd)
    {
        if (vd.currentPath == null || vd.activePath == null) return;
        if (vd.waypointToTileIdx == null || vd.waypointToTileIdx.Length == 0) return;

        float[] cumLens = vd.activePath.cumulativeLengths;
        int lastPassedIdx = 0;
        for (int i = 0; i < cumLens.Length; i++)
        {
            if (cumLens[i] <= vd.vehicleDistance)
                lastPassedIdx = i;
            else
                break;
        }

        int subPathTileIdx = vd.waypointToTileIdx[lastPassedIdx];
        int tileIdx = vd.pathIndexAtPathStart + subPathTileIdx;
        if (tileIdx >= 0 && tileIdx < vd.currentPath.Count)
            vd.currentTile = vd.currentPath[tileIdx];
    }

    // =====================================================================
    // VIZUÁLNE VOZIDLO – 1 KVÁDER (žiadne vagóny)
    // =====================================================================

    /// <summary>
    /// Aktualizuje pozíciu, rotáciu a viditeľnosť vozidla.
    /// Oproti TrainSystem UpdateConsistVisuals (kde sa iteruje cez
    /// lokomotívu + N vagónov) ide o jediný kváder bez offsetu.
    /// </summary>
    void UpdateVehicleVisual(VehicleData vd)
    {
        if (vd.activePath == null) return;
        if (vd.vehicleBody == null) return;

        const float DEPOT_HIDE_RADIUS = 0.55f;
        bool isReturning = vd.isReturningToDepot;
        Vector3 depotCenter = TileCenter(new Vector2Int(vd.depotX, vd.depotZ));

        bool shouldBeVisible = true;

        // ── Skrývanie pri návrate do depa
        if (isReturning)
        {
            Vector3 pos = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
            if (Vector3.Distance(pos, depotCenter) < DEPOT_HIDE_RADIUS)
                shouldBeVisible = false;
        }

        if (vd.vehicleBody.activeSelf != shouldBeVisible)
            vd.vehicleBody.SetActive(shouldBeVisible);

        if (!shouldBeVisible) return;

        Vector3 posBody = vd.activePath.GetPositionAtDistance(vd.vehicleDistance);
        if (float.IsNaN(posBody.x) || float.IsNaN(posBody.y) || float.IsNaN(posBody.z))
        {
            Debug.LogWarning($"[VehicleSystem] NaN pozícia – preskočené.");
            return;
        }

        Vector3 dir = vd.activePath.GetDirectionAtDistance(vd.vehicleDistance);
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        vd.vehicleBody.transform.position = posBody;
        vd.vehicleBody.transform.rotation = rot;
    }

    // =====================================================================
    // DOKONČENIE CESTY
    // =====================================================================

    void OnPathComplete(VehicleData vd)
    {
        if (vd.isReturningToDepot)
        {
            vd.isRunning = false;
            vd.isAtDepot = true;
            vd.isReturningToDepot = false;
            vd.isGoingToDepotViaStation = false;
            vd.currentTile = new Vector2Int(vd.depotX, vd.depotZ);
            vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
            vd.activePath = null;
            vd.vehicleDistance = 0f;

            HideVehicle(vd);
            Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] dorazilo do DEPA – skryté.");
            return;
        }

        if (vd.isGoingToDepotViaStation)
        {
            bool onStation = vd.stations.Contains(vd.currentTile)
                             && GetRoadTileID(vd.currentTile.x, vd.currentTile.y) == 2;
            if (onStation)
            {
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{vd.currentTile.x},{vd.currentTile.y}] pred depom – čaká {STATION_WAIT}s.");
                vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
                vd.isWaiting = true;
                vd.waitTimer = STATION_WAIT;
                // Zastávka pred návratom do depa – obchod sa nerealizuje.
                vd.tradeDoneAtStation = true;
            }
            else
            {
                Debug.LogWarning($"[VehicleSystem] Vozidlo nedosiahlo stanicu pred depom. Zastane.");
                vd.isRunning = false;
                vd.isGoingToDepotViaStation = false;
            }
            return;
        }

        Vector2Int targetStation = vd.stations[vd.currentStationIndex];
        if (vd.currentTile == targetStation)
        {
            if (vd.pendingReturnToDepot)
            {
                vd.pendingReturnToDepot = false;
                vd.isGoingToDepotViaStation = true;
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{targetStation.x},{targetStation.y}] (pending návrat) – čaká {STATION_WAIT}s, potom depo.");
                // Posledná zastávka pred depom – obchod sa nerealizuje.
                vd.tradeDoneAtStation = true;
            }
            else
            {
                Debug.Log($"[VehicleSystem] Vozidlo dorazilo na stanicu [{targetStation.x},{targetStation.y}] – čaká {STATION_WAIT}s.");
                // Riadna zastávka na cieľovej stanici – po 2 s prebehne obchod.
                vd.tradeDoneAtStation = false;
            }
            vd.vehicleObject.transform.position = TileCenter(vd.currentTile);
            vd.isWaiting = true;
            vd.waitTimer = STATION_WAIT;
        }
        else
        {
            Debug.LogWarning($"[VehicleSystem] Vozidlo nedosiahlo stanicu [{targetStation.x},{targetStation.y}]. Čaká {BREAK_WAIT}s.");
            vd.isWaiting = true;
            vd.waitTimer = BREAK_WAIT;
            // Núdzové prerušenie – nie je to zastávka na stanici, žiadny obchod.
            vd.tradeDoneAtStation = true;
        }
    }

    void HideVehicle(VehicleData vd)
    {
        if (vd.vehicleBody != null) vd.vehicleBody.SetActive(false);
    }

    // =====================================================================
    // OBCHOD NA STANICI – TRADE (VehicleTradeSystem)
    // =====================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi vozidlom a továrňami stanice, na ktorej
    /// vozidlo práve čaká. Volá sa z UpdateVehicle po 2 s čakania
    /// (TRADE_DELAY), práve raz za zastávku.
    ///
    /// Postup (analógia k TrainSystem.TryExecuteTradeAtStation):
    ///   1) Zisti tile, na ktorom vozidlo stojí (cieľová stanica).
    ///   2) Nájdi StationInstance pre tento tile v RoadStationRegistry.
    ///      Ak ešte nie je zaregistrovaná (napr. stanica pribudla bez
    ///      následného scanu), dorovná sa cez RescanAll.
    ///   3) Odovzdaj vozidlo + stanicu do VehicleTradeSystem.Execute.
    ///   4) Podľa výsledku vypíš Debug.Log (úspech / neúspech).
    ///
    /// Výpisy presne zodpovedajú zadaniu:
    ///   úspech  → "Predaj prebehol úspešne, cena {N} euro"
    ///   neúspech→ "Obchod neprebehol."
    /// </summary>
    void TryExecuteTradeAtStation(VehicleData vd)
    {
        // Bezpečnostné kontroly – bez dátovej štruktúry vozidla nemá
        // obchod zmysel.
        if (vd == null || vd.vehicleInstance == null)
        {
            Debug.Log("Obchod neprebehol.");
            return;
        }

        Vector2Int stationTile = vd.currentTile;

        // Nájdi cestnú stanicu v registri. Ak chýba, skús dorovnať register.
        StationInstance station = RoadStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        if (station == null)
        {
            RoadStationRegistry.RescanAll();
            station = RoadStationRegistry.GetStationAt(stationTile.x, stationTile.y);
        }

        if (station == null)
        {
            Debug.Log("Obchod neprebehol.");
            Debug.LogWarning($"[VehicleSystem] Pre tile [{stationTile.x},{stationTile.y}] " +
                             $"sa nenašla cestná StationInstance – obchod sa neuskutočnil.");
            return;
        }

        // Vykonaj samotnú transakciu (čisto dátová operácia).
        VehicleTradeSystem.TradeResult result =
            VehicleTradeSystem.Execute(vd.vehicleInstance, station);

        if (result.Success)
        {
            // Pripíš zárobok na herné konto – preprava tovaru je jediný zdroj
            // príjmu, ktorý drží konto v pluse (a umožní reset cenového
            // násobiteľa po prepadnutí do mínusu).
            if (result.Revenue > 0)
            {
                if (GameEconomy.instance != null)
                    GameEconomy.instance.AddCredits((uint)result.Revenue);

                // EVIDENCIA pre ročnú uzávierku – tržba z úspešnej prepravy
                // (jediný zdroj príjmu hry, rovnako pre vlaky aj vozidlá).
                BudgetSystem.instance?.RecordRevenue(result.Revenue);
            }

            Debug.Log($"Obchodná transakcia bola vykonaná úspešne, suma {result.Revenue} CR.");
            Debug.Log($"[VehicleSystem] Obchod na stanici [{stationTile.x},{stationTile.y}]: " +
                      $"naložené {result.LoadedUnits}, vyložené {result.UnloadedUnits}, " +
                      $"zárobok {result.Revenue} CR.");

            // Plávajúci cenový label nad vozidlom na 3 s. Volá sa presne tu –
            // teda po 2 s od príchodu na stanicu (keď prebehne transakcia).
            if (vd.vehicleBody != null)
                SalePriceLabelManager.Instance.ShowPrice(
                    vd.vehicleBody.transform.position, result.Revenue);
        }
        else
        {
            Debug.Log("Obchod neprebehol.");
            Debug.Log($"[VehicleSystem] Obchod na stanici [{stationTile.x},{stationTile.y}] " +
                      $"neprebehol – dôvod: {result.Reason}.");
        }
    }

    // =====================================================================
    // ADVANCE STATION – OBRAT SMERU
    // =====================================================================

    void AdvanceStation(VehicleData vd)
    {
        if (!vd.reverseDirection)
        {
            vd.currentStationIndex++;
            if (vd.currentStationIndex >= vd.stations.Count)
            {
                vd.reverseDirection = true;
                vd.currentStationIndex = vd.stations.Count - 2;
                if (vd.currentStationIndex < 0) vd.currentStationIndex = 0;
            }
        }
        else
        {
            vd.currentStationIndex--;
            if (vd.currentStationIndex < 0)
            {
                vd.reverseDirection = false;
                vd.currentStationIndex = 1;
                if (vd.currentStationIndex >= vd.stations.Count)
                    vd.currentStationIndex = 0;
            }
        }

        ComputeNextPathAsync(vd);
    }

    // =====================================================================
    // ASYNC A* WRAPPER
    // =====================================================================

    void ComputeNextPathAsync(VehicleData vd)
    {
        if (vd.stations.Count == 0) return;
        if (vd.isComputingPath) return;

        Vector2Int goal = vd.stations[vd.currentStationIndex];
        Vector2Int start = vd.currentTile;
        int key = DepotKey(vd.depotX, vd.depotZ);
        var snap = SnapshotTileGrid();

        vd.isComputingPath = true;

        Task.Run(() =>
        {
            var path = AStarPathThreaded(snap, start, goal);
            _pendingPathResults.Enqueue(new PathResult
            {
                depotKey = key,
                path = path,
                isReturnToDepot = false
            });
        });
    }

    // =====================================================================
    // TRIGGER MAP CHANGED REROUTE
    // =====================================================================

    void TriggerMapChangedReroute()
    {
        // Mapa sa zmenila (pribudli/ubudli cesty, stanice alebo továrne) –
        // prepočítaj 9×9 zóny cestných staníc a ich evidenciu tovární.
        // Lacná operácia (analógia k TrainSystem.TriggerMapChangedReroute,
        // ktorý volá StationRegistry.RescanAll()).
        RoadStationRegistry.RescanAll();

        var snap = SnapshotTileGrid();

        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            if (!vd.isRunning) continue;
            if (vd.isComputingPath) continue;

            int key = kvp.Key;

            if (vd.isReturningToDepot)
            {
                Vector2Int start = vd.currentTile;
                Vector2Int goal = new Vector2Int(vd.depotX, vd.depotZ);
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = true,
                        destination = PathDestination.Depot
                    });
                });
            }
            else if (vd.isGoingToDepotViaStation)
            {
                Vector2Int? nearest = FindNearestStation(vd);
                if (!nearest.HasValue) continue;
                Vector2Int start = vd.currentTile;
                Vector2Int goal = nearest.Value;
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.ReturnViaStation
                    });
                });
            }
            else
            {
                if (vd.stations.Count == 0) continue;
                Vector2Int start = vd.currentTile;
                Vector2Int goal = vd.stations[vd.currentStationIndex];
                vd.isComputingPath = true;
                Task.Run(() =>
                {
                    var path = AStarPathThreaded(snap, start, goal);
                    _pendingPathResults.Enqueue(new PathResult
                    {
                        depotKey = key,
                        path = path,
                        isReturnToDepot = false,
                        destination = PathDestination.Station
                    });
                });
            }
        }
    }

    Vector2Int? FindNearestStation(VehicleData vd)
    {
        Vector2Int? nearest = null;
        int bestDist = int.MaxValue;
        foreach (var st in vd.stations)
        {
            if (GetRoadTileID(st.x, st.y) != 2) continue;
            int dist = Math.Abs(vd.currentTile.x - st.x) + Math.Abs(vd.currentTile.y - st.y);
            if (dist < bestDist) { bestDist = dist; nearest = st; }
        }
        return nearest;
    }

    // =====================================================================
    // APLIKÁCIA VÝSLEDKOV A*
    // =====================================================================

    void ApplyPendingPathResults()
    {
        while (_pendingPathResults.TryDequeue(out PathResult result))
        {
            if (!vehicles.TryGetValue(result.depotKey, out VehicleData vd)) continue;

            vd.isComputingPath = false;
            bool hasPath = result.path != null && result.path.Count > 0;

            if (result.destination == PathDestination.Depot)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    vd.isRunning = false; vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] nemôže nájsť cestu do depa, čaká.");
                }
                continue;
            }

            if (result.destination == PathDestination.ReturnViaStation)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    Debug.LogWarning($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}] nemôže nájsť cestu na stanicu. Zastane.");
                    vd.isRunning = false; vd.isWaiting = false; vd.isGoingToDepotViaStation = false;
                }
                continue;
            }

            if (result.isReturnToDepot)
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    vd.isRunning = false; vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                    Debug.LogWarning($"[VehicleSystem] Vozidlo nemôže nájsť cestu do depa, čaká.");
                }
            }
            else
            {
                if (hasPath) { ApplyNewPath(vd, result.path); vd.isWaiting = false; }
                else
                {
                    bool anyStationExists = false;
                    foreach (var st in vd.stations)
                        if (GetRoadTileID(st.x, st.y) == 2) { anyStationExists = true; break; }

                    if (!anyStationExists)
                    {
                        Debug.Log($"[VehicleSystem] Vozidlo z depa [{vd.depotX},{vd.depotZ}]: žiadne dostupné stanice – vozidlo zastane. Použite Send To Depot pre návrat.");
                        vd.isRunning = false; vd.isWaiting = false;
                        vd.isStoppedAwaitingDepotReturn = true;
                        vd.currentPath = new List<Vector2Int>(); vd.pathIndex = 0;
                        vd.activePath = null;

                        if (vd.currentTile.x == vd.depotX && vd.currentTile.y == vd.depotZ)
                            vd.isAtDepot = true;
                    }
                    else
                    {
                        if (vd.stations.Count > 0)
                        {
                            var goal = vd.stations[vd.currentStationIndex];
                            Debug.LogWarning($"[VehicleSystem] A* nenašiel cestu do [{goal.x},{goal.y}]. Čakám {BREAK_WAIT}s.");
                        }
                        vd.isWaiting = true; vd.waitTimer = BREAK_WAIT;
                        vd.currentPath = new List<Vector2Int>(); vd.pathIndex = 0;
                        vd.activePath = null;

                        if (vd.currentTile.x == vd.depotX && vd.currentTile.y == vd.depotZ)
                            vd.isAtDepot = true;
                    }
                }
            }
        }
    }

    // =====================================================================
    // APPLY NEW PATH – konvertuje List<Vector2Int> na PathData (waypoint-based)
    // =====================================================================

    void ApplyNewPath(VehicleData vd, List<Vector2Int> newPath)
    {
        bool isMoving = vd.activePath != null && vd.vehicleDistance < vd.activePath.totalLength;

        Vector2Int startTile = vd.currentTile;
        int startIdxInNew = -1;
        for (int i = 0; i < newPath.Count; i++)
        {
            if (newPath[i] == startTile) { startIdxInNew = i; break; }
        }

        if (startIdxInNew < 0)
        {
            if (isMoving)
            {
                vd.pendingPath = newPath;
                return;
            }
            startIdxInNew = 0;
        }

        List<Vector2Int> subPath = newPath.GetRange(startIdxInNew, newPath.Count - startIdxInNew);

        vd.currentPath = newPath;
        vd.pathIndex = startIdxInNew;
        vd.pathIndexAtPathStart = startIdxInNew;
        vd.moveTimer = 0f;

        BuildWaypointPath(subPath, out var pts, out var tileIdxList);
        var newPathData = new PathData(pts);

        float expectedTime = (subPath.Count - 1) * MOVE_TIME;
        vd.vehicleSpeed = (newPathData.totalLength > 0f && expectedTime > 0f)
            ? newPathData.totalLength / expectedTime
            : 1f / MOVE_TIME;

        vd.vehicleDistance = 0f;
        vd.activePath = newPathData;
        vd.waypointToTileIdx = tileIdxList.ToArray();
    }

    void RebuildActivePathFromCurrentPath(VehicleData vd)
    {
        if (vd.currentPath == null || vd.currentPath.Count == 0)
        {
            vd.activePath = null;
            return;
        }

        int startIdx = vd.pathIndex;
        if (startIdx >= vd.currentPath.Count) { vd.activePath = null; return; }

        List<Vector2Int> sub = vd.currentPath.GetRange(startIdx, vd.currentPath.Count - startIdx);

        BuildWaypointPath(sub, out var pts, out var tileIdxList);
        var pd = new PathData(pts);

        vd.pathIndexAtPathStart = startIdx;
        float expectedTime = (sub.Count - 1) * MOVE_TIME;
        vd.vehicleSpeed = (pd.totalLength > 0f && expectedTime > 0f)
            ? pd.totalLength / expectedTime
            : 1f / MOVE_TIME;
        vd.vehicleDistance = 0f;
        vd.activePath = pd;
        vd.waypointToTileIdx = tileIdxList.ToArray();
    }

    // =====================================================================
    // A* PATHFINDING – THREAD-SAFE
    // =====================================================================

    class AStarNode
    {
        public Vector2Int pos;
        public AStarNode parent;
        public float g, h;
        public float f => g + h;

        public AStarNode(Vector2Int p, AStarNode par, float g, float h)
        {
            pos = p; parent = par; this.g = g; this.h = h;
        }
    }

    static readonly Vector2Int[] DIRECTIONS =
    {
        new Vector2Int( 1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int( 0, 1),
        new Vector2Int( 0,-1)
    };

    static List<Vector2Int> AStarPathThreaded(TileGridSnapshot snap, Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return new List<Vector2Int>();

        var open = new List<AStarNode>();
        var closed = new HashSet<Vector2Int>();
        var nodeMap = new Dictionary<Vector2Int, AStarNode>();

        open.Add(new AStarNode(start, null, 0f, Heuristic(start, goal)));
        nodeMap[start] = open[0];

        int iterations = 0;
        const int MAX_ITER = 10000;

        while (open.Count > 0 && iterations < MAX_ITER)
        {
            iterations++;
            int bestIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bestIdx].f) bestIdx = i;

            AStarNode current = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (current.pos == goal)
                return ReconstructPath(current);

            closed.Add(current.pos);

            foreach (var dir in DIRECTIONS)
            {
                Vector2Int neighbor = current.pos + dir;
                if (closed.Contains(neighbor)) continue;

                IndicatrixAPI.DirectionMask dirMask = DirFromStep(dir);
                if (!CanMove(snap.tileIDs, snap.connections, current.pos, neighbor, dirMask))
                    continue;

                float newG = current.g + 1f;
                if (nodeMap.TryGetValue(neighbor, out AStarNode existing))
                {
                    if (newG < existing.g) { existing.g = newG; existing.parent = current; }
                }
                else
                {
                    var node = new AStarNode(neighbor, current, newG, Heuristic(neighbor, goal));
                    open.Add(node);
                    nodeMap[neighbor] = node;
                }
            }
        }

        return null;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
        => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    static List<Vector2Int> ReconstructPath(AStarNode node)
    {
        var path = new List<Vector2Int>();
        var cur = node;
        while (cur.parent != null) { path.Add(cur.pos); cur = cur.parent; }
        path.Reverse();
        return path;
    }

    public void WriteSave(System.IO.BinaryWriter bw)
    {
        bw.Write(vehicles.Count);
        foreach (var kvp in vehicles)
        {
            VehicleData vd = kvp.Value;
            var inst = vd.vehicleInstance;

            bw.Write(vd.depotX);
            bw.Write(vd.depotZ);

            // Typ vozidla ukladáme ako Name (CreateVehicle berie názov typu).
            bw.Write(inst != null ? inst.Spec.Name : "");

            bw.Write(inst != null ? inst.Age : 0);
            bw.Write(inst != null ? inst.CurrentCapacity : 0);

            bw.Write(vd.stations != null ? vd.stations.Count : 0);
            if (vd.stations != null)
                foreach (var s in vd.stations) { bw.Write(s.x); bw.Write(s.y); }

            bw.Write(vd.isRunning);
        }
    }

    public void ReadSave(System.IO.BinaryReader br)
    {
        int count = br.ReadInt32();
        for (int k = 0; k < count; k++)
        {
            int dx = br.ReadInt32();
            int dz = br.ReadInt32();
            string typeName = br.ReadString();
            int age = br.ReadInt32();
            int cargo = br.ReadInt32();

            int stationCnt = br.ReadInt32();
            var stations = new System.Collections.Generic.List<Vector2Int>(stationCnt);
            for (int i = 0; i < stationCnt; i++)
                stations.Add(new Vector2Int(br.ReadInt32(), br.ReadInt32()));

            bool wasRunning = br.ReadBoolean();

            if (!CreateVehicle(dx, dz, typeName))
                continue;

            VehicleData vd = GetVehicle(dx, dz);
            if (vd == null) continue;

            if (vd.vehicleInstance != null)
            {
                vd.vehicleInstance.Age = age;
                vd.vehicleInstance.CurrentCapacity = cargo;
            }

            foreach (var s in stations)
                AddStation(dx, dz, s.x, s.y);

            if (wasRunning)
                StartVehicle(dx, dz);
        }
    }

}
```


### VehicleTradeSystem.cs

```csharp
using UnityEngine;
using Game.VehicleStock;

/// <summary>
/// VehicleTradeSystem.cs
/// ------------------------------------------------------------------
/// TRANSAKČNÝ ALGORITMUS PRE CESTNÉ VOZIDLÁ – výmena tovaru medzi
/// vozidlom a továrňami stanice.
///
/// Toto je CESTNÝ EKVIVALENT triedy TradeSystem (ktorá rieši obchod pre
/// VLAKY). Princíp obchodu je medzi vlakmi a vozidlami ROVNAKÝ – kontroluje
/// sa len surovina a množstvo – líši sa iba dátová štruktúra "dopravcu":
///
///     VLAK    = TrainInstance + List&lt;WagonInstance&gt;  (náklad nesú vagóny)
///     VOZIDLO = VehicleInstance                       (JEDEN kus, žiadne vagóny)
///
/// Vozidlo sa správa "akoby jeden vagón": má MaximumCapacity a premenlivý
/// CurrentCapacity (VehicleStock.cs). Preto je cestná transakcia jednoduchšia
/// – tam, kde TradeSystem prechádza vagón po vagóne (FillWagons / UnloadWagons),
/// tu pracujeme s jediným kusom (FillVehicle / UnloadVehicle).
///
/// ------------------------------------------------------------------
/// AKO TO FUNGUJE – KONCEPT (zhodný s TradeSystem)
///
/// Vozidlo má typ (VehicleType), ktorý nesie ResourceType cez ČÍSELNÉ ID:
///     VehicleType.Id == (int)ResourceType
///     ("Coal Truck" #1 → ResourceType.Coal,  "Wood Truck" #2 → Wood)
/// Mapovanie je 1:1 – presne ako WagonType.Id v TradeSystem.
///
/// Stanica (StationInstance, evidovaná v RoadStationRegistry) eviduje
/// 0..N tovární vo svojej 9×9 zóne. Každá továreň má pre danú surovinu buď
/// VÝDAJOVÝ slot (UnLoad – surovinu produkuje) alebo PRÍJMOVÝ slot
/// (Load – surovinu spotrebúva).
///
/// Keď vozidlo zastane na stanici, pre svoj typ suroviny urobí:
///
///   KROK 1 – VYLOŽENIE (Unload):
///     Ak je v zóne stanice továreň, ktorá danú surovinu PRIJÍMA (Load slot),
///     a vo vozidle niečo je → vyloží náklad do tej továrne.
///
///   KROK 2 – NALOŽENIE (Load):
///     Ak je v zóne stanice továreň, ktorá danú surovinu VYDÁVA (UnLoad slot),
///     a vo vozidle je voľné miesto → naloží z tej továrne do vozidla.
///
/// Obchod je ÚSPEŠNÝ ("Predaj prebehol") IBA vtedy, keď sa pri zastávke
/// SKUTOČNE VYLOŽILA aspoň 1 jednotka tovaru do príjmovej továrne.
/// Naloženie surovín do vozidla NEROBÍ obchod úspešným – je to len presun,
/// príprava na predaj na ďalšej stanici (rovnaké pravidlo ako pri vlakoch).
/// ------------------------------------------------------------------
/// </summary>
public static class VehicleTradeSystem
{
    // Cena predaja sa už NEráta paušálne za jednotku. Kompletný cenník (cena
    // ZA PLNÉ VOZIDLO podľa suroviny + alikvotná časť pri neúplnom vozidle +
    // dynamický násobiteľ pri zápornom konte) je centralizovaný v
    // ResourcePricing.RoadSaleRevenue(...). Pozri krok 4 v Execute(...).

    // ==================================================================
    // VÝSLEDOK TRANSAKCIE
    // ==================================================================

    /// <summary>
    /// Výsledok jednej zastávky vozidla na stanici. VehicleSystem si z neho
    /// prečíta Success + Revenue a vypíše príslušný Debug.Log.
    /// (Štruktúra zhodná s TradeSystem.TradeResult.)
    /// </summary>
    public struct TradeResult
    {
        /// <summary>True, ak sa vyložila (predala) aspoň 1 jednotka tovaru.</summary>
        public bool Success;

        /// <summary>Koľko jednotiek sa naložilo z továrne do vozidla.</summary>
        public int LoadedUnits;

        /// <summary>Koľko jednotiek sa vyložilo z vozidla do továrne.</summary>
        public int UnloadedUnits;

        /// <summary>Celkový výnos z obchodu (euro).</summary>
        public int Revenue;

        /// <summary>Stručný textový dôvod (najmä pri neúspechu) – na debug.</summary>
        public string Reason;

        /// <summary>Celkový počet presunutých jednotiek (load + unload).</summary>
        public int MovedUnits => LoadedUnits + UnloadedUnits;
    }

    // ==================================================================
    // HLAVNÝ VSTUP – vykonaj transakciu pre vozidlo na stanici
    // ==================================================================

    /// <summary>
    /// Vykoná výmenu tovaru medzi vozidlom <paramref name="vehicle"/> a
    /// továrňami v 9×9 zóne stanice <paramref name="station"/>.
    ///
    /// Postup (analogický k TradeSystem.Execute):
    ///   0) Validácia (vozidlo existuje, stanica eviduje aspoň 1 továreň).
    ///   1) Zisti surovinu, s ktorou vozidlo pracuje (z typu vozidla).
    ///   2) UNLOAD – ak je v zóne príjmová továreň, vyloží náklad do nej.
    ///   3) LOAD   – ak je v zóne výdajová továreň, naloží z nej do vozidla.
    ///   4) Vyhodnoť úspech a vyčísli cenu.
    ///
    /// Metóda je čisto dátová (žiadne Unity GameObject API), takže sa dá
    /// volať z hlavného vlákna v Update slučke VehicleSystem-u bez rizika.
    /// </summary>
    public static TradeResult Execute(VehicleInstance vehicle, StationInstance station)
    {
        var result = new TradeResult
        {
            Success = false,
            LoadedUnits = 0,
            UnloadedUnits = 0,
            Revenue = 0,
            Reason = ""
        };

        // ---- 0) VALIDÁCIA -------------------------------------------------
        if (vehicle == null)
        {
            result.Reason = "vozidlo nemá dátovú štruktúru (VehicleInstance == null)";
            return result;
        }
        if (station == null)
        {
            result.Reason = "neznáma stanica";
            return result;
        }
        if (!station.HasFactories)
        {
            result.Reason = "stanica neeviduje žiadnu továreň v 9×9 zóne";
            return result;
        }

        // ---- 1) SUROVINA VOZIDLA -----------------------------------------
        ResourceType vehicleResource = VehicleResource(vehicle);
        if (vehicleResource == ResourceType.None)
        {
            result.Reason = $"typ vozidla '{vehicle.Type}' nemá priradenú surovinu";
            return result;
        }

        // ---- 2) UNLOAD (vyloženie do príjmovej továrne) -------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // PRIJÍMA. Pri čisto výdajovej stanici sa tento krok preskočí –
        // tým je ošetrený "bonus": vozidlo môže prejsť cez výdajovú stanicu
        // a náklad mu zostane.
        FactoryInstance consumer = station.FindConsumer(vehicleResource);
        if (consumer != null)
        {
            ResourceSlot loadSlot = consumer.GetLoadSlot(vehicleResource);
            result.UnloadedUnits = UnloadVehicle(vehicle, loadSlot);
            if (result.UnloadedUnits > 0)
                Debug.Log($"[VehicleTradeSystem] Vyložené {result.UnloadedUnits}× {vehicleResource} " +
                          $"do '{consumer.Name}' (slot {loadSlot}).");
        }

        // ---- 3) LOAD (naloženie z výdajovej továrne) ----------------------
        // Vykoná sa len ak je v zóne stanice továreň, ktorá danú surovinu
        // VYDÁVA. FillVehicle dopĺňa do VOĽNÉHO miesta, takže existujúci
        // náklad (z inej stanice) zostáva a nová surovina sa k nemu pripočíta.
        FactoryInstance supplier = station.FindSupplier(vehicleResource);
        if (supplier != null)
        {
            ResourceSlot unloadSlot = supplier.GetUnLoadSlot(vehicleResource);
            result.LoadedUnits = FillVehicle(vehicle, unloadSlot);
            if (result.LoadedUnits > 0)
                Debug.Log($"[VehicleTradeSystem] Naložené {result.LoadedUnits}× {vehicleResource} " +
                          $"z '{supplier.Name}' (slot {unloadSlot}).");
        }

        // ---- 4) VYHODNOTENIE ---------------------------------------------
        // DÔLEŽITÉ: peniaze plynú IBA z VYLOŽENIA (predaja tovaru továrni).
        // Naloženie surovín z výdajovej továrne do vozidla je len presun –
        // NEGENERUJE žiadny výnos a samo o sebe nerobí obchod úspešným.
        result.Success = result.UnloadedUnits > 0;

        if (result.Success)
        {
            // Cena ZA PLNÉ VOZIDLO podľa suroviny, alikvotne podľa reálne
            // predaného (vyloženého) množstva, prenásobená aktuálnym cenovým
            // násobiteľom konta. Menovateľom alikvotného prepočtu je
            // MaximumCapacity vozidla.
            result.Revenue = ResourcePricing.RoadSaleRevenue(
                vehicleResource, result.UnloadedUnits, vehicle.MaximumCapacity);
        }
        else
        {
            // Žiadny PREDAJ (vyloženie) – upresni dôvod pre debug.
            if (consumer == null)
            {
                if (result.LoadedUnits > 0)
                    result.Reason = $"naložené {result.LoadedUnits}× {vehicleResource}, " +
                                    $"ale stanica nemá príjmovú továreň – predaj sa nekoná";
                else if (supplier == null)
                    result.Reason = $"v zóne stanice nie je továreň pre surovinu {vehicleResource}";
                else
                    result.Reason = "stanica nemá príjmovú továreň – nie je kam predať";
            }
            else
            {
                result.Reason = "vozidlo prázdne alebo príjmová továreň plná – nič sa nevyložilo";
            }
        }

        return result;
    }

    // ==================================================================
    // POMOCNÉ – mapovanie typu vozidla na surovinu
    // ==================================================================

    /// <summary>
    /// Vráti surovinu, s ktorou vie dané vozidlo pracovať.
    ///
    /// Mapovanie je cez ČÍSELNÉ ID: VehicleType.Id == (int)ResourceType.
    /// Katalógy sú zámerne zladené (rovnako ako pri vlakoch):
    ///     "Coal Truck" má Id 1  →  ResourceType.Coal == 1
    ///     "Wood Truck" má Id 2  →  ResourceType.Wood == 2
    ///
    /// Ak by v budúcnosti ID nesedeli, stačí toto mapovanie nahradiť
    /// explicitnou tabuľkou (Dictionary&lt;int, ResourceType&gt;).
    /// </summary>
    public static ResourceType VehicleResource(VehicleInstance vehicle)
    {
        if (vehicle == null) return ResourceType.None;

        int id = vehicle.Type.Id;
        if (System.Enum.IsDefined(typeof(ResourceType), id))
            return (ResourceType)id;

        return ResourceType.None;
    }

    // ==================================================================
    // POMOCNÉ – LOAD: naplnenie vozidla z výdajového slotu továrne
    // ==================================================================

    /// <summary>
    /// Naplnenie vozidla surovinou zo skladu továrne
    /// (<paramref name="source"/> = UnLoad slot).
    ///
    /// ALGORITMUS:
    ///   Vozidlo je JEDEN kus – naplní sa do svojej maximálnej kapacity,
    ///   pokým je v továrni surovina k dispozícii. Naloží sa
    ///   min(voľné miesto vo vozidle, dostupné v továrni).
    ///
    ///   (Toto je zjednodušená verzia TradeSystem.FillWagons, ktorý
    ///   prechádza vagón po vagóne – vozidlo má len jeden "vagón".)
    ///
    /// Rešpektuje BONUS: ak vo vozidle už nejaká surovina je (z inej
    /// stanice), dopĺňa sa len jeho VOĽNÉ miesto – existujúce množstvo
    /// sa nemaže.
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE naložilo (a odpočítalo z továrne).
    /// </summary>
    public static int FillVehicle(VehicleInstance vehicle, ResourceSlot source)
    {
        if (vehicle == null || source == null) return 0;
        if (source.IsEmpty) return 0;

        // Voľné miesto vo vozidle.
        int free = vehicle.MaximumCapacity - vehicle.CurrentCapacity;
        if (free <= 0) return 0;            // vozidlo plné

        // Do vozidla ide min(voľné miesto, dostupné v továrni).
        int toLoad = Mathf.Min(free, source.amount);
        if (toLoad <= 0) return 0;

        vehicle.CurrentCapacity += toLoad;

        // Odpočítaj naloženú surovinu zo skladu továrne.
        // ResourceSlot.Remove je orezané dostupným množstvom (bezpečné).
        source.Remove(toLoad);

        return toLoad;
    }

    // ==================================================================
    // POMOCNÉ – UNLOAD: vyloženie vozidla do príjmového slotu továrne
    // ==================================================================

    /// <summary>
    /// Vyloženie nákladu vozidla do skladu továrne
    /// (<paramref name="target"/> = Load slot).
    ///
    /// ALGORITMUS:
    ///   Náklad vozidla sa presunie do príjmového slotu továrne. Slot má
    ///   kapacitu – ak by sa nezmestilo všetko, presunie sa len toľko,
    ///   koľko je v slote voľné, a zvyšok ZOSTANE vo vozidle (odvezie ho ďalej).
    ///
    ///   (Toto je zjednodušená verzia TradeSystem.UnloadWagons – vozidlo
    ///   má len jeden "vagón".)
    ///
    /// Vráti, koľko jednotiek sa SKUTOČNE vyložilo (a pripočítalo k továrni).
    /// </summary>
    public static int UnloadVehicle(VehicleInstance vehicle, ResourceSlot target)
    {
        if (vehicle == null || target == null) return 0;
        if (vehicle.CurrentCapacity <= 0) return 0;     // prázdne vozidlo

        // Voľné miesto v príjmovom slote továrne.
        int free = target.capacity - target.amount;
        if (free <= 0) return 0;                        // továreň plná

        // Z vozidla presunieme min(jeho náklad, voľné miesto v továrni).
        int toUnload = Mathf.Min(vehicle.CurrentCapacity, free);
        if (toUnload <= 0) return 0;

        vehicle.CurrentCapacity -= toUnload;

        // Pripočítaj vyloženú surovinu do skladu továrne.
        // ResourceSlot.Add je orezané kapacitou (bezpečné).
        target.Add(toUnload);

        return toUnload;
    }
}
```
