using System.Text.Json;
using Hajimi.Interaction;
using Hajimi.Platform;

namespace Hajimi.Diagnostics;

public static class InputProbe
{
    // Briefly attach at the current cursor pixel and restore in finally; no cursor route is run.
    public static void Run(string output)
    {
        var before=CursorCapture.CurrentConstraint(); Native.GetCursorPos(out var point);
        using var capture=new CursorCapture();
        bool started=false,confined=false;
        try
        {
            started=capture.Start();
            if(started)
            {
                capture.Update(new(point.X,point.Y));
                confined=CursorCapture.CurrentConstraint()==new Rectangle(point.X,point.Y,1,1);
            }
        }
        finally { capture.Release(); }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"input-probe.json"),JsonSerializer.Serialize(new
        { hookInstalled=started,cursorAttached=confined,constraintRestored=CursorCapture.CurrentConstraint()==before,capture.Warning },new JsonSerializerOptions { WriteIndented=true }));
        if(!started || !confined || CursorCapture.CurrentConstraint()!=before) throw new InvalidOperationException("Native input probe failed.");
    }
}
