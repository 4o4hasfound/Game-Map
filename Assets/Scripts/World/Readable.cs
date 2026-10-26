using UnityEngine;

// Paper left behind: newspapers on the floor, memos and notes on desks and bedside tables. E reads it on the
// camcorder's display. The text is chosen by where it lies, so the staff office has staff memos and so on.
public class Readable : Interactable
{
    public string kind = "note";          // newspaper, memo, note, guest
    public string text;

    public override string Prompt => kind == "newspaper" ? "READ THE NEWSPAPER" : "READ";

    static readonly string[] Papers =
    {
        "THE MERIDIAN COURIER  OCT 30 2003\nHOTEL TO CLOSE AFTER 71 YEARS\n\"guests asked to vacate by friday\"",
        "COASTAL GAZETTE\nSEARCH FOR MISSING NIGHT PORTER\nENTERS SECOND WEEK",
        "THE MERIDIAN COURIER\nELEVATOR INSPECTOR: \"I WOULD NOT\nRIDE THE GUEST CARS\"",
        "COASTAL GAZETTE  CLASSIFIEDS\nPIANO TUNER WANTED - GRAND HOTEL\nURGENT. NIGHT WORK. ASK FOR MR. HALE",
        "THE MERIDIAN COURIER\nSTORM FLOODS BASEMENT LEVEL\nBOILER CREW SENT HOME",
        "COASTAL GAZETTE\nFIRE ON SEVENTH FLOOR CONTAINED\nCAUSE \"UNDER INVESTIGATION\"",
    };
    static readonly string[] Memos =
    {
        "STAFF MEMO\nDO NOT USE THE GUEST ELEVATORS.\nSERVICE LIFT ONLY UNTIL FURTHER NOTICE.",
        "HOUSEKEEPING\nROOM 717 - DO NOT SERVICE.\nLEAVE TRAYS OUTSIDE THE DOOR.",
        "NIGHT DESK\nTHE LOBBY PIANO IS NOT TO BE PLAYED.\nIT IS NOT CONNECTED TO ANYTHING.",
        "MAINTENANCE\nB1 BREAKERS KEEP TRIPPING.\nIF THE LIGHTS GO, STAY WHERE YOU ARE.",
        "SECURITY LOG 02:40\nCAMERA 7 SHOWS SOMEONE ON THE\nMEZZANINE. NOBODY UP THERE.",
        "MANAGER\nTHE TAPES FROM THE 7TH FLOOR\nARE TO BE DESTROYED. ALL OF THEM.",
    };
    static readonly string[] Notes =
    {
        "hotel notepad, handwritten:\n\"it follows the sound. keep quiet.\"",
        "hotel notepad, handwritten:\n\"the TV keeps showing my room.\nI turned it off. it was on again.\"",
        "hotel notepad, handwritten:\n\"checked out? no. they say\nI checked out on the 31st.\"",
        "hotel notepad, handwritten:\n\"don't run. it hears the doors.\"",
        "a room service bill:\nROOM 703  ONE BOTTLE  CHAMPAGNE\nSIGNED  - illegible -",
        "hotel notepad, handwritten:\n\"the basement goes further down\nthan the plans say\"",
    };

    void Start()
    {
        if (string.IsNullOrEmpty(text))
        {
            var pool = kind == "newspaper" ? Papers : kind == "memo" ? Memos : Notes;
            int seed = Mathf.Abs(Mathf.RoundToInt(transform.position.x * 7.3f + transform.position.z * 3.1f));
            text = pool[seed % pool.Length];
        }
    }

    public override void Use(PlayerMotor player)
    {
        var osd = FindAnyObjectByType<VHSOverlay>();
        if (osd) osd.Announce(text, 7f);
        OneShot("cloth", transform.position, 0.05f, 1.3f, 0.3f, 5f);    // the rustle of paper
    }
}
