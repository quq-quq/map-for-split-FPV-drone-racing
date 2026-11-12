using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Copies every visible mesh of the open scene into Blender: same objects, names, hierarchy (as collections),
// transforms, UVs, normals and URP Lit materials with their textures.
// Unity (Y-up, left-handed) -> Blender (Z-up, right-handed): (x, y, z) -> (x, z, y), triangle winding reversed.
// The Blender side is Tools/Blender/import_unity_greybox.py.
public static class BlenderGreyboxExporter
{
    const string BlenderPrefKey = "BlenderGreybox.BlenderExe";
    const string DefaultBlender = @"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe";
    static readonly HashSet<string> BlenderImageExt = new HashSet<string> { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".hdr", ".bmp" };

    public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
    public static string ExportDir => Path.Combine(ProjectRoot, "Exports", "Blender");
    public static string ImporterScript => Path.Combine(ProjectRoot, "Tools", "Blender", "import_unity_greybox.py");
    public static string JsonPath => Path.Combine(ExportDir, "greybox.json");
    public static string BlendPath => Path.Combine(ExportDir, SceneManager.GetActiveScene().name + "_Greybox.blend");

    [Serializable] class MeshInfo { public string name; public long offset; public int verts; public int loops; public int[] subTris; }
    [Serializable] class MatInfo
    {
        public string name, tex, normalTex, metallicTex, emissionTex;
        public float[] color, st, emission;
        public float normalScale, metallic, smoothness, cutoff;
        public bool transparent, alphaClip;
    }
    [Serializable] class ObjInfo { public string name, path; public int mesh; public int[] mats; public float[] matrix; }
    [Serializable] class SceneInfo
    {
        public string scene, binFile;
        public List<MeshInfo> meshes = new List<MeshInfo>();
        public List<MatInfo> materials = new List<MatInfo>();
        public List<ObjInfo> objects = new List<ObjInfo>();
    }

    [MenuItem("Tools/Blender/Export Greybox to .blend")]
    static void ExportAndBuildMenu()
    {
        Debug.Log(Export());
        string log;
        if (BuildBlend(out log)) Debug.Log("Blender greybox saved: " + BlendPath + "\n" + log);
        else Debug.LogError("Blender build failed:\n" + log);
    }

    [MenuItem("Tools/Blender/Export Greybox (data only)")]
    static void ExportMenu() => Debug.Log(Export());

