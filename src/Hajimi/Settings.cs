using System.Text.Json;
using Hajimi.Behavior;

namespace Hajimi;

public sealed class Settings
{
    public float Scale { get; set; } = .85f;
    public bool Muted { get; set; }
    public float Volume { get; set; } = .18f;
    public double IdleSeconds { get; set; } = 35;
    public int ClickThreshold { get; set; } = 3;
    public bool AlwaysOnTop { get; set; } = true;
    public bool SnoreEnabled { get; set; }
    public bool GoodCat { get; set; }
    public bool Neutered { get; set; }
    public bool MouseStealEnabled { get; set; }
    public bool RandomSleep { get; set; }
    public double SleepMinSeconds { get; set; } = 20;
    public double SleepMaxSeconds { get; set; } = 70;
    public bool FeedingEnabled { get; set; } = true;
    public double HungerSeconds { get; set; } = 180;
    public float? BowlX { get; set; }
    public float? BowlY { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Hajimi");
    public static string FilePath => Path.Combine(Folder,"settings.json");
    public static Settings Load()
    {
        try
        {
            var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            s.Scale=float.IsFinite(s.Scale)?Math.Clamp(s.Scale,.4f,1.6f):.85f;
            s.Volume=float.IsFinite(s.Volume)?Math.Clamp(s.Volume,0,1):.18f;
            s.IdleSeconds=double.IsFinite(s.IdleSeconds)?Math.Clamp(s.IdleSeconds,5,3600):35;
            s.SleepMinSeconds=double.IsFinite(s.SleepMinSeconds)?Math.Clamp(s.SleepMinSeconds,5,3600):20;
            s.SleepMaxSeconds=double.IsFinite(s.SleepMaxSeconds)?Math.Clamp(s.SleepMaxSeconds,s.SleepMinSeconds,3600):Math.Max(70,s.SleepMinSeconds);
            s.HungerSeconds=double.IsFinite(s.HungerSeconds)?Math.Clamp(s.HungerSeconds,30,3600):180;
            s.ClickThreshold=Math.Clamp(s.ClickThreshold,2,8);
            if (s.X.HasValue && !float.IsFinite(s.X.Value)) s.X=null;
            if (s.Y.HasValue && !float.IsFinite(s.Y.Value)) s.Y=null;
            if (s.BowlX.HasValue && !float.IsFinite(s.BowlX.Value)) s.BowlX=null;
            if (s.BowlY.HasValue && !float.IsFinite(s.BowlY.Value)) s.BowlY=null;
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        string tmp=FilePath+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(tmp,FilePath,true);
    }
    public BehaviorOptions Behavior() => new()
    {
        IdleSeconds=IdleSeconds,ClickThreshold=ClickThreshold,GoodCat=GoodCat,Neutered=Neutered,
        MouseStealEnabled=MouseStealEnabled,RandomSleep=RandomSleep,SleepMinSeconds=SleepMinSeconds,
        SleepMaxSeconds=SleepMaxSeconds,FeedingEnabled=FeedingEnabled,HungerSeconds=HungerSeconds
    };
}
