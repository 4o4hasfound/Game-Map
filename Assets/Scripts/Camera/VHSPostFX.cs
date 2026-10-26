using UnityEngine;

// Drives VHSFX.shader: the HW1 body-cam image chain (auto exposure with adaptation, motion blur, bloom, lens)
// followed by the VHS tape model. Tracking errors come and go on their own and spike during the elevator
// "fast-forward" and when the camera is jolted.
[RequireComponent(typeof(Camera))]
public class VHSPostFX : MonoBehaviour
{
    public Material material;

    [Header("Exposure")]
    public float key = 0.09f;
    public float minExposure = 0.45f, maxExposure = 3.4f, adaptSpeed = 1.1f;

    [Header("Bloom")]
    public float bloomThreshold = 1.6f, bloomKnee = 0.5f, bloomIntensity = 0.1f;

    [Header("Lens")]
    public float distortion = 0.08f;
    public float vignette = 0.55f;

    [Header("Tape")]
    public float jitter = 0.5f;
    public float wobble = 0.35f;
    public float headSwitch = 0.7f;
    public float chromaBlur = 2.2f;
    public float chromaShift = 1.4f;
    public float halo = 0.7f;
    public float saturation = 0.68f;
    public float blackLevel = 0.045f;
    public float grain = 0.016f;      // horizontal tape-noise streaks, fixed amplitude
    public float chromaNoise = 0.025f;
    public float dropouts = 0.00025f;
    public float scanlines = 0.04f;
    public Color tint = new Color(1.02f, 0.99f, 0.95f);

    [Header("Air")]
    public float fogDensity = 0.012f;
    public Color fogColor = new Color(0.010f, 0.011f, 0.010f);

    [Header("Motion blur")]
    public float shutterTime = 1f / 50f;
    public float maxBlur = 0.03f;

    public static float FastForward;       // 0..1, set by the elevator
    public static float ExtraTracking;     // 0..1 bursts (impacts, power cuts)
    public static bool DiagSkipTape, DiagSkipMotion, DiagSkipBloom;   // -stress -ab GPU attribution only

