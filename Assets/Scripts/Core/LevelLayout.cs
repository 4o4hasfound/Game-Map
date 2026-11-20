using System.Collections.Generic;
using UnityEngine;

// The hotel as data (plan metres: x east, z north). Three STAGES, each its own part of the building:
//   stage 1  THE HOTEL     floor 0 ground / lobby (y 0) and floor 1 guest floor (y 4), joined by two stairs
//                           goal: the basement key in the manager's safe (its code is in two halves upstairs)
//   stage 2  THE BASEMENT  floor -1 (y -4)   goal: power for the service lift, the lift key, call the lift
//   stage 3  THE ROOF      floor 3 penthouse (y 32) and floor 4 roof (y 35.6), joined by the roof stair
//                           goal: the master tape in the antenna room on the roof
//   floor 2  your bedroom (prologue), far away at BedroomOrigin
// Only the stage you are in is switched on (Game.ShowStage); you move between stages down the basement stair and
// up the service lift.
// Built around the monster: it is blind and hunts by sound, so every room is a lure, a tool, a hiding place,
// a quiet or loud route, a scouting camera or a clue. Decay is part of it: rubble, glass and water are loud.
// The editor builder makes the geometry from this, LevelGraph the acoustic room graph, MapExport the 2D map.
public static class LevelLayout
{
    public const float Floor2 = 4.0f;        // guest floor level
    public const float Basement = -4.0f;     // B1 level
    public const float Roof = 6.8f;
    public const float GroundWall = 4.0f;
    public const float Penthouse = 32.0f;    // stage 3: the penthouse floor (the top of the tower)
    public const float RoofTop = 35.6f;      // stage 3: the roof
    public const float UpperWall = 2.8f;
    public static readonly Vector3 BedroomOrigin = new Vector3(200f, 0f, 0f);

    public static float FloorY(int f) => f == 1 ? Floor2 : f == -1 ? Basement : f == 3 ? Penthouse : f == 4 ? RoofTop : 0f;
    public static int FloorOf(Vector3 p) => p.x > 150f ? 2 : p.y >= RoofTop - 0.4f ? 4 : p.y >= Penthouse - 0.4f ? 3 : p.y >= Floor2 - 0.4f ? 1 : p.y < -0.4f ? -1 : 0;
    // 0 the prologue (bedroom), 1 the hotel, 2 the basement, 3 the roof
    public static int StageOfFloor(int f) => f == 2 ? 0 : f == -1 ? 2 : f >= 3 ? 3 : 1;
    public static int StageOf(Vector3 p) => StageOfFloor(FloorOf(p));
    public static readonly string[] StageNames = { "THE TAPE", "PART ONE: THE HOTEL", "PART TWO: THE BASEMENT", "PART THREE: THE ROOF" };

    public class Room
    {
        public string name, floorMat, wallMat, kind, purpose;
        public int floor;
        public Rect r;
        public float ceil;
        public bool twoStorey;        // the lobby and the staff stair also occupy the guest floor
        public bool noFloor;          // the basement stairhead: open to the floor below (slabs added separately)
        public string decay = "";     // "", "light", "burnt", "flooded", "collapsed"
        public Room(string n, int f, float x0, float z0, float x1, float z1, string floorMat, string wallMat, float ceil, string kind, string purpose, bool two = false)
        { name = n; floor = f; r = Rect.MinMaxRect(x0, z0, x1, z1); this.floorMat = floorMat; this.wallMat = wallMat; this.ceil = ceil; this.kind = kind; this.purpose = purpose; twoStorey = two; }
        public float Y => FloorY(floor);
        public Room D(string d) { decay = d; return this; }
        public Room NoFloor() { noFloor = true; return this; }
        public string ceilingMat() => floor == 2 ? "Ceiling_Home" : floor == -1 || floor == 4 ? "Concrete_Wall" : floor == 3 ? (kind == "service" || kind == "stair" ? "Concrete_Wall" : "Ceiling_Plaster") : decay == "burnt" ? "Plaster_Burnt" : (kind == "corridor" || kind == "service" || kind == "office" || kind == "lair") ? "Ceiling_Tiles" : "Ceiling_Plaster";
    }

    public enum Gap { Door, Opening, Railing, Locked, Static, Lift, Window, Jammed }

    public class Opening
    {
        public string name;
        public int floor;
        public Vector2 a, b;
        public Gap kind;
        public string key;
        public bool stuck, startOpen, bolt;   // bolt: can be bolted from inside (the monster can't open it)
        public float height = 2.1f;
        public Opening(string n, int f, float x0, float z0, float x1, float z1, Gap k, string key = null)
        { name = n; floor = f; a = new Vector2(x0, z0); b = new Vector2(x1, z1); kind = k; this.key = key; }
        public Vector2 Mid => (a + b) * 0.5f;
        public bool AlongX => Mathf.Abs(a.y - b.y) < 0.01f;
        public float Width => Vector2.Distance(a, b);
    }

    static Opening DX(string n, int f, float x, float zc, float w = 0.9f) => new Opening(n, f, x, zc - w / 2, x, zc + w / 2, Gap.Door);
    static Opening DZ(string n, int f, float z, float xc, float w = 0.9f) => new Opening(n, f, xc - w / 2, z, xc + w / 2, z, Gap.Door);
    static Opening OX(string n, int f, float x, float z0, float z1) => new Opening(n, f, x, z0, x, z1, Gap.Opening);
    static Opening OZ(string n, int f, float z, float x0, float x1) => new Opening(n, f, x0, z, x1, z, Gap.Opening);
    static Opening RX(string n, int f, float x, float z0, float z1) => new Opening(n, f, x, z0, x, z1, Gap.Railing);
    static Opening RZ(string n, int f, float z, float x0, float x1) => new Opening(n, f, x0, z, x1, z, Gap.Railing);
    static Opening B(Opening o) { o.bolt = true; return o; }
    static Opening W1(string n, int f, float x, float zc, float w = 1.6f) => new Opening(n, f, x, zc - w / 2, x, zc + w / 2, Gap.Window);
    static Opening WZ(string n, int f, float z, float xc, float w = 1.6f) => new Opening(n, f, xc - w / 2, z, xc + w / 2, z, Gap.Window);

