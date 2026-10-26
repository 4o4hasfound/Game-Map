using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class HotelBuilder
{
    // Preserve original collider meshes/material indexing for physics and acoustics.
    // Render only spatial chunks; a cubemap face must not submit an entire floor's material batches.
    static void ChunkEnvironmentRenderers(Dictionary<string,Transform> objects)
    {
        string folder="Assets/Generated/Chunks_"+Path.GetFileNameWithoutExtension(ScenePath);
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Generated",Path.GetFileName(folder));
        long originalTriangles=0,chunkTriangles=0;int count=0;
        foreach(var pair in objects)
        {
            string name=pair.Key;
            if(!(name.StartsWith("Props_")||name.StartsWith("Walls_")||name.StartsWith("Floor_")||name.StartsWith("Ceiling_")||name.StartsWith("Railings_")||name.StartsWith("Trim_")))continue;
            var parent=pair.Value;var renderer=parent.GetComponent<MeshRenderer>();var filter=parent.GetComponent<MeshFilter>();
            if(!renderer||!filter||!filter.sharedMesh)continue;
            var source=filter.sharedMesh;var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var tangents=source.tangents;var mats=renderer.sharedMaterials;
            var groups=new Dictionary<Vector3Int,Dictionary<int,List<int>>>();
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var triangles=source.GetTriangles(sub);originalTriangles+=triangles.Length/3;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var centre=(vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3f;
                    var cell=new Vector3Int(Mathf.FloorToInt(centre.x/12f),Mathf.FloorToInt(centre.y/6f),Mathf.FloorToInt(centre.z/12f));
                    if(!groups.TryGetValue(cell,out var group)){group=new Dictionary<int,List<int>>();groups[cell]=group;}
                    if(!group.TryGetValue(sub,out var list)){list=new List<int>();group[sub]=list;}
                    list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);
                }
            }
            int number=0;
            foreach(var group in groups.Values)
            {
                var remap=new Dictionary<int,int>();var points=new List<Vector3>();var ns=new List<Vector3>();var uvs=new List<Vector2>();var ts=new List<Vector4>();var parts=new List<int[]>();var materials=new List<Material>();
                foreach(var sub in group)
                {
                    var indices=new int[sub.Value.Count];
                    for(int i=0;i<indices.Length;i++)
                    {
                        int old=sub.Value[i];
                        if(!remap.TryGetValue(old,out int index))
                        {
                            index=points.Count;remap[old]=index;points.Add(vertices[old]);
                            if(normals.Length==vertices.Length)ns.Add(normals[old]);
                            if(uv.Length==vertices.Length)uvs.Add(uv[old]);
                            if(tangents.Length==vertices.Length)ts.Add(tangents[old]);
                        }
                        indices[i]=index;
                    }
                    parts.Add(indices);materials.Add(mats[sub.Key]);chunkTriangles+=indices.Length/3;
                }
                var mesh=new Mesh{name=name+"_render_"+number,indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
                mesh.SetVertices(points);if(ns.Count>0)mesh.SetNormals(ns);if(uvs.Count>0)mesh.SetUVs(0,uvs);if(ts.Count>0)mesh.SetTangents(ts);
                mesh.subMeshCount=parts.Count;for(int i=0;i<parts.Count;i++)mesh.SetTriangles(parts[i],i,false);mesh.RecalculateBounds();if(ns.Count==0)mesh.RecalculateNormals();
                AssetDatabase.CreateAsset(mesh,folder+"/"+mesh.name+".asset");
                var child=new GameObject("Render_"+number++);child.transform.SetParent(parent,false);child.layer=parent.gameObject.layer;
                child.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=child.AddComponent<MeshRenderer>();mr.sharedMaterials=materials.ToArray();
                mr.shadowCastingMode=renderer.shadowCastingMode;mr.receiveShadows=renderer.receiveShadows;
                // Keep spatial culling granularity instead of recombining a complete floor in static batching.
                child.isStatic=false;count++;
            }
            renderer.enabled=false;
        }
        if(originalTriangles!=chunkTriangles)throw new System.Exception("Render chunk triangle mismatch");
        Debug.Log($"[HotelBuilder] {count} spatial render chunks; triangle preservation {originalTriangles}/{chunkTriangles}; original collision meshes retained");
    }
}
