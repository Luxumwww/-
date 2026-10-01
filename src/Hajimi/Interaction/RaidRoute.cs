namespace Hajimi.Interaction;

public sealed class RaidRoute(int seed=0)
{
    private readonly Random random=seed==0?new Random():new Random(seed);
    private PointF target;
    private double elapsed;
    private bool initialized;
    public PointF Move(PointF position,Rectangle work,RectangleF visible,double dt,out double facing)
    {
        elapsed+=dt;
        var min=new PointF(work.Left-visible.Left,work.Top-visible.Top);
        var max=new PointF(Math.Max(min.X,work.Right-visible.Right),Math.Max(min.Y,work.Bottom-visible.Bottom));
        if(!initialized || elapsed>1.35 || Math.Abs(position.X-target.X)+Math.Abs(position.Y-target.Y)<45)
        {
            target=new(min.X+(float)random.NextDouble()*(max.X-min.X),min.Y+(float)random.NextDouble()*(max.Y-min.Y));
            initialized=true; elapsed=0;
        }
        double dx=target.X-position.X,dy=target.Y-position.Y,distance=Math.Sqrt(dx*dx+dy*dy);
        facing=dx;
        double step=Math.Min(distance,420*dt);
        return new(position.X+(float)(dx/Math.Max(1,distance)*step),position.Y+(float)(dy/Math.Max(1,distance)*step));
    }
    public void Reset() { initialized=false; elapsed=0; }
}