    const string Lino = "Linoleum_Staff", Marble = "Marble_Lobby", Carpet = "Carpet_Room", CarpetH = "Carpet_Hall", CarpetG = "Carpet_Gallery";
    const string Conc = "Concrete_Stair", Wood = "Wood_Floor", Tile = "Tile_Kitchen";

    public static readonly List<Room> Rooms = new List<Room>
    {
        // ================================================================= ground floor (0)
        new Room("Grand Lobby", 0, 20, 8, 40, 26, Marble, "Wallpaper_Lobby", Roof, "lobby", "Marble is loud: cross on the rugs. The atrium lets you see (and hear) the guest floor galleries. CCTV.", true).D("light"),
        new Room("Entrance Vestibule", 0, 26, 2, 34, 8, Marble, "Wallpaper_Lobby", 3.7f, "lobby", "The street doors open on white static: the edge of the tape. No way out.").D("light"),
        new Room("Luggage Room", 0, 14, 2, 20, 10, Wood, "Paint_Staff", 3.2f, "service", "Luggage cage to hide in. Glass bottles (throw one to make a noise somewhere else).").D("light"),
        new Room("Front Office", 0, 14, 10, 20, 18, CarpetH, "Paint_Office", 3.2f, "office", "START. The previous viewer's letter explains everything. Door to the reception desk."),
        new Room("Concierge", 0, 14, 18, 20, 26, CarpetH, "Paint_Office", 3.2f, "office", "Desk radio: switch it on to lure it to the lobby. Second door to the staff passage (a loop).").D("light"),
        new Room("West Staff Passage", 0, 12, 2, 14, 38, Lino, "Paint_Staff", 3.0f, "corridor", "The quiet back route behind the front-of-house rooms. Every room has a second door here."),
        new Room("Staff Lockers", 0, 6, 2, 12, 10, Lino, "Paint_Staff", 3.0f, "service", "Lockers to hide in. A portable radio to carry and set down as a lure.").D("light"),
        new Room("Security Office", 0, 6, 10, 12, 18, Lino, "Paint_Office", 3.0f, "office", "CCTV monitors of all three floors: find it before you go anywhere. Connecting door to the manager's office."),
        new Room("Manager's Office", 0, 6, 18, 12, 26, CarpetH, "Paint_Office", 3.0f, "office", "The wall safe with the basement key (a four-digit code, in two halves upstairs). The hotel's story in the files. Desk to hide under.").D("light"),
        new Room("Mail Room", 0, 6, 26, 12, 32, Lino, "Paint_Staff", 3.0f, "service", "Letters from earlier viewers (dated after 2003) that were never collected.").D("light"),
        new Room("Staff Stair", 0, 6, 32, 12, 40, Conc, "Paint_Staff", Roof, "stair", "Ground floor to guest floor, away from the lobby. Two flights.", true),
        new Room("Chapel", 0, 14, 26, 24, 38, Wood, "Wallpaper_Room", 3.7f, "room", "The organ: the loudest lure in the hotel. Pews to hide behind. Three doors.").D("light"),
        new Room("Restaurant", 0, 24, 26, 42, 38, CarpetG, "Wallpaper_Hall", 3.7f, "room", "Piano (lure). Tables with long cloths to hide under. The crashed chandelier: glass all round it.").D("collapsed"),
        new Room("Dish Pit", 0, 42, 26, 48, 30, Tile, "Paint_Staff", 3.2f, "service", "Stacked plates: knock them and everything hears. Short cut between restaurant and kitchen.").D("flooded"),
        new Room("Cold Room", 0, 48, 26, 54, 30, "Metal_Floor", "Metal_Device", 2.6f, "service", "Heavy door you can pull shut from inside: a hiding place it doesn't hear into."),
        new Room("Kitchen", 0, 42, 30, 54, 38, Tile, "Paint_Staff", 3.2f, "service", "Tile and hanging pans: a loud room. Swing doors to the restaurant. Bottles."),
        new Room("North Staff Corridor", 0, 12, 38, 54, 40, Lino, "Paint_Staff", 3.0f, "corridor", "Links the staff stair, chapel, restaurant, kitchen and the locked basement door."),
        new Room("East Service Hall", 0, 54, 2, 56, 40, Lino, "Paint_Staff", 3.0f, "corridor", "The east back route: gift shop, telephones, bar storage, kitchen.").D("light"),
        new Room("Gift Shop", 0, 40, 2, 54, 10, Tile, "Wallpaper_Lobby", 3.4f, "room", "Music box on the counter (a quiet lure that plays for a while).").D("light"),
        new Room("Blue Note Bar", 0, 40, 10, 50, 26, Wood, "Wallpaper_Hall", 3.6f, "room", "Jukebox (lure). Broken glass behind the bar (loud). Bottles to throw.").D("light"),
        new Room("Telephone Room", 0, 50, 10, 54, 18, Carpet, "Paint_Office", 3.0f, "service", "Payphones: dial a guest room to make its phone ring upstairs."),
        new Room("Bar Storage", 0, 50, 18, 54, 26, Conc, "Paint_Staff", 3.0f, "service", "Crates of bottles (throwables). Shelving to hide behind."),
        new Room("Laundry Chute Room", 0, 12, 40, 18, 44, Conc, "Paint_Staff", 3.0f, "service", "The chute from the guest floor falls past here to the basement laundry. Drop a bottle down it: it smashes in the basement."),
        new Room("Basement Stairhead", 0, 18, 40, 30, 44, Conc, "Paint_Staff", 3.4f, "stair", "LOCKED: the key is in the manager's safe. Down these stairs is part two.").NoFloor(),
        new Room("Service Lift Lobby", 0, 40, 40, 48, 44, Lino, "Paint_Staff", 3.0f, "lair", "The dead lift. It climbs the shaft between floors: you hear it inside.").D("light"),
        new Room("Loading Dock", 0, 48, 40, 56, 44, Conc, "Paint_Staff", 3.4f, "service", "The roller door opens on static. Rubble.").D("collapsed"),

        // ================================================================= guest floor (1)
        new Room("West Gallery", 1, 20, 8, 22, 24, CarpetG, "Wallpaper_Hall", UpperWall, "gallery", "Overlooks the lobby."),
        new Room("North Gallery", 1, 20, 24, 40, 26, CarpetG, "Wallpaper_Hall", UpperWall, "gallery", "House phone (the switchboard can ring it). Seen from reception. CCTV."),
        new Room("East Gallery", 1, 38, 8, 40, 24, CarpetG, "Wallpaper_Hall", UpperWall, "gallery", "Overlooks the lobby; the grand stair arrives at its north end."),
        new Room("South Mezzanine", 1, 20, 2, 40, 8, CarpetG, "Wallpaper_Hall", UpperWall, "gallery", "Writing desks above the entrance; joins the galleries into a full loop round the atrium.").D("light"),
        new Room("West Corridor", 1, 12, 2, 14, 38, CarpetH, "Wallpaper_Hall", UpperWall, "corridor", "Guest corridor. The north end is half blocked by fallen ceiling (rubble is loud). CCTV.").D("collapsed"),
        new Room("West Cross Hall", 1, 14, 24, 20, 26, CarpetH, "Wallpaper_Hall", UpperWall, "corridor", "Joins the west corridor to the galleries."),
        new Room("Room 201", 1, 4, 2, 12, 8, Carpet, "Wallpaper_Room", UpperWall, "room", "Wardrobe. Connecting door to 203.").D("light"),
        new Room("Room 203", 1, 4, 8, 12, 14, Carpet, "Wallpaper_Room", UpperWall, "room", "Bed to hide under. Note about bolting doors.").D("light"),
        new Room("Room 205", 1, 4, 14, 12, 20, Carpet, "Wallpaper_Room", UpperWall, "room", "The TV: switch it on and it comes to watch (a lure). Note about lures.").D("light"),
        new Room("Room 207", 1, 4, 20, 12, 26, "Soot_Floor", "Plaster_Burnt", UpperWall, "room", "Burnt out. Ash and glass underfoot: loud. A hole through to 209.").D("burnt"),
        new Room("Room 209", 1, 4, 26, 12, 32, "Soot_Floor", "Plaster_Burnt", UpperWall, "room", "The fire started here. Its corridor door is jammed; reach it through 207. The previous viewer's bag, with the other half of the safe code.").D("burnt"),
        new Room("Room 202", 1, 14, 2, 20, 8, Carpet, "Wallpaper_Room", UpperWall, "room", "Wardrobe. The minibar bottles are still here (throwables).").D("light"),
        new Room("Room 204", 1, 14, 8, 20, 14, Carpet, "Wallpaper_Room", UpperWall, "room", "Radio on the nightstand (lure).").D("light"),
        new Room("Room 206", 1, 14, 14, 20, 20, Carpet, "Wallpaper_Room", UpperWall, "room", "Bathtub to hide in.").D("flooded"),
        new Room("Room 208", 1, 14, 20, 20, 24, Carpet, "Wallpaper_Room", UpperWall, "room", "Small single. Bottles on the minibar.").D("light"),
        new Room("Housekeeping", 1, 14, 26, 20, 32, Lino, "Paint_Staff", UpperWall, "service", "Linen carts to hide in. Staff door into the ballroom.").D("light"),
        new Room("Ice & Vending", 1, 14, 32, 20, 38, Tile, "Paint_Staff", UpperWall, "service", "Vending machine: buy a can (one loud thump). The ice machine hums and covers your steps."),
        new Room("Ballroom", 1, 20, 26, 40, 38, Wood, "Wallpaper_Lobby", UpperWall, "room", "Where it waits at the start. Grand piano (lure). Stage curtains to hide behind. Three ways out.").D("collapsed"),
        new Room("Guest North Corridor", 1, 12, 38, 48, 40, CarpetH, "Wallpaper_Hall", UpperWall, "corridor", "Links the staff stair, linen chute, ballroom, lift and the east wing.").D("light"),
        new Room("Linen Chute", 1, 12, 40, 18, 44, Lino, "Paint_Staff", UpperWall, "service", "The laundry chute down to the basement. Something drops down it now and then."),
        new Room("Lift Vestibule", 1, 42, 40, 48, 44, Lino, "Paint_Staff", UpperWall, "lair", "The dead lift. It goes into the shaft here when it leaves the floor."),
        new Room("East Cross Hall", 1, 40, 24, 46, 26, CarpetH, "Wallpaper_Hall", UpperWall, "corridor", "Joins the galleries to the east corridor."),
        new Room("East Corridor", 1, 46, 2, 48, 38, CarpetH, "Wallpaper_Hall", UpperWall, "corridor", "Wet carpet near 214 (squelches). CCTV.").D("flooded"),
        new Room("Room 212", 1, 40, 2, 46, 8, Carpet, "Wallpaper_Room", UpperWall, "room", "Connecting door into 214: the quiet way in.").D("light"),
        new Room("Room 214", 1, 40, 8, 46, 14, Carpet, "Wallpaper_Room", UpperWall, "room", "Half of the safe code, behind the dresser. The front door's lock sticks. Mirror. Connecting doors to 212 and 216."),
        new Room("Room 216", 1, 40, 14, 46, 20, Carpet, "Wallpaper_Room", UpperWall, "room", "Wardrobe. Connecting door into 214.").D("light"),
        new Room("Room 218", 1, 40, 20, 46, 24, Carpet, "Wallpaper_Room", UpperWall, "room", "Telephone with a long cord; the bed to hide under.").D("light"),
        new Room("Room 211", 1, 48, 2, 56, 8, Carpet, "Wallpaper_Room", UpperWall, "room", "Corner suite. Record player (lure).").D("light"),
        new Room("Room 213", 1, 48, 8, 56, 14, Carpet, "Wallpaper_Room", UpperWall, "room", "Its door is bolted from inside (it can't open a bolted door). Get in through 215's connecting door: a safe room.").D("light"),
        new Room("Room 215", 1, 48, 14, 56, 20, Carpet, "Wallpaper_Room", UpperWall, "room", "Wardrobe. Connecting door to 213.").D("flooded"),
        new Room("Room 217", 1, 48, 20, 56, 26, Carpet, "Wallpaper_Room", UpperWall, "room", "Bathtub to hide in.").D("light"),
        new Room("Linen Store", 1, 40, 26, 46, 32, Lino, "Paint_Staff", UpperWall, "service", "Shelves to hide behind. Bottles of bleach (throwables)."),
        new Room("Maid's Office", 1, 40, 32, 46, 38, Lino, "Paint_Office", UpperWall, "office", "Monitors for the guest floor cameras.").D("light"),
        new Room("Staff Lounge", 1, 48, 26, 56, 32, Carpet, "Paint_Office", UpperWall, "room", "TV and sofa. A note from the hotel's night staff.").D("light"),
        new Room("Room 219", 1, 48, 32, 56, 38, Carpet, "Wallpaper_Room", UpperWall, "room", "Honeymoon suite. Music box (lure). Wardrobe.").D("light"),

        // ================================================================= basement (-1)
        new Room("Basement Stair", -1, 18, 34, 30, 44, Conc, "Concrete_Wall", 3.6f, "stair", "From the ground floor. Emergency light."),
        new Room("Main Corridor", -1, 4, 30, 56, 34, Conc, "Concrete_Wall", 3.2f, "corridor", "Puddles under the dripping pipes (splash). CCTV.").D("flooded"),
        new Room("Laundry", -1, 4, 34, 18, 44, Tile, "Concrete_Wall", 3.4f, "service", "Washing machines: start one to lure it and cover your noise. The chute drops here.").D("light"),
        new Room("Boiler Room", -1, 30, 34, 46, 44, Conc, "Concrete_Wall", 3.6f, "service", "The boiler roars on and off: while it roars, it can't hear you. Hide behind it."),
        new Room("Pump Room", -1, 46, 34, 56, 44, Conc, "Concrete_Wall", 3.4f, "lair", "The service lift: call it from here (30 s, the bell is loud). Two doors you can bolt. It comes down the shaft here too.").D("flooded"),
        new Room("Electrical Room", -1, 4, 22, 14, 30, Conc, "Concrete_Wall", 3.2f, "service", "The main breaker, with no fuse in it. Very loud when thrown. Powers the lights and the lift."),
        new Room("Recorder Room", -1, 14, 22, 22, 30, Lino, "Paint_Office", 3.0f, "office", "The night engineer's room: the lift key on the desk. The monitors show where the tape really goes: up, to the roof. The door bolts from inside."),
        new Room("Workshop", -1, 22, 22, 32, 30, Conc, "Concrete_Wall", 3.2f, "service", "Maintenance log. A portable radio. Tools on the bench (a dropped one rings out).").D("light"),
        new Room("Storage Cages", -1, 32, 14, 48, 30, Conc, "Concrete_Wall", 3.2f, "service", "The spare main fuse, in the end cage. Chain-link cages and boxes to hide behind; two doors to the corridor.").D("light"),
        new Room("Coal Store", -1, 48, 14, 56, 30, "Rubble_Floor", "Concrete_Wall", 3.2f, "lair", "Coal underfoot (loud). It rests here between hunts.").D("collapsed"),
        new Room("Wine Cellar", -1, 4, 6, 18, 22, Conc, "Brick_Wall", 2.8f, "service", "Racks of bottles (throwables). Narrow aisles to hide in. Back way to the electrical room.").D("light"),
        new Room("Flooded Passage", -1, 18, 6, 32, 22, "Water_Floor", "Brick_Wall", 2.8f, "corridor", "Knee-deep water: every step splashes. A risky short cut.").D("flooded"),
        new Room("Hotel Archive", -1, 32, 6, 48, 14, Conc, "Concrete_Wall", 3.0f, "office", "Shelves of tapes labelled 31.10.2003, one for every viewer. What the tape is.").D("light"),
        new Room("Generator Room", -1, 48, 6, 56, 14, Conc, "Concrete_Wall", 3.2f, "service", "Start the generator: a big noise for a minute (lure and cover)."),
        new Room("South Corridor", -1, 4, 2, 56, 6, Conc, "Concrete_Wall", 3.0f, "corridor", "The long back way along the south of the basement. Dark.").D("flooded"),

        // ================================================================= stage 3: the penthouse (3)
        new Room("Penthouse Corridor", 3, 26, 34, 58, 38, "Carpet_Deco", "Wood_Panel", 3.0f, "corridor", "The owner's floor: walnut panelling and portraits of earlier viewers, faces worn away. The lift at one end, the roof door in the middle, the owner's chained door.").D("light"),
        new Room("PH Lift Lobby", 3, 42, 38, 52, 44, "Marble_Dark", "Wood_Panel", 3.0f, "lair", "The lift brings you up and dies. It comes out of the shaft here too.").D("light"),
        new Room("Butler's Pantry", 3, 26, 38, 34, 44, Tile, "Paint_Staff", 3.0f, "service", "Bottles (throwables). A shelf to hide behind.").D("light"),
        new Room("Roof Stair", 3, 34, 38, 42, 44, Conc, "Concrete_Wall", 6.4f, "stair", "Up to the roof. The door from the corridor reads the owner's keycard.", true),
        new Room("Plant Room", 3, 52, 38, 58, 44, Conc, "Concrete_Wall", 3.0f, "service", "The air handler: start it and it drones for a minute (a lure, and it covers noise nearby)."),
        new Room("Servants' Passage", 3, 33, 14, 34, 34, Lino, "Paint_Staff", 2.6f, "corridor", "A one-metre passage behind the walls: the staff's way into the owner's rooms. Tight, nowhere to hide in it.").D("light"),
        new Room("Suite 901", 3, 26, 14, 33, 34, "Wood_Parquet", "Fabric_Wall", 3.0f, "room", "An art deco guest suite: parquet, a clawfoot bath, a writing desk. A door into the servants' passage: one way in to the owner's rooms.").D("light"),
        new Room("Owner's Suite", 3, 34, 22, 50, 34, "Marble_Dark", "Wood_Panel", 3.0f, "room", "The owner's screening room: a wall of monitors, shelves of tapes, cables everywhere. Chained from the corridor. Grand piano (lure), a bar, a fireplace, sofas to hide behind.").D("light"),
        new Room("Owner's Study", 3, 34, 14, 42, 22, "Wood_Parquet", "Wood_Panel", 3.0f, "office", "THE ROOF KEYCARD in the desk. The owner's diary: why the hotel records its guests."),
        new Room("Owner's Bedroom", 3, 42, 14, 50, 22, "Carpet_Deco", "Fabric_Wall", 3.0f, "room", "Bed to hide under. Mirror. A door through to Suite 903.").D("light"),
        new Room("Suite 903", 3, 50, 14, 58, 34, "Wood_Parquet", "Fabric_Wall", 3.0f, "room", "TV (lure). Bathtub to hide in. The other way into the owner's rooms.").D("flooded"),

        // ================================================================= stage 3: the roof (4)
        new Room("Roof Deck", 4, 26, 14, 58, 38, "Rubble_Floor", "Concrete_Wall", 1.1f, "outdoor", "Gravel (loud) and puddles. Water tanks and air units to hide behind. The storm siren on its mast in the south-west corner: the big lure."),
        new Room("Lift Machine Room", 4, 42, 38, 52, 44, Conc, "Metal_Corrugated", 3.0f, "lair", "The lift motor over the shaft. It climbs out here."),
        new Room("Antenna Room", 4, 52, 38, 58, 44, Lino, "Paint_Office", 3.0f, "office", "THE GOAL: the master VCR, broadcasting you. The door bolts, but it breaks a bolt in four blows. Hold eject for 18 s; let go and the tape winds back."),
        new Room("Siren Housing", 4, 26, 38, 34, 44, Conc, "Metal_Corrugated", 3.0f, "service", "Tools, the siren's spare horn. A place to hide."),

        // ================================================================= your bedroom (2)
        // your house (the prologue): warm, lived in, everything in it can be used
        new Room("Bedroom", 2, 0, 0, 4.2f, 5.0f, "Wood_Home", "Paint_Home", 2.6f, "bedroom", "Prologue: you wake up here."),
        new Room("Living Room", 2, 4.2f, 0, 10.4f, 5.4f, "Wood_Home", "Paint_Home", 2.6f, "home", "The fireplace, the sofa, the TV and the VCR."),
        new Room("Kitchen", 2, 4.2f, 5.4f, 8.4f, 9.4f, "Tile_Home", "Paint_Home", 2.6f, "home", "Fridge, stove, toaster, kettle, microwave, the fruit bowl."),
        new Room("Hallway", 2, 8.4f, 5.4f, 10.4f, 9.4f, "Wood_Home", "Paint_Home", 2.6f, "home", "Coats, shoes, keys; the front door."),
    };

