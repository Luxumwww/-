namespace Hajimi.Behavior;

// Pure behavior: all times, pointer observations and food observations are supplied by the host.
public sealed class PetBrain
{
    private readonly Random random;
    private readonly Queue<double> provocations=new();
    private readonly Queue<double> hisses=new();
    private double time,lastInterest,cooldownUntil,wanderDuration=2,sleepInputQuiet,lastMealTime;
    private bool movementSinceSleep,hungry,manualFeeding;
    public BehaviorOptions Options { get; }
    public PetState State { get; private set; }=PetState.Sleep;
    public double Age { get; private set; }
    public double CurrentIdleSeconds { get; private set; }
    public int Facing { get; private set; }=1;
    public double VelocityX { get; private set; }
    public double VelocityY { get; private set; }
    public int Irritation=>provocations.Count;
    public bool Peaceful=>Options.Neutered || Options.GoodCat;
    public bool IsResting=>State is PetState.Sleep or PetState.LieDown or PetState.Rag or PetState.Curl;
    public bool IsFeeding=>State is PetState.HungryWalk or PetState.HungryWait or PetState.Eat or PetState.Sated;
    public bool CanSteal=>Options.MouseStealEnabled && !Peaceful;
    public double HungerSecondsRemaining=>hungry?0:Math.Max(0,lastMealTime+HungerDelay-time);
    private double HungerDelay=>double.IsFinite(Options.HungerSeconds)?Math.Max(10,Options.HungerSeconds):180;
    public event Action<PetState>? StateChanged;

