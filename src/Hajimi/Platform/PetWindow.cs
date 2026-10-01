using System.Diagnostics;
using Hajimi.Animation;
using Hajimi.Assets;
using Hajimi.Audio;
using Hajimi.Behavior;
using Hajimi.Interaction;

namespace Hajimi.Platform;

public sealed class PetWindow : Form
{
    private readonly Settings settings;
    private readonly AssetCatalog assets;
    private readonly PetAnimator animator;
    private readonly AudioEngine audio;
    private readonly MouseTracker mouse=new();
    private readonly DragGesture gesture=new();
    private readonly CursorCapture capture;
    private readonly RaidRoute route=new();
    private readonly System.Windows.Forms.Timer timer=new() { Interval=33 };
    private readonly Stopwatch clock=new();
    private readonly NotifyIcon tray;
    private readonly Icon applicationIcon=PetIcon.Load();
    private readonly ContextMenuStrip menu=new();
    private readonly string? smokeOutput;
    private readonly List<string> trace=new();
    private static readonly int[] mouseKeys={ 1,2,4,5,6 };
    private readonly IntPtr initialForeground;
    private SettingsWindow? settingsWindow;
    private Bitmap? frame;
    private PointF position;
    private PointF? bowl;
    private string bowlStatus="位置检测中";
    private bool queryingIcon;
    private int iconLookupVersion;
    private long? foodCount;
    private double previousTime,lastRender,previousDistance=1000,lastScreenCheck,lastFoodCheck=-99,lastIconCheck=-99,walkDistance,lastDrawnWalkDistance;
    private bool menuOpen,forceRender=true,queryingFood,snapFoodMouth,lastNeutered;
    private Rectangle activeWork;
    private float scale;
    private int smokeStage;
    private int previousMenuButtons;
    private double smokeStageAt;
    private bool diagnostic=>smokeOutput!=null;
    private bool dragging=>gesture.IsDragging;
    public PetBrain Brain { get; }

