using System.Collections.Generic;
using UnityEngine;

public class RoomInfo
{
    public int id, layer, number;
    public string name, kind, acoustic, floor;
    public float z, ceil;
    public bool water;
    public LevelInfo level;
    public readonly List<int> portals = new List<int>();
}

public class PortalInfo
{
    public int a, b, index;
    public Vector3 pos;
    public float width, height;
    public string kind, doorName;
    public HingeDoor door;
    public System.Func<float> openFunc;      // e.g. elevator landing doors

    // 1 = fully open opening, 0 when a closed door blocks it
    public float Openness => door ? door.Openness : (openFunc != null ? openFunc() : 1f);
}

public class LayerInfo
{
    public float z;
    public int w, h;
    public int[] cells;
}

public class LevelInfo
{
    public int index;
    public string key, name, label;
    public float ox;
    public readonly List<LayerInfo> layers = new List<LayerInfo>();
    public readonly List<RoomInfo> rooms = new List<RoomInfo>();
    public readonly List<PortalInfo> portals = new List<PortalInfo>();
    public Dictionary<string, object> raw;
    public GameObject root;
    public int W => layers.Count > 0 ? layers[0].w : 0;
    public int H => layers.Count > 0 ? layers[0].h : 0;
}

// Owns the map layout exported from Blender (rooms, portals, devices...) and the active level.
public class HotelWorld : MonoBehaviour
{
    public static HotelWorld Instance { get; private set; }

    public TextAsset layoutJson;
    // Blender -> Unity axis signs measured by the builder when the FBX was imported
    public float axisX = -1f, axisZ = -1f;
    public GameObject[] levelRoots;
    public int startLevel;

    public readonly List<LevelInfo> levels = new List<LevelInfo>();
    public LevelInfo Current { get; private set; }
    public event System.Action<LevelInfo> LevelChanged;

    void Awake()
    {
        Instance = this;
        Load();
        SetLevel(startLevel, true);
    }

    public void Load()
    {
        levels.Clear();
        if (!layoutJson) return;
        var root = (Dictionary<string, object>)MiniJson.Parse(layoutJson.text);
        int li = 0;
        foreach (Dictionary<string, object> L in root.L("levels"))
        {
            var lv = new LevelInfo { index = li, key = L.S("key"), name = L.S("name"), label = L.S("label"), raw = L };
            lv.ox = (float)(double)L.L("origin")[0];
            foreach (Dictionary<string, object> la in L.L("layers"))
            {
                var info = new LayerInfo { z = la.F("z"), w = la.I("w"), h = la.I("h") };
                info.cells = new int[info.w * info.h];
                var rows = la.L("grid");
                for (int j = 0; j < info.h; j++)
                {
                    var row = (List<object>)rows[j];
                    for (int i = 0; i < info.w; i++) info.cells[j * info.w + i] = (int)(double)row[i];
                }
                lv.layers.Add(info);
            }
            foreach (Dictionary<string, object> r in L.L("rooms"))
            {
                lv.rooms.Add(new RoomInfo
                {
                    id = r.I("id"), layer = r.I("layer"), number = r.I("number", 0), name = r.S("name"), kind = r.S("kind"),
                    acoustic = r.S("acoustic"), floor = r.S("floor"), z = r.F("z"), ceil = r.F("ceil"), water = r.B("water"), level = lv
                });
            }
            int pi = 0;
            foreach (Dictionary<string, object> p in L.L("portals"))
            {
                var pt = new PortalInfo
                {
                    a = p.I("a"), b = p.I("b"), index = pi++, pos = ToUnity(p.L("p")), width = p.F("w", 1f), height = p.F("h", 2f),
                    kind = p.S("kind"), doorName = p.S("door")
                };
                lv.portals.Add(pt);
                lv.rooms[pt.a].portals.Add(pt.index);
                lv.rooms[pt.b].portals.Add(pt.index);
            }
            if (levelRoots != null && li < levelRoots.Length) lv.root = levelRoots[li];
            levels.Add(lv);
            li++;
        }
    }

    // ------------------------------------------------------------ coordinates
    public Vector3 ToUnity(float bx, float by, float bz) => new Vector3(axisX * bx, bz, axisZ * by);

    public Vector3 ToUnity(List<object> p) => ToUnity((float)(double)p[0], (float)(double)p[1], (float)(double)p[2]);

    public Vector3 DirToUnity(List<object> d) => new Vector3(axisX * (float)(double)d[0], d.Count > 2 ? (float)(double)d[2] : 0f, axisZ * (float)(double)d[1]);

    public Vector3 DirToUnity(float bx, float by) => new Vector3(axisX * bx, 0f, axisZ * by);

    public Vector3 ToBlender(Vector3 u) => new Vector3(u.x * axisX, u.z * axisZ, u.y);

    // Blender yaw (radians, CCW from +X) of a direction -> Unity yaw (degrees)
    public float YawToUnity(float blenderYaw) => YawStatic(blenderYaw, axisX, axisZ);

    public static float YawStatic(float blenderYaw, float ax, float az)
    {
        float dx = ax * Mathf.Cos(blenderYaw), dz = az * Mathf.Sin(blenderYaw);
        return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------ queries
    public LevelInfo LevelAt(Vector3 unityPos)
    {
        var b = ToBlender(unityPos);
        foreach (var lv in levels)
            if (b.x >= lv.ox - 2f && b.x <= lv.ox + lv.W + 2f) return lv;
        return null;
    }

    public RoomInfo RoomAt(Vector3 unityPos)
    {
        var lv = LevelAt(unityPos);
        if (lv == null) return null;
        var b = ToBlender(unityPos);
        int i = Mathf.FloorToInt(b.x - lv.ox), j = Mathf.FloorToInt(b.y);
        if (i < 0 || j < 0 || i >= lv.W || j >= lv.H) return null;
        for (int k = lv.layers.Count - 1; k >= 0; k--)
        {
            var L = lv.layers[k];
            if (b.z < L.z - 0.9f) continue;
            int v = L.cells[j * L.w + i];
            if (v >= 0)
            {
                var r = lv.rooms[v];
                if (b.z < L.z + r.ceil + 0.6f) return r;
                continue;
            }
            if (v == -2) continue;    // void: look further down
        }
        // fall back to the room of any layer below whose column contains the point
        for (int k = lv.layers.Count - 1; k >= 0; k--)
        {
            int v = lv.layers[k].cells[j * lv.layers[k].w + i];
            if (v >= 0 && b.z >= lv.layers[k].z - 0.9f) return lv.rooms[v];
        }
        return null;
    }

    public void SetLevel(int index, bool force = false)
    {
        if (index < 0 || index >= levels.Count) return;
        if (!force && Current != null && Current.index == index) return;
        Current = levels[index];
        if (levelRoots != null)
            for (int i = 0; i < levelRoots.Length; i++)
                if (levelRoots[i]) levelRoots[i].SetActive(i == index);
        LevelChanged?.Invoke(Current);
    }

    public LevelInfo Level(string key) => levels.Find(l => l.key == key);
}
