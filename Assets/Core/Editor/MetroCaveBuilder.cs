using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Editor-only authoring. All results are saved as ordinary scene objects/assets.
public static class MetroCaveBuilder
{
    const string Folder = "Assets/Core/Visual/Environment/ServiceCavern";
    const string DefaultSource = "Assets/Core/Visual/Models/40DefaultRocks/source/cbe931dbfed44d3e8cb48438ac3228aa.zip/40Rocks.obj";
    const string ColorSource = "Assets/Core/Visual/Models/Rocks/rocks.fbx";
    static System.Random rng;
    static Texture2D stamp;
    static Material stone, shell, steel;
    static readonly List<Mesh> rocks = new List<Mesh>();
    static readonly List<Mesh> accents = new List<Mesh>();
    static readonly List<Material> accentMats = new List<Material>();
    static float R(float a, float b) { return Mathf.Lerp(a, b, (float)rng.NextDouble()); }
    static float Smooth(float a, float b, float x) { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x)); }
    static void Folders(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/'); Folders(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
    static T Store<T>(T obj, string path) where T : Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old != null) { EditorUtility.CopySerialized(obj, old); Object.DestroyImmediate(obj); return old; }
        AssetDatabase.CreateAsset(obj, path); return obj;
    }
    static GameObject Group(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
    }
    static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent, bool collider = true)
    {
        var go = Group(name, parent);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.On;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true; return go;
    }
    static Material Lit(string name, Color tint, string texture = null, string normal = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.name = name;
        m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", .13f);
        if (texture != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        if (normal != null)
        {
            var importer = AssetImporter.GetAtPath(normal) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap; importer.sRGBTexture = false; importer.SaveAndReimport();
            }
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            m.SetFloat("_BumpScale", 1.0f); m.EnableKeyword("_NORMALMAP");
        }
        m.enableInstancing = true; return Store(m, Folder + "/Materials/" + name + ".mat");
    }
    static Texture2D ReadTexture(Texture tex)
    {
        var rt = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active; Graphics.Blit(tex, rt); RenderTexture.active = rt;
        var copy = new Texture2D(512, 512, TextureFormat.RGBAFloat, false, true);
        copy.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); copy.Apply();
        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); return copy;
    }
    static float Stamp(float x, float z)
    {
        return stamp.GetPixelBilinear(Mathf.Repeat(x / 36f, 1), Mathf.Repeat(z / 31f, 1)).r;
    }
    static float Boundary(float x, float z)
    {
        return Mathf.Pow(Mathf.Pow(Mathf.Abs((x - 60.5f) / 34.5f), 6) + Mathf.Pow(Mathf.Abs((z - 63.5f) / 32.5f), 6), 1f / 6);
    }
    static float Ground(float x, float z)
    {
        float n = Mathf.PerlinNoise(x * .14f + 19, z * .14f + 5);
        float h = -26.5f + n * 1.0f + Stamp(x, z) * 1.5f;
        h += Smooth(75, 93, x) * (1.6f + Stamp(x + 8, z) * 2.3f);
        h += Smooth(79, 95, z) * (1.6f + Stamp(x, z + 8) * 2.8f);
        // A low apron meets the walkway; the rest drops into the cavern.
        if (z < 45 && x > 32 && x < 94) h = Mathf.Lerp(-23.57f, h, Smooth(38, 45, z));
        if (x < 43 && z < 55 && z > 37)
        {
            float ramp = Mathf.Lerp(-23.45f, -21.1f, Mathf.InverseLerp(38, 55, z));
            h = Mathf.Lerp(ramp - .4f, h, Smooth(33.5f, 43, x));
        }
        return h;
    }
    static Mesh RockMesh(Mesh source, string path)
    {
        var mesh = Object.Instantiate(source); mesh.name = source.name;
        mesh.RecalculateBounds(); var b = mesh.bounds;
        float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (size < .000001f) throw new InvalidOperationException("Empty rock: " + source.name);
        var origin = new Vector3(b.center.x, b.min.y, b.center.z);
        mesh.vertices = mesh.vertices.Select(v => (v - origin) / size).ToArray();
        mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
        return Store(mesh, path);
    }
    static void PrepareRocks()
    {
        rocks.Clear(); accents.Clear(); accentMats.Clear();
        stone = Lit("DefaultRock_WarmGrey", new Color(.64f, .61f, .56f),
            "Assets/Core/Visual/Models/40DefaultRocks/source/cbe931dbfed44d3e8cb48438ac3228aa.zip/RockColor.jpg",
            "Assets/Core/Visual/Models/40DefaultRocks/source/cbe931dbfed44d3e8cb48438ac3228aa.zip/RockNormal.jpg");
        shell = Lit("Cavern_Bedrock", new Color(.58f, .55f, .49f),
            "Assets/TerrainSampleAssets/Textures/Terrain/Rock_BaseColor.tif",
            "Assets/TerrainSampleAssets/Textures/Terrain/Rock_Normal.tif");
        shell.SetFloat("_Cull", 0); EditorUtility.SetDirty(shell);
        steel = Lit("Guardrail_DarkSteel", new Color(.19f, .22f, .23f));
        steel.SetFloat("_Metallic", .75f); steel.SetFloat("_Smoothness", .4f); EditorUtility.SetDirty(steel);
        foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(DefaultSource).OfType<Mesh>().OrderBy(m => m.name))
        {
            var centered = RockMesh(mesh, Folder + "/Meshes/" + mesh.name + ".asset"); rocks.Add(centered);
            var temp = MeshObject(mesh.name, centered, stone, null);
            PrefabUtility.SaveAsPrefabAsset(temp, Folder + "/RockPrefabs/" + mesh.name + ".prefab");
            Object.DestroyImmediate(temp);
        }
        foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(ColorSource).OfType<Mesh>().OrderBy(m => m.name).Take(12))
        {
            accents.Add(RockMesh(mesh, Folder + "/Meshes/Accent_" + mesh.name + ".asset"));
            string digits = mesh.name.Substring(4, 3);
            accentMats.Add(AssetDatabase.LoadAssetAtPath<Material>("Assets/Core/Visual/Models/Rocks/Materials/Rock" + digits + ".mat"));
        }
    }
    static int OpenPassage()
    {
        var modules = GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Modules");
        if (modules == null) throw new InvalidOperationException("Service corridor not found");
        int removed = 0;
        foreach (var mf in modules.GetComponentsInChildren<MeshFilter>())
        {
            int index; if (!int.TryParse(mf.name.Substring(0, 2), out index) || index < 6) continue;
            string path = Folder + "/Meshes/Opened_" + mf.name + ".asset";
            if (AssetDatabase.GetAssetPath(mf.sharedMesh) == path) continue;
            var mesh = Object.Instantiate(mf.sharedMesh); mesh.name = "CaveOpen_" + mf.name;
            var vv = mesh.vertices.Select(mf.transform.TransformPoint).ToArray();
            var mats = mf.GetComponent<Renderer>().sharedMaterials;
            int changed = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var src = mesh.GetTriangles(sub); var dst = new List<int>();
                string matName = sub < mats.Length && mats[sub] != null ? mats[sub].name : "";
                for (int j = 0; j < src.Length; j += 3)
                {
                    var a = vv[src[j]]; var b = vv[src[j + 1]]; var c = vv[src[j + 2]];
                    bool wall = matName.Contains("Wall");
                    bool east = a.x > 32.7f && b.x > 32.7f && c.x > 32.7f && a.x < 33f && b.x < 33f && c.x < 33f;
                    bool north = a.z > 37.75f && b.z > 37.75f && c.z > 37.75f && a.z < 38 && b.z < 38 && c.z < 38;
                    bool roofHall = mf.name.Contains("StairHall") && (matName.Contains("Ceiling") || ((a.y + b.y + c.y) / 3 > 2.0f));
                    if ((wall && ((index <= 21 && east) || (index >= 21 && north))) || roofHall) { changed++; continue; }
                    dst.Add(src[j]); dst.Add(src[j + 1]); dst.Add(src[j + 2]);
                }
                mesh.SetTriangles(dst, sub);
            }
            if (changed == 0) { Object.DestroyImmediate(mesh); continue; }
            removed += changed; mesh.RecalculateBounds();
            var saved = Store(mesh, path); Undo.RecordObject(mf, "Open corridor toward cavern"); mf.sharedMesh = saved;
            foreach (var mc in mf.GetComponents<MeshCollider>()) { Undo.RecordObject(mc, "Update open corridor collision"); mc.sharedMesh = saved; }
        }
        return removed;
    }
    static void TerrainFloor(Transform parent)
    {
        var td = new TerrainData(); td.name = "ServiceCavern_Floor"; td.heightmapResolution = 257;
        td.size = new Vector3(74, 14, 72); td.alphamapResolution = 256; td.baseMapResolution = 512;
        var heights = new float[257, 257];
        for (int z = 0; z < 257; z++) for (int x = 0; x < 257; x++)
            heights[z, x] = (Ground(24 + 74f * x / 256, 28 + 72f * z / 256) + 30) / 14;
        td.SetHeights(0, 0, heights);
        var holes = new bool[256, 256];
        for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++)
        {
            float wx = 24 + 74f * (x + .5f) / 256, wz = 28 + 72f * (z + .5f) / 256;
            holes[z, x] = Boundary(wx, wz) < 1.015f;
        }
        td.SetHoles(0, 0, holes);
        var layerNames = new[] { "Soil_Rocks", "Rock", "Pebbles_B" };
        var layers = new List<TerrainLayer>();
        foreach (string name in layerNames)
        {
            var l = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/TerrainSampleAssets/TerrainLayers/" + name + "_TerrainLayer.terrainlayer"));
            l.tileSize = new Vector2(name == "Rock" ? 5 : 3.5f, name == "Rock" ? 5 : 3.5f);
            l.smoothness = .08f; l.normalScale = 1.15f;
            l.diffuseRemapMax = new Vector4(.64f, .61f, .56f, 1);
            layers.Add(Store(l, Folder + "/Terrain/" + name + ".terrainlayer"));
        }
        td.terrainLayers = layers.ToArray(); var alpha = new float[256, 256, 3];
        for (int z = 0; z < 256; z++) for (int x = 0; x < 256; x++)
        {
            float wx = 24 + 74f * x / 255, wz = 28 + 72f * z / 255;
            float rock = .14f + .54f * Mathf.Max(Smooth(74, 94, wx), Smooth(78, 95, wz));
            float pebble = .12f + .22f * Mathf.PerlinNoise(wx * .17f, wz * .17f);
            alpha[z, x, 0] = 1 - rock - pebble; alpha[z, x, 1] = rock; alpha[z, x, 2] = pebble;
        }
        td.SetAlphamaps(0, 0, alpha); td = Store(td, Folder + "/Terrain/ServiceCavern_Floor.asset");
        var go = Terrain.CreateTerrainGameObject(td); go.name = "Cavern Floor [Terrain Tools]";
        go.transform.SetParent(parent); go.transform.position = new Vector3(24, -30, 28); go.isStatic = true;
        var terrain = go.GetComponent<Terrain>(); terrain.heightmapPixelError = 3; terrain.drawInstanced = true;
        terrain.materialTemplate = Store(new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")), Folder + "/Materials/Cavern_Terrain.mat");
        terrain.Flush();
    }
    static Vector3 ShellPoint(float a, float y, float r)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        float nx = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 1f / 3), nz = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 1f / 3);
        float noise = (Mathf.PerlinNoise(nx * 8 + 32, nz * 7 + y * .25f + 15) - .5f) * 1.8f;
        float strata = Mathf.Sin(y * 1.6f + a * 7) * .32f + Mathf.Sin(a * 17 + y * .7f) * .24f;
        return new Vector3(60.5f + nx * (34.5f * r + noise + strata), y + (r < .99f ? Mathf.Sin(a * 11 + y) * .65f : 0), 63.5f + nz * (32.5f * r + noise + strata));
    }
    static void Shell(Transform parent)
    {
        float[] ys = { -30, -27, -24, -21, -18, -15, -12, -9, -6, -3, 0, 2.5f, 5, 7.5f, 9.5f, 11, 12 };
        float[] rs = { 1, 1, 1, 1, .995f, 1, .99f, 1, .995f, 1, .99f, .96f, .88f, .74f, .57f, .33f, .035f };
        const int count = 128; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        for (int j = 0; j < ys.Length; j++) for (int i = 0; i <= count; i++)
        {
            v.Add(ShellPoint(i * Mathf.PI * 2 / count, ys[j], rs[j]));
            uv.Add(new Vector2(i * 210f / count / 5, (ys[j] + 30) / 5));
        }
        Action<int, int, int> triangle = (a, b, c) =>
        {
            var p = (v[a] + v[b] + v[c]) / 3;
            // Preserve the two existing connections through the cave boundary.
            if (p.x > 91.5f && p.z > 33.7f && p.z < 39.1f && p.y > -24.2f && p.y < -18.6f) return;
            if (p.x < 30 && p.z > 89 && p.z < 95.5f && p.y > -2.2f && p.y < 3.4f) return;
            var normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            var inward = new Vector3(60.5f - p.x, -Mathf.Max(0, p.y + 1) * 3, 63.5f - p.z);
            if (Vector3.Dot(normal, inward) < 0) { int temp = b; b = c; c = temp; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        };
        for (int j = 0; j < ys.Length - 1; j++) for (int i = 0; i < count; i++)
        {
            int a = j * (count + 1) + i, b = a + 1, c = a + count + 1, d = c + 1;
            triangle(a, c, b); triangle(b, c, d);
        }
        int top = v.Count; v.Add(new Vector3(60.5f, 12.25f, 63.5f)); uv.Add(new Vector2(21, 8.5f));
        for (int i = 0; i < count; i++) triangle(top, (ys.Length - 1) * (count + 1) + i, (ys.Length - 1) * (count + 1) + i + 1);
        var mesh = new Mesh { name = "ServiceCavern_ContinuousVault" }; mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        MeshObject("Continuous rock walls and vaulted ceiling", Store(mesh, Folder + "/Meshes/CavernVault.asset"), shell, parent);
    }
    static void Rock(Transform parent, Vector3 position, Vector3 size, Vector3 rotation, int i, bool accent = false)
    {
        int n = accent ? i % accents.Count : i % rocks.Count;
        var go = MeshObject((accent ? "Mineral accent " : "Limestone ") + i.ToString("000"), accent ? accents[n] : rocks[n], accent ? accentMats[n] : stone, parent);
        go.transform.position = position; go.transform.rotation = Quaternion.Euler(rotation); go.transform.localScale = size;
    }
    static void Dress(Transform root)
    {
        var masses = Group("01 Rock outcrops", root).transform;
        var rubble = Group("02 Talus and loose stones", root).transform;
        var mineral = Group("03 Sparse coloured minerals", root).transform;
        int id = 0;
        // Layered banks to the right, leaving the walkway and central flight volume free.
        for (int i = 0; i < 46; i++)
        {
            bool east = i % 2 == 0;
            float x = east ? R(84, 92) : R(42, 88), z = east ? R(45, 88) : R(85, 93);
            if (Boundary(x, z) > .995f) continue;
            float s = R(3.0f, 7.8f);
            Rock(masses, new Vector3(x, Ground(x, z) - R(.3f, 1.1f), z), new Vector3(s * R(.8f, 1.45f), s * R(.7f, 1.4f), s), new Vector3(R(-12, 12), R(0, 360), R(-12, 12)), id++);
        }
        // Wall buttresses and overhanging rock plates are above the stair/flight routes.
        for (int i = 0; i < 58; i++)
        {
            float a = i * Mathf.PI * 2 / 58, y = R(-17, 3), s = R(4.5f, 9);
            var p = ShellPoint(a, y, .985f);
            if (p.x < 35 && y < 4) continue;
            if (p.z < 40) continue;
            Rock(masses, p - new Vector3(0, s * .25f, 0), new Vector3(s, s * R(.55f, 1.3f), s * R(.65f, 1.1f)), new Vector3(R(-25, 25), R(0, 360), R(-25, 25)), id++);
        }
        for (int i = 0; i < 38; i++)
        {
            float a = R(0, Mathf.PI * 2), r = R(.35f, .91f), y = 12 - r * r * 8.5f;
            var p = ShellPoint(a, y, r); float s = R(4, 9);
            Rock(masses, new Vector3(p.x, p.y - s * .25f, p.z), new Vector3(s * 1.3f, s * .5f, s), new Vector3(R(-25, 25), R(0, 360), R(-20, 20)), id++);
        }
        Vector2[] islands = { new Vector2(70, 75), new Vector2(78, 54), new Vector2(51, 79), new Vector2(62, 48) };
        foreach (var c in islands) for (int j = 0; j < 4; j++)
        {
            float x = c.x + R(-3, 3), z = c.y + R(-2, 2), s = j == 0 ? R(3, 5) : R(.8f, 2.3f);
            Rock(masses, new Vector3(x, Ground(x, z) - .3f, z), new Vector3(s * 1.1f, s * .85f, s), new Vector3(R(-10, 10), R(0, 360), R(-10, 10)), id++);
        }
        for (int i = 0; i < 210; i++)
        {
            float x = R(37, 93), z = R(41, 93); if (Boundary(x, z) > .98f) continue;
            // Sparse central rubble, denser talus near the walls.
            if (x < 77 && z < 79 && i % 3 != 0) continue;
            float s = R(.18f, 1.3f);
            Rock(rubble, new Vector3(x, Ground(x, z) - .08f, z), new Vector3(s * R(.8f, 1.5f), s * .6f, s), new Vector3(R(-15, 15), R(0, 360), R(-15, 15)), id++);
        }
        for (int i = 0; i < 11; i++)
        {
            float x = R(76, 89), z = R(59, 88), s = R(.5f, 1.8f);
            Rock(mineral, new Vector3(x, Ground(x, z) - .12f, z), new Vector3(s, s * .8f, s), new Vector3(0, R(0, 360), 0), i, true);
        }
    }
    static void Beam(Transform parent, Vector3 a, Vector3 b, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = "Steel railing";
        go.transform.SetParent(parent); go.transform.position = (a + b) * .5f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
        go.transform.localScale = new Vector3(radius * 2, Vector3.Distance(a, b) * .5f, radius * 2);
        go.GetComponent<Renderer>().sharedMaterial = steel; go.isStatic = true;
    }
    static void Details(Transform root)
    {
        var rails = Group("04 Open walkway guardrail", root).transform;
        for (float x = 34; x < 93; x += 3.5f)
        {
            Beam(rails, new Vector3(x, -23.27f, 37.72f), new Vector3(x, -22.2f, 37.72f), .035f);
        }
        Beam(rails, new Vector3(34, -22.2f, 37.72f), new Vector3(92.9f, -22.2f, 37.72f), .04f);
        Beam(rails, new Vector3(34, -22.73f, 37.72f), new Vector3(92.9f, -22.73f, 37.72f), .025f);
        var lights = Group("05 Cavern lighting", root).transform;
        Vector3[] pos = { new Vector3(44,-17,44),new Vector3(66,-18,44),new Vector3(85,-17,45),new Vector3(85,-10,75),new Vector3(50,-8,84),new Vector3(42,2,65) };
        for (int i = 0; i < pos.Length; i++)
        {
            var go = Group("Indirect rock fill " + (i+1), lights); go.transform.position = pos[i];
            var light = go.AddComponent<Light>(); light.type = LightType.Point;
            light.color = i < 3 ? new Color(1, .79f, .54f) : new Color(.57f, .68f, .81f);
            light.intensity = i < 3 ? 8 : 5; light.range = i < 3 ? 22 : 27;
            light.shadows = LightShadows.None; light.renderMode = LightRenderMode.ForcePixel;
        }
    }
    static float RoofHeight(float radius)
    {
        float[] r = { 0, .035f, .33f, .57f, .74f, .88f, .96f, .99f, 1.1f };
        float[] y = { 12.25f, 12, 11, 9.5f, 7.5f, 5, 2.5f, 0, -3 };
        for (int i = 1; i < r.Length; i++) if (radius <= r[i]) return Mathf.Lerp(y[i - 1], y[i], Mathf.InverseLerp(r[i - 1], r[i], radius));
        return -3;
    }
    public static string Finish()
    {
        var root = GameObject.Find("ServiceCavern");
        var shader = Shader.Find("Metro/Cavern Triplanar");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Triplanar shader unavailable");
        shell = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/Bedrock_Triplanar.mat");
        if (shell == null) shell = Store(new Material(shader), Folder + "/Materials/Bedrock_Triplanar.mat");
        shell.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TerrainSampleAssets/Textures/Terrain/Rock_BaseColor.tif"));
        shell.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TerrainSampleAssets/Textures/Terrain/Rock_Normal.tif"));
        shell.enableInstancing = true; shell.SetFloat("_TextureScale", .27f); shell.SetFloat("_BumpScale", 1.0f);
        shell.SetColor("_BaseColor", new Color(.70f,.65f,.57f)); shell.SetFloat("_Cull", 0); EditorUtility.SetDirty(shell);
        stone = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/DefaultRock_WarmGrey.mat");
        string normalPath = "Assets/Core/Visual/Models/40DefaultRocks/source/cbe931dbfed44d3e8cb48438ac3228aa.zip/RockNormal.jpg";
        var ti = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; ti.SaveAndReimport(); }
        stone.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)); stone.SetFloat("_BumpScale", .65f); stone.EnableKeyword("_NORMALMAP");
        stone.SetColor("_BaseColor", new Color(.82f,.77f,.68f)); EditorUtility.SetDirty(stone);
        var outcrop = Store(Object.Instantiate(shell), Folder + "/Materials/Cavern_Outcrops.mat");
        outcrop.SetFloat("_Cull", 2); outcrop.SetColor("_BaseColor", new Color(.83f,.77f,.67f)); EditorUtility.SetDirty(outcrop);
        var masses = root.transform.Find("01 Rock outcrops");
        foreach (var renderer in masses.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial = outcrop;
        // Set roof stones into the roof using their real transformed bounds.
        for (int i = masses.childCount - 54; i < masses.childCount - 16; i++)
        {
            var t = masses.GetChild(i); var renderer = t.GetComponent<Renderer>();
            var center = renderer.bounds.center; float roof = RoofHeight(Boundary(center.x, center.z));
            t.position += Vector3.up * (roof + .85f - renderer.bounds.max.y);
            t.name = "Embedded roof plate " + (i - (masses.childCount - 54) + 1).ToString("00");
        }
        // Smaller sectors let Unity select local lights independently across the very large vault.
        var original = root.transform.Find("00 Bedrock vault/Continuous rock walls and vaulted ceiling");
        if (original != null)
        {
            var source = original.GetComponent<MeshFilter>().sharedMesh; var vertices = source.vertices; var triangles = source.triangles;
            for (int sector = 0; sector < 8; sector++)
            {
                var indices = new List<int>();
                for (int j = 0; j < triangles.Length; j += 3)
                {
                    var p = (vertices[triangles[j]] + vertices[triangles[j+1]] + vertices[triangles[j+2]]) / 3;
                    float a = Mathf.Atan2(p.z-63.5f,p.x-60.5f); if (a < 0) a += Mathf.PI * 2;
                    if (Mathf.Min(7,Mathf.FloorToInt(a / (Mathf.PI * .25f))) != sector) continue;
                    indices.Add(triangles[j]); indices.Add(triangles[j+1]); indices.Add(triangles[j+2]);
                }
                var mesh = Object.Instantiate(source); mesh.name = "Vault sector " + sector; mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
                MeshObject("Vault sector " + (sector+1).ToString("00"), Store(mesh,Folder + "/Meshes/VaultSector"+sector+".asset"),shell,original.parent);
            }
            Object.DestroyImmediate(original.gameObject);
        }
        var lighting = root.transform.Find("05 Cavern lighting");
        for (int i = 0; i < 3; i++)
        {
            string name = "Warm canopy flood " + (i+1); var t = lighting.Find(name);
            var go = t != null ? t.gameObject : Group(name,lighting);
            go.transform.position = new Vector3(47+i*21,-19.95f,37.5f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-.15f,.4f,1));
            var light = go.GetComponent<Light>(); if (light == null) light = go.AddComponent<Light>();
            light.type = LightType.Spot; light.spotAngle = 85; light.innerSpotAngle = 48;
            light.range = 48; light.intensity = 105; light.color = new Color(1,.77f,.48f); light.shadows = LightShadows.Soft;
        }
        for (int i = 0; i < 2; i++)
        {
            string name = "Soft cavern bounce " + (i+1); var t = lighting.Find(name);
            var go = t != null ? t.gameObject : Group(name,lighting);
            go.transform.position = new Vector3(58+i*18,-12-i*5,62+i*10);
            var light = go.GetComponent<Light>(); if (light == null) light = go.AddComponent<Light>();
            light.type = LightType.Point; light.range = 38; light.intensity = 20;
            light.color = new Color(.82f,.77f,.68f); light.shadows = LightShadows.None;
        }
        Polish();
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
        return "Triplanar rock material, embedded roof plates and local lighting applied.";
    }
    public static string Polish()
    {
        var root = GameObject.Find("ServiceCavern");
        var vault = root.transform.Find("00 Bedrock vault");
        var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/CavernVault.asset"));
        var vv = source.vertices;
        for (int i = 0; i < vv.Length; i++)
        {
            var p = vv[i];
            float roof = Smooth(-2,6,p.y);
            p.y += roof * ((Mathf.PerlinNoise(p.x*.145f+8,p.z*.145f+17)-.48f)*6.5f + (Mathf.PerlinNoise(p.x*.48f,p.z*.48f)-.5f)*1.8f);
            var radial = new Vector3(p.x-60.5f,0,p.z-63.5f).normalized;
            p += radial * ((Mathf.PerlinNoise(p.x*.21f+p.y*.09f,p.z*.19f+31)-.5f)*2.8f * Smooth(-27,-19,p.y) * (1-roof));
            vv[i] = p;
        }
        source.vertices=vv; source.RecalculateNormals(); source.RecalculateTangents(); source.RecalculateBounds();
        source=Store(source,Folder+"/Meshes/CavernVault_Sculpted.asset");
        var tri=source.triangles; var sourceUV=source.uv; var normals=source.normals; var tangents=source.tangents;
        var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/Bedrock_Triplanar.mat");
        for (int sector=0;sector<8;sector++)
        {
            var indices=new List<int>();
            for (int j=0;j<tri.Length;j+=3)
            {
                var p=(vv[tri[j]]+vv[tri[j+1]]+vv[tri[j+2]])/3;
                float a=Mathf.Atan2(p.z-63.5f,p.x-60.5f); if(a<0)a+=Mathf.PI*2;
                if(Mathf.Min(7,Mathf.FloorToInt(a/(Mathf.PI*.25f)))!=sector)continue;
                indices.Add(tri[j]);indices.Add(tri[j+1]);indices.Add(tri[j+2]);
            }
            var used=indices.Distinct().ToArray();var map=new Dictionary<int,int>();for(int j=0;j<used.Length;j++)map[used[j]]=j;
            var mesh=new Mesh{name="Sculpted vault sector "+sector};mesh.vertices=used.Select(j=>vv[j]).ToArray();
            mesh.uv=used.Select(j=>sourceUV[j]).ToArray();mesh.normals=used.Select(j=>normals[j]).ToArray();mesh.tangents=used.Select(j=>tangents[j]).ToArray();
            mesh.triangles=indices.Select(j=>map[j]).ToArray();mesh.RecalculateBounds();
            var saved=Store(mesh,Folder+"/Meshes/VaultSector"+sector+".asset");
            var t=vault.Find("Vault sector "+(sector+1).ToString("00"));
            if(t==null)t=MeshObject("Vault sector "+(sector+1).ToString("00"),saved,material,vault).transform;
            t.GetComponent<MeshFilter>().sharedMesh=saved;t.GetComponent<MeshCollider>().sharedMesh=null;t.GetComponent<MeshCollider>().sharedMesh=saved;
            t.GetComponent<Renderer>().sharedMaterial=material;
        }
        Physics.SyncTransforms();var roofColliders=vault.GetComponentsInChildren<MeshCollider>();
        var masses=root.transform.Find("01 Rock outcrops");
        foreach(var t in masses.Cast<Transform>().Where(t=>t.name.StartsWith("Embedded roof plate")))
        {
            var b=t.GetComponent<Renderer>().bounds;
            var ray=new Ray(new Vector3(b.center.x,-17,b.center.z),Vector3.up);float hitY=float.MaxValue;
            foreach(var collider in roofColliders){RaycastHit hit;if(collider.Raycast(ray,out hit,60))hitY=Mathf.Min(hitY,hit.point.y);}
            if(hitY<float.MaxValue)t.position+=Vector3.up*(hitY+b.size.y*.6f-b.max.y);
        }
        foreach(var t in masses.Cast<Transform>().Where(t=>t.name.StartsWith("Limestone") && t.position.y > -20))
        {
            var b=t.GetComponent<Renderer>().bounds;
            var origin=new Vector3(60.5f,b.center.y,63.5f);
            var direction=new Vector3(b.center.x-origin.x,0,b.center.z-origin.z).normalized;
            var ray=new Ray(origin,direction);float distance=float.MaxValue;Vector3 point=Vector3.zero;
            foreach(var collider in roofColliders)
            {
                RaycastHit hit;if(collider.Raycast(ray,out hit,75)&&hit.distance<distance){distance=hit.distance;point=hit.point;}
            }
            if(distance<float.MaxValue)
            {
                float extent=Mathf.Abs(direction.x)*b.extents.x+Mathf.Abs(direction.z)*b.extents.z;
                t.position+=point+direction*extent*.5f-b.center;
            }
        }
        foreach(var light in root.transform.Find("05 Cavern lighting").GetComponentsInChildren<Light>())
        {
            if(light.name.StartsWith("Indirect")){light.enabled=false;continue;}
            light.renderMode=LightRenderMode.ForcePixel; light.intensity=light.type==LightType.Spot?850:100;
        }
        RoundFreeSide();
        FitOutsideDepot();
        MetroCavePortals.Repair();
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Sculpted and joined cave roof; tightened sector bounds; updated lighting.";
    }
    struct CutVertex
    {
        public Vector3 p, n;
        public Vector2 uv;
        public Vector4 tangent;
        public static CutVertex Lerp(CutVertex a, CutVertex b, float t)
        {
            return new CutVertex { p=Vector3.Lerp(a.p,b.p,t), n=Vector3.Lerp(a.n,b.n,t).normalized,
                uv=Vector2.Lerp(a.uv,b.uv,t), tangent=Vector4.Lerp(a.tangent,b.tangent,t) };
        }
    }
    static float RoundedNorthEdge(float x)
    {
        float u=Mathf.Clamp01((x-30)/64);
        float arc=62+33.5f*Mathf.Sqrt(Mathf.Max(0,1-u*u));
        return Mathf.Lerp(96,arc,Smooth(36,48,x));
    }
    static float RoundZ(float x,float z)
    {
        if(z<=52)return z;
        // Monotone deformation: unchanged derivative at the fixed part of the passage.
        float k=Mathf.Clamp((RoundedNorthEdge(x)-56)/40,.12f,1);
        float d=z-52;
        return 52+k*d+(1-k)*4*(1-Mathf.Exp(-d/4));
    }
    static Vector3 RoundPoint(Vector3 p) { p.z=RoundZ(p.x,p.z);return p; }
    static float UnroundZ(float x,float z)
    {
        if(z<=52)return z;
        float low=52,high=500;
        for(int i=0;i<24;i++){float mid=(low+high)*.5f;if(RoundZ(x,mid)<z)low=mid;else high=mid;}
        return (low+high)*.5f;
    }
    static float SampleHeight(float[,] values,float x,float z)
    {
        int maxX=values.GetLength(1)-1,maxZ=values.GetLength(0)-1;
        x=Mathf.Clamp(x,0,maxX);z=Mathf.Clamp(z,0,maxZ);
        int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z),jx=Mathf.Min(ix+1,maxX),jz=Mathf.Min(iz+1,maxZ);
        return Mathf.Lerp(Mathf.Lerp(values[iz,ix],values[iz,jx],x-ix),Mathf.Lerp(values[jz,ix],values[jz,jx],x-ix),z-iz);
    }
    public static string RoundFreeSide()
    {
        var root=GameObject.Find("ServiceCavern");if(root==null)throw new InvalidOperationException("Cavern not found");
        bool applied=root.transform.Find("Rounded footprint authoring marker")!=null;int sectors=0,moved=0;
        foreach(var mf in root.transform.Find("00 Bedrock vault").GetComponentsInChildren<MeshFilter>())
        {
            string meshPath=AssetDatabase.GetAssetPath(mf.sharedMesh);
            if(meshPath.Contains("/Rounded_")||meshPath.Contains("/Portal_Vault_"))continue;
            var mesh=Object.Instantiate(mf.sharedMesh);mesh.name="Rounded "+mf.name;
            mesh.vertices=mesh.vertices.Select(p=>mf.transform.InverseTransformPoint(RoundPoint(mf.transform.TransformPoint(p)))).ToArray();
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            var saved=Store(mesh,Folder+"/Meshes/Rounded_"+mf.name.Replace(' ','_')+".asset");
            Undo.RecordObject(mf,"Round cave footprint");mf.sharedMesh=saved;
            foreach(var collider in mf.GetComponents<MeshCollider>()){Undo.RecordObject(collider,"Round cave collision");collider.sharedMesh=null;collider.sharedMesh=saved;}
            sectors++;
        }
        if(!applied)
        {
            var terrain=root.GetComponentInChildren<Terrain>();var td=terrain.terrainData;var position=terrain.transform.position;
            var backup=Store(Object.Instantiate(td),Folder+"/Terrain/ServiceCavern_Floor_BeforeRounding.asset");
            int h=td.heightmapResolution,a=td.alphamapResolution,r=td.holesResolution;
            var oldHeights=backup.GetHeights(0,0,h,h);var oldHoles=backup.GetHoles(0,0,r,r);var oldAlpha=backup.GetAlphamaps(0,0,a,a);
            var heights=new float[h,h];var holes=new bool[r,r];var alpha=new float[a,a,td.alphamapLayers];
            for(int z=0;z<h;z++)for(int x=0;x<h;x++)
            {
                float wx=position.x+x*td.size.x/(h-1),wz=position.z+z*td.size.z/(h-1);
                float sourceZ=(UnroundZ(wx,wz)-position.z)/td.size.z*(h-1);
                heights[z,x]=SampleHeight(oldHeights,x,sourceZ);
            }
            for(int z=0;z<r;z++)for(int x=0;x<r;x++)
            {
                float wx=position.x+(x+.5f)*td.size.x/r,wz=position.z+(z+.5f)*td.size.z/r;
                float sourceZ=(UnroundZ(wx,wz)-position.z)/td.size.z*r;
                int iz=Mathf.FloorToInt(sourceZ);
                float maxCellX=position.x+(x+1)*td.size.x/r,maxCellZ=position.z+(z+1)*td.size.z/r;
                holes[z,x]=iz>=0&&iz<r&&oldHoles[iz,x]&&maxCellZ<=RoundedNorthEdge(maxCellX)+.05f;
            }
            for(int z=0;z<a;z++)for(int x=0;x<a;x++)
            {
                float wx=position.x+x*td.size.x/(a-1),wz=position.z+z*td.size.z/(a-1);
                float sourceZ=Mathf.Clamp((UnroundZ(wx,wz)-position.z)/td.size.z*(a-1),0,a-1);
                int iz=Mathf.FloorToInt(sourceZ),jz=Mathf.Min(iz+1,a-1);
                for(int l=0;l<td.alphamapLayers;l++)alpha[z,x,l]=Mathf.Lerp(oldAlpha[iz,x,l],oldAlpha[jz,x,l],sourceZ-iz);
            }
            Undo.RegisterCompleteObjectUndo(td,"Round terrain boundary");td.SetHeights(0,0,heights);td.SetHoles(0,0,holes);td.SetAlphamaps(0,0,alpha);terrain.Flush();EditorUtility.SetDirty(td);
            foreach(string name in new[]{"01 Rock outcrops","02 Talus and loose stones","03 Sparse coloured minerals"})
            foreach(var t in root.transform.Find(name).Cast<Transform>())
            {
                var renderer=t.GetComponent<Renderer>();if(renderer==null)continue;
                var center=renderer.bounds.center;var delta=RoundPoint(center)-center;
                if(delta.sqrMagnitude<.000001f)continue;Undo.RecordObject(t,"Arrange rocks on curved cave boundary");t.position+=delta;moved++;
            }
            foreach(var t in root.transform.Find("05 Cavern lighting").Cast<Transform>())
            {
                Undo.RecordObject(t,"Follow curved cave with lighting");t.position=RoundPoint(t.position);
            }
            var marker=Group("Rounded footprint authoring marker",root.transform);marker.SetActive(false);
            Undo.RegisterCreatedObjectUndo(marker,"Remember rounded cave authoring");
        }
        Physics.SyncTransforms();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Rounded cave sectors="+sectors+"; rocks repositioned="+moved+"; terrain updated="+(!applied);
    }
    static Mesh CutRockAtDepot(Mesh source, Transform transform, float limit)
    {
        var positions=source.vertices;var normals=source.normals;var uvs=source.uv;var tangents=source.tangents;
        var output=new List<CutVertex>();var triangles=new List<int>();var edge=new List<Vector3>();
        var indices=source.triangles;
        for(int j=0;j<indices.Length;j+=3)
        {
            var polygon=new List<CutVertex>();
            for(int k=0;k<3;k++)
            {
                int i=indices[j+k];polygon.Add(new CutVertex {p=positions[i],n=normals[i],uv=uvs.Length>i?uvs[i]:Vector2.zero,tangent=tangents.Length>i?tangents[i]:new Vector4(1,0,0,1)});
            }
            var clipped=new List<CutVertex>();
            for(int k=0;k<polygon.Count;k++)
            {
                var a=polygon[k];var b=polygon[(k+1)%polygon.Count];
                float ax=transform.TransformPoint(a.p).x-limit,bx=transform.TransformPoint(b.p).x-limit;
                if(ax<=0)clipped.Add(a);
                if((ax<0 && bx>0)||(ax>0 && bx<0))
                {
                    var intersection=CutVertex.Lerp(a,b,ax/(ax-bx));clipped.Add(intersection);
                    if(!edge.Any(p=>(p-intersection.p).sqrMagnitude<.00000001f))edge.Add(intersection.p);
                }
            }
            for(int k=1;k+1<clipped.Count;k++)
            {
                int offset=output.Count;output.Add(clipped[0]);output.Add(clipped[k]);output.Add(clipped[k+1]);
                triangles.Add(offset);triangles.Add(offset+1);triangles.Add(offset+2);
            }
        }
        // Close the back of each trimmed rock, preserving a solid collider.
        if(edge.Count>=3)
        {
            var center=edge.Aggregate(Vector3.zero,(a,b)=>a+b)/edge.Count;
            var worldCenter=transform.TransformPoint(center);
            edge=edge.OrderBy(p=>{var w=transform.TransformPoint(p)-worldCenter;return Mathf.Atan2(w.z,w.y);}).ToList();
            var normal=transform.localToWorldMatrix.transpose.MultiplyVector(Vector3.right).normalized;
            for(int i=0;i<edge.Count;i++)
            {
                var a=edge[i];var b=edge[(i+1)%edge.Count];
                if(Vector3.Cross(transform.TransformPoint(a)-worldCenter,transform.TransformPoint(b)-worldCenter).x<0){var swap=a;a=b;b=swap;}
                int offset=output.Count;
                foreach(var p in new[]{center,a,b})
                {
                    var w=transform.TransformPoint(p);output.Add(new CutVertex{p=p,n=normal,uv=new Vector2(w.z*.25f,w.y*.25f),tangent=new Vector4(0,0,1,1)});
                }
                triangles.Add(offset);triangles.Add(offset+1);triangles.Add(offset+2);
            }
        }
        var mesh=new Mesh{name=source.name+"_OutsideDepot"};
        if(output.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.vertices=output.Select(v=>v.p).ToArray();mesh.normals=output.Select(v=>v.n).ToArray();
        mesh.uv=output.Select(v=>v.uv).ToArray();mesh.tangents=output.Select(v=>v.tangent).ToArray();
        mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();return mesh;
    }
    public static string FitOutsideDepot()
    {
        var root=GameObject.Find("ServiceCavern");var depot=GameObject.Find("MetroDepot/MetroDepot_Model");
        if(root==null||depot==null)throw new InvalidOperationException("Cavern or depot not found");
        var westBoundary=depot.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.x);
        float limit=westBoundary-.25f;int vaults=0,rocksTrimmed=0,hidden=0,holesCut=0;
        foreach(var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var renderer=mf.GetComponent<Renderer>();if(renderer==null||renderer.bounds.max.x<=limit+.0001f)continue;
            bool vault=mf.transform.parent==root.transform.Find("00 Bedrock vault");
            if(!vault && mf.transform.parent!=root.transform.Find("01 Rock outcrops") && mf.transform.parent!=root.transform.Find("02 Talus and loose stones") && mf.transform.parent!=root.transform.Find("03 Sparse coloured minerals"))continue;
            if(!vault && renderer.bounds.min.x>=limit)
            {
                Undo.RecordObject(mf.gameObject,"Keep rocks outside depot");mf.gameObject.SetActive(false);hidden++;continue;
            }
            Mesh mesh;
            if(vault)
            {
                mesh=Object.Instantiate(mf.sharedMesh);
                mesh.vertices=mesh.vertices.Select(p=>{var w=mf.transform.TransformPoint(p);w.x=Mathf.Min(w.x,limit);return mf.transform.InverseTransformPoint(w);}).ToArray();
                mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();vaults++;
            }
            else {mesh=CutRockAtDepot(mf.sharedMesh,mf.transform,limit);rocksTrimmed++;}
            var saved=Store(mesh,Folder+"/Meshes/DepotFit_"+mf.name.Replace(' ','_')+".asset");
            Undo.RecordObject(mf,"Fit cavern outside depot");mf.sharedMesh=saved;
            foreach(var collider in mf.GetComponents<MeshCollider>()){Undo.RecordObject(collider,"Fit cave collision outside depot");collider.sharedMesh=null;collider.sharedMesh=saved;}
        }
        var terrain=root.GetComponentInChildren<Terrain>();var td=terrain.terrainData;int resolution=td.holesResolution;
        Undo.RegisterCompleteObjectUndo(td,"Keep Terrain outside depot");var holes=td.GetHoles(0,0,resolution,resolution);
        for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
        {
            float cellMaxX=terrain.transform.position.x+(x+1)*td.size.x/resolution;
            if(cellMaxX>limit && holes[z,x]){holes[z,x]=false;holesCut++;}
        }
        td.SetHoles(0,0,holes);terrain.Flush();EditorUtility.SetDirty(td);Physics.SyncTransforms();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Depot west boundary="+westBoundary.ToString("F3")+"; cave limit="+limit.ToString("F3")+"; vault sectors="+vaults+"; rocks trimmed="+rocksTrimmed+"; outside rocks disabled="+hidden+"; Terrain cells removed="+holesCut;
    }
    public static string Build()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Core/Scenes/Metro.unity") throw new InvalidOperationException("Open Metro first");
        if (GameObject.Find("ServiceCavern") != null) throw new InvalidOperationException("ServiceCavern already exists; edit the authored result instead of duplicating it");
        Folders(Folder + "/Meshes"); Folders(Folder + "/Materials"); Folders(Folder + "/Terrain"); Folders(Folder + "/RockPrefabs");
        System.IO.Directory.CreateDirectory("Library/CaveReview");
        EditorSceneManager.SaveScene(scene, "Library/CaveReview/Metro-before-cave.unity", true);
        Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Create service passage cavern");
        rng = new System.Random(72519);
        var brush = AssetDatabase.LoadMainAssetAtPath("Assets/TerrainSampleAssets/TerrainBrushes/Terrainstamp_Ridge03_Brush.brush");
        var serialized = new SerializedObject(brush);
        stamp = ReadTexture(serialized.FindProperty("m_Mask").objectReferenceValue as Texture);
        PrepareRocks(); int removed = OpenPassage();
        var root = Group("ServiceCavern", null); Undo.RegisterCreatedObjectUndo(root, "Create service cavern");
        TerrainFloor(root.transform); Shell(Group("00 Bedrock vault", root.transform).transform); Dress(root.transform); Details(root.transform);
        Finish();
        Object.DestroyImmediate(stamp); stamp = null;
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        return "Cavern created. Removed wall/ceiling triangles: " + removed + "; separate rock prefabs: " + rocks.Count + "; scene objects: " + root.GetComponentsInChildren<Transform>().Length;
    }
}
