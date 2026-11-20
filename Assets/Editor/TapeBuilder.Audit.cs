using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Placement audit, run on the generated scene before it is split into stages: what pokes into a doorway (into the
// frame or the gap you walk through), what floats over or sinks into the thing it stands on (a book between two
// shelf boards), and solid furniture that overlaps other furniture or pushes through a wall. It writes
// verification/thetape/v4/placement_audit.txt and logs "PLACEMENT AUDIT: n issues". Nothing is changed by it: the
// fixes go into the generators (LevelLayout, TapeBuilder.*).
//   Unity -batchmode -quit -projectPath TheTape -executeMethod TapeBuilder.BuildSceneOnly   (the scene and this report)
public static partial class TapeBuilder
{
    class AuditItem
    {
        public Transform t;
        public Bounds b;
        public List<MeshFilter> meshes = new List<MeshFilter>();
        public string group;
    }

    static string Where(Vector3 p)
    {
        var r = LevelLayout.RoomAt(p);
        return r != null ? r.name : $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
    }

    static bool Hangs(string n)
    {
        n = n.ToLowerInvariant();
        // hung from the ceiling, or not a thing at all (decals, glows, effects); things on walls are found by the wall test
        string[] words = { "lamp", "light", "chandelier", "bulb", "tube", "fan", "shade", "cable", "wire", "vent", "pipe", "sconce", "camera",
                           "camcorder", "tally", "lens", "smoke", "speaker", "horn", "curtain", "cobweb", "web", "chain", "rope", "antenna", "drip",
                           "stain", "decal", "grime", "mould", "mold", "crack", "glow", "rain", "spark", "flame", "fire", "emitter", "sign", "string" };
        return words.Any(n.Contains);
    }

    // placed so on purpose: debris lying in heaps, a plant in its pot, pans on a hanging rack, buttons on a panel, the
    // letterbox in the front door, a cord in its socket, a drawer in its counter, the throw over the sofa's arm
    static bool AcceptedAlone(string n) =>
        new[] { "Plaster chunk", "Rubble", "Glass", "Cord", "Button", "Hanging", "Dead plant", "Planter", "Phone ", "Safe lip", "Call button",
                "Letterbox", "Cutlery drawer", "Throw", "Keyhole", "Socket", "Shelf board", "Safe ", "Fallen beam" }.Any(n.Contains);
    // (the fallen beam half blocks the west corridor's north end on purpose: the ceiling came down there)
    // pieces of one built thing that meet (a headboard against its bed frame, the fireplace's bricks, a fallen
    // chandelier in its own debris, a hiding space inside the desk it is under)
    static bool AcceptedPair(string a, string b)
    {
        bool Has(string x, params string[] w) => w.Any(x.Contains);
        if (AcceptedAlone(a) || AcceptedAlone(b)) return true;
        if (Has(a, "Bed frame", "Headboard") && Has(b, "Bed frame", "Headboard")) return true;
        if (Has(a, "Fireplace", "Hearth", "Chimney", "Mantel") && Has(b, "Fireplace", "Hearth", "Chimney", "Mantel")) return true;
        if (Has(a, "Fallen chandelier", "Coal heap", "Pallet") || Has(b, "Fallen chandelier", "Coal heap", "Pallet")) return true;
        if (Has(a, "(under)") || Has(b, "(under)")) return true;
        return false;
    }

    static void AuditPlacement()
    {
        var lines = new List<string>();
        try
        {
            Physics.SyncTransforms();
            // the items: each direct child of a props group (a model with its parts, a box, a lamp)
            var items = new List<AuditItem>();
            foreach (Transform grp in propRoot)
                foreach (Transform t in grp)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    var mfs = t.GetComponentsInChildren<MeshFilter>(false).Where(m => m.sharedMesh && m.GetComponent<MeshRenderer>() && m.GetComponent<MeshRenderer>().enabled).ToList();
                    if (mfs.Count == 0) continue;
                    var it = new AuditItem { t = t, group = grp.name };
                    it.meshes = mfs;
                    it.b = mfs[0].GetComponent<MeshRenderer>().bounds;
                    foreach (var m in mfs) it.b.Encapsulate(m.GetComponent<MeshRenderer>().bounds);
                    items.Add(it);
                }

            // ---------------- 1. doorways: nothing in the frame or the gap (above the floor: rugs and mats are fine)
            int doorIssues = 0;
            foreach (var o in LevelLayout.Openings)
            {
                if (o.kind != LevelLayout.Gap.Door && o.kind != LevelLayout.Gap.Locked && o.kind != LevelLayout.Gap.Opening) continue;
                float y = Base(o.floor);
                var org = Org(o.floor);
                Vector3 A = new Vector3(o.a.x, y, o.a.y) + org, B = new Vector3(o.b.x, y, o.b.y) + org;
                float h = Mathf.Min(o.height, 2.1f) - 0.05f;
                // the clear opening: between the jambs, through the wall and the frame's depth (0.2 m), floor to lintel
                Vector3 min = Vector3.Min(A, B), max = Vector3.Max(A, B);
                if (o.AlongX) { min.x += 0.025f; max.x -= 0.025f; min.z -= 0.12f; max.z += 0.12f; }
                else { min.z += 0.025f; max.z -= 0.025f; min.x -= 0.12f; max.x += 0.12f; }
                min.y = y + 0.03f; max.y = y + h;
                var box = new Bounds((min + max) * 0.5f, max - min);
                foreach (var it in items)
                {
                    if (!it.b.Intersects(box) || AcceptedAlone(it.t.name)) continue;
                    int inside = 0; float deepest = 0f;
                    foreach (var mf in it.meshes)
                    {
                        if (mf.gameObject.layer == LDoor) continue;
                        var m = mf.transform.localToWorldMatrix;
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            var w = m.MultiplyPoint3x4(v);
                            if (!box.Contains(w)) continue;
                            inside++;
                            float d = o.AlongX ? Mathf.Min(w.x - min.x, max.x - w.x) : Mathf.Min(w.z - min.z, max.z - w.z);
                            deepest = Mathf.Max(deepest, d);
                        }
                    }
                    if (inside > 0 && deepest > 0.01f)
                    {
                        doorIssues++;
                        lines.Add($"[doorway] {o.name} (floor {o.floor}): \"{it.t.name}\" ({it.group}) pokes {deepest:0.00} m into the doorway ({inside} vertices)");
                    }
                }
            }

            // ---------------- 1b. in front of a door: solid things in the 0.6 m you step through on either side
            foreach (var o in LevelLayout.Openings)
            {
                if (o.kind != LevelLayout.Gap.Door && o.kind != LevelLayout.Gap.Locked && o.kind != LevelLayout.Gap.Opening) continue;
                float y = Base(o.floor);
                var org = Org(o.floor);
                Vector3 A = new Vector3(o.a.x, y, o.a.y) + org, B = new Vector3(o.b.x, y, o.b.y) + org;
                foreach (float sideSign in new[] { -1f, 1f })
                {
                    Vector3 min = Vector3.Min(A, B), max = Vector3.Max(A, B);
                    if (o.AlongX) { min.x += 0.05f; max.x -= 0.05f; if (sideSign > 0) { min.z += 0.1f; max.z += 0.7f; } else { max.z -= 0.1f; min.z -= 0.7f; } }
                    else { min.z += 0.05f; max.z -= 0.05f; if (sideSign > 0) { min.x += 0.1f; max.x += 0.7f; } else { max.x -= 0.1f; min.x -= 0.7f; } }
                    min.y = y + 0.15f; max.y = y + 1.8f;
                    var c = (min + max) * 0.5f;
                    float lo = o.AlongX ? min.x : min.z, hi = o.AlongX ? max.x : max.z;
                    var blocked = new List<(float a, float b, string n)>();
                    foreach (var col in Physics.OverlapBox(c, (max - min) * 0.5f, Quaternion.identity, (1 << LProp) | (1 << LInteract), QueryTriggerInteraction.Ignore))
                    {
                        if (col.gameObject.layer == LDoor || col.transform.name.StartsWith("Frame") || AcceptedAlone(col.transform.name)) continue;
                        if (col.transform.IsChildOf(levelRoot)) continue;                  // the door's own frame, railings
                        var cb = col.bounds;
                        blocked.Add((o.AlongX ? cb.min.x : cb.min.z, o.AlongX ? cb.max.x : cb.max.z, col.transform.name));
                    }
                    if (blocked.Count == 0) continue;
                    // the widest free stretch left across the door; a column at the edge of a wide arch is fine
                    float free = 0f, at = lo;
                    foreach (var bl in blocked.OrderBy(x => x.a)) { free = Mathf.Max(free, Mathf.Min(bl.a, hi) - at); at = Mathf.Max(at, bl.b); }
                    free = Mathf.Max(free, hi - at);
                    float width = hi - lo, need = width <= 1.2f ? width - 0.02f : Mathf.Max(0.8f, 0.6f * width);   // a door: all of it
                    if (free >= need) continue;
                    doorIssues++;
                    string sideName = o.AlongX ? (sideSign > 0 ? "north" : "south") : (sideSign > 0 ? "east" : "west");
                    lines.Add($"[blocks door] {o.name} (floor {o.floor}): {string.Join(", ", blocked.Select(x => "\"" + x.n + "\""))} in the way on the {sideName} side ({free:0.00} of {hi - lo:0.00} m free)");
                }
            }

            // ---------------- 2. solid furniture: overlapping other furniture, or pushed into a wall
            int overlapIssues = 0;
            var solids = new List<(BoxCollider c, AuditItem it, bool exact)>();
            foreach (var it in items)
                foreach (var c in it.t.GetComponentsInChildren<BoxCollider>(false))
                {
                    if (c.isTrigger || !c.enabled || c.gameObject.layer == LDoor) continue;
                    bool exact = c.GetComponent<MeshFilter>() != null;      // a built box (exact), or a model's bounding box
                    solids.Add((c, it, exact));
                }
            for (int i = 0; i < solids.Count; i++)
                for (int j = i + 1; j < solids.Count; j++)
                {
                    var a = solids[i]; var b = solids[j];
                    if (a.it == b.it || AcceptedPair(a.it.t.name, b.it.t.name)) continue;
                    if (!a.c.bounds.Intersects(b.c.bounds)) continue;
                    if (!Physics.ComputePenetration(a.c, a.c.transform.position, a.c.transform.rotation, b.c, b.c.transform.position, b.c.transform.rotation, out _, out float dist)) continue;
                    float limit = a.exact && b.exact ? 0.04f : 0.10f;
                    if (dist <= limit) continue;
                    overlapIssues++;
                    lines.Add($"[overlap] {Where(a.c.bounds.center)}: \"{a.it.t.name}\" and \"{b.it.t.name}\" overlap by {dist:0.00} m");
                }
            var walls = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == LStructure && !c.isTrigger && c.enabled).ToList();
            foreach (var s in solids)
                foreach (var w in walls)
                {
                    if (!s.c.bounds.Intersects(w.bounds)) continue;
                    // floors and ceilings are structure too: only walls (taller than wide in one axis) count here
                    var wb = w.bounds;
                    if (wb.size.y < 1.2f || (wb.size.x > 0.4f && wb.size.z > 0.4f)) continue;
                    if (!Physics.ComputePenetration(s.c, s.c.transform.position, s.c.transform.rotation, w, w.transform.position, w.transform.rotation, out _, out float dist)) continue;
                    if (dist <= (s.exact ? 0.08f : 0.10f)) continue;      // the wall skin is 7.5 cm: deeper comes out the other side
                    overlapIssues++;
                    lines.Add($"[wall] {Where(s.c.bounds.center)}: \"{s.it.t.name}\" ({s.it.group}) is {dist:0.00} m into a wall");
                }

            // ---------------- 3. small things: floating over, or sunk into, what they stand on
            int restIssues = 0;
            var allRenderers = items.SelectMany(i => i.meshes.Select(m => (m, i))).ToList();
            foreach (var it in items)
            {
                var b = it.b;
                float big = Mathf.Max(b.size.x, b.size.z);
                if (big > 1.0f || b.size.y > 1.2f) continue;                       // furniture stands on the floor
                if (b.size.y < 0.012f || Mathf.Min(b.size.x, b.size.z) < 0.012f) continue;   // decals
                if (Hangs(it.t.name) || AcceptedAlone(it.t.name)) continue;
                // wall-mounted things (within 8 cm of a wall) are held by the wall
                var near = Physics.OverlapBox(b.center, b.extents + new Vector3(0.08f, 0f, 0.08f), Quaternion.identity, 1 << LStructure, QueryTriggerInteraction.Ignore);
                bool onWall = near.Any(c => c.bounds.size.y > 1.2f && (c.bounds.size.x <= 0.4f || c.bounds.size.z <= 0.4f));
                // what's under its middle: colliders (not its own), and the top faces of models without colliders
                // looked for from just under its top, so a board or table top passing through it is found (sunk)
                Vector3 p = new Vector3(b.center.x, b.max.y - 0.01f, b.center.z);
                float support = float.NegativeInfinity; string under = null;
                foreach (var hit in Physics.RaycastAll(p, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(it.t)) continue;                  // anything can hold it up (a board, a heap)
                    if (hit.point.y > support) { support = hit.point.y; under = hit.transform.name; }
                }
                foreach (var (mf, owner) in allRenderers)
                {
                    if (owner == it) continue;
                    var rb = owner.b;
                    if (p.x < rb.min.x || p.x > rb.max.x || p.z < rb.min.z || p.z > rb.max.z || rb.min.y > p.y) continue;
                    if (mf.GetComponent<Collider>()) continue;                // already found by the rays
                    float top = MeshTopBelow(mf, p);
                    if (top > support) { support = top; under = owner.t.name; }
                }
                if (float.IsNegativeInfinity(support)) continue;
                float gap = b.min.y - support;
                if (gap > 0.04f && !onWall)
                {
                    restIssues++;
                    lines.Add($"[floats] {Where(b.center)}: \"{it.t.name}\" ({it.group}) floats {gap:0.00} m above \"{under}\"");
                }
                else if (gap < -0.05f && !onWall)
                {
                    restIssues++;
                    lines.Add($"[sunk] {Where(b.center)}: \"{it.t.name}\" ({it.group}) is {-gap:0.00} m down into \"{under}\"");
                }
            }

            lines.Insert(0, $"PLACEMENT AUDIT: {lines.Count} issues ({doorIssues} doorways, {overlapIssues} overlaps, {restIssues} floating or sunk), {items.Count} items checked");
        }
        catch (System.Exception e)
        {
            lines.Insert(0, "PLACEMENT AUDIT failed: " + e);
        }
        Debug.Log(lines[0]);
        foreach (var l in lines.Skip(1)) Debug.Log("PLACEMENT " + l);
        var outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../verification/thetape/v4/placement_audit.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllLines(outPath, lines);
    }

    // the heights of a model's horizontal faces at (x, z), lowest first (a shelf's boards: each board's top and
    // underside; a table's top)
    static List<float> SurfacesAt(GameObject model, float x, float z)
    {
        var ys = new List<float>();
        var probe = new Vector3(x, 1e6f, z);
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh) MeshFaces(mf, probe, ys);
        ys.Sort();
        var merged = new List<float>();
        foreach (var y in ys) if (merged.Count == 0 || y - merged[merged.Count - 1] > 0.01f) merged.Add(y);
        return merged;
    }

    // the highest horizontal face of a mesh under point p (at or below p), or -infinity
    static float MeshTopBelow(MeshFilter mf, Vector3 p)
    {
        var ys = new List<float>();
        MeshFaces(mf, p, ys);
        return ys.Count == 0 ? float.NegativeInfinity : ys.Max();
    }

    // the heights (at or below p) of the mesh's horizontal faces over p's x and z
    static void MeshFaces(MeshFilter mf, Vector3 p, List<float> ys)
    {
        var mesh = mf.sharedMesh;
        var m = mf.transform.localToWorldMatrix;
        var v = mesh.vertices;
        var w = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) w[i] = m.MultiplyPoint3x4(v[i]);
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tri = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Vector3 a = w[tri[i]], b = w[tri[i + 1]], c = w[tri[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                if (Mathf.Abs(n.y) < 0.85f) continue;                           // only (near) horizontal faces
                // is p inside the triangle seen from above?
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
                float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (y <= p.y) ys.Add(y);
            }
        }
    }
}
