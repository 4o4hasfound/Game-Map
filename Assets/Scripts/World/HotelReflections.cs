using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Room reflections remain geometry-derived, but only one time-sliced capture may be in flight.
// ViaScripting prevents the Editor and floor activation from queuing dozens of OnAwake cubemaps together.
public class HotelReflections : MonoBehaviour
{
    ReflectionProbe[] probes;
    readonly Dictionary<ReflectionProbe,float> captured = new Dictionary<ReflectionProbe,float>();
    ReflectionProbe pending;
    int renderId;
    float next;
    float nextSelection;
    readonly ReflectionProbe[] nearby=new ReflectionProbe[3];
    readonly float[] distances=new float[3];
    public static int Completed { get; private set; }
    public static int Pending => instance && instance.pending ? 1 : 0;
    static HotelReflections instance;
    void Awake()
    {
        instance=this;Completed=0;
        probes=FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        foreach(var probe in probes){probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting;probe.enabled=false;}
        next=Time.time+.75f;
    }
    void Update()
    {
        if(!Camera.main)return;
        var position=Camera.main.transform.position;
        if(Time.time>=nextSelection)
        {
            nextSelection=Time.time+.4f;
            for(int i=0;i<3;i++){nearby[i]=null;distances[i]=float.MaxValue;}
            foreach(var probe in probes)
            {
                if(!probe||!probe.gameObject.activeInHierarchy)continue;
                var bounds=new Bounds(probe.transform.TransformPoint(probe.center),probe.size);
                float d=Vector3.Distance(position,bounds.ClosestPoint(position));if(d>10f)continue;
                d+=Vector3.Distance(position,probe.transform.position)*.02f;
                for(int i=0;i<3;i++)if(d<distances[i]){for(int j=2;j>i;j--){nearby[j]=nearby[j-1];distances[j]=distances[j-1];}nearby[i]=probe;distances[i]=d;break;}
            }
            foreach(var probe in probes)if(probe)probe.enabled=probe==pending||probe==nearby[0]||probe==nearby[1]||probe==nearby[2];
        }
        if(pending)
        {
            if(!pending.IsFinishedRendering(renderId))return;
            captured[pending]=Time.time;pending=null;Completed++;next=Time.time+.25f;
        }
        if(Time.time<next || !Camera.main || !QualitySettings.realtimeReflectionProbes)return;
        next=Time.time+.25f;
        ReflectionProbe best=null;float score=float.MaxValue;
        foreach(var probe in probes)
        {
            if(!probe || !probe.isActiveAndEnabled)continue;
            float distance=Vector3.Distance(position,probe.bounds.ClosestPoint(position));
            if(distance>10f)continue;
            bool seen=captured.TryGetValue(probe,out float when);
            if(seen && Time.time-when<45f)continue;
            float priority=distance+(seen?8f:0f)+Vector3.Distance(position,probe.transform.position)*.02f;
            if(priority<score){score=priority;best=probe;}
        }
        if(!best)return;
        pending=best;renderId=best.RenderProbe();
    }
}
