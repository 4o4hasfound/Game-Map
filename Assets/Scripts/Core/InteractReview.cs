using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// -interacttest <report>: the opening and the new interactions, exercised through their public entry points.
//   spawn TV live feed, flashlight not owned until picked up, sitting and standing, carrying and throwing,
//   reading, and that every music player is already playing.
public class InteractReview : MonoBehaviour
{
    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int a = System.Array.IndexOf(args, "-interacttest");
        if (a < 0 || a + 1 >= args.Length) yield break;
        string report = args[a + 1];
        var sb = new StringBuilder("Interaction review\n");
        yield return new WaitForSeconds(3f);
        var player = FindAnyObjectByType<PlayerMotor>();
        var light = FindAnyObjectByType<CamcorderLight>();
        var rb = player.GetComponent<Rigidbody>();
        bool Pass(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); return ok; }

        // ---- opening
        var tv = SpawnAtTV.SpawnTV;
        Pass(tv != null, $"spawn TV chosen: {(tv ? tv.name : "none")}");
        Pass(tv && tv.GetComponent<LiveFeedTV>() && LiveFeedTV.Feed, "spawn TV shows the camcorder feed");
        Pass(tv && Vector3.Distance(player.transform.position, tv.transform.position) < 3.2f,
             $"player starts in front of it ({(tv ? Vector3.Distance(player.transform.position, tv.transform.position) : 0f):0.0} m)");
        Pass(light && !light.owned && !light.lamp.enabled, "no flashlight at the start");
        var pickup = FindAnyObjectByType<FlashlightPickup>();
        Pass(pickup && Vector3.Distance(pickup.transform.position, player.transform.position) < 2.5f,
             $"lit flashlight lies nearby ({(pickup ? Vector3.Distance(pickup.transform.position, player.transform.position) : 0f):0.0} m)");
        if (pickup) pickup.Use(player);
        yield return null;
        Pass(light && light.owned && light.lamp.enabled, "picking it up gives a working flashlight");

        // ---- music plays by default
        yield return new WaitForSeconds(2.5f);
        var players = FindObjectsByType<SpatialVoice>(FindObjectsSortMode.None)
            .Where(v => v.label == "PianoSoundboard" || v.label == "RadioProgramme" || v.label == "JukeboxSpeaker" || v.label == "GramophoneHorn" || v.label == "MusicBox").ToArray();
        int on = players.Count(v => v.IsPlaying);
        Pass(players.Length > 0 && on == players.Length, $"music devices on this floor already playing: {on}/{players.Length}");

        // ---- sit / stand
        var seat = FindObjectsByType<Seat>(FindObjectsSortMode.None).OrderBy(s => Vector3.Distance(s.transform.position, player.transform.position)).FirstOrDefault();
        if (seat)
        {
            float before = player.EyeHeight;
            seat.Use(player);
            yield return new WaitForSeconds(1.5f);
            Pass(player.SeatedOn == seat && player.EyeHeight < before - 0.3f, $"sitting on a {seat.kind}: eye {before:0.00} -> {player.EyeHeight:0.00} m");
            seat.Use(player);
            yield return new WaitForSeconds(1.5f);
            Pass(!player.SeatedOn && player.EyeHeight > 1.4f && !rb.isKinematic, $"standing up again: eye {player.EyeHeight:0.00} m");
        }
        else Pass(false, "no seat on this floor");

        // ---- carry / throw
        var thing = FindObjectsByType<Carryable>(FindObjectsSortMode.None).OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position)).FirstOrDefault();
        Pass(thing, $"loose objects placed: {LooseObjects.Spawned}");
        if (thing)
        {
            // walk up to it
            Vector3 stand = thing.transform.position + Vector3.right * 0.6f;
            stand.y = thing.transform.position.y;
            if (Physics.Raycast(stand + Vector3.up * 0.5f, Vector3.down, out var floor, 3f, ~(1 << 9 | 1 << 2 | 1 << LooseObjects.InteractLayer), QueryTriggerInteraction.Ignore)) stand = floor.point;
            player.Teleport(stand + Vector3.up * 0.05f, 0f);
            yield return new WaitForSeconds(0.3f);
            Vector3 start = thing.transform.position;
            Interactor.Pick(thing);
            yield return new WaitForSeconds(0.8f);
            Pass(Interactor.Carrying && thing.Carried, $"picked up a {thing.material}");
            Vector3 carriedAt = thing.transform.position;
            var inter = FindAnyObjectByType<Interactor>();
            int noiseBefore = NoiseBus.Total;
            // throw it the way the Interactor does
            var m = typeof(Interactor).GetMethod("Release", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            m.Invoke(inter, new object[] { inter.transform.forward * 7.5f + Vector3.up * 1.2f, true });
            yield return new WaitForSeconds(2.5f);
            float moved = Vector3.Distance(thing.transform.position, carriedAt);
            Pass(!Interactor.Carrying && moved > 1.0f, $"thrown {moved:0.0} m from the hand");
            Pass(NoiseBus.Total > noiseBefore, $"its landing made a noise the monster can hear ({NoiseBus.Total - noiseBefore} events)");
        }

        // ---- reading
        var paper = FindObjectsByType<Readable>(FindObjectsSortMode.None).FirstOrDefault();
        if (paper)
        {
            paper.Use(player);
            Pass(!string.IsNullOrEmpty(paper.text), $"readable {paper.kind}: \"{paper.text.Split('\n')[0]}\"");
        }
        else sb.AppendLine("CHECK no readable on this floor");
        File.WriteAllText(report, sb.ToString());
        Application.Quit();
    }
}
