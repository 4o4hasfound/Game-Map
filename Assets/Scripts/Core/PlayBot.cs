using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// A playthrough by a bot that walks the way a player does: TheTape.exe -playthrough [-playthroughOut file]
// It drives the real movement (CharacterController, collisions, stairs, doors), aims at each thing it uses from
// where it stands (in reach and in sight, as the Interactor needs), and goes through the whole chain: the bedroom,
// part one, the basement stair, part two, the lift, part three, the tape out, the end screen. The listener is kept
// out of the way: this run is about whether the game can be walked and played through, not about evading it (the
// autotest checks the hold-outs with it). It reports every stuck spot (and gets past it), anything out of reach,
// frame hitches with where they happened, exceptions, and how long each step and part took.
public class PlayBot : MonoBehaviour
{
    public static bool Requested => System.Environment.GetCommandLineArgs().Contains("-playthrough");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Requested || FindAnyObjectByType<PlayBot>()) return;
        var go = new GameObject("PlayBot");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayBot>();
    }

    readonly List<string> lines = new List<string>();
    readonly List<(float ms, string where, float t)> frames = new List<(float, string, float)>();
    readonly List<string> errors = new List<string>();
    int stuck, warnings, failures;
    float t0;
    bool recording;
    static Game G => Game.Instance;
    static PlayerMotor P => G.player;

    void Log(string s) { lines.Add($"{Time.time - t0,7:0.0}s  {s}"); Debug.Log("PLAYBOT " + s); }
    void Warn(string s) { warnings++; Log("WARN  " + s); }
    void Fail(string s) { failures++; Log("FAIL  " + s); }

    void OnEnable() { Application.logMessageReceived += OnLog; }
    void OnDisable() { Application.logMessageReceived -= OnLog; }
    void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) errors.Add(msg + " | " + (stack ?? "").Split('\n').FirstOrDefault());
    }

    void Update()
    {
        if (!recording || !G) return;
        frames.Add((Time.unscaledDeltaTime * 1000f, LevelLayout.RoomAt(G.PlayerFeet)?.name ?? LevelLayout.FloorOf(G.PlayerFeet).ToString(), Time.time - t0));
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    static float YawTo(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    static string Here() => LevelLayout.RoomAt(G.PlayerFeet)?.name ?? "?";

    IEnumerator Start()
    {
        yield return null; yield return null; yield return null;
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-playthroughOut");
        string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.dataPath, "../playthrough.txt");
        t0 = Time.time;
        lines.Add("THE TAPE playthrough bot  " + System.DateTime.Now);
        lines.Add($"GPU {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}");
        PlayerMotor.BotDriven = true;
        recording = true;
        // -playthroughFrom 2 or 3 starts at that part (for quick reruns)
        int from = 1;
        int fi = System.Array.IndexOf(args, "-playthroughFrom");
        if (fi >= 0 && fi + 1 < args.Length) int.TryParse(args[fi + 1], out from);
        float tPart = Time.time;
        if (from <= 1) { yield return Prologue(); yield return PartOne(); Log($"== part one took {Time.time - tPart:0} s"); tPart = Time.time; }
        else { G.GoConsent(); G.ConsentDone(false, false); yield return new WaitForSeconds(0.5f); G.EnterHotel(); G.EnterLevel(from); yield return new WaitForSeconds(1f); KeepItAway(); Log($"started at part {from}"); }
        if (from <= 2) { yield return PartTwo(); Log($"== part two took {Time.time - tPart:0} s"); tPart = Time.time; }
        yield return PartThree(); Log($"== part three took {Time.time - tPart:0} s");
        recording = false;
        PlayerMotor.BotDriven = false;
        Report();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllLines(outPath, lines);
        yield return new WaitForSeconds(1f);
        Application.Quit();
    }

    void Report()
    {
        var ms = frames.Select(f => f.ms).OrderBy(x => x).ToList();
        if (ms.Count > 0)
        {
            float p50 = ms[ms.Count / 2], p95 = ms[(int)(ms.Count * 0.95f)], p99 = ms[(int)(ms.Count * 0.99f)], max = ms[ms.Count - 1];
            var hitches = frames.Where(f => f.ms > 50f).ToList();
            lines.Add($"FRAMES  {ms.Count} frames: p50 {p50:0.0} ms, p95 {p95:0.0} ms, p99 {p99:0.0} ms, max {max:0} ms; {hitches.Count} hitches over 50 ms");
            foreach (var h in hitches.OrderByDescending(h => h.ms).Take(12)) lines.Add($"   hitch {h.ms:0} ms at {h.t:0.0} s in {h.where}");
        }
        lines.Add($"DETOURS {detours} times round an open door leaf");
        lines.Add($"ERRORS  {errors.Count}");
        foreach (var e in errors.Distinct().Take(15)) lines.Add("   " + e);
        lines.Add($"RESULT  {(failures == 0 && stuck == 0 && errors.Count == 0 && G.Ending == "OUT" ? "SMOOTH" : "PROBLEMS")}   ending {G.Ending}, {failures} failures, {stuck} stuck spots, {warnings} warnings, {errors.Count} errors, whole run {Time.time - t0:0} s");
    }

    // ------------------------------------------------------------------ walking like a player
    float lastPartialNote = -100f;
    // The path is planned again from where the player is every 0.4 s, and it walks towards the first corner that is not
    // already reached (a corner counts only on its own level: switchback stairs stack corners above each other).
    // Two watchdogs: not moving for 2.5 s, or no progress along the path for 15 s (circling, sliding along a wall).
    IEnumerator WalkTo(Vector3 target, string what, float arrive = 0.6f, float timeout = 120f, System.Func<bool> until = null)
    {
        float start = Time.time;
        var path = new NavMeshPath();
        Vector3[] c = null;
        int attempts = 0;
        float nextPlan = 0f;
        Vector3 lastPos = P.transform.position;
        float lastMove = Time.time, best = float.MaxValue, bestAt = Time.time;
        while (Time.time - start < timeout)
        {
            if (until != null && until()) { PlayerMotor.BotMove = Vector2.zero; yield break; }
            if (G.Transitioning || P.Locked) { PlayerMotor.BotMove = Vector2.zero; lastMove = bestAt = Time.time; yield return null; continue; }
            var pos = P.transform.position;
            if (Flat(pos - target).magnitude < arrive && Mathf.Abs(pos.y - target.y) < 1.6f) { PlayerMotor.BotMove = Vector2.zero; yield break; }
            if (Time.time >= nextPlan || c == null)
            {
                nextPlan = Time.time + 0.4f;
                bool direct = !(NavMesh.SamplePosition(pos, out var a, 1.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(target, out var b, 1.8f, NavMesh.AllAreas)
                           && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid);
                c = direct ? new[] { pos, target } : path.corners.Concat(new[] { target }).ToArray();
                if (!direct && path.status == NavMeshPathStatus.PathPartial && Time.time - lastPartialNote > 20f)
                {
                    lastPartialNote = Time.time;
                    var e = path.corners.Last();
                    Log($"note: the path to {what} is partial: from ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}) it ends at ({e.x:0.0}, {e.y:0.0}, {e.z:0.0}), {Vector3.Distance(e, target):0.0} m from the target; corners " + string.Join(" ", path.corners.Select(q => $"({q.x:0.#},{q.y:0.#},{q.z:0.#})")));
                }
            }
            int corner = 1;
            while (corner < c.Length - 1 && Flat(pos - c[corner]).magnitude < 0.4f && Mathf.Abs(pos.y - c[corner].y) < 1.0f) corner++;
            // a target off the navmesh (inside the lift car): once nearer to it than the path's last corner is, go straight
            if (corner == c.Length - 2 && Flat(pos - target).magnitude <= Flat(c[corner] - target).magnitude + 0.05f) corner = c.Length - 1;
            // round the free end of an open door leaf it walked into (doors are not in the navmesh)
            if (detour.HasValue && (Time.time > detourUntil || Flat(pos - detour.Value).magnitude < 0.35f)) detour = null;
            var aim = detour ?? c[corner];
            var d = Flat(aim - pos);
            if (d.sqrMagnitude > 0.0004f) P.SetLook(YawTo(d), 0f);
            PlayerMotor.BotMove = Time.time < doorWaitUntil ? new Vector2(0f, -0.4f) : new Vector2(0f, 1f);
            OpenDoorAhead();

            // how far there is still to walk, along the path
            float left = Vector3.Distance(pos, c[corner]);
            for (int i = corner; i + 1 < c.Length; i++) left += Vector3.Distance(c[i], c[i + 1]);
            if (left < best - 0.5f) { best = left; bestAt = Time.time; attempts = 0; }
            if (Flat(pos - lastPos).magnitude > 0.3f) { lastPos = pos; lastMove = Time.time; }
            bool still = Time.time - lastMove > 2.5f, circling = Time.time - bestAt > 15f;
            if (still || circling)
            {
                attempts++;
                lastMove = Time.time;
                if (circling) { best = left; bestAt = Time.time; }
                if (attempts == 1) OpenDoorsNear(2.2f);
                if (attempts <= 2)
                {
                    // step aside and back off a little, as a player would, then carry on
                    float side = (attempts == 1 ? 1f : -1f) * (Random.value < 0.5f ? -1f : 1f);
                    for (float t = 0; t < 0.7f; t += Time.deltaTime) { PlayerMotor.BotMove = new Vector2(side, -0.3f); yield return null; }
                    nextPlan = 0f;
                }
                else
                {
                    stuck++;
                    Log($"STUCK on the way to {what}{(circling ? " (no progress for 15 s)" : "")}: at ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}) in {Here()}, heading for ({aim.x:0.0}, {aim.y:0.0}, {aim.z:0.0}), {left:0.0} m of path left; moved on");
                    var next = c[Mathf.Min(corner + 1, c.Length - 1)];
                    P.Teleport(Vector3.Lerp(aim, next, 0.3f) + Vector3.up * 0.05f, P.Yaw, 0f);
                    attempts = 0; nextPlan = 0f;
                    lastPos = P.transform.position;
                }
            }
            yield return null;
        }
        PlayerMotor.BotMove = Vector2.zero;
        var sp = P.transform.position;
        Fail($"could not reach {what} in {timeout:0} s (stopped at ({sp.x:0.0}, {sp.y:0.0}, {sp.z:0.0}) in {Here()}); moved there");
        P.Teleport(target, P.Yaw, 0f);
        yield return null;
    }

    static readonly int DoorMask = 1 << Layers.Door;
    float nextDoorTry, doorWaitUntil, detourUntil;
    Vector3? detour;
    int detours;
    bool doorsOff;
    void OpenDoorAhead()
    {
        var cam = P.transform.position + Vector3.up * 1.1f;
        if (!Physics.Raycast(cam, P.transform.forward, out var hit, 1.1f, DoorMask, QueryTriggerInteraction.Ignore)) return;
        var door = hit.collider.GetComponentInParent<HingeDoor>();
        if (!door) return;
        if (door.Openness < 0.6f)
        {
            if (!doorsOff && Time.time >= nextDoorTry && !door.locked && !door.bolted) { door.Tap(); nextDoorTry = Time.time + 1.2f; doorWaitUntil = Time.time + 0.7f; }
        }
        else if (!detour.HasValue && hit.distance < 0.8f && Vector3.Dot(hit.normal, -P.transform.forward) > 0.5f)
        {
            // an open leaf across its way, face on: walk past its free end, as a player steps round an open door
            var hinge = Flat(door.transform.position);
            var along = Flat(door.HandlePos) - hinge;
            if (along.magnitude < 0.1f) return;
            var pt = hinge + along * 2f + along.normalized * 0.55f;
            detour = new Vector3(pt.x, P.transform.position.y, pt.z);
            detourUntil = Time.time + 3f;
            detours++;
        }
    }
    void OpenDoorsNear(float r)
    {
        foreach (var d in HingeDoor.All)
            if (d && !d.locked && !d.bolted && d.Openness < 0.6f && Vector3.Distance(d.HandlePos, P.transform.position + Vector3.up) < r) d.Tap();
    }

    // use something the way the Interactor would: stand in reach, aim at it, it must be in sight
    static readonly int SeeMask = ~((1 << Layers.Player) | (1 << Layers.Avatar) | (1 << Layers.Monster));
    static Vector3 AimPoint(Interactable it) => it is HingeDoor hd ? hd.HandlePos : it.transform.position;
    static bool Sees(Vector3 eye, Interactable it, out string blocker)
    {
        blocker = "";
        var d = AimPoint(it) - eye;
        if (!Physics.Raycast(eye, d.normalized, out var hit, d.magnitude + 0.3f, SeeMask, QueryTriggerInteraction.Collide)) return true;
        var hi = hit.collider.GetComponentInParent<Interactable>();
        bool ok = hi == it || hit.collider.transform.IsChildOf(it.transform) || it.transform.IsChildOf(hit.collider.transform);
        if (!ok) blocker = hit.collider.name;
        return ok;
    }

    // a place to stand: on the walkable floor, within reach, with a clear line of sight to it, reachable from here
    Vector3 StandPoint(Interactable it)
    {
        var target = AimPoint(it);
        var path = new NavMeshPath();
        bool haveStart = NavMesh.SamplePosition(P.transform.position, out var start, 1.5f, NavMesh.AllAreas);
        Vector3 best = target; float bestScore = float.MaxValue;
        var targetRoom = LevelLayout.RoomAt(target + Vector3.down * 0.3f);
        for (int k = 0; k < 16; k++)
        {
            float a = k * 22.5f * Mathf.Deg2Rad;
            foreach (float r in new[] { 0.7f, 1.0f, 1.4f, 1.8f })
            {
                var p = target + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (!NavMesh.SamplePosition(new Vector3(p.x, target.y - 0.6f, p.z), out var h, 1.8f, NavMesh.AllAreas)) continue;
                if (Flat(h.position - p).magnitude > 0.35f) continue;
                var eye = h.position + Vector3.up * 1.55f;
                if (Vector3.Distance(eye, target) > 2.0f) continue;
                if (!Sees(eye, it, out _)) continue;
                if (haveStart && !(NavMesh.CalculatePath(start.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
                float score = r + (LevelLayout.RoomAt(h.position + Vector3.up * 0.5f) == targetRoom ? 0f : 3f);
                if (score < bestScore) { bestScore = score; best = h.position; }
            }
        }
        return best;
    }

    IEnumerator Use(Interactable it, string what, float holdFor = 0f, float standWithin = 1.2f)
    {
        if (!it) { Fail(what + ": not found"); yield break; }
        float t = Time.time;
        var stand = StandPoint(it);
        if (stand == AimPoint(it)) yield return WalkTo(AimPoint(it), what, standWithin);
        else yield return WalkTo(stand, what, 0.45f);
        var cam = G.PlayerCamera.transform;
        var dir = AimPoint(it) - cam.position;
        P.SetLook(YawTo(dir), -Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
        yield return null; yield return null;
        dir = AimPoint(it) - cam.position;
        var reach = cam.GetComponent<Interactor>() ? cam.GetComponent<Interactor>().reach : 2.1f;
        bool inReach = dir.magnitude <= reach + 0.2f;
        bool seen = Sees(cam.position, it, out string blocker);
        if (!inReach || !seen) Warn($"{what}: a player couldn't use it from here ({dir.magnitude:0.0} m away{(seen ? "" : ", blocked by " + blocker)}) in {Here()}");
        if (holdFor > 0f) { for (float h = 0; h < holdFor; h += Time.deltaTime) { it.Hold(Time.deltaTime, Vector2.zero); yield return null; } it.Release(); }
        else it.Tap();
        yield return null;
        // a paper (a note, a label, a plan) holds the player still until it is put away, as a reader would
        if (G.PaperOpen) { yield return new WaitForSeconds(0.6f); G.ClosePaper(); }
        Log($"used {what} ({Time.time - t:0} s to get there and use it)");
    }

    T Find<T>(System.Func<T, bool> where = null) where T : Object => FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault(x => where == null || where(x));
    static HingeDoor Door(string n) => HingeDoor.All.FirstOrDefault(d => d.portalName == n);

    void KeepItAway()
    {
        var it = G.It;
        if (!it) return;
        if (it.state != Listener.State.Away) it.SendAway(99999f, 0);
        it.enabled = false;
    }

    // ------------------------------------------------------------------ the run
    IEnumerator Prologue()
    {
        G.GoConsent();
        G.ConsentDone(false, false);
        yield return new WaitForSeconds(1f);
        Log("prologue: the bedroom");
        // out of bed (by itself in a bot run), the doorbell and the knocking: to the front door, then the envelope
        float wb = Time.time;
        while (G.PrologueBeat != Game.Beat.Doorbell && Time.time - wb < 10f) yield return null;
        yield return WalkTo(LevelLayout.FrontDoorInside, "the front door (the knocking)", 0.8f);
        float w0 = Time.time;
        TapeItem tape = null;
        while (!(tape = Find<TapeItem>()) && Time.time - w0 < 15f) yield return null;
        yield return Use(tape, "the envelope by the front door");
        yield return Use(Find<BedroomVcr>(), "the VCR under the TV");
        float w = Time.time;
        while (G.State != Game.Phase.Hotel && Time.time - w < 60f) yield return null;
        if (G.State != Game.Phase.Hotel) { Fail("never pulled into the tape"); G.EnterHotel(); }
        else Log($"pulled into the tape after {Time.time - w:0} s");
        yield return new WaitForSeconds(1f);
        KeepItAway();
    }

    IEnumerator PartOne()
    {
        Log("== part one: the hotel");
        var letter = Find<Readable>(r => r.isLetter);
        yield return Use(letter, "the letter");
        G.ClosePaper();
        yield return Use(Find<FloorPlan>(f => f.floor == 0 && LevelLayout.RoomAt(f.transform.position)?.name == "Front Office"), "the ground floor fire plan");
        // up to 214 by the grand stair
        yield return WalkTo(LevelLayout.W(47f, 11f, 1), "Room 214's door");
        var d214 = Door("Door 214");
        if (d214) { P.SetLook(270f, 0f); d214.Tap(); yield return new WaitForSeconds(1f); }
        var dresser = Find<Dresser>();
        if (dresser)
        {
            yield return WalkTo(dresser.transform.position + Vector3.back * 1.0f, "the dresser", 0.6f);
            for (int i = 0; i < 10 && dresser.pulled < dresser.needed; i++) { dresser.PullStep(); yield return new WaitForSeconds(0.25f); }
            Log("pulled the dresser out");
        }
        yield return Use(Find<KeyItem>(k => k.keyId == "code_a"), "half of the safe code (214)");
        yield return Use(Find<KeyItem>(k => k.keyId == "code_b"), "the other half (209, through 207)");
        if (!(G.HasKey("code_a") && G.HasKey("code_b"))) Fail("didn't get both halves of the code");
        var safe = Find<Safe>();
        yield return Use(safe, "the manager's safe");
        if (safe && !safe.Open) Fail("the safe didn't open");
        yield return Use(Find<KeyItem>(k => k.keyId == "basement"), "the basement key");
        if (!G.HasKey("basement")) Fail("no basement key");
        var bd = Door("Basement Door");
        yield return Use(bd, "the basement door (unlock)", 0f, 1.0f);
        if (bd && bd.locked) Fail("the basement door is still locked");
        if (bd && bd.Openness < 0.5f) { bd.Tap(); yield return new WaitForSeconds(1f); }
        yield return WalkTo(LevelLayout.W(19.0f, 41.6f, 0), "the basement stairhead", 0.5f, 60f, () => G.Level >= 2);
        float w = Time.time;
        while (G.Level != 2 && Time.time - w < 6f) yield return null;
        while (G.Transitioning) yield return null;
        if (G.Level != 2) Fail("walking into the stairhead didn't start part two"); else Log($"down the basement stair: part two ({Here()})");
        KeepItAway();
    }

    IEnumerator PartTwo()
    {
        Log("== part two: the basement");
        yield return Use(Find<FloorPlan>(f => f.floor == -1), "the basement fire plan");
        yield return Use(Find<KeyItem>(k => k.keyId == "fuse"), "the spare fuse (storage cages)");
        yield return Use(Breaker.Instance, "the main breaker", 1.5f);
        if (!G.PowerIsOn) Fail("no power after the breaker");
        yield return Use(Find<KeyItem>(k => k.keyId == "liftkey"), "the lift key (recorder room)");
        yield return Use(Find<NoiseDevice>(d => d.label == "generator"), "the generator (the lure)");
        var panel = G.liftPanel;
        yield return Use(panel, "the lift call panel", 1.0f);
        if (!panel || !panel.Calling) Fail("the lift didn't start coming");
        float w = Time.time;
        while (panel && !panel.Arrived && Time.time - w < 40f) yield return null;
        if (panel && panel.Arrived) Log($"the lift arrived after {Time.time - w:0} s"); else Fail("the lift never arrived");
        yield return WalkTo(LevelLayout.LiftCarSpot, "the lift car", 0.7f, 30f, () => G.Level >= 3);
        w = Time.time;
        while ((G.Level != 3 || G.Transitioning) && Time.time - w < 20f) yield return null;
        if (G.Level != 3) Fail("stepping into the lift didn't start part three"); else Log($"the lift ride: part three ({Here()})");
        KeepItAway();
    }

    // which link of the walk up to the roof is missing, if any
    void DiagnoseRoofStair()
    {
        var pts = new (string n, Vector3 p)[] {
            ("corridor", LevelLayout.W(38f, 36f, 3)), ("stair room", LevelLayout.W(38f, 39f, 3)), ("stair foot", LevelLayout.W(35.4f, 41.2f, 3)),
            ("mid-stair", new Vector3(38f, LevelLayout.Penthouse + 1.8f, 41.2f)), ("top landing", LevelLayout.W(41.5f, 41f, 4)),
            ("walkway", LevelLayout.W(38f, 39f, 4)), ("roof deck", LevelLayout.W(38f, 36.2f, 4)) };
        var path = new NavMeshPath();
        for (int k = 0; k + 1 < pts.Length; k++)
        {
            bool sa = NavMesh.SamplePosition(pts[k].p, out var a, 0.8f, NavMesh.AllAreas), sb = NavMesh.SamplePosition(pts[k + 1].p, out var b, 0.8f, NavMesh.AllAreas);
            string st = !sa ? "no navmesh at " + pts[k].n : !sb ? "no navmesh at " + pts[k + 1].n :
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) ? path.status.ToString() : "no path";
            Log($"roof stair link {pts[k].n} -> {pts[k + 1].n}: {st}" + (sa ? $" (from {a.position.y:0.0})" : "") + (sb ? $" (to {b.position.y:0.0})" : ""));
        }
    }

    IEnumerator PartThree()
    {
        Log("== part three: the penthouse and the roof");
        DiagnoseRoofStair();
        yield return Use(Find<FloorPlan>(f => f.floor == 3), "the penthouse fire plan");
        yield return Use(Find<KeyItem>(k => k.keyId == "roofcard"), "the owner's keycard (through the servants' passage)");
        if (!G.HasKey("roofcard")) Fail("no keycard");
        var rd = Door("Roof Access Door");
        yield return Use(rd, "the roof door (keycard)", 0f, 1.0f);
        if (rd && rd.locked) Fail("the roof door is still locked");
        if (rd && rd.Openness < 0.5f) { rd.Tap(); yield return new WaitForSeconds(1f); }
        yield return WalkTo(LevelLayout.W(38f, 36.2f, 4), "the roof (up the stair)", 0.7f);
        if (LevelLayout.FloorOf(G.PlayerFeet) != 4) Fail("not on the roof");
        yield return Use(Find<FloorPlan>(f => f.floor == 4), "the roof fire plan");
        yield return Use(Find<NoiseDevice>(d => d.label == "PA horn"), "the PA horn (the lure)");
        var ad = Door("Door Antenna Room");
        yield return WalkTo(LevelLayout.W(55f, 40.6f, 4), "inside the antenna room", 0.6f);
        if (ad)
        {
            P.SetLook(180f, 0f);
            // shut it the way a player does (a tap swings it closed), then the bolt, without bumping it open again
            if (ad.Openness > 0.06f) { ad.Tap(); yield return new WaitForSeconds(1.2f); }
            if (ad.Openness > 0.06f) Warn($"the antenna room door didn't swing shut ({ad.Openness * 100f:0}% open)");
            var bolt = Find<DoorBolt>(b => b.door == ad);
            doorsOff = true;
            yield return Use(bolt, "the antenna room bolt", 0f, 1.0f);
            doorsOff = false;
            if (!ad.bolted) Warn("the antenna room door didn't bolt");
        }
        yield return WalkTo(LevelLayout.W(56.0f, 41.0f, 4), "the master VCR", 0.4f);
        var vcr = G.securityVcr;
        yield return Use(vcr, "the master VCR (eject)", 19f, 1.6f);
        float w = Time.time;
        while (G.State != Game.Phase.Ending && Time.time - w < 6f) yield return null;
        if (G.Ending != "OUT") Fail($"the tape didn't come out (ending '{G.Ending}', tape {vcr.progress:0.0} of {vcr.ejectTime} s)");
        else Log("the tape is out: ending OUT");
        w = Time.time;
        while (!(Hud.Instance && Hud.Instance.EndShown) && Time.time - w < 25f) yield return null;
        if (Hud.Instance && Hud.Instance.EndShown) Log($"the end screen came up after {Time.time - w:0} s"); else Fail("no end screen");
    }
}
