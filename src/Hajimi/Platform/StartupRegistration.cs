using Microsoft.Win32;

namespace Hajimi.Platform;

public interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

// Only this user's Run entry is touched. The registry is the source of truth; saving
// ordinary pet preferences never enables startup or recreates a removed entry.
public sealed class StartupRegistration(IStartupStore store,string executable)
{
    public static StartupRegistration Current { get; }=new(new WindowsStartupStore(),Environment.ProcessPath??"");
    public bool Available=>Path.IsPathFullyQualified(executable) && executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(executable).Equals("dotnet.exe",StringComparison.OrdinalIgnoreCase);
    public bool Enabled=>!string.IsNullOrWhiteSpace(store.Read());
    public static string CommandFor(string executable)
    {
        if(!Path.IsPathFullyQualified(executable) || executable.Contains('"') || executable.IndexOfAny(['\r','\n'])>=0)
            throw new ArgumentException("自启程序必须使用完整 EXE 路径。",nameof(executable));
        return $"\"{executable}\"";
    }
    public void SetEnabled(bool enabled)
    {
        if(enabled)
        {
            if(!Available || !File.Exists(executable)) throw new InvalidOperationException("请从打包后的 EXE 设置开机自启。");
            store.Write(CommandFor(executable));
        }
        else store.Remove();
    }
    public void RefreshEnabledPath()
    {
        if(!Available) return;
        string? command=store.Read();
        if(!string.IsNullOrWhiteSpace(command) && command!=CommandFor(executable)) store.Write(CommandFor(executable));
    }
}

public sealed class WindowsStartupStore : IStartupStore
{
    public const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName="Hajimi.DesktopPet";
    public string? Read()
    {
        using var key=Registry.CurrentUser.OpenSubKey(KeyPath,false);
        return key?.GetValue(ValueName) as string;
    }
    public void Write(string command)
    {
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath,true);
        key.SetValue(ValueName,command,RegistryValueKind.String);
    }
    public void Remove()
    {
        using var key=Registry.CurrentUser.OpenSubKey(KeyPath,true);
        key?.DeleteValue(ValueName,false);
    }
}
