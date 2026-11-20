using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The things that carry you from one part to the next: the safe code halves and the manager's safe (part one), the
// fuse, the lift key, the lift call panel and its car (part two). Part three is in TapeBuilder.Roof.cs.
public static partial class TapeBuilder
{
    // something to take: a code half, a key, the fuse, the keycard
    static KeyItem Item(Transform g, string name, Vector3 pos, Vector3 size, string mat, string id, string label, bool reachable = true)
    {
        var go = Box(name, g, pos, size, M(mat), LInteract, true, Quaternion.Euler(0, Rnd(-25, 25), 0), false, false);
        var bc = go.GetComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(Mathf.Max(size.x, 0.25f), Mathf.Max(size.y, 0.25f), Mathf.Max(size.z, 0.25f));
        var ki = go.AddComponent<KeyItem>();
        ki.keyId = id; ki.label = label; ki.reachable = reachable;
        return ki;
    }

    // ------------------------------------------------------------------ part one: the manager's wall safe
    static Safe ManagerSafe(Transform g)
    {
        var p = LevelLayout.SafeSpot;
        Box("Safe body", g, p + new Vector3(0.05f, 0f, 0f), new Vector3(0.12f, 0.55f, 0.55f), M("Metal_Device"), LProp, true, null, false);
        // the door, hinged on its south edge so it swings into the room
        var hinge = new GameObject("Safe hinge").transform;
        hinge.SetParent(g, false);
        hinge.position = p + new Vector3(0.13f, 0f, -0.25f);
        var door = Box("Safe door", hinge, p + new Vector3(0.135f, 0f, 0f), new Vector3(0.04f, 0.5f, 0.5f), M("Metal_Lift"), LProp, false, null, false, false);
        Box("Dial", door.transform, p + new Vector3(0.165f, 0.05f, 0.05f), new Vector3(0.03f, 0.1f, 0.1f), M("Brass_Fitting"), LProp, false, null, false, false);
        Box("Handle", door.transform, p + new Vector3(0.165f, -0.12f, 0.05f), new Vector3(0.03f, 0.04f, 0.14f), M("Brass_Fitting"), LProp, false, null, false, false);
        var use = new GameObject("Use safe"); use.transform.SetParent(g, false); use.transform.position = p + new Vector3(0.2f, 0f, 0f); use.layer = LInteract;
        var bc = use.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.3f, 0.6f, 0.6f);
        var safe = use.AddComponent<Safe>();
        safe.door = hinge;
        // the key lies at the mouth of the safe, hidden until the door opens: it used to sit inside the safe's solid body
        // and behind its trigger, where a player's aim could never reach it
        safe.key = Item(g, "Basement key", p + new Vector3(0.17f, -0.25f, 0.05f), new Vector3(0.05f, 0.02f, 0.11f), "Brass_Fitting", "basement", "basement key", false);
        // it rests on the safe's open lip (it used to float), with a paper tag that catches the camera light
        Box("Safe lip", g, p + new Vector3(0.16f, -0.27f, 0f), new Vector3(0.1f, 0.02f, 0.5f), M("Metal_Device"), LProp, false, null, false, false);
        var keyTr = safe.key.transform;
        Box("Key tag", keyTr, keyTr.TransformPoint(new Vector3(0f, 0f, 0.09f)), new Vector3(0.05f, 0.006f, 0.07f), M("Paper_Note"), LInteract, false, keyTr.rotation, false, false);
        safe.key.gameObject.SetActive(false);
        return safe;
    }

    // ------------------------------------------------------------------ part two: the lift call panel and its car
    static LiftPanel LiftCallPanel(Transform g)
    {
        var p = LevelLayout.LiftPanelSpot;
        Box("Lift call panel", g, p, new Vector3(0.24f, 0.4f, 0.05f), M("Metal_Lift"), LProp, true, null, false);
        Box("Keyhole", g, p + new Vector3(0f, -0.1f, -0.03f), new Vector3(0.04f, 0.04f, 0.01f), M("Brass_Fitting"), LProp, false, null, false, false);
        Box("Call button", g, p + new Vector3(0f, 0.06f, -0.03f), new Vector3(0.06f, 0.06f, 0.015f), M("Lamp_Red"), LProp, false, null, false, false);
        var use = new GameObject("Use lift panel"); use.transform.SetParent(g, false); use.transform.position = p + new Vector3(0f, 0f, -0.15f); use.layer = LInteract;
        var bc = use.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.45f, 0.55f, 0.3f);
        var panel = use.AddComponent<LiftPanel>();

        // the car behind the doors: lit, waiting (switched on when it arrives)
        var car = new GameObject("Lift car");
        car.transform.SetParent(g, false);
        var c = LevelLayout.LiftCarSpot;
        Box("Car floor", car.transform, c + new Vector3(0f, -0.05f, 0f), new Vector3(2.0f, 0.1f, 2.3f), M("Metal_Floor"), LStructure, true, null, false, false);
        Box("Car ceiling", car.transform, c + new Vector3(0f, 2.45f, 0f), new Vector3(2.0f, 0.1f, 2.3f), M("Metal_Lift"), LStructure, true, null, false, false);
        Box("Car wall W", car.transform, c + new Vector3(-1.02f, 1.2f, 0f), new Vector3(0.06f, 2.4f, 2.3f), M("Metal_Lift"), LStructure, true, null, false, false);
        Box("Car wall E", car.transform, c + new Vector3(1.02f, 1.2f, 0f), new Vector3(0.06f, 2.4f, 2.3f), M("Metal_Lift"), LStructure, true, null, false, false);
        Box("Car wall N", car.transform, c + new Vector3(0f, 1.2f, 1.17f), new Vector3(2.0f, 2.4f, 0.06f), M("Metal_Lift"), LStructure, true, null, false, false);
        Box("Handrail", car.transform, c + new Vector3(0f, 0.95f, 1.1f), new Vector3(1.7f, 0.05f, 0.05f), M("Brass_Fitting"), LProp, false, null, false, false);
        MakeLamp(car.transform, "Car light", c + new Vector3(0f, 2.35f, 0f), 3.5f, 0.45f, Warm, true, false, new Vector3(0.5f, 0.04f, 0.5f), Lamp.Pattern.Buzz);
        car.SetActive(false);
        panel.car = car;

        // the closed doors (and what is taped to them) disappear when the car arrives
        var door = new Vector3(47f, LevelLayout.Basement + 1.05f, 44f);
        panel.hideOnArrival = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => (t.name == "Closed Doors Lift Pit Doors" || t.name == "Gap" || t.name == "Out of order sign" || t.name == "Lift indicator") && Vector3.Distance(t.position, door) < 1.6f)
            .Select(t => t.gameObject).ToArray();
        return panel;
    }

    // the stair collapses behind you: no way back up from the basement
    static void CollapsedStair(Transform g)
    {
        var p = LevelLayout.W(22.4f, 42.2f, -1, 3.1f);
        Box("Collapsed stair (blocker)", g, p, new Vector3(1.4f, 2.6f, 2.6f), M("Rubble_Floor"), LStructure, true, null, false);
        for (int i = 0; i < 6; i++)
            Box("Rubble", g, p + new Vector3(Rnd(0.6f, 1.4f), Rnd(-1.4f, -0.8f), Rnd(-1.0f, 1.0f)), new Vector3(Rnd(0.3f, 0.7f), Rnd(0.2f, 0.4f), Rnd(0.3f, 0.6f)), M("Rubble_Floor"), LProp, false, Quaternion.Euler(Rnd(-20, 20), Rnd(0, 360), Rnd(-20, 20)), false);
    }
}
