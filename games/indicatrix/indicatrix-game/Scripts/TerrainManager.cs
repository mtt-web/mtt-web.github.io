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


    // =====================================================================
    // OCHRANNÝ PÁS PRI OKRAJI MAPY
    //
    // Vrcholy na vonkajšom obvode mriežky (index 0 alebo terrainWidth v osi
    // X/Z) tvoria hranicu mesh-u. Ich posun by roztrhol obvod mapy a kaskáda
    // TerrainCollapse by nemala kam pokračovať za hranicou, preto sú pre
    // editor terénu (LevelUp/LevelDown) zamknuté.
    //
    // ProtectedBorderRings = počet zamknutých "prstencov" vrcholov od kraja:
    //   1 → len posledný (vonkajší) rad vrcholov,
    //   2 → posledné dva rady, atď.
    // =====================================================================

    public const int ProtectedBorderRings = 1;


    /// <summary>
    /// Vráti true, ak vrchol so súradnicami (snapX, snapZ) leží v chránenom
    /// páse pri okraji mapy, a teda sa nesmie výškovo upravovať.
    ///
    /// Súradnice sú tie isté, aké vracia IndicatrixAPI.SnapVertex – celé čísla
    /// v rozsahu 0..terrainWidth (zaokrúhlenie je len poistka proti float
    /// nepresnosti).
    /// </summary>
    public bool IsProtectedBorderVertex(float snapX, float snapZ)
    {
        int vx = Mathf.RoundToInt(snapX);
        int vz = Mathf.RoundToInt(snapZ);

        int ring = Mathf.Max(1, ProtectedBorderRings);

        return vx <= ring - 1
            || vz <= ring - 1
            || vx >= terrainWidth - (ring - 1)
            || vz >= terrainWidth - (ring - 1);
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
