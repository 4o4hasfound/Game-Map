using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// -acoustictest <report>: the spec's audio acceptance tests, measured in the built game.
//  1  a radio in a guest room; the listener walks from inside, through the doorway, along the corridor and back.
//     Largest frame-to-frame level change must stay small (no boundary cut-off).
//  2  the same radio from behind a wall (next room) vs. from the open doorway.
//  3  the reverberation the geometry produces in very different spaces.
//  5  the door between radio and listener is closed slowly: level and tone change continuously.
public class AcousticTest : MonoBehaviour
{
    public PlayerMotor player;
    public VHSCamera cam;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-acoustictest") StartCoroutine(Run(args[i + 1]));
    }

    static Vector3 Centre(HotelWorld w, LevelInfo lv, RoomInfo r)
    {
        var L = lv.layers[r.layer];
        float sx = 0, sy = 0;
        int c = 0;
        for (int j = 0; j < L.h; j++)
            for (int i = 0; i < L.w; i++)
                if (L.cells[j * L.w + i] == r.id) { sx += i + 0.5f; sy += j + 0.5f; c++; }
        // nearest real cell of the room to its centroid (L-shaped rooms)
        float cx = sx / c, cy = sy / c, best = 1e9f, bx = cx, by = cy;
        for (int j = 0; j < L.h; j++)
            for (int i = 0; i < L.w; i++)
                if (L.cells[j * L.w + i] == r.id)
                {
                    float d = (i + 0.5f - cx) * (i + 0.5f - cx) + (j + 0.5f - cy) * (j + 0.5f - cy);
                    if (d < best) { best = d; bx = i + 0.5f; by = j + 0.5f; }
                }
        return w.ToUnity(lv.ox + bx, by, L.z);
    }

    static void Put(Rigidbody rb, Vector3 p)
    {
        rb.position = p;
        rb.transform.position = p;
        Physics.SyncTransforms();
    }

    IEnumerator Run(string report)
    {
        var sb = new StringBuilder();
        var w = HotelWorld.Instance;
        player.Frozen = true;
        var rb = player.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
        w.SetLevel(0);
        yield return new WaitForSeconds(1.5f);
        var lv = w.levels[0];
        var room = lv.rooms.Find(r => r.name == "Room 703");
        var hall = lv.rooms.Find(r => r.name == "North Hall") ?? lv.rooms.Find(r=>r.name=="Sea Gallery");
        var nextRoom = lv.rooms.Find(r => r.name == "Room 704");
        PortalInfo door = null;
        foreach (int pi in room.portals)
        {
            var p = lv.portals[pi];
            if ((p.a == hall.id || p.b == hall.id) && p.door) door = p;
        }
        Vector3 rc = Centre(w, lv, room);
        var go = new GameObject("TestRadio");
        go.transform.position = rc + Vector3.up * 0.8f;
        go.AddComponent<AudioSource>();
        var radio = go.AddComponent<SpatialVoice>();
        radio.volume = 0.8f;
        radio.minDistance = 1f;
        radio.maxDistance = 30f;
        radio.label = "test radio";
        radio.Play(SoundBank.Get("radio_station_0"), true, 0f);
        // hold the door wide open
        door.door.Held=true;
        door.door.OpenAway(rc);
        yield return new WaitForSeconds(2f);

        // ---- test 1: walk out and along the corridor and back
        Vector3 dp = door.pos;
        dp.y = rc.y;
        Vector3 outDir = (dp - rc);
        outDir.y = 0f;
        outDir.Normalize();
        Vector3 hallPoint = dp + outDir * 1.0f;
        Vector3 along = Vector3.Cross(Vector3.up, outDir).normalized;
        Vector3[] route = { rc, dp, hallPoint, hallPoint + along * 10f, hallPoint, dp, rc };
        sb.AppendLine("TEST 1  radio in Room 703, door open; listener walks room -> doorway -> corridor 10 m -> back");
        Put(player.GetComponent<Rigidbody>(), rc);
        yield return new WaitForSeconds(1.5f);          // settle after the teleport before measuring
        float maxJump = 0f, prev = float.NaN;
        string lastKind = "";
        for (int seg = 0; seg < route.Length - 1; seg++)
        {
            float len = Vector3.Distance(route[seg], route[seg + 1]);
            for (float t = 0f; t < len; t += 1.4f * Time.deltaTime)
            {
                Put(rb, Vector3.Lerp(route[seg], route[seg + 1], t / len));
                cam.yaw = Mathf.Atan2(outDir.x, outDir.z) * Mathf.Rad2Deg;
                yield return null;
                float g = radio.LastGainDb;
                if (!float.IsNaN(prev)) maxJump = Mathf.Max(maxJump, Mathf.Abs(g - prev));
                prev = g;
                if (radio.LastPath != lastKind)
                {
                    lastKind = radio.LastPath;
                    sb.AppendLine($"   at {Vector3.Distance(player.transform.position, rc):0.0} m from radio: {g:0.0} dB  [{radio.LastPath}]  " +
                                  $"space RT60 {GeometricAcoustics.Instance.Space.Rt60:0.00}s");
                }
            }
        }
        sb.AppendLine($"   largest level change between frames: {maxJump:0.00} dB  -> {(maxJump < 2f ? "PASS" : "FAIL")}");

        // ---- test 2: behind the wall (Room 704) vs. in the doorway
        Put(rb, Centre(w, lv, nextRoom));
        yield return new WaitForSeconds(1.2f);
        float wall = radio.LastGainDb;
        string wallPath = radio.LastPath;
        Put(rb, hallPoint);
        yield return new WaitForSeconds(1.2f);
        float open = radio.LastGainDb;
        sb.AppendLine($"TEST 2  behind the wall (Room 704): {wall:0.0} dB [{wallPath}];  outside the open door: {open:0.0} dB [{radio.LastPath}]; door {door.door.Angle:0.0} deg" +
                      $"  -> {(open - wall > 10f ? "PASS" : "FAIL")}");

        // ---- test 5: close the door slowly while standing outside it
        sb.AppendLine("TEST 5  door closed slowly, listener 1 m outside");
        float jump5 = 0f;
        prev = radio.LastGainDb;
        door.door.Held = true;    // hold it so it keeps moving slowly
        for (float t = 0f; t < 7f; t += Time.deltaTime)
        {
            door.door.SwingTowardsShut(28f);
            yield return null;
            float g = radio.LastGainDb;
            jump5 = Mathf.Max(jump5, Mathf.Abs(g - prev));
            prev = g;
            if (Mathf.Abs(door.door.Angle) < 0.5f) break;
            if ((int)(t * 4) != (int)((t - Time.deltaTime) * 4))
                sb.AppendLine($"   door {Mathf.Abs(door.door.Angle):0} deg open: {g:0.0} dB [{radio.LastPath}]");
        }
        door.door.Held = false;
        yield return new WaitForSeconds(1f);
        sb.AppendLine($"   open {open:0.0} dB -> shut {radio.LastGainDb:0.0} dB [{radio.LastPath}], largest step {jump5:0.00} dB" +
                      $"  -> {(jump5 < 3f && open - radio.LastGainDb > 6f ? "PASS" : "CHECK")}");

        // ---- test 3: reverberation from geometry in different spaces
        sb.AppendLine("TEST 3  reverberation computed from the geometry around the listener");
        string[][] spaces =
        {
            new[] { "0", "Room 701" }, new[] { "0", "Room 701 Bath" }, new[] { "0", "North Hall" }, new[] { "0", "Sky Lounge" },
            new[] { "0", "Plumbing Gallery" }, new[] { "1", "Grand Lobby" }, new[] { "1", "Library" }, new[] { "2", "Boiler Hall" },
            new[] { "2", "Light Well" }, new[] { "2", "Pipe Gallery" },
        };
        if (lv.rooms.Exists(r=>r.name=="Sea Gallery")) spaces = new[] {
            new[]{"0","Room 701"}, new[]{"0","Sea Gallery"}, new[]{"0","Collapsed Palm Conservatory"},
            new[]{"0","Plumbing Back Route"},new[]{"1","Winter Garden"},new[]{"1","Bridge Reading Library"},
            new[]{"2","Drained Thermal Pool"},new[]{"2","Water Filtration Hall"},new[]{"2","Dry Pipe Bypass"}
        };
        radio.Stop();
        foreach (var sp in spaces)
        {
            int li = int.Parse(sp[0]);
            w.SetLevel(li);
            var L = w.levels[li];
            var r = L.rooms.Find(x => x.name == sp[1]);
            if (r == null) continue;
            Put(rb, Centre(w, L, r));
            yield return new WaitForSeconds(1.2f);
            var s = GeometricAcoustics.Instance.Space;
            sb.AppendLine($"   {sp[1],-20} RT60 {s.Rt60:0.00}s  high {s.Rt60High:0.00}s  mean free path {s.MeanFree:0.0} m  absorption {s.Absorb:0.00}  open {s.Openness:0.00}");
        }
        File.WriteAllText(report, sb.ToString());
        Application.Quit();
    }
}
