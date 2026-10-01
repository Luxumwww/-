namespace Hajimi.Behavior;

public enum PetState
{
    Sleep, Wake, Wander, Contempt, SitContempt, CalmSit, Groom, Observe, Approach,
    Chew, Hiss, SitHiss, Scared, PostHissChew, Pounce, CursorRaid, Recover,
    LieDown, Rag, Curl, HungryWalk, HungryWait, Eat, Sated
}

public readonly record struct PetInput(double Distance, double DeltaX, double DeltaY,
    bool MouseMoved, double SystemIdleSeconds, bool SuddenApproach = false, double BodyDeltaX = double.NaN,
    bool? FoodAvailable = null, double FoodDistance = double.PositiveInfinity, double FoodDeltaX = 0,
    double FoodDeltaY = 0, bool FoodReachable = false,double FoodBodyDeltaX = double.NaN);

public sealed class BehaviorOptions
{
    public double IdleSeconds { get; set; } = 35;
    public int ClickThreshold { get; set; } = 3;
    public double ObserveRadius { get; set; } = 290;
    public double NearRadius { get; set; } = 38;
    public double WalkSpeed { get; set; } = 48;
    public double ApproachSpeed { get; set; } = 85;
    public double ObserveSeconds { get; set; } = 1.8;
    public double ChewSeconds { get; set; } = 9;
    public bool GoodCat { get; set; }
    public bool Neutered { get; set; }
    public bool MouseStealEnabled { get; set; }
    public bool RandomSleep { get; set; }
    public double SleepMinSeconds { get; set; } = 20;
    public double SleepMaxSeconds { get; set; } = 70;
    // Controls autonomous hunger visits. FeedNow remains available when this is disabled.
    public bool FeedingEnabled { get; set; } = true;
    public double HungerSeconds { get; set; } = 180;
    public double HissSeconds { get; set; } = 1.7;
    public double ScaredSeconds { get; set; } = 5;
}
