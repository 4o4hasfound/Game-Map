using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Your house, the prologue: a warm, lived-in place at night, with the rain outside. You wake up in the bedroom; the
// living room has a fire going, the sofa, the TV and the VCR; the kitchen has the fridge (food inside), the stove,
// toaster, kettle, microwave and the fruit bowl; the hallway leads to the front door and the porch, where the parcel
// is left. Photo-scanned models from Poly Haven (CC0, Assets/Art/PolyHaven), recorded sounds from BigSoundBank (CC0).
// Nearly everything can be used (Fiddle). Things with no free model (the fridge, the fireplace, the toaster, rugs,
// the floor lamp, the VCR, cabinets) are built here from the project's photo-scanned surfaces.
public static partial class TapeBuilder
{
    static readonly Color HomeWarm = new Color(1f, 0.76f, 0.52f);
    // yaw corrections for models whose front isn't +Z after import (set from the screenshots)
    static readonly Dictionary<string, float> PHYaw = new Dictionary<string, float>();

    // ------------------------------------------------------------------ a Poly Haven model, stood on a spot
    static GameObject PH(Transform g, string id, Vector3 bottomCentre, float yaw, float expect, bool solid = false, string name = null, bool fit = false)
    {
        string path = $"Assets/Art/PolyHaven/{id}/{id}_1k.fbx";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) { Debug.LogWarning("POLYHAVEN missing " + path); return null; }
        var go = (GameObject)Object.Instantiate(asset);
        go.name = name ?? id;
        go.transform.SetParent(g, false);
        go.transform.position = Vector3.zero;
        var own = go.transform.localRotation;          // Z-up models are stood upright by their root's rotation: keep it
        go.transform.rotation = Quaternion.Euler(0f, yaw + (PHYaw.TryGetValue(id, out var dy) ? dy : 0f), 0f) * own;
        var wb = WorldBounds(go.transform);
        float native = Mathf.Max(wb.size.x, wb.size.y, wb.size.z);
        if (expect > 0f && (fit || native > expect * 2.5f || native < expect / 2.5f))
        {
            go.transform.localScale *= expect / Mathf.Max(native, 1e-4f);
            wb = WorldBounds(go.transform);
        }
        go.transform.position += new Vector3(bottomCentre.x - wb.center.x, bottomCentre.y - wb.min.y, bottomCentre.z - wb.center.z);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LProp;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.sharedMaterials = FixMaterials(r.sharedMaterials, id);
        }
        if (solid)
        {
            var lb = LocalBounds(go.transform);
            var bc = go.AddComponent<BoxCollider>(); bc.center = lb.center; bc.size = lb.size;
        }
        var pb = WorldBounds(go.transform);
        Debug.Log($"POLYHAVEN {id}: native size {native:0.000} m, placed {pb.size.x:0.00} x {pb.size.y:0.00} x {pb.size.z:0.00} at {pb.center}");
        return go;
    }

    static Bounds WorldBounds(Transform root)
    {
        bool any = false; var b = new Bounds(root.position, Vector3.zero);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var p = mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    static Bounds LocalBounds(Transform root)
    {
        bool any = false; var b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var w = mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                var p = root.InverseTransformPoint(w);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // the FBX's own materials when they came in with their textures; otherwise a URP material made from the model's
    // texture files (base colour and OpenGL normal map that best match the material's name)
    static Material[] FixMaterials(Material[] mats, string id)
    {
        var folder = $"Assets/Art/PolyHaven/{id}/textures";
        var texs = AssetDatabase.FindAssets("t:Texture2D", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        var outMats = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            bool ok = m && (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
            if (ok) { outMats[i] = m; continue; }
            string mname = m ? m.name.ToLowerInvariant() : id.ToLowerInvariant();
            string Best(string kind)
            {
                string best = null; int bestScore = -1;
                foreach (var t in texs)
                {
                    var stem = System.IO.Path.GetFileNameWithoutExtension(t).ToLowerInvariant();
                    int k = stem.IndexOf("_" + kind);
                    if (k < 0) continue;
                    var pre = stem.Substring(0, k);
                    int score = 0; while (score < pre.Length && score < mname.Length && pre[score] == mname[score]) score++;
                    if (pre == mname) score += 100;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                return best;
            }
            var diff = Best("diff"); var nor = Best("nor_gl");
            var safe = string.Concat((m ? m.name : id).Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
            var mp = $"Assets/Art/PolyHaven/{id}/{safe}_urp.mat";
            var nm = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!nm) { nm = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(nm, mp); }
            if (diff != null) nm.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diff));
            if (nor != null) { nm.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nor)); nm.EnableKeyword("_NORMALMAP"); }
            nm.SetColor("_BaseColor", Color.white);
            nm.SetFloat("_Smoothness", 0.35f);
            nm.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(nm);
            outMats[i] = nm;
        }
        return outMats;
    }

    // ------------------------------------------------------------------ making things usable
    static Fiddle Use(GameObject target, string prompt, string sound, params string[] lines)
    {
        if (!target) return null;
        var b = LocalBounds(target.transform);
        var t = new GameObject("Use");
        t.transform.SetParent(target.transform, false);
        t.layer = LInteract;
        var bc = t.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = b.center;
        var s = target.transform.lossyScale;
        float pad = 0.05f / Mathf.Max(0.01f, Mathf.Min(s.x, s.y, s.z));
        float min = 0.16f / Mathf.Max(0.01f, Mathf.Min(s.x, s.y, s.z));
        bc.size = new Vector3(Mathf.Max(b.size.x + pad, min), Mathf.Max(b.size.y + pad, min), Mathf.Max(b.size.z + pad, min));
        var f = target.AddComponent<Fiddle>();
        f.prompt = prompt; f.sound = sound; f.lines = lines;
        return f;
    }
    // steam from a kettle: soft white puffs rising, spreading and fading (Unity's own particle texture)
    static GameObject Steam(Transform parent, Vector3 at)
    {
        var go = new GameObject("Steam");
        go.transform.SetParent(parent, false);                                       // the room's group (scale 1), not the model
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(-90f, 0f, 0f));      // the cone points up
        go.transform.localScale = Vector3.one;
        go.layer = LProp;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 5f; main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1f, 1f, 1f, 0.85f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;     // its world size (under a scaled model it was 1/100th: invisible)
        main.maxParticles = 80;
        main.gravityModifier = -0.015f;
        var em = ps.emission; em.rateOverTime = 30f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.012f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 4.0f));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.07f; noise.frequency = 0.8f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SteamMaterial();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static Material SteamMaterial()
    {
        const string path = "Assets/Art/Materials/Steam.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            // a copy of URP's own particle material (its soft round particle), tinted to a thin white
            var src = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
            m = src ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.name = "Steam";
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", new Color(0.97f, 0.97f, 0.97f, 0.8f));     // white puffs you can see against a pale wall
        EditorUtility.SetDirty(m);
        return m;
    }

    static Fiddle Eat(GameObject target, string prompt, string sound, int uses, params string[] lines)
    {
        var f = Use(target, prompt, sound, lines);
        if (f) { f.consume = true; f.uses = uses; }
        return f;
    }

    // ------------------------------------------------------------------ the house's own materials (Poly Haven textures)
    // Nothing in your house shares a surface with the hotel: these come from Assets/Art/PolyHaven/Textures (CC0).
    static void HomeMaterial(string name, string tex, float tile, Color tint, float smooth)
    {
        string folder = $"Assets/Art/PolyHaven/Textures/{tex}/";
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + tex + "_diff_1k.jpg");
        if (!diff) { Debug.LogWarning("HOME texture missing: " + tex); return; }
        var nor = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + tex + "_nor_gl_1k.jpg");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string path = $"Assets/Art/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(lit) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = lit;
        m.shaderKeywords = new string[0];
        foreach (var t in new[] { "_DetailAlbedoMap", "_DetailNormalMap", "_OcclusionMap", "_MetallicGlossMap", "_SpecGlossMap", "_ParallaxMap", "_DetailMask", "_EmissionMap" })
            if (m.HasProperty(t)) m.SetTexture(t, null);
        if (m.HasProperty("_OcclusionStrength")) m.SetFloat("_OcclusionStrength", 1f);
        m.SetTexture("_BaseMap", diff);
        m.SetTextureScale("_BaseMap", Vector2.one / tile);
        m.SetColor("_BaseColor", tint);
        if (nor) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        mats[name] = m;
    }

    // a plain material from one colour texture (the envelope uses the parcel box's scanned cardboard)
    static void HomeMaterialFrom(string name, string texPath, float tile, Color tint, float smooth)
    {
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string path = $"Assets/Art/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(lit) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = lit;
        m.shaderKeywords = new string[0];
        m.SetTexture("_BaseMap", diff);
        m.SetTextureScale("_BaseMap", Vector2.one / tile);
        m.SetColor("_BaseColor", diff ? tint : new Color(0.72f, 0.58f, 0.42f));
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        mats[name] = m;
    }

    static void HomeMaterials()
    {
        HomeMaterial("Wood_Home", "diagonal_parquet", 1.6f, new Color(1f, 0.97f, 0.93f), 0.55f);
        HomeMaterial("Paint_Home", "white_stucco", 2.2f, new Color(1f, 0.95f, 0.86f), 0.2f);
        HomeMaterial("Ceiling_Home", "white_stucco", 2.2f, new Color(1f, 0.98f, 0.95f), 0.15f);
        HomeMaterial("Tile_Home", "interior_tiles", 1.2f, Color.white, 0.6f);
        HomeMaterial("Brick_Home", "large_red_bricks", 1.4f, Color.white, 0.3f);
        HomeMaterial("Brick_Soot", "large_red_bricks", 1.4f, new Color(0.22f, 0.18f, 0.16f), 0.2f);
        HomeMaterial("Fabric_Rug", "wool_boucle", 0.7f, new Color(1f, 0.9f, 0.8f), 0.1f);
        HomeMaterial("Fabric_Throw", "knitted_fleece", 0.5f, Color.white, 0.1f);
        HomeMaterial("Fabric_Curtain_Home", "wool_boucle", 0.5f, new Color(0.78f, 0.42f, 0.3f), 0.1f);
        HomeMaterial("Fabric_Shade", "knitted_fleece", 0.4f, new Color(1f, 0.93f, 0.8f), 0.1f);
        HomeMaterial("Wood_Cherry", "cherry_veneer", 0.8f, Color.white, 0.5f);
        HomeMaterial("Wood_Log", "cherry_veneer", 0.4f, new Color(0.32f, 0.22f, 0.15f), 0.2f);
        HomeMaterial("Door_Home", "cherry_veneer", 1.0f, new Color(1f, 0.95f, 0.9f), 0.5f);
        HomeMaterial("Wood_Porch", "wood_floor_deck", 1.5f, Color.white, 0.35f);
        HomeMaterialFrom("Kraft_Envelope", "Assets/Art/PolyHaven/cardboard_box_01/textures/cardboard_box_01_diff_1k.jpg", 0.4f, new Color(1f, 0.95f, 0.88f), 0.15f);
    }

    // ------------------------------------------------------------------ built pieces
    static Fiddle Fridge(Transform g, Vector3 c, List<GameObject> inside)
    {
        // c: the floor under its middle; it faces south (-Z), the door hinged on its west edge
        const float w = 0.76f, d = 0.72f, h = 1.8f, t = 0.05f;
        var body = new GameObject("Fridge").transform; body.SetParent(g, false); body.position = c;
        var m = M("Enamel_Fridge");
        Box("Fridge back", body, c + new Vector3(0, h / 2, d / 2 - t / 2), new Vector3(w, h, t), m, LProp, true, null, false, false);
        Box("Fridge side", body, c + new Vector3(-w / 2 + t / 2, h / 2, 0), new Vector3(t, h, d), m, LProp, true, null, false, false);
        Box("Fridge side", body, c + new Vector3(w / 2 - t / 2, h / 2, 0), new Vector3(t, h, d), m, LProp, true, null, false, false);
        Box("Fridge top", body, c + new Vector3(0, h - t / 2, 0), new Vector3(w, t, d), m, LProp, true, null, false, false);
        Box("Fridge base", body, c + new Vector3(0, 0.06f, 0), new Vector3(w, 0.12f, d), m, LProp, true, null, false, false);
        Box("Fridge shelf", body, c + new Vector3(0, 0.72f, 0.03f), new Vector3(w - 2 * t, 0.015f, d - 0.12f), M("Plastic_Beige"), LProp, false, null, false, false);
        Box("Fridge shelf", body, c + new Vector3(0, 1.18f, 0.03f), new Vector3(w - 2 * t, 0.015f, d - 0.12f), M("Plastic_Beige"), LProp, false, null, false, false);
        var hinge = new GameObject("Fridge hinge").transform; hinge.SetParent(body, false); hinge.position = c + new Vector3(-w / 2, 0, -d / 2 - 0.025f);
        Box("Fridge door", hinge, c + new Vector3(0, h / 2, -d / 2 - 0.025f), new Vector3(w, h - 0.02f, 0.05f), m, LProp, false, null, false, false);
        Box("Fridge handle", hinge, c + new Vector3(w / 2 - 0.08f, 1.05f, -d / 2 - 0.075f), new Vector3(0.03f, 0.4f, 0.04f), M("Metal_Steel"), LProp, false, null, false, false);
        Box("Fridge magnet", hinge, c + new Vector3(-0.12f, 1.4f, -d / 2 - 0.055f), new Vector3(0.12f, 0.16f, 0.006f), M("Paper_Note"), LProp, false, null, false, false);
        var light = MakeLamp(body, "Fridge light", c + new Vector3(0, h - 0.14f, 0.12f), 1.8f, 0.3f, new Color(0.9f, 0.95f, 1f), true, false, new Vector3(0.1f, 0.03f, 0.1f));
        inside.Add(light.gameObject);
        // the use zone rides on the door, so once it's open it is out of the way of what's inside
        var use = new GameObject("Use fridge"); use.transform.SetParent(hinge, false); use.layer = LInteract;
        use.transform.position = c + new Vector3(0, 1.0f, -d / 2 - 0.07f);
        var bc = use.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(w - 0.04f, 1.4f, 0.1f);
        var f = use.AddComponent<Fiddle>();
        f.swing = hinge; f.swingAngle = 105f;
        f.openPrompt = "Open the fridge"; f.closePrompt = "Close the fridge";
        f.sound = "fridge_door"; f.volume = 0.55f;
        f.lines = new[] { "The fridge light. Cold air on your face.", "Still the same things in there.", "You stare into the fridge. The fridge stares back." };
        Emit(c + new Vector3(0, 0.3f, 0.2f), "amb_fridge", 0.08f);
        return f;
    }

    static Lamp Fireplace(Transform g, Vector3 wall, List<Lamp> lamps)
    {
        // wall: the east wall's face at floor level, the fire's middle; it opens to the west (-X)
        float x = wall.x, y = wall.y, z = wall.z;
        var brick = M("Brick_Home");
        Box("Fireplace jamb", g, new Vector3(x - 0.25f, y + 0.55f, z - 0.82f), new Vector3(0.5f, 1.1f, 0.36f), brick, LProp, true, null, false);
        Box("Fireplace jamb", g, new Vector3(x - 0.25f, y + 0.55f, z + 0.82f), new Vector3(0.5f, 1.1f, 0.36f), brick, LProp, true, null, false);
        Box("Fireplace lintel", g, new Vector3(x - 0.25f, y + 1.0f, z), new Vector3(0.5f, 0.2f, 2.0f), brick, LProp, true, null, false);
        Box("Fireplace back", g, new Vector3(x - 0.05f, y + 0.45f, z), new Vector3(0.1f, 0.9f, 1.3f), M("Brick_Soot"), LProp, true, null, false);
        Box("Chimney breast", g, new Vector3(x - 0.2f, y + 1.9f, z), new Vector3(0.4f, 1.4f, 1.8f), brick, LProp, true, null, false);
        Box("Hearth", g, new Vector3(x - 0.45f, y + 0.03f, z), new Vector3(0.9f, 0.06f, 2.2f), M("Tile_Home"), LProp, true, null, false);
        Box("Mantel", g, new Vector3(x - 0.33f, y + 1.14f, z), new Vector3(0.66f, 0.07f, 2.3f), M("Wood_Cherry"), LProp, true, null, false);
        Box("Fire grate", g, new Vector3(x - 0.32f, y + 0.1f, z), new Vector3(0.36f, 0.04f, 0.8f), M("Metal_Steel"), LProp, false, null, false, false);
        for (int i = 0; i < 4; i++)
            Box("Log", g, new Vector3(x - 0.32f + (i % 2) * 0.05f, y + 0.18f + (i / 2) * 0.1f, z - 0.15f + i * 0.1f), new Vector3(0.12f, 0.11f, 0.6f), M("Wood_Log"), LProp, false, Quaternion.Euler(Rnd(-6, 6), 70f + i * 14f, 0f), false, false);
        var fire = MakeLamp(g, "Fire", new Vector3(x - 0.33f, y + 0.26f, z), 8f, 2.6f, new Color(1f, 0.55f, 0.26f), true, false, new Vector3(0.3f, 0.12f, 0.55f), Lamp.Pattern.Candle);
        lamps.Add(fire);
        Emit(new Vector3(x - 0.4f, y + 0.4f, z), "house_fire", 0.45f);
        return fire;
    }

    static Lamp FloorLamp(Transform g, Vector3 c, List<Lamp> lamps)
    {
        Box("Floor lamp base", g, c + Vector3.up * 0.02f, new Vector3(0.3f, 0.04f, 0.3f), M("Brass_Fitting"), LProp, true, null, false, false);
        Box("Floor lamp pole", g, c + Vector3.up * 0.78f, new Vector3(0.03f, 1.5f, 0.03f), M("Brass_Fitting"), LProp, false, null, false, false);
        var shade = Box("Floor lamp shade", g, c + Vector3.up * 1.6f, new Vector3(0.44f, 0.32f, 0.44f), M("Fabric_Shade"), LProp, false, null, false, false);
        var l = MakeLamp(g, "Floor lamp", c + Vector3.up * 1.52f, 6f, 2.4f, HomeWarm, true, false, new Vector3(0.16f, 0.1f, 0.16f));
        lamps.Add(l);
        var f = Use(shade, "Switch it", "light_tick");
        if (f) f.anim = InteractAnim.Kind.Press;
        if (f) f.lamp = l;
        return l;
    }

    // an oil-lamp style table lamp (the model) with its warm light, switched by using it
    static Lamp TableLamp(Transform g, Vector3 onTop, float yaw, List<Lamp> lamps, float intensity = 2.2f)
    {
        var model = PH(g, "vintage_oil_lamp", onTop, yaw, 0.45f, false, null, true);
        float h = model ? WorldBounds(model.transform).size.y : 0.45f;
        var l = MakeLamp(g, "Table lamp", onTop + Vector3.up * h * 0.62f, 5f, intensity, HomeWarm, true, false, new Vector3(0.08f, 0.08f, 0.08f));
        if (l.bulb) l.bulb.enabled = false;
        lamps.Add(l);
        var f = Use(model, "Switch it", "light_tick");
        if (f) f.anim = InteractAnim.Kind.Press;
        if (f) f.lamp = l;
        return l;
    }

    // a world-aligned solid box (for models whose whole bounds would swallow what sits on or in them)
    static void SolidBox(Transform g, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(g, false); go.transform.position = centre; go.layer = LProp;
        go.AddComponent<BoxCollider>().size = size;
    }

    // a ceiling lamp model hung from the ceiling over a room's light
    static void HangLamp(Transform g, Lamp light, float ceilY)
    {
        if (!light) return;
        if (light.bulb) light.bulb.enabled = false;
        var p = light.transform.position;
        var lampModel = PH(g, "modern_ceiling_lamp_01", new Vector3(p.x, 0f, p.z), 0f, 0.55f, false, null, true);
        if (!lampModel) return;
        var b = WorldBounds(lampModel.transform);
        lampModel.transform.position += Vector3.up * (ceilY - b.max.y);
        var nb = WorldBounds(lampModel.transform);
        light.transform.position = new Vector3(p.x, nb.min.y + nb.size.y * 0.35f, p.z);
    }

    static void HomeChair(Transform g, Vector3 c, float yaw)
    {
        var chair = PH(g, "painted_wooden_chair_01", c, yaw, 0.95f, true, "Chair");
        var _f10 = Use(chair, "Sit down", "cloth", "You sit at the table. Nobody to talk to.", "You get up again.");
        if (_f10) _f10.anim = InteractAnim.Kind.Sit;
    }

    static void Curtains(Transform g, Vector3 a, Vector3 b, Vector3 inward)
    {
        // either side of a window (a, b: the window's two ends at floor level)
        var along = (b - a).normalized;
        foreach (var (p, s) in new[] { (a, -1f), (b, 1f) })
        {
            var c = p + along * (0.12f * s) + inward * 0.08f + Vector3.up * 1.35f;
            Box("Curtain", g, c, Mathf.Abs(along.x) > 0.5f ? new Vector3(0.38f, 2.3f, 0.05f) : new Vector3(0.05f, 2.3f, 0.38f), M("Fabric_Curtain_Home"), LProp, false, null, false, false);
        }
    }

    // a radio with its own voice: soft music (a recorded nocturne on a record), on from the start, switched by using it
    static void Radio(Transform g, Vector3 onTop, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var r = Box("Radio", g, onTop + Vector3.up * 0.09f, new Vector3(0.3f, 0.18f, 0.14f), M("Wood_Cherry"), LProp, false, rot, false, false);
        Box("Radio grille", r.transform, onTop + Vector3.up * 0.09f + rot * new Vector3(-0.05f, 0f, 0.071f), new Vector3(0.15f, 0.12f, 0.005f), M("Fabric_Shade"), LProp, false, rot, false, false);
        Box("Radio dial", r.transform, onTop + Vector3.up * 0.1f + rot * new Vector3(0.09f, 0f, 0.072f), new Vector3(0.06f, 0.06f, 0.006f), M("Brass_Fitting"), LProp, false, rot, false, false);
        var vo = new GameObject("Radio music"); vo.transform.SetParent(r.transform, false); vo.transform.position = onTop + Vector3.up * 0.1f;
        vo.AddComponent<AudioSource>();
        var v = vo.AddComponent<SpatialVoice>();
        v.volume = 0.22f; v.minDistance = 0.8f; v.maxDistance = 16f; v.sendLevel = 0.5f; v.directivity = 0.3f;
        var f = Use(r, "Radio", "radio_click", "Soft piano. Late-night radio.", "Quiet. You can hear the rain again.");
        if (f) f.anim = InteractAnim.Kind.Press;
        if (f) { f.music = v; f.musicClip = "record_nocturne"; f.musicOn = true; }
    }

    // ================================================================== the house
    static void House(Refs refs)
    {
        var g = Group("House");
        var o = LevelLayout.BedroomOrigin;
        Vector3 B(float x, float z, float y = 0f) => o + new Vector3(x, y, z);
        var lamps = refs.houseLamps;
        const float ceil = 2.6f;

        // ---------------- bedroom: the bed you wake up in, the bedside lamp, the dresser, the cabinet
        var bed = PH(g, "vintage_day_bed", B(0.85f, 3.9f), 90f, 2.05f, false, "Bed", true);
        if (bed)
        {
            // the bed runs along the west wall, its head to the north (where you wake up)
            var bb = WorldBounds(bed.transform);
            if (bb.size.x > bb.size.z) { bed.transform.RotateAround(bb.center, Vector3.up, 90f); bb = WorldBounds(bed.transform); }
            bed.transform.position += new Vector3(B(0.85f, 3.9f).x - bb.center.x, 0f, B(0.85f, 3.9f).z - bb.center.z);
            bb = WorldBounds(bed.transform);
            // the mattress is low (the day bed's back and ends rise above it)
            float mattress = bb.min.y + Mathf.Min(0.5f, bb.size.y * 0.42f);
            // solid only up to the mattress (a box round the whole day bed swallowed the cushions on it)
            SolidBox(g, "Bed (solid)", new Vector3(bb.center.x, (bb.min.y + mattress) / 2f, bb.center.z), new Vector3(bb.size.x, mattress - bb.min.y, bb.size.z));
            Box("Blanket", g, new Vector3(bb.center.x, mattress + 0.02f, bb.center.z - 0.25f), new Vector3(bb.size.x * 0.82f, 0.05f, bb.size.z * 0.6f), M("Fabric_Throw"), LProp, false, null, false, false);
            var pillows = PH(g, "throw_pillows_01", new Vector3(bb.center.x, mattress, bb.max.z - 0.35f), 180f, 0.7f, false, "Pillows", true);
            var _f11 = Use(pillows, "Fluff the pillows", "cloth", "Better.", "You consider going back to bed. The doorbell disagrees.");
            if (_f11) _f11.anim = InteractAnim.Kind.Squash;
        }
        var stand = PH(g, "ClassicNightstand_01", B(1.85f, 4.65f), 180f, 0.6f, true, "Nightstand");
        float standTop = stand ? WorldBounds(stand.transform).max.y : o.y + 0.6f;
        var bedside = TableLamp(g, new Vector3(o.x + 1.78f, standTop, o.z + 4.68f), 0f, lamps, 3.2f);
        refs.bedroomLamp = bedside;
        var clock = PH(g, "alarm_clock_01", new Vector3(o.x + 2.0f, standTop, o.z + 4.58f), 200f, 0.12f);
        var fClock = Use(clock, "Check the clock", "light_tick", "11:34 PM. Who rings a doorbell at this hour?", "It ticks. Very loud, now you're awake.", "You put it face down. Better.");
        if (fClock) fClock.anim = InteractAnim.Kind.Inspect;
        var dresser = PH(g, "vintage_wooden_drawer_01", B(3.84f, 1.1f), 270f, 1.2f, true, "Dresser", true);
        float dresserTop = dresser ? WorldBounds(dresser.transform).max.y : o.y + 0.8f;
        var tapes = Box("Box of tapes", g, new Vector3(o.x + 3.85f, dresserTop + 0.11f, o.z + 0.85f), new Vector3(0.3f, 0.22f, 0.4f), M("Paper_Note"), LProp, false, null, false, false);
        var _f3 = Use(tapes, "Look through the old tapes", "cloth", "'Holiday, 1998'.", "'Mum's 60th'.", "'DO NOT TAPE OVER'. Somebody taped over it.");
        if (_f3) _f3.anim = InteractAnim.Kind.PullOut;          // one tape slides out of the box
        var photo = PH(g, "fancy_picture_frame_01", new Vector3(o.x + 3.9f, dresserTop, o.z + 1.35f), 250f, 0.25f);
        var _f1 = Use(photo, "Look at the photo", "cloth", "You, at the seaside. You don't remember who took it.", "You look happy. You look tired.");
        if (_f1) _f1.anim = InteractAnim.Kind.Inspect;
        var vase = PH(g, "ceramic_vase_01", new Vector3(o.x + 3.85f, dresserTop, o.z + 1.6f), 0f, 0.25f);
        var _f6 = Use(vase, "Look at the vase", "cloth", "Empty. You keep meaning to buy flowers.");
        if (_f6) _f6.anim = InteractAnim.Kind.Inspect;
        var cabinet = PH(g, "painted_wooden_cabinet_02", B(0.32f, 1.2f), 90f, 1.95f, true, "Cabinet", true);
        var fCabinet = Use(cabinet, "Look in the cabinet", "door_creak", "Jumpers, folded. Nothing suitable for a haunted hotel.", "Your good coat. You never go anywhere good.");
        if (fCabinet) fCabinet.anim = InteractAnim.Kind.Lean;
        Box("Rug", g, B(2.4f, 2.4f, 0.01f), new Vector3(2.0f, 0.02f, 1.5f), M("Fabric_Rug"), LProp, true);
        Curtains(g, B(1.2f, 4.95f), B(3.0f, 4.95f), Vector3.back);
        Emit(B(2.1f, 4.9f, 1.6f), "amb_rain", 0.25f);

        // ---------------- living room: the fire, the TV and the VCR, the sofa off to the east (clear of the bedroom door)
        var lr = R("Living Room");
        var living = CeilingLight(g, lr, true, B(4.3f, 3.6f, 1.25f), Vector3.right, 3.2f);
        living.lightSource.color = HomeWarm;
        lamps.Add(living);
        HangLamp(g, living, o.y + ceil);
        Fireplace(g, B(10.4f, 2.6f), lamps);
        var fz = Use(Box("Fire (use)", g, B(10.05f, 2.6f, 0.4f), new Vector3(0.5f, 0.7f, 1.2f), M("Glass_Bulb"), LInteract, false, null, false, false), "Warm your hands", "cloth",
            "Warm.", "You hold your hands to the fire. Toasty.", "You poke the logs. A shower of sparks.");
        if (fz) fz.anim = InteractAnim.Kind.Lean;              // you lean in to the fire
        if (fz) fz.GetComponent<MeshRenderer>().enabled = false;
        var mclock = PH(g, "mantel_clock_01", B(10.07f, 2.0f, 1.18f), 270f, 0.3f);
        var fMclock = Use(mclock, "Check the clock", "clock_chime", "11:36. Late.", "It chimes. It always chimes at the wrong time.");
        if (fMclock) fMclock.anim = InteractAnim.Kind.Inspect;
        var mphoto = PH(g, "fancy_picture_frame_01", B(10.1f, 3.3f, 1.18f), 270f, 0.25f, false, "Photo on the mantel");
        var _f2 = Use(mphoto, "Look at the photo", "cloth", "Your grandparents, on their wedding day.", "Someone has written a date on the back: 31.10.2003. That's not their wedding.");
        if (_f2) _f2.anim = InteractAnim.Kind.Inspect;
        Emit(B(10.0f, 2.0f, 1.3f), "amb_clock_tick", 0.06f);
        // the TV stand against the south wall, the TV on top, the VCR on the shelf under it
        var sc = B(5.3f, 0.32f);
        F(g, "TV stand top", sc + Vector3.up * 0.6f, new Vector3(1.2f, 0.04f, 0.5f), "Wood_Cherry");
        F(g, "TV stand shelf", sc + Vector3.up * 0.22f, new Vector3(1.16f, 0.03f, 0.48f), "Wood_Cherry", 0f, false);
        F(g, "TV stand side", sc + new Vector3(-0.58f, 0.31f, 0f), new Vector3(0.04f, 0.62f, 0.5f), "Wood_Cherry");
        F(g, "TV stand side", sc + new Vector3(0.58f, 0.31f, 0f), new Vector3(0.04f, 0.62f, 0.5f), "Wood_Cherry");
        var tvm = PH(g, "Television_01", sc + Vector3.up * 0.62f, 0f, 0.8f, true, "Television", true);
        var tb = tvm ? WorldBounds(tvm.transform) : new Bounds(sc + Vector3.up * 0.9f, new Vector3(0.6f, 0.5f, 0.5f));
        var scr = Quad("TV screen", g, new Vector3(tb.center.x, tb.min.y + tb.size.y * 0.55f, tb.max.z + 0.004f), new Vector2(tb.size.x * 0.62f, tb.size.y * 0.55f), Quaternion.LookRotation(Vector3.back), M("Screen"));
        var tv = scr.AddComponent<MonitorScreen>();
        tv.screen = scr.GetComponent<Renderer>();
        tv.on = false;
        tv.clean = 1f;                     // a videotape on a TV: no security-monitor scanlines and noise
        refs.bedroomTv = tv;
        refs.screens.Add((tv, "TAPE"));
        var tvUse = Use(tvm, "Change the channel", "tv_zap", "Static.", "More static.", "A shopping channel. Then static.", "Static. You could swear something moved in it.");
        if (tvUse) tvUse.anim = InteractAnim.Kind.None;
        if (tvUse) tvUse.screen = tv;
        var vcr = Box("VCR", g, sc + new Vector3(0f, 0.29f, 0.04f), new Vector3(0.44f, 0.09f, 0.3f), M("Plastic_Device"), LInteract, true, null, false, false);
        vcr.AddComponent<BedroomVcr>();
        var vz = new GameObject("Use VCR"); vz.transform.SetParent(vcr.transform, false); vz.layer = LInteract;
        var vzb = vz.AddComponent<BoxCollider>(); vzb.isTrigger = true; vzb.size = new Vector3(0.9f, 0.5f, 0.6f); vzb.center = new Vector3(0f, 0.08f, 0.08f);
        // the sofa (cushions on it) and the coffee table, east of the doorway
        var sofa = PH(g, "Sofa_01", B(6.6f, 3.65f), 180f, 2.0f, true, "Sofa");
        if (sofa)
        {
            var sb = WorldBounds(sofa.transform);
            var cushions = PH(g, "throw_pillows_01", new Vector3(sb.center.x, sb.min.y + sb.size.y * 0.55f, sb.center.z + 0.08f), 180f, 0.6f, false, "Cushions");
            var _f12 = Use(cushions, "Plump the cushions", "cloth", "Plump.", "Perfect. Nobody will notice.");
            if (_f12) _f12.anim = InteractAnim.Kind.Squash;
            Box("Throw", g, new Vector3(sb.max.x - 0.3f, sb.min.y + sb.size.y * 0.5f, sb.center.z), new Vector3(0.5f, 0.04f, sb.size.z * 0.8f), M("Fabric_Throw"), LProp, false, Quaternion.Euler(0f, 0f, -8f), false, false);
        }
        var _f8 = Use(sofa, "Sit down for a second", "cloth", "You sink into the sofa. Comfortable. Too comfortable.", "The cushion still has the shape of you.", "You get up before you fall asleep again.");
        if (_f8) _f8.anim = InteractAnim.Kind.Sit;
        var table = PH(g, "CoffeeTable_01", B(6.5f, 2.05f), 0f, 1.1f, true, "Coffee table");
        float tableTop = table ? WorldBounds(table.transform).max.y : o.y + 0.45f;
        var chips = Box("Crisps", g, B(6.0f, 2.0f, tableTop - o.y + 0.03f), new Vector3(0.16f, 0.05f, 0.2f), M("Lamp_Red"), LProp, false, Quaternion.Euler(0f, 20f, 0f), false, false);
        Eat(chips, "Eat some crisps", "chips_eat", 3, "Salt and vinegar.", "Crunch.", "The bag is empty. You check again anyway.");
        var guide = Box("TV guide", g, B(6.3f, 2.12f, tableTop - o.y + 0.006f), new Vector3(0.21f, 0.01f, 0.28f), M("Paper_Note"), LProp, false, Quaternion.Euler(0f, -12f, 0f), false, false);
        var _f4 = Use(guide, "Read the TV guide", "page_turn", "Friday 31 October 2003. There's nothing on.", "A circle round 11:45 PM, channel 0. There is no channel 0.");
        if (_f4) _f4.anim = InteractAnim.Kind.Inspect;
        var tea = PH(g, "tea_set_01", B(6.85f, 2.05f, tableTop - o.y), 20f, 0.7f, false, "Tea set", true);
        var _f7 = Use(tea, "Pour a cup of tea", "drink", "Cold tea. You drink it anyway.", "The pot's empty now.");
        if (_f7) _f7.anim = InteractAnim.Kind.Pour;             // the teapot tips and pours
        // the armchair by the fire, a side table with a lamp and the radio
        var arm = PH(g, "ArmChair_01", B(8.9f, 1.5f), 300f, 1.0f, true, "Armchair");
        var _f9 = Use(arm, "Sit in the armchair", "cloth", "Your chair. By the fire. Perfect.", "You could sit here all night.");
        if (_f9) _f9.anim = InteractAnim.Kind.Sit;
        F(g, "Side table", B(8.0f, 0.5f, 0.3f), new Vector3(0.45f, 0.6f, 0.45f), "Wood_Cherry");
        TableLamp(g, B(7.9f, 0.45f, 0.6f), 0f, lamps, 2.0f);
        Radio(g, B(8.12f, 0.58f, 0.6f), 0f);
        var shelf = PH(g, "Shelf_01", B(7.95f, 5.12f), 180f, 1.8f, false, "Bookshelf");
        if (shelf)
        {
            var sb = WorldBounds(shelf.transform);
            // solid at the back; the books stand at the front of their shelf, where you can reach them
            SolidBox(g, "Bookshelf (solid)", new Vector3(sb.center.x, sb.center.y, sb.max.z - sb.size.z * 0.3f), new Vector3(sb.size.x, sb.size.y, sb.size.z * 0.6f));
            var books = PH(g, "book_encyclopedia_set_01", new Vector3(sb.center.x, sb.min.y + sb.size.y * 0.52f, sb.min.z + 0.12f), 180f, 0.6f);
            if (books)
            {
                // stood on a real board (they used to hang between two): of the shelf's boards at the front, the one
                // nearest the middle with room above for them
                var bb = WorldBounds(books.transform);
                var levels = SurfacesAt(shelf, sb.center.x, sb.min.z + 0.12f);
                float mid = sb.min.y + sb.size.y * 0.5f, best = float.NaN;
                for (int i = 0; i < levels.Count; i++)
                {
                    float above = i + 1 < levels.Count ? levels[i + 1] : sb.max.y + 1f;
                    if (above - levels[i] < bb.size.y + 0.015f || levels[i] > sb.max.y - 0.05f) continue;
                    if (float.IsNaN(best) || Mathf.Abs(levels[i] - mid) < Mathf.Abs(best - mid)) best = levels[i];
                }
                if (!float.IsNaN(best)) books.transform.position += Vector3.up * (best + 0.002f - bb.min.y);
                Debug.Log($"HOUSE books on the shelf board at {(float.IsNaN(best) ? -1f : best - sb.min.y):0.00} m (boards at {string.Join(", ", levels.Select(l => (l - sb.min.y).ToString("0.00")))})");
            }
            var _f0 = Use(books, "Take a book", "page_turn", "Encyclopaedia, volume K. You read about kangaroos.", "Volume M: magnetic tape. You put it back quickly.", "Volume H: hotels. A page has been torn out.");
            if (_f0) _f0.anim = InteractAnim.Kind.PullOut;      // one volume slides out to be read
        }
        FloorLamp(g, B(4.65f, 4.9f), lamps);
        var plant = PH(g, "potted_plant_02", B(9.95f, 0.45f), 0f, 1.0f, true, "Plant");
        var _f14 = Use(plant, "Water the plant", "cloth", "You've got nothing to water it with. You give it a pep talk instead.", "It looks disappointed.");
        if (_f14) _f14.anim = InteractAnim.Kind.Sway;
        var frame = PH(g, "hanging_picture_frame_01", B(9.2f, 0.095f, 1.35f), 0f, 0.6f, false, "Painting");
        var fFrame = Use(frame, "Look at the painting", "cloth", "A lighthouse in a storm. You bought it because it was cheap.", "It's crooked. You straighten it. It's still crooked.");
        if (fFrame) fFrame.anim = InteractAnim.Kind.Straighten;
        if (fFrame)
        {
            var st = frame.AddComponent<StraightTarget>();
            st.straight = frame.transform.rotation; st.slipBack = 2.5f; st.slipAxis = Vector3.forward;
            frame.transform.rotation = Quaternion.AngleAxis(5f, Vector3.forward) * frame.transform.rotation;
        }
        Box("Rug", g, B(6.5f, 2.6f, 0.01f), new Vector3(3.0f, 0.02f, 2.4f), M("Fabric_Rug"), LProp, true);
        Curtains(g, B(6.4f, 0.05f), B(8.2f, 0.05f), Vector3.forward);

        // ---------------- kitchen: counters, the fridge, the stove, the table and the fruit bowl
        var kr = R("Kitchen");
        var kitchenLight = CeilingLight(g, kr, true, B(5.1f, 5.5f, 1.25f), Vector3.forward, 3.2f);
        kitchenLight.lightSource.color = HomeWarm;
        lamps.Add(kitchenLight);
        HangLamp(g, kitchenLight, o.y + ceil);
        float top = 0.9f;
        F(g, "Counter", B(6.0f, 9.08f, 0.43f), new Vector3(3.2f, 0.86f, 0.6f), "Wood_Cherry");
        F(g, "Worktop", B(6.0f, 9.07f, 0.88f), new Vector3(3.24f, 0.04f, 0.64f), "Tile_Home");
        Box("Splashback", g, B(6.0f, 9.312f, 1.2f), new Vector3(3.24f, 0.6f, 0.02f), M("Tile_Home"), LProp, false, null, false, false);
        Box("Sink", g, B(6.05f, 9.08f, 0.885f), new Vector3(0.5f, 0.02f, 0.38f), M("Metal_Steel"), LProp, false, null, false, false);
        var tap = Box("Tap", g, B(6.05f, 9.3f, 1.0f), new Vector3(0.04f, 0.22f, 0.04f), M("Metal_Steel"), LProp, false, null, false, false);
        Box("Tap spout", tap.transform, B(6.05f, 9.22f, 1.09f), new Vector3(0.03f, 0.03f, 0.16f), M("Metal_Steel"), LProp, false, null, false, false);
        var _f17 = Use(tap, "Run the tap", null, "Cold water. You drink from your hand.", "The pipes knock. Old house.");
        if (_f17)
        {
            _f17.anim = InteractAnim.Kind.Press;
            _f17.runSound = "amb_faucet"; _f17.runSeconds = 3.5f; _f17.volume = 0.5f;
            var water = Box("Water", tap.transform, B(6.05f, 9.15f, 0.985f), new Vector3(0.012f, 0.2f, 0.012f), M("Water_Stream"), LProp, false, null, false, false);
            water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _f17.whileRunning = water;
        }
        var drawer = Box("Cutlery drawer", g, B(5.0f, 8.77f, 0.75f), new Vector3(0.5f, 0.14f, 0.04f), M("Wood_Cherry"), LProp, false, null, false, false);
        var df = Use(drawer, "Open the drawer", "cutlery", "Knives, forks, one spoon. Where do the spoons go?", "The drawer sticks. It always sticks.");
        if (df) { df.slide = drawer.transform; df.slideBy = new Vector3(0f, 0f, -0.28f); df.openPrompt = "Open the drawer"; df.closePrompt = "Close the drawer"; }
        var toaster = Box("Toaster", g, B(4.8f, 9.12f, top + 0.1f), new Vector3(0.28f, 0.2f, 0.17f), M("Metal_Steel"), LProp, false, null, false, false);
        Box("Toaster slot", toaster.transform, B(4.75f, 9.12f, top + 0.2f), new Vector3(0.04f, 0.005f, 0.13f), M("Brick_Soot"), LProp, false, null, false, false);
        Box("Toaster slot", toaster.transform, B(4.85f, 9.12f, top + 0.2f), new Vector3(0.04f, 0.005f, 0.13f), M("Brick_Soot"), LProp, false, null, false, false);
        var tf = Use(toaster, "Make some toast", "toaster", "Two slices in.", "It pops. You jump. Every time.", "You eat the toast standing up.");
        if (tf) tf.anim = InteractAnim.Kind.Press;
        if (tf) { tf.sound2 = "toaster"; tf.sound2Delay = 3.5f; tf.sound2Volume = 0.6f; tf.runSeconds = 3.5f; }
        var kettle = PH(g, "vintage_electric_kettle", B(5.35f, 9.12f, top), 180f, 0.3f);
        var kf = Use(kettle, "Put the kettle on", "tv_click", "You put the kettle on.", "Tea fixes everything. Almost everything.");
        if (kf) kf.anim = InteractAnim.Kind.Press;
        if (kf) { kf.runSound = "kettle"; kf.runSeconds = 14f; kf.volume = 0.45f; }
        if (kf && kettle) kf.whileRunning = Steam(g, WorldBounds(kettle.transform).center + Vector3.up * (WorldBounds(kettle.transform).extents.y + 0.01f));
        var board = PH(g, "wooden_cutting_board", B(6.7f, 9.08f, top), 0f, 0.4f);
        float boardTop = board ? WorldBounds(board.transform).max.y - o.y : top + 0.02f;
        var croissant = PH(g, "croissant", B(6.7f, 9.06f, boardTop), 30f, 0.13f);
        Eat(croissant, "Eat the croissant", "apple_bite", 2, "Yesterday's. Still good.", "Flakes everywhere.");
        var micro = PH(g, "vintage_microwave", B(7.15f, 9.08f, top), 180f, 0.55f, true, "Microwave", true);
        var mf = Use(micro, "Run the microwave", "microwave_bell", "There's nothing in it. You run it anyway.", "The plate turns. Nothing on it. Hypnotic.");
        if (mf) mf.anim = InteractAnim.Kind.Press;
        if (mf) { mf.sound = "tv_click"; mf.runSound = "microwave"; mf.runSeconds = 10f; mf.sound2 = "microwave_bell"; mf.sound2Delay = 10f; mf.volume = 0.4f; }
        var wclock = PH(g, "wall_clock", B(5.6f, 9.29f, 1.85f), 180f, 0.32f, false, "Wall clock");
        var fWclock = Use(wclock, "Check the clock", "light_tick", "11:37. It's always slow.", "Tick. Tock. You should go to bed. Too late.");
        if (fWclock) fWclock.anim = InteractAnim.Kind.Lean;
        // the fridge, food inside
        var inside = new List<GameObject>();
        var fc = B(8.0f, 9.0f);
        var milk = PH(g, "long_life_food", fc + new Vector3(0f, 0.735f, 0.05f), 180f, 0.4f, false, "Milk and tins");
        var fMilk = Use(milk, "Sniff the milk", "drink", "It's... fine. Probably.", "You drink straight from the carton. Nobody saw.", "Beans. Not now.");
        if (fMilk) fMilk.anim = InteractAnim.Kind.Drink;
        if (milk) inside.Add(milk);
        var cake = PH(g, "carrot_cake", fc + new Vector3(0f, 1.195f, 0.05f), 0f, 0.28f, false, "Carrot cake");
        Eat(cake, "Have a slice of cake", "apple_bite", 3, "A slice. Just one.", "Another slice. Just one more.", "Gone. You regret nothing.");
        if (cake) inside.Add(cake);
        var can = Box("Soda can", g, fc + new Vector3(0.22f, 0.135f + 0.065f, -0.12f), new Vector3(0.066f, 0.12f, 0.066f), M("Lamp_Red"), LProp, false, null, false, false);
        var canF = Eat(can, "Drink the soda", "can_open", 1, "Fizz. Sugar at midnight. Great idea.");
        if (canF) { canF.sound2 = "drink"; canF.sound2Delay = 0.5f; canF.sound2Volume = 0.45f; }
        inside.Add(can);
        var fridge = Fridge(g, fc, inside);
        fridge.whileOpen = inside.ToArray();
        var wine = PH(g, "wine_bottles_01", fc + new Vector3(0f, 1.8f, 0f), 0f, 0.45f);
        var fWine = Use(wine, "Look at the wine", "cutlery", "It's a school night.", "You don't have school. You still don't open it.");
        if (fWine) fWine.anim = InteractAnim.Kind.Inspect;
        var stove = PH(g, "electric_stove", B(4.6f, 6.2f), 90f, 0.9f, true, "Stove");
        var ring = MakeLamp(g, "Hob ring", B(4.55f, 6.2f, 0.96f), 1.2f, 0.25f, new Color(1f, 0.35f, 0.15f), false, false, new Vector3(0.12f, 0.01f, 0.12f));
        var sf = Use(stove, "Turn on a ring", "light_tick", "The ring glows orange. You turn it off again. You weren't going to cook.", "You hold your hand over it. Warm.");
        if (sf) sf.anim = InteractAnim.Kind.Press;
        if (sf) { sf.glowWhileRunning = ring; sf.runSeconds = 5f; sf.runSound = null; }
        // the table, two chairs, the fruit bowl, the buns, plates
        var ktable = PH(g, "round_wooden_table_01", B(6.4f, 7.2f), 0f, 1.05f, true, "Kitchen table", true);
        float kt = ktable ? WorldBounds(ktable.transform).max.y - o.y : 0.75f;
        HomeChair(g, B(5.6f, 7.2f), 90f);
        HomeChair(g, B(7.2f, 7.2f), 270f);
        var bowl = PH(g, "wooden_bowl_01", B(6.4f, 7.2f, kt), 0f, 0.3f);
        float bowlY = bowl ? WorldBounds(bowl.transform).min.y - o.y + 0.03f : kt + 0.03f;
        var bananas = PH(g, "bananas", B(6.33f, 7.16f, bowlY), 40f, 0.22f);
        Eat(bananas, "Eat a banana", "apple_bite", 3, "Potassium.", "Another banana. Why not.", "That was the last banana. Someone will be upset. You.");
        var apple = PH(g, "food_apple_01", B(6.5f, 7.28f, bowlY + 0.02f), 0f, 0.085f);
        Eat(apple, "Eat the apple", "apple_bite", 2, "Crunchy.", "Better than whatever they serve at that hotel. Probably.");
        var pear = PH(g, "food_pears_asian_01", B(6.44f, 7.13f, bowlY + 0.02f), 0f, 0.1f);
        Eat(pear, "Eat the pear", "apple_bite", 2, "Juicy. Sticky fingers now.", "Core and all? No.");
        var lemon = PH(g, "lemon", B(6.35f, 7.24f, bowlY + 0.005f), 0f, 0.08f);               // in the bowl (it hung past its rim)
        Eat(lemon, "Bite the lemon", "apple_bite", 1, "Why did you do that.");
        var buns = PH(g, "hamburger_buns", B(6.05f, 6.9f, kt), -30f, 0.25f);
        Eat(buns, "Eat a bun", "apple_bite", 2, "A plain bun. Dry. Very dry.", "You're eating dry buns at midnight. Reflect on that.");
        if (tf)
        {
            // the toast: bread from the buns' own scan, down inside the slots until it pops
            var bread = buns ? buns.GetComponentInChildren<Renderer>().sharedMaterial : M("Kraft_Envelope");
            var s1 = Box("Toast", toaster.transform, B(4.75f, 9.12f, top + 0.14f), new Vector3(0.012f, 0.1f, 0.11f), bread, LProp, false, null, false, false);
            var s2 = Box("Toast", toaster.transform, B(4.85f, 9.12f, top + 0.14f), new Vector3(0.012f, 0.1f, 0.11f), bread, LProp, false, null, false, false);
            tf.popUp = new[] { s1.transform, s2.transform };
            tf.popHeight = 0.07f;
        }
        var plates = Box("Plates", g, B(6.75f, 6.95f, kt + 0.03f), new Vector3(0.24f, 0.05f, 0.24f), M("Plastic_Beige"), LProp, false, null, false, false);
        var _f16 = Use(plates, "Straighten the plates", "plate", "Neat.", "You stack them again. Neater.");
        if (_f16)
        {
            _f16.anim = InteractAnim.Kind.Straighten;
            var st = plates.AddComponent<StraightTarget>();
            st.straight = plates.transform.rotation;
            plates.transform.rotation = Quaternion.AngleAxis(9f, Vector3.up) * plates.transform.rotation;
        }
        Box("Rug", g, B(6.4f, 7.2f, 0.01f), new Vector3(2.0f, 0.02f, 1.8f), M("Fabric_Rug"), LProp, true);
        Curtains(g, B(4.25f, 6.7f), B(4.25f, 8.1f), Vector3.right);

        // ---------------- hallway: coats, shoes, keys, a plant, a basket, the front door
        var hr = R("Hallway");
        var hallLight = CeilingLight(g, hr, true, B(8.5f, 6.2f, 1.25f), Vector3.right, 2.4f);
        hallLight.lightSource.color = HomeWarm;
        lamps.Add(hallLight);
        HangLamp(g, hallLight, o.y + ceil);
        Box("Coat hooks", g, B(10.3f, 6.6f, 1.7f), new Vector3(0.04f, 0.05f, 0.9f), M("Wood_Cherry"), LProp, false, null, false, false);
        var coat = Box("Coat", g, B(10.2f, 6.4f, 1.15f), new Vector3(0.18f, 1.0f, 0.5f), M("Fabric_Curtain_Home"), LProp, false, null, false, false);
        var _f13 = Use(coat, "Check your coat pockets", "cloth", "A bus ticket. A receipt for batteries. Lint.", "Nothing new in the pockets.");
        if (_f13) _f13.anim = InteractAnim.Kind.Sway;
        Box("Coat", g, B(10.2f, 6.9f, 1.2f), new Vector3(0.16f, 0.9f, 0.45f), M("Fabric_Throw"), LProp, false, null, false, false);
        var shoes = F(g, "Shoe rack", B(10.15f, 8.2f, 0.2f), new Vector3(0.35f, 0.4f, 0.9f), "Wood_Cherry");
        var fShoes = Use(shoes, "Look at your shoes", "steps_wood_house", "Still wet. You walked home in the rain.", "Your good shoes. You never wear them.");
        if (fShoes) fShoes.anim = InteractAnim.Kind.Lean;
        var basket = PH(g, "wicker_basket_01", B(10.05f, 7.35f), 0f, 0.45f, true, "Basket");
        var fBasket = Use(basket, "Look in the basket", "cloth", "Umbrellas. Three of them, all broken.", "Gloves that don't match.");
        if (fBasket) fBasket.anim = InteractAnim.Kind.Lean;
        F(g, "Hall table", B(8.65f, 7.0f, 0.4f), new Vector3(0.4f, 0.8f, 0.9f), "Wood_Cherry");
        var keys = Box("Keys", g, B(8.65f, 7.2f, 0.815f), new Vector3(0.09f, 0.02f, 0.05f), M("Brass_Fitting"), LProp, false, Quaternion.Euler(0f, 30f, 0f), false, false);
        var _f5 = Use(keys, "Pick up your keys", "door_unlatch_metal", "Your keys. You're not going anywhere tonight.", "You put them back. In exactly the same place.");
        if (_f5) _f5.anim = InteractAnim.Kind.Inspect;
        var hplant = PH(g, "potted_plant_04", B(8.65f, 6.75f, 0.8f), 0f, 0.35f);
        var _f15 = Use(hplant, "Touch the plant", "cloth", "Plastic. You've watered it for years.");
        if (_f15) _f15.anim = InteractAnim.Kind.Sway;
        Box("Hall runner", g, B(9.4f, 7.4f, 0.01f), new Vector3(0.9f, 0.02f, 3.0f), M("Fabric_Rug"), LProp, true);

        // ---------------- the front door (locked), its letterbox, the doormat: the tape comes in through the flap
        refs.bedroomHall = null;
        Box("Doormat", g, B(9.4f, 8.95f, 0.01f), new Vector3(0.9f, 0.02f, 0.6f), M("Fabric_Rug"), LProp, true);
        var flap = Box("Letterbox", g, LevelLayout.Letterbox + Vector3.back * 0.005f, new Vector3(0.28f, 0.07f, 0.012f), M("Brass_Fitting"), LProp, false, null, false, false);
        var _f18 = Use(flap, "Peek through the letterbox", "door_unlatch_metal", "Rain. The street light. Nobody.", "Just the rain.");
        if (_f18) _f18.anim = InteractAnim.Kind.FlapUp;        // the flap lifts and snaps shut
        // the envelope (kraft paper, the parcel box's own scanned card), waiting by the mat until the bell rings
        var env = Box("Envelope", g, LevelLayout.ParcelSpot + Vector3.up * 0.015f, new Vector3(0.32f, 0.03f, 0.24f), M("Kraft_Envelope"), LProp, false, Quaternion.Euler(0f, 12f, 0f), false, false);
        var eu = new GameObject("Use envelope"); eu.transform.SetParent(env.transform, false); eu.layer = LInteract;
        var ebc = eu.AddComponent<BoxCollider>(); ebc.isTrigger = true; ebc.size = new Vector3(0.5f, 0.3f, 0.42f); ebc.center = new Vector3(0f, 0.1f, 0f);
        env.AddComponent<TapeItem>();
        env.SetActive(false);
        refs.bedroomParcel = env;
        Emit(LevelLayout.FrontDoorOutside, "amb_rain", 0.35f);
    }
}
