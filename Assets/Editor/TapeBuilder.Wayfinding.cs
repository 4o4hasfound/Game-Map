using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Finding your way, the way a real hotel does it: a brass plate beside every door seen from a corridor (room
// numbers on the guest rooms, names on the rest), direction signs at the junctions you arrive at, and fire
// evacuation plans ("YOU ARE HERE") at the start of each part and at the stair heads. Filming a plan puts that
// floor on the camcorder map (MapView).
public static partial class TapeBuilder
{
    static readonly Dictionary<string, string> PlateNames = new Dictionary<string, string>
    {
        { "Manager's Office", "MANAGER" }, { "Security Office", "SECURITY" }, { "Staff Lockers", "STAFF" }, { "Front Office", "OFFICE" },
        { "Mail Room", "MAIL" }, { "Staff Stair", "STAIRS" }, { "Luggage Room", "LUGGAGE" }, { "Laundry Chute Room", "CHUTE" },
        { "Basement Stairhead", "BASEMENT" }, { "Service Lift Lobby", "SERVICE LIFT" }, { "Lift Vestibule", "SERVICE LIFT" }, { "PH Lift Lobby", "LIFT" },
        { "Telephone Room", "TELEPHONES" }, { "Bar Storage", "STORE" }, { "Ice & Vending", "ICE" }, { "Linen Chute", "CHUTE" },
        { "Linen Store", "LINEN" }, { "Maid's Office", "MAID" }, { "Staff Lounge", "STAFF" }, { "Electrical Room", "ELECTRICAL" },
        { "Recorder Room", "RECORDER" }, { "Storage Cages", "STORAGE" }, { "Coal Store", "COAL" }, { "Wine Cellar", "CELLAR" },
        { "Hotel Archive", "ARCHIVE" }, { "Generator Room", "GENERATOR" }, { "Pump Room", "PUMP ROOM" }, { "Boiler Room", "BOILER" },
        { "Basement Stair", "STAIRS" }, { "Butler's Pantry", "PANTRY" }, { "Roof Stair", "ROOF ACCESS" }, { "Plant Room", "PLANT" },
        { "Owner's Suite", "PRIVATE" }, { "Lift Machine Room", "LIFT MOTOR" }, { "Antenna Room", "ANTENNA" }, { "Siren Housing", "STORE" },
        { "Servants' Passage", "STAFF" }, { "Blue Note Bar", "BAR" }, { "Dish Pit", "KITCHEN" }, { "Cold Room", "COLD ROOM" },
    };

    static bool IsPublic(LevelLayout.Room r) => r.kind == "corridor" || r.kind == "lobby" || r.kind == "gallery" || r.kind == "outdoor";

    static string PlateText(LevelLayout.Room r)
    {
        if (r.name.StartsWith("Room ")) return r.name.Substring(5);
        if (r.name.StartsWith("Suite ")) return r.name.Substring(6);
        return PlateNames.TryGetValue(r.name, out var s) ? s : r.name.ToUpperInvariant();
    }

    static void Wayfinding()
    {
        var g = Group("Wayfinding");
        int plates = 0;
        foreach (var o in LevelLayout.Openings)
        {
            if (o.floor == 2) continue;
            if (o.kind != LevelLayout.Gap.Door && o.kind != LevelLayout.Gap.Locked && o.kind != LevelLayout.Gap.Jammed) continue;
            var m = o.Mid;
            float y = LevelLayout.FloorY(o.floor);
            var n = o.AlongX ? Vector3.forward : Vector3.right;
            var c = new Vector3(m.x, y, m.y);
            var ra = LevelLayout.RoomAt(c + n * 0.6f + Vector3.up * 0.5f);
            var rb = LevelLayout.RoomAt(c - n * 0.6f + Vector3.up * 0.5f);
            if (ra == null || rb == null) continue;
            bool aOut = IsPublic(ra), bOut = IsPublic(rb);
            if (aOut == bOut) continue;                       // a connecting door, or corridor to corridor
            var outside = aOut ? n : -n;
            var room = aOut ? rb : ra;
            string label = PlateText(room);
            var along = o.AlongX ? Vector3.right : Vector3.forward;
            var pos = c + outside * 0.085f + along * (o.Width / 2f + 0.24f) + Vector3.up * 1.52f;
            Plate(g, pos, outside, label, false);
            plates++;
        }

        // direction signs where you arrive on a floor (arrows are as you face the sign)
        Sign(g, LevelLayout.W(36.5f, 25.9f, 1, 1.95f), Vector3.back, "<  WEST WING   201 - 209\nEAST WING   211 - 219  >\n<  BALLROOM");
        Sign(g, LevelLayout.W(13.9f, 33.4f, 1, 1.95f), Vector3.left, "<  BALLROOM   LIFT   211 - 219\nWEST WING   201 - 209  >");
        Sign(g, LevelLayout.W(28.6f, 30.1f, -1, 1.95f), Vector3.forward, "<  PUMP ROOM   BOILER   COAL\nELECTRICAL   RECORDER   LAUNDRY  >\nSTORAGE . WORKSHOP: the doors behind you");
        Sign(g, LevelLayout.W(47f, 34.1f, 3, 1.95f), Vector3.forward, "<  SUITE 903   PLANT\nROOF ACCESS   SUITE 901   PANTRY  >");
        // the lobby: a standing sign outside the front office
        var post = LevelLayout.W(22.6f, 16.6f, 0);
        Box("Sign post", g, post + Vector3.up * 0.75f, new Vector3(0.06f, 1.5f, 0.06f), M("Brass_Fitting"), LProp, true, null, false);
        Sign(g, post + new Vector3(-0.04f, 1.45f, 0f), Vector3.left, "GUEST ROOMS 201 - 219\nGRAND STAIR  >\n< STAFF STAIR (back passage)");

        // fire plans: the start of each part and the stair heads
        int posters = 0;
        void Poster(int floor, Vector3 at, Vector3 nrm, string name) { FirePlan(g, floor, at, nrm, name, posters++); }
        Poster(0, LevelLayout.W(17.0f, 10.07f, 0, 1.45f), Vector3.forward, "Ground floor");
        Poster(0, LevelLayout.W(12.07f, 36.6f, 0, 1.45f), Vector3.right, "Ground floor");
        Poster(1, LevelLayout.W(33.4f, 25.93f, 1, 1.45f), Vector3.back, "Guest floor");
        Poster(1, LevelLayout.W(13.93f, 30.8f, 1, 1.45f), Vector3.left, "Guest floor");
        Poster(-1, LevelLayout.W(29.93f, 38.2f, -1, 1.45f), Vector3.left, "Basement");
        Poster(3, LevelLayout.W(51.93f, 41.2f, 3, 1.45f), Vector3.left, "Penthouse");
        Poster(4, LevelLayout.W(41.93f, 42.6f, 4, 1.45f), Vector3.left, "Roof");
        Debug.Log($"WAYFINDING: {plates} door plates, 5 signs, {posters} fire plans");
    }

    static void Plate(Transform g, Vector3 pos, Vector3 outward, string label, bool dark)
    {
        float w = Mathf.Clamp(0.06f + label.Length * 0.028f, 0.16f, 0.4f);
        bool alongX = Mathf.Abs(outward.z) > 0.5f;
        var plate = Box("Door plate " + label, g, pos, alongX ? new Vector3(w, 0.11f, 0.012f) : new Vector3(0.012f, 0.11f, w), M("Brass_Fitting"), LProp, false, null, false, false);
        var holder = new GameObject("Plate text");
        holder.transform.SetParent(plate.transform, false);
        holder.transform.position = pos + outward * 0.007f;
        holder.transform.rotation = Quaternion.LookRotation(-outward);
        var pq = holder.AddComponent<Plaque>();
        pq.textMaterial = M("Plaque_Text");
        pq.text = label;
        pq.charSize = label.Length > 5 ? 0.0068f : 0.0105f;
    }

    static void Sign(Transform g, Vector3 pos, Vector3 outward, string text)
    {
        bool alongX = Mathf.Abs(outward.z) > 0.5f;
        var board = Box("Direction sign", g, pos, alongX ? new Vector3(1.05f, 0.42f, 0.02f) : new Vector3(0.02f, 0.42f, 1.05f), M("Plastic_Device"), LProp, false, null, false, false);
        var holder = new GameObject("Sign text");
        holder.transform.SetParent(board.transform, false);
        holder.transform.position = pos + outward * 0.012f;
        holder.transform.rotation = Quaternion.LookRotation(-outward);
        var pq = holder.AddComponent<Plaque>();
        pq.textMaterial = M("Plaque_Text");
        pq.text = text;
        pq.charSize = 0.0062f;
        pq.engraved = false;
    }

    // a fire evacuation plan drawn from the layout, with a red YOU ARE HERE dot
    static void FirePlan(Transform g, int floor, Vector3 at, Vector3 nrm, string floorName, int index)
    {
        var rooms = new List<LevelLayout.Room>();
        foreach (var r in LevelLayout.Rooms) if (r.floor == floor || (r.twoStorey && r.floor + 1 == floor)) rooms.Add(r);
        var b = rooms[0].r;
        foreach (var r in rooms) { b.xMin = Mathf.Min(b.xMin, r.r.xMin); b.yMin = Mathf.Min(b.yMin, r.r.yMin); b.xMax = Mathf.Max(b.xMax, r.r.xMax); b.yMax = Mathf.Max(b.yMax, r.r.yMax); }
        float span = Mathf.Max(b.width, b.height) + 2f;
        var origin = new Vector2(b.center.x - span / 2f, b.center.y - span / 2f);
        var here = new Vector2(at.x, at.z);
        float wallT = span / 256f * 1.4f;
        var tex = Tex($"fireplan_{index}", 256, (u, v) =>
        {
            var p = origin + new Vector2(u, v) * span;
            if ((p - here).sqrMagnitude < 0.8f) return new Color(0.82f, 0.12f, 0.1f);
            if ((p - here).sqrMagnitude < 1.3f) return new Color(0.98f, 0.97f, 0.94f);
            foreach (var r in rooms)
            {
                if (!r.r.Contains(p)) continue;
                float d = Mathf.Min(Mathf.Min(p.x - r.r.xMin, r.r.xMax - p.x), Mathf.Min(p.y - r.r.yMin, r.r.yMax - p.y));
                if (d < wallT) return new Color(0.12f, 0.12f, 0.12f);
                if (r.kind == "stair") return Mathf.Repeat((p.x + p.y) * 1.2f, 1f) < 0.5f ? new Color(0.62f, 0.62f, 0.6f) : new Color(0.9f, 0.9f, 0.88f);
                if (IsPublic(r)) return new Color(0.8f, 0.87f, 0.8f);         // escape routes
                return new Color(0.97f, 0.97f, 0.95f);
            }
            return new Color(0.9f, 0.89f, 0.85f);
        });
        string matPath = $"Assets/Art/Materials/FirePlan_{index}.mat";
        Directory.CreateDirectory("Assets/Art/Materials");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", 0.35f);
        AssetDatabase.CreateAsset(mat, matPath);
        bool alongX = Mathf.Abs(nrm.z) > 0.5f;
        Box("Fire plan frame", g, at - nrm * 0.005f, alongX ? new Vector3(0.68f, 0.78f, 0.02f) : new Vector3(0.02f, 0.78f, 0.68f), M("Plastic_Beige"), LProp, false, null, false, false);
        var q = Quad("Fire plan", g, at + nrm * 0.008f + Vector3.down * 0.04f, new Vector2(0.6f, 0.6f), Quaternion.LookRotation(-nrm), mat);
        q.layer = LInteract;
        var bc = q.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(1f, 1f, 0.3f);
        var fp = q.AddComponent<FloorPlan>();
        fp.floor = floor; fp.floorName = floorName;
        var title = new GameObject("Fire plan title");
        title.transform.SetParent(g, false);
        title.transform.position = at + nrm * 0.012f + Vector3.up * 0.32f;
        title.transform.rotation = Quaternion.LookRotation(-nrm);
        var pq = title.AddComponent<Plaque>();
        pq.textMaterial = M("Plaque_Text");
        pq.text = "FIRE EVACUATION PLAN   " + floorName.ToUpperInvariant() + "\nYOU ARE HERE (red)";
        pq.charSize = 0.0045f;
    }
}
