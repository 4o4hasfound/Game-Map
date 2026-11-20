using System.Collections.Generic;
using UnityEngine;

// A security camera. It renders into a small texture shown on monitors (reception quad, security office wall,
// the bedroom TV in the prologue). It renders only while a screen showing it is on the player's screen, at a CCTV
// frame rate (infrared: it sees in the dark).
public class CctvCamera : MonoBehaviour
{
    public static readonly List<CctvCamera> All = new List<CctvCamera>();
    public string label = "CAM 1";
    public Camera cam;
    public RenderTexture Texture { get; private set; }
    public int width = 320, height = 240;
    public float fps = 15f;
    public bool infrared = true;   // CCTV with IR lamps sees in the dark
    public float WantedUntil;     // a screen showing this camera was visible recently
    float nextFrame;

    void Awake()
    {
        Texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = label, filterMode = FilterMode.Bilinear };
        if (!cam) cam = GetComponentInChildren<Camera>();
        cam.targetTexture = Texture;
        cam.enabled = false;
        All.Add(this);
    }

    void OnDestroy() { All.Remove(this); if (Texture) Texture.Release(); }

    public static CctvCamera Find(string label) => All.Find(c => c.label == label);

    void LateUpdate()
    {
        bool wanted = Time.time < WantedUntil;
        cam.enabled = false;
        if (wanted && Time.time >= nextFrame)
        {
            nextFrame = Time.time + 1f / fps;
            cam.enabled = true;          // renders this frame only
        }
    }

    // can this camera see a point (frustum, distance, nothing in the way)?
    public bool Sees(Vector3 p, float maxDist = 30f)
    {
        var v = cam.WorldToViewportPoint(p);
        if (v.z <= 0.05f || v.z > maxDist || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return false;
        var o = cam.transform.position;
        var d = p - o;
        return !Physics.Raycast(o, d.normalized, d.magnitude - 0.25f, Layers.Blockers, QueryTriggerInteraction.Ignore);
    }
}
