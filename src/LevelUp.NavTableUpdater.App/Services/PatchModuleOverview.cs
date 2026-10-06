using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.App.Services;

// Install records describe versions, not the current contents of aircraft files.
public sealed record PatchModuleOverview(
    string ModuleId, string Name, string Policy, string Installed, string Available, string Status, bool IsBeta)
{
    public static PatchModuleOverview Create(string moduleId, AircraftPatchVersion patch,
        bool releaseCheckFailed, bool checking)
    {
        var recorded = !string.IsNullOrWhiteSpace(patch.Installed) && patch.Installed is not ("-" or "Not checked");
        var available = !string.IsNullOrWhiteSpace(patch.Available) && patch.Available is not ("-" or "Not checked")
            && !patch.Available.Equals("unknown", StringComparison.OrdinalIgnoreCase);
        var status = checking ? "Checking releases…"
            : releaseCheckFailed ? "Release check failed"
            : !recorded ? "No Toolkit install record"
            : patch.Installed!.Equals("version unknown", StringComparison.OrdinalIgnoreCase)
                || patch.Installed.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? "Installed version unknown"
            : !available ? "Latest release not checked"
            : patch.Installed.Trim().TrimStart('v', 'V').Equals(patch.Available!.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase)
                ? "Versions match; files not checked" : "Different version available";
        var beta = Regex.IsMatch($"{patch.Name} {patch.Installed} {patch.Available}",
            @"\b(beta|preview|alpha|experimental)\b|(?<=\d)(b|rc)\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new(moduleId, patch.Name, patch.Required ? "Required" : "Optional",
            recorded ? patch.Installed! : "—", available ? patch.Available! : "Not checked", status, beta);
    }
}
