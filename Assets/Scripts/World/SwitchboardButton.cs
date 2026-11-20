using System.Collections.Generic;
using UnityEngine;
// One button on the front office switchboard: rings that room's telephone (a lure far from you).
public class SwitchboardButton : Interactable
{
    public string room;
    public Renderer lamp;
    static MaterialPropertyBlock mpb;
    public override string Prompt { get { var p = Phone.Find(room); return p && p.OffHook ? $"{room}: no answer (line dead)" : $"Ring {(char.IsDigit(room[0]) ? "Room " + room : room)}"; } }
    public override void Tap()
    {
        InteractAnim.Play(InteractAnim.Kind.Press, transform);
        var p = Phone.Find(room);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("radio_click"), transform.position, 0.35f, 1.3f, 0.5f, 8f);
        if (p) p.Ring();
        if (Game.Instance) Game.Instance.Log("switchboard " + room);
    }
    void Update()
    {
        if (!lamp) return;
        var p = Phone.Find(room);
        mpb ??= new MaterialPropertyBlock();
        bool lit = p && (p.Ringing ? Mathf.Repeat(Time.time, 0.6f) < 0.3f : p.OffHook);
        mpb.SetColor("_EmissionColor", lit ? (p.OffHook ? new Color(1f, 0.15f, 0.05f) : new Color(1f, 0.7f, 0.1f)) * 2f : Color.black);
        mpb.SetColor("_BaseColor", lit ? Color.white : new Color(0.2f, 0.18f, 0.15f));
        lamp.SetPropertyBlock(mpb);
    }
}
