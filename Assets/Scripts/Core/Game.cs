using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Game flow: title -> camera consent / calibration -> prologue (your bedroom) -> three parts -> ending.
//   PART ONE, THE HOTEL (ground and guest floors): read the letter; the two halves of the safe code (Room 214
//     behind the dresser, Room 209 in the bag); open the manager's safe; the basement key; down the basement stair.
//   PART TWO, THE BASEMENT: the spare main fuse (storage cages); the main breaker (loud); the lift key (recorder
//     room); call the service lift from the pump room (30 s of bell: it comes, and breaks bolts); ride it up.
//   PART THREE, THE ROOF (penthouse and roof): the owner's keycard (through Suite 901 or 903); the roof door; the
//     master tape in the antenna room (18 s of grinding: it comes, and breaks bolts). Then the tape comes out.
// Only the part you are in is switched on (ShowStage). Being caught skips the tape back to the last checkpoint of
// that part; your face wears away a little each time: three times and you are erased.
public class Game : MonoBehaviour
{
    public enum Phase { Title, Consent, Calibrate, Prologue, Hotel, Ending }
    public static Game Instance { get; private set; }
    public static bool InMenu => Instance && (Instance.State == Phase.Title || Instance.State == Phase.Consent || Instance.State == Phase.Calibrate || Instance.paused || Instance.paperOpen || Instance.State == Phase.Ending);

    [Header("Set by the builder")]
    public PlayerMotor player;
    public Camera playerCamera;
    public Material skinMat, blobMat, clothMat, faceMat;
    public SecurityVcr securityVcr;
    public MonitorScreen bedroomTv;
    public GameObject bedroomParcel;                  // the parcel on the doorstep
    public Lamp bedroomHall, bedroomLamp;             // the porch light (comes on with the bell); your bedside lamp
    public Lamp[] houseLamps;                         // every lamp in the house (they stutter, then die, as the tape pulls you in)
    public HingeDoor recorderDoor;
    public GameObject[] stageRoots;          // 0 bedroom, 1 hotel, 2 basement, 3 roof (set by the builder)
    public LiftPanel liftPanel;

    public Phase State { get; private set; } = Phase.Title;
    public Camera PlayerCamera => playerCamera;
    public Vector3 PlayerFeet => player ? player.transform.position : Vector3.zero;
    public int Catches { get; private set; }
    public bool Finale { get; private set; }
    public string Ending { get; private set; } = "";
    public Listener It { get; private set; }
    public readonly List<string> Record = new List<string>();
    public Figure Avatar { get; private set; }
    public float StartedAt { get; private set; }
    public int Stage { get; private set; }
    public string Objective { get; private set; } = "";
    public float ObjectiveChangedAt { get; private set; } = -100f;
    public bool PowerIsOn { get; private set; }
    public bool DemoDone => demoRung;
    public int Level { get; private set; } = 1;          // which part you have reached (1 hotel, 2 basement, 3 roof)
    public int ShownStage { get; private set; } = -1;    // which stage root is switched on
    public bool Transitioning { get; private set; }

    static readonly string[] Objectives =
    {
        "Read the letter on the desk.",                                                                                       // 0
        "Find both halves of the safe code on the guest floor: Room 214 (east wing) and Room 209 (west wing, through 207).",   // 1
        "Open the manager's safe: manager's office, ground floor, behind the security office.",                             // 2
        "Unlock the basement door (north staff corridor, ground floor) and go down.",                                         // 3
        "The breaker has no fuse. Find the spare main fuse: the storage cages (basement, middle).",                          // 4
        "Fit the fuse and throw the main breaker: the electrical room (basement, west). It is loud.",                        // 5
        "Get the lift key: the recorder room, next to the electrical room.",                                                  // 6
        "Call the service lift from the pump room (basement, east). 30 s of bell: draw it away first, or bolt the doors.",   // 7
        "The lift is here: get in.",                                                                                          // 8
        "The roof door needs the owner's keycard, in his study. His suite is chained: get in by the servants' passage (corridor west end) or Suite 903.",             // 9
        "Unlock the roof door with the keycard (middle of the corridor) and go up.",                                          // 10
        "Eject the master tape: the antenna room, north-east corner of the roof. 18 s, and it hears it: draw it away first.", // 11
        "",
    };

    readonly HashSet<string> keys = new HashSet<string>();
    // the camcorder map: rooms you have walked through, and floors whose fire plan you have filmed
    readonly HashSet<string> visited = new HashSet<string>();
    readonly HashSet<int> plans = new HashSet<int>();
    public bool HasVisited(string room) => visited.Contains(room);
    public int VisitedCount => visited.Count;
    public bool HasPlan(int floor) => plans.Contains(floor);
    public void FilmPlan(int floor) { if (plans.Add(floor)) Log("filmed the fire plan of floor " + floor); }
    bool demoRung;
    float enteredHotelAt;
    bool paused, paperOpen;
    int agentType;
    Vector3 checkpoint; float checkpointYaw;

    void Awake()
    {
        Instance = this;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        // no baked lighting data: set the flat ambient into the ambient probe ourselves, or dark rooms render pitch black
        var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
        sh.AddAmbientLight(RenderSettings.ambientLight);
        RenderSettings.ambientProbe = sh;
    }

    void Start()
    {
        if (!GetComponent<ShadowBudget>()) gameObject.AddComponent<ShadowBudget>();      // only the nearest lamps cast shadows
        BuildNavMesh();
        SpawnPeople();
        player.Teleport(LevelLayout.BedroomSpawn, 30f);
        ShowStage(0);
        player.Locked = true;
        checkpoint = LevelLayout.ReceptionSpawn; checkpointYaw = LevelLayout.ReceptionYaw;
        if (AutoTest.Requested) { State = Phase.Prologue; return; }
        if (PlayerPrefs.GetInt("tape_quickstart", 0) == 1 && Settings.CameraAsked)
        {
            PlayerPrefs.SetInt("tape_quickstart", 0);
            StartedAt = Time.time;
            EnterHotel();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }
        State = Phase.Title;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ------------------------------------------------------------------ navigation (built at start, all three floors)
    void BuildNavMesh()
    {
        var settings = NavMesh.CreateSettings();
        settings.agentRadius = 0.28f;
        settings.agentHeight = 1.8f;
        settings.agentSlope = 46f;
        settings.agentClimb = 0.35f;
        settings.minRegionArea = 1f;
        agentType = settings.agentTypeID;
        // every stage is switched on in the saved scene, so both volumes see all their geometry: the hotel and
        // basement, and the penthouse and roof at the top of the tower
        foreach (var bounds in new[] { new Bounds(new Vector3(30f, 1.5f, 23f), new Vector3(66f, 13f, 54f)), new Bounds(new Vector3(42f, 34.5f, 29f), new Vector3(38f, 9f, 36f)),
                                       new Bounds(LevelLayout.BedroomOrigin + new Vector3(5.3f, 1.5f, 5.5f), new Vector3(13f, 4f, 13f)) })
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, (1 << Layers.Structure) | (1 << Layers.Prop), NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            NavMesh.AddNavMeshData(data);
        }
    }

