using UnityEngine;

// Anything the player can use with [E]. The Interactor finds it on the looked-at collider or its parents.
public abstract class Interactable : MonoBehaviour
{
    public virtual string Prompt => "USE";
    public virtual bool CanUse => true;
    public abstract void Use(PlayerMotor player);

    // called every frame while the player looks at it (mouse wheel, number keys ...)
    public virtual void WhileLooking(PlayerMotor player) { }
    public virtual string Hint => null;

    // child placed by the builder at the device's centre, facing the way the device faces (speaker cone, keyboard)
    protected Transform Front
    {
        get
        {
            var f = transform.Find("Front");
            return f ? f : transform;
        }
    }

    protected SpatialVoice MakeVoice(string label, float vol = 1f, float minD = 1f, float maxD = 25f, float directivity = 0f)
    {
        var go = new GameObject(label);
        go.transform.SetParent(Front, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        var v = go.AddComponent<SpatialVoice>();
        v.volume = vol;
        v.minDistance = minD;
        v.maxDistance = maxD;
        v.directivity = directivity;
        v.sendLevel = 0.65f;
        v.label = label;
        return v;
    }

    protected static void OneShot(string clip, Vector3 pos, float vol = 1f, float pitch = 1f, float minD = 1f, float maxD = 25f)
    {
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(clip), pos, vol, pitch, minD, maxD);
    }
}
