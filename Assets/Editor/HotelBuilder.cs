using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the whole game from the Blender export (Assets/Map/Hotel.fbx + hotel_layout.json):
// project settings, baked audio, procedural textures/materials, the scene, and the Windows player.
// Menu: Hotel > Build Everything.   Batch: Unity -batchmode -quit -projectPath . -executeMethod HotelBuilder.BuildAll
public static partial class HotelBuilder
{
    static string MapFbx = "Assets/Map/Hotel.fbx";
    static string MapJson = "Assets/Map/hotel_layout.json";
    static string ScenePath = "Assets/Scenes/Hotel.unity";
    const string AudioDir = "Assets/Resources/RecordedAudio";
    static readonly string[] BothScenes = { "Assets/Scenes/Hotel.unity", "Assets/Scenes/HotelBellwether.unity" };
    public static void BuildBothScenes()
    {
        MapFbx="Assets/Map/Hotel.fbx"; MapJson="Assets/Map/hotel_layout.json"; ScenePath=BothScenes[0]; BuildScene();
        MapFbx="Assets/MapBellwether/Hotel.fbx"; MapJson="Assets/MapBellwether/hotel_layout.json"; ScenePath=BothScenes[1]; BuildScene();
        MapFbx="Assets/Map/Hotel.fbx"; MapJson="Assets/Map/hotel_layout.json"; ScenePath=BothScenes[0];
    }
    static string ExePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/HotelMeridian.exe"));

    public const int PlayerLayer = 9, StructureLayer = 10, DoorLayer = 11, PropLayer = 12, CeilingLayer = 13;

    public static void BuildAll()
    {
        SetupProject();
        BakeAudio(false);
        BuildBothScenes();
        if (!BuildPlayer()) EditorApplication.Exit(1);
    }

    public static void BuildAllFresh()
    {
        SetupProject();
        BakeAudio(true);
        BuildBothScenes();
        if (!BuildPlayer()) EditorApplication.Exit(1);
    }

    [MenuItem("Hotel/Build Everything")]
    public static void BuildEverythingMenu()
    {
        SetupProject();
        BakeAudio(false);
        BuildBothScenes();
        BuildPlayer();
    }

    // runtime-script-only changes: rebuild the player from the existing generated scenes
    public static void BuildPlayerOnly()
    {
        if (!BuildPlayer()) EditorApplication.Exit(1);
    }

    [MenuItem("Hotel/Rebake Audio")]
    public static void RebakeAudio() => BakeAudio(true);

    [MenuItem("Hotel/Build Player")]
    public static bool BuildPlayer()
    {
        PlayerSettings.enableFrameTimingStats = true;   // CPU / GPU frame timings for the stress harness
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = BothScenes,
            locationPathName = ExePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log("[HotelBuilder] Build " + report.summary.result + " size " + report.summary.totalSize);
        if(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            string credits=Path.GetFullPath(Path.Combine(Application.dataPath,"../AUDIO_CREDITS.md"));
            if(!File.Exists(credits)) credits=Path.GetFullPath(Path.Combine(Application.dataPath,"../../AUDIO_CREDITS.md"));
            File.Copy(credits,Path.Combine(Path.GetDirectoryName(ExePath),"AUDIO_CREDITS.md"),true);
        }
        return report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
    }

    static void ConfigureAudioImports()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var st = ai.defaultSampleSettings;
            bool longClip = clip && clip.length > 6f;
            st.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            st.compressionFormat = longClip ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM; // keep impacts free of lossy pre-echo
            st.quality = longClip ? 0.7f : 0.85f;
            st.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            st.preloadAudioData = true;
            ai.defaultSampleSettings = st;
            ai.forceToMono = !path.EndsWith("amb_room_tone.wav");
            // Mono conversion must retain authored levels: normalizing tiny clicks to full scale destroys the mix.
            var importerSettings = new SerializedObject(ai);
            var normalize = importerSettings.FindProperty("m_Normalize") ?? importerSettings.FindProperty("normalize");
            if (normalize != null) { normalize.boolValue = false; importerSettings.ApplyModifiedPropertiesWithoutUndo(); }
            else Debug.LogWarning("[HotelBuilder] Cannot find AudioImporter normalization property: " + path);
            ai.loadInBackground = longClip;
            ai.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ project
    static void SetupProject()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        layers.GetArrayElementAtIndex(8).stringValue = "";
        layers.GetArrayElementAtIndex(PlayerLayer).stringValue = "Player";
        layers.GetArrayElementAtIndex(StructureLayer).stringValue = "Structure";
        layers.GetArrayElementAtIndex(DoorLayer).stringValue = "Doors";
        layers.GetArrayElementAtIndex(PropLayer).stringValue = "Props";
        layers.GetArrayElementAtIndex(CeilingLayer).stringValue = "Ceiling";
        layers.GetArrayElementAtIndex(LooseObjects.InteractLayer).stringValue = "Interact";
        tm.ApplyModifiedPropertiesWithoutUndo();

        var am = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
        void SetInt(string prop, int v)
        {
            var sp = am.FindProperty(prop);
            if (sp != null) sp.intValue = v;
            else Debug.LogWarning("[HotelBuilder] AudioManager has no " + prop);
        }
        SetInt("m_RealVoiceCount", 48);
        SetInt("m_VirtualVoiceCount", 128);
        SetInt("Default Speaker Mode", 2);    // stereo
        SetInt("m_DSPBufferSize", 512);
        SetInt("m_RequestedDSPBufferSize", 512);
        am.ApplyModifiedPropertiesWithoutUndo();

        PlayerSettings.companyName = "NTHU Group 11";
        PlayerSettings.productName = "Hotel Meridian";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        // D3D11 graphics jobs exhaust the reflection command ring on this Intel driver.
        // Keep the normal render thread, but avoid native graphics-worker jobs on Windows.
        PlayerSettings.graphicsJobs = false;
        PlayerSettings.visibleInBackground = true;
        int level = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.shadowDistance = 30f;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowCascades = 2;
            QualitySettings.pixelLightCount = 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.vSyncCount = 1;
        }
        QualitySettings.SetQualityLevel(level, false);
        foreach (var d in new[] { "Assets/Generated", TexDir, MatDir, "Assets/Scenes", "Assets/Resources", AudioDir })
            if (!AssetDatabase.IsValidFolder(d))
            {
                var parent = Path.GetDirectoryName(d).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
    }

    // ------------------------------------------------------------------ audio
    static void BakeAudio(bool force)
    {
        if (!File.Exists(Path.Combine(AudioDir,"catalog.json")))
            throw new System.Exception("Build the recorded bank with audio_sources/build_recorded_bank.py before building Unity. Synthetic baking is retired.");
        AssetDatabase.Refresh();
        ConfigureAudioImports();
        Debug.Log("[HotelBuilder] verified recorded-only audio imports");
    }

    // ------------------------------------------------------------------ map import
    static void ConfigureModel()
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(MapFbx);
        if (mi == null) throw new System.Exception("map not found: " + MapFbx);
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.isReadable = true;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.addCollider = false;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.indexFormat = ModelImporterIndexFormat.Auto;
        mi.SaveAndReimport();
    }
}