    // ------------------------------------------------------------------ stages
    // Only the stage you are in is drawn, lit and heard. The bedroom keeps the hotel switched on as well: its TV
    // shows the lobby camera.
    public void ShowStage(int s)
    {
        ShownStage = s;
        if (stageRoots == null) return;
        for (int i = 0; i < stageRoots.Length; i++)
            if (stageRoots[i]) stageRoots[i].SetActive(i == s || (s == 0 && i == 1));
    }

    // the listener gets sharper and quicker part by part
    void SetDifficulty(int level)
    {
        It.hearingDb = level == 1 ? 20f : level == 2 ? 18.5f : 17f;
        It.investigateSpeed = level == 1 ? 1.45f : level == 2 ? 1.6f : 1.75f;
        It.rushSpeed = level == 1 ? 2.7f : level == 2 ? 2.85f : 3.0f;
        It.wanderSpeed = level == 1 ? 1.0f : 1.15f;
    }

    public int AgentType => agentType;

    // ------------------------------------------------------------------ people
    void SpawnPeople()
    {
        var root = new GameObject("People").transform;
        var fig = Figure.Build(root, "It", new Color(0.09f, 0.09f, 0.1f), new Color(0.07f, 0.07f, 0.08f), skinMat, blobMat, clothMat, Layers.Monster, 2.05f);
        // on the navmesh before the agent is added, or the agent fails to place itself ("not close enough to the NavMesh")
        fig.transform.position = NavMesh.SamplePosition(LevelLayout.MonsterStart, out var onMesh, 3f, NavMesh.AllAreas) ? onMesh.position : LevelLayout.MonsterStart;
        It = fig.gameObject.AddComponent<Listener>();
        It.figure = fig;
        // added while the figure is switched off, so it is created with the navmesh's agent type straight away (created
        // with the default type first, it found no navmesh of that type: "Failed to create agent")
        fig.gameObject.SetActive(false);
        var ag = fig.gameObject.AddComponent<NavMeshAgent>();
        ag.agentTypeID = agentType;
        ag.radius = 0.28f;
        ag.height = 1.8f;
        ag.speed = 1f;
        ag.angularSpeed = 300f;
        ag.acceleration = 12f;
        ag.stoppingDistance = 0.2f;
        ag.autoBraking = true;
        ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        ag.areaMask = NavMesh.AllAreas;
        It.agent = ag;
        fig.gameObject.SetActive(true);
        if (NavMesh.SamplePosition(LevelLayout.MonsterStart, out var hit, 3f, NavMesh.AllAreas)) ag.Warp(hit.position);
        It.Place(LevelLayout.MonsterStart, 180f, Listener.State.Wander);
        // your avatar: only CCTV cameras and mirrors see it (layer 15)
        Avatar = Figure.Build(root, "You", new Color(0.18f, 0.19f, 0.2f), new Color(0.16f, 0.18f, 0.24f), skinMat, faceMat, clothMat, Layers.Avatar);
    }

