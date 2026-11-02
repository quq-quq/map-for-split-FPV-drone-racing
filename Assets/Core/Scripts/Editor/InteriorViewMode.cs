using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Interior view for fly-throughs: hides every face that looks out into the rock, so from outside the map
// you see straight into tunnels, stations and passages (like a single-sided tube). Colliders are untouched.
public static class InteriorViewMode
{
    const string AssetPath = "Assets/Core/Visual/Models/InteriorView_Meshes.asset";
    const float Probe = 0.2f;       // step off the face before probing
    const float Escape = 1000f;     // a probe ray that travels this far without a hit has reached open space

    static readonly Vector3[] Dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

    [MenuItem("Tools/Interior View/Enable")]
    public static void Enable()
    {
        Disable();
        Physics.SyncTransforms();
        AssetDatabase.DeleteAsset(AssetPath);
        var container = new Mesh { name = "InteriorView" };
        AssetDatabase.CreateAsset(container, AssetPath);

        var filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        int changed = 0;
        try
        {
            for (int f = 0; f < filters.Length; f++)
            {
                var mf = filters[f];
                if (f % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Interior View", mf.name, (float)f / filters.Length)) break;
                if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                var culled = BuildInterior(mf);
                if (culled == null) continue;
                AssetDatabase.AddObjectToAsset(culled, container);
                var keep = mf.gameObject.AddComponent<InteriorViewOriginal>();
                keep.original = mf.sharedMesh;
                mf.sharedMesh = culled;
                changed++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Interior View: " + changed + " meshes trimmed.");
    }

    [MenuItem("Tools/Interior View/Disable")]
    public static void Disable()
    {
        int restored = 0;
        foreach (var keep in Object.FindObjectsByType<InteriorViewOriginal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mf = keep.GetComponent<MeshFilter>();
            if (mf != null && keep.original != null) mf.sharedMesh = keep.original;
            Object.DestroyImmediate(keep);
            restored++;
        }
        if (restored > 0)
        {
            AssetDatabase.DeleteAsset(AssetPath);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("Interior View: " + restored + " meshes restored.");
        }
    }

    static Mesh BuildInterior(MeshFilter mf)
    {
        var src = mf.sharedMesh;
        var tr = mf.transform;
        var v = src.vertices;
        var world = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) world[i] = tr.TransformPoint(v[i]);

        var subs = new List<int>[src.subMeshCount];
        int removed = 0;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var t = src.GetTriangles(s);
            var keep = new List<int>(t.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                var a = world[t[i]]; var b = world[t[i + 1]]; var c = world[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f || !FacesOutward((a + b + c) / 3f, n.normalized)) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                else removed++;
            }
            subs[s] = keep;
        }
        if (removed == 0) return null;

        var m = Object.Instantiate(src);
        m.name = src.name + "_interior";
        if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        for (int s = 0; s < subs.Length; s++) m.SetTriangles(subs[s], s);
        m.RecalculateBounds();
        return m;
    }

    // A face looks outward when the space in front of it is open: some axis ray from there never hits anything.
    static bool FacesOutward(Vector3 centre, Vector3 normal)
    {
        var p = centre + normal * Probe;
        foreach (var d in Dirs)
            if (!Physics.Raycast(p, d, Escape, ~0, QueryTriggerInteraction.Ignore)) return true;
        return false;
    }
}
