using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Blender 5 procedural materials are baked by the export worker. This step maps
// those PBR assets to URP and freezes the tested FBX axis correction into the mesh.
public static class MetroTunnelNetworkImport
{
    const string Folder="Assets/Core/Visual/Environment/MetroTunnelNetwork/";
    [Serializable] class Coordinates { public Vector3 min,max; public Vector3[] points; }
    static Texture2D Texture(string file,bool normal)
    {
        string path=Folder+"Textures/"+file+".png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var ti=(TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        ti.sRGBTexture=!normal;ti.convertToNormalmap=false;ti.flipGreenChannel=false;
        ti.maxTextureSize=4096;ti.mipmapEnabled=true;ti.anisoLevel=8;ti.wrapMode=TextureWrapMode.Clamp;
        ti.filterMode=FilterMode.Trilinear;ti.textureCompression=TextureImporterCompression.CompressedHQ;
        ti.alphaSource=TextureImporterAlphaSource.None;ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material Material(string name,float metallic,float smoothness)
    {
        string dir=Folder+"Materials";if(!AssetDatabase.IsValidFolder(dir))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"Materials");
        string path=dir+"/MTN_"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
        material.name="MTN_"+name;material.SetColor("_BaseColor",Color.white);
        material.SetTexture("_BaseMap",Texture(name+"_BaseColor",false));
        material.SetTexture("_BumpMap",Texture(name+"_Normal",true));material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",1);
        material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smoothness);
        material.SetFloat("_Cull",2);material.doubleSidedGI=true;material.enableInstancing=true;
        EditorUtility.SetDirty(material);return material;
    }
    static Mesh Store(Mesh source,string path)
    {
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){AssetDatabase.CreateAsset(source,path);return source;}
        mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.tangents=source.tangents;mesh.uv=source.uv;
        if(source.uv2.Length==source.vertexCount)mesh.uv2=source.uv2;
        mesh.subMeshCount=source.subMeshCount;for(int i=0;i<source.subMeshCount;i++)mesh.SetTriangles(source.GetTriangles(i),i,false);
        mesh.bounds=source.bounds;mesh.MarkModified();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);Object.DestroyImmediate(source);return mesh;
    }
    static object CoordinateAudit(Mesh mesh)
    {
        var data=JsonUtility.FromJson<Coordinates>(System.IO.File.ReadAllText(Folder+"SourceCoordinates.json"));
        float maximum=0;var v=mesh.vertices;
        foreach(var point in data.points){float nearest=float.PositiveInfinity;foreach(var p in v)nearest=Mathf.Min(nearest,(point-p).sqrMagnitude);maximum=Mathf.Max(maximum,Mathf.Sqrt(nearest));}
        float boundsError=Mathf.Max((mesh.bounds.min-data.min).magnitude,(mesh.bounds.max-data.max).magnitude);
        if(maximum>.001f||boundsError>.002f)throw new InvalidOperationException("Blender/Unity coordinate mismatch: vertices="+maximum+", bounds="+boundsError);
        return new{sourceSamples=data.points.Length,maximumVertexError=maximum,maximumBoundsError=boundsError};
    }
    static Mesh BothSides(Mesh source)
    {
        int count=source.vertexCount;var v=source.vertices;var n=source.normals;var t=source.tangents;
        var vertices=v.Concat(v).ToArray();var normals=n.Concat(n.Select(p=>-p)).ToArray();
        // Negate the complete tangent frame on the interior side, matching the
        // original Blender back-face shading after its normal-map perturbation.
        var tangents=t.Concat(t.Select(p=>new Vector4(-p.x,-p.y,-p.z,-p.w))).ToArray();
        var m=new Mesh{name="MetroTunnelNetwork_Unity",indexFormat=IndexFormat.UInt32};m.vertices=vertices;m.normals=normals;m.tangents=tangents;
        m.uv=source.uv.Concat(source.uv).ToArray();m.subMeshCount=source.subMeshCount;
        for(int s=0;s<source.subMeshCount;s++)
        {
            var a=source.GetTriangles(s);var both=new int[a.Length*2];Array.Copy(a,both,a.Length);
            for(int i=0;i<a.Length;i+=3){both[a.Length+i]=a[i+2]+count;both[a.Length+i+1]=a[i+1]+count;both[a.Length+i+2]=a[i]+count;}
            m.SetTriangles(both,s,false);
        }
        m.RecalculateBounds();Unwrapping.GenerateSecondaryUVSet(m);return m;
    }
    static Mesh ReverseCollision(Mesh source)
    {
        var m=new Mesh{name="MetroTunnelNetwork_CollisionBack",indexFormat=IndexFormat.UInt32};m.vertices=source.vertices;var ids=source.triangles;
        for(int i=0;i<ids.Length;i+=3){int swap=ids[i];ids[i]=ids[i+2];ids[i+2]=swap;}m.triangles=ids;m.RecalculateBounds();return m;
    }
    public static object Prepare()
    {
        AssetDatabase.Refresh();
        var concrete=Material("Concrete",0,0);var steel=Material("RustedSteel",1,1-.3036363721f);
        string fbx=Folder+"MetroTunnelNetwork.fbx";AssetDatabase.ImportAsset(fbx,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
        importer.globalScale=1;importer.useFileScale=true;importer.meshCompression=ModelImporterMeshCompression.Off;
        importer.isReadable=true;importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;
        importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.generateSecondaryUV=true;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),"MTN_Concrete"),concrete);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),"MTN_RustedSteel"),steel);
        importer.SaveAndReimport();
        var imported=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
        var combine=new List<CombineInstance>();var materials=new List<Material>();var correction=Matrix4x4.Rotate(Quaternion.Euler(0,180,0));
        foreach(var f in imported.GetComponentsInChildren<MeshFilter>())
        {
            var slots=f.GetComponent<Renderer>().sharedMaterials;
            for(int s=0;s<f.sharedMesh.subMeshCount;s++)
            {
                combine.Add(new CombineInstance{mesh=f.sharedMesh,subMeshIndex=s,transform=correction*f.transform.localToWorldMatrix});
                materials.Add(slots[s].name.Contains("RustedSteel")?steel:concrete);
            }
        }
        var mesh=new Mesh{name="MetroTunnelNetwork_Unity",indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combine.ToArray(),false,true);mesh.RecalculateBounds();
        Object.DestroyImmediate(imported);var audit=CoordinateAudit(mesh);var source=Store(mesh,Folder+"MetroTunnelNetwork_Surface.asset");
        var back=Store(ReverseCollision(source),Folder+"MetroTunnelNetwork_CollisionBack.asset");
        mesh=Store(BothSides(source),Folder+"MetroTunnelNetwork_Unity.asset");
        var go=new GameObject("TunnelNetwork [Blender]");go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials.ToArray();renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.receiveShadows=true;
        go.AddComponent<MeshCollider>().sharedMesh=source;go.AddComponent<MeshCollider>().sharedMesh=back;go.isStatic=true;
        var prefab=PrefabUtility.SaveAsPrefabAsset(go,Folder+"MetroTunnelNetwork.prefab");Object.DestroyImmediate(go);
        var existing=GameObject.Find("TunnelNetwork [Blender]");if(existing!=null)Undo.DestroyObjectImmediate(existing);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);instance.name="TunnelNetwork [Blender]";instance.transform.SetParent(GameObject.Find("MetroTunnels").transform,true);
        Undo.RegisterCreatedObjectUndo(instance,"Import baked Blender tunnel network");
        Physics.SyncTransforms();AssetDatabase.SaveAssets();
        return new{sceneObject=instance.name,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,materials=materials.Select(m=>m.name).ToArray(),bounds=mesh.bounds.ToString(),coordinates=audit};
    }
    public static object ReplaceGreybox(bool retainUnmodeledLoops)
    {
        if(GameObject.Find("TunnelNetwork [Blender]")==null)throw new InvalidOperationException("Prepare and verify the new model first");
        var root=GameObject.Find("MetroTunnels").transform;var targets=new List<GameObject>();
        // The vertical junction's Extension includes its floor, gallery, railings
        // and lights. It is architecture belonging to the junction and must survive
        // a tunnel-shell replacement.
        var unmodeled=new[]{"Tubes/A_SouthLoop","Tubes/B_WestLoop"};
        if(!retainUnmodeledLoops)foreach(var path in unmodeled){var t=root.Find(path);if(t!=null)targets.Add(t.gameObject);}
        var tubes=root.Find("Tubes");foreach(var child in tubes.Cast<Transform>())if(!child.gameObject.activeSelf)targets.Add(child.gameObject);
        foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!f.name.StartsWith("Shell")&&f.name!="Collar")continue;
            var junction=root.Find("VerticalJunction");
            if(junction!=null&&f.transform.IsChildOf(junction))continue;
            if(f.transform.IsChildOf(root.Find("AbandonedStation")))continue;
            if(retainUnmodeledLoops&&unmodeled.Any(path=>root.Find(path)!=null&&f.transform.IsChildOf(root.Find(path))))continue;
            if(!targets.Any(g=>f.transform.IsChildOf(g.transform)))targets.Add(f.gameObject);
        }
        var tubeN=GameObject.Find("MetroKosino/TubeN_R");if(tubeN!=null)targets.Add(tubeN);
        targets=targets.Distinct().Where(g=>!targets.Any(other=>other!=g&&g.transform.IsChildOf(other.transform))).ToList();
        if(targets.Count==0)return new{removedObjects=0,alreadyReplaced=true};
        string backupDir=Folder+"LegacyBackup";if(!AssetDatabase.IsValidFolder(backupDir))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"LegacyBackup");
        var backup=new GameObject("Legacy tunnel greybox backup");backup.SetActive(false);
        var paths=new List<string>();
        foreach(var target in targets)
        {
            string path=target.name;var parent=target.transform.parent;while(parent!=null){path=parent.name+"/"+path;parent=parent.parent;}paths.Add(path);
            var copy=Object.Instantiate(target,target.transform.parent);copy.transform.SetParent(backup.transform,true);copy.name=path.Replace('/','_');
        }
        PrefabUtility.SaveAsPrefabAsset(backup,backupDir+"/OldTunnelGreybox.prefab");Object.DestroyImmediate(backup);
        Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Replace old tunnel greybox with Blender network");
        int renderers=targets.Sum(g=>g.GetComponentsInChildren<Renderer>(true).Length);
        foreach(var target in targets)Undo.DestroyObjectImmediate(target);
        System.IO.File.WriteAllText(Folder+"ReplacementReport.txt","Removed scene objects (backup: LegacyBackup/OldTunnelGreybox.prefab):\n"+string.Join("\n",paths.ToArray()));
        Physics.SyncTransforms();AssetDatabase.SaveAssets();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{removedObjects=targets.Count,removedRenderers=renderers,retainedUnmodeledLoops=retainUnmodeledLoops,newMeshRenderers=GameObject.Find("TunnelNetwork [Blender]").GetComponentsInChildren<MeshRenderer>().Length};
    }
}