    // atrium void on the guest floor (no floor slab); railings run along the gallery edges
    public static readonly Rect[] Void = { Rect.MinMaxRect(22, 8, 38, 24) };

    public static readonly List<Opening> Openings = new List<Opening>
    {
        // ---------------- ground
        new Opening("Street Doors", 0, 28, 2, 32, 2, Gap.Static) { height = 3.0f },
        OZ("Vestibule Arch", 0, 8, 27, 33),
        DX("Door Luggage Lobby", 0, 20, 9.1f), DX("Door Luggage Passage", 0, 14, 6),
        DX("Door Front Office", 0, 20, 14), DX("Door Front Office Back", 0, 14, 12),
        DX("Door Concierge", 0, 20, 22), DX("Door Concierge Back", 0, 14, 24),
        DX("Door Lockers", 0, 12, 6), DX("Door Security", 0, 12, 14), DZ("Door Security Manager", 0, 18, 9),
        DX("Door Manager", 0, 12, 22), DX("Door Mail", 0, 12, 29), DX("Stair Door Ground", 0, 12, 34.65f),
        DX("Door Chapel Passage", 0, 14, 32), DZ("Door Chapel Lobby", 0, 26, 22.3f), DZ("Door Chapel North", 0, 38, 18),
        OZ("Restaurant Arch", 0, 26, 29, 31.5f), DZ("Door Restaurant North", 0, 38, 33),
        DX("Swing Door Kitchen", 0, 42, 34), DX("Door Dish Pit Restaurant", 0, 42, 28),
        DZ("Door Dish Pit Kitchen", 0, 30, 45), DZ("Door Cold Room", 0, 30, 51),
        DZ("Door Kitchen North", 0, 38, 50), DX("Door Kitchen East", 0, 54, 34),
        OZ("Passage North End", 0, 38, 12, 14), OX("North Corridor East End", 0, 54, 38, 40),
        DX("Door Gift Shop Lobby", 0, 40, 9.1f), DX("Door Gift Shop Hall", 0, 54, 6),
        OX("Bar Arch", 0, 40, 15, 21), DX("Door Bar Storage", 0, 50, 22), DX("Door Bar Telephones", 0, 50, 13),
        DX("Door Telephones Hall", 0, 54, 14), DX("Door Bar Storage Hall", 0, 54, 22),
        DZ("Door Chute Room", 0, 40, 15),
        new Opening("Basement Door", 0, 18.75f, 40, 19.65f, 40, Gap.Locked, "basement"),
        DZ("Door Lift Lobby", 0, 40, 44), new Opening("Lift Doors Ground", 0, 46.1f, 44, 47.9f, 44, Gap.Lift),
        DZ("Door Loading Dock", 0, 40, 51), new Opening("Roller Door", 0, 50, 44, 54, 44, Gap.Static) { height = 3.0f },
        // ---------------- guest floor
        OZ("Gallery NW", 1, 24, 20, 22), OZ("Gallery NE", 1, 24, 38, 40), OZ("Gallery SW", 1, 8, 20, 22), OZ("Gallery SE", 1, 8, 38, 40),
        RX("Rail West", 1, 22, 8, 24), RX("Rail East", 1, 38, 8, 24), RZ("Rail North", 1, 24, 22, 35), OZ("Stair Top", 1, 24, 35, 37.5f),
        RZ("Rail North E", 1, 24, 37.5f, 38), RZ("Rail Mezzanine", 1, 8, 22, 38),
        OX("West Cross Hall W", 1, 14, 24, 26), OX("West Cross Hall E", 1, 20, 24, 26),
        B(DX("Door 201", 1, 12, 5)), B(DX("Door 203", 1, 12, 11)), DZ("Door 201-203", 1, 8, 8), B(DX("Door 205", 1, 12, 17)),
        DX("Door 207", 1, 12, 23), new Opening("Door 209", 1, 12, 28.55f, 12, 29.45f, Gap.Jammed), OZ("Hole 207-209", 1, 26, 6, 7.4f),
        B(DX("Door 202", 1, 14, 5)), B(DX("Door 204", 1, 14, 11)), B(DX("Door 206", 1, 14, 17)), B(DX("Door 208", 1, 14, 22)),
        DX("Door Housekeeping", 1, 14, 29), DX("Door Ice", 1, 14, 35),
        OZ("Ballroom Doors", 1, 26, 29, 31.5f), DZ("Door Ballroom North", 1, 38, 34), DX("Door Ballroom Staff", 1, 20, 30),
        OZ("West Corridor North End", 1, 38, 12, 14), OZ("East Corridor North End", 1, 38, 46, 48), DX("Stair Door Upper", 1, 12, 32.8f),
        DZ("Door Linen Chute", 1, 40, 15), DZ("Door Lift Vestibule", 1, 40, 45), new Opening("Lift Doors Upper", 1, 46.1f, 44, 47.9f, 44, Gap.Lift),
        OX("East Cross Hall W", 1, 40, 24, 26), OX("East Cross Hall E", 1, 46, 24, 26),
        B(DX("Door 212", 1, 46, 5)), new Opening("Door 214", 1, 46, 10.55f, 46, 11.45f, Gap.Door) { stuck = true, bolt = true }, B(DX("Door 216", 1, 46, 17)), B(DX("Door 218", 1, 46, 22)),
        DZ("Door 212-214", 1, 8, 41.3f), DZ("Door 214-216", 1, 14, 41.3f),
        B(DX("Door 211", 1, 48, 5)), B(DX("Door 213", 1, 48, 11)), B(DX("Door 215", 1, 48, 17)), B(DX("Door 217", 1, 48, 23)), DZ("Door 213-215", 1, 14, 53),
        DX("Door Linen Store", 1, 46, 29), DX("Door Maid's Office", 1, 46, 35), DX("Door Staff Lounge", 1, 48, 29), B(DX("Door 219", 1, 48, 35)),
        // ---------------- basement
        DZ("Door Basement Stair", -1, 34, 28.5f),
        DZ("Door Laundry", -1, 34, 11), DZ("Door Boiler", -1, 34, 38), B(DX("Door Boiler Pump", -1, 46, 39)), B(DZ("Door Pump", -1, 34, 51)),
        new Opening("Lift Pit Doors", -1, 46.1f, 44, 47.9f, 44, Gap.Lift),
        DZ("Door Electrical", -1, 30, 9), DZ("Door Electrical Cellar", -1, 22, 8),
        new Opening("Door Recorder Room", -1, 17.55f, 30, 18.45f, 30, Gap.Door) { bolt = true },
        DZ("Door Workshop", -1, 30, 27), DZ("Door Workshop Flooded", -1, 22, 26),
        DZ("Door Cages West", -1, 30, 36), DZ("Door Cages East", -1, 30, 44), DZ("Door Cages Archive", -1, 14, 40),
        DZ("Door Coal Store", -1, 30, 52), DX("Door Coal Cages", -1, 48, 22), DZ("Door Coal Generator", -1, 14, 52),
        DZ("Door Wine Cellar", -1, 6, 11), DX("Door Cellar Flooded", -1, 18, 14), DZ("Door Flooded South", -1, 6, 25),
        DZ("Door Archive", -1, 6, 40), DZ("Door Generator", -1, 6, 52),
        // ---------------- penthouse
        new Opening("Lift Doors PH", 3, 46.1f, 44, 47.9f, 44, Gap.Lift),
        OZ("PH Lobby Arch", 3, 38, 44, 50),
        DZ("Door Pantry", 3, 38, 30),
        new Opening("Roof Access Door", 3, 37.55f, 38, 38.45f, 38, Gap.Locked, "roofcard"),
        DZ("Door Plant Room", 3, 38, 55),
        B(DZ("Door 901", 3, 34, 30)),
        new Opening("Door Owner's Suite", 3, 41.55f, 34, 42.45f, 34, Gap.Jammed),
        B(DZ("Door 903", 3, 34, 54)),
        DX("Door 901-Passage", 3, 33, 29.4f), DX("Door Passage-Suite", 3, 34, 27), DX("Door Passage-Study", 3, 34, 18.6f), DZ("Door Passage-Corridor", 3, 34, 33.5f, 0.8f),
        DZ("Door Suite-Study", 3, 22, 38), DZ("Door Suite-Bedroom", 3, 22, 46),
        DX("Door Study-Bedroom", 3, 42, 18), DX("Door Bedroom-903", 3, 50, 18),
        // ---------------- roof
        DZ("Door Roof Stair", 4, 38, 38), DZ("Door Machine Room", 4, 38, 47), B(DZ("Door Antenna Room", 4, 38, 55)), DZ("Door Siren Housing", 4, 38, 30),
        // ---------------- windows on the outside walls (boarded up; the storm flashes between the planks)
        W1("Window 201", 1, 4, 6.6f), W1("Window 203", 1, 4, 12.6f), W1("Window 205", 1, 4, 18.6f), W1("Window 207", 1, 4, 24.6f), W1("Window 209", 1, 4, 30.6f),
        W1("Window 211", 1, 56, 6.6f), W1("Window 213", 1, 56, 12.6f), W1("Window 215", 1, 56, 18.6f), W1("Window 217", 1, 56, 24.6f), W1("Window Lounge", 1, 56, 30.6f), W1("Window 219", 1, 56, 36.6f),
        WZ("Window 202", 1, 2, 17f), WZ("Window 212", 1, 2, 43f), WZ("Window Mezzanine W", 1, 2, 24f, 2.0f), WZ("Window Mezzanine C", 1, 2, 30f, 2.0f), WZ("Window Mezzanine E", 1, 2, 36f, 2.0f),
        W1("Window Lockers", 0, 6, 9.0f), W1("Window Mail", 0, 6, 30.4f), WZ("Window Gift Shop", 0, 2, 51f),
        W1("Window Hall S", 0, 56, 8f), W1("Window Hall C", 0, 56, 20f), W1("Window Hall N", 0, 56, 31f),
        W1("Window 901", 3, 26, 24f), W1("Window 903", 3, 58, 24f), WZ("Window Study", 3, 14, 38f), WZ("Window Owner's Bedroom", 3, 14, 46f),
        // ---------------- bedroom
        DX("Bedroom Door", 2, 4.2f, 2.45f),
        new Opening("Bedroom Window", 2, 1.2f, 5.0f, 3.0f, 5.0f, Gap.Window),
        OZ("Kitchen Arch", 2, 5.4f, 5.2f, 7.2f), OZ("Hall Arch", 2, 5.4f, 8.7f, 10.1f),
        new Opening("Front Door", 2, 8.9f, 9.4f, 9.9f, 9.4f, Gap.Locked, "front_door"),
        WZ("Living Window", 2, 0f, 7.3f, 1.8f), W1("Kitchen Window", 2, 4.2f, 7.4f, 1.4f),

    };

