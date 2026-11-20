using UnityEngine;
using UnityEngine.Rendering.Universal;

// PART THREE: the penthouse (floor 3) and the roof (floor 4), at the top of the tower. It looks like nowhere else in
// the hotel: the owner's private floor is dark walnut, black marble and parquet, lit by table lamps, picture lights
// and the cold glow of his monitors; he watched and kept the guests, so there are screens, tapes and cables
// everywhere and the earlier viewers' portraits line the corridor with their faces worn away. The lift brings you up
// and dies. His suite is chained from the corridor: get in through the servants' passage (a one-metre gap behind the
// walls) or through Suite 903, to the study, where the keycard for the roof door is. On the roof, in the storm, the
// master VCR is in the antenna room; the PA horn on its mast in the far corner is the big lure.
public static partial class TapeBuilder
{
    const string OwnerDiary =
        "1 November 2003\n\nThe master in the antenna room has not stopped since midnight. It isn't recording the hotel any more. It is broadcasting it.\n\n" +
        "Every copy of the tape is a window, and whoever looks through one is let in. The building keeps them. I have their faces on the corridor wall.\n\n" +
        "The keycard for the roof is in this desk. I am not going up there again. It sits in the lift motor room when it is quiet, right next door to the antenna room.";
    const string PantryNote = "Night service cancelled.\n\nThe owner says nobody goes up to the roof. Not for the antenna, not for the tanks, not for anything.\n\nUse the passage behind the suites, not the corridor. He doesn't like being walked past.";
    const string AntennaLabel = "MASTER\n\n{DATE}\n\nBROADCAST";
    const string RoofNote =
        "The horn on the mast (south-west corner) is the hotel's old PA. Switch it on and the whole roof hears the lift music.\n\n" +
        "It went straight to it and stood under it a long time before it tore it down. That was when I got into the antenna room.\n\n" +
        "Bolt the door. It still breaks a bolt in four blows.\n\n— the one before you";
    const string CorkBoard =
        "Photographs pinned in rows, each one a still from a security camera: somebody in the lobby, looking up at the lens.\n\n" +
        "Under each a date in the owner's hand. 2006. 2011. 2015. 2019. 2023.\n\nThe last pin holds nothing yet. The card under it says today's date.";

    // ---------------------------------------------------------------- penthouse furniture
    static void Portrait(Transform g, Vector3 at, Vector3 outward, bool lit)
    {
        if (OpeningAt(at, 0.36f) != null) return;      // not hanging across an arch or a door (two hung in the lift lobby arch)
        bool alongX = Mathf.Abs(outward.z) > 0.5f;
        Box("Portrait frame", g, at - outward * 0.01f, alongX ? new Vector3(0.62f, 0.8f, 0.04f) : new Vector3(0.04f, 0.8f, 0.62f), M("Brass_Fitting"), LProp, false, null, false, false);
        Quad("Portrait (face worn)", g, at + outward * 0.012f, new Vector2(0.5f, 0.68f), Quaternion.LookRotation(-outward), M("Blob_Face"));
        if (lit) MakeLamp(g, "Picture light", at + outward * 0.12f + Vector3.up * 0.5f, 3.0f, 0.38f, Warm, true, false, new Vector3(alongX ? 0.4f : 0.05f, 0.04f, alongX ? 0.05f : 0.4f), Lamp.Pattern.Steady);
    }

    static void MonitorWall(Transform g, Vector3 leftBottom, Vector3 right, Vector3 outward, int cols, int rows)
    {
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
            {
                var p = leftBottom + right * (c * 0.62f) + Vector3.up * (r * 0.5f);
                bool alongX = Mathf.Abs(outward.z) > 0.5f;
                Box("CRT", g, p - outward * 0.2f, alongX ? new Vector3(0.56f, 0.46f, 0.44f) : new Vector3(0.44f, 0.46f, 0.56f), M("Plastic_Device"), LProp, false, null, false, false);
                Quad("CRT screen", g, p + outward * 0.025f, new Vector2(0.44f, 0.34f), Quaternion.LookRotation(-outward), M("Screen"));
            }
        // the screens' cold glow on the room
        MakeLamp(g, "Cold light", leftBottom + right * (cols * 0.31f) + Vector3.up * (rows * 0.25f) + outward * 0.6f, 6f, 0.55f, new Color(0.88f, 0.94f, 1f), true, false, new Vector3(0.05f, 0.05f, 0.05f), Lamp.Pattern.Tv);
    }

