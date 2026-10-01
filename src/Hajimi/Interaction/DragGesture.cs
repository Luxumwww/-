namespace Hajimi.Interaction;

public sealed class DragGesture
{
    private PointF press, origin;
    public bool IsDown { get; private set; }
    public bool IsDragging { get; private set; }
    public void Begin(PointF pointer,PointF window) { press=pointer; origin=window; IsDown=true; IsDragging=false; }
    public PointF? Move(PointF pointer)
    {
        if(!IsDown) return null;
        float dx=pointer.X-press.X,dy=pointer.Y-press.Y;
        if(!IsDragging && dx*dx+dy*dy>49) IsDragging=true;
        return IsDragging ? new PointF(origin.X+dx,origin.Y+dy) : null;
    }
    public bool End() { bool dragged=IsDragging; IsDown=false; IsDragging=false; return dragged; }
}