    public PetWindow(Settings settings,string assetsPath,string? smokeOutput=null)
    {
        this.settings=settings; this.smokeOutput=smokeOutput; lastNeutered=settings.Neutered;
        initialForeground=Native.GetForegroundWindow();
        assets=new(assetsPath); animator=new(assets);
        audio=new(Path.Combine(assetsPath,"audio")) { Muted=diagnostic||settings.Muted,Volume=settings.Volume,SnoreEnabled=settings.SnoreEnabled };
        capture=new(diagnostic); Brain=new(settings.Behavior()); Brain.StateChanged+=StateChanged;
        Text="哈基米桌宠"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=settings.AlwaysOnTop;
        AutoScaleMode=AutoScaleMode.None; StartPosition=FormStartPosition.Manual; scale=settings.Scale;
        activeWork=(Screen.PrimaryScreen??Screen.AllScreens[0]).WorkingArea;
        position=new(settings.X??activeWork.Right-480*scale,settings.Y??activeWork.Bottom-340*scale);
        if(!Screen.AllScreens.Any(s=>s.WorkingArea.Contains(Point.Round(new PointF(position.X+250*scale,position.Y+250*scale)))))
            position=new(activeWork.Right-480*scale,activeWork.Bottom-340*scale);
        Location=Point.Round(position); ClientSize=new((int)(500*scale),(int)(340*scale));
        Icon=applicationIcon;
        tray=new NotifyIcon { Icon=applicationIcon,Visible=true,Text="哈基米 · 蜷缩睡觉",ContextMenuStrip=menu };
        tray.DoubleClick+=(_,_)=>Brain.Wake();
        menu.AutoClose=true;
        menu.Opening+=(_,_)=> { StopCapture(); EndDrag(); menuOpen=true; previousMenuButtons=PressedMouseButtons(); audio.Paused=true; BuildMenu(); };
        menu.Closed+=(_,_)=> { menuOpen=false; audio.Paused=settingsWindow!=null; SaveSettings(); };
        timer.Tick+=(_,_)=>TickPet();
        MouseDown+=OnPetMouseDown; MouseMove+=OnPetMouseMove; MouseUp+=(_,e)=> { if(e.Button==MouseButtons.Left) EndDrag(); };
        MouseCaptureChanged+=(_,_)=> { if(gesture.IsDown && !Capture) EndDrag(); };
        DpiChanged+=(_,_)=> { scale=settings.Scale*DeviceDpi/96f; forceRender=true; };
        Shown+=(_,_)=>
        {
            scale=settings.Scale*DeviceDpi/96f; Render(); RefreshIcon(); RefreshFood();
            clock.Start(); previousTime=0; timer.Start();
            if(!diagnostic && (audio.Warning!=null || assets.Warnings.Count>0))
                tray.ShowBalloonTip(5000,"哈基米已启动",string.Join("\n",assets.Warnings.Append(audio.Warning).Where(s=>s!=null)),ToolTipIcon.Info);
        };
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x80000|0x80|0x08000000; return p; } }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21) { m.Result=(IntPtr)3; return; }
        if(m.Msg==0x14) { m.Result=(IntPtr)1; return; }
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    private void StateChanged(PetState state)
    {
        forceRender=true; audio.StateChanged(state);
        if(state is PetState.Hiss or PetState.SitHiss) Brain.Options.HissSeconds=audio.CurrentDuration+.12;
        if(state==PetState.Scared) Brain.Options.ScaredSeconds=audio.CurrentDuration+.12;
        if(state==PetState.Eat) snapFoodMouth=true;
        if(state==PetState.CursorRaid)
        {
            gesture.End(); Capture=false; route.Reset();
            if(!capture.Start()) { tray.ShowBalloonTip(3000,"没叼到鼠标",capture.Warning??"鼠标不可用。",ToolTipIcon.Info); Brain.ReleaseRaid(); }
        }
        else capture.Release();
        if(diagnostic) trace.Add($"{clock.Elapsed.TotalSeconds:F2}: {state}; audio={audio.CurrentKey??"none"}");
        tray.Text=$"哈基米 · {NameOf(state)}";
    }
    private void TickPet()
    {
        double now=clock.Elapsed.TotalSeconds,dt=Math.Clamp(now-previousTime,0,.1); previousTime=now;
        switch(capture.ConsumeCommand())
        {
            case CursorCommand.Neuter:
                capture.Release(); settings.Neutered=true; lastNeutered=true; Brain.Neuter(); SaveSettings(); break;
            case CursorCommand.Escape:
            case CursorCommand.Timeout:
                capture.Release(); Brain.ReleaseRaid(); break;
        }
        mouse.Poll(dt);
        DismissMenuOnOutsideClick(mouse.Position,PressedMouseButtons());
        if(now-lastScreenCheck>2)
        {
            lastScreenCheck=now;
            activeWork=Screen.FromPoint(Point.Round(new PointF(position.X+250*scale,position.Y+250*scale))).WorkingArea;
        }
        if(now-lastFoodCheck>4) { lastFoodCheck=now; RefreshFood(); }
        if(now-lastIconCheck>15) { lastIconCheck=now; RefreshIcon(); }
        if(diagnostic) TickSmoke(now);
        if(!dragging && !menuOpen && settingsWindow==null)
        {
            var input=MakeInput(); if(diagnostic) input=SmokeInput();
            Brain.Tick(dt,input);
            var beforeMovement=position;
            if(Brain.State==PetState.CursorRaid)
            { position=route.Move(position,activeWork,animator.VisibleBounds,dt,out double facing); Brain.SetFacing(facing); }
            else { position.X+=(float)(Brain.VelocityX*dt*scale); position.Y+=(float)(Brain.VelocityY*dt*scale); }
            var before=position; ClampPosition();
            if(Brain.State is PetState.Wander or PetState.Approach or PetState.HungryWalk or PetState.CursorRaid
                || Brain.State==PetState.HungryWait && Math.Abs(Brain.VelocityX)>1)
            {
                double dx=position.X-beforeMovement.X,dy=position.Y-beforeMovement.Y;
                walkDistance+=Math.Sqrt(dx*dx+dy*dy)/scale;
            }
            if(Math.Abs(position.X-before.X)>.1f && Math.Abs(Brain.VelocityX)>1 && Brain.State==PetState.Wander) Brain.Bounce();
        }
        // Switch complete sprites only at a frame boundary. A whole frame is never bent or sliced.
        if(WalkCycle.FrameIndex(walkDistance)!=WalkCycle.FrameIndex(lastDrawnWalkDistance)) forceRender=true;
        if(forceRender || now-lastRender>=.12) { Render(); lastRender=now; forceRender=false; }
        else Native.SetWindowPos(Handle,IntPtr.Zero,(int)position.X,(int)position.Y,0,0,0x1|0x4|0x10);
        if(capture.Active) capture.Update(Point.Round(new(position.X+animator.Mouth.X,position.Y+animator.Mouth.Y)));
        audio.Pump();
        if(diagnostic && (smokeStage>=9 || now>95)) FinishSmoke();
    }
    private PetInput MakeInput()
    {
        double dx=(mouse.Position.X-position.X-animator.Mouth.X)/scale,dy=(mouse.Position.Y-position.Y-animator.Mouth.Y)/scale;
        double distance=Math.Sqrt(dx*dx+dy*dy),fdx=0,fdy=0,fd=double.PositiveInfinity,foodBody=0;
        if(bowl.HasValue) { (fd,fdx,fdy,foodBody)=FeedingNavigation.Measure(position,bowl.Value,scale,Brain.Facing); }
        var input=new PetInput(distance,dx,dy,mouse.Moved,mouse.IdleSeconds,
            mouse.Speed>850 && previousDistance>90 && distance<30,(mouse.Position.X-position.X)/scale-250,
            foodCount.HasValue?foodCount>0:null,fd,fdx,fdy,bowl.HasValue,foodBody);
        previousDistance=distance; return input;
    }
    private void Render()
    {
        var look=Brain.Peaceful || Brain.State==PetState.CursorRaid?new PointF():new PointF((mouse.Position.X-position.X-animator.Mouth.X)/scale,(mouse.Position.Y-position.Y-animator.Mouth.Y)/scale);
        var state=dragging?(Brain.Peaceful?PetState.CalmSit:PetState.Contempt):Brain.State;
        if(state==PetState.HungryWait && Math.Abs(Brain.VelocityX)>1) state=PetState.Wander;
        var next=animator.Render(state,Brain.Age,Brain.Facing,look,scale,clock.Elapsed.TotalSeconds,Brain.Peaceful,audio.MouthAmount,walkDistance);
        lastDrawnWalkDistance=walkDistance;
        frame?.Dispose(); frame=next;
        if(snapFoodMouth)
        {
            if(!diagnostic && bowl.HasValue) position=new(bowl.Value.X-animator.Mouth.X,bowl.Value.Y-animator.Mouth.Y);
            snapFoodMouth=false;
        }
        ClampPosition(); Native.Present(Handle,frame,(int)position.X,(int)position.Y);
    }
    private void ClampPosition()=>position=ScreenBounds.Clamp(position,animator.VisibleBounds,activeWork);
    private void OnPetMouseDown(object? sender,MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Right) { StopCapture(); menu.Show(this,e.Location); return; }
        if(e.Button!=MouseButtons.Left || capture.Active) return;
        Point pointer=mouse.Position; if(Native.GetCursorPos(out var p)) pointer=new(p.X,p.Y);
        gesture.Begin(pointer,position); Capture=true;
        if(!settings.GoodCat) Brain.Click((pointer.X-position.X)/scale-250);
        Render();
    }
    private void OnPetMouseMove(object? sender,MouseEventArgs e)
    {
        if(!gesture.IsDown || !Native.GetCursorPos(out var p)) return;
        bool before=dragging; var next=gesture.Move(new PointF(p.X,p.Y));
        if(!before && dragging) { StopCapture(); audio.Paused=true; }
        if(next==null) return;
        activeWork=Screen.FromPoint(new Point(p.X,p.Y)).WorkingArea; position=next.Value; ClampPosition(); forceRender=true;
    }
    private void EndDrag()
    {
        bool before=gesture.End(); Capture=false;
        if(before) Brain.DragFinished(); audio.Paused=menuOpen || settingsWindow!=null; forceRender=true;
    }
    private static int PressedMouseButtons()
    {
        int buttons=0;
        for(int i=0;i<mouseKeys.Length;i++)
            if((Native.GetAsyncKeyState(mouseKeys[i])&0x8000)!=0) buttons|=1<<i;
        return buttons;
    }
    private void DismissMenuOnOutsideClick(Point pointer,int pressedButtons)
    {
        // The pet cannot activate, so another app's click need not produce a WinForms message.
        // Observe button edges without consuming the click; submenu controls keep their capture.
        int pressedNow=pressedButtons&~previousMenuButtons;
        previousMenuButtons=pressedButtons;
        if(menuOpen && menu.Visible && pressedNow!=0 && !MenuContainsScreenPoint(menu,pointer))
            menu.Close(ToolStripDropDownCloseReason.AppClicked);
    }
    private static bool MenuContainsScreenPoint(ToolStripDropDown dropdown,Point pointer)
    {
        if(!dropdown.Visible) return false;
        if(dropdown.Bounds.Contains(pointer)) return true;
        foreach(var item in dropdown.Items.OfType<ToolStripDropDownItem>())
            if(item.HasDropDownItems && MenuContainsScreenPoint(item.DropDown,pointer)) return true;
        return false;
    }
    private void StopCapture() { capture.Release(); if(Brain.State==PetState.CursorRaid) Brain.ReleaseRaid(); }
    private void RefreshIcon(bool notify=false)
    {
        if(settings.BowlX.HasValue && settings.BowlY.HasValue)
        {
            iconLookupVersion++; queryingIcon=false;
            SetBowlLocation(new(new PointF(settings.BowlX.Value,settings.BowlY.Value),$"手动饭盆：({settings.BowlX:F0}, {settings.BowlY:F0})",""),notify);
            return;
        }
        if(queryingIcon && !notify) return;
        int version=++iconLookupVersion; queryingIcon=true;
        if(!bowl.HasValue) bowlStatus="正在寻找桌面回收站…";
        Task.Run(RecycleBin.LocateDesktopIcon).ContinueWith(task=>
        {
            if(IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(()=>
                {
                    if(version!=iconLookupVersion) return;
                    queryingIcon=false;
                    var location=task.Status==TaskStatus.RanToCompletion?task.Result:
                        new RecycleBin.IconLocation(null,"读取桌面图标失败","请重新显示桌面再试；也可把猫拖到回收站旁手动设置饭盆。");
                    SetBowlLocation(location,notify);
                }));
            }
            catch(InvalidOperationException) { }
        });
    }
    private void SetBowlLocation(RecycleBin.IconLocation location,bool notify)
    {
        bowl=location.Position; bowlStatus=location.Status;
        string advice=location.Advice;
        if(bowl.HasValue && !Screen.AllScreens.Any(s=>s.WorkingArea.Contains(Point.Round(bowl.Value))))
        { bowl=null; bowlStatus="回收站位置不在当前屏幕上"; advice="请把桌面回收站移到可见位置后重试，或将猫拖到回收站旁手动设置饭盆。"; }
        if(diagnostic) trace.Add($"CHECK recycleLocation={bowl}; status={bowlStatus}; shellName={location.DisplayName}; notify={notify}");
        if(notify && !diagnostic)
            tray.ShowBalloonTip(5000,bowl.HasValue?"已定位桌面回收站":"回收站图标未找到",
                bowl.HasValue?$"{bowlStatus}\n选择“去回收站找饭”立即进食，或开启“自动进食”等它饿了自己过去。":$"{bowlStatus}\n{advice}",bowl.HasValue?ToolTipIcon.Info:ToolTipIcon.Warning);
    }
    private void FindRecycleBinNow()
    {
        settings.BowlX=settings.BowlY=null; bowl=null;
        lastIconCheck=clock.Elapsed.TotalSeconds;
        RefreshIcon(true); RefreshFood(); SaveSettings();
    }
    private void RefreshFood()
    {
        if(queryingFood) return;
        queryingFood=true;
        Task.Run(()=>RecycleBin.QueryCount()).ContinueWith(task=>
        {
            if(IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(()=> { foodCount=task.Status==TaskStatus.RanToCompletion?task.Result:null; queryingFood=false; })); }
            catch(InvalidOperationException) { }
        });
    }
    private void BuildMenu()
    {
        while(menu.Items.Count>0) menu.Items[0].Dispose();
        menu.Items.Add(new ToolStripMenuItem($"哈基米 · {NameOf(Brain.State)}") { Enabled=false });
        var actions=Group("动作","actions");
        actions.DropDownItems.Add("叫醒",null,(_,_)=>Brain.Wake());
        actions.DropDownItems.Add("睡觉",null,(_,_)=>Brain.Rest());
        actions.DropDownItems.Add("烂抹布",null,(_,_)=>Brain.Flatten());
        if(!Brain.Peaceful)
        {
            actions.DropDownItems.Add("站着蔑视",null,(_,_)=>Brain.SetIdlePose(false));
            actions.DropDownItems.Add("坐着蔑视",null,(_,_)=>Brain.SetIdlePose(true));
        }
        var character=Group("性格","character");
        Toggle(character,"好猫模式",settings.GoodCat,v=>settings.GoodCat=v);
        Toggle(character,"叼走鼠标",settings.MouseStealEnabled,v=>settings.MouseStealEnabled=v);
        if(settings.Neutered) character.DropDownItems.Add("装回铃铛",null,(_,_)=> { settings.Neutered=false; Brain.RestoreBell(); lastNeutered=false; ApplySettings(); });
        var clicks=new ToolStripMenuItem("挑衅次数") { Name="threshold" };
        foreach(int value in new[] { 2,3,5 })
        {
            var item=new ToolStripMenuItem($"{value} 次") { Checked=settings.ClickThreshold==value };
            item.Click+=(_,_)=> { settings.ClickThreshold=value; ApplySettings(); }; clicks.DropDownItems.Add(item);
        }
        character.DropDownItems.Add(clicks);

        var feeding=Group("吃饭","feeding");
        Toggle(feeding,"自动进食",settings.FeedingEnabled,v=>settings.FeedingEnabled=v,"auto-food");
        var food=new ToolStripMenuItem("去找饭") { Name="manual-food",Enabled=bowl.HasValue };
        food.Click+=(_,_)=> { RefreshFood(); Brain.FeedNow(); }; feeding.DropDownItems.Add(food);
        string hungerStatus=!settings.FeedingEnabled?"自动进食已关":Brain.IsFeeding?"正在找饭或进食"
            :Brain.HungerSecondsRemaining<=0?"已经饿了"
            :$"约 {TimeSpan.FromSeconds(Math.Ceiling(Brain.HungerSecondsRemaining)):m\\:ss} 后会饿";
        feeding.DropDownItems.Add(new ToolStripSeparator());
        feeding.DropDownItems.Add(new ToolStripMenuItem(hungerStatus) { Enabled=false });
        feeding.DropDownItems.Add(new ToolStripMenuItem(bowlStatus) { Enabled=false });
        feeding.DropDownItems.Add(new ToolStripMenuItem(bowl.HasValue?$"回收站：{(foodCount.HasValue?foodCount.Value==0?"空的":"有饭":"检测中")}":"未找到回收站") { Enabled=false });
        feeding.DropDownItems.Add(new ToolStripSeparator());
        feeding.DropDownItems.Add("这里设为饭盆",null,(_,_)=>
        { settings.BowlX=position.X+animator.Mouth.X; settings.BowlY=position.Y+animator.Mouth.Y; RefreshIcon(); SaveSettings(); });
        feeding.DropDownItems.Add("寻找回收站",null,(_,_)=>FindRecycleBinNow());

        var sound=Group("声音","sound");
        Toggle(sound,"静音",settings.Muted,v=>settings.Muted=v);
        Toggle(sound,"睡觉打呼噜",settings.SnoreEnabled,v=>settings.SnoreEnabled=v);
        sound.DropDownItems.Add("音量…",null,(_,_)=>ShowSettings());

        var display=Group("显示","display");
        Toggle(display,"置顶",settings.AlwaysOnTop,v=>settings.AlwaysOnTop=v);
        var size=new ToolStripMenuItem($"大小 · {(int)(settings.Scale*100)}%") { Name="size" };
        var slider=new TrackBar { Minimum=40,Maximum=160,Value=(int)(settings.Scale*100),TickFrequency=20,Size=new(250,50) };
        slider.ValueChanged+=(_,_)=> { settings.Scale=slider.Value/100f; size.Text=$"大小 · {slider.Value}%"; ApplySettings(false); };
        size.DropDownItems.Add(new ToolStripControlHost(slider) { AutoSize=false,Size=new(260,55) }); display.DropDownItems.Add(size);
        display.DropDownItems.Add("移回当前屏幕",null,(_,_)=>
        { activeWork=Screen.FromPoint(mouse.Position).WorkingArea; position=new(activeWork.Right-480*scale,activeWork.Bottom-340*scale); forceRender=true; });
        var startup=new ToolStripMenuItem("开机自启") { Name="startup",Enabled=!diagnostic && StartupRegistration.Current.Available };
        try { startup.Checked=StartupRegistration.Current.Enabled; }
        catch(Exception e) when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { startup.Enabled=false; startup.ToolTipText=e.Message; }
        startup.Click+=(_,_)=>
        {
            try { StartupRegistration.Current.SetEnabled(!startup.Checked); startup.Checked=StartupRegistration.Current.Enabled; }
            catch(Exception e) when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
            { tray.ShowBalloonTip(4000,"开机自启未更改",e.Message,ToolTipIcon.Warning); }
        };
        display.DropDownItems.Add(startup);

        var sleep=Group("睡眠","sleep");
        foreach(double seconds in new[] { 20d,35,60,120 })
        {
            var item=new ToolStripMenuItem($"{seconds} 秒") { Checked=!settings.RandomSleep && settings.IdleSeconds==seconds };
            item.Click+=(_,_)=> { settings.RandomSleep=false; settings.IdleSeconds=seconds; ApplySettings(); }; sleep.DropDownItems.Add(item);
        }
        var random=new ToolStripMenuItem($"随机 · {settings.SleepMinSeconds}～{settings.SleepMaxSeconds} 秒") { Checked=settings.RandomSleep };
        random.Click+=(_,_)=> { settings.RandomSleep=true; ApplySettings(); }; sleep.DropDownItems.Add(random);
        sleep.DropDownItems.Add("自定义…",null,(_,_)=>ShowSettings("behavior"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("全部设置…",null,(_,_)=>ShowSettings());
        menu.Items.Add("退出",null,(_,_)=>Close());
    }
    private ToolStripMenuItem Group(string title,string id)
    {
        var item=new ToolStripMenuItem(title) { Name=id }; menu.Items.Add(item); return item;
    }
    private void Toggle(ToolStripMenuItem parent,string title,bool value,Action<bool> set,string? id=null)
    {
        var item=new ToolStripMenuItem(title) { Name=id??"",Checked=value };
        item.Click+=(_,_)=> { item.Checked=!item.Checked; set(item.Checked); ApplySettings(); }; parent.DropDownItems.Add(item);
    }
    private ToolStripMenuItem MenuItem(string id)=>(ToolStripMenuItem)menu.Items.Find(id,true).Single();
    private void ShowSettings(string? tab=null)
    {
        StopCapture();
        if(settingsWindow!=null) { settingsWindow.Activate(); return; }
        settingsWindow=new(settings,()=>ApplySettings(false),tab,!diagnostic);
        settingsWindow.FormClosed+=(_,_)=> { settingsWindow=null; audio.Paused=menuOpen; SaveSettings(); };
        audio.Paused=true; settingsWindow.Show();
    }
    private void ApplySettings(bool save=true)
    {
        StopCapture();
        if(lastNeutered!=settings.Neutered)
        {
            if(settings.Neutered) Brain.Neuter(); else Brain.RestoreBell();
            lastNeutered=settings.Neutered;
        }
        TopMost=settings.AlwaysOnTop;
        if(IsHandleCreated)
        {
            long style=Native.GetWindowLongPtr(Handle,-20).ToInt64();
            Native.SetWindowLongPtr(Handle,-20,new IntPtr(style&~0x20L));
        }
        float old=scale; scale=settings.Scale*DeviceDpi/96f;
        position.X+=(old-scale)*250; position.Y+=(old-scale)*318;
        audio.Muted=diagnostic||settings.Muted; audio.Volume=settings.Volume; audio.SnoreEnabled=settings.SnoreEnabled;
        var o=Brain.Options;
        o.GoodCat=settings.GoodCat; o.Neutered=settings.Neutered; o.MouseStealEnabled=settings.MouseStealEnabled;
        o.RandomSleep=settings.RandomSleep; o.SleepMinSeconds=settings.SleepMinSeconds; o.SleepMaxSeconds=settings.SleepMaxSeconds;
        o.IdleSeconds=settings.IdleSeconds; o.ClickThreshold=settings.ClickThreshold; o.FeedingEnabled=settings.FeedingEnabled; o.HungerSeconds=settings.HungerSeconds;
        Brain.ApplyOptions(); forceRender=true;
        if(save) SaveSettings();
    }
    private void SaveSettings()
    {
        if(diagnostic) return;
        settings.X=position.X; settings.Y=position.Y;
        try { settings.Save(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { tray.ShowBalloonTip(3000,"设置未保存",e.Message,ToolTipIcon.Warning); }
    }
    public static string NameOf(PetState state)=>state switch
    {
        PetState.Sleep=>"蜷缩睡觉",PetState.Wake=>"醒来",PetState.Wander=>"自己溜达",PetState.Contempt=>"蔑视",PetState.SitContempt=>"坐立蔑视",
        PetState.CalmSit=>"安静坐着",PetState.Groom=>"自己舔毛",PetState.Observe=>"观察鼠标",PetState.Approach=>"凑近鼠标",PetState.Chew=>"空嚼低吼",
        PetState.Hiss=>"炸毛哈气",PetState.SitHiss=>"坐立哈气",PetState.Scared=>"惊恐弓背",PetState.PostHissChew=>"气得咂嘴",PetState.Pounce=>"短扑",
        PetState.CursorRaid=>"叼走鼠标 · 空格 / Esc",PetState.Recover=>"缓一缓",PetState.LieDown=>"趴下",PetState.Rag=>"烂抹布",PetState.Curl=>"缩回去",
        PetState.HungryWalk=>"去回收站找饭",PetState.HungryWait=>"饭盆空了 · 等饭",PetState.Eat=>"进食",PetState.Sated=>"吃饱了",_=>state.ToString()
    };
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop(); capture.Dispose(); settingsWindow?.Close(); SaveSettings(); audio.Dispose();
        tray.Visible=false; tray.Dispose(); menu.Dispose(); timer.Dispose(); frame?.Dispose(); animator.Dispose(); assets.Dispose(); base.OnFormClosed(e); applicationIcon.Dispose();
    }
    protected override void Dispose(bool disposing) { if(disposing) capture?.Dispose(); base.Dispose(disposing); }

    private PetInput SmokeInput()
    {
        var near=new PetInput(130,130,0,true,0,false,180);
        if(smokeStage==0) return new(600,600,0,false,60);
        if(smokeStage==1) return Brain.State is PetState.Approach or PetState.Chew?new(20,20,0,false,0,false,180):near;
        if(smokeStage is >=5 and <=7)
        {
            bool food=smokeStage>=6;
            return new(650,650,0,false,0,false,600,food,20,20,0,true);
        }
        return new(650,650,0,false,smokeStage>=8?100:0);
    }
    private void NextSmoke(int stage,double now) { smokeStage=stage; smokeStageAt=now; }
    private void TickSmoke(double now)
    {
        double elapsed=now-smokeStageAt;
        if(smokeStage==0 && now>1 && frame!=null)
        {
            var style=Native.GetWindowLongPtr(Handle,-20).ToInt64();
            trace.Add($"CHECK layered={(style&0x80000)!=0}; noactivate={(style&0x08000000)!=0}; topmost={(style&8)!=0}; noTaskbar={!ShowInTaskbar}");
            trace.Add($"CHECK foregroundPreserved={Native.GetForegroundWindow()==initialForeground}");
            trace.Add($"CHECK transparentCornerPasses={Native.WindowFromPoint(new((int)position.X+1,(int)position.Y+1))!=Handle}");
            bool hit=false;
            for(int y=0;y<frame.Height&&!hit;y+=4) for(int x=0;x<frame.Width&&!hit;x+=4)
                if(frame.GetPixel(x,y).A>240) hit=Native.WindowFromPoint(new((int)position.X+x,(int)position.Y+y))==Handle;
            trace.Add($"CHECK opaqueCatReceivesMouse={hit}; shellIcon={bowl}; recycleCount={foodCount}");
            settings.BowlX=activeWork.Left+100; settings.BowlY=activeWork.Top+100; RefreshIcon();
            settings.FeedingEnabled=false; ApplySettings(); BuildMenu();
            trace.Add($"CHECK compactMenu={menu.Items.Count<=10}; rootItems={menu.Items.Count}; startupOption={MenuItem("startup").Text}; diagnosticStartupDisabled={!MenuItem("startup").Enabled}");
            trace.Add($"CHECK packagedIcon={tray.Icon==applicationIcon && Icon==applicationIcon}");
            var autoFood=MenuItem("auto-food");
            var manualFood=MenuItem("manual-food");
            trace.Add($"CHECK autoFeedToggleVisible={!autoFood.Checked}; manualFeedEnabledWhileAutoOff={manualFood.Enabled}");
            manualFood.PerformClick(); trace.Add($"CHECK manualFeedWorksWhileAutoOff={Brain.IsFeeding}");
            autoFood.PerformClick(); trace.Add($"CHECK autoFeedToggleApplied={Brain.Options.FeedingEnabled}");
            Brain.Rest(); FindRecycleBinNow();
            trace.Add($"CHECK autoFindClearsManual={!settings.BowlX.HasValue && !settings.BowlY.HasValue}; autoFindStartsImmediately={queryingIcon}");
            BuildMenu(); BuildMenu();
            var threshold=MenuItem("threshold");
            ((ToolStripMenuItem)threshold.DropDownItems[0]).PerformClick(); trace.Add($"CHECK threshold={Brain.Options.ClickThreshold}");
            ((ToolStripMenuItem)threshold.DropDownItems[1]).PerformClick();
            settings.AlwaysOnTop=false; ApplySettings(); trace.Add($"CHECK topmostOff={(Native.GetWindowLongPtr(Handle,-20).ToInt64()&8)==0}");
            settings.AlwaysOnTop=true; settings.SnoreEnabled=true; ApplySettings(); trace.Add($"CHECK sleepSound={audio.CurrentKey}");
            settings.SnoreEnabled=false; ApplySettings();
            trace.Add($"CHECK GDI_start={Native.GetGuiResources(Process.GetCurrentProcess().Handle,0)}");
            Brain.Wake(); NextSmoke(1,now);
        }
        else if(smokeStage==1 && Brain.State==PetState.Chew && Brain.Age>1.5)
        {
            Brain.Click(180); NextSmoke(2,now);
        }
        else if(smokeStage==2 && Brain.State==PetState.Recover)
        {
            trace.Add($"CHECK singleHissRecoveryPose={PetAnimator.SelectPose(Brain.State,0)}");
            Brain.SetIdlePose(true); Brain.Click(180);
            trace.Add($"CHECK seatedReaction={Brain.State}; audio={audio.CurrentKey}");
            Brain.Click(180); Brain.Click(180); NextSmoke(3,now);
        }
        else if(smokeStage==3 && Brain.State==PetState.PostHissChew && Brain.Age>.6)
        {
            trace.Add($"CHECK repeatedHissPose={animator.CurrentPose}; cursorFree={!capture.Active}");
            settings.MouseStealEnabled=true; ApplySettings(); Brain.SetIdlePose(true); Brain.Click(180); Brain.Click(180); Brain.Click(180); NextSmoke(4,now);
        }
        else if(smokeStage==4 && Brain.State==PetState.CursorRaid && Brain.Age>1.2)
        {
            trace.Add($"CHECK cursorRaidDryRun={capture.IsDryRun}; attached={capture.AttachedPoint}");
            capture.Request(CursorCommand.Neuter); NextSmoke(5,now);
        }
        else if(smokeStage==5 && elapsed>1.5 && settings.Neutered)
        {
            trace.Add($"CHECK neutered={settings.Neutered}; state={Brain.State}; released={!capture.Active}");
            Brain.Click(180); trace.Add($"CHECK neuteredClick={Brain.State}");
            settings.Neutered=false; lastNeutered=false; Brain.RestoreBell(); ApplySettings();
            settings.GoodCat=true; ApplySettings();
            var peacefulState=Brain.State;
            OnPetMouseDown(this,new(MouseButtons.Left,1,250,250,0)); EndDrag();
            trace.Add($"CHECK goodCatLeftDoesNotProvoke={Brain.State==peacefulState}; goodCatRightEnabled={(Native.GetWindowLongPtr(Handle,-20).ToInt64()&0x20)==0}; alphaStillPasses={Native.WindowFromPoint(new((int)position.X+1,(int)position.Y+1))!=Handle}");
            OnPetMouseDown(this,new(MouseButtons.Right,1,250,250,0));
            trace.Add($"CHECK goodCatRightMenu={menu.Visible && menuOpen}; menuPausesAudio={audio.Paused}; menuAutoClose={menu.AutoClose}");
            trace.Add($"CHECK menuFitsScreen={Screen.FromControl(menu).WorkingArea.Contains(menu.Bounds)}; menuSize={menu.Size}");
            SaveMenuPreview(menu,"menu-root.png");
            previousMenuButtons=0; DismissMenuOnOutsideClick(new(menu.Bounds.Left+5,menu.Bounds.Top+5),1);
            trace.Add($"CHECK insideMenuKeepsOpen={menu.Visible && menuOpen}");
            var displayMenu=MenuItem("display"); displayMenu.ShowDropDown();
            SaveMenuPreview(displayMenu.DropDown,"menu-display.png");
            var sizeMenu=MenuItem("size");
            sizeMenu.ShowDropDown();
            previousMenuButtons=0; DismissMenuOnOutsideClick(new(sizeMenu.DropDown.Bounds.Left+5,sizeMenu.DropDown.Bounds.Top+5),1);
            trace.Add($"CHECK insideSubmenuKeepsOpen={menu.Visible && menuOpen}");
            sizeMenu.HideDropDown(); displayMenu.HideDropDown();
            previousMenuButtons=0;
            DismissMenuOnOutsideClick(new(menu.Bounds.Right+100,menu.Bounds.Bottom+100),1);
            trace.Add($"CHECK outsideClickClosesMenu={!menu.Visible && !menuOpen}; outsideClickResumesAudio={!audio.Paused}");
            int menuGdi=Native.GetGuiResources(Process.GetCurrentProcess().Handle,0);
            for(int i=0;i<5;i++)
            {
                OnPetMouseDown(this,new(MouseButtons.Right,1,250,250,0));
                var repeatDisplay=MenuItem("display"); repeatDisplay.ShowDropDown();
                var repeatSize=MenuItem("size");
                repeatSize.ShowDropDown(); repeatSize.HideDropDown(); repeatDisplay.HideDropDown();
                menu.Close(ToolStripDropDownCloseReason.AppClicked);
            }
            trace.Add($"CHECK repeatedMenuGdiStable={Native.GetGuiResources(Process.GetCurrentProcess().Handle,0)==menuGdi}; menuGdi={menuGdi}->{Native.GetGuiResources(Process.GetCurrentProcess().Handle,0)}");
            settings.GoodCat=false; ApplySettings(); Brain.FeedNow();
            // stage 5 supplies an empty bowl; wait for HungryWait before adding food.
            if(Brain.State==PetState.HungryWait) NextSmoke(6,now);
        }
        if(smokeStage==5 && Brain.State==PetState.HungryWait && elapsed>3) NextSmoke(6,now);
        if(smokeStage==6 && Brain.State==PetState.Sated)
        { trace.Add($"CHECK satisfiedAudio={audio.CurrentKey}"); NextSmoke(7,now); }
        if(smokeStage==7 && elapsed>2) { settings.SnoreEnabled=true; ApplySettings(); Brain.Rest(); NextSmoke(8,now); }
        if(smokeStage==8 && Brain.State==PetState.Sleep)
        { trace.Add($"CHECK finalSleepSound={audio.CurrentKey}"); NextSmoke(9,now); }
    }
    private void SaveMenuPreview(ToolStripDropDown dropdown,string filename)
    {
        Directory.CreateDirectory(smokeOutput!);
        using var image=new Bitmap(dropdown.Width,dropdown.Height);
        dropdown.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
        image.Save(Path.Combine(smokeOutput!,filename));
    }
    private void FinishSmoke()
    {
        timer.Stop(); capture.Release();
        trace.Add($"CHECK GDI_end={Native.GetGuiResources(Process.GetCurrentProcess().Handle,0)}; audio={audio.Warning??"available"}; final={Brain.State}; finished={smokeStage==9}");
        using(var p=Process.GetCurrentProcess()) trace.Add($"CHECK workingSetMB={p.WorkingSet64/1048576d:F1}; cpuSeconds={p.TotalProcessorTime.TotalSeconds:F2}");
        Directory.CreateDirectory(smokeOutput!); File.WriteAllLines(Path.Combine(smokeOutput!,"window-smoke.txt"),trace);
        frame?.Save(Path.Combine(smokeOutput!,"last-frame.png")); Close();
    }
}
