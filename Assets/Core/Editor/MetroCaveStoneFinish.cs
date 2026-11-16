using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Authoring only: settle loose stones by their actual lower surface, while keeping
// cliff outcrops and roof plates anchored to the bedrock instead of the floor.
public static class MetroCaveStoneFinish
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/";
    const float DepotLimit=93.35435f;
    static Terrain terrain;
    static MeshCollider[] vault;
    static float Ground(Vector3 p){return terrain.SampleHeight(p)+terrain.transform.position.y;}
    static bool Solid(Vector3 p)
    {
        var td=terrain.terrainData;var q=p-terrain.transform.position;
        int n=td.holesResolution,x=Mathf.FloorToInt(q.x/td.size.x*n),z=Mathf.FloorToInt(q.z/td.size.z*n);
        return x>=0&&z>=0&&x<n&&z<n&&!td.IsHole(x,z);
    }
    static bool Bedrock(Ray ray,float length,out RaycastHit nearest)
    {
        nearest=new RaycastHit();float distance=length;bool found=false;
        foreach(var c in vault){RaycastHit h;if(c.Raycast(ray,out h,length)&&h.distance<distance){nearest=h;distance=h.distance;found=true;}}
        return found;
    }
    static Vector3[] World(MeshFilter f){return f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray();}
    // Sample the lower envelope at a regular grid. Every occupied cell has equal
    // weight, so dense mesh vertices cannot disguise a single-point contact.
    static List<Vector3> Footprint(MeshFilter f,bool upper=false)
    {
        var v=World(f);var tri=f.sharedMesh.triangles;var b=new Bounds(v[0],Vector3.zero);
        foreach(var p in v)b.Encapsulate(p);
        var samples=new List<Vector3>();const int n=7;
        for(int iz=0;iz<n;iz++)for(int ix=0;ix<n;ix++)
        {
            float x=Mathf.Lerp(b.min.x,b.max.x,(ix+.5f)/n),z=Mathf.Lerp(b.min.z,b.max.z,(iz+.5f)/n),bottom=upper?float.NegativeInfinity:float.PositiveInfinity;
            for(int j=0;j<tri.Length;j+=3)
            {
                var a=v[tri[j]];var c=v[tri[j+1]];var d=v[tri[j+2]];
                float den=(c.z-d.z)*(a.x-d.x)+(d.x-c.x)*(a.z-d.z);
                if(Mathf.Abs(den)<.0000001f)continue;
                float u=((c.z-d.z)*(x-d.x)+(d.x-c.x)*(z-d.z))/den;
                float w=((d.z-a.z)*(x-d.x)+(a.x-d.x)*(z-d.z))/den;
                if(u<0||w<0||u+w>1)continue;
                float y=u*a.y+w*c.y+(1-u-w)*d.y;
                bottom=upper?Mathf.Max(bottom,y):Mathf.Min(bottom,y);
            }
            if(!float.IsInfinity(bottom))samples.Add(new Vector3(x,bottom,z));
        }
        return samples;
    }
    static void Settle(MeshFilter f)
    {
        var t=f.transform;var r=f.GetComponent<Renderer>();var p=r.bounds.center;
        var normal=terrain.terrainData.GetInterpolatedNormal((p.x-terrain.transform.position.x)/terrain.terrainData.size.x,(p.z-terrain.transform.position.z)/terrain.terrainData.size.z);
        normal=Vector3.RotateTowards(Vector3.up,normal,50*Mathf.Deg2Rad,0);
        Undo.RecordObject(t,"Seat rock across its footprint");
        float yaw=t.eulerAngles.y;
        t.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,yaw,0);
        var samples=Footprint(f);if(samples.Count==0)return;
        var shifts=samples.Select(v=>Ground(v)-v.y).OrderBy(v=>v).ToArray();
        float burial=Mathf.Max(.025f,Mathf.Min(.30f,r.bounds.size.y*.075f));
        t.position+=Vector3.up*(shifts[Mathf.FloorToInt((shifts.Length-1)*.32f)]-burial);
    }
    static List<Vector3> WallFootprint(MeshFilter f,Vector3 normal)
    {
        var u=Vector3.Cross(Vector3.up,normal).normalized;var v=Vector3.Cross(normal,u).normalized;
        var world=World(f);var projected=world.Select(p=>new Vector3(Vector3.Dot(p,u),Vector3.Dot(p,normal),Vector3.Dot(p,v))).ToArray();
        var b=new Bounds(projected[0],Vector3.zero);foreach(var p in projected)b.Encapsulate(p);
        var tri=f.sharedMesh.triangles;var samples=new List<Vector3>();const int n=7;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            float px=Mathf.Lerp(b.min.x,b.max.x,(x+.5f)/n),pz=Mathf.Lerp(b.min.z,b.max.z,(z+.5f)/n),back=float.PositiveInfinity;
            for(int j=0;j<tri.Length;j+=3)
            {
                var a=projected[tri[j]];var c=projected[tri[j+1]];var d=projected[tri[j+2]];
                float den=(c.z-d.z)*(a.x-d.x)+(d.x-c.x)*(a.z-d.z);if(Mathf.Abs(den)<.0000001f)continue;
                float aa=((c.z-d.z)*(px-d.x)+(d.x-c.x)*(pz-d.z))/den;
                float cc=((d.z-a.z)*(px-d.x)+(a.x-d.x)*(pz-d.z))/den;
                if(aa<0||cc<0||aa+cc>1)continue;
                back=Mathf.Min(back,aa*a.y+cc*c.y+(1-aa-cc)*d.y);
            }
            if(!float.IsInfinity(back))samples.Add(u*px+v*pz+normal*back);
        }
        return samples;
    }
    static float OutcropSupport(MeshFilter f,bool roof)
    {
        var b=f.GetComponent<Renderer>().bounds;Vector3 normal=Vector3.down;RaycastHit hit;
        if(!roof)
        {
            var start=new Vector3(60.5f,Mathf.Min(b.center.y,5),63.5f);
            if(!Bedrock(new Ray(start,(b.center-start).normalized),80,out hit))return 0;
            normal=hit.normal;
        }
        var points=roof?Footprint(f,true):WallFootprint(f,normal);int supported=0;
        float range=b.size.magnitude+3;
        foreach(var p in points)
            if(Bedrock(new Ray(p+normal*range,-normal),range*2+10,out hit)&&Vector3.Dot(p-hit.point,normal)<=.025f)supported++;
        return (float)supported/Mathf.Max(1,points.Count);
    }
    static Mesh SaveMesh(Mesh source,string name)
    {
        string path=Folder+"Meshes/Grounded_"+name.Replace(' ','_')+".asset";
        source.name=System.IO.Path.GetFileNameWithoutExtension(path);
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null){AssetDatabase.CreateAsset(source,path);return source;}
        mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=source.vertices;mesh.normals=source.normals;
        mesh.uv=source.uv;mesh.tangents=source.tangents;mesh.triangles=source.triangles;mesh.bounds=source.bounds;
        mesh.MarkModified();mesh.UploadMeshData(false);Object.DestroyImmediate(source);EditorUtility.SetDirty(mesh);return mesh;
    }
    static bool TrimAtDepot(MeshFilter f)
    {
        var v=World(f);if(v.Max(p=>p.x)<=DepotLimit+.0001f)return true;
        var method=typeof(MetroCaveBuilder).GetMethod("CutRockAtDepot",BindingFlags.NonPublic|BindingFlags.Static);
        var cut=(Mesh)method.Invoke(null,new object[]{f.sharedMesh,f.transform,DepotLimit});
        if(cut.vertexCount<3){Object.DestroyImmediate(cut);f.gameObject.SetActive(false);return false;}
        f.sharedMesh=SaveMesh(cut,f.name);
        foreach(var c in f.GetComponents<MeshCollider>()){c.sharedMesh=null;c.sharedMesh=f.sharedMesh;}
        return true;
    }
    static bool Anchor(MeshFilter f,bool roof)
    {
        var b=f.GetComponent<Renderer>().bounds;RaycastHit hit;
        if(roof)
        {
            var shifts=new List<float>();
            foreach(var p in Footprint(f,true))
                if(Bedrock(new Ray(new Vector3(p.x,-10,p.z),Vector3.up),40,out hit))shifts.Add(hit.point.y-p.y);
            if(shifts.Count==0)return false;
            shifts.Sort();Undo.RecordObject(f.transform,"Seat roof outcrop across the curved vault");
            f.transform.position+=Vector3.up*(shifts[(int)((shifts.Count-1)*.72f)]+Mathf.Min(.4f,b.size.y*.08f));
            return TrimAtDepot(f);
        }
        Vector3 start=roof?new Vector3(b.center.x,-10,b.center.z):new Vector3(60.5f,Mathf.Min(b.center.y,5),63.5f);
        Vector3 direction=roof?Vector3.up:(b.center-start).normalized;
        if(!Bedrock(new Ray(start,direction),80,out hit))return false;
        var normal=hit.normal;var shiftsWall=new List<float>();float range=b.size.magnitude+3;
        foreach(var p in WallFootprint(f,normal))
            if(Bedrock(new Ray(p+normal*range,-normal),range*2+10,out hit))shiftsWall.Add(Vector3.Dot(hit.point-p,normal));
        if(shiftsWall.Count==0)return false;shiftsWall.Sort();
        Undo.RecordObject(f.transform,"Embed outcrop into continuous bedrock");
        f.transform.position+=normal*(shiftsWall[(int)((shiftsWall.Count-1)*.28f)]-Mathf.Min(.3f,b.size.magnitude*.025f));
        return TrimAtDepot(f);
    }
    static List<MeshFilter> GroundStones(Transform root)
    {
        var result=new List<MeshFilter>();
        foreach(string name in new[]{"01 Rock outcrops","02 Talus and loose stones","03 Sparse coloured minerals","07 Stair slope stone scatter","12 Coloured stone clusters"})
        {
            var group=root.Find(name);if(group==null)continue;
            foreach(var f in group.GetComponentsInChildren<MeshFilter>())
            {
                int id;
                if(f.name.StartsWith("Embedded roof plate"))continue;
                if(f.name.StartsWith("South wall outcrop "))continue;
                if(name=="01 Rock outcrops"&&f.name.StartsWith("Limestone ")&&int.TryParse(f.name.Substring(10),out id)&&id>=46&&id<=67)continue;
                if(f.sharedMesh!=null&&f.sharedMesh.vertexCount>2)result.Add(f);
            }
        }
        return result;
    }
    static int Coloured(Transform root)
    {
        var old=root.Find("12 Coloured stone clusters");if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
        var group=new GameObject("12 Coloured stone clusters");group.transform.SetParent(root,false);Undo.RegisterCreatedObjectUndo(group,"Add coloured stone clusters");
        var rng=new System.Random(151126);int created=0;
        var palette=new[]{3,8,18,19,22,28,48,52,58,60,64,66,70,73,86,88,89,5,12,15,25,34,40,62};
        var sources=AssetDatabase.LoadAllAssetsAtPath("Assets/Core/Visual/Models/Rocks/rocks.fbx").OfType<Mesh>().ToArray();
        var centres=new[]{new Vector2(38.8f,78),new Vector2(42,87),new Vector2(49,83),new Vector2(54,74),new Vector2(43,65),new Vector2(40,56),new Vector2(50,53),new Vector2(64,65),new Vector2(73,49),new Vector2(80,66),new Vector2(69,76),new Vector2(59,86)};
        var occupied=GroundStones(root).Select(f=>f.GetComponent<Renderer>().bounds).Where(b=>Mathf.Max(b.size.x,b.size.z)>1.2f).ToList();
        foreach(var centre in centres)
        {
            int count=0;
            for(int attempt=0;attempt<100&&count<7;attempt++)
            {
                float angle=(float)rng.NextDouble()*Mathf.PI*2,radius=(float)Math.Sqrt(rng.NextDouble())*3.8f;
                var p=new Vector3(centre.x+Mathf.Cos(angle)*radius,0,centre.y+Mathf.Sin(angle)*radius);
                if(p.x<36.6f||p.x>89||p.z<41||p.z>92||!Solid(p))continue;
                p.y=Ground(p);float size=Mathf.Lerp(.4f,1.35f,(float)rng.NextDouble());
                if(occupied.Any(b=>{var q=b.ClosestPoint(p);return new Vector2(q.x-p.x,q.z-p.z).magnitude<size*.65f;}))continue;
                RaycastHit ceiling;if(Bedrock(new Ray(p+Vector3.up*.15f,Vector3.up),35,out ceiling)&&ceiling.point.y-p.y<size+1.4f)continue;
                int id=palette[(created*7)%palette.Length];
                var go=new GameObject("Coloured stone "+(created+1).ToString("000"));go.transform.SetParent(group.transform,false);go.transform.position=p;
                go.transform.rotation=Quaternion.Euler(0,(float)rng.NextDouble()*360,0);
                go.transform.localScale=new Vector3(size*Mathf.Lerp(.85f,1.2f,(float)rng.NextDouble()),size*Mathf.Lerp(.7f,1.1f,(float)rng.NextDouble()),size);
                string meshPath=Folder+"Meshes/Accent_rock"+id.ToString("000")+"_lod4.asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(mesh==null)
                {
                    var source=sources.First(m=>m.name=="rock"+id.ToString("000")+"_lod4");mesh=Object.Instantiate(source);
                    var b=mesh.bounds;float length=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
                    var pivot=new Vector3(b.center.x,b.min.y,b.center.z);
                    mesh.vertices=mesh.vertices.Select(v=>(v-pivot)/length).ToArray();mesh.RecalculateBounds();
                    mesh.name=System.IO.Path.GetFileNameWithoutExtension(meshPath);AssetDatabase.CreateAsset(mesh,meshPath);
                }
                var f=go.AddComponent<MeshFilter>();f.sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Core/Visual/Models/Rocks/Materials/Rock"+id.ToString("000")+".mat");
                go.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;go.isStatic=true;
                Settle(f);occupied.Add(go.GetComponent<Renderer>().bounds);created++;count++;
            }
        }
        return created;
    }
    public static string Apply()
    {
        var root=GameObject.Find("ServiceCavern").transform;terrain=root.GetComponentInChildren<Terrain>();vault=root.Find("00 Bedrock vault").GetComponentsInChildren<MeshCollider>();
        Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Blend cave stone dressing into the landscape");
        int anchored=0;
        foreach(var f in root.Find("01 Rock outcrops").GetComponentsInChildren<MeshFilter>())
        {
            if(f.sharedMesh.vertexCount<3){Undo.RecordObject(f.gameObject,"Hide empty clipped rock");f.gameObject.SetActive(false);continue;}
            int id;bool roof=f.name.StartsWith("Embedded roof plate");
            bool wall=f.name.StartsWith("South wall outcrop ")||(f.name.StartsWith("Limestone ")&&int.TryParse(f.name.Substring(10),out id)&&id>=46&&id<=67);
            if((roof||wall)&&f.sharedMesh.vertexCount>2&&Anchor(f,roof))anchored++;
        }
        int settled=0;
        foreach(var f in GroundStones(root)){Settle(f);if(TrimAtDepot(f))settled++;}
        int added=Coloured(root);
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);
        return "Existing ground stones seated="+settled+"; cliff/roof outcrops embedded="+anchored+"; new coloured stones="+added;
    }
    public static int ReseatUnsupportedGround()
    {
        var root=GameObject.Find("ServiceCavern").transform;terrain=root.GetComponentInChildren<Terrain>();
        int changed=0;
        foreach(var f in GroundStones(root))
        {
            var samples=Footprint(f);
            float contact=(float)samples.Count(p=>Solid(p)&&p.y<=Ground(p)+.012f)/Mathf.Max(1,samples.Count);
            if(contact>=.65f)continue;
            Settle(f);TrimAtDepot(f);changed++;
        }
        return changed;
    }
    public static object Audit()
    {
        var root=GameObject.Find("ServiceCavern").transform;terrain=root.GetComponentInChildren<Terrain>();vault=root.Find("00 Bedrock vault").GetComponentsInChildren<MeshCollider>();
        var stones=GroundStones(root);var failures=new List<string>();float minimum=1;
        foreach(var f in stones)
        {
            var samples=Footprint(f);float contact=(float)samples.Count(p=>Solid(p)&&p.y<=Ground(p)+.012f)/Mathf.Max(1,samples.Count);
            minimum=Mathf.Min(minimum,contact);if(contact<.60f)failures.Add(f.name+"="+contact.ToString("F2"));
        }
        var detached=new List<string>();int attached=0;float minimumRock=1;
        foreach(var f in root.Find("01 Rock outcrops").GetComponentsInChildren<MeshFilter>())
        {
            if(f.sharedMesh.vertexCount<3)continue;
            int id;bool roof=f.name.StartsWith("Embedded roof plate");
            bool wall=f.name.StartsWith("South wall outcrop ")||(f.name.StartsWith("Limestone ")&&int.TryParse(f.name.Substring(10),out id)&&id>=46&&id<=67);
            if(!roof&&!wall)continue;
            float contact=OutcropSupport(f,roof);minimumRock=Mathf.Min(minimumRock,contact);
            if(contact<.60f)detached.Add(f.name+"="+contact.ToString("F2"));else attached++;
        }
        return new{groundStones=stones.Count,minimumSupportedFootprint=minimum,unsupported=failures.ToArray(),attachedOutcrops=attached,minimumOutcropSupport=minimumRock,detached=detached.ToArray()};
    }
}
