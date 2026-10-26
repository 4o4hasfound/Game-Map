using System.Collections.Generic;
using UnityEngine;

// Loads the baked clips (Assets/Resources/Audio) by name; "name" also finds variants name_0, name_1 ...
public static class SoundBank
{
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, AudioClip[]> variants = new Dictionary<string, AudioClip[]>();
    static readonly Dictionary<string, int> lastVariant = new Dictionary<string, int>();
    static Dictionary<string, object> catalog;
    static readonly Dictionary<string,AudioClip> recorded = new Dictionary<string,AudioClip>();
    static void LoadCatalog()
    {
        if (catalog != null) return;
        var json = Resources.Load<TextAsset>("RecordedAudio/catalog");
        catalog = json ? (Dictionary<string,object>)MiniJson.Parse(json.text) : new Dictionary<string,object>();
        // Decode short transients up front; no disk/resource lookup on a footstep or door impact.
        foreach(var clip in Resources.LoadAll<AudioClip>("RecordedAudio"))recorded[clip.name]=clip;
        if (!json) Debug.LogError("Recorded sound catalog missing; synthetic fallback is disabled.");
    }

    public static AudioClip Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (clips.TryGetValue(name, out var c)) return c;
        var v = Variants(name);
        c = v.Length > 0 ? Pick(name) : null;
        clips[name] = c;
        return c;
    }

    public static AudioClip[] Variants(string name)
    {
        if (variants.TryGetValue(name, out var v)) return v;
        LoadCatalog();
        var list = new List<AudioClip>();
        if (catalog.TryGetValue(name,out var entry))
        foreach (string file in (List<object>)entry)
        {
            recorded.TryGetValue(file,out var c);
            if (c) list.Add(c);
            else Debug.LogError("Recorded clip missing: " + file);
        }
        v = list.ToArray();
        variants[name] = v;
        return v;
    }

    // random variant, never the same one twice in a row
    public static AudioClip Pick(string name)
    {
        var v = Variants(name);
        if (v.Length == 0) return null;
        lastVariant.TryGetValue(name, out int last);
        int i = Random.Range(0, v.Length);
        if (v.Length > 1 && i == last) i = (i + 1) % v.Length;
        lastVariant[name] = i;
        return v[i];
    }
}
