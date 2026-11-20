using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A physical hinged door.
//   Tap E: slam it open or shut, fast and loud: for running away.
//   Hold E: ease it (DoorEase): a pointer drifts and you keep it in the middle with the arrow keys. While it stays
//           in, the door eases slowly and quietly, in the direction it was going (open if mostly shut, shut if mostly
//           open). If it slips out, the door swings the rest of the way, as loud as how much was left (Slip). Let go
//           and it stays where it is, part open. The mouse wheel nudges it a little either way.
// Locked doors need a key. Guest-room doors have a bolt on the inside: the listener cannot open a bolted door; it
// beats on it and gives up (in the finale the bolt breaks). Room 214's lock sticks (hold to jiggle, tap to shove).
// Openness drives sound through the doorway (AcousticWorld).
public class HingeDoor : Interactable
{
    public static readonly List<HingeDoor> All = new List<HingeDoor>();
    public string portalName;
    public string key;             // locked until the player has this key
    public bool locked, stuck, metal, bolted;
    public float maxAngle = 100f;
    public float hingeSign = 1f;   // +1 opens one way, -1 the other
    public Transform leaf;
    public float startAngle;
    public const float EaseSpeed = 42f;      // degrees per second while held (shut to open in about 2 s): below the creak threshold

    public float Angle { get; private set; }
    public float Openness => Mathf.Clamp01(Mathf.Abs(Angle) / 80f);
    public bool CanEase => !locked && !bolted && !stuck;
    public const float SlamSpeed = 240f;     // a tap: fast enough to be heard (and to get away)
    float quietUntil;                        // a slip has already made its noise: the swing itself adds none
    public bool Held { get; private set; }
    public int BangCount { get; private set; }

    float target, speed = 120f, jiggle, lastCreak, holdDir;
    int mover;                     // who moved it last: 0 you, 1 the listener, 2 the building (draughts)
    bool holdStarted;
    NavMeshObstacle obstacle;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public override string Prompt
    {
        get
        {
            if (locked) return Game.Instance && Game.Instance.HasKey(key) ? "Unlock" : "Locked";
            if (bolted) return "Bolted";
            if (stuck) return "Hold: jiggle the lock   Tap: shove (loud)";
            if (Held) return holdDir > 0 ? "Easing it open   ← → keep it steady" : "Easing it shut   ← → keep it steady";
            return Openness > 0.5f ? "Slam shut   ·   hold: ease it shut" : "Slam open   ·   hold: ease it open";
        }
    }
    public override float HoldProgress => stuck ? jiggle / 4f : -1f;

    void Start()
    {
        obstacle = leaf && leaf.childCount > 0 ? leaf.GetChild(0).GetComponent<NavMeshObstacle>() : null;
        if (startAngle != 0f) SetOpen(startAngle);
        UpdateObstacle();
    }

    public void SetOpen(float angle) { Angle = target = angle; Apply(); }

    void UpdateObstacle() { if (obstacle) obstacle.enabled = locked || bolted; }

    public Vector3 HandlePos => leaf && leaf.childCount > 0 ? leaf.GetChild(0).position : transform.position + Vector3.up;

    public override void Tap()
    {
        mover = 0;
        if (locked)
        {
            if (Game.Instance && Game.Instance.HasKey(key))
            {
                locked = false;
                UpdateObstacle();
                Sound("door_unlatch", 0.5f);
                NoiseBus.Emit(HandlePos, 35f, true, "unlock", this);
                if (Game.Instance) Game.Instance.Unlocked(this);
            }
            else
            {
                Sound("door_locked", 0.5f); NoiseBus.Emit(HandlePos, 30f, true, "rattle", this);
                if (portalName == "Front Door" && Hud.Instance) Hud.Instance.Subtitle("Locked. It's pouring out there; you're staying in.", HandlePos, 2.6f);
            }
            return;
        }
        if (bolted) { Sound("door_locked", 0.35f); NoiseBus.Emit(HandlePos, 28f, true, "rattle", this); return; }
        if (stuck)
        {
            // shove: quick and loud
            stuck = false;
            Sound("door_unlatch", 0.9f);
            Sound("amb_door_slam_far", 0.6f);
            NoiseBus.Emit(HandlePos, 58f, true, "shove", this);
            target = maxAngle * 0.6f * hingeSign;
            speed = 220f;
            return;
        }
        target = Openness > 0.5f ? 0f : 90f * hingeSign;
        speed = SlamSpeed;
        // slammed open: loud whatever creaked a moment ago (slammed shut, the slam itself is heard when it hits)
        if (target != 0f)
        {
            Sound("door_unlatch", 0.5f);
            Sound(metal ? "door_creak_metal" : "door_creak", 0.45f);
            NoiseBus.Emit(HandlePos, 52f, true, "door slammed open", this);
            lastCreak = Time.time;
        }
    }

