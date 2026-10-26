using UnityEngine;
using UnityEngine.AI;

// Test monster for chase and sound validation (M releases it, N removes it).
//  Wander      - drifts between rooms at a walk
//  Investigate - walks to the last noise it heard (heard through the building's acoustic paths, so a splash in the
//                flooded tunnel carries much further than a step on carpet behind a closed door)
//  Chase       - sees the player (in its field of view, nothing solid between, and the player is lit: flashlight on,
//                near a working lamp, or very close) and runs; faster than walking, a bit slower than hurrying
//  Search      - lost sight: goes to where it last saw you and looks around
// It opens doors in its way, cannot fit through the crawlspaces, and every step it takes is a positioned sound.
[RequireComponent(typeof(NavMeshAgent))]
public class Stalker : MonoBehaviour
{
    public enum State { Wander, Investigate, Chase, Search }
    public static Stalker Active { get; private set; }

    public State state = State.Wander;
    public float walkSpeed = 1.3f, huntSpeed = 2.3f, chaseSpeed = 3.5f;
    public float sightRange = 24f, fov = 120f, hearThreshold = 0.025f;

    NavMeshAgent agent;
    Transform eye;
    PlayerMotor player;
    Camera cam;
    CamcorderLight lamp;
    Vector3 lastSeen, goal;
    float lastSeenTime = -99f, stateUntil, nextDoor, nextRepath, strideDist, lastNoiseTime;
    SpatialVoice breath;
    Vector3 lastPos;
    static readonly int SightMask = (1 << MaterialAcoustics.StructureLayer) | (1 << MaterialAcoustics.DoorLayer);

    public static Stalker Spawn(Vector3 at)
    {
        var go = new GameObject("Stalker");
        go.transform.position = at;
        var ag = go.AddComponent<NavMeshAgent>();
        ag.radius = NavBaker.AgentRadius;
        ag.height = NavBaker.AgentHeight;
        ag.acceleration = 9f;
        ag.angularSpeed = 300f;
        ag.stoppingDistance = 0.6f;
        ag.autoBraking = true;
        ag.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        var s = go.AddComponent<Stalker>();
        s.BuildBody();
        Active = s;
        return s;
    }

