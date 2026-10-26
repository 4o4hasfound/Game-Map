using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// -pianotest <directory>: stand in front of every piano/organ, press E on it (Use) and the number keys (Note),
// record the actual output mix and list which voices sounded at what level.
public class PianoReview : MonoBehaviour
{
    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int arg = System.Array.IndexOf(args, "-pianotest");
        if (arg < 0 || arg + 1 >= args.Length) yield break;
        string dir = args[arg + 1];
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("Piano review: player placed 1 m in front of each keyboard.\n");
        yield return new WaitForSeconds(3f);
        var w = HotelWorld.Instance;
        var player = FindAnyObjectByType<PlayerMotor>();
        var rb = player.GetComponent<Rigidbody>();
        player.Frozen = true;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
        var eng = AudioEngine.Instance;
        int n = 0;
        for (int pass = 0; pass < w.levels.Count + 1; pass++)
        {
            int li = pass % w.levels.Count;   // the first floor again at the end: is anything time-dependent?
            w.SetLevel(li);
            yield return new WaitForSeconds(1.5f);
            var pianos = FindObjectsByType<PianoDevice>(FindObjectsSortMode.None)
                .Where(p => p.transform.IsChildOf(w.levels[li].root.transform)).ToArray();
            foreach (var piano in pianos)
            {
                var front = piano.transform.Find("Front") ?? piano.transform;
                Vector3 stand = front.position + front.forward * 1.0f;
                stand.y = piano.transform.position.y + 0.05f;
                rb.position = stand;
                player.transform.position = stand;
                Physics.SyncTransforms();
                yield return new WaitForSeconds(1f);
                // 1: the piano playing by itself (default state)
                string file = $"{n:00}_{piano.name}_roll.wav";
                bool rollAtStart = piano.RollOn;
                AudioEngine.StartRecording();
                yield return new WaitForSeconds(4f);
                AudioEngine.StopRecording(Path.Combine(dir, file));
                sb.AppendLine($"level {li} {piano.name} tuning {piano.tuning}: playing by default {rollAtStart} -> {file}");
                foreach (var v in AudioEngine.Voices.Where(v => v && v.IsPlaying && v.label == "PianoSoundboard" && v.transform.IsChildOf(piano.transform)))
                    sb.AppendLine($"    roll {v.Source.clip.name} {v.LastGainDb:0.0} dB [{v.LastPath}]{(v.UnityVirtual ? " UNITY-VIRTUAL (not mixed)" : "")}");
                // 2: E stops it; then a chord on the number keys
                piano.Use(player);
                yield return new WaitForSeconds(0.6f);
                string keys = $"{n++:00}_{piano.name}_keys.wav";
                AudioEngine.StartRecording();
                piano.Note(0); piano.Note(2); piano.Note(4);
                yield return new WaitForSeconds(0.4f);
                var heard = AudioEngine.Voices.Where(v => v && v.IsPlaying && v.label != null && v.label.StartsWith("piano_recorded"))
                                              .Select(v => $"{v.label} {v.LastGainDb:0.0} dB [{v.LastPath}]{(v.UnityVirtual ? " UNITY-VIRTUAL (not mixed)" : "")}").ToArray();
                yield return new WaitForSeconds(2.6f);
                AudioEngine.StopRecording(Path.Combine(dir, keys));
                sb.AppendLine($"    after E: still rolling {piano.RollOn}; keys -> {keys}");
                foreach (var h in heard) sb.AppendLine("    " + h);
                piano.Use(player);   // leave it playing as found
            }
        }
        File.WriteAllText(Path.Combine(dir, "piano.txt"), sb.ToString());
        Application.Quit();
    }
}
