using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Solid foundations below the existing walking surfaces. Original floor heights,
// stair treads, rails and terrain remain the source of the visible route.
public static class MetroServiceFloorVolume
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/";
    const float Inset=.006f,Thickness=.45f;
    static string Key(Vector3 p){return Mathf.RoundToInt(p.x*10000)+"/"+Mathf.RoundToInt(p.y*10000)+"/"+Mathf.RoundToInt(p.z*10000);}
    sealed class Writer
    {
        public List<Vector3> v=new List<Vector3>(),normals=new List<Vector3>();
        public List<Vector2> uv=new List<Vector2>();
        public List<int>[] tri={new List<int>(),new List<int>(),new List<int>()};
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,int sub,Vector3 outward)
        {
            var n=Vector3.Cross(b-a,c-a);if(n.sqrMagnitude<1e-12f)return;
            if(Vector3.Dot(n,outward)<0){var swap=b;b=c;c=swap;n=-n;}n.Normalize();
            foreach(var p in new[]{a,b,c})
            {
                tri[sub].Add(v.Count);v.Add(p);normals.Add(n);
                var abs=new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z));
                uv.Add((abs.y>=abs.x&&abs.y>=abs.z?new Vector2(p.x,p.z):abs.x>=abs.z?new Vector2(p.z,p.y):new Vector2(p.x,p.y))*.5f);
            }
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int sub,Vector3 n)
        {Triangle(a,b,c,sub,n);Triangle(a,c,d,sub,n);}
        public Mesh Mesh(string name)
        {
            var m=new Mesh{name=name};m.SetVertices(v);m.SetNormals(normals);m.SetUVs(0,uv);m.subMeshCount=3;
            for(int i=0;i<3;i++)m.SetTriangles(tri[i],i,false);
            m.RecalculateBounds();m.RecalculateTangents();return m;
        }
    }
    static Mesh Store(Mesh source,string name)
    {
        string path=Folder+"Meshes/"+name+".asset";source.name=name;
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){AssetDatabase.CreateAsset(source,path);return source;}
        mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.uv=source.uv;mesh.tangents=source.tangents;
        mesh.subMeshCount=source.subMeshCount;for(int i=0;i<source.subMeshCount;i++)mesh.SetTriangles(source.GetTriangles(i),i,false);
        mesh.bounds=source.bounds;mesh.name=name;mesh.MarkModified();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);Object.DestroyImmediate(source);return mesh;
    }
    static int[] FloorTriangles(MeshFilter f)
    {
        var mats=f.GetComponent<Renderer>().sharedMaterials;var mesh=f.sharedMesh;var result=new List<int>();
        for(int s=0;s<Math.Min(mesh.subMeshCount,mats.Length);s++)
            if(mats[s]!=null&&(mats[s].name.Contains("Floor")||mats[s].name.Contains("Stairs")))result.AddRange(mesh.GetTriangles(s));
        return result.ToArray();
    }
    static Mesh FlatOrRamp(MeshFilter f,int[] indices,string name)
    {
        var source=f.sharedMesh.vertices;var v=new List<Vector3>();var map=new Dictionary<string,int>();var tri=new List<int>();
        foreach(int index in indices)
        {
            var p=f.transform.TransformPoint(source[index]);string key=Key(p);int k;
            if(!map.TryGetValue(key,out k)){k=v.Count;map[key]=k;v.Add(p-Vector3.up*Inset);}tri.Add(k);
        }
        var edges=new Dictionary<string,List<int>>();var w=new Writer();
        for(int i=0;i<tri.Count;i+=3)
        {
            int a=tri[i],b=tri[i+1],c=tri[i+2];var normal=Vector3.Cross(v[b]-v[a],v[c]-v[a]).normalized;
            w.Triangle(v[a],v[b],v[c],0,normal);
            w.Triangle(v[a]-Vector3.up*Thickness,v[c]-Vector3.up*Thickness,v[b]-Vector3.up*Thickness,2,-normal);
            for(int j=0;j<3;j++)
            {
                int aa=tri[i+j],bb=tri[i+(j+1)%3];string key=Mathf.Min(aa,bb)+"/"+Mathf.Max(aa,bb);
                List<int> edge;if(!edges.TryGetValue(key,out edge)){edge=new List<int>();edges[key]=edge;}edge.Add(aa);edge.Add(bb);
            }
        }
        foreach(var edge in edges.Values.Where(e=>e.Count==2))
        {
            var a=v[edge[0]];var b=v[edge[1]];var n=Vector3.Cross(b-a,Vector3.up);
            w.Quad(a,a-Vector3.up*Thickness,b-Vector3.up*Thickness,b,1,n);
        }
        return w.Mesh(name);
    }
    static float Cross(Vector2 a,Vector2 b,Vector2 c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);}
    static List<int> Triangulate(List<Vector2> polygon)
    {
        float area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=a.x*b.y-a.y*b.x;}
        var active=Enumerable.Range(0,polygon.Count).ToList();if(area<0)active.Reverse();
        var output=new List<int>();int attempts=0;
        while(active.Count>3&&attempts++<polygon.Count*polygon.Count)
        {
            bool found=false;
            for(int j=0;j<active.Count;j++)
            {
                int a=active[(j+active.Count-1)%active.Count],b=active[j],c=active[(j+1)%active.Count];
                if(Cross(polygon[a],polygon[b],polygon[c])<=1e-7f)continue;
                bool contains=false;
                foreach(int k in active)
                    if(k!=a&&k!=b&&k!=c&&Cross(polygon[a],polygon[b],polygon[k])>=-1e-7f&&Cross(polygon[b],polygon[c],polygon[k])>=-1e-7f&&Cross(polygon[c],polygon[a],polygon[k])>=-1e-7f){contains=true;break;}
                if(contains)continue;
                output.Add(a);output.Add(b);output.Add(c);active.RemoveAt(j);found=true;break;
            }
            if(!found)throw new InvalidOperationException("Cannot triangulate stair foundation profile");
        }
        if(active.Count==3)output.AddRange(active);return output;
    }
    static Mesh Stairs(MeshFilter f,int[] indices,string name)
    {
        var verts=f.sharedMesh.vertices;var world=indices.Distinct().Select(i=>f.transform.TransformPoint(verts[i])).ToArray();
        float x0=world.Min(p=>p.x),x1=world.Max(p=>p.x);
        var profile=world.Where(p=>Mathf.Abs(p.x-x1)<.002f).GroupBy(p=>Mathf.RoundToInt(p.z*10000)+"/"+Mathf.RoundToInt(p.y*10000))
            .Select(g=>g.First()).OrderBy(p=>Mathf.RoundToInt(p.z*10000)).ThenBy(p=>p.y).Select(p=>new Vector2(p.z,p.y-Inset)).ToList();
        float low=profile.Min(p=>p.y),high=profile.Max(p=>p.y);
        profile.Add(new Vector2(profile[profile.Count-1].x,high-Thickness));
        profile.Add(new Vector2(profile[0].x,low-Thickness));
        // Keep the lower face smooth; side plates follow every individual tread.
        for(int i=profile.Count-1;i>=0;i--)
        {
            var a=profile[(i+profile.Count-1)%profile.Count];var b=profile[i];var c=profile[(i+1)%profile.Count];
            if(Mathf.Abs(Cross(a,b,c))<1e-6f)profile.RemoveAt(i);
        }
        var side=Triangulate(profile);var w=new Writer();
        Func<int,float,Vector3> p=(i,x)=>new Vector3(x,profile[i].y,profile[i].x);
        for(int i=0;i<side.Count;i+=3)
        {
            w.Triangle(p(side[i],x0),p(side[i+1],x0),p(side[i+2],x0),1,Vector3.left);
            w.Triangle(p(side[i],x1),p(side[i+1],x1),p(side[i+2],x1),1,Vector3.right);
        }
        float area=0;for(int i=0;i<profile.Count;i++){var a=profile[i];var b=profile[(i+1)%profile.Count];area+=a.x*b.y-a.y*b.x;}
        for(int i=0;i<profile.Count;i++)
        {
            int j=(i+1)%profile.Count;var d=profile[j]-profile[i];var n=new Vector3(0,d.x,-d.y)*(area<0?1:-1);
            int sub=n.y>1e-5f?0:n.y<-1e-5f?2:1;
            w.Quad(p(i,x0),p(j,x0),p(j,x1),p(i,x1),sub,n);
        }
        return w.Mesh(name);
    }
    static Material Material(string file,Material source,Color tint)
    {
        string path=Folder+"Materials/"+file+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(source);mat.name=file;mat.SetColor("_BaseColor",tint);AssetDatabase.CreateAsset(mat,path);}return mat;
    }
    public static object Apply()
    {
        var root=GameObject.Find("ServiceCavern");var modules=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor/Modules");
        if(root==null||modules==null)throw new InvalidOperationException("Service passage not found");
        var group=root.transform.Find("13 Solid service passage floor");
        if(group==null){var go=new GameObject("13 Solid service passage floor");go.transform.SetParent(root.transform,false);group=go.transform;Undo.RegisterCreatedObjectUndo(go,"Add full-length solid passage floor");}
        var source=modules.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).First(m=>m!=null&&m.name=="M_Corridor_Floor");
        var edge=Material("ServicePassage_FloorEdges",source,new Color(.72f,.75f,.76f));
        var underside=Material("ServicePassage_FloorUnderside",source,new Color(.53f,.56f,.58f));int count=0,stairs=0;
        foreach(var f in modules.GetComponentsInChildren<MeshFilter>())
        {
            var indices=FloorTriangles(f);if(indices.Length==0)continue;
            bool stair=f.name.Contains("Stairs");string name="SolidFloor_"+f.name;
            var mesh=Store(stair?Stairs(f,indices,name):FlatOrRamp(f,indices,name),name);
            var child=group.Find(f.name);GameObject go;
            if(child==null){go=new GameObject(f.name);go.transform.SetParent(group,false);go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();Undo.RegisterCreatedObjectUndo(go,"Build solid floor segment");}else go=child.gameObject;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;var collider=go.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            var top=f.GetComponent<Renderer>().sharedMaterials.First(m=>m!=null&&(m.name.Contains("Floor")||m.name.Contains("Stairs")));
            go.GetComponent<Renderer>().sharedMaterials=new[]{top,edge,underside};go.isStatic=true;count++;if(stair)stairs++;
        }
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return new{solidModules=count,stairFlights=stairs,verticalThickness=Thickness};
    }
    public static object Audit()
    {
        var group=GameObject.Find("ServiceCavern").transform.Find("13 Solid service passage floor");var bad=new List<string>();double total=0;
        foreach(var f in group.GetComponentsInChildren<MeshFilter>())
        {
            var mesh=f.sharedMesh;var v=mesh.vertices;var tri=mesh.triangles;var edges=new Dictionary<string,int>();double volume=0;
            for(int i=0;i<tri.Length;i+=3)
            {
                var a=v[tri[i]];var b=v[tri[i+1]];var c=v[tri[i+2]];volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6.0;
                for(int j=0;j<3;j++){string ka=Key(v[tri[i+j]]),kb=Key(v[tri[i+(j+1)%3]]);string key=string.CompareOrdinal(ka,kb)<0?ka+"|"+kb:kb+"|"+ka;int n;edges.TryGetValue(key,out n);edges[key]=n+1;}
            }
            if(edges.Any(e=>e.Value!=2)||volume<=0)bad.Add(f.name+": unpaired edges="+edges.Count(e=>e.Value!=2)+", signed volume="+volume);
            total+=volume;
        }
        return new{modules=group.childCount,totalVolumeCubicMetres=total,invalid=bad.ToArray()};
    }
}
