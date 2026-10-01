namespace Hajimi.Platform;

public static class PetIcon
{
    public static Icon Load()
    {
        using var stream=typeof(PetIcon).Assembly.GetManifestResourceStream("Hajimi.ApplicationIcon")
            ?? throw new FileNotFoundException("内置图标缺失。");
        using var icon=new Icon(stream);
        return (Icon)icon.Clone();
    }
}
