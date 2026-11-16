using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Continuous cliff shaping and exact cuts for the service route openings.
public static class MetroCavePortals
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern";
    const string SourceCorridor="Assets/Core/Visual/Models/SubwayCorridor/FBX/Corridor_Straight.fbx";
    struct Vertex
    {
        public Vector3 p,n;
        public Vector2 uv;
        public static Vertex Mix(Vertex a,Vertex b,float t)
        {return new Vertex{p=Vector3.Lerp(a.p,b.p,t),n=Vector3.Lerp(a.n,b.n,t).normalized,uv=Vector2.Lerp(a.uv,b.uv,t)};}
    }
    static Bounds Box(Vector3 min,Vector3 max){var b=new Bounds();b.SetMinMax(min,max);return b;}
    static readonly Bounds[] Tunnels={
        Box(new Vector3(89.6f,-23.28f,34.64f),new Vector3(96.2f,-19.76f,37.88f)),
        Box(new Vector3(29.58f,-1.31f,86.3f),new Vector3(32.82f,2.21f,94.02f)),
        Box(new Vector3(15.4f,-1.31f,90.78f),new Vector3(32.82f,2.21f,94.02f))
    };
    static bool WideEntry { get {var r=GameObject.Find("ServiceCavern");return r!=null&&r.transform.Find("14 Wide upper flight entrance")!=null;} }
    static IEnumerable<Bounds> Cuts
    {
        get
        {
            foreach(var b in Tunnels)yield return b;
            if(WideEntry)yield return Box(new Vector3(29.58f,-1.31f,86.1f),new Vector3(40.02f,4.01f,96.72f));
        }
    }
    static T Store<T>(T obj,string path) where T:Object
    {
        obj.name=System.IO.Path.GetFileNameWithoutExtension(path);
        var old=AssetDatabase.LoadAssetAtPath<T>(path);
        if(old==null){AssetDatabase.CreateAsset(obj,path);return obj;}
        var sourceMesh=obj as Mesh;var mesh=old as Mesh;
        if(sourceMesh!=null&&mesh!=null)
        {
            // Native setters invalidate Unity's render buffers; CopySerialized alone leaves stale geometry.
            mesh.Clear();mesh.indexFormat=sourceMesh.indexFormat;mesh.vertices=sourceMesh.vertices;
            mesh.normals=sourceMesh.normals;mesh.uv=sourceMesh.uv;mesh.tangents=sourceMesh.tangents;
            mesh.subMeshCount=sourceMesh.subMeshCount;
            for(int i=0;i<sourceMesh.subMeshCount;i++)mesh.SetTriangles(sourceMesh.GetTriangles(i),i,false);
            mesh.bounds=sourceMesh.bounds;mesh.name=obj.name;mesh.MarkModified();mesh.UploadMeshData(false);
        }
        else EditorUtility.CopySerialized(obj,old);
        Object.DestroyImmediate(obj);
        EditorUtility.SetDirty(old);return old;
    }
    static List<Vertex> Clip(List<Vertex> poly,int axis,float plane,float sign,bool inside)
    {
        var result=new List<Vertex>();
        for(int j=0;j<poly.Count;j++)
        {
            var a=poly[j];var b=poly[(j+1)%poly.Count];
            float da=(a.p[axis]-plane)*sign,db=(b.p[axis]-plane)*sign;
            bool ia=inside?da<=0:da>=0,ib=inside?db<=0:db>=0;
            if(ia)result.Add(a);
            if(ia!=ib && Mathf.Abs(da-db)>.00000001f)result.Add(Vertex.Mix(a,b,Mathf.Clamp01(da/(da-db))));
        }
        return result;
    }
    static List<List<Vertex>> Subtract(List<Vertex> poly,Bounds box)
    {
        var bounds=new Bounds(poly[0].p,Vector3.zero);foreach(var v in poly)bounds.Encapsulate(v.p);
        if(!bounds.Intersects(box))return new List<List<Vertex>>{poly};
        var outside=new List<List<Vertex>>();var remaining=poly;
        for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
        {
            if(remaining.Count<3)return outside;
            float sign=side==0?-1:1,plane=side==0?box.min[axis]:box.max[axis];
            var fragment=Clip(remaining,axis,plane,sign,false);
            if(fragment.Count>=3)outside.Add(fragment);
            remaining=Clip(remaining,axis,plane,sign,true);
        }
        return outside;
    }
    static Mesh Carve(Mesh source,Transform transform)
    {
        var p=source.vertices;var n=source.normals;var uv=source.uv;var indices=source.triangles;
        var cuts=Cuts.ToArray();
        var output=new List<Vertex>();var triangles=new List<int>();
        for(int j=0;j<indices.Length;j+=3)
        {
            var poly=new List<Vertex>();
            for(int k=0;k<3;k++)
            {
                int i=indices[j+k];poly.Add(new Vertex{p=transform!=null?transform.TransformPoint(p[i]):p[i],n=n.Length>i?n[i]:Vector3.up,uv=uv.Length>i?uv[i]:Vector2.zero});
            }
            var pieces=new List<List<Vertex>>{poly};
            foreach(var tunnel in cuts)
            {
                var next=new List<List<Vertex>>();foreach(var piece in pieces)next.AddRange(Subtract(piece,tunnel));pieces=next;
            }
            foreach(var piece in pieces)for(int k=1;k+1<piece.Count;k++)
            {
                if(Vector3.Cross(piece[k].p-piece[0].p,piece[k+1].p-piece[0].p).sqrMagnitude<.000000001f)continue;
                int offset=output.Count;output.Add(piece[0]);output.Add(piece[k]);output.Add(piece[k+1]);
                triangles.Add(offset);triangles.Add(offset+1);triangles.Add(offset+2);
            }
        }
        var mesh=new Mesh{name="Exact portal cut "+source.name};if(output.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.vertices=output.Select(v=>transform!=null?transform.InverseTransformPoint(v.p):v.p).ToArray();
        mesh.normals=output.Select(v=>v.n).ToArray();mesh.uv=output.Select(v=>v.uv).ToArray();
        mesh.triangles=triangles.ToArray();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
    }
    static float Smooth(float a,float b,float x){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));}
    static Vector3 Round(Vector3 p)
    {
        if(p.z<=52)return p;
        float u=Mathf.Clamp01((p.x-30)/64);
        float edge=Mathf.Lerp(96,62+33.5f*Mathf.Sqrt(Mathf.Max(0,1-u*u)),Smooth(36,48,p.x));
        float k=Mathf.Clamp((edge-56)/40,.12f,1),d=p.z-52;
        p.z=52+k*d+(1-k)*4*(1-Mathf.Exp(-d/4));return p;
    }
    static void Assign(MeshFilter mf,Mesh mesh,string file)
    {
        var saved=Store(mesh,Folder+"/Meshes/"+file+".asset");
        Undo.RecordObject(mf,"Fit service passage portal");mf.sharedMesh=saved;
        foreach(var collider in mf.GetComponents<MeshCollider>())
        {Undo.RecordObject(collider,"Fit service passage portal collision");collider.sharedMesh=null;collider.sharedMesh=saved;}
    }
    static void RestoreApproaches()
    {
        var original=AssetDatabase.LoadAllAssetsAtPath(SourceCorridor).OfType<Mesh>().First();
        foreach(string name in new[]{"06_Corridor_Straight","39_Corridor_Straight"})
        {
            var go=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Modules/"+name);
            var mf=go.GetComponent<MeshFilter>();Undo.RecordObject(mf,"Enclose portal approach");mf.sharedMesh=original;
            foreach(var c in go.GetComponents<MeshCollider>()){Undo.RecordObject(c,"Enclose portal approach collision");c.sharedMesh=original;}
        }
    }
    static void RebuildVault(Transform vault,float depotLimit)
    {
        // Recreate the omitted triangles first; no old, oversized entrance holes survive.
        var original=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Meshes/CavernVault_Sculpted.asset");
        // Subdivide the original continuous shell BEFORE shaping the entrances. The
        // shoulders now belong to the wall, rather than being separate ellipsoids.
        const int subdivision=4,count=128*subdivision,rows=16*subdivision+1;
        var old=original.vertices;var oldUV=original.uv;
        bool wide=WideEntry;
        var vertices=new Vector3[rows*(count+1)+1];var uv=new Vector2[vertices.Length];
        for(int j=0;j<rows;j++)for(int i=0;i<=count;i++)
        {
            float sj=(float)j/subdivision,si=(float)i/subdivision;
            int a=Mathf.Min(15,Mathf.FloorToInt(sj)),b=Mathf.Min(127,Mathf.FloorToInt(si));
            float v=sj-a,u=si-b;int k=a*129+b,index=j*(count+1)+i;
            var p=Vector3.Lerp(Vector3.Lerp(old[k],old[k+1],u),Vector3.Lerp(old[k+129],old[k+130],u),v);
            p.x=Mathf.Min(p.x,depotLimit);p=Round(p);
            // Broad, smooth, continuous rock shoulders; rectangular cores remain
            // behind the steel frames and are cut by the exact corridor volumes.
            float upperX=Smooth(23,28.9f,p.x)*(1-Smooth(wide?40.6f:33.5f,wide?49:43,p.x));
            float upperY=Smooth(-12,-2,p.y)*(1-Smooth(wide?4.8f:2.9f,wide?11:9,p.y));
            if(p.z>86.65f)p.z=Mathf.Lerp(p.z,86.65f,upperX*upperY);
            float lowerZ=Smooth(28,34.15f,p.z)*(1-Smooth(38.4f,46,p.z));
            float lowerY=Smooth(-29,-23.8f,p.y)*(1-Smooth(-19.25f,-12,p.y));
            if(p.x>90.05f)p.x=Mathf.Lerp(p.x,90.05f,lowerZ*lowerY);
            vertices[index]=p;
            uv[index]=Vector2.Lerp(Vector2.Lerp(oldUV[k],oldUV[k+1],u),Vector2.Lerp(oldUV[k+129],oldUV[k+130],u),v);
        }
        vertices[vertices.Length-1]=Round(old[old.Length-1]);uv[uv.Length-1]=oldUV[oldUV.Length-1];
        var triangles=new List<int>();
        Action<int,int,int> add=(a,b,c)=>
        {
            var center=(vertices[a]+vertices[b]+vertices[c])/3;
            var inward=new Vector3(60.5f-center.x,-Mathf.Max(0,center.y+1)*3,63.5f-center.z);
            if(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),inward)<0){int t=b;b=c;c=t;}
            triangles.Add(a);triangles.Add(b);triangles.Add(c);
        };
        for(int j=0;j<rows-1;j++)for(int i=0;i<count;i++)
        {int a=j*(count+1)+i,b=a+1,c=a+count+1,d=c+1;add(a,c,b);add(b,c,d);}
        int top=vertices.Length-1;for(int i=0;i<count;i++)add(top,(rows-1)*(count+1)+i,(rows-1)*(count+1)+i+1);
        var whole=new Mesh{name="Closed rounded cavern"};whole.vertices=vertices;whole.uv=uv;whole.triangles=triangles.ToArray();
        whole.RecalculateNormals();whole.RecalculateTangents();var normals=whole.normals;
        for(int sector=0;sector<8;sector++)
        {
            var selected=new List<int>();
            for(int j=0;j<triangles.Count;j+=3)
            {
                var p=(vertices[triangles[j]]+vertices[triangles[j+1]]+vertices[triangles[j+2]])/3;
                float angle=Mathf.Atan2(p.z-63.5f,p.x-60.5f);if(angle<0)angle+=Mathf.PI*2;
                if(Mathf.Min(7,Mathf.FloorToInt(angle/(Mathf.PI*.25f)))!=sector)continue;
                selected.Add(triangles[j]);selected.Add(triangles[j+1]);selected.Add(triangles[j+2]);
            }
            var source=new Mesh{name="Portal vault "+sector};source.vertices=vertices;source.normals=normals;source.uv=uv;source.triangles=selected.ToArray();
            var mf=vault.Find("Vault sector "+(sector+1).ToString("00")).GetComponent<MeshFilter>();
            Assign(mf,Carve(source,null),"Portal_Vault_"+sector);Object.DestroyImmediate(source);
        }
        Object.DestroyImmediate(whole);
    }
    static Mesh RockCollar(Bounds bounds,bool upper)
    {
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
        var normals=new List<Vector3>();
        var half=bounds.extents;
        for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
        {
            int a=(axis+1)%3,b=(axis+2)%3;
            int na=Mathf.Max(1,Mathf.CeilToInt(bounds.size[a]/.38f)),nb=Mathf.Max(1,Mathf.CeilToInt(bounds.size[b]/.38f));
            int offset=vertices.Count;
            for(int j=0;j<=nb;j++)for(int i=0;i<=na;i++)
            {
                var p=bounds.min;p[axis]=side==0?bounds.min[axis]:bounds.max[axis];
                p[a]=Mathf.Lerp(bounds.min[a],bounds.max[a],(float)i/na);p[b]=Mathf.Lerp(bounds.min[b],bounds.max[b],(float)j/nb);
                // A closed ellipsoidal rock shoulder has no flat shelf or square outside corner.
                var direction=new Vector3((p.x-bounds.center.x)/half.x,(p.y-bounds.center.y)/half.y,(p.z-bounds.center.z)/half.z).normalized;
                p=bounds.center+Vector3.Scale(direction,half);
                var normal=new Vector3(direction.x/half.x,direction.y/half.y,direction.z/half.z).normalized;
                bool front=side==0&&axis==(upper?2:0);
                if(front)p[axis]+=(Mathf.PerlinNoise(p[a]*.7f+13,p[b]*.7f+41)-.5f)*.04f;
                vertices.Add(p);normals.Add(normal);uvs.Add(new Vector2(p[a]*.25f,p[b]*.25f));
            }
            for(int j=0;j<nb;j++)for(int i=0;i<na;i++)
            {
                int aa=offset+j*(na+1)+i,bb=aa+1,cc=aa+na+1,dd=cc+1;
                if(side==0){triangles.Add(aa);triangles.Add(cc);triangles.Add(bb);triangles.Add(bb);triangles.Add(cc);triangles.Add(dd);}
                else {triangles.Add(aa);triangles.Add(bb);triangles.Add(cc);triangles.Add(bb);triangles.Add(dd);triangles.Add(cc);}
            }
        }
        var mesh=new Mesh{name=upper?"Upper rock portal collar":"Depot rock portal collar"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uvs);mesh.SetTriangles(triangles,0);
        return mesh;
    }
    static void PlaceCollar(Transform group,string name,Bounds bounds,bool upper,Material material)
    {
        var existing=group.Find(name);GameObject go;
        if(existing==null)
        {
            go=new GameObject(name);go.transform.SetParent(group,false);go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();
            Undo.RegisterCreatedObjectUndo(go,"Create fitted rock portal");
        }
        else go=existing.gameObject;
        var source=RockCollar(bounds,upper);var carved=Carve(source,null);Object.DestroyImmediate(source);
        Assign(go.GetComponent<MeshFilter>(),carved,upper?"Portal_Upper_Collar":"Portal_Depot_Collar");
        go.GetComponent<MeshRenderer>().sharedMaterial=material;go.isStatic=true;
    }
    public static string Repair()
    {
        var root=GameObject.Find("ServiceCavern");if(root==null)throw new InvalidOperationException("Cavern not found");
        var depot=GameObject.Find("MetroDepot/MetroDepot_Model");float limit=depot.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.x)-.25f;
        RestoreApproaches();RebuildVault(root.transform.Find("00 Bedrock vault"),limit);
        int cleared=0;
        foreach(string name in new[]{"01 Rock outcrops","02 Talus and loose stones","03 Sparse coloured minerals"})
        foreach(var mf in root.transform.Find(name).GetComponentsInChildren<MeshFilter>())
        {
            if(AssetDatabase.GetAssetPath(mf.sharedMesh).Contains("/PortalClear_"))continue;
            if(!Cuts.Any(b=>b.Intersects(mf.GetComponent<Renderer>().bounds)))continue;
            Assign(mf,Carve(mf.sharedMesh,mf.transform),"PortalClear_"+mf.name.Replace(' ','_'));cleared++;
        }
        var group=root.transform.Find("06 Fitted service portals");
        if(group==null){var go=new GameObject("06 Fitted service portals");go.transform.SetParent(root.transform,false);Undo.RegisterCreatedObjectUndo(go,"Create service portal group");group=go.transform;}
        foreach(var collar in group.Cast<Transform>().ToArray())
            if(collar.name=="Upper passage rock surround"||collar.name=="Depot passage rock surround")
                Undo.DestroyObjectImmediate(collar.gameObject);
        var flood=root.transform.Find("05 Cavern lighting/Warm canopy flood 3");
        if(flood!=null)
        {
            Undo.RecordObject(flood,"Aim light away from portal reveal");
            flood.position=new Vector3(86.5f,-20,37.5f);flood.rotation=Quaternion.LookRotation(new Vector3(-.55f,.25f,1));
            var light=flood.GetComponent<Light>();Undo.RecordObject(light,"Balance portal lighting");light.intensity=160;
        }
        MetroCaveTerrain.GradeUnderStairs();
        if(root.transform.Find("08 Open service walkway")!=null)MetroOpenServiceWalkway.Apply();
        if(root.transform.Find("10 Stair lamps and guardrail")!=null)MetroStairFixtures.Apply();
        if(WideEntry)MetroWideUpperEntry.Reapply();
        else if(root.transform.Find("15 Service wall backing and terrain joins")!=null)MetroServiceLandscapeJoin.Apply();
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Continuous cliff shoulders blended around exact service openings; separate ellipsoid collars removed; obstructing rock meshes clipped: "+cleared;
    }
    public static void RebuildForWideEntry()
    {
        var root=GameObject.Find("ServiceCavern");
        RebuildVault(root.transform.Find("00 Bedrock vault"),93.35435f);
    }
}