    void BuildBody()
    {
        // a tall, too-thin pale figure (placeholder art)
        var skin = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.52f, 0.47f) };
        skin.SetFloat("_Glossiness", 0.45f);
        void Part(PrimitiveType t, Vector3 p, Vector3 sc, Vector3 rot)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            g.transform.localPosition = p;
            g.transform.localScale = sc;
            g.transform.localEulerAngles = rot;
            g.GetComponent<Renderer>().sharedMaterial = skin;
        }
        Part(PrimitiveType.Capsule, new Vector3(-0.11f, 0.55f, 0), new Vector3(0.11f, 0.55f, 0.11f), Vector3.zero);
        Part(PrimitiveType.Capsule, new Vector3(0.11f, 0.55f, 0), new Vector3(0.11f, 0.55f, 0.11f), Vector3.zero);
        Part(PrimitiveType.Capsule, new Vector3(0, 1.45f, 0.05f), new Vector3(0.32f, 0.45f, 0.2f), new Vector3(10, 0, 0));
        Part(PrimitiveType.Sphere, new Vector3(0, 2.02f, 0.16f), new Vector3(0.22f, 0.3f, 0.24f), Vector3.zero);
        Part(PrimitiveType.Capsule, new Vector3(-0.28f, 1.25f, 0.12f), new Vector3(0.07f, 0.62f, 0.07f), new Vector3(15, 0, 8));
        Part(PrimitiveType.Capsule, new Vector3(0.28f, 1.25f, 0.12f), new Vector3(0.07f, 0.62f, 0.07f), new Vector3(15, 0, -8));
        eye = new GameObject("Eye").transform;
        eye.SetParent(transform, false);
        eye.localPosition = new Vector3(0, 1.98f, 0.2f);
        var bgo = new GameObject("Breath");
        bgo.transform.SetParent(eye, false);
        bgo.AddComponent<AudioSource>();
        breath = bgo.AddComponent<SpatialVoice>();
        breath.minDistance = 0.8f;
        breath.maxDistance = 22f;
        breath.volume = 0.35f;
        breath.label = "Stalker breathing";
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        player = FindAnyObjectByType<PlayerMotor>();
        cam = Camera.main;
        lamp = cam ? cam.GetComponent<CamcorderLight>() : null;
        lastPos = transform.position;
        lastNoiseTime = Time.time;
        var c = SoundBank.Get("monster_breath");
        if (c) breath.Play(c, true, Random.value * c.length, Random.Range(0.9f, 1.05f));
        Wander();
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    void Wander()
    {
        state = State.Wander;
        agent.speed = walkSpeed;
        if (NavBaker.RandomPoint(transform.position, 8f, 25f, out var p)) agent.SetDestination(p);
        stateUntil = Time.time + Random.Range(10f, 25f);
    }

    bool CanSeePlayer()
    {
        if (!player || !cam) return false;
        Vector3 target = cam.transform.position;
        Vector3 d = target - eye.position;
        float dist = d.magnitude;
        if (dist > sightRange) return false;
        if (Vector3.Angle(transform.forward, d) > fov * 0.5f && dist > 2.5f) return false;
        if (Physics.Linecast(eye.position, target, SightMask, QueryTriggerInteraction.Ignore)) return false;
        if (dist < 3.5f) return true;
        if (lamp && lamp.lamp && lamp.lamp.enabled) return true;
        // standing in a working light?
        foreach (var l in LightFlicker.All)
        {
            if (!l.Light || !l.Light.enabled || l.Level < 0.3f) continue;
            float ld = Vector3.Distance(l.transform.position, player.transform.position);
            if (ld < l.Light.range * 0.45f) return true;
        }
        return false;
    }

    void Update()
    {
        if (!agent.isOnNavMesh) return;
        // ---- perception
        if (CanSeePlayer())
        {
            lastSeen = player.transform.position;
            lastSeenTime = Time.time;
            if (state != State.Chase)
            {
                state = State.Chase;
                agent.speed = chaseSpeed;
            }
        }
        else if (state != State.Chase)
        {
            for (int i = NoiseBus.Recent.Count - 1; i >= 0; i--)
            {
                var n = NoiseBus.Recent[i];
                if (n.time <= lastNoiseTime) break;
                if (NoiseBus.Heard(n, eye.position) > hearThreshold)
                {
                    state = State.Investigate;
                    agent.speed = huntSpeed;
                    goal = n.pos;
                    agent.SetDestination(goal);
                    stateUntil = Time.time + 20f;
                    break;
                }
            }
            lastNoiseTime = Time.time;
        }

        // ---- behaviour
        switch (state)
        {
            case State.Chase:
                if (Time.time > nextRepath)
                {
                    nextRepath = Time.time + 0.25f;
                    agent.SetDestination(lastSeen);
                }
                if (Time.time - lastSeenTime > 3.5f)
                {
                    state = State.Search;
                    agent.speed = huntSpeed;
                    agent.SetDestination(lastSeen);
                    stateUntil = Time.time + 10f;
                }
                if (player && Vector3.Distance(transform.position, player.transform.position) < 1.2f && Time.time - lastSeenTime < 0.3f) Caught();
                break;
            case State.Investigate:
            case State.Search:
                if (!agent.pathPending && agent.remainingDistance < 1f)
                {
                    // look around: small random hops near the spot
                    if (NavBaker.RandomPoint(transform.position, 2f, 6f, out var p)) agent.SetDestination(p);
                }
                if (Time.time > stateUntil) Wander();
                break;
            default:
                if ((!agent.pathPending && agent.remainingDistance < 1f) || Time.time > stateUntil) Wander();
                break;
        }

        // ---- doors in the way
        if (Time.time > nextDoor)
        {
            nextDoor = Time.time + 0.2f;
            var hits = Physics.OverlapSphere(transform.position + transform.forward * 0.9f + Vector3.up, 0.8f, 1 << MaterialAcoustics.DoorLayer);
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<HingeDoor>();
                if (d && d.Openness < 0.6f) d.OpenAway(transform.position);
            }
        }

        // ---- footsteps from where it really is
        Vector3 moved = transform.position - lastPos;
        moved.y = 0f;
        lastPos = transform.position;
        strideDist += moved.magnitude;
        float stride = state == State.Chase ? 1.25f : 0.95f;
        if (strideDist > stride)
        {
            strideDist = 0f;
            Step();
        }
        breath.volume = state == State.Chase ? 0.6f : 0.3f;
    }

    void Step()
    {
        string surf = "concrete";
        if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            var room = HotelWorld.Instance ? HotelWorld.Instance.RoomAt(transform.position + Vector3.up * 0.3f) : null;
            string m = MaterialAcoustics.MaterialName(hit);
            surf = room != null && room.water ? "water" : m != null ? Footsteps.FromMaterial(m, hit.collider.name) : "concrete";
        }
        float v = state == State.Chase ? 1.0f : 0.7f;
        if (AudioEngine.Instance)
            AudioEngine.Instance.PlayAt(SoundBank.Pick("fs_" + surf), transform.position + Vector3.up * 0.05f, v, 1f, 1f, 40f);
    }

    void Caught()
    {
        VHSPostFX.ExtraTracking = 1f;
        if (VHSOverlay.Instance) VHSOverlay.Instance.Announce("SIGNAL LOST", 2.5f);
        if (player && NavBaker.RandomPoint(player.transform.position, 30f, 45f, out var p)) agent.Warp(p);
        Wander();
    }

    public string Describe => $"STALKER {state}  {agent.velocity.magnitude:0.0} m/s  {(player ? Vector3.Distance(transform.position, player.transform.position) : 0):0}m";
}
