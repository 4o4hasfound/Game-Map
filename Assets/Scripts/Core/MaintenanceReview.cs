using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// -mixreview <directory>: record the shipped room mix and nearby effects; exercise the door's public interactions.
public class MaintenanceReview : MonoBehaviour
{
    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int arg = System.Array.IndexOf(args, "-mixreview");
        if (arg < 0 || arg + 1 >= args.Length) yield break;
        string dir = args[arg + 1];
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("Maintenance review: default start room, all normal environmental sources enabled.\n");
        yield return new WaitForSeconds(4f);
        var eng = AudioEngine.Instance;
        if (!eng || !eng.Listener) { File.WriteAllText(Path.Combine(dir, "review.txt"), "FAIL: missing audio engine"); Application.Quit(1); yield break; }
        AudioEngine.StartRecording();
        yield return new WaitForSeconds(8f);
        AudioEngine.StopRecording(Path.Combine(dir, "01_quiet_room.wav"));
        var voices = AudioEngine.Voices.Where(v => v && v.IsPlaying && v.LastGainDb > -65f).ToArray();
        sb.AppendLine("Active audible sources after quiet section: " + voices.Length);
        foreach (var v in voices.OrderByDescending(v => v.LastGainDb)) sb.AppendLine($"  {v.label}: direct gain {v.LastGainDb:0.0} dB");
        string[] effects = { "amb_drip", "door_close", "door_close_metal", "light_tick", "door_creak" };
        float[] volumes = { 0.2f, 0.85f, 0.85f, 0.08f, 0.12f };
        Vector3 p = eng.Listener.position + eng.Listener.forward * 1.5f;
        for (int i = 0; i < effects.Length; i++)
        {
            AudioEngine.StartRecording();
            var voice = eng.PlayAt(SoundBank.Pick(effects[i]), p, volumes[i], 1f, 0.6f, 20f);
            if (voice) voice.sendLevel = effects[i] == "light_tick" ? 0.12f : 0.65f;
            yield return new WaitForSeconds(2f);
            AudioEngine.StopRecording(Path.Combine(dir, $"{i + 2:00}_{effects[i]}.wav"));
        }
        var door = FindObjectsByType<HingeDoor>(FindObjectsSortMode.None).FirstOrDefault(d => !d.locked && Mathf.Abs(d.Angle) < 0.1f);
        if (door)
        {
            float before = door.Angle;
            for (int i = 0; i < 30; i++)
            {
                door.Shove(door.transform.position + Vector3.right * 0.7f, Vector3.forward * 3.2f, false);
                yield return new WaitForFixedUpdate();
            }
            float change = Mathf.Abs(door.Angle - before);
            sb.AppendLine($"Walking collision: angle change {change:0.000} deg: {(change < 0.05f ? "PASS" : "FAIL")}");
            door.Shove(door.transform.position + Vector3.right * 0.7f, Vector3.forward * 5.5f, true);
            yield return new WaitForSeconds(0.35f);
            sb.AppendLine($"Running impact: angle {door.Angle:0.0} deg: {(Mathf.Abs(door.Angle) > 10f ? "PASS" : "FAIL")}");
            yield return new WaitForSeconds(5f);
            float resting = door.Angle;
            yield return new WaitForSeconds(1f);
            sb.AppendLine($"Door stays ajar: angle {door.Angle:0.0} deg, drift {Mathf.Abs(door.Angle - resting):0.000}: {(Mathf.Abs(door.Angle - resting) < 0.1f ? "PASS" : "FAIL")}");
            door.Held = true;
            float start = door.Angle;
            for (int i = 0; i < 20; i++) { door.Drag(null, -Mathf.Sign(start) * 0.3f); yield return new WaitForFixedUpdate(); }
            door.Held = false;
            sb.AppendLine($"Held drag: moved {Mathf.Abs(door.Angle - start):0.0} deg: {(Mathf.Abs(door.Angle - start) > 3f ? "PASS" : "FAIL")}");
        }
        else sb.AppendLine("CHECK: no closed unlocked door available for interaction checks");
        File.WriteAllText(Path.Combine(dir, "review.txt"), sb.ToString());
        Application.Quit();
    }
}