    static void TapeShelf(Transform g, Vector3 c, bool alongX, float len, int rows = 5)
    {
        var size = alongX ? new Vector3(len, 2.2f, 0.32f) : new Vector3(0.32f, 2.2f, len);
        Box("Tape shelf", g, c + Vector3.up * 1.1f, size, M("Wood_Panel"), LProp, true, null, false);
        for (int i = 0; i < rows; i++)
        {
            float y = 0.3f + i * 0.4f;
            var strip = alongX ? new Vector3(len - 0.1f, 0.2f, 0.06f) : new Vector3(0.06f, 0.2f, len - 0.1f);
            var off = alongX ? new Vector3(0, 0, 0.15f) : new Vector3(0.15f, 0, 0);
            Box("Tapes", g, c + Vector3.up * (y + 0.1f) + off, strip, M("Plastic_Device"), LProp, false, null, false, false);
            Box("Tapes", g, c + Vector3.up * (y + 0.1f) - off, strip, M("Plastic_Device"), LProp, false, null, false, false);
            Box("Labels", g, c + Vector3.up * (y + 0.12f) + off * 1.25f, alongX ? new Vector3(len - 0.12f, 0.05f, 0.005f) : new Vector3(0.005f, 0.05f, len - 0.12f), M("Paper_Note"), LProp, false, null, false, false);
        }
    }

    static void Cable(Transform g, Vector3 a, Vector3 b)
    {
        var d = b - a; d.y = 0;
        Box("Cable", g, (a + b) / 2f + Vector3.up * 0.015f, new Vector3(0.03f, 0.03f, d.magnitude), M("Plastic_Device"), LProp, false, Quaternion.LookRotation(d.normalized), false, false);
    }

    static void FourPoster(Transform g, Vector3 c)
    {
        Bed(g, c, 0f, true);
        foreach (var (dx, dz) in new[] { (-0.8f, -1.05f), (0.8f, -1.05f), (-0.8f, 1.05f), (0.8f, 1.05f) })
            Box("Bed post", g, c + new Vector3(dx, 1.1f, dz), new Vector3(0.08f, 2.2f, 0.08f), M("Wood_Panel"), LProp, true, null, false);
        Box("Canopy", g, c + new Vector3(0, 2.2f, 0), new Vector3(1.7f, 0.08f, 2.2f), M("Wood_Panel"), LProp, false, null, false);
        Box("Bed curtain", g, c + new Vector3(-0.84f, 1.4f, 0.2f), new Vector3(0.02f, 1.6f, 1.6f), M("Velvet_Dark"), LProp, false, null, false);
        Box("Bed curtain", g, c + new Vector3(0.84f, 1.4f, 0.6f), new Vector3(0.02f, 1.6f, 0.8f), M("Velvet_Dark"), LProp, false, null, false);
        Box("Headboard", g, c + new Vector3(0, 0.9f, -1.06f), new Vector3(1.6f, 1.0f, 0.08f), M("Velvet_Dark"), LProp, false, null, false);
    }

    static void Crown(Transform g, Rect r, int floor, float h)
    {
        float y = LevelLayout.FloorY(floor) + h - 0.08f;
        Box("Cornice", g, new Vector3(r.center.x, y, r.yMin + 0.09f), new Vector3(r.width, 0.14f, 0.06f), M("Wood_Panel"), LProp, false, null, false);
        Box("Cornice", g, new Vector3(r.center.x, y, r.yMax - 0.09f), new Vector3(r.width, 0.14f, 0.06f), M("Wood_Panel"), LProp, false, null, false);
        Box("Cornice", g, new Vector3(r.xMin + 0.09f, y, r.center.y), new Vector3(0.06f, 0.14f, r.height), M("Wood_Panel"), LProp, false, null, false);
        Box("Cornice", g, new Vector3(r.xMax - 0.09f, y, r.center.y), new Vector3(0.06f, 0.14f, r.height), M("Wood_Panel"), LProp, false, null, false);
    }

    static void TableLamp(Transform g, Vector3 at, bool on = true)
    {
        Box("Lamp base", g, at + Vector3.up * 0.18f, new Vector3(0.12f, 0.36f, 0.12f), M("Brass_Fitting"), LProp, false, null, false, false);
        MakeLamp(g, "Bedside lamp", at + Vector3.up * 0.45f, 3.5f, 0.45f, Warm, on, false, new Vector3(0.28f, 0.22f, 0.28f), Lamp.Pattern.Steady);
    }

