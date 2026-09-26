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

        // Dáta pre shadery (výška + vzdialenosť od vody) – prepočítajú sa raz
        // v LateUpdate, aj keď sa v tom istom frame prestaví viac elementov.
        TerrainSurfaceData.MarkDirty();
    }


    public void Rebuild()
    {
        mesh.Clear();
        BuildMesh();
    }



}
