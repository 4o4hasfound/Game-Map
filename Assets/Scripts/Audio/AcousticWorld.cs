using System.Collections.Generic;
using UnityEngine;

// Sound propagation through the hotel.
//  - Same room / clear line of sight: direct path.
//  - Otherwise: shortest route through the room graph's portals (doorways, archways, open corridor joins,
//    atrium voids, stairs). Every portal on the route bends the sound (diffraction: level and treble loss
//    grow with the bend angle); doors pass sound according to how far they are open. The sound then appears to
//    come from the doorway nearest the listener, at the full route length.
//  - In parallel: transmission straight through whatever is in the way, from its material (MaterialAcoustics).
// The stronger contribution decides the apparent direction; levels add as energies.
public class AcousticWorld : MonoBehaviour
{
    public static AcousticWorld Instance { get; private set; }

    public LayerMask occluders = 1 << 10 | 1 << 11;
    public LayerMask softOccluders = 1 << 12;

    public struct Path
    {
        public Vector3 apparent;
        public float length, gain, cutoff, send;
        public float remote;       // 0: heard in the listener's own space, 1: arrives from another space
        public string kind;

        public static Path Direct(Vector3 src, Vector3 lis)
        {
            return new Path { apparent = src, length = Vector3.Distance(src, lis), gain = 1f, cutoff = 20000f, send = 1f, kind = "direct" };
        }
    }

    readonly RaycastHit[] hits = new RaycastHit[32];

