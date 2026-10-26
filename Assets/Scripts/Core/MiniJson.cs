using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Small JSON reader for the map layout (JsonUtility cannot read nested arrays or free-form objects).
// Objects become Dictionary<string, object>, arrays List<object>, numbers double.
public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        return Value(json, ref i);
    }

    static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static object Value(string s, ref int i)
    {
        Skip(s, ref i);
        char c = s[i];
        if (c == '{') return Obj(s, ref i);
        if (c == '[') return Arr(s, ref i);
        if (c == '"') return Str(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        return Num(s, ref i);
    }

    static Dictionary<string, object> Obj(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++;
        Skip(s, ref i);
        if (s[i] == '}') { i++; return d; }
        while (true)
        {
            Skip(s, ref i);
            string k = Str(s, ref i);
            Skip(s, ref i);
            i++; // :
            d[k] = Value(s, ref i);
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // }
            return d;
        }
    }

    static List<object> Arr(string s, ref int i)
    {
        var l = new List<object>();
        i++;
        Skip(s, ref i);
        if (s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(Value(s, ref i));
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // ]
            return l;
        }
    }

    static string Str(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;
        while (s[i] != '"')
        {
            char c = s[i++];
            if (c == '\\')
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        i++;
        return sb.ToString();
    }

    static double Num(string s, ref int i)
    {
        int st = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }

    // ---- typed helpers
    public static float F(this Dictionary<string, object> d, string k, float def = 0f)
    {
        return d.TryGetValue(k, out var v) && v != null ? (v is double x ? (float)x : (v is bool b ? (b ? 1f : 0f) : def)) : def;
    }

    public static int I(this Dictionary<string, object> d, string k, int def = 0)
    {
        return d.TryGetValue(k, out var v) && v is double x ? (int)x : def;
    }

    public static string S(this Dictionary<string, object> d, string k, string def = null)
    {
        return d.TryGetValue(k, out var v) && v is string x ? x : def;
    }

    public static bool B(this Dictionary<string, object> d, string k, bool def = false)
    {
        return d.TryGetValue(k, out var v) && v is bool x ? x : def;
    }

    public static List<object> L(this Dictionary<string, object> d, string k)
    {
        return d.TryGetValue(k, out var v) && v is List<object> x ? x : new List<object>();
    }

    public static Dictionary<string, object> D(this Dictionary<string, object> d, string k)
    {
        return d.TryGetValue(k, out var v) && v is Dictionary<string, object> x ? x : new Dictionary<string, object>();
    }
}
