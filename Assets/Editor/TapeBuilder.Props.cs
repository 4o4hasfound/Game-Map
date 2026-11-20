using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Props, lights and game objects, room by room (ground floor here; the guest floor, basement and bedroom in
// TapeBuilder.Rooms.cs). Every room has a job (see LevelLayout's purpose strings): a lure, a tool, a place to hide,
// a quiet or loud route, a camera, a clue. Decay is dressed from the room's decay tag: rubble, glass and water are
// LOUD under your feet (Footsteps reads the material), ceilings have fallen in, lights flicker or are dying.
public static partial class TapeBuilder
{
    static Vector3 W(float x, float z, int floor = 0, float up = 0f) => LevelLayout.W(x, z, floor, up);
    static Transform Group(string name) { var t = new GameObject(name).transform; t.SetParent(propRoot, false); return t; }
    static LevelLayout.Room R(string name) => LevelLayout.Rooms.Find(r => r.name == name);
    // light colours as a camcorder on auto white balance records them: incandescent a warm white, fluorescent near
    // neutral, emergency units the white of their halogen heads (real ones aren't red)
    static readonly Color Warm = new Color(1f, 0.86f, 0.7f), Tubeish = new Color(0.93f, 0.97f, 1f), EmergencyWhite = new Color(1f, 0.96f, 0.9f);
    static System.Random rng = new System.Random(7);
    static float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ lights
    static Lamp MakeLamp(Transform parent, string name, Vector3 pos, float range, float intensity, Color color, bool on, bool shadows = false, Vector3? bulbSize = null,
                         Lamp.Pattern pattern = Lamp.Pattern.Steady, bool fluorescent = false, bool powered = true)
    {
        var bulb = Box(name, parent, pos, bulbSize ?? new Vector3(0.18f, 0.12f, 0.18f), M("Glass_Bulb"), LProp, false, null, false, false);
        var lg = new GameObject("Light");
        lg.transform.SetParent(bulb.transform, false);
        lg.transform.localPosition = Vector3.down * 0.12f;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        intensity *= 5f;                 // URP point lights fall off with 1/d^2 (the camcorder's auto exposure does the rest)
        l.intensity = intensity;
        l.color = color;
        l.shadows = shadows ? LightShadows.Hard : LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        lg.AddComponent<UniversalAdditionalLightData>();
        var lamp = bulb.AddComponent<Lamp>();
        lamp.lightSource = l;
        lamp.bulb = bulb.GetComponent<Renderer>();
        lamp.on = on;
        lamp.powered = powered;
        lamp.baseIntensity = intensity;
        lamp.emission = color;
        lamp.pattern = pattern;
        lamp.fluorescent = fluorescent;
        return lamp;
    }
    static Lamp Tube(Transform g, Vector3 pos, float range, Lamp.Pattern p, bool powered = true, float intensity = 1.0f, bool alongX = true)
        => MakeLamp(g, "Tube", pos, range, intensity * 0.6f, Tubeish, true, false, alongX ? new Vector3(1.2f, 0.06f, 0.1f) : new Vector3(0.1f, 0.06f, 1.2f), p, true, powered);
    static Lamp Bulb(Transform g, Vector3 pos, float range, float intensity, Lamp.Pattern p, bool on = true) => MakeLamp(g, "Bulb", pos, range, intensity, Warm, on, false, null, p);
    static Lamp Emergency(Transform g, Vector3 pos, float range = 7f) => MakeLamp(g, "Emergency light", pos, range, 0.55f, EmergencyWhite, true, false, new Vector3(0.25f, 0.1f, 0.1f), Lamp.Pattern.Emergency);
    static Lamp Sconce(Transform g, Vector3 pos, Lamp.Pattern p = Lamp.Pattern.Steady, bool on = true, float range = 5.5f, float intensity = 0.85f)
    {
        // not in a doorway (a corridor's evenly spaced sconces used to land in room doors and arches)
        var o = OpeningAt(pos, 0.15f);
        if (o != null) pos = PastOpening(pos, o, 0.45f);
        return MakeLamp(g, "Sconce", pos, range, intensity, Warm, on, false, new Vector3(0.16f, 0.22f, 0.16f), p);
    }

    // the door, arch or window gap in the wall at p (within halfWidth along the wall, plus a margin), or null
    static LevelLayout.Opening OpeningAt(Vector3 p, float halfWidth, float margin = 0.08f)
    {
        int f = LevelLayout.FloorOf(p);
        var org = Org(f);
        float lx = p.x - org.x, lz = p.z - org.z, y = p.y - Base(f);
        foreach (var o in LevelLayout.Openings)
        {
            if (o.floor != f || o.kind == LevelLayout.Gap.Railing) continue;
            float lo, hi, along;
            if (o.AlongX) { if (Mathf.Abs(lz - o.a.y) > 0.22f) continue; lo = Mathf.Min(o.a.x, o.b.x); hi = Mathf.Max(o.a.x, o.b.x); along = lx; }
            else { if (Mathf.Abs(lx - o.a.x) > 0.22f) continue; lo = Mathf.Min(o.a.y, o.b.y); hi = Mathf.Max(o.a.y, o.b.y); along = lz; }
            if (along + halfWidth < lo - margin || along - halfWidth > hi + margin) continue;
            float top = o.kind == LevelLayout.Gap.Window ? 2.3f : o.height;
            if (y > top + 0.1f) continue;
            return o;
        }
        return null;
    }

    // p moved along its wall to just past the nearer end of the opening
    static Vector3 PastOpening(Vector3 p, LevelLayout.Opening o, float clear)
    {
        var org = Org(o.floor);
        if (o.AlongX) { float lo = Mathf.Min(o.a.x, o.b.x) + org.x, hi = Mathf.Max(o.a.x, o.b.x) + org.x; p.x = p.x - lo < hi - p.x ? lo - clear : hi + clear; }
        else { float lo = Mathf.Min(o.a.y, o.b.y) + org.z, hi = Mathf.Max(o.a.y, o.b.y) + org.z; p.z = p.z - lo < hi - p.z ? lo - clear : hi + clear; }
        return p;
    }

    static LightSwitch MakeSwitch(Transform parent, Vector3 pos, Vector3 wallNormal, Lamp[] lamps, bool cord = false)
    {
        var s = Box(cord ? "Pull cord" : "Light switch", parent, pos, cord ? new Vector3(0.03f, 0.35f, 0.03f) : new Vector3(Mathf.Abs(wallNormal.x) > 0.5f ? 0.03f : 0.09f, 0.12f, Mathf.Abs(wallNormal.z) > 0.5f ? 0.03f : 0.09f), M(cord ? "Fabric_Bed" : "Plastic_Beige"), LInteract, true, null, false, false);
        s.GetComponent<BoxCollider>().isTrigger = true;
        s.GetComponent<BoxCollider>().size *= 2.2f;
        var ls = s.AddComponent<LightSwitch>();
        ls.lamps = lamps;
        ls.pullCord = cord;
        return ls;
    }

    static Lamp CeilingLight(Transform p, LevelLayout.Room room, bool on, Vector3 switchPos, Vector3 wallN, float intensity = 1.2f, Lamp.Pattern pattern = Lamp.Pattern.Steady)
    {
        var c = room.r.center;
        // through LevelLayout.W, which puts floor 2 (your house) at its own origin (the lights used to land in the hotel)
        var at = LevelLayout.W(c.x, c.y, room.floor, room.ceil - 0.15f);
        var lamp = MakeLamp(p, "Ceiling light", at, Mathf.Max(room.r.width, room.r.height) * 1.0f, intensity, new Color(1f, 0.85f, 0.65f), on, false, new Vector3(0.35f, 0.08f, 0.35f), pattern);
        MakeSwitch(p, switchPos, wallN, new[] { lamp });
        return lamp;
    }

    // ------------------------------------------------------------------ furniture, objects
    static GameObject F(Transform g, string name, Vector3 c, Vector3 size, string mat, float yaw = 0f, bool col = true)
        => Box(name, g, c, size, M(mat), LProp, col, yaw == 0f ? (Quaternion?)null : Quaternion.Euler(0, yaw, 0), false);

    static Phone MakePhone(Transform parent, Vector3 pos, float yaw, string room, bool dialable = true)
    {
        var body = Box("Phone " + room, parent, pos + Vector3.up * 0.04f, new Vector3(0.2f, 0.08f, 0.22f), M("Plastic_Beige"), LInteract, true, Quaternion.Euler(0, yaw, 0), false, false);
        body.GetComponent<BoxCollider>().isTrigger = true;
        body.GetComponent<BoxCollider>().size = new Vector3(0.35f, 0.25f, 0.35f);
        var rec = Box("Receiver", body.transform, pos + Vector3.up * 0.1f, new Vector3(0.22f, 0.05f, 0.06f), M("Plastic_Beige"), LInteract, false, Quaternion.Euler(0, yaw, 0), false, false);
        var p = body.AddComponent<Phone>();
        p.room = room;
        p.receiver = rec.transform;
        p.dialable = dialable;
        return p;
    }

    static HideSpot MakeHide(Transform parent, string name, Vector3 pos, Vector3 size, float yaw, string kind, Vector3 eyeLocal, Material mat, float viewYaw = 0f, bool solid = true)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        var go = Box(name, parent, pos, size, mat, LProp, true, rot, false, false);    // solid furniture (you hide inside it)
        var trig = new GameObject("Use");
        trig.transform.SetParent(go.transform, false);
        trig.layer = LInteract;
        var bc = trig.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        // a margin round the sides and below, none above the top: a note lying on a desk you hide under must catch your
        // aim before the desk does (the manager's incident log could not be read: you hid under the desk instead)
        bc.size = size + new Vector3(0.2f, 0.1f, 0.2f);
        bc.center = new Vector3(0f, -0.05f, 0f);
        var h = trig.AddComponent<HideSpot>();
        h.kind = kind;
        var view = new GameObject("View").transform;
        view.SetParent(go.transform, false);
        view.localPosition = eyeLocal;
        view.localRotation = Quaternion.Euler(0, viewYaw, 0);
        h.view = view;
        if (kind == "wardrobe" || kind == "locker")
            for (int i = 0; i < 7; i++)
                Box("Slat", go.transform, go.transform.TransformPoint(new Vector3(0, -0.6f + i * 0.22f, size.z / 2 + 0.01f)), new Vector3(size.x * 0.95f, 0.06f, 0.02f), M("Wood_Furniture"), LProp, false, rot, false, false);
        return h;
    }

    static NoiseDevice Device(Transform g, string label, Vector3 pos, Vector3 size, string mat, string[] clips, float db, float vol, float duration,
                              float mask = 0f, float maskR = 6f, bool oneShot = false, int uses = 1, string desc = "", float yaw = 0f, string startClip = "radio_click")
    {
        var go = Box(Cap(label), g, pos, size, M(mat), LInteract, true, yaw == 0f ? (Quaternion?)null : Quaternion.Euler(0, yaw, 0), false, false);
        var bc = go.GetComponent<BoxCollider>();
        bc.isTrigger = false;
        var trig = new GameObject("Use"); trig.transform.SetParent(go.transform, false); trig.layer = LInteract;
        var tb = trig.AddComponent<BoxCollider>(); tb.isTrigger = true; tb.size = size + Vector3.one * 0.2f;
        go.layer = LProp;
        var d = trig.AddComponent<NoiseDevice>();
        d.label = label; d.clips = clips; d.db = db; d.volume = vol; d.duration = duration; d.mask = mask; d.maskRadius = maskR;
        d.oneShot = oneShot; d.uses = uses; d.description = desc; d.startClip = startClip;
        return d;
    }
    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    static NoiseDevice AutoMachine(Transform g, string label, Vector3 pos, string clip, float mask, float maskR, Vector2 cycle, float vol, string desc)
    {
        var go = new GameObject(Cap(label));
        go.transform.SetParent(g, false);
        go.transform.position = pos;
        var d = go.AddComponent<NoiseDevice>();
        d.label = label; d.clips = new[] { clip }; d.db = 0f; d.volume = vol; d.auto = true; d.autoCycle = cycle; d.mask = mask; d.maskRadius = maskR; d.description = desc;
        return d;
    }

    static Pickup Bottles(Transform g, Vector3 pos, int count, string desc = "Glass bottles: throw one (right click) and it smashes where it lands. It goes there.")
    {
        var root = new GameObject("Bottles");
        root.transform.SetParent(g, false);
        root.transform.position = pos;
        root.layer = LInteract;
        for (int i = 0; i < count; i++)
            Box("Bottle", root.transform, pos + new Vector3(i * 0.09f - count * 0.045f, 0.12f, (i % 2) * 0.05f), new Vector3(0.07f, 0.26f, 0.07f), M("Glass_Bottle"), LInteract, false, null, false, false);
        var bc = root.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0, 0.13f, 0); bc.size = new Vector3(0.45f, 0.35f, 0.3f);
        var p = root.AddComponent<Pickup>();
        p.kind = "bottle"; p.count = count; p.description = desc;
        return p;
    }

    static Pickup BatteryPickup(Transform g, Vector3 pos)
    {
        var go = Box("Camera battery", g, pos + Vector3.up * 0.03f, new Vector3(0.12f, 0.05f, 0.07f), M("Plastic_Device"), LInteract, true, Quaternion.Euler(0, Rnd(0, 360), 0), false, false);
        go.GetComponent<BoxCollider>().isTrigger = true;
        go.GetComponent<BoxCollider>().size = new Vector3(0.35f, 0.3f, 0.3f);
        var p = go.AddComponent<Pickup>();
        p.kind = "battery"; p.description = "A spare camcorder battery: refills the camera light (F).";
        return p;
    }

    static Pickup RadioPickup(Transform g, Vector3 pos)
    {
        var go = Box("Portable radio", g, pos + Vector3.up * 0.09f, new Vector3(0.3f, 0.18f, 0.1f), M("Plastic_Device"), LInteract, true, null, false, false);
        go.GetComponent<BoxCollider>().isTrigger = true;
        go.GetComponent<BoxCollider>().size = new Vector3(0.45f, 0.35f, 0.3f);
        var p = go.AddComponent<Pickup>();
        p.kind = "radio"; p.description = "A portable radio: carry it, set it down somewhere (right click). It switches itself on 5 s later and plays until it comes and smashes it.";
        return p;
    }

    static Readable Note(Transform p, string name, Vector3 pos, Vector3 size, Quaternion rot, string title, string text, bool hand, string mat = "Paper_Note")
    {
        var go = Box(name, p, pos, size, M(mat), LInteract, true, rot, false, false);
        go.GetComponent<BoxCollider>().isTrigger = true;
        go.GetComponent<BoxCollider>().size = size + Vector3.one * 0.12f;
        var r = go.AddComponent<Readable>();
        r.title = title; r.text = text; r.handwritten = hand;
        return r;
    }
    static Readable DeskNote(Transform g, string name, Vector3 pos, string title, string text, bool hand = true)
        => Note(g, name, pos, new Vector3(0.22f, 0.01f, 0.3f), Quaternion.Euler(0, Rnd(-15f, 15f), 0), title, text, hand);
    static Readable WallNote(Transform g, string name, Vector3 pos, Vector3 normal, string title, string text, bool hand = true)
        => Note(g, name, pos + normal * 0.01f, Mathf.Abs(normal.x) > 0.5f ? new Vector3(0.01f, 0.3f, 0.22f) : new Vector3(0.22f, 0.3f, 0.01f), Quaternion.identity, title, text, hand);

    // ------------------------------------------------------------------ decay
    static Rect? patchClamp;       // keeps decay patches inside the room being dressed
    static void Patch(Transform g, string name, float x0, float z0, float x1, float z1, int floor, string mat, float h = 0.012f)
    {
        if (patchClamp.HasValue)
        {
            var c = patchClamp.Value;
            x0 = Mathf.Max(x0, c.xMin + 0.08f); x1 = Mathf.Min(x1, c.xMax - 0.08f); z0 = Mathf.Max(z0, c.yMin + 0.08f); z1 = Mathf.Min(z1, c.yMax - 0.08f);
            if (x1 - x0 < 0.1f || z1 - z0 < 0.1f) return;
        }
        float y = LevelLayout.FloorY(floor);
        Box(name, g, new Vector3((x0 + x1) / 2, y + h / 2, (z0 + z1) / 2), new Vector3(x1 - x0, h, z1 - z0), M(mat), LProp, true, null, true);
    }
    static void Puddle(Transform g, float x, float z, int floor, float sx = 1.4f, float sz = 1.0f) => Patch(g, "Water_Puddle", x - sx / 2, z - sz / 2, x + sx / 2, z + sz / 2, floor, "Water_Floor", 0.012f);
    static void GlassPatch(Transform g, float x0, float z0, float x1, float z1, int floor) => Patch(g, "Glass_Shards", x0, z0, x1, z1, floor, "Glass_Shards", 0.012f);
    static void RubblePile(Transform g, float x, float z, int floor, float s = 1.4f)
    {
        float y = LevelLayout.FloorY(floor);
        Patch(g, "Rubble", x - s, z - s * 0.8f, x + s, z + s * 0.8f, floor, "Rubble_Floor", 0.05f);
        for (int i = 0; i < 6; i++)
        {
            var c = new Vector3(x + Rnd(-s * 0.7f, s * 0.7f), y + 0.1f, z + Rnd(-s * 0.5f, s * 0.5f));
            if (patchClamp.HasValue) { var cr = patchClamp.Value; c.x = Mathf.Clamp(c.x, cr.xMin + 0.3f, cr.xMax - 0.3f); c.z = Mathf.Clamp(c.z, cr.yMin + 0.3f, cr.yMax - 0.3f); }
            Box("Plaster chunk", g, c, new Vector3(Rnd(0.15f, 0.5f), Rnd(0.06f, 0.2f), Rnd(0.15f, 0.5f)), M(i % 2 == 0 ? "Ceiling_Plaster" : "Rubble_Floor"), LProp, false, Quaternion.Euler(Rnd(-20, 20), Rnd(0, 360), Rnd(-20, 20)), false);
        }
    }
    static void Stain(Transform g, Vector3 pos, Vector3 normal, Vector2 size)
    {
        // a stain on a wall, not hanging in the air across a doorway
        if (Mathf.Abs(normal.y) < 0.5f && OpeningAt(pos, size.x * 0.5f) != null) return;
        Quad("Stain", g, pos + normal * 0.012f, size, Quaternion.LookRotation(-normal), M("Stain_Dark"));
    }
    static void Boarded(Transform g, Vector3 pos, Vector3 inward)
    {
        // a window on an outside wall, boarded from inside: glass dark behind the planks
        bool alongX = Mathf.Abs(inward.z) > 0.5f;
        Box("Window glass", g, pos - inward * 0.02f, alongX ? new Vector3(1.2f, 1.1f, 0.02f) : new Vector3(0.02f, 1.1f, 1.2f), M("Glass_Night"), LProp, false);
        for (int i = 0; i < 3; i++)
        {
            var rot = Quaternion.LookRotation(inward) * Quaternion.Euler(0, 0, Rnd(-14f, 14f));
            Box("Board", g, pos + inward * 0.02f + Vector3.up * (i - 1) * 0.36f, new Vector3(1.45f, 0.18f, 0.025f), M("Plywood_Board"), LProp, false, rot, false);
        }
    }
    static void Debris(Transform g, LevelLayout.Room room, int n)
    {
        float y = room.Y;
        for (int i = 0; i < n; i++)
        {
            var c = new Vector3(Rnd(room.r.xMin + 0.5f, room.r.xMax - 0.5f), y + 0.04f, Rnd(room.r.yMin + 0.5f, room.r.yMax - 0.5f));
            string mat = i % 3 == 0 ? "Paper_Note" : i % 3 == 1 ? "Ceiling_Plaster" : "Wood_Furniture";
            Box("Debris", g, c, new Vector3(Rnd(0.1f, 0.4f), 0.04f, Rnd(0.1f, 0.4f)), M(mat), LProp, false, Quaternion.Euler(0, Rnd(0, 360), 0), false);
        }
    }
    static void Dress(Transform g, LevelLayout.Room room)
    {
        var r = room.r;
        float y = room.Y;
        if (room.decay == "") return;
        patchClamp = r;
        Debris(g, room, room.decay == "light" ? 3 : 7);
        Stain(g, new Vector3(Rnd(r.xMin + 1, r.xMax - 1), y + 0.001f, Rnd(r.yMin + 1, r.yMax - 1)), Vector3.up, new Vector2(Rnd(0.8f, 1.8f), Rnd(0.6f, 1.4f)));
        Stain(g, new Vector3(r.xMin + 0.08f, y + Rnd(1.2f, 2.2f), Rnd(r.yMin + 1, r.yMax - 1)), Vector3.right, new Vector2(Rnd(0.5f, 1.2f), Rnd(0.8f, 1.6f)));
        if (room.decay == "collapsed")
        {
            var h = CeilingHole(room);
            RubblePile(g, h.center.x, h.center.y, room.floor, 1.2f);
        }
        if (room.decay == "flooded")
        {
            Puddle(g, Rnd(r.xMin + 1.2f, r.xMax - 1.2f), Rnd(r.yMin + 1.2f, r.yMax - 1.2f), room.floor, Rnd(1.2f, 2.2f), Rnd(0.8f, 1.6f));
            var drip = new Vector3(Rnd(r.xMin + 1, r.xMax - 1), y + room.ceil - 0.3f, Rnd(r.yMin + 1, r.yMax - 1));
            Emit(drip, "amb_drip", 0.35f, false, new Vector2(1.5f, 5f));
        }
        if (room.decay == "burnt")
        {
            GlassPatch(g, r.xMin + 1.5f, r.yMin + 1.5f, r.xMax - 1.5f, r.yMax - 1.5f, room.floor);
        }
        patchClamp = null;
    }

    // ------------------------------------------------------------------ ambience
    static Transform ambRoot;
    static void Emit(Vector3 pos, string clip, float vol, bool loop = true, Vector2? interval = null)
    {
        if (!ambRoot) { ambRoot = new GameObject("Ambience").transform; ambRoot.SetParent(propRoot, false); }
        var go = new GameObject("Ambient " + clip);
        go.transform.SetParent(ambRoot, false);
        go.transform.position = pos;
        go.AddComponent<AudioSource>();
        go.AddComponent<SpatialVoice>();
        var e = go.AddComponent<AmbientEmitter>();
        e.clip = clip; e.volume = vol; e.loop = loop;
        if (interval.HasValue) e.interval = interval.Value;
    }

    // ------------------------------------------------------------------ the whole building
    static void BuildProps(Refs refs, List<HingeDoor> doors)
    {
        rng = new System.Random(7);
        ambRoot = null;
        GroundFloor(refs);
        GuestFloor(refs);
        Basement(refs);
        Penthouse(refs);
        RoofTop(refs);
        House(refs);
        Wayfinding();
        foreach (var room in LevelLayout.Rooms) if (room.floor != 2) Dress(Group("Decay " + room.name), room);
    }

    // ================================================================== ground floor
    static void GroundFloor(Refs refs)
    {
        // ---------------- the lobby: marble is loud, the rugs are quiet paths; the atrium shows the guest floor
        var g = Group("Grand Lobby");
        MakeLamp(g, "Chandelier", W(30f, 16f, 0, 5.4f), 11f, 1.1f, Warm, true, true, new Vector3(0.3f, 0.2f, 0.3f), Lamp.Pattern.Flicker);
        Sconce(g, W(20.1f, 20f, 0, 2.3f), Lamp.Pattern.Steady, true, 5f, 0.7f);
        Sconce(g, W(39.9f, 20f, 0, 2.3f), Lamp.Pattern.Dying, true, 5f, 0.7f);
        Sconce(g, W(25f, 8.1f, 0, 2.3f), Lamp.Pattern.Flicker, true, 5f, 0.7f);
        Sconce(g, W(35f, 8.1f, 0, 2.3f), Lamp.Pattern.Steady, false);
        // quiet paths: a runner from the front office to the grand stair, and one from the doors to the restaurant
        Box("Rug runner", g, W(28.0f, 13.2f, 0, 0.012f), new Vector3(14.5f, 0.025f, 1.6f), M("Carpet_Gallery"), LProp, true);
        Box("Rug runner", g, W(30f, 18f, 0, 0.012f), new Vector3(1.6f, 0.025f, 15.5f), M("Carpet_Gallery"), LProp, true);
        Box("Rug", g, W(26.5f, 21.5f, 0, 0.012f), new Vector3(4f, 0.025f, 3f), M("Carpet_Room"), LProp, true);
        // reception counter in front of the front office door
        F(g, "Reception counter", W(22.6f, 13.2f, 0, 0.55f), new Vector3(0.6f, 1.1f, 6.4f), "Wood_Furniture");
        F(g, "Counter top", W(22.6f, 13.2f, 0, 1.12f), new Vector3(0.8f, 0.05f, 6.6f), "Marble_Lobby", 0f, false);
        Box("Bell", g, W(22.6f, 11.2f, 0, 1.18f), new Vector3(0.1f, 0.07f, 0.1f), M("Brass_Fitting"), LProp, false);
        MakeLamp(g, "Desk lamp", W(22.6f, 15.6f, 0, 1.35f), 4f, 0.6f, Warm, true, false, null, Lamp.Pattern.Buzz);
        Note(g, "Guest register", W(22.6f, 13.0f, 0, 1.16f), new Vector3(0.4f, 0.04f, 0.3f), Quaternion.identity, "GUEST REGISTER",
            "Room   Name                      Checked in\n214    ▓▓▓▓▓▓ ▓▓▓▓            31.10.2006\n201    ▒▒▒▒▒ ▒▒▒▒▒▒▒          31.10.2011\n209    (burnt)                   31.10.2015\n212    ▓▓▓▓▓ ▓▓▓▓             31.10.2019\n216    ▒▒▒▒▒▒▒ ▒▒▒            31.10.2023\n\n—      ____________________  {DATE}\n              (the ink is still wet)\n\nEvery one of them checked in on 31 October.\nNone of them checked out.", true);
        Box("Key board", g, W(20.1f, 11.2f, 0, 1.6f), new Vector3(0.04f, 0.6f, 0.9f), M("Wood_Furniture"), LProp, true);
        Note(g, "Key tag", W(20.15f, 11.4f, 0, 1.5f), new Vector3(0.02f, 0.07f, 0.05f), Quaternion.identity, "AN EMPTY HOOK", "BASEMENT\n\n(the hook is empty. Scratched into the wood beneath it:\n\"214 — behind the dresser\")", true, "Brass_Fitting");
        // seating, planters, a toppled luggage trolley
        F(g, "Sofa", W(25.2f, 21.5f, 0, 0.4f), new Vector3(0.9f, 0.8f, 2.2f), "Fabric_Cover");
        F(g, "Sofa", W(27.8f, 21.5f, 0, 0.4f), new Vector3(0.9f, 0.8f, 2.2f), "Fabric_Cover");
        F(g, "Coffee table", W(26.5f, 21.5f, 0, 0.25f), new Vector3(0.7f, 0.5f, 1.4f), "Wood_Furniture");
        F(g, "Armchair (overturned)", W(33.5f, 21.0f, 0, 0.35f), new Vector3(0.9f, 0.7f, 0.9f), "Fabric_Cover", 0f).transform.rotation = Quaternion.Euler(80, 30, 0);
        foreach (var px in new[] { 26.6f, 33.4f })
        {
            F(g, "Planter", W(px, 8.6f, 0, 0.3f), new Vector3(0.6f, 0.6f, 0.6f), "Brass_Fitting");
            F(g, "Dead plant", W(px, 8.6f, 0, 1.0f), new Vector3(0.7f, 0.9f, 0.7f), "Plant_Leaf", 0f, false);
        }
        F(g, "Luggage trolley (fallen)", W(33.8f, 11.4f, 0, 0.3f), new Vector3(1.2f, 0.5f, 0.8f), "Brass_Fitting", 28f);
        GlassPatch(g, 34.4f, 9.2f, 37.2f, 10.8f, 0);           // a smashed display case by the doors (clear of the arch and the planter)
        F(g, "Display case (smashed)", W(35.6f, 9.0f, 0, 0.5f), new Vector3(1.6f, 1.0f, 0.5f), "Wood_Furniture");
        Emit(W(22f, 13f, 0, 2.5f), "amb_clock_tick", 0.3f);
        Emit(W(30f, 16f, 0, 2f), "amb_room_tone_low", 0.25f);
        Emit(W(30f, 16f, 0, 6f), "amb_rain_roof", 0.35f);
        Emit(W(36f, 23f, 0, 3f), "amb_settle", 0.3f, false, new Vector2(8f, 20f));

        // ---------------- vestibule: the doors open on white static
        g = Group("Entrance Vestibule");
        Sconce(g, W(26.1f, 5f, 0, 2.3f), Lamp.Pattern.Dying, true, 5f, 0.6f);
        F(g, "Bench", W(33.4f, 5f, 0, 0.25f), new Vector3(0.5f, 0.5f, 1.8f), "Wood_Furniture");
        F(g, "Umbrella stand", W(26.5f, 2.6f, 0, 0.35f), new Vector3(0.3f, 0.7f, 0.3f), "Brass_Fitting");
        WallNote(g, "Notice", W(26.08f, 6.4f, 0, 1.5f), Vector3.right, "NOTICE ON THE DOOR", "THE GRAND MERIDIAN HOTEL\nis closed from 1 November 2003.\n\nWe thank our guests.", false);

        // ---------------- luggage room: a cage to hide in, bottles
        g = Group("Luggage Room");
        Bulb(g, W(17f, 6f, 0, 3.0f), 6f, 0.7f, Lamp.Pattern.Flicker);
        MakeHide(g, "Luggage cage", W(15.6f, 3.6f, 0, 1.05f), new Vector3(2.4f, 2.1f, 2.0f), 0f, "cage", new Vector3(0f, 0.55f, 0.6f), M("Metal_Lift"), 0f);
        for (int i = 0; i < 5; i++) F(g, "Suitcase", W(18.8f, 3.0f + i * 0.9f, 0, 0.3f + (i % 2) * 0.15f), new Vector3(0.6f, 0.6f + (i % 2) * 0.3f, 0.25f), i % 2 == 0 ? "Fabric_Cover" : "Plastic_Device", Rnd(-10, 10));
        F(g, "Shelf", W(19.7f, 7.4f, 0, 0.9f), new Vector3(0.5f, 1.8f, 2.0f), "Wood_Furniture");
        F(g, "Shelf board", W(19.29f, 7.4f, 0, 0.975f), new Vector3(0.32f, 0.03f, 0.7f), "Wood_Furniture");
        Bottles(g, W(19.36f, 7.4f, 0, 1.0f), 3);

        // ---------------- front office: START. The letter. The switchboard rings guest-room phones.
        g = Group("Front Office");
        var fo = R("Front Office");
        CeilingLight(g, fo, true, W(19.92f, 15.0f, 0, 1.35f), Vector3.left, 1.0f, Lamp.Pattern.Flicker);
        F(g, "Desk", W(17.6f, 14.0f, 0, 0.38f), new Vector3(0.8f, 0.76f, 1.6f), "Wood_Furniture");
        MakeLamp(g, "Desk lamp", W(17.6f, 13.4f, 0, 1.05f), 3.5f, 0.6f, Warm, true, true, new Vector3(0.2f, 0.12f, 0.2f), Lamp.Pattern.Steady);
        var letter = DeskNote(g, "The letter", W(17.55f, 14.1f, 0, 0.775f), "A LETTER, LEFT ON THE DESK", Letter);
        letter.isLetter = true;
        F(g, "Chair", W(16.9f, 14.0f, 0, 0.45f), new Vector3(0.5f, 0.9f, 0.5f), "Fabric_Cover");
        F(g, "Filing cabinet", W(14.4f, 16.8f, 0, 0.65f), new Vector3(0.6f, 1.3f, 0.5f), "Metal_Device");
        F(g, "Filing cabinet (open)", W(14.4f, 16.1f, 0, 0.65f), new Vector3(0.6f, 1.3f, 0.5f), "Metal_Device");
        // the switchboard on the north wall
        F(g, "Switchboard table", W(16.8f, 17.6f, 0, 0.4f), new Vector3(1.8f, 0.8f, 0.6f), "Wood_Furniture");
        F(g, "Switchboard", W(16.8f, 17.75f, 0, 1.05f), new Vector3(1.4f, 0.5f, 0.3f), "Plastic_Device");
        string[] rooms = { "201", "203", "205", "211", "213", "214", "216", "218", "219", "Gallery" };
        for (int i = 0; i < rooms.Length; i++)
        {
            var pos = W(16.25f + (i % 5) * 0.27f, 17.58f, 0, 1.13f - (i / 5) * 0.18f);
            var b = Box($"Button {rooms[i]}", g, pos, new Vector3(0.06f, 0.06f, 0.04f), M("Plastic_Beige"), LInteract, true, null, false, false);
            b.GetComponent<BoxCollider>().isTrigger = true;
            b.GetComponent<BoxCollider>().size = new Vector3(0.12f, 0.1f, 0.08f);
            var lampR = Box("Lamp", b.transform, pos + new Vector3(0, 0.05f, -0.01f), new Vector3(0.025f, 0.025f, 0.02f), M("Lamp_Red"), LProp, false, null, false, false).GetComponent<Renderer>();
            var sbb = b.AddComponent<SwitchboardButton>();
            sbb.room = rooms[i];
            sbb.lamp = lampR;
        }
        Note(g, "Switchboard card", W(17.7f, 17.45f, 0, 0.82f), new Vector3(0.18f, 0.01f, 0.12f), Quaternion.identity, "SWITCHBOARD",
            "Press a button to ring that room's telephone.\n\n201  203  205  211  213\n214  216  218  219  GALLERY (house phone)\n\n(in pencil, newer:) A ringing phone brings it running.\nRing a room far from where you want to go.", true);
        Box("Coat hook", g, W(14.05f, 13.0f, 0, 1.7f), new Vector3(0.05f, 0.05f, 0.25f), M("Brass_Fitting"), LProp, false);

        // ---------------- concierge: a desk radio (lure into the lobby), a desk to hide under, back door
        g = Group("Concierge");
        Bulb(g, W(17f, 22f, 0, 3.0f), 6f, 0.6f, Lamp.Pattern.Dying);
        MakeHide(g, "Concierge desk", W(17.4f, 22.0f, 0, 0.38f), new Vector3(1.0f, 0.76f, 1.8f), 0f, "desk", new Vector3(-0.2f, -0.15f, 0f), M("Wood_Furniture"), 90f);
        Device(g, "desk radio", W(17.4f, 21.4f, 0, 0.86f), new Vector3(0.32f, 0.18f, 0.14f), "Plastic_Device", new[] { "radio_station_0", "radio_station_2" }, 62f, 0.45f, 40f,
            desc: "Switch it on: it plays for 40 s. It will come to the lobby side of the hotel to find it.");
        F(g, "Map stand", W(19.6f, 25.4f, 0, 0.8f), new Vector3(0.5f, 1.6f, 0.05f), "Wood_Furniture");
        F(g, "Luggage", W(15f, 19f, 0, 0.3f), new Vector3(0.6f, 0.6f, 0.25f), "Fabric_Cover", 20f);

        // ---------------- the west staff passage: the quiet back route
        g = Group("West Staff Passage");
        var pats = new[] { Lamp.Pattern.Steady, Lamp.Pattern.Flicker, Lamp.Pattern.Dying, Lamp.Pattern.Buzz, Lamp.Pattern.Steady, Lamp.Pattern.Strobe };
        for (int i = 0; i < 6; i++) Tube(g, W(13f, 5f + i * 6f, 0, 2.9f), 6f, pats[i], true, 0.8f, false);
        F(g, "Mop bucket", W(13.5f, 9.2f, 0, 0.25f), new Vector3(0.4f, 0.5f, 0.4f), "Plastic_Beige");
        F(g, "Laundry bags", W(12.5f, 26.8f, 0, 0.3f), new Vector3(0.7f, 0.6f, 0.9f), "Fabric_Bed");
        Emit(W(13f, 20f, 0, 2.5f), "light_buzz", 0.4f);

        // ---------------- staff lockers: lockers to hide in, the portable radio
        g = Group("Staff Lockers");
        Tube(g, W(9f, 6f, 0, 2.9f), 6f, Lamp.Pattern.Buzz, true, 0.8f);
        for (int i = 0; i < 6; i++)
        {
            var c = W(6.35f, 3.0f + i * 0.85f, 0, 0.95f);
            if (i == 1 || i == 4) MakeHide(g, "Locker", c, new Vector3(0.8f, 1.9f, 0.55f), 90f, "locker", new Vector3(0, 0.55f, 0.05f), M("Metal_Lift"));
            else F(g, "Locker", c, new Vector3(0.55f, 1.9f, 0.8f), "Metal_Lift");
        }
        F(g, "Bench", W(8.4f, 5.5f, 0, 0.22f), new Vector3(0.4f, 0.44f, 3.0f), "Wood_Furniture");
        RadioPickup(g, W(8.4f, 4.6f, 0, 0.44f));
        WallNote(g, "Rota", W(11.92f, 8.6f, 0, 1.5f), Vector3.left, "NIGHT ROTA, 31.10.2003", "Porter ........ ▓▓▓▓▓\nSecurity ...... ▒▒▒▒▒▒\nKitchen ....... (burnt)\n\nNobody signed out.", false);

        // ---------------- security office: CCTV of all three floors. Look before you move.
        g = Group("Security Office");
        CeilingLight(g, R("Security Office"), true, W(11.92f, 15.0f, 0, 1.35f), Vector3.left, 1.0f, Lamp.Pattern.Buzz);
        string[] cams = { "CAM 1", "CAM 2", "CAM 3", "CAM 4", "CAM 6", "CAM 5" };
        string[] camNames = { "LOBBY", "GUEST FL. N GALLERY", "GUEST FL. WEST", "GUEST FL. EAST", "RESTAURANT", "BASEMENT" };
        for (int i = 0; i < 6; i++)
        {
            float z = 12.2f + (i % 3) * 0.6f, y = 1.95f - (i / 3) * 0.48f;
            F(g, "Monitor", W(6.3f, z, 0, y), new Vector3(0.42f, 0.44f, 0.54f), "Plastic_Device");
            var s = Quad($"Screen {cams[i]}", g, W(6.52f, z, 0, y), new Vector2(0.44f, 0.33f), Quaternion.LookRotation(Vector3.left), M("Screen"));
            if (i % 3 == 0) F(g, "Monitor shelf", W(6.33f, 12.8f, 0, y - 0.235f), new Vector3(0.5f, 0.03f, 1.9f), "Metal_Device");
            var ms = s.AddComponent<MonitorScreen>();
            ms.screen = s.GetComponent<Renderer>();
            refs.screens.Add((ms, cams[i]));
            Box("Label", g, W(6.66f, z, 0, y - 0.25f), new Vector3(0.01f, 0.04f, 0.3f), M("Paper_Note"), LProp, false);
        }
        F(g, "Monitor desk", W(7.0f, 13.0f, 0, 0.38f), new Vector3(0.8f, 0.76f, 2.4f), "Wood_Furniture");
        F(g, "Chair", W(7.9f, 13.2f, 0, 0.45f), new Vector3(0.5f, 0.9f, 0.5f), "Fabric_Cover");
        DeskNote(g, "Security notes", W(7.0f, 14.0f, 0, 0.775f), "NOTES BY THE MONITORS", SecurityNote);
        Note(g, "Camera list", W(6.66f, 11.6f, 0, 1.5f), new Vector3(0.01f, 0.28f, 0.2f), Quaternion.identity, "CAMERAS", string.Join("\n", cams.Select((c, i) => c + "  " + camNames[i])), false);
        F(g, "Cabinet", W(11.6f, 10.6f, 0, 0.9f), new Vector3(0.6f, 1.8f, 0.9f), "Metal_Device");

        // ---------------- manager's office: the hotel's last night, in the manager's files
        g = Group("Manager's Office");
        MakeLamp(g, "Desk lamp", W(9.6f, 23.4f, 0, 1.05f), 4f, 0.6f, Warm, true, false, null, Lamp.Pattern.Candle);
        MakeHide(g, "Manager's desk", W(9.0f, 22.6f, 0, 0.38f), new Vector3(1.8f, 0.76f, 0.9f), 0f, "desk", new Vector3(0f, -0.15f, -0.1f), M("Wood_Furniture"), 180f);
        DeskNote(g, "Incident log", W(8.6f, 22.6f, 0, 0.775f), "THE NIGHT MANAGER'S LOG, 31 OCTOBER 2003", ManagerLog, false);
        refs.safe = ManagerSafe(g);
        F(g, "Bookcase", W(6.3f, 21f, 0, 1.0f), new Vector3(0.5f, 2.0f, 2.4f), "Wood_Furniture");
        F(g, "Portrait", W(6.1f, 24.6f, 0, 1.7f), new Vector3(0.04f, 0.7f, 0.55f), "Wood_Furniture", 0f, false);
        Quad("Face (worn)", g, W(6.13f, 24.6f, 0, 1.72f), new Vector2(0.3f, 0.4f), Quaternion.LookRotation(Vector3.left), M("Blob_Face"));

        // ---------------- mail room: the letters that came for the people on the tape
        g = Group("Mail Room");
        Bulb(g, W(9f, 29f, 0, 2.8f), 5.5f, 0.6f, Lamp.Pattern.Dying);
        for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++) Box("Pigeonhole", g, W(6.15f, 27.2f + i * 0.5f, 0, 1.2f + j * 0.35f), new Vector3(0.28f, 0.3f, 0.46f), M("Wood_Furniture"), LProp, false);
        F(g, "Mail sacks", W(10.8f, 30.8f, 0, 0.3f), new Vector3(1.0f, 0.6f, 0.9f), "Fabric_Bed");
        DeskNote(g, "Returned letters", W(10.6f, 27.4f, 0, 0.9f), "A BUNDLE OF LETTERS, ALL RETURNED", MailNote);
        F(g, "Sorting table", W(10.6f, 27.6f, 0, 0.42f), new Vector3(1.4f, 0.84f, 0.8f), "Wood_Furniture");

        // ---------------- the staff stair
        g = Group("Staff Stair");
        Emergency(g, W(6.2f, 38.5f, 0, 3.6f));
        Emergency(g, W(11.8f, 33f, 1, 2.4f));
        Box("Fire hose", g, W(11.85f, 37.8f, 0, 1.3f), new Vector3(0.12f, 0.6f, 0.5f), M("Lamp_Red"), LProp, true);
        Emit(W(9f, 36f, 0, 3f), "amb_wind_shaft", 0.3f);

        // ---------------- chapel: the organ, the loudest lure. Pews to hide behind.
        g = Group("Chapel");
        for (int i = 0; i < 4; i++)
        {
            F(g, "Candle stand", W(14.5f + (i % 2) * 9f, 30f + (i / 2) * 5f, 0, 0.755f), new Vector3(0.05f, 1.51f, 0.05f), "Brass_Fitting");
            F(g, "Candle stand foot", W(14.5f + (i % 2) * 9f, 30f + (i / 2) * 5f, 0, 0.015f), new Vector3(0.26f, 0.03f, 0.26f), "Brass_Fitting", 0f, false);
        }
        for (int i = 0; i < 4; i++) MakeLamp(g, "Candle", W(14.5f + (i % 2) * 9f, 30f + (i / 2) * 5f, 0, 1.6f), 4f, 0.35f, new Color(1f, 0.74f, 0.48f), true, false, new Vector3(0.05f, 0.18f, 0.05f), Lamp.Pattern.Candle);
        // the organ beside the north door (x 17.55 to 18.45), not across it
        Device(g, "organ", W(20.6f, 37.3f, 0, 0.8f), new Vector3(2.4f, 1.6f, 0.9f), "Wood_Furniture", new[] { "piano_tune_organ" }, 82f, 0.8f, 25f,
            desc: "The chapel organ: the loudest lure in the hotel, heard on every floor near the stairs. It plays for 25 s.", startClip: "light_tick");
        for (int row = 0; row < 4; row++)
            for (int side = 0; side < 2; side++)
            {
                var c = W(side == 0 ? 16.5f : 21.8f, 28.6f + row * 1.8f, 0, 0.42f);      // the west pews clear of the passage door
                if (row == 1 && side == 0) MakeHide(g, "Pew", c, new Vector3(3.2f, 0.84f, 0.6f), 0f, "pew", new Vector3(0f, -0.2f, -0.55f), M("Wood_Furniture"), 0f, false);
                else if (row == 3 && side == 1) MakeHide(g, "Pew", c, new Vector3(3.2f, 0.84f, 0.6f), 0f, "pew", new Vector3(0f, -0.2f, -0.55f), M("Wood_Furniture"), 0f, false);
                else F(g, "Pew", c, new Vector3(3.2f, 0.84f, 0.6f), "Wood_Furniture");
            }
        F(g, "Altar", W(19f, 36.0f, 0, 0.5f), new Vector3(2.0f, 1.0f, 0.8f), "Fabric_Cloth");

        // ---------------- restaurant: piano, tables to hide under, the crashed chandelier (glass)
        g = Group("Restaurant");
        Sconce(g, W(24.1f, 30f, 0, 2.4f), Lamp.Pattern.Flicker);
        Sconce(g, W(41.9f, 30f, 0, 2.4f), Lamp.Pattern.Steady);
        Sconce(g, W(33f, 37.9f, 0, 2.4f), Lamp.Pattern.Dying);
        var rest = R("Restaurant");
        var hole = CeilingHole(rest);
        Box("Fallen chandelier", g, W(hole.center.x, hole.center.y, 0, 0.35f), new Vector3(1.6f, 0.7f, 1.6f), M("Brass_Fitting"), LProp, true, Quaternion.Euler(12, 30, 8));
        GlassPatch(g, hole.center.x - 2.4f, hole.center.y - 2.0f, hole.center.x + 2.4f, hole.center.y + 2.0f, 0);
        Device(g, "piano", W(39.2f, 35.6f, 0, 0.55f), new Vector3(1.6f, 1.1f, 0.7f), "Plastic_Device", new[] { "piano_tune_detuned", "piano_lounge" }, 72f, 0.6f, 25f,
            desc: "The restaurant piano: a loud lure (25 s).", startClip: "light_tick");
        var tables = new[] { (27f, 28.5f), (27f, 33.5f), (31f, 36f), (37f, 29f), (40f, 30.5f), (26.2f, 36.6f) };
        for (int i = 0; i < tables.Length; i++)
        {
            var (tx, tz) = tables[i];
            var c = W(tx, tz, 0, 0.38f);
            if (i == 1 || i == 3) MakeHide(g, "Table (long cloth)", c, new Vector3(1.6f, 0.76f, 1.0f), 0f, "table", new Vector3(0f, -0.15f, 0f), M("Fabric_Cloth"), Rnd(0, 360), false);
            else F(g, "Table", c, new Vector3(1.4f, 0.76f, 0.9f), "Fabric_Cloth");
            for (int k = 0; k < 2; k++) F(g, "Chair", W(tx + (k == 0 ? -1.18f : 1.18f), tz, 0, 0.45f), new Vector3(0.45f, 0.9f, 0.45f), "Wood_Furniture", Rnd(-40, 40));
        }
        F(g, "Chair (overturned)", W(34.5f, 27.6f, 0, 0.25f), new Vector3(0.45f, 0.45f, 0.9f), "Wood_Furniture", 60f);

        // ---------------- dish pit, cold room, kitchen
        g = Group("Dish Pit");
        Tube(g, W(45f, 28f, 0, 3.1f), 5f, Lamp.Pattern.Buzz, true, 0.7f);
        F(g, "Sink", W(46.75f, 29.6f, 0, 0.45f), new Vector3(2.4f, 0.9f, 0.6f), "Metal_Lift");
        F(g, "Plate shelf", W(43.6f, 26.275f, 0, 0.885f), new Vector3(1.8f, 0.03f, 0.4f), "Metal_Lift");
        for (int i = 0; i < 4; i++) F(g, "Plate stack", W(43.0f + i * 0.4f, 26.3f, 0, 1.0f + (i % 2) * 0.08f), new Vector3(0.28f, 0.2f + (i % 2) * 0.16f, 0.28f), "Plastic_Beige", 0f, false);
        Emit(W(46.75f, 29.6f, 0, 1.2f), "amb_faucet", 0.3f);

        g = Group("Cold Room");
        MakeLamp(g, "Cold light", W(51f, 28f, 0, 2.4f), 4f, 0.4f, new Color(0.9f, 0.96f, 1f), true, false, null, Lamp.Pattern.Buzz);
        // at the back of the room: it used to stand 0.3 m in front of the only door (north wall), shutting the room off.
        // You hide among the carcasses and watch the door.
        MakeHide(g, "Meat rack", W(51f, 27.0f, 0, 0.95f), new Vector3(2.4f, 1.9f, 0.6f), 0f, "cold", new Vector3(0f, 0.55f, 0f), M("Metal_Lift"), 0f);
        Emit(W(51f, 28f, 0, 2f), "amb_fridge", 0.4f);

        g = Group("Kitchen");
        Tube(g, W(45f, 34f, 0, 3.1f), 7f, Lamp.Pattern.Flicker, true, 0.9f);
        Tube(g, W(51f, 34f, 0, 3.1f), 7f, Lamp.Pattern.Dying, true, 0.9f);
        F(g, "Steel island", W(48f, 34f, 0, 0.45f), new Vector3(6f, 0.9f, 1.2f), "Metal_Lift");
        F(g, "Stove", W(53.4f, 32f, 0, 0.45f), new Vector3(1.0f, 0.9f, 2.4f), "Metal_Device");
        F(g, "Shelf", W(42.4f, 32.0f, 0, 0.9f), new Vector3(0.6f, 1.8f, 2.0f), "Metal_Device");
        for (int i = 0; i < 5; i++) Box("Hanging pan", g, W(46f + i * 0.8f, 34f, 0, 2.2f), new Vector3(0.3f, 0.03f, 0.3f), M("Metal_Device"), LProp, false);
        F(g, "Shelf board", W(42.86f, 32.0f, 0, 0.975f), new Vector3(0.32f, 0.03f, 0.7f), "Metal_Device");
        Bottles(g, W(42.93f, 32.0f, 0, 1.0f), 3);
        Emit(W(48f, 34f, 0, 2f), "amb_fridge", 0.35f);

        // ---------------- north staff corridor and the locked basement door
        g = Group("North Staff Corridor");
        for (int i = 0; i < 6; i++) Tube(g, W(15f + i * 7.5f, 39f, 0, 2.9f), 6f, i == 2 ? Lamp.Pattern.Strobe : i % 2 == 0 ? Lamp.Pattern.Flicker : Lamp.Pattern.Steady, true, 0.8f);
        Box("Sign BASEMENT", g, W(19.2f, 39.93f, 0, 2.25f), new Vector3(0.6f, 0.15f, 0.02f), M("Paper_Note"), LProp, false);
        WallNote(g, "Basement door sign", W(20.2f, 39.92f, 0, 1.5f), Vector3.back, "ON THE BASEMENT DOOR", "BASEMENT — STAFF ONLY\n\nKey at reception.\n\n(the reception hook is empty. Under it, in marker: 214)", false);
        Emit(W(25f, 39f, 0, 2.5f), "amb_pipe_knock", 0.3f, false, new Vector2(8f, 20f));

        // ---------------- east service hall, gift shop, bar, telephones, bar storage
        g = Group("East Service Hall");
        for (int i = 0; i < 6; i++) Tube(g, W(55f, 5f + i * 6.5f, 0, 2.9f), 5f, i % 3 == 1 ? Lamp.Pattern.Dying : Lamp.Pattern.Flicker, true, 0.7f, false);
        F(g, "Boxes", W(55.3f, 12f, 0, 0.4f), new Vector3(0.8f, 0.8f, 1.4f), "Paper_Note");

        g = Group("Gift Shop");
        MakeLamp(g, "Display fridge", W(53.4f, 4f, 0, 1.2f), 4f, 0.5f, new Color(0.8f, 0.95f, 1f), true, false, new Vector3(0.1f, 1.6f, 0.8f), Lamp.Pattern.Vending);
        F(g, "Counter", W(47f, 8.6f, 0, 0.5f), new Vector3(3.0f, 1.0f, 0.7f), "Wood_Furniture");
        Device(g, "music box", W(47.6f, 8.6f, 0, 1.07f), new Vector3(0.22f, 0.14f, 0.16f), "Wood_Furniture", new[] { "musicbox_lullaby" }, 54f, 0.4f, 30f,
            desc: "A music box: a quiet lure (54 dB, 30 s). It only reaches nearby rooms.", startClip: "musicbox_wind");
        for (int i = 0; i < 3; i++) MakeHide(g, "Shelf", W(42.4f + i * 2.6f, 4.4f, 0, 0.9f), new Vector3(1.8f, 1.8f, 0.5f), 0f, "shelf", new Vector3(0f, -0.1f, 0.7f), M("Wood_Furniture"), 180f);
        GlassPatch(g, 49.5f, 5.0f, 52.5f, 6.6f, 0);

        g = Group("Blue Note Bar");
        Sconce(g, W(40.1f, 13f, 0, 2.3f), Lamp.Pattern.Flicker);
        Sconce(g, W(40.1f, 23f, 0, 2.3f), Lamp.Pattern.Dying);
        var jglow = MakeLamp(g, "Jukebox glow", W(49.4f, 24.6f, 0, 1.4f), 3f, 0.3f, new Color(1f, 0.78f, 0.62f), false, false, new Vector3(0.6f, 0.3f, 0.1f), Lamp.Pattern.Tv);
        var juke = Device(g, "jukebox", W(49.4f, 25.3f, 0, 0.8f), new Vector3(1.0f, 1.6f, 0.6f), "Plastic_Device", new[] { "juke_swing", "juke_doowop", "juke_ballad" }, 68f, 0.6f, 45f,
            desc: "The jukebox: a loud lure (45 s).", startClip: "juke_select");
        juke.glow = jglow;
        F(g, "Bar counter", W(47.0f, 16f, 0, 0.55f), new Vector3(0.7f, 1.1f, 8f), "Wood_Furniture");
        // the back shelf stops short of the door into the telephone room (z 12.55 to 13.45): it used to cover half of it
        F(g, "Back shelf", W(49.7f, 16.5f, 0, 1.1f), new Vector3(0.5f, 2.2f, 5f), "Wood_Furniture");
        MakeLamp(g, "Neon sign", W(49.4f, 17.5f, 0, 2.35f), 2.5f, 0.12f, new Color(1f, 0.45f, 0.45f), true, false, new Vector3(0.03f, 0.22f, 1.1f), Lamp.Pattern.Buzz);
        GlassPatch(g, 47.5f, 12.2f, 49.4f, 19.8f, 0);
        F(g, "Shelf board", W(49.29f, 14.5f, 0, 1.175f), new Vector3(0.32f, 0.03f, 0.7f), "Wood_Furniture");
        Bottles(g, W(49.36f, 14.5f, 0, 1.2f), 3);
        for (int i = 0; i < 4; i++) F(g, "Stool", W(46.2f, 13.5f + i * 1.6f, 0, 0.38f), new Vector3(0.35f, 0.76f, 0.35f), "Brass_Fitting");
        F(g, "Booth", W(41.0f, 12.5f, 0, 0.5f), new Vector3(1.6f, 1.0f, 2.0f), "Fabric_Cover");
        F(g, "Booth", W(41.0f, 23.5f, 0, 0.5f), new Vector3(1.6f, 1.0f, 2.0f), "Fabric_Cover");

        g = Group("Telephone Room");
        Bulb(g, W(52f, 14f, 0, 2.8f), 5f, 0.6f, Lamp.Pattern.Buzz);
        // on the east wall either side of the door to the hall (z 13.55 to 14.45): the middle one used to stand across it
        var payZ = new[] { 11.0f, 12.6f, 15.8f };
        for (int i = 0; i < 3; i++)
        {
            F(g, "Payphone", W(53.75f, payZ[i], 0, 1.3f), new Vector3(0.3f, 0.7f, 0.5f), "Metal_Device");
            MakePhone(g, W(53.55f, payZ[i], 0, 1.05f), 270f, "payphone " + (i + 1), false);
        }
        WallNote(g, "Payphone card", W(50.08f, 14f, 0, 1.5f), Vector3.right, "SCRATCHED INTO THE WALL", "dial a room upstairs.\nit goes to the ringing.\nthen you go the other way.", true);

        g = Group("Bar Storage");
        Bulb(g, W(52f, 22f, 0, 2.8f), 5f, 0.5f, Lamp.Pattern.Flicker);
        F(g, "Crate", W(50.6f, 19.0f, 0, 0.3f), new Vector3(0.7f, 0.6f, 0.7f), "Wood_Furniture");
        F(g, "Crate", W(51.4f, 19.0f, 0, 0.3f), new Vector3(0.7f, 0.6f, 0.7f), "Wood_Furniture");
        F(g, "Crate", W(50.6f, 19.0f, 0, 0.9f), new Vector3(0.7f, 0.6f, 0.7f), "Wood_Furniture", 6f);
        F(g, "Crate", W(50.6f, 19.8f, 0, 0.3f), new Vector3(0.7f, 0.6f, 0.7f), "Wood_Furniture", -4f);
        Bottles(g, W(51.4f, 19.0f, 0, 0.61f), 3);
        MakeHide(g, "Shelving", W(53.4f, 23.7f, 0, 0.9f), new Vector3(0.6f, 1.8f, 2.4f), 0f, "shelf", new Vector3(-0.7f, -0.1f, 0f), M("Metal_Device"), 270f);

        // ---------------- north strip: chute room, basement stairhead, lift lobby, loading dock
        g = Group("Laundry Chute Room");
        Bulb(g, W(15f, 42f, 0, 2.8f), 5f, 0.5f, Lamp.Pattern.Dying);
        var hatch = Box("Chute hatch", g, W(15f, 43.85f, 0, 1.0f), new Vector3(0.7f, 0.7f, 0.08f), M("Metal_Device"), LInteract, true, null, false, false);
        var hc = hatch.GetComponent<BoxCollider>(); hc.isTrigger = true; hc.size = new Vector3(0.9f, 0.9f, 0.4f);
        hatch.AddComponent<ChuteHatch>().bottom = W(13.5f, 42.5f, -1, 0.5f);
        F(g, "Laundry bin", W(13f, 41f, 0, 0.45f), new Vector3(0.9f, 0.9f, 0.7f), "Fabric_Bed");
        Emit(W(15f, 43.6f, 0, 1.2f), "amb_chute_wind", 0.35f);

        g = Group("Basement Stairhead");
        Emergency(g, W(18.2f, 42f, 0, 2.6f));
        WallNote(g, "Basement note", W(18.08f, 43.2f, 0, 1.5f), Vector3.right, "PINNED BY THE BASEMENT STAIRS", BasementNote);

        g = Group("Service Lift Lobby");
        Tube(g, W(44f, 42f, 0, 2.9f), 5f, Lamp.Pattern.Strobe, true, 0.7f);
        F(g, "Chair", W(41.0f, 43.3f, 0, 0.45f), new Vector3(0.5f, 0.9f, 0.5f), "Fabric_Cover", 20f);
        F(g, "Dead plant", W(43.0f, 43.5f, 0, 0.6f), new Vector3(0.5f, 1.2f, 0.5f), "Plant_Leaf", 0f, false);
        Emit(W(47f, 44.6f, 0, 1.5f), "amb_wind_shaft", 0.4f);

        g = Group("Loading Dock");
        Bulb(g, W(52f, 42f, 0, 3.2f), 6f, 0.5f, Lamp.Pattern.Dying);
        for (int i = 0; i < 3; i++) F(g, "Pallet", W(49.5f + i * 1.6f, 41.6f, 0, 0.08f + (i % 2) * 0.16f), new Vector3(1.2f, 0.15f + (i % 2) * 0.16f, 1.0f), "Wood_Furniture", Rnd(-15, 15));
        Emit(W(52f, 44f, 0, 1.5f), "amb_wind", 0.5f);
    }
}
