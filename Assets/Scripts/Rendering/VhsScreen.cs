using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// The camcorder picture, ported from the earlier HotelGame's VHSPostFX: the game camera renders (HDR) into a
// texture; after it has rendered, this runs the image chain (auto exposure with adaptation, rotational motion blur,
// bloom) and the VHS tape model into the picture shown full screen. The tracking band rolls in when the thing that
// hunts you is close on the same floor; being caught or pulled into the tape runs fast-forward bars.
public class VhsScreen : MonoBehaviour
{
    public static VhsScreen Instance { get; private set; }
    public Camera gameCamera;
    public Material vhsMat;          // Hidden/TheTape/VHSFX

    [Header("Exposure")]
    public float key = 0.065f, minExposure = 0.3f, maxExposure = 3.8f, adaptSpeed = 1.1f;
    // highlight protection: the meter also follows this share of the (clamped) average brightness, so a brightly lit
    // part of the frame isn't driven to white by a dark surrounding
    public float highlightGuard = 0.35f;    // darker than a camcorder: a horror hotel
    [Header("Bloom")]
    public float bloomThreshold = 2.4f, bloomKnee = 0.5f, bloomIntensity = 0.035f;
    [Header("Lens")]
    public float distortion = 0.08f, vignette = 0.55f;
    [Header("Tape")]
    public float jitter = 0.5f, wobble = 0.35f, headSwitch = 0.7f, chromaBlur = 2.2f, chromaShift = 1.4f, halo = 0.7f;
    public float saturation = 0.66f, blackLevel = 0.018f, grain = 0.016f, chromaNoise = 0.025f, dropouts = 0.00025f, scanlines = 0.04f;
    public Color tint = new Color(1.02f, 0.99f, 0.95f);
    [Header("Camcorder")]
    public float smear = 0.012f, ghost = 0.15f, overscan = 0.012f;
    [Header("Motion blur")]
    public float shutterTime = 1f / 50f, maxBlur = 0.03f;
    // 1 = your house, real life: a clean, bright, warm picture with none of the tape (no grain, wobble, tracking,
    // head-switch, colour bleed, focus hunting, vignette, drained colour). The game sets it in the prologue and fades
    // it to 0 as the TV pulls you in; the hotel is on the tape (0).
    [Range(0f, 1f)] public float home;

    RenderTexture rt, outRt, outPrev, adaptA, adaptB;
    float angSpeed, focusT = -10f, nextFlutter, wasFast;
    RawImage img;
    int lastW, lastH, lastQ = -1;
    float tearUntil, tearStrength, tracking, trackPos = 0.8f;
    bool first = true, hasPrev;
    Quaternion prevRot;
    public float Tracking => tracking;

    void Awake()
    {
        Instance = this;
        var go = new GameObject("TapePicture");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        img = new GameObject("Picture").AddComponent<RawImage>();
        img.transform.SetParent(go.transform, false);
        var r = img.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        // a camera that only clears the back buffer, so the overlay canvases are drawn
        var sc = new GameObject("ScreenCamera").AddComponent<Camera>();
        sc.transform.SetParent(transform, false);
        sc.cullingMask = 0;
        sc.clearFlags = CameraClearFlags.SolidColor;
        sc.backgroundColor = Color.black;
        sc.depth = 10;
        sc.useOcclusionCulling = false;
    }