    public static string Export()
    {
        Directory.CreateDirectory(ExportDir);
        var info = new SceneInfo { scene = SceneManager.GetActiveScene().name, binFile = "greybox.bin" };
        var meshIds = new Dictionary<Mesh, int>();
        var matIds = new Dictionary<Material, int>();

        var renderers = new List<MeshRenderer>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>() != null) renderers.Add(r);
        renderers.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));

        int baked = 0;
        try
        {
            using (var bw = new BinaryWriter(File.Open(Path.Combine(ExportDir, info.binFile), FileMode.Create)))
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    var r = renderers[i];
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar("Greybox -> Blender", r.name, (float)i / renderers.Count);
                    // Interior View swaps in trimmed meshes; Blender gets the full original.
                    var keep = r.GetComponent<InteriorViewOriginal>();
                    var mesh = keep != null && keep.original != null ? keep.original : r.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == null) continue;

                    var m = r.transform.localToWorldMatrix;
                    bool bake = NeedsBake(m); // shear or mirror: Blender object transforms can't hold it, so bake into the mesh
                    int meshId;
                    if (bake) { meshId = WriteMesh(bw, info, mesh, m, true); baked++; }
                    else if (!meshIds.TryGetValue(mesh, out meshId)) { meshId = WriteMesh(bw, info, mesh, Matrix4x4.identity, false); meshIds[mesh] = meshId; }

                    var mats = new int[mesh.subMeshCount];
                    for (int s = 0; s < mats.Length; s++)
                        mats[s] = s < r.sharedMaterials.Length ? MaterialId(r.sharedMaterials[s], info, matIds) : -1;

                    var parent = r.transform.parent;
                    info.objects.Add(new ObjInfo
                    {
                        name = r.name,
                        path = parent != null ? PathOf(parent) : "",
                        mesh = meshId,
                        mats = mats,
                        matrix = ToBlender(bake ? Matrix4x4.identity : m),
                    });
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        File.WriteAllText(JsonPath, JsonUtility.ToJson(info));
        return "Greybox export: " + info.objects.Count + " objects, " + info.meshes.Count + " meshes (" + baked + " baked), "
               + info.materials.Count + " materials -> " + ExportDir;
    }

    public static bool BuildBlend(out string log)
    {
        var exe = EditorPrefs.GetString(BlenderPrefKey, DefaultBlender);
        if (!File.Exists(exe))
        {
            exe = EditorUtility.OpenFilePanel("Locate blender.exe", "", "exe");
            if (string.IsNullOrEmpty(exe)) { log = "blender.exe not set"; return false; }
            EditorPrefs.SetString(BlenderPrefKey, exe);
        }
        var psi = new ProcessStartInfo(exe,
            "-b --factory-startup --python \"" + ImporterScript + "\" -- \"" + JsonPath + "\" \"" + BlendPath + "\"")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        EditorUtility.DisplayProgressBar("Greybox -> Blender", "Building .blend in Blender...", 0.5f);
        try
        {
            using (var p = Process.Start(psi))
            {
                log = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 && File.Exists(BlendPath);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ---------------------------------------------------------------- meshes

    static int WriteMesh(BinaryWriter bw, SceneInfo info, Mesh mesh, Matrix4x4 bakeM, bool bake)
    {
        var v = mesh.vertices;
        var n = mesh.normals;
        if (n == null || n.Length != v.Length)
        {
            var tmp = Object.Instantiate(mesh);
            tmp.RecalculateNormals();
            n = tmp.normals;
            Object.DestroyImmediate(tmp);
        }
        var uv = mesh.uv;
        bool hasUV = uv != null && uv.Length == v.Length;
        var normalM = bakeM.inverse.transpose;
        bool mirror = bake && bakeM.determinant < 0f;

        // Unity splits vertices on hard edges / UV seams; Blender keeps one vertex per position and
        // stores normals and UVs per face corner, so positions are welded and the rest goes to loops.
        var pos = new List<Vector3>(v.Length);
        var weld = new Dictionary<Vector3, int>(v.Length);
        var remap = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var p = bake ? bakeM.MultiplyPoint3x4(v[i]) : v[i];
            if (!weld.TryGetValue(p, out int k)) { k = pos.Count; pos.Add(p); weld[p] = k; }
            remap[i] = k;
        }

        var loopV = new List<int>();
        var loopSrc = new List<int>();
        var faces = new HashSet<(int, int, int)>();
        var subTris = new int[mesh.subMeshCount];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var topo = mesh.GetTopology(s);
            if (topo != MeshTopology.Triangles && topo != MeshTopology.Quads) continue;
            var idx = mesh.GetIndices(s);
            var tris = new List<int>();
            if (topo == MeshTopology.Triangles) tris.AddRange(idx);
            else for (int q = 0; q + 3 < idx.Length; q += 4) tris.AddRange(new[] { idx[q], idx[q + 1], idx[q + 2], idx[q], idx[q + 2], idx[q + 3] });

            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                // reverse winding for the handedness change (a mirrored bake flips it back)
                int i0 = tris[t], i1 = mirror ? tris[t + 1] : tris[t + 2], i2 = mirror ? tris[t + 2] : tris[t + 1];
                int a = remap[i0], b = remap[i1], c = remap[i2];
                if (a == b || b == c || a == c) continue; // zero-area, invalid in Blender
                if (!faces.Add(Key(a, b, c)))
                {
                    // same three corners already used (e.g. a double-sided wall): give this face its own vertices
                    a = pos.Count; pos.Add(pos[remap[i0]]);
                    b = pos.Count; pos.Add(pos[remap[i1]]);
                    c = pos.Count; pos.Add(pos[remap[i2]]);
                }
                loopV.Add(a); loopV.Add(b); loopV.Add(c);
                loopSrc.Add(i0); loopSrc.Add(i1); loopSrc.Add(i2);
                subTris[s]++;
            }
        }

        var mi = new MeshInfo { name = mesh.name, offset = bw.BaseStream.Position, verts = pos.Count, loops = loopV.Count, subTris = subTris };
        foreach (var p in pos) { bw.Write(p.x); bw.Write(p.z); bw.Write(p.y); }
        foreach (var i in loopV) bw.Write(i);
        foreach (var i in loopSrc)
        {
            var nn = bake ? normalM.MultiplyVector(n[i]).normalized : n[i];
            bw.Write(nn.x); bw.Write(nn.z); bw.Write(nn.y);
        }
        foreach (var i in loopSrc)
        {
            var t = hasUV ? uv[i] : Vector2.zero;
            bw.Write(t.x); bw.Write(t.y);
        }
        info.meshes.Add(mi);
        return info.meshes.Count - 1;
    }

    static (int, int, int) Key(int a, int b, int c)
    {
        if (a > b) { var t = a; a = b; b = t; }
        if (b > c) { var t = b; b = c; c = t; }
        if (a > b) { var t = a; a = b; b = t; }
        return (a, b, c);
    }

    static bool NeedsBake(Matrix4x4 m)
    {
        if (m.determinant < 0f) return true;
        Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
        if (x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f || z.sqrMagnitude < 1e-12f) return true;
        x.Normalize(); y.Normalize(); z.Normalize();
        return Mathf.Abs(Vector3.Dot(x, y)) > 1e-4f || Mathf.Abs(Vector3.Dot(x, z)) > 1e-4f || Mathf.Abs(Vector3.Dot(y, z)) > 1e-4f;
    }

    // Blender matrix = C * M * C, C swaps Y and Z. Row-major.
    static float[] ToBlender(Matrix4x4 m)
    {
        int[] ax = { 0, 2, 1, 3 };
        var r = new float[16];
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
                r[row * 4 + col] = m[ax[row], ax[col]];
        return r;
    }

    // ---------------------------------------------------------------- materials

    static int MaterialId(Material mat, SceneInfo info, Dictionary<Material, int> ids)
    {
        if (mat == null) return -1;
        if (ids.TryGetValue(mat, out int id)) return id;

        string baseProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : mat.HasProperty("_MainTex") ? "_MainTex" : null;
        var col = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        var lin = col.linear;
        var sc = baseProp != null ? mat.GetTextureScale(baseProp) : Vector2.one;
        var of = baseProp != null ? mat.GetTextureOffset(baseProp) : Vector2.zero;
        bool emissive = mat.IsKeywordEnabled("_EMISSION") && mat.HasProperty("_EmissionColor");
        var em = emissive ? mat.GetColor("_EmissionColor").linear : Color.black;

        var mi = new MatInfo
        {
            name = mat.name,
            color = new[] { lin.r, lin.g, lin.b, col.a },
            tex = TexturePath(baseProp != null ? mat.GetTexture(baseProp) : null),
            st = new[] { sc.x, sc.y, of.x, of.y },
            normalTex = TexturePath(Tex(mat, "_BumpMap")),
            normalScale = Float(mat, "_BumpScale", 1f),
            metallic = Float(mat, "_Metallic", 0f),
            metallicTex = TexturePath(Tex(mat, "_MetallicGlossMap")),
            smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : Float(mat, "_Glossiness", 0.5f),
            emission = new[] { em.r, em.g, em.b },
            emissionTex = emissive ? TexturePath(Tex(mat, "_EmissionMap")) : null,
            transparent = Float(mat, "_Surface", 0f) > 0.5f,
            alphaClip = Float(mat, "_AlphaClip", 0f) > 0.5f,
            cutoff = Float(mat, "_Cutoff", 0.5f),
        };
        info.materials.Add(mi);
        id = info.materials.Count - 1;
        ids[mat] = id;
        return id;
    }

    static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
    static float Float(Material m, string p, float def) => m.HasProperty(p) ? m.GetFloat(p) : def;

    static readonly Dictionary<Texture, string> texCache = new Dictionary<Texture, string>();

    static string TexturePath(Texture tex)
    {
        if (tex == null) return null;
        if (texCache.TryGetValue(tex, out var cached) && (cached == null || File.Exists(cached))) return cached;
        string result = null;
        var ap = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(ap))
        {
            var full = Path.GetFullPath(Path.Combine(ProjectRoot, ap));
            if (BlenderImageExt.Contains(Path.GetExtension(ap).ToLowerInvariant()) && File.Exists(full)) result = full;
        }
        if (result == null && tex is Texture2D) result = BakePng((Texture2D)tex);
        texCache[tex] = result;
        return result;
    }

    // Fallback for textures without a Blender-readable source file.
    static string BakePng(Texture2D tex)
    {
        var dir = Path.Combine(ExportDir, "textures");
        Directory.CreateDirectory(dir);
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var path = Path.Combine(dir, Sanitize(tex.name) + "_" + tex.GetInstanceID() + ".png");
        File.WriteAllBytes(path, copy.EncodeToPNG());
        Object.DestroyImmediate(copy);
        return path;
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static string PathOf(Transform t)
    {
        var p = t.name;
        for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
        return p;
    }
}
