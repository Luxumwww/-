using Hajimi.Platform;

namespace Hajimi.Interaction;

public sealed class MouseTracker
{
    public Point Position { get; private set; }
    public bool Moved { get; private set; }
    public double Speed { get; private set; }
    public double IdleSeconds { get; private set; }
    public MouseTracker() { if (Native.GetCursorPos(out var p)) Position = new(p.X,p.Y); }
    public void Poll(double dt)
    {
        if (Native.GetCursorPos(out var p))
        {
            double dx=p.X-Position.X, dy=p.Y-Position.Y;
            Moved=dx*dx+dy*dy>1; Speed=Math.Sqrt(dx*dx+dy*dy)/Math.Max(.01,dt);
            Position=new(p.X,p.Y);
        }
        IdleSeconds=Native.IdleSeconds();
    }
}
