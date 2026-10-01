using System.Drawing.Imaging;
using Hajimi.Animation;
using Hajimi.Assets;
using Hajimi.Behavior;

namespace Hajimi.Diagnostics;

public static class Preview
{
    public static void Create(string assetsPath,string output)
    {
        Directory.CreateDirectory(output);
        using var assets=new AssetCatalog(assetsPath); using var animator=new PetAnimator(assets);
        (PetState State,double Age,string Label)[] poses=[
            (PetState.Sleep,0,"01 / SLEEP"),(PetState.Wake,.6,"02 / WAKE"),(PetState.Wake,1.1,"03 / RISE"),
            (PetState.Contempt,1,"04 / CONTEMPT"),(PetState.Wander,0,"05 / WALK A"),(PetState.Wander,.2,"06 / WALK B"),
            (PetState.Chew,.1,"07 / LEAN"),(PetState.Chew,2.3,"08 / CHEW"),(PetState.Hiss,.2,"09 / HISS"),
            (PetState.Pounce,.2,"10 / POUNCE"),(PetState.LieDown,.5,"11 / LIE DOWN"),(PetState.Rag,1,"12 / RAG"),
            (PetState.Scared,.7,"13 / SCARED + OLD WU"),(PetState.SitContempt,1,"14 / SEATED CONTEMPT"),(PetState.SitHiss,.4,"15 / SEATED HISS"),
            (PetState.PostHissChew,1.2,"16 / POST HISS, NORMAL NECK"),(PetState.CalmSit,1,"17 / CALM SIT"),(PetState.Groom,1.1,"18 / SELF GROOM"),
            (PetState.HungryWait,1,"19 / WAIT FOR FOOD"),(PetState.Eat,1.4,"20 / EAT"),(PetState.Sated,1,"21 / SATISFIED"),
            (PetState.Wander,.36,"22 / WALK C"),(PetState.Wander,.53,"23 / WALK D"),(PetState.CursorRaid,1.2,"24 / MOUSE RAID")];
        using var sheet=new Bitmap(1500,110+(int)Math.Ceiling(poses.Length/3d)*365+20); using var g=Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(24,26,29)); using var title=new Font("Segoe UI",25,FontStyle.Bold); using var label=new Font("Segoe UI",12);
        g.DrawString("HAJIMI  /  MOTION STUDY",title,Brushes.White,25,22);
        g.DrawString("Original character poses + generated transitions | 8 fps attitude",label,Brushes.Gray,28,68);
        for(int i=0;i<poses.Length;i++)
        {
            var p=poses[i]; int x=i%3*500,y=110+i/3*365;
            using var bg=new SolidBrush(i%2==0?Color.FromArgb(45,48,51):Color.FromArgb(52,54,56)); g.FillRectangle(bg,x+5,y+5,490,355);
            using var frame=animator.Render(p.State,p.Age,1,new(30,-10),1,p.Age,false,.7f); g.DrawImage(frame,x,y);
            g.DrawString(p.Label,label,Brushes.Silver,x+20,y+334);
            frame.Save(Path.Combine(output,$"{i+1:00}-{p.State.ToString().ToLowerInvariant()}.png"),ImageFormat.Png);
        }
        sheet.Save(Path.Combine(output,"contact-sheet.png"),ImageFormat.Png);
        using(var settings=new Platform.SettingsWindow(new Settings(),()=> { }))
        {
            settings.Show();
            using var bitmap=new Bitmap(settings.Width,settings.Height);
            settings.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
            bitmap.Save(Path.Combine(output,"settings-general.png")); settings.Close();
        }
        using(var settings=new Platform.SettingsWindow(new Settings(),()=> { },"behavior"))
        {
            settings.Show();
            using var bitmap=new Bitmap(settings.Width,settings.Height);
            settings.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
            bitmap.Save(Path.Combine(output,"settings-behavior.png")); settings.Close();
        }
        foreach(string name in new[] { "walk","mouth","scared","post-chew","eat" })
        {
            string folder=Path.Combine(output,name); Directory.CreateDirectory(folder);
            int count=name=="walk"?8:16;
            for(int i=0;i<count;i++)
            {
                var state=name switch { "walk"=>PetState.Wander,"mouth"=>PetState.Chew,"scared"=>PetState.Scared,"post-chew"=>PetState.PostHissChew,_=>PetState.Eat };
                using var frame=animator.Render(state,i/8d+2,1,new(40,(float)Math.Sin(i/16d*Math.PI*2)*95),1,i/8d,false,(float)(.5+.5*Math.Sin(i*1.3)),name=="walk"?i*WalkCycle.StrideLength/count:null);
                frame.Save(Path.Combine(folder,$"{i:00}.png"));
            }
        }
        // Inspect complete sprites at the same sizes used by the desktop window, against an opaque
        // background so hidden RGB in transparent PNG pixels cannot masquerade as visible fragments.
        foreach(float scale in new[] { .4f,.85f,1f,1.6f })
        foreach(int facing in new[] { 1,-1 })
        {
            string folder=Path.Combine(output,$"walk-review-{(int)(scale*100)}-{(facing>0?"right":"left")}");
            Directory.CreateDirectory(folder);
            int tileWidth=(int)(500*scale),tileHeight=(int)(340*scale)+26;
            using var review=new Bitmap(tileWidth*4,tileHeight*2); using var rg=Graphics.FromImage(review);
            rg.Clear(Color.FromArgb(78,82,87));
            for(int i=0;i<WalkCycle.FrameCount;i++)
            {
                using var frame=animator.Render(PetState.Wander,0,facing,new(),scale,walkDistance:(i+.5)*WalkCycle.StrideLength/WalkCycle.FrameCount);
                using var solid=new Bitmap(frame.Width,frame.Height); using(var sg=Graphics.FromImage(solid))
                { sg.Clear(Color.FromArgb(78,82,87)); sg.DrawImageUnscaled(frame,0,0); }
                solid.Save(Path.Combine(folder,$"{i:00}.png"));
                int col=i%4,row=i/4; rg.DrawImageUnscaled(solid,col*tileWidth,row*tileHeight);
                rg.DrawString($"FRAME {i+1}",label,Brushes.White,col*tileWidth+8,row*tileHeight+frame.Height+3);
            }
            review.Save(Path.Combine(output,$"walk-review-{(int)(scale*100)}-{(facing>0?"right":"left")}.png"));
        }
    }
}
