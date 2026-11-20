using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Rendering;

// Webcam eye tracking, all on this computer, nothing saved or sent.
//  1. BlazeFace (Unity Inference Engine, Apache-2.0) finds your face and 6 keypoints (eyes, nose, mouth, ears).
//  2. Small patches around each eye are compared with how your open and closed eyes looked at calibration.
//  3. Head direction (nose against the eyes) tells whether you face the screen; head position gives the lean.
//  4. A crop of your face is kept in a GPU texture for your avatar on monitors and mirrors.
public class FaceTracker : MonoBehaviour
{
    public static FaceTracker Instance { get; private set; }
    public ModelAsset model;
    public TextAsset anchorsCsv;
    public Material normMat;      // Hidden/TheTape/FaceNorm: crop + scale to -1..1 / grey for eyes

    public bool Running { get; private set; }
    public bool FacePresent { get; private set; }
    public bool EyesClosed { get; private set; }
    public bool Facing { get; private set; } = true;
    public float Lean { get; private set; }
    public float Closedness { get; private set; }
    public float Score { get; private set; }
    public float Yaw { get; private set; }
    public float PitchV { get; private set; }
    public float Brightness { get; private set; }
    public float InferenceMs { get; private set; }
    public Vector2[] Keypoints { get; } = new Vector2[6];   // 0..1, top-left origin of the square crop
    public Rect Box { get; private set; }
    public RenderTexture FaceTexture { get; private set; }
    public bool Calibrated { get; private set; }
    public string Error { get; private set; } = "";
    // which camera mode is in use, and whether its picture is live (shown on the calibration screen, never the picture)
    public string CameraStatus { get; private set; } = "";
    public bool PictureOk { get; private set; }
    // Some laptop cameras (e.g. "HD User Facing", the Intel MIPI kind) give black frames at 640x480 and only work at
    // their own sizes. Each mode is tried for a few seconds; a mode whose picture stays black or frozen is skipped.
    static readonly Vector3Int[] Modes = { new Vector3Int(640, 480, 30), new Vector3Int(1280, 720, 30), new Vector3Int(1920, 1080, 30), new Vector3Int(0, 0, 0) };
    int modeIndex;
    string device;
    float modeStartedAt, lastFrameAt, darkSince = -1f, nextProbe;
    // how bright the camera picture is (0..1, the average of 49 sample pixels), the gain that brings a dim picture up
    // to a normal exposure for the face model, and whether Unity hands the picture to shaders already decoded to
    // linear colour (then it is encoded back; the model expects ordinary camera values)
    public float PictureLight { get; private set; }
    public float Gain { get; private set; } = 1f;
    public bool DecodedToLinear { get; private set; }
    // The face model only finds upright faces, and how this camera's picture arrives (vertically mirrored or not,
    // decoded to linear or not) can't be known in advance. Until a face is found, each frame tries the next of four
    // readings (upright / flipped x as is / gamma-swapped); the one that finds a face is kept. If the face is lost
    // for a long time the search starts again.
    int variant;
    bool variantLocked;
    int variantFrames;
    float lockedLastSeen;
    public float BestSearchScore { get; private set; }
    bool ExtraFlip => (variant & 1) != 0;
    bool GammaSwap => (variant & 2) != 0;
    public string DetectStatus => variantLocked ? $"face found ({(ExtraFlip ? "flipped" : "upright")}{(GammaSwap ? ", colour swapped" : "")})"
                                                : $"looking for a face (trying reading {variant + 1} of 4, best score {BestSearchScore:0.00})";
    bool colourChecked;
    float nextLightProbe;

    float SampleLight(out float max)
    {
        float sum = 0f; max = 0f; int k = 0;
        for (int y = 1; y < 8; y++)
            for (int x = 1; x < 8; x++)
            {
                var c = cam.GetPixel(cam.width * x / 8, cam.height * y / 8);
                float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                sum += l; max = Mathf.Max(max, l); k++;
            }
        return sum / k;
    }