    // how much of this hold's travel is left (1 = all of it, 0 = there)
    public float EaseLeft
    {
        get
        {
            float dir = holdStarted ? holdDir : (Openness < 0.5f ? 1f : -1f);
            return dir > 0f ? Mathf.Clamp01(1f - Mathf.Abs(Angle) / 90f) : Mathf.Clamp01(Mathf.Abs(Angle) / 90f);
        }
    }

    // easing it, the pointer slipped out: the door swings the rest of the way, and the noise is as big as what was left
    // (a slip at the very start: about 72 dB, heard through the floor; near the end: about 42 dB, heard nearby)
    public float Slip()
    {
        float dir = holdStarted ? holdDir : (Openness < 0.5f ? 1f : -1f);
        float left = EaseLeft;
        mover = 0;
        Held = false; holdStarted = false;
        target = dir > 0f ? 92f * hingeSign : 0f;
        speed = 320f;
        quietUntil = Time.time + 0.9f;
        float db = SlipDb(left);
        if (dir > 0f) { Sound("amb_door_slam_far", Mathf.Lerp(0.25f, 1f, left)); Sound("door_unlatch", 0.5f); }
        else Sound(metal ? "door_close_metal" : "door_close", Mathf.Lerp(0.3f, 1f, left));
        NoiseBus.Emit(HandlePos, db, true, dir > 0f ? "door slips open" : "door slips shut", this);
        if (Hud.Instance) Hud.Instance.Caption(left > 0.5f ? "[the door bangs]" : "[the door knocks]", HandlePos);
        return db;
    }
    public static float SlipDb(float left) => Mathf.Lerp(42f, 72f, Mathf.Clamp01(left));

    // tests and the skip key
    public void ForceUnlock()
    {
        if (!locked) return;
        locked = false;
        UpdateObstacle();
        Sound("door_unlatch", 0.5f);
        if (Game.Instance) Game.Instance.Unlocked(this);
    }

    public override void Hold(float dt, Vector2 mouse)
    {
        if (locked || bolted) return;
        if (stuck)
        {
            jiggle += dt;
            if (Time.time - lastCreak > 0.6f) { lastCreak = Time.time; Sound("door_locked", 0.12f); NoiseBus.Emit(HandlePos, 24f, true, "jiggle", this); }
            if (jiggle >= 4f) { stuck = false; Sound("door_unlatch", 0.3f); target = 12f * hingeSign; speed = EaseSpeed; }
            return;
        }
        mover = 0;
        if (!holdStarted) { holdStarted = true; holdDir = Openness < 0.5f ? 1f : -1f; }
        Held = true;
        // the target leads the door by a tenth of a second, so the door's own Update moves it at EaseSpeed every frame
        // whatever order the two run in (with only one frame's lead, uneven frames eased it at half speed)
        float open = Mathf.Abs(Angle) + holdDir * EaseSpeed * Mathf.Max(dt, 0.1f);
        open = Mathf.Clamp(open, 0f, maxAngle);
        target = open * hingeSign;
        speed = EaseSpeed;
        // a held door creaks very softly now and then
        if (Mathf.Abs(Angle - target) > 0.01f && Time.time - lastCreak > 1.8f)
        {
            lastCreak = Time.time;
            Sound(metal ? "door_creak_metal" : "door_creak", 0.05f);
            NoiseBus.Emit(HandlePos, 18f, true, "door ease", this);
        }
    }

    public override void Release()
    {
        Held = false;
        holdStarted = false;
        if (!stuck && !locked) { target = Angle; }
    }

    // mouse wheel while looking at it
    public void Wheel(float notches)
    {
        if (locked || bolted || stuck || Held) return;
        mover = 0;
        float open = Mathf.Clamp(Mathf.Abs(target) + notches * 10f, 0f, maxAngle);
        target = open * hingeSign;
        speed = EaseSpeed;
    }

    // the listener pushes doors open as it walks (hard when it rushes)
    public bool OpenBy(Vector3 who, bool hard)
    {
        if (locked || bolted) return false;
        mover = 1;
        stuck = false;
        target = 95f * hingeSign;
        speed = hard ? 260f : 120f;
        Sound(hard ? "door_close" : "door_creak", hard ? 0.7f : 0.45f);
        NoiseBus.Emit(HandlePos, hard ? 60f : 40f, false, "it door", this);
        return true;
    }

