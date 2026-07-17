namespace LevelUp.NavTableUpdater.Core.Transactions;

internal static class TextFileRewrite
{
    public static void Apply(
        string sourcePath,
        string backupPath,
        Func<TextDocument, TextDocument> rewrite,
        Action<string> validate)
    {
        var document = TextDocument.Read(sourcePath);
        var rewrittenDocument = rewrite(document);
        var attributes = File.GetAttributes(sourcePath);
        var unixMode = TryGetUnixFileMode(sourcePath);

        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)
            ?? throw new InvalidOperationException("Backup path has no parent directory."));
        File.Copy(sourcePath, backupPath, overwrite: false);

        var tempPath = sourcePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            rewrittenDocument.Write(tempPath);
            validate(tempPath);
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