    public PetBrain(BehaviorOptions? options=null,int seed=0)
    {
        Options=options??new(); random=seed==0?new Random():new Random(seed);
        ChooseSleepDelay();
    }
    public void Tick(double dt,PetInput input)
    {
        dt=Math.Clamp(dt,0,.15); time+=dt; Age+=dt; VelocityX=VelocityY=0;
        PruneProvocations();
        double facingDelta=double.IsNaN(input.BodyDeltaX)?input.DeltaX:input.BodyDeltaX;
        if(Peaceful && IsAggressive(State)) { Enter(PetState.CalmSit); return; }
        if(State==PetState.CursorRaid)
        {
            if(!CanSteal || Age>8) { ReleaseRaid(); return; }
            VelocityX=Facing*350;
            return;
        }
        if(Options.FeedingEnabled && time-lastMealTime>=HungerDelay) hungry=true;
        if(Options.FeedingEnabled && !IsFeeding && !IsAggressive(State)
            && State is not (PetState.Recover or PetState.Rag or PetState.LieDown or PetState.Curl)
            && input.FoodReachable && hungry)
        { hungry=true; Enter(PetState.HungryWalk); }
        if(IsFeeding) { TickFeeding(dt,input); return; }
        if(State==PetState.Sleep)
        {
            if(Peaceful)
            {
                if(Age>14+CurrentIdleSeconds*.25 && input.SystemIdleSeconds<300) Wake();
            }
            else
            {
                if(input.MouseMoved) movementSinceSleep=true;
                sleepInputQuiet=input.MouseMoved?0:sleepInputQuiet+dt;
                if(Age>4 && movementSinceSleep && input.SystemIdleSeconds<1.5 && sleepInputQuiet<.8) Wake();
            }
            return;
        }
        if(State is PetState.LieDown or PetState.Rag or PetState.Curl)
        {
            if(State==PetState.Rag && Age>=5 || State==PetState.LieDown && Age>=1.2 || State==PetState.Curl && Age>=1.5)
                Enter(PetState.Sleep);
            return;
        }
        if(!Peaceful && input.MouseMoved && input.Distance<Options.ObserveRadius && time>=cooldownUntil) lastInterest=time;
        if(!IsAggressive(State) && State is not (PetState.Recover or PetState.Wake)
            && (input.SystemIdleSeconds>CurrentIdleSeconds || time-lastInterest>CurrentIdleSeconds))
        { Enter(PetState.Sleep); return; }
        if(!Peaceful && input.SuddenApproach && input.Distance<30 && time>=cooldownUntil
            && State is PetState.Contempt or PetState.SitContempt or PetState.Observe or PetState.Wander)
        { cooldownUntil=time+6; Face(facingDelta); Enter(PetState.Scared); return; }
        switch(State)
        {
            case PetState.Wake:
                if(Age>=1.8) StartWander();
                break;
            case PetState.Wander:
                VelocityX=Facing*Options.WalkSpeed;
                if(!Peaceful && Age>.8 && input.Distance<Options.ObserveRadius && time>=cooldownUntil)
                { Face(facingDelta); Enter(PetState.Observe); }
                else if(Age>=wanderDuration) Enter(Peaceful?PetState.CalmSit:random.Next(3)==0?PetState.SitContempt:PetState.Contempt);
                break;
            case PetState.CalmSit:
                if(Age>5) Enter(PetState.Groom);
                break;
            case PetState.Groom:
                if(Age>4) StartWander();
                break;
            case PetState.Contempt:
            case PetState.SitContempt:
                if(!Peaceful && input.Distance<Options.ObserveRadius && time>=cooldownUntil)
                { Face(facingDelta); Enter(PetState.Observe); }
                else if(Age>5) StartWander();
                break;
            case PetState.Observe:
                if(Peaceful) { Enter(PetState.CalmSit); break; }
                Face(facingDelta);
                if(input.Distance>Options.ObserveRadius*1.35) Enter(PetState.Contempt);
                else if(Age>Options.ObserveSeconds) Enter(PetState.Approach);
                break;
            case PetState.Approach:
                if(Peaceful) { Enter(PetState.CalmSit); break; }
                Face(facingDelta);
                if(input.Distance<=Options.NearRadius) Enter(PetState.Chew);
                else if(input.Distance>Options.ObserveRadius*1.8 || Age>9)
                { cooldownUntil=time+4; Enter(PetState.Contempt); }
                else MoveToward(dt,input.Distance,input.DeltaX,input.DeltaY,Options.NearRadius,Options.ApproachSpeed);
                break;
            case PetState.Chew:
                Face(facingDelta);
                if(Age>Options.ChewSeconds) { cooldownUntil=time+12; Enter(PetState.Recover); }
                else if(input.Distance>95) Enter(PetState.Approach);
                break;
            case PetState.Hiss:
            case PetState.SitHiss:
                if(Age>=Options.HissSeconds) EndAggression();
                break;
            case PetState.Scared:
                if(Age>=Options.ScaredSeconds) EndAggression();
                break;
            case PetState.PostHissChew:
                if(CanSteal && provocations.Count>=Options.ClickThreshold && Age>=1.3) Enter(PetState.CursorRaid);
                else if(Age>=3.5) { ClearProvocations(); Enter(PetState.Recover); }
                break;
            case PetState.Pounce:
                if(Age>=.45) EndAggression();
                break;
            case PetState.Recover:
                if(Age>=1.7) Enter(Peaceful?PetState.CalmSit:PetState.Contempt);
                break;
        }
    }
    private void TickFeeding(double dt,PetInput input)
    {
        if((!Options.FeedingEnabled && !manualFeeding) || !input.FoodReachable)
        {
            if(!Options.FeedingEnabled || manualFeeding) hungry=false;
            // A hidden or temporarily unavailable icon does not feed the cat. Retry once the host locates it.
            Enter(Peaceful?PetState.CalmSit:PetState.Contempt); return;
        }
        Face(double.IsNaN(input.FoodBodyDeltaX)?input.FoodDeltaX:input.FoodBodyDeltaX);
        switch(State)
        {
            case PetState.HungryWalk:
                if(input.FoodDistance<46) Enter(input.FoodAvailable==true?PetState.Eat:PetState.HungryWait);
                else MoveToward(dt,input.FoodDistance,input.FoodDeltaX,input.FoodDeltaY,38,95);
                break;
            case PetState.HungryWait:
                if(input.FoodAvailable==true) Enter(PetState.Eat);
                else if(input.FoodDistance>120) Enter(PetState.HungryWalk);
                else
                {
                    double phase=Age%8;
                    if(phase>2 && phase<3.5) VelocityX=-18;
                    else if(phase>5 && phase<6.5) VelocityX=18;
                    if(Math.Abs(VelocityX)>1) Facing=VelocityX<0?-1:1;
                }
                break;
            case PetState.Eat:
                if(input.FoodAvailable==false) Enter(PetState.HungryWait);
                else if(Age>5.5) { hungry=false; lastMealTime=time; lastInterest=time; Enter(PetState.Sated); }
                break;
            case PetState.Sated:
                if(Age>6) Enter(PetState.CalmSit);
                break;
        }
    }
    private void MoveToward(double dt,double distance,double dx,double dy,double stop,double limit)
    {
        double speed=Math.Min(limit,Math.Max(0,distance-stop)/Math.Max(dt,.001));
        VelocityX=dx/Math.Max(distance,1)*speed; VelocityY=dy/Math.Max(distance,1)*speed;
    }
    public void Click(double pointerDeltaX)
    {
        if(Options.GoodCat || State==PetState.CursorRaid) return;
        lastInterest=time; cooldownUntil=time+6; Face(pointerDeltaX);
        if(Options.Neutered) { Enter(random.Next(2)==0?PetState.CalmSit:PetState.Groom,true); return; }
        if(State is PetState.Sleep or PetState.Curl or PetState.Wake)
        {
            provocations.Clear();
            hisses.Clear();
            Enter(new[] { PetState.Scared,PetState.Contempt,PetState.SitContempt }[random.Next(3)],true);
            return;
        }
        PruneProvocations(); provocations.Enqueue(time);
        PetState next=State switch
        {
            PetState.SitContempt or PetState.SitHiss=>PetState.SitHiss,
            PetState.Scared=>PetState.Hiss,
            PetState.Contempt=>random.Next(2)==0?PetState.Hiss:PetState.Scared,
            _=>PetState.Hiss
        };
        if(next is PetState.Hiss or PetState.SitHiss) hisses.Enqueue(time);
        Enter(next,true);
    }
    private void EndAggression()
    {
        if(hisses.Count>=Options.ClickThreshold) Enter(PetState.PostHissChew);
        else if(CanSteal && provocations.Count>=Options.ClickThreshold) Enter(PetState.CursorRaid);
        else Enter(PetState.Recover);
    }
    private void PruneProvocations()
    {
        while(provocations.Count>0 && time-provocations.Peek()>7) provocations.Dequeue();
        while(hisses.Count>0 && time-hisses.Peek()>7) hisses.Dequeue();
    }
    private void ClearProvocations()
    {
        provocations.Clear(); hisses.Clear();
    }
    public void Wake() { lastInterest=time; Enter(PetState.Wake); }
    public void Rest() { Enter(PetState.Sleep); }
    public void Flatten() { Enter(PetState.Rag); }
    public void FeedNow() { manualFeeding=true; hungry=true; Enter(PetState.HungryWalk); }
    public void Neuter() { Options.Neutered=true; ClearProvocations(); Enter(PetState.Rag); }
    public void RestoreBell() { Options.Neutered=false; ClearProvocations(); lastInterest=time; Enter(Options.GoodCat?PetState.CalmSit:PetState.SitContempt); }
    public void ReleaseRaid() { ClearProvocations(); cooldownUntil=time+8; Enter(PetState.Recover); }
    public void ApplyOptions()
    {
        ChooseSleepDelay();
        if(!Options.FeedingEnabled && !manualFeeding) hungry=false;
        if(Peaceful) ClearProvocations();
        if((Peaceful || !Options.MouseStealEnabled) && State==PetState.CursorRaid) ReleaseRaid();
        if(Peaceful && (IsAggressive(State) || State is PetState.Contempt or PetState.SitContempt or PetState.Observe or PetState.Approach)) Enter(PetState.CalmSit);
    }
    public void DragFinished() { lastInterest=time; cooldownUntil=time+3; Enter(Peaceful?PetState.CalmSit:PetState.Contempt); }
    public void Bounce() { Facing=-Facing; if(State==PetState.Approach) { cooldownUntil=time+4; Enter(PetState.Observe); } }
    public void SetFacing(double dx)=>Face(dx);
    public void SetIdlePose(bool seated) { lastInterest=time; Enter(Peaceful?PetState.CalmSit:seated?PetState.SitContempt:PetState.Contempt); }
    public void ChooseSleepDelay()=>CurrentIdleSeconds=Options.RandomSleep
        ?Math.Max(5,Options.SleepMinSeconds)+random.NextDouble()*Math.Max(0,Options.SleepMaxSeconds-Options.SleepMinSeconds)
        :Math.Max(5,Options.IdleSeconds);
    public static bool IsAggressive(PetState state)=>state is PetState.Hiss or PetState.SitHiss or PetState.Scared or PetState.Pounce or PetState.PostHissChew or PetState.CursorRaid;
    private void Face(double dx) { if(Math.Abs(dx)>18) Facing=dx<0?-1:1; }
    private void StartWander() { Facing=random.Next(2)==0?-1:1; wanderDuration=1.5+random.NextDouble()*2.5; Enter(PetState.Wander); }
    private void Enter(PetState state,bool restart=false)
    {
        if(state==State && !restart) return;
        State=state; Age=0; VelocityX=VelocityY=0;
        if(state is not (PetState.HungryWalk or PetState.HungryWait or PetState.Eat or PetState.Sated))
        {
            if(manualFeeding) hungry=false;
            manualFeeding=false;
        }
        if(state==PetState.Sleep) { movementSinceSleep=false; sleepInputQuiet=0; ClearProvocations(); ChooseSleepDelay(); }
        StateChanged?.Invoke(state);
    }
}
