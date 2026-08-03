namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class AtomicFileMutation
{
    public static void Write(string targetPath, byte[] bytes)
    {
        var existed = File.Exists(targetPath);
        var attributes = existed ? File.GetAttributes(targetPath) : (FileAttributes?)null;
        var unixMode = existed ? TryGetUnixFileMode(targetPath) : null;
        var parent = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Patch target has no parent directory.");
        Directory.CreateDirectory(parent);

        var tempPath = targetPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, targetPath, overwrite: true);
            if (attributes is not null)
            {
                File.SetAttributes(targetPath, attributes.Value);
            }

            TrySetUnixFileMode(targetPath, unixMode);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static UnixFileMode? TryGetUnixFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

#pragma warning disable CA1416
        return File.GetUnixFileMode(path);
#pragma warning restore CA1416
    }

    private static void TrySetUnixFileMode(string path, UnixFileMode? mode)
    {
        if (mode is null || OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416
        File.SetUnixFileMode(path, mode.Value);
#pragma warning restore CA1416
    }
}
