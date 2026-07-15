using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Analysis;

public sealed class AircraftInstallAnalyzer
{
    public AircraftAnalysisResult Analyze(string aircraftFolder, PackageManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(aircraftFolder))
        {
            return AircraftAnalysisResult.Empty(manifest.PackageVersion);
        }

        var findings = new List<string>();
        var plannedChanges = new List<string>();
        var components = new List<ComponentStatus>();
        var aircraftPath = Path.GetFullPath(aircraftFolder);
        var targetScriptPath = Path.Combine(aircraftPath, manifest.TargetRelativePath);

        if (!File.Exists(targetScriptPath))
        {
            if (LooksLikePortNoLuaLevelUp(aircraftPath))
            {
                findings.Add("LevelUp aircraft structure was detected.");
                findings.Add("The port/no-Lua plugin layout was detected via plugins/zibomod.");
                findings.Add($"The Lua target script is not present: {manifest.TargetRelativePath}");
                AddPlan(InstallState.PortNoLuaInstallation, plannedChanges);

                return Result(
                    aircraftPath,
                    targetScriptPath,
                    targetScriptExists: false,
                    InstallState.PortNoLuaInstallation,
                    "Port / no-Lua install",
                    localPackageVersion: "-",
                    manifest.PackageVersion,
                    lineEnding: "-",
                    "This aircraft appears to use the port/no-Lua runtime. The VNAV Lua patch package is not applicable to this install.",
                    isSafeToPatch: false,
                    components,
                    plannedChanges,
                    findings);
            }

            findings.Add($"Required target script was not found: {manifest.TargetRelativePath}");
            findings.Add("This prototype does not accept a target based on folder name only.");
            AddPlan(InstallState.UnsupportedTarget, plannedChanges);

            return Result(
                aircraftPath,
                targetScriptPath,
                targetScriptExists: false,
                InstallState.UnsupportedTarget,
                "Unsupported target",
                localPackageVersion: "-",
                manifest.PackageVersion,
                lineEnding: "-",
                "The selected folder does not look like a supported LevelUp v2 aircraft.",
                isSafeToPatch: false,
                components,
                plannedChanges,
                findings);
        }

        var bytes = File.ReadAllBytes(targetScriptPath);
        var lineEnding = DetectLineEnding(bytes);
        var text = DecodeUtf8(bytes, findings);
        if (text is null)
        {
            return Result(
                aircraftPath,
                targetScriptPath,
                targetScriptExists: true,
                InstallState.UnknownThirdPartyModification,
                "Unsupported encoding",
                localPackageVersion: "-",
                manifest.PackageVersion,
                lineEnding,
                "The target Lua script is not valid UTF-8.",
                isSafeToPatch: false,
                components,
                plannedChanges,
                findings);
        }

        var lines = NormalizeLines(text);
        var operationFindings = manifest.PatchOperations
            .Select(operation => AnalyzeOperation(lines, operation))
            .ToArray();

        foreach (var finding in operationFindings)
        {
            components.Add(new ComponentStatus(
                Name: $"{finding.Id} hook",
                State: finding.ComponentState,
                Version: finding.LocalPackageVersion ?? "-",
                Detail: finding.Detail));
        }

        AddPayloadComponents(Path.GetDirectoryName(targetScriptPath)!, manifest, components);

        var localPackageVersion = ExtractPackageVersion(lines) ?? "-";
        var state = Classify(operationFindings, manifest.PackageVersion);
        var isSafeToPatch = state is InstallState.NotInstalled
            or InstallState.CorrectlyInstalled
            or InstallState.OutdatedMarkedInstallation
            or InstallState.KnownLegacyInstallation;

        findings.Add($"Target script line ending: {lineEnding}.");
        findings.Add($"Target script size: {bytes.Length:N0} bytes.");
        findings.AddRange(operationFindings.Select(f => f.Detail));
        AddPlan(state, plannedChanges);

