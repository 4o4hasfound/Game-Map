using System.Collections.Generic;
using UnityEngine;

// One CRT screen showing a CCTV camera: the way to scout where the listener is before you move.
public class MonitorScreen : MonoBehaviour
{
    public static readonly List<MonitorScreen> All = new List<MonitorScreen>();
    public CctvCamera source;
    public Renderer screen;
    public bool on = true;
    public float clean;            // 1: a clean picture (the tape on your TV): no scanlines, noise or flicker
    public float VisibleUntil { get; private set; }
    static MaterialPropertyBlock mpb;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start() { Refresh(); }

    public void Show(CctvCamera cam) { source = cam; Refresh(); }

    // no signal (snow 0..1) and bad tracking (the picture rolls by this much); the bedroom TV in the prologue
    public void Signal(float snow, float roll)
    {
        if (!screen) return;
        mpb ??= new MaterialPropertyBlock();
        screen.GetPropertyBlock(mpb);
        mpb.SetFloat("_Snow", snow);
        mpb.SetFloat("_Roll", roll);
        screen.SetPropertyBlock(mpb);
    }

    public void Refresh()
    {
        if (!screen) return;
        mpb ??= new MaterialPropertyBlock();
        screen.GetPropertyBlock(mpb);
        if (source && source.Texture) mpb.SetTexture("_MainTex", source.Texture);
        mpb.SetFloat("_On", on ? 1f : 0f);
        mpb.SetFloat("_Clean", clean);
        screen.SetPropertyBlock(mpb);
    }

    void Update()
    {
        if (!on || !source || !screen) return;
        // is the screen itself on the player's screen and not hidden?
        var cam = Game.Instance ? Game.Instance.PlayerCamera : Camera.main;
        if (!cam) return;
        var c = screen.bounds.center;
        var v = cam.WorldToViewportPoint(c);
        float dist = Vector3.Distance(cam.transform.position, c);
        bool vis = v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f && dist < 9f &&
                   !Physics.Raycast(cam.transform.position, (c - cam.transform.position).normalized, dist - 0.15f, Layers.Blockers, QueryTriggerInteraction.Ignore);
        if (vis)
        {
            VisibleUntil = Time.time + 0.1f;
            source.WantedUntil = Time.time + 0.5f;
        }
    }

    public bool OnPlayerScreen => on && Time.time < VisibleUntil;
}
