using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Hajimi.Assets;
using Hajimi.Behavior;

namespace Hajimi.Animation;

public sealed class PetAnimator(AssetCatalog assets) : IDisposable
{
    public const int Width = 500, Height = 340;
    public string CurrentPose { get; private set; } = "sleep";
    public RectangleF VisibleBounds { get; private set; }
    public PointF Head { get; private set; } = new(310, 235);

    public PointF Mouth=>Head;
    public float HeadAngle { get; private set; }
    public Bitmap Render(PetState state,double age,int facing,PointF look,float scale,double clock=0,bool peaceful=false,float mouthAmount=0,double? walkDistance=null)
    {
        var frame = new Bitmap((int)(Width * scale), (int)(Height * scale), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(frame);
        g.CompositingMode = CompositingMode.SourceOver;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(scale, scale);
        double step = Math.Floor(age * 8) / 8;
        string key = SelectPose(state, age, walkDistance);
        if(peaceful && state is PetState.Wake or PetState.Contempt or PetState.SitContempt or PetState.Observe or PetState.Recover)
            key=state==PetState.Wake && age<.6?"sleep":"calm-sit";
        if(peaceful && state==PetState.LieDown && age<.3) key="calm-sit";
        CurrentPose = key;
        var pose=assets[key];
        int renderFacing=facing*(pose.FacesLeft?-1:1);
        if(renderFacing<0) { g.TranslateTransform(Width,0); g.ScaleTransform(-1,1); }
        float width = pose.Width;
        float height = width * pose.Image.Height / pose.Image.Width;
        float x = (Width - width) / 2, y = 318 - height;
        float bob = 0, stretch = 1;
        if (state == PetState.Sleep) { stretch = 1 + (float)Math.Sin(clock * 1.6) * .007f; y -= height * (stretch - 1); }
        if(state is PetState.Hiss or PetState.SitHiss or PetState.Scared) bob=age<.18?-5:((int)(step*8)%2)*-1;
        if (state == PetState.Pounce) { x += (float)Math.Sin(Math.Min(1, age / .45) * Math.PI) * 24; bob = -(float)Math.Sin(Math.Min(1, age / .45) * Math.PI) * 12; }
        var destination = new RectangleF(x, y + bob, width, height * stretch);
        PointF mouthUV=key switch
        {
            "chew"=>new(.94f,.60f),"scared"=>new(.30f,.47f),"sit-hiss"=>new(.69f,.245f),
            "hiss"=>new(.81f,.425f),"post-chew"=>new(.925f,.265f),"eat"=>new(.945f,.755f),
            "contempt"=>new(.83f,.245f),"sit-contempt"=>new(.72f,.24f),"calm-sit"=>new(.87f,.27f),
            "groom"=>new(.865f,.36f),"sleep"=>new(.77f,.60f),"rag" or "rag-before"=>new(.91f,.35f),
            var walk when walk.StartsWith("walk-",StringComparison.Ordinal)=>new(.85f,.35f),
            _=>new(.935f,.54f)
        };
        PointF mouthLocal=new(mouthUV.X*width,mouthUV.Y*height);
        HeadAngle=0;
        if(key=="chew")
        {
            HeadAngle=(float)Math.Clamp(Math.Atan2(look.Y,Math.Max(14,look.X*renderFacing+35)),-.65,.65);
            mouthLocal=DrawChew(g,pose.Image,destination,age,HeadAngle,mouthAmount,mouthLocal);
        }
        // Walking plays complete painted PNGs. Never cut, bend or remap individual limbs.
        else if(key.StartsWith("walk-")) g.DrawImage(pose.Image,destination);
        else if(key=="contempt" || key=="sit-contempt") DrawWatch(g,pose.Image,destination,peaceful?new():look,renderFacing);
        else if(state is PetState.Hiss or PetState.SitHiss or PetState.Scared or PetState.PostHissChew or PetState.Eat or PetState.HungryWait)
            DrawSpeechJaw(g,pose.Image,destination,mouthUV,state,age,mouthAmount);
        else if(state==PetState.Groom)
            DrawWatch(g,pose.Image,destination,new(0,(float)Math.Sin(step*7)*100),renderFacing);
        else g.DrawImage(pose.Image, destination);
        float extra=key=="chew"?20:0;
        VisibleBounds=new((renderFacing>0?x:Width-x-width-extra)*scale,(y+bob)*scale,(width+extra)*scale,height*stretch*scale);
        var head=new PointF(x+mouthLocal.X,y+bob+mouthLocal.Y);
        Head=new((renderFacing>0?head.X:Width-head.X)*scale,head.Y*scale);
        return frame;
    }

    public static string SelectPose(PetState state, double age, double? walkDistance=null) => state switch
    {
        PetState.Sleep => "sleep",
        PetState.Wake => age < .4 ? "sleep" : age < .85 ? "wake-low" : age < 1.35 ? "wake-high" : "contempt",
        PetState.Wander or PetState.Approach or PetState.HungryWalk or PetState.CursorRaid => $"walk-{WalkCycle.FrameIndex(walkDistance??age*48)}",
        PetState.Chew => "chew",
        PetState.Hiss or PetState.Pounce => "hiss",
        PetState.Scared=>"scared",
        PetState.SitContempt=>"sit-contempt",
        PetState.SitHiss=>"sit-hiss",
        PetState.CalmSit or PetState.Sated=>"calm-sit",
        PetState.Groom=>"groom",
        PetState.PostHissChew=>"post-chew",
        PetState.HungryWait=>"calm-sit",
        PetState.Eat=>"eat",
        PetState.Recover => "contempt",
        PetState.LieDown => age < .3 ? "contempt" : age < .8 ? "lower" : "rag-before",
        PetState.Rag => "rag",
        PetState.Curl => age < .45 ? "rag" : age < 1 ? "wake-low" : "sleep",
        _ => "contempt"
    };

    private static void DrawWatch(Graphics g, Bitmap image, RectangleF d, PointF look, int facing)
    {
        float dx = Math.Clamp(look.X * facing / 55, -4, 4), dy = Math.Clamp(look.Y / 65, -3, 3);
        DrawRemapped(g,image,d,(x,y)=>
        {
            float sy=y<d.Height*.31f?(y-dy)/(1-dy/(d.Height*.31f)):y;
            float head=Math.Clamp(1-sy/(d.Height*.31f),0,1);
            return new(x-dx*head,sy);
        });
    }

    private static PointF DrawChew(Graphics g,Bitmap image,RectangleF d,double age,float angle,float audio,PointF mouth)
    {
        float extension=4+12*(float)Math.Clamp(age/1.5,0,1);
        float jaw=(new float[] { 0,1,2.4f,.5f,0,1.5f })[(int)(age*8)%6]+audio;
        float cosine=(float)Math.Cos(angle),sine=(float)Math.Sin(angle);
        var pivot=new PointF(d.Width*.8f,d.Height*.46f);
        PointF Forward(float sx,float sy)
        {
            float u=sx/d.Width,v=sy/d.Height;
            float head=Math.Clamp((u-.68f)/.22f,0,1)*Math.Clamp((.85f-v)/.18f,0,1);
            float rx=(sx-pivot.X)*cosine-(sy-pivot.Y)*sine+pivot.X;
            float ry=(sx-pivot.X)*sine+(sy-pivot.Y)*cosine+pivot.Y;
            float lip=Math.Clamp((u-.84f)/.08f,0,1)*Math.Clamp((v-.51f)/.11f,0,1);
            return new(sx+(rx-sx)*head+head*extension,sy+(ry-sy)*head+lip*jaw);
        }
        DrawRemapped(g,image,d,(x,y)=>
        {
            float sx=x,sy=y;
            for(int i=0;i<4;i++) { var p=Forward(sx,sy); sx+=(x-p.X)*.8f; sy+=(y-p.Y)*.8f; }
            return new(sx,sy);
        });
        return Forward(mouth.X,mouth.Y);
    }
    private static void DrawSpeechJaw(Graphics g,Bitmap image,RectangleF d,PointF uv,PetState state,double age,float audio)
    {
        float cycle=(float)(.5+.5*Math.Sin(Math.Floor(age*9)*1.9));
        float opening=state is PetState.Eat or PetState.PostHissChew?cycle:Math.Max(audio,age<.2?.15f:0);
        float cx=d.Width*uv.X,cy=d.Height*uv.Y;
        DrawRemapped(g,image,d,(x,y)=>
        {
            if(state is PetState.Hiss or PetState.SitHiss)
            {
                // Collapse the supplied open-mouth gap during quiet syllables, rejoining the neck below it.
                float mask=Math.Clamp(1-Math.Abs(x-cx)/18,0,1);
                float gap=d.Height*.075f,top=cy-gap*.6f,rejoin=28;
                float compression=1-(1-opening)*.6f*mask,end=top+gap*compression;
                float sy=y;
                if(y>top && y<end) sy=top+(y-top)/compression;
                else if(y>=end && y<top+gap+rejoin) sy=top+gap+(y-end)/(1+gap*(1-compression)/rejoin);
                return new(x,sy);
            }
            float weight=Math.Clamp(1-Math.Abs(x-cx)/18,0,1)*Math.Clamp(1-Math.Abs(y-cy)/20,0,1);
            return new(x,y-weight*opening*4);
        });
        if(state is PetState.Scared or PetState.HungryWait && opening>.08)
        {
            float w=8+opening*3,h=1+opening*7;
            using var dark=new SolidBrush(Color.FromArgb(235,40,24,27));
            using var pink=new SolidBrush(Color.FromArgb(220,188,111,119));
            g.FillEllipse(dark,d.X+cx-w/2,d.Y+cy,w,h);
            g.FillEllipse(pink,d.X+cx-w/3,d.Y+cy+h*.65f,w*.65f,h*.28f);
        }
    }

    // Inverse texture mapping samples one continuous image, so there are no alpha seams between tiles.
    private static void DrawRemapped(Graphics g,Bitmap image,RectangleF d,Func<float,float,PointF> inverse)
    {
        const int pad=20;
        float density=image.Width/d.Width;
        int padding=(int)Math.Ceiling(pad*density),w=image.Width+padding*2,h=image.Height+padding*2;
        var read=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        var source=new byte[read.Stride*read.Height]; Marshal.Copy(read.Scan0,source,0,source.Length); image.UnlockBits(read);
        var output=new byte[w*h*4]; int sourceStride=image.Width*4;
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            var p=inverse((x-padding)/density,(y-padding)/density);
            float sx=p.X*density,sy=p.Y*density; int ix=(int)Math.Floor(sx),iy=(int)Math.Floor(sy);
            if(ix<0 || iy<0 || ix>=image.Width-1 || iy>=image.Height-1) continue;
            float fx=sx-ix,fy=sy-iy; int a=iy*sourceStride+ix*4,b=a+sourceStride,index=(y*w+x)*4;
            for(int c=0;c<4;c++)
                output[index+c]=(byte)((source[a+c]*(1-fx)+source[a+4+c]*fx)*(1-fy)+(source[b+c]*(1-fx)+source[b+4+c]*fx)*fy);
        }
        using var mapped=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
        var write=mapped.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        Marshal.Copy(output,0,write.Scan0,output.Length); mapped.UnlockBits(write);
        g.DrawImage(mapped,new RectangleF(d.X-padding/density,d.Y-padding/density,w/density,h/density));
    }
    // The catalog owns every frame; the animator retains no native image resources.
    public void Dispose() { }
}
