using System.Diagnostics;

namespace LevelUp.NavTableUpdater.Core.Platform;

public static class XPlaneProcessDetector
{
    public static bool IsXPlaneRunning()
    {
        try
        {
            return Process.GetProcesses().Any(process =>
            {
                try
                {
                    return process.ProcessName.Contains("X-Plane", StringComparison.OrdinalIgnoreCase);
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
        }
        catch
        {
            return false;
        }
    }
}
