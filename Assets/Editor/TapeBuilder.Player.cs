using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class TapeBuilder
{
    static void BuildPlayerAndGame(Refs refs, List<HingeDoor> doors)
    {
        // monitors -> their cameras
        var cams = Object.FindObjectsByType<CctvCamera>(FindObjectsSortMode.None);
        foreach (var (screen, label) in refs.screens) screen.source = cams.FirstOrDefault(c => c.label == label);
        foreach (var (screen, label) in refs.screens) if (!screen.source) Debug.LogError("No camera " + label);

        // Room 213 is bolted from inside at the start (a safe room reached through 215)
        var d213 = doors.FirstOrDefault(d => d.portalName == "Door 213");
        if (d213) d213.bolted = true;

        // player
        var player = new GameObject("Player");
        player.layer = 8;
        player.transform.position = LevelLayout.BedroomSpawn;
        var cc = player.AddComponent<CharacterController>();
        cc.radius = 0.25f;          // a person's width: 0.9 m doorways leave a comfortable gap (0.3 snagged on door frames)
        cc.height = 1.75f;
        cc.center = new Vector3(0, 0.875f, 0);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.03f;
        var motor = player.AddComponent<PlayerMotor>();
        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.04f;
        cam.farClipPlane = 45f;
        cam.cullingMask = ~((1 << 15) | (1 << 5) | (1 << 16));
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.allowHDR = true;
        cam.allowMSAA = false;
        var ucd = camGo.AddComponent<UniversalAdditionalCameraData>();
        ucd.renderPostProcessing = false;
        ucd.antialiasing = AntialiasingMode.None;
        camGo.AddComponent<AudioListener>();
        var eng = camGo.AddComponent<AudioEngine>();
        eng.Listener = camGo.transform;
        camGo.AddComponent<ReverbBus>();
        camGo.AddComponent<GeometricAcoustics>();
        var test = camGo.AddComponent<AudioTest>();
        test.head = camGo.transform;
        // the camcorder's video light: a warm spot just under and beside the lens
        var lightGo = new GameObject("Camera light");
        lightGo.transform.SetParent(camGo.transform, false);
        lightGo.transform.localPosition = new Vector3(0.07f, -0.07f, 0.1f);
        var spot = lightGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 105f;
        spot.innerSpotAngle = 24f;
        spot.range = 14f;
        spot.intensity = 7f;
        spot.color = new Color(1f, 0.93f, 0.8f);
        spot.shadows = LightShadows.Soft;          // your camera light's shadows have soft edges
        spot.enabled = false;
        lightGo.AddComponent<UniversalAdditionalLightData>();
        var cl = camGo.AddComponent<CameraLight>();
        cl.spot = spot;
        var inter = camGo.AddComponent<Interactor>();
        inter.cam = cam;
        motor.cameraTransform = camGo.transform;
        player.AddComponent<Footsteps>();
        var hands = player.AddComponent<PlayerHands>();
        hands.glassMat = M("Glass_Bottle");
        hands.radioMat = M("Plastic_Device");

        // game
        var game = new GameObject("Game");
        game.AddComponent<LevelGraph>();
        game.AddComponent<AcousticWorld>();
        game.AddComponent<AmbienceDirector>();
        game.AddComponent<Breath>();
        game.AddComponent<PowerGrid>();
        game.AddComponent<Lightning>();
        var g = game.AddComponent<Game>();
        g.player = motor;
        g.playerCamera = cam;
        g.skinMat = M("Skin");
        g.blobMat = M("Blob_Face");
        g.clothMat = M("Fabric_Cloth");
        g.faceMat = M("FaceTape");
        g.securityVcr = refs.vcr;
        g.liftPanel = refs.liftPanel;
        g.bedroomTv = refs.bedroomTv;
        g.bedroomParcel = refs.bedroomParcel;
        g.bedroomHall = refs.bedroomHall;
        g.bedroomLamp = refs.bedroomLamp;
        g.houseLamps = refs.houseLamps.ToArray();
        g.tapeLights = refs.tapeLights.ToArray();
        g.recorderDoor = doors.FirstOrDefault(d => d.portalName == "Door Recorder Room");
        var vhs = game.AddComponent<VhsScreen>();
        vhs.gameCamera = cam;
        vhs.vhsMat = M("VHSFX");
        game.AddComponent<Hud>();
        EditorUtility.SetDirty(game);
    }
}
