using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class MetroCaveTerrain
{
    const string Folder="Assets/Core/Visual/Environment/ServiceCavern/Terrain/";
    static readonly float[] Z={34.66f,37.86f,41.36f,44.86f,48.36f,51.86f,55.36f,58.86f,62.36f,65.86f,69.36f,72.86f,76.36f,79.86f,83.36f,86.86f,94};
    static readonly float[] Y={-23.27f,-23.27f,-22.68f,-22.09f,-21.49f,-20.90f,-20.90f,-18.10f,-15.30f,-12.50f,-12.50f,-9.70f,-6.90f,-6.90f,-4.10f,-1.30f,-1.30f};
    static float Smooth(float a,float b,float x){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,x));}
    public static float StairDatum(float z)
    {
        if(z<=Z[0])return Y[0];
        for(int i=1;i<Z.Length;i++)if(z<=Z[i])return Mathf.Lerp(Y[i-1],Y[i],Mathf.InverseLerp(Z[i-1],Z[i],z));
        return Y[Y.Length-1];
    }
    static float Sample(float[,] values,float x,float z)
    {
        int mx=values.GetLength(1)-1,mz=values.GetLength(0)-1;x=Mathf.Clamp(x,0,mx);z=Mathf.Clamp(z,0,mz);
        int a=Mathf.FloorToInt(x),b=Mathf.FloorToInt(z),c=Mathf.Min(a+1,mx),d=Mathf.Min(b+1,mz);
        return Mathf.Lerp(Mathf.Lerp(values[b,a],values[b,c],x-a),Mathf.Lerp(values[d,a],values[d,c],x-a),z-b);
    }
    static float Weight(float x,float z,float baseline)
    {
        float top=StairDatum(z)-.43f;
        float rise=Mathf.Max(0,top-baseline),width=12+1.6f*rise;
        // Continue the bank into the cliff, instead of tapering it down just before
        // the north wall and leaving a steep, exposed edge behind the landing.
        return (1-Smooth(0,width,Mathf.Max(0,x-32.6f)))*Smooth(35.5f,39,z);
    }
    public static string GradeUnderStairs()
    {
        var root=GameObject.Find("ServiceCavern");if(root==null)throw new InvalidOperationException("Cavern not found");
        var terrain=root.GetComponentInChildren<Terrain>();var td=terrain.terrainData;var pos=terrain.transform.position;
        int h=td.heightmapResolution;var before=td.GetHeights(0,0,h,h);float previousHeight=td.size.y;
        string backupPath=Folder+"ServiceCavern_Floor_BeforeStairGrade.asset";
        bool first=root.transform.Find("Stair bank authoring marker")==null;
        var baseline=AssetDatabase.LoadAssetAtPath<TerrainData>(backupPath);
        if(first||baseline==null)
        {
            var copy=Object.Instantiate(td);copy.name="ServiceCavern_Floor_BeforeStairGrade";
            if(baseline==null){AssetDatabase.CreateAsset(copy,backupPath);baseline=copy;}
            else {EditorUtility.CopySerialized(copy,baseline);Object.DestroyImmediate(copy);}
        }
        var source=baseline.GetHeights(0,0,h,h);var heights=new float[h,h];
        var alpha=baseline.GetAlphamaps(0,0,td.alphamapResolution,td.alphamapResolution);
        var holes=baseline.GetHoles(0,0,td.holesResolution,td.holesResolution);
        var size=td.size;size.y=Mathf.Max(40,size.y);
        for(int z=0;z<h;z++)for(int x=0;x<h;x++)
        {
            float wx=pos.x+x*size.x/(h-1),wz=pos.z+z*size.z/(h-1);
            float old=pos.y+source[z,x]*baseline.size.y,weight=Weight(wx,wz,old);
            float average=(StairDatum(wz-1.2f)+2*StairDatum(wz-.6f)+3*StairDatum(wz)+2*StairDatum(wz+.6f)+StairDatum(wz+1.2f))/9;
            float top=Mathf.Min(average-.43f,StairDatum(wz)-.38f);
            float target=old+Mathf.Max(0,top-old)*weight;
            target+=(Mathf.PerlinNoise(wx*.2f,wz*.2f)-.5f)*.18f*weight*(1-weight);
            heights[z,x]=Mathf.Clamp01((target-pos.y)/size.y);
        }
        int ar=td.alphamapResolution;
        for(int z=0;z<ar;z++)for(int x=0;x<ar;x++)
        {
            float wx=pos.x+x*size.x/(ar-1),wz=pos.z+z*size.z/(ar-1);
            float old=pos.y+Sample(source,(float)x*(h-1)/(ar-1),(float)z*(h-1)/(ar-1))*baseline.size.y;
            float w=Weight(wx,wz,old)*Smooth(0,3,StairDatum(wz)-.43f-old);
            if(alpha.GetLength(2)==3)
            {
                alpha[z,x,0]=Mathf.Lerp(alpha[z,x,0],.62f,w);
                alpha[z,x,1]=Mathf.Lerp(alpha[z,x,1],.30f,w);
                alpha[z,x,2]=Mathf.Lerp(alpha[z,x,2],.08f,w);
            }
        }
        int hr=td.holesResolution;
        var vault=root.transform.Find("00 Bedrock vault").GetComponentsInChildren<MeshCollider>();
        for(int z=0;z<hr;z++)for(int x=0;x<hr;x++)
        {
            float wx=pos.x+(x+.5f)*size.x/hr,wz=pos.z+(z+.5f)*size.z/hr;
            if(wx>=29.4f&&wx<=33.4f&&wz>=37.86f&&wz<=91)holes[z,x]=true;
            if(wx>=29.4f&&wx<90&&wz>=79)
            {
                float y=pos.y+Sample(heights,(x+.5f)*(h-1)/hr,(z+.5f)*(h-1)/hr)*size.y;
                float wall=0;
                foreach(var c in vault)
                {
                    RaycastHit hit;
                    if(c.Raycast(new Ray(new Vector3(wx,y,60),Vector3.forward),out hit,42)&&hit.normal.z<-.15f)
                        wall=wall==0?hit.point.z:Mathf.Min(wall,hit.point.z);
                }
                // Deliberate overlap hides the tessellated terrain-hole boundary
                // inside the solid rock rather than leaving a daylight crevice.
                if(wall>0&&wz<=wall+.85f)holes[z,x]=true;
            }
            if(pos.x+(x+1)*size.x/hr>93.35435f)holes[z,x]=false;
        }
        Undo.RegisterCompleteObjectUndo(td,"Sculpt continuous ground beneath service stairs");
        td.size=size;td.SetHeights(0,0,heights);td.SetAlphamaps(0,0,alpha);td.SetHoles(0,0,holes);terrain.Flush();EditorUtility.SetDirty(td);
        Physics.SyncTransforms();
        var roofs=root.transform.Find("00 Bedrock vault").GetComponentsInChildren<MeshCollider>();
        int moved=0,hidden=0;
        foreach(string group in new[]{"01 Rock outcrops","02 Talus and loose stones","03 Sparse coloured minerals"})
        foreach(var t in root.transform.Find(group).Cast<Transform>())
        {
            if(!t.gameObject.activeSelf)continue;var renderer=t.GetComponent<Renderer>();if(renderer==null)continue;
            var b=renderer.bounds;float sx=(b.center.x-pos.x)/size.x*(h-1),sz=(b.center.z-pos.z)/size.z*(h-1);
            float old=pos.y+Sample(before,sx,sz)*previousHeight,newHeight=pos.y+Sample(heights,sx,sz)*size.y;
            float lift=newHeight-old;if(lift<.01f||b.min.y<old-3||b.min.y>old+1||t.position.y>old+1)continue;
            if(b.center.x<36&&b.center.z>54)
            {Undo.RecordObject(t.gameObject,"Keep stair edge clear");t.gameObject.SetActive(false);hidden++;continue;}
            float ceiling=25;
            foreach(var roof in roofs){RaycastHit hit;if(roof.Raycast(new Ray(new Vector3(b.center.x,newHeight+.15f,b.center.z),Vector3.up),out hit,60))ceiling=Mathf.Min(ceiling,hit.point.y);}
            float available=ceiling-newHeight-1.5f;
            if(available<.6f){Undo.RecordObject(t.gameObject,"Keep flying clearance above bank");t.gameObject.SetActive(false);hidden++;continue;}
            Undo.RecordObject(t,"Set rocks onto sculpted slope");
            if(b.size.y>available)t.localScale*=Mathf.Clamp(available/b.size.y,.1f,1);
            t.position+=Vector3.up*(newHeight-.18f-renderer.bounds.min.y);moved++;
        }
        // Keep the raised bank readable: large floor boulders become smaller loose stones here.
        foreach(var t in root.transform.Find("01 Rock outcrops").Cast<Transform>())
        {
            if(!t.gameObject.activeSelf)continue;var renderer=t.GetComponent<Renderer>();if(renderer==null)continue;
            var b=renderer.bounds;if(b.center.x>65||b.center.z<55)continue;
            float sx=(b.center.x-pos.x)/size.x*(h-1),sz=(b.center.z-pos.z)/size.z*(h-1);
            float old=pos.y+Sample(source,sx,sz)*baseline.size.y,ground=pos.y+Sample(heights,sx,sz)*size.y;
            if(ground-old<3||b.min.y<ground-2||b.min.y>ground+1.0f)continue;
            float maxHeight=b.center.x<43?2.5f:3.2f;if(b.size.y<=maxHeight)continue;
            Undo.RecordObject(t,"Reduce loose stone size on stair bank");t.localScale*=maxHeight/b.size.y;
            t.position+=Vector3.up*(ground-.18f-renderer.bounds.min.y);
        }
        if(first)
        {
            var marker=new GameObject("Stair bank authoring marker");marker.transform.SetParent(root.transform,false);marker.SetActive(false);
            Undo.RegisterCreatedObjectUndo(marker,"Remember sculpted stair bank");
        }
        Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
        return "Continuous stair bank sculpted; rocks placed on slope="+moved+"; rocks removed from stair/roof clearance="+hidden;
    }
}
