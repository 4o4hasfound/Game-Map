using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Twenty years of nobody: grime where the walls meet the floor and the ceiling, water running down from leaks,
// mould, soot over the burnt rooms' doors, wallpaper peeling off in strips, ceiling tiles missing or hanging by a
// corner, live cables dangling (some arcing), paper and fallen tiles on the floor. And the light in the air: shafts
// under the lamps with dust drifting through them, and shadows that jump when a light flickers.
public static partial class TapeBuilder
{
    static Transform decayRoot;

    // a quad mesh with UVs repeating along its length (decals along walls)
    static GameObject DecalQuad(string name, Transform parent, Vector3 center, Vector2 size, Quaternion rot, Material mat, float uRepeat = 1f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rot);
        go.layer = LProp;
        var m = new Mesh { name = "Decal" };
        float hx = size.x / 2, hy = size.y / 2;
        m.vertices = new[] { new Vector3(-hx, -hy, 0), new Vector3(hx, -hy, 0), new Vector3(hx, hy, 0), new Vector3(-hx, hy, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(uRepeat, 0), new Vector2(uRepeat, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
        m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static float DecayAmount(LevelLayout.Room r) => r.decay == "" ? 0.35f : r.decay == "light" ? 0.6f : 1f;

    static void DecayPass()
    {
        decayRoot = new GameObject("Decay").transform;
        decayRoot.SetParent(levelRoot, false);
        rng = new System.Random(31);
        WallDecay();
        foreach (var room in LevelLayout.Rooms) if (room.floor != 2 && room.kind != "outdoor") { CeilingDecay(room); FloorClutter(room); }
        LightInTheAir();
    }

    // ------------------------------------------------------------------ walls
    static void WallDecay()
    {
        var gb = M("Grime_Bottom"); var gt = M("Grime_Top"); var leak = M("Stain_Leak"); var mold = M("Stain_Mold"); var soot = M("Stain_Soot");
        foreach (var w in wallRuns)
        {
            var room = w.room;
            if (room.floor == 2) continue;          // your bedroom is clean
            float len = Vector3.Distance(w.a, w.b);
            var dir = (w.b - w.a).normalized;
            var rot = Quaternion.LookRotation(-w.inward);     // decal quads face into the room
            var midB = (w.a + w.b) / 2;
            float dec = DecayAmount(room);
            bool basement = room.floor == -1;
            // baseboard (not in the basement or on stairs)
            if (!basement && room.kind != "stair")
                Box("Baseboard", decayRoot, midB + Vector3.up * 0.06f + w.inward * 0.012f, Mathf.Abs(dir.x) > 0.5f ? new Vector3(len, 0.12f, 0.024f) : new Vector3(0.024f, 0.12f, len), M("Wood_Door"), LProp, false);
            // grime creeping up from the floor and down from the ceiling
            float hb = Mathf.Lerp(0.45f, 1.1f, dec) * Rnd(0.8f, 1.2f);
            DecalQuad("Grime", decayRoot, midB + Vector3.up * hb / 2 + w.inward * 0.03f, new Vector2(len, hb), rot, gb, len / 1.7f);
            float wallH = w.ceilY - w.floorY;
            if (wallH > 1.5f && wallH < 7f)
            {
                float ht = Mathf.Lerp(0.3f, 0.75f, dec) * Rnd(0.8f, 1.2f);
                DecalQuad("Grime", decayRoot, new Vector3(midB.x, w.ceilY - ht / 2, midB.z) + w.inward * 0.025f, new Vector2(len, ht), rot, gt, len / 2.1f);
            }
            // stains along the run
            int stains = Mathf.FloorToInt(len / 3.2f * dec + Rnd(0f, 0.9f));
            for (int i = 0; i < stains; i++)
            {
                float t = Rnd(0.1f, 0.9f);
                var at = w.a + dir * (t * len);
                if (room.decay == "burnt" && Rnd(0f, 1f) < 0.7f)
                {
                    float sw = Rnd(1.2f, 2.4f), sh = Mathf.Min(wallH, Rnd(1.6f, 2.6f));
                    DecalQuad("Soot", decayRoot, at + Vector3.up * sh / 2 + w.inward * 0.02f, new Vector2(sw, sh), rot, soot);
                }
                else if ((room.decay == "flooded" || basement) && Rnd(0f, 1f) < 0.5f)
                {
                    float s = Rnd(0.6f, 1.6f);
                    DecalQuad("Mould", decayRoot, at + Vector3.up * Rnd(0.2f, 0.9f) + w.inward * 0.02f, new Vector2(s * 1.4f, s), rot, mold);
                }
                else
                {
                    float sw = Rnd(0.7f, 1.8f), sh = Mathf.Min(wallH - 0.05f, Rnd(1.2f, 2.6f));
                    DecalQuad("Water stain", decayRoot, new Vector3(at.x, w.ceilY - sh / 2 - 0.01f, at.z) + w.inward * 0.02f, new Vector2(sw, sh), rot, leak);
                }
            }
            // wallpaper peeling off in strips
            if (room.wallMat.StartsWith("Wallpaper") && wallH > 2f)
            {
                int strips = Mathf.FloorToInt(len / 4f * dec + Rnd(0f, 0.8f));
                for (int i = 0; i < strips; i++)
                {
                    var at = w.a + dir * (Rnd(0.15f, 0.85f) * len);
                    float sl = Rnd(0.5f, 1.3f), sw = Rnd(0.35f, 0.6f);
                    float top = Mathf.Min(w.ceilY, w.floorY + 2.6f) - Rnd(0.1f, 0.6f);
                    float tilt = Rnd(12f, 35f);
                    var axis = dir;
                    var pivotPos = new Vector3(at.x, top, at.z) + w.inward * 0.012f;
                    var q = Quaternion.AngleAxis(-tilt, axis) * Quaternion.LookRotation(-w.inward);
                    var center = pivotPos + q * new Vector3(0, -sl / 2, 0);
                    if (Vector3.Dot(center - pivotPos, w.inward) < 0f)        // curl away from the wall, not into it
                    {
                        q = Quaternion.AngleAxis(tilt, axis) * Quaternion.LookRotation(-w.inward);
                        center = pivotPos + q * new Vector3(0, -sl / 2, 0);
                    }
                    Box("Peeling wallpaper", decayRoot, center, new Vector3(sw, sl, 0.004f), M(room.wallMat), LProp, false, q, false);
                    DecalQuad("Bare plaster", decayRoot, new Vector3(at.x, top - sl / 2, at.z) + w.inward * 0.018f, new Vector2(sw * 1.1f, sl), rot, gb, 0.6f);
                }
            }
        }
    }

    // ------------------------------------------------------------------ ceilings: missing and hanging tiles, live cables
    static void CeilingDecay(LevelLayout.Room room)
    {
        if (room.twoStorey || room.noFloor) return;
        var r = room.r;
        float cy = room.Y + room.ceil;
        float dec = DecayAmount(room);
        bool tiles = room.ceilingMat() == "Ceiling_Tiles";
        float area = r.width * r.height;
        int holes = tiles ? Mathf.FloorToInt(area / 14f * dec * Rnd(0.5f, 1.2f)) : 0;
        for (int i = 0; i < holes; i++)
        {
            var c = new Vector3(Mathf.Round(Rnd(r.xMin + 0.6f, r.xMax - 0.6f) / 0.6f) * 0.6f, cy, Mathf.Round(Rnd(r.yMin + 0.6f, r.yMax - 0.6f) / 0.6f) * 0.6f);
            if (!r.Contains(new Vector2(c.x, c.z))) continue;
            // the black void above a missing tile
            Box("Missing ceiling tile", decayRoot, c + Vector3.down * 0.005f, new Vector3(0.58f, 0.012f, 0.58f), M("Soot_Floor"), LCeiling, false);
            if (Rnd(0f, 1f) < 0.55f)
            {
                // ...the tile hanging down from one corner
                float tilt = Rnd(35f, 75f);
                var q = Quaternion.Euler(tilt, Rnd(0f, 360f), Rnd(-10f, 10f));
                var center = c + Vector3.down * 0.02f + q * new Vector3(0, 0, 0.28f) + Vector3.down * Mathf.Sin(tilt * Mathf.Deg2Rad) * 0.15f;
                Box("Hanging ceiling tile", decayRoot, center, new Vector3(0.58f, 0.018f, 0.58f), M("Ceiling_Tiles"), LProp, false, q, false);
            }
            else
            {
                // ...or lying broken on the floor below
                Box("Fallen ceiling tile", decayRoot, new Vector3(c.x + Rnd(-0.4f, 0.4f), room.Y + 0.03f, c.z + Rnd(-0.4f, 0.4f)), new Vector3(0.58f, 0.02f, 0.58f), M("Ceiling_Tiles"), LProp, false, Quaternion.Euler(Rnd(-6f, 6f), Rnd(0f, 360f), Rnd(-6f, 6f)), false);
            }
            if (Rnd(0f, 1f) < 0.5f) Cable(c + Vector3.down * 0.02f, Rnd(0.5f, 1.3f), Rnd(0f, 1f) < 0.25f);
        }
        // cables out of the collapsed hole and along decayed corridors
        if (room.decay == "collapsed")
        {
            var h = CeilingHole(room);
            for (int i = 0; i < 3; i++) Cable(new Vector3(h.center.x + Rnd(-0.6f, 0.6f), cy, h.center.y + Rnd(-0.6f, 0.6f)), Rnd(0.8f, 1.8f), i == 0);
        }
        if (room.kind == "corridor" && room.decay != "")
        {
            int n = Mathf.FloorToInt(Mathf.Max(r.width, r.height) / 9f);
            for (int i = 0; i < n; i++) Cable(new Vector3(Rnd(r.xMin + 0.4f, r.xMax - 0.4f), cy, Rnd(r.yMin + 0.4f, r.yMax - 0.4f)), Rnd(0.4f, 1.1f), Rnd(0f, 1f) < 0.3f);
        }
        // water damage on the ceiling
        if (room.decay == "flooded" || (room.floor == -1 && Rnd(0f, 1f) < 0.5f))
        {
            var c = new Vector3(Rnd(r.xMin + 1f, r.xMax - 1f), cy - 0.01f, Rnd(r.yMin + 1f, r.yMax - 1f));
            float s = Rnd(1.2f, 2.6f);
            DecalQuad("Ceiling stain", decayRoot, c, new Vector2(s, s * Rnd(0.7f, 1.2f)), Quaternion.LookRotation(Vector3.up), M("Stain_Mold"));
        }
    }

    static void Cable(Vector3 top, float length, bool live)
    {
        // a cable in two sagging segments, with a bare end
        var mid = top + new Vector3(Rnd(-0.15f, 0.15f), -length * 0.55f, Rnd(-0.15f, 0.15f));
        var end = mid + new Vector3(Rnd(-0.12f, 0.12f), -length * 0.45f, Rnd(-0.12f, 0.12f));
        void Seg(Vector3 a, Vector3 b)
        {
            var c = (a + b) / 2;
            var q = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            Box("Cable", decayRoot, c, new Vector3(0.014f, Vector3.Distance(a, b), 0.014f), M("Plastic_Device"), LProp, false, q, false);
        }
        Seg(top, mid);
        Seg(mid, end);
        if (!live) return;
        var go = new GameObject("Live cable");
        go.transform.SetParent(decayRoot, false);
        go.transform.position = end;
        var lg = new GameObject("Arc light");
        lg.transform.SetParent(go.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 6f; l.intensity = 4f; l.color = new Color(0.7f, 0.82f, 1f);
        l.shadows = LightShadows.Hard; l.enabled = false;
        var ud = lg.AddComponent<UniversalAdditionalLightData>();
        var ps = new GameObject("Sparks").AddComponent<ParticleSystem>();
        ps.transform.SetParent(go.transform, false);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.maxParticles = 80;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = M("Spark");
        pr.shadowCastingMode = ShadowCastingMode.Off;
        var sw = go.AddComponent<SparkingWire>();
        sw.flash = l;
        sw.sparks = ps;
    }

    // ------------------------------------------------------------------ floors
    static void FloorClutter(LevelLayout.Room room)
    {
        var r = room.r;
        float dec = DecayAmount(room);
        int papers = Mathf.FloorToInt(r.width * r.height / 6f * dec * Rnd(0.6f, 1.3f));
        for (int i = 0; i < papers; i++)
        {
            // drifts along the walls more than in the middle
            float x = Rnd(r.xMin + 0.3f, r.xMax - 0.3f), z = Rnd(r.yMin + 0.3f, r.yMax - 0.3f);
            if (Rnd(0f, 1f) < 0.6f) { if (Rnd(0f, 1f) < 0.5f) x = Rnd(0f, 1f) < 0.5f ? r.xMin + Rnd(0.15f, 0.6f) : r.xMax - Rnd(0.15f, 0.6f); else z = Rnd(0f, 1f) < 0.5f ? r.yMin + Rnd(0.15f, 0.6f) : r.yMax - Rnd(0.15f, 0.6f); }
            Box("Paper", decayRoot, new Vector3(x, room.Y + 0.004f + i * 0.0003f, z), new Vector3(0.21f, 0.003f, 0.29f), M("Paper_Note"), LProp, false, Quaternion.Euler(Rnd(-3f, 3f), Rnd(0f, 360f), Rnd(-3f, 3f)), false);
        }
        if (room.decay == "collapsed" || room.decay == "burnt")
        {
            for (int i = 0; i < 4; i++)
                Box("Broken plank", decayRoot, new Vector3(Rnd(r.xMin + 0.5f, r.xMax - 0.5f), room.Y + 0.04f, Rnd(r.yMin + 0.5f, r.yMax - 0.5f)), new Vector3(Rnd(0.6f, 1.6f), 0.04f, 0.12f), M(room.decay == "burnt" ? "Soot_Floor" : "Wood_Furniture"), LProp, false, Quaternion.Euler(Rnd(-8f, 8f), Rnd(0f, 360f), Rnd(-4f, 4f)), false);
        }
    }

    // ------------------------------------------------------------------ exit signs: the only lights still on a battery
    static void ExitSigns(Transform root)
    {
        string[] over = { "Stair Door Ground", "Stair Door Upper", "Basement Door", "Door Basement Stair", "Door Lift Lobby", "Door Lift Vestibule", "Door Kitchen East",
                          "Door Gift Shop Hall", "Passage North End", "West Corridor North End", "East Corridor North End", "Vestibule Arch", "Door Chute Room", "Door Loading Dock" };
        // inside the big rooms the signs hang over their own exits (the only light left in there)
        string[] inside = { "Restaurant Arch", "Bar Arch", "Door Chapel Lobby", "Door Chapel North", "Ballroom Doors", "Door Ballroom North", "Door Restaurant North",
                            "Door Wine Cellar", "Door Archive", "Door Cages West", "Door Laundry", "Door Boiler", "Door Gift Shop Lobby", "Door Kitchen North" };
        foreach (var name in over.Concat(inside))
        {
            bool roomSide = inside.Contains(name);
            var o = LevelLayout.Openings.Find(x => x.name == name);
            if (o == null) continue;
            var m = o.Mid;
            var nrm = o.AlongX ? Vector3.forward : Vector3.right;
            float y = LevelLayout.FloorY(o.floor);
            var center = new Vector3(m.x, y, m.y);
            // the sign hangs on the corridor side
            var side = nrm;
            var ra = LevelLayout.RoomAt(center + nrm * 0.5f + Vector3.up);
            bool hall = ra != null && (ra.kind == "corridor" || ra.kind == "lobby" || ra.kind == "gallery");
            if (hall == roomSide) side = -nrm;
            float h = o.kind == LevelLayout.Gap.Opening ? (o.floor == 0 ? 3.1f : 2.55f) : 2.3f;
            var pos = center + Vector3.up * (h + 0.12f) + side * 0.1f;
            var lamp = MakeLamp(root, "Exit sign", pos, 1.3f, 0.05f, new Color(0.5f, 1f, 0.62f), true, false, o.AlongX ? new Vector3(0.36f, 0.14f, 0.05f) : new Vector3(0.05f, 0.14f, 0.36f), Lamp.Pattern.Buzz);
            lamp.pattern = Rnd(0f, 1f) < 0.3f ? Lamp.Pattern.Flicker : Lamp.Pattern.Buzz;
            lamp.glow = 0.7f;
        }
    }

    // ------------------------------------------------------------------ real light fittings
    // Every lamp gets a fitting that suits it (a tube in its housing, a flush dome, a fabric shade, a bare bulb on its
    // cord, a twin-head emergency unit, the chandelier's brass frame), its own colour temperature and strength, and
    // about a fifth of the tubes, sconces and bare bulbs are simply dead. Flickering ceiling lights cast shadows.
    static readonly HashSet<string> FixtureTypes = new HashSet<string> { "Tube", "Ceiling light", "Chandelier", "Emergency light", "Stage light", "Bulb", "Cold light", "Sconce", "Bedside lamp", "Desk lamp" };

    static void LightInTheAir()
    {
        var root = new GameObject("Light fittings").transform;
        root.SetParent(levelRoot, false);
        Physics.SyncTransforms();
        var lamps = Object.FindObjectsByType<Lamp>(FindObjectsSortMode.None).Where(l => l.transform.position.x < 150f && FixtureTypes.Contains(l.name)).OrderBy(l => l.transform.position.x * 1000f + l.transform.position.z).ToList();
        // dead fittings: per room, never the last working one
        int dead = 0;
        foreach (var grp in lamps.Where(l => l.name == "Tube" || l.name == "Sconce" || l.name == "Bulb").GroupBy(l => LevelLayout.RoomAt(l.transform.position + Vector3.down * 0.6f)?.name ?? "?"))
        {
            var list = grp.Where(l => l.on).ToList();
            int kill = Mathf.FloorToInt(list.Count * 0.2f + Rnd(0f, 0.6f));
            kill = Mathf.Min(kill, list.Count - 1);
            for (int i = 0; i < kill; i++)
            {
                var l = list[rng.Next(list.Count)];
                if (l.dead) continue;
                l.dead = true; l.on = false; dead++;
            }
        }
        int shadowed = 0;
        foreach (var lamp in lamps)
        {
            string n = lamp.name;
            var light = lamp.lightSource;
            var bulb = lamp.bulb;
            var p = lamp.transform.position;
            bool fluorescent = n == "Tube" || n == "Cold light";
            bool emergency = n == "Emergency light";
            // colour temperature and strength: no two the same
            Color c = lamp.emission;
            if (fluorescent) c = Color.Lerp(new Color(0.92f, 0.97f, 1f), new Color(0.93f, 1f, 0.95f), Rnd(0f, 1f));
            else if (emergency) c = Color.Lerp(new Color(1f, 0.95f, 0.87f), new Color(1f, 0.98f, 0.94f), Rnd(0f, 1f));
            else if (n == "Bulb") c = Color.Lerp(new Color(1f, 0.8f, 0.6f), new Color(1f, 0.86f, 0.68f), Rnd(0f, 1f));
            else if (n != "Stage light") c = Color.Lerp(new Color(1f, 0.83f, 0.64f), new Color(1f, 0.91f, 0.78f), Rnd(0f, 1f));
            // emergency heads are weak battery lamps; in the basement they are all there is, so a little stronger there
            float k = Rnd(0.75f, 1.2f) * (emergency ? (p.y > -0.5f ? 0.4f : 0.5f) : 1f);
            if (light) { light.color = c; light.intensity *= k; }
            lamp.emission = c;
            lamp.baseIntensity *= k;
            // flickering ceiling fittings throw shadows that jump
            bool flickery = lamp.pattern == Lamp.Pattern.Flicker || lamp.pattern == Lamp.Pattern.Dying || lamp.pattern == Lamp.Pattern.Strobe;
            // shadows cost: at most 24 of them, and the parts are switched on one at a time
            if (flickery && !lamp.dead && (n == "Tube" || n == "Ceiling light" || n == "Chandelier" || n == "Bulb") && light && shadowed < 24)
            {
                light.shadows = LightShadows.Hard;
                var ud = light.GetComponent<UniversalAdditionalLightData>();
                if (ud) { var uso = new SerializedObject(ud); var tier = uso.FindProperty("m_AdditionalLightsShadowResolutionTier"); if (tier != null) { tier.intValue = 0; uso.ApplyModifiedPropertiesWithoutUndo(); } }
                shadowed++;
            }
            if (!bulb) continue;
            var parent = bulb.transform.parent;
            var bt = bulb.transform;
            var size = bulb.bounds.size;
            switch (n)
            {
                case "Tube":
                {
                    bool alongX = size.x > size.z;
                    bool askew = Rnd(0f, 1f) < 0.12f;
                    var rot = askew ? Quaternion.Euler(alongX ? 0f : Rnd(10f, 22f), 0f, alongX ? Rnd(10f, 22f) : 0f) : Quaternion.identity;
                    Box("Tube housing", parent, p + Vector3.up * 0.045f, alongX ? new Vector3(1.32f, 0.05f, 0.2f) : new Vector3(0.2f, 0.05f, 1.32f), M("Metal_Device"), LProp, false, rot, false);
                    bt.localScale = alongX ? new Vector3(1f, 0.5f, 0.45f) : new Vector3(0.45f, 0.5f, 1f);
                    bt.rotation = rot;
                    lamp.glow = 0.55f;
                    break;
                }
                case "Cold light":
                    lamp.glow = 0.5f;
                    break;
                case "Ceiling light":
                    Box("Fitting base", parent, p + Vector3.up * 0.05f, new Vector3(0.46f, 0.03f, 0.46f), M("Brass_Fitting"), LProp, false);
                    bt.localScale = new Vector3(0.9f, 0.8f, 0.9f);
                    lamp.glow = 0.4f;
                    break;
                case "Chandelier":
                {
                    Box("Chandelier rod", parent, p + Vector3.up * 0.55f, new Vector3(0.03f, 1.0f, 0.03f), M("Brass_Fitting"), LProp, false);
                    Box("Chandelier ring", parent, p + Vector3.down * 0.05f, new Vector3(1.1f, 0.05f, 1.1f), M("Brass_Fitting"), LProp, false, Quaternion.Euler(0, 45f, 0), false);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * Mathf.PI * 2f;
                        var bp = p + new Vector3(Mathf.Cos(a) * 0.55f, 0.02f, Mathf.Sin(a) * 0.55f);
                        Box("Chandelier arm", parent, (p + bp) / 2f, new Vector3(0.55f, 0.02f, 0.02f), M("Brass_Fitting"), LProp, false, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), false);
                        Box("Dead bulb", parent, bp + Vector3.up * 0.06f, new Vector3(0.05f, 0.08f, 0.05f), M("Bulb_Dead"), LProp, false);
                    }
                    bt.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                    lamp.glow = 0.8f;
                    break;
                }
                case "Bulb":
                {
                    // a bare bulb on its cord
                    bt.localScale = new Vector3(0.45f, 0.6f, 0.45f);
                    float up = Physics.Raycast(p + Vector3.up * 0.05f, Vector3.up, out var hit, 2f, (1 << LCeiling) | (1 << LStructure), QueryTriggerInteraction.Ignore) ? hit.distance + 0.05f : 0.4f;
                    Box("Cord", parent, p + Vector3.up * (0.04f + up / 2f), new Vector3(0.01f, up, 0.01f), M("Plastic_Device"), LProp, false);
                    Box("Socket", parent, p + Vector3.up * 0.07f, new Vector3(0.04f, 0.05f, 0.04f), M("Plastic_Device"), LProp, false);
                    lamp.glow = 1.0f;
                    break;
                }
                case "Emergency light":
                {
                    // a twin-head battery unit
                    Box("Emergency unit", parent, p + Vector3.up * 0.05f, new Vector3(0.32f, 0.11f, 0.1f), M("Plastic_Beige"), LProp, false);
                    Box("Lamp head", parent, p + new Vector3(0.11f, -0.04f, 0.04f), new Vector3(0.07f, 0.06f, 0.07f), M("Plastic_Beige"), LProp, false);
                    bt.localScale = new Vector3(0.28f, 0.6f, 0.7f);
                    lamp.glow = 0.5f;
                    break;
                }
                case "Sconce":
                case "Bedside lamp":
                case "Desk lamp":
                {
                    if (n == "Desk lamp") { lamp.glow = 0.45f; break; }
                    // a fabric shade that glows softly; the bulb inside is hidden
                    var shade = Box("Lamp shade", parent, p + Vector3.up * 0.03f, n == "Sconce" ? new Vector3(0.22f, 0.17f, 0.22f) : new Vector3(0.3f, 0.22f, 0.3f), M("Lamp_Shade"), LProp, false);
                    bulb.enabled = false;
                    lamp.bulb = shade.GetComponent<Renderer>();
                    lamp.glow = 0.3f;
                    if (n == "Sconce")
                    {
                        // the bracket back to the wall
                        foreach (var dir in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                            if (Physics.Raycast(p, dir, out var wh, 0.5f, 1 << LStructure, QueryTriggerInteraction.Ignore))
                            { Box("Sconce bracket", parent, (p + wh.point) / 2f, Mathf.Abs(dir.x) > 0.5f ? new Vector3(wh.distance, 0.03f, 0.03f) : new Vector3(0.03f, 0.03f, wh.distance), M("Brass_Fitting"), LProp, false); break; }
                    }
                    break;
                }
                case "Stage light":
                    lamp.glow = 0.5f;
                    break;
            }
        }
        ExitSigns(root);
        Debug.Log($"LIGHT FITTINGS: {lamps.Count} fittings, {dead} dead, {shadowed} shadow-casting flickering lights");
    }
}
