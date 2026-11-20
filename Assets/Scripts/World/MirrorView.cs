using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A planar mirror: a reflection camera renders what the player would see in it (including your own face on the
// avatar, worn away a little more each time you are caught).
public class MirrorView : MonoBehaviour
{
    public static readonly List<MirrorView> All = new List<MirrorView>();
    public Renderer glass;            // quad facing +forward (into the room)
    public Vector2 size = new Vector2(0.8f, 0.6f);
    Camera mirrorCam;
    RenderTexture rt;
    public bool VisibleNow { get; private set; }
    static MaterialPropertyBlock mpb;

    void OnEnable()
    {
        All.Add(this);
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }
    void OnDisable()
    {
        All.Remove(this);
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
    }

    void Begin(ScriptableRenderContext ctx, Camera c) { if (c == mirrorCam) GL.invertCulling = true; }
    void End(ScriptableRenderContext ctx, Camera c) { if (c == mirrorCam) GL.invertCulling = false; }

    void Start()
    {
        var go = new GameObject("MirrorCamera");
        go.transform.SetParent(transform, false);
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;
        rt = new RenderTexture(480, 270, 16, RenderTextureFormat.ARGB32) { name = "Mirror" };
        mirrorCam.targetTexture = rt;
        mirrorCam.cullingMask = ~((1 << 5) | (1 << 16));
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = Color.black;
        mpb ??= new MaterialPropertyBlock();
        glass.GetPropertyBlock(mpb);
        mpb.SetTexture("_MainTex", rt);
        glass.SetPropertyBlock(mpb);
    }

    void LateUpdate()
    {
        var main = Game.Instance ? Game.Instance.PlayerCamera : Camera.main;
        if (!main || !mirrorCam) return;
        // only render while the mirror is on screen and close
        var c = glass.bounds.center;
        var v = main.WorldToViewportPoint(c);
        float dist = Vector3.Distance(main.transform.position, c);
        Vector3 n = -transform.forward;     // the quad faces the room with its back (-Z) side
        bool facing = Vector3.Dot(main.transform.position - c, n) > 0.05f;
        VisibleNow = facing && v.z > 0f && dist < 7f && v.x > -0.3f && v.x < 1.3f && v.y > -0.3f && v.y < 1.3f;
        mirrorCam.enabled = VisibleNow;
        if (!VisibleNow) return;
        mirrorCam.fieldOfView = main.fieldOfView;
        mirrorCam.aspect = main.aspect;
        mirrorCam.nearClipPlane = main.nearClipPlane;
        mirrorCam.farClipPlane = 40f;
        float d = -Vector3.Dot(n, c);
        var plane = new Vector4(n.x, n.y, n.z, d);
        var R = Reflection(plane);
        mirrorCam.transform.position = R.MultiplyPoint(main.transform.position);
        mirrorCam.worldToCameraMatrix = main.worldToCameraMatrix * R;
        // oblique near plane on the mirror so nothing behind it shows
        var cpos = mirrorCam.worldToCameraMatrix.MultiplyPoint(c + n * 0.01f);
        var cnorm = mirrorCam.worldToCameraMatrix.MultiplyVector(n).normalized;
        var clip = new Vector4(cnorm.x, cnorm.y, cnorm.z, -Vector3.Dot(cpos, cnorm));
        mirrorCam.projectionMatrix = main.CalculateObliqueMatrix(clip);
    }

    static Matrix4x4 Reflection(Vector4 p)
    {
        var m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y; m.m02 = -2f * p.x * p.z; m.m03 = -2f * p.w * p.x;
        m.m10 = -2f * p.y * p.x; m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z; m.m13 = -2f * p.w * p.y;
        m.m20 = -2f * p.z * p.x; m.m21 = -2f * p.z * p.y; m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
        return m;
    }

    // can the player see point p in this mirror right now?
    public bool ShowsToPlayer(Vector3 p, Vector3 eye)
    {
        if (!VisibleNow) return false;
        Vector3 n = -transform.forward, c = glass.bounds.center;
        if (Vector3.Dot(p - c, n) <= 0f) return false;               // behind the mirror
        Vector3 pr = p - 2f * Vector3.Dot(p - c, n) * n;              // reflected point
        var dir = pr - eye;
        float denom = Vector3.Dot(dir, n);
        if (Mathf.Abs(denom) < 1e-4f) return false;
        float t = Vector3.Dot(c - eye, n) / denom;
        if (t <= 0f || t >= 1f) return false;
        Vector3 hit = eye + dir * t;
        var local = transform.InverseTransformPoint(hit);
        if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.y) > size.y * 0.5f) return false;
        if (Physics.Linecast(eye, hit - n * -0.02f, Layers.Blockers, QueryTriggerInteraction.Ignore)) return false;
        if (Physics.Linecast(hit + n * 0.05f, p, Layers.Blockers, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }
}
