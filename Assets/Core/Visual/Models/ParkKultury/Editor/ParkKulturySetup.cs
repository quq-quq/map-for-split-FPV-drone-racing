// Park Kultury (Moscow Metro, 1935) - north surface vestibule. Auto setup for Unity.
// Drop the "ParkKultury" folder into Assets. Everything is configured automatically on first import.
// Re-run any time: Tools > Park Kultury > Setup Materials and Prefab
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkKultury
{
    public class ParkKulturyPostprocessor : AssetPostprocessor
    {
        public static bool IsOurs(string path)
        {
            return path.Replace('\\', '/').EndsWith("ParkKultury_NorthVestibule.fbx", StringComparison.OrdinalIgnoreCase);
        }

        void OnPreprocessModel()
        {
            if (!IsOurs(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.generateSecondaryUV = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsOurs(assetPath)) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("UCX_"))
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mc = t.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                    }
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null) UnityEngine.Object.DestroyImmediate(mr);
                    if (mf != null) UnityEngine.Object.DestroyImmediate(mf);
                }
                else if (t.name.StartsWith("SM_PK_Glass"))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var p in importedAssets)
            {
                if (!IsOurs(p)) continue;
                string dir = Path.GetDirectoryName(p).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(dir + "/Materials"))
                {
                    string path = p;
                    EditorApplication.delayCall += () => ParkKulturySetup.Run(path);
                }
            }
        }
    }

    public static class ParkKulturySetup
    {
        struct MatDef
        {
            public string name, tex; public Color color; public float metallic, smoothness; public Color emission; public float emissionIntensity;
            public MatDef(string n, string t, Color c, float m, float s, Color e, float ei)
            { name = n; tex = t; color = c; metallic = m; smoothness = s; emission = e; emissionIntensity = ei; }
        }

        static readonly MatDef[] Defs = new MatDef[]
        {
            new MatDef("M_BlackMarblite", null, new Color(0.112f, 0.112f, 0.123f), 0.000f, 0.840f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Brass", null, new Color(0.896f, 0.786f, 0.584f), 1.000f, 0.720f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Bronze", null, new Color(0.485f, 0.396f, 0.293f), 0.900f, 0.620f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Dark", null, new Color(0.000f, 0.000f, 0.000f), 0.000f, 0.000f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_FloorTile_Metlakh", "FloorTile", new Color(1f, 1f, 1f), 0f, 0.563f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_Glass_Exterior", null, new Color(0.190f, 0.221f, 0.235f), 0.000f, 0.965f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Glass_Interior_Mirror", null, new Color(0.798f, 0.809f, 0.821f), 1.000f, 0.960f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Granite", "Granite", new Color(1f, 1f, 1f), 0f, 0.429f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_Lamp_Emissive", null, new Color(0.978f, 0.969f, 0.945f), 0.000f, 0.700f, new Color(1.000f, 0.936f, 0.832f), 3.200f),
            new MatDef("M_Lamp_Matte", null, new Color(0.964f, 0.959f, 0.945f), 0.000f, 0.650f, new Color(1.000f, 0.945f, 0.854f), 0.160f),
            new MatDef("M_Marble", "Marble", new Color(1f, 1f, 1f), 0f, 0.86f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_MetalFrame", null, new Color(0.235f, 0.235f, 0.240f), 0.800f, 0.620f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Oak", "Oak", new Color(1f, 1f, 1f), 0f, 0.49f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_Plaster", "Plaster", new Color(1f, 1f, 1f), 0f, 0.16f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_RedLetters", null, new Color(0.735f, 0.206f, 0.172f), 0.200f, 0.650f, new Color(0.000f, 0.000f, 0.000f), 0.000f),
            new MatDef("M_Star_Ruby", null, new Color(0.798f, 0.152f, 0.152f), 0.000f, 0.800f, new Color(1.000f, 0.313f, 0.221f), 0.600f),
            new MatDef("M_StoneCladding", "StoneCladding", new Color(1f, 1f, 1f), 0f, 0.296f, new Color(0f, 0f, 0f), 0f),
            new MatDef("M_WallTile_Glazed", "WallTile", new Color(1f, 1f, 1f), 0f, 0.739f, new Color(0f, 0f, 0f), 0f),
        };

        [MenuItem("Tools/Park Kultury/Setup Materials and Prefab")]
        public static void RunMenu()
        {
            string guid = AssetDatabase.FindAssets("ParkKultury_NorthVestibule t:Model").FirstOrDefault();
            if (string.IsNullOrEmpty(guid)) { Debug.LogError("[ParkKultury] ParkKultury_NorthVestibule.fbx not found"); return; }
            Run(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static void Run(string fbxPath)
        {
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string texDir = dir + "/Textures", matDir = dir + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materials");

            if (AssetDatabase.IsValidFolder(texDir))
            {
                foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti == null) continue;
                    bool changed = false;
                    if (p.EndsWith("_Normal.png") && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                    if (p.EndsWith("_MetallicSmoothness.png") && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
                    if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
                    if (ti.anisoLevel < 4) { ti.anisoLevel = 4; changed = true; }
                    if (changed) ti.SaveAndReimport();
                }
            }

            var rp = GraphicsSettings.currentRenderPipeline;
            string rpName = rp != null ? rp.GetType().Name : "";
            bool urp = rpName.Contains("Universal");
            bool hdrp = rpName.Contains("HDRenderPipeline") || rpName.Contains("HighDefinition");
            Shader shader = urp ? Shader.Find("Universal Render Pipeline/Lit") : (hdrp ? Shader.Find("HDRP/Lit") : Shader.Find("Standard"));
            if (shader == null) { shader = Shader.Find("Standard"); urp = false; hdrp = false; }

            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            foreach (var d in Defs)
            {
                string mp = matDir + "/" + d.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, mp); }
                else mat.shader = shader;
                Texture2D bc = null, nm = null, ms = null;
                if (!string.IsNullOrEmpty(d.tex))
                {
                    bc = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/T_" + d.tex + "_BaseColor.png");
                    nm = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/T_" + d.tex + "_Normal.png");
                    ms = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "/T_" + d.tex + "_MetallicSmoothness.png");
                }
                Apply(mat, d, bc, nm, ms, urp, hdrp);
                EditorUtility.SetDirty(mat);
                if (mi != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), d.name), mat);
            }
            AssetDatabase.SaveAssets();
            if (mi != null) mi.SaveAndReimport();
            MakePrefab(fbxPath, dir);
            AssetDatabase.SaveAssets();
            Debug.Log("[ParkKultury] Setup done for " + (urp ? "URP" : (hdrp ? "HDRP" : "Built-in")) + ". Prefab: " + dir + "/PK_NorthVestibule.prefab");
        }

        static void Apply(Material m, MatDef d, Texture2D bc, Texture2D nm, Texture2D ms, bool urp, bool hdrp)
        {
            if (hdrp)
            {
                m.SetColor("_BaseColor", d.color);
                if (bc != null) m.SetTexture("_BaseColorMap", bc);
                if (nm != null) { m.SetTexture("_NormalMap", nm); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE"); }
                m.SetFloat("_Metallic", d.metallic);
                m.SetFloat("_Smoothness", d.smoothness);
                if (d.emissionIntensity > 0f) { m.SetColor("_EmissiveColorLDR", d.emission); m.SetColor("_EmissiveColor", d.emission * d.emissionIntensity * 100f); }
                var t = Type.GetType("UnityEditor.Rendering.HighDefinition.HDShaderUtils, Unity.RenderPipelines.HighDefinition.Editor");
                var mth = t != null ? t.GetMethod("ResetMaterialKeywords", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                if (mth != null) { try { mth.Invoke(null, new object[] { m }); } catch (Exception) { } }
                return;
            }
            if (urp)
            {
                m.SetColor("_BaseColor", d.color);
                if (bc != null) m.SetTexture("_BaseMap", bc);
            }
            if (m.HasProperty("_Color")) m.SetColor("_Color", d.color);
            if (bc != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", bc);
            if (nm != null) { m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            if (ms != null)
            {
                m.SetTexture("_MetallicGlossMap", ms);
                m.EnableKeyword(urp ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP");
                if (urp) m.SetFloat("_Smoothness", 1f); else m.SetFloat("_GlossMapScale", 1f);
            }
            else
            {
                m.SetFloat("_Metallic", d.metallic);
                m.SetFloat(urp ? "_Smoothness" : "_Glossiness", d.smoothness);
            }
            if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (d.emissionIntensity > 0f)
            {
                m.SetColor("_EmissionColor", d.emission * d.emissionIntensity);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
        }

        static void MakePrefab(string fbxPath, string dir)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                      | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                bool door = false;
                for (var p = t; p != null; p = p.parent) { if (p.name.StartsWith("SM_PK_DoorLeaf")) { door = true; break; } }
                if (!door) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            var probeGo = new GameObject("ReflectionProbe_Interior");
            probeGo.transform.SetParent(inst.transform, false);
            probeGo.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(9.6f, 10.6f, 9.6f);
            probe.boxProjection = true;
            probe.importance = 2;
            PrefabUtility.SaveAsPrefabAsset(inst, dir + "/PK_NorthVestibule.prefab");
            UnityEngine.Object.DestroyImmediate(inst);
        }
    }
}
