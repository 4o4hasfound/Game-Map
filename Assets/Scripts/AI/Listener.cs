using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// "It": what is left of everyone the tape has erased. It hunts by sound; its eyes are poor.
// It hears every noise through the same propagation the player's ears use (NoiseBus + AcousticWorld): footsteps by
// surface and gait (crouched: none; walking: a few metres; running: far), doors by how fast they swing, breathing,
// phones, radios, thrown bottles, switches (your camera light's click too), the breaker, the VCR. Your own noises it
// follows step by step: each one it hears moves its target to where you are now.
// It sees you only in obviously strong light (see Exposure): your camera light on, or standing under a bright lamp.
//   Wander     slow patrol of its floor (1.0 m/s, slower than you walk)
//   Investigate goes to the loudest thing it heard (1.45 m/s, still slower than you walk), then searches; when it
//              heard YOU clearly it hurries (2.0 m/s: faster than you walk, slower than you run)
//   Rush       when it hears you close and loud, or sees you (2.7 m/s): run (3.8 m/s for 8 s), then go quiet
//   Attend     a radio, TV, jukebox...: it stands by it, listens, then smashes it
//   Bang       a bolted door: it beats on it, then gives up (in the finale the bolt breaks)
//   Away       in the lift shaft between floors (you hear it climb); this is your time to explore
// The director gives it a calm start, sends it away into the shaft now and then, and brings it to your floor.
public class Listener : MonoBehaviour
{
    public enum State { Scripted, Away, Leaving, Wander, Investigate, Rush, Search, Attend, Bang }
    public static Listener Instance { get; private set; }

    public State state = State.Wander;
    public Figure figure;
    public NavMeshAgent agent;
    public float wanderSpeed = 1.0f, investigateSpeed = 1.45f, rushSpeed = 2.7f;
    public float hearingDb = 20f;           // quietest sound it notices, at its ear
    public float rushDb = 33f;              // a player noise this loud, close by, makes it rush (walking on marble: within about 3 m)
    public float rushRange = 12f;
    public float hurrySpeed = 2.0f;         // after a noise of yours it heard clearly
    public float hurryMargin = 6f;          // "clearly": this many dB over what it can just hear
    public const float FieldOfView = 70f;   // degrees either side of where it faces
    public const float UrgentRange = 8f;    // your noises this close (and heard) always get its attention
    public bool SeesYou { get; private set; }
    public static bool NoCatch;             // tests: it never catches you
    float chaseUntil;                       // it knows where you are (it broke in, or it sees you): straight for you
    public float LastSawAt { get; private set; } = -100f;
    public float catchRange = 0.85f;
    public bool Finale;                     // the lift call (stage 2) and the master tape (stage 3): it hurries, hears more, breaks bolts
    public int FinaleFloor = -1;            // where it comes back out of the shaft during the finale
    public bool BasementOpen;               // the basement is part of its world once you've unlocked the door
    public float CalmUntil;                 // calm start: it ignores everything but loud sounds
    public float HoldUntil;                 // tests / catches: it stays where it is

    public Vector3 Target { get; private set; }
    public string LastHeard { get; private set; } = "";
    public float LastHeardDb { get; private set; }
    public float LastHeardAt { get; private set; } = -100f;
    public bool Visible => state != State.Away;
    public int Floor => LevelLayout.FloorOf(transform.position);
    public int AwayFloor { get; private set; }
    public float StateSince { get; private set; }
    public Object Attending => attending;
    public HingeDoor Banging => banging;
    public readonly List<string> Log = new List<string>();

    float targetDb, stateUntil, nextAway, otherFloorSince = -1f, nextBang, stepAcc, nextBreath, lastPlayerNoiseAt = -100f, nextLook, seenFor;
    bool targetByPlayer, hurry;
    Vector3 lastPlayerNoisePos;
    int patrolIndex, bangs;
    Object attending;                       // the radio / TV / phone it is going to
    bool AttendActive => attending is NoiseDevice nd ? nd && nd.Playing : attending is Phone ph && ph && ph.Ringing;
    Vector3 AttendPos => attending is Component c && c ? c.transform.position : transform.position;
    HingeDoor banging;
    readonly List<Renderer> renderers = new List<Renderer>();
    bool scriptedMove;