    // once: does sampling the camera in a shader give the same brightness as its raw pixels, or the linear-decoded
    // (much darker) version? Only an 8x8 average is read back, never the picture.
    void CheckColourSpace()
    {
        colourChecked = true;
        if (QualitySettings.activeColorSpace != ColorSpace.Linear) { DecodedToLinear = false; return; }
        float raw = SampleLight(out _);
        var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        normMat.SetVector("_Crop", new Vector4(1, 1, 0, 0));
        normMat.SetFloat("_Mode", 2f); normMat.SetFloat("_ToGamma", 0f); normMat.SetFloat("_Gain", 1f);
        Graphics.Blit(cam, rt, normMat);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
        t.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        float sum = 0f;
        foreach (var c in t.GetPixels()) sum += 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        Destroy(t);
        float shader = sum / 64f;
        // a decoded picture is roughly raw^2.2: clearly darker than the raw average
        DecodedToLinear = raw > 0.04f && shader < raw * 0.7f;
        Debug.Log($"FaceTracker: camera brightness raw {raw:0.000}, in shader {shader:0.000} -> decoded to linear: {DecodedToLinear}");
    }

    // keep the face model's input at a normal exposure, and tell the shader how to read the camera
    void PrepareInput()
    {
        float now = Time.realtimeSinceStartup;
        if (now >= nextLightProbe)
        {
            nextLightProbe = now + 0.5f;
            PictureLight = SampleLight(out _);
            float want = Mathf.Clamp(0.42f / Mathf.Max(PictureLight, 0.03f), 1f, 5f);
            Gain = Mathf.Lerp(Gain, want, 0.5f);
        }
        if (!colourChecked) CheckColourSpace();
        normMat.SetFloat("_ToGamma", (DecodedToLinear ^ GammaSwap) ? 1f : 0f);
        normMat.SetFloat("_Gain", Gain);
    }
    public WebCamTexture Cam => cam;
    public int Blinks { get; private set; }
    public float Motion { get; private set; }          // head movement, face widths per second (smoothed)
    Vector2 lastCenter; float lastSeen;

    WebCamTexture cam;
    Worker worker;
    Tensor<float> input;
    Tensor<float> outBoxes, outScores;
    bool pending;
    float scheduledAt;
    float[] anchors;
    RenderTexture inRT, eyeL, eyeR;
    Vector2 cropScale, cropOffset;
    readonly float[] featL = new float[EyeW * EyeH], featR = new float[EyeW * EyeH];
    float[] openL, openR, closedL, closedR;
    readonly List<float[]> calibL = new List<float[]>(), calibR = new List<float[]>();
    float yaw0, pitch0, x0 = 0.5f;
    float closedLv, closedRv;
    bool wasClosed;
    float closedStart;
    const int EyeW = 24, EyeH = 12;
    public enum CalibStep { None, Open, Closed, Done }
    public CalibStep Collecting { get; private set; }

    public static FaceTracker Ensure()
    {
        if (Instance) return Instance;
        var prefab = Resources.Load<FaceTrackerConfig>("FaceTrackerConfig");
        var go = new GameObject("FaceTracker");
        var ft = go.AddComponent<FaceTracker>();
        if (prefab) { ft.model = prefab.model; ft.anchorsCsv = prefab.anchors; ft.normMat = prefab.normMat; }
        return ft;
    }

    void Awake() { Instance = this; DontDestroyOnLoad(gameObject); }