    public struct Stair { public Vector3 bottom, top; public float width; public string mat; }
    public static readonly Stair[] Stairs =
    {
        new Stair { bottom = new Vector3(36.25f, 0f, 12f), top = new Vector3(36.25f, Floor2, 24f), width = 2.5f, mat = "Carpet_Stair" },
        new Stair { bottom = new Vector3(7.5f, 0f, 33.2f), top = new Vector3(7.5f, 2.0f, 37.0f), width = 2.6f, mat = "Concrete_Stair" },
        new Stair { bottom = new Vector3(10.5f, 2.0f, 37.0f), top = new Vector3(10.5f, Floor2, 33.6f), width = 2.6f, mat = "Concrete_Stair" },
        new Stair { bottom = new Vector3(27.8f, Basement, 42.2f), top = new Vector3(20.0f, 0f, 42.2f), width = 2.4f, mat = "Concrete_Stair" },
        // the foot is 2 m clear of the west wall: at 1 m the side boards left a gap too tight to walk round to it
        new Stair { bottom = new Vector3(36.0f, Penthouse, 41.2f), top = new Vector3(41.0f, RoofTop, 41.2f), width = 2.4f, mat = "Concrete_Stair" },
    };

    // extra floor slabs (landings) and level railings that are not room edges
    public static readonly (Rect r, float y, string mat)[] Slabs =
    {
        (Rect.MinMaxRect(6, 37, 12, 40), 2.0f, "Concrete_Stair"),        // staff stair mid landing
        (Rect.MinMaxRect(9, 32, 12, 33.6f), Floor2, "Concrete_Stair"),   // staff stair top landing
        (Rect.MinMaxRect(18, 40, 20.0f, 44), 0f, "Concrete_Stair"),      // basement stairhead landing
        (Rect.MinMaxRect(20.0f, 40, 30, 41.0f), 0f, "Concrete_Stair"),   // walkway past the stairwell
        (Rect.MinMaxRect(41.0f, 38, 42, 44), RoofTop, "Concrete_Stair"),     // roof stair: top landing
        (Rect.MinMaxRect(35.6f, 38, 41.0f, 40.0f), RoofTop, "Concrete_Stair"), // roof stair: walkway to the roof door
    };
    public static readonly (Vector3 a, Vector3 b)[] Rails =
    {
        (new Vector3(9.0f, Floor2, 32f), new Vector3(9.0f, Floor2, 33.6f)),
        (new Vector3(20.0f, 0f, 41.0f), new Vector3(30f, 0f, 41.0f)),
        (new Vector3(20.0f, 0f, 43.4f), new Vector3(20.0f, 0f, 44f)),      // the gap beside the basement stair, at the landing
        (new Vector3(35.6f, RoofTop, 40.0f), new Vector3(41.0f, RoofTop, 40.0f)),   // roof stair walkway, over the stairwell
    };

