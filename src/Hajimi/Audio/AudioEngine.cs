using System.Runtime.InteropServices;
using Hajimi.Behavior;

namespace Hajimi.Audio;

// PCM is decoded once. A small per-instance waveOut queue mixes sound without changing device volume.
public sealed class AudioEngine : IDisposable
{
    [StructLayout(LayoutKind.Sequential,Pack=2)] private struct Format { public ushort Tag,Channels; public uint Rate,Bytes; public ushort Align,Bits,Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public IntPtr Data; public uint Length,Recorded; public UIntPtr User; public uint Flags,Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
    private readonly List<(IntPtr Header,IntPtr Data)> buffers=new();
    private readonly Dictionary<string,WaveClip> clips=new();
    private readonly AudioPolicy policy=new();
    private IntPtr device;
    private float gain;
    private long sampleIndex;
    private AudioCue? cue;
    private PetState state=PetState.Sleep;
    private bool snore;
    private const int SamplesPerBuffer=1102;
    private static readonly uint HeaderSize=(uint)Marshal.SizeOf<Header>();
    public bool Muted { get; set; }
    public bool Paused { get; set; }
    public float Volume { get; set; }=.18f;
    public string? Warning { get; private set; }
    public string? CurrentKey=>cue?.Key;
    public double CurrentDuration=>cue!=null && clips.TryGetValue(cue.Key,out var clip)?clip.Samples.Length/(double)clip.Rate:1.7;
    public bool SnoreEnabled
    {
        get=>snore;
        set { if(snore==value) return; snore=value; if(state==PetState.Sleep) StateChanged(state); }
    }
    public float MouthAmount
    {
        get
        {
            if(cue==null || !clips.TryGetValue(cue.Key,out var clip)) return 0;
            int at=ClipIndex(Math.Max(0,sampleIndex-2205),cue,clip);
            if(at<0) return 0;
            double power=0; int n=Math.Min(400,clip.Samples.Length-at);
            for(int i=0;i<n;i++) power+=clip.Samples[at+i]*clip.Samples[at+i];
            return (float)Math.Clamp(Math.Sqrt(power/Math.Max(1,n))/.18,0,1);
        }
    }
    public AudioEngine(string root)
    {
        var files=new Hajimi.Assets.AssetSource(Path.GetDirectoryName(root)!);
        foreach(string key in new[] { "growl","hiss-1","hiss-2","hiss-3","scared","meow","snore","satisfied" })
        {
            try { using var stream=files.OpenRead("audio/"+key+".wav"); clips[key]=WaveClip.Load(stream); }
            catch(Exception e) when(e is IOException or InvalidDataException) { Warning=$"{key}: {e.Message}"; }
        }
        var format=new Format { Tag=1,Channels=1,Rate=22050,Bytes=44100,Align=2,Bits=16 };
        uint result=waveOutOpen(out device,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0);
        if(result!=0) { Warning=$"音频设备不可用（{result}），已静音运行。"; device=IntPtr.Zero; return; }
        for(int i=0;i<3;i++)
        {
            var data=Marshal.AllocHGlobal(SamplesPerBuffer*2); var header=Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.StructureToPtr(new Header { Data=data,Length=SamplesPerBuffer*2 },header,false);
            if(waveOutPrepareHeader(device,header,HeaderSize)!=0)
            { Marshal.FreeHGlobal(data); Marshal.FreeHGlobal(header); Warning="音频缓冲初始化失败。"; Dispose(); return; }
            buffers.Add((header,data));
        }
    }
    public void StateChanged(PetState next)
    {
        state=next; cue=policy.Select(next,snore); sampleIndex=0;
        // Flush the preceding cry on a state interruption instead of layering multiple full recordings.
        if(device!=IntPtr.Zero) waveOutReset(device);
    }
    public void Silence() { Paused=true; }
    public void Pump()
    {
        if(device==IntPtr.Zero) return;
        WaveClip? clip=null;
        if(cue!=null) clips.TryGetValue(cue.Key,out clip);
        foreach(var buffer in buffers)
        {
            var header=Marshal.PtrToStructure<Header>(buffer.Header);
            if((header.Flags&16)!=0 && (header.Flags&1)==0) continue;
            var pcm=new short[SamplesPerBuffer];
            for(int i=0;i<pcm.Length;i++)
            {
                gain+=((Muted || Paused?0:Volume)-gain)*.0025f;
                int at=cue!=null && clip!=null?ClipIndex(sampleIndex,cue,clip):-1;
                float sample=at>=0?clip!.Samples[at]:0;
                pcm[i]=(short)(Math.Clamp(sample*gain,-1,1)*32767);
                if(!Paused) sampleIndex++;
            }
            Marshal.Copy(pcm,0,buffer.Data,pcm.Length);
            waveOutWrite(device,buffer.Header,HeaderSize);
        }
    }
    private static int ClipIndex(long index,AudioCue cue,WaveClip clip)
    {
        if(cue.Loop) return (int)(index%clip.Samples.Length);
        if(cue.RepeatSeconds>0) index%=Math.Max(clip.Samples.Length,(long)(cue.RepeatSeconds*clip.Rate));
        return index<clip.Samples.Length?(int)index:-1;
    }
    public void Dispose()
    {
        if(device==IntPtr.Zero) return;
        waveOutReset(device);
        foreach(var b in buffers) { waveOutUnprepareHeader(device,b.Header,HeaderSize); Marshal.FreeHGlobal(b.Header); Marshal.FreeHGlobal(b.Data); }
        buffers.Clear(); waveOutClose(device); device=IntPtr.Zero;
    }
}
