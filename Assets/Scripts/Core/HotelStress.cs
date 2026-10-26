using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

// Actual runtime routes, moving stalker, dynamic doors, and recording-derived spatial voices on every floor.
public class HotelStress : MonoBehaviour
{
    IEnumerator Start()
    {
        var args=System.Environment.GetCommandLineArgs();int ix=System.Array.IndexOf(args,"-stress");
        if(ix<0||ix+1>=args.Length)yield break;
        string dir=args[ix+1];Directory.CreateDirectory(dir);
        yield return new WaitForSeconds(3f);
        var world=HotelWorld.Instance;var player=FindFirstObjectByType<PlayerMotor>();
        player.Frozen=true;var body=player.GetComponent<Rigidbody>();body.isKinematic=true;body.interpolation=RigidbodyInterpolation.None;
        Application.targetFrameRate=60;QualitySettings.vSyncCount=0;AudioTiming.Enabled=true;
        var report=new StringBuilder("Both-scene stress harness: geometry propagation + 24 recording voices + moving stalker, 3 floors.\n");
        for(int floor=0;floor<3;floor++)
        {
            world.SetLevel(floor);NavBaker.Instance.Bake(floor);
            while(!NavBaker.Instance.Ready(floor))yield return null;
            var level=world.levels[floor];var positions=new List<Vector3>();
            foreach(var room in level.rooms)
            {
                if(room.kind=="shaft")continue;
                var L=level.layers[room.layer];int x=-1,y=-1;
                for(int j=1;j<L.h-1&&x<0;j++)for(int i=1;i<L.w-1;i++)
                    if(L.cells[j*L.w+i]==room.id && L.cells[j*L.w+i+1]==room.id && L.cells[(j+1)*L.w+i]==room.id){x=i;y=j;break;}
                if(x>=0 && NavMesh.SamplePosition(world.ToUnity(level.ox+x+0.5f,y+0.5f,L.z),out var hit,2f,-1))positions.Add(hit.position);
            }
            if(positions.Count<2){report.AppendLine(level.name+": FAIL insufficient navigable rooms");continue;}
            body.position=positions[0];player.transform.position=body.position;Physics.SyncTransforms();
            var targetPath=new NavMeshPath();Vector3 target=positions[positions.Count-1];
            foreach(var pos in positions)if(NavMesh.CalculatePath(body.position,pos,-1,targetPath)&&targetPath.status==NavMeshPathStatus.PathComplete&&targetPath.corners.Length>3){target=pos;break;}
            NavMesh.CalculatePath(body.position,target,-1,targetPath);int corner=1;
            var sounds=new List<GameObject>();
            for(int s=0;s<24;s++)
            {
                var g=new GameObject("Stress recorded source");g.transform.position=positions[s%positions.Count]+Vector3.up;
                g.AddComponent<AudioSource>();var v=g.AddComponent<SpatialVoice>();v.volume=s<4?.08f:.025f;v.maxDistance=35;v.label="stress source";
                v.Play(SoundBank.Pick(s%3==0?"radio_station_0":s%3==1?"amb_pump":"amb_water_trickle"),true);sounds.Add(g);
            }
            if(NavBaker.RandomPoint(body.position,7,15,out var stalkAt))Stalker.Spawn(stalkAt);
            var frames=new List<float>(1800);int maxAudible=0,maxSources=0,maxRays=0;double maxSolve=0;float walked=0;
            AudioEngine.StartRecording();
            var doors=FindObjectsByType<HingeDoor>(FindObjectsSortMode.None);
            for(float t=0;t<16f;t+=Time.deltaTime)
            {
                var previous=body.position;
                if(corner<targetPath.corners.Length)
                {
                    var p=Vector3.MoveTowards(previous,targetPath.corners[corner],2.2f*Time.deltaTime);
                    body.position=p;player.transform.position=p;if(Vector3.Distance(p,targetPath.corners[corner])<.1f)corner++;
                }
                walked+=Vector3.Distance(previous,body.position);
                foreach(var door in doors)if(!door.locked&&Vector3.Distance(door.transform.position,body.position)<2f)door.OpenAway(body.position);
                if(Stalker.Active)NoiseBus.Emit(body.position,2f,true);
                yield return null;
                frames.Add(Time.unscaledDeltaTime*1000);int audible=0,playing=0;
                foreach(var v in AudioEngine.Voices)if(v&&v.IsPlaying){playing++;if(v.Audible)audible++;}
                maxAudible=Mathf.Max(maxAudible,audible);maxSources=Mathf.Max(maxSources,playing);maxRays=Mathf.Max(maxRays,AcousticWork.ReflectionRays);maxSolve=System.Math.Max(maxSolve,AcousticWork.SolveMilliseconds);
            }
            AudioEngine.StopRecording(Path.Combine(dir,"floor"+floor+".wav"));
            if(System.Array.IndexOf(args,"-ab")>=0)
            {
                // the same route again with one recent change switched off at a time; CPU / GPU split from FrameTimingManager
                var cam=Camera.main;var fx=cam?cam.GetComponent<VHSPostFX>():null;var lamp=cam?cam.GetComponent<CamcorderLight>():null;
                var steps=player.GetComponent<Footsteps>();
                string[] configs=System.Array.IndexOf(args,"-abgpu")>=0
                    ? new[]{"current","no VHS post","no tape pass","no motion/haze pass","no bloom chain","current again"}
                    : new[]{"current","no VHS post","pause parking (old)","no inaudible parking","no flashlight script","no breathing script","current again"};
                foreach(var config in configs)
                {
                    if(fx)fx.enabled=config!="no VHS post";
                    VHSPostFX.DiagSkipTape=config=="no tape pass";VHSPostFX.DiagSkipMotion=config=="no motion/haze pass";VHSPostFX.DiagSkipBloom=config=="no bloom chain";
                    SpatialVoice.DiagPauseParking=config=="pause parking (old)";
                    SpatialVoice.DiagNoInaudibleParking=config=="no inaudible parking";
                    if(lamp)lamp.enabled=config!="no flashlight script";
                    if(steps)steps.enabled=config!="no breathing script";
                    body.position=positions[0];player.transform.position=body.position;Physics.SyncTransforms();
                    NavMesh.CalculatePath(body.position,target,-1,targetPath);int c2=1;
                    yield return new WaitForSeconds(1f);
                    var ft=new List<float>(600);double cpuMain=0,cpuRender=0,gpu=0;int timed=0;var timing=new FrameTiming[1];
                    for(float t=0;t<8f;t+=Time.deltaTime)
                    {
                        if(c2<targetPath.corners.Length)
                        {
                            var p=Vector3.MoveTowards(body.position,targetPath.corners[c2],2.2f*Time.deltaTime);
                            body.position=p;player.transform.position=p;if(Vector3.Distance(p,targetPath.corners[c2])<.1f)c2++;
                        }
                        FrameTimingManager.CaptureFrameTimings();
                        if(FrameTimingManager.GetLatestTimings(1,timing)>0){cpuMain+=timing[0].cpuMainThreadFrameTime;cpuRender+=timing[0].cpuRenderThreadFrameTime;gpu+=timing[0].gpuFrameTime;timed++;}
                        yield return null;ft.Add(Time.unscaledDeltaTime*1000);
                    }
                    ft.Sort();timed=Mathf.Max(timed,1);
                    report.AppendLine($"    A/B {config,-22} p95 {ft[(int)(ft.Count*.95f)]:0.00} ms  median {ft[ft.Count/2]:0.00} ms  cpu main {cpuMain/timed:0.00}  render thread {cpuRender/timed:0.00}  gpu {gpu/timed:0.00} ms");
                }
                if(fx)fx.enabled=true;VHSPostFX.DiagSkipTape=VHSPostFX.DiagSkipMotion=VHSPostFX.DiagSkipBloom=false;SpatialVoice.DiagPauseParking=false;SpatialVoice.DiagNoInaudibleParking=false;if(lamp)lamp.enabled=true;if(steps)steps.enabled=true;
            }
            frames.Sort();float p95=frames.Count>0?frames[(int)(frames.Count*.95f)]:0;
            report.AppendLine("    adaptive lights: "+LightManager.QualityReport);report.AppendLine($"{level.name}: sampled navigable rooms {positions.Count}; route {targetPath.status}; travelled {walked:0.0} m; frame p95 {p95:0.00} ms; peak solves {maxSolve:0.000} ms; source reflection rays/frame <= {maxRays}; logical playing {maxSources}; audible {maxAudible}");
            foreach(var g in sounds)Destroy(g);if(Stalker.Active)Destroy(Stalker.Active.gameObject);
            yield return new WaitForSeconds(1f);
        }
        AudioSettings.GetDSPBufferSize(out int buffer,out int buffers);
        report.AppendLine($"Output {AudioSettings.outputSampleRate} Hz, {buffer} frames x {buffers} buffers.");report.AppendLine(AudioTiming.Report);
        File.WriteAllText(Path.Combine(dir,"stress.txt"),report.ToString());Application.Quit();
    }
}