    void Awake() { Instance = this; }

    void Start()
    {
        NoiseBus.Emitted += OnNoise;
        GetComponentsInChildren(true, renderers);
        nextAway = Time.time + Random.Range(80f, 110f);
    }

    void OnDestroy() { NoiseBus.Emitted -= OnNoise; if (Instance == this) Instance = null; }

    void SetState(State s, float duration = 0f)
    {
        if (state != s) { StateSince = Time.time; Note(s.ToString()); }
        state = s;
        stateUntil = duration > 0f ? Time.time + duration : 0f;
    }

    void Note(string what)
    {
        Log.Add($"{Time.time:0.0} {what}");
        if (Log.Count > 200) Log.RemoveAt(0);
    }

    // ------------------------------------------------------------------ hearing
    public float Ear => transform.position.y + 1.7f;

    public float HeardLevel(NoiseBus.Noise n)
    {
        var ear = transform.position + Vector3.up * 1.7f;
        float lvl = NoiseBus.Heard(n, ear);
        // a loud machine running near the noise covers it, and one running near it deafens it a little
        lvl -= NoiseDevice.MaskAt(n.pos, n.src as NoiseDevice);
        lvl -= NoiseDevice.MaskAt(ear, n.src as NoiseDevice) * 0.6f;
        return lvl;
    }