    public void Begin()
    {
        if (Running) return;
        try
        {
            if (WebCamTexture.devices.Length == 0) { Error = "No camera found."; return; }
            // skip infrared / depth / virtual cameras: they give black pictures to ordinary apps
            device = WebCamTexture.devices[0].name;
            foreach (var d in WebCamTexture.devices)
            {
                string n = d.name.ToLowerInvariant();
                if (n.Contains(" ir") || n.Contains("infrared") || n.Contains("depth") || n.Contains("virtual") || n.Contains("obs")) continue;
                device = d.name;
                if (d.isFrontFacing) break;
            }
            StartMode(0);
            var m = ModelLoader.Load(model);
            worker = new Worker(m, SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
            input = new Tensor<float>(new TensorShape(1, 128, 128, 3));
            inRT = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBFloat) { name = "FaceIn" };
            eyeL = new RenderTexture(EyeW, EyeH, 0, RenderTextureFormat.ARGB32) { name = "EyeL" };
            eyeR = new RenderTexture(EyeW, EyeH, 0, RenderTextureFormat.ARGB32) { name = "EyeR" };
            FaceTexture = new RenderTexture(96, 96, 0, RenderTextureFormat.ARGB32) { name = "Face" };
            LoadAnchors();
            Running = true;
            Error = "";
        }
        catch (System.Exception e) { Error = e.Message; Running = false; Debug.LogWarning("FaceTracker: " + e); }
    }

    void StartMode(int i)
    {
        if (cam) { cam.Stop(); Destroy(cam); }
        modeIndex = i;
        var m = Modes[i];
        cam = m.x > 0 ? new WebCamTexture(device, m.x, m.y, m.z) : new WebCamTexture(device);
        cam.Play();
        modeStartedAt = lastFrameAt = Time.realtimeSinceStartup;
        darkSince = -1f;
        PictureOk = false;
        CameraStatus = $"{device}: starting ({(m.x > 0 ? m.x + "x" + m.y : "default size")})";
    }

    // a few seconds per mode: is the picture live and not black? (pixels are only averaged here, never kept)
    void CheckPicture()
    {
        float now = Time.realtimeSinceStartup;
        if (cam.didUpdateThisFrame && cam.width >= 32) lastFrameAt = now;
        if (PictureOk || now < nextProbe) return;
        nextProbe = now + 0.4f;
        bool frozen = now - lastFrameAt > 3f || (cam.width < 32 && now - modeStartedAt > 3f);
        bool dark = false;
        if (cam.width >= 32)
        {
            float mean = SampleLight(out float max);
            PictureLight = mean;
            dark = mean < 0.025f && max < 0.08f;
            if (dark) { if (darkSince < 0f) darkSince = now; } else darkSince = -1f;
            if (!dark && !frozen && now - modeStartedAt > 1f)
            {
                PictureOk = true;
                CameraStatus = $"{device}: {cam.width}x{cam.height}, picture OK";
                return;
            }
            CameraStatus = $"{device}: {cam.width}x{cam.height}, " + (dark ? "the picture is black" : "waiting for a picture");
        }
        if (frozen || (darkSince > 0f && now - darkSince > 2.5f))
        {
            if (modeIndex + 1 < Modes.Length) { StartMode(modeIndex + 1); return; }
            Error = frozen ? "The camera gives no picture. Close other apps that use the camera (video calls), then try again."
                           : "The camera picture is black. Open the lens shutter (many laptops have a slider or a key for it), add some light, or close other apps using the camera.";
            Debug.LogWarning("FaceTracker: " + Error + " (" + CameraStatus + ")");
            Stop();
        }
    }

