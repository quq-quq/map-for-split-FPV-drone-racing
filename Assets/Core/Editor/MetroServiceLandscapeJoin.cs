using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Solid wall backing and a raised soil apron stitch the service route into the
// cavern. The original tiled faces remain the inner boundary of the passage.
public static class MetroServiceLandscapeJoin
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/";
    static float Smooth(float a,float b,float value){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value));}
    static string Key(Vector3 p){return Mathf.RoundToInt(p.x*10000)+"/"+Mathf.RoundToInt(p.y*10000)+"/"+Mathf.RoundToInt(p.z*10000);}
    static float Sample(float[,] h,float x,float z)
    {
        x=Mathf.Clamp(x,0,h.GetLength(1)-1);z=Mathf.Clamp(z,0,h.GetLength(0)-1);
        int a=Mathf.FloorToInt(x),b=Mathf.FloorToInt(z),c=Mathf.Min(a+1,h.GetLength(1)-1),d=Mathf.Min(b+1,h.GetLength(0)-1);
        return Mathf.Lerp(Mathf.Lerp(h[b,a],h[b,c],x-a),Mathf.Lerp(h[d,a],h[d,c],x-a),z-b);
    }
    sealed class Writer
    {
        readonly List<Vector3> v=new List<Vector3>(),n=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> tri=new List<int>();
        public void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=Vector3.Cross(b-a,c-a);if(normal.sqrMagnitude<1e-12f)return;normal.Normalize();
            foreach(var p in new[]{a,b,c})
            {
                tri.Add(v.Count);v.Add(p);n.Add(normal);var abs=new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z));
                uv.Add((abs.y>=abs.x&&abs.y>=abs.z?new Vector2(p.x,p.z):abs.x>=abs.z?new Vector2(p.z,p.y):new Vector2(p.x,p.y))/3.5f);
            }
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
        public Mesh Mesh(string name)
        {var m=new Mesh{name=name};m.SetVertices(v);m.SetNormals(n);m.SetUVs(0,uv);m.SetTriangles(tri,0);m.RecalculateBounds();m.RecalculateTangents();return m;}
    }
    static Mesh Store(Mesh source,string name)
    {
        source.name=name;string path=Folder+"Meshes/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null){AssetDatabase.CreateAsset(source,path);return source;}
        mesh.Clear();mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.uv=source.uv;mesh.tangents=source.tangents;mesh.triangles=source.triangles;mesh.bounds=source.bounds;
        mesh.MarkModified();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);Object.DestroyImmediate(source);return mesh;
    }
    static GameObject Child(Transform parent,string name)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(go,"Join service passage to cavern");return go;
    }
    static int[] InnerWall(MeshFilter f,int id)
    {
        var mesh=f.sharedMesh;var v=mesh.vertices;var mats=f.GetComponent<Renderer>().sharedMaterials;var output=new List<int>();
        for(int s=0;s<Math.Min(mesh.subMeshCount,mats.Length);s++)
        {
            if(mats[s]==null||!mats[s].name.Contains("Wall"))continue;var tri=mesh.GetTriangles(s);
            for(int i=0;i<tri.Length;i+=3)
            {
                var a=f.transform.TransformPoint(v[tri[i]]);var b=f.transform.TransformPoint(v[tri[i+1]]);var c=f.transform.TransformPoint(v[tri[i+2]]);
                var normal=Vector3.Cross(b-a,c-a).normalized;
                bool keep=Mathf.Abs(normal.y)<.1f&&(id<21?normal.x>.9f:id>21?normal.z>.9f:normal.x>0&&normal.z>0);
                if(keep){output.Add(tri[i]);output.Add(tri[i+1]);output.Add(tri[i+2]);}
            }
        }
        return output.ToArray();
    }
    static Mesh Extrude(MeshFilter f,int[] ids,int id,float frontDepth,float backDepth,bool rock,string name)
    {
        var source=f.sharedMesh.vertices;var v=new List<Vector3>();var weld=new Dictionary<string,int>();var tri=new List<int>();
        foreach(int i in ids)
        {
            var p=f.transform.TransformPoint(source[i]);float floor=id<=20?MetroCaveTerrain.StairDatum(p.z):-23.27f;
            if(Mathf.Abs(p.y-floor)<.06f)p.y-=.50f;
            string key=Key(p);int k;if(!weld.TryGetValue(key,out k)){k=v.Count;weld[key]=k;v.Add(p);}tri.Add(k);
        }
        var uniqueNormals=v.Select(p=>new List<Vector3>()).ToArray();var boundary=new Dictionary<string,List<int>>();
        for(int i=0;i<tri.Count;i+=3)
        {
            var normal=Vector3.Cross(v[tri[i+1]]-v[tri[i]],v[tri[i+2]]-v[tri[i]]).normalized;
            for(int j=0;j<3;j++)
            {
                int a=tri[i+j],b=tri[i+(j+1)%3];if(!uniqueNormals[a].Any(n=>Vector3.Dot(n,normal)>.9999f))uniqueNormals[a].Add(normal);
                string key=Mathf.Min(a,b)+"/"+Mathf.Max(a,b);List<int> edge;
                if(!boundary.TryGetValue(key,out edge)){edge=new List<int>();boundary[key]=edge;}edge.Add(a);edge.Add(b);
            }
        }
        var front=new Vector3[v.Count];var back=new Vector3[v.Count];
        for(int i=0;i<v.Count;i++)
        {
            // Share the bend's miter with the two adjacent straight modules, so
            // the backing meets without wedge-shaped slits at the corner.
            Vector3 join=Vector3.zero;
            if(Mathf.Abs(v[i].x-29.6f)<.005f&&Mathf.Abs(v[i].z-37.86f)<.005f)
                join=id==21?Vector3.right:id==20?new Vector3(.9659258f,0,.2588190f):Vector3.zero;
            if(Mathf.Abs(v[i].x-32.8f)<.005f&&Mathf.Abs(v[i].z-34.66f)<.005f)
                join=id==21?Vector3.forward:id==22?new Vector3(.2588190f,0,.9659258f):Vector3.zero;
            if(join.sqrMagnitude>.5f&&!uniqueNormals[i].Any(n=>Vector3.Dot(n,join)>.9999f))uniqueNormals[i].Add(join);
            var normal=uniqueNormals[i].Aggregate(Vector3.zero,(a,b)=>a+b).normalized;
            var miter=normal/Mathf.Max(.25f,Vector3.Dot(normal,uniqueNormals[i][0]));
            front[i]=v[i]-miter*frontDepth;back[i]=v[i]-miter*backDepth;
            if(rock){front[i].x=Mathf.Min(front[i].x,93.35435f);back[i].x=Mathf.Min(back[i].x,93.35435f);}
        }
        var w=new Writer();
        for(int i=0;i<tri.Count;i+=3)
        {
            int a=tri[i],b=tri[i+1],c=tri[i+2];w.Triangle(front[a],front[b],front[c]);w.Triangle(back[c],back[b],back[a]);
        }
        foreach(var edge in boundary.Values.Where(e=>e.Count==2))w.Quad(front[edge[0]],back[edge[0]],back[edge[1]],front[edge[1]]);
        return w.Mesh(name);
    }
    static void Assign(Transform group,string name,Mesh mesh,Material material)
    {
        var go=Child(group,name);var saved=Store(mesh,mesh.name);go.AddComponent<MeshFilter>().sharedMesh=saved;
        go.AddComponent<MeshRenderer>().sharedMaterial=material;go.AddComponent<MeshCollider>().sharedMesh=saved;go.isStatic=true;
    }
    static int TerrainAprons(Transform root)
    {
        var terrain=root.GetComponentInChildren<Terrain>();var td=terrain.terrainData;var pos=terrain.transform.position;var size=td.size;int n=td.heightmapResolution;
        string path=Folder+"Terrain/ServiceCavern_Floor_BeforeWallJoin.asset";var baseline=AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if(baseline==null){baseline=Object.Instantiate(td);baseline.name="ServiceCavern_Floor_BeforeWallJoin";AssetDatabase.CreateAsset(baseline,path);}
        var before=td.GetHeights(0,0,n,n);var heights=(float[,])before.Clone();var source=baseline.GetHeights(0,0,n,n);int changed=0;
        Undo.RegisterCompleteObjectUndo(td,"Raise soil into service passage foundations");
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            float wx=pos.x+x*size.x/(n-1),wz=pos.z+z*size.z/(n-1),old=pos.y+source[z,x]*baseline.size.y;
            float gallery=Smooth(31.5f,34,wx)*(1-Smooth(92.7f,93.35f,wx))*Smooth(28.5f,32,wz)*(1-Smooth(38.6f,44.8f,wz));
            float stair=Smooth(24,27,wx)*(1-Smooth(33.2f,39.5f,wx))*Smooth(36.8f,38.5f,wz)*(1-Smooth(86.8f,88.2f,wz));
            float target=Mathf.Max(old,old+Mathf.Max(0,-23.52f-old)*gallery);
            target=Mathf.Max(target,old+Mathf.Max(0,MetroCaveTerrain.StairDatum(wz)-.25f-old)*stair);
            float current=pos.y+before[z,x]*size.y;
            if(target>current+.001f){heights[z,x]=(target-pos.y)/size.y;changed++;}
        }
        td.SetHeights(0,0,heights);var holes=td.GetHoles(0,0,td.holesResolution,td.holesResolution);int h=td.holesResolution;
        for(int z=0;z<h;z++)for(int x=0;x<h;x++)
        {
            float wx=pos.x+(x+.5f)*size.x/h,wz=pos.z+(z+.5f)*size.z/h;
            if((wx>=26&&wx<=33.5f&&wz>=37.3f&&wz<=87.2f)||(wx>=31.8f&&wz>=29.2f&&wz<=39.5f))holes[z,x]=true;
            if(pos.x+(x+1)*size.x/h>93.35435f)holes[z,x]=false;
        }
        td.SetHoles(0,0,holes);terrain.Flush();EditorUtility.SetDirty(td);
        var flags=BindingFlags.NonPublic|BindingFlags.Static;var type=typeof(MetroCaveStoneFinish);MetroCaveStoneFinish.Audit();
        var ground=(List<MeshFilter>)type.GetMethod("GroundStones",flags).Invoke(null,new object[]{root});var settle=type.GetMethod("Settle",flags);var trim=type.GetMethod("TrimAtDepot",flags);
        foreach(var f in ground)
        {
            var centre=f.GetComponent<Renderer>().bounds.center;
            float old=pos.y+Sample(before,(centre.x-pos.x)/size.x*(n-1),(centre.z-pos.z)/size.z*(n-1))*size.y;
            if(terrain.SampleHeight(centre)+pos.y-old<=.04f)continue;
            settle.Invoke(null,new object[]{f});trim.Invoke(null,new object[]{f});
        }
        MetroCaveStoneFinish.ReseatUnsupportedGround();return changed;
    }
    public static object Apply()
    {
        var root=GameObject.Find("ServiceCavern");var modules=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Modules").transform;
        var group=root.transform.Find("15 Service wall backing and terrain joins");
        if(group==null)group=Child(root.transform,"15 Service wall backing and terrain joins").transform;
        foreach(var child in group.Cast<Transform>().ToArray())Undo.DestroyObjectImmediate(child.gameObject);
        var structure=Child(group,"01 Structural wall thickness").transform;var bedrock=Child(group,"02 Bedrock packing").transform;
        var concrete=AssetDatabase.LoadAssetAtPath<Material>("Assets/Core/Visual/Models/SubwayCorridor/FBX/Materials/M_Corridor_Wall.mat");
        var rock=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/Bedrock_Triplanar.mat");int count=0;
        foreach(var f in modules.GetComponentsInChildren<MeshFilter>())
        {
            int id;if(f.name.Length<2||!int.TryParse(f.name.Substring(0,2),out id)||id<7||id>39||f.name.Contains("StairHall"))continue;
            var indices=InnerWall(f,id);if(indices.Length==0)continue;
            Assign(structure,f.name,Extrude(f,indices,id,.006f,.35f,false,"WallBacking_"+f.name),concrete);
            Assign(bedrock,f.name,Extrude(f,indices,id,.34f,8,true,"WallBedrock_"+f.name),rock);count++;
        }
        int terrain=TerrainAprons(root.transform);
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return new{wallSegments=count,wallThickness=.35f,bedrockReach=8,terrainSamplesRaised=terrain};
    }
    public static object Audit()
    {
        var group=GameObject.Find("ServiceCavern").transform.Find("15 Service wall backing and terrain joins");var invalid=new List<string>();int total=0;
        foreach(var f in group.GetComponentsInChildren<MeshFilter>())
        {
            var v=f.sharedMesh.vertices;var tri=f.sharedMesh.triangles;var edges=new Dictionary<string,int>();double volume=0;
            for(int i=0;i<tri.Length;i+=3)
            {
                volume+=Vector3.Dot(v[tri[i]],Vector3.Cross(v[tri[i+1]],v[tri[i+2]]))/6.0;
                for(int j=0;j<3;j++){string a=Key(v[tri[i+j]]),b=Key(v[tri[i+(j+1)%3]]);string key=string.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;int n;edges.TryGetValue(key,out n);edges[key]=n+1;}
            }
            if(edges.Any(e=>e.Value!=2)||volume<=0)invalid.Add(f.name+": edges="+edges.Count(e=>e.Value!=2)+", volume="+volume);
            total++;
        }
        return new{closedMeshes=total,invalid=invalid.ToArray()};
    }
}
