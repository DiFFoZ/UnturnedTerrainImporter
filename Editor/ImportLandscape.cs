/*
    @DiFFoZ/UnturnedTerrainImporter
    https://github.com/DiFFoZ/UnturnedTerrainImporter

    MIT License

    Copyright (c) 2024 Leonid
    
    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:
    
    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
    
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
*/

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ImportLandscape : EditorWindow
{
    private const float c_TileSize = 1024f;
    private const float c_TileHeight = 2048f;
    private const int c_HeightmapResolution = 257;

    private static readonly Vector3 s_TileSize = new(c_TileSize, c_TileHeight, c_TileSize);

    private string m_MapPath;
    private Material m_Material;

    [MenuItem("Window/Unturned/Terrain Importer/Open Window")]
    private static void Open()
    {
        var window = GetWindow<ImportLandscape>();
        window.titleContent = new GUIContent("Unturned Terrain Importer");
        window.Focus();
    }

    private void OnGUI()
    {
        GUILayout.BeginVertical();

        if (GUILayout.Button(new GUIContent("Select map", tooltip: m_MapPath)))
        {
            m_MapPath = EditorUtility.OpenFolderPanel("Map", m_MapPath, string.Empty);
        }

        bool mapValid = !string.IsNullOrEmpty(m_MapPath);
        if (mapValid)
        {
            GUILayout.Box("Selected: " + m_MapPath);

            if (!Directory.Exists(m_MapPath))
            {
                mapValid = false;
                EditorGUILayout.HelpBox("Path does not exists!", MessageType.Error);
            }

            var levelFilePath = Path.Combine(m_MapPath, "Level.dat");
            if (!File.Exists(levelFilePath))
            {
                mapValid = false;
                EditorGUILayout.HelpBox("Path does not contain Level.dat!", MessageType.Error);
            }
        }

        GUILayout.Space(10);

        m_Material = EditorGUILayout.ObjectField("Select Material", m_Material, typeof(Material), false) as Material;

        if (m_Material != null && m_Material.GetTag("TerrainCompatible", false)?.Equals("true") == false)
        {
            EditorGUILayout.HelpBox("The provided Material's shader might be unsuitable for use with Terrain. Recommended to use a shader from Nature/Terrain instead.", MessageType.Warning);
        }

        GUILayout.Space(10);

        var hasMaterial = m_Material != null;
        GUI.enabled = mapValid && hasMaterial;

        if (GUILayout.Button("Import map"))
        {
            var assetFolder = EditorUtility.SaveFolderPanel("Save Landscape Folder", Application.dataPath, "Landscapes");

            if (assetFolder.Length == 0)
            {
                return;
            }

            assetFolder = FileUtil.GetProjectRelativePath(assetFolder);

            var prefab = new GameObject();
            prefab.name = new DirectoryInfo(m_MapPath).Name;

            Import(prefab, assetFolder);

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefab, Path.Combine(assetFolder, $"{prefab.name}.prefab"));
            DestroyImmediate(prefab);

            if (Selection.count == 0)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeGameObject = prefabAsset;
            }
        }

        GUI.enabled = mapValid;
        if (GUILayout.Button("Export as .obj file"))
        {
            var objFile = EditorUtility.SaveFilePanel("Saving .OBJ file", Application.dataPath, "landscape", "obj");

            if (objFile.Length == 0)
            {
                return;
            }

            var prefab = new GameObject();

            Import(prefab, null);
            ExportTerrainToObjFile(prefab, objFile);

            DestroyImmediate(prefab);
        }

        GUILayout.EndVertical();
    }

    private void ExportTerrainToObjFile(GameObject gameObject, string filePath)
    {
        const int HeightmapResolutionMinusOne = c_HeightmapResolution - 1;

        var childCount = gameObject.transform.childCount;
        var vertexCount = c_HeightmapResolution * c_HeightmapResolution;
        var meshScale = new Vector3(s_TileSize.x / HeightmapResolutionMinusOne, s_TileSize.y, s_TileSize.z / HeightmapResolutionMinusOne);
        var rotation = Quaternion.AngleAxis(0, Vector3.up);

        var triangles = new int[HeightmapResolutionMinusOne * HeightmapResolutionMinusOne * 4];
        var index = 0;
        for (var y = 0; y < HeightmapResolutionMinusOne; y++)
        {
            for (var x = 0; x < HeightmapResolutionMinusOne; x++)
            {
                triangles[index++] = y * c_HeightmapResolution + x;
                triangles[index++] = y * c_HeightmapResolution + x + 1;
                triangles[index++] = (y + 1) * c_HeightmapResolution + x + 1;
                triangles[index++] = (y + 1) * c_HeightmapResolution + x;
            }
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var sw = new StreamWriter(fs, new UTF8Encoding(false));

        var previousX = gameObject.transform.GetChild(0).position.x;
        var offsetX = -previousX;

        for (var i = 0; i < childCount; i++)
        {
            var child = gameObject.transform.GetChild(i);
            var terrain = child.GetComponent<Terrain>();

            var position = child.localPosition;
            if (previousX != position.x)
            {
                previousX = position.x;
                offsetX -= 1024f;
            }
            position = new Vector3(offsetX, -1024f, position.z);

            var data = terrain.terrainData.GetHeights(0, 0, c_HeightmapResolution, c_HeightmapResolution);

            for (var x = 0; x < c_HeightmapResolution; x++)
            {
                for (var y = 0; y < c_HeightmapResolution; y++)
                {
                    var vertex = rotation * (Vector3.Scale(meshScale, new(-y, data[x, y], x)) + position);

                    sw.Write('v');
                    sw.Write(' ');

                    sw.Write(vertex.x.ToString(CultureInfo.InvariantCulture));
                    sw.Write(' ');
                    sw.Write(vertex.y.ToString(CultureInfo.InvariantCulture));
                    sw.Write(' ');
                    sw.WriteLine(vertex.z.ToString(CultureInfo.InvariantCulture));
                }
            }

            sw.WriteLine("g Tile_" + i);
            sw.WriteLine("s 0");

            var offset = 1 + vertexCount * i;
            for (var t = 0; t < triangles.Length; t += 4)
            {
                sw.Write('f');
                sw.Write(' ');

                sw.Write(triangles[t] + offset);
                sw.Write(' ');
                sw.Write(triangles[t + 1] + offset);
                sw.Write(' ');
                sw.Write(triangles[t + 2] + offset);
                sw.Write(' ');
                sw.WriteLine(triangles[t + 3] + offset);
            }
        }

        sw.Flush();
        fs.Flush();
    }

    private void Import(GameObject parent, string assetFolder)
    {
        int GetLevelSize()
        {
            using var fs = new FileStream(Path.Combine(m_MapPath, "Level.dat"), FileMode.Open, FileAccess.Read);
            fs.Seek(9, SeekOrigin.Begin); // skip version, steamid

            return fs.ReadByte();
        }

        var levelSize = GetLevelSize();

        var heightmapsFolder = Path.Combine(m_MapPath, "Landscape", "Heightmaps");
        var tempTiles = new Dictionary<Vector2Int, Terrain>();

        for (var x = -levelSize; x < levelSize; x++)
        {
            for (var y = -levelSize; y < levelSize; y++)
            {
                var coords = new Vector2Int(x, y);
                var heightmapFilePath = Path.Combine(heightmapsFolder,
                    $"Tile_{x.ToString(CultureInfo.InvariantCulture)}_{y.ToString(CultureInfo.InvariantCulture)}_Source.heightmap");

                if (!File.Exists(heightmapFilePath))
                {
                    continue;
                }

                var data = ReadData(heightmapFilePath, $"x: {coords.x} y: {coords.y}");

                if (assetFolder != null)
                {
                    var dataAssetPath = Path.Combine(assetFolder,
                        $"TerrainData_{coords.x.ToString(CultureInfo.InvariantCulture)}_{coords.y.ToString(CultureInfo.InvariantCulture)}.asset");
                    AssetDatabase.CreateAsset(data, AssetDatabase.GenerateUniqueAssetPath(dataAssetPath));
                }

                tempTiles[coords] = AddTileToPrefab(parent, data, coords);
            }
        }

        // links neighborhoods
        foreach (var terrainTile in tempTiles)
        {
            var coords = terrainTile.Key;

            tempTiles.TryGetValue(new(coords.x - 1, coords.y), out var terrain1);
            tempTiles.TryGetValue(new(coords.x + 1, coords.y), out var terrain2);
            tempTiles.TryGetValue(new(coords.x, coords.y + 1), out var terrain3);
            tempTiles.TryGetValue(new(coords.x, coords.y - 1), out var terrain4);

            terrainTile.Value.SetNeighbors(terrain1, terrain2, terrain3, terrain4);
        }
    }

    private TerrainData ReadData(string heightmapFilePath, string assetName)
    {
        var heightMap = new float[c_HeightmapResolution, c_HeightmapResolution];

        using var fs = new FileStream(heightmapFilePath, FileMode.Open, FileAccess.Read);
        for (var x = 0; x < c_HeightmapResolution; x++)
        {
            for (var y = 0; y < c_HeightmapResolution; y++)
            {
                heightMap[x, y] = (ushort)((fs.ReadByte() << 8) | fs.ReadByte()) / 65535f;
            }
        }

        var data = new TerrainData
        {
            heightmapResolution = c_HeightmapResolution,
            alphamapResolution = 256,
            baseMapResolution = 128,
            size = s_TileSize,
            wavingGrassTint = Color.white,
            name = assetName
        };
        data.SetHeightsDelayLOD(0, 0, heightMap);
        data.SyncHeightmap();

        return data;
    }

    private Terrain AddTileToPrefab(GameObject prefab, TerrainData data, Vector2Int coords)
    {
        var go = new GameObject();
        go.transform.parent = prefab.transform;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.transform.position = new(coords.x * c_TileSize, -c_TileHeight / 2f, coords.y * c_TileSize);
        go.name = $"Terrain ({coords.x} {coords.y})";

        var terrain = go.AddComponent<Terrain>();
        terrain.terrainData = data;
        terrain.heightmapPixelError = 8f;
        terrain.drawHeightmap = true;
        terrain.allowAutoConnect = false;
        terrain.groupingID = 1;
        terrain.materialTemplate = m_Material;
        terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
        terrain.shadowCastingMode = ShadowCastingMode.Off;
        terrain.basemapDistance = 256;
        terrain.collectDetailPatches = false;
        terrain.drawInstanced = false;
        terrain.drawTreesAndFoliage = false;
        terrain.Flush();

        var collider = go.AddComponent<TerrainCollider>();
        collider.terrainData = data;

        return terrain;
    }
}
#endif