    public void SetBolt(bool on)
    {
        bolted = on;
        UpdateObstacle();
        Sound(on ? "door_unlatch_metal" : "door_unlatch", 0.3f);
        NoiseBus.Emit(HandlePos, 28f, true, on ? "bolt" : "unbolt", this);
    }

    public void Bang(int n)
    {
        BangCount++;
        mover = 1;
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick(n % 2 == 0 ? "amb_door_slam_far" : "door_close"), HandlePos, 0.95f, Random.Range(0.7f, 0.85f), 1.5f, 40f);
            ae.PlayAt(SoundBank.Pick("door_locked"), HandlePos, 0.5f, 0.8f, 1f, 20f);
        }
        NoiseBus.Emit(HandlePos, 72f, false, "it bang", this);
        if (Hud.Instance) Hud.Instance.Caption("[banging on the door]", HandlePos);
        if (PlayerMotor.Instance && Game.Instance && Vector3.Distance(Game.Instance.PlayerFeet, HandlePos) < 8f) PlayerMotor.Instance.Shake(0.35f);
        // the leaf jumps in its frame
        Angle = hingeSign * 2.5f; target = 0f; speed = 60f; Apply();
    }

    public void BreakBolt()
    {
        bolted = false;
        mover = 1;
        UpdateObstacle();
        target = 98f * hingeSign;
        speed = 420f;
        var ae = AudioEngine.Instance;
        if (ae) { ae.PlayAt(SoundBank.Pick("glass_break"), HandlePos, 0.6f, 0.6f, 1f, 30f); ae.PlayAt(SoundBank.Pick("amb_door_slam_far"), HandlePos, 1f, 0.7f, 1.5f, 40f); }
        NoiseBus.Emit(HandlePos, 80f, false, "it break bolt", this);
        if (PlayerMotor.Instance) PlayerMotor.Instance.Shake(0.6f);
    }

    public void Slam() { if (!locked && !bolted && !Held) { mover = 2; target = 0f; speed = 400f; } }
    public void Nudge(float deg) { if (!locked && !bolted && !stuck && !Held) { mover = 2; target = Mathf.Clamp(Angle + deg, -maxAngle, maxAngle); if (hingeSign > 0) target = Mathf.Max(0f, target); else target = Mathf.Min(0f, target); speed = 20f; } }

    void Update()
    {
        float before = Angle;
        Angle = Mathf.MoveTowards(Angle, target, speed * Time.deltaTime);
        Apply();
        float w = Mathf.Abs(Angle - before) / Mathf.Max(Time.deltaTime, 1e-4f);
        // creak and noise depend on how fast it swings: easing it is silent (after a slip the slip made the noise)
        bool quiet = Time.time < quietUntil;
        if (quiet) { if (Mathf.Abs(Angle - target) < 0.01f) quietUntil = 0f; return; }
        if (w > 50f && Time.time - lastCreak > 0.5f)          // easing (42 deg/s) stays under this: silent
        {
            lastCreak = Time.time;
            float db = w > 150f ? 52f : w > 80f ? 38f : 28f;
            Sound(metal ? "door_creak_metal" : "door_creak", Mathf.InverseLerp(20f, 200f, w) * 0.5f + 0.08f);
            NoiseBus.Emit(HandlePos, db, mover == 0, mover == 1 ? "it door swing" : "door swing", this);
        }
        // slam shut
        if (Mathf.Abs(before) > 3f && Mathf.Abs(Angle) < 0.5f && w > 120f)
        {
            Sound(metal ? "door_close_metal" : "door_close", 0.8f);
            NoiseBus.Emit(HandlePos, 65f, mover == 0, mover == 1 ? "it slam" : "slam", this);
        }
        else if (Mathf.Abs(before) > 1f && Angle == 0f && target == 0f && w > EaseSpeed * 1.5f)
        {
            Sound(metal ? "door_close_metal" : "door_close", 0.25f);
            NoiseBus.Emit(HandlePos, 34f, mover == 0, mover == 1 ? "it door shut" : "door shut", this);
        }
        else if (Mathf.Abs(before) > 0.5f && Angle == 0f && target == 0f)
            Sound(metal ? "door_close_metal" : "door_close", 0.06f);      // eased shut: the faintest latch
    }

    void Apply() { if (leaf) leaf.localRotation = Quaternion.Euler(0f, Angle, 0f); }

    void Sound(string name, float vol)
    {
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(name), HandlePos, vol, Random.Range(0.94f, 1.06f), 1f, 30f);
    }
}
