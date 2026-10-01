namespace Hajimi.Platform;

public static class ScreenBounds
{
    public static PointF Clamp(PointF location, RectangleF visible, Rectangle work)
    {
        // Constrain the visible cat, not its transparent canvas margin. Negative monitor coordinates are valid.
        float minX=work.Left-visible.Left, maxX=work.Right-visible.Right;
        float minY=work.Top-visible.Top, maxY=work.Bottom-visible.Bottom;
        return new(Math.Clamp(location.X,minX,Math.Max(minX,maxX)), Math.Clamp(location.Y,minY,Math.Max(minY,maxY)));
    }
}