    void LoadAnchors()
    {
        var lines = anchorsCsv ? anchorsCsv.text.Split('\n') : new string[0];
        var a = new List<float>();
        foreach (var l in lines)
        {
            var p = l.Trim().Split(',');
            if (p.Length < 2) continue;
            a.Add(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture));
            a.Add(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        anchors = a.ToArray();
    }

    public void Pause() { if (cam && cam.isPlaying) cam.Pause(); }
    public void Resume() { if (cam && Running) cam.Play(); }
    public void Stop()
    {
        Running = false;
        FacePresent = false;
        if (cam) cam.Stop();
    }

    void OnDestroy()
    {
        if (cam) cam.Stop();
        worker?.Dispose();
        input?.Dispose();
    }

    void Update()
    {
        if (!Running || cam == null || !cam.isPlaying) return;
        CheckPicture();
        if (!Running || cam.width < 32 || !PictureOk) return;
        PrepareInput();
        if (pending)
        {
            if (outBoxes != null && outScores != null && outBoxes.IsReadbackRequestDone() && outScores.IsReadbackRequestDone())
            {
                var boxes = outBoxes.DownloadToArray();
                var scores = outScores.DownloadToArray();
                pending = false;
                InferenceMs = (Time.realtimeSinceStartup - scheduledAt) * 1000f;
                Decode(boxes, scores);
            }
            else if (Time.realtimeSinceStartup - scheduledAt > 0.5f) pending = false;   // give up on a stuck frame
            return;
        }
        if (!cam.didUpdateThisFrame) return;
        // centre square of the camera picture, upright, scaled to -1..1
        float w = cam.width, h = cam.height;
        cropScale = w > h ? new Vector2(h / w, 1f) : new Vector2(1f, w / h);
        cropOffset = new Vector2((1f - cropScale.x) * 0.5f, (1f - cropScale.y) * 0.5f);
        bool flip = cam.videoVerticallyMirrored ^ ExtraFlip;
        normMat.SetVector("_Crop", new Vector4(cropScale.x, flip ? -cropScale.y : cropScale.y, cropOffset.x, flip ? 1f - cropOffset.y : cropOffset.y));
        normMat.SetFloat("_Mode", 0f);
        Graphics.Blit(cam, inRT, normMat);
        TextureConverter.ToTensor(inRT, input, new TextureTransform().SetTensorLayout(TensorLayout.NHWC).SetCoordOrigin(CoordOrigin.TopLeft));
        worker.Schedule(input);
        // peeked outputs belong to the worker; a non-blocking readback request, polled next frames
        outBoxes = worker.PeekOutput("regressors") as Tensor<float>;
        outScores = worker.PeekOutput("classificators") as Tensor<float>;
        outBoxes.ReadbackRequest();
        outScores.ReadbackRequest();
        scheduledAt = Time.realtimeSinceStartup;
        pending = true;
    }

    void Decode(float[] boxes, float[] scores)
    {
        int n = scores.Length, best = -1;
        float bestLogit = -1e9f;
        for (int i = 0; i < n; i++) if (scores[i] > bestLogit) { bestLogit = scores[i]; best = i; }
        Score = 1f / (1f + Mathf.Exp(-Mathf.Clamp(bestLogit, -50f, 50f)));
        bool found = best >= 0 && Score >= 0.5f && anchors != null && anchors.Length >= 2 * n;
        // the reading search: keep a reading that finds a face, otherwise try the next one every few frames
        if (found) { variantLocked = true; lockedLastSeen = Time.unscaledTime; }
        else if (variantLocked && Time.unscaledTime - lockedLastSeen > 12f) variantLocked = false;
        if (!variantLocked)
        {
            BestSearchScore = Mathf.Max(BestSearchScore * 0.995f, Score);
            if (++variantFrames >= 3) { variantFrames = 0; variant = (variant + 1) % 4; }
        }
        if (!found) { FacePresent = false; return; }
        FacePresent = true;
        float ax = anchors[best * 2], ay = anchors[best * 2 + 1];
        int o = best * 16;
        float cx = boxes[o] / 128f + ax, cy = boxes[o + 1] / 128f + ay;
        float bw = boxes[o + 2] / 128f, bh = boxes[o + 3] / 128f;
        Box = new Rect(cx - bw / 2f, cy - bh / 2f, bw, bh);
        float now = Time.unscaledTime;
        if (lastSeen > 0f && now - lastSeen < 0.5f)
        {
            float sp = (Box.center - lastCenter).magnitude / Mathf.Max(0.05f, bw) / Mathf.Max(0.02f, now - lastSeen);
            Motion = Mathf.Lerp(Motion, sp, 0.5f);
        }
        lastCenter = Box.center; lastSeen = now;
        for (int k = 0; k < 6; k++) Keypoints[k] = new Vector2(boxes[o + 4 + k * 2] / 128f + ax, boxes[o + 5 + k * 2] / 128f + ay);

        Vector2 re = Keypoints[0], le = Keypoints[1], nose = Keypoints[2];
        Vector2 mid = (re + le) * 0.5f;
        float ed = Mathf.Max(0.02f, Vector2.Distance(re, le));
        Yaw = (nose.x - mid.x) / ed;
        PitchV = (nose.y - mid.y) / ed;
        Facing = !Calibrated || (Mathf.Abs(Yaw - yaw0) < 0.34f && Mathf.Abs(PitchV - pitch0) < 0.22f);
        float dx = cx - x0;
        Lean = Calibrated ? Mathf.Clamp(-(Mathf.Abs(dx) < 0.03f ? 0f : dx - Mathf.Sign(dx) * 0.03f) / 0.09f, -1f, 1f) : 0f;

        // eye patches, then the face crop for the avatar
        GrabEye(re, ed, eyeL, true);
        GrabEye(le, ed, eyeR, false);
        GrabFace();
    }

    // crop coordinates (top-left origin, 0..1 of the square) -> camera texture uv (bottom-left origin)
    Vector2 ToUv(Vector2 p)
    {
        bool flip = cam.videoVerticallyMirrored ^ ExtraFlip;
        float u = cropOffset.x + p.x * cropScale.x;
        float vTop = cropOffset.y + p.y * cropScale.y;
        return new Vector2(u, flip ? vTop : 1f - vTop);
    }

    void GrabEye(Vector2 k, float eyeDist, RenderTexture rt, bool left)
    {
        var c = ToUv(k + new Vector2(0f, 0.02f * eyeDist));
        float wUv = 0.46f * eyeDist * cropScale.x;
        float hUv = 0.23f * eyeDist * cropScale.y;
        normMat.SetVector("_Crop", new Vector4(wUv, hUv, c.x - wUv * 0.5f, c.y - hUv * 0.5f));
        normMat.SetFloat("_Mode", 1f);
        Graphics.Blit(cam, rt, normMat);
        AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
        {
            if (req.hasError || !Running) return;
            var data = req.GetData<Color32>();
            var f = left ? featL : featR;
            float mean = 0f;
            for (int i = 0; i < f.Length && i < data.Length; i++) { f[i] = data[i].r / 255f; mean += f[i]; }
            mean /= f.Length;
            if (left) Brightness = mean;
            float var = 0f;
            for (int i = 0; i < f.Length; i++) { f[i] -= mean; var += f[i] * f[i]; }
            float sd = Mathf.Sqrt(var / f.Length) + 1e-3f;
            for (int i = 0; i < f.Length; i++) f[i] /= sd;
            OnEyeFeature(left);
        });
    }

