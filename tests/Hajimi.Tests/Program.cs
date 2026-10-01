using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Hajimi.Animation;
using Hajimi.Assets;
using Hajimi.Audio;
using Hajimi.Behavior;
using Hajimi.Platform;
using Hajimi.Interaction;

int passed=0;
var failures=new List<string>();
void Test(string name,Action run) { try { run(); Console.WriteLine("PASS "+name); passed++; } catch(Exception e) { failures.Add(name+": "+e.Message); Console.WriteLine("FAIL "+name+": "+e.Message); } }
void Check(bool result,string message) { if(!result) throw new Exception(message); }
byte[] Pixels(Bitmap bitmap)
{
    var region=new Rectangle(0,0,bitmap.Width,bitmap.Height);
    var bits=bitmap.LockBits(region,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
    try
    {
        var result=new byte[bitmap.Width*bitmap.Height*4];
        for(int y=0;y<bitmap.Height;y++) Marshal.Copy(bits.Scan0+y*bits.Stride,result,y*bitmap.Width*4,bitmap.Width*4);
        return result;
    }
    finally { bitmap.UnlockBits(bits); }
}
void EqualFrames(Bitmap actual,Bitmap expected,string message)
{
    Check(actual.Size==expected.Size && Pixels(actual).AsSpan().SequenceEqual(Pixels(expected)),message);
}
void ConnectedAnatomy(Bitmap bitmap,string message)
{
    var pixels=Pixels(bitmap); int w=bitmap.Width,h=bitmap.Height;
    var visible=new bool[w*h];
    for(int i=0;i<visible.Length;i++) visible[i]=pixels[i*4+3]>80;
    var sizes=new List<int>(); var stack=new Stack<int>();
    for(int start=0;start<visible.Length;start++)
    {
        if(!visible[start]) continue;
        int size=0; stack.Push(start); visible[start]=false;
        while(stack.Count>0)
        {
            int index=stack.Pop(),x=index%w,y=index/w; size++;
            for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
            {
                int nx=x+dx,ny=y+dy;
                if(nx<0 || ny<0 || nx>=w || ny>=h) continue;
                int next=ny*w+nx;
                if(visible[next]) { visible[next]=false; stack.Push(next); }
            }
        }
        sizes.Add(size);
    }
    sizes.Sort((a,b)=>b.CompareTo(a));
    Check(sizes.Count>0,message+": frame is empty");
    // Separate hairs and whiskers can be tiny islands; a detached paw or leg is much larger.
    Check(sizes.Skip(1).All(size=>size<60),message+$": detached component sizes {string.Join(", ",sizes.Skip(1).Take(6))}");
}
void Advance(PetBrain brain,double seconds,PetInput input) { for(double t=0;t<seconds;t+=.05) brain.Tick(.05,input); }
void Until(PetBrain brain,PetState state,PetInput input,double timeout=20)
{
    for(double t=0;t<timeout && brain.State!=state;t+=.05) brain.Tick(.05,input);
    Check(brain.State==state,$"Expected {state}, got {brain.State}");
}
var far=new PetInput(600,600,0,false,0);
var near=new PetInput(110,110,0,false,0,false,180);
var close=new PetInput(20,20,0,false,0,false,180);
Test("Complete MVP cycle, then returns to sleep",()=>
{
    var b=new PetBrain(new() { IdleSeconds=16 },42); var seen=new HashSet<PetState> { b.State }; b.StateChanged+=s=>seen.Add(s);
    Advance(b,7,new(600,600,0,false,70)); Check(b.State==PetState.Sleep,"Idle machine must sleep");
    Until(b,PetState.Wake,new(600,600,0,true,0)); Until(b,PetState.Wander,far);
    Until(b,PetState.Observe,near); Until(b,PetState.Approach,near); Until(b,PetState.Chew,close);
    b.Click(180); Check(b.State==PetState.Hiss,"Click must immediately hiss");
    Until(b,PetState.Recover,far); Until(b,PetState.Contempt,far);
    Until(b,PetState.Sleep,new(600,600,0,false,90),15);
    foreach(var state in new[] { PetState.Sleep,PetState.Wake,PetState.Wander,PetState.Observe,PetState.Approach,PetState.Chew,PetState.Hiss,PetState.Recover })
        Check(seen.Contains(state),$"Missing {state}");
    Check(!seen.Overlaps(new[] { PetState.LieDown,PetState.Rag,PetState.Curl }),"Normal sleep must go directly to the sleeping pose");
});
Test("Idle and manual sleep enter the sleeping pose directly",()=>
{
    var b=new PetBrain(new() { IdleSeconds=8,FeedingEnabled=false }); b.Wake(); Until(b,PetState.Wander,far);
    b.Tick(.1,far with { SystemIdleSeconds=90 }); Check(b.State==PetState.Sleep,"Idle sleep must be immediate");
    b.SetIdlePose(true); b.Rest(); Check(b.State==PetState.Sleep,"Menu sleep must be immediate");
    b.Flatten(); Check(b.State==PetState.Rag,"Deliberate rag pose should remain available");
    var seen=new HashSet<PetState>(); b.StateChanged+=s=>seen.Add(s); Until(b,PetState.Sleep,far,6);
    Check(!seen.Contains(PetState.Curl),"Rag finishes with the sleeping pose directly");
});
Test("No automatic waking from stale mouse activity",()=>
{
    var b=new PetBrain(); Advance(b,60,new(10,10,0,false,0)); Check(b.State==PetState.Sleep,"No new mouse activity");
});
Test("Click interrupts rest, repeated seated hisses escalate with configurable threshold",()=>
{
    var b=new PetBrain(new() { ClickThreshold=3 }); b.SetIdlePose(true); b.Click(10); Check(b.State==PetState.SitHiss,"First seated hiss");
    b.Click(10); b.Click(10); Until(b,PetState.PostHissChew,far,3);
    Check(PetAnimator.SelectPose(b.State,1)=="post-chew","Must use a normal neck");
    Advance(b,8,far); b.Flatten(); b.Click(10); Check(b.State==PetState.Hiss,"Rag interruption");
    b.Options.ClickThreshold=2; b.Click(10); Until(b,PetState.PostHissChew,far,3);
});
Test("Static cursor cannot keep cat growling forever",()=>
{
    var b=new PetBrain(); b.Wake(); Until(b,PetState.Wander,far); Until(b,PetState.Observe,near);
    Until(b,PetState.Approach,near); Until(b,PetState.Chew,close); Until(b,PetState.Recover,close,12);
    Until(b,PetState.Sleep,new(20,20,0,false,80),20);
});
Test("Mouse fleeing causes pursuit, far target aborts",()=>
{
    var b=new PetBrain(); b.Wake(); Until(b,PetState.Wander,far); Until(b,PetState.Observe,near);
    Until(b,PetState.Approach,near); Until(b,PetState.Chew,close);
    b.Tick(.1,near); Check(b.State==PetState.Approach,"Should chase moved pointer");
    b.Tick(.1,far); Check(b.State==PetState.Contempt,"Should abandon far target");
});
Test("Mouse between head and torso does not flip the cat repeatedly",()=>
{
    var b=new PetBrain(); b.Wake(); Until(b,PetState.Wander,far); Until(b,PetState.Observe,near);
    for(int i=0;i<10;i++) b.Tick(.05,new(40,-40,0,true,0,false,100));
    Check(b.Facing==1,"Body-space facing must win over head-space dx");
});
Test("Self-approach never creates false sudden-mouse aggression",()=>
{
    var b=new PetBrain(); b.Wake(); Until(b,PetState.Wander,far); Until(b,PetState.Observe,near);
    b.Tick(.1,new(10,10,0,false,0,false,180)); Check(b.State!=PetState.Hiss,"Pet motion should not hiss");
    b.Tick(.1,new(10,10,0,true,0,true,180)); Check(b.State==PetState.Scared,"Sudden mouse approach should scare");
});
Test("Bounds handle negative monitor origins and transparent margin",()=>
{
    var area=new Rectangle(-1920,-200,1920,1080); var visible=new RectangleF(100,160,230,130);
    var result=ScreenBounds.Clamp(new(-2200,-400),visible,area);
    Check(result.X+visible.Left>=area.Left && result.Y+visible.Top>=area.Top,"Top left must stay visible");
    result=ScreenBounds.Clamp(new(300,1000),visible,area);
    Check(result.X+visible.Right<=area.Right && result.Y+visible.Bottom<=area.Bottom,"Bottom right must stay visible");
});
string assetRoot=Path.Combine(AppContext.BaseDirectory,"assets");
Test("Drag threshold, negative coordinates and lost-capture reset",()=>
{
    var drag=new DragGesture(); drag.Begin(new(-1300,80),new(-1350,-200));
    Check(drag.Move(new(-1298,82))==null,"Tiny click movement is not dragging");
    var result=drag.Move(new(-1250,180));
    Check(result==new PointF(-1300,-100),"Drag offset must preserve pointer anchoring");
    Check(drag.End(),"Expected drag end"); Check(!drag.IsDown && !drag.IsDragging,"Capture state must clear");
    Check(drag.Move(new(0,0))==null,"Movement after release must do nothing");
});
Test("All keyframes load, render alpha and flip correctly",()=>
{
    using var catalog=new AssetCatalog(assetRoot); using var animator=new PetAnimator(catalog);
    foreach(var state in Enum.GetValues<PetState>())
    {
        using var frame=animator.Render(state,.6,1,new(20,0),1);
        Check(frame.GetPixel(0,0).A==0,"Transparent canvas required");
        Check(animator.VisibleBounds.Bottom<=340 && animator.VisibleBounds.Top>=0,$"Clipped {state}");
    }
    using var a=animator.Render(PetState.Chew,2,1,new(0,0),1);
    using var b=animator.Render(PetState.Chew,2,-1,new(0,0),1);
    int mismatches=0,compared=0;
    for(int y=20;y<330;y+=5) for(int x=5;x<495;x+=5) { compared++; if(Math.Abs(a.GetPixel(x,y).A-b.GetPixel(499-x,y).A)>8) mismatches++; }
    Check(mismatches<compared*.1,"Mirror changed too many pixels");
});
Test("Jaw loop actually changes visible pixels",()=>
{
    using var catalog=new AssetCatalog(assetRoot); using var animator=new PetAnimator(catalog);
    using var a=animator.Render(PetState.Chew,3,1,new(),1); using var b=animator.Render(PetState.Chew,3.25,1,new(),1);
    int changed=0; for(int y=180;y<280;y++) for(int x=400;x<490;x++) if(a.GetPixel(x,y)!=b.GetPixel(x,y)) changed++;
    Check(changed>50,$"Only {changed} pixels changed");
});
Test("Audio clips decode and contain non-silent bounded PCM",()=>
{
    foreach(var name in new[] { "growl.wav","hiss-1.wav","hiss-2.wav","hiss-3.wav","scared.wav","snore.wav","meow.wav","satisfied.wav" })
    {
        var clip=WaveClip.Load(Path.Combine(assetRoot,"audio",name));
        Check(clip.Samples.Length>10000,"Clip too short"); Check(clip.Samples.Max(Math.Abs)>.1,"Silent audio");
        Check(clip.Samples.All(s=>float.IsFinite(s) && Math.Abs(s)<=1),"Invalid PCM");
    }
});
Test("Sleep clicks select only the three requested waking states",()=>
{
    var outcomes=new HashSet<PetState>();
    for(int i=1;i<=40;i++)
    {
        var b=new PetBrain(seed:i); b.Click(180); outcomes.Add(b.State);
        Check(b.State is PetState.Scared or PetState.Contempt or PetState.SitContempt,"Illegal sleep click");
    }
    Check(outcomes.Count==3,"Random sleep reactions should include all three");
});
Test("Seated and standing click transitions follow the provided poses",()=>
{
    for(int i=1;i<=12;i++)
    {
        var b=new PetBrain(seed:i); b.SetIdlePose(true); b.Click(180); Check(b.State==PetState.SitHiss,"Seated cat cannot turn into standing hiss");
        b.SetIdlePose(false); b.Click(180); Check(b.State is PetState.Hiss or PetState.Scared,"Standing click");
        if(b.State==PetState.Scared) { b.Click(180); Check(b.State==PetState.Hiss,"Scared click must hiss"); }
    }
});
Test("A single hiss recovers with no chewing pose",()=>
{
    var b=new PetBrain(); b.SetIdlePose(true); b.Click(180); Until(b,PetState.Recover,far,3);
    Check(PetAnimator.SelectPose(b.State,0)=="contempt","Recovery must not display chew");
});
Test("A scare followed by one hiss does not count as repeated hissing",()=>
{
    for(int seed=1;seed<50;seed++)
    {
        var b=new PetBrain(new() { ClickThreshold=2 },seed); b.SetIdlePose(false); b.Click(180);
        if(b.State!=PetState.Scared) continue;
        b.Click(180); Until(b,PetState.Recover,far,3);
        return;
    }
    throw new Exception("Could not create a scared test case");
});
Test("Mouse theft requires its switch and obeys deadline",()=>
{
    var b=new PetBrain(new() { MouseStealEnabled=true }); b.SetIdlePose(true);
    b.Click(180); b.Click(180); b.Click(180); Until(b,PetState.CursorRaid,far,5);
    Until(b,PetState.Recover,far,10);
    b.Options.MouseStealEnabled=false; b.SetIdlePose(true); b.Click(180); b.Click(180); b.Click(180);
    Until(b,PetState.PostHissChew,far,3); Check(b.State!=PetState.CursorRaid,"Disabled theft");
});
Test("Neutering during theft gives rag and blocks every aggressive click",()=>
{
    var b=new PetBrain(new() { MouseStealEnabled=true }); b.SetIdlePose(true);
    b.Click(180); b.Click(180); b.Click(180); Until(b,PetState.CursorRaid,far,5); b.Neuter();
    Check(b.State==PetState.Rag && b.Options.Neutered,"Neuter must collapse");
    for(int i=0;i<80;i++)
    {
        b.Click(180); b.Tick(.1,new(10,10,0,true,0,true,180));
        Check(!PetBrain.IsAggressive(b.State) && b.State is not (PetState.Contempt or PetState.SitContempt),"Neutered aggression");
    }
    b.RestoreBell(); Check(b.State==PetState.SitContempt && !b.Options.Neutered,"Bell restores old temperament");
});
Test("Good cat ignores clicks, pointer movement and sudden approaches",()=>
{
    var b=new PetBrain(new() { GoodCat=true,MouseStealEnabled=true }); b.Wake();
    for(int i=0;i<800;i++)
    {
        if(i%4==0) b.Click(180);
        b.Tick(.05,new(10,10,0,true,0,true,180));
        Check(!PetBrain.IsAggressive(b.State) && b.State is not (PetState.Observe or PetState.Approach or PetState.Chew or PetState.Contempt or PetState.SitContempt),"Good cat interfered");
    }
});
Test("Random sleep stays inside custom limits and fixed sleep is exact",()=>
{
    var b=new PetBrain(new() { RandomSleep=true,SleepMinSeconds=11,SleepMaxSeconds=19 },19); var seen=new HashSet<double>();
    for(int i=0;i<30;i++) { b.ChooseSleepDelay(); Check(b.CurrentIdleSeconds>=11 && b.CurrentIdleSeconds<=19,"Random delay out of range"); seen.Add(b.CurrentIdleSeconds); }
    Check(seen.Count>20,"Random delay should vary"); b.Options.RandomSleep=false; b.Options.IdleSeconds=87; b.ChooseSleepDelay(); Check(b.CurrentIdleSeconds==87,"Custom fixed delay");
});
Test("Empty bin waits indefinitely; adding food triggers eat and satisfied",()=>
{
    var b=new PetBrain(new() { IdleSeconds=8 }); b.FeedNow();
    var empty=new PetInput(600,600,0,false,90,false,600,false,20,20,0,true);
    Until(b,PetState.HungryWait,empty,2); Advance(b,60,empty); Check(b.State==PetState.HungryWait,"Must wait even when idle");
    var food=empty with { FoodAvailable=true }; Until(b,PetState.Eat,food,2); Until(b,PetState.Sated,food,7);
    Until(b,PetState.CalmSit,food,7);
});
Test("Default hunger automatically wakes a sleeping cat after three minutes",()=>
{
    var b=new PetBrain(); var empty=far with { FoodAvailable=false,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Advance(b,179,empty); Check(b.State==PetState.Sleep && b.HungerSecondsRemaining>0,"Should still be full before the deadline");
    Until(b,PetState.HungryWalk,empty,2); Check(b.VelocityX>0,"Hunger should navigate without a menu action");
    var beside=empty with { FoodDistance=20,FoodDeltaX=20 }; Until(b,PetState.HungryWait,beside,1);
    Until(b,PetState.Eat,beside with { FoodAvailable=true },1);
});
Test("Finishing a meal resets fullness and triggers the next meal automatically",()=>
{
    var b=new PetBrain(new() { HungerSeconds=30 }); var food=far with { FoodAvailable=true,FoodDistance=20,FoodDeltaX=20,FoodReachable=true };
    Until(b,PetState.Eat,food,32); Until(b,PetState.Sated,food,7);
    Check(b.HungerSecondsRemaining>29,"Fullness should restart after eating completes");
    Advance(b,28,food); Check(b.State is not (PetState.HungryWalk or PetState.HungryWait or PetState.Eat),"Must not immediately eat again");
    Until(b,PetState.Eat,food,4);
});
Test("Hunger interval changes apply to elapsed time instead of restarting the clock",()=>
{
    var b=new PetBrain(new() { HungerSeconds=180 }); var empty=far with { FoodAvailable=false,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Advance(b,45,empty); b.Options.HungerSeconds=30; b.ApplyOptions(); Until(b,PetState.HungryWalk,empty,1);
    var longer=new PetBrain(new() { HungerSeconds=30 }); Advance(longer,20,empty);
    longer.Options.HungerSeconds=60; longer.ApplyOptions(); Advance(longer,39,empty);
    Check(longer.State==PetState.Sleep && longer.HungerSecondsRemaining>0,"Extended interval must defer hunger");
    Until(longer,PetState.HungryWalk,empty,2);
});
Test("Sleeping does not reset the hunger clock",()=>
{
    var b=new PetBrain(new() { HungerSeconds=30 }); var empty=far with { FoodAvailable=false,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Advance(b,24,empty); b.Rest(); Check(b.HungerSecondsRemaining<6.1,"Resting must not count as a meal");
    Until(b,PetState.HungryWalk,empty,7);
});
Test("An overdue meal cannot interrupt the five-second rag pose after neutering",()=>
{
    var b=new PetBrain(new() { HungerSeconds=30 }); Advance(b,40,far);
    b.Neuter(); var food=far with { SystemIdleSeconds=90,FoodAvailable=true,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Advance(b,4.8,food); Check(b.State==PetState.Rag && b.HungerSecondsRemaining==0,"Hunger must wait while the neutered cat is collapsed");
    double age=b.Age;
    for(int i=0;i<10;i++) b.ApplyOptions();
    Check(b.State==PetState.Rag && b.Age==age,"Applying paused settings must preserve rag and its age");
    Until(b,PetState.Sleep,food,.3); Until(b,PetState.HungryWalk,food,.2);
    Check(b.Options.Neutered,"Feeding after resting must keep the neutered behavior");
});
Test("Unavailable bin preserves hunger and retries immediately after relocation",()=>
{
    var b=new PetBrain(new() { HungerSeconds=30 }); var unknown=far with { FoodReachable=false };
    Advance(b,50,unknown); Check(b.State==PetState.Sleep && b.HungerSecondsRemaining==0,"Missing icon must not reset hunger");
    var found=far with { FoodAvailable=false,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Until(b,PetState.HungryWalk,found,1); b.Tick(.1,unknown); Check(!b.IsFeeding,"Unavailable bin aborts navigation");
    Until(b,PetState.HungryWalk,found,1);
});
Test("Automatic feeding obeys its switch and preserves elapsed hunger while disabled",()=>
{
    var b=new PetBrain(new() { HungerSeconds=30,FeedingEnabled=false });
    var empty=far with { FoodAvailable=false,FoodDistance=200,FoodDeltaX=200,FoodReachable=true };
    Advance(b,40,empty); Check(!b.IsFeeding,"Disabled automatic feeding must stay inactive");
    b.Options.FeedingEnabled=true; b.ApplyOptions(); Until(b,PetState.HungryWalk,empty,1);
    b.Options.FeedingEnabled=false; b.ApplyOptions(); b.Tick(.1,empty); Check(!b.IsFeeding,"Disabling feeding must stop navigation");
});
Test("Manual feeding works with automatic feeding disabled and finishes its satisfied sound",()=>
{
    var b=new PetBrain(new() { FeedingEnabled=false,HungerSeconds=30 }); b.FeedNow();
    var empty=far with { FoodAvailable=false,FoodDistance=20,FoodDeltaX=20,FoodReachable=true };
    Until(b,PetState.HungryWait,empty,1); Advance(b,35,empty); Check(b.State==PetState.HungryWait,"Manual visit must wait for food even with automatic feeding disabled");
    var food=empty with { FoodAvailable=true }; Until(b,PetState.Eat,food,1); Until(b,PetState.Sated,food,7);
    Advance(b,5,food); Check(b.State==PetState.Sated,"Manual meal should keep its full satisfied period");
    Until(b,PetState.CalmSit,food,2); Advance(b,40,food); Check(!b.IsFeeding,"Manual meal must not re-enable later automatic visits");
});
Test("Disabling automatic feeding stops an automatic wait but preserves a manual wait",()=>
{
    var empty=far with { FoodAvailable=false,FoodDistance=20,FoodDeltaX=20,FoodReachable=true };
    var automatic=new PetBrain(new() { HungerSeconds=30 }); Until(automatic,PetState.HungryWait,empty,32);
    automatic.Options.FeedingEnabled=false; automatic.ApplyOptions(); automatic.Tick(.1,empty);
    Check(!automatic.IsFeeding,"Turning off automatic feeding must cancel its empty-bin wait");
    var manual=new PetBrain(); manual.FeedNow(); Until(manual,PetState.HungryWait,empty,1);
    manual.Options.FeedingEnabled=false; manual.ApplyOptions(); Advance(manual,3,empty);
    Check(manual.State==PetState.HungryWait,"Turning off automatic feeding must preserve a manual visit");
    Until(manual,PetState.Eat,empty with { FoodAvailable=true },1);
});
Test("Cancelling a manual meal clears its exemption from the automatic feeding switch",()=>
{
    var empty=far with { FoodAvailable=false,FoodDistance=20,FoodDeltaX=20,FoodReachable=true };
    foreach(Action<PetBrain> cancel in new Action<PetBrain>[] { b=>b.Rest(),b=>b.Flatten(),b=>b.Neuter() })
    {
        var b=new PetBrain(new() { HungerSeconds=30 }); b.FeedNow(); Until(b,PetState.HungryWait,empty,1); cancel(b);
        Check(b.HungerSecondsRemaining>29,"Cancelling the manual request must restore the original fullness clock");
        Until(b,PetState.HungryWait,empty,32);
        b.Options.FeedingEnabled=false; b.ApplyOptions(); b.Tick(.1,empty);
        Check(!b.IsFeeding,"A cancelled manual visit must not make the next automatic wait ignore the switch");
    }
});
Test("Food removed while eating returns to waiting; missing icon aborts",()=>
{
    var b=new PetBrain(); b.FeedNow(); var food=new PetInput(600,600,0,false,0,false,600,true,20,20,0,true);
    Until(b,PetState.Eat,food,2); b.Tick(.1,food with { FoodAvailable=false }); Check(b.State==PetState.HungryWait,"Removed food");
    b.Tick(.1,food with { FoodReachable=false }); Check(!b.IsFeeding,"Unavailable bin must abort movement");
});
Test("Sitting or turning beside the bin does not restart navigation",()=>
{
    var b=new PetBrain(); b.FeedNow(); var window=new PointF(700,300); var bin=new PointF(1128,545);
    using var catalog=new AssetCatalog(assetRoot); using var animator=new PetAnimator(catalog);
    foreach(var pose in new[] { PetState.HungryWalk,PetState.HungryWait,PetState.Wander,PetState.HungryWait })
    {
        using var frame=animator.Render(pose,.5,-1,new(),1);
        var m=FeedingNavigation.Measure(window,bin,1,-1);
        var input=new PetInput(600,600,0,false,0,false,600,false,m.Distance,m.X,m.Y,true,m.BodyX);
        b.Tick(.1,input); Check(b.State==PetState.HungryWait,"Pose switch restarted walking to the same bin");
    }
});
Test("Audio routing and three non-repeating random hisses",()=>
{
    var p=new AudioPolicy(19); var variants=new HashSet<string>(); string? last=null;
    for(int i=0;i<40;i++) { string key=p.Select(PetState.Hiss,false)!.Key; Check(key!=last,"Consecutive hiss variant repeated"); variants.Add(key); last=key; }
    Check(variants.SetEquals(new[] { "hiss-1","hiss-2","hiss-3" }),"Hiss variants");
    Check(p.Select(PetState.SitHiss,false)!.Key=="hiss-1","Seated hiss audio"); Check(p.Select(PetState.Scared,false)!.Key=="scared","Old Wu audio");
    Check(p.Select(PetState.Sleep,false)==null && p.Select(PetState.Sleep,true) is { Key:"snore",Loop:true },"Snore switch");
    Check(p.Select(PetState.HungryWait,false) is { Key:"meow",RepeatSeconds:6 },"Waiting meow");
    Check(p.Select(PetState.Sated,false)!.Key=="satisfied" && p.Select(PetState.Recover,false)==null,"Satisfied and quiet recovery");
});
Test("Complete walk frames follow travel distance and repeat after one cycle",()=>
{
    var frames=new HashSet<int>();
    for(int i=0;i<WalkCycle.FrameCount;i++)
    {
        double distance=(i+.5)*WalkCycle.StrideLength/WalkCycle.FrameCount;
        int frame=WalkCycle.FrameIndex(distance); frames.Add(frame);
        Check(frame==i,"Walk frames are skipped or out of order");
        Check(frame==WalkCycle.FrameIndex(distance+WalkCycle.StrideLength),"Loop changes at the cycle boundary");
        Check(frame==WalkCycle.FrameIndex(distance-WalkCycle.StrideLength),"Negative monitor travel changes the cycle");
    }
    Check(frames.Count==8,"All eight complete poses must be available");
    Check(WalkCycle.FrameIndex(0)==0 && WalkCycle.FrameIndex(WalkCycle.StrideLength)==0,"Loop starts with a different pose");
});
Test("Fixed-canvas loading preserves unequal transparent margins",()=>
{
    string temp=Path.Combine(Path.GetTempPath(),"Hajimi-walk-canvas-"+Guid.NewGuid()); Directory.CreateDirectory(temp);
    try
    {
        for(int i=0;i<2;i++)
        {
            using var image=new Bitmap(60,40,PixelFormat.Format32bppArgb); using var g=Graphics.FromImage(image);
            g.FillRectangle(Brushes.Black,i==0?new Rectangle(4,7,13,10):new Rectangle(20,3,36,32));
            image.Save(Path.Combine(temp,$"frame-{i}.png"),ImageFormat.Png);
        }
        var definition=new CharacterDefinition { Poses=new()
        {
            ["frame-0"]=new() { File="frame-0.png",Width=60,Trim=false },
            ["frame-1"]=new() { File="frame-1.png",Width=60,Trim=false }
        } };
        File.WriteAllText(Path.Combine(temp,"character.json"),JsonSerializer.Serialize(definition));
        using var c=new AssetCatalog(temp);
        Check(c["frame-0"].Image.Size==new Size(102,68) && c["frame-1"].Image.Size==new Size(102,68),"Frames were resized by their individual alpha bounds");
        Check(c["frame-0"].Image.GetPixel(5,5).A==0 && c["frame-1"].Image.GetPixel(5,5).A==0,"Transparent margins were trimmed");
    }
    finally
    {
        File.Delete(Path.Combine(temp,"frame-0.png")); File.Delete(Path.Combine(temp,"frame-1.png"));
        File.Delete(Path.Combine(temp,"character.json")); Directory.Delete(temp);
    }
});
Test("Walking pose stops animating when travelled distance stops",()=>
{
    using var c=new AssetCatalog(assetRoot); using var animator=new PetAnimator(c);
    foreach(var state in new[] { PetState.Wander,PetState.Approach,PetState.HungryWalk,PetState.CursorRaid })
    {
        using var a=animator.Render(state,.1,1,new(),1,walkDistance:17);
        using var b=animator.Render(state,8,1,new(90,70),1,walkDistance:17);
        EqualFrames(a,b,"Stopped cat keeps pedalling or its legs follow the pointer");
    }
});
Test("Every walking state displays the complete frame unchanged at both directions and all supported scales",()=>
{
    using var c=new AssetCatalog(assetRoot); using var animator=new PetAnimator(c);
    foreach(var state in new[] { PetState.Wander,PetState.Approach,PetState.HungryWalk,PetState.CursorRaid })
    foreach(float scale in new[] { .4f,1f,1.6f })
    foreach(int facing in new[] { -1,1 })
    for(int index=0;index<WalkCycle.FrameCount;index++)
    {
        string key=$"walk-{index}"; var pose=c[key];
        double distance=(index+.5)*WalkCycle.StrideLength/WalkCycle.FrameCount;
        using var actual=animator.Render(state,7,facing,new(100,-40),scale,walkDistance:distance);
        Check(animator.CurrentPose==key,$"{state} uses the wrong walk frame");
        using var expected=new Bitmap(actual.Width,actual.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(expected))
        {
            g.CompositingMode=CompositingMode.SourceOver; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.ScaleTransform(scale,scale);
            if(facing*(pose.FacesLeft?-1:1)<0) { g.TranslateTransform(PetAnimator.Width,0); g.ScaleTransform(-1,1); }
            float height=pose.Width*pose.Image.Height/pose.Image.Width;
            g.DrawImage(pose.Image,new RectangleF((PetAnimator.Width-pose.Width)/2,318-height,pose.Width,height));
        }
        EqualFrames(actual,expected,$"{state} frame {index} was cut or deformed at scale {scale} facing {facing}");
    }
});
Test("Walk canvases remain fixed and have no detached leg or paw fragments",()=>
{
    using var c=new AssetCatalog(assetRoot); using var animator=new PetAnimator(c);
    Size size=c["walk-0"].Image.Size; float width=c["walk-0"].Width;
    for(int index=0;index<WalkCycle.FrameCount;index++)
    {
        string key=$"walk-{index}"; var pose=c[key];
        Check(!c.Definition.Poses[key].Trim,"Walking frame must preserve its full canvas");
        Check(pose.Image.Size==size && pose.Width==width,"Walking frames have different canvas sizes or scales");
        ConnectedAnatomy(pose.Image,$"Source {key}");
        using var frame=animator.Render(PetState.Wander,0,1,new(),1,walkDistance:(index+.5)*WalkCycle.StrideLength/WalkCycle.FrameCount);
        ConnectedAnatomy(frame,$"Rendered {key}");
    }
});
Test("Walking mouth anchor stays at the new sprite mouth and mirrors with the complete canvas",()=>
{
    using var c=new AssetCatalog(assetRoot); using var animator=new PetAnimator(c);
    for(int index=0;index<WalkCycle.FrameCount;index++)
    foreach(float scale in new[] { .4f,.85f,1.6f })
    {
        double distance=(index+.5)*WalkCycle.StrideLength/WalkCycle.FrameCount;
        using var right=animator.Render(PetState.CursorRaid,0,1,new(),scale,walkDistance:distance);
        PointF rightMouth=animator.Mouth;
        Check(Math.Abs(rightMouth.X/scale-407)<12 && Math.Abs(rightMouth.Y/scale-143)<12,"Mouth anchor still belongs to the old cropped sprite");
        int radius=Math.Max(3,(int)Math.Ceiling(8*scale)); bool nearCat=false;
        for(int y=Math.Max(0,(int)rightMouth.Y-radius);y<Math.Min(right.Height,(int)rightMouth.Y+radius);y++)
        for(int x=Math.Max(0,(int)rightMouth.X-radius);x<Math.Min(right.Width,(int)rightMouth.X+radius);x++)
            if(right.GetPixel(x,y).A>80) nearCat=true;
        Check(nearCat,"Cursor is anchored outside the head pixels");
        using var left=animator.Render(PetState.CursorRaid,0,-1,new(),scale,walkDistance:distance);
        Check(Math.Abs(rightMouth.X+animator.Mouth.X-PetAnimator.Width*scale)<.001f && Math.Abs(rightMouth.Y-animator.Mouth.Y)<.001f,"Mirroring moves the pointer attachment away from the mouth");
    }
});
Test("Chew mouth turns toward pointer; post-hiss neck does not extend",()=>
{
    using var c=new AssetCatalog(assetRoot); using var a=new PetAnimator(c);
    using var up=a.Render(PetState.Chew,2,1,new(30,-100),1); float above=a.HeadAngle;
    using var down=a.Render(PetState.Chew,2,1,new(30,100),1); Check(above<0 && a.HeadAngle>0,"Head must pitch toward mouse");
    using var normal=a.Render(PetState.PostHissChew,2,1,new(30,100),1); Check(a.CurrentPose=="post-chew" && a.HeadAngle==0,"Normal neck chewing");
});
Test("Cursor capture releases for space, escape, disable and disposal without touching real pointer",()=>
{
    using var c=new CursorCapture(true); Check(c.Start(),"Start dry capture"); c.Update(new(-300,200)); Check(c.AttachedPoint==new Point(-300,200),"Mouth attachment");
    c.Request(CursorCommand.Neuter); Check(c.ConsumeCommand()==CursorCommand.Neuter && c.ConsumeCommand()==CursorCommand.None,"Consume space once");
    c.Release(); Check(!c.Active,"Release"); c.Start(); c.Request(CursorCommand.Escape); Check(c.ConsumeCommand()==CursorCommand.Escape,"Esc"); c.Dispose(); Check(!c.Active,"Dispose");
});
Test("Full-screen raid route varies in both axes and respects visible margins",()=>
{
    var route=new RaidRoute(32); var visible=new RectangleF(40,110,420,200); var work=new Rectangle(-1920,-100,1920,1080); var pos=new PointF(-1200,400); var visited=new List<PointF>();
    for(int i=0;i<160;i++) { pos=route.Move(pos,work,visible,.05,out _); pos=ScreenBounds.Clamp(pos,visible,work); visited.Add(pos); }
    Check(visited.Max(p=>p.X)-visited.Min(p=>p.X)>300 && visited.Max(p=>p.Y)-visited.Min(p=>p.Y)>150,"Must run in both axes");
});
Test("Packaged images and audio load with no external assets directory",()=>
{
    string absent=Path.Combine(AppContext.BaseDirectory,"absent-"+Guid.NewGuid().ToString("N"));
    Check(!Directory.Exists(absent),"Test must not have sidecar assets");
    using var embedded=new AssetCatalog(absent);
    using var disk=new AssetCatalog(assetRoot);
    Check(embedded.Keys.Count()==disk.Keys.Count(),"Missing packaged poses");
    foreach(string key in embedded.Keys) EqualFrames(embedded[key].Image,disk[key].Image,"Embedded pose differs: "+key);
    var files=new AssetSource(absent);
    foreach(string key in new[] { "growl","hiss-1","hiss-2","hiss-3","scared","meow","snore","satisfied" })
    {
        using var stream=files.OpenRead("audio/"+key+".wav");
        Check(WaveClip.Load(stream).Samples.Length>0,"Packaged audio missing: "+key);
    }
    Check(!files.Exists("generated/walk-v031-candidate.png") && !files.Exists("generated/walk-v031-clean.png"),"Rejected walk sheets were packaged");
});
Test("Packaged icon is the supplied ICO",()=>
{
    using var stream=typeof(PetIcon).Assembly.GetManifestResourceStream("Hajimi.ApplicationIcon")!;
    using var content=new MemoryStream(); stream.CopyTo(content);
    string original=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../图标.ico"));
    Check(content.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(original)),"Embedded ICO differs from supplied file");
    using var icon=PetIcon.Load(); Check(icon.Width>0 && icon.Height>0,"ICO cannot be loaded");
});
Test("Startup is opt-in, quoted, refreshed only when enabled and reversible",()=>
{
    var store=new MemoryStartupStore();
    string executable=Path.Combine(AppContext.BaseDirectory,"Hajimi.Tests.exe");
    var startup=new StartupRegistration(store,executable);
    startup.RefreshEnabledPath(); Check(!startup.Enabled && store.Writes==0,"Starting disabled must not enable startup");
    Check(StartupRegistration.CommandFor(@"D:\Desktop pets\哈基米.exe")=="\"D:\\Desktop pets\\哈基米.exe\"","EXE path with spaces must be quoted");
    startup.SetEnabled(true); Check(startup.Enabled && store.Value==StartupRegistration.CommandFor(executable),"Enable must use real executable path");
    store.Value="\"D:\\old\\哈基米.exe\"";
    startup.RefreshEnabledPath(); Check(store.Value==StartupRegistration.CommandFor(executable),"Moving enabled pet must refresh its path");
    int writes=store.Writes; startup.RefreshEnabledPath(); Check(store.Writes==writes,"Unchanged path should not be rewritten");
    startup.SetEnabled(false); Check(!startup.Enabled && store.Removes==1,"Disable must remove only the pet entry");
    startup.RefreshEnabledPath(); Check(!startup.Enabled && store.Writes==writes,"Disabled entry must stay removed");
});
Test("Startup rejects command injection and the dotnet development host",()=>
{
    foreach(string path in new[] { "relative.exe", "D:\\pet\".exe", "D:\\pet\n.exe" })
    {
        bool rejected=false; try { StartupRegistration.CommandFor(path); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"Unsafe startup command accepted");
    }
    var store=new MemoryStartupStore();
    var startup=new StartupRegistration(store,@"C:\Program Files\dotnet\dotnet.exe");
    Check(!startup.Available,"Development host must not be registered");
    bool rejectedHost=false; try { startup.SetEnabled(true); } catch(InvalidOperationException) { rejectedHost=true; }
    Check(rejectedHost && store.Writes==0,"Development host registered");
});
Console.WriteLine($"\n{passed} passed, {failures.Count} failed");
Environment.ExitCode=failures.Count==0?0:1;

sealed class MemoryStartupStore : IStartupStore
{
    public string? Value { get; set; }
    public int Writes { get; private set; }
    public int Removes { get; private set; }
    public string? Read()=>Value;
    public void Write(string command) { Value=command; Writes++; }
    public void Remove() { Value=null; Removes++; }
}
