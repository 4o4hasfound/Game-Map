using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Writes design/map_export.json from the built scene for the 2D map viewer (design/map_viewer.py): rooms with their
// purpose and decay, openings (doors, bolts, locks), stairs, and every object that matters to play: lures, machines,
// pickups, hiding places, notes, phones, cameras and monitors, lamps (with their flicker pattern), the key, the
// breaker, the recorder; the listener's start, patrol routes and lift-shaft lairs; spawn and checkpoints.
//   Unity -batchmode -quit -projectPath TheTape -executeMethod MapExport.Run
public static class MapExport
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        var sb = new StringBuilder();
        sb.Append("{\n\"rooms\":[");
        sb.Append(string.Join(",\n", LevelLayout.Rooms.Select(r =>
            $"{{\"name\":{Q(r.name)},\"floor\":{r.floor},\"kind\":{Q(r.kind)},\"mat\":{Q(r.floorMat)},\"x0\":{F(r.r.xMin)},\"z0\":{F(r.r.yMin)},\"x1\":{F(r.r.xMax)},\"z1\":{F(r.r.yMax)},\"two\":{B(r.twoStorey)},\"noFloor\":{B(r.noFloor)},\"decay\":{Q(r.decay)},\"purpose\":{Q(r.purpose)}}}")));
        sb.Append("],\n\"openings\":[");
        sb.Append(string.Join(",\n", LevelLayout.Openings.Select(o =>
            $"{{\"name\":{Q(o.name)},\"floor\":{o.floor},\"kind\":{Q(o.kind.ToString())},\"ax\":{F(o.a.x)},\"az\":{F(o.a.y)},\"bx\":{F(o.b.x)},\"bz\":{F(o.b.y)},\"key\":{Q(o.key ?? "")},\"stuck\":{B(o.stuck)},\"bolt\":{B(o.bolt)}}}")));
        sb.Append("],\n\"voids\":[");
        sb.Append(string.Join(",", LevelLayout.Void.Select(v => $"[{F(v.xMin)},{F(v.yMin)},{F(v.xMax)},{F(v.yMax)}]")));
        sb.Append("],\n\"stairs\":[");
        sb.Append(string.Join(",", LevelLayout.Stairs.Select(s => $"[{F(s.bottom.x)},{F(s.bottom.y)},{F(s.bottom.z)},{F(s.top.x)},{F(s.top.y)},{F(s.top.z)},{F(s.width)}]")));
        sb.Append("],\n\"objects\":[");
        var items = new List<string>();
        void Add(string type, Vector3 p, string label, string detail)
        {
            int floor = LevelLayout.FloorOf(p + Vector3.up * 0.2f);
            if (floor == 2) p -= LevelLayout.BedroomOrigin;
            items.Add($"{{\"type\":{Q(type)},\"floor\":{floor},\"x\":{F(p.x)},\"z\":{F(p.z)},\"label\":{Q(label)},\"detail\":{Q(detail)}}}");
        }
        foreach (var d in Object.FindObjectsByType<HingeDoor>(FindObjectsSortMode.None))
            Add("door", d.transform.position + d.leaf.right * 0.45f, d.portalName,
                (d.locked ? "LOCKED: needs the " + d.key + " key. " : "") + (d.stuck ? "The lock sticks: hold E to jiggle it quietly (4 s), or tap to shove it (58 dB). " : "") +
                (d.bolted ? "Bolted from inside at the start. " : "") + "Tap E: open/close (a creak, heard nearby). Hold E: ease it silently; let go to leave it part open.");
        foreach (var b in Object.FindObjectsByType<DoorBolt>(FindObjectsSortMode.None)) Add("bolt", b.transform.position, "Bolt: " + b.door.portalName, "Slide the bolt (door closed): it can't open this door. It bangs on it and gives up; in the finale it breaks the bolt after four blows.");
        foreach (var x in Object.FindObjectsByType<Phone>(FindObjectsSortMode.None)) Add("phone", x.transform.position, x.room.StartsWith("payphone") ? "Payphone" : x.room == "Gallery" ? "House phone (north gallery)" : "Phone, Room " + x.room,
            (x.room == "Gallery" ? "Rings by itself early on: watch it come out of the ballroom and tear it off. " : "") + "A ringing phone (75 dB) brings it; it tears the receiver off. Use a phone to ring another room.");
        foreach (var x in Object.FindObjectsByType<SwitchboardButton>(FindObjectsSortMode.None)) Add("switch", x.transform.position, "Switchboard: " + x.room, "Rings " + x.room + "'s phone from the front office.");
        foreach (var x in Object.FindObjectsByType<HideSpot>(FindObjectsSortMode.None)) Add("hide", x.transform.position, "Hide: " + x.kind, "Hide here. It can't see you; keep still and hold your breath when it is close.");
        foreach (var x in Object.FindObjectsByType<Readable>(FindObjectsSortMode.None)) Add(x.isLetter ? "letter" : "note", x.transform.position, x.title, x.text);
        foreach (var x in Object.FindObjectsByType<LightSwitch>(FindObjectsSortMode.None)) Add("light", x.transform.position, x.pullCord ? "Pull cord" : "Light switch", "Turns lights on or off (a click it can hear, 35 dB).");
        foreach (var x in Object.FindObjectsByType<Lamp>(FindObjectsSortMode.None))
            Add(x.dead ? "lamp_off" : !x.powered ? "lamp_power" : x.on ? "lamp_on" : "lamp_off", x.transform.position, x.name + (x.dead ? " (dead)" : x.pattern != Lamp.Pattern.Steady ? " (" + x.pattern.ToString().ToLowerInvariant() + ")" : ""),
                x.dead ? "A dead fitting: it never lights." : !x.powered ? "Dead until the main breaker is thrown; then it struggles to strike." : x.on ? "Lit. Pattern: " + x.pattern : "Off at the start.");
        foreach (var x in Object.FindObjectsByType<CctvCamera>(FindObjectsSortMode.None)) Add("cctv", x.transform.position, x.label, "CCTV camera (infrared). Watch it on the monitors to see where it is.");
        foreach (var x in Object.FindObjectsByType<MonitorScreen>(FindObjectsSortMode.None)) if (x.transform.position.x < 150f) Add("monitor", x.transform.position, "Monitor: " + (x.source ? x.source.label : "?"), x.on ? "Shows " + (x.source ? x.source.label : "?") : "Shows " + (x.source ? x.source.label : "?") + " once the power is on.");
        foreach (var x in Object.FindObjectsByType<MirrorView>(FindObjectsSortMode.None)) Add("mirror", x.transform.position, "Mirror", "Shows you, with your face worn by tape damage.");
        foreach (var x in Object.FindObjectsByType<Dresser>(FindObjectsSortMode.None)) Add("dresser", x.transform.position, "Dresser", "Hold E to pull it out: every pull scrapes (55 dB). The key is behind it.");
        foreach (var x in Object.FindObjectsByType<KeyItem>(FindObjectsSortMode.None))
        {
            string detail = x.keyId == "code_a" ? "Part one: half of the safe code, behind the dresser in Room 214." :
                            x.keyId == "code_b" ? "Part one: the other half of the safe code, in the previous viewer's bag in Room 209 (through 207)." :
                            x.keyId == "basement" ? "Part one: the basement key, inside the manager's safe. Opens the basement door (north staff corridor)." :
                            x.keyId == "fuse" ? "Part two: the spare main fuse, in the end cage. The breaker needs it." :
                            x.keyId == "liftkey" ? "Part two: the lift key, on the recorder room desk. The lift call panel needs it." :
                            x.keyId == "roofcard" ? "Part three: the owner's keycard, in the study desk. Opens the roof door." : x.label;
            Add(x.keyId == "basement" ? "key" : x.keyId == "fuse" || x.keyId == "liftkey" || x.keyId == "roofcard" ? "key" : "note", x.transform.position, Cap(x.label), detail);
        }
        foreach (var x in Object.FindObjectsByType<Safe>(FindObjectsSortMode.None)) Add("safe", x.transform.position, "Manager's wall safe", "Part one: needs both halves of the code. The basement key is inside.");
        foreach (var x in Object.FindObjectsByType<LiftPanel>(FindObjectsSortMode.None)) Add("liftpanel", x.transform.position, "Lift call panel", "Part two: needs power and the lift key. Calling rings a bell for 30 s (64 dB every 2 s): it comes, hurrying, and breaks bolts. Draw it away first (the generator holds it 14 s).");
        if (false) Add("key", Vector3.zero, "The basement key", "Behind the dresser in Room 214. Opens the basement door (ground floor, north staff corridor).");
        foreach (var x in Object.FindObjectsByType<Breaker>(FindObjectsSortMode.None)) Add("breaker", x.transform.position, "Main breaker", "Hold E to throw it: 72 dB, heard across the basement. Powers the basement lights and the recorder room.");
        foreach (var x in Object.FindObjectsByType<SecurityVcr>(FindObjectsSortMode.None)) Add("goal", x.transform.position, "Master VCR (the goal)", "Needs power. Hold E 12 s to eject the tape: it grinds (60 dB each second) and it comes. Bolt the door first.");
        foreach (var x in Object.FindObjectsByType<BedroomVcr>(FindObjectsSortMode.None)) Add("vcr", x.transform.position, "Your VCR", "Put the tape in.");
        foreach (var x in Object.FindObjectsByType<TapeItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Add("tape", x.transform.position, "The envelope", "It comes through the letterbox once you reach the front door: the tape and a card, \"PLAY ME\".");
        // everything in your house you can use (eat, drink, switch, open, sit on, look at)
        foreach (var x in Object.FindObjectsByType<Fiddle>(FindObjectsSortMode.None))
            Add("fiddle", x.transform.position, x.Prompt, string.Join("\n", x.lines ?? new string[0]));
        foreach (var x in Object.FindObjectsByType<NoiseDevice>(FindObjectsSortMode.None))
        {
            if (x.auto) Add("machine", x.transform.position, Cap(x.label), x.description + (x.mask > 0 ? $" Covers noise within {x.maskRadius:0} m by up to {x.mask:0} dB." : ""));
            else Add("lure", x.transform.position, Cap(x.label), x.description + $" ({x.db:0} dB at 1 m)" + (x.mask > 0 ? $" Covers noise within {x.maskRadius:0} m." : ""));
        }
        foreach (var x in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)) Add("pickup", x.transform.position, x.kind == "battery" ? "Camera battery" : x.kind == "radio" ? "Portable radio" : $"Bottles x{x.count}", x.description);
        foreach (var x in Object.FindObjectsByType<ChuteHatch>(FindObjectsSortMode.None)) Add("chute", x.transform.position, "Laundry chute", "Drop a bottle down: it smashes in the basement laundry (72 dB), far from you.");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == "Ceiling hole") Add("hole", t.position, "Fallen ceiling", "Rubble below: very loud underfoot (60 dB). Plaster still falls now and then.");
        foreach (var t in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (t.name == "Glass_Shards") Add("glass", t.transform.position, "Broken glass", "Loud underfoot (60 dB).");
            if (t.name == "Water_Puddle") Add("water", t.transform.position, "Water", "Splashes underfoot (54 dB).");
            if (t.name == "Rubble") Add("rubble", t.transform.position, "Rubble", "Loud underfoot (60 dB).");
            if (t.name == "Rug runner") Add("rug", t.transform.position, "Rug runner", "A quiet path across the loud marble (30 dB instead of 44).");
        }
        Add("monster", LevelLayout.MonsterStart, "It (start)", "Waits in the ballroom, calm for the first 40 s: only loud sounds reach it. Wander 1.0 m/s, investigate 1.45 m/s, rush 2.7 m/s (you: walk 1.6, run 3.8 for 8 s).");
        foreach (int f in new[] { -1, 0, 1, 3, 4 }) Add("lair", LevelLayout.Lair(f), "Service lift (its shaft)", "It leaves the floor into the lift shaft for 20-35 s, then comes out of the lift doors on your floor. You hear it climbing.");
        Add("spawn", LevelLayout.ReceptionSpawn, "Start: the front office", "You arrive here. The letter is on the desk.");
        Add("checkpoint", LevelLayout.GuestCheckpoint, "Checkpoint (after the key)", "Caught after taking the key: the tape skips back to the top of the staff stair.");
        Add("checkpoint", LevelLayout.BasementCheckpoint, "Part two starts here", "The foot of the basement stair (it collapses behind you). Caught in the basement: the tape skips back here.");
        Add("checkpoint", LevelLayout.Stage3Spawn, "Part three starts here", "Out of the lift on the penthouse floor (the lift dies). Caught before the roof door: back here.");
        Add("checkpoint", LevelLayout.Stage3Checkpoint, "Checkpoint (roof door)", "After the keycard: caught on the roof, the tape skips back to the roof stair.");
        Add("monster", LevelLayout.MonsterStart2, "It (part two)", "Rests in the coal store when part two starts. Hears a little more, investigates at 1.6 m/s.");
        Add("monster", LevelLayout.MonsterStart3, "It (part three)", "In the lift motor room on the roof, next to the antenna room. Investigates at 1.75 m/s, rushes 3.0 m/s.");
        Add("spawn", LevelLayout.BedroomSpawn, "Prologue start", "Your bedroom.");
        sb.Append(string.Join(",\n", items));
        sb.Append("],\n\"patrol\":{");
        sb.Append(string.Join(",", LevelLayout.Patrol.Select(kv => $"\"{kv.Key}\":[" + string.Join(",", kv.Value.Select(w => $"[{F(w.x)},{F(w.z)}]")) + "]")));
        sb.Append("}\n}\n");
        var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../design/map_export.json"));
        File.WriteAllText(path, sb.ToString());
        Debug.Log("MAP EXPORTED: " + path + "  objects " + items.Count);
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    static string B(bool b) => b ? "true" : "false";
    static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
}
