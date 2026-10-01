namespace Hajimi.Platform;

public sealed class SettingsWindow : Form
{
    private readonly Settings settings;
    private readonly Action changed;
    private readonly Label sizeLabel=new() { AutoSize=true };
    private readonly Icon applicationIcon=PetIcon.Load();
    public SettingsWindow(Settings settings,Action changed,string? select=null,bool allowStartup=true)
    {
        this.settings=settings; this.changed=changed;
        Text="哈基米设置"; ClientSize=new(510,530); MinimumSize=new(500,560);
        Icon=applicationIcon;
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Microsoft YaHei UI",9);
        var tabs=new TabControl { Dock=DockStyle.Fill };
        var general=Page(tabs,"外观与声音"); var behavior=Page(tabs,"行为与睡眠");
        Add(general,"猫的大小",SizeControls());
        Check(general,"始终显示在普通窗口上方",settings.AlwaysOnTop,v=>settings.AlwaysOnTop=v);
        StartupCheckbox(general,allowStartup);
        Check(general,"静音",settings.Muted,v=>settings.Muted=v);
        Check(general,"睡觉时打呼噜",settings.SnoreEnabled,v=>settings.SnoreEnabled=v);
        var volume=new TrackBar { Minimum=0,Maximum=100,Value=(int)(settings.Volume*100),TickFrequency=10,Width=270,Height=45 };
        volume.ValueChanged+=(_,_)=> { settings.Volume=volume.Value/100f; changed(); };
        Add(general,"音量",volume);
        Note(general,"坐立哈气：哈气 1\n炸毛哈气：哈气 1～3 随机\n惊恐弓背：老吴\n等饭：喵喵叫；吃饱：舒服的低鸣");
        Check(behavior,"好猫模式：自己玩，不追鼠标、不挑衅",settings.GoodCat,v=>settings.GoodCat=v);
        Note(behavior,"仍可右键猫打开菜单，按住左键拖动位置。\n猫周围的透明区域允许鼠标穿透。");
        Check(behavior,"嘎蛋状态：温和，不哈气、不蔑视",settings.Neutered,v=>settings.Neutered=v);
        Check(behavior,"允许连点后叼走鼠标",settings.MouseStealEnabled,v=>settings.MouseStealEnabled=v);
        Note(behavior,"叼鼠标时：空格 → 嘎蛋并趴成抹布；Esc → 释放鼠标。\n每次最多跑 8 秒。嘎蛋后右键可以装回铃铛。");
        Add(behavior,"连续挑衅阈值",Number(settings.ClickThreshold,2,8,v=>settings.ClickThreshold=(int)v,"次"));
        Check(behavior,"随机选择睡前等待时间",settings.RandomSleep,v=>settings.RandomSleep=v);
        Add(behavior,"固定等待时间",Number((decimal)settings.IdleSeconds,5,3600,v=>settings.IdleSeconds=(double)v,"秒"));
        Add(behavior,"随机最短等待",Number((decimal)settings.SleepMinSeconds,5,3600,v=>
        { settings.SleepMinSeconds=(double)v; settings.SleepMaxSeconds=Math.Max(settings.SleepMaxSeconds,settings.SleepMinSeconds); },"秒"));
        Add(behavior,"随机最长等待",Number((decimal)settings.SleepMaxSeconds,5,3600,v=>
        { settings.SleepMaxSeconds=Math.Max(settings.SleepMinSeconds,(double)v); },"秒"));
        Check(behavior,"自动进食：饿了自己去回收站找饭",settings.FeedingEnabled,v=>settings.FeedingEnabled=v);
        Add(behavior,"吃饱后多久再饿",Number((decimal)(settings.HungerSeconds/60),.5m,60,v=>settings.HungerSeconds=(double)v*60,"分钟",1));
        Note(behavior,"关闭自动进食仍可右键手动叫它找饭。\n回收站为空时在旁边等饭，检测到内容后进食。\n进食只表现动画，回收站里的文件会保留。");
        var footer=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=47,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(10,5,10,5) };
        var close=new Button { Text="完成",Size=new(90,30) };
        close.Click+=(_,_)=>Close(); footer.Controls.Add(close);
        Controls.Add(tabs); Controls.Add(footer);
        if(select=="behavior") tabs.SelectedIndex=1;
    }
    private void StartupCheckbox(FlowLayoutPanel flow,bool allowed)
    {
        var startup=new CheckBox { Text="开机自启动",AutoSize=true,Enabled=allowed && StartupRegistration.Current.Available,
            Margin=new Padding(0,6,0,7) };
        try { startup.Checked=StartupRegistration.Current.Enabled; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { startup.Enabled=false; }
        bool restoring=false;
        startup.CheckedChanged+=(_,_)=>
        {
            if(restoring) return;
            bool requested=startup.Checked;
            try { StartupRegistration.Current.SetEnabled(startup.Checked); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            {
                restoring=true;
                try { startup.Checked=StartupRegistration.Current.Enabled; }
                catch(Exception readError) when(readError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { startup.Checked=!requested; startup.Enabled=false; }
                finally { restoring=false; }
                MessageBox.Show(this,e.Message,"开机自启未更改",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        flow.Controls.Add(startup);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if(disposing) applicationIcon.Dispose();
    }
    private static FlowLayoutPanel Page(TabControl tabs,string title)
    {
        var page=new TabPage(title);
        var flow=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(14) };
        page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
    }
    private Control SizeControls()
    {
        var panel=new FlowLayoutPanel { Width=325,Height=68,FlowDirection=FlowDirection.TopDown,WrapContents=false };
        var slider=new TrackBar { Minimum=40,Maximum=160,Value=(int)(settings.Scale*100),TickFrequency=20,Width=300,Height=42 };
        sizeLabel.Text=$"{slider.Value}%";
        slider.ValueChanged+=(_,_)=> { settings.Scale=slider.Value/100f; sizeLabel.Text=$"{slider.Value}%"; changed(); };
        panel.Controls.Add(slider); panel.Controls.Add(sizeLabel); return panel;
    }
    private void Check(FlowLayoutPanel flow,string title,bool value,Action<bool> set)
    {
        var check=new CheckBox { Text=title,Checked=value,AutoSize=true,Margin=new Padding(0,6,0,7) };
        check.CheckedChanged+=(_,_)=> { set(check.Checked); changed(); }; flow.Controls.Add(check);
    }
    private Control Number(decimal value,decimal min,decimal max,Action<decimal> set,string unit,int decimals=0)
    {
        var row=new FlowLayoutPanel { Width=310,Height=34 };
        var number=new NumericUpDown { Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),DecimalPlaces=decimals,Increment=decimals==0?1:.5m,Width=145 };
        number.ValueChanged+=(_,_)=> { set(number.Value); changed(); };
        row.Controls.Add(number); row.Controls.Add(new Label { Text=unit,AutoSize=true,Margin=new Padding(9,5,0,0) }); return row;
    }
    private static void Add(FlowLayoutPanel flow,string title,Control control)
    {
        var row=new FlowLayoutPanel { Width=450,Height=Math.Max(40,control.Height+5),WrapContents=false };
        row.Controls.Add(new Label { Text=title,Width=118,Height=32,TextAlign=ContentAlignment.MiddleLeft });
        row.Controls.Add(control); flow.Controls.Add(row);
    }
    private static void Note(FlowLayoutPanel flow,string text)=>flow.Controls.Add(new Label
    { Text=text,AutoSize=true,MaximumSize=new(450,0),ForeColor=Color.DimGray,Margin=new Padding(0,6,0,10) });
}
