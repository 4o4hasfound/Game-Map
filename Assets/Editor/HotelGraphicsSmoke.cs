using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class HotelGraphicsSmoke
{
    const string Key="HotelGraphicsSmoke.";
    static HotelGraphicsSmoke() { if(SessionState.GetBool(Key+"active",false))Hook(); }
    static void Hook() { EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log; }
    static void Log(string message,string trace,LogType type)
    {
        if(message.Contains("Graphics Ring Buffer"))SessionState.SetInt(Key+"warnings",SessionState.GetInt(Key+"warnings",0)+1);
        if(type==LogType.Exception)SessionState.SetInt(Key+"exceptions",SessionState.GetInt(Key+"exceptions",0)+1);
    }
    public static void Run()
    {
        SessionState.SetBool(Key+"active",true);SessionState.SetString(Key+"state","launch0");SessionState.SetString(Key+"report","");
        SessionState.SetInt(Key+"warnings",0);SessionState.SetInt(Key+"exceptions",0);Hook();
        EditorSceneManager.OpenScene("Assets/Scenes/Hotel.unity");
    }
    static void Tick()
    {
        string state=SessionState.GetString(Key+"state","");
        if(state.StartsWith("launch"))
        {
            if(EditorApplication.isPlaying){SessionState.SetString(Key+"state","play"+state.Substring(6));SessionState.SetFloat(Key+"start",(float)EditorApplication.timeSinceStartup);return;}
            if(!EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.EnterPlaymode();return;
        }
        if(state.StartsWith("play"))
        {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"start",0)<10f)return;
            var probes=Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include,FindObjectsSortMode.None);int controlled=0;
            foreach(var p in probes)if(p.refreshMode==ReflectionProbeRefreshMode.ViaScripting)controlled++;
            string line=$"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}: {controlled}/{probes.Length} script-controlled probes; completed {HotelReflections.Completed}; pending {HotelReflections.Pending}\n";
            SessionState.SetString(Key+"report",SessionState.GetString(Key+"report","")+line);
            if(controlled!=probes.Length || HotelReflections.Completed<1)SessionState.SetInt(Key+"exceptions",SessionState.GetInt(Key+"exceptions",0)+1);
            SessionState.SetString(Key+"state","stop"+state.Substring(4));EditorApplication.ExitPlaymode();return;
        }
        if(state.StartsWith("stop")&&!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if(state=="stop0") {SessionState.SetString(Key+"state","launch1");EditorSceneManager.OpenScene("Assets/Scenes/HotelBellwether.unity");return;}
            SessionState.SetString(Key+"state","finish");SessionState.SetFloat(Key+"finishAt",(float)EditorApplication.timeSinceStartup+2f);
            EditorSceneManager.OpenScene("Assets/Scenes/Hotel.unity");return;
        }
        if(state=="finish" && EditorApplication.timeSinceStartup>=SessionState.GetFloat(Key+"finishAt",0))
        {
            int warnings=SessionState.GetInt(Key+"warnings",0),exceptions=SessionState.GetInt(Key+"exceptions",0);
            string report=SessionState.GetString(Key+"report","")+$"Graphics ring-buffer warnings: {warnings}; exceptions/failed checks: {exceptions}\n";
            File.WriteAllText(Path.GetFullPath("../verification/editor_graphics_smoke.txt"),report);
            SessionState.SetBool(Key+"active",false);EditorApplication.update-=Tick;
            EditorApplication.Exit(warnings==0&&exceptions==0?0:1);
        }
    }
}
