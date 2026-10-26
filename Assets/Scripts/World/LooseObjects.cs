using System.Collections.Generic;
using UnityEngine;

// Creates the small things left behind on table tops (from the generator's "surfaces" list): bottles and glasses on
// bars and dining tables, cups and memos on desks, notes and books on bedside tables. Each object is only placed
// where it actually fits on the surface (no overlap with whatever already stands there).
public class LooseObjects : MonoBehaviour
{
    public UnityEngine.Material glass, ceramic, tin, paper, book;
    public float density = 0.55f;
    public static int Spawned { get; private set; }

    void Start()
    {
        var world = HotelWorld.Instance;
        if (!world) return;
        world.LevelChanged += l => Fill(l);
        if (world.Current != null) Fill(world.Current);
    }

    void Fill(LevelInfo level)
    {
        if (level == null || !level.root) return;
        var spots = level.root.GetComponent<SurfaceSpots>();
        if (!spots || spots.populated || spots.pos == null) return;
        spots.populated = true;
        Physics.SyncTransforms();
        var list = new List<(Vector3, float, float, float, string, string)>();
        for (int i = 0; i < spots.pos.Length; i++) list.Add((spots.pos[i], spots.yaw[i], spots.w[i], spots.d[i], spots.kind[i], spots.roomKind[i]));
        Populate(level.root.transform, list, level.index * 7919 + 17);
    }

    public void Populate(Transform levelRoot, List<(Vector3 pos, float yaw, float w, float d, string kind, string roomKind)> surfaces, int seed)
    {
        var rng = new System.Random(seed);
        int mask = (1 << MaterialAcoustics.StructureLayer) | (1 << MaterialAcoustics.PropLayer) | (1 << MaterialAcoustics.DoorLayer) | (1 << InteractLayer);
        foreach (var s in surfaces)
        {
            if (rng.NextDouble() > density) continue;
            int n = s.kind == "bar_counter" || s.kind == "dining_table" || s.kind == "conference_table" ? 1 + rng.Next(3) : 1;
            var rot = Quaternion.Euler(0f, s.yaw, 0f);
            for (int k = 0; k < n; k++)
            {
                string what = Pick(s.kind, s.roomKind, rng);
                Vector3 off = new Vector3((float)(rng.NextDouble() - 0.5) * s.w, 0f, (float)(rng.NextDouble() - 0.5) * s.d);
                Vector3 p = s.pos + rot * off;
                // land it on whatever is actually there (cloth, table top), and only where nothing else stands
                if (!Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out var hit, 0.7f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > 10f || hit.point.y < s.pos.y - 0.06f) continue;
                Vector3 at = hit.point + Vector3.up * 0.005f;
                if (Physics.CheckBox(at + Vector3.up * 0.13f, new Vector3(0.06f, 0.11f, 0.06f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore)) continue;
                var go = Make(what, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), levelRoot);
                if (go) Spawned++;
            }
        }
    }

    public const int InteractLayer = 14;

    static string Pick(string kind, string roomKind, System.Random rng)
    {
        double r = rng.NextDouble();
        switch (kind)
        {
            case "bar_counter": return r < 0.6 ? "bottle" : "glass";
            case "dining_table": return r < 0.4 ? "glass" : r < 0.75 ? "cup" : "bottle";
            case "desk":
            case "reception_desk": return r < 0.45 ? "memo" : r < 0.75 ? "cup" : "book";
            case "nightstand":
            case "side_table": return r < 0.5 ? "note" : r < 0.75 ? "glass" : "book";
            case "prep_table": return r < 0.5 ? "tin" : "cup";
            default: return r < 0.3 ? "cup" : r < 0.55 ? "bottle" : r < 0.8 ? "tin" : "note";
        }
    }

    GameObject Make(string what, Vector3 at, Quaternion rot, Transform parent)
    {
        var root = new GameObject("Loose_" + what);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(at, rot);
        root.layer = InteractLayer;
        if (what == "memo" || what == "note")
        {
            var sheet = Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.002f, 0f), new Vector3(0.21f, 0.003f, 0.28f), paper);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.02f, 0f);
            box.size = new Vector3(0.24f, 0.05f, 0.3f);
            box.isTrigger = true;
            var rd = root.AddComponent<Readable>();
            rd.kind = what;
            return root;
        }
        var body = root.AddComponent<Rigidbody>();
        var c = root.AddComponent<Carryable>();
        switch (what)
        {
            case "bottle":
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.11f, 0f), new Vector3(0.07f, 0.11f, 0.07f), glass);
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.26f, 0f), new Vector3(0.026f, 0.045f, 0.026f), glass);
                Collider(root, new Vector3(0f, 0.15f, 0f), 0.036f, 0.3f);
                body.mass = 0.6f; c.material = Carryable.Material.Glass;
                break;
            case "glass":
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.065f, 0.05f, 0.065f), glass);
                Collider(root, new Vector3(0f, 0.05f, 0f), 0.034f, 0.1f);
                body.mass = 0.2f; c.material = Carryable.Material.Glass;
                break;
            case "cup":
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.045f, 0f), new Vector3(0.08f, 0.045f, 0.08f), ceramic);
                Collider(root, new Vector3(0f, 0.045f, 0f), 0.042f, 0.09f);
                body.mass = 0.25f; c.material = Carryable.Material.Ceramic;
                break;
            case "tin":
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.06f, 0f), new Vector3(0.075f, 0.06f, 0.075f), tin);
                Collider(root, new Vector3(0f, 0.06f, 0f), 0.04f, 0.12f);
                body.mass = 0.35f; c.material = Carryable.Material.Tin;
                break;
            default:   // book
                Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.15f, 0.035f, 0.22f), book);
                var bc = root.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.02f, 0f);
                bc.size = new Vector3(0.15f, 0.035f, 0.22f);
                body.mass = 0.5f; c.material = Carryable.Material.Book;
                break;
        }
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.Sleep();
        return root;
    }

    static void Collider(GameObject root, Vector3 centre, float radius, float height)
    {
        var cc = root.AddComponent<CapsuleCollider>();
        cc.center = centre;
        cc.radius = radius;
        cc.height = Mathf.Max(height, radius * 2f);
    }

    static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, UnityEngine.Material m)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<UnityEngine.Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.layer = InteractLayer;
        var r = g.GetComponent<Renderer>();
        if (m) r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return g;
    }
}