        return Result(
            aircraftPath,
            targetScriptPath,
            targetScriptExists: true,
            state,
            LabelFor(state),
            localPackageVersion,
            manifest.PackageVersion,
            lineEnding,
            SummaryFor(state),
            isSafeToPatch,
            components,
            plannedChanges,
            findings);
    }

    private static AircraftAnalysisResult Result(
        string aircraftPath,
        string targetScriptPath,
        bool targetScriptExists,
        InstallState state,
        string stateLabel,
        string localPackageVersion,
        string availablePackageVersion,
        string lineEnding,
        string summary,
        bool isSafeToPatch,
        IReadOnlyList<ComponentStatus> components,
        IReadOnlyList<string> plannedChanges,
        IReadOnlyList<string> findings) =>
        new(
            aircraftPath,
            targetScriptPath,
            targetScriptExists,
            state,
            stateLabel,
            localPackageVersion,
            availablePackageVersion,
            lineEnding,
            summary,
            isSafeToPatch,
            components.ToArray(),
            plannedChanges.ToArray(),
            findings.ToArray());

    private static string? DecodeUtf8(byte[] bytes, ICollection<string> findings)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            findings.Add(ex.Message);
            return null;
        }
    }

    private static IReadOnlyList<string> NormalizeLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static OperationFinding AnalyzeOperation(IReadOnlyList<string> lines, PatchOperation operation)
    {
        var beginCount = CountContaining(lines, operation.BeginMarker);
        var endCount = CountContaining(lines, operation.EndMarker);
        var anchorCount = CountNonCommentedContaining(lines, operation.Anchor);
        var legacyCount = operation.LegacySignatures.Sum(signature => CountNonCommentedContaining(lines, signature));
        var localVersion = ExtractPackageVersion(lines, operation.BeginMarker, operation.EndMarker);

        if (beginCount == 1 && endCount == 1)
        {
            return new OperationFinding(
                operation.Id,
                OperationState.Marked,
                localVersion,
                "Installed",
                $"{operation.Id}: marked block found once.");
        }

        if (beginCount != endCount || beginCount > 1 || endCount > 1)
        {
            return new OperationFinding(
                operation.Id,
                OperationState.PartialOrDuplicateMarker,
                localVersion,
                "Conflict",
                $"{operation.Id}: marker mismatch or duplicate marker detected.");
        }

        if (legacyCount > 0)
        {
            return new OperationFinding(
                operation.Id,
                OperationState.Legacy,
                localVersion,
                "Legacy",
                $"{operation.Id}: known legacy signature found.");
        }

        if (anchorCount == 1)
        {
            return new OperationFinding(
                operation.Id,
                OperationState.NotInstalled,
                null,
                "Missing",
                $"{operation.Id}: unique anchor found; hook can be inserted.");
        }

        if (anchorCount == 0)
        {
            return new OperationFinding(
                operation.Id,
                OperationState.MissingAnchor,
                null,
                "Conflict",
                $"{operation.Id}: required anchor not found.");
        }

        return new OperationFinding(
            operation.Id,
            OperationState.DuplicateAnchor,
            null,
            "Conflict",
            $"{operation.Id}: required anchor found {anchorCount} times.");
    }

    private static InstallState Classify(IReadOnlyList<OperationFinding> findings, string availablePackageVersion)
    {
        if (findings.Any(f => f.State is OperationState.PartialOrDuplicateMarker))
        {
            return InstallState.PartiallyInstalled;
        }

        if (findings.Any(f => f.State is OperationState.DuplicateAnchor))
        {
            return InstallState.UnknownThirdPartyModification;
        }

        if (findings.Any(f => f.State is OperationState.MissingAnchor))
        {
            return InstallState.AircraftUpdateOverwroteInstallation;
        }

        if (findings.All(f => f.State is OperationState.Marked))
        {
            var versions = findings.Select(f => f.LocalPackageVersion).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToArray();
            return versions.Length == 1 && versions[0] == availablePackageVersion
                ? InstallState.CorrectlyInstalled
                : InstallState.OutdatedMarkedInstallation;
        }

        if (findings.Any(f => f.State is OperationState.Legacy))
        {
            return InstallState.KnownLegacyInstallation;
        }

        return InstallState.NotInstalled;
    }

    private static void AddPayloadComponents(string scriptFolder, PackageManifest manifest, List<ComponentStatus> components)
    {
        foreach (var payload in manifest.Payloads)
        {
            var payloadPath = Path.Combine(scriptFolder, payload.FileName);
            if (!File.Exists(payloadPath))
            {
                components.Add(new ComponentStatus(payload.FileName, "Missing", manifest.PackageVersion, "Payload file is not present."));
                continue;
            }

            var fileInfo = new FileInfo(payloadPath);
            var hash = ComputeSha256(payloadPath);
            var hashMatches = hash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase);
            var sizeMatches = fileInfo.Length == payload.Size;
            var state = hashMatches && sizeMatches ? "Current" : "Changed";
            var detail = hashMatches && sizeMatches
                ? "Payload size and SHA-256 match the manifest."
                : $"Expected {payload.Size} bytes / {payload.Sha256}; found {fileInfo.Length} bytes / {hash}.";

            components.Add(new ComponentStatus(payload.FileName, state, manifest.PackageVersion, detail));
        }
    }

    private static string? ExtractPackageVersion(IReadOnlyList<string> lines) =>
        lines.Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("-- package-version|", StringComparison.Ordinal))?
            .Split('|')
            .LastOrDefault();

    private static string? ExtractPackageVersion(IReadOnlyList<string> lines, string beginMarker, string endMarker)
    {
        var begin = IndexOfContaining(lines, beginMarker);
        if (begin < 0)
        {
            return null;
        }

        var end = IndexOfContaining(lines, endMarker, begin + 1);
        if (end < 0)
        {
            return null;
        }

        return ExtractPackageVersion(lines.Skip(begin).Take(end - begin + 1).ToArray());
    }

    private static int CountContaining(IReadOnlyList<string> lines, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : lines.Count(line => line.Contains(text, StringComparison.Ordinal));

    private static int CountNonCommentedContaining(IReadOnlyList<string> lines, string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : lines.Count(line => line.Contains(text, StringComparison.Ordinal) && !line.TrimStart().StartsWith("--", StringComparison.Ordinal));

    private static int IndexOfContaining(IReadOnlyList<string> lines, string text, int start = 0)
    {
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string DetectLineEnding(byte[] bytes)
    {
        var crlf = CountSequence(bytes, "\r\n"u8.ToArray());
        var lf = bytes.Count(b => b == (byte)'\n') - crlf;
        var cr = bytes.Count(b => b == (byte)'\r') - crlf;

        return (crlf, lf, cr) switch
        {
            (> 0, 0, 0) => "CRLF",
            (0, > 0, 0) => "LF",
            (0, 0, > 0) => "CR",
            _ => "Mixed"
        };
    }

    private static int CountSequence(byte[] bytes, byte[] sequence)
    {
        var count = 0;
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                count++;
            }
        }

        return count;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AddPlan(InstallState state, ICollection<string> plannedChanges)
    {
        switch (state)
        {
            case InstallState.PortNoLuaInstallation:
                plannedChanges.Add("No Lua patch can be applied because B738.a_fms.lua is not present.");
                plannedChanges.Add("For port/no-Lua builds, VNAV table behavior must be handled by the plugin/runtime, not by this Lua patch package.");
                break;
            case InstallState.NotInstalled:
                plannedChanges.Add("Would create a generation backup for B738.a_fms.lua.");
                plannedChanges.Add("Would insert dofile, KIAS and MACH hook blocks at manifest anchors.");
                plannedChanges.Add("Would copy verified payload files into the B738.a_fms script folder.");
                break;
            case InstallState.CorrectlyInstalled:
                plannedChanges.Add("No patch required. Repair would verify and refresh installer-owned files only.");
                break;
            case InstallState.OutdatedMarkedInstallation:
            case InstallState.KnownLegacyInstallation:
                plannedChanges.Add("Would create a generation backup before migration.");
                plannedChanges.Add("Would replace known installer-owned or legacy hook blocks with current manifest blocks.");
                plannedChanges.Add("Would refresh verified payload files.");
                break;
            case InstallState.PartiallyInstalled:
            case InstallState.AircraftUpdateOverwroteInstallation:
            case InstallState.UnknownThirdPartyModification:
                plannedChanges.Add("No automatic patch planned. User should export a diagnostic report.");
                break;
            case InstallState.UnsupportedTarget:
            case InstallState.NoTargetSelected:
            default:
                plannedChanges.Add("Select a supported LevelUp aircraft folder before planning changes.");
                break;
        }
    }

    private static string LabelFor(InstallState state) =>
        state switch
        {
            InstallState.NoTargetSelected => "No aircraft selected",
            InstallState.UnsupportedTarget => "Unsupported target",
            InstallState.PortNoLuaInstallation => "Port / no-Lua install",
            InstallState.NotInstalled => "Not installed",
            InstallState.CorrectlyInstalled => "Installed",
            InstallState.OutdatedMarkedInstallation => "Installed, update available",
            InstallState.KnownLegacyInstallation => "Legacy installation",
            InstallState.PartiallyInstalled => "Partial or damaged installation",
            InstallState.AircraftUpdateOverwroteInstallation => "Anchor mismatch",
            InstallState.UnknownThirdPartyModification => "Unsafe foreign change",
            _ => "Unknown"
        };

    private static string SummaryFor(InstallState state) =>
        state switch
        {
            InstallState.NotInstalled => "The aircraft appears patchable and the VNAV table hooks are not installed.",
            InstallState.CorrectlyInstalled => "The manifest-owned hooks are present. Verify payload status before repair.",
            InstallState.OutdatedMarkedInstallation => "Marked hooks exist but should be migrated to the current package.",
            InstallState.KnownLegacyInstallation => "Known legacy hook signatures were found and can be migrated.",
            InstallState.PartiallyInstalled => "Installer markers are incomplete or duplicated; automatic patching is blocked.",
            InstallState.AircraftUpdateOverwroteInstallation => "Required anchors were not found; the target script may have changed.",
            InstallState.UnknownThirdPartyModification => "The target has an unsafe state and should not be overwritten automatically.",
            InstallState.UnsupportedTarget => "The selected folder is missing required LevelUp v2 structure.",
            InstallState.PortNoLuaInstallation => "The selected LevelUp aircraft uses the port/no-Lua layout. The Lua patch package cannot be installed here.",
            _ => "Select or scan a target aircraft folder."
        };

    private static bool LooksLikePortNoLuaLevelUp(string aircraftPath)
    {
        var hasLevelUpAcfs = File.Exists(Path.Combine(aircraftPath, "737_70NG.acf"))
            || File.Exists(Path.Combine(aircraftPath, "737_9ENG.acf"));
        var hasPortPlugin = Directory.Exists(Path.Combine(aircraftPath, "plugins", "zibomod"));

        return hasLevelUpAcfs && hasPortPlugin;
    }

    private sealed record OperationFinding(
        string Id,
        OperationState State,
        string? LocalPackageVersion,
        string ComponentState,
        string Detail);

    private enum OperationState
    {
        NotInstalled,
        Marked,
        Legacy,
        MissingAnchor,
        DuplicateAnchor,
        PartialOrDuplicateMarker
    }
}