    // open stairwells between rooms on different floors (the room graph links them for sound and for the map)
    public static readonly (string a, string b, Vector3 pos, float width)[] VerticalPortals =
    {
        ("Basement Stairhead", "Basement Stair", new Vector3(24f, -1.6f, 42.2f), 2.4f),
    };

    // ------------------------------------------------------------------ points
    public static Vector3 W(float x, float z, int floor = 0, float up = 0f)
    {
        if (floor == 2) return BedroomOrigin + new Vector3(x, up, z);
        return new Vector3(x, FloorY(floor) + up, z);
    }

    public static readonly Vector3 ReceptionSpawn = W(16.0f, 14.0f);        // the front office, behind reception
    public const float ReceptionYaw = 90f;
    public static readonly Vector3 GuestCheckpoint = W(11.0f, 33.0f, 1);     // top of the staff stair
    public static readonly Vector3 BasementCheckpoint = W(28.5f, 36.0f, -1); // bottom of the basement stair
    public static readonly Vector3 BedroomSpawn = W(1.2f, 1.2f, 2);
    // the house: lying in bed (your eye on the pillow), standing up beside it, the sofa facing the TV, the doorstep
    public static readonly Vector3 BedEye = W(0.85f, 4.35f, 2, 0.78f);
    public static readonly Vector3 BedGetUp = W(2.0f, 3.4f, 2);
    public const float BedGetUpYaw = 90f;
    // the tape: someone left a camcorder running on a tripod in the hotel lobby; it walks up to the lens
    public static readonly Vector3 TapeCam = W(38.6f, 9.0f, 0, 1.65f);
    public static readonly Vector3 TapeCamLook = W(30f, 17f, 0, 1.3f);
    public static readonly Vector3 TapeFigureStart = W(32.6f, 14.6f);         // under the chandelier, facing the lens
    public static readonly Vector3 TapeFigureStop = W(38.27f, 9.31f);         // right in front of the lens (0.45 m)
    public static readonly Vector3 LivingTvView = W(6.1f, 3.0f, 2);       // in front of the sofa, the TV ahead
    public static readonly Vector3 ParcelSpot = W(9.35f, 8.95f, 2, 0.02f);           // on the doormat, inside the front door
    public static readonly Vector3 Letterbox = W(9.4f, 9.33f, 2, 0.95f);            // the flap in the front door
    public static readonly Vector3 FrontDoorInside = W(9.4f, 8.6f, 2);               // standing at the front door
    public static readonly Vector3 FrontDoorOutside = W(9.4f, 9.8f, 2, 1.1f);
    public static readonly Vector3 FrontDoorKnock = W(9.4f, 9.3f, 2, 1.25f);        // the inside face of the front door
    public static readonly Vector3 DoorbellChime = W(10.3f, 8.2f, 2, 2.1f);         // the chime box on the hall wall
    public static readonly Vector3 MonsterStart = W(30f, 32f, 1);           // the ballroom
    // where each stage starts, and where it starts
    public static readonly Vector3 Stage2Spawn = W(28.5f, 36.0f, -1);        // the bottom of the basement stair
    public const float Stage2Yaw = 180f;
    public static readonly Vector3 Stage3Spawn = W(47.0f, 41.4f, 3);         // out of the lift, the penthouse
    public const float Stage3Yaw = 180f;
    public static readonly Vector3 Stage3Checkpoint = W(36.4f, 39.2f, 3);    // inside the roof door (after the keycard)
    public static readonly Vector3 MonsterStart2 = W(52f, 22f, -1);          // the coal store, where it rests
    public static readonly Vector3 MonsterStart3 = W(47f, 41f, 4);           // the lift machine room
    // the things each stage turns on
    public static readonly Vector3 SafeSpot = W(6.12f, 23.0f, 0, 1.25f);     // manager's office, west wall
    public static readonly Vector3 CodeSpotB = W(9.2f, 29.4f, 1, 0.33f);     // the previous viewer's bag in 209
    public static readonly Vector3 FuseSpot = W(46.6f, 16.0f, -1, 1.02f);    // storage cages, the end cage
    public static readonly Vector3 LiftKeySpot = W(17.4f, 23.2f, -1, 0.79f); // recorder room desk
    public static readonly Vector3 LiftPanelSpot = W(48.75f, 43.86f, -1, 1.3f); // beside the lift doors, pump room
    public static readonly Vector3 LiftCarSpot = W(47.0f, 45.2f, -1);        // inside the lift car, behind the doors
    public static readonly Vector3 KeycardSpot = W(38.6f, 15.6f, 3, 0.79f);  // owner's study desk
    public static readonly Vector3 AntennaVcrSpot = W(57.3f, 41.0f, 4, 1.3f);// antenna room, on the rack
    public static readonly Vector3 SirenSpot = W(27.6f, 15.6f, 4, 0.9f);     // the storm siren, south-west corner
    public static readonly Vector3 KeySpot = W(44.0f, 13.93f, 1, 0.62f);
    public static readonly Vector3 VcrSpot = W(14.6f, 26.0f, -1, 1.3f);
    public static readonly Vector3 BreakerSpot = W(4.15f, 26.0f, -1, 1.4f);
    // where it goes when it leaves a floor (the lift shaft) and comes back
    public static Vector3 Lair(int floor) => W(47f, 42.6f, floor);