    void OnNoise(NoiseBus.Noise n)
    {
        if (!agent || !agent.isOnNavMesh) return;
        if (state == State.Scripted || state == State.Away || state == State.Leaving && !Finale) return;
        if (Time.time < HoldUntil) return;
        if (n.what != null && n.what.StartsWith("it ")) return;            // its own noises
        if (Time.time < chaseUntil) { if (n.byPlayer) { lastPlayerNoiseAt = Time.time; lastPlayerNoisePos = n.pos; } return; }
        float lvl = HeardLevel(n);
        float baseThreshold = hearingDb - (Finale ? 4f : 0f);
        // YOU, close by on its floor, and loud enough to hear: that always gets through. Not the calm start, not a
        // louder lure it is going to, not the radio it stands over. (Before, a ringing phone or the calm start made it
        // ignore you even running round it.)
        float near = Vector3.Distance(transform.position, n.pos);
        bool urgent = n.byPlayer && Mathf.Abs(n.pos.y - transform.position.y) < 2.2f && near < UrgentRange && lvl >= baseThreshold;
        // already standing over this noise (listening to the radio, beating on the door): keep at it
        if (!urgent && (state == State.Attend || state == State.Bang) && (n.src == attending || Vector3.Distance(n.pos, Target) < 4f)) { LastHeardAt = Time.time; return; }
        if (state == State.Bang && banging && (n.src == (Object)banging || Vector3.Distance(n.pos, banging.HandlePos) < 9f)) { LastHeardAt = Time.time; return; }   // you, behind the door it is beating on
        float threshold = urgent ? baseThreshold : baseThreshold + (Time.time < CalmUntil ? 22f : 0f);
        if (lvl < threshold)
        {
            // a very loud noise on the other floor of this stage (the storm siren, the organ): it climbs the shaft to it
            if (n.db >= 75f && (state == State.Wander || state == State.Search) && Time.time >= CalmUntil)
            {
                int nf = LevelLayout.FloorOf(n.pos);
                if (nf != Floor && nf != 2 && LevelLayout.StageOfFloor(nf) == LevelLayout.StageOfFloor(Floor) && (nf != -1 || BasementOpen))
                { Note($"heard {n.what} on floor {nf}: into the shaft"); SendAway(2.5f, nf); }
            }
            return;
        }
        // sent somewhere by the game (the finale: the grinding VCR behind the recorder-room door) it keeps going until
        // it gets there. Sounds from that room only confirm it; only you, loud and close, turn it aside. Before this,
        // a stray noise on the way (a live cable sparking) took over once the 5 s 'busy' window below ran out, and it
        // never reached the door.
        if (Sent && (state == State.Investigate || state == State.Rush))
        {
            if (sentRoom != null && LevelLayout.RoomAt(n.pos + Vector3.up * 0.5f) == sentRoom) { LastHeardAt = Time.time; return; }
            bool closeYou = n.byPlayer && Mathf.Abs(n.pos.y - transform.position.y) < 2.2f && Vector3.Distance(transform.position, n.pos) < rushRange && lvl >= rushDb;
            if (!closeYou) return;
            sentUntil = 0f;
        }
        bool busy = state == State.Investigate || state == State.Rush || state == State.Attend || state == State.Bang;
        // already after something louder, unless this is you again: then it follows you, step by step
        bool tracking = n.byPlayer && targetByPlayer && (state == State.Investigate || state == State.Rush) && Time.time - LastHeardAt < 6f;
        if (busy && !tracking && !urgent && lvl < targetDb - 3f && Time.time - LastHeardAt < 5f) return;
        if (!NavMesh.SamplePosition(n.pos, out var hit, 2.5f, agent.areaMask)) return;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, path) || path.status == NavMeshPathStatus.PathInvalid) return;
        Target = path.status == NavMeshPathStatus.PathPartial ? DoorInTheWay(hit.position) : hit.position;
        var before = state;
        targetDb = tracking ? Mathf.Max(lvl, targetDb - 2f) : lvl;
        targetByPlayer = n.byPlayer;
        hurry = n.byPlayer && lvl >= threshold + hurryMargin;
        LastHeard = n.what;
        LastHeardDb = lvl;
        LastHeardAt = Time.time;
        if (n.byPlayer) { lastPlayerNoiseAt = Time.time; lastPlayerNoisePos = n.pos; }
        bool rushing = state == State.Rush;
        attending = n.src is NoiseDevice || n.src is Phone ? n.src : null;
        float dist = Vector3.Distance(transform.position, n.pos);
        bool sameFloor = Mathf.Abs(n.pos.y - transform.position.y) < 2.2f;
        if (n.byPlayer && sameFloor && dist < rushRange && lvl >= rushDb) SetState(State.Rush);
        else if (!(rushing && n.byPlayer)) SetState(State.Investigate);
        // you hear it react: a sharp breath when your noise turns it
        if (n.byPlayer && (before == State.Wander || before == State.Search) && AudioEngine.Instance)
        {
            AudioEngine.Instance.PlayAt(SoundBank.Pick("monster_breath"), transform.position + Vector3.up * 1.6f, 0.5f, 0.75f, 1f, 22f);
            if (Hud.Instance) Hud.Instance.Caption("[a sharp breath: it heard you]", transform.position);
        }
        Note($"heard {n.what} {lvl:0} dB at {dist:0} m -> {state}");
        agent.SetDestination(Target);
        if (state == State.Rush && AudioEngine.Instance && Random.value < 0.6f)
            AudioEngine.Instance.PlayAt(SoundBank.Pick("monster_breath"), transform.position + Vector3.up * 1.6f, 0.55f, 0.8f, 1f, 25f);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        var g = Game.Instance;
        if (!g || !agent) return;
        float dt = Time.deltaTime;
        if (state == State.Away) { AwayTick(); return; }
        if (!agent.isOnNavMesh) return;

        if (Time.time < HoldUntil)
        {
            agent.isStopped = true;
            figure.Animate = false;
            return;
        }
        figure.Animate = true;
        agent.isStopped = false;

        switch (state)
        {
            case State.Scripted:
                agent.speed = scriptedSpeed;
                if (scriptedMove && (agent.destination - Target).sqrMagnitude > 0.04f) agent.SetDestination(Target);
                break;
            case State.Wander:
                agent.speed = wanderSpeed;
                if (!agent.pathPending && agent.remainingDistance < 0.6f)
                {
                    if (Random.value < 0.35f) { SetState(State.Search, Random.Range(2.5f, 4.5f)); break; }
                    NextPatrol();
                }
                Director();
                break;
            case State.Investigate:
            case State.Rush:
                agent.speed = state == State.Rush || Finale ? rushSpeed : hurry && targetByPlayer ? hurrySpeed : investigateSpeed;
                if (Time.time < chaseUntil && NavMesh.SamplePosition(g.PlayerFeet, out var hc, 2f, agent.areaMask))
                { if (state != State.Rush) SetState(State.Rush); Target = hc.position; if ((agent.destination - Target).sqrMagnitude > 0.09f) agent.SetDestination(Target); break; }
                if (state == State.Rush && Time.time - lastPlayerNoiseAt < 0.5f && NavMesh.SamplePosition(lastPlayerNoisePos, out var h2, 2f, agent.areaMask))
                { Target = h2.position; agent.SetDestination(Target); }
                if (!agent.pathPending && agent.remainingDistance < 0.7f) Arrive();
                break;
            case State.Search:
                agent.speed = 0f;
                agent.isStopped = true;
                figure.Animate = false;
                if (figure.head) figure.head.localRotation = Quaternion.Euler(10f + Mathf.Sin(Time.time * 0.9f) * 8f, Mathf.Sin(Time.time * 0.7f) * 60f, Mathf.Sin(Time.time * 0.5f) * 18f);
                if (Time.time > stateUntil) { SetState(State.Wander); NextPatrol(true); }
                break;
            case State.Attend:
                agent.isStopped = true;
                figure.Animate = false;
                if (attending) figure.LookAt(AttendPos, 90f);
                if (Time.time > stateUntil)
                {
                    if (attending is NoiseDevice nd && nd && nd.Playing) nd.Break();
                    if (attending is Phone ph && ph && ph.Ringing) ph.Smash();
                    attending = null;
                    SetState(State.Search, 3f);
                }
                break;
            case State.Bang:
                agent.isStopped = true;
                figure.Animate = false;
                BangTick();
                break;
            case State.Leaving:
                agent.speed = Finale ? rushSpeed : investigateSpeed;
                if (!agent.pathPending && agent.remainingDistance < 0.6f) GoAway();
                break;
        }

        if (state == State.Wander || state == State.Search || state == State.Investigate || state == State.Rush || state == State.Attend || state == State.Leaving) Look();
        else { SeesYou = false; seenFor = 0f; }
        OpenDoorsAhead();
        Sounds(dt);
        if (state != State.Search && state != State.Attend && figure.head) figure.head.localRotation = Quaternion.Slerp(figure.head.localRotation, Quaternion.Euler(14f, 0f, 12f), dt * 3f);
        CatchCheck(g);
    }

    // ------------------------------------------------------------------ sight (poor: only strong light)
    static readonly int SightMask = (1 << Layers.Structure) | (1 << Layers.Ceiling) | (1 << Layers.Door) | (1 << Layers.Prop);
    void Look()
    {
        if (Time.time < nextLook) return;
        nextLook = Time.time + 0.15f;
        var g = Game.Instance;
        bool can = g && g.State == Game.Phase.Hotel && g.PlayerCamera && Time.time >= CalmUntil;
        if (!can) { SeesYou = false; seenFor = 0f; return; }
        var eye = transform.position + Vector3.up * 1.75f;
        var head = g.PlayerCamera.transform.position;
        var to = head - eye;
        float dist = to.magnitude;
        bool sees = Mathf.Abs(head.y - eye.y) < 2.6f && Exposure.VisibleFrom(eye, dist);
        if (sees)
        {
            // its field of view (while it searches, its head turns: it looks where the head looks)
            var fwd = state == State.Search && figure.head ? figure.head.forward : transform.forward;
            fwd.y = 0f; to.y = 0f;
            sees = Vector3.Angle(fwd, to) <= FieldOfView;
        }
        // walls, doors and furniture in between (aim at your head and at your chest)
        if (sees) sees = !Physics.Linecast(eye, head, SightMask, QueryTriggerInteraction.Ignore) || !Physics.Linecast(eye, head + Vector3.down * 0.5f, SightMask, QueryTriggerInteraction.Ignore);
        // sent somewhere by the game (the finale): only you close by turns it aside
        if (sees && Sent && dist > rushRange) sees = false;
        if (!sees) { SeesYou = false; seenFor = Mathf.Max(0f, seenFor - 0.15f); return; }
        seenFor += 0.15f;
        if (seenFor < 0.45f) return;                 // it takes a moment to make you out
        bool first = !SeesYou && Time.time - LastSawAt > 3f;
        SeesYou = true;
        LastSawAt = Time.time;
        chaseUntil = Mathf.Max(chaseUntil, Time.time + 1.2f);
        var feet = g.PlayerFeet;
        if (!NavMesh.SamplePosition(feet, out var hit, 2f, agent.areaMask)) return;
        Target = hit.position;
        targetDb = 99f; targetByPlayer = true; hurry = true;
        LastHeard = "saw you"; LastHeardAt = Time.time;
        lastPlayerNoiseAt = Time.time; lastPlayerNoisePos = feet;
        attending = null; sentUntil = 0f;
        if (state != State.Rush) { SetState(State.Rush); Note($"saw you at {dist:0} m ({Exposure.State})"); }
        agent.SetDestination(Target);
        if (first && AudioEngine.Instance)
        {
            AudioEngine.Instance.PlayAt(SoundBank.Pick("monster_breath"), eye, 0.8f, 0.7f, 1f, 30f);
            if (Hud.Instance) Hud.Instance.Caption("[it sees you]", transform.position);
        }
    }

    void Arrive()
    {
        sentUntil = 0f;
        if (AttendActive && Vector3.Distance(AttendPos, transform.position) < 2.6f)
        {
            SetState(State.Attend, attending is NoiseDevice ndv && ndv ? ndv.attendTime : 2.2f);
            return;
        }
        // at the door of a room it can't get into, or stopped short at one?
        if (blockedBy && (blockedBy.bolted || blockedBy.locked) && Vector3.Distance(blockedBy.HandlePos, transform.position + Vector3.up) < 2.4f)
        { banging = blockedBy; blockedBy = null; bangs = 0; nextBang = Time.time + 0.6f; SetState(State.Bang); return; }
        if (agent.pathStatus == NavMeshPathStatus.PathPartial || Vector3.Distance(transform.position, Target) > 1.5f)
        {
            var d = BoltedDoorNear(transform.position, 2.2f);
            if (d) { banging = d; bangs = 0; nextBang = Time.time + 0.6f; SetState(State.Bang); return; }
        }
        Note($"arrived at {transform.position} ({LevelLayout.RoomAt(transform.position + Vector3.up)?.name}), path {agent.pathStatus}, nothing there -> search");
        SetState(State.Search, state == State.Rush ? 5f : Random.Range(3.5f, 6f));
    }

    // The sound is in a room it can't get into (bolted or locked): go to that room's door and beat on it,
    // rather than pressing against the wall from the room next door.
    HingeDoor blockedBy;
    float sentUntil;
    LevelLayout.Room sentRoom;
    public bool Sent => Time.time < sentUntil;
    Vector3 DoorInTheWay(Vector3 goal)
    {
        blockedBy = null;
        var room = LevelLayout.RoomAt(goal + Vector3.up * 0.5f);
        if (room == null) return goal;
        HingeDoor best = null; Vector3 bestPos = goal; float bestLen = float.MaxValue;
        var p = new NavMeshPath();
        foreach (var d in HingeDoor.All)
        {
            if (!d || !(d.bolted || d.locked)) continue;
            var h = d.HandlePos;
            if (Mathf.Abs(h.y - 1f - room.Y) > 1.5f && !(room.twoStorey)) continue;
            var r = room.r;
            bool onEdge = h.x > r.xMin - 0.3f && h.x < r.xMax + 0.3f && h.z > r.yMin - 0.3f && h.z < r.yMax + 0.3f &&
                          (Mathf.Abs(h.x - r.xMin) < 0.3f || Mathf.Abs(h.x - r.xMax) < 0.3f || Mathf.Abs(h.z - r.yMin) < 0.3f || Mathf.Abs(h.z - r.yMax) < 0.3f);
            if (!onEdge) continue;
            var n = d.leaf.forward; n.y = 0; n.Normalize();
            foreach (float sgn in new[] { 1f, -1f })
            {
                var c = new Vector3(h.x, h.y - 1.04f, h.z) + n * sgn * 0.9f;
                if (!NavMesh.SamplePosition(c, out var hh, 0.8f, agent.areaMask)) continue;
                if (!NavMesh.CalculatePath(transform.position, hh.position, agent.areaMask, p) || p.status != NavMeshPathStatus.PathComplete) continue;
                float len = 0f; for (int i = 1; i < p.corners.Length; i++) len += Vector3.Distance(p.corners[i - 1], p.corners[i]);
                if (len < bestLen) { bestLen = len; best = d; bestPos = hh.position; }
            }
        }
        if (best) { blockedBy = best; return bestPos; }
        return goal;
    }

    static HingeDoor BoltedDoorNear(Vector3 p, float r)
    {
        HingeDoor best = null; float bd = r;
        foreach (var d in HingeDoor.All)
        {
            if (!d || !(d.bolted || d.locked)) continue;
            float dd = Vector3.Distance(d.HandlePos, p + Vector3.up);
            if (dd < bd) { bd = dd; best = d; }
        }
        return best;
    }

    void BangTick()
    {
        if (!banging) { SetState(State.Search, 3f); return; }
        figure.LookAt(banging.HandlePos, 90f);
        if (Time.time < nextBang) return;
        nextBang = Time.time + Random.Range(1.3f, 2.0f);
        bangs++;
        banging.Bang(bangs);
        Note("bang " + banging.portalName + " " + bangs);
        int limit = Finale ? 4 : 3;
        if (bangs >= limit)
        {
            if (Finale && banging.bolted && !banging.locked)
            {
                // it knows you are in there
                banging.BreakBolt();
                SetState(State.Rush);
                chaseUntil = Time.time + 6f;
                if (Game.Instance) Target = Game.Instance.PlayerFeet;
                targetDb = 99f; LastHeardAt = Time.time;          // for a few seconds nothing else matters, not even the bell or the VCR
                agent.SetDestination(Target);
            }
            else SetState(State.Search, 2.5f);
            banging = null;
        }
    }

    void OpenDoorsAhead()
    {
        if (agent.velocity.sqrMagnitude < 0.05f) return;
        var fwd = agent.velocity.normalized;
        if (Physics.Raycast(transform.position + Vector3.up * 1.1f, fwd, out var hit, 1.2f, 1 << 11, QueryTriggerInteraction.Ignore))
        {
            var door = hit.collider.GetComponentInParent<HingeDoor>();
            if (door && door.Openness < 0.7f && !door.locked && !door.bolted) door.OpenBy(transform.position, state == State.Rush);
        }
    }

    void Sounds(float dt)
    {
        var ae = AudioEngine.Instance;
        if (!ae) return;
        float v = agent.velocity.magnitude;
        figure.Tick(v, dt);
        stepAcc += v * dt;
        if (stepAcc > (state == State.Rush ? 1.0f : 0.8f))
        {
            stepAcc = 0f;
            var room = LevelLayout.RoomAt(transform.position + Vector3.up * 0.3f);
            string fs = room == null ? "monster_step" : room.floorMat.StartsWith("Carpet") ? "fs_carpet" : room.floorMat.StartsWith("Marble") || room.floorMat.StartsWith("Tile") ? "fs_tile"
                : room.floorMat.StartsWith("Water") ? "fs_water" : room.floorMat.StartsWith("Rubble") || room.floorMat.StartsWith("Soot") ? "fs_rubble" : "monster_step";
            float vol = state == State.Rush ? 0.75f : 0.5f;
            ae.PlayAt(SoundBank.Pick(fs), transform.position, vol, 0.8f, 0.8f, 30f);
            if (fs != "monster_step") ae.PlayAt(SoundBank.Pick("monster_step"), transform.position, vol * 0.6f, 0.85f, 0.8f, 26f);
        }
        if (Time.time > nextBreath)
        {
            nextBreath = Time.time + Random.Range(state == State.Search ? 2.5f : 4f, state == State.Search ? 4f : 8f);
            ae.PlayAt(SoundBank.Pick("monster_breath"), transform.position + Vector3.up * 1.6f, state == State.Search ? 0.45f : 0.3f, Random.Range(0.75f, 0.9f), 0.8f, 18f);
        }
    }

    void CatchCheck(Game g)
    {
        if (g.State != Game.Phase.Hotel || state == State.Scripted || NoCatch) return;
        var feet = g.PlayerFeet;
        if (Mathf.Abs(feet.y - transform.position.y) > 1.3f) return;
        float d = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(feet.x, 0, feet.z));
        if (HideSpot.Inside)
        {
            // hidden: safe, unless it has just heard you inside (breath, a rustle, getting in) and stands right there
            var hs = HideSpot.Current;
            float dh = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(hs.transform.position.x, 0, hs.transform.position.z));
            if (dh < 1.6f && Time.time - lastPlayerNoiseAt < 3f && Vector3.Distance(lastPlayerNoisePos, hs.transform.position) < 2.5f) g.Caught(this);
            return;
        }
        if (d < catchRange) g.Caught(this);
    }

    // ------------------------------------------------------------------ patrol and the director
    void NextPatrol(bool nearest = false)
    {
        int f = Floor;
        if (!LevelLayout.Patrol.TryGetValue(f, out var pts)) return;
        if (nearest)
        {
            float bd = float.MaxValue;
            for (int i = 0; i < pts.Length; i++) { float d = (pts[i] - transform.position).sqrMagnitude; if (d < bd) { bd = d; patrolIndex = i; } }
        }
        patrolIndex = (patrolIndex + 1 + (Random.value < 0.25f ? 1 : 0)) % pts.Length;
        Target = pts[patrolIndex];
        agent.SetDestination(Target);
    }

    void Director()
    {
        var g = Game.Instance;
        if (Finale || g.State != Game.Phase.Hotel) return;
        int pf = LevelLayout.FloorOf(g.PlayerFeet);
        if (pf != Floor) { if (otherFloorSince < 0f) otherFloorSince = Time.time; }
        else otherFloorSince = -1f;
        bool follow = otherFloorSince > 0f && Time.time - otherFloorSince > 45f && (pf != -1 || BasementOpen);
        if (Time.time > nextAway || follow)
        {
            AwayFloor = pf == -1 && !BasementOpen ? 0 : pf;
            Target = LevelLayout.Lair(Floor);
            agent.SetDestination(Target);
            SetState(State.Leaving);
            Note("leaving for the shaft, next floor " + AwayFloor);
        }
    }

    void GoAway(float seconds = -1f)
    {
        SetState(State.Away, seconds > 0f ? seconds : Random.Range(22f, 34f));
        agent.enabled = false;
        foreach (var r in renderers) if (r) r.enabled = false;
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("elev_door"), LevelLayout.Lair(Floor) + Vector3.up * 1.2f, 0.6f, 0.8f, 1f, 30f);
            ae.PlayAt(SoundBank.Pick("amb_metal_groan"), LevelLayout.Lair(Floor) + new Vector3(0, 1.5f, 1.6f), 0.7f, 0.8f, 1f, 35f);
        }
        nextShaftSound = Time.time + 4f;
    }

    float nextShaftSound;
    void AwayTick()
    {
        // you hear it climbing in the shaft, near whichever lift door is closest to you
        var g = Game.Instance;
        if (Time.time > nextShaftSound && AudioEngine.Instance && g)
        {
            nextShaftSound = Time.time + Random.Range(5f, 9f);
            int pf = LevelLayout.FloorOf(g.PlayerFeet); if (pf == 2) pf = 0;
            var p = LevelLayout.Lair(pf) + new Vector3(0, 1.5f, 2.2f);
            AudioEngine.Instance.PlayAt(SoundBank.Pick(Random.value < 0.5f ? "amb_metal_groan" : "elev_creak"), p, 0.6f, Random.Range(0.7f, 0.9f), 1f, 35f);
        }
        if (Finale) stateUntil = Mathf.Min(stateUntil, Time.time + 2f);
        if (Time.time < stateUntil) return;
        int floor = Finale ? FinaleFloor : AwayFloor;
        if (floor == -1 && !BasementOpen) floor = 0;
        Return(floor);
    }

    public void Return(int floor)
    {
        var p = LevelLayout.Lair(floor);
        agent.enabled = true;
        if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position); else transform.position = p;
        foreach (var r in renderers) if (r) r.enabled = true;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("elev_door"), p + Vector3.up * 1.2f, 0.7f, 0.75f, 1f, 30f);
        NoiseBus.Emit(p, 50f, false, "it lift doors");
        nextAway = Time.time + Random.Range(85f, 120f);
        otherFloorSince = -1f;
        if (Finale && Game.Instance) { Target = Game.Instance.PlayerFeet; SetState(State.Investigate); agent.SetDestination(Target); }
        else { SetState(State.Wander); NextPatrol(true); }
        Note("returned on floor " + floor);
    }

    // ------------------------------------------------------------------ control from the game
    public void Place(Vector3 pos, float yaw, State s = State.Wander)
    {
        chaseUntil = 0f;
        if (!agent.enabled) { agent.enabled = true; foreach (var r in renderers) if (r) r.enabled = true; }
        if (NavMesh.SamplePosition(pos, out var hit, 2.5f, NavMesh.AllAreas)) agent.Warp(hit.position); else transform.position = pos;
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        Target = transform.position;
        attending = null; banging = null;
        scriptedMove = false;
        sentUntil = 0f;
        SetState(s);
        if (agent.isOnNavMesh) agent.ResetPath();
        if (s == State.Wander) NextPatrol(true);
    }

    public void Listen(float seconds) { SetState(State.Search, seconds); }

    // The lift bell and the master VCR carry through the building: if it isn't busy with something else (a lure, a
    // door, another noise) it comes. A lure it is standing over keeps it there; when it is done with the lure, the
    // next ring or grind brings it.
    public void Alert(Vector3 pos, string what)
    {
        if (!agent || !agent.isOnNavMesh || Time.time < HoldUntil) return;
        if (state != State.Wander && state != State.Search) return;
        if (LevelLayout.FloorOf(pos) != Floor) return;
        if (!NavMesh.SamplePosition(pos, out var hit, 2.5f, agent.areaMask)) return;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, path) || path.status == NavMeshPathStatus.PathInvalid) return;
        Target = path.status == NavMeshPathStatus.PathPartial ? DoorInTheWay(hit.position) : hit.position;
        targetDb = 40f;
        LastHeard = what;
        LastHeardAt = Time.time;
        attending = null;
        SetState(State.Investigate);
        Note($"alerted by {what} -> {Target}" + (blockedBy ? " (door " + blockedBy.portalName + ")" : ""));
        agent.SetDestination(Target);
    }

    float scriptedSpeed = 0.9f;
    public void ScriptedWalk(Vector3 to, float speed = 0.9f) { scriptedMove = true; scriptedSpeed = speed; Target = to; SetState(State.Scripted); if (agent.isOnNavMesh) agent.SetDestination(to); }

    public void SendAway(float seconds, int returnFloor)
    {
        chaseUntil = 0f;
        if (state == State.Away) { stateUntil = Time.time + seconds; AwayFloor = returnFloor; return; }
        AwayFloor = returnFloor;
        GoAway(seconds);
    }

    public void GoTo(Vector3 pos, bool rush)
    {
        if (state == State.Away) { int gf = LevelLayout.FloorOf(pos); if (gf == -1 && !BasementOpen) gf = 0; Return(gf); }
        Target = pos;
        blockedBy = null;
        if (agent.isOnNavMesh && NavMesh.SamplePosition(pos, out var hit, 2.5f, agent.areaMask))
        {
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, path) && path.status == NavMeshPathStatus.PathPartial) Target = DoorInTheWay(hit.position);
        }
        targetDb = 99f;
        LastHeardAt = Time.time;
        sentUntil = Time.time + 30f;
        sentRoom = LevelLayout.RoomAt(pos + Vector3.up * 0.5f);
        SetState(rush ? State.Rush : State.Investigate);
        Note($"sent to {pos} -> {Target}" + (blockedBy ? " (door " + blockedBy.portalName + ")" : ""));
        if (agent.isOnNavMesh) agent.SetDestination(Target);
    }

    public void StartFinale()
    {
        Finale = true;
        CalmUntil = 0f;
        if (state == State.Away) stateUntil = Time.time + 1.5f;
    }
}
