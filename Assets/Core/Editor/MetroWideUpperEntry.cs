using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class MetroWideUpperEntry
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/";
    const float Floor=-1.3f,Ceiling=4f;
    static readonly Vector3 RoomMin=new Vector3(29.6f,Floor,86.86f),RoomMax=new Vector3(40,Ceiling,96.4f);
    static float Smooth(float a,float b,float value){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));}
    static Mesh Store(Mesh source,string name)
    {
        string path=Folder+"Meshes/"+name+".asset";source.name=name;
        var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(source,path);return source;}
        old.Clear();old.vertices=source.vertices;old.normals=source.normals;old.uv=source.uv;old.tangents=source.tangents;old.triangles=source.triangles;old.bounds=source.bounds;
        old.MarkModified();old.UploadMeshData(false);EditorUtility.SetDirty(old);Object.DestroyImmediate(source);return old;
    }
    static void Box(Transform parent,string name,Vector3 min,Vector3 max,Material material)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(go,"Build wide flight entrance");
        var points=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
        {
            int a=(axis+1)%3,b=(axis+2)%3;var n=Vector3.zero;n[axis]=side==0?-1:1;int start=points.Count;
            foreach(var corner in new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)})
            {
                var p=min;p[axis]=side==0?min[axis]:max[axis];p[a]=Mathf.Lerp(min[a],max[a],corner.x);p[b]=Mathf.Lerp(min[b],max[b],corner.y);
                points.Add(p);normals.Add(n);uv.Add(new Vector2(p[a],p[b])/3.5f);
            }
            foreach(var face in new[]{new[]{0,1,2},new[]{0,2,3}})
            {
                int i=start+face[0],j=start+face[1],k=start+face[2];if(Vector3.Dot(Vector3.Cross(points[j]-points[i],points[k]-points[i]),n)<0){int t=j;j=k;k=t;}
                triangles.Add(i);triangles.Add(j);triangles.Add(k);
            }
        }
        var mesh=new Mesh();mesh.SetVertices(points);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();
        mesh=Store(mesh,"WideEntry_"+name.Replace(' ','_'));
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
    }
    static void Apron(Transform root)
    {
        var terrain=root.GetComponentInChildren<Terrain>();var td=terrain.terrainData;var pos=terrain.transform.position;var size=td.size;int n=td.heightmapResolution;
        string baselinePath=Folder+"Terrain/ServiceCavern_Floor_BeforeWideEntry.asset";
        var baseline=AssetDatabase.LoadAssetAtPath<TerrainData>(baselinePath);
        if(baseline==null)
        {
            MetroCaveTerrain.GradeUnderStairs();
            baseline=Object.Instantiate(td);baseline.name="ServiceCavern_Floor_BeforeWideEntry";AssetDatabase.CreateAsset(baseline,baselinePath);
        }
        var source=baseline.GetHeights(0,0,n,n);
        var heights=td.GetHeights(0,0,n,n);Undo.RegisterCompleteObjectUndo(td,"Blend broad upper entrance apron");
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            float wx=pos.x+x*size.x/(n-1),wz=pos.z+z*size.z/(n-1);
            float entranceX=Smooth(32.9f,34.8f,wx)*(1-Smooth(40.3f,46.5f,wx));
            float approach=Smooth(79,86.7f,wz)*(1-Smooth(97,99,wz));
            if(entranceX*approach<=0)continue;
            float original=pos.y+source[z,x]*baseline.size.y;
            float target=Mathf.Max(original,Mathf.Lerp(original,-1.67f,entranceX*approach));
            heights[z,x]=(target-pos.y)/size.y;
        }
        td.SetHeights(0,0,heights);var holes=td.GetHoles(0,0,td.holesResolution,td.holesResolution);int h=td.holesResolution;
        for(int z=0;z<h;z++)for(int x=0;x<h;x++)
        {
            float wx=pos.x+(x+.5f)*size.x/h,wz=pos.z+(z+.5f)*size.z/h;
            if(wx>29.35f&&wx<40.4f&&wz>85.8f&&wz<97)holes[z,x]=true;
        }
        td.SetHoles(0,0,holes);EditorUtility.SetDirty(td);terrain.Flush();
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        MetroCaveStoneFinish.Audit();var settle=typeof(MetroCaveStoneFinish).GetMethod("Settle",flags);
        // Move the boulder away from the flight lane, then reseat the affected scree.
        int relocated=0;
        foreach(string group in new[]{"01 Rock outcrops","02 Talus and loose stones","03 Sparse coloured minerals","07 Stair slope stone scatter","12 Coloured stone clusters"})
        foreach(var f in root.Find(group).GetComponentsInChildren<MeshFilter>())
        {
            var r=f.GetComponent<Renderer>();if(r==null||f.sharedMesh.vertexCount<3)continue;var b=r.bounds;
            if(f.name=="Limestone 064 - bank embedded"&&b.min.x<40.3f&&b.max.z>86.3f)
            {
                Undo.RecordObject(f.transform,"Clear broad upper flight lane");f.transform.position+=new Vector3(47-b.center.x,0,85-b.center.z);settle.Invoke(null,new object[]{f});continue;
            }
            int id;
            if(group=="01 Rock outcrops"&&(f.name.StartsWith("Embedded roof")||f.name.StartsWith("South wall outcrop")||(f.name.StartsWith("Limestone ")&&int.TryParse(f.name.Substring(10),out id)&&id>=46&&id<=67)))continue;
            if(b.center.x>=33.2f&&b.center.x<=46.8f&&b.center.z>=79&&b.center.z<=98&&b.max.y<1.5f)
                settle.Invoke(null,new object[]{f});
            b=r.bounds;
            if(b.min.x<40.35f&&b.max.x>29.3f&&b.min.z<96.8f&&b.max.z>86.3f&&b.max.y>Floor-.02f&&b.min.y<Ceiling)
            {
                Undo.RecordObject(f.transform,"Keep vestibule floor free of stones");
                f.transform.position+=new Vector3(44.8f+(relocated%3)*1.4f-b.center.x,0,81+(relocated*7%11)*.45f-b.center.z);
                settle.Invoke(null,new object[]{f});relocated++;
            }
        }
        MetroCaveStoneFinish.ReseatUnsupportedGround();
    }
    static void AddLamp(Transform parent,string name,Vector3 position,bool point)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"ServiceStair_Lamp.prefab");var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.name=name;go.transform.SetParent(parent,false);go.transform.position=position;go.isStatic=true;Undo.RegisterCreatedObjectUndo(go,"Light wide upper vestibule");
        if(point)
        {
            var child=new GameObject("Warm light");child.transform.SetParent(go.transform,false);child.transform.localPosition=Vector3.down*.3f;
            var light=child.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.86f,.68f);light.intensity=1.5f;light.range=12;light.shadows=LightShadows.Soft;
        }
    }
    static object Build(bool rebuildShell)
    {
        var root=GameObject.Find("ServiceCavern");var route=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor").transform;
        var group=root.transform.Find("14 Wide upper flight entrance");
        if(group==null){var go=new GameObject("14 Wide upper flight entrance");go.transform.SetParent(root.transform,false);group=go.transform;Undo.RegisterCreatedObjectUndo(go,"Widen upper cave entrance");}
        foreach(var child in group.Cast<Transform>().ToArray())Undo.DestroyObjectImmediate(child.gameObject);
        foreach(string name in new[]{"04_Corridor_Turn90_R","05_Corridor_Straight_0p44","06_Corridor_Straight"})
        {
            var go=route.Find("Modules/"+name).gameObject;Undo.RecordObject(go,"Replace narrow upper approach with flight vestibule");go.SetActive(false);
            var oldFloor=root.transform.Find("13 Solid service passage floor/"+name);if(oldFloor!=null){Undo.RecordObject(oldFloor.gameObject,"Join widened floor");oldFloor.gameObject.SetActive(false);}
        }
        foreach(string name in new[]{"Frame_3","Frame_4"}){var frame=route.Find("Frames/"+name);Undo.RecordObject(frame.gameObject,"Clear narrow upper entrance frame");frame.gameObject.SetActive(false);}
        var support=root.transform.Find("11 Stair ceiling supports/Steel support 08");
        if(support!=null&&support.position.x>-.1f){Undo.RecordObject(support,"Move roof support beside flight lane");support.position+=Vector3.left*2.94f;}
        if(rebuildShell)MetroCavePortals.RebuildForWideEntry();
        Apron(root.transform);
        string materials="Assets/Core/Visual/Models/SubwayCorridor/FBX/Materials/";
        var floor=AssetDatabase.LoadAssetAtPath<Material>(materials+"M_Corridor_Floor.mat");var wall=AssetDatabase.LoadAssetAtPath<Material>(materials+"M_Corridor_Wall.mat");
        var ceiling=AssetDatabase.LoadAssetAtPath<Material>(materials+"M_Corridor_Ceiling.mat");var steel=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/StairRoof_SupportSteel.mat");
        Box(group,"Solid vestibule floor",new Vector3(29.6f,-1.75f,86.86f),new Vector3(40,Floor,96.4f),floor);
        Box(group,"Thick vestibule ceiling",new Vector3(29.3f,Ceiling,86.60f),new Vector3(40.3f,4.45f,96.7f),ceiling);
        Box(group,"East side wall",new Vector3(40,Floor,86.65f),new Vector3(40.3f,Ceiling,96.7f),wall);
        Box(group,"Rear wall",new Vector3(29.3f,Floor,96.4f),new Vector3(40.3f,Ceiling,96.7f),wall);
        Box(group,"West wall beside stairs",new Vector3(29.3f,Floor,86.65f),new Vector3(29.6f,Ceiling,90.8f),wall);
        Box(group,"West wall beyond station exit",new Vector3(29.3f,Floor,94),new Vector3(29.6f,Ceiling,96.7f),wall);
        Box(group,"Wall above station corridor exit",new Vector3(29.3f,2.2f,90.8f),new Vector3(29.6f,Ceiling,94),wall);
        // The front frame overlaps the wall end caps, hiding the rock/lining seam.
        Box(group,"Left portal upright",new Vector3(29.20f,Floor,86.58f),new Vector3(29.63f,3.88f,87.02f),steel);
        Box(group,"Right portal upright",new Vector3(39.98f,Floor,86.58f),new Vector3(40.40f,3.88f,87.02f),steel);
        Box(group,"Broad portal lintel",new Vector3(29.20f,3.88f,86.58f),new Vector3(40.40f,4.16f,87.02f),steel);
        var originalLamp=root.transform.Find("10 Stair lamps and guardrail/Ceiling lamp for Light_2");
        if(originalLamp!=null){Undo.RecordObject(originalLamp,"Raise entrance lamp to broad ceiling");originalLamp.position=new Vector3(31.2f,4,88.61f);originalLamp.rotation=Quaternion.identity;}
        var light2=route.Find("ServicePassage2_Lights/Light_2");if(light2!=null){Undo.RecordObject(light2,"Raise upper entrance light");light2.position=new Vector3(31.2f,3.7f,88.61f);}
        AddLamp(group,"East entrance ceiling lamp",new Vector3(37.5f,4,89),true);
        AddLamp(group,"Rear vestibule ceiling lamp",new Vector3(35,4,94.5f),true);
        if(root.transform.Find("15 Service wall backing and terrain joins")!=null)MetroServiceLandscapeJoin.Apply();
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return new{width=RoomMax.x-RoomMin.x,height=Ceiling-Floor,vestibuleDepth=RoomMax.z-RoomMin.z,stationExitWidth=3.2f};
    }
    public static object Apply(){return Build(true);}
    public static object Reapply(){return Build(false);}
}
