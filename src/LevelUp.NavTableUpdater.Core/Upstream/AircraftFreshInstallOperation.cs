using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Platform;
using LevelUp.NavTableUpdater.Core.Tools;

namespace LevelUp.NavTableUpdater.Core.Upstream;

public sealed class AircraftFreshInstallOperation
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly Func<bool> _isXPlaneRunning;
    private readonly AircraftViewAnalyzer _viewAnalyzer;

    public AircraftFreshInstallOperation(
        Func<bool>? isXPlaneRunning = null,
        AircraftViewAnalyzer? viewAnalyzer = null)
    {
        _isXPlaneRunning = isXPlaneRunning ?? XPlaneProcessDetector.IsXPlaneRunning;
        _viewAnalyzer = viewAnalyzer ?? new AircraftViewAnalyzer();
    }

    public MaintenanceOperationResult Apply(
        string xPlaneRoot,
        string targetFolder,
        AircraftFreshInstallProduct product,
        AircraftUpstreamUpdateCheckResult installPlan,
        IReadOnlyList<AircraftUpdatePackageCacheEntry> cachedPackages,
        CancellationToken cancellationToken = default,
        Action? writePhaseStarting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xPlaneRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(installPlan);
        ArgumentNullException.ThrowIfNull(cachedPackages);
        cancellationToken.ThrowIfCancellationRequested();

        var log = new List<string>
        {
            $"[START] Fresh install of {product.DisplayName}",
            $"[TARGET] {Path.GetFullPath(targetFolder)}",
            $"[PLAN] {installPlan.Summary}"
        };

        var validationError = ValidateRequest(xPlaneRoot, targetFolder, product, installPlan, cachedPackages);
        if (validationError is not null)
        {
            log.Add($"[BLOCKED] {validationError}");
            return MaintenanceOperationResult.Blocked(validationError, log);
        }

        if (_isXPlaneRunning())
        {
            log.Add("[BLOCKED] X-Plane is running.");
            return MaintenanceOperationResult.Blocked(
                "X-Plane is running. Close X-Plane before installing an aircraft.",
                log);
        }

        var fullTarget = Path.GetFullPath(targetFolder);
        var aircraftRoot = Path.GetFullPath(Path.Combine(xPlaneRoot, "Aircraft"));
        var stagePath = Path.Combine(
            aircraftRoot,
            $".{Path.GetFileName(fullTarget)}.toolkit-install-{Guid.NewGuid():N}");
        var targetCreated = false;

        try
        {
            Directory.CreateDirectory(stagePath);
            foreach (var cachedPackage in cachedPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AircraftFullBaselineReplacement.ExtractPackage(
                    stagePath,
                    cachedPackage,
                    cancellationToken,
                    log);
            }

            ValidateStagedAircraft(stagePath, product);
            WriteToolkitMetadata(stagePath, product, installPlan);
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(fullTarget) || File.Exists(fullTarget))
            {
                throw new IOException("The aircraft destination was created or changed during staging.");
            }

            writePhaseStarting?.Invoke();
            Directory.Move(stagePath, fullTarget);
            targetCreated = true;
            ValidateStagedAircraft(fullTarget, product);

            log.Add("[OK] Fresh aircraft installation completed and structurally validated.");
            return MaintenanceOperationResult.Applied(
                $"{product.DisplayName} {installPlan.AvailableVersionDisplay} was installed.",
                [fullTarget],
                log);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            if (targetCreated && Directory.Exists(fullTarget))
            {
                Directory.Delete(fullTarget, recursive: true);
                log.Add("[ROLLBACK] Removed the incomplete fresh-install target.");
            }

            log.Add($"[FAILED] {ex.Message}");
            return MaintenanceOperationResult.Blocked(
                $"Fresh installation failed without replacing an existing aircraft: {ex.Message}",
                log);
        }
        finally
        {
            if (Directory.Exists(stagePath))
            {
                Directory.Delete(stagePath, recursive: true);
            }
        }
    }

    private static string? ValidateRequest(
        string xPlaneRoot,
        string targetFolder,
        AircraftFreshInstallProduct product,
        AircraftUpstreamUpdateCheckResult installPlan,
        IReadOnlyList<AircraftUpdatePackageCacheEntry> cachedPackages)
    {
        var fullXPlaneRoot = Path.GetFullPath(xPlaneRoot);
        if (!XPlaneInstallationLocator.LooksLikeXPlaneRoot(fullXPlaneRoot))
        {
            return "The selected path is not a structurally valid X-Plane installation.";
        }

        if (!AircraftProductIds.IsSupported(product.ProductId)
            || !string.Equals(product.ProductId, installPlan.Family, StringComparison.OrdinalIgnoreCase))
        {
            return "The selected product does not match the release package plan.";
        }

        var fullTarget = Path.GetFullPath(targetFolder);
        var aircraftRoot = Path.GetFullPath(Path.Combine(fullXPlaneRoot, "Aircraft"));
        var targetParent = Path.GetDirectoryName(fullTarget);
        if (string.IsNullOrWhiteSpace(targetParent)
            || !PathsEqual(AircraftUpdatePath.ResolvePhysicalPath(targetParent), AircraftUpdatePath.ResolvePhysicalPath(aircraftRoot)))
        {
            return "A fresh aircraft must be installed into a direct child folder of X-Plane 12/Aircraft.";
        }

        if (Directory.Exists(fullTarget) || File.Exists(fullTarget))
        {
            return "The destination already exists. Select and update that aircraft instead, or choose a new folder name.";
        }

        if (installPlan.RequiredPackages.Count == 0
            || installPlan.RequiredPackages[0].Kind != AircraftUpdatePackageKind.FullBaseline
            || installPlan.RequiredPackages.Count(package => package.Kind == AircraftUpdatePackageKind.FullBaseline) != 1
            || installPlan.RequiredPackages.Skip(1).Any(package => package.Kind != AircraftUpdatePackageKind.CumulativePatch))
        {
            return "A fresh install requires exactly one full baseline first, followed only by cumulative patches.";
        }

        if (cachedPackages.Count != installPlan.RequiredPackages.Count)
        {
            return "Not every required fresh-install package is present in the cache.";
        }

        for (var index = 0; index < installPlan.RequiredPackages.Count; index++)
        {
            var required = installPlan.RequiredPackages[index];
            var cached = cachedPackages[index];
            if (!cached.IsCached
                || !File.Exists(cached.CachePath)
                || !string.Equals(required.Family, cached.Package.Family, StringComparison.OrdinalIgnoreCase)
                || required.Kind != cached.Package.Kind
                || required.Version != cached.Package.Version
                || !string.Equals(required.FileName, cached.Package.FileName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(required.ExpectedSha256, cached.Package.ExpectedSha256, StringComparison.OrdinalIgnoreCase)
                || required.ExpectedSizeBytes != cached.Package.ExpectedSizeBytes)
            {
                return $"Required package is missing or invalid: {required.FileName}.";
            }
        }

        return null;
    }

    private void ValidateStagedAircraft(string stagePath, AircraftFreshInstallProduct product)
    {
        if (!Directory.Exists(Path.Combine(stagePath, "plugins")))
        {
            throw new InvalidDataException("Staged aircraft image does not contain the expected plugins directory.");
        }

        var requiredAcfs = string.Equals(product.ProductId, AircraftProductIds.Zibo737Ng, StringComparison.Ordinal)
            ? new[] { "b738.acf", "b738_4k.acf" }
            : new[] { "737_60NG.acf", "737_70NG.acf", "737_80NG.acf", "737_90NG.acf", "737_9ENG.acf" };
        var missingAcfs = requiredAcfs.Where(name => !File.Exists(Path.Combine(stagePath, name))).ToArray();
        if (missingAcfs.Length > 0)
        {
            throw new InvalidDataException(
                $"Staged aircraft image is missing required aircraft file(s): {string.Join(", ", missingAcfs)}.");
        }

        var analysis = _viewAnalyzer.Analyze(stagePath);
        if (!analysis.Variants.Any(variant => string.Equals(
                variant.Family,
                product.ProductId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Staged aircraft image does not match the structural identity of {product.DisplayName}.");
        }
    }

    private static void WriteToolkitMetadata(
        string stagePath,
        AircraftFreshInstallProduct product,
        AircraftUpstreamUpdateCheckResult installPlan)
    {
        var metadata = new AircraftMaintenanceMetadata(
            SchemaVersion: 1,
            AircraftFamily: product.ProductId,
            Variant: null,
            Distribution: null,
            DistributionVersion: installPlan.AvailableVersionDisplay,
            UpstreamFamily: installPlan.Family,
            UpstreamBaseVersion: installPlan.AvailableVersionDisplay,
            UpstreamSourceRef: null,
            UpstreamReleaseTag: null,
            Runtime: null);
        File.WriteAllText(
            Path.Combine(stagePath, AircraftMaintenanceMetadata.FileName),
            JsonSerializer.Serialize(metadata, MetadataJsonOptions));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
