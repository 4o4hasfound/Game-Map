using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Builds the walkable mesh for a floor at runtime from its colliders (structure, stairs ramps, furniture as
// obstacles; doors are left out because the monster opens them). Low spaces like the pipe crawl get no mesh
// because the agent is taller than they are, which is what keeps the monster out of them.
public class NavBaker : MonoBehaviour
{
    public static NavBaker Instance { get; private set; }
    public const float AgentRadius = 0.24f, AgentHeight = 1.95f;
    readonly Dictionary<int, NavMeshDataInstance> baked = new Dictionary<int, NavMeshDataInstance>();
    readonly HashSet<int> building = new HashSet<int>();
    readonly List<NavMeshData> ownedData=new List<NavMeshData>();
    bool disposed;
    public bool Ready(int level) => baked.ContainsKey(level);

    void Awake() => Instance = this;

    public static NavMeshBuildSettings Settings()
    {
        var s = NavMesh.GetSettingsByID(0);
        s.agentRadius = AgentRadius;
        s.agentHeight = AgentHeight;
        s.agentClimb = 0.45f;
        s.agentSlope = 46f;
        s.overrideVoxelSize = true;
        s.voxelSize = 0.06f;
        return s;
    }

    public AsyncOperation Bake(int level)
    {
        var world = HotelWorld.Instance;
        if (world == null || baked.ContainsKey(level) || building.Contains(level)) return null;
        var root = world.levels[level].root;
        if (!root) return null;
        var b = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        foreach (var c in root.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) b.Encapsulate(c.bounds);
        b.Expand(2f);
        var sources = new List<NavMeshBuildSource>();
        int mask = (1 << MaterialAcoustics.StructureLayer) | (1 << MaterialAcoustics.PropLayer);
        NavMeshBuilder.CollectSources(b, mask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var data = new NavMeshData(0);
        ownedData.Add(data);
        building.Add(level);
        var op = NavMeshBuilder.UpdateNavMeshDataAsync(data, Settings(), sources, b);
        op.completed += _ =>
        {
            if(disposed)return;
            building.Remove(level);
            baked[level] = NavMesh.AddNavMeshData(data);
            Debug.Log($"[NavBaker] level {level}: {sources.Count} sources baked");
        };
        return op;
    }

    void OnDestroy()
    {
        disposed=true;
        foreach(var entry in baked)entry.Value.Remove();
        baked.Clear();
        foreach(var data in ownedData)if(data){NavMeshBuilder.Cancel(data);Destroy(data);}
        ownedData.Clear();building.Clear();
    }

    public static bool RandomPoint(Vector3 around, float minR, float maxR, out Vector3 p)
    {
        for (int i = 0; i < 40; i++)
        {
            var d = Random.insideUnitCircle.normalized * Random.Range(minR, maxR);
            if (NavMesh.SamplePosition(around + new Vector3(d.x, 0f, d.y), out var hit, 3f, NavMesh.AllAreas))
            {
                p = hit.position;
                return true;
            }
        }
        p = around;
        return false;
    }

    // Select a floor-level standing point, rather than snapping a room centroid onto a tabletop nav island.
    public static bool RoomPoint(HotelWorld world,LevelInfo level,RoomInfo room,out NavMeshHit result)
    {
        result=default;var layer=level.layers[room.layer];float cx=0,cy=0;int count=0;
        for(int y=0;y<layer.h;y++)for(int x=0;x<layer.w;x++)if(layer.cells[y*layer.w+x]==room.id){cx+=x+.5f;cy+=y+.5f;count++;}
        if(count==0)return false;cx/=count;cy/=count;float best=float.MaxValue;bool found=false;
        int mask=(1<<MaterialAcoustics.StructureLayer)|(1<<MaterialAcoustics.PropLayer);
        for(int y=0;y<layer.h;y++)for(int x=0;x<layer.w;x++)
        {
            if(layer.cells[y*layer.w+x]!=room.id)continue;float d=(x+.5f-cx)*(x+.5f-cx)+(y+.5f-cy)*(y+.5f-cy);if(d>=best)continue;
            var p=world.ToUnity(level.ox+x+.5f,y+.5f,layer.z+.05f);
            if(!NavMesh.SamplePosition(p,out var hit,.6f,-1)||Mathf.Abs(hit.position.y-p.y)>.32f)continue;
            var foot=hit.position;
            if(Physics.CheckCapsule(foot+Vector3.up*(AgentRadius+.04f),foot+Vector3.up*(AgentHeight-AgentRadius),AgentRadius,mask,QueryTriggerInteraction.Ignore))continue;
            found=true;best=d;result=hit;
        }
        return found;
    }
}
