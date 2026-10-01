using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Hajimi.Platform;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int X, Y; public Size(int x, int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential, Pack=1)] internal struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point position, ref Size size, IntPtr source, ref Point origin, uint color, ref Blend blend, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] internal static extern int GetGuiResources(IntPtr process, int flag);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    internal static double IdleSeconds()
    {
        var value = new LastInput { Size=(uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref value) ? unchecked((uint)Environment.TickCount-value.Time)/1000d : 0;
    }
    internal static void Present(IntPtr hwnd, Bitmap bitmap, int x, int y)
    {
        var screen=GetDC(IntPtr.Zero); var source=CreateCompatibleDC(screen);
        IntPtr hBitmap=IntPtr.Zero, previous=IntPtr.Zero;
        try
        {
            hBitmap=bitmap.GetHbitmap(Color.FromArgb(0)); previous=SelectObject(source,hBitmap);
            var location=new Point(x,y); var size=new Size(bitmap.Width,bitmap.Height); var origin=new Point(0,0);
            var blend=new Blend { Alpha=255, Format=1 };
            if (!UpdateLayeredWindow(hwnd,screen,ref location,ref size,source,ref origin,0,ref blend,2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (previous!=IntPtr.Zero) SelectObject(source,previous);
            if (hBitmap!=IntPtr.Zero) DeleteObject(hBitmap);
            DeleteDC(source); ReleaseDC(IntPtr.Zero,screen);
        }
    }
}
