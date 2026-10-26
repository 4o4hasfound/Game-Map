using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class HotelNavigationAudit
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Hotel.unity");
        var world=Object.FindFirstObjectByType<HotelWorld>();world.Load();world.SetLevel(0,true);Physics.SyncTransforms();
        var sources=new List<NavMeshBuildSource>();int mask=(1<<10)|(1<<12);
        var bounds=new Bounds(world.ToUnity(50,25,1),new Vector3(120,20,70));
        NavMeshBuilder.CollectSources(bounds,mask,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
        var data=NavMeshBuilder.BuildNavMeshData(NavBaker.Settings(),sources,bounds,Vector3.zero,Quaternion.identity);
        var instance=NavMesh.AddNavMeshData(data);var report=new StringBuilder();var lv=world.levels[0];
        var target=lv.rooms.Find(r=>r.number==725);var hall=lv.rooms.Find(r=>r.name=="West Hall (Renovation)");
        NavBaker.RoomPoint(world,lv,hall,out var from);NavBaker.RoomPoint(world,lv,target,out var to);
        var path=new NavMeshPath();NavMesh.CalculatePath(from.position,to.position,-1,path);
        report.AppendLine($"Room 725: {from.position} -> {to.position}: {path.status}");
        for(float z=.25f;z<2.05f;z+=.35f)for(int k=0;k<8;k++)
        {
            var direction=new Vector3(Mathf.Cos(k*Mathf.PI/4),0,Mathf.Sin(k*Mathf.PI/4));
            var hits=Physics.SphereCastAll(world.ToUnity(8.25f,30.5f,z),.15f,direction,.7f,mask,QueryTriggerInteraction.Ignore);
            foreach(var h in hits)if(h.collider.name.StartsWith("Props"))report.AppendLine($"DOOR OBSTACLE z={z:0.00} {h.collider.name} {MaterialAcoustics.MaterialName(h)} at {h.point}");
        }
        for(float x=3.5f;x<=9.5f;x+=.5f)for(float y=28.5f;y<=33.5f;y+=.5f)
        {
            var p=world.ToUnity(x,y,.04f);bool sampled=NavMesh.SamplePosition(p,out var h,.25f,-1);
            if(sampled)NavMesh.CalculatePath(from.position,h.position,-1,path);
            var cols=Physics.OverlapCapsule(p+Vector3.up*.3f,p+Vector3.up*1.7f,.24f,mask,QueryTriggerInteraction.Ignore);
            var names=new List<string>();foreach(var col in cols)names.Add(col.name);
            report.AppendLine($"({x:0.0},{y:0.0}) mesh={sampled} y={(sampled?h.position.y:0):0.00} path={(sampled?path.status.ToString():"none")} colliders={string.Join(",",names)}");
        }
        File.WriteAllText(Path.GetFullPath("../verification/room725_navigation_audit.txt"),report.ToString());
        instance.Remove();EditorApplication.Exit(0);
    }
}
