using System.Collections.Generic;
using UnityEngine;

// Room graph used by the ported acoustic code (AcousticWorld, AudioEngine, Footsteps) and the listener (the monster).
// All three hotel floors are one graph: the atrium, the stairs and the basement stairwell join them, so sound
// climbs and falls through the building the way it would.
// Built at start from LevelLayout, so it always matches the generated geometry.
public class RoomInfo
{
    public int id, layer, number;
    public string name, kind, acoustic, floor;
    public float z, ceil;
    public bool water;
    public LevelInfo level;
    public LevelLayout.Room def;
    public readonly List<int> portals = new List<int>();
}

public class PortalInfo
{
    public int a, b, index;
    public Vector3 pos;
    public float width, height;
    public string kind, doorName;
    public HingeDoor door;
    public System.Func<float> openFunc;

    public float Openness => door ? door.Openness : (openFunc != null ? openFunc() : 1f);
}

public class LevelInfo
{
    public int index;
    public string key, name, label;
    public readonly List<RoomInfo> rooms = new List<RoomInfo>();
    public readonly List<PortalInfo> portals = new List<PortalInfo>();
}

public class LevelGraph : MonoBehaviour
{
    public static LevelGraph Instance { get; private set; }
    public readonly List<LevelInfo> levels = new List<LevelInfo>();
    readonly Dictionary<LevelLayout.Room, RoomInfo> byDef = new Dictionary<LevelLayout.Room, RoomInfo>();

    void Awake()
    {
        Instance = this;
        Build();
    }

    void Build()
    {
        var hotel = new LevelInfo { index = 0, key = "hotel", name = "Hotel", label = "HOTEL" };
        var bed = new LevelInfo { index = 1, key = "bedroom", name = "Bedroom", label = "BEDROOM" };
        levels.Add(hotel);
        levels.Add(bed);
        foreach (var d in LevelLayout.Rooms)
        {
            var lv = d.floor == 2 ? bed : hotel;
            var r = new RoomInfo
            {
                id = lv.rooms.Count, name = d.name, kind = d.kind, def = d, level = lv, z = d.Y, ceil = d.ceil,
                acoustic = d.kind, floor = d.floorMat, layer = d.floor, water = d.floorMat == "Water_Floor"
            };
            lv.rooms.Add(r);
            byDef[d] = r;
        }
        foreach (var o in LevelLayout.Openings)
        {
            if (o.kind == LevelLayout.Gap.Static || o.kind == LevelLayout.Gap.Lift || o.kind == LevelLayout.Gap.Window) continue;
            float y = LevelLayout.FloorY(o.floor);
            var m = o.Mid;
            Vector2 n = o.AlongX ? new Vector2(0, 1) : new Vector2(1, 0);
            var pa = new Vector3(m.x - n.x * 0.35f, y + 1.2f, m.y - n.y * 0.35f);
            var pb = new Vector3(m.x + n.x * 0.35f, y + 1.2f, m.y + n.y * 0.35f);
            if (o.floor == 2) { pa += LevelLayout.BedroomOrigin; pb += LevelLayout.BedroomOrigin; }
            var ra = RoomAt(pa);
            var rb = RoomAt(pb);
            if (ra == null || rb == null || ra == rb) continue;
            var lv = ra.level;
            var p = new PortalInfo
            {
                a = ra.id, b = rb.id, index = lv.portals.Count,
                pos = new Vector3(m.x, y + 1.05f, m.y) + (o.floor == 2 ? LevelLayout.BedroomOrigin : Vector3.zero),
                width = o.Width, height = o.kind == LevelLayout.Gap.Railing ? 3f : o.kind == LevelLayout.Gap.Opening ? 2.6f : o.height,
                kind = o.kind.ToString().ToLowerInvariant(), doorName = o.name
            };
            lv.portals.Add(p);
            ra.portals.Add(p.index);
            rb.portals.Add(p.index);
        }
        foreach (var v in LevelLayout.VerticalPortals)
        {
            var ra = Room(v.a); var rb = Room(v.b);
            if (ra == null || rb == null) continue;
            var p = new PortalInfo { a = ra.id, b = rb.id, index = hotel.portals.Count, pos = v.pos, width = v.width, height = 3f, kind = "stairwell", doorName = null };
            hotel.portals.Add(p);
            ra.portals.Add(p.index);
            rb.portals.Add(p.index);
        }
    }

    public RoomInfo RoomAt(Vector3 pos)
    {
        var d = LevelLayout.RoomAt(pos);
        return d != null && byDef.TryGetValue(d, out var r) ? r : null;
    }

    public RoomInfo Room(string name)
    {
        foreach (var lv in levels) foreach (var r in lv.rooms) if (r.name == name) return r;
        return null;
    }
}
