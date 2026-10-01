namespace Hajimi.Audio;

public sealed record WaveClip(float[] Samples, int Rate)
{
    public static WaveClip Load(string path)
    {
        using var stream=File.OpenRead(path);
        return Load(stream);
    }
    public static WaveClip Load(Stream stream)
    {
        using var r=new BinaryReader(stream,System.Text.Encoding.UTF8,leaveOpen:true);
        if (new string(r.ReadChars(4))!="RIFF") throw new InvalidDataException("WAV 缺少 RIFF 头。");
        r.ReadInt32(); if (new string(r.ReadChars(4))!="WAVE") throw new InvalidDataException("不是 WAV 音频。");
        int rate=0, channels=0, bits=0, format=0; byte[]? data=null;
        while (r.BaseStream.Position+8<=r.BaseStream.Length)
        {
            string chunk=new(r.ReadChars(4)); int length=r.ReadInt32();
            if (length<0 || length>r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException("WAV 数据长度错误。");
            long end=r.BaseStream.Position+length;
            if (chunk=="fmt ") { format=r.ReadInt16(); channels=r.ReadInt16(); rate=r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits=r.ReadInt16(); }
            else if (chunk=="data") data=r.ReadBytes(length);
            r.BaseStream.Position=end+(length%2);
        }
        if (format!=1 || channels!=1 || bits!=16 || rate!=22050 || data==null) throw new InvalidDataException("音频需为 22050Hz / 单声道 / PCM16 WAV。");
        var samples=new float[data.Length/2];
        for(int i=0;i<samples.Length;i++) samples[i]=BitConverter.ToInt16(data,i*2)/32768f;
        if (samples.Length==0) throw new InvalidDataException("音频为空。");
        return new(samples,rate);
    }

    public static void CreateDemoSounds(string directory)
    {
        Directory.CreateDirectory(directory);
        Write(Path.Combine(directory,"growl.wav"),6,false);
        Write(Path.Combine(directory,"hiss.wav"),.8,true);
    }
    private static void Write(string path,double duration,bool hiss)
    {
        const int rate=22050; int n=(int)(rate*duration); var data=new short[n];
        var rng=new Random(hiss?71:39); double low=0,phase=0,previous=0;
        for(int i=0;i<n;i++)
        {
            double t=i/(double)rate, noise=rng.NextDouble()*2-1;
            low=.94*low+.06*noise;
            phase+=2*Math.PI*(61+5*Math.Sin(2*Math.PI*t/3))/rate;
            double pulse=.72+.28*Math.Sin(2*Math.PI*23*t);
            double signal;
            if(hiss) { signal=(noise-previous*.6)*Math.Sin(Math.PI*Math.Min(1,t/duration))*.52; previous=noise; }
            else signal=(Math.Sin(phase)*.23+Math.Sin(phase*2)*.14+Math.Sin(phase*3)*.07+low*1.4)*pulse*(.78+.22*Math.Sin(2*Math.PI*t/2));
            double envelope=Math.Min(1,Math.Min(t/.08,(duration-t)/.12));
            data[i]=(short)(Math.Clamp(signal*envelope,-.95,.95)*32767);
        }
        using var w=new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36+n*2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate*2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(n*2);
        foreach(var sample in data) w.Write(sample);
    }
}