    void GrabFace()
    {
        var c = ToUv(new Vector2(Box.center.x, Box.center.y - Box.height * 0.05f));
        float s = Mathf.Max(Box.width, Box.height) * 1.45f;
        normMat.SetVector("_Crop", new Vector4(s * cropScale.x, s * cropScale.y, c.x - s * cropScale.x * 0.5f, c.y - s * cropScale.y * 0.5f));
        normMat.SetFloat("_Mode", 2f);
        Graphics.Blit(cam, FaceTexture, normMat);
    }

    void OnEyeFeature(bool left)
    {
        var f = left ? featL : featR;
        if (Collecting == CalibStep.Open || Collecting == CalibStep.Closed)
        {
            (left ? calibL : calibR).Add((float[])f.Clone());
            return;
        }
        if (!Calibrated || closedL == null || closedR == null || openL == null || openR == null) { EyesClosed = false; return; }
        float c = Closed(f, left ? openL : openR, left ? closedL : closedR);
        if (left) closedLv = c; else closedRv = c;
        Closedness = (closedLv + closedRv) * 0.5f;
        bool closed = EyesClosed ? Closedness > 0.45f : Closedness > 0.56f;
        if (closed && !wasClosed) closedStart = Time.unscaledTime;
        if (!closed && wasClosed && Time.unscaledTime - closedStart < 0.5f) Blinks++;
        wasClosed = closed;
        EyesClosed = closed;
    }

