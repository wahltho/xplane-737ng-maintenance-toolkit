using LevelUp.NavTableUpdater.Core.Upstream;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed record AircraftPatchVersion(string Name, bool Required, string? Installed, string? Available);

// Version summary only. Install records do not prove that files are unchanged.
public sealed record AircraftOverviewSummary(
    string Status, string Aircraft, string RequiredPatches, string OptionalPatches, string NextStep, string LastChecked)
{
    public static AircraftOverviewSummary Create(bool isLevelUp, bool patchListAvailable,
        string installedAircraft, AircraftUpstreamUpdateCheckResult? aircraftCheck,
        IReadOnlyList<AircraftPatchVersion> patches, bool checking, bool aircraftCheckFailed,
        bool patchCheckFailed, string aircraftChecked, string patchesChecked)
    {
        var required = patches.Where(p => p.Required).ToArray();
        patchListAvailable &= !isLevelUp || required.Length > 0;
        var installedRequired = required.Count(p => Recorded(p.Installed));
        var missingRequired = isLevelUp && patchListAvailable && installedRequired < required.Length;
        var installedOptional = patches.Where(p => !p.Required && Recorded(p.Installed)).ToArray();
        var differentRequired = required.Any(Different);
        var differentOptional = installedOptional.Any(Different);
        var patchVersionsChecked = patchListAvailable && patches.All(p => Known(p.Available)
            && (!Recorded(p.Installed) || Known(p.Installed)));
        var localAircraft = aircraftCheck?.LocalVersionDisplay ?? installedAircraft;
        var aircraftVersionsMatch = aircraftCheck is not null && SameVersion(localAircraft, aircraftCheck.AvailableVersionDisplay);

        var aircraft = $"Installed: {Display(localAircraft)}";
        if (aircraftCheckFailed) aircraft += "; release check failed";
        else if (aircraftCheck is null) aircraft += "; latest release not checked";
        else
        {
            aircraft += $"; available: {Display(aircraftCheck.AvailableVersionDisplay)}";
            aircraft += aircraftCheck.IsCustomDistribution ? "; custom aircraft" : aircraftCheck.Action switch
            {
                AircraftUpdatePlanAction.UpToDate => aircraftVersionsMatch ? "; current" : "; version comparison unavailable",
                AircraftUpdatePlanAction.LocalNewerThanIndex => "; newer than the release index",
                AircraftUpdatePlanAction.Unknown => "; version comparison unavailable",
                _ => ""
            };
        }

        var requiredText = !isLevelUp ? "None required for Zibo. All patches are optional."
            : !patchListAvailable ? "Required patch list unavailable"
            : $"{installedRequired} of {required.Length} installed"
                + (patchCheckFailed ? "; release check failed"
                    : missingRequired ? "; install the missing patches"
                    : differentRequired ? "; different versions available"
                    : required.Any(p => !Known(p.Installed)) ? "; installed version unknown"
                    : required.Any(p => !Known(p.Available)) ? "; latest releases not checked"
                    : "; versions current");
        var optionalText = installedOptional.Length == 0 ? "None recorded by the Toolkit"
            : string.Join(", ", installedOptional.Select(p => $"{p.Name} {p.Installed}"));

        var status = "Not fully checked";
        var next = "Check aircraft and patch releases to compare the installed versions.";
        if (checking) { status = "Checking releases…"; next = "Wait for the release checks to finish."; }
        else if (missingRequired) { status = "Required patches missing"; next = "Use Update to install the missing LevelUp patches."; }
        else if (aircraftCheckFailed || patchCheckFailed) { status = "Release check failed"; next = "Retry the release check. Installed patches are still listed below."; }
        else if (!patchListAvailable) { status = "Patch list unavailable"; next = "Refresh the catalog to load the patch list."; }
        else if (aircraftCheck?.IsCustomDistribution == true || aircraftCheck?.Action == AircraftUpdatePlanAction.LocalNewerThanIndex)
        { status = "Review aircraft version"; next = "Check the aircraft's update instructions before applying an official package."; }
        else if (aircraftCheck?.HasUpdate == true)
        { status = "Aircraft update available"; next = "Use Update to review the aircraft update and its patches."; }
        else if (aircraftCheck is not null && (aircraftCheck.Action != AircraftUpdatePlanAction.UpToDate || !aircraftVersionsMatch))
        { status = "Review aircraft version"; next = "See the aircraft update plan below before making changes."; }
        else if (differentRequired || differentOptional)
        { status = differentRequired ? "Required patch versions differ" : "Optional patch versions differ"; next = "Review the installed and available patch versions below."; }
        else if (aircraftCheck?.Action == AircraftUpdatePlanAction.UpToDate && aircraftVersionsMatch && patchVersionsChecked)
        { status = "No updates found"; next = "No version update is needed for the aircraft or installed patches."; }

        return new(status, aircraft, requiredText, optionalText, next,
            $"Aircraft releases: {aircraftChecked}. Patch releases: {patchesChecked}.");
    }

    private static bool Recorded(string? value) => !string.IsNullOrWhiteSpace(value) && value is not ("-" or "Not checked");
    private static bool Known(string? value) => Recorded(value) && !value!.Equals("unknown", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("version unknown", StringComparison.OrdinalIgnoreCase);
    private static string Display(string? value) => Known(value) ? value! : "unknown";
    private static bool SameVersion(string? left, string? right) => Known(left) && Known(right)
        && left!.Trim().TrimStart('v', 'V').Equals(right!.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
    private static bool Different(AircraftPatchVersion patch) => Known(patch.Installed) && Known(patch.Available)
        && !SameVersion(patch.Installed, patch.Available);
}
