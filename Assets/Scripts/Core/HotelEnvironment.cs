using UnityEngine;
using UnityEngine.SceneManagement;

// Both hotels ship in one player. F6 changes hotel; command-line selection also supports automated checks.
public class HotelEnvironment : MonoBehaviour
{
    static bool commandHandled;
    void Awake()
    {
        MaterialAcoustics.ClearGeometryCache();AcousticWork.ClearGeometryCache();
        NoiseBus.Recent.Clear();
        Time.timeScale=1f;AudioListener.pause=false;
        VHSPostFX.FastForward=0f;VHSPostFX.ExtraTracking=0f;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!FindFirstObjectByType<HotelEnvironment>()) new GameObject("Hotel selection").AddComponent<HotelEnvironment>();
        if (commandHandled) return;
        commandHandled=true;
        var args=System.Environment.GetCommandLineArgs();
        for(int i=0;i<args.Length-1;i++) if(args[i]=="-hotel")
        {
            string wanted=args[i+1].ToLowerInvariant()=="bellwether" ? "HotelBellwether":"Hotel";
            if(SceneManager.GetActiveScene().name!=wanted) SceneManager.LoadScene(wanted);
        }
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.F6)) SceneManager.LoadScene(SceneManager.GetActiveScene().name=="Hotel"?"HotelBellwether":"Hotel");
    }
    void OnGUI()
    {
        GUI.color=new Color(0.8f,0.85f,0.8f,0.65f);
        GUI.Label(new Rect(18,Screen.height-28,500,24),(SceneManager.GetActiveScene().name=="Hotel"?"HOTEL MERIDIAN":"HOTEL BELLWETHER")+"    F6  CHANGE HOTEL");
    }
}