    public static readonly Dictionary<int, Vector3[]> Patrol = new Dictionary<int, Vector3[]>
    {
        { 1, new[] { W(30, 32, 1), W(30, 25, 1), W(21, 16, 1), W(13, 20, 1), W(13, 36, 1), W(30, 39, 1), W(47, 30, 1), W(47, 10, 1), W(39, 16, 1), W(30, 5, 1), W(21, 5, 1), W(30, 25, 1) } },
        { 0, new[] { W(30, 17, 0), W(45, 18, 0), W(55, 20, 0), W(48, 31.6f, 0), W(33, 32, 0), W(30, 39, 0), W(13, 30, 0), W(13, 8, 0), W(30, 5, 0), W(30, 17, 0) } },
        { -1, new[] { W(28, 32, -1), W(11, 38, -1), W(9, 26, -1), W(11, 10.6f, -1), W(25, 4, -1), W(40, 10, -1), W(40, 22, -1), W(52, 22, -1), W(33.5f, 38.5f, -1), W(28, 32, -1) } },
        { 3, new[] { W(30, 36, 3), W(47, 36, 3), W(55, 36, 3), W(54, 24, 3), W(46, 18, 3), W(42, 28, 3), W(30, 24, 3), W(30, 36, 3) } },
        { 4, new[] { W(47, 41, 4), W(47, 35, 4), W(31, 33, 4), W(31, 19, 4), W(45, 17, 4), W(55, 24, 4), W(55, 35, 4), W(40, 35, 4) } },
    };

    public static Room RoomAt(Vector3 p)
    {
        int floor = FloorOf(p);
        if (floor == 2)
        {
            var lq = new Vector2(p.x - BedroomOrigin.x, p.z - BedroomOrigin.z);
            foreach (var r in Rooms) if (r.floor == 2 && r.r.Contains(lq)) return r;
            return null;
        }
        var q = new Vector2(p.x, p.z);
        foreach (var r in Rooms) if (r.floor == floor && r.r.Contains(q)) return r;
        foreach (var r in Rooms) if (r.twoStorey && r.floor == floor - 1 && r.r.Contains(q)) return r;
        return null;
    }

    public static Room Find(string name) => Rooms.Find(r => r.name == name);
}
