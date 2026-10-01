using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace Hajimi.Platform;

public static class RecycleBin
{
    public sealed record IconLocation(PointF? Position,string Status,string Advice,string? DisplayName=null);
    [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Size; public long Bytes,Items; }
    private delegate bool EnumWindow(IntPtr hwnd,IntPtr parameter);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root,ref Info info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? cls,string? text);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent,EnumWindow callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHParseDisplayName(string name,IntPtr bind,out IntPtr pidl,uint attributes,out uint returnedAttributes);
    [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl,uint nameType,out IntPtr name);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible accessible,int start,int count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=2)] object[] children,out int obtained);

    public static long? QueryCount()
    {
        var info=new Info { Size=(uint)Marshal.SizeOf<Info>() };
        return SHQueryRecycleBin(null,ref info)==0?info.Items:null;
    }
    public static PointF? FindDesktopIcon()=>LocateDesktopIcon().Position;
    public static IconLocation LocateDesktopIcon()
    {
        IntPtr list=IntPtr.Zero;
        EnumWindows((window,_)=>
        {
            // Limit the search to desktop hosts; an Explorer folder window also has a DefView.
            var className=new StringBuilder(128); GetClassName(window,className,className.Capacity);
            if(className.ToString() is not ("Progman" or "WorkerW")) return true;
            EnumChildWindows(window,(child,_)=>
            {
                var name=new StringBuilder(128); GetClassName(child,name,name.Capacity);
                if(name.ToString()=="SHELLDLL_DefView") list=FindWindowEx(child,IntPtr.Zero,"SysListView32",null);
                return list==IntPtr.Zero;
            },IntPtr.Zero);
            return list==IntPtr.Zero;
        },IntPtr.Zero);
        if(list==IntPtr.Zero) return new(null,"未找到桌面图标窗口","请先显示 Windows 桌面并重新寻找；仍失败时可把猫拖到回收站旁，选择“把当前位置设为饭盆”。");
        if(!IsWindowVisible(list)) return new(null,"桌面图标已隐藏","右键桌面 → 查看 → 勾选“显示桌面图标”，然后重新自动寻找；也可手动设置饭盆。");
        string? displayName=RecycleBinDisplayName();
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "回收站","Recycle Bin","資源回收筒","垃圾桶" };
        if(!string.IsNullOrWhiteSpace(displayName)) names.Add(displayName.Trim());
        var iid=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        IAccessible? accessible=null;
        object[] children=Array.Empty<object>();
        try
        {
            if(AccessibleObjectFromWindow(list,0xFFFFFFFC,ref iid,out accessible)!=0 || accessible==null)
                return new(null,"读取桌面图标失败","请重新显示桌面后再试；若使用第三方桌面整理软件，可将猫拖到回收站旁手动设置饭盆。",displayName);
            int count=Math.Min(accessible.accChildCount,2048);
            children=new object[count];
            if(count==0 || AccessibleChildren(accessible,0,count,children,out int obtained)<0)
                return new(null,"桌面未显示可读取的图标","右键桌面 → 查看 → 显示桌面图标；若回收站仍未出现，请在“个性化 → 主题 → 桌面图标设置”中勾选回收站。",displayName);
            for(int i=0;i<obtained;i++)
            {
                object child=children[i];
                string? name=child is IAccessible item?item.get_accName(0):accessible.get_accName(child);
                if(name==null || !names.Contains(name.Trim())) continue;
                var target=child as IAccessible??accessible;
                target.accLocation(out int x,out int y,out int w,out int h,child is IAccessible?0:child);
                if(w<=0 || h<=0) continue;
                var point=new PointF(x+w/2f,y+h/2f);
                return new(point,$"位置已识别：({point.X:F0}, {point.Y:F0})","",name);
            }
            return new(null,"桌面未找到回收站图标","请在“设置 → 个性化 → 主题 → 桌面图标设置”中勾选回收站，再重新寻找；也可把猫拖到回收站旁手动设置饭盆。",displayName);
        }
        catch(COMException) { return new(null,"读取桌面图标失败","请重新显示桌面后再试；也可把猫拖到回收站旁，选择“把当前位置设为饭盆”。",displayName); }
        finally
        {
            foreach(var child in children.OfType<IAccessible>())
                if(!ReferenceEquals(child,accessible) && Marshal.IsComObject(child)) Marshal.ReleaseComObject(child);
            if(accessible!=null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
        }
    }
    private static string? RecycleBinDisplayName()
    {
        IntPtr pidl=IntPtr.Zero,name=IntPtr.Zero;
        try
        {
            // Shell returns the localized display label, including a user-renamed Recycle Bin.
            if(SHParseDisplayName("::{645FF040-5081-101B-9F08-00AA002F954E}",IntPtr.Zero,out pidl,0,out _)!=0) return null;
            return SHGetNameFromIDList(pidl,0,out name)==0?Marshal.PtrToStringUni(name):null;
        }
        finally { if(name!=IntPtr.Zero) Marshal.FreeCoTaskMem(name); if(pidl!=IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }
}
