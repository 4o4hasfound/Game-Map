using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The guest floor, the basement, your bedroom, the CCTV cameras, and the notes (the previous viewer's trail).
public static partial class TapeBuilder
{
    // ================================================================== the notes
    const string Letter =
        "If you're reading this, you put the tape in. So did I.\n\n" +
        "This is the hotel's last night, 31 October 2003. Something walks these floors. It HEARS you, and it only sees you in bright light.\n\n" +
        "· Crouch to move without a sound. Running carries.\n" +
        "· Keep to the dark, and mind your camera light (F).\n" +
        "· Film the fire plans on the walls (E). M shows the map.\n\n" +
        "The way out starts in the BASEMENT. Its key is in the MANAGER'S SAFE. The code is torn in two: ROOM 214, behind the dresser, and ROOM 209, through the hole in 207.\n\n" +
        "— the one before you";
    const string SecurityNote =
        "The cameras still work. Look here before you go anywhere.\n\n" +
        "When it's calm it waits in the ballroom upstairs (CAM 2 sees the gallery outside it).\n\n" +
        "If it isn't on any screen, it's in the service lift shaft. You'll hear it climbing. It comes out of the lift doors on whatever floor it likes, usually yours.\n\n— me";
    const string ManagerLog =
        "22:40  Last guests checked in. The hotel closes tomorrow.\n" +
        "23:10  Complaints: knocking inside the service lift shaft.\n" +
        "23:41  Security: camera 1 shows someone standing in the lobby. The lobby is empty.\n" +
        "23:58  The figure on camera 1 has no face. It is walking towards the camera.\n" +
        "00:00  The master recorder will not stop. We are leaving it running.\n" +
        "00:06  Basement key in my safe. I have torn the combination in two and hidden the halves upstairs (214, and the guest in 209 has the other). Nobody goes down there tonight.\n\n(The log ends there.)";
    const string MailNote =
        "Letters for guests who never checked out. Every name has been scribbled out, and every envelope is stamped\n\nRETURN TO SENDER: NO SUCH GUEST\n\n" +
        "Postmarks 2006, 2011, 2015, 2019, 2023.\nThe newest has today's date. The name on it is a smear of tape snow.";
    const string BasementNote =
        "Down there:\n\n" +
        "· The lights are dead. The MAIN BREAKER (electrical room, west end) has no fuse: the spare is in the STORAGE CAGES. Throwing it is LOUD.\n" +
        "· The recorder room isn't the end. The cable from its machine goes up the lift shaft, all the way to the roof. The LIFT KEY is on the engineer's desk in there.\n" +
        "· The lift call panel is in the PUMP ROOM. The car takes half a minute to come down and the bell rings the whole time. It comes for the bell, and it breaks bolts when it's like that.\n" +
        "· Start the GENERATOR first (south-east). It stands over the generator a long time before it tears it apart.\n" +
        "· The boiler roars every minute or so. While it roars, it can't hear you.\n\n— me";
    const string Note203 = "Every guest room has a bolt on the inside of the door. It can't open a bolted door. It hits it a few times and goes away.\n\nA bolted room with a connecting door is a good way to lose it.";
    const string Note205 = "I switched the TV on and hid in the bath. It came straight in, stood over the TV listening, and smashed it.\n\nIt goes for the loudest thing it can hear.";
    const string Note214 = "Half the safe code is behind the dresser. Pulling the dresser out scrapes, loud.\n\nDo it when it's far away, or make a noise somewhere else first. The phone by the bed rings other rooms.";
    const string Note209 = "My bag. I came back up here to hide after the basement. The other half of the safe code is in the side pocket.\n\nThe guest floor is safest when it's downstairs. Footsteps on marble: it's in the lobby. Footsteps on carpet: it's close.";
    const string HousekeepingMemo = "STAFF NOTICE, 31.10.2003\n\nDo not use the service lift. Maintenance reports something in the shaft.\n\nNight staff: if you hear the lift moving, it is coming up. Get off the corridor.\n\n— Management";
    const string WorkshopLog = "MAINTENANCE\n\nMain fuse blown 31.10, spare is in the end cage (storage).\nGenerator (east end): runs 60 s. Very loud.\nWashing machines (laundry): 45 s a load.\nBoiler: cycles by itself, about half a minute on, half off. You can't hear a thing in there while it runs.\nSump pump: the same, quieter.";
    const string ArchiveNote = "Shelves of video tapes. Every label says 31.10.2003. Every one is a copy of the same night.\n\nThe last one on the shelf has no name on it yet.";
    const string LoungeNote = "To the night porter:\n\nThe TV in here carries down the whole corridor. Keep it low.\n\nAnd stop leaving the bolt across on 213. We can't get in to clean.";

    // ================================================================== guest floor
    static void GuestFloor(Refs refs)
    {
        // ---------------- galleries round the atrium
        var g = Group("Galleries");
        foreach (var z in new[] { 12f, 20f }) { Sconce(g, W(20.08f, z, 1, 2.0f), z == 12f ? Lamp.Pattern.Flicker : Lamp.Pattern.Steady); Sconce(g, W(39.92f, z, 1, 2.0f), z == 12f ? Lamp.Pattern.Steady : Lamp.Pattern.Dying); }
        Sconce(g, W(24f, 25.92f, 1, 2.0f), Lamp.Pattern.Steady);
        Sconce(g, W(36f, 25.92f, 1, 2.0f), Lamp.Pattern.Flicker);
        Sconce(g, W(25f, 2.08f, 1, 2.0f), Lamp.Pattern.Dying);
        Sconce(g, W(35f, 2.08f, 1, 2.0f), Lamp.Pattern.Steady, false);
        F(g, "Phone table", W(33f, 25.65f, 1, 0.38f), new Vector3(0.7f, 0.76f, 0.5f), "Wood_Furniture");
        MakePhone(g, W(33f, 25.6f, 1, 0.76f), 180f, "Gallery");
        F(g, "Bench", W(26f, 25.6f, 1, 0.25f), new Vector3(1.6f, 0.5f, 0.5f), "Fabric_Cover");
        F(g, "Planter", W(20.6f, 25.4f, 1, 0.3f), new Vector3(0.5f, 0.6f, 0.5f), "Brass_Fitting");
        for (int i = 0; i < 3; i++) F(g, "Writing desk", W(24f + i * 6f, 2.4f, 1, 0.38f), new Vector3(1.2f, 0.76f, 0.6f), "Wood_Furniture");
        F(g, "Chair (overturned)", W(31f, 3.6f, 1, 0.25f), new Vector3(0.45f, 0.45f, 0.9f), "Wood_Furniture", 70f);
        Emit(W(30f, 5f, 1, 2.4f), "amb_rain_roof", 0.35f);

        // ---------------- west corridor (fallen ceiling at the north end), west cross hall
        g = Group("West Corridor");
        var wp = new[] { Lamp.Pattern.Steady, Lamp.Pattern.Flicker, Lamp.Pattern.Dying, Lamp.Pattern.Steady, Lamp.Pattern.Strobe };
        for (int i = 0; i < 5; i++) Sconce(g, W(12.08f, 4f + i * 7f, 1, 2.0f), wp[i], i != 3);
        RubblePile(g, 13.4f, 34.5f, 1, 0.6f);
        // north of the ice room's door (z 34.55 to 35.45): it used to lie right across it, 0.8 m high, and the ice room
        // (the vending machine lure) could not be entered
        F(g, "Fallen beam", W(13.43f, 36.9f, 1, 0.5f), new Vector3(0.9f, 0.3f, 2.0f), "Wood_Furniture", 0f).transform.rotation = Quaternion.Euler(0, 0f, 22f);
        Sconce(Group("West Cross Hall"), W(17f, 25.92f, 1, 2.0f), Lamp.Pattern.Flicker);

        // ---------------- guest rooms
        foreach (var num in new[] { "201", "203", "205", "202", "204", "206", "208", "212", "214", "216", "218", "211", "213", "215", "217", "219" }) GuestRoom(num, refs);
        BurntRooms();

        // ---------------- housekeeping, ice & vending
        g = Group("Housekeeping");
        Tube(g, W(17f, 29f, 1, 2.65f), 5f, Lamp.Pattern.Flicker, true, 0.8f);
        MakeHide(g, "Linen cart", W(18.6f, 27.2f, 1, 0.5f), new Vector3(1.0f, 1.0f, 0.6f), 0f, "cart", new Vector3(0, 0.1f, 0f), M("Fabric_Bed"));
        MakeHide(g, "Linen cart", W(18.6f, 30.6f, 1, 0.5f), new Vector3(1.0f, 1.0f, 0.6f), 0f, "cart", new Vector3(0, 0.1f, 0f), M("Fabric_Bed"));
        F(g, "Shelves", W(14.4f, 27.3f, 1, 1.0f), new Vector3(0.5f, 2.0f, 2.4f), "Wood_Furniture");     // clear of the door (z 28.55)
        Box("Photo board", g, W(17f, 31.92f, 1, 1.6f), new Vector3(1.6f, 0.9f, 0.04f), M("Wood_Furniture"), LProp, true);
        for (int i = 0; i < 6; i++)
        {
            var px = W(16.45f + (i % 3) * 0.55f, 31.89f, 1, 1.82f - (i / 3) * 0.42f);
            Box("Photo", g, px, new Vector3(0.36f, 0.34f, 0.01f), M("Paper_Note"), LProp, false);
            Quad("Face", g, px + Vector3.back * 0.007f + Vector3.up * 0.03f, new Vector2(0.16f, 0.2f), Quaternion.LookRotation(Vector3.forward), M("Blob_Face"));
        }
        WallNote(g, "Staff notice", W(14.08f, 30.5f, 1, 1.5f), Vector3.right, "STAFF NOTICE", HousekeepingMemo, false);

        g = Group("Ice & Vending");
        MakeLamp(g, "Vending glow", W(19.5f, 34f, 1, 1.2f), 4f, 0.6f, new Color(0.8f, 0.95f, 1f), true, false, new Vector3(0.1f, 1.4f, 0.8f), Lamp.Pattern.Vending);
        Device(g, "vending machine", W(19.55f, 34f, 1, 0.95f), new Vector3(0.8f, 1.9f, 1.0f), "Metal_Device", new[] { "vending_buy" }, 62f, 0.8f, 0f, oneShot: true, uses: 3,
            desc: "Buy a can: one loud thump (62 dB). Three uses.", startClip: "elev_button");
        F(g, "Ice machine", W(19.5f, 36.6f, 1, 0.7f), new Vector3(0.8f, 1.4f, 1.0f), "Metal_Lift");
        AutoMachine(g, "ice machine", W(19.3f, 36.6f, 1, 0.8f), "amb_fridge", 9f, 4f, new Vector2(3f, 99999f), 0.5f, "The ice machine hums all the time: it covers your steps right next to it.");

        // ---------------- ballroom: where it waits. Piano, stage curtains, three ways out.
        g = Group("Ballroom");
        Sconce(g, W(20.08f, 34f, 1, 2.0f), Lamp.Pattern.Dying, true, 6f, 0.7f);
        Sconce(g, W(39.92f, 30f, 1, 2.0f), Lamp.Pattern.Flicker, true, 6f, 0.7f);
        MakeLamp(g, "Stage light", W(26f, 36.5f, 1, 2.6f), 6f, 0.6f, new Color(1f, 0.5f, 0.35f), true, false, null, Lamp.Pattern.Dying);
        F(g, "Stage", W(26f, 36.6f, 1, 0.25f), new Vector3(8f, 0.5f, 2.8f), "Wood_Floor");
        MakeHide(g, "Stage curtain", W(22.3f, 36.4f, 1, 1.65f), new Vector3(0.6f, 2.3f, 2.2f), 0f, "curtain", new Vector3(0f, 0.25f, -0.3f), M("Fabric_Curtain"), 90f, false);  // on the stage
        Device(g, "grand piano", W(35f, 35.2f, 1, 0.55f), new Vector3(1.6f, 1.1f, 2.0f), "Plastic_Device", new[] { "piano_tune_grand" }, 74f, 0.7f, 25f,
            desc: "The grand piano: a loud lure on the guest floor (25 s).", startClip: "light_tick");
        var rt = new[] { (25f, 30f), (31f, 29f), (36f, 31f), (28f, 33.4f) };
        foreach (var (x, z) in rt)
        {
            F(g, "Round table", W(x, z, 1, 0.38f), new Vector3(1.4f, 0.76f, 1.4f), "Fabric_Cloth");
            for (int k = 0; k < 3; k++) F(g, "Chair", W(x + Mathf.Cos(k * 2.1f) * 1.35f, z + Mathf.Sin(k * 2.1f) * 1.35f, 1, 0.45f), new Vector3(0.45f, 0.9f, 0.45f), "Wood_Furniture", Rnd(0, 360));
        }
        Emit(W(30f, 32f, 1, 2.4f), "amb_creak", 0.3f, false, new Vector2(10f, 25f));

        // ---------------- north corridor, linen chute, lift vestibule, east cross hall, east corridor
        g = Group("Guest North Corridor");
        for (int i = 0; i < 5; i++) Sconce(g, W(15f + i * 8f, 38.08f, 1, 2.0f), i % 2 == 0 ? Lamp.Pattern.Flicker : Lamp.Pattern.Steady);
        g = Group("Linen Chute");
        Bulb(g, W(15f, 42f, 1, 2.5f), 4.5f, 0.5f, Lamp.Pattern.Buzz);
        var hatch = Box("Chute hatch", g, W(15f, 43.85f, 1, 1.0f), new Vector3(0.7f, 0.7f, 0.08f), M("Metal_Device"), LInteract, true, null, false, false);
        var hc = hatch.GetComponent<BoxCollider>(); hc.isTrigger = true; hc.size = new Vector3(0.9f, 0.9f, 0.4f);
        hatch.AddComponent<ChuteHatch>().bottom = W(13.5f, 42.5f, -1, 0.5f);
        F(g, "Laundry bags", W(13f, 41f, 1, 0.3f), new Vector3(0.9f, 0.6f, 0.8f), "Fabric_Bed");
        g = Group("Lift Vestibule");
        Tube(g, W(45f, 42f, 1, 2.65f), 5f, Lamp.Pattern.Strobe, true, 0.7f);
        Sconce(Group("East Cross Hall"), W(43f, 25.92f, 1, 2.0f), Lamp.Pattern.Dying);
        g = Group("East Corridor");
        for (int i = 0; i < 5; i++) Sconce(g, W(47.92f, 4f + i * 8f, 1, 2.0f), i == 1 ? Lamp.Pattern.Dying : i % 2 == 0 ? Lamp.Pattern.Steady : Lamp.Pattern.Flicker);
        Puddle(g, 47f, 10.5f, 1, 1.6f, 3.2f);       // wet carpet near 214: squelches
        Puddle(g, 47f, 16f, 1, 1.4f, 1.4f);

        // ---------------- linen store, maid's office, staff lounge
        g = Group("Linen Store");
        Tube(g, W(43f, 29f, 1, 2.65f), 5f, Lamp.Pattern.Dying, true, 0.7f);
        MakeHide(g, "Linen shelves", W(40.4f, 29f, 1, 1.0f), new Vector3(0.5f, 2.0f, 3.0f), 0f, "shelf", new Vector3(0.75f, -0.1f, 0f), M("Wood_Furniture"), 90f);
        Bottles(g, W(45.5f, 27f, 1, 0.9f), 2, "Bleach bottles: throw one and it smashes where it lands.");
        F(g, "Shelf", W(45.6f, 27f, 1, 0.45f), new Vector3(0.6f, 0.9f, 1.2f), "Wood_Furniture");

        g = Group("Maid's Office");
        Bulb(g, W(43f, 35f, 1, 2.6f), 5f, 0.5f, Lamp.Pattern.Buzz);
        string[] mc = { "CAM 2", "CAM 3", "CAM 4", "CAM 1" };
        for (int i = 0; i < 4; i++)
        {
            float x = 41.6f + (i % 2) * 0.6f, y = 1.75f - (i / 2) * 0.48f;
            F(g, "Monitor", W(x, 37.7f, 1, y), new Vector3(0.54f, 0.44f, 0.42f), "Plastic_Device");
            var s = Quad($"Screen {mc[i]}", g, W(x, 37.48f, 1, y), new Vector2(0.44f, 0.33f), Quaternion.LookRotation(Vector3.forward), M("Screen"));
            if (i % 2 == 0) F(g, "Monitor shelf", W(41.9f, 37.69f, 1, y - 0.235f), new Vector3(1.3f, 0.03f, 0.47f), "Metal_Device");
            var ms = s.AddComponent<MonitorScreen>();
            ms.screen = s.GetComponent<Renderer>();
            refs.screens.Add((ms, mc[i]));
        }
        F(g, "Desk", W(42f, 36.8f, 1, 0.38f), new Vector3(1.6f, 0.76f, 0.7f), "Wood_Furniture");

        g = Group("Staff Lounge");
        var tglow = MakeLamp(g, "TV glow", W(55.0f, 29f, 1, 1.0f), 5f, 0.5f, new Color(0.6f, 0.75f, 1f), false, false, new Vector3(0.02f, 0.3f, 0.45f), Lamp.Pattern.Tv);
        F(g, "TV stand", W(55.5f, 29f, 1, 0.31f), new Vector3(0.55f, 0.62f, 0.9f), "Wood_Furniture");
        Device(g, "TV", W(55.5f, 29f, 1, 0.9f), new Vector3(0.5f, 0.55f, 0.7f), "Plastic_Device", new[] { "tv_news", "tv_show" }, 62f, 0.5f, 40f, desc: "The lounge TV: a lure (40 s).", startClip: "tv_click").glow = tglow;
        F(g, "Sofa", W(51.5f, 29f, 1, 0.4f), new Vector3(0.9f, 0.8f, 2.4f), "Fabric_Cover");
        DeskNote(g, "Note to the porter", W(53.5f, 30.6f, 1, 0.52f), "A NOTE ON THE COFFEE TABLE", LoungeNote, true);
        F(g, "Coffee table", W(53.5f, 30.6f, 1, 0.25f), new Vector3(1.0f, 0.5f, 0.6f), "Wood_Furniture");
    }

    // one guest room from its rect and door side, plus what makes it worth visiting
    static void GuestRoom(string num, Refs refs)
    {
        var room = R("Room " + num);
        var door = LevelLayout.Openings.First(o => o.name == "Door " + num);
        var r = room.r;
        int side = Mathf.Abs(door.a.x - r.xMax) < 0.01f ? 1 : -1;            // +1: door on the east wall
        float far = side > 0 ? r.xMin : r.xMax, near = side > 0 ? r.xMax : r.xMin;
        float zm = r.center.y;
        var g = Group("Room " + num);
        float bedYaw = side > 0 ? 270f : 90f;
        bool bedHide = num == "203" || num == "208" || num == "213" || num == "218";
        bool hasTub = num == "206" || num == "217" || num == "205";
        bool hasWardrobe = num == "201" || num == "202" || num == "212" || num == "216" || num == "215" || num == "219";
        Bed(g, W(far + side * 1.1f, zm - 0.5f, 1), bedYaw, bedHide);
        bool bedside = !(num == "205" || num == "213" || num == "215");         // most guests left the bedside lamp on
        var lamp = Nightstand(g, W(far + side * 0.3f, zm + 0.85f, 1), side > 0 ? 90f : 270f, num, bedside, num == "214");
        var doorZ = door.Mid.y;
        CeilingLight(g, room, false, W(near - side * 0.08f, doorZ + (doorZ + 0.8f < r.yMax ? 0.75f : -0.75f), 1, 1.35f), side > 0 ? Vector3.left : Vector3.right);
        float wardX = far + side * 3.4f;
        // not in front of a door in that wall (201's wardrobe stood across the door through to 203)
        var wardDoor = hasWardrobe ? OpeningAt(W(wardX, r.yMax - 0.08f, 1, 1.0f), 0.6f) : null;
        if (wardDoor != null) wardX = side > 0 ? Mathf.Max(wardDoor.a.x, wardDoor.b.x) + 0.75f : Mathf.Min(wardDoor.a.x, wardDoor.b.x) - 0.75f;
        if (hasWardrobe) MakeHide(g, "Wardrobe " + num, W(wardX, r.yMax - 0.36f, 1, 1.0f), new Vector3(1.1f, 2.0f, 0.6f), 180f, "wardrobe", new Vector3(0, 0.55f, 0.1f), M("Wood_Furniture"));
        if (hasTub) Tub(g, W(far + side * 3.6f, r.yMax - 0.5f, 1), 0f);
        if (r.height > 5f) { F(g, "Desk", W(near - side * 1.6f, r.yMin + 0.4f, 1, 0.38f), new Vector3(1.2f, 0.76f, 0.6f), "Wood_Furniture"); F(g, "Chair", W(near - side * 1.6f, r.yMin + 1.0f, 1, 0.45f), new Vector3(0.5f, 0.9f, 0.5f), "Fabric_Cover", Rnd(-30, 30)); }
        switch (num)
        {
            case "203":
                DeskNote(g, "Note", W(near - side * 1.6f, r.yMin + 0.4f, 1, 0.775f), "A NOTE ON THE DESK", Note203);
                break;
            case "205":
                var tv205 = Device(g, "TV", W(far + side * 3.2f, r.yMin + 0.35f, 1, 1.0f), new Vector3(0.6f, 0.5f, 0.5f), "Plastic_Device", new[] { "tv_show", "tv_news" }, 62f, 0.5f, 40f,
                    desc: "A guest-room TV: switch it on and it comes to listen, then smashes it (40 s).", startClip: "tv_click");
                tv205.glow = MakeLamp(g, "TV glow", W(far + side * 3.2f, r.yMin + 0.75f, 1, 1.0f), 5f, 0.5f, new Color(0.6f, 0.75f, 1f), false, false, new Vector3(0.3f, 0.2f, 0.02f), Lamp.Pattern.Tv);
                F(g, "TV stand", W(far + side * 3.2f, r.yMin + 0.35f, 1, 0.35f), new Vector3(0.9f, 0.7f, 0.5f), "Wood_Furniture");
                DeskNote(g, "Note", W(far + side * 1.1f, zm - 0.3f, 1, 0.6f), "A NOTE ON THE BED", Note205);
                break;
            case "202": Bottles(g, W(near - side * 1.6f, r.yMin + 0.4f, 1, 0.78f), 2, "Minibar bottles: throw one and it smashes where it lands."); break;
            case "208": Bottles(g, W(far + side * 0.3f, zm + 0.85f, 1, 0.62f), 2, "Minibar bottles."); break;
            case "204":
                F(g, "Side table", W(far + side * 0.3f, zm + 1.25f, 1, 0.3f), new Vector3(0.4f, 0.6f, 0.35f), "Wood_Furniture");   // the radio stood in the air past the nightstand
                Device(g, "bedside radio", W(far + side * 0.3f, zm + 1.25f, 1, 0.68f), new Vector3(0.28f, 0.14f, 0.12f), "Plastic_Device", new[] { "radio_station_5", "radio_station_2" }, 60f, 0.4f, 40f,
                    desc: "A bedside radio: a lure (40 s).");
                break;
            case "211":
                Device(g, "record player", W(near - side * 1.6f, r.yMin + 0.4f, 1, 0.86f), new Vector3(0.45f, 0.15f, 0.38f), "Wood_Furniture", new[] { "record_waltz", "record_nocturne" }, 60f, 0.5f, 40f,
                    desc: "A record player: a lure (40 s).", startClip: "gramophone_needle");
                break;
            case "219":
                F(g, "Side table", W(far + side * 0.3f, zm + 1.25f, 1, 0.3f), new Vector3(0.4f, 0.6f, 0.35f), "Wood_Furniture");
                Device(g, "music box", W(far + side * 0.3f, zm + 1.25f, 1, 0.66f), new Vector3(0.2f, 0.12f, 0.15f), "Wood_Furniture", new[] { "musicbox_waltz_box" }, 54f, 0.4f, 30f,
                    desc: "A music box: a quiet lure (30 s).", startClip: "musicbox_wind");
                break;
            case "214":
                // the dresser on the north wall, the key behind it, the mirror above
                var dr = Box("Dresser", g, W(44f, 13.6f, 1, 0.5f), new Vector3(1.3f, 1.0f, 0.55f), M("Wood_Furniture"), LProp, true, null, false, false);
                var drTrig = new GameObject("Use"); drTrig.transform.SetParent(dr.transform, false); drTrig.layer = LInteract;
                var dbc = drTrig.AddComponent<BoxCollider>(); dbc.isTrigger = true; dbc.size = new Vector3(1.4f, 1.1f, 0.7f);
                for (int i = 0; i < 3; i++) Box("Drawer", dr.transform, W(44f, 13.32f, 1, 0.2f + i * 0.3f), new Vector3(1.2f, 0.24f, 0.02f), M("Wood_Door"), LProp, false, null, false, false);
                var dresser = drTrig.AddComponent<Dresser>();
                dresser.pullDir = Vector3.back;
                dresser.body = dr.transform;
                refs.dresser = dresser;
                // the navmesh is built from where it stands at the start: once pulled out, it carves its new place out
                // of the navmesh, or the listener would walk through it
                var dob = dr.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                dob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                dob.size = new Vector3(1.3f, 1.0f, 0.55f);
                dob.carving = true;
                dob.carveOnlyStationary = true;
                var key = Box("Safe code (half)", g, LevelLayout.KeySpot, new Vector3(0.09f, 0.12f, 0.01f), M("Paper_Note"), LInteract, true, null, false, false);
                key.GetComponent<BoxCollider>().isTrigger = true;
                key.GetComponent<BoxCollider>().size = new Vector3(0.25f, 0.3f, 0.12f);
                var ki = key.AddComponent<KeyItem>();
                ki.keyId = "code_a"; ki.label = "torn half of the safe code";
                dresser.key = ki;
                refs.key = ki;
                Mirror(g, W(44f, 13.93f, 1, 1.62f), Vector3.back, new Vector2(0.8f, 0.6f));
                DeskNote(g, "Notepad", W(far + side * 0.3f, zm + 0.65f, 1, 0.61f), "NOTEPAD BY THE PHONE", Note214);
                break;
        }
    }

    static void BurntRooms()
    {
        foreach (var num in new[] { "207", "209" })
        {
            var g = Group("Room " + num);
            var r = R("Room " + num).r;
            Bulb(g, W(r.center.x, r.center.y, 1, 2.6f), 4.5f, 0.4f, Lamp.Pattern.Dying);
            F(g, "Charred bed frame", W(5.2f, r.center.y - 0.4f, 1, 0.2f), new Vector3(1.5f, 0.4f, 2.0f), "Soot_Floor", Rnd(-8, 8));
            F(g, "Charred wardrobe (fallen)", W(9.4f, r.yMax - 0.8f, 1, 0.35f), new Vector3(2.0f, 0.7f, 1.1f), "Soot_Floor", 12f);
            Emit(W(r.center.x, r.center.y, 1, 2.4f), "amb_settle", 0.3f, false, new Vector2(8f, 18f));
        }
        var g9 = Group("Room 209");
        F(g9, "A camera bag", W(9.0f, 29.6f, 1, 0.15f), new Vector3(0.45f, 0.3f, 0.3f), "Fabric_Cover", 25f);
        DeskNote(g9, "Note in the bag", W(9.2f, 29.0f, 1, 0.31f), "IN THE BAG", Note209);
        Item(g9, "Safe code (other half)", LevelLayout.CodeSpotB, new Vector3(0.09f, 0.01f, 0.12f), "Paper_Note", "code_b", "other half of the safe code");
    }

    static void Bed(Transform p, Vector3 c, float yaw, bool hide)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Box("Bed frame", p, c + Vector3.up * 0.18f, new Vector3(1.5f, 0.36f, 2.05f), M("Wood_Furniture"), LProp, true, rot, false);
        Box("Mattress", p, c + Vector3.up * 0.45f, new Vector3(1.42f, 0.2f, 1.95f), M("Fabric_Bed"), LProp, false, rot, false);
        Box("Cover", p, c + rot * new Vector3(0, 0.56f, -0.2f), new Vector3(1.46f, 0.04f, 1.5f), M("Fabric_Cover"), LProp, false, rot, false);
        Box("Pillow", p, c + rot * new Vector3(0, 0.6f, 0.75f), new Vector3(1.1f, 0.12f, 0.4f), M("Fabric_Bed"), LProp, false, rot, false);
        Box("Headboard", p, c + rot * new Vector3(0, 0.7f, 1.02f), new Vector3(1.55f, 1.0f, 0.08f), M("Wood_Furniture"), LProp, true, rot, false);
        if (hide)
        {
            var t = new GameObject("Under the bed");
            t.transform.SetParent(p, false);
            t.transform.position = c + Vector3.up * 0.2f;
            t.transform.rotation = rot;
            t.layer = LInteract;
            var bc = t.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(1.7f, 0.5f, 2.2f);
            var h = t.AddComponent<HideSpot>();
            h.kind = "bed";
            var view = new GameObject("View").transform;
            view.SetParent(t.transform, false);
            view.localPosition = new Vector3(0, 0.05f, -0.75f);
            view.localRotation = Quaternion.Euler(0, 180, 0);
            h.view = view;
        }
    }

    static Lamp Nightstand(Transform p, Vector3 c, float yaw, string room, bool lampOn, bool shadows = false)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Box("Nightstand", p, c + Vector3.up * 0.3f, new Vector3(0.45f, 0.6f, 0.4f), M("Wood_Furniture"), LProp, true, rot, false);
        if (room != "home") MakePhone(p, c + rot * new Vector3(0.1f, 0.6f, 0f), yaw, room);
        Box("Lamp base", p, c + rot * new Vector3(-0.12f, 0.75f, 0.05f), new Vector3(0.08f, 0.3f, 0.08f), M("Brass_Fitting"), LProp, false, rot, false);
        var lamp = MakeLamp(p, "Bedside lamp", c + rot * new Vector3(-0.12f, 0.98f, 0.05f), 4.5f, 0.9f, new Color(1f, 0.72f, 0.45f), lampOn, shadows, new Vector3(0.26f, 0.2f, 0.26f), lampOn ? Lamp.Pattern.Flicker : Lamp.Pattern.Steady);
        MakeSwitch(p, c + rot * new Vector3(-0.12f, 0.85f, -0.12f), Vector3.up, new[] { lamp });
        return lamp;
    }

    static void Tub(Transform g, Vector3 c, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        MakeHide(g, "Bath", c + Vector3.up * 0.3f, new Vector3(1.6f, 0.6f, 0.75f), yaw, "bath", new Vector3(0, 0.15f, 0f), M("Plastic_Beige"), 180f);
        Box("Curtain", g, c + rot * new Vector3(0, 1.2f, -0.4f), new Vector3(1.6f, 1.6f, 0.02f), M("Fabric_Curtain"), LProp, false, rot, false);
    }

    static void Mirror(Transform g, Vector3 c, Vector3 facing, Vector2 size)
    {
        Box("Mirror frame", g, c - facing * 0.01f, new Vector3(Mathf.Abs(facing.x) > 0.5f ? 0.03f : size.x + 0.08f, size.y + 0.08f, Mathf.Abs(facing.z) > 0.5f ? 0.03f : size.x + 0.08f), M("Wood_Furniture"), LProp, false);
        var q = Quad("Mirror", g, c + facing * 0.012f, size, Quaternion.LookRotation(-facing), M("Mirror_Glass"));
        var mv = q.AddComponent<MirrorView>();
        mv.glass = q.GetComponent<Renderer>();
        mv.size = size;
    }

    // ================================================================== basement
    static void Basement(Refs refs)
    {
        // before the breaker only the red emergency lights work; the tubes come on (struggling) with the power
        Lamp T(Transform g, float x, float z, Lamp.Pattern p, bool alongX = true) { var l = Tube(g, W(x, z, -1, 2.9f), 7f, p, false, 0.9f, alongX); refs.basementLamps.Add(l); return l; }

        var g = Group("Basement Stair");
        CollapsedStair(g);
        Emergency(g, W(29.8f, 36f, -1, 2.6f));
        Emergency(g, W(18.2f, 42f, -1, 2.0f), 6f);

        g = Group("Main Corridor");
        for (int i = 0; i < 6; i++) T(g, 8f + i * 9f, 32f, i % 3 == 0 ? Lamp.Pattern.Flicker : i % 3 == 1 ? Lamp.Pattern.Steady : Lamp.Pattern.Dying);
        Emergency(g, W(4.2f, 32f, -1, 2.4f));
        Emergency(g, W(55.8f, 32f, -1, 2.4f));
        Emergency(g, W(30f, 30.1f, -1, 2.4f));
        F(g, "Pipe", W(30f, 33.7f, -1, 2.8f), new Vector3(52f, 0.2f, 0.2f), "Metal_Device", 0f, false);
        F(g, "Pipe", W(30f, 33.4f, -1, 2.95f), new Vector3(52f, 0.12f, 0.12f), "Metal_Device", 0f, false);
        foreach (var x in new[] { 10f, 22f, 36f, 47f }) Puddle(g, x, 32f, -1, 2.0f, 2.2f);
        Emit(W(22f, 32f, -1, 2.6f), "amb_drip", 0.4f, false, new Vector2(1f, 4f));
        Emit(W(40f, 33.5f, -1, 2.8f), "amb_pipe_knock", 0.35f, false, new Vector2(6f, 16f));

        g = Group("Laundry");
        T(g, 11f, 39f, Lamp.Pattern.Buzz);
        for (int i = 0; i < 3; i++)
        {
            Device(g, "washing machine", W(6.0f + i * 1.0f, 43.5f, -1, 0.45f), new Vector3(0.8f, 0.9f, 0.7f), "Plastic_Beige", new[] { "amb_washer" }, 58f, 0.5f, 45f, mask: 12f, maskR: 5f,
                desc: "A washing machine: runs 45 s. A lure (it listens to it a while), and it covers noise near it.", startClip: "elev_button").attendTime = 8f;
        }
        F(g, "Linen heap (chute)", W(13.5f, 42.5f, -1, 0.35f), new Vector3(1.8f, 0.7f, 1.6f), "Fabric_Bed");
        Box("Chute mouth", g, W(13.5f, 43.9f, -1, 2.2f), new Vector3(0.8f, 0.8f, 0.15f), M("Metal_Device"), LProp, false);
        MakeHide(g, "Laundry cart", W(16.8f, 36.0f, -1, 0.5f), new Vector3(1.0f, 1.0f, 0.6f), 0f, "cart", new Vector3(0, 0.1f, 0f), M("Fabric_Bed"));
        F(g, "Folding table", W(10f, 36.0f, -1, 0.42f), new Vector3(2.4f, 0.84f, 0.9f), "Wood_Furniture");

        g = Group("Boiler Room");
        T(g, 34f, 39f, Lamp.Pattern.Steady);
        MakeLamp(g, "Boiler glow", W(38f, 37.9f, -1, 0.6f), 3f, 0.3f, new Color(1f, 0.62f, 0.38f), true, false, new Vector3(0.4f, 0.15f, 0.05f), Lamp.Pattern.Candle);
        MakeHide(g, "Boiler", W(38f, 40f, -1, 1.5f), new Vector3(4.0f, 3.0f, 3.0f), 0f, "shelf", new Vector3(0f, -0.9f, 1.95f), M("Metal_Device"), 180f);
        AutoMachine(g, "boiler", W(38f, 40f, -1, 1.5f), "amb_boiler", 18f, 9f, new Vector2(30f, 25f), 0.8f, "The boiler roars on its own (about 25 s on, 30 s off). While it roars, noise near it is covered.");
        F(g, "Pipe", W(38f, 43.6f, -1, 2.4f), new Vector3(15f, 0.3f, 0.3f), "Metal_Device", 0f, false);

        g = Group("Pump Room");
        refs.liftPanel = LiftCallPanel(g);
        Emergency(g, W(55.8f, 39f, -1, 2.4f));
        F(g, "Sump pump", W(53f, 42.5f, -1, 0.5f), new Vector3(1.2f, 1.0f, 1.2f), "Metal_Device");
        AutoMachine(g, "sump pump", W(53f, 42.5f, -1, 0.5f), "amb_pump", 7f, 4f, new Vector2(22f, 14f), 0.5f, "The sump pump cycles on its own.");

        g = Group("Electrical Room");
        T(g, 9f, 26f, Lamp.Pattern.Buzz, false);
        Emergency(g, W(13.8f, 24f, -1, 2.4f));
        Emergency(g, W(4.3f, 28.6f, -1, 2.4f), 5f);
        MakeLamp(g, "Pilot light", LevelLayout.BreakerSpot + new Vector3(0.12f, 0.55f, -0.45f), 2.5f, 0.25f, new Color(1f, 0.4f, 0.2f), true, false, new Vector3(0.04f, 0.04f, 0.04f), Lamp.Pattern.Buzz);
        var panel = Box("Breaker panel", g, W(4.2f, 26f, -1, 1.3f), new Vector3(0.2f, 1.4f, 1.2f), M("Metal_Device"), LProp, true, null, false);
        var lever = new GameObject("Lever").transform;
        lever.SetParent(panel.transform, false);
        lever.position = LevelLayout.BreakerSpot;
        Box("Lever arm", lever, LevelLayout.BreakerSpot + new Vector3(0.18f, 0.12f, 0f), new Vector3(0.06f, 0.35f, 0.06f), M("Lamp_Red"), LProp, false, null, false, false);
        lever.localRotation = Quaternion.Euler(-40f, 0, 0);
        var bt = new GameObject("Main breaker"); bt.transform.SetParent(g, false); bt.transform.position = LevelLayout.BreakerSpot + Vector3.right * 0.2f; bt.layer = LInteract;
        var bbc = bt.AddComponent<BoxCollider>(); bbc.isTrigger = true; bbc.size = new Vector3(0.5f, 0.6f, 0.6f);
        var brk = bt.AddComponent<Breaker>();
        brk.lever = lever;
        refs.breaker = brk;
        Note(g, "Breaker label", W(4.32f, 26.75f, -1, 1.85f), new Vector3(0.01f, 0.12f, 0.3f), Quaternion.identity, "LABEL ON THE PANEL", "MAIN\n\nBASEMENT LIGHTING\nSERVICE LIFT\n\nFUSE: (empty)\n(hold to throw)", false);
        F(g, "Transformer", W(12.6f, 28.6f, -1, 0.7f), new Vector3(1.4f, 1.4f, 1.4f), "Metal_Device");
        Emit(W(12.6f, 28.6f, -1, 1f), "amb_transformer", 0.4f);

        g = Group("Recorder Room");
        var rl = T(g, 18f, 26f, Lamp.Pattern.Flicker, false);
        Emergency(g, W(21.8f, 24f, -1, 2.3f), 5f);
        F(g, "Rack", W(14.45f, 26f, -1, 0.62f), new Vector3(0.55f, 1.24f, 0.9f), "Metal_Device");
        var v = Box("Relay VCR", g, LevelLayout.VcrSpot, new Vector3(0.34f, 0.11f, 0.46f), M("Plastic_Device"), LProp, true, null, false, false);
        Box("Cable up the wall", g, W(14.2f, 26.4f, -1, 2.1f), new Vector3(0.05f, 1.6f, 0.05f), M("Plastic_Device"), LProp, false);
        Note(g, "Relay label", LevelLayout.VcrSpot + new Vector3(0.18f, 0.0f, 0.15f), new Vector3(0.01f, 0.05f, 0.12f), Quaternion.identity, "THE LABEL ON THE MACHINE",
            "RELAY ONLY\n\nMaster: ANTENNA ROOM (roof)\nfeed via lift shaft riser\n\n{DATE}", true);
        Item(g, "Lift key", LevelLayout.LiftKeySpot, new Vector3(0.06f, 0.02f, 0.12f), "Brass_Fitting", "liftkey", "lift key");
        string[] rc = { "CAM 5", "CAM 7" };
        for (int i = 0; i < 2; i++)
        {
            float z = 25.4f + i * 1.2f;
            F(g, "Monitor", W(14.3f, z, -1, 1.75f), new Vector3(0.42f, 0.44f, 0.54f), "Plastic_Device");
            var s = Quad($"Screen {rc[i]}", g, W(14.52f, z, -1, 1.75f), new Vector2(0.44f, 0.33f), Quaternion.LookRotation(Vector3.left), M("Screen"));
            if (i == 0) F(g, "Monitor shelf", W(14.33f, 26.0f, -1, 1.515f), new Vector3(0.5f, 0.03f, 1.9f), "Metal_Device");
            var ms = s.AddComponent<MonitorScreen>();
            ms.screen = s.GetComponent<Renderer>();
            ms.on = false;
            refs.screens.Add((ms, rc[i]));
            refs.recorderScreens.Add(ms);
        }
        F(g, "Desk", W(17f, 23.2f, -1, 0.38f), new Vector3(1.6f, 0.76f, 0.7f), "Wood_Furniture");

        g = Group("Workshop");
        T(g, 27f, 26f, Lamp.Pattern.Dying);
        F(g, "Workbench", W(24.4f, 29.5f, -1, 0.45f), new Vector3(4f, 0.9f, 0.8f), "Wood_Furniture");      // beside the door, not across it
        DeskNote(g, "Maintenance log", W(23.4f, 29.5f, -1, 0.91f), "MAINTENANCE LOG", WorkshopLog, false);
        Bottles(g, W(25.6f, 29.5f, -1, 0.91f), 2, "Empty bottles: throw one to make a noise somewhere else.");
        for (int i = 0; i < 4; i++) Box("Tool", g, W(25f + i * 0.5f, 29.92f, -1, 1.4f), new Vector3(0.06f, 0.4f, 0.02f), M("Metal_Device"), LProp, false);

        g = Group("Storage Cages");
        T(g, 36f, 22f, Lamp.Pattern.Flicker);
        T(g, 44f, 22f, Lamp.Pattern.Strobe);
        MakeHide(g, "Storage cage", W(34.2f, 16.6f, -1, 1.1f), new Vector3(3.0f, 2.2f, 3.0f), 0f, "cage", new Vector3(0f, 0.5f, 0.9f), M("Metal_Lift"), 0f);
        MakeHide(g, "Storage cage", W(45.8f, 27.4f, -1, 1.1f), new Vector3(3.0f, 2.2f, 3.0f), 0f, "cage", new Vector3(0f, 0.5f, -0.9f), M("Metal_Lift"), 180f);
        for (int i = 0; i < 6; i++) F(g, "Boxes", W(37f + (i % 3) * 3f, 16f + (i / 3) * 10f, -1, 0.5f), new Vector3(1.2f, 1.0f, 1.0f), "Paper_Note", Rnd(-20, 20));
        F(g, "Shelf (end cage)", W(46.6f, 16.0f, -1, 0.5f), new Vector3(1.2f, 1.0f, 0.6f), "Metal_Device");
        Item(g, "Main fuse", LevelLayout.FuseSpot, new Vector3(0.1f, 0.06f, 0.24f), "Plastic_Beige", "fuse", "spare main fuse");

        g = Group("Coal Store");
        Emergency(g, W(55.8f, 22f, -1, 2.4f));
        F(g, "Coal heap", W(53f, 18f, -1, 0.4f), new Vector3(4f, 0.8f, 4f), "Rubble_Floor");

        g = Group("Wine Cellar");
        Bulb(g, W(11f, 14f, -1, 2.5f), 7f, 0.5f, Lamp.Pattern.Dying);
        Emergency(g, W(17.8f, 14f, -1, 2.2f), 6f);
        for (int i = 0; i < 4; i++)
        {
            float z = 9f + i * 3.2f;
            if (i == 2) MakeHide(g, "Wine rack", W(9f, z, -1, 1.0f), new Vector3(7f, 2.0f, 0.6f), 0f, "shelf", new Vector3(0f, -0.1f, 0.9f), M("Wood_Furniture"), 0f);
            else F(g, "Wine rack", W(9f, z, -1, 1.0f), new Vector3(7f, 2.0f, 0.6f), "Wood_Furniture");
        }
        Bottles(g, W(13.4f, 9f, -1, 0.0f), 3);
        Bottles(g, W(13.4f, 15.4f, -1, 0.0f), 3);

        g = Group("Flooded Passage");
        Emergency(g, W(31.8f, 14f, -1, 2.2f));
        Emit(W(25f, 14f, -1, 2.2f), "amb_water_trickle", 0.5f);
        Emit(W(21f, 10f, -1, 2.2f), "amb_drip", 0.5f, false, new Vector2(1f, 3f));

        g = Group("Hotel Archive");
        T(g, 40f, 10f, Lamp.Pattern.Dying);
        for (int i = 0; i < 3; i++) F(g, "Tape shelves", W(35f + i * 4f, 10f, -1, 1.0f), new Vector3(0.5f, 2.0f, 5f), "Wood_Furniture");
        DeskNote(g, "Archive note", W(46f, 8f, -1, 0.9f), "TAPES", ArchiveNote, false);
        F(g, "Archive table", W(46f, 8f, -1, 0.42f), new Vector3(1.2f, 0.84f, 0.8f), "Wood_Furniture");

        g = Group("Generator Room");
        Emergency(g, W(48.2f, 10f, -1, 2.4f));
        Device(g, "generator", W(52f, 10f, -1, 0.7f), new Vector3(2.4f, 1.4f, 1.2f), "Metal_Device", new[] { "amb_generator" }, 76f, 0.8f, 60f, mask: 20f, maskR: 10f,
            desc: "The generator: runs 60 s, very loud. The best lure in the basement: it stands over it a long time. It covers noise near it.", startClip: "power_up").attendTime = 14f;
        for (int i = 0; i < 3; i++) F(g, "Fuel can", W(49.0f + i * 0.4f, 13.4f, -1, 0.25f), new Vector3(0.3f, 0.5f, 0.2f), "Lamp_Red");

        g = Group("South Corridor");
        for (int i = 0; i < 5; i++) T(g, 8f + i * 11f, 4f, i % 2 == 0 ? Lamp.Pattern.Dying : Lamp.Pattern.Flicker);
        Emergency(g, W(4.2f, 4f, -1, 2.3f));
        Emergency(g, W(30f, 2.1f, -1, 2.3f));
        foreach (var x in new[] { 16f, 33f, 44f }) Puddle(g, x, 4f, -1, 2.4f, 2.4f);
        Emit(W(30f, 4f, -1, 2.3f), "amb_pipe_knock", 0.3f, false, new Vector2(8f, 20f));

        // the breaker powers the basement tubes and the recorder room monitors
        refs.breaker.lamps.AddRange(refs.basementLamps);
        refs.breaker.screens.AddRange(refs.recorderScreens);
    }

    // your house (the prologue) is in TapeBuilder.House.cs

    // ================================================================== CCTV cameras
    static void BuildCameras(Refs refs)
    {
        var g = new GameObject("CCTV").transform;
        CctvCamera Cam(string label, Vector3 pos, Vector3 lookAt, float fov)
        {
            var housing = Box("Camera " + label, g, pos, new Vector3(0.14f, 0.12f, 0.3f), M("Plastic_Beige"), LProp, false, Quaternion.LookRotation(lookAt - pos), false, false);
            Box("Tally", housing.transform, housing.transform.TransformPoint(new Vector3(0, 0.07f, 0.12f)), new Vector3(0.02f, 0.02f, 0.02f), M("Lamp_Red"), LProp, false, housing.transform.rotation, false, false);
            var cg = new GameObject("Lens");
            cg.transform.SetParent(housing.transform, false);
            cg.transform.localPosition = new Vector3(0, 0, 0.2f);
            var c = cg.AddComponent<Camera>();
            c.fieldOfView = fov;
            c.nearClipPlane = 0.1f;
            c.farClipPlane = 40f;
            c.cullingMask = ~((1 << 5) | (1 << 16));
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            c.enabled = false;
            var ud = cg.AddComponent<UniversalAdditionalCameraData>();
            ud.renderPostProcessing = false;
            ud.renderShadows = false;
            var cc = housing.AddComponent<CctvCamera>();
            cc.label = label;
            cc.cam = c;
            return cc;
        }
        Cam("CAM 1", W(39.5f, 8.5f, 0, 3.6f), W(26f, 19f, 0, 0.8f), 70f);          // lobby, from the south-east corner
        Cam("CAM 2", W(20.5f, 25.5f, 1, 2.5f), W(36f, 25f, 1, 0.9f), 64f);         // north gallery, outside the ballroom
        Cam("CAM 3", W(13f, 2.4f, 1, 2.6f), W(13f, 28f, 1, 0.8f), 48f);            // west corridor
        Cam("CAM 4", W(47f, 37.6f, 1, 2.6f), W(47f, 6f, 1, 0.8f), 48f);            // east corridor (214)
        Cam("CAM 5", W(55.6f, 32f, -1, 2.9f), W(14f, 32f, -1, 0.6f), 46f);         // basement main corridor
        Cam("CAM 6", W(41.6f, 37.6f, 0, 3.6f), W(28f, 28f, 0, 0.6f), 70f);         // restaurant
        Cam("CAM 7", W(4.4f, 4f, -1, 2.8f), W(50f, 4f, -1, 0.6f), 46f);            // basement south corridor

        // the tape you watch at home: a camcorder left running on a tripod in the lobby, at head height (it walks right
        // up to the lens). A sharper picture than the CCTV, at video frame rate. It stays where it was left.
        var tc = Cam("TAPE", LevelLayout.TapeCam, LevelLayout.TapeCamLook, 60f);
        tc.width = 640; tc.height = 480; tc.fps = 25f;
        var tr = tc.transform;
        tr.name = "Camcorder (the tape)";
        var body = tr.GetComponent<MeshRenderer>();
        if (body) body.sharedMaterial = M("Plastic_Device");
        Box("Camcorder lens", tr, tr.TransformPoint(new Vector3(0f, 0f, 0.17f)), new Vector3(0.07f, 0.07f, 0.06f), M("Plastic_Device"), LProp, false, tr.rotation, false, false);
        Box("Camcorder viewfinder", tr, tr.TransformPoint(new Vector3(-0.08f, 0.04f, -0.06f)), new Vector3(0.03f, 0.05f, 0.09f), M("Plastic_Device"), LProp, false, tr.rotation, false, false);
        var foot = new Vector3(LevelLayout.TapeCam.x, 0f, LevelLayout.TapeCam.z);
        var head = LevelLayout.TapeCam + Vector3.down * 0.09f;
        Box("Tripod head", g, head, new Vector3(0.07f, 0.05f, 0.07f), M("Plastic_Device"), LProp, false, null, false, false);
        for (int i = 0; i < 3; i++)
        {
            float a = (i * 120f + 30f) * Mathf.Deg2Rad;
            var toe = foot + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.42f;
            var top = head + Vector3.down * 0.03f;
            var mid = (toe + top) * 0.5f;
            float len = Vector3.Distance(toe, top);
            Box("Tripod leg", g, mid, new Vector3(0.025f, len, 0.025f), M("Metal_Steel"), LProp, false, Quaternion.FromToRotation(Vector3.up, top - toe), false, false);
        }
        // its own lamp, on while the tape plays: the lobby in front of it, and (a softer second light that only reaches
        // things that walk, rendering layer 1) the figure coming up to it, so it reads on your TV even far off
        Light TapeLight(string n, float intensity, float range, float angle, uint layers)
        {
            var lg = new GameObject(n);
            lg.transform.SetParent(tr, false);
            lg.transform.localPosition = new Vector3(0f, 0.08f, 0.2f);
            var li = lg.AddComponent<Light>();
            li.type = LightType.Spot; li.range = range; li.spotAngle = angle; li.innerSpotAngle = angle * 0.55f;
            li.intensity = intensity; li.color = new Color(1f, 0.94f, 0.84f); li.shadows = LightShadows.None; li.enabled = false;
            SetLightLayers(li, layers);
            return li;
        }
        refs.tapeLights.Add(TapeLight("Camcorder light", 3.2f, 15f, 70f, uint.MaxValue));
        refs.tapeLights.Add(TapeLight("Camcorder light (figure)", 2.4f, 14f, 40f, 1u));
    }
}