    void LateUpdate()
    {
        if (Avatar && player)
        {
            Avatar.transform.position = player.transform.position + (HideSpot.Inside ? Vector3.down * 3f : Vector3.zero);
            Avatar.transform.rotation = Quaternion.Euler(0, player.Yaw, 0);
            if (Avatar.head) Avatar.head.localRotation = Quaternion.Euler(player.Pitch * 0.6f, 0, 0);
            Avatar.Tick(player.HorizontalSpeed, Time.deltaTime);
            if (faceMat) faceMat.SetFloat("_Erase", Catches / 3f);
        }
        if (faceMat && Settings.ShowFace && FaceTracker.Instance && FaceTracker.Instance.FaceTexture) faceMat.SetTexture("_FaceTex", FaceTracker.Instance.FaceTexture);
        if (blobMat) blobMat.SetTextureOffset("_BaseMap", new Vector2(Mathf.Floor(Time.time * 12f) * 0.37f % 1f, Mathf.Floor(Time.time * 12f) * 0.61f % 1f));
        int here = LevelLayout.StageOf(PlayerFeet);
        if (here != ShownStage) ShowStage(here);
        if (State == Phase.Hotel) { var rv = LevelLayout.RoomAt(PlayerFeet); if (rv != null) visited.Add(rv.name); }
        if (State == Phase.Hotel && !Transitioning)
        {
            // part one -> two: down the basement stair, once the door is open
            if (Level == 1 && HasKey("basement") && LevelLayout.RoomAt(PlayerFeet)?.name == "Basement Stairhead")
                StartCoroutine(ToBasement());
            // part two -> three: into the lift car once it has come down
            if (Level == 2 && liftPanel && liftPanel.Arrived && Vector3.Distance(PlayerFeet, LevelLayout.LiftCarSpot) < 1.1f)
                StartCoroutine(RideLift());
        }
        if (State == Phase.Hotel)
        {
            // the demonstration: the house phone on the north gallery rings by itself; from the lobby you can watch
            // it come out of the ballroom, stand over the phone and tear it off
            bool leftOffice = LevelLayout.RoomAt(PlayerFeet)?.name != "Front Office";
            if (!demoRung && !AutoTest.Requested && (Time.time - enteredHotelAt > 50f || (leftOffice && Time.time - enteredHotelAt > 12f)))
            {
                demoRung = true;
                var house = Phone.Find("Gallery");
                if (house) house.Ring(12);
                Log("demo: house phone rings");
            }
        }
        if (State == Phase.Prologue && PrologueBeat == Beat.InBed) GettingUp();
        // the tape in the bedroom: E / Enter / Space skips it
        if (State == Phase.Prologue && PrologueBeat == Beat.Playing && Time.time - BedroomVcr.PlayingSince > 0.5f && !prologueSkip && !PullingIn && !InMenu &&
            (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
            prologueSkip = true;
        // testing: F9 skips the current objective, F10 the rest of this part
        if (State == Phase.Hotel && !Transitioning && !catching && !InMenu)
        {
            if (Input.GetKeyDown(KeyCode.F9)) SkipObjective();
            else if (Input.GetKeyDown(KeyCode.F10)) SkipPart();
        }
        if (Input.GetKeyDown(KeyCode.Escape) && (State == Phase.Prologue || State == Phase.Hotel))
        {
            if (paperOpen) ClosePaper();
            else SetPaused(!paused);
        }
    }

    // ------------------------------------------------------------------ menus
    public void SetPaused(bool p)
    {
        paused = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;
        Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = p;
        if (Settings.UseCamera && FaceTracker.Instance) { if (p) FaceTracker.Instance.Pause(); else FaceTracker.Instance.Resume(); }
    }
    public bool Paused => paused;
    public void OpenPaper() { paperOpen = true; }
    // closing a paper: the key that closed it must not also use whatever you are looking at (it reopened at once)
    public void ClosePaper() { paperOpen = false; if (Hud.Instance) Hud.Instance.HidePaper(); Interactor.BlockUntilRelease(); }
    public bool PaperOpen => paperOpen;

    public void GoConsent() { State = Phase.Consent; }

    public void ConsentDone(bool camera, bool face)
    {
        Settings.UseCamera = camera;
        Settings.ShowFace = face;
        Settings.CameraAsked = true;
        if (faceMat) faceMat.SetFloat("_UseCam", face ? 1f : 0f);
        if (camera)
        {
            FaceTracker.Ensure().Begin();
            State = Phase.Calibrate;
        }
        else BeginPrologue();
    }

    public void CalibrationDone(bool ok)
    {
        if (!ok) { Settings.UseCamera = false; if (FaceTracker.Instance) FaceTracker.Instance.Stop(); }
        BeginPrologue();
    }

    // The prologue, in your house, in beats: you wake up in bed and get up (hold E and keep the pointer steady with the
    // arrow keys, as you will to ease doors; slip and you sink back a little); the doorbell rings, the porch light
    // comes on, steps go away down the path; the parcel on the doorstep; open it: the tape and a card, "PLAY ME"; the
    // VCR under the TV; snow, and a rolling picture you steady with the arrow keys; the tape: the hotel lobby, filmed on
    // a camcorder left on a tripod, something walking up to the lens while the lamps stutter; its body fills the
    // picture, the lamps die and the TV pulls you in (PulledIn).
    public enum Beat { InBed, Awake, Doorbell, Letter, HasTape, Tracking, Playing, Pulling }
    public Light[] tapeLights = new Light[0];       // the camcorder's lamps in the lobby, on while the tape plays
    void TapeLights(bool on) { foreach (var l in tapeLights) if (l) l.enabled = on; }
    public bool AtFrontDoor { get { var d = PlayerFeet - LevelLayout.FrontDoorInside; d.y = 0f; return d.magnitude < 1.4f && Mathf.Abs(PlayerFeet.y - LevelLayout.FrontDoorInside.y) < 1f; } }
    // facing the living-room TV from in front of the sofa (the endings)
    float TvYaw() { if (!bedroomTv) return 180f; var d = bedroomTv.transform.position - LevelLayout.LivingTvView; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
    public Beat PrologueBeat { get; private set; }
    public static bool FastPrologue;                  // the screenshot tour: the tracking locks itself quickly
    public float GetUpProgress { get; private set; }
    Transform bedView;
    bool AutoPrologue => AutoTest.Requested || PlayBot.Requested;

    public void BeginPrologue()
    {
        State = Phase.Prologue;
        StartedAt = Time.time;
        BedroomVcr.HasTape = false;
        BedroomVcr.Playing = false;
        prologueSkip = false;
        PrologueBeat = Beat.InBed;
        GetUpProgress = 0f;
        // your house is real life: a clean, warm picture (the tape's look comes when it pulls you in)
        if (VhsScreen.Instance) VhsScreen.Instance.home = 1f;
        if (bedroomParcel) bedroomParcel.SetActive(false);
        if (bedroomHall) bedroomHall.Set(false);
        if (houseLamps != null) foreach (var l in houseLamps) if (l) { l.pattern = l.name == "Fire" ? Lamp.Pattern.Candle : Lamp.Pattern.Steady; l.Set(true); }
        // lying in bed: the view from the pillow, looking up at the ceiling and the lamp
        player.Teleport(LevelLayout.BedGetUp, LevelLayout.BedGetUpYaw, 0f);
        if (!bedView) bedView = new GameObject("Bed view").transform;
        bedView.SetPositionAndRotation(LevelLayout.BedEye, BedLook(0f));
        player.HideView = bedView;
        player.Locked = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // the tape: the hotel lobby, filmed on a camcorder left on a tripod; something walks up to the lens (about 8 s)
        It.Place(LevelLayout.TapeFigureStart, 135f, Listener.State.Scripted);
        if (bedroomTv) { bedroomTv.on = false; bedroomTv.Signal(0f, 0f); }
        if (AutoPrologue) StartCoroutine(AutoGetUp());
        Log("prologue");
    }

    // lying (looking at the ceiling, a little towards the room) to sitting up on the edge of the bed
    static Quaternion BedLook(float k) => Quaternion.Slerp(Quaternion.Euler(-62f, 120f, 0f), Quaternion.Euler(12f, 95f, 0f), k);
    static Vector3 BedEyeAt(float k) => Vector3.Lerp(LevelLayout.BedEye, LevelLayout.BedEye + new Vector3(0.55f, 0.35f, -0.5f), k);

    IEnumerator AutoGetUp() { yield return new WaitForSeconds(0.5f); if (PrologueBeat == Beat.InBed) GotUp(); }

    float lastRustle;
    void GettingUp()
    {
        var ease = DoorEase.Instance;
        bool held = Interactor.PressHeld && !InMenu;
        if (held)
        {
            if (ease && !ease.Free) { ease.BeginFree("getting up"); if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("door_creak"), LevelLayout.BedEye, 0.12f, 0.55f, 0.5f, 6f); }
            bool inBand = !ease || ease.TickFree(Time.deltaTime, DoorEase.ArrowInput);
            GetUpProgress = Mathf.Clamp01(GetUpProgress + (inBand ? Time.deltaTime / 1.8f : -Time.deltaTime * 0.9f));
            if (Time.time - lastRustle > 0.7f && AudioEngine.Instance)
            { lastRustle = Time.time; AudioEngine.Instance.PlayAt(SoundBank.Pick("cloth"), BedEyeAt(GetUpProgress) + Vector3.down * 0.3f, inBand ? 0.15f : 0.3f, inBand ? 1f : 0.8f, 0.5f, 6f); }
        }
        else
        {
            if (ease && ease.Free) ease.End();
            GetUpProgress = Mathf.Max(0f, GetUpProgress - Time.deltaTime * 0.25f);     // let go: you sink back, slowly
        }
        float k = Mathf.SmoothStep(0f, 1f, GetUpProgress);
        if (bedView) bedView.SetPositionAndRotation(BedEyeAt(k), BedLook(k));
        if (GetUpProgress >= 1f) GotUp();
    }

    public void GotUp()
    {
        if (PrologueBeat != Beat.InBed) return;
        var ease = DoorEase.Instance;
        if (ease && ease.Free) ease.End();
        player.HideView = null;
        player.Teleport(LevelLayout.BedGetUp, LevelLayout.BedGetUpYaw, 0f);
        player.Locked = false;
        PrologueBeat = Beat.Awake;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("steps_wood_house"), LevelLayout.BedGetUp, 0.3f, 1f, 0.5f, 8f);
        StartCoroutine(Doorbell());
        Log("got up");
    }

    // the doorbell, then knocking, again and again, until you come to the front door; then the letterbox clacks, an
    // envelope drops onto the doormat, and steps go away outside. The door stays locked: nothing outside, the tape
    // comes in.
    IEnumerator Doorbell()
    {
        yield return new WaitForSeconds(AutoPrologue ? 0.3f : 3.5f);
        if (State != Phase.Prologue) yield break;
        var ae = AudioEngine.Instance;
        var door = LevelLayout.FrontDoorKnock;
        if (ae) ae.PlayAt(SoundBank.Pick("doorbell"), LevelLayout.DoorbellChime, 0.8f, 1f, 1.5f, 30f);
        if (Hud.Instance) Hud.Instance.Caption("[the doorbell]", LevelLayout.DoorbellChime);
        if (PrologueBeat == Beat.Awake) PrologueBeat = Beat.Doorbell;
        float nextKnock = Time.time + 2.5f;
        while (State == Phase.Prologue && !AtFrontDoor)
        {
            if (Time.time >= nextKnock)
            {
                nextKnock = Time.time + Random.Range(6.5f, 8.5f);
                var kv = ae ? ae.PlayAt(SoundBank.Pick("door_knock"), door, 2.5f, 1f, 3f, 30f) : null;
                if (kv) StartCoroutine(MeasureKnock(kv));
                if (Hud.Instance) Hud.Instance.Caption("[knocking at the front door]", door);
            }
            yield return null;
        }
        if (State != Phase.Prologue) yield break;
        yield return new WaitForSeconds(0.6f);
        if (State != Phase.Prologue) yield break;
        var slot = LevelLayout.Letterbox;
        if (ae) ae.PlayAt(SoundBank.Pick("door_unlatch_metal"), slot, 0.5f, 1.3f, 0.5f, 12f);
        if (bedroomParcel) { bedroomParcel.SetActive(true); StartCoroutine(DropThroughLetterbox(bedroomParcel.transform)); }
        if (Hud.Instance) Hud.Instance.Caption("[something drops through the letterbox]", slot);
        if (PrologueBeat == Beat.Doorbell || PrologueBeat == Beat.Awake) PrologueBeat = Beat.Letter;
        yield return new WaitForSeconds(1.0f);
        for (int i = 0; i < 6; i++)
        {
            if (ae) ae.PlayAt(SoundBank.Pick("steps_wood_house"), door + new Vector3(0.3f * i, -1.1f, 0.5f + i * 0.45f), 0.3f - i * 0.04f, 1f, 1f, 16f);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // it comes through the flap on its edge, tips over and lands flat on the mat
    IEnumerator DropThroughLetterbox(Transform env)
    {
        var restPos = env.position; var restRot = env.rotation;
        var start = LevelLayout.Letterbox + Vector3.back * 0.03f;
        var startRot = Quaternion.Euler(80f, 0f, 0f) * restRot;
        const float T = 0.45f;
        for (float t = 0f; t < T; t += Time.deltaTime)
        {
            float k = t / T;
            env.position = Vector3.Lerp(start, restPos, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.05f) + Vector3.back * (Mathf.Sin(k * Mathf.PI) * 0.12f) - Vector3.up * 0f;
            env.position = new Vector3(env.position.x, Mathf.Lerp(start.y, restPos.y, k * k), env.position.z);
            env.rotation = Quaternion.Slerp(startRot, restRot, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        env.SetPositionAndRotation(restPos, restRot);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("land_wood"), restPos, 0.2f, 1.6f, 0.5f, 8f);
    }

    public void TapeTaken() { if (State == Phase.Prologue) PrologueBeat = Beat.HasTape; }

    public void TapeInserted() { if (State != Phase.Prologue) return; PrologueBeat = Beat.Tracking; StartCoroutine(Tracking()); }

    // the TV comes on to snow, then a rolling picture: steady it with the arrow keys (the pointer you will use to ease
    // doors) until it holds; if it won't, it settles by itself after a while
    IEnumerator Tracking()
    {
        var ae = AudioEngine.Instance;
        if (bedroomTv) { bedroomTv.on = true; bedroomTv.Show(CctvCamera.Find("TAPE")); bedroomTv.Signal(1f, 0.3f); }
        TapeLights(true);
        if (ae && bedroomTv) { ae.PlayAt(SoundBank.Pick("tv_zap"), bedroomTv.transform.position, 0.5f, 1f, 1f, 10f); ae.PlayAt(SoundBank.Pick("amb_tv_static"), bedroomTv.transform.position, 0.45f, 1f, 1f, 10f); }
        yield return new WaitForSeconds(0.9f);
        var ease = DoorEase.Instance;
        if (ease) ease.BeginFree("tracking");
        float locked = 0f, t0 = Time.time, roll = 0.3f;
        float give = AutoTest.Requested || PlayBot.Requested ? 2f : FastPrologue ? 3f : 15f;
        while (State == Phase.Prologue && locked < 2.2f && Time.time - t0 < give)
        {
            bool inBand = !ease || Game.InMenu || ease.TickFree(Time.deltaTime, DoorEase.ArrowInput);
            locked = Mathf.Max(0f, locked + (inBand ? Time.deltaTime : -Time.deltaTime * 0.6f));
            float k = locked / 2.2f;
            float miss = ease ? Mathf.Clamp01(Mathf.Abs(ease.Pointer - ease.BandCentre) / 0.4f) : 0f;
            roll = Mathf.Repeat(roll + Time.deltaTime * (0.15f + miss * 1.6f) * (1f - k), 1f);
            if (bedroomTv) bedroomTv.Signal(Mathf.Lerp(0.85f, 0.05f, k), roll * (1f - k));
            yield return null;
        }
        if (ease && ease.Free) ease.End();
        if (State != Phase.Prologue) yield break;
        if (bedroomTv) bedroomTv.Signal(0f, 0f);
        if (ae && bedroomTv) ae.PlayAt(SoundBank.Pick("tv_click"), bedroomTv.transform.position, 0.4f, 1.2f, 1f, 10f);
        PrologueBeat = Beat.Playing;
        TapePlaying();
    }

    public void TapePlaying()
    {
        if (bedroomTv) { bedroomTv.on = true; bedroomTv.Show(CctvCamera.Find("TAPE")); bedroomTv.Signal(0f, 0f); }
        PrologueBeat = Beat.Playing;
        // the lamps start to stutter as the thing on the tape comes closer
        if (houseLamps != null) foreach (var l in houseLamps) if (l && l.name != "Fire") l.pattern = Lamp.Pattern.Flicker;
        Log("tape playing");
        It.ScriptedWalk(LevelLayout.TapeFigureStop);
        StartCoroutine(WatchTv());
        StartCoroutine(PrologueWatch());
        StartCoroutine(PrologueLines());
    }

    // while it plays: only what you're watching (the rules are in the letter at the hotel)
    bool prologueSkip;
    IEnumerator PrologueLines()
    {
        string[] lines =
        {
            "A hotel lobby. The tape is dated 31 October 2003.",
            "Someone is standing in the lobby.",
            "It's walking towards the camera.",
        };
        foreach (var l in lines)
        {
            if (State != Phase.Prologue) yield break;
            if (Hud.Instance) Hud.Instance.Subtitle(l, PlayerFeet, 2.6f);
            float t = Time.time;
            while (Time.time - t < 2.7f) { if (State != Phase.Prologue) yield break; yield return null; }
        }
    }

    IEnumerator PrologueWatch()
    {
        // it walks up to the camera; the last two metres it comes fast, and its body fills the picture: then it takes you
        float t0 = Time.time;
        bool lunge = false;
        while (State == Phase.Prologue)
        {
            var dv = It.transform.position - LevelLayout.TapeFigureStop; dv.y = 0f;
            float d = dv.magnitude;
            if (!lunge && d < 2.2f)
            {
                lunge = true;
                It.ScriptedWalk(LevelLayout.TapeFigureStop, 2.3f);
                var ae = AudioEngine.Instance;
                if (ae && bedroomTv) ae.PlayAt(SoundBank.Pick("monster_breath"), bedroomTv.transform.position, 0.7f, 0.9f, 1f, 10f);
            }
            if (d < 0.3f || Time.time - t0 > 16f || prologueSkip) break;
            yield return null;
        }
        if (State != Phase.Prologue) yield break;
        TapeReachedLens = !prologueSkip;
        yield return new WaitForSeconds(prologueSkip ? 0.2f : 0.35f);
        if (State != Phase.Prologue) yield break;
        yield return PulledIn();
    }

    // Sucked into the tape: your view is dragged across the room into the TV, faster and faster; the room stretches
    // (the field of view widens as you go), the picture comes apart (grain, wobble, colour bleed, tears), the hiss
    // and the fast-forward whine rise. The screen fills your sight, and you are on the tape, in the hotel.
    // how loud the last knock reached you (dB of gain at your ear, after the walls and doors; the autotest reads it)
    public float KnockHeardDb { get; private set; } = -999f;
    public string KnockPath { get; private set; } = "";
    IEnumerator MeasureKnock(SpatialVoice v)
    {
        float t = Time.time;
        while (v && v.isActiveAndEnabled && Time.time - t < 1.2f) { if (v.LastGainDb > KnockHeardDb) { KnockHeardDb = v.LastGainDb; KnockPath = v.LastPath; } yield return null; }
    }

    public bool PullingIn { get; private set; }
    public bool TapeReachedLens { get; private set; }

    // While it plays you kneel in front of the TV (your view eases down to it and closes in a little): the picture
    // fills your sight, so what's on the tape is plain to see. The pull into the tape starts from here.
    Transform tvView;
    float fovBeforeTv = -1f;
    public bool WatchingTv => tvView;
    IEnumerator WatchTv()
    {
        if (!bedroomTv || !player || !playerCamera) yield break;
        var cam = playerCamera.transform;
        var scr = bedroomTv.transform;
        var view = new GameObject("Watching the tape").transform;
        view.SetPositionAndRotation(cam.position, cam.rotation);
        tvView = view;
        player.HideView = view;
        player.Locked = true;
        fovBeforeTv = playerCamera.fieldOfView;
        Vector3 p0 = cam.position, p1 = scr.position - scr.forward * 0.85f + Vector3.up * 0.03f;
        Quaternion q0 = cam.rotation, q1 = Quaternion.LookRotation(scr.forward, Vector3.up);
        for (float t = 0f; t < 1.3f; t += Time.deltaTime)
        {
            if (State != Phase.Prologue || PullingIn || !view) yield break;
            float f = Mathf.SmoothStep(0f, 1f, t / 1.3f);
            view.SetPositionAndRotation(Vector3.Lerp(p0, p1, f), Quaternion.Slerp(q0, q1, f));
            playerCamera.fieldOfView = Mathf.Lerp(fovBeforeTv, 46f, f);
            yield return null;
        }
        while (State == Phase.Prologue && !PullingIn && view)
        {
            view.position = p1 + Vector3.up * Mathf.Sin(Time.time * 1.5f) * 0.004f;       // your breathing
            yield return null;
        }
    }
    IEnumerator PulledIn()
    {
        PullingIn = true;
        PrologueBeat = Beat.Pulling;
        player.Locked = true;
        // the lamps die (the fire too), the radio goes quiet: only the TV's light is left
        if (houseLamps != null) foreach (var l in houseLamps) if (l) l.Set(false);
        Fiddle.SilenceRadios();
        var cam = playerCamera.transform;
        var screen = bedroomTv ? bedroomTv.transform : null;
        var ae = AudioEngine.Instance;
        var vhs = VhsScreen.Instance;
        float fov0 = playerCamera.fieldOfView;
        float h0 = vhs ? vhs.home : 0f;
        float g0 = vhs ? vhs.grain : 0f, d0 = vhs ? vhs.distortion : 0f, w0 = vhs ? vhs.wobble : 0f, c0 = vhs ? vhs.chromaShift : 0f, j0 = vhs ? vhs.jitter : 0f, n0 = vhs ? vhs.chromaNoise : 0f;
        var view = new GameObject("Pulled in").transform;
        view.SetPositionAndRotation(cam.position, cam.rotation);
        player.HideView = view;
        if (tvView) { Destroy(tvView.gameObject); tvView = null; }
        Vector3 p0 = cam.position;
        Quaternion r0 = cam.rotation;
        // into the middle of the picture, from the front (the quad faces the room)
        Vector3 into = screen ? screen.position - screen.forward * 0.04f : p0 + cam.forward * 1.5f;
        Quaternion face = screen ? Quaternion.LookRotation(screen.forward, Vector3.up) : r0;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("amb_tv_static"), into, 0.75f, 1f, 1f, 12f);
            ae.PlayAt(SoundBank.Pick("ui_ff"), p0, 0.6f, 0.55f, 1f, 10f);
        }
        const float T = 2.6f;
        float nextWhine = 0.9f;
        for (float t = 0f; t < T; t += Time.deltaTime)
        {
            float k = t / T, e = k * k * k;
            view.position = Vector3.Lerp(p0, into, e) + Random.insideUnitSphere * 0.012f * k;
            view.rotation = Quaternion.Slerp(r0, face, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 2.2f))) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 3f * k);
            playerCamera.fieldOfView = Mathf.Lerp(fov0, fov0 + 38f, e);
            if (vhs)
            {
                vhs.home = Mathf.Lerp(h0, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f)));     // the tape seeps in
                vhs.grain = Mathf.Lerp(g0, 0.22f, e);
                vhs.distortion = Mathf.Lerp(d0, 0.45f, e);
                vhs.wobble = Mathf.Lerp(w0, 3.5f, e);
                vhs.chromaShift = Mathf.Lerp(c0, 9f, e);
                vhs.jitter = Mathf.Lerp(j0, 3f, e);
                vhs.chromaNoise = Mathf.Lerp(n0, 0.2f, e);
            }
            if (Hud.Instance && Random.value < 0.04f + k * 0.2f) Hud.Instance.Tear(0.12f + k * 0.2f, k > 0.55f);
            if (ae && t > nextWhine) { nextWhine = 99f; ae.PlayAt(SoundBank.Pick("ui_ff"), view.position, 0.8f, 0.85f, 1f, 10f); }
            yield return null;
        }
        if (Hud.Instance) Hud.Instance.Tear(0.9f, true);
        if (ae) ae.PlayAt(SoundBank.Pick("tv_click"), view.position, 0.6f, 0.7f, 1f, 10f);
        yield return new WaitForSeconds(0.15f);
        // on the tape
        if (vhs) { vhs.grain = g0; vhs.distortion = d0; vhs.wobble = w0; vhs.chromaShift = c0; vhs.jitter = j0; vhs.chromaNoise = n0; }
        playerCamera.fieldOfView = fovBeforeTv > 0f ? fovBeforeTv : fov0;       // as it was before you knelt at the TV
        fovBeforeTv = -1f;
        player.HideView = null;
        Destroy(view.gameObject);
        PullingIn = false;
        EnterHotel();
        if (Hud.Instance) Hud.Instance.Tear(0.8f, true);
    }

    public void EnterHotel()
    {
        State = Phase.Hotel;
        enteredHotelAt = Time.time;
        if (VhsScreen.Instance) VhsScreen.Instance.home = 0f;          // on the tape
        TapeLights(false);
        if (tvView) { Destroy(tvView.gameObject); tvView = null; }
        if (fovBeforeTv > 0f && playerCamera) { playerCamera.fieldOfView = fovBeforeTv; fovBeforeTv = -1f; }
        // the bedroom as it was (the ending comes back to it)
        if (houseLamps != null) foreach (var l in houseLamps) if (l) { l.pattern = l.name == "Fire" ? Lamp.Pattern.Candle : Lamp.Pattern.Steady; l.Set(true); }
        if (bedroomHall) bedroomHall.Set(false);
        if (bedroomParcel) bedroomParcel.SetActive(false);
        player.HideView = null;
        player.Teleport(LevelLayout.ReceptionSpawn, LevelLayout.ReceptionYaw, 24f);     // looking down at the letter on the desk
        player.Locked = false;
        // it starts in the ballroom on the guest floor, calm: only a loud sound reaches it for the first 40 s
        It.Place(LevelLayout.MonsterStart, 180f, Listener.State.Wander);
        It.CalmUntil = Time.time + 40f;
        Level = 1;
        SetDifficulty(1);
        checkpoint = LevelLayout.ReceptionSpawn; checkpointYaw = LevelLayout.ReceptionYaw;
        if (CameraLight.Instance) { CameraLight.Instance.Refill(1f); CameraLight.Instance.SetOn(true); }
        if (bedroomTv) bedroomTv.on = false;
        SetStage(0);
        if (Hud.Instance) Hud.Instance.Announce(LevelLayout.StageNames[1], 3f);
        Log("entered hotel");
    }

    // ------------------------------------------------------------------ the ways between the parts
    IEnumerator ToBasement()
    {
        Transitioning = true;
        player.Locked = true;
        if (Hud.Instance) Hud.Instance.Tear(1.2f, true);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ui_ff"), player.transform.position + Vector3.up * 1.5f, 0.6f, 0.8f, 1f, 10f);
        yield return new WaitForSeconds(0.6f);
        EnterLevel(2);
        player.Locked = false;
        Transitioning = false;
    }

    IEnumerator RideLift()
    {
        Transitioning = true;
        player.Locked = true;
        var ae = AudioEngine.Instance;
        var p = player.transform.position + Vector3.up * 1.6f;
        if (ae) { ae.PlayAt(SoundBank.Pick("elev_door"), p, 0.6f, 0.9f, 1f, 10f); ae.PlayAt(SoundBank.Pick("elev_start"), p, 0.6f, 0.9f, 1f, 10f); }
        It.Finale = false;
        yield return new WaitForSeconds(1.0f);
        // the ride: the tape fast-forwards through the floors
        string[] floors = { "B1", "1", "2", "3", "4", "5", "6", "7", "8", "PH" };
        for (int i = 0; i < floors.Length; i++)
        {
            if (Hud.Instance) { Hud.Instance.Announce("SERVICE LIFT   " + floors[i], 0.7f); if (i % 3 == 0) Hud.Instance.Tear(0.35f, false); }
            if (ae && i % 2 == 0) ae.PlayAt(SoundBank.Pick("elev_creak"), player.transform.position + Vector3.up * 2.4f, 0.4f, Random.Range(0.8f, 1f), 1f, 10f);
            if (PlayerMotor.Instance) PlayerMotor.Instance.Shake(0.08f);
            yield return new WaitForSeconds(0.65f);
        }
        if (ae) ae.PlayAt(SoundBank.Pick("elev_stop"), player.transform.position + Vector3.up * 1.6f, 0.6f, 0.9f, 1f, 10f);
        EnterLevel(3);
        if (ae) ae.PlayAt(SoundBank.Pick("elev_door"), player.transform.position + Vector3.up * 1.6f, 0.6f, 0.85f, 1f, 10f);
        player.Locked = false;
        Transitioning = false;
    }

    // puts you at the start of a part (also used by the tests and the screenshot tour)
    public void EnterLevel(int level)
    {
        State = Phase.Hotel;
        Level = level;
        Finale = false;
        It.Finale = false;
        SetDifficulty(level);
        if (level == 2)
        {
            keys.Add("basement");
            It.BasementOpen = true;
            player.Teleport(LevelLayout.Stage2Spawn, LevelLayout.Stage2Yaw, 0f);
            It.Place(LevelLayout.MonsterStart2, 270f, Listener.State.Wander);
            It.FinaleFloor = -1;
            It.CalmUntil = Time.time + 15f;
            SetCheckpoint(LevelLayout.Stage2Spawn, LevelLayout.Stage2Yaw);
            if (Stage < 4) SetStage(4);
        }
        else if (level == 3)
        {
            keys.Add("basement"); keys.Add("fuse"); keys.Add("liftkey");
            PowerIsOn = true;
            player.Teleport(LevelLayout.Stage3Spawn, LevelLayout.Stage3Yaw, 0f);
            It.Place(LevelLayout.MonsterStart3, 180f, Listener.State.Wander);
            It.FinaleFloor = 4;
            It.CalmUntil = Time.time + 12f;
            SetCheckpoint(LevelLayout.Stage3Spawn, LevelLayout.Stage3Yaw);
            if (Stage < 9) SetStage(9);
        }
        ShowStage(LevelLayout.StageOf(PlayerFeet));
        if (Hud.Instance) Hud.Instance.Announce(LevelLayout.StageNames[level], 3.5f);
        Log("entered level " + level);
    }

    // ------------------------------------------------------------------ testing: skip an objective or a part
    // F9 does what the current objective asks (gives the item, opens the safe, throws the breaker, brings the lift,
    // unlocks the door) or takes you on to the next part where the objective is a way on. F10 skips to the next part.
    public void SkipObjective()
    {
        if (State != Phase.Hotel || Transitioning || !string.IsNullOrEmpty(Ending)) return;
        int was = Stage;
        string what = Objectives[Mathf.Clamp(Stage, 0, Objectives.Length - 1)];
        switch (Stage)
        {
            case 0: LetterRead(); break;
            case 1: GiveKey("code_a"); GiveKey("code_b"); break;
            case 2:
                var safe = FindAnyObjectByType<Safe>();
                if (safe && !safe.Open) safe.Tap();
                GiveKey("basement");
                break;
            case 3:
                var bd = HingeDoor.All.Find(d => d && d.portalName == "Basement Door");
                if (bd) bd.ForceUnlock();
                keys.Add("basement");
                StartCoroutine(ToBasement());
                break;
            case 4: GiveKey("fuse"); break;
            case 5: keys.Add("fuse"); if (Breaker.Instance && !Breaker.Instance.On) Breaker.Instance.Throw(); else PowerOn(); break;
            case 6: GiveKey("liftkey"); break;
            case 7: keys.Add("liftkey"); if (liftPanel) liftPanel.ArriveNow(); else SetStage(8); break;
            case 8: StartCoroutine(RideLift()); break;
            case 9: GiveKey("roofcard"); break;
            case 10:
                keys.Add("roofcard");
                var rd = HingeDoor.All.Find(d => d && d.portalName == "Roof Access Door");
                if (rd) rd.ForceUnlock(); else SetStage(11);
                break;
            default: TapeOut(); break;
        }
        Log($"TEST skipped objective {was}");
        if (Hud.Instance) Hud.Instance.Notice("skipped: " + what);
    }

    public void SkipPart()
    {
        if (State != Phase.Hotel || Transitioning || !string.IsNullOrEmpty(Ending)) return;
        Log($"TEST skipped part {Level}");
        if (Hud.Instance) Hud.Instance.Notice($"skipped part {Level}");
        if (Level == 1) { keys.Add("code_a"); keys.Add("code_b"); keys.Add("basement"); StartCoroutine(ToBasement()); }
        else if (Level == 2) { keys.Add("fuse"); keys.Add("liftkey"); PowerIsOn = true; StartCoroutine(RideLift()); }
        else TapeOut();
    }

    // ------------------------------------------------------------------ objectives, keys, checkpoints
    void SetStage(int s)
    {
        Stage = Mathf.Max(Stage, s);
        if (s == 0) Stage = 0;
        Objective = Objectives[Mathf.Clamp(Stage, 0, Objectives.Length - 1)];
        if (Stage == 1) Objective += $" ({(HasKey("code_a") ? 1 : 0) + (HasKey("code_b") ? 1 : 0)} of 2)";
        ObjectiveChangedAt = Time.unscaledTime;
        Log("objective " + Stage);
    }

    void SetCheckpoint(Vector3 p, float yaw) { checkpoint = p; checkpointYaw = yaw; Log("checkpoint " + p); }

    public void LetterRead() { if (Stage == 0) SetStage(1); }

    public bool HasKey(string id) => !string.IsNullOrEmpty(id) && keys.Contains(id);
    public void GiveKey(string id)
    {
        keys.Add(id);
        Log("key " + id);
        string say = id;
        switch (id)
        {
            case "code_a":
            case "code_b":
                bool both = HasKey("code_a") && HasKey("code_b");
                say = both ? "THE SAFE CODE\nboth halves: back down to the manager's office" : "HALF OF THE SAFE CODE\n(1 of 2)";
                if (Stage < 1) SetStage(1);
                if (both) { if (Stage < 2) SetStage(2); SetCheckpoint(LevelLayout.GuestCheckpoint, 180f); }
                else SetStage(Stage);
                break;
            case "basement": say = "BASEMENT KEY"; if (Stage < 3) SetStage(3); break;
            case "fuse": say = "MAIN FUSE\nfit it in the breaker (electrical room)"; if (Stage < 5) SetStage(5); break;
            case "liftkey": say = "LIFT KEY\nthe call panel is in the pump room"; if (Stage < 7) SetStage(7); break;
            case "roofcard": say = "THE OWNER'S KEYCARD\nthe roof door"; if (Stage < 10) SetStage(10); break;
        }
        if (Hud.Instance) Hud.Instance.Announce(say, 2.5f);
    }

    public void SafeOpened()
    {
        Log("safe open");
        // the door swings open in a dark office: say so, and what is inside
        if (Hud.Instance) Hud.Instance.Announce("THE SAFE IS OPEN\nthe basement key is on its lip", 3f);
    }

    // part two: the lift call. It hears the bell; during the call it hurries and breaks bolts.
    public void LiftCallStarted()
    {
        Log("lift called");
        It.FinaleFloor = -1;
        It.StartFinale();
    }

    public void LiftArrived()
    {
        Log("lift arrived");
        It.Finale = false;
        if (Stage < 8) SetStage(8);
        if (Hud.Instance) Hud.Instance.Announce("THE LIFT IS HERE", 2f);
    }

    public void Unlocked(HingeDoor d)
    {
        Log("unlocked " + d.portalName);
        if (d.portalName == "Basement Door")
        {
            It.BasementOpen = true;
            if (Stage < 3) SetStage(3);
        }
        if (d.portalName == "Roof Access Door")
        {
            if (Stage < 11) SetStage(11);
            SetCheckpoint(LevelLayout.Stage3Checkpoint, 90f);
        }
    }

    public void PowerOn()
    {
        PowerIsOn = true;
        Log("power on");
        if (Stage < 6) SetStage(6);
    }

    public void Caught(Listener by)
    {
        if (State != Phase.Hotel || catching) return;
        Catches++;
        Log("caught " + Catches);
        if (Catches >= 3) { StartCoroutine(EndErased()); return; }
        StartCoroutine(SkipBack());
    }
    bool catching;

    IEnumerator SkipBack()
    {
        catching = true;
        player.Locked = true;
        HideSpot.ForceLeave();
        if (Hud.Instance) Hud.Instance.Tear(1.4f, !Settings.LowScare);
        if (AudioEngine.Instance)
        {
            AudioEngine.Instance.PlayAt(SoundBank.Pick("monster_breath"), player.transform.position + Vector3.up * 1.5f, Settings.LowScare ? 0.3f : 0.8f, 0.7f, 1f, 10f);
            AudioEngine.Instance.PlayAt(SoundBank.Pick(Settings.LowScare ? "amb_tv_static" : "ui_ff"), player.transform.position + Vector3.up * 1.5f, 0.8f, 0.6f, 1f, 10f);
        }
        It.HoldUntil = Time.time + 2f;
        yield return new WaitForSeconds(0.7f);
        player.Teleport(checkpoint, checkpointYaw, -4f);
        // it goes back into the shaft for a while
        It.HoldUntil = 0f;
        It.SendAway(20f, LevelLayout.FloorOf(checkpoint));
        if (Finale || (liftPanel && liftPanel.Calling)) ResetFinale();
        if (Hud.Instance) Hud.Instance.Announce(Catches == 1 ? "THE TAPE SKIPS BACK\nyour face on the tape is wearing away" : "THE TAPE SKIPS BACK\none more time and it keeps you", 3.5f);
        player.Locked = false;
        catching = false;
    }

    // ------------------------------------------------------------------ finale
    public void StartFinale()
    {
        if (Finale) return;
        Finale = true;
        Log("finale");
        It.FinaleFloor = 4;
        It.StartFinale();
        // the grinding carries through the tower: if it is idle on the penthouse floor it climbs the shaft to the roof
        bool idle = It.state == Listener.State.Wander || It.state == Listener.State.Search;
        if (idle && It.Floor != 4 && It.state != Listener.State.Away) It.SendAway(2f, 4);
    }

    void ResetFinale()
    {
        Finale = false;
        It.Finale = false;
        if (securityVcr) securityVcr.ResetForLoop();
        if (liftPanel) liftPanel.ResetCall();
    }

    public void TapeOut()
    {
        Log("tape out");
        StartCoroutine(EndOut());
    }

    IEnumerator EndOut()
    {
        Ending = "OUT";
        Finale = false;
        if (Hud.Instance) Hud.Instance.Tear(1.5f, true);
        yield return new WaitForSeconds(0.9f);
        State = Phase.Ending;
        It.Place(LevelLayout.W(30f, 16f), 160f, Listener.State.Scripted);
        It.HoldUntil = Time.time + 999f;
        player.Teleport(LevelLayout.LivingTvView, TvYaw(), -6f);
        player.Locked = true;
        if (VhsScreen.Instance) VhsScreen.Instance.home = 1f;          // out: real life again
        Fiddle.RadiosOn();
        yield return new WaitForSeconds(2.5f);
        if (Hud.Instance) Hud.Instance.ShowPaper("THE TAPE", "Your VCR pushes the tape out.\n\nOn the label, below the names that are scribbled out and burnt,\nthe blank last line now reads:\n\n" + Expand("{DATE}") + "\n\nand then, in the same handwriting as the letter:\n\"not this one.\"", true);
        yield return new WaitForSecondsRealtime(5f);
        ClosePaper();
        // the TV switches itself on: the lobby camera. It stands in the middle of the lobby, listening.
        if (bedroomTv) { bedroomTv.on = true; bedroomTv.Show(CctvCamera.Find("CAM 1")); }
        player.SetLook(30f, -8f);
        yield return new WaitForSeconds(4.5f);
        if (Hud.Instance) Hud.Instance.ShowEnd("OUT", "You got the tape out. It is still in there, listening.");
    }

    IEnumerator EndErased()
    {
        Ending = "ERASED";
        Finale = false;
        player.Locked = true;
        HideSpot.ForceLeave();
        if (Hud.Instance) Hud.Instance.Tear(2f, !Settings.LowScare);
        yield return new WaitForSeconds(1f);
        State = Phase.Ending;
        // on the bedroom TV, the lobby camera: two shapes now, side by side; the new one has your clothes
        It.Place(LevelLayout.W(29.2f, 15f), 160f, Listener.State.Scripted);
        It.HoldUntil = Time.time + 999f;
        player.Teleport(LevelLayout.LivingTvView, TvYaw(), -6f);
        StartCoroutine(HoldAvatar(LevelLayout.W(31f, 15.4f)));
        if (bedroomTv) { bedroomTv.on = true; bedroomTv.Show(CctvCamera.Find("CAM 1")); }
        player.SetLook(30f, -8f);
        float t = 0f;
        while (t < 6f) { t += Time.deltaTime; if (faceMat) faceMat.SetFloat("_Erase", Mathf.Lerp(0.67f, 1f, t / 6f)); yield return null; }
        if (Hud.Instance) Hud.Instance.ShowEnd("ERASED", "The tape keeps playing. Something new walks the hotel, listening.");
    }

    IEnumerator HoldAvatar(Vector3 pos)
    {
        var a = Avatar;
        Avatar = null;
        while (State == Phase.Ending && a)
        {
            a.transform.position = pos;
            a.transform.rotation = Quaternion.Euler(0, 200f, 0);
            yield return null;
        }
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        BedroomVcr.HasTape = BedroomVcr.Playing = false;
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }

    public void RestartAtReception()
    {
        BedroomVcr.HasTape = BedroomVcr.Playing = false;
        PlayerPrefs.SetInt("tape_quickstart", 1);
        Restart();
    }

    // ------------------------------------------------------------------ util
    public void Log(string what)
    {
        Record.Add($"{Time.time - StartedAt:0.0}s {what}");
        if (Record.Count > 400) Record.RemoveAt(0);
    }

    public static string Expand(string s)
    {
        var d = System.DateTime.Now;
        return s.Replace("{DATE}", d.ToString("dd.MM.yyyy")).Replace("{DATE_LONG}", d.ToString("d MMMM yyyy").ToUpperInvariant());
    }
}
