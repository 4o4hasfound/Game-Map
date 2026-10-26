using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public class HotelSwapTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-swaptest")<0)return;
        var g=new GameObject("Environment lifecycle check");DontDestroyOnLoad(g);g.AddComponent<HotelSwapTest>();
    }
    IEnumerator Start()
    {
        var args=System.Environment.GetCommandLineArgs();int i=System.Array.IndexOf(args,"-swaptest");string output=args[i+1];var report=new StringBuilder();
        int meridian=0,bellwether=0;bool pass=true;
        foreach(var scene in new[]{"Hotel","HotelBellwether","Hotel","HotelBellwether"})
        {
            if(SceneManager.GetActiveScene().name!=scene)yield return SceneManager.LoadSceneAsync(scene);
            yield return null;
            while(!NavBaker.Instance||!NavBaker.Instance.Ready(0))yield return null;
            yield return new WaitForSeconds(2f);
            int count=NavMesh.CalculateTriangulation().vertices.Length;int previous=scene=="Hotel"?meridian:bellwether;
            if(previous!=0&&previous!=count)pass=false;
            if(scene=="Hotel")meridian=count;else bellwether=count;
            report.AppendLine(scene+": "+count+" active navigation vertices");
        }
        pass &= meridian>0&&bellwether>0&&meridian!=bellwether;
        report.AppendLine(pass?"PASS: navigation data does not accumulate across hotel switches":"FAIL: scene lifecycle mismatch");
        File.WriteAllText(output,report.ToString());Application.Quit(pass?0:1);
    }
}
