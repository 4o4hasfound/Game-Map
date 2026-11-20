using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class TapeBuilder
{
    static Transform levelRoot, propRoot, lightRoot;
    // every stretch of wall that reaches the floor, for baseboards, grime and stains (TapeBuilder.Decay.cs)
    struct WallRun { public Vector3 a, b, inward; public float floorY, ceilY; public LevelLayout.Room room; public int floor; }
    static readonly List<WallRun> wallRuns = new List<WallRun>();
    static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
    const int LStructure = 10, LDoor = 11, LProp = 12, LCeiling = 13, LInteract = 14;

    static void BuildScene()
    {
        meshCache.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.085f, 0.083f, 0.09f);
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.006f, 0.006f, 0.007f);
        RenderSettings.fogDensity = 0.045f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        levelRoot = new GameObject("Level").transform;
        propRoot = new GameObject("Props").transform;
        lightRoot = new GameObject("Lights").transform;

        BuildShell();
        BuildOpenings(out var doors);
        BuildStairs();
        var refs = new Refs();
        BuildProps(refs, doors);
        DecayPass();
        BuildCameras(refs);
        BuildPlayerAndGame(refs, doors);
        IsolateLights();
        AuditPlacement();             // report what pokes into doorways, floats, sinks or overlaps (fixes go in the generators)
        SplitIntoStages();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("SCENE BUILT: " + ScenePath);
    }

    class Refs
    {
        public SecurityVcr vcr;
        public MonitorScreen bedroomTv;
        public GameObject bedroomParcel;
        public Lamp bedroomHall, bedroomLamp;
        public readonly List<Lamp> houseLamps = new List<Lamp>();
        public readonly List<Light> tapeLights = new List<Light>();
        public readonly List<(MonitorScreen screen, string cam)> screens = new List<(MonitorScreen, string)>();
        public Dresser dresser;
        public KeyItem key;
        public Breaker breaker;
        public readonly List<Lamp> basementLamps = new List<Lamp>();
        public readonly List<MonitorScreen> recorderScreens = new List<MonitorScreen>();
        public LiftPanel liftPanel;
        public Safe safe;
    }

    // ------------------------------------------------------------------ meshes
    // a box with UVs in metres (world-aligned for structure so textures run on across pieces)
    static Mesh BoxMesh(Vector3 size, Vector3 worldOffset, bool worldUv)
    {
        string key = worldUv ? null : $"{size.x:0.00}_{size.y:0.00}_{size.z:0.00}";
        if (key != null && meshCache.TryGetValue(key, out var cached)) return cached;
        var h = size * 0.5f;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Face(Vector3 normal, Vector3 u, Vector3 w)
        {
            int b = v.Count;
            Vector3 c = Vector3.Scale(normal, h);
            Vector3 du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
            Vector3[] q = { c - du - dw, c + du - dw, c + du + dw, c - du + dw };
            foreach (var p in q)
            {
                v.Add(p); n.Add(normal);
                var wp = p + (worldUv ? worldOffset : Vector3.zero);
                Vector2 t = Mathf.Abs(normal.x) > 0.5f ? new Vector2(wp.z, wp.y) : Mathf.Abs(normal.y) > 0.5f ? new Vector2(wp.x, wp.z) : new Vector2(wp.x, wp.y);
                uv.Add(t);
            }
            tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        Face(Vector3.right, Vector3.forward, Vector3.up);
        Face(Vector3.left, Vector3.back, Vector3.up);
        Face(Vector3.up, Vector3.right, Vector3.forward);
        Face(Vector3.down, Vector3.left, Vector3.forward);
        Face(Vector3.forward, Vector3.left, Vector3.up);
        Face(Vector3.back, Vector3.right, Vector3.up);
        var m = new Mesh { name = "Box" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        FixWinding(m);
        m.RecalculateBounds();
        m.RecalculateTangents();
        if (key != null) meshCache[key] = m;
        return m;
    }

    static void FixWinding(Mesh m)
    {
        var v = m.vertices; var n = m.normals; var t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            var c = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (Vector3.Dot(c, n[t[i]]) < 0) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
        }
        m.triangles = t;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, int layer, bool collider = true, Quaternion? rot = null, bool worldUv = false, bool isStatic = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.rotation = rot ?? Quaternion.identity;
        go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, center, worldUv && rot == null);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = layer == LStructure || layer == LCeiling ? ShadowCastingMode.TwoSided : ShadowCastingMode.On;
        if (collider) go.AddComponent<BoxCollider>().size = size;
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    static GameObject Quad(string name, Transform parent, Vector3 center, Vector2 size, Quaternion rot, Material mat, int layer = LProp)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.rotation = rot;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.layer = layer;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static float Base(int floor) => floor == 2 ? 0f : LevelLayout.FloorY(floor);
    static float WallTop(int floor, LevelLayout.Room room) =>
        floor == -1 ? 0f : floor == 0 ? LevelLayout.GroundWall : floor == 1 ? LevelLayout.Roof :
        floor == 3 ? LevelLayout.RoofTop :
        floor == 4 ? (room.twoStorey ? room.Y + room.ceil : LevelLayout.RoofTop + room.ceil) :     // outdoor rooms: room.ceil is the parapet
        room.ceil;
    static Vector3 Org(int floor) => floor == 2 ? LevelLayout.BedroomOrigin : Vector3.zero;

    // a rectangle minus a list of rectangles (axis aligned): the pieces left over
    static List<Rect> Subtract(Rect r, IEnumerable<Rect> holes)
    {
        var pieces = new List<Rect> { r };
        foreach (var h in holes)
        {
            var next = new List<Rect>();
            foreach (var p in pieces)
            {
                if (!p.Overlaps(h)) { next.Add(p); continue; }
                float x0 = Mathf.Max(p.xMin, h.xMin), x1 = Mathf.Min(p.xMax, h.xMax), z0 = Mathf.Max(p.yMin, h.yMin), z1 = Mathf.Min(p.yMax, h.yMax);
                if (p.yMin < z0) next.Add(Rect.MinMaxRect(p.xMin, p.yMin, p.xMax, z0));
                if (z1 < p.yMax) next.Add(Rect.MinMaxRect(p.xMin, z1, p.xMax, p.yMax));
                if (p.xMin < x0) next.Add(Rect.MinMaxRect(p.xMin, z0, x0, z1));
                if (x1 < p.xMax) next.Add(Rect.MinMaxRect(x1, z0, p.xMax, z1));
            }
            pieces = next;
        }
        return pieces.Where(p => p.width > 0.01f && p.height > 0.01f).ToList();
    }

    // where a collapsed room's ceiling has fallen in
    public static Rect CeilingHole(LevelLayout.Room room)
    {
        var c = room.r.center + new Vector2(room.r.width * 0.18f, -room.r.height * 0.12f);
        float hw = Mathf.Min(0.8f, room.r.width * 0.5f - 0.2f), hh = Mathf.Min(0.8f, room.r.height * 0.5f - 0.2f);
        c.x = Mathf.Clamp(c.x, room.r.xMin + hw + 0.1f, room.r.xMax - hw - 0.1f);
        c.y = Mathf.Clamp(c.y, room.r.yMin + hh + 0.1f, room.r.yMax - hh - 0.1f);
        return new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
    }

    // ------------------------------------------------------------------ walls, floors, ceilings
    // Each room lines its own edges with a half-thickness "skin" in its own wall material, so shared walls show the
    // right finish on each side. Openings cut gaps through the skins of both rooms.
    static void BuildShell()
    {
        var shell = new GameObject("Shell").transform;
        shell.SetParent(levelRoot, false);
        wallRuns.Clear();
        const float half = 0.075f;
        foreach (var room in LevelLayout.Rooms)
        {
            var floors = room.twoStorey ? new[] { room.floor, room.floor + 1 } : new[] { room.floor };
            foreach (int f in floors)
            {
                var r = room.r;
                var edges = new (bool alongX, float line, float a, float b, float inward)[]
                {
                    (true, r.yMin, r.xMin, r.xMax, +1), (true, r.yMax, r.xMin, r.xMax, -1),
                    (false, r.xMin, r.yMin, r.yMax, +1), (false, r.xMax, r.yMin, r.yMax, -1),
                };
                foreach (var e in edges)
                {
                    float c = e.line + e.inward * half * 0.5f;
                    foreach (var (lo, hi, ylo, yhi) in SkinSegments(room, f, e.alongX, e.line, e.a, e.b, e.inward))
                    {
                        if (hi - lo < 0.01f || yhi - ylo < 0.01f) continue;
                        var center = e.alongX ? new Vector3((lo + hi) / 2, (ylo + yhi) / 2, c) : new Vector3(c, (ylo + yhi) / 2, (lo + hi) / 2);
                        var size = e.alongX ? new Vector3(hi - lo, yhi - ylo, half) : new Vector3(half, yhi - ylo, hi - lo);
                        Box($"Wall {room.name}", shell, center + Org(f), size, M(room.wallMat), LStructure, true, null, true);
                        if (Mathf.Abs(ylo - Base(f)) < 0.01f && hi - lo > 0.3f && (f == room.floor || room.twoStorey) && room.kind != "outdoor")
                        {
                            float face = e.line + e.inward * half;
                            var a = e.alongX ? new Vector3(lo, ylo, face) : new Vector3(face, ylo, lo);
                            var b = e.alongX ? new Vector3(hi, ylo, face) : new Vector3(face, ylo, hi);
                            var inward = e.alongX ? new Vector3(0, 0, e.inward) : new Vector3(e.inward, 0, 0);
                            float ceil = room.twoStorey ? (f == room.floor ? LevelLayout.FloorY(room.floor + 1) - 0.2f : room.Y + room.ceil) : (room.floor == 2 ? room.ceil : room.Y + room.ceil);
                            wallRuns.Add(new WallRun { a = a + Org(f), b = b + Org(f), inward = inward, floorY = ylo, ceilY = ceil, room = room, floor = f });
                        }
                    }
                }
            }
            // floor
            var fr = room.r;
            float fy = room.floor == 2 ? 0f : room.Y;
            if (!room.noFloor)
                Box($"Floor {room.name}", shell, new Vector3(fr.center.x, fy - 0.1f, fr.center.y) + Org(room.floor), new Vector3(fr.width, 0.2f, fr.height), M(room.floorMat), LStructure, true, null, true);
            // ceiling
            if (room.kind == "lobby" && room.twoStorey)
            {
                foreach (var v in LevelLayout.Void)
                    Box("Ceiling Atrium", shell, new Vector3(v.center.x, LevelLayout.Roof + 0.05f, v.center.y), new Vector3(v.width, 0.1f, v.height), M("Ceiling_Plaster"), LCeiling, true, null, true);
                continue;
            }
            if (room.kind == "outdoor") continue;                         // the roof: open to the storm
            float cy = room.floor == 2 ? room.ceil : room.Y + room.ceil;
            var holes = new List<Rect>();
            // open stairwells above (the basement stair under the stairhead)
            foreach (var above in LevelLayout.Rooms) if (above.noFloor && above.floor == room.floor + 1 && above.r.Overlaps(room.r)) holes.Add(above.r);
            if (room.decay == "collapsed" && !room.twoStorey) holes.Add(CeilingHole(room));
            foreach (var piece in Subtract(fr, holes))
                Box($"Ceiling {room.name}", shell, new Vector3(piece.center.x, cy + 0.05f, piece.center.y) + Org(room.floor), new Vector3(piece.width, 0.1f, piece.height), M(room.ceilingMat()), LCeiling, true, null, true);
            if (room.decay == "collapsed" && !room.twoStorey)
            {
                var h = CeilingHole(room);
                var marker = new GameObject("Ceiling hole");
                marker.transform.SetParent(shell, false);
                marker.transform.position = new Vector3(h.center.x, cy - 0.1f, h.center.y);
                // the void above the hole: a dark plug so you don't look into the floor slab's underside of nothing. It stops
                // under the floor above, if there is one (it used to stand 0.85 m up through the ballroom floor)
                float top = cy + 0.85f;
                foreach (var above in LevelLayout.Rooms)
                    if (above.floor == room.floor + 1 && !above.noFloor && above.r.Overlaps(h)) top = Mathf.Min(top, LevelLayout.FloorY(room.floor + 1) - 0.205f);
                if (top - (cy + 0.05f) > 0.02f)
                    Box("Ceiling void", shell, new Vector3(h.center.x, (cy + 0.05f + top) * 0.5f, h.center.y), new Vector3(h.width, top - cy - 0.05f, h.height), M("Soot_Floor"), LCeiling, false, null, true);
            }
        }
        // extra slabs (stair landings, the walkway past the basement stairwell)
        foreach (var (r, y, mat) in LevelLayout.Slabs)
            Box("Landing", shell, new Vector3(r.center.x, y - 0.1f, r.center.y), new Vector3(r.width, 0.2f, r.height), M(mat), LStructure, true, null, true);
    }

    // skin pieces for one room edge: (along lo, along hi, y lo, y hi), cut by openings, only where this room is inside
    static List<(float, float, float, float)> SkinSegments(LevelLayout.Room room, int f, bool alongX, float line, float a, float b, float inward)
    {
        float y0 = Base(f);
        float y1 = WallTop(f, room);
        // where along the edge is the room really on the inside (two-storey rooms on the upper floor)?
        var spans = new List<(float, float)>();
        float step = 0.25f;
        float start = -1;
        for (float s = a; s < b - 1e-4f; s += step)
        {
            float m = Mathf.Min(s + step * 0.5f, b - 0.01f);
            var p = alongX ? new Vector3(m, y0 + 1.2f, line + inward * 0.3f) : new Vector3(line + inward * 0.3f, y0 + 1.2f, m);
            if (f == 2) p += LevelLayout.BedroomOrigin;
            bool mine = LevelLayout.RoomAt(p) == room;
            if (mine && start < 0) start = s;
            if (!mine && start >= 0) { spans.Add((start, s)); start = -1; }
        }
        if (start >= 0) spans.Add((start, b));
        var cuts = new List<LevelLayout.Opening>();
        foreach (var o in LevelLayout.Openings)
        {
            if (o.floor != f || o.AlongX != alongX) continue;
            float lineO = alongX ? o.a.y : o.a.x;
            if (Mathf.Abs(lineO - line) > 0.01f) continue;
            cuts.Add(o);
        }
        var result = new List<(float, float, float, float)>();
        foreach (var (s0, s1) in spans)
        {
            var parts = new List<(float lo, float hi)> { (s0, s1) };
            foreach (var o in cuts)
            {
                float oa = alongX ? Mathf.Min(o.a.x, o.b.x) : Mathf.Min(o.a.y, o.b.y);
                float ob = alongX ? Mathf.Max(o.a.x, o.b.x) : Mathf.Max(o.a.y, o.b.y);
                var next = new List<(float, float)>();
                foreach (var (lo, hi) in parts)
                {
                    if (ob <= lo || oa >= hi) { next.Add((lo, hi)); continue; }
                    if (oa > lo) next.Add((lo, oa));
                    if (ob < hi) next.Add((ob, hi));
                    bool header = o.kind != LevelLayout.Gap.Railing;
                    float top = o.kind == LevelLayout.Gap.Opening ? (f == 0 ? 3.0f : f == -1 ? 2.6f : 2.5f) : o.kind == LevelLayout.Gap.Window ? 2.2f : o.height;
                    if (header && y0 + top < y1) result.Add((Mathf.Max(lo, oa), Mathf.Min(hi, ob), y0 + top, y1));
                    if (o.kind == LevelLayout.Gap.Window) result.Add((Mathf.Max(lo, oa), Mathf.Min(hi, ob), y0, y0 + 0.9f));
                }
                parts = next;
            }
            foreach (var (lo, hi) in parts) result.Add((lo, hi, y0, y1));
        }
        return result;
    }

    // ------------------------------------------------------------------ doors, railings, special openings
    static void BuildOpenings(out List<HingeDoor> doors)
    {
        doors = new List<HingeDoor>();
        var root = new GameObject("Openings").transform;
        root.SetParent(levelRoot, false);
        foreach (var o in LevelLayout.Openings)
        {
            float y = Base(o.floor);
            var org = Org(o.floor);
            Vector3 A = new Vector3(o.a.x, y, o.a.y) + org, B = new Vector3(o.b.x, y, o.b.y) + org;
            Vector3 dir = (B - A).normalized;
            Vector3 nrm = o.AlongX ? Vector3.forward : Vector3.right;
            float len = Vector3.Distance(A, B);
            var mid = (A + B) / 2;
            switch (o.kind)
            {
                case LevelLayout.Gap.Door:
                case LevelLayout.Gap.Locked:
                    doors.Add(MakeDoor(root, o, A, B, dir, nrm, y));
                    break;
                case LevelLayout.Gap.Jammed:
                {
                    // a door stuck ajar in a warped frame: you can see through the gap, not get through
                    var pivot = new GameObject("Jammed " + o.name).transform;
                    pivot.SetParent(root, false);
                    pivot.position = A;
                    pivot.rotation = Quaternion.FromToRotation(Vector3.right, dir) * Quaternion.Euler(0, 9f, 0);
                    var panel = Box("Panel", pivot, Vector3.zero, new Vector3(len - 0.03f, 2.07f, 0.045f), M("Soot_Floor"), LStructure, true, null, false);
                    panel.transform.localPosition = new Vector3(len / 2f, 1.04f, 0f);
                    panel.transform.localRotation = Quaternion.identity;
                    Box("Rubble behind", root, mid - nrm * 0.0f + Vector3.up * 0.3f, new Vector3(o.AlongX ? len : 0.5f, 0.6f, o.AlongX ? 0.5f : len), M("Rubble_Floor"), LProp, true);
                    break;
                }
                case LevelLayout.Gap.Railing:
                {
                    var size = o.AlongX ? new Vector3(len, 1.0f, 0.06f) : new Vector3(0.06f, 1.0f, len);
                    Box($"Railing {o.name}", root, mid + Vector3.up * 0.5f, size, M("Wood_Furniture"), LProp, true);
                    var top = o.AlongX ? new Vector3(len, 0.06f, 0.14f) : new Vector3(0.14f, 0.06f, len);
                    Box($"Rail {o.name}", root, mid + Vector3.up * 1.03f, top, M("Brass_Fitting"), LProp, false);
                    // balusters
                    int nb = Mathf.FloorToInt(len / 0.5f);
                    for (int i = 0; i <= nb; i++)
                    {
                        var p = A + dir * (i * len / Mathf.Max(1, nb));
                        Box("Baluster", root, p + Vector3.up * 0.5f, new Vector3(0.05f, 1f, 0.05f), M("Wood_Furniture"), LProp, false);
                    }
                    break;
                }
                case LevelLayout.Gap.Static:
                {
                    var c = mid + Vector3.up * o.height / 2;
                    var outward = o.AlongX ? (o.a.y < 10f ? Vector3.back : Vector3.forward) : (o.a.x < 30f ? Vector3.left : Vector3.right);
                    Quad("Street Static", root, c + outward * 0.06f, new Vector2(len, o.height), Quaternion.LookRotation(outward), M("Static"));
                    var size = o.AlongX ? new Vector3(len, o.height, 0.1f) : new Vector3(0.1f, o.height, len);
                    Box("Static barrier", root, c, size, M("Glass_Shards"), LStructure, true).GetComponent<MeshRenderer>().enabled = false;
                    Box("Frame", root, c, new Vector3(o.AlongX ? 0.08f : 0.1f, o.height, o.AlongX ? 0.1f : 0.08f), M("Brass_Fitting"), LProp, false);
                    break;
                }
                case LevelLayout.Gap.Lift:
                {
                    var c = mid + Vector3.up * 1.05f;
                    var size = o.AlongX ? new Vector3(len, 2.1f, 0.08f) : new Vector3(0.08f, 2.1f, len);
                    Box($"Closed Doors {o.name}", root, c, size, M(o.floor == 2 ? "Wood_Door" : "Metal_Lift"), LStructure, true);
                    if (o.floor != 2)
                    {
                        // the lift doors are prised a hand's width apart: darkness behind
                        Box("Gap", root, c + nrm * 0.0f, new Vector3(o.AlongX ? 0.05f : 0.09f, 2.05f, o.AlongX ? 0.09f : 0.05f), M("Soot_Floor"), LProp, false);
                        var inward = o.AlongX ? (o.a.y > 30f ? Vector3.back : Vector3.forward) : Vector3.left;
                        var sign = Box("Out of order sign", propRoot, c + inward * 0.06f + Vector3.up * 0.3f, new Vector3(o.AlongX ? 0.3f : 0.01f, 0.2f, o.AlongX ? 0.01f : 0.3f), M("Paper_Note"), LInteract, true, null, false, false);
                        sign.GetComponent<BoxCollider>().isTrigger = true;
                        var rd = sign.AddComponent<Readable>();
                        rd.title = "TAPED TO THE LIFT"; rd.text = "OUT OF ORDER\n\nDO NOT USE\n\nSomething is in the shaft.\n— Night Manager, 31.10.2003"; rd.handwritten = true;
                        Box("Lift indicator", root, c + inward * 0.05f + Vector3.up * 1.25f, new Vector3(o.AlongX ? 0.5f : 0.02f, 0.12f, o.AlongX ? 0.02f : 0.5f), M("Brass_Fitting"), LProp, false);
                    }
                    break;
                }
                case LevelLayout.Gap.Window:
                {
                    var c = mid + Vector3.up * 1.55f;
                    var outward = o.floor == 2 ? Vector3.forward : o.AlongX ? (o.a.y < 10f ? Vector3.back : Vector3.forward) : (o.a.x < 30f ? Vector3.left : Vector3.right);
                    var inward = -outward;
                    if (o.floor == 2)
                    {
                        // your bedroom: rain on dark glass (the glass lets the lightning through)
                        var w = Box("Window", root, c, o.AlongX ? new Vector3(len, 1.3f, 0.05f) : new Vector3(0.05f, 1.3f, len), M("Glass_Night"), LStructure, true);
                        w.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                    else
                    {
                        // boarded up from inside: a frame, planks with gaps between them, glass on the sill,
                        // and an invisible barrier so nobody climbs out
                        Box("Window barrier", root, c, o.AlongX ? new Vector3(len, 1.3f, 0.05f) : new Vector3(0.05f, 1.3f, len), M("Glass_Night"), LStructure, true).GetComponent<MeshRenderer>().enabled = false;
                        var fr = M("Wood_Furniture");
                        var along = dir;
                        Box("Window frame", root, c + Vector3.up * 0.67f + inward * 0.04f, o.AlongX ? new Vector3(len + 0.1f, 0.07f, 0.1f) : new Vector3(0.1f, 0.07f, len + 0.1f), fr, LProp, false);
                        Box("Window sill", root, c + Vector3.down * 0.66f + inward * 0.07f, o.AlongX ? new Vector3(len + 0.16f, 0.05f, 0.2f) : new Vector3(0.2f, 0.05f, len + 0.16f), fr, LProp, true);
                        Box("Window frame", root, A + Vector3.up * 1.55f + inward * 0.04f, new Vector3(o.AlongX ? 0.07f : 0.1f, 1.36f, o.AlongX ? 0.1f : 0.07f), fr, LProp, false);
                        Box("Window frame", root, B + Vector3.up * 1.55f + inward * 0.04f, new Vector3(o.AlongX ? 0.07f : 0.1f, 1.36f, o.AlongX ? 0.1f : 0.07f), fr, LProp, false);
                        int planks = 5;
                        for (int i = 0; i < planks; i++)
                        {
                            if (i == 3 && Rnd(0f, 1f) < 0.5f) continue;                  // one plank fell off
                            float yy = 0.95f + i * 0.27f + Rnd(-0.03f, 0.03f);
                            float pl = len + 0.3f - (i == 1 ? Rnd(0f, 0.5f) : 0f);
                            var rot = Quaternion.LookRotation(inward) * Quaternion.Euler(0, 0, Rnd(-9f, 9f));
                            var pc = new Vector3(mid.x, y, mid.z) + Vector3.up * yy + inward * (0.09f + i % 2 * 0.012f) + along * Rnd(-0.1f, 0.1f);
                            Box("Board", root, pc, new Vector3(pl, 0.17f, 0.022f), M("Plywood_Board"), LProp, false, rot, false);
                        }
                        Box("Glass_Shards", root, new Vector3(mid.x, y + 0.92f, mid.z) + inward * 0.07f, o.AlongX ? new Vector3(len * 0.8f, 0.01f, 0.14f) : new Vector3(0.14f, 0.01f, len * 0.8f), M("Glass_Shards"), LProp, false);
                    }
                    // the storm: a spot light outside, aimed in and down; off except during a flash (Lightning.cs)
                    var lg = new GameObject("Lightning light");
                    lg.transform.SetParent(root, false);
                    lg.transform.position = c + outward * 2.4f + Vector3.up * 1.4f;
                    lg.transform.rotation = Quaternion.LookRotation(inward + Vector3.down * 0.55f);
                    var li = lg.AddComponent<Light>();
                    li.type = LightType.Spot;
                    li.spotAngle = 80f;
                    li.innerSpotAngle = 40f;
                    li.range = 12f;
                    li.intensity = 14f;
                    li.color = new Color(0.72f, 0.8f, 1f);
                    li.shadows = LightShadows.Hard;
                    li.enabled = false;
                    var uad = lg.AddComponent<UniversalAdditionalLightData>();
                    break;
                }
            }
        }
    }

    static HingeDoor MakeDoor(Transform root, LevelLayout.Opening o, Vector3 A, Vector3 B, Vector3 dir, Vector3 nrm, float y)
    {
        float w = Vector3.Distance(A, B);
        // the door root lies along the wall; the pivot under it only swings (HingeDoor sets its local rotation to the
        // swing angle, so the wall orientation must not live on the pivot)
        var go = new GameObject("Door " + o.name);
        go.transform.SetParent(root, false);
        go.transform.position = A;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(go.transform, false);
        pivot.localRotation = Quaternion.identity;
        bool metal = o.name.Contains("Stair") || o.name.Contains("Security") || o.name.Contains("Cold") || o.name.Contains("Basement") || o.name.Contains("Recorder") || o.name.Contains("Pump") || o.name.Contains("Boiler") || o.name.Contains("Electrical") || o.name.Contains("Generator");
        string mat = o.floor == 2 ? "Door_Home" : o.kind == LevelLayout.Gap.Locked || metal ? "Paint_Office" : o.name.Contains("Swing") ? "Metal_Device" : "Wood_Door";
        var panel = Box("Panel", pivot, Vector3.zero, new Vector3(w - 0.03f, 2.07f, 0.045f), M(mat), LDoor, true, null, false, false);
        panel.transform.localPosition = new Vector3(w / 2f, 1.04f, 0f);
        panel.transform.localRotation = Quaternion.identity;
        var handle = Box("Handle", panel.transform, Vector3.zero, new Vector3(0.12f, 0.03f, 0.1f), M("Brass_Fitting"), LDoor, false, null, false, false).transform;
        handle.localPosition = new Vector3(w * 0.4f, -0.05f, 0f);
        handle.localRotation = Quaternion.identity;
        if (o.name.StartsWith("Door 2"))
        {
            // the room number plate
            var plate = Box("Number plate", panel.transform, Vector3.zero, new Vector3(0.14f, 0.06f, 0.06f), M("Brass_Fitting"), LDoor, false, null, false, false).transform;
            plate.localPosition = new Vector3(0f, 0.45f, 0f);
            plate.localRotation = Quaternion.identity;
        }
        var obstacle = panel.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(w, 2.07f, 0.4f);
        obstacle.carving = true;
        obstacle.enabled = false;
        var mid = (A + B) / 2;
        Box("Frame Top", go.transform, mid + Vector3.up * 2.12f, o.AlongX ? new Vector3(w + 0.12f, 0.08f, 0.2f) : new Vector3(0.2f, 0.08f, w + 0.12f), M(o.floor == 2 ? "Wood_Cherry" : "Wood_Furniture"), LProp, false, null, false);
        Box("Frame A", go.transform, A + Vector3.up * 1.06f, new Vector3(o.AlongX ? 0.06f : 0.2f, 2.12f, o.AlongX ? 0.2f : 0.06f), M(o.floor == 2 ? "Wood_Cherry" : "Wood_Furniture"), LProp, false, null, false);
        Box("Frame B", go.transform, B + Vector3.up * 1.06f, new Vector3(o.AlongX ? 0.06f : 0.2f, 2.12f, o.AlongX ? 0.2f : 0.06f), M(o.floor == 2 ? "Wood_Cherry" : "Wood_Furniture"), LProp, false, null, false);

        var d = go.AddComponent<HingeDoor>();
        d.portalName = o.name;
        d.leaf = pivot;
        d.locked = o.kind == LevelLayout.Gap.Locked;
        d.key = o.key;
        d.stuck = o.stuck;
        d.metal = metal;
        // open into the room (guest room / office / service) side
        var m2 = o.Mid;
        var pa = new Vector3(m2.x, y + 1.2f, m2.y) - nrm * 0.4f + Org(o.floor);
        var pb = new Vector3(m2.x, y + 1.2f, m2.y) + nrm * 0.4f + Org(o.floor);
        var ra = LevelLayout.RoomAt(pa); var rb = LevelLayout.RoomAt(pb);
        bool Inner(LevelLayout.Room r) => r != null && (r.kind == "room" || r.kind == "office" || r.kind == "service" || r.kind == "stair" || r.kind == "lair" || r.kind == "bedroom");
        bool intoB = Inner(rb) && !(Inner(ra) && ra.kind == "room" && rb.kind != "room");
        if (o.name.Contains("-")) intoB = true;      // connecting doors between rooms
        Vector3 target = intoB ? nrm : -nrm;
        d.hingeSign = Vector3.Dot(pivot.forward, target) > 0 ? -1f : 1f;
        if (o.startOpen) d.startAngle = 80f * d.hingeSign;
        // a slide bolt on the room side of the frame, at the latch side
        if (o.bolt)
        {
            var bp = B - dir * 0.09f + target * 0.07f + Vector3.up * 1.25f;
            var bolt = Box("Bolt " + o.name, go.transform, bp, new Vector3(o.AlongX ? 0.16f : 0.04f, 0.05f, o.AlongX ? 0.04f : 0.16f), M("Brass_Fitting"), LInteract, true, null, false, false);
            var bc = bolt.GetComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(o.AlongX ? 0.3f : 0.18f, 0.2f, o.AlongX ? 0.18f : 0.3f);
            var slide = Box("Slide", bolt.transform, bp, new Vector3(0.08f, 0.025f, 0.025f), M("Brass_Fitting"), LInteract, false, Quaternion.FromToRotation(Vector3.right, dir), false, false);
            var db = bolt.AddComponent<DoorBolt>();
            db.door = d;
            db.slide = slide.transform;
        }
        return d;
    }

    // ------------------------------------------------------------------ stairs (visual steps + a ramp collider)
    static void BuildStairs()
    {
        var root = new GameObject("Stairs").transform;
        root.SetParent(levelRoot, false);
        int si = 0;
        foreach (var s in LevelLayout.Stairs)
        {
            var run = new Vector3(s.top.x - s.bottom.x, 0, s.top.z - s.bottom.z);
            float rise = s.top.y - s.bottom.y;
            int n = Mathf.RoundToInt(rise / 0.17f);
            var fwd = run.normalized;
            var side = Vector3.Cross(Vector3.up, fwd);
            for (int i = 0; i < n; i++)
            {
                float h = (i + 1) * rise / n;
                var c = s.bottom + run * ((i + 0.5f) / n);
                var size = new Vector3(Mathf.Abs(fwd.x) > 0.5f ? run.magnitude / n : s.width, h, Mathf.Abs(fwd.z) > 0.5f ? run.magnitude / n : s.width);
                Box($"Step {si}-{i}", root, new Vector3(c.x, s.bottom.y + h / 2, c.z), size, M(s.mat), LProp, false, null, false);
            }
            // the space under the flight is solid (invisible blocks kept just under the ramp), so nobody walks into it
            int k = Mathf.Max(4, Mathf.RoundToInt(run.magnitude));
            for (int j = 1; j < k; j++)
            {
                float h = rise * j / k - 0.12f;
                if (h < 0.35f) continue;
                float seg = run.magnitude / k;
                var c = s.bottom + run * ((j + 0.5f) / k);
                var size = new Vector3(Mathf.Abs(fwd.x) > 0.5f ? seg : s.width, h, Mathf.Abs(fwd.z) > 0.5f ? seg : s.width);
                Box($"Under stair {si}-{j}", root, new Vector3(c.x, s.bottom.y + h / 2, c.z), size, M(s.mat), LProp, true, null, false).GetComponent<MeshRenderer>().enabled = false;
            }
            var a = s.bottom; var b = s.top;
            var mid = (a + b) / 2;
            float len = Vector3.Distance(a, b);
            var rot = Quaternion.LookRotation(b - a);
            var ramp = new GameObject($"Ramp_{s.mat}");
            ramp.transform.SetParent(root, false);
            ramp.transform.position = mid + rot * Vector3.down * 0.05f + Vector3.up * 0.04f;
            ramp.transform.rotation = rot;
            ramp.layer = LStructure;
            ramp.AddComponent<BoxCollider>().size = new Vector3(s.width, 0.1f, len + 0.3f);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var railC = mid + side * sgn * (s.width / 2 + 0.04f) + Vector3.up * 0.55f;
                Box($"Stair Rail {si}", root, railC, new Vector3(0.06f, 1.0f, len), M("Wood_Furniture"), LProp, true, rot, false);
            }
            si++;
        }
        foreach (var (a, b) in LevelLayout.Rails)
        {
            var mid = (a + b) / 2;
            float len = Vector3.Distance(a, b);
            bool alongX = Mathf.Abs(a.z - b.z) < 0.01f;
            Box("Landing Rail", root, mid + Vector3.up * 0.5f, alongX ? new Vector3(len, 1f, 0.06f) : new Vector3(0.06f, 1f, len), M("Wood_Furniture"), LProp, true);
        }
    }

    // ------------------------------------------------------------------ stages
    // Everything the builder made (shell, openings, props, lights, decay, cameras) is moved under one root per stage,
    // by where it is: 0 the bedroom, 1 the hotel (ground and guest floors), 2 the basement, 3 the penthouse and roof.
    // Game.ShowStage switches the roots, so only the stage you are in is drawn, lit and heard.
    static void SplitIntoStages()
    {
        string[] names = { "Stage 0 Prologue", "Stage 1 Hotel", "Stage 2 Basement", "Stage 3 Roof" };
        var roots = new GameObject[4];
        for (int i = 0; i < 4; i++) roots[i] = new GameObject(names[i]);
        var subs = new Dictionary<string, Transform>();
        Transform Sub(int st, string n)
        {
            string k = st + "/" + n;
            if (!subs.TryGetValue(k, out var t)) { t = new GameObject(n).transform; t.SetParent(roots[st].transform, false); subs[k] = t; }
            return t;
        }
        int StageOfObject(Transform t)
        {
            // particle effects have no bounds until they play (they sit at the origin): only meshes say where a thing is
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            Vector3 p = t.position;
            if (rs.Length > 0) { var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); p = b.center; if (b.size.y > 3f) p.y = b.min.y + 0.5f; }
            return LevelLayout.StageOf(p);
        }
        int moved = 0;
        foreach (var top in new[] { levelRoot, propRoot, lightRoot })
        {
            var containers = new List<Transform>();
            foreach (Transform c in top) containers.Add(c);
            foreach (var c in containers)
            {
                if (c.childCount == 0) { c.SetParent(Sub(StageOfObject(c), top.name), true); moved++; continue; }
                var kids = new List<Transform>();
                foreach (Transform k in c) kids.Add(k);
                foreach (var k in kids) { k.SetParent(Sub(StageOfObject(k), top.name + " " + c.name), true); moved++; }
                Object.DestroyImmediate(c.gameObject);
            }
            Object.DestroyImmediate(top.gameObject);
        }
        var game = Object.FindFirstObjectByType<Game>();
        if (game) { game.stageRoots = roots; EditorUtility.SetDirty(game); }
        var counts = new int[4];
        for (int i = 0; i < 4; i++) counts[i] = roots[i].GetComponentsInChildren<Light>(true).Length;
        Debug.Log($"STAGES: {moved} objects split; lights per stage {counts[0]} / {counts[1]} / {counts[2]} / {counts[3]}");
    }
}
