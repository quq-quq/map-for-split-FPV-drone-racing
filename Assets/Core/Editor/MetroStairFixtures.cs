using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class MetroStairFixtures
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/";
    static Mesh Store(Mesh source,string name)
    {
        source.name=name;string path=Folder+"Meshes/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null){AssetDatabase.CreateAsset(source,path);return source;}
        mesh.Clear();mesh.indexFormat=source.indexFormat;mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.uv=source.uv;mesh.tangents=source.tangents;mesh.subMeshCount=source.subMeshCount;
        for(int s=0;s<source.subMeshCount;s++)mesh.SetTriangles(source.GetTriangles(s),s,false);
        mesh.bounds=source.bounds;mesh.name=name;mesh.MarkModified();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);Object.DestroyImmediate(source);return mesh;
    }
    static void Assign(MeshFilter mf,Mesh mesh)
    {
        Undo.RecordObject(mf,"Replace stair outer pipes with guardrail");mf.sharedMesh=mesh;
        foreach(var c in mf.GetComponents<MeshCollider>()){Undo.RecordObject(c,"Update outer stair collision");c.sharedMesh=null;c.sharedMesh=mesh;}
    }
    static void ClearOuterServices(Transform modules)
    {
        foreach(var mf in modules.GetComponentsInChildren<MeshFilter>())
        {
            int index;if(!int.TryParse(mf.name.Substring(0,2),out index)||index<7||index>20||mf.name.Contains("StairHall"))continue;
            string name="StairServicesSource_"+mf.name,path=Folder+"Meshes/"+name+".asset";
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(source==null)source=Store(Object.Instantiate(mf.sharedMesh),name);
            var mesh=Object.Instantiate(source);var vertices=source.vertices.Select(mf.transform.TransformPoint).ToArray();
            var mats=mf.GetComponent<Renderer>().sharedMaterials;
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                if(!mats[sub].name.Contains("Details")&&!mats[sub].name.Contains("Neon"))continue;
                var ids=source.GetTriangles(sub);var keep=new List<int>();
                for(int i=0;i<ids.Length;i+=3)
                {
                    if(vertices[ids[i]].x>31.205f||vertices[ids[i+1]].x>31.205f||vertices[ids[i+2]].x>31.205f)continue;
                    keep.Add(ids[i]);keep.Add(ids[i+1]);keep.Add(ids[i+2]);
                }
                mesh.SetTriangles(keep,sub);
            }
            mesh.RecalculateBounds();Assign(mf,Store(mesh,"StairOuterClear_"+mf.name));
        }
    }
    sealed class TubeMesh
    {
        readonly List<Vector3> v=new List<Vector3>(),n=new List<Vector3>();
        readonly List<Vector2> uv=new List<Vector2>();readonly List<int> tri=new List<int>();
        const int Sides=10;
        void Triangle(int a,int b,int c,Vector3 normal)
        {if(Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]),normal)<0){int t=b;b=c;c=t;}tri.Add(a);tri.Add(b);tri.Add(c);}
        public void Add(Vector3[] path,float radius)
        {
            int offset=v.Count;
            for(int j=0;j<path.Length;j++)
            {
                Vector3 tangent=(j==0?path[1]-path[0]:j==path.Length-1?path[j]-path[j-1]:((path[j]-path[j-1]).normalized+(path[j+1]-path[j]).normalized)).normalized;
                var right=Mathf.Abs(Vector3.Dot(tangent,Vector3.right))<.8f?Vector3.right:Vector3.up;
                right=(right-tangent*Vector3.Dot(right,tangent)).normalized;var up=Vector3.Cross(tangent,right).normalized;
                for(int i=0;i<Sides;i++){float a=i*Mathf.PI*2/Sides;var normal=right*Mathf.Cos(a)+up*Mathf.Sin(a);v.Add(path[j]+normal*radius);n.Add(normal);uv.Add(new Vector2((float)i/Sides,j));}
            }
            for(int j=0;j<path.Length-1;j++)for(int i=0;i<Sides;i++)
            {
                int a=offset+j*Sides+i,b=offset+j*Sides+(i+1)%Sides,c=a+Sides,d=b+Sides;
                Triangle(a,b,c,n[a]);Triangle(b,d,c,n[b]);
            }
            foreach(int end in new[]{0,path.Length-1})
            {
                var normal=(end==0?path[0]-path[1]:path[end]-path[end-1]).normalized;int center=v.Count;v.Add(path[end]);n.Add(normal);uv.Add(Vector2.zero);
                for(int i=0;i<Sides;i++)Triangle(center,offset+end*Sides+i,offset+end*Sides+(i+1)%Sides,normal);
            }
        }
        public Mesh Mesh()
        {
            var m=new Mesh{name="Service stair guardrail"};m.SetVertices(v);m.SetNormals(n);m.SetUVs(0,uv);m.SetTriangles(tri,0);m.RecalculateBounds();m.RecalculateTangents();return m;
        }
    }
    static GameObject Child(Transform parent,string name)
    {
        var t=parent.Find(name);if(t!=null)return t.gameObject;
        var go=new GameObject(name);go.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(go,"Add stair lights and railings");return go;
    }
    static void BuildRails(Transform parent,Transform modules,Material steel)
    {
        float[] knots={37.72f,37.86f,41.36f,44.86f,48.36f,51.86f,55.36f,58.86f,62.36f,65.86f,69.36f,72.86f,76.36f,79.86f,83.36f,86.86f};
        var mesh=new TubeMesh();
        foreach(float height in new[]{1.07f,.54f})
        {
            var path=knots.Select(z=>new Vector3(32.70f,MetroCaveTerrain.StairDatum(z)+height,z)).ToArray();
            mesh.Add(path,height>1?.04f:.025f);
            mesh.Add(new[]{path[path.Length-1],path[path.Length-1]+Vector3.right*.16f},height>1?.04f:.025f);
        }
        var posts=new List<float>(knots);for(float z=39.1f;z<86;z+=1.4f)posts.Add(z);
        var colliders=modules.GetComponentsInChildren<MeshCollider>();
        foreach(float z in posts.OrderBy(p=>p).Aggregate(new List<float>(),(list,p)=>{if(list.Count==0||p-list[list.Count-1]>.32f)list.Add(p);return list;}))
        {
            float floor=MetroCaveTerrain.StairDatum(z),bottom=floor;
            foreach(var collider in colliders)
            {
                RaycastHit hit;if(collider.Raycast(new Ray(new Vector3(32.70f,floor+.45f,z),Vector3.down),out hit,1))bottom=Mathf.Max(bottom,hit.point.y);
            }
            mesh.Add(new[]{new Vector3(32.70f,bottom-.015f,z),new Vector3(32.70f,floor+1.07f,z)},.035f);
        }
        var go=Child(parent,"Continuous outer stair guardrail");if(go.GetComponent<MeshFilter>()==null){go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();}
        Assign(go.GetComponent<MeshFilter>(),Store(mesh.Mesh(),"ServiceStair_Guardrail"));go.GetComponent<Renderer>().sharedMaterial=steel;go.isStatic=true;
    }
    static void Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material,bool solid)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());go.isStatic=true;
    }
    static GameObject LampPrefab(Material steel)
    {
        string path=Folder+"ServiceStair_Lamp.prefab";var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab!=null)return prefab;
        string materialPath=Folder+"Materials/ServiceStair_LampLens.mat";var lens=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(lens==null)
        {
            lens=new Material(Shader.Find("Universal Render Pipeline/Lit"));lens.name="ServiceStair_LampLens";
            lens.SetColor("_BaseColor",new Color(.95f,.90f,.78f));lens.SetColor("_EmissionColor",new Color(1,.84f,.61f)*2.4f);lens.EnableKeyword("_EMISSION");lens.SetFloat("_Smoothness",.35f);AssetDatabase.CreateAsset(lens,materialPath);
        }
        var template=new GameObject("Service stair ceiling lamp");
        Part(template.transform,"Metal housing",new Vector3(0,-.06f,0),new Vector3(.34f,.12f,.90f),steel,true);
        Part(template.transform,"Frosted illuminated lens",new Vector3(0,-.145f,0),new Vector3(.25f,.05f,.74f),lens,false);
        foreach(float x in new[]{-.15f,.15f})Part(template.transform,"Side rim",new Vector3(x,-.12f,0),new Vector3(.035f,.1f,.9f),steel,false);
        foreach(float z in new[]{-.42f,.42f})Part(template.transform,"End cap",new Vector3(0,-.12f,z),new Vector3(.34f,.1f,.065f),steel,false);
        foreach(float z in new[]{-.24f,0,.24f})Part(template.transform,"Protective crossbar",new Vector3(0,-.185f,z),new Vector3(.3f,.025f,.025f),steel,false);
        prefab=PrefabUtility.SaveAsPrefabAsset(template,path);Object.DestroyImmediate(template);return prefab;
    }
    public static string Apply()
    {
        var cave=GameObject.Find("ServiceCavern");var route=GameObject.Find("MetroDepotPassage/ServicePassage2_Corridor");
        if(cave==null||route==null)throw new InvalidOperationException("Service stairs not found");
        var group=Child(cave.transform,"10 Stair lamps and guardrail").transform;
        var steel=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Materials/Guardrail_DarkSteel.mat");var modules=route.transform.Find("Modules");
        ClearOuterServices(modules);BuildRails(group,modules,steel);Physics.SyncTransforms();
        var prefab=LampPrefab(steel);IEnumerable<Collider> roof=cave.transform.Find("09 Solid stair hall ceiling").GetComponentsInChildren<Collider>();
        var wideEntry=cave.transform.Find("14 Wide upper flight entrance");
        if(wideEntry!=null)roof=roof.Concat(wideEntry.GetComponentsInChildren<Collider>());
        var surfaces=roof.Concat(route.GetComponentsInChildren<MeshCollider>().Cast<Collider>()).ToArray();
        int lamps=0;
        foreach(var light in route.GetComponentsInChildren<Light>())
        {
            int index;if(!int.TryParse(light.name.Replace("Light_",""),out index)||index<2||index>6)continue;
            string name="Ceiling lamp for "+light.name;var t=group.Find(name);
            var position=t!=null?t.position:light.transform.position;var ray=new Ray(new Vector3(position.x,MetroCaveTerrain.StairDatum(position.z)+2,position.z),Vector3.up);
            RaycastHit nearest=new RaycastHit();float distance=float.MaxValue;
            foreach(var surface in surfaces){RaycastHit hit;if(surface.Raycast(ray,out hit,5)&&hit.distance<distance){distance=hit.distance;nearest=hit;}}
            if(distance==float.MaxValue)throw new InvalidOperationException("Ceiling mount not found for "+light.name);
            var normal=-nearest.normal.normalized;if(normal.y<0)normal=-normal;
            GameObject go;
            if(t==null){go=Object.Instantiate(prefab);go.name=name;go.transform.SetParent(group,false);Undo.RegisterCreatedObjectUndo(go,"Fit lamp to existing stair lighting");}else go=t.gameObject;
            Undo.RecordObject(go.transform,"Place stair lamp");go.transform.position=nearest.point;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,normal);
            Undo.RecordObject(light.transform,"Place light below lamp lens");light.transform.position=nearest.point-normal*.30f;lamps++;
        }
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(cave.scene);EditorSceneManager.SaveScene(cave.scene);
        return "Ceiling lamps placed="+lamps+"; outer stair pipe assemblies removed; continuous gallery-style guardrail installed.";
    }
}
