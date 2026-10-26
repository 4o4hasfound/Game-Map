using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// M releases the test monster somewhere 20-35 m away on the current floor, N removes it.
// Builds the floor's navmesh the first time it is needed (and in the background on arrival on a new floor).
public class StalkerControl : MonoBehaviour
{
    public PlayerMotor player;

    void Start()
    {
        var w = HotelWorld.Instance;
        if (w != null)
        {
            w.LevelChanged += lv =>
            {
                if (NavBaker.Instance) NavBaker.Instance.Bake(lv.index);
                if (Stalker.Active) Destroy(Stalker.Active.gameObject);
            };
            if (w.Current != null && NavBaker.Instance) NavBaker.Instance.Bake(w.Current.index);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M) && !Stalker.Active) StartCoroutine(Release());
        if (Input.GetKeyDown(KeyCode.N) && Stalker.Active)
        {
            Destroy(Stalker.Active.gameObject);
            if (VHSOverlay.Instance) VHSOverlay.Instance.Announce("STALKER REMOVED", 1.5f);
        }
    }

    public IEnumerator Release()
    {
        var w = HotelWorld.Instance;
        if (w == null || w.Current == null || !NavBaker.Instance) yield break;
        int lvl = w.Current.index;
        var op = NavBaker.Instance.Bake(lvl);
        while (!NavBaker.Instance.Ready(lvl)) yield return null;
        if (!NavBaker.RandomPoint(player.transform.position, 20f, 35f, out var p))
            NavBaker.RandomPoint(player.transform.position, 6f, 20f, out p);
        Stalker.Spawn(p);
        if (VHSOverlay.Instance) VHSOverlay.Instance.Announce("SOMETHING IS ON THIS FLOOR", 2.5f);
    }
}
