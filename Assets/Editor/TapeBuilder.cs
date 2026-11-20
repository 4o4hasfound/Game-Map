using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the whole game from code (so it can be rebuilt): project settings, URP, layers, generated textures and
// materials, and the scene (geometry from LevelLayout, props, lights, cameras, the player, game objects).
//   Unity -batchmode -quit -projectPath TheTape -executeMethod TapeBuilder.BuildAll -logFile build.log
public static partial class TapeBuilder
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("The Tape/Build scene")]
    public static void BuildSceneOnly() { Setup(); MakeMaterials(); BuildScene(); }

    [MenuItem("The Tape/Build all (scene + player)")]
    public static void BuildAll()
    {
        Setup();
        MakeMaterials();
        BuildScene();
        BuildPlayer();
    }

    public static void BuildPlayerOnly() { BuildPlayer(); }

    // ------------------------------------------------------------------ project
    static void Setup()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Art/Textures");
        Directory.CreateDirectory("Assets/Art/Materials");
        Directory.CreateDirectory("Assets/Resources");
        // layers
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        string[] names = { null, null, null, null, null, null, null, null, "Player", "Monster", "Structure", "Door", "Prop", "Ceiling", "Interactable", "PlayerAvatar", "Hiding" };
        for (int i = 8; i < names.Length; i++) layers.GetArrayElementAtIndex(i).stringValue = names[i];
        tm.ApplyModifiedProperties();
        for (int a = 0; a < 32; a++) for (int b = 0; b < 32; b++) Physics.IgnoreLayerCollision(a, b, false);
        Physics.IgnoreLayerCollision(8, 9, true);
        Physics.IgnoreLayerCollision(8, 14, true);
        Physics.IgnoreLayerCollision(8, 15, true);

        // legacy input manager (the game reads Input directly)
        var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var aih = ps.FindProperty("activeInputHandler");
        if (aih != null) { aih.intValue = 0; ps.ApplyModifiedProperties(); }
        PlayerSettings.companyName = "Homework";
        PlayerSettings.productName = "The Tape";
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        // audio: 64 real voices (32 dropped sounds when a lot happened at once: steps, a door, a bottle, the rain)
        var am = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
        SetProp(am, "m_RealVoiceCount", 64);
        am.ApplyModifiedProperties();

        // URP: Forward+, no HDR/MSAA, short shadow distance (target: Intel Iris Xe laptops)
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Renderer.asset");
        if (!rd)
        {
            rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rd, "Assets/Settings/Renderer.asset");
        }
        rd.renderingMode = RenderingMode.ForwardPlus;
        EditorUtility.SetDirty(rd);
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP.asset");
        if (!urp)
        {
            urp = UniversalRenderPipelineAsset.Create(rd);
            AssetDatabase.CreateAsset(urp, "Assets/Settings/URP.asset");
        }
        urp.supportsHDR = true;          // the camcorder chain (auto exposure, bloom) works on HDR values
        urp.msaaSampleCount = 1;
        urp.shadowDistance = 16f;
        urp.renderScale = 1f;
        var so = new SerializedObject(urp);
        SetProp(so, "m_AdditionalLightsRenderingMode", 1);
        SetProp(so, "m_AdditionalLightShadowsSupported", 1);
        SetProp(so, "m_AdditionalLightsShadowmapResolution", 2048);
        SetProp(so, "m_AdditionalLightsShadowResolutionTierLow", 256);
        SetProp(so, "m_AdditionalLightsShadowResolutionTierMedium", 512);
        SetProp(so, "m_AdditionalLightsShadowResolutionTierHigh", 1024);
        SetProp(so, "m_MainLightShadowsSupported", 0);
        SetProp(so, "m_SoftShadowsSupported", 1);             // soft edges: affordable now only a few lamps cast at once (ShadowBudget)
        SetProp(so, "m_SoftShadowQuality", 1);                // low
        SetProp(so, "m_SupportsLightLayers", 1);              // lamps light only their own room (TapeBuilder.LightLayers.cs)
        SetProp(so, "m_RequireDepthTexture", 0);
        SetProp(so, "m_RequireOpaqueTexture", 0);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(urp);
        GraphicsSettings.defaultRenderPipeline = urp;
        // ambient occlusion: dirt in the creases, things sitting on the floor instead of floating on it
        if (!rd.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion))
        {
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, rd);
            rd.rendererFeatures.Add(ssao);
            var rso = new SerializedObject(rd);
            var map = rso.FindProperty("m_RendererFeatureMap");
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId))
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                rso.ApplyModifiedProperties();
            }
        }
        foreach (var f in rd.rendererFeatures)
        {
            if (!(f is ScreenSpaceAmbientOcclusion)) continue;
            var fso = new SerializedObject(f);
            void P(string n, float v) { var pp = fso.FindProperty("m_Settings." + n); if (pp == null) return; if (pp.propertyType == SerializedPropertyType.Float) pp.floatValue = v; else if (pp.propertyType == SerializedPropertyType.Boolean) pp.boolValue = v != 0f; else pp.intValue = (int)v; }
            P("Downsample", 1); P("AfterOpaque", 0); P("Source", 0); P("NormalSamples", 1); P("Intensity", 1.2f); P("DirectLightingStrength", 0.2f);
            P("Radius", 0.2f); P("Samples", 1); P("BlurQuality", 1); P("Falloff", 25f);
            fso.ApplyModifiedProperties();
            f.SetActive(true);
        }
        EditorUtility.SetDirty(rd);
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        AssetDatabase.SaveAssets();
    }

    static void SetProp(SerializedObject so, string name, int v)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0; else p.intValue = v;
    }

    // ------------------------------------------------------------------ materials and textures
    static Material M(string name) => mats.TryGetValue(name, out var m) ? m : mats["Paint_Staff"];

    static void MakeMaterials()
    {
        mats.Clear();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        void Lit(string name, Texture2D tex, float tile, float smooth, Color tint, float metal = 0f)
        {
            string path = $"Assets/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(lit) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = lit;
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", Vector2.one / tile);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            mats[name] = m;
        }
        Lit("Marble_Lobby", Tex("marble", 512, MarbleChecker), 1.6f, 0.75f, Color.white);
        Lit("Wallpaper_Lobby", Tex("wallpaper_green", 256, (x, y) => Wallpaper(x, y, new Color(0.18f, 0.26f, 0.2f), new Color(0.32f, 0.36f, 0.26f))), 1.2f, 0.15f, Color.white);
        Lit("Wallpaper_Hall", Tex("wallpaper_red", 256, (x, y) => Wallpaper(x, y, new Color(0.34f, 0.12f, 0.1f), new Color(0.46f, 0.3f, 0.18f))), 1.0f, 0.15f, Color.white);
        Lit("Wallpaper_Room", Tex("wallpaper_beige", 256, (x, y) => Wallpaper(x, y, new Color(0.52f, 0.46f, 0.36f), new Color(0.6f, 0.55f, 0.44f))), 1.0f, 0.12f, Color.white);
        Lit("Carpet_Gallery", Tex("carpet_red", 256, (x, y) => Carpet(x, y, new Color(0.36f, 0.06f, 0.06f), new Color(0.55f, 0.38f, 0.14f))), 1.4f, 0.05f, Color.white);
        Lit("Carpet_Hall", Tex("carpet_hall", 256, (x, y) => Carpet(x, y, new Color(0.28f, 0.07f, 0.08f), new Color(0.44f, 0.3f, 0.12f))), 1.2f, 0.05f, Color.white);
        Lit("Carpet_Room", Tex("carpet_room", 256, (x, y) => Carpet(x, y, new Color(0.22f, 0.2f, 0.26f), new Color(0.3f, 0.28f, 0.34f))), 1.0f, 0.05f, Color.white);
        Lit("Carpet_Stair", M2("Carpet_Gallery"), 1.4f, 0.05f, Color.white);
        Lit("Linoleum_Staff", Tex("lino_green", 256, (x, y) => Speckle(x, y, new Color(0.3f, 0.38f, 0.32f), 0.12f)), 0.8f, 0.4f, Color.white);
        Lit("Linoleum_Service", Tex("lino_grey", 256, (x, y) => Speckle(x, y, new Color(0.42f, 0.42f, 0.4f), 0.12f)), 0.8f, 0.4f, Color.white);
        Lit("Paint_Staff", Tex("paint_staff", 256, (x, y) => Speckle(x, y, new Color(0.62f, 0.62f, 0.56f), 0.04f)), 1.5f, 0.2f, Color.white);
        Lit("Enamel_Fridge", Tex("fridge_enamel", 128, (x, y) => Speckle(x, y, new Color(0.9f, 0.9f, 0.88f), 0.015f)), 1f, 0.75f, Color.white);
        Lit("Metal_Steel", Tex("brushed_steel", 128, (x, y) => Speckle(x, y, new Color(0.62f, 0.63f, 0.64f), 0.03f)), 1f, 0.8f, Color.white, 0.6f);
        Lit("Paint_Office", Tex("paint_office", 256, (x, y) => Speckle(x, y, new Color(0.55f, 0.58f, 0.5f), 0.04f)), 1.5f, 0.2f, Color.white);
        Lit("Paint_Bedroom", Tex("paint_bedroom", 256, (x, y) => Speckle(x, y, new Color(0.4f, 0.44f, 0.5f), 0.03f)), 1.5f, 0.15f, Color.white);
        Lit("Concrete_Stair", Tex("concrete", 256, (x, y) => Speckle(x, y, new Color(0.45f, 0.45f, 0.44f), 0.18f)), 1.5f, 0.1f, Color.white);
        Lit("Wood_Floor", Tex("wood_floor", 256, (x, y) => Wood(x, y, new Color(0.36f, 0.24f, 0.14f), 8)), 1.6f, 0.35f, Color.white);
        Lit("Wood_Door", Tex("wood_door", 256, (x, y) => Wood(x, y, new Color(0.3f, 0.18f, 0.1f), 1)), 1.0f, 0.3f, Color.white);
        Lit("Wood_Furniture", Tex("wood_furniture", 256, (x, y) => Wood(x, y, new Color(0.26f, 0.15f, 0.08f), 3)), 0.8f, 0.4f, Color.white);
        Lit("Ceiling_Plaster", Tex("ceiling", 256, (x, y) => Speckle(x, y, new Color(0.7f, 0.68f, 0.62f), 0.05f)), 2f, 0.05f, Color.white);
        Lit("Fabric_Bed", Tex("fabric_bed", 128, (x, y) => Speckle(x, y, new Color(0.62f, 0.6f, 0.54f), 0.06f)), 0.6f, 0.0f, Color.white);
        Lit("Fabric_Cover", Tex("fabric_cover", 128, (x, y) => Speckle(x, y, new Color(0.38f, 0.14f, 0.12f), 0.08f)), 0.6f, 0.0f, Color.white);
        Lit("Fabric_Curtain", Tex("fabric_curtain", 128, (x, y) => Stripe(x, y, new Color(0.3f, 0.26f, 0.18f))), 0.5f, 0.0f, Color.white);
        Lit("Fabric_Cloth", Tex("fabric_cloth", 128, (x, y) => Speckle(x, y, new Color(0.8f, 0.8f, 0.8f), 0.06f)), 0.3f, 0.1f, Color.white);
        Lit("Skin", Tex("skin", 64, (x, y) => Speckle(x, y, new Color(0.62f, 0.5f, 0.42f), 0.03f)), 1f, 0.2f, Color.white);
        Lit("Brass_Fitting", Tex("brass", 64, (x, y) => Speckle(x, y, new Color(0.6f, 0.46f, 0.2f), 0.05f)), 1f, 0.7f, Color.white, 0.8f);
        Lit("Metal_Device", Tex("metal", 64, (x, y) => Speckle(x, y, new Color(0.3f, 0.3f, 0.31f), 0.05f)), 1f, 0.5f, Color.white, 0.5f);
        Lit("Plastic_Device", Tex("plastic", 64, (x, y) => Speckle(x, y, new Color(0.12f, 0.12f, 0.12f), 0.03f)), 1f, 0.45f, Color.white);
        Lit("Plastic_Beige", Tex("plastic_beige", 64, (x, y) => Speckle(x, y, new Color(0.62f, 0.58f, 0.5f), 0.03f)), 1f, 0.4f, Color.white);
        Lit("Paper_Note", Tex("paper", 64, (x, y) => Speckle(x, y, new Color(0.86f, 0.83f, 0.72f), 0.03f)), 1f, 0.05f, Color.white);
        Lit("Glass_Bulb", Tex("bulb", 16, (x, y) => new Color(0.9f, 0.85f, 0.7f)), 1f, 0.8f, Color.white);
        Lit("Plant_Leaf", Tex("leaf", 64, (x, y) => Speckle(x, y, new Color(0.12f, 0.2f, 0.1f), 0.15f)), 0.5f, 0.3f, Color.white);
        Lit("Blob_Face", Tex("blob", 64, BlobNoise), 0.25f, 0.0f, Color.white);
        Lit("Glass_Shards", Tex("glass", 64, (x, y) => Speckle(x, y, new Color(0.5f, 0.55f, 0.56f), 0.4f)), 0.3f, 0.95f, Color.white);
        Lit("Metal_Lift", Tex("lift", 128, (x, y) => Speckle(x, y, new Color(0.38f, 0.36f, 0.32f), 0.06f)), 1f, 0.6f, Color.white, 0.7f);
        Lit("Lamp_Red", Tex("red", 8, (x, y) => new Color(0.6f, 0.05f, 0.03f)), 1f, 0.6f, Color.white);
        // decay, basement, service
        Lit("Soot_Floor", Tex("soot", 256, (x, y) => Speckle(x, y, new Color(0.07f, 0.065f, 0.06f), 0.35f)), 1.2f, 0.08f, Color.white);
        Lit("Plaster_Burnt", Tex("plaster_burnt", 256, (x, y) => Speckle(x, y, new Color(0.2f, 0.17f, 0.14f), 0.3f) * (0.55f + 0.45f * y)), 1.6f, 0.05f, Color.white);
        Lit("Rubble_Floor", Tex("rubble", 256, (x, y) => Speckle(x, y, new Color(0.34f, 0.31f, 0.27f), 0.5f)), 0.8f, 0.05f, Color.white);
        Lit("Water_Stream", Tex("water_stream", 8, (x, y) => new Color(0.78f, 0.86f, 0.92f)), 1f, 0.97f, Color.white);
        Lit("Water_Floor", Tex("water", 128, (x, y) => Speckle(x, y, new Color(0.05f, 0.06f, 0.065f), 0.15f)), 1.5f, 0.93f, Color.white);
        Lit("Concrete_Wall", Tex("concrete_wall", 256, (x, y) => Speckle(x, y, new Color(0.36f, 0.36f, 0.34f), 0.22f) * (0.8f + 0.2f * T(x, y, 3f))), 2f, 0.08f, Color.white);
        Lit("Brick_Wall", Tex("brick", 256, Brick), 1.2f, 0.06f, Color.white);
        Lit("Metal_Floor", Tex("metal_floor", 128, (x, y) => Speckle(x, y, new Color(0.3f, 0.31f, 0.32f), 0.12f) * (Mathf.Repeat(x * 8f + y * 8f, 1f) < 0.1f ? 0.7f : 1f)), 1f, 0.45f, Color.white, 0.5f);
        Lit("Tile_Kitchen", Tex("tile_kitchen", 256, (x, y) => (Mathf.Repeat(x * 8f, 1f) < 0.06f || Mathf.Repeat(y * 8f, 1f) < 0.06f ? new Color(0.35f, 0.35f, 0.33f) : Speckle(x, y, new Color(0.7f, 0.7f, 0.66f), 0.05f))), 1.6f, 0.6f, Color.white);
        Lit("Glass_Night", Tex("glass_night", 16, (x, y) => new Color(0.04f, 0.05f, 0.09f)), 1f, 0.9f, Color.white);
        mats["Glass_Night"].SetColor("_EmissionColor", new Color(0.012f, 0.016f, 0.03f));
        Lit("Stain_Dark", Tex("stain", 128, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)); return Color.Lerp(new Color(0.06f, 0.045f, 0.03f), new Color(0.14f, 0.11f, 0.08f), Mathf.Clamp01(d * 2f + T(x, y, 6f) * 0.5f)); }), 1f, 0.3f, Color.white);
        Lit("Plywood_Board", Tex("plywood", 128, (x, y) => Wood(x, y, new Color(0.44f, 0.34f, 0.22f), 1)), 1f, 0.2f, Color.white);
        Lit("Glass_Bottle", Tex("bottle", 16, (x, y) => new Color(0.12f, 0.28f, 0.16f)), 1f, 0.92f, Color.white);

        MakePhotoMaterials();
        // decals that darken what is behind them (multiply), light shafts, dust, sparks
        Decal("Grime_Bottom", Tex("grime_bottom", 256, (x, y) => { float edge = 0.55f + 0.4f * T(x, 0.3f, 9f); float a = Mathf.Pow(Mathf.Clamp01((edge - y) / edge), 1.6f) * (0.55f + 0.45f * T(x, y, 14f)); return new Color(0.2f, 0.16f, 0.12f, a); }), 0.85f);
        Decal("Grime_Top", Tex("grime_top", 256, (x, y) => { float edge = 0.5f + 0.4f * T(x, 0.7f, 7f); float a = Mathf.Pow(Mathf.Clamp01((y - (1f - edge)) / edge), 1.8f) * (0.5f + 0.5f * T(x, y, 11f)); return new Color(0.24f, 0.2f, 0.15f, a); }), 0.7f);
        Decal("Stain_Leak", Tex("stain_leak", 256, LeakStain), 1f);
        Decal("Stain_Mold", Tex("stain_mold", 256, (x, y) => { float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f; float n = T(x, y, 7f) * 0.6f + T(x, y, 23f) * 0.4f; float a = Mathf.Clamp01((n - 0.38f - r * 0.45f) * 4f); return new Color(0.12f, 0.15f, 0.1f, a); }), 1f);
        Decal("Stain_Soot", Tex("stain_soot", 256, (x, y) => { float w = 0.12f + y * 0.38f; float d = Mathf.Abs(x - 0.5f) / w; float a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(1.2f - y) * (0.6f + 0.4f * T(x, y, 6f)); return new Color(0.04f, 0.035f, 0.03f, a); }), 1f);
        var dot = Tex("dust_dot", 32, (x, y) => { float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f; float v = Mathf.Clamp01(1f - d); v *= v; return new Color(v, v, v, 1f); });
        Lit("Lamp_Shade", Tex("lamp_shade", 128, (x, y) => Speckle(x, y, new Color(0.82f, 0.74f, 0.58f), 0.08f) * (0.85f + 0.15f * Mathf.Repeat(x * 18f, 1f))), 0.3f, 0.05f, Color.white);
        Lit("Bulb_Dead", Tex("bulb_dead", 16, (x, y) => new Color(0.36f, 0.34f, 0.28f)), 1f, 0.7f, Color.white);
        var spark = Custom("Spark", "TheTape/LightShaft");
        spark.SetTexture("_MainTex", dot); spark.SetFloat("_EdgePower", 0f); spark.SetColor("_Color", new Color(1f, 0.8f, 0.45f, 3f));
        Custom("Screen", "TheTape/Screen");
        Custom("Mirror_Glass", "TheTape/Mirror");
        Custom("Static", "TheTape/Static");
        Custom("Plaque_Text", "TheTape/Text3D");                   // door plates and signs: depth-tested text
        var face = Custom("FaceTape", "TheTape/FaceTape");
        face.SetTexture("_DefaultFace", Tex("default_face", 128, DefaultFace));
        Custom("VHSFX", "Hidden/TheTape/VHSFX");
        Custom("FaceNorm", "Hidden/TheTape/FaceNorm");
        AssetDatabase.SaveAssets();

        // face tracker config (Resources)
        var cfgPath = "Assets/Resources/FaceTrackerConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<FaceTrackerConfig>(cfgPath);
        if (!cfg) { cfg = ScriptableObject.CreateInstance<FaceTrackerConfig>(); AssetDatabase.CreateAsset(cfg, cfgPath); }
        cfg.model = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Face/Models/blaze_face_short_range.onnx");
        cfg.anchors = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Face/Models/blaze_anchors.csv");
        cfg.normMat = mats["FaceNorm"];
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();
        if (!cfg.model) Debug.LogError("Face model did not import");
        if (!cfg.anchors) Debug.LogError("Face anchors did not import");
    }

    static Texture2D M2(string matName) => (Texture2D)mats[matName].GetTexture("_BaseMap");

    static Material Decal(string name, Texture2D tex, float strength)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        if (imp && (imp.wrapModeV != TextureWrapMode.Clamp || !imp.alphaIsTransparency)) { imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp; imp.alphaIsTransparency = true; imp.SaveAndReimport(); }
        var m = Custom(name, "TheTape/Multiply");
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Strength", strength);
        m.enableInstancing = true;
        return m;
    }

    static Material Custom(string name, string shader)
    {
        string path = $"Assets/Art/Materials/{name}.mat";
        var sh = Shader.Find(shader);
        if (!sh) Debug.LogError("Missing shader " + shader);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static Texture2D Tex(string name, int size, System.Func<float, float, Color> f)
    {
        string path = $"Assets/Art/Textures/{name}.png";
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x / (float)size, y / (float)size);
        t.SetPixels(px);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.filterMode = size <= 16 ? FilterMode.Point : FilterMode.Bilinear;
        imp.mipmapEnabled = true;
        imp.sRGBTexture = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---- texture recipes (tileable: x, y in 0..1)
    static float N(float x, float y, float s) => Mathf.PerlinNoise(x * s + 13.1f, y * s + 7.7f);
    static float T(float x, float y, float s)    // tileable noise
    {
        float a = N(x, y, s), b = N(x - 1, y, s), c = N(x, y - 1, s), d = N(x - 1, y - 1, s);
        return Mathf.Lerp(Mathf.Lerp(a, b, x), Mathf.Lerp(c, d, x), y);
    }
    static Color MarbleChecker(float x, float y)
    {
        bool dark = (x < 0.5f) ^ (y < 0.5f);
        float v = Mathf.Abs(Mathf.Sin((x * 6 + T(x, y, 6) * 4f) * Mathf.PI));
        float vein = Mathf.Pow(v, 18f);
        Color baseC = dark ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.82f, 0.8f, 0.76f);
        Color c = Color.Lerp(baseC, dark ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.55f, 0.55f, 0.55f), vein * 0.6f);
        float grout = (Mathf.Min(Mathf.Abs(x - 0.5f), Mathf.Abs(y - 0.5f)) < 0.004f || x < 0.004f || y < 0.004f) ? 0.6f : 1f;
        return c * grout;
    }
    static Color Wallpaper(float x, float y, Color a, Color b)
    {
        float stripe = Mathf.Repeat(x * 8f, 1f) < 0.5f ? 0f : 1f;
        float dam = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(x * Mathf.PI * 16f) * Mathf.Sin(y * Mathf.PI * 8f)), 4f);
        var c = Color.Lerp(a, b, stripe * 0.35f + dam * 0.5f);
        float age = T(x, y, 4f) * 0.25f;
        return c * (0.85f + 0.15f * T(x, y, 30f)) * (1f - age * 0.4f);
    }
    static Color Carpet(float x, float y, Color a, Color b)
    {
        float u = Mathf.Repeat(x * 4f, 1f) - 0.5f, v = Mathf.Repeat(y * 4f, 1f) - 0.5f;
        float dia = Mathf.Abs(u) + Mathf.Abs(v);
        float pat = dia > 0.42f && dia < 0.47f ? 1f : dia < 0.08f ? 1f : 0f;
        var c = Color.Lerp(a, b, pat * 0.8f);
        float fibre = 0.85f + 0.3f * (Mathf.PerlinNoise(x * 180f, y * 180f) - 0.5f);
        float wear = T(x, y, 3f);
        return c * fibre * (0.8f + 0.25f * wear);
    }
    static Color Speckle(float x, float y, Color c, float amt)
    {
        float n = Mathf.PerlinNoise(x * 120f, y * 120f) - 0.5f;
        float m = T(x, y, 5f) - 0.5f;
        return c * (1f + n * amt * 2f + m * amt);
    }
    static Color Stripe(float x, float y, Color c) => c * (Mathf.Repeat(x * 10f, 1f) < 0.5f ? 0.85f : 1.05f) * (0.9f + 0.1f * Mathf.PerlinNoise(x * 60, y * 60));
    static Color Wood(float x, float y, Color c, int planks)
    {
        float plank = Mathf.Floor(x * planks);
        float grain = Mathf.Sin((y * 14f + T(x, y, 3f) * 3f + plank * 1.7f) * Mathf.PI * 2f) * 0.5f + 0.5f;
        float seam = planks > 1 && Mathf.Repeat(x * planks, 1f) < 0.03f ? 0.5f : 1f;
        return c * (0.75f + 0.35f * grain) * seam * (0.9f + 0.2f * Mathf.PerlinNoise(plank * 3.3f, 1.7f));
    }
    static Color Brick(float x, float y)
    {
        float row = Mathf.Floor(y * 8f);
        float bx = Mathf.Repeat(x * 4f + (row % 2) * 0.5f, 1f), by = Mathf.Repeat(y * 8f, 1f);
        bool mortar = bx < 0.04f || by < 0.08f;
        var c = mortar ? new Color(0.3f, 0.29f, 0.27f) : new Color(0.36f, 0.17f, 0.12f) * (0.8f + 0.4f * Mathf.PerlinNoise(Mathf.Floor(x * 4f + (row % 2) * 0.5f) * 3.1f, row * 1.7f));
        return c * (0.85f + 0.15f * T(x, y, 9f));
    }
    static Color LeakStain(float x, float y)
    {
        // water running down from the ceiling: streaks of different lengths, a darker tide mark where each stops
        float top = 1f - y;
        float len = 0.3f + 0.6f * T(x, 0.2f, 4f);
        float streak = Mathf.Clamp01(1f - top / len);
        float tide = Mathf.Exp(-Mathf.Pow((top - len) * 9f, 2f)) * 0.22f;
        float blot = Mathf.Clamp01(1f - top * 5f) * (0.5f + 0.5f * T(x, y, 6f));
        float side = Mathf.Clamp01(Mathf.Min(x, 1f - x) * 6f);
        float a = Mathf.Clamp01((streak * (0.35f + 0.4f * T(x, y, 21f)) + tide + blot * 0.6f) * side);
        return new Color(0.5f, 0.4f, 0.27f, a * 0.8f);
    }
    static Color BlobNoise(float x, float y)
    {
        float block = Mathf.Floor(y * 16f) * 0.37f + Mathf.Floor(x * 8f) * 0.11f;
        float n = Mathf.Repeat(Mathf.Sin(block * 12.9898f + Mathf.Floor(x * 32f) * 78.233f) * 43758.5453f, 1f);
        float v = n > 0.82f ? 0.5f : n * 0.12f;
        return new Color(v, v, v * 1.05f);
    }
    static Color DefaultFace(float x, float y)
    {
        // a plain, grainy, anonymous face (used when the camera face is off)
        var skin = new Color(0.62f, 0.52f, 0.45f);
        float dx = (x - 0.5f) / 0.36f, dy = (y - 0.47f) / 0.46f;
        if (dx * dx + dy * dy > 1f) return new Color(0.08f, 0.07f, 0.06f);
        var c = skin * (0.85f + 0.15f * (1f - dx * dx));
        float eyeY = 0.58f;
        foreach (float ex in new[] { 0.38f, 0.62f })
        {
            float e = ((x - ex) / 0.06f) * ((x - ex) / 0.06f) + ((y - eyeY) / 0.025f) * ((y - eyeY) / 0.025f);
            if (e < 1f) c = new Color(0.12f, 0.1f, 0.1f);
            float br = ((x - ex) / 0.08f) * ((x - ex) / 0.08f) + ((y - eyeY - 0.06f) / 0.012f) * ((y - eyeY - 0.06f) / 0.012f);
            if (br < 1f) c = new Color(0.2f, 0.15f, 0.12f);
        }
        float nose = Mathf.Abs(x - 0.5f) < 0.02f && y > 0.4f && y < 0.54f ? 0.85f : 1f;
        float mouth = ((x - 0.5f) / 0.1f) * ((x - 0.5f) / 0.1f) + ((y - 0.3f) / 0.015f) * ((y - 0.3f) / 0.015f) < 1f ? 0.55f : 1f;
        return c * nose * mouth * (0.92f + 0.16f * Mathf.PerlinNoise(x * 90, y * 90));
    }

    // ------------------------------------------------------------------ player build
    static void BuildPlayer()
    {
        Directory.CreateDirectory("Build");
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/TheTape.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD RESULT: " + report.summary.result + "  errors " + report.summary.totalErrors + "  size " + report.summary.totalSize);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
