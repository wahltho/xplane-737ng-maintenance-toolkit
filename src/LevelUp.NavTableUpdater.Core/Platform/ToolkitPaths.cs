namespace LevelUp.NavTableUpdater.Core.Platform;

public static class ToolkitPaths
{
    private const string AppFolderName = "XPlane737NGMaintenanceToolkit";
    private const string UserVisibleFolderName = "X-Plane 737NG Maintenance Toolkit";

    public static string RoamingAppDataRoot
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xplane-737ng-maintenance-toolkit");
            }

            return Path.Combine(appData, AppFolderName);
        }
    }

    public static string LocalAppDataRoot
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xplane-737ng-maintenance-toolkit", "local");
            }

            return Path.Combine(appData, AppFolderName);
        }
    }

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
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")
                : documents;
        }
    }
}
