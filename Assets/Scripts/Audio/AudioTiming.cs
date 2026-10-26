using System.Diagnostics;
using System.Threading;
public static class AudioTiming
{
    public static bool Enabled;
    static long voiceTicks, maxTicks, blocks, overBudget;
    public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public static void Voice(long start) { if(start!=0) Interlocked.Add(ref voiceTicks,Stopwatch.GetTimestamp()-start); }
    public static void Block(long start,int frames,int sampleRate)
    {
        if(start==0)return;
        long elapsed=Stopwatch.GetTimestamp()-start+Interlocked.Exchange(ref voiceTicks,0);
        Interlocked.Increment(ref blocks);
        if(elapsed/(double)Stopwatch.Frequency > frames/(double)sampleRate)Interlocked.Increment(ref overBudget);
        long previous=Interlocked.Read(ref maxTicks);
        while(elapsed>previous) { long found=Interlocked.CompareExchange(ref maxTicks,elapsed,previous);if(found==previous)break;previous=found; }
    }
    public static string Report => $"Measured DSP blocks {Interlocked.Read(ref blocks)}, peak custom source+reverb CPU {Interlocked.Read(ref maxTicks)*1000.0/Stopwatch.Frequency:0.000} ms, blocks over buffer duration {Interlocked.Read(ref overBudget)}. This measures custom DSP CPU, not hardware underruns or total FMOD cost.";
}
