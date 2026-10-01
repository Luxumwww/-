namespace Hajimi.Interaction;

public static class FeedingNavigation
{
    // A fixed body anchor prevents a sitting/standing change from repeatedly restarting the walk.
    public static (double Distance,double X,double Y,double BodyX) Measure(PointF window,PointF bowl,float scale,int facing)
    {
        double body=(bowl.X-window.X)/scale-250;
        int side=Math.Abs(body)<18?facing:body<0?-1:1;
        double dx=body-side*178,dy=(bowl.Y-window.Y)/scale-245;
        return (Math.Sqrt(dx*dx+dy*dy),dx,dy,body);
    }
}