    void OnEnable() { RenderPipelineManager.endCameraRendering += AfterCamera; }
    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        if (adaptA) adaptA.Release();
        if (adaptB) adaptB.Release();
        adaptA = adaptB = null;
    }

    public void Tear(float seconds, bool strong)
    {
        tearUntil = Time.unscaledTime + seconds;
        tearStrength = strong ? 1f : 0.45f;
    }

    void Resize()
    {
        int q = Settings.Picture;
        float scale = q == 0 ? 1f : q == 1 ? 0.75f : 0.6f;
        int h = Mathf.Clamp(Mathf.RoundToInt(Screen.height * scale), 480, q == 0 ? 1080 : 720);
        int w = Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height));
        if (rt && w == lastW && h == lastH && q == lastQ) return;
        if (rt) { gameCamera.targetTexture = null; rt.Release(); }
        if (outRt) outRt.Release();
        if (outPrev) outPrev.Release();
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf) { name = "Scene", filterMode = FilterMode.Bilinear };
        int oh = Mathf.Clamp(Screen.height, 480, 1080);
        int ow = Mathf.RoundToInt(oh * (float)Screen.width / Mathf.Max(1, Screen.height));
        outRt = new RenderTexture(ow, oh, 0, RenderTextureFormat.ARGB32) { name = "Tape", filterMode = FilterMode.Bilinear };
        outPrev = new RenderTexture(ow, oh, 0, RenderTextureFormat.ARGB32) { name = "TapePrev", filterMode = FilterMode.Bilinear };
        gameCamera.targetTexture = rt;
        gameCamera.allowHDR = true;
        img.texture = outRt;
        lastW = w; lastH = h; lastQ = q;
    }

    void LateUpdate()
    {
        if (!gameCamera) return;
        Resize();
        // the tracking band: near the listener on your floor, while being pulled in / caught, and in the finale
        var g = Game.Instance;
        float near = 0f;
        var it = Listener.Instance;
        if (g && it && it.Visible && g.State == Game.Phase.Hotel)
        {
            var d = it.transform.position - g.PlayerFeet;
            if (Mathf.Abs(d.y) < 2.5f) near = Mathf.Clamp01(1f - (new Vector2(d.x, d.z).magnitude - 2f) / 9f);
        }
        float target = near * 0.55f;
        if (Time.unscaledTime < tearUntil) target = Mathf.Max(target, tearStrength * Mathf.Clamp01((tearUntil - Time.unscaledTime) * 2f));
        if (g && g.Finale) target = Mathf.Max(target, 0.25f);
        // the tape flutters now and then on its own (a crease, a worn patch)
        if (Time.unscaledTime > nextFlutter) { nextFlutter = Time.unscaledTime + Random.Range(40f, 95f); if (g && g.State == Game.Phase.Hotel) Tear(Random.Range(0.25f, 0.5f), false); }
        tracking = Mathf.MoveTowards(tracking, target, Time.unscaledDeltaTime * 2.5f);
        trackPos = Mathf.Lerp(trackPos, 0.5f + 0.62f - 0.42f * tracking, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
    }

    static RenderTexture Temp(int w, int h, RenderTextureFormat fmt)
    {
        var t = RenderTexture.GetTemporary(Mathf.Max(1, w), Mathf.Max(1, h), 0, fmt);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    void AfterCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != gameCamera || !rt || !outRt || !vhsMat) return;
        var prevActive = RenderTexture.active;
        (outRt, outPrev) = (outPrev, outRt);           // last frame's picture feeds the ghosting and the interlace
        Process(rt, outRt);
        img.texture = outRt;
        RenderTexture.active = prevActive;
    }

    void Process(RenderTexture src, RenderTexture dst)
    {
        var m = vhsMat;
        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        int q = Settings.Picture;                    // 0 clean, 1 tape, 2 worn
        float k = (q == 0 ? 0.35f : q == 1 ? 1f : 1.5f) * (1f - home);
        float hm = home;
        m.SetFloat("_Key", Mathf.Lerp(key, 0.085f, hm));
        m.SetFloat("_MinExposure", Mathf.Lerp(minExposure, 0.4f, hm));
        m.SetFloat("_MaxExposure", maxExposure);
        m.SetFloat("_Guard", Mathf.Lerp(highlightGuard, 0.12f, hm));
        m.SetFloat("_Bright", Settings.Brightness);
        m.SetFloat("_Frame", Time.frameCount % 997);
        m.SetFloat("_TimeU", Time.unscaledTime % 1000f);

        // auto exposure: log luminance, down to 1x1, adapted over time
        var lum = Temp(64, 64, RenderTextureFormat.RGHalf);
        Graphics.Blit(src, lum, m, 0);
        int size = 64;
        while (size > 1)
        {
            size /= 2;
            var next = Temp(size, size, RenderTextureFormat.RGHalf);
            Graphics.Blit(lum, next);
            RenderTexture.ReleaseTemporary(lum);
            lum = next;
        }
        if (!adaptA)
        {
            adaptA = new RenderTexture(1, 1, 0, RenderTextureFormat.RHalf);
            adaptB = new RenderTexture(1, 1, 0, RenderTextureFormat.RHalf);
            first = true;
        }
        m.SetTexture("_PrevLumTex", adaptA);
        m.SetFloat("_First", first ? 1f : 0f);
        m.SetFloat("_AdaptSpeed", adaptSpeed);
        m.SetFloat("_DeltaTime", dt);
        Graphics.Blit(lum, adaptB, m, 1);
        (adaptA, adaptB) = (adaptB, adaptA);
        RenderTexture.ReleaseTemporary(lum);
        first = false;
        m.SetTexture("_LumTex", adaptA);

        // rotational motion blur
        var rot = gameCamera.transform.rotation;
        if (!hasPrev) { prevRot = rot; hasPrev = true; }
        float ang = Quaternion.Angle(prevRot, rot) / dt;
        angSpeed = Mathf.Lerp(angSpeed, ang, 1f - Mathf.Exp(-dt * 10f));
        // autofocus hunts after a fast pan, and now and then by itself
        if (angSpeed > 120f) wasFast = 1f;
        else if (wasFast > 0f && angSpeed < 30f) { wasFast = 0f; focusT = Time.unscaledTime; }
        if (Random.value < dt / 60f) focusT = Time.unscaledTime;
        float ft = Time.unscaledTime - focusT;
        float focus = ft < 0.7f ? Mathf.Sin(ft / 0.7f * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(ft * 30f)) : 0f;
        m.SetFloat("_Focus", Mathf.Max(0f, focus) * Mathf.Min(1f, k));
        m.SetFloat("_Motion", Mathf.Clamp01((angSpeed - 60f) / 240f) * Mathf.Min(1f, k));
        float wb = Mathf.PerlinNoise(Time.unscaledTime * 0.03f, 3.7f);
        // the camcorder's wandering white balance; at home a steady, warm one
        m.SetColor("_WB", Color.Lerp(Color.Lerp(new Color(1.07f, 1.0f, 0.86f), new Color(0.94f, 1.05f, 0.96f), wb), new Color(1.02f, 1.0f, 0.95f), hm));
        float tanY = Mathf.Tan(gameCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        m.SetFloat("_TanY", tanY);
        m.SetFloat("_TanX", tanY * gameCamera.aspect);
        m.SetMatrix("_CurRot", Matrix4x4.Rotate(rot));
        m.SetMatrix("_PrevInvRot", Matrix4x4.Rotate(Quaternion.Inverse(prevRot)));
        m.SetFloat("_BlurScale", Mathf.Min(shutterTime / dt, 1.5f));
        m.SetFloat("_MaxBlur", maxBlur);
        var blurred = Temp(src.width, src.height, src.format);
        Graphics.Blit(src, blurred, m, 5);
        prevRot = rot;

        // bloom
        m.SetFloat("_Threshold", bloomThreshold);
        m.SetFloat("_Knee", bloomKnee);
        const int levels = 6;
        var chain = new RenderTexture[levels];
        int w = src.width / 2, h = src.height / 2;
        chain[0] = Temp(w, h, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(blurred, chain[0], m, 2);
        int used = 1;
        for (int i = 1; i < levels; i++)
        {
            w /= 2; h /= 2;
            if (w < 2 || h < 2) break;
            chain[i] = Temp(w, h, RenderTextureFormat.ARGBHalf);
            Graphics.Blit(chain[i - 1], chain[i], m, 3);
            used++;
        }
        // the CCD smear comes from the quarter-resolution bright pass, before the up-sampling adds the levels together
        var smearRt = Temp(Mathf.Max(2, src.width / 4), Mathf.Max(2, src.height / 8), RenderTextureFormat.ARGBHalf);
        Graphics.Blit(used > 1 ? chain[1] : chain[0], smearRt, m, 7);
        m.SetTexture("_SmearTex", smearRt);
        m.SetFloat("_Smear", smear * Mathf.Min(1f, k));
        for (int i = used - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], m, 4);
        m.SetTexture("_BloomTex", chain[0]);
        m.SetFloat("_BloomIntensity", bloomIntensity);

        // the tape
        float ff = Time.unscaledTime < tearUntil ? tearStrength * Mathf.Clamp01((tearUntil - Time.unscaledTime) * 1.5f) : 0f;
        float dist = Mathf.Lerp(distortion, 0f, hm);
        m.SetFloat("_Distortion", dist);
        float r2c = 0.25f * gameCamera.aspect * gameCamera.aspect + 0.25f;
        m.SetFloat("_DistortScale", 1f / (1f + dist * r2c));
        m.SetFloat("_Vignette", Mathf.Lerp(vignette, 0.18f, hm));
        m.SetFloat("_Grain", grain * k);
        m.SetFloat("_ChromaNoise", chromaNoise * k);
        m.SetFloat("_Jitter", jitter * k * (1f + tracking * 3f));
        m.SetFloat("_Wobble", wobble * k * (1f + tracking * 2f));
        m.SetFloat("_Tracking", tracking * (1f - hm));
        m.SetFloat("_TrackPos", trackPos);
        m.SetFloat("_HeadSwitch", headSwitch * Mathf.Min(1f, k));
        m.SetFloat("_ChromaBlur", chromaBlur * Mathf.Lerp(0.5f, 1f, Mathf.Min(1f, k)));
        m.SetFloat("_ChromaShift", chromaShift * Mathf.Min(1f, k));
        m.SetFloat("_Halo", halo * Mathf.Min(1f, k));
        m.SetFloat("_Saturation", Mathf.Lerp(saturation, 0.9f, hm));
        m.SetFloat("_BlackLevel", Mathf.Lerp(blackLevel, 0f, hm));
        m.SetFloat("_Dropouts", (dropouts * (q == 2 ? 4f : 1f) + tracking * 0.003f) * Mathf.Min(1f, k));
        m.SetFloat("_Scanlines", scanlines * Mathf.Min(1f, k));
        m.SetFloat("_FF", ff);
        m.SetColor("_Tint", Color.Lerp(tint, new Color(1.0f, 0.99f, 0.97f), hm));
        m.SetTexture("_PrevTex", outPrev);
        m.SetFloat("_Ghost", ghost * Mathf.Min(1f, k));
        m.SetFloat("_Overscan", overscan * Mathf.Min(1f, k));
        Graphics.Blit(blurred, dst, m, 6);
        RenderTexture.ReleaseTemporary(smearRt);

        for (int i = 0; i < used; i++) RenderTexture.ReleaseTemporary(chain[i]);
        RenderTexture.ReleaseTemporary(blurred);
    }
}
