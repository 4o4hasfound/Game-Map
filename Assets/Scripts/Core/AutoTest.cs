using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// Automated checks, run in the built game:  TheTape.exe -autotest [-autotestOut path]
// They drive the game through its own code (teleports, emitted noises, simulated holds) and write PASS/FAIL lines.
// They are not a playtest.
public class AutoTest : MonoBehaviour
{
    public static bool Requested => System.Environment.GetCommandLineArgs().Contains("-autotest");
    static readonly List<string> lines = new List<string>();
    static int pass, fail;
    string outPath;
    static readonly List<NoiseBus.Noise> heard = new List<NoiseBus.Noise>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Requested || FindAnyObjectByType<AutoTest>()) return;
        var go = new GameObject("AutoTest");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoTest>();
    }

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-autotestOut");
        outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.dataPath, "../autotest.txt");
        NoiseBus.Emitted += n => { heard.Add(n); if (heard.Count > 500) heard.RemoveAt(0); };
        PowerGrid.Quiet = true;              // random brownouts would land on the light checks (the forced one still runs)
        StartCoroutine(Run());
    }

    // closed before the end (the window shut, the game quit): what ran so far is still written, marked INTERRUPTED
    static bool finished;
    void OnApplicationQuit()
    {
        if (finished || lines.Count == 0 || string.IsNullOrEmpty(outPath)) return;
        var l = new List<string>(lines) { $"RESULT  INTERRUPTED   ({pass} PASS, {fail} FAIL so far): the game was closed before the tests finished" };
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))); File.WriteAllLines(outPath, l); } catch { }
    }

    static void Check(bool ok, string what)
    {
        lines.Add((ok ? "PASS  " : "FAIL  ") + what);
        if (ok) pass++; else fail++;
        Debug.Log((ok ? "PASS " : "FAIL ") + what);
    }
    static void Info(string s) { lines.Add("      " + s); Debug.Log(s); }
    static void Dump(string why)
    {
        if (!It) return;
        Info($"{why}: it is at {It.transform.position} ({LevelLayout.RoomAt(It.transform.position)?.name}), state {It.state}, target {It.Target}, path {It.agent.pathStatus}, remaining {It.agent.remainingDistance:0.0}, onMesh {It.agent.isOnNavMesh}");
        foreach (var l in It.Log.Skip(Mathf.Max(0, It.Log.Count - 12))) Info("   it: " + l);
    }

    static Game G => Game.Instance;
    static PlayerMotor P => G.player;
    static Listener It => G.It;
    static Vector3 W(float x, float z, int f = 0, float up = 0f) => LevelLayout.W(x, z, f, up);
    static HingeDoor Door(string n) => HingeDoor.All.First(d => d.portalName == n);

    void Look(Vector3 from, Vector3 at)
    {
        var d = at - (from + Vector3.up * 1.6f);
        P.Teleport(from, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
    }

    IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    IEnumerator Run()
    {
        yield return Frames(10);
        lines.Add("THE TAPE automated checks  " + System.DateTime.Now);
        lines.Add($"GPU {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}");
        // -finaleRepeat N: only the finale, N times on fresh scenes (to catch an intermittent failure)
        var cl = System.Environment.GetCommandLineArgs();
        int fr = System.Array.IndexOf(cl, "-finaleRepeat");
        if (fr >= 0 && fr + 1 < cl.Length && int.TryParse(cl[fr + 1], out int reps))
        {
            repeatMode = true;
            for (int r = 0; r < reps; r++)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
                yield return Frames(30);
                Info($"--- finale run {r + 1} of {reps}");
                yield return Part2();
            }
            finished = true;
        lines.Add($"RESULT  {(fail == 0 ? "PASS" : "FAIL")}   ({pass} PASS, {fail} FAIL)");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllLines(outPath, lines);
            Application.Quit();
            yield break;
        }
        yield return Part1();
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        yield return Frames(30);
        yield return Part2();
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        yield return Frames(30);
        yield return Part3();
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        yield return Frames(30);
        yield return Senses();
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        yield return Frames(30);
        yield return SkipRun();
        finished = true;
        lines.Add($"RESULT  {(fail == 0 ? "PASS" : "FAIL")}   ({pass} PASS, {fail} FAIL)");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllLines(outPath, lines);
        Application.Quit();
    }

    static bool repeatMode;
    static void Calm() { It.HoldUntil = 0f; It.CalmUntil = 0f; It.Finale = false; }

    IEnumerator Part1()
    {
        AudioTiming.Enabled = true;
        Settings.LowScare = false;
        Breath.Override = false;
        yield return Frames(3);
        yield return UsableFromFloor("the house", 0);
        // ---------------- the house: things to fiddle with (the fridge opens and shows the food; fruit gets eaten)
        var fiddles = FindObjectsByType<Fiddle>(FindObjectsSortMode.None);
        var fridgeF = fiddles.FirstOrDefault(f => f.swing && f.swing.name == "Fridge hinge");
        var appleF = fiddles.FirstOrDefault(f => f.name.StartsWith("food_apple"));
        bool fridgeShows = false, appleGone = false;
        if (fridgeF) { fridgeF.Tap(); yield return new WaitForSeconds(0.6f); fridgeShows = fridgeF.IsOpen && fridgeF.whileOpen.Any(x => x && x.activeInHierarchy && x.GetComponent<Fiddle>()); fridgeF.Tap(); yield return new WaitForSeconds(0.6f); }
        if (appleF) { appleF.Tap(); yield return new WaitForSeconds(1.2f); appleF.Tap(); yield return new WaitForSeconds(2.0f); appleGone = !appleF.gameObject.activeInHierarchy; }   // a bite (~1 s), then the last bite and it's taken
        Check(fiddles.Length >= 25 && fridgeShows && appleGone, $"the house: {fiddles.Length} things to fiddle with; the fridge opens on its food ({fridgeShows}); the apple gets eaten ({appleGone})");
        yield return HouseIsSolid();
        // ---------------- prologue: in bed, up, the doorbell, the parcel, the VCR; the tape plays the lobby camera;
        // it walks towards the camera; you are pulled in
        G.BeginPrologue();
        bool inBed = G.PrologueBeat == Game.Beat.InBed && P.Locked && P.HideView;
        float tb = Time.time;
        while (G.PrologueBeat != Game.Beat.Doorbell && Time.time - tb < 6f) yield return null;
        yield return new WaitForSeconds(3.6f);                 // the first knock comes 2.5 s after the bell
        var parcel = G.bedroomParcel;
        bool knocking = G.PrologueBeat == Game.Beat.Doorbell && parcel && !parcel.activeInHierarchy;     // nothing yet: it waits for you
        float knockDb = G.KnockHeardDb;
        Check(knockDb > -36f, $"prologue: the knocking at the front door is heard from the bed ({knockDb:0} dB at your ear, by way of {G.KnockPath})");
        P.Teleport(LevelLayout.FrontDoorInside, 0f, 0f);
        while (G.PrologueBeat != Game.Beat.Letter && Time.time - tb < 15f) yield return null;
        yield return new WaitForSeconds(0.6f);
        bool bell = knocking && G.PrologueBeat == Game.Beat.Letter && parcel && parcel.activeInHierarchy && !P.Locked && !P.HideView;
        if (parcel && parcel.activeInHierarchy) { parcel.GetComponent<TapeItem>().Tap(); yield return null; G.ClosePaper(); }
        Check(inBed && bell && BedroomVcr.HasTape && G.PrologueBeat == Game.Beat.HasTape, $"prologue: you wake up in bed, get up, the doorbell rings, the knocking goes on until you reach the front door, then the envelope comes through the letterbox; opening it gives you the tape ({G.PrologueBeat})");
        FindAnyObjectByType<BedroomVcr>().Tap();
        Look(LevelLayout.LivingTvView, G.bedroomTv.transform.position);
        float t0 = Time.time, closest = 99f;
        var tapeCam = CctvCamera.Find("TAPE");
        bool onTv = false;
        while (G.State == Game.Phase.Prologue && Time.time - t0 < 50f)
        {
            if (G.PrologueBeat == Game.Beat.Playing || G.PrologueBeat == Game.Beat.Pulling)
            {
                var dv = It.transform.position - LevelLayout.TapeCam; dv.y = 0f;
                closest = Mathf.Min(closest, dv.magnitude);
                onTv |= tapeCam && G.bedroomTv.source == tapeCam;
            }
            yield return null;
        }
        Check(G.State == Game.Phase.Hotel && G.TapeReachedLens && onTv && closest < 0.9f,
            $"prologue: the tape (the lobby camcorder) plays on your TV; the figure walks right up to the lens ({closest:0.00} m) and you are pulled in ({Time.time - t0:0.0} s)");
        yield return Frames(5);
        Check(Vector3.Distance(P.transform.position, LevelLayout.ReceptionSpawn) < 0.5f && LevelLayout.RoomAt(P.transform.position)?.name == "Front Office", "you arrive in the front office");
        Check(It.Floor == 1 && LevelLayout.RoomAt(It.transform.position)?.name == "Ballroom" && It.CalmUntil > Time.time + 30f, $"it starts in the ballroom on the guest floor, calm (floor {It.Floor})");
        var letter = FindObjectsByType<Readable>(FindObjectsSortMode.None).First(r => r.isLetter);
        letter.Tap(); G.ClosePaper();
        Check(G.Stage == 1 && G.Objective.Contains("214") && G.Objective.Contains("209"), $"reading the letter sets the objective: \"{G.Objective}\"");
        Check(letter.text.Contains("HEARS") && letter.text.Contains("214") && letter.text.Contains("209") && letter.text.Contains("SAFE"), "the letter states the rule, the goal and where the safe code is");
        Check(G.stageRoots != null && G.stageRoots.Length == 4 && G.stageRoots[1].activeSelf && !G.stageRoots[2].activeSelf && !G.stageRoots[3].activeSelf,
            "stages: in the hotel only part one is switched on (the basement and the roof are off)");
        yield return UsableFromFloor("part one", 1);
        yield return Frames(2);
        var plaques = FindObjectsByType<Plaque>(FindObjectsSortMode.None);
        var plans = FindObjectsByType<FloorPlan>(FindObjectsSortMode.None);
        var fp0 = plans.FirstOrDefault(pl => pl.floor == 0);
        if (fp0) fp0.Tap();
        if (MapView.Instance) MapView.Instance.SetOpen(true);
        yield return Frames(2);
        bool mapOk = MapView.Instance && MapView.Instance.Open;
        if (MapView.Instance) MapView.Instance.SetOpen(false);
        Check(plaques.Length >= 30 && plaques.Any(pl => pl.text == "214") && plaques.Any(pl => pl.text == "209") && plans.Any(pl => pl.floor == 1) && fp0 && G.HasPlan(0) && mapOk,
            $"wayfinding: {plaques.Length} door plates and signs in part one (214 and 209 among them), {plans.Length} fire plans on the ground and guest floors; filming the front office plan puts the ground floor on the camcorder map (M)");

        // ---------------- hearing through the building (dB at its ear; it notices 20 dB)
        float carpet = NoiseBus.Arriving(30f, W(13f, 10f, 1, 0.05f), W(13f, 15f, 1, 1.7f));
        Check(carpet < 20f, $"hearing: a carpet footstep 5 m away in the guest corridor arrives at {carpet:0} dB (not heard)");
        float lino = NoiseBus.Arriving(32f, W(13f, 8f, 0, 0.05f), W(13f, 13f, 0, 1.7f));
        Check(lino < 20f, $"hearing: a step on the staff passage lino 5 m away arrives at {lino:0} dB (not heard)");
        float marble = NoiseBus.Arriving(56f, W(30f, 14f, 0, 0.05f), W(30f, 25f, 1, 1.7f));
        Check(marble >= 20f, $"hearing: running on the lobby marble is heard on the north gallery upstairs ({marble:0} dB)");
        var ph211 = Phone.Find("211");
        float ring = NoiseBus.Arriving(75f, ph211.transform.position, W(47f, 20f, 1, 1.7f));
        Check(ring >= 20f, $"hearing: Room 211's phone ringing (door shut) reaches the east corridor at {ring:0} dB");
        float house = NoiseBus.Arriving(75f, Phone.Find("Gallery").transform.position, W(30f, 32f, 1, 1.7f));
        Check(house >= 42f, $"hearing: the gallery house phone reaches the ballroom at {house:0} dB (enough to break its calm, 42)");
        var d212 = Door("Door 212");
        d212.SetOpen(0f);
        Physics.SyncTransforms();
        var dsrc = W(43f, 5f, 1, 1f); var dlis = W(47f, 5f, 1, 1.7f);
        float closed = NoiseBus.Arriving(60f, dsrc, dlis);
        var hitsAlong = Physics.RaycastAll(dlis, (dsrc - dlis).normalized, Vector3.Distance(dsrc, dlis), AcousticSpace.Mask, QueryTriggerInteraction.Ignore);
        Info("ray 212 hits: " + string.Join(", ", hitsAlong.OrderBy(h => h.distance).Select(h => $"{h.collider.name}(L{h.collider.gameObject.layer}) {h.distance:0.00}")) + $"  door angle {d212.Angle:0}");
        var pc = d212.leaf.GetChild(0).GetComponent<Collider>();
        Info($"door 212: root {d212.transform.position}, panel {d212.leaf.GetChild(0).position} ({d212.leaf.GetChild(0).name}), collider {(pc ? pc.bounds.ToString() + " enabled " + pc.enabled + " layer " + pc.gameObject.layer : "none")}");
        var allHits = Physics.RaycastAll(dlis, (dsrc - dlis).normalized, Vector3.Distance(dsrc, dlis), ~0, QueryTriggerInteraction.Collide);
        Info("ray 212 all layers: " + string.Join(", ", allHits.Select(h => $"{h.collider.name}(L{h.collider.gameObject.layer},{(h.collider.isTrigger ? "trig" : "solid")})")));
        d212.SetOpen(90f * d212.hingeSign);
        Physics.SyncTransforms();
        float open = NoiseBus.Arriving(60f, dsrc, dlis);
        d212.SetOpen(0f);
        Physics.SyncTransforms();
        Check(open > closed + 6f, $"acoustics: a sound in 212 is louder in the corridor with the door open ({open:0} dB) than shut ({closed:0} dB)");

        // ---------------- speeds: you can outrun it
        Check(It.wanderSpeed < P.walkSpeed && It.investigateSpeed < P.walkSpeed && It.rushSpeed < P.runSpeed,
            $"speeds: it wanders {It.wanderSpeed} and investigates {It.investigateSpeed} m/s (you walk {P.walkSpeed}), rushes {It.rushSpeed} m/s (you run {P.runSpeed})");
        float gap = (P.runSpeed - It.rushSpeed) * P.staminaMax;
        Check(gap > 6f, $"speeds: one full sprint ({P.staminaMax} s) gains you {gap:0.0} m on a rushing listener");

        // ---------------- it hears a bottle and goes there (investigate, slower than walking)
        Calm();
        It.Place(W(30f, 32f, 1), 180f);
        yield return Frames(3);
        var bottleAt = W(36f, 25.2f, 1, 0.1f);
        NoiseBus.Emit(bottleAt, 72f, false, "bottle smash");
        yield return Frames(2);
        Check(It.state == Listener.State.Investigate && Vector3.Distance(It.Target, bottleAt) < 2.6f, $"listener: a smashed bottle on the gallery sends it to investigate (state {It.state}, heard {It.LastHeard} {It.LastHeardDb:0} dB)");
        var p0 = It.transform.position;
        yield return new WaitForSeconds(3f);
        float sp = Vector3.Distance(p0, It.transform.position) / 3f;
        Check(sp > 0.5f && sp <= P.walkSpeed + 0.05f, $"listener: investigating it moves {sp:0.00} m/s (no faster than you walk)");

        // ---------------- it rushes only when it hears YOU close and loud
        Calm();
        It.Place(W(30f, 32f, 1), 180f);
        yield return Frames(3);
        NoiseBus.Emit(W(34.5f, 32f, 1, 0.05f), 56f, true, "step wood");      // open floor between the tables
        yield return Frames(2);
        Check(It.state == Listener.State.Rush, $"listener: a running step 4.5 m away makes it rush (state {It.state})");
        Calm();
        It.Place(W(30f, 32f, 1), 180f);
        yield return Frames(3);
        NoiseBus.Emit(W(27f, 29.5f, 1, 0.05f), 30f, true, "step carpet");
        yield return Frames(2);
        Check(It.state == Listener.State.Wander || It.state == Listener.State.Search, $"listener: a quiet footstep (30 dB) 3.9 m away is not heard (state {It.state})");

        // ---------------- calm start: only loud sounds for the first seconds
        It.Place(W(30f, 32f, 1), 180f);
        It.CalmUntil = Time.time + 30f;
        yield return Frames(3);
        NoiseBus.Emit(W(20f, 30f, 1, 0.1f), 40f, true, "door");                // 10 m: beyond the close range
        yield return Frames(2);
        bool calmIgnored = It.state == Listener.State.Wander || It.state == Listener.State.Search;
        NoiseBus.Emit(Phone.Find("Gallery").transform.position, 75f, false, "phone Gallery");
        yield return Frames(2);
        Check(calmIgnored && It.state == Listener.State.Investigate, $"calm start: it ignores a door 10 m away but goes to a ringing phone (state {It.state})");


        // ---------------- the camera light: on when you arrive, short bursts: drains in about 30 s, charges back fast
        var cam = CameraLight.Instance;
        bool camOn = cam && cam.On && cam.spot && cam.spot.enabled;
        float b0 = cam ? cam.Battery : 0f;
        yield return new WaitForSeconds(3f);
        float b1 = cam ? cam.Battery : 0f;
        if (cam) cam.SetOn(false);
        yield return new WaitForSeconds(3f);
        float b2 = cam ? cam.Battery : 0f;
        if (cam) cam.SetOn(true);
        // stand facing a wall from 0.4 m: the light should pull itself down
        float nearDim = 1f;
        if (cam && cam.spot)
        {
            var probe = new GameObject("near-dim probe").AddComponent<BoxCollider>();
            probe.size = new Vector3(3f, 3f, 0.1f);
            var st = cam.spot.transform;
            probe.transform.SetPositionAndRotation(st.position + st.forward * 0.45f, Quaternion.LookRotation(st.forward));
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.8f);
            nearDim = cam.NearDim;
            Destroy(probe.gameObject);
        }
        Check(camOn && b1 < b0 - 0.07f && b2 > b1 + 0.08f && nearDim < 0.5f, $"camera light: on when you arrive; 3 s on drains it {b0:0.00} -> {b1:0.00} (about 30 s a charge); 3 s off charges it back to {b2:0.00}; against a wall it dims to {nearDim:0.00}");

        // ---------------- the picture itself: the tape chain (a shader) must not leave the screen black or white.
        // A shader compile error once blacked the whole picture while every other check still passed.
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        float lumSum = 0f; int lumN = 0, black = 0, white = 0;
        for (int y = 4; y < shot.height; y += 8)
            for (int x = 4; x < shot.width; x += 8)
            {
                var c = shot.GetPixel(x, y);
                float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                lumSum += l; lumN++;
                if (l < 0.03f) black++;
                if (l > 0.97f) white++;
            }
        Destroy(shot);
        float meanLum = lumN > 0 ? lumSum / lumN : 0f;
        // 0.2: a normal front office with the camera light measures about 0.3; an unlit one (all room surfaces dropped
        // from the lights' rendering layers) measured 0.15 and still passed the old 0.05
        Check(meanLum > 0.2f && meanLum < 0.8f && black < lumN * 0.9f && white < lumN * 0.3f, $"picture: mean brightness {meanLum:0.00} with the camera light on, {100f * black / Mathf.Max(1, lumN):0}% near-black, {100f * white / Mathf.Max(1, lumN):0}% clipped white");

        // ---------------- the building's failing light (more than the lamps)
        var ln = Lightning.Instance;
        Check(ln && ln.LightCount >= 15, $"storm: {(ln ? ln.LightCount : 0)} lightning lights outside the boarded windows");
        bool litDuring = false;
        if (ln) { ln.Flash(0.6f); for (float t = 0; t < 0.6f; t += Time.deltaTime) { litDuring |= ln.AnyLit; yield return null; } yield return new WaitForSeconds(0.2f); }
        Check(litDuring && ln && !ln.AnyLit, "storm: a lightning flash switches the window lights on and off again");
        var thunder = SoundBank.Variants("thunder");
        Check(thunder.Length == 2 && thunder.All(c => c && c.length > 5f), "storm: the thunder recordings load (" + string.Join(", ", thunder.Select(c => c ? c.name + " " + c.length.ToString("0.0") + " s" : "missing")) + ")");
        var tubes = Lamp.All.Where(l => l && l.name == "Tube" && l.powered && l.on && !l.dead && l.pattern != Lamp.Pattern.Dying && LevelLayout.FloorOf(l.transform.position) == 0).ToList();
        float tubesBefore = tubes.Average(l => l.Level);
        PowerGrid.Instance.Force(0, true, 3f);
        yield return new WaitForSeconds(1.2f);
        float tubesDuring = tubes.Average(l => l.Level);
        float emergency = Lamp.All.Where(l => l && l.pattern == Lamp.Pattern.Emergency && LevelLayout.FloorOf(l.transform.position) == 0).Select(l => l.Level).DefaultIfEmpty(0f).Max();
        yield return new WaitForSeconds(2.5f);
        float after = tubes.Average(l => l.Level);
        Check(tubesBefore > 0.3f && tubesDuring < 0.05f && after > 0.3f && emergency > 0.1f, $"power: a ground-floor blackout takes the tubes from {tubesBefore:0.00} to {tubesDuring:0.00} and back to {after:0.00}; the emergency lights stay on ({emergency:0.00})");
        Check(SparkingWire.All.Count >= 5, $"decay: {SparkingWire.All.Count} live cables that arc");
        if (SparkingWire.All.Count > 0)
        {
            var w = SparkingWire.All[0];
            w.Burst();
            bool arc = false;
            for (float t = 0; t < 0.6f; t += Time.deltaTime) { arc |= w.flash && w.flash.enabled; yield return null; }
            Check(arc, "decay: a live cable arcs (its light flashes)");
        }
        int deadLamps = Lamp.All.Count(l => l && l.dead);
        int colours = Lamp.All.Where(l => l && l.lightSource).Select(l => ColorUtility.ToHtmlStringRGB(l.lightSource.color)).Distinct().Count();
        int stains = FindObjectsByType<MeshRenderer>().Count(r => r.name == "Water stain" || r.name == "Mould" || r.name == "Soot" || r.name == "Grime");
        int deadKinds = Lamp.All.Count(l => l && (l.name == "Tube" || l.name == "Sconce" || l.name == "Bulb"));
        // after the brighten pass about one fitting in ten is dead: some, but not so many the hotel goes black
        Check(deadLamps >= 5 && deadLamps <= deadKinds / 4 && colours >= 40 && stains >= 300, $"decay: {deadLamps} of {deadKinds} tubes, sconces and bare bulbs are dead fittings (5 to a quarter wanted); {colours} different light colours; {stains} grime and stain decals");

        // ---------------- a lure: the concierge radio. It comes, listens, smashes it.
        Calm();
        It.Place(W(27f, 18f, 0), 270f);
        Door("Door Concierge").SetOpen(90f * Door("Door Concierge").hingeSign);
        var radio = NoiseDevice.All.First(d => d.label == "desk radio");
        radio.TurnOn(true);
        t0 = Time.time;
        while (!radio.Broken && Time.time - t0 < 30f) yield return null;
        Check(radio.Broken, $"lure: the concierge radio brings it from the lobby; it smashes the radio after {Time.time - t0:0} s");
        if (!radio.Broken) Dump("radio lure");

        // ---------------- a phone lure: ring 211 from the switchboard
        Calm();
        It.Place(W(47f, 22f, 1), 180f);
        FindObjectsByType<SwitchboardButton>(FindObjectsSortMode.None).First(b => b.room == "211").Tap();
        t0 = Time.time;
        while (!ph211.OffHook && Time.time - t0 < 35f) yield return null;
        Check(ph211.OffHook, $"lure: the switchboard rings 211; it walks in and tears the phone off ({Time.time - t0:0} s)");
        if (!ph211.OffHook) Dump("phone lure");

        // ---------------- a bolted door stops it (it bangs, then gives up); in the finale the bolt breaks
        // (Room 205: no connecting door, so the bolted door is the only way in)
        Calm();
        var d203 = Door("Door 205");
        d203.SetOpen(0f);
        var bolt203 = FindObjectsByType<DoorBolt>(FindObjectsSortMode.None).First(b => b.door == d203);
        bolt203.Tap();
        yield return Frames(5);
        Check(d203.bolted, "bolt: Room 205's door bolts from inside");
        It.Place(W(13f, 22f, 1), 180f);
        yield return Frames(5);
        It.GoTo(W(8f, 17f, 1), false);
        t0 = Time.time;
        int bangs0 = d203.BangCount;
        bool entered = false;
        while (Time.time - t0 < 20f && d203.BangCount < bangs0 + 3) { if (LevelLayout.RoomAt(It.transform.position)?.name == "Room 205") entered = true; yield return null; }
        yield return new WaitForSeconds(1f);
        Check(d203.BangCount >= bangs0 + 1 && d203.bolted && !entered && d203.Openness < 0.1f, $"bolt: it can't open the bolted door; it bangs on it ({d203.BangCount - bangs0} blows) and stays out");
        if (d203.BangCount == bangs0) Dump("bolt");
        It.Finale = true;
        It.GoTo(W(8f, 17f, 1), true);
        t0 = Time.time;
        while (Time.time - t0 < 20f && d203.bolted) yield return null;
        Check(!d203.bolted, $"bolt: in the finale it breaks the bolt after a few blows ({Time.time - t0:0.0} s)");
        It.Finale = false;

        // ---------------- doors: hold E eases a door silently and stops part open; a tap swings it (heard)
        It.SendAway(60f, 0);
        var d201 = Door("Door 201");
        d201.SetOpen(0f);
        yield return Frames(3);
        heard.Clear();
        for (float t = 0; t < 1.2f; t += Time.deltaTime) { d201.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        d201.Release();
        float eased = d201.Openness;
        float easedLoud = heard.Where(n => n.src == (Object)d201).Select(n => n.db).DefaultIfEmpty(0f).Max();
        yield return new WaitForSeconds(0.8f);
        Check(eased > 0.4f && eased < 0.8f && Mathf.Abs(d201.Openness - eased) < 0.01f && easedLoud < 22f,
            $"door: holding E for 1.2 s eases it {eased * 100f:0}% open, it stays there when let go, loudest noise {easedLoud:0} dB");
        d201.SetOpen(0f);
        yield return Frames(2);
        heard.Clear();
        d201.Tap();
        yield return new WaitForSeconds(1.2f);
        float tapLoud = heard.Where(n => n.src == (Object)d201).Select(n => n.db).DefaultIfEmpty(0f).Max();
        Check(d201.Openness > 0.9f && tapLoud >= 34f, $"door: a tap swings it open ({d201.Openness * 100f:0}%), loudest noise {tapLoud:0} dB");
        heard.Clear();
        for (float t = 0; t < 1.0f; t += Time.deltaTime) { d201.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        d201.Release();
        Check(d201.Openness < 0.8f && d201.Openness > 0.3f, $"door: holding E on an open door eases it shut ({d201.Openness * 100f:0}% open after 1 s)");
        // uneven frames (as in the editor, or a hitch): the ease must keep its speed. Frame times alternate between
        // about 40 ms and 7 ms while E is held; the door used to ease at about half speed like this.
        d201.SetOpen(0f);
        yield return Frames(3);
        float easeStart = Time.time;
        for (int f = 0; Time.time - easeStart < 1.2f; f++)
        {
            Application.targetFrameRate = f % 2 == 0 ? 25 : 144;
            d201.Hold(Time.deltaTime, Vector2.zero);
            yield return null;
        }
        d201.Release();
        Application.targetFrameRate = 60;
        float unevenEase = d201.Openness;
        Check(unevenEase > 0.4f && unevenEase < 0.8f && Mathf.Abs(unevenEase - eased) < 0.1f, $"door: with uneven frame times (25 / 144 fps alternating) holding E for 1.2 s still eases it {unevenEase * 100f:0}% open (steady frames: {eased * 100f:0}%)");
        d201.SetOpen(0f);

        // ---------------- breath: no breathing noise while you hold it; hold too long and you gasp
        Look(W(17f, 22f, 0), W(17f, 25f, 0, 1.6f));
        yield return new WaitForSeconds(1.5f);
        heard.Clear();
        yield return new WaitForSeconds(6f);
        int breathsFree = heard.Count(n => n.what == "breath");
        heard.Clear();
        Breath.Override = true;
        yield return new WaitForSeconds(6f);
        int breathsHeld = heard.Count(n => n.what == "breath");
        Check(breathsFree >= 1 && breathsHeld == 0, $"breath: {breathsFree} breaths in 6 s normally, {breathsHeld} while holding your breath");
        yield return new WaitForSeconds(5f);
        bool gasped = heard.Any(n => n.what == "gasp");
        Breath.Override = false;
        Check(gasped, "breath: holding it past 10 s ends in a loud gasp");
        yield return new WaitForSeconds(2f);

        // ---------------- hiding: keep still and it walks past; move (rustle) right next to it and it finds you
        var ward = FindObjectsByType<HideSpot>(FindObjectsSortMode.None).First(h => h.transform.parent && h.transform.parent.name == "Wardrobe 201");
        Look(ward.transform.position + Vector3.right * 1.0f + Vector3.down * ward.transform.position.y + Vector3.up * W(0, 0, 1).y, ward.transform.position);
        ward.Tap();
        Breath.Override = false;           // breathing calmly (16 dB): too quiet to hear through the wardrobe
        yield return new WaitForSeconds(4f);
        int c0 = G.Catches;
        Calm();
        It.Place(ward.transform.position + Vector3.right * 1.1f + Vector3.down * 1f, 270f, Listener.State.Search);
        It.Listen(10f);
        yield return new WaitForSeconds(3f);
        bool safe = G.Catches == c0 && HideSpot.Inside;
        if (!safe) { Info($"hiding: during the still window catches {c0} -> {G.Catches}, inside {HideSpot.Inside}"); Dump("hiding"); }
        NoiseBus.Emit(ward.view.position, 30f, true, "rustle");
        t0 = Time.time;
        while (G.Catches == c0 && Time.time - t0 < 3f) yield return null;
        Check(safe && G.Catches == c0 + 1, $"hiding: still and breathing calmly in the wardrobe, it stands 1 m away and doesn't find you; a rustle does (catches {c0} -> {G.Catches})");
        yield return new WaitForSeconds(1.2f);
        Check(Vector3.Distance(P.transform.position, LevelLayout.ReceptionSpawn) < 0.8f && It.state == Listener.State.Away, $"caught: the tape skips back to the front office; it goes into the shaft (state {It.state})");
        Check(Mathf.Abs(G.faceMat.GetFloat("_Erase") - 1f / 3f) < 0.01f, $"caught: your face erase stage {G.faceMat.GetFloat("_Erase"):0.00}");

        // ---------------- reachability (its navmesh: all floors, locked/bolted doors carved out)
        var path = new NavMeshPath();
        bool Reach(Vector3 a, Vector3 b)
        {
            if (!NavMesh.SamplePosition(a, out var ha, 1.6f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out var hb, 1.6f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
        Check(Reach(LevelLayout.ReceptionSpawn, W(43f, 11f, 1)), "reachable: front office -> Room 214 (grand stair)");
        Check(Reach(W(43f, 11f, 1), W(19.2f, 39f, 0)), "reachable: Room 214 -> the basement door (ground floor)");
        Check(Reach(W(13f, 10f, 0), W(11f, 33f, 1)), "reachable: staff passage -> staff stair -> guest floor");
        Check(!Reach(W(19.2f, 39f, 0), W(28.5f, 36f, -1)), "before the basement door is unlocked, the basement can't be reached");
        int floorsOk = 0;
        foreach (var kv in LevelLayout.Patrol)
        {
            bool all = kv.Value.All(p => Reach(kv.Value[0], p)) && (kv.Key == -1 || Reach(kv.Value[0], LevelLayout.Lair(kv.Key)));
            if (kv.Key == -1) all = kv.Value.All(p => Reach(kv.Value[0], p)) && Reach(kv.Value[0], LevelLayout.Lair(-1));
            if (all) floorsOk++;
            else Info($"patrol on floor {kv.Key}: unreachable " + string.Join(" ", kv.Value.Where(p => !Reach(kv.Value[0], p)).Select(p => $"({p.x:0.#},{p.z:0.#})")) + (Reach(kv.Value[0], LevelLayout.Lair(kv.Key)) ? "" : " lair"));
        }
        Check(floorsOk == LevelLayout.Patrol.Count, $"its patrol routes and lift lairs are reachable on every floor of every part ({floorsOk}/{LevelLayout.Patrol.Count})");
        int hidesOk = 0, hides = 0;
        foreach (var h in FindObjectsByType<HideSpot>(FindObjectsSortMode.None)) { hides++; if (NavMesh.SamplePosition(h.transform.position, out _, 1.8f, NavMesh.AllAreas)) hidesOk++; else Info("hide not near navmesh: " + h.transform.parent.name); }
        Check(hides >= 20 && hidesOk == hides, $"hiding places in part one: {hides}, next to walkable floor: {hidesOk}");
        int lures = NoiseDevice.All.Count(d => !d.auto);
        Check(lures >= 10, $"lures in part one: {lures} devices, {Phone.All.Count} phones, {FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length} pickups");

        // ---------------- performance route through all three floors
        yield return PerfRoute();

        // ---------------- face tracking model
        var cfg = Resources.Load<FaceTrackerConfig>("FaceTrackerConfig");
        if (cfg && cfg.model)
        {
            float ms = FaceTracker.SelfTest(cfg.model, cfg.normMat, out float score);
            Check(score < 0.5f, $"face model loads and runs ({ms:0.0} ms first run), no face found in a grey picture (score {score:0.00})");
        }
        else Check(false, "face model config missing");

        // ---------------- the whole chain, the listener switched off
        yield return BotRun();
    }

    IEnumerator PerfRoute()
    {
        Info($"shadows: {ShadowBudget.Managed} room lamps can cast shadows; at most {(FindAnyObjectByType<ShadowBudget>() ? FindAnyObjectByType<ShadowBudget>().budget : -1)} nearest do at once ({ShadowBudget.Casting} now), soft edges; real voices {AudioSettings.GetConfiguration().numRealVoices}");
        It.SendAway(999f, 0);
        var route = new[] { LevelLayout.ReceptionSpawn, W(21f, 14f), W(30f, 14f), W(36.25f, 11f), W(36.25f, 25f, 1), W(30f, 25f, 1), W(21f, 16f, 1), W(13f, 16f, 1), W(13f, 30f, 1),
            W(30f, 39f, 1), W(47f, 30f, 1), W(47f, 8f, 1), W(39f, 5f, 1), W(36.25f, 25f, 1), W(36.25f, 11f), W(30f, 30f), W(30f, 39f), W(19.2f, 39.4f),
            W(28.5f, 36f, -1), W(28.5f, 32f, -1), W(10f, 32f, -1), W(9f, 26f, -1), W(10f, 14f, -1), W(30f, 4f, -1), W(52f, 22f, -1), W(38f, 39f, -1), W(50f, 40f, -1),
            W(47f, 41.4f, 3), W(47f, 36f, 3), W(30f, 36f, 3), W(30f, 24f, 3), W(42f, 28f, 3), W(38f, 18f, 3), W(46f, 18f, 3), W(54f, 24f, 3), W(47f, 36f, 3), W(38f, 37f, 3),
            W(38f, 36f, 4), W(31f, 20f, 4), W(45f, 17f, 4), W(55f, 26f, 4), W(55f, 40.5f, 4) };
        var frames = new List<float>();
        var cpu = new List<double>(); var gpu = new List<double>();
        var ft = new FrameTiming[1];
        for (int i = 0; i < route.Length - 1; i++)
        {
            int steps = Mathf.Max(20, Mathf.RoundToInt(Vector3.Distance(route[i], route[i + 1]) * 12f));
            for (int k = 0; k < steps; k++)
            {
                var pos = Vector3.Lerp(route[i], route[i + 1], k / (float)steps);
                var nxt = route[i + 1];
                P.Teleport(pos, Mathf.Atan2(nxt.x - pos.x, nxt.z - pos.z) * Mathf.Rad2Deg, 0f);
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { cpu.Add(ft[0].cpuMainThreadFrameTime); gpu.Add(ft[0].gpuFrameTime); }
            }
        }
        frames.Sort(); cpu.Sort(); gpu.Sort();
        float p95 = frames[(int)(frames.Count * 0.95f)];
        string g = gpu.Count > 0 ? $"GPU p95 {gpu[(int)(gpu.Count * 0.95f)]:0.0} ms" : "GPU timing unavailable";
        string c = cpu.Count > 0 ? $"CPU main p95 {cpu[(int)(cpu.Count * 0.95f)]:0.0} ms" : "";
        Check(p95 < 25f, $"performance route (all three parts): frame p95 {p95:0.0} ms ({frames.Count} frames, 60 fps cap)  {c}  {g}");
        Info($"lights in the scene: {Lamp.All.Count}");
        Info("audio: " + AudioTiming.Report);
    }

    IEnumerator Walk(Vector3 to)
    {
        var path = new NavMeshPath();
        NavMesh.SamplePosition(P.transform.position, out var a, 2f, NavMesh.AllAreas);
        NavMesh.SamplePosition(to, out var b, 2f, NavMesh.AllAreas);
        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
        if (path.status != NavMeshPathStatus.PathComplete) Info($"walk: path to {to} is {path.status}");
        var c = path.corners;
        for (int i = 0; i + 1 < c.Length; i++)
        {
            float len = Vector3.Distance(c[i], c[i + 1]);
            for (float d = 0; d < len; d += 2.4f / 30f)
            {
                var pos = Vector3.Lerp(c[i], c[i + 1], d / len);
                P.Teleport(pos, Mathf.Atan2(c[i + 1].x - c[i].x, c[i + 1].z - c[i].z) * Mathf.Rad2Deg, 0f);
                yield return null;
            }
        }
        P.Teleport(b.position, P.Yaw, 0f);
        yield return null;
    }

    // every hiding place sits next to floor it can walk (or it would be useless or a trap); enough of them, and lures
    IEnumerator HidesAndLures(string part, int minHides, int minLures)
    {
        yield return Frames(2);
        int hides = 0, ok = 0;
        var bad = new List<string>();
        foreach (var h in FindObjectsByType<HideSpot>(FindObjectsSortMode.None)) { hides++; if (NavMesh.SamplePosition(h.transform.position, out _, 1.8f, NavMesh.AllAreas)) ok++; else bad.Add(h.transform.parent ? h.transform.parent.name : h.name); }
        int lures = NoiseDevice.All.Count(d => !d.auto);
        Check(hides >= minHides && ok == hides && lures >= minLures, $"{part}: {hides} hiding places ({ok} next to walkable floor{(bad.Count > 0 ? "; not: " + string.Join(", ", bad) : "")}), {lures} lures");
    }

    // Everything you can use in a stage can be aimed at from walkable floor you can get to: within the Interactor's
    // reach, standing or crouched, with nothing in front of it that its ray would hit first (triggers count, as they
    // do for the Interactor). The basement key once lay inside the safe's body, where no player could ever take it.
    // Locked and bolted doors carve the navmesh shut; they are the stage's gates, not faults, so they are opened here.
    IEnumerator UsableFromFloor(string part, int stage)
    {
        var gates = FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None).Where(o => o.enabled && o.GetComponentInParent<HingeDoor>()).ToList();
        foreach (var o in gates) o.enabled = false;
        yield return Frames(3);
        Physics.SyncTransforms();
        const int mask = ~((1 << 8) | (1 << 9) | (1 << 15) | (1 << 16));
        var root = G.stageRoots[stage].transform;
        var path = new NavMeshPath();
        bool haveStart = NavMesh.SamplePosition(P.transform.position, out var start, 1.5f, NavMesh.AllAreas);
        int n = 0, skipped = 0;
        var bad = new List<string>();
        foreach (var it in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
        {
            if (!it.isActiveAndEnabled || !it.transform.IsChildOf(root)) continue;
            if (it is KeyItem ki && !ki.reachable) { skipped++; continue; }     // revealed later (behind the dresser, in the bag)
            var col = it.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled);
            if (!col) continue;
            n++;
            var b = col.bounds;
            var blockers = new Dictionary<string, int>();
            bool ok = false, sawIt = false, anyFloor = false;
            string pathEnd = "";
            for (int k = 0; k < 16 && !ok; k++)
            {
                float a = k * 22.5f * Mathf.Deg2Rad;
                foreach (float r in new[] { 0.45f, 0.75f, 1.05f, 1.4f, 1.75f })
                {
                    var p = b.center + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    if (!NavMesh.SamplePosition(new Vector3(p.x, b.center.y - 1.0f, p.z), out var h, 1.6f, NavMesh.AllAreas)) continue;
                    if (new Vector2(h.position.x - p.x, h.position.z - p.z).magnitude > 0.35f) continue;
                    foreach (float eyeH in new[] { 1.6f, 1.0f })
                    {
                        var eye = h.position + Vector3.up * eyeH;
                        bool seen = false;
                        foreach (var aim in new[] { b.center, Vector3.Lerp(b.center, b.ClosestPoint(eye), 0.7f) })
                        {
                            var d = aim - eye;
                            if (d.magnitude > 2.1f) continue;
                            anyFloor = true;
                            if (Physics.Raycast(eye, d.normalized, out var hit, d.magnitude + 0.3f, mask, QueryTriggerInteraction.Collide)
                                && hit.collider.GetComponentInParent<Interactable>() != it)
                            { blockers[hit.collider.name] = blockers.TryGetValue(hit.collider.name, out int bn) ? bn + 1 : 1; continue; }
                            seen = true; break;
                        }
                        if (!seen) continue;
                        sawIt = true;
                        if (haveStart && !(NavMesh.CalculatePath(start.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete))
                        {
                            if (pathEnd == "" && path.corners.Length > 0) { var e = path.corners.Last(); pathEnd = $"; the path from the start ends at ({e.x:0.0}, {e.y:0.0}, {e.z:0.0}) in {LevelLayout.RoomAt(e + Vector3.up * 0.5f)?.name ?? "?"}"; }
                            continue;
                        }
                        ok = true; break;
                    }
                    if (ok) break;
                }
            }
            if (!ok)
            {
                string why = sawIt ? "it can be seen only from floor that can't be walked to" + pathEnd
                           : blockers.Count > 0 ? "blocked by " + string.Join(", ", blockers.OrderByDescending(x => x.Value).Take(2).Select(x => x.Key))
                           : anyFloor ? "?" : "no walkable floor within reach";
                bad.Add($"{it.GetType().Name} \"{it.name}\" ({(it.transform.parent ? it.transform.parent.name : "")}) at ({b.center.x:0.0}, {b.center.y:0.0}, {b.center.z:0.0}) in {LevelLayout.RoomAt(b.center + Vector3.down * 0.3f)?.name ?? "?"}: {why}");
            }
        }
        foreach (var o in gates) if (o) o.enabled = true;
        Check(n > 0 && bad.Count == 0, $"{part}: all {n} things you can use can be aimed at from walkable floor within reach ({skipped} hidden until revealed; locked doors opened for this){(bad.Count > 0 ? $"; {bad.Count} not:" : "")}");
        foreach (var s in bad.Take(25)) Info("   " + s);
        yield return Frames(3);
    }

    // ================================================================== senses, doors, the map (fresh scene)
    // In the lobby, on bare marble (the rugs are quiet), it stands still and listens 6 m north of you.
    IEnumerator Gait(string how, bool crouch, bool run, Vector3 from, float seconds, float itZ = 22.5f)
    {
        P.Teleport(from, 90f, 0f);
        yield return Frames(3);
        It.Place(W(34f, itZ), 180f);
        It.Listen(40f);
        Calm();
        heard.Clear();
        PlayerMotor.BotDriven = true; PlayerMotor.BotCrouch = crouch; PlayerMotor.BotRun = run;
        yield return new WaitForSeconds(0.4f);                // crouch down before setting off
        PlayerMotor.BotMove = new Vector2(0f, 1f);
        float t0 = Time.time;
        while (Time.time - t0 < seconds) { P.SetLook(90f, 0f); yield return null; }
        PlayerMotor.BotMove = Vector2.zero;
        yield return Frames(2);
    }
    void BotOff() { PlayerMotor.BotDriven = false; PlayerMotor.BotCrouch = false; PlayerMotor.BotRun = false; PlayerMotor.BotMove = Vector2.zero; }

    IEnumerator Senses()
    {
        G.EnterHotel();
        yield return Frames(5);
        Listener.NoCatch = true;                           // these are about its senses: being caught would send you back
        var cam = CameraLight.Instance;
        if (cam) cam.SetOn(false);
        // ---------------- how much lamp light falls on you at a few places (the meter, and what it can see)
        var spots = new (string, Vector3)[] { ("front office", LevelLayout.ReceptionSpawn), ("lobby, middle", W(34f, 16.5f)), ("by the front office desk lamp", W(17.1f, 13.6f)),
            ("guest floor west corridor", W(13f, 15f, 1)), ("ballroom", W(30f, 32f, 1)), ("west staff passage", W(13f, 15f)) };
        string ex = "";
        int brightSpots = 0;
        foreach (var (name, pos) in spots)
        {
            P.Teleport(pos, 0f, 0f);
            yield return new WaitForSeconds(0.25f);
            Exposure.Tick();
            ex += $"{name} {Exposure.Ambient:0.00} ({Exposure.State}); ";
            if (Exposure.Ambient >= Exposure.Bright) brightSpots++;
        }
        Info("exposure (lamp light at your chest; visible standing from " + Exposure.Bright + ", crouched from " + Exposure.BrightCrouched + "): " + ex);
        Check(brightSpots < spots.Length, $"visibility: ordinary rooms are not bright enough for it to see you ({brightSpots} of {spots.Length} sample places are)");

        // ---------------- hearing by gait: crouched nothing, walking a few metres, running far
        Exposure.TestAmbient = 0f;                         // in the dark: only hearing counts here
        yield return Gait("crouched", true, false, W(32f, 16.5f), 2.5f);
        int crouchNoises = heard.Count(n => n.byPlayer && n.what != null && n.what.Contains("step"));
        bool crouchUnheard = It.state == Listener.State.Search;
        float stBefore = P.Stamina;
        Check(crouchNoises == 0 && crouchUnheard, $"hearing: crouch-walking 6 m from it makes no step noise ({crouchNoises}) and it keeps listening (state {It.state})");
        yield return Gait("walking", false, false, W(32f, 16.5f), 2.5f, 25.3f);
        var walkState = It.state;
        string walkHeard = It.LastHeard;
        Check(walkState == Listener.State.Investigate && walkHeard.Contains("step"), $"hearing: walking on marble 9 m from it, it hears you and comes, without rushing ({walkState}, heard \"{walkHeard}\" {It.LastHeardDb:0} dB, at {It.agent.speed:0.0} m/s)");
        // it follows: keep walking and its target keeps up with you
        PlayerMotor.BotMove = new Vector2(0f, 1f);
        float tw = Time.time;
        while (Time.time - tw < 1.5f) { P.SetLook(90f, 0f); yield return null; }
        PlayerMotor.BotMove = Vector2.zero;
        yield return Frames(2);
        float lag = Vector3.Distance(new Vector3(It.Target.x, 0, It.Target.z), new Vector3(P.transform.position.x, 0, P.transform.position.z));
        Check(lag < 2.5f, $"hearing: it follows your steps (its target is {lag:0.0} m from you after 1.5 s more walking)");
        yield return Gait("running", false, true, W(32f, 11.5f), 1.6f);
        Check(It.state == Listener.State.Rush, $"hearing: running on marble 11 m from it makes it rush (state {It.state}, heard \"{It.LastHeard}\" {It.LastHeardDb:0} dB)");
        BotOff();
        // running round it: in the calm start, and while it stands over a ringing phone, it still goes for you
        foreach (var phase in new[] { "calm start", "over a ringing phone" })
        {
            It.Place(W(34f, 19f), 180f);
            yield return Frames(2);
            if (phase == "calm start") { It.Listen(40f); It.CalmUntil = Time.time + 40f; }
            else
            {
                Calm();
                var ph = FindObjectsByType<Phone>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, W(34f, 19f))).First();
                It.Place(ph.transform.position + Vector3.back * 1.2f, 0f);
                ph.Ring(30);
                yield return new WaitForSeconds(2.5f);
            }
            var c = It.transform.position;
            P.Teleport(c + new Vector3(3f, 0f, 0f), 0f, 0f);
            yield return Frames(3);
            PlayerMotor.BotDriven = true; PlayerMotor.BotRun = true;
            PlayerMotor.BotMove = new Vector2(0f, 1f);
            float tr = Time.time;
            bool went = false;
            while (Time.time - tr < 4f && !went)
            {
                var off = P.transform.position - c; off.y = 0f;
                P.SetLook(Mathf.Atan2(-off.z, off.x) * Mathf.Rad2Deg, 0f);      // circle it, 3 m out
                went = It.state == Listener.State.Rush || (It.state == Listener.State.Investigate && It.LastHeard.Contains("step"));
                yield return null;
            }
            BotOff();
            Check(went, $"hearing: running round it {phase}, it goes for you ({It.state}, heard \"{It.LastHeard}\", after {Time.time - tr:0.0} s)");
            if (phase != "calm start") foreach (var ph in FindObjectsByType<Phone>(FindObjectsSortMode.None)) if (ph.Ringing) ph.Smash();
        }
        Calm();

        // crouching costs stamina
        P.Teleport(W(36f, 11.5f), 270f, 0f);
        yield return new WaitForSeconds(2.5f);              // stamina back
        float s0 = P.Stamina;
        PlayerMotor.BotDriven = true; PlayerMotor.BotCrouch = true;
        yield return new WaitForSeconds(0.3f);
        PlayerMotor.BotMove = new Vector2(0f, 1f);
        yield return new WaitForSeconds(2f);
        float s1 = P.Stamina;
        bool wasSilent = P.Silent;
        BotOff();
        Check(s1 < s0 - 0.6f && wasSilent, $"crouching: silent while you have stamina, and it uses it ({s0:0.0} -> {s1:0.0} s of {P.staminaMax:0} in 2.3 s)");

        // ---------------- sight: poor; only strong light, or your camera light
        P.Teleport(W(34f, 16.5f), 0f, 0f);
        Exposure.TestAmbient = 0f;
        It.Place(W(34f, 22.5f), 180f); It.Listen(40f); Calm();
        yield return new WaitForSeconds(1.5f);
        Check(!It.SeesYou && It.state == Listener.State.Search, $"sight: in the dark, standing still 6 m in front of it, it doesn't see you (state {It.state})");
        if (cam) cam.SetOn(true);
        yield return new WaitForSeconds(1.2f);
        Check(It.state == Listener.State.Rush && It.LastHeard == "saw you", $"sight: your camera light on in front of it: it sees you and comes (state {It.state}, {It.LastHeard})");
        It.Place(W(34f, 22.5f), 0f); It.Listen(40f); Calm();
        float tBehind = Time.time;
        yield return new WaitForSeconds(1.2f);
        Check(!It.SeesYou && It.LastSawAt < tBehind, $"sight: the same light behind its back: it doesn't see you (state {It.state})");
        if (cam) cam.SetOn(false);
        yield return new WaitForSeconds(0.3f);
        Exposure.TestAmbient = Exposure.Bright * 1.2f;
        It.Place(W(34f, 22.5f), 180f); It.Listen(40f); Calm();
        yield return new WaitForSeconds(1.2f);
        bool brightSeen = It.state == Listener.State.Rush && It.LastHeard == "saw you";
        PlayerMotor.BotDriven = true; PlayerMotor.BotCrouch = true;
        It.Place(W(34f, 22.5f), 180f); It.Listen(40f); Calm();
        yield return new WaitForSeconds(1.2f);
        bool crouchHidden = !It.SeesYou && It.state == Listener.State.Search;
        BotOff();
        Check(brightSeen && crouchHidden, $"sight: standing under a bright lamp it sees you ({brightSeen}); crouched in the same light it doesn't ({crouchHidden})");
        Exposure.TestAmbient = 0f;
        // the light's click: heard from 2 m (it faces away, so it can't see the light)
        P.Teleport(W(34f, 20.5f), 0f, 0f);
        It.Place(W(34f, 22.5f), 0f); It.Listen(40f); Calm();
        yield return new WaitForSeconds(0.5f);
        if (cam) cam.SetOn(true);
        yield return Frames(3);
        Check(It.LastHeard == "camera light click" && It.state == Listener.State.Investigate, $"the camera light's click: it hears it from 2 m ({It.LastHeard}, state {It.state})");
        if (cam) cam.SetOn(false);
        Exposure.TestAmbient = null;
        It.SendAway(120f, 0);

        // ---------------- easing a door with the arrow keys
        var ease = DoorEase.Instance;
        var d = Door("Door 201");
        P.Teleport(W(12.8f, 3.6f, 1), 90f, 0f);
        d.SetOpen(0f);
        yield return Frames(3);
        DoorEase.NoDrift = true;
        heard.Clear();
        ease.Begin(d);
        for (float t = 0; t < 1.0f; t += Time.deltaTime) { ease.Tick(Time.deltaTime, 0f); yield return null; }
        float quiet = heard.Where(n => n.src == (Object)d).Select(n => n.db).DefaultIfEmpty(0f).Max();
        float opened = d.Openness;
        heard.Clear();
        ease.ForceSlip();
        float early = ease.LastSlipDb, earlyLeft = ease.LastSlipLeft;
        yield return new WaitForSeconds(0.6f);
        float earlyHeard = heard.Where(n => n.src == (Object)d).Select(n => n.db).DefaultIfEmpty(0f).Max();
        bool swung = d.Openness > 0.95f;
        Check(opened > 0.2f && quiet < 22f && swung && earlyHeard >= 55f, $"door ease: in the band it eases {opened * 100f:0}% open quietly ({quiet:0} dB); a slip with {earlyLeft * 100f:0}% left swings it the rest of the way at {earlyHeard:0} dB");
        d.SetOpen(0f);
        yield return Frames(3);
        ease.Begin(d);
        while (d.EaseLeft > 0.15f) { ease.Tick(Time.deltaTime, 0f); yield return null; }
        heard.Clear();
        ease.ForceSlip();
        float late = ease.LastSlipDb, lateLeft = ease.LastSlipLeft;
        yield return Frames(3);
        Check(late < early - 10f && late < 50f, $"door ease: the noise of a slip is as big as what was left ({early:0} dB with {earlyLeft * 100f:0}% left, {late:0} dB with {lateLeft * 100f:0}% left)");
        DoorEase.NoDrift = false;
        // untended, the hinge pulls the pointer out; steered back with the arrows, it stays in
        d.SetOpen(0f);
        yield return Frames(3);
        ease.Begin(d);
        float tu = Time.time;
        while (ease.Door && Time.time - tu < 8f) { ease.Tick(Time.deltaTime, 0f); yield return null; }
        bool slipsAlone = !ease.Door;
        float slipAfter = Time.time - tu;
        d.SetOpen(0f);
        yield return Frames(3);
        ease.Begin(d);
        float ts = Time.time;
        float vPrev = ease.Pointer;
        while (ease.Door && Time.time - ts < 2.5f && d.EaseLeft > 0.02f)
        {
            // a careful player: push back towards the middle, ease off as it comes back
            float off = ease.Pointer - ease.BandCentre, v = (ease.Pointer - vPrev) / Mathf.Max(Time.deltaTime, 1e-4f);
            vPrev = ease.Pointer;
            float input = Mathf.Clamp(-(off * 22f + v * 2.2f), -1f, 1f);
            ease.Tick(Time.deltaTime, input);
            yield return null;
        }
        bool held = ease.Door || d.EaseLeft <= 0.02f;
        float heldOpen = d.Openness;
        ease.End(); d.Release();
        float bandEnd = ease.BandHalf;
        Check(slipsAlone && held, $"door ease: left alone the pointer slips out ({slipAfter:0.0} s); steered with the arrows after the moving band it stays in (door {heldOpen * 100f:0}% open after {Time.time - ts:0.0} s; the band narrowed from {DoorEase.StartBand * 2f * 100f:0}% to {bandEnd * 2f * 100f:0}% of the bar)");
        // a tap slams it: fast and loud (for getting away)
        d.SetOpen(0f);
        yield return Frames(3);
        heard.Clear();
        float tt = Time.time;
        d.Tap();
        while (d.Openness < 0.95f && Time.time - tt < 2f) yield return null;
        float slamTime = Time.time - tt;
        yield return new WaitForSeconds(0.3f);
        float slamLoud = heard.Where(n => n.src == (Object)d).Select(n => n.db).DefaultIfEmpty(0f).Max();
        Check(slamTime < 0.6f && slamLoud >= 50f, $"door: a tap slams it open in {slamTime:0.00} s, {slamLoud:0} dB");
        d.SetOpen(0f);

        // ---------------- the map marks the fire plans of the part you are in
        G.EnterLevel(3);
        yield return Frames(10);
        MapView.Instance.SetOpen(true);
        yield return Frames(2);
        int marks = MapView.Instance.PlanMarks;
        MapView.Instance.SetOpen(false);
        Check(marks >= 1, $"map: the penthouse map marks its fire plan ({marks} marked)");
        // the visibility meter
        if (cam) cam.SetOn(true);
        yield return new WaitForSeconds(0.2f);
        var withLight = Exposure.State;
        if (cam) cam.SetOn(false);
        Check(withLight == Exposure.Seen.OwnLight, $"visibility meter: with your light on it reads 'visible · your light' ({withLight})");
        Listener.NoCatch = false;
    }

    // ---------------- F9 all the way: every objective skipped, through both transitions, to the end
    IEnumerator SkipRun()
    {
        G.EnterHotel();
        yield return Frames(5);
        It.SendAway(9999f, 0);
        var levels = new List<int>();
        int skips = 0;
        float t0 = Time.time;
        while (G.Ending == "" && skips < 20 && Time.time - t0 < 90f)
        {
            while (G.Transitioning && Time.time - t0 < 90f) yield return null;
            if (!levels.Contains(G.Level)) levels.Add(G.Level);
            G.SkipObjective();
            skips++;
            yield return new WaitForSeconds(0.4f);
            if (G.State == Game.Phase.Hotel) It.SendAway(9999f, 0);
        }
        float tEnd = Time.time;
        while (G.State != Game.Phase.Ending && Time.time - tEnd < 5f) yield return null;
        Check(G.Ending == "OUT" && levels.Count == 3, $"skip key (F9): {skips} skips go through parts {string.Join(", ", levels)} to the ending ({G.Ending})");
    }

    // Your house: every room closed in by walls (a probe every 25 cm along each edge, at chest height, must hit a wall
    // unless a door, an arch or a window is there), and nothing floating (everything you can use, and every model,
    // rests on something, apart from what hangs on a wall or from the ceiling).
    IEnumerator HouseIsSolid()
    {
        yield return Frames(2);
        Physics.SyncTransforms();
        var o = LevelLayout.BedroomOrigin;
        int walls = (1 << Layers.Structure) | (1 << Layers.Door);
        var leaks = new List<string>();
        int probes = 0;
        foreach (var room in LevelLayout.Rooms)
        {
            if (room.floor != 2) continue;
            var r = room.r;
            var edges = new (bool alongX, float line, float a, float b, float inward)[]
            { (true, r.yMin, r.xMin, r.xMax, +1), (true, r.yMax, r.xMin, r.xMax, -1), (false, r.xMin, r.yMin, r.yMax, +1), (false, r.xMax, r.yMin, r.yMax, -1) };
            foreach (var e in edges)
                for (float t = e.a + 0.2f; t < e.b - 0.2f; t += 0.25f)
                {
                    bool gap = LevelLayout.Openings.Any(op => op.floor == 2 && op.AlongX == e.alongX && Mathf.Abs((e.alongX ? op.a.y : op.a.x) - e.line) < 0.01f
                        && t > Mathf.Min(e.alongX ? op.a.x : op.a.y, e.alongX ? op.b.x : op.b.y) - 0.1f && t < Mathf.Max(e.alongX ? op.a.x : op.a.y, e.alongX ? op.b.x : op.b.y) + 0.1f);
                    if (gap) continue;
                    probes++;
                    var from = o + (e.alongX ? new Vector3(t, 1.2f, e.line + e.inward * 0.35f) : new Vector3(e.line + e.inward * 0.35f, 1.2f, t));
                    var dir = e.alongX ? new Vector3(0, 0, -e.inward) : new Vector3(-e.inward, 0, 0);
                    if (!Physics.Raycast(from, dir, 0.6f, walls, QueryTriggerInteraction.Ignore))
                        leaks.Add($"{room.name} ({(e.alongX ? "z" : "x")} {e.line:0.0}, at {t:0.0})");
                }
        }
        Check(probes > 50 && leaks.Count == 0, $"the house: every room is walled in ({probes} probes along the walls; {leaks.Count} gaps where no door, arch or window is)");
        foreach (var l in leaks.Take(15)) Info("   gap: " + l);
        // nothing floats
        var house = G.stageRoots[0].transform;
        var hangs = new[] { "Painting", "Wall clock", "Letterbox", "Coat", "Curtain", "switch", "Fire (use)", "Ceiling", "Fridge", "Use", "Splashback", "lamp", "Wine", "Cushions", "Pillows", "book", "drawer" };
        var floating = new List<string>();
        int items = 0;
        var things = house.GetComponentsInChildren<Fiddle>(true).Select(f => f.transform)
            .Concat(house.GetComponentsInChildren<Transform>(true).Where(t => t.parent && t.parent.name == "House" && t.GetComponentInChildren<MeshFilter>()))
            .Distinct();
        foreach (var t in things)
        {
            if (!t.gameObject.activeInHierarchy || hangs.Any(h => t.name.Contains(h))) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
            items++;
            var start = new Vector3(b.center.x, b.min.y + 0.03f, b.center.z);
            bool supported = Physics.RaycastAll(start, Vector3.down, 0.2f, ~((1 << Layers.Player) | (1 << Layers.Interactable)), QueryTriggerInteraction.Ignore)
                .Any(h => !h.collider.transform.IsChildOf(t));
            if (!supported) floating.Add($"{t.name} at {b.center.x - o.x:0.0}, {b.min.y:0.00}, {b.center.z - o.z:0.0}");
        }
        Check(items > 20 && floating.Count == 0, $"the house: nothing floats ({items} things checked; {floating.Count} with nothing under them)");
        foreach (var f in floating.Take(15)) Info("   floating: " + f);
    }

    static KeyItem ItemOf(string id) => FindObjectsByType<KeyItem>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(k => k.keyId == id && k.gameObject.activeInHierarchy);
    static bool OnlyStage(int s) => G.stageRoots.Select((r, i) => r.activeSelf == (i == s)).All(x => x);
    IEnumerator BoltShut(HingeDoor d)
    {
        d.SetOpen(0f);
        yield return null;
        var b = FindObjectsByType<DoorBolt>(FindObjectsSortMode.None).First(x => x.door == d);
        if (!d.bolted) b.Tap();
    }

    // the whole chain through the three parts, the listener switched off
    IEnumerator BotRun()
    {
        float t0 = Time.time;
        It.SendAway(9999f, 0);
        It.enabled = false;
        P.Teleport(LevelLayout.ReceptionSpawn, 90f, 0f);
        yield return null;
        // ---- part one: the two halves of the code, the safe, the basement key
        var d214 = Door("Door 214");
        yield return Walk(W(47f, 11f, 1));
        d214.Tap();                                      // shove the sticking lock
        yield return new WaitForSeconds(0.8f);
        Check(d214.Openness > 0.3f, "bot: shoved Room 214's sticking door open");
        yield return Walk(W(44f, 12.4f, 1));
        var dresser = FindAnyObjectByType<Dresser>();
        for (int i = 0; i < 8 && dresser.pulled < dresser.needed; i++) { dresser.PullStep(); yield return new WaitForSeconds(0.1f); }
        var codeA = ItemOf("code_a");
        Check(codeA && codeA.reachable, "bot: dresser pulled out, the first half of the safe code is reachable");
        codeA.Tap();
        yield return Walk(W(9.2f, 28.6f, 1));
        var codeB = ItemOf("code_b");
        if (codeB) codeB.Tap();
        Check(G.HasKey("code_a") && G.HasKey("code_b") && G.Stage == 2, $"bot: through 207 into 209, the second half from the bag (objective: {G.Objective})");
        yield return Walk(W(8.6f, 23.4f, 0));
        var safe = FindAnyObjectByType<Safe>();
        safe.Tap();
        var key = ItemOf("basement");
        Check(safe.Open && key && key.reachable, "bot: back down in the manager's office, the code opens the safe");
        key.Tap();
        Check(G.HasKey("basement") && G.Stage == 3, $"bot: took the basement key (objective: {G.Objective})");
        yield return Walk(W(19.2f, 39.2f, 0));
        var bd = Door("Basement Door");
        bd.Tap();
        Check(!bd.locked && It.BasementOpen, "bot: the key opens the basement door");
        bd.SetOpen(90f * bd.hingeSign);
        yield return new WaitForSeconds(0.3f);
        P.Teleport(W(19.0f, 41.6f, 0), 90f, 0f);
        float tw = Time.time;
        while (G.Level != 2 && Time.time - tw < 4f) yield return null;
        yield return Frames(3);
        Check(G.Level == 2 && LevelLayout.FloorOf(P.transform.position) == -1 && OnlyStage(2), $"bot: down the basement stair into part two; only the basement is switched on ({LevelLayout.RoomAt(P.transform.position)?.name})");
        // ---- part two: the fuse, the breaker, the lift key, the lift
        yield return Walk(W(46.6f, 17.1f, -1));
        var fuse = ItemOf("fuse");
        if (fuse) fuse.Tap();
        Check(G.HasKey("fuse") && G.Stage == 5, $"bot: the spare main fuse from the end cage (objective: {G.Objective})");
        yield return Walk(W(5.2f, 26f, -1));
        var brk = Breaker.Instance;
        for (float t = 0; t < 1.5f && !brk.On; t += Time.deltaTime) { brk.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        Check(brk.On && G.PowerIsOn && G.Stage == 6, "bot: fitted the fuse and threw the main breaker (power on)");
        yield return new WaitForSeconds(2.5f);
        int lit = Lamp.All.Count(l => l.powered && l.transform.position.y < -0.5f && l.Level > 0.05f);
        Info($"basement lamps lit after the breaker: {lit}");
        yield return Walk(W(18f, 31f, -1));
        Door("Door Recorder Room").Tap();
        yield return new WaitForSeconds(1.2f);
        yield return Walk(W(17.4f, 24.3f, -1));
        var lk = ItemOf("liftkey");
        if (lk) lk.Tap();
        Check(G.HasKey("liftkey") && G.Stage == 7, $"bot: the lift key from the recorder room (objective: {G.Objective})");
        yield return Walk(W(49.5f, 42.4f, -1));
        var panel = G.liftPanel;
        panel.callTime = 4f;                              // the hold-out itself is checked separately
        for (float t = 0; t < 1.2f && !panel.Calling; t += Time.deltaTime) { panel.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        bool calling = panel.Calling && It.Finale;
        tw = Time.time;
        while (!panel.Arrived && Time.time - tw < 7f) yield return null;
        Check(calling && panel.Arrived && G.Stage == 8, $"bot: called the lift (the listener goes into its hunting state), the car arrives (objective: {G.Objective})");
        P.Teleport(LevelLayout.LiftCarSpot, 180f, 0f);
        tw = Time.time;
        while (G.Level != 3 && Time.time - tw < 14f) yield return null;
        while (G.Transitioning && Time.time - tw < 16f) yield return null;
        yield return Frames(3);
        Check(G.Level == 3 && LevelLayout.FloorOf(P.transform.position) == 3 && OnlyStage(3), $"bot: rode the lift up to part three, the penthouse; only the roof stage is switched on ({LevelLayout.RoomAt(P.transform.position)?.name})");
        // ---- part three: the keycard, the roof door, the PA horn, the master tape
        yield return Walk(W(30f, 30f, 3));
        yield return Walk(W(37f, 26f, 3));
        yield return Walk(W(38.6f, 16.9f, 3));
        var card = ItemOf("roofcard");
        if (card) card.Tap();
        Check(G.HasKey("roofcard") && G.Stage == 10, $"bot: through Suite 901 into the owner's rooms, the keycard from the study (objective: {G.Objective})");
        yield return Walk(W(38f, 37.0f, 3));
        var rdoor = Door("Roof Access Door");
        rdoor.Tap();
        Check(!rdoor.locked && G.Stage == 11, $"bot: the keycard opens the roof door (objective: {G.Objective})");
        rdoor.SetOpen(90f * rdoor.hingeSign);
        yield return Walk(W(38f, 36.2f, 4));
        Check(LevelLayout.FloorOf(P.transform.position) == 4, $"bot: up the roof stair onto the roof ({LevelLayout.RoomAt(P.transform.position)?.name})");
        var pa = NoiseDevice.All.FirstOrDefault(d => d.label == "PA horn");
        if (pa) pa.Tap();
        Check(pa && pa.Playing, "bot: switched on the PA horn in the far corner");
        yield return Walk(W(55f, 40.4f, 4));
        var ad = Door("Door Antenna Room");
        yield return BoltShut(ad);
        Check(ad.bolted, "bot: bolted the antenna room door");
        var vcr = G.securityVcr;
        for (float t = 0; t < 19.5f && G.Ending == ""; t += Time.deltaTime) { vcr.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        tw = Time.time;
        while (G.State != Game.Phase.Ending && Time.time - tw < 5f) yield return null;
        Check(G.Ending == "OUT", $"bot: held eject for 18 s, ending {G.Ending} (whole run {Time.time - t0:0} s with teleport-walking)");
        // the end screen: it must come up with its buttons, the cursor free (it once looked stuck)
        tw = Time.time;
        while (!(Hud.Instance && Hud.Instance.EndShown) && Time.time - tw < 20f) yield return null;
        yield return Frames(3);
        var buttons = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None);
        Check(Hud.Instance && Hud.Instance.EndShown && buttons.Length >= 3 && Cursor.lockState == CursorLockMode.None && UnityEngine.EventSystems.EventSystem.current != null,
            $"the end screen comes up {Time.time - tw:0} s after the tape is out: {buttons.Length} buttons (play again, start over, quit; also Enter / R / Esc), cursor {Cursor.lockState}");
    }

    // fresh scene: the two hold-outs. Each is checked twice: without a lure it gets you, with the right lure you make it.
    IEnumerator Part2()
    {
        G.EnterHotel();
        yield return Frames(5);
        // ================= part two: the lift call
        G.EnterLevel(2);
        yield return Frames(5);
        // hiding through a hold-out, a player keeps the light off (it would see it): these checks are about hearing
        if (CameraLight.Instance) CameraLight.Instance.SetOn(false);
        Check(OnlyStage(2) && G.Stage == 4 && G.Objective.Contains("fuse"), $"part two starts at the bottom of the basement stair, only the basement switched on (objective: {G.Objective})");
        Check(FindObjectsByType<FloorPlan>(FindObjectsSortMode.None).Any(pl => pl.floor == -1) && FindObjectsByType<Plaque>(FindObjectsSortMode.None).Any(pl => pl.text == "PUMP ROOM"), "wayfinding in part two: a fire plan of the basement, and door plates (PUMP ROOM among them)");
        yield return HidesAndLures("part two", 4, 4);
        yield return UsableFromFloor("part two", 2);
        // masking: a running generator covers noise near it
        var gen = NoiseDevice.All.First(d => d.label == "generator");
        float before = NoiseDevice.MaskAt(gen.transform.position + Vector3.right * 2f);
        gen.TurnOn(false);
        float during = NoiseDevice.MaskAt(gen.transform.position + Vector3.right * 2f);
        gen.TurnOff();
        Check(before < 0.1f && during >= 15f, $"masking: the running generator covers noise 2 m away by {during:0} dB (0 when off)");
        var brk = Breaker.Instance;
        Check(brk && brk.Prompt.Contains("fuse") && brk.HoldProgress < 0f, $"the breaker can't be thrown without the fuse (\"{(brk ? brk.Prompt : "none")}\")");
        G.GiveKey("fuse"); G.GiveKey("liftkey");
        brk.Throw();
        var panel = G.liftPanel;
        var pumpDoors = new[] { Door("Door Pump"), Door("Door Boiler Pump") };
        // --- without a lure
        P.Teleport(W(51f, 40f, -1), 0f, 0f);
        foreach (var d in pumpDoors) yield return BoltShut(d);
        It.HoldUntil = 0f; It.CalmUntil = 0f;
        It.Place(W(30f, 32f, -1), 90f);
        yield return Frames(5);
        int c0 = G.Catches;
        panel.Call();
        float t0 = Time.time;
        bool brokeIn = false;
        while (Time.time - t0 < 33f && G.Catches == c0 && !panel.Arrived) { brokeIn |= pumpDoors.Any(d => !d.bolted); yield return null; }
        float took = Time.time - t0;
        if (G.Catches == c0) Dump("lift call without a lure");
        Check(G.Catches > c0 && !panel.Arrived, $"lift call without a lure: it hears the bell, {(brokeIn ? "breaks a bolt" : "gets in")} and catches you before the lift comes ({took:0} s of 30)");
        yield return new WaitForSeconds(1.5f);
        // --- with the generator running first
        It.HoldUntil = 0f; It.CalmUntil = 0f;
        panel.ResetLift();
        It.Place(W(40f, 10f, -1), 90f);
        yield return Frames(3);
        gen.TurnOn(true);
        yield return new WaitForSeconds(3f);
        P.Teleport(W(51f, 40f, -1), 0f, 0f);
        foreach (var d in pumpDoors) yield return BoltShut(d);
        c0 = G.Catches;
        panel.Call();
        t0 = Time.time;
        while (Time.time - t0 < 33f && G.Catches == c0 && !panel.Arrived) yield return null;
        if (!panel.Arrived) Dump("lift call with the generator");
        Check(panel.Arrived && G.Catches == c0, $"lift call with the generator running: it stands over the generator, the lift comes in {Time.time - t0:0} s and you are not caught ({It.state}, {Vector3.Distance(It.transform.position, P.transform.position):0} m away)");
        yield return new WaitForSeconds(1f);

        // ================= part three: the master tape
        G.EnterLevel(3);
        yield return Frames(5);
        Check(OnlyStage(3) && G.Stage == 9 && LevelLayout.RoomAt(P.transform.position)?.name == "PH Lift Lobby", $"part three starts out of the lift on the penthouse floor, only the roof stage switched on (objective: {G.Objective})");
        var plans3 = FindObjectsByType<FloorPlan>(FindObjectsSortMode.None);
        Check(plans3.Any(pl => pl.floor == 3) && plans3.Any(pl => pl.floor == 4) && FindObjectsByType<Plaque>(FindObjectsSortMode.None).Any(pl => pl.text == "901"), "wayfinding in part three: fire plans of the penthouse and the roof, door plates (901 among them)");
        yield return HidesAndLures("part three", 6, 4);
        yield return UsableFromFloor("part three", 3);
        var rdoor = Door("Roof Access Door");
        rdoor.Tap();
        Check(rdoor.locked, "the roof door stays locked without the owner's keycard");
        G.GiveKey("roofcard");
        // with the door unlocked you can walk from the penthouse corridor up the roof stair onto the roof
        rdoor.Tap();
        yield return Frames(5);
        var roofPath = new NavMeshPath();
        bool roofOk = NavMesh.SamplePosition(W(38f, 36.5f, 3), out var rpa, 1f, NavMesh.AllAreas) && NavMesh.SamplePosition(W(38f, 36.2f, 4), out var rpb, 1f, NavMesh.AllAreas)
                      && NavMesh.CalculatePath(rpa.position, rpb.position, NavMesh.AllAreas, roofPath) && roofPath.status == NavMeshPathStatus.PathComplete;
        Check(!rdoor.locked && roofOk, $"the keycard opens the roof door, and the way up the roof stair onto the roof is walkable ({roofPath.status})");
        var vcr = G.securityVcr;
        var ad = Door("Door Antenna Room");
        // --- without a lure
        P.Teleport(W(55f, 40.6f, 4), 90f, 0f);
        yield return BoltShut(ad);
        It.HoldUntil = 0f; It.CalmUntil = 0f;
        It.Place(W(40f, 34f, 4), 90f);
        yield return Frames(5);
        c0 = G.Catches;
        t0 = Time.time;
        float most = 0f;
        while (Time.time - t0 < 20f && G.Catches == c0 && G.Ending == "") { vcr.Hold(Time.deltaTime, Vector2.zero); most = Mathf.Max(most, vcr.progress); yield return null; }
        took = Time.time - t0;
        if (G.Catches == c0) Dump("master tape without a lure");
        Check(G.Catches > c0 && G.Ending == "", $"master tape without a lure: it hears the grinding, breaks the bolt and catches you before the tape is out ({took:0} s, tape {most / vcr.ejectTime * 100f:0}% out)");
        yield return new WaitForSeconds(1.5f);
        if (G.Ending != "") { Check(false, "part three tests stopped: the tape came out without a catch"); yield break; }
        // let go and the tape winds back
        vcr.progress = 6f;
        yield return new WaitForSeconds(1f);
        Check(vcr.progress < 5.2f, $"let go of eject and the tape winds back in (6.0 -> {vcr.progress:0.0} s in 1 s)");
        vcr.progress = 0f;
        // --- with the PA horn
        It.HoldUntil = 0f; It.CalmUntil = 0f;
        It.Place(W(40f, 30f, 4), 90f);
        var pa = NoiseDevice.All.First(d => d.label == "PA horn");
        pa.TurnOn(true);
        yield return new WaitForSeconds(4f);
        P.Teleport(W(55f, 40.6f, 4), 90f, 0f);
        yield return BoltShut(ad);
        c0 = G.Catches;
        t0 = Time.time;
        while (Time.time - t0 < 21f && G.Catches == c0 && G.Ending == "") { vcr.Hold(Time.deltaTime, Vector2.zero); yield return null; }
        if (repeatMode || G.Ending != "OUT") Dump("master tape with the PA horn");
        float tw = Time.time;
        while (G.State != Game.Phase.Ending && Time.time - tw < 5f) yield return null;
        Check(G.Ending == "OUT" && G.Catches == c0, $"master tape with the PA horn blaring: it stands under the horn, the tape comes out (ending {G.Ending}, catches {G.Catches})");
        yield return new WaitForSeconds(2f);
    }

    IEnumerator Part3()
    {
        // fresh scene: three catches = erased
        G.EnterHotel();
        yield return Frames(5);
        for (int i = 0; i < 3; i++)
        {
            P.Teleport(W(30f, 25f, 1), 90f, 0f);
            yield return Frames(2);
            It.HoldUntil = 0f;
            It.Place(W(30.5f, 25f, 1), 270f);
            It.GoTo(W(30f, 25f, 1), true);
            float t0 = Time.time;
            int before = G.Catches;
            while (G.Catches == before && Time.time - t0 < 3f) yield return null;
            yield return new WaitForSeconds(1.2f);
        }
        float tw = Time.time;
        while (G.State != Game.Phase.Ending && Time.time - tw < 5f) yield return null;
        Check(G.Ending == "ERASED", $"three catches: ending {G.Ending}");
        yield return new WaitForSeconds(2f);
    }
}
