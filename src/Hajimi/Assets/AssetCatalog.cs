using System.Drawing.Imaging;
using System.Text.Json;

namespace Hajimi.Assets;

public sealed class CharacterDefinition
{
    public string Name { get; set; } = "哈基米";
    public int CanvasWidth { get; set; } = 500;
    public int CanvasHeight { get; set; } = 340;
    public Dictionary<string, PoseDefinition> Poses { get; set; } = new();
}
public sealed class PoseDefinition
{
    public string File { get; set; } = "";
    public float Width { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int Cell { get; set; }
    public string? Fallback { get; set; }
    public int[]? Source { get; set; }
    public bool FacesLeft { get; set; }
    // Animation frames share a fixed canvas; trimming them separately would stretch each pose.
    public bool Trim { get; set; } = true;
}
public sealed record Pose(Bitmap Image, float Width,bool FacesLeft=false);

public sealed class AssetCatalog : IDisposable
{
    private readonly Dictionary<string, Pose> poses = new();
    public CharacterDefinition Definition { get; }
    public List<string> Warnings { get; } = new();
    public AssetCatalog(string root)
    {
        var files=new AssetSource(root);
        using var manifest=files.OpenRead("character.json");
        Definition = JsonSerializer.Deserialize<CharacterDefinition>(manifest,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("角色配置为空。");
        foreach (var (key, spec) in Definition.Poses)
        {
            var path = Path.Combine(root, spec.File);
            if (!files.Exists(spec.File))
            {
                if (spec.Fallback != null) { Warnings.Add($"{key} 缺失，将使用 {spec.Fallback}"); continue; }
                throw new FileNotFoundException($"缺少关键角色素材：{spec.File}", path);
            }
            using var imageStream=files.OpenRead(spec.File);
            using var source = new Bitmap(imageStream);
            int w = source.Width / spec.Columns, h = source.Height / spec.Rows;
            var region = spec.Source is { Length: 4 } r ? new Rectangle(r[0],r[1],r[2],r[3]) : new Rectangle(spec.Cell % spec.Columns * w,spec.Cell / spec.Columns * h,w,h);
            using var cell = source.Clone(region, PixelFormat.Format32bppArgb);
            // Always check for visible pixels. Preserve the full cell for fixed-canvas sequences.
            var visible = AlphaBounds(cell);
            var bounds = spec.Trim ? visible : new Rectangle(0,0,cell.Width,cell.Height);
            using var trimmed = cell.Clone(bounds, PixelFormat.Format32bppArgb);
            var bitmap = new Bitmap((int)Math.Ceiling(spec.Width * 1.7), (int)Math.Ceiling(spec.Width * 1.7 * bounds.Height / bounds.Width), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(trimmed, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            poses[key] = new(bitmap,spec.Width,spec.FacesLeft);
        }
        foreach (var (key, spec) in Definition.Poses)
            if (!poses.ContainsKey(key)) poses[key] = poses[spec.Fallback!];
    }
    public Pose this[string key] => poses[key];
    public IEnumerable<string> Keys => poses.Keys;
    private static Rectangle AlphaBounds(Bitmap image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).A > 80)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left) throw new InvalidDataException("角色图像没有可见像素。");
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
    public void Dispose() { foreach (var bitmap in poses.Values.Select(x => x.Image).Distinct()) bitmap.Dispose(); }
}
