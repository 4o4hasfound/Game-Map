using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A hotel telephone. It can be rung from the front office switchboard, or dialled from another phone (the guest
// rooms, the payphones in the telephone room). A ringing phone is loud (75 dB): the listener walks to it, stands
// over it listening, and rips the receiver out. A ringing phone far from you is the best lure in the hotel.
public class Phone : Interactable
{
    public static readonly List<Phone> All = new List<Phone>();
    public string room;
    public bool dialable = true;
    public bool OffHook { get; private set; }
    public bool Ringing => ringsLeft > 0;
    public Transform receiver;

    int ringsLeft;
    float nextRing;
    int dialIndex;
    float holdT;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static Phone Find(string room) => All.Find(p => p.room == room);

    public void Ring(int rings = 12)
    {
        if (OffHook || Ringing) return;
        ringsLeft = rings;
        nextRing = Time.time + 0.6f;
    }

    // the listener rips the receiver out
    public void Smash()
    {
        ringsLeft = 0;
        OffHook = true;
        if (receiver) { receiver.localPosition += new Vector3(0.15f, -0.05f, 0.1f); receiver.localRotation = Quaternion.Euler(20, 70, 95); }
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("phone_hangup"), transform.position, 0.8f, 0.8f, 1f, 30f);
            ae.PlayAt(SoundBank.Pick("amb_debris"), transform.position, 0.5f, 0.9f, 1f, 25f);
        }
        NoiseBus.Emit(transform.position, 55f, false, "it smash phone", this);
        if (Hud.Instance) Hud.Instance.Caption("[the phone is torn off]", transform.position);
        if (Game.Instance) Game.Instance.Log("it tore off phone " + room);
    }

    void Update()
    {
        if (ringsLeft > 0 && Time.time >= nextRing)
        {
            ringsLeft--;
            nextRing = Time.time + 3.0f;
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Get(room.StartsWith("payphone") ? "payphone_ring" : "phone_ring"), transform.position, 0.75f, 1f, 1.5f, 40f);
            NoiseBus.Emit(transform.position, 75f, false, "phone " + room, this);
            if (Hud.Instance) Hud.Instance.Caption("[phone ringing]", transform.position);
        }
    }

    // ---- the player using this phone to call another
    List<string> Targets => All.Where(p => p && p != this && p.dialable && !p.OffHook && !p.room.StartsWith("payphone")).Select(p => p.room).OrderBy(r => r).ToList();
    string Target { get { var t = Targets; return t.Count == 0 ? null : t[dialIndex % t.Count]; } }
    static string Name(string r) => r == null ? "-" : char.IsDigit(r[0]) ? "Room " + r : r;
    public override string Prompt => OffHook ? "Torn off" : Target == null ? "No line" : $"Tap: choose ({Name(Target)})   Hold: ring {Name(Target)}";
    public override bool Usable => !OffHook && Target != null;
    public override float HoldProgress => holdT > 0f ? holdT / 0.7f : -1f;
    public override void Tap()
    {
        dialIndex++;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("elev_button"), transform.position, 0.3f, 1.6f, 0.5f, 6f);
    }
    public override void Hold(float dt, Vector2 mouse)
    {
        if (holdT < 0f) return;
        holdT += dt;
        if (holdT >= 0.7f)
        {
            holdT = -1f;
            var p = Find(Target);
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("phone_dial_tone"), transform.position, 0.25f, 1f, 0.5f, 6f);
            if (p) p.Ring();
            if (Game.Instance) Game.Instance.Log("called " + Target + " from " + room);
        }
    }
    public override void Release() { holdT = 0f; }
}
