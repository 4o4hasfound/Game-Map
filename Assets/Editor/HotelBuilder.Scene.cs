using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class HotelBuilder
{
    static float axisX = -1f, axisZ = -1f;
    static Vector3 U(List<object> p) => new Vector3(axisX * F(p[0]), F(p[2]), axisZ * F(p[1]));
    static Vector3 UDir(List<object> d) => new Vector3(axisX * F(d[0]), d.Count > 2 ? F(d[2]) : 0f, axisZ * F(d[1]));
    static float F(object o) => o is double d ? (float)d : 0f;

    static readonly float[] LevelX = { 0f, 120f, 240f };

    [MenuItem("Hotel/Build Scene")]
    public static void BuildScene()
    {
        SetupProject();
        ConfigureModel();
        materials.Clear();
        MakeGrime();
        ConfigureTexture($"{TexDir}/Grime.png", false, false);
        grimeTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Grime.png");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupLighting();
        var layout = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(MapJson));
        var levels = layout.L("levels").Cast<Dictionary<string, object>>().ToList();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MapFbx);
        var map = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(map, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        map.name = "HotelMesh";
        DetectAxes(map, levels);

        // ---- level roots
        var roots = new GameObject[levels.Count];
        for (int i = 0; i < levels.Count; i++) roots[i] = new GameObject("Level_" + levels[i].S("key"));
        var byName = new Dictionary<string, Transform>();
        var children = new List<Transform>();
        foreach (Transform c in map.transform) children.Add(c);
        foreach (var c in children)
        {
            byName[c.name] = c;
            int li = LevelOf(c);
            c.SetParent(roots[li].transform, true);
        }
        Object.DestroyImmediate(map);

        // ---- materials, layers, colliders
        int meshes = 0;
        foreach (var t in byName.Values)
        {
            var mr = t.GetComponent<MeshRenderer>();
            if (!mr) continue;
            meshes++;
            var mats = mr.sharedMaterials;
            for (int k = 0; k < mats.Length; k++)
                if (mats[k]) mats[k] = MakeMaterial(Clean(mats[k].name));
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            string n = t.name;
            var mf = t.GetComponent<MeshFilter>();
            if (n.StartsWith("Walls_") || n.StartsWith("Floor_") || n.StartsWith("Railings_") || n.StartsWith("Stairs_") || n.StartsWith("StairRails_"))
            {
                t.gameObject.layer = StructureLayer;
                if (!n.StartsWith("Stairs_")) t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                t.gameObject.isStatic = true;
            }
            else if (n.StartsWith("Props_") || n.StartsWith("Trim_"))
            {
                t.gameObject.layer = PropLayer;
                t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                t.gameObject.isStatic = true;
            }
            else if (n.StartsWith("Ceiling_"))
            {
                // ceilings block and reflect sound (own layer so the monster's navmesh ignores their tops)
                t.gameObject.layer = CeilingLayer;
                t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                t.gameObject.isStatic = true;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
            else if (n.StartsWith("Ramp_"))
            {
                t.gameObject.layer = StructureLayer;
                t.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                mr.enabled = false;
            }
            else if (n.StartsWith("Decals_"))
            {
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                t.gameObject.isStatic = false; // ambience reads original ceiling-hole vertices at runtime
            }
            else if (n.StartsWith("Plaque_") || n.StartsWith("Fixture_"))
            {
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
        var elevMats = new ElevatorStop.Mats
        {
            panel = MakeMaterial("Wood_Panel_Dark"), steel = MakeMaterial("Steel"), brass = MakeMaterial("Brass"), floor = MakeMaterial("Carpet_Red"),
            ceiling = MakeMaterial("Ceiling_Plaster"), mirror = MakeMaterial("Mirror"), glow = MakeMaterial("Emit_Warm"), button = MakeMaterial("Plastic_White"),
            text = SavedMaterial("ElevatorText", new Material(Shader.Find("Hotel/TextDepth")) { mainTexture = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf").material.mainTexture })
        };
        var stepMat = MakeMaterial("Steel");
        AssetDatabase.SaveAssets();

        // ---- per level: doors, lights, sounds, devices, stairs, elevator
        var fixtureLights = new Dictionary<string, LightFlicker>();
        var stops = new List<ElevatorStop>();
        var stopLinks = new List<string>();
        string[] floorLabels = levels.Select(l => ShortLabel(l.S("label"))).ToArray();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        int nLights = 0, nSounds = 0, nDevices = 0, nDoors = 0, nSeats = 0, nReadables = 0;
        for (int li = 0; li < levels.Count; li++)
        {
            var L = levels[li];
            var root = roots[li].transform;
            string key = L.S("key");
            var lightRoot = new GameObject("Lights").transform;
            lightRoot.SetParent(root, false);
            var soundRoot = new GameObject("Sounds").transform;
            soundRoot.SetParent(root, false);

            foreach (Dictionary<string, object> d in L.L("doors"))
            {
                if (!byName.TryGetValue(d.S("name"), out var t)) continue;
                SetupDoor(t, d);
                nDoors++;
            }
            foreach (Dictionary<string, object> l in L.L("lights"))
            {
                var lf = MakeLight(l, lightRoot, byName, RoomReach(L, l.I("room", -1)));
                string fixtureName = l.S("fixture");
                if (fixtureName != null) fixtureLights[fixtureName] = lf;
                nLights++;
            }
            foreach (Dictionary<string, object> s in L.L("sounds"))
            {
                MakeSound(s, soundRoot, fixtureLights);
                nSounds++;
            }
            foreach (Dictionary<string, object> dv in L.L("devices"))
            {
                if (!byName.TryGetValue(dv.S("name"), out var t)) continue;
                if (MakeDevice(t, dv, key)) nDevices++;
            }
            // places to sit, newspapers to read, table tops for loose objects
            var interRoot = new GameObject("Interactables").transform;
            interRoot.SetParent(root, false);
            foreach (Dictionary<string, object> st in L.L("seats"))
            {
                var go = new GameObject("Seat");
                go.layer = LooseObjects.InteractLayer;
                go.transform.SetParent(interRoot, false);
                go.transform.position = U(st.L("pos"));
                go.transform.rotation = Quaternion.Euler(0f, HotelWorld.YawStatic(st.F("yaw"), axisX, axisZ), 0f);
                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.center = new Vector3(0f, 0.08f, 0f);
                bc.size = new Vector3(0.42f, 0.2f, 0.42f);
                var seat = go.AddComponent<Seat>();
                seat.height = st.F("h");
                seat.kind = st.S("kind") ?? "chair";
                nSeats++;
            }
            foreach (Dictionary<string, object> rd in L.L("readables"))
            {
                var go = new GameObject("Newspaper");
                go.layer = LooseObjects.InteractLayer;
                go.transform.SetParent(interRoot, false);
                go.transform.position = U(rd.L("pos"));
                var bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.center = new Vector3(0f, 0.06f, 0f);
                bc.size = new Vector3(0.6f, 0.14f, 0.6f);
                go.AddComponent<Readable>().kind = rd.S("kind") ?? "newspaper";
                nReadables++;
            }
            var surf = L.L("surfaces");
            var spots = root.gameObject.AddComponent<SurfaceSpots>();
            int ns = surf.Count;
            spots.pos = new Vector3[ns]; spots.yaw = new float[ns]; spots.w = new float[ns]; spots.d = new float[ns];
            spots.kind = new string[ns]; spots.roomKind = new string[ns];
            for (int k = 0; k < ns; k++)
            {
                var sd = (Dictionary<string, object>)surf[k];
                spots.pos[k] = U(sd.L("pos"));
                spots.yaw[k] = HotelWorld.YawStatic(sd.F("yaw"), axisX, axisZ);
                spots.w[k] = sd.F("w"); spots.d[k] = sd.F("d");
                spots.kind[k] = sd.S("kind"); spots.roomKind[k] = sd.S("kind_room");
            }
            foreach (Dictionary<string, object> st in L.L("stairs"))
            {
                if (st.S("kind") != "escalator") continue;
                var go = new GameObject("Escalator_" + st.S("name"));
                go.transform.SetParent(root, false);
                var esc = go.AddComponent<Escalator>();
                esc.bottom = U(st.L("bottom"));
                esc.top = U(st.L("top"));
                esc.width = F(st["width"]) * 0.55f;
                esc.running = st.B("running");
                esc.stepMaterial = stepMat;
                if (esc.running)
                    foreach (var p in new[] { esc.bottom, esc.top, (esc.bottom + esc.top) * 0.5f })
                        MakeSound(new Dictionary<string, object>
                        {
                            { "clip", "amb_escalator" }, { "vol", 0.5 }, { "loop", true }, { "min", 1.0 }, { "max", 22.0 },
                            { "pos", new List<object> { (double)(p.x * axisX), (double)(p.z * axisZ), (double)(p.y + 0.4f) } }
                        }, soundRoot, fixtureLights);
            }
            foreach (Dictionary<string, object> e in L.L("elevators"))
            {
                string link = e.S("link");
                string dest = "";
                if (link != null)
                    for (int lj = 0; lj < levels.Count; lj++)
                        if (lj != li && levels[lj].L("elevators").Cast<Dictionary<string, object>>().Any(o => o.S("link") == link))
                            dest = floorLabels[lj];
                var stop = ElevatorStop.Build(root, li, floorLabels[li], U(e.L("door")), UDir(e.L("inward")), F(e["width"]), elevMats, font,
                                              link != null ? new[] { dest } : new string[0], out _);
                if (link == null) continue;
                stops.Add(stop);
                stopLinks.Add(link);
            }
        }

        ChunkEnvironmentRenderers(byName);
        // ---- reflection probes: glass, chrome, marble and water need something to reflect
        int probes = 0;
        for (int li = 0; li < levels.Count; li++)
            foreach (Dictionary<string, object> r in levels[li].L("rooms"))
            {
                if (r.I("cells") < 24 || r.S("kind") == "shaft") continue;
                var bnd = r.L("bounds");
                float z = r.F("z"), ch = Mathf.Min(r.F("ceil"), 8f);
                Vector3 a = U(new List<object> { bnd[0], bnd[1], (double)z }), b = U(new List<object> { bnd[2], bnd[3], (double)(z + ch) });
                var go = new GameObject("Probe_" + r.S("name"));
                go.transform.SetParent(roots[li].transform, false);
                go.transform.position = (a + b) * 0.5f;
                var rp = go.AddComponent<ReflectionProbe>();
                rp.enabled = false; // enable a small nearby set only after the player exists
                // Never enqueue every room's six cubemap faces on scene load (graphics ring-buffer exhaustion).
                rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                rp.mode = ReflectionProbeMode.Realtime;
                rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                rp.resolution = 64;
                rp.size = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z)) + Vector3.one * 0.5f;
                rp.boxProjection = true;
                rp.hdr = true;
                rp.cullingMask = (1 << StructureLayer) | (1 << PropLayer) | 1;
                rp.farClipPlane = Mathf.Clamp(rp.size.magnitude*.65f,5f,18f);
                rp.shadowDistance = 0f; // reflection captures must not schedule another full set of shadow maps
                probes++;
            }

        // ---- world services
        var world = new GameObject("World");
        var hw = world.AddComponent<HotelWorld>();
        hw.layoutJson = AssetDatabase.LoadAssetAtPath<TextAsset>(MapJson);
        hw.axisX = axisX;
        hw.axisZ = axisZ;
        hw.levelRoots = roots;
        hw.startLevel = 0;
        world.AddComponent<AcousticWorld>();
        world.AddComponent<LightManager>().shadowCasters = 6;
        world.AddComponent<NavBaker>();
        world.AddComponent<AmbienceDirector>();
        var elev = world.AddComponent<ElevatorSystem>();
        elev.stops = stops.ToArray();
        elev.links = stopLinks.ToArray();

        // ---- player + camcorder
        var spawn = levels[0].D("spawn");
        var player = new GameObject("Player");
        player.layer = PlayerLayer;
        player.transform.position = U(spawn.L("pos")) + Vector3.up * 0.05f;
        var rb = player.AddComponent<Rigidbody>();
        rb.mass = 70f;
        var cap = player.AddComponent<CapsuleCollider>();
        cap.radius = 0.28f;
        var pm = new PhysicsMaterial("PlayerFrictionless") { dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
        AssetDatabase.CreateAsset(pm, "Assets/Generated/PlayerFrictionless.physicsMaterial");
        cap.sharedMaterial = pm;
        var motor = player.AddComponent<PlayerMotor>();
        var steps = player.AddComponent<Footsteps>();
        elev.player = motor;

        var camGo = new GameObject("Camcorder");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 64f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 150f;
        cam.renderingPath = RenderingPath.DeferredShading;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        var eng = camGo.AddComponent<AudioEngine>();
        eng.Listener = camGo.transform;
        camGo.AddComponent<ReverbBus>();
        var vcam = camGo.AddComponent<VHSCamera>();
        vcam.player = motor;
        vcam.yaw = HotelWorld.YawStatic(F(spawn["yaw"]), axisX, axisZ);
        motor.cameraTransform = camGo.transform;
        var fx = camGo.AddComponent<VHSPostFX>();
        var fxMat = new Material(Shader.Find("Hidden/VHSFX"));
        AssetDatabase.CreateAsset(fxMat, "Assets/Generated/VHSFX.mat");
        fx.material = fxMat;
        var inter = camGo.AddComponent<Interactor>();
        inter.player = motor;
        var test = camGo.AddComponent<AudioTest>();
        test.head = camGo.transform;
        var lampGo = new GameObject("CamcorderLight");
        var lamp = lampGo.AddComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.range = 22f;
        lamp.spotAngle = 70f;
        lamp.cookie = MakeFlashlightCookie();
        lamp.innerSpotAngle = 25f;
        lamp.intensity = 1.5f;
        lamp.color = new Color(1f, 0.93f, 0.82f);
        lamp.shadows = LightShadows.Soft;
        var cl = camGo.AddComponent<CamcorderLight>();
        cl.lamp = lamp;
        cl.cam = camGo.transform;
        cl.motor = motor;
        cl.owned = false;     // found on the floor in front of the spawn TV
        var spawnTv = world.AddComponent<SpawnAtTV>();
        spawnTv.player = motor;
        spawnTv.cam = vcam;
        spawnTv.flashlight = cl;
        spawnTv.feedMaterial = SavedMaterial("LiveFeed", new Material(Shader.Find("Unlit/Texture")));
        var torchBody = new Material(Shader.Find("Standard")) { color = new Color(0.07f, 0.07f, 0.075f) };
        torchBody.SetFloat("_Glossiness", 0.45f);
        torchBody.SetFloat("_Metallic", 0.6f);
        spawnTv.flashlightBody = SavedMaterial("FlashlightBody", torchBody);
        var lens = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.95f, 0.85f) };
        lens.EnableKeyword("_EMISSION");
        lens.SetColor("_EmissionColor", new Color(1f, 0.93f, 0.8f) * 3f);
        spawnTv.flashlightLens = SavedMaterial("FlashlightLens", lens);
        var loose = world.AddComponent<LooseObjects>();
        Material Std(string n, Color c, float gloss, float metal, bool transparent = false)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metal);
            if (transparent)
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            return SavedMaterial(n, m);
        }
        loose.glass = Std("LooseGlass", new Color(0.32f, 0.42f, 0.3f, 0.55f), 0.92f, 0f, true);
        loose.ceramic = Std("LooseCeramic", new Color(0.82f, 0.8f, 0.74f), 0.7f, 0f);
        loose.tin = Std("LooseTin", new Color(0.45f, 0.43f, 0.4f), 0.5f, 0.85f);
        loose.paper = Std("LoosePaper", new Color(0.78f, 0.75f, 0.66f), 0.1f, 0f);
        loose.book = Std("LooseBook", new Color(0.25f, 0.09f, 0.07f), 0.2f, 0f);

        var osd = new GameObject("VCR_OSD");
        var ov = osd.AddComponent<VHSOverlay>();
        ov.interactor = inter;
        ov.footsteps = steps;
        camGo.AddComponent<GeometricAcoustics>();
        var sc = world.AddComponent<StalkerControl>();
        sc.player = motor;
        var at = world.AddComponent<AcousticTest>();
        at.player = motor;
        at.cam = vcam;
        world.AddComponent<MaintenanceReview>();
        world.AddComponent<PianoReview>();
        world.AddComponent<InteractReview>();
        world.AddComponent<HotelEnvironment>();
        world.AddComponent<HotelStress>();
        world.AddComponent<HotelReflections>();
        var tour = world.AddComponent<CaptureTour>();
        tour.player = motor;
        tour.cam = vcam;
        tour.audioTest = test;

        foreach (var r in roots) r.isStatic = false;
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = BothScenes.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[HotelBuilder] scene built: {meshes} meshes, {nDoors} doors, {nLights} lights, {nSounds} sound emitters, {nDevices} devices, {nSeats} seats, {nReadables} newspapers, " +
                  $"{stops.Count} elevator stops, axes ({axisX}, {axisZ})");
    }

    static string Clean(string n)
    {
        n = n.Replace(" (Instance)", "");
        int dot = n.LastIndexOf('.');
        if (dot > 0 && n.Length - dot == 4 && char.IsDigit(n[n.Length - 1])) n = n.Substring(0, dot);
        return n;
    }

    static string ShortLabel(string label) => label.Split(' ')[0];

    static void DetectAxes(GameObject map, List<Dictionary<string,object>> levels)
    {
        var probe = map.transform.Find("Floor_L2_1");
        if (!probe) throw new System.Exception("Floor_L2_1 not found in the FBX");
        var b = probe.GetComponent<Renderer>().bounds;
        var layer=(Dictionary<string,object>)levels[1].L("layers")[1];
        var rows=layer.L("grid"); int xmin=999,ymin=999,xmax=-1,ymax=-1;
        for(int y=0;y<rows.Count;y++) { var row=(List<object>)rows[y]; for(int x=0;x<row.Count;x++) if(F(row[x])>=0) { xmin=Mathf.Min(xmin,x);xmax=Mathf.Max(xmax,x+1);ymin=Mathf.Min(ymin,y);ymax=Mathf.Max(ymax,y+1); } }
        float expectedX=F(levels[1].L("origin")[0])+(xmin+xmax)*0.5f, expectedZ=(ymin+ymax)*0.5f;
        axisX = Mathf.Sign(b.center.x / expectedX);
        axisZ = Mathf.Sign(b.center.z / expectedZ);
        bool ok = Mathf.Abs(Mathf.Abs(b.center.x)-expectedX)<3f && Mathf.Abs(Mathf.Abs(b.center.z)-expectedZ)<3f && Mathf.Abs(b.center.y-layer.F("z"))<0.3f;
        Debug.Log($"[HotelBuilder] probe bounds {b.center} size {b.size} -> axes ({axisX}, {axisZ}) {(ok ? "OK" : "UNEXPECTED")}");
        if (!ok) throw new System.Exception("unexpected FBX axis mapping: " + b.center);
    }

    static int LevelOf(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        float bx = r ? r.bounds.center.x * axisX : t.position.x * axisX;
        int best = 0;
        for (int i = 0; i < LevelX.Length; i++)
            if (bx >= LevelX[i] - 10f) best = i;
        return best;
    }

    // beam of a cheap halogen video light: bright core, soft spill, faint reflector rings, a few blemishes
    static Texture2D MakeFlashlightCookie()
    {
        const int N = 256;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp };  // spot cookies use alpha
        var px = new Color[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float x = (i + 0.5f) / N * 2f - 1f, y = (j + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float core = Mathf.Exp(-r * r / 0.06f);
                float spill = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.98f, r))) * 0.3f;
                float ring = 0.06f * Mathf.Exp(-Mathf.Pow((r - 0.55f) / 0.04f, 2f)) + 0.04f * Mathf.Exp(-Mathf.Pow((r - 0.31f) / 0.03f, 2f));
                float ang = Mathf.Atan2(y, x);
                float uneven = 1f + 0.05f * Mathf.Sin(ang * 3f + 1.3f) + 0.04f * (Mathf.PerlinNoise(x * 6f + 3f, y * 6f + 7f) - 0.5f);
                float smudge = 1f - 0.12f * Mathf.Exp(-((x - 0.18f) * (x - 0.18f) + (y + 0.1f) * (y + 0.1f)) / 0.01f);
                float v = Mathf.Clamp01((core * 0.75f + spill + ring) * uneven * smudge) * (r < 1f ? 1f : 0f);
                px[j * N + i] = new Color(v, v, v, v);
            }
        t.SetPixels(px);
        t.Apply();
        AssetDatabase.CreateAsset(t, "Assets/Generated/FlashlightCookie.asset");
        return t;
    }

    static void SetupLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.005f, 0.0055f, 0.0065f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        RenderSettings.reflectionIntensity = 0.2f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        var ls = new LightingSettings { bakedGI = false, realtimeGI = false, autoGenerate = false };
        AssetDatabase.CreateAsset(ls, "Assets/Generated/Lighting.lighting");
        Lightmapping.lightingSettings = ls;
    }

    // ------------------------------------------------------------------ doors
    static void SetupDoor(Transform t, Dictionary<string, object> d)
    {
        var go = t.gameObject;
        go.layer = DoorLayer;
        var mf = go.GetComponent<MeshFilter>();
        var bc = go.AddComponent<BoxCollider>();
        bc.center = mf.sharedMesh.bounds.center;
        bc.size = mf.sharedMesh.bounds.size + new Vector3(0.01f, 0.01f, 0.01f);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var hd = go.AddComponent<HingeDoor>();
        string name = d.S("name");
        hd.portalName = name.EndsWith("_L") || name.EndsWith("_R") ? name.Substring(0, name.Length - 2) : name;
        hd.locked = d.B("locked");
        hd.metal = d.S("sound") == "metal";
        float h = Mathf.Abs(name.GetHashCode() % 100) / 100f;
        hd.startOpen = hd.locked ? 0f : h < 0.3f ? 0.15f + h : h > 0.85f ? 1f : 0f;
    }

    // ------------------------------------------------------------------ lights
    // half the diagonal of a room plus a little: how far a lamp in it may reach
    static float RoomReach(Dictionary<string, object> level, int roomId)
    {
        if (roomId < 0) return 99f;
        foreach (Dictionary<string, object> r in level.L("rooms"))
        {
            if (r.I("id") != roomId) continue;
            var b = r.L("bounds");
            float w = F(b[2]) - F(b[0]), d = F(b[3]) - F(b[1]);
            return Mathf.Max(3f, 0.5f * Mathf.Sqrt(w * w + d * d + r.F("ceil") * r.F("ceil")) + 1.2f);
        }
        return 99f;
    }

    static LightFlicker MakeLight(Dictionary<string, object> l, Transform parent, Dictionary<string, Transform> byName, float reach)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.position = U(l.L("pos"));
        var li = go.AddComponent<Light>();
        bool spot = l.S("kind") == "spot";
        li.type = spot ? LightType.Spot : LightType.Point;
        var c = l.L("color");
        li.color = new Color(F(c[0]), F(c[1]), F(c[2]));
        li.intensity = F(l["intensity"]) * 0.85f;
        li.range = Mathf.Min(F(l["range"]), reach);
        li.shadows = LightShadows.None;
        li.renderMode = LightRenderMode.ForcePixel;
        li.lightmapBakeType = LightmapBakeType.Realtime;
        if (spot)
        {
            li.spotAngle = l.F("angle", 70f);
            var dir = l.L("dir");
            if (dir.Count == 3)
            {
                var d = UDir(dir);
                if (d.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
            }
        }
        var lf = go.AddComponent<LightFlicker>();
        lf.pattern = LightFlicker.Parse(l.S("pattern"));
        // Additional failing point fixtures; emergency lights and directional window light retain their role.
        if(lf.pattern==LightFlicker.Pattern.Steady && go.GetComponent<Light>().type==LightType.Point && Mathf.Abs(go.transform.position.x*13f+go.transform.position.z*7f)%10f<3f)
            lf.pattern=LightFlicker.Pattern.Flicker;
        lf.fluorescent=go.GetComponent<Light>().color.g > go.GetComponent<Light>().color.r*0.9f;
        lf.circuit = l.S("circuit") ?? "";
        lf.shadowsWanted = l.B("shadows");
        string fx = l.S("fixture");
        if (fx != null && byName.TryGetValue(fx, out var ft)) lf.fixture = ft.GetComponent<Renderer>();
        return lf;
    }

    static Material SavedMaterial(string name, Material m)
    {
        string path = "Assets/Generated/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ sounds
    static void MakeSound(Dictionary<string, object> s, Transform parent, Dictionary<string, LightFlicker> fixtureLights)
    {
        var go = new GameObject("Sound_" + s.S("clip"));
        go.transform.SetParent(parent, false);
        go.transform.position = U(s.L("pos"));
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        var v = go.AddComponent<SpatialVoice>();
        v.minDistance = s.F("min", 1f);
        v.maxDistance = s.F("max", 18f);
        v.label = s.S("clip");
        var em = go.AddComponent<AmbientEmitter>();
        em.clip = s.S("clip");
        em.volume = s.F("vol", 0.6f);
        em.loop = s.B("loop", true);
        em.pitch = s.F("pitch", 1f);
        var iv = s.L("interval");
        if (iv.Count == 2) em.interval = new Vector2(F(iv[0]), F(iv[1]));
        string link = s.S("link");
        if (link != null && fixtureLights.TryGetValue(link, out var lf)) em.link = lf;
    }

    // ------------------------------------------------------------------ devices
    static bool MakeDevice(Transform t, Dictionary<string, object> d, string circuit)
    {
        string type = d.S("type");
        if (type == "fixture") return false;
        var p = d.D("params");
        var go = t.gameObject;
        var mf = go.GetComponent<MeshFilter>();
        var b = mf ? mf.sharedMesh.bounds : new Bounds(Vector3.up * 0.5f, Vector3.one * 0.5f);
        var bc = go.AddComponent<BoxCollider>();
        bc.center = b.center;
        bc.size = b.size + new Vector3(0.04f, 0.04f, 0.04f);
        // "Front": the direction the device faces, at its centre (speakers, keyboards ...)
        var front = new GameObject("Front").transform;
        front.SetParent(t, false);
        front.localPosition = b.center;
        float yaw = HotelWorld.YawStatic(d.F("yaw"), axisX, axisZ);
        front.rotation = Quaternion.Euler(0f, yaw, 0f);
        Interactable comp = null;
        switch (type)
        {
            case "radio": var r = go.AddComponent<RadioDevice>(); r.station = p.I("station"); comp = r; break;
            case "jukebox": comp = go.AddComponent<JukeboxDevice>(); break;
            case "piano": var pi = go.AddComponent<PianoDevice>(); pi.tuning = p.S("tuning", "grand"); pi.autoplay = p.B("autoplay"); comp = pi; break;
            case "organ": var og = go.AddComponent<PianoDevice>(); og.tuning = "organ"; comp = og; break;
            case "gramophone": var g = go.AddComponent<GramophoneDevice>(); g.track = p.S("track", "waltz"); comp = g; break;
            case "music_box": var mb = go.AddComponent<MusicBoxDevice>(); mb.tune = p.S("tune", "lullaby"); comp = mb; break;
            case "tv": var tv = go.AddComponent<TVDevice>(); tv.channel = p.S("channel", "static"); tv.startOn = false; comp = tv; break;
            case "phone":
            case "payphone":
                var ph = go.AddComponent<PhoneDevice>();
                ph.payphone = type == "payphone";
                ph.rings = p.B("rings", true);
                ph.firstRing = p.F("first_ring", 30f);
                ph.voiceClip = p.S("voice", "phone_voice_1");
                comp = ph;
                break;
            case "breaker": var br = go.AddComponent<BreakerPanel>(); br.circuit = p.S("circuit", circuit); comp = br; break;
            default:
                var ld = go.AddComponent<LoopDevice>();
                ld.kind = type == "typewriter" ? LoopDevice.Kind.Typewriter : type == "bell" ? LoopDevice.Kind.Bell :
                    type == "vending" ? LoopDevice.Kind.Vending : type == "ice_machine" ? LoopDevice.Kind.IceMachine :
                    type == "faucet" ? LoopDevice.Kind.Faucet : type == "clock" ? LoopDevice.Kind.Clock :
                    type == "projector" ? LoopDevice.Kind.Projector : LoopDevice.Kind.Monitors;
                ld.loopClip = p.S("clip");
                ld.startOn = ld.kind == LoopDevice.Kind.Vending || ld.kind == LoopDevice.Kind.IceMachine || ld.kind == LoopDevice.Kind.Clock;
                if (ld.kind == LoopDevice.Kind.Projector)
                    foreach (var lf in Object.FindObjectsByType<LightFlicker>(FindObjectsSortMode.None))
                        if (lf.pattern == LightFlicker.Pattern.Projector && Vector3.Distance(lf.transform.position, t.position) < 8f) ld.projectorLight = lf;
                comp = ld;
                break;
        }
        if (comp is PoweredDevice pd) pd.circuit = circuit;
        return comp != null;
    }
}