    static float Closed(float[] f, float[] open, float[] closed)
    {
        float dO = 0f, dC = 0f;
        for (int i = 0; i < f.Length; i++) { float a = f[i] - open[i], b = f[i] - closed[i]; dO += a * a; dC += b * b; }
        dO = Mathf.Sqrt(dO); dC = Mathf.Sqrt(dC);
        return dO / Mathf.Max(1e-4f, dO + dC);
    }

    // ------------------------------------------------------------------ calibration
    public void StartCollect(CalibStep step)
    {
        calibL.Clear();
        calibR.Clear();
        Collecting = step;
        if (step == CalibStep.Open) { yawSum = pitchSum = xSum = 0f; poseN = 0; }
    }
    float yawSum, pitchSum, xSum; int poseN;
    void LateUpdate()
    {
        if (Collecting == CalibStep.Open && FacePresent) { yawSum += Yaw; pitchSum += PitchV; xSum += Box.center.x; poseN++; }
    }

    // returns false if too few frames were seen
    public bool EndCollect()
    {
        var step = Collecting;
        Collecting = CalibStep.None;
        if (calibL.Count < 8 || calibR.Count < 8) return false;
        var l = Mean(calibL); var r = Mean(calibR);
        if (step == CalibStep.Open)
        {
            openL = l; openR = r;
            if (poseN > 0) { yaw0 = yawSum / poseN; pitch0 = pitchSum / poseN; x0 = xSum / poseN; }
            Calibrated = true;            // neutral pose known: lean and stillness work (eye closure is not used)
        }
        else { closedL = l; closedR = r; Calibrated = openL != null; Blinks = 0; }
        return true;
    }

    static float[] Mean(List<float[]> list)
    {
        var m = new float[list[0].Length];
        foreach (var v in list) for (int i = 0; i < m.Length; i++) m[i] += v[i];
        for (int i = 0; i < m.Length; i++) m[i] /= list.Count;
        return m;
    }

    // automated check: run the model once on a plain grey picture and time it (no face expected)
    public static float SelfTest(ModelAsset model, Material norm, out float score)
    {
        var m = ModelLoader.Load(model);
        using var w = new Worker(m, SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
        using var t = new Tensor<float>(new TensorShape(1, 128, 128, 3));
        var tex = new Texture2D(128, 128);
        var px = new Color[128 * 128];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 0.5f);
        tex.SetPixels(px); tex.Apply();
        var rt = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBFloat);
        norm.SetVector("_Crop", new Vector4(1, 1, 0, 0));
        norm.SetFloat("_Mode", 0f);
        Graphics.Blit(tex, rt, norm);
        TextureConverter.ToTensor(rt, t, new TextureTransform().SetTensorLayout(TensorLayout.NHWC).SetCoordOrigin(CoordOrigin.TopLeft));
        float t0 = Time.realtimeSinceStartup;
        w.Schedule(t);
        using var s = (w.PeekOutput("classificators") as Tensor<float>).ReadbackAndClone();
        float ms = (Time.realtimeSinceStartup - t0) * 1000f;
        var arr = s.DownloadToArray();
        float best = -1e9f;
        foreach (var v in arr) best = Mathf.Max(best, v);
        score = 1f / (1f + Mathf.Exp(-best));
        rt.Release();
        Object.Destroy(tex);
        return ms;
    }
}