    Camera cam;
    RenderTexture adaptA, adaptB;
    bool first = true;
    Quaternion prevRot;
    bool hasPrev;
    float tracking, trackPos, nextGlitch;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        cam.allowHDR = true;
        cam.allowMSAA = false;
        cam.depthTextureMode |= DepthTextureMode.Depth;
        first = true;
        nextGlitch = Time.time + Random.Range(10f, 30f);
    }

    void OnDisable()
    {
        if (adaptA) adaptA.Release();
        if (adaptB) adaptB.Release();
        adaptA = adaptB = null;
    }

    static RenderTexture Temp(int w, int h, RenderTextureFormat fmt)
    {
        var rt = RenderTexture.GetTemporary(Mathf.Max(1, w), Mathf.Max(1, h), 0, fmt);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        return rt;
    }

    void Update()
    {
        // occasional tracking trouble, like an old tape
        if (Time.time > nextGlitch)
        {
            nextGlitch = Time.time + Random.Range(35f, 110f);
            ExtraTracking = Mathf.Max(ExtraTracking, Random.Range(0.12f, 0.4f));
        }
        float target = Mathf.Max(ExtraTracking, FastForward * 0.8f);
        tracking = Mathf.MoveTowards(tracking, target, Time.deltaTime * 1.5f);
        ExtraTracking = Mathf.MoveTowards(ExtraTracking, 0f, Time.deltaTime * 0.35f);
        trackPos = Mathf.Repeat(trackPos + Time.deltaTime * (0.07f + tracking * 0.5f), 1.2f) ;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!material)
        {
            Graphics.Blit(src, dst);
            return;
        }
        var m = material;
        float dt = Time.unscaledDeltaTime;
        m.SetFloat("_Key", key);
        m.SetFloat("_MinExposure", minExposure);
        m.SetFloat("_MaxExposure", maxExposure);
        m.SetFloat("_Frame", Time.frameCount % 997);
        m.SetFloat("_TimeU", Time.unscaledTime % 1000f);

        var lum = Temp(64, 64, RenderTextureFormat.RHalf);
        Graphics.Blit(src, lum, m, 0);
        int size = 64;
        while (size > 1)
        {
            size /= 2;
            var next = Temp(size, size, RenderTextureFormat.RHalf);
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

        if (!hasPrev)
        {
            prevRot = transform.rotation;
            hasPrev = true;
        }
        float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        m.SetFloat("_TanY", tanY);
        m.SetFloat("_TanX", tanY * cam.aspect);
        m.SetMatrix("_CurRot", Matrix4x4.Rotate(transform.rotation));
        m.SetMatrix("_PrevInvRot", Matrix4x4.Rotate(Quaternion.Inverse(prevRot)));
        m.SetFloat("_BlurScale", dt > 1e-4f ? Mathf.Min(shutterTime / dt, 1.5f) : 0f);
        m.SetFloat("_MaxBlur", maxBlur);
        m.SetFloat("_FogDensity", fogDensity);
        m.SetColor("_FogColor", fogColor);
        var blurred = Temp(src.width, src.height, src.format);
        if (DiagSkipMotion) Graphics.Blit(src, blurred); else Graphics.Blit(src, blurred, m, 5);
        prevRot = transform.rotation;

        m.SetFloat("_Threshold", bloomThreshold);
        m.SetFloat("_Knee", bloomKnee);
        const int levels = 6;
        var chain = new RenderTexture[levels];
        int w = src.width / 2, h = src.height / 2;
        chain[0] = Temp(w, h, RenderTextureFormat.DefaultHDR);
        Graphics.Blit(blurred, chain[0], m, 2);
        int used = 1;
        for (int i = 1; i < levels && !DiagSkipBloom; i++)
        {
            w /= 2;
            h /= 2;
            if (w < 2 || h < 2) break;
            chain[i] = Temp(w, h, RenderTextureFormat.DefaultHDR);
            Graphics.Blit(chain[i - 1], chain[i], m, 3);
            used++;
        }
        for (int i = used - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], m, 4);
        m.SetTexture("_BloomTex", chain[0]);
        m.SetFloat("_BloomIntensity", bloomIntensity);

        m.SetFloat("_Distortion", distortion);
        float r2c = 0.25f * cam.aspect * cam.aspect + 0.25f;
        m.SetFloat("_DistortScale", 1f / (1f + distortion * r2c));
        m.SetFloat("_Vignette", vignette);
        m.SetFloat("_Grain", grain);
        m.SetFloat("_ChromaNoise", chromaNoise);
        m.SetFloat("_Jitter", jitter * (1f + tracking * 3f));
        m.SetFloat("_Wobble", wobble * (1f + tracking * 2f));
        m.SetFloat("_Tracking", tracking);
        m.SetFloat("_TrackPos", trackPos - 0.1f);
        m.SetFloat("_HeadSwitch", headSwitch);
        m.SetFloat("_ChromaBlur", chromaBlur);
        m.SetFloat("_ChromaShift", chromaShift);
        m.SetFloat("_Halo", halo);
        m.SetFloat("_Saturation", saturation);
        m.SetFloat("_BlackLevel", blackLevel);
        m.SetFloat("_Dropouts", dropouts + tracking * 0.003f);
        m.SetFloat("_Scanlines", scanlines);
        m.SetFloat("_FF", FastForward);
        m.SetColor("_Tint", tint);
        if (DiagSkipTape) Graphics.Blit(blurred, dst); else Graphics.Blit(blurred, dst, m, 6);
        // the camcorder's live feed for the spawn TV (a 320x240 copy, only while that screen is in view)
        if (LiveFeedTV.Wanted && LiveFeedTV.Feed) Graphics.Blit(blurred, LiveFeedTV.Feed, m, 6);

        for (int i = 0; i < used; i++) RenderTexture.ReleaseTemporary(chain[i]);
        RenderTexture.ReleaseTemporary(blurred);
    }
}
