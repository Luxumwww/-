using Hajimi.Audio;
using Hajimi.Diagnostics;
using Hajimi.Platform;

namespace Hajimi;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string assets=Path.Combine(AppContext.BaseDirectory,"assets");
        try
        {
            if(args.Length>=2 && args[0]=="--make-audio") { WaveClip.CreateDemoSounds(Path.GetFullPath(args[1])); return 0; }
            if(args.Length>=2 && args[0]=="--render-preview") { Preview.Create(assets,Path.GetFullPath(args[1])); return 0; }
            if(args.Length>=2 && args[0]=="--probe-input") { InputProbe.Run(Path.GetFullPath(args[1])); return 0; }
            string? smoke=args.Length>=2 && args[0]=="--smoke-test"?Path.GetFullPath(args[1]):null;
            using var mutex=new Mutex(true,smoke==null?"Local\\Hajimi.DesktopPet":"Local\\Hajimi.DesktopPet.Smoke",out bool first);
            if(!first) { MessageBox.Show("哈基米已经在运行。右键猫或任务栏托盘图标可以设置、退出。","哈基米"); return 0; }
            if(smoke==null)
            {
                try { StartupRegistration.Current.RefreshEnabledPath(); }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { System.Diagnostics.Debug.WriteLine($"自启路径未更新：{e.Message}"); }
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using var pet=new PetWindow(smoke==null?Settings.Load():new Settings(),assets,smoke);
            Application.Run(pet);
            return 0;
        }
        catch(Exception e)
        {
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(Path.Combine(Settings.Folder,"error.log"),e.ToString());
            if(args.Length==0) MessageBox.Show($"启动失败：{e.Message}\n\n详细信息：{Settings.Folder}\\error.log","哈基米",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
