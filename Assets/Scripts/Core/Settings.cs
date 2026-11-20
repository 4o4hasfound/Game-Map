using UnityEngine;

// Player settings, kept in PlayerPrefs. Nothing from the camera is ever stored.
public static class Settings
{
    static int I(string k, int d) => PlayerPrefs.GetInt("tape_" + k, d);
    static void SetI(string k, int v) { PlayerPrefs.SetInt("tape_" + k, v); PlayerPrefs.Save(); }
    static float F(string k, float d) => PlayerPrefs.GetFloat("tape_" + k, d);
    static void SetF(string k, float v) { PlayerPrefs.SetFloat("tape_" + k, v); PlayerPrefs.Save(); }

    // camera choices are asked every launch at the consent screen (not remembered between sessions)
    public static bool UseCamera;
    public static bool ShowFace;
    public static bool CameraAsked;

    public static bool LowScare { get => I("lowscare", 0) == 1; set => SetI("lowscare", value ? 1 : 0); }
    public static bool Classroom { get => I("classroom", 0) == 1; set => SetI("classroom", value ? 1 : 0); }
    public static bool Osd { get => I("osd", 1) == 1; set => SetI("osd", value ? 1 : 0); }
    public static bool Captions { get => I("captions", 0) == 1; set => SetI("captions", value ? 1 : 0); }
    public static bool HeadLean { get => I("headlean", 1) == 1; set => SetI("headlean", value ? 1 : 0); }
    public static int Picture { get => I("picture", 1); set => SetI("picture", Mathf.Clamp(value, 0, 2)); }   // 0 clean, 1 tape, 2 worn
    public static float Sensitivity { get => F("sens", 1.0f); set => SetF("sens", Mathf.Clamp(value, 0.2f, 3f)); }
    public static float Brightness { get => F("bright", 1.0f); set => SetF("bright", Mathf.Clamp(value, 0.5f, 1.8f)); }
    public static string[] PictureNames = { "CLEAN", "TAPE", "WORN" };
}
