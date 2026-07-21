namespace LevelUp.NavTableUpdater.Core.Platform;

public static class ToolkitPaths
{
    private const string AppFolderName = "XPlane737NGMaintenanceToolkit";
    private const string UserVisibleFolderName = "X-Plane 737NG Maintenance Toolkit";

    public static string RoamingAppDataRoot => Path.Combine(GetRoamingAppDataRoot(), AppFolderName);

    public static string LocalAppDataRoot => Path.Combine(GetLocalAppDataRoot(), AppFolderName);

    public static string DefaultBackupRootPath => Path.Combine(RoamingAppDataRoot, "backups");

    public static string DefaultAircraftUpdateCacheRootPath => Path.Combine(LocalAppDataRoot, "aircraft-updates");

    public static string DefaultOfflinePackageRootPath => Path.Combine(UserDocumentsRoot, UserVisibleFolderName, "Packages");

    public static string DefaultDiagnosticsExportRootPath => Path.Combine(UserDocumentsRoot, UserVisibleFolderName, "Diagnostics");

    private static string UserDocumentsRoot
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(UserProfileRoot, "Documents")
                : documents;
        }
    }

    private static string GetRoamingAppDataRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            return GetXdgRoot("XDG_CONFIG_HOME", ".config");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(UserProfileRoot, ".xplane-737ng-maintenance-toolkit")
            : appData;
    }

    private static string GetLocalAppDataRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            return GetXdgRoot("XDG_CACHE_HOME", ".cache");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(UserProfileRoot, ".xplane-737ng-maintenance-toolkit", "local")
            : appData;
    }

    private static string GetXdgRoot(string environmentVariableName, string fallbackFolderName)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(environmentVariableName);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(UserProfileRoot, fallbackFolderName)
            : configuredRoot.Trim());
    }

    private static string UserProfileRoot
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(profile)
                ? Environment.CurrentDirectory
                : profile;
        }
    }
}
