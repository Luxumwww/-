namespace Hajimi.Animation;

/// <summary>Chooses complete walk frames from actual travel, without changing their anatomy.</summary>
public static class WalkCycle
{
    public const int FrameCount=8;
    public const double StrideLength=64;

    public static int FrameIndex(double distance)
    {
        if(!double.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        double phase=distance/StrideLength;
        phase-=Math.Floor(phase);
        return Math.Min(FrameCount-1,(int)(phase*FrameCount));
    }
}
