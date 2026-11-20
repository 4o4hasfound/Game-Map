using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

// Screenshots of key views for review:  TheTape.exe -capture <folder>
public class CaptureTour : MonoBehaviour
{
    static string Arg(string name)
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Arg("-capture") == null || FindAnyObjectByType<CaptureTour>()) return;
        new GameObject("CaptureTour").AddComponent<CaptureTour>();
    }

    string dir;
    static Vector3 W(float x, float z, int f = 0, float up = 0f) => LevelLayout.W(x, z, f, up);

    IEnumerator Start()
    {
        // relative to where the game was started (ScreenCapture would resolve it against the data folder and fail)
        dir = Path.GetFullPath(Arg("-capture"));
        Directory.CreateDirectory(dir);
        var g = Game.Instance;
        yield return new WaitForSeconds(1.5f);
        yield return Shot("00_title");
        Breath.Override = false;
        g.BeginPrologue();
        yield return new WaitForSeconds(1.2f);
        yield return Shot("01_wake_up_in_bed");
        // getting up: the pointer, then up
        if (DoorEase.Instance) { DoorEase.NoDrift = true; DoorEase.Instance.BeginFree("getting up"); for (int i = 0; i < 20; i++) { DoorEase.Instance.TickFree(Time.deltaTime, 0f); yield return null; } }
        yield return Shot("01a_getting_up");
        if (DoorEase.Instance) { DoorEase.Instance.End(); DoorEase.NoDrift = false; }
        g.GotUp();
        Game.FastPrologue = true;
        // the house
        var O = LevelLayout.BedroomOrigin;
        Look(O + new Vector3(3.6f, 0, 0.6f), O + new Vector3(0.9f, 0.8f, 4.0f));
        yield return new WaitForSeconds(1.6f);
        yield return Shot("01b_bedroom");
        Look(O + new Vector3(8.6f, 0, 4.6f), O + new Vector3(5.6f, 0.7f, 1.2f));
        yield return new WaitForSeconds(1.6f);
        yield return Shot("01c_living_room_tv_sofa");
        Look(O + new Vector3(6.2f, 0, 2.8f), O + new Vector3(10.3f, 0.9f, 2.6f));
        yield return new WaitForSeconds(1.6f);
        yield return Shot("01d_fireplace");
        Look(O + new Vector3(6.0f, 0, 5.9f), O + new Vector3(6.3f, 0.8f, 9.2f));
        yield return new WaitForSeconds(1.6f);
        yield return Shot("01e_kitchen");
        var fridgeF = FindObjectsByType<Fiddle>(FindObjectsSortMode.None).FirstOrDefault(f => f.swing && f.swing.name == "Fridge hinge");
        if (fridgeF) fridgeF.Tap();
        Look(O + new Vector3(7.6f, 0, 7.4f), O + new Vector3(8.0f, 1.0f, 9.0f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("01f_fridge_open");
        if (fridgeF) fridgeF.Tap();
        Look(O + new Vector3(5.2f, 0, 6.2f), O + new Vector3(6.4f, 0.8f, 7.2f));
        yield return new WaitForSeconds(0.6f);
        yield return Shot("01g_kitchen_table_fruit");
        // close-ups for placement: the books on their shelf, the dresser top, the hall's coats
        Look(O + new Vector3(7.95f, 0, 3.7f), O + new Vector3(7.95f, 1.0f, 5.2f));
        yield return new WaitForSeconds(0.6f);
        yield return Shot("01g2_bookshelf");
        Look(O + new Vector3(2.4f, 0, 1.4f), O + new Vector3(3.9f, 0.9f, 1.35f));
        yield return new WaitForSeconds(0.6f);
        yield return Shot("01g3_bedroom_dresser");
        Look(O + new Vector3(9.0f, 0, 5.9f), O + new Vector3(10.2f, 1.1f, 7.4f));
        yield return new WaitForSeconds(0.6f);
        yield return Shot("01g4_hall_coats");
        // the interactions: the kettle steaming, the toast popping, a photo held up and looked at
        var fids = FindObjectsByType<Fiddle>(FindObjectsSortMode.None);
        var kettleF = fids.FirstOrDefault(f => f.whileRunning && f.whileRunning.name == "Steam");
        var toastF = fids.FirstOrDefault(f => f.popUp != null && f.popUp.Length > 0);
        Look(O + new Vector3(5.1f, 0, 8.2f), O + new Vector3(5.1f, 1.05f, 9.1f));
        yield return new WaitForSeconds(0.4f);
        if (kettleF) kettleF.Tap();
        if (toastF) toastF.Tap();
        yield return new WaitForSeconds(3.9f);
        yield return Shot("01g5_kettle_steam_toast");
        var photoF = fids.FirstOrDefault(f => f.name == "Photo on the mantel");
        Look(O + new Vector3(9.2f, 0, 3.3f), O + new Vector3(10.1f, 1.2f, 3.3f));
        yield return new WaitForSeconds(0.5f);
        if (photoF) { photoF.Tap(); yield return new WaitForSeconds(1.0f); }
        yield return Shot("01g6_inspect_photo");
        yield return new WaitForSeconds(1.6f);
        // the doorbell, the parcel on the doorstep
        float tb = Time.time;
        while (g.PrologueBeat != Game.Beat.Doorbell && Time.time - tb < 10f) yield return null;
        yield return new WaitForSeconds(1.5f);                     // the envelope drops through the letterbox
        Look(O + new Vector3(9.4f, 0, 7.4f), O + new Vector3(9.4f, 0.3f, 9.2f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("01h_front_door_parcel");
        var env = FindAnyObjectByType<TapeItem>();
        if (env) { env.Tap(); yield return new WaitForSeconds(0.4f); yield return Shot("01i_parcel_note"); g.ClosePaper(); } else BedroomVcr.HasTape = true;
        FindAnyObjectByType<BedroomVcr>().Tap();
        Look(LevelLayout.LivingTvView, g.bedroomTv.transform.position);
        yield return new WaitForSeconds(1.8f);
        yield return Shot("02a_tv_tracking");
        Look(LevelLayout.W(5.5f, 2.0f, 2), g.bedroomTv.transform.position);        // closer: what's on the tape
        yield return new WaitForSeconds(3.5f);
        yield return Shot("02_tv_lobby_camera");
        float tw = Time.time;
        while (!g.TapeReachedLens && !g.PullingIn && Time.time - tw < 20f) yield return null;
        yield return Shot("02c_it_at_the_lens");
        while (!g.PullingIn && Time.time - tw < 20f) yield return null;
        yield return new WaitForSeconds(1.9f);
        yield return Shot("02b_pulled_in");
        while (g.State != Game.Phase.Hotel && Time.time - tw < 30f) yield return null;
        Game.FastPrologue = false;
        g.EnterHotel();
        var it = g.It;
        it.SendAway(999f, 1);
        yield return null;
        g.player.Teleport(LevelLayout.ReceptionSpawn, LevelLayout.ReceptionYaw, -12f);
        yield return new WaitForSeconds(2.5f);
        yield return Shot("03_front_office_letter");
        Look(W(16.8f, 15.6f), W(16.8f, 17.7f, 0, 1.1f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("04_switchboard");
        Look(W(21f, 14f), W(32f, 18f, 0, 2.5f));
        yield return new WaitForSeconds(1.5f);
        yield return Shot("05_lobby_from_reception");
        Look(W(36.4f, 11.8f), LevelLayout.TapeCam + Vector3.down * 0.5f);
        yield return new WaitForSeconds(1.0f);
        yield return Shot("05b_lobby_camcorder");
        Look(W(26f, 14f), W(33f, 25.6f, 1, 1.2f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("06_lobby_up_to_gallery_phone");
        Look(W(8.5f, 16f), W(6.5f, 12.8f, 0, 1.7f));
        yield return new WaitForSeconds(1.5f);
        yield return Shot("07_security_monitors");
        Look(W(13f, 6f), W(13f, 30f, 0, 1.4f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("08_west_staff_passage");
        Look(W(28f, 27f), W(34f, 32f, 0, 0.6f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("09_restaurant_chandelier");
        Look(W(19f, 27f), W(19f, 37f, 0, 1.2f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("10_chapel_organ");
        Look(W(43f, 18f), W(49f, 22f, 0, 1.2f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("11_bar");
        // the spots fixed after the reachability check: the telephone room (both doors clear), the cold room, the safe
        Look(W(48.4f, 11.6f), W(50f, 13.0f, 0, 1.0f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("11b_bar_door_to_telephones");
        Look(W(50.8f, 10.6f), W(53.6f, 13.6f, 0, 1.1f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("11c_telephone_room");
        Look(W(51f, 29.6f), W(51f, 27f, 0, 1.0f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("11d_cold_room_rack");
        g.GiveKey("code_a"); g.GiveKey("code_b");
        var safe = FindAnyObjectByType<Safe>();
        if (safe) safe.Tap();
        Look(W(7.3f, 22.7f), LevelLayout.SafeSpot + new Vector3(0.17f, -0.2f, 0f));
        yield return new WaitForSeconds(0.8f);
        yield return Shot("11e_safe_open_key");
        Look(W(23f, 39f), W(19.2f, 39.9f, 0, 1.4f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("12_basement_door");
        Look(W(13f, 4f, 1), W(13f, 34f, 1, 1.0f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("13_guest_west_corridor");
        Look(W(13.0f, 32.6f, 1), W(14.0f, 35.0f, 1, 1.0f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("13b_ice_room_door");
        // the interface: easing a door (the pointer and its band), crouching (the stamina bar), the map's fire plans
        var d201 = HingeDoor.All.Find(x => x && x.portalName == "Door 201");
        if (d201 && DoorEase.Instance)
        {
            d201.SetOpen(0f);
            Look(W(13.2f, 5.0f, 1), d201.HandlePos);
            DoorEase.NoDrift = true;
            DoorEase.Instance.Begin(d201);
            for (int i = 0; i < 40; i++) { DoorEase.Instance.Tick(Time.deltaTime, i < 20 ? 0.6f : 0f); yield return null; }
            yield return Shot("13c_hud_door_ease");
            DoorEase.Instance.End(); d201.Release(); DoorEase.NoDrift = false;
        }
        Look(W(13f, 8f, 1), W(13f, 20f, 1, 1.2f));
        PlayerMotor.BotDriven = true; PlayerMotor.BotCrouch = true;
        yield return new WaitForSeconds(0.3f);
        PlayerMotor.BotMove = new Vector2(0f, 1f);
        yield return new WaitForSeconds(1.5f);
        PlayerMotor.BotMove = Vector2.zero;
        yield return Shot("13d_hud_crouch_stamina");
        PlayerMotor.BotDriven = false; PlayerMotor.BotCrouch = false;
        if (MapView.Instance)
        {
            MapView.Instance.SetOpen(true);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("13e_map_fire_plans");
            MapView.Instance.SetOpen(false);
        }
        Look(W(43f, 10.5f, 1), W(44f, 13.9f, 1, 1.0f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("14_room214_dresser_mirror");
        it.Place(W(31f, 31f, 1), 200f, Listener.State.Search);
        it.Listen(999f);
        Look(W(30f, 25.5f, 1), W(31f, 31f, 1, 1.4f));
        yield return new WaitForSeconds(1.5f);
        yield return Shot("15_ballroom_it");
        it.Place(W(47f, 18f, 1), 0f, Listener.State.Search);
        it.Listen(999f);
        Look(W(47f, 12f, 1), W(47f, 18f, 1, 1.5f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("16_east_corridor_it_close");
        it.SendAway(999f, 1);
        Look(W(17f, 27.5f, 1), W(17f, 31.9f, 1, 1.6f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("17_housekeeping_photos");
        Look(W(28.5f, 36f, -1), W(10f, 32f, -1, 1.2f));
        yield return new WaitForSeconds(2.0f);
        yield return Shot("18_basement_dark");
        Look(W(7f, 26f, -1), W(4.2f, 26f, -1, 1.4f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("19_breaker");
        Breaker.Instance.Throw();
        yield return new WaitForSeconds(0.6f);
        Look(W(40f, 32f, -1), W(8f, 32f, -1, 1.2f));
        yield return new WaitForSeconds(0.4f);
        yield return Shot("20_basement_power_struggling");
        yield return new WaitForSeconds(3.5f);
        yield return Shot("21_basement_power_on");
        Look(W(19f, 27f, -1), W(14.6f, 26f, -1, 1.3f));
        yield return new WaitForSeconds(1.5f);
        yield return Shot("22_recorder_room");
        Look(W(10f, 20f, -1), W(10f, 10f, -1, 1f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("23_wine_cellar");
        Look(W(25f, 20f, -1), W(25f, 9f, -1, 0.5f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("24_flooded_passage");
        Look(W(50.5f, 39.5f, -1), LevelLayout.LiftPanelSpot);
        yield return new WaitForSeconds(1.0f);
        yield return Shot("24b_pump_room_lift_panel");
        // part three: the penthouse and the roof
        Look(W(47f, 41.2f, 3), W(47f, 34f, 3, 1.4f));
        yield return new WaitForSeconds(2.0f);
        yield return Shot("29_penthouse_lift_lobby");
        Look(W(36f, 24f, 3), W(46f, 31f, 3, 1.2f));
        yield return new WaitForSeconds(1.2f);
        yield return Shot("30_owners_suite");
        Look(W(38.6f, 19.6f, 3), LevelLayout.KeycardSpot);
        yield return new WaitForSeconds(1.2f);
        yield return Shot("31_owners_study_keycard");
        Look(W(41f, 35f, 4), W(30f, 20f, 4, 1.5f));
        yield return new WaitForSeconds(1.5f);
        yield return Shot("32_roof_deck_dark");
        Lightning.Instance.Hold(0.6f);
        yield return new WaitForSeconds(0.15f);
        yield return Shot("33_roof_lightning");
        yield return new WaitForSeconds(0.8f);
        Look(W(31f, 19f, 4), W(27.3f, 15.3f, 4, 2.0f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("34_pa_horn");
        Look(W(53.6f, 40.6f, 4), LevelLayout.AntennaVcrSpot);
        yield return new WaitForSeconds(1.2f);
        yield return Shot("35_antenna_room_master_vcr");
        g.player.Teleport(W(8.4f, 7.0f, 1), 0f, 0f);
        yield return null; yield return null;                     // the hotel stage switches back on
        var ward = FindObjectsByType<HideSpot>(FindObjectsSortMode.None).First(h => h.transform.parent && h.transform.parent.name == "Wardrobe 201");
        ward.Tap();
        it.Place(W(8.5f, 5.2f, 1), 0f, Listener.State.Search);
        it.Listen(999f);
        yield return new WaitForSeconds(1.2f);
        yield return Shot("25_hiding_wardrobe");
        HideSpot.ForceLeave();
        it.SendAway(999f, 1);
        // the storm through a boarded window (Room 201)
        Look(W(9.5f, 6.6f, 1), W(4.2f, 6.6f, 1, 1.4f));
        yield return new WaitForSeconds(1.0f);
        yield return Shot("26a_room201_before_flash");
        Lightning.Instance.Hold(0.6f);
        yield return new WaitForSeconds(0.15f);
        yield return Shot("26_lightning_room201");
        // a blackout in the west staff passage: only the battery lights
        Look(W(13f, 6f), W(13f, 30f, 0, 1.4f));
        yield return new WaitForSeconds(1.0f);
        PowerGrid.Instance.Force(0, true, 4f);
        yield return new WaitForSeconds(1.5f);
        yield return Shot("27_blackout_passage");
        yield return new WaitForSeconds(3f);
        // a live cable arcing
        if (SparkingWire.All.Count > 0)
        {
            var w = SparkingWire.All[0];
            var wp = w.transform.position;
            var floorY = LevelLayout.FloorY(LevelLayout.FloorOf(wp + Vector3.up * 0.3f));
            Look(new Vector3(wp.x + 1.8f, floorY, wp.z + 0.6f), wp);
            yield return new WaitForSeconds(0.8f);
            w.Burst();
            yield return new WaitForSeconds(0.15f);
            yield return Shot("28_live_cable");
        }
        // the menus: pause, then settings (time stops while paused: real-time waits)
        Game.Instance.SetPaused(true);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Shot("40_pause_menu");
        var settings = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
            .FirstOrDefault(b => b.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text == "Settings"));
        if (settings) { settings.onClick.Invoke(); yield return new WaitForSecondsRealtime(0.4f); yield return Shot("41_settings"); }
        Game.Instance.SetPaused(false);
        Application.Quit();
    }

    void Look(Vector3 from, Vector3 at)
    {
        // the tour keeps the camera light charged and on, so every shot shows the room as a player with the light sees it
        if (CameraLight.Instance) { CameraLight.Instance.Refill(1f); CameraLight.Instance.SetOn(true); }
        var d = at - (from + Vector3.up * 1.6f);
        Game.Instance.player.Teleport(from, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null;
        yield return null;
    }
}
