namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class RestoreFileTransaction
{
    public static void Restore(string sourcePath, string backupPath, string preRestoreBackupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file is missing.", backupPath);
        }

        var attributes = File.Exists(sourcePath) ? File.GetAttributes(sourcePath) : File.GetAttributes(backupPath);
        var unixMode = TryGetUnixFileMode(File.Exists(sourcePath) ? sourcePath : backupPath);

        Directory.CreateDirectory(Path.GetDirectoryName(preRestoreBackupPath)
            ?? throw new InvalidOperationException("Pre-restore backup path has no parent directory."));
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, preRestoreBackupPath, overwrite: false);
        }
        else
        {
            File.Copy(backupPath, preRestoreBackupPath, overwrite: false);
        }

        var tempPath = sourcePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.Copy(backupPath, tempPath, overwrite: false);
            File.Move(tempPath, sourcePath, overwrite: true);
            File.SetAttributes(sourcePath, attributes);
            TrySetUnixFileMode(sourcePath, unixMode);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
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