    static void Penthouse(Refs refs)
    {
        Vector3 P(float x, float z, float up = 0f) => W(x, z, 3, up);

        // ---- the corridor: walnut, a long runner, the earlier viewers' portraits under picture lights
        var g = Group("Penthouse Corridor");
        Crown(g, R("Penthouse Corridor").r, 3, 3.0f);
        Box("Runner", g, P(42f, 36f, 0.006f), new Vector3(30f, 0.012f, 1.4f), M("Velvet_Dark"), LProp, false);
        int pi = 0;
        foreach (var x in new[] { 28.5f, 31.5f, 36f, 40f, 45f, 50f })
        {
            if (Mathf.Abs(x - 30f) < 0.8f || Mathf.Abs(x - 38f) < 0.8f) continue;
            Portrait(g, P(x, 37.9f, 1.55f), Vector3.back, pi++ % 3 != 2);
        }
        foreach (var x in new[] { 28f, 37f, 46f, 51f, 56.5f }) Portrait(g, P(x, 34.1f, 1.55f), Vector3.forward, x != 51f);
        MakeLamp(g, "Ceiling light", P(33f, 36f, 2.9f), 6f, 0.5f, Warm, true, false, new Vector3(0.3f, 0.08f, 0.3f), Lamp.Pattern.Dying);
        MakeLamp(g, "Ceiling light", P(52f, 36f, 2.9f), 6f, 0.5f, Warm, true, false, new Vector3(0.3f, 0.08f, 0.3f), Lamp.Pattern.Flicker);
        // brass wall sconces between the portraits
        foreach (var x in new[] { 34f, 42.5f, 48f, 55f }) Sconce(g, P(x, 34.14f, 1.95f), x == 48f ? Lamp.Pattern.Flicker : Lamp.Pattern.Steady, x != 55f, 5f, 0.75f);
        Emit(P(42f, 36f, 2.6f), "amb_rain_roof", 0.35f);
        F(g, "Console table", P(56.6f, 36f, 0.4f), new Vector3(0.45f, 0.8f, 1.4f), "Wood_Panel");
        Mirror(g, P(57.9f, 36f, 1.6f), Vector3.left, new Vector2(0.9f, 0.7f));

        // ---- the lift lobby: black marble, a brass chandelier, the lift that won't go down again
        g = Group("PH Lift Lobby");
        Crown(g, R("PH Lift Lobby").r, 3, 3.0f);
        MakeLamp(g, "Chandelier", P(47f, 40.5f, 2.55f), 7f, 0.9f, Warm, true, false, new Vector3(0.25f, 0.18f, 0.25f), Lamp.Pattern.Flicker);
        F(g, "Bench", P(43.0f, 41f, 0.25f), new Vector3(0.5f, 0.5f, 1.8f), "Velvet_Dark");
        F(g, "Column", P(44.2f, 38.6f, 1.5f), new Vector3(0.45f, 3.0f, 0.45f), "Marble_Dark");
        F(g, "Column", P(49.8f, 38.6f, 1.5f), new Vector3(0.45f, 3.0f, 0.45f), "Marble_Dark");
        Emit(P(47f, 43.6f, 1.5f), "elev_shaft_wind", 0.3f);
        WallNote(g, "Lift note", P(50.8f, 43.86f, 1.5f), Vector3.back, "ON THE LIFT DOORS", "SERVICE LIFT\n\nTOP FLOOR\n\nThe motor room is on the roof, over the shaft. Keep out.", false);

        // ---- the butler's pantry: tiles, silver, a dumbwaiter
        g = Group("Butler's Pantry");
        Tube(g, P(30f, 41f, 2.8f), 6f, Lamp.Pattern.Buzz);
        Bottles(g, P(32.0f, 43.5f, 0.91f), 3, "Wine bottles: throw one and it smashes where it lands.");      // on the counter (they stood in the air)
        F(g, "Counter", P(30f, 43.5f, 0.45f), new Vector3(4.5f, 0.9f, 0.8f), "Tile_Kitchen");
        F(g, "Dumbwaiter", P(26.4f, 40f, 1.2f), new Vector3(0.6f, 0.8f, 0.8f), "Metal_Lift");
        for (int i = 0; i < 4; i++) Box("Silver tray", g, P(28.6f + i * 0.5f, 43.4f, 0.92f), new Vector3(0.4f, 0.02f, 0.3f), M("Metal_Lift"), LProp, false);
        MakeHide(g, "Pantry shelving", P(32.9f, 40f, 1.0f), new Vector3(0.6f, 2.0f, 2.2f), 90f, "shelf", new Vector3(-0.7f, 0.5f, 0f), M("Wood_Furniture"), 270f);
        DeskNote(g, "Pantry note", P(28.5f, 43.5f, 0.91f), "ON THE COUNTER", PantryNote);

        g = Group("Plant Room");
        Tube(g, P(55f, 41f, 2.8f), 6f, Lamp.Pattern.Dying, true, 0.9f, false);
        Device(g, "air handler", P(55f, 42.6f, 0.9f), new Vector3(4.0f, 1.8f, 1.6f), "Metal_Device", new[] { "amb_fan" }, 70f, 0.7f, 60f, mask: 16f, maskR: 8f,
            desc: "The air handler: drones for a minute. A lure, and it covers noise near it.", startClip: "power_up").attendTime = 10f;
        F(g, "Duct", P(53f, 41f, 2.6f), new Vector3(0.8f, 0.6f, 5.6f), "Metal_Corrugated");

        g = Group("Roof Stair");
        Emergency(g, P(41.8f, 38.6f, 2.6f), 6f);
        Emergency(g, W(41.8f, 43.6f, 4, 2.4f), 6f);

        // ---- the servants' passage: bare, narrow, one bulb
        g = Group("Servants' Passage");
        Bulb(g, P(33.5f, 24f, 2.3f), 4f, 0.35f, Lamp.Pattern.Flicker);
        Bulb(g, P(33.5f, 31.5f, 2.3f), 4f, 0.3f, Lamp.Pattern.Dying, false);
        F(g, "Coat hooks", P(33.08f, 21f, 1.6f), new Vector3(0.04f, 0.08f, 1.2f), "Brass_Fitting", 0f, false);

        // ---- Suite 901: an art deco guest suite (parquet, fabric walls, clawfoot bath, writing desk)
        g = Group("Suite 901");
        var r901 = R("Suite 901").r;
        Crown(g, r901, 3, 3.0f);
        Bed(g, P(28.2f, 17.4f), 0f, true);
        Box("Headboard (deco)", g, P(28.2f, 16.3f, 1.0f), new Vector3(1.9f, 1.4f, 0.1f), M("Velvet_Dark"), LProp, false);
        TableLamp(g, P(26.6f, 15.0f, 0.6f));
        F(g, "Side table", P(26.6f, 15.0f, 0.3f), new Vector3(0.4f, 0.6f, 0.4f), "Wood_Panel");
        MakeHide(g, "Wardrobe 901", P(32.4f, 31.6f, 1.0f), new Vector3(0.6f, 2.0f, 1.1f), 270f, "wardrobe", new Vector3(0, 0.55f, 0.1f), M("Wood_Panel"));
        Tub(g, P(27.4f, 31.8f), 90f);
        Box("Screen (divider)", g, P(28.9f, 30.2f, 0.85f), new Vector3(1.6f, 1.7f, 0.04f), M("Fabric_Wall"), LProp, true, Quaternion.Euler(0, 12f, 0), false);
        F(g, "Armchair", P(27.2f, 25.2f, 0.4f), new Vector3(0.8f, 0.8f, 0.8f), "Velvet_Dark", 20f);
        F(g, "Armchair", P(27.2f, 27.0f, 0.4f), new Vector3(0.8f, 0.8f, 0.8f), "Velvet_Dark", -15f);
        F(g, "Round table", P(28.4f, 26.1f, 0.35f), new Vector3(0.7f, 0.7f, 0.7f), "Marble_Dark");
        F(g, "Writing desk", P(32.3f, 18.0f, 0.38f), new Vector3(0.7f, 0.76f, 1.4f), "Wood_Panel");
        Device(g, "radio", P(32.3f, 18.4f, 0.84f), new Vector3(0.3f, 0.15f, 0.14f), "Wood_Furniture", new[] { "radio_station_5", "radio_station_2" }, 60f, 0.4f, 40f,
            desc: "A walnut radio: a lure (40 s).").attendTime = 4f;
        CeilingLamp(g, r901, 0.7f, Lamp.Pattern.Flicker);

        // ---- the owner's suite: his screening room
        g = Group("Owner's Suite");
        var rs = R("Owner's Suite").r;
        Crown(g, rs, 3, 3.0f);
        MakeLamp(g, "Chandelier", P(42f, 28f, 2.55f), 10f, 0.95f, Warm, true, true, new Vector3(0.3f, 0.2f, 0.3f), Lamp.Pattern.Flicker);
        MonitorWall(g, P(35.4f, 33.8f, 0.55f), Vector3.right, Vector3.back, 7, 4);
        Box("Monitor stand", g, P(37.3f, 33.55f, 0.27f), new Vector3(4.6f, 0.54f, 0.5f), M("Wood_Panel"), LProp, true, null, false);
        MakeHide(g, "Sofa", P(37.6f, 30.0f, 0.4f), new Vector3(2.4f, 0.8f, 0.9f), 180f, "desk", new Vector3(0f, -0.1f, -0.75f), M("Velvet_Dark"), 0f);
        F(g, "Coffee table", P(37.6f, 31.6f, 0.22f), new Vector3(1.4f, 0.44f, 0.6f), "Marble_Dark");
        for (int i = 0; i < 6; i++) Box("Tape", g, P(37.1f + (i % 3) * 0.22f, 31.5f + (i / 3) * 0.14f, 0.46f), new Vector3(0.19f, 0.025f, 0.1f), M("Plastic_Device"), LProp, false, Quaternion.Euler(0, Rnd(-30, 30), 0));
        F(g, "Projector", P(37.6f, 27.6f, 0.9f), new Vector3(0.4f, 0.3f, 0.5f), "Metal_Device");
        F(g, "Projector stand", P(37.6f, 27.6f, 0.37f), new Vector3(0.5f, 0.74f, 0.5f), "Wood_Panel");
        Device(g, "grand piano", P(47.0f, 31.2f, 0.55f), new Vector3(1.6f, 1.1f, 2.0f), "Plastic_Device", new[] { "piano_tune_grand" }, 74f, 0.7f, 25f,
            desc: "The owner's grand piano: a loud lure (25 s).", startClip: "door_creak").attendTime = 6f;
        MakeHide(g, "Chesterfield", P(46.8f, 25.0f, 0.4f), new Vector3(2.2f, 0.8f, 0.9f), 0f, "desk", new Vector3(0f, -0.1f, -0.75f), M("Velvet_Dark"), 180f);
        TapeShelf(g, P(34.3f, 24.05f), false, 2.5f);                    // clear of the passage door (z 27) and the bar counter
        TapeShelf(g, P(34.3f, 31.4f), false, 3.0f);
        // the fireplace on the east wall, a low glow
        F(g, "Fireplace surround", P(49.7f, 27.6f, 0.65f), new Vector3(0.5f, 1.3f, 2.0f), "Marble_Dark");
        Box("Firebox", g, P(49.5f, 27.6f, 0.4f), new Vector3(0.3f, 0.7f, 1.1f), M("Soot_Floor"), LProp, false);
        MakeLamp(g, "Fire glow", P(49.3f, 27.6f, 0.35f), 3.2f, 0.22f, new Color(1f, 0.62f, 0.38f), true, false, new Vector3(0.2f, 0.08f, 0.5f), Lamp.Pattern.Candle);
        Portrait(g, P(49.9f, 27.6f, 2.0f), Vector3.left, true);
        // the bar in the south-west corner
        // the bar sits back against the wall, well clear of the study door (x 38): it used to pinch the way to it
        F(g, "Bar counter", P(35.4f, 22.35f, 0.55f), new Vector3(2.2f, 1.1f, 0.6f), "Wood_Panel");
        Bottles(g, P(34.9f, 22.35f, 1.12f), 3, "The owner's whisky: throw a bottle and it smashes where it lands.");
        foreach (var x in new[] { 34.9f, 35.9f }) F(g, "Bar stool", P(x, 23.25f, 0.35f), new Vector3(0.35f, 0.7f, 0.35f), "Velvet_Dark");
        foreach (var (x, z) in new[] { (40.2f, 24.6f), (44.0f, 24.6f) }) F(g, "Column", P(x, z, 1.5f), new Vector3(0.5f, 3.0f, 0.5f), "Marble_Dark");
        Cable(g, P(36f, 33.2f), P(36.2f, 28f)); Cable(g, P(38.6f, 33.2f), P(37.6f, 28.2f)); Cable(g, P(40.5f, 33.2f), P(44f, 30f));
        WallNote(g, "Chain", P(42f, 34.15f, 1.3f), Vector3.back, "THE DOOR TO THE CORRIDOR", "Chained and padlocked from this side.\n\nWhoever lived here didn't want anything coming in from the corridor.", false);
        TableLamp(g, P(48.6f, 23.0f, 0.75f));
        TableLamp(g, P(36.2f, 22.35f, 1.1f));
        F(g, "Side table", P(48.6f, 23.0f, 0.37f), new Vector3(0.5f, 0.75f, 0.5f), "Wood_Panel");

        // ---- the study: shelves to the ceiling, his diary, the board of faces, the keycard
        g = Group("Owner's Study");
        Crown(g, R("Owner's Study").r, 3, 3.0f);
        F(g, "Desk", P(38.6f, 15.8f, 0.38f), new Vector3(1.8f, 0.76f, 0.8f), "Wood_Panel");
        TableLamp(g, P(37.7f, 15.6f, 0.76f));
        Item(g, "Roof keycard", LevelLayout.KeycardSpot, new Vector3(0.09f, 0.005f, 0.055f), "Plastic_Beige", "roofcard", "owner's keycard");
        DeskNote(g, "Owner's diary", P(39.4f, 15.9f, 0.78f), "THE OWNER'S DIARY", OwnerDiary);
        F(g, "Typewriter", P(38.0f, 16.0f, 0.86f), new Vector3(0.4f, 0.18f, 0.32f), "Metal_Device");
        TapeShelf(g, P(34.3f, 16.5f), false, 2.6f, 6);                  // clear of the door from the passage (z 18.15 to 19.05)
        TapeShelf(g, P(41.7f, 20.4f), false, 2.4f, 6);
        Box("Cork board", g, P(36.2f, 21.9f, 1.6f), new Vector3(1.6f, 1.0f, 0.03f), M("Wood_Furniture"), LProp, false);
        for (int i = 0; i < 8; i++) Quad("Photo", g, P(35.65f + (i % 4) * 0.36f, 21.87f, 1.82f - (i / 4) * 0.4f), new Vector2(0.24f, 0.3f), Quaternion.LookRotation(Vector3.forward), M("Blob_Face"));
        WallNote(g, "The board", P(36.2f, 21.86f, 1.05f), Vector3.back, "THE BOARD ABOVE THE DESK", CorkBoard, false);
        MakeHide(g, "Study desk (under)", P(38.6f, 15.8f, 0.38f), new Vector3(1.6f, 0.7f, 0.6f), 0f, "desk", new Vector3(0f, -0.15f, 0.1f), M("Wood_Panel"), 0f, false);
        F(g, "Leather chair", P(38.6f, 16.9f, 0.45f), new Vector3(0.6f, 0.9f, 0.6f), "Fabric_Cover");

        // ---- the owner's bedroom: a four-poster, a vanity, his own TV
        g = Group("Owner's Bedroom");
        Crown(g, R("Owner's Bedroom").r, 3, 3.0f);
        FourPoster(g, P(47.5f, 15.3f));
        TableLamp(g, P(45.9f, 14.6f, 0.6f));
        F(g, "Nightstand", P(45.9f, 14.6f, 0.3f), new Vector3(0.45f, 0.6f, 0.4f), "Wood_Panel");
        F(g, "Vanity", P(42.4f, 15.6f, 0.4f), new Vector3(0.5f, 0.8f, 1.2f), "Wood_Panel");
        Mirror(g, P(42.08f, 15.6f, 1.45f), Vector3.right, new Vector2(0.8f, 0.9f));
        MakeHide(g, "Wardrobe (owner)", P(43.0f, 21.4f, 1.0f), new Vector3(1.1f, 2.0f, 0.6f), 180f, "wardrobe", new Vector3(0, 0.55f, 0.1f), M("Wood_Panel"));
        F(g, "TV on a stand", P(49.4f, 20.6f, 1.0f), new Vector3(0.45f, 0.5f, 0.55f), "Plastic_Device");
        F(g, "TV stand", P(49.4f, 20.6f, 0.375f), new Vector3(0.5f, 0.75f, 0.6f), "Wood_Panel");
        Quad("TV screen", g, P(49.16f, 20.6f, 1.02f), new Vector2(0.4f, 0.3f), Quaternion.LookRotation(Vector3.right), M("Screen"));

        // ---- Suite 903: the other guest suite, flooded from the roof
        g = Group("Suite 903");
        var r903 = R("Suite 903").r;
        Crown(g, r903, 3, 3.0f);
        Bed(g, P(55.8f, 17.4f), 0f, true);
        Box("Headboard (deco)", g, P(55.8f, 16.3f, 1.0f), new Vector3(1.9f, 1.4f, 0.1f), M("Velvet_Dark"), LProp, false);
        TableLamp(g, P(57.4f, 15.0f, 0.6f));
        Tub(g, P(56.6f, 31.6f), 90f);
        MakeHide(g, "Wardrobe 903", P(50.6f, 31.5f, 1.0f), new Vector3(0.6f, 2.0f, 1.1f), 90f, "wardrobe", new Vector3(0, 0.55f, 0.1f), M("Wood_Panel"));
        Device(g, "TV", P(57.5f, 23f, 0.95f), new Vector3(0.5f, 0.5f, 0.6f), "Plastic_Device", new[] { "tv_show", "tv_news" }, 62f, 0.5f, 40f, desc: "A TV: a lure (40 s).", startClip: "tv_click").attendTime = 4f;
        F(g, "TV cabinet", P(57.5f, 23f, 0.35f), new Vector3(0.5f, 0.7f, 1.2f), "Wood_Panel");
        F(g, "Bar cart", P(51.0f, 26.5f, 0.4f), new Vector3(0.5f, 0.8f, 0.9f), "Brass_Fitting");
        Puddle(g, 54f, 28f, 3, 2.2f, 1.6f);
        Box("Fallen ceiling panel", g, P(54.6f, 28.4f, 0.04f), new Vector3(1.2f, 0.06f, 1.2f), M("Ceiling_Plaster"), LProp, false, Quaternion.Euler(4, 20, 0));
        CeilingLamp(g, r903, 0.6f, Lamp.Pattern.Dying);
    }

