using System.Runtime.InteropServices;

namespace Hajimi.Interaction;

public enum CursorCommand { None,Neuter,Escape,Timeout }

// Installed only during the opt-in gag. Space/Esc are reserved only while the pointer is attached.
public sealed class CursorCapture : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X,Y; }
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32.dll",EntryPoint="ClipCursor")] private static extern bool ReleaseClip(IntPtr rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Pt pt);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    private readonly bool dryRun;
    private readonly object sync=new();
    private readonly Hook callback;
    private IntPtr hook;
    private Rect originalClip;
    private Pt originalPosition;
    private System.Threading.Timer? watchdog;
    private int command;
    private volatile bool active;
    public bool Active=>active;
    public Point AttachedPoint { get; private set; }
    public string? Warning { get; private set; }
    public bool IsDryRun=>dryRun;
    public CursorCapture(bool dryRun=false) { this.dryRun=dryRun; callback=Keyboard; AppDomain.CurrentDomain.ProcessExit+=OnExit; }
    public bool Start()
    {
        lock(sync)
        {
            if(Active) return true;
            command=0;
            if(!dryRun)
            {
                if(!GetClipCursor(out originalClip) || !GetCursorPos(out originalPosition)) { Warning="不能读取鼠标状态。"; return false; }
                hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
                if(hook==IntPtr.Zero) { Warning="快捷键安装失败，已放弃叼鼠标。"; return false; }
            }
            active=true;
            watchdog=new System.Threading.Timer(_=> { Request(CursorCommand.Timeout); Release(); },null,10000,Timeout.Infinite);
            return true;
        }
    }
    public void Update(Point mouth)
    {
        lock(sync)
        {
            if(!Active) return;
            AttachedPoint=mouth;
            if(dryRun) return;
            var rect=new Rect { Left=mouth.X,Top=mouth.Y,Right=mouth.X+1,Bottom=mouth.Y+1 };
            if(!ClipCursor(ref rect) || !SetCursorPos(mouth.X,mouth.Y)) { Request(CursorCommand.Escape); Release(); }
        }
    }
    public void Request(CursorCommand value)=>Interlocked.Exchange(ref command,(int)value);
    public CursorCommand ConsumeCommand()=>(CursorCommand)Interlocked.Exchange(ref command,0);
    private IntPtr Keyboard(int code,IntPtr message,IntPtr data)
    {
        if(code>=0 && Active)
        {
            int key=Marshal.ReadInt32(data);
            if(key is 32 or 27)
            {
                if(message==(IntPtr)0x100 || message==(IntPtr)0x104) Request(key==32?CursorCommand.Neuter:CursorCommand.Escape);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Release()
    {
        lock(sync)
        {
            if(!Active) return;
            active=false; watchdog?.Dispose(); watchdog=null;
            if(!dryRun)
            {
                if(!ClipCursor(ref originalClip)) ReleaseClip(IntPtr.Zero);
                SetCursorPos(originalPosition.X,originalPosition.Y);
                if(hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
            }
        }
    }
    private void OnExit(object? sender,EventArgs args)=>Release();
    public static Rectangle CurrentConstraint()
    {
        if(!GetClipCursor(out var r)) throw new InvalidOperationException("GetClipCursor failed.");
        return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
    }
    public void Dispose() { Release(); AppDomain.CurrentDomain.ProcessExit-=OnExit; }
}
