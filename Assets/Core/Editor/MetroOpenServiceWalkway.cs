using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class MetroOpenServiceWalkway
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/Meshes/";
    static readonly float[] PipeHeights={.40f,.65f,.90f,1.55f,2.60f,2.90f};
    sealed class Writer
    {
        public readonly List<Vector3> v=new List<Vector3>(),n=new List<Vector3>();
        public readonly List<Vector2> uv=new List<Vector2>();
        public readonly List<Vector4> tan=new List<Vector4>();
        public readonly List<int>[] triangles;
        public Writer(int sub){triangles=Enumerable.Range(0,sub).Select(i=>new List<int>()).ToArray();}
        public void Add(Mesh source,int[] ids,int offset,int sub,Func<Vector3,Vector3> point=null,Func<Vector3,Vector3> direction=null)
        {
            var sv=source.vertices;var sn=source.normals;var su=source.uv;var st=source.tangents;
            for(int j=0;j<3;j++)
            {
                int i=ids[offset+j],next=v.Count;v.Add(point==null?sv[i]:point(sv[i]));
                n.Add(direction==null?sn[i]:direction(sn[i]));uv.Add(su.Length>i?su[i]:Vector2.zero);
                var t=st.Length>i?st[i]:new Vector4(1,0,0,1);var d=direction==null?new Vector3(t.x,t.y,t.z):direction(new Vector3(t.x,t.y,t.z));
                tan.Add(new Vector4(d.x,d.y,d.z,t.w));triangles[sub].Add(next);
            }
        }
        public Mesh Mesh(string name)
        {
            var m=new Mesh{name=name};if(v.Count>65535)m.indexFormat=IndexFormat.UInt32;
            m.SetVertices(v);m.SetNormals(n);m.SetUVs(0,uv);m.SetTangents(tan);m.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)m.SetTriangles(triangles[i],i,false);
            m.RecalculateBounds();return m;
        }
    }
    static Mesh Store(Mesh source,string file)
    {
        source.name=file;string path=Folder+file+".asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(m==null){AssetDatabase.CreateAsset(source,path);return source;}
        m.Clear();m.indexFormat=source.indexFormat;m.vertices=source.vertices;m.normals=source.normals;m.uv=source.uv;m.tangents=source.tangents;m.subMeshCount=source.subMeshCount;
        for(int i=0;i<source.subMeshCount;i++)m.SetTriangles(source.GetTriangles(i),i,false);
        m.bounds=source.bounds;m.name=file;m.MarkModified();m.UploadMeshData(false);EditorUtility.SetDirty(m);Object.DestroyImmediate(source);return m;
    }
    static void Assign(MeshFilter mf,Mesh m)
    {
        Undo.RecordObject(mf,"Open service gallery");mf.sharedMesh=m;
        foreach(var c in mf.GetComponents<MeshCollider>()){Undo.RecordObject(c,"Remove gallery canopy collision");c.sharedMesh=null;c.sharedMesh=m;}
    }
    static List<int[]> Components(Mesh m,int sub)
    {
        var vertices=m.vertices;var tris=m.GetTriangles(sub);var parent=Enumerable.Range(0,vertices.Length).ToArray();
        Func<int,int> find=null;find=i=>parent[i]==i?i:(parent[i]=find(parent[i]));Action<int,int> union=(a,b)=>parent[find(a)]=find(b);
        var weld=new Dictionary<string,int>();
        foreach(int i in tris.Distinct())
        {
            var p=vertices[i];string key=Mathf.RoundToInt(p.x*10000)+"/"+Mathf.RoundToInt(p.y*10000)+"/"+Mathf.RoundToInt(p.z*10000);int other;
            if(weld.TryGetValue(key,out other))union(i,other);else weld[key]=i;
        }
        for(int i=0;i<tris.Length;i+=3){union(tris[i],tris[i+1]);union(tris[i],tris[i+2]);}
        return Enumerable.Range(0,tris.Length/3).GroupBy(i=>find(tris[i*3])).Select(g=>g.SelectMany(i=>new[]{tris[i*3],tris[i*3+1],tris[i*3+2]}).ToArray()).ToList();
    }
    static Mesh Source(MeshFilter mf)
    {
        string path=Folder+"GallerySource_"+mf.name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        return saved!=null?saved:Store(Object.Instantiate(mf.sharedMesh),"GallerySource_"+mf.name);
    }
    static void Straight(MeshFilter mf)
    {
        var source=Source(mf);var mats=mf.GetComponent<Renderer>().sharedMaterials;var writer=new Writer(source.subMeshCount);float length=source.bounds.size.z;
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            string name=mats[sub].name;if(name.Contains("Ceiling"))continue;
            if(!name.Contains("Details")&&!name.Contains("Neon"))
            {
                var ids=source.GetTriangles(sub);for(int i=0;i<ids.Length;i+=3)writer.Add(source,ids,i,sub);continue;
            }
            foreach(var ids in Components(source,sub))
            {
                var b=new Bounds(source.vertices[ids[0]],Vector3.zero);foreach(int i in ids)b.Encapsulate(source.vertices[i]);
                bool lamp=name.Contains("Neon")||(b.center.y>3.35f&&Mathf.Abs(b.center.x)>1.3f);
                bool negative=b.center.x<0;
                if(lamp)
                {
                    if(length<2)continue;
                    var pivot=new Vector3(negative?-1.45f:1.45f,3.43f,length*.5f);
                    var target=new Vector3(1.53f,2.05f,length*(negative?.75f:.25f));
                    var rotation=Quaternion.FromToRotation(Vector3.down,Vector3.left);
                    for(int i=0;i<ids.Length;i+=3)writer.Add(source,ids,i,sub,p=>rotation*(p-pivot)+target,d=>rotation*d);
                }
                else
                {
                    int bucket=b.center.y<1.2f?0:b.center.y<2.2f?1:2;
                    float oldHeight=bucket==0?.9f:bucket==1?1.55f:3.26f;
                    float newHeight=PipeHeights[bucket*2+(negative?1:0)],baseX=bucket==2?1.07f:1.44f;
                    Func<Vector3,Vector3> point=p=>new Vector3((negative?-p.x:p.x)+(1.44f-baseX),newHeight+(negative?oldHeight-p.y:p.y-oldHeight),p.z);
                    Func<Vector3,Vector3> normal=d=>negative?new Vector3(-d.x,-d.y,d.z):d;
                    for(int i=0;i<ids.Length;i+=3)writer.Add(source,ids,i,sub,point,normal);
                }
            }
        }
        Assign(mf,Store(writer.Mesh("Open gallery"),"OpenGallery_"+mf.name));
    }
    static void Corner(MeshFilter mf)
    {
        var source=Source(mf);var mats=mf.GetComponent<Renderer>().sharedMaterials;var writer=new Writer(source.subMeshCount);
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            string name=mats[sub].name;if(!name.Contains("Floor")&&!name.Contains("Wall"))continue;
            var ids=source.GetTriangles(sub);for(int i=0;i<ids.Length;i+=3)writer.Add(source,ids,i,sub);
        }
        Assign(mf,Store(writer.Mesh("Open gallery corner"),"OpenGallery_"+mf.name));
    }
    static void RemoveReturn(MeshFilter mf)
    {
        var source=Source(mf);var writer=new Writer(source.subMeshCount);var vertices=source.vertices;
        for(int s=0;s<source.subMeshCount;s++)
        {
            var ids=source.GetTriangles(s);for(int i=0;i<ids.Length;i+=3)
            {
                var a=mf.transform.TransformPoint(vertices[ids[i]]);var b=mf.transform.TransformPoint(vertices[ids[i+1]]);var c=mf.transform.TransformPoint(vertices[ids[i+2]]);
                if(Mathf.Abs(a.z-37.86f)<.01f&&Mathf.Abs(b.z-37.86f)<.01f&&Mathf.Abs(c.z-37.86f)<.01f)continue;
                writer.Add(source,ids,i,s);
            }
        }
        Assign(mf,Store(writer.Mesh("Stair wall without return"),"OpenGallery_StairHall_NoReturn"));
    }
    static GameObject Place(string name,Mesh mesh,Material[] materials,Transform parent,bool solid)
    {
        var existing=parent.Find(name);var go=existing!=null?existing.gameObject:new GameObject(name);
        if(existing==null){go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();if(solid)go.AddComponent<MeshCollider>();Undo.RegisterCreatedObjectUndo(go,"Add wall services");}
        Assign(go.GetComponent<MeshFilter>(),mesh);go.GetComponent<Renderer>().sharedMaterials=materials;go.isStatic=true;return go;
    }
    static Mesh Bend()
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        const int segments=18,sides=8;
        foreach(float height in PipeHeights)
        {
            int offset=vertices.Count;
            for(int j=0;j<=segments;j++)for(int i=0;i<sides;i++)
            {
                float a=Mathf.PI+j*Mathf.PI*.5f/segments,t=i*Mathf.PI*2/sides;
                var radial=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var n=Vector3.up*Mathf.Cos(t)+radial*Mathf.Sin(t);
                vertices.Add(new Vector3(32.8f,-23.27f+height,37.86f)+radial*3.04f+n*.055f);normals.Add(n);uv.Add(new Vector2((float)i/sides,(float)j/segments*2));
            }
            for(int j=0;j<segments;j++)for(int i=0;i<sides;i++)
            {
                int a=offset+j*sides+i,b=offset+j*sides+(i+1)%sides,c=a+sides,d=b+sides;
                foreach(var t in new[]{new[]{a,b,c},new[]{b,d,c}})
                {
                    if(Vector3.Dot(Vector3.Cross(vertices[t[1]]-vertices[t[0]],vertices[t[2]]-vertices[t[0]]),normals[t[0]])<0){int swap=t[1];t[1]=t[2];t[2]=swap;}
                    triangles.AddRange(t);
                }
            }
        }
        var m=new Mesh{name="Wall pipe corner"};m.SetVertices(vertices);m.SetNormals(normals);m.SetUVs(0,uv);m.SetTriangles(triangles,0);m.RecalculateTangents();return m;
    }
    static void CornerLamp(Transform parent)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Core/Visual/Models/SubwayCorridor/FBX/Corridor_Straight.fbx").OfType<Mesh>().First();
        var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Core/Visual/Models/SubwayCorridor/FBX/Corridor_Straight.fbx");
        var mats=template.GetComponentInChildren<Renderer>().sharedMaterials;var writer=new Writer(2);var vertices=source.vertices;
        var inward=new Vector3(.7071f,0,.7071f);var tangent=new Vector3(.7071f,0,-.7071f);
        var rotation=Quaternion.LookRotation(tangent,-inward);var pivot=new Vector3(1.45f,3.43f,1.75f);var target=new Vector3(30.54f,-21.22f,35.60f)+inward*.05f;
        for(int s=3;s<=4;s++)
        {
            var ids=source.GetTriangles(s);for(int i=0;i<ids.Length;i+=3)
            {
                var p=(vertices[ids[i]]+vertices[ids[i+1]]+vertices[ids[i+2]])/3;
                if(p.x<1.3f||p.y<3.35f)continue;
                writer.Add(source,ids,i,s-3,v=>rotation*(v-pivot)+target,n=>rotation*n);
            }
        }
        Place("Corner wall lamp",Store(writer.Mesh("Corner wall lamp"),"OpenGallery_CornerLamp"),new[]{mats[3],mats[4]},parent,false);
        Place("Curved wall pipes",Store(Bend(),"OpenGallery_CornerPipes"),new[]{mats[3]},parent,false);
    }
    static void Rail(Transform parent,Vector3 a,Vector3 b,float radius,Material material,string name)
    {
        if(parent.Find(name)!=null)return;
        var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent);go.transform.position=(a+b)*.5f;
        go.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);go.transform.localScale=new Vector3(radius*2,Vector3.Distance(a,b)*.5f,radius*2);
        go.GetComponent<Renderer>().sharedMaterial=material;go.isStatic=true;Undo.RegisterCreatedObjectUndo(go,"Join open walkway railings");
    }
    public static string Apply()
    {
        var root=GameObject.Find("ServiceCavern");if(root==null)throw new InvalidOperationException("Cavern missing");
        var modules=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Modules");
        RemoveReturn(modules.transform.Find("15b_Corridor_StairHall").GetComponent<MeshFilter>());
        int count=0;
        foreach(var mf in modules.GetComponentsInChildren<MeshFilter>())
        {
            int index;if(!int.TryParse(mf.name.Substring(0,2),out index)||index<21||index>38)continue;
            if(index==21)Corner(mf);else Straight(mf);count++;
        }
        var frames=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Frames");
        foreach(string name in new[]{"Frame_11","Frame_12","Frame_13","Frame_14","Frame_15"})
        {
            var t=frames.transform.Find(name);if(t!=null){Undo.RecordObject(t.gameObject,"Remove gallery canopy supports");t.gameObject.SetActive(false);}
        }
        var group=root.transform.Find("08 Open service walkway");
        if(group==null){var go=new GameObject("08 Open service walkway");go.transform.SetParent(root.transform,false);Undo.RegisterCreatedObjectUndo(go,"Open service walkway");group=go.transform;}
        CornerLamp(group);
        var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/Core/Visual/Environment/ServiceCavern/Materials/Guardrail_DarkSteel.mat");
        Rail(group,new Vector3(32.7f,-22.2f,37.72f),new Vector3(34,-22.2f,37.72f),.04f,steel,"Corner top railing");
        Rail(group,new Vector3(32.7f,-22.73f,37.72f),new Vector3(34,-22.73f,37.72f),.025f,steel,"Corner middle railing");
        Rail(group,new Vector3(33,-23.27f,37.72f),new Vector3(33,-22.2f,37.72f),.035f,steel,"Corner railing post");
        var lights=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/ServicePassage2_Lights");
        foreach(var light in lights.GetComponentsInChildren<Light>())
        {
            int i;if(!int.TryParse(light.name.Replace("Light_",""),out i)||i<7||i>12)continue;
            Undo.RecordObject(light.transform,"Mount gallery lights at the wall");
            light.transform.position=i==7?new Vector3(30.65f,-21.20f,35.72f):new Vector3(light.transform.position.x,-21.20f,34.94f);
        }
        foreach(var light in root.transform.Find("05 Cavern lighting").GetComponentsInChildren<Light>())
        {
            if(!light.name.StartsWith("Warm canopy flood"))continue;
            Undo.RecordObject(light.transform,"Wall mount cavern floodlight");
            var p=light.transform.position;p.y=-21.16f;p.z=34.95f;light.transform.position=p;
            light.transform.rotation=Quaternion.LookRotation(new Vector3(-.15f,.3f,1));
        }
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Removed stair-hall return; opened gallery modules="+count+"; pipes and fixtures mounted along inner wall; five canopy arches removed.";
    }
}