    // Everything solid on the straight line between source and listener, grouped into obstacles (both faces of a
    // wall are one obstacle). Returns total transmission loss (dB), the lowest cutoff and whether any of it is structure.
    float Transmission(Vector3 src, Vector3 lis, out float cutoff, out bool structural)
    {
        cutoff = 20000f;
        structural = false;
        Vector3 d = src - lis;
        float len = d.magnitude;
        if (len < 0.05f) return 0f;
        int n = Physics.RaycastNonAlloc(new Ray(lis, d / len), hits, len, AcousticSpace.Mask, QueryTriggerInteraction.Ignore);
        if (n == 0) return 0f;
        System.Array.Sort(hits, 0, n, Comparer.Instance);
        float total = 0f, groupTl = 0f, groupCut = 20000f, groupEnd = -1f;
        bool groupStruct = false;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (len - h.distance < 0.25f) continue;        // the surface the source itself sits on
            // the body of the device making the sound (a piano's cabinet radiates the sound; it does not block it)
            if (h.collider is BoxCollider && (h.collider.ClosestPoint(src) - src).sqrMagnitude < 1e-6f) continue;
            var surf = MaterialAcoustics.Of(h);
            int hl = h.collider.gameObject.layer;
            // A partly open leaf can cross the straight ray while leaving a clear route through its aperture.
            // Include doors in the path search rather than returning their straight-line transmission alone.
            bool st = hl == MaterialAcoustics.DoorLayer ||
                ((hl == MaterialAcoustics.StructureLayer || hl == MaterialAcoustics.CeilingLayer) && surf.tlDb > 20f);
            if (h.distance > groupEnd)
            {
                total += groupTl;
                cutoff = Mathf.Min(cutoff, groupCut);
                structural |= groupStruct;
                groupTl = 0f;
                groupCut = 20000f;
                groupStruct = false;
            }
            groupTl = Mathf.Max(groupTl, surf.tlDb);
            groupCut = Mathf.Min(groupCut, surf.tlCutoff);
            groupStruct |= st;
            groupEnd = h.distance + 0.45f;
        }
        total += groupTl;
        cutoff = Mathf.Min(cutoff, groupCut);
        structural |= groupStruct;
        return total;
    }

    class Comparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly Comparer Instance = new Comparer();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
    float[] cost = new float[256];
    float[] gainAcc = new float[256];
    float[] cutAcc = new float[256];
    int[] prev = new int[256];
    bool[] done = new bool[256];
    readonly Dictionary<LevelInfo,float[][]> staticDistances = new Dictionary<LevelInfo,float[][]>();
    int[] heapNode=new int[65536]; float[] heapCost=new float[65536]; int heapCount;
    void Push(int node,float value)
    {
        int i=++heapCount;
        while(i>1 && heapCost[i/2]>value){heapNode[i]=heapNode[i/2];heapCost[i]=heapCost[i/2];i/=2;}
        heapNode[i]=node;heapCost[i]=value;
    }
    int Pop(out float value)
    {
        int result=heapNode[1];value=heapCost[1];int node=heapNode[heapCount];float c=heapCost[heapCount--];int i=1;
        while(i*2<=heapCount){int child=i*2;if(child<heapCount&&heapCost[child+1]<heapCost[child])child++;if(heapCost[child]>=c)break;heapNode[i]=heapNode[child];heapCost[i]=heapCost[child];i=child;}
        heapNode[i]=node;heapCost[i]=c;return result;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // bind portals to the hinged doors that close them
        var doors = new Dictionary<string, HingeDoor>();
        foreach (var d in FindObjectsByType<HingeDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string key = d.portalName;
            if (!string.IsNullOrEmpty(key) && !doors.ContainsKey(key)) doors[key] = d;
        }
        if (HotelWorld.Instance == null) return;
        foreach (var lv in HotelWorld.Instance.levels)
        {
            int count=lv.portals.Count;var distances=new float[count][];
            for(int a=0;a<count;a++){distances[a]=new float[count];for(int b=0;b<count;b++)distances[a][b]=Vector3.Distance(lv.portals[a].pos,lv.portals[b].pos);}
            staticDistances[lv]=distances;
            foreach (var p in lv.portals)
                if (!string.IsNullOrEmpty(p.doorName) && doors.TryGetValue(p.doorName, out var hd)) p.door = hd;
        }
    }

    public Path Solve(Vector3 src, Vector3 lis)
    {
        var world = HotelWorld.Instance;
        var direct = Path.Direct(src, lis);
        if (world == null) return direct;
        RoomInfo rs = world.RoomAt(src), rl = world.RoomAt(lis);

        // straight line first: open air, or only soft / thin obstacles (furniture, glass, grating) in the way
        float tl = Transmission(src, lis, out float tlCut, out bool structural);
        if (!structural)
        {
            if (tl > 0.01f)
            {
                direct.gain = Mathf.Pow(10f, -tl / 20f);
                direct.cutoff = tlCut;
                direct.kind = "direct through obstacles";
            }
            return direct;
        }
        if (rs == null || rl == null || rs.level != rl.level)
            return Through(direct, tl, tlCut);
        if (rs == rl)
        {
            // same space, a solid element in between (column, stair, boiler): diffraction around it
            direct.gain = 0.5f;
            direct.cutoff = 2200f;
            direct.kind = "around obstacle";
            return direct;
        }

        var lv = rs.level;
        var portals = lv.portals;
        int P = portals.Count;
        heapCount=0;
        staticDistances.TryGetValue(lv,out var geometry);
        if (cost.Length < P)
        {
            cost = new float[P]; gainAcc = new float[P]; cutAcc = new float[P]; prev = new int[P]; done = new bool[P];
        }
        for (int i = 0; i < P; i++)
        {
            cost[i] = float.MaxValue;
            done[i] = false;
            prev[i] = -1;
        }
        foreach (int pi in rs.portals)
        {
            var p = portals[pi];
            float c = Vector3.Distance(src, p.pos) + DoorPenalty(p);
            if (c < cost[pi])
            {
                cost[pi] = c;
                Push(pi,c);
                gainAcc[pi] = DoorGain(p) * Bend(src, p.pos, NextGuess(p, rl));
                cutAcc[pi] = DoorCut(p);
            }
        }
        int best = -1;
        float bestTotal = float.MaxValue;
        while (heapCount>0)
        {
            int u=Pop(out float uc);
            if(done[u] || uc>cost[u])continue;
            if (uc >= bestTotal) break;
            done[u] = true;
            var pu = portals[u];
            if (pu.a == rl.id || pu.b == rl.id)
            {
                float total = uc + Vector3.Distance(pu.pos, lis);
                if (total < bestTotal) { bestTotal = total; best = u; }
            }
            for (int side = 0; side < 2; side++)
            {
                var room = lv.rooms[side == 0 ? pu.a : pu.b];
                foreach (int v in room.portals)
                {
                    if (v == u || done[v]) continue;
                    var pv = portals[v];
                    float c = uc + (geometry!=null?geometry[u][v]:Vector3.Distance(pu.pos,pv.pos)) + DoorPenalty(pv);
                    if (c < cost[v])
                    {
                        cost[v] = c;
                        Push(v,c);
                        prev[v] = u;
                        Vector3 from = prev[u] >= 0 ? portals[prev[u]].pos : src;
                        gainAcc[v] = gainAcc[u] * DoorGain(pv) * Bend(from, pu.pos, pv.pos);
                        cutAcc[v] = Mathf.Min(cutAcc[u], DoorCut(pv)) * BendCut(from, pu.pos, pv.pos);
                    }
                }
            }
        }
        var through = Through(direct, tl, tlCut);
        if (best < 0) return through;

        var last = portals[best];
        Vector3 before = prev[best] >= 0 ? portals[prev[best]].pos : src;
        float g = gainAcc[best] * Bend(before, last.pos, lis);
        float cut = Mathf.Min(cutAcc[best], 20000f) * BendCut(before, last.pos, lis);
        Vector3 dir = (last.pos - lis);
        if (dir.sqrMagnitude < 1e-4f) dir = src - lis;
        var portal = new Path
        {
            apparent = lis + dir.normalized * bestTotal,
            length = bestTotal,
            gain = g,
            cutoff = cut,
            send = Mathf.Max(0.35f, g),
            remote = 0.75f,
            kind = last.doorName != null ? "via door openings" : "via connected openings"
        };

        // combine with wall transmission as energies (distance laws differ: route vs straight line)
        float ep = Sq(portal.gain / Mathf.Max(1f, portal.length));
        float et = Sq(through.gain / Mathf.Max(1f, through.length));
        if (et > ep)
        {
            through.gain = Mathf.Sqrt(et + ep) * Mathf.Max(1f, through.length);
            return through;
        }
        portal.gain = Mathf.Sqrt(et + ep) * Mathf.Max(1f, portal.length);
        portal.cutoff = Mathf.Lerp(portal.cutoff, through.cutoff, et / (et + ep + 1e-9f));
        return portal;
    }

    static float Sq(float x) => x * x;

    int CountHops(int i)
    {
        int n = 1;
        while (prev[i] >= 0 && n < 50) { i = prev[i]; n++; }
        return n;
    }

    static Vector3 NextGuess(PortalInfo p, RoomInfo target) => p.pos;

    static Path Through(Path direct, float tlDb, float cutoff)
    {
        direct.gain = Mathf.Pow(10f, -Mathf.Min(tlDb, 90f) / 20f);
        direct.cutoff = Mathf.Max(cutoff, 120f);
        direct.send = 0.3f;
        direct.remote = 0.9f;
        direct.kind = "through structure";
        return direct;
    }

    static float DoorPenalty(PortalInfo p) => (1f - p.Openness) * 10f;
    static float DoorGain(PortalInfo p) => Mathf.Lerp(0.09f, 1f, Mathf.Sqrt(p.Openness));
    static float DoorCut(PortalInfo p) => Mathf.Lerp(650f, 20000f, p.Openness * p.Openness);

    // diffraction around the edge of an opening: up to about -6 dB for a full U-turn
    static float Bend(Vector3 a, Vector3 b, Vector3 c)
    {
        float ang = Vector3.Angle(b - a, c - b) / 180f;
        return Mathf.Lerp(1f, 0.5f, ang);
    }

    static float BendCut(Vector3 a, Vector3 b, Vector3 c)
    {
        float ang = Vector3.Angle(b - a, c - b) / 180f;
        return Mathf.Lerp(1f, 0.22f, Mathf.Sqrt(ang));
    }
}
