using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Automated checks for the built game:
//   -capture <dir>   walk the camera through a list of rooms on every level and save screenshots
//   -audiotest <wav> record the final mix while the test beacon circles the head, then quit
public class CaptureTour : MonoBehaviour
{
    public PlayerMotor player;
    public VHSCamera cam;
    public AudioTest audioTest;

    // (level, room name, view direction in degrees relative to the room's long axis, pitch)
    static readonly (int, string, float, float)[] Shots =
    {
        (0, "Room 701", 0, 5), (0, "Room 703", 0, 0), (0, "North Hall", 90, 0), (0, "East Hall (Flooded)", 0, 0),
        (0, "South Hall (Fire)", -90, 0), (0, "West Hall (Renovation)", 180, 0), (0, "7F Elevator Lobby", 180, 0), (0, "Kids Club", 45, 0),
        (0, "Presidential Suite 708", 45, 0), (0, "Room 717", 0, 0),
        (1, "Grand Lobby", 0, -8), (1, "Grand Lobby", 90, -25), (1, "Mezzanine Gallery", 135, 15), (1, "The Blue Note Bar", 0, 0),
        (1, "Grand Ballroom", 0, -5), (1, "Staff Passage", 0, 0), (1, "Kitchen", 0, 0), (1, "Restaurant Le Jardin", 45, 0),
        (0, "Sky Lounge", 0, 0), (0, "Sky Lounge", 135, -5), (0, "Collapsed Wing", 0, 10), (0, "Plumbing Gallery", 0, 0),
        (2, "Pipe Gallery", 0, 0),
        (0, "Service Elevator Vestibule", 0, 0), (0, "North Hall East", 90, 0), (1, "Service Elevator Hall", 0, 0),
        (2, "Well Catwalk", 0, -35), (2, "Well Catwalk", 180, 30), (2, "Boiler Hall", 30, 5), (2, "Boiler Control Balcony", 0, -20),
        (2, "Maintenance Catwalk", 45, -40), (2, "Light Well", 0, 60), (2, "Industrial Laundry", 0, 0), (2, "Cistern Chamber", 0, 0),
        (2, "North Service Corridor", 0, 0), (2, "Freight Elevator Lobby", 0, 0),
    };

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-capture") StartCoroutine(Capture(args[i + 1]));
            if (args[i] == "-audiotest") StartCoroutine(Audio(args[i + 1]));
            if (args[i] == "-navtest") StartCoroutine(NavTest(args[i + 1]));
        }
    }

    IEnumerator Capture(string dir)
    {
        Directory.CreateDirectory(dir);
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        var inspectionFx = cam.GetComponent<VHSPostFX>();
        if (inspectionFx) { inspectionFx.shutterTime = 0f; inspectionFx.maxBlur = 0f; } // static geometry inspection
        player.Frozen = true;
        var captureBody = player.GetComponent<Rigidbody>();
        captureBody.isKinematic = true;
        captureBody.interpolation = RigidbodyInterpolation.None;
        var world = HotelWorld.Instance;
        yield return new WaitForSeconds(2f);
        var cl = cam.GetComponent<CamcorderLight>();
        if (cl && cl.lamp) cl.lamp.enabled = true;      // shown the way it is played: with the camera light on
        // Render into a texture so capture works even with the automated player's window hidden.
        var captureCamera = cam.GetComponent<Camera>();
        var captureTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        captureCamera.targetTexture = captureTarget;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = captureCamera;
                canvas.planeDistance = 0.5f;
            }
        int n = 0;
        var shots = Shots;
        if(world.levels[0].rooms.Exists(r=>r.name=="Sea Gallery")) shots=new[] {
            (0,"Room 701",0f,0f),(0,"Room 706",0f,0f),(0,"Sea Gallery",90f,0f),
            (0,"Collapsed Palm Conservatory",30f,0f),(0,"Garden Gallery",90f,0f),
            (0,"Plumbing Back Route",0f,0f),(0,"Ocean Reading Room",0f,0f),(0,"Terrace Service Lift",0f,0f),
            (1,"Winter Garden",30f,-5f),(1,"Balustrade Promenade",90f,20f),
            (1,"Tidal Ballroom",0f,0f),(1,"The Saltwater Bar",0f,0f),
            (1,"Porter Luggage Room",0f,0f),(1,"East Garden Cross Passage",0f,0f),
            (1,"Cinema Projection Salon",0f,0f),(1,"East Freight Vestibule",0f,0f),
            (2,"Drained Thermal Pool",30f,-10f),(2,"Poolside Promenade",90f,20f),
            (2,"Water Filtration Hall",0f,0f),(2,"Filtration Control Deck",0f,20f),
            (2,"Overflow Culvert",0f,0f),(2,"Dry Pipe Bypass",0f,0f),
            (2,"Changing Lockers",0f,0f),(2,"Pool Towel Laundry",0f,0f)
        };
        // -shots <file>: "level;room;yaw;pitch[;x;y]" per line; x/y are absolute level cells (default: room centre),
        // yaw is then absolute (0 = +y of the plan), otherwise relative to the room's long axis
        var custom = new System.Collections.Generic.Dictionary<int, Vector2>();
        var cargs = System.Environment.GetCommandLineArgs();
        int si = System.Array.IndexOf(cargs, "-shots");
        if (si >= 0 && si + 1 < cargs.Length && File.Exists(cargs[si + 1]))
        {
            var list = new System.Collections.Generic.List<(int, string, float, float)>();
            foreach (var line in File.ReadAllLines(cargs[si + 1]))
            {
                var f = line.Split(';');
                if (f.Length < 4) continue;
                if (f.Length >= 6) custom[list.Count] = new Vector2(float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture));
                list.Add((int.Parse(f[0]), f[1], float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture)));
            }
            shots = list.ToArray();
        }
        int shotIndex = -1;
        foreach (var (lvl, roomName, yaw, pitch) in shots)
        {
            shotIndex++;
            world.SetLevel(lvl);
            var lv = world.levels[lvl];
            if (roomName == "@spawn")
            {
                // the opening view: in front of the live-feed TV, flashlight still on the floor
                captureBody.position = SpawnAtTV.SpawnPos;
                player.transform.position = SpawnAtTV.SpawnPos;
                Physics.SyncTransforms();
                cam.yaw = SpawnAtTV.SpawnYaw + yaw;
                cam.pitch = SpawnAtTV.SpawnPitch + pitch;
                cam.AddYaw(0f);
                if (cl && cl.lamp) cl.lamp.enabled = false;
                yield return new WaitForSeconds(2f);
                string sp = Path.Combine(dir, $"{n:00}_spawn_{(int)yaw}.png");
                captureCamera.Render();
                var prevRt = RenderTexture.active;
                RenderTexture.active = captureTarget;
                var shotTex = new Texture2D(captureTarget.width, captureTarget.height, TextureFormat.RGB24, false);
                shotTex.ReadPixels(new Rect(0, 0, captureTarget.width, captureTarget.height), 0, 0);
                shotTex.Apply();
                RenderTexture.active = prevRt;
                File.WriteAllBytes(sp, shotTex.EncodeToPNG());
                Destroy(shotTex);
                n++;
                if (cl && cl.lamp) cl.lamp.enabled = true;
                continue;
            }
            var room = lv.rooms.Find(r => r.name == roomName);
            if (room == null) continue;
            Vector3 p = RoomCentre(world, lv, room);
            bool absolute = custom.TryGetValue(shotIndex, out var cellXY);
            if (absolute) p = world.ToUnity(lv.ox + cellXY.x, cellXY.y, lv.layers[room.layer].z);
            captureBody.position = p;
            player.transform.position = p;
            Physics.SyncTransforms();
            float baseYaw = absolute ? AbsoluteYaw(world) : LongAxisYaw(world, lv, room);
            cam.yaw = baseYaw + yaw;
            cam.pitch = pitch;
            cam.AddYaw(0f);
            yield return new WaitForSeconds(1.5f); // settle by elapsed time, even when a hidden window runs very fast
            if (cl && cl.lamp)
                Debug.Log($"[CaptureTour] lamp {roomName}: local {cam.transform.InverseTransformPoint(cl.lamp.transform.position)} " +
                          $"off-axis {Vector3.Angle(cl.lamp.transform.forward, cam.transform.forward):0.0} deg intensity {cl.lamp.intensity:0.00} enabled {cl.lamp.enabled}");
            string path = Path.Combine(dir, $"{n:00}_{lv.key}_{roomName.Replace(' ', '_').Replace("'", "")}_{(int)yaw}.png");
            captureCamera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = captureTarget;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Destroy(pixels);
            RenderTexture.active = previous;
            yield return null;
            n++;
        }
        Application.Quit();
    }

    static Vector3 RoomCentre(HotelWorld world, LevelInfo lv, RoomInfo room)
    {
        var L = lv.layers[room.layer];
        float sx = 0, sy = 0;
        int c = 0;
        for (int j = 0; j < L.h; j++)
            for (int i = 0; i < L.w; i++)
                if (L.cells[j * L.w + i] == room.id) { sx += i + 0.5f; sy += j + 0.5f; c++; }
        if (c == 0) return Vector3.zero;
        // nearest actual cell of the room to its centroid (L-shaped rooms)
        float cx = sx / c, cy = sy / c, best = 1e9f;
        float bx = cx, by = cy;
        for (int j = 0; j < L.h; j++)
            for (int i = 0; i < L.w; i++)
                if (L.cells[j * L.w + i] == room.id)
                {
                    float d = (i + 0.5f - cx) * (i + 0.5f - cx) + (j + 0.5f - cy) * (j + 0.5f - cy);
                    if (d < best) { best = d; bx = i + 0.5f; by = j + 0.5f; }
                }
        return world.ToUnity(lv.ox + bx, by, L.z + 0.05f);
    }

    static float LongAxisYaw(HotelWorld world, LevelInfo lv, RoomInfo room)
    {
        var L = lv.layers[room.layer];
        int minx = 999, maxx = -1, miny = 999, maxy = -1;
        for (int j = 0; j < L.h; j++)
            for (int i = 0; i < L.w; i++)
                if (L.cells[j * L.w + i] == room.id) { minx = Mathf.Min(minx, i); maxx = Mathf.Max(maxx, i); miny = Mathf.Min(miny, j); maxy = Mathf.Max(maxy, j); }
        bool alongX = maxx - minx >= maxy - miny;
        var d = world.DirToUnity(alongX ? 1f : 0f, alongX ? 0f : 1f);
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    // monster navigation check: bake every floor, then try paths between many random pairs of points
    IEnumerator NavTest(string report)
    {
        player.Frozen = true;
        var world = HotelWorld.Instance;
        var sb = new System.Text.StringBuilder();
        var nb = NavBaker.Instance;
        for (int lvl = 0; lvl < world.levels.Count; lvl++)
        {
            world.SetLevel(lvl);
            yield return null;
            nb.Bake(lvl);
            float t0 = Time.realtimeSinceStartup;
            while (!nb.Ready(lvl)) yield return null;
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            var lv = world.levels[lvl];
            int ok = 0, tried = 0;
            float pathLen = 0f;
            var path = new UnityEngine.AI.NavMeshPath();
            for (int k = 0; k < 120; k++)
            {
                var ra = lv.rooms[Random.Range(0, lv.rooms.Count)];
                var rb = lv.rooms[Random.Range(0, lv.rooms.Count)];
                if (ra.kind == "shaft" || rb.kind == "shaft" || ra.kind.Contains("crawl") || rb.kind.Contains("crawl")) continue;
                if (!NavBaker.RoomPoint(world,lv,ra,out var ha)) continue;
                if (!NavBaker.RoomPoint(world,lv,rb,out var hb)) continue;
                tried++;
                if (UnityEngine.AI.NavMesh.CalculatePath(ha.position, hb.position, -1, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    ok++;
                    for (int c = 1; c < path.corners.Length; c++) pathLen += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                }
            }
            // rooms the monster cannot reach from the first room that has a walkable centre
            var unreach = new System.Collections.Generic.List<string>();
            UnityEngine.AI.NavMeshHit refHit = default;
            bool haveRef = false;
            foreach (var r in lv.rooms)
            {
                if (r.kind == "shaft" || r.kind.Contains("crawl")) continue;
                if (!NavBaker.RoomPoint(world,lv,r,out var h)) { unreach.Add(r.name + "(no standing floor)"); continue; }
                if (!haveRef) { refHit = h; haveRef = true; continue; }
                if (!UnityEngine.AI.NavMesh.CalculatePath(refHit.position, h.position, -1, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    unreach.Add(r.name);
            }
            sb.AppendLine("   unreachable: " + string.Join(", ", unreach));
            sb.AppendLine($"{lv.key} {lv.name}: navmesh {tri.vertices.Length} verts, bake {Time.realtimeSinceStartup - t0:0.0}s, " +
                          $"{ok}/{tried} room-to-room paths complete, mean length {(ok > 0 ? pathLen / ok : 0):0.0} m");
            yield return null;
        }
        File.WriteAllText(report, sb.ToString());
        Application.Quit();
    }

    IEnumerator Audio(string wav)
    {
        yield return new WaitForSeconds(3f);
        player.Frozen = true;
        AudioEngine.StartRecording();
        audioTest.Begin();
        yield return new WaitForSeconds(16f);
        AudioEngine.StopRecording(wav);
        Application.Quit();
    }

    // unity yaw that looks along the plan's +y axis
    static float AbsoluteYaw(HotelWorld world)
    {
        Vector3 d = world.ToUnity(0f, 1f, 0f) - world.ToUnity(0f, 0f, 0f);
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }
}
