using System.Text.RegularExpressions;

namespace LevelUp.NavTableUpdater.App.Services;

// Presentation only: recognition of an existing engine message never authorizes
// a repair, adopts ownership, or identifies who changed a file.
public sealed record BlockedOperationHelp(string Reason, string? AffectedPath, string NextStep)
{
    public bool HasAffectedPath => !string.IsNullOrWhiteSpace(AffectedPath);

    public static BlockedOperationHelp? FromResult(string status, string message)
    {
        if (!IsUnsuccessful(status)) return null;

        const string pendingPrefix = "Required patches are still pending. ";
        if (message.StartsWith(pendingPrefix, StringComparison.Ordinal))
            message = message[pendingPrefix.Length..];

        if (message.StartsWith("X-Plane is running.", StringComparison.OrdinalIgnoreCase))
            return new("X-Plane is still running, so this operation cannot continue.", null,
                "Close X-Plane completely, then retry the same operation.");

        if (message.Contains("has standalone or unowned patch evidence at ", StringComparison.Ordinal))
            return new("Patch content or a patch record was found without a verified Toolkit backup history. This does not establish how it was installed.",
                ExtractPath(message, "has standalone or unowned patch evidence at "),
                "Export a diagnostic package before changing anything. If you installed this patch separately, use its documented uninstall procedure first. If you moved the aircraft, check the Toolkit's moved-aircraft notice. Keep all state and backup files.");

        if (message.Contains("Cannot verify the complete backup chain for ", StringComparison.Ordinal))
            return new("The Toolkit cannot verify a complete original-to-current backup history for this file.",
                ExtractPath(message, "Cannot verify the complete backup chain for "),
                "Export a diagnostic package and keep the aircraft, state and backups intact. If the aircraft was moved, check the moved-aircraft notice. Support needs to verify the history before a repair or restore is safe.");

        var backup = Regex.Match(message,
            @"Original (?:(?:compatibility-package|compatibility) )?backup (?:is missing|failed (?:(?:size/SHA-256 |integrity )?validation|verification)) for (?:retired file )?(?<path>.+?)(?:;|\.\s|\.$|$)",
            RegexOptions.CultureInvariant);
        if (backup.Success)
            return new("A required original backup is missing or does not pass verification.", backup.Groups["path"].Value,
                "Check that the recorded backup location is accessible, including any external disk. Export a diagnostic package if the backup cannot be recovered. Keep the current aircraft and remaining backups intact.");

        if (message.Contains("Managed target changed after installation: ", StringComparison.Ordinal))
            return new("This file differs from the version recorded by the Toolkit. The message does not identify what changed it.",
                ExtractPath(message, "Managed target changed after installation: "),
                "Keep your current file and export a diagnostic package before repairing or restoring. Support can check whether another patch or a local edit must be preserved.");

        if (message.Contains("package-owned", StringComparison.OrdinalIgnoreCase)
            && message.Contains("files changed after", StringComparison.OrdinalIgnoreCase))
            return new("Package files differ from the recorded installation, so restoring them would risk overwriting changes.", null,
                "Export a diagnostic package before repairing or restoring. Keep the changed files and the original backup for review.");

        foreach (var prefix in new[] { "Managed-scope copy target has an unknown source hash: ",
                     "Unrecognized original: ", "Retired file has an unknown or locally modified SHA-256: " })
            if (message.Contains(prefix, StringComparison.Ordinal))
                return new("The existing file is not a recognized source version for this package.", ExtractPath(message, prefix),
                    "Export a diagnostic package and keep this file unchanged. Support needs to check the aircraft and patch versions before any replacement or removal.");

        if (message.Contains("rate limit exceeded", StringComparison.OrdinalIgnoreCase))
            return new("The release server rejected the request because its request limit was reached.", null,
                "Wait for the server limit to reset, then retry. Repeated clicks will not resolve the limit. Keep the existing aircraft and backups; export diagnostics if the problem persists.");

        if (message == "Required aircraft update packages are missing or changed in the cache.")
            return new("A required downloaded aircraft package is missing or no longer matches the verified download.", null,
                "Check for updates and download the required package again, then review it before applying. Your aircraft and backup folders are separate from the download cache.");

        if (message == "Aircraft folder is missing.")
            return new("The selected aircraft folder cannot be found.", null,
                "Reconnect its disk if necessary, or browse to its current folder and scan it. If the aircraft was moved, use the Toolkit's verified reconnection when offered.");

        if (message.Contains("Custom distributions are review-only", StringComparison.Ordinal))
            return new("Official aircraft updates cannot be applied to this custom distribution.", null,
                "Use the update instructions supplied by the distribution's maintainer. Export diagnostics if you believe the selected aircraft was incorrectly identified as custom.");

        if (new[] { " contains a symbolic link: ", " traverses a nested symbolic link: ",
                    " must not be a symbolic link: ", " is a symbolic link: ",
                    " contains an unsupported symbolic link" }
            .Any(marker => message.Contains(marker, StringComparison.Ordinal)))
            return new("A symbolic link was rejected by a file safety check.", null,
                "Export a diagnostic package so the rejected path can be reviewed. Do not bypass the check or delete the linked files.");

        if (status == "Required patches pending"
            && (message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)))
            return new("The required patch step was not confirmed or was canceled.", null,
                "Review the required maintenance patches and confirm their installation when you are ready. Check the aircraft and patch results separately.");

        return new("The Toolkit could not safely complete this operation. The technical details are available in the operation log.", null,
            "Export a diagnostic package and attach it to your support request. Keep the aircraft, installation state and backups intact until the result has been reviewed.");
    }

    private static bool IsUnsuccessful(string status) =>
        status.Contains("blocked", StringComparison.OrdinalIgnoreCase)
        || status.EndsWith("failed", StringComparison.OrdinalIgnoreCase)
        || status is "Required patches pending" or "Review required" or "Update incomplete";

    private static string? ExtractPath(string message, string prefix)
    {
        var start = message.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var match = Regex.Match(message[start..], @"^(?<path>.+?)(?:;|\.\s|\.$| \([0-9a-fA-F]{64}\)|$)",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["path"].Value : null;
    }
}