    // a ceiling fitting in the middle of a room (no switch)
    static void CeilingLamp(Transform g, Rect r, float intensity, Lamp.Pattern p)
    {
        MakeLamp(g, "Ceiling light", W(r.center.x, r.center.y, 3, 2.9f), Mathf.Max(r.width, r.height) * 0.8f, intensity, new Color(1f, 0.88f, 0.72f), true, false, new Vector3(0.35f, 0.08f, 0.35f), p);
    }

    // sheet-metal ribs on the outside of a roof hut
    static void Ribs(Transform g, Rect r)
    {
        float y = LevelLayout.RoofTop + 1.5f;
        for (float x = r.xMin + 0.4f; x < r.xMax - 0.2f; x += 0.6f)
            Box("Rib", g, new Vector3(x, y, r.yMin - 0.02f), new Vector3(0.05f, 3.0f, 0.04f), M("Metal_Corrugated"), LProp, false, null, false);
    }

    static void RoofTop(Refs refs)
    {
        Vector3 Q(float x, float z, float up = 0f) => W(x, z, 4, up);

        var g = Group("Roof Deck");
        // water tanks and air units to hide behind; skylights; the antenna mast; puddles everywhere
        MakeHide(g, "Water tank", Q(31.5f, 29.0f, 1.7f), new Vector3(3.0f, 3.2f, 3.0f), 0f, "cage", new Vector3(0f, -0.5f, 1.9f), M("Metal_Lift"), 0f);
        F(g, "Water tank", Q(36.0f, 29.0f, 1.7f), new Vector3(3.0f, 3.2f, 3.0f), "Metal_Lift");                  // on their plinth (0.1 m)
        F(g, "Tank plinth", Q(33.8f, 29.0f, 0.05f), new Vector3(8.0f, 0.1f, 3.4f), "Metal_Device");     // low: a step you walk up, not a ledge
        MakeHide(g, "Air unit", Q(50.5f, 18.5f, 0.75f), new Vector3(3.0f, 1.5f, 2.0f), 0f, "cage", new Vector3(0f, -0.2f, 1.4f), M("Metal_Device"), 0f);
        F(g, "Air unit", Q(38.5f, 19.5f, 0.75f), new Vector3(3.0f, 1.5f, 2.0f), "Metal_Device");
        F(g, "Skylight", Q(43.0f, 26.0f, 0.3f), new Vector3(4.0f, 0.6f, 2.4f), "Metal_Lift");
        Box("Skylight glass", g, Q(43.0f, 26.0f, 0.62f), new Vector3(3.8f, 0.04f, 2.2f), M("Mirror_Glass"), LProp, false);
        F(g, "Antenna mast", Q(57.0f, 36.6f, 4.0f), new Vector3(0.3f, 8.0f, 0.3f), "Metal_Device");
        F(g, "Dish", Q(56.4f, 36.6f, 3.0f), new Vector3(0.2f, 1.4f, 1.4f), "Metal_Lift", 0f, false);
        // a flat cable cover you walk over (a 30 cm tray here blocked the antenna room door: it is exactly the step height)
        F(g, "Cable cover", Q(56.6f, 37.4f, 0.02f), new Vector3(1.0f, 0.04f, 0.3f), "Metal_Device", 0f, false);
        F(g, "Walkway grating", Q(46.5f, 31.5f, 0.05f), new Vector3(1.0f, 0.1f, 9.0f), "Metal_Floor");
        foreach (var (x, z) in new[] { (30f, 22f), (41f, 33f), (47f, 21f), (53f, 30f), (36f, 16f), (45f, 36f) }) Puddle(g, x, z, 4, Rnd(1.6f, 2.8f), Rnd(1.0f, 2.0f));
        Ribs(g, R("Siren Housing").r); Ribs(g, R("Lift Machine Room").r); Ribs(g, R("Antenna Room").r);
        // the PA horn on its mast: the big lure
        F(g, "PA mast", Q(27.0f, 15.0f, 2.5f), new Vector3(0.2f, 5.0f, 0.2f), "Metal_Device");
        F(g, "PA horn", Q(27.0f, 15.3f, 4.9f), new Vector3(0.5f, 0.5f, 0.8f), "Metal_Lift", 0f, false);
        Device(g, "PA horn", LevelLayout.SirenSpot, new Vector3(0.5f, 0.6f, 0.35f), "Metal_Lift", new[] { "elev_muzak" }, 82f, 0.9f, 35f, mask: 10f, maskR: 10f,
            desc: "The roof PA: blares the hotel's lift music across the roof for 35 s. The loudest lure up here: it stands under it a long time.", startClip: "radio_click").attendTime = 16f;
        WallNote(g, "Note on the PA", Q(27.6f, 15.36f, 1.5f), Vector3.forward, "TAPED TO THE PA AMPLIFIER", RoofNote);
        Emergency(g, Q(37.9f, 37.85f, 2.4f), 6f);
        Emergency(g, Q(52.1f, 37.85f, 2.4f), 6f);
        // the storm: rain, wind, and the lightning that shows you the roof
        foreach (var (x, z) in new[] { (32f, 20f), (48f, 20f), (32f, 33f), (48f, 33f) }) Emit(Q(x, z, 2.0f), "amb_rain", 0.5f);
        Emit(Q(42f, 26f, 3.0f), "amb_wind", 0.4f);
        foreach (var (x, z) in new[] { (34f, 22f), (50f, 26f), (40f, 34f) })
        {
            var lg = new GameObject("Lightning light");
            lg.transform.SetParent(g, false);
            lg.transform.position = Q(x, z, 14f);
            lg.transform.rotation = Quaternion.LookRotation(Vector3.down);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 120f; l.innerSpotAngle = 60f; l.range = 30f; l.intensity = 40f;
            l.color = new Color(0.86f, 0.9f, 1f);
            l.shadows = LightShadows.Hard;
            l.enabled = false;
            lg.AddComponent<UniversalAdditionalLightData>();
        }

        g = Group("Lift Machine Room");
        Tube(g, Q(47f, 41f, 2.8f), 6f, Lamp.Pattern.Dying);
        F(g, "Lift motor", Q(44.2f, 41.5f, 0.75f), new Vector3(2.2f, 1.5f, 1.6f), "Metal_Device");
        F(g, "Sheave", Q(45.6f, 41.5f, 1.4f), new Vector3(0.3f, 1.2f, 1.2f), "Metal_Lift");
        F(g, "Controller cabinet", Q(51.6f, 40f, 1.0f), new Vector3(0.6f, 2.0f, 1.2f), "Metal_Corrugated");
        Box("Shaft hatch", g, Q(47f, 43.0f, 0.02f), new Vector3(1.8f, 0.04f, 1.6f), M("Soot_Floor"), LProp, false);
        Emit(Q(44.2f, 41.5f, 1.2f), "amb_elevator_machine", 0.35f);

        // the antenna room: crammed with the broadcast, a wall of screens showing nothing but snow
        g = Group("Antenna Room");
        Tube(g, Q(55f, 41f, 2.8f), 6f, Lamp.Pattern.Flicker, true, 0.9f, false);
        F(g, "Rack", Q(57.45f, 41f, 0.62f), new Vector3(0.55f, 1.24f, 0.9f), "Metal_Device");
        F(g, "Transmitter", Q(57.3f, 43.0f, 0.9f), new Vector3(0.8f, 1.8f, 1.0f), "Metal_Device");
        F(g, "Rack", Q(52.5f, 43.4f, 1.0f), new Vector3(0.9f, 2.0f, 0.6f), "Metal_Device");
        for (int i = 0; i < 6; i++) Box("Status lamp", g, Q(52.2f + (i % 3) * 0.2f, 43.08f, 1.4f + (i / 3) * 0.2f), new Vector3(0.03f, 0.03f, 0.01f), M(i % 2 == 0 ? "Lamp_Red" : "Brass_Fitting"), LProp, false);
        MonitorWall(g, Q(53.6f, 43.85f, 1.3f), Vector3.right, Vector3.back, 4, 2);
        F(g, "Cable ladder", Q(55f, 41f, 2.6f), new Vector3(5.6f, 0.08f, 0.4f), "Metal_Device");
        F(g, "Chair", Q(55.4f, 41f, 0.45f), new Vector3(0.5f, 0.9f, 0.5f), "Fabric_Cover");
        var v = Box("Master VCR", g, LevelLayout.AntennaVcrSpot, new Vector3(0.34f, 0.11f, 0.46f), M("Plastic_Device"), LInteract, true, null, false, false);
        refs.vcr = v.AddComponent<SecurityVcr>();
        Box("REC light", v.transform, LevelLayout.AntennaVcrSpot + new Vector3(-0.18f, 0.01f, -0.16f), new Vector3(0.01f, 0.015f, 0.02f), M("Lamp_Red"), LProp, false, null, false, false);
        Note(g, "VCR label", LevelLayout.AntennaVcrSpot + new Vector3(-0.18f, 0.0f, 0.15f), new Vector3(0.01f, 0.05f, 0.12f), Quaternion.identity, "THE LABEL ON THE MASTER VCR", AntennaLabel, true);
        Emit(Q(57.3f, 43f, 1.2f), "amb_transformer", 0.3f);

        g = Group("Siren Housing");
        Emergency(g, Q(26.2f, 41f, 2.4f), 5f);
        F(g, "Spare horn", Q(28f, 43.2f, 0.4f), new Vector3(0.8f, 0.8f, 1.0f), "Metal_Lift");
        F(g, "Workbench", Q(31f, 43.5f, 0.45f), new Vector3(3f, 0.9f, 0.8f), "Wood_Furniture");
        MakeHide(g, "Tool lockers", Q(32.85f, 41f, 1.0f), new Vector3(0.6f, 2.0f, 2.0f), 90f, "wardrobe", new Vector3(-0.1f, 0.55f, 0f), M("Metal_Lift"), 270f);
        Bottles(g, Q(30.0f, 43.5f, 0.91f), 2, "Empty bottles.");                 // on the bench, not over its edge
    }
}
