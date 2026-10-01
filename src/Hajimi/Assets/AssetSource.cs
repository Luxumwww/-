namespace Hajimi.Assets;

// Development may override packaged assets with files. Published builds carry all assets
// inside the assembly, so the EXE does not depend on its working directory or sidecar files.
public sealed class AssetSource(string root)
{
    private static readonly System.Reflection.Assembly assembly=typeof(AssetSource).Assembly;
    private static readonly Dictionary<string,string> resources=assembly.GetManifestResourceNames()
        .Where(name=>name.StartsWith("PetAsset/",StringComparison.Ordinal))
        .ToDictionary(name=>name[9..].Replace('\\','/'),StringComparer.OrdinalIgnoreCase);

    public bool Exists(string relative)=>File.Exists(Path.Combine(root,relative)) || resources.ContainsKey(Normalize(relative));

    public Stream OpenRead(string relative)
    {
        string file=Path.Combine(root,relative);
        if(File.Exists(file)) return File.OpenRead(file);
        if(resources.TryGetValue(Normalize(relative),out string? resource))
            return assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException("内置素材不可读。",relative);
        throw new FileNotFoundException("缺少角色素材。",relative);
    }

    private static string Normalize(string relative)=>relative.Replace('\\','/');
}
