using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class HotelBuilder
{
    public static void RefreshReflectionPolicyAndBuild()
    {
        SetupProject();
        foreach(var path in BothScenes)
        {
            var scene=EditorSceneManager.OpenScene(path);
            foreach(var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
                probe.enabled=false;probe.shadowDistance=0f;
                probe.farClipPlane=Mathf.Clamp(probe.size.magnitude*.65f,5f,18f);
            }
            EditorSceneManager.SaveScene(scene,path);
        }
        if(!BuildPlayer())EditorApplication.Exit(1);
    }
    // Refresh the same runtime-read decal flag as BuildScene without regenerating unchanged textures and geometry.
    public static void RefreshMaintenanceSceneAndBuild()
    {
        EditorSceneManager.OpenScene(ScenePath);
        int changed = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.name.StartsWith("Decals_"))
            {
                r.gameObject.isStatic = false;
                changed++;
            }
        foreach (var fx in Object.FindObjectsByType<VHSPostFX>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            fx.grain = 0.016f;
            fx.fogDensity = 0.012f;
        }
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        Debug.Log("[HotelBuilder] Preserved readable decal geometry on " + changed + " meshes");
        if (!BuildPlayer()) EditorApplication.Exit(1);
    }
}
