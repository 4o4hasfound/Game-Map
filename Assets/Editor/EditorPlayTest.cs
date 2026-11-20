using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the automated checks inside the editor's Play mode (where the slowdown was seen), then closes the editor:
//   Unity.exe -projectPath TheTape -executeMethod EditorPlayTest.Run -autotest -autotestOut <file>
// The checks are the same as in the built game (AutoTest); the performance route reports Play-mode frame times.
public static class EditorPlayTest
{
    const string Key = "tape_editor_playtest";

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetBool(Key, true);
        SessionState.SetFloat(Key + "_t0", (float)EditorApplication.timeSinceStartup);
        EditorApplication.update += Watch;
        EditorApplication.EnterPlaymode();
    }

    // entering Play mode reloads the scripts: pick the watch up again
    [InitializeOnLoadMethod]
    static void Reattach() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Watch; }

    static void Watch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-autotestOut");
        string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        bool done = outPath != null && File.Exists(outPath);
        double t0 = SessionState.GetFloat(Key + "_t0", 0f);
        bool timeout = EditorApplication.timeSinceStartup - t0 > 1800.0;
        if (!done && !timeout) return;
        EditorApplication.update -= Watch;
        SessionState.EraseBool(Key);
        Debug.Log(done ? "EDITOR PLAYTEST: results written to " + outPath : "EDITOR PLAYTEST: timed out");
        EditorApplication.Exit(done ? 0 : 1);
    }
}
