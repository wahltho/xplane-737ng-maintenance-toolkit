using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Content;

internal static class ContentPatchOwnershipVerifier
{
    public static async Task<ContentPatchPlan> BuildPlanAsync(ContentPatchAction action,
        AircraftVariantViewAnalysis variant, ContentPatchDescriptor descriptor, string packageVersion,
        ToolStateStore store, Func<ContentPackageCatalog> catalogProvider, IEnumerable<string> declaredTargets,
        Func<Task<ContentPatchPlan>> build, IReadOnlyList<string>? moduleIds = null,
        IReadOnlyList<ResolvedCatalogSource>? sources = null, KnownAircraftBaselines? baselines = null,
        IEnumerable<string>? metadataTargets = null)
    {
        var root = Path.GetDirectoryName(variant.AcfPath) ?? "";
        try
        {
            var catalog = catalogProvider();
            var installation = store.TryGetContentInstallation(root);
            var declared = declaredTargets.ToArray();
            var ownPolicies = RequirePackage(catalog, descriptor, AircraftProductIds.Normalize(variant.Family), moduleIds);
            if ((metadataTargets ?? declared).Any(path => !ownPolicies.Any(policy => policy.TargetPaths
                .Any(pattern => PatchOwnershipPolicy.PatternCovers(pattern, path)))))
                throw new InvalidOperationException("The package contains a target outside its catalog ownership contract.");
            var sourceIds = sources?.Select(source => source.PackageId).ToHashSet(StringComparer.Ordinal) ?? [];
            var targets = declared.Concat(installation?.ContentComponents.Values
                .Where(component => component.ComponentId == descriptor.ComponentId || sourceIds.Contains(component.ComponentId))
                .SelectMany(component => component.Files.Select(file => file.RelativePath)) ?? [])
                .Distinct(StringComparer.Ordinal).ToArray();
            var policies = Resolve(catalog, installation?.ContentComponents, AircraftProductIds.Normalize(variant.Family))
                .Where(policy => Touches(policy, targets)).ToArray();
            var evidence = Capture(root, policies);
            foreach (var target in targets) evidence.TryAdd(target, FileHash(root, target));
            var before = new ContentPatchOwnershipCheck(new()
            {
                CatalogVersion = catalog.CatalogVersion,
                Policies = policies.Select(PatchOwnershipPolicy.Copy).ToList()
            }, evidence, CaptureBackups(installation, targets), StateFingerprint(installation), CatalogFingerprint(catalog));
            var plan = await build().ConfigureAwait(false);
            return ProtectPlan(plan, variant, store, catalog, targets, before, moduleIds, sources, baselines);
        }
        catch (Exception ex) when (IsVerificationFailure(ex))
        {
            return ContentPatchPlan.Blocked(descriptor, packageVersion, action, root, ex.Message,
                [$"[BLOCKED] {ex.Message}"]);
        }
    }

    public static ContentPatchPlan ProtectPlan(ContentPatchPlan plan, AircraftVariantViewAnalysis variant,
        ToolStateStore store, ContentPackageCatalog catalog, IEnumerable<string> declaredTargets,
        ContentPatchOwnershipCheck before,
        IReadOnlyList<string>? moduleIds = null, IReadOnlyList<ResolvedCatalogSource>? sources = null, KnownAircraftBaselines? baselines = null)
    {
        if (!plan.IsSafe) return plan;
        try
        {
            var product = AircraftProductIds.Normalize(variant.Family);
            var ownPolicies = RequirePackage(catalog, plan.Descriptor, product, moduleIds);
            var entry = catalog.Packages.SingleOrDefault(item => item.PackageId == plan.Descriptor.ComponentId);
            if (entry?.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup
                && (sources is null || sources.Count != entry.Members.Count
                    || entry.Members.Any(member => !sources.Any(source => source.PackageId == member.PackageId
                        && source.ModuleId == member.ModuleId
                        && source.RepositoryUrl.TrimEnd('/').Equals(catalog.OwnershipPolicies
                            .Single(policy => policy.PackageId == member.PackageId).RepositoryUrl.TrimEnd('/'),
                                StringComparison.OrdinalIgnoreCase)))))
                throw new InvalidOperationException("Catalog group source identities differ from its ownership contract.");
            var targets = declaredTargets.Concat(plan.Mutations.Select(m => m.RelativePath))
                .Concat(plan.OwnedRelativePaths ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            if (targets.Any(path => !ownPolicies.Any(policy => policy.TargetPaths
                .Any(pattern => PatchOwnershipPolicy.PatternCovers(pattern, path)))))
                throw new InvalidOperationException("The package contains a target outside its catalog ownership contract.");
            var installation = store.TryGetContentInstallation(plan.AircraftRoot);
            var policies = Resolve(catalog, installation?.ContentComponents, product)
                .Where(policy => Touches(policy, targets)).ToArray();
            Check(plan.AircraftRoot, policies, installation?.ContentComponents, installation?.Backups, catalog, baselines);
            var snapshot = new ContentPatchOwnershipSnapshot
            {
                CatalogVersion = catalog.CatalogVersion,
                Policies = policies.Select(PatchOwnershipPolicy.Copy).ToList()
            };
            var evidence = Capture(plan.AircraftRoot, policies);
            // Keep even an empty/no-change plan bound to the files and state reviewed.
            foreach (var path in targets)
                evidence.TryAdd(path, FileHash(plan.AircraftRoot, path));
            if (before.CatalogFingerprint != CatalogFingerprint(catalog)
                || before.StateFingerprint != StateFingerprint(installation)
                || !EqualHashes(evidence, before.EvidenceHashes)
                || plan.ExpectedSourceHashes.Any(pair => !evidence.TryGetValue(pair.Key, out var current)
                    || !string.Equals(current, pair.Value, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Aircraft files, ownership state or catalog changed while planning. Review the operation again.");
            VerifyBackups(before.BackupHashes);
            return plan with
            {
                OwnershipCheck = new(snapshot, evidence, before.BackupHashes,
                    StateFingerprint(installation), CatalogFingerprint(catalog)),
                OwnershipSnapshot = new ContentPatchOwnershipSnapshot
                {
                    CatalogVersion = catalog.CatalogVersion,
                    // Retain cross-namespace prose exceptions used for these targets,
                    // including direct single-package imports. Saving a rule does not
                    // claim that package: Owns still requires its ID/source/module.
                    Policies = ownPolicies.Select(policy => policies.FirstOrDefault(saved => saved.PackageId == policy.PackageId) ?? policy)
                        .Concat(policies.Where(policy => policy.MarkerNamespaces
                            .Any(marker => marker.InformationalCommentLines.Count > 0)))
                        .DistinctBy(policy => policy.PackageId).Select(PatchOwnershipPolicy.Copy).ToList()
                },
                ExpectedSourceHashes = evidence
            };
        }
        catch (Exception ex) when (IsVerificationFailure(ex))
        {
            return ContentPatchPlan.Blocked(plan.Descriptor, plan.PackageVersion, plan.Action,
                plan.AircraftRoot, ex.Message, [.. plan.Log, $"[BLOCKED] {ex.Message}"]);
        }
    }

    public static void Recheck(ContentPatchPlan plan, ToolStateStore store, ContentPackageCatalog catalog, KnownAircraftBaselines? baselines = null)
    {
        var check = plan.OwnershipCheck
            ?? throw new InvalidOperationException("The patch plan has no catalog ownership check. Review the package again.");
        if (plan.Mutations.Select(m => m.RelativePath).Concat(plan.OwnedRelativePaths ?? Enumerable.Empty<string>()).Any(path => !check.EvidenceHashes.ContainsKey(path)))
            throw new InvalidOperationException("Patch targets changed after planning. Review the operation again.");
        var installation = store.TryGetContentInstallation(plan.AircraftRoot);
        if (check.CatalogFingerprint != CatalogFingerprint(catalog)
            || check.StateFingerprint != StateFingerprint(installation))
            throw new InvalidOperationException("The catalog or patch ownership state changed after planning. Review the operation again.");
        Check(plan.AircraftRoot, check.Snapshot.Policies, installation?.ContentComponents, installation?.Backups, catalog, baselines);
        var current = Capture(plan.AircraftRoot, check.Snapshot.Policies);
        foreach (var path in check.EvidenceHashes.Keys) current.TryAdd(path, FileHash(plan.AircraftRoot, path));
        if (!EqualHashes(current, check.EvidenceHashes))
            throw new InvalidOperationException("Patch evidence changed after planning. Review the operation again.");
        VerifyBackups(check.BackupHashes);
    }

    public static void CheckFinal(ContentPatchPlan plan)
    {
        var check = plan.OwnershipCheck
            ?? throw new InvalidOperationException("Missing catalog ownership check.");
        var expected = new Dictionary<string, string?>(check.EvidenceHashes, StringComparer.Ordinal);
        foreach (var mutation in plan.Mutations)
            expected[mutation.RelativePath] = mutation.Kind == ContentPatchMutationKind.Delete
                ? null : Hash(mutation.DesiredBytes!);
        CheckFinal(plan.AircraftRoot, check, expected);
    }

    public static void CheckRestoreFinal(string root, ContentPatchOwnershipCheck check, ContentComponentState component)
    {
        var expected = new Dictionary<string, string?>(check.EvidenceHashes, StringComparer.Ordinal);
        foreach (var file in component.Files)
            expected[file.RelativePath] = file.OriginalExisted ? file.OriginalSha256?.ToLowerInvariant() : null;
        CheckFinal(root, check, expected);
    }

    private static void CheckFinal(string root, ContentPatchOwnershipCheck check, IReadOnlyDictionary<string, string?> expected)
    {
        var actual = Capture(root, check.Snapshot.Policies);
        // A deleted wildcard match disappears from enumeration but remains expected absent.
        foreach (var path in expected.Keys) actual.TryAdd(path, FileHash(root, path));
        ValidateNamespaces(root, check.Snapshot.Policies);
        if (!EqualHashes(actual, expected))
            throw new InvalidOperationException("Patch files changed during the transaction; rolling back.");
    }

    public static string? FindConflict(string root, IEnumerable<string> targets,
        IReadOnlyDictionary<string, ContentComponentState>? components, ContentPackageCatalog catalog,
        IReadOnlyList<BackupRecord>? journal = null, string? product = null, KnownAircraftBaselines? baselines = null)
    {
        try
        {
            var paths = targets.ToArray();
            Check(root, Resolve(catalog, components, product).Where(policy => Touches(policy, paths)).ToArray(),
                components, journal, catalog, baselines);
            return null;
        }
        catch (Exception ex) when (IsVerificationFailure(ex)) { return ex.Message; }
    }

    public static void CheckAircraft(string root, ContentInstallationToolState? installation,
        ContentPackageCatalog catalog, string? product = null)
    {
        Check(root, Resolve(catalog, installation?.ContentComponents, product),
            installation?.ContentComponents, installation?.Backups, catalog);
    }

    public static ContentPatchOwnershipCheck PrepareRestore(string root, ContentComponentState component,
        ContentInstallationToolState? installation, ContentPackageCatalog catalog, string? product)
    {
        // Old, provably managed states can obtain the policy from the current catalog.
        // New states also retain their original contract, even if the live catalog changes.
        var policies = Resolve(catalog, installation?.ContentComponents, product)
            .Where(policy => Touches(policy, component.Files.Select(file => file.RelativePath))).ToArray();
        if (component.OwnershipSnapshot is null)
            _ = RequirePackage(catalog, new(component.ComponentId, component.ComponentId, "",
                new(ContentPatchActivation.Managed, new HashSet<ContentPatchTrigger>()), true), product, null);
        var ownPolicies = policies.Where(policy => Owns(component, policy, catalog)).ToArray();
        if (component.Files.Any(file => !ownPolicies.Any(policy => policy.TargetPaths
            .Any(pattern => PatchOwnershipPolicy.PatternCovers(pattern, file.RelativePath)))))
            throw new InvalidOperationException("Restore includes a file outside the recorded catalog ownership contract.");
        Check(root, policies, installation?.ContentComponents, installation?.Backups, catalog);
        return new(new() { CatalogVersion = catalog.CatalogVersion, Policies = policies.Select(PatchOwnershipPolicy.Copy).ToList() },
            Capture(root, policies), CaptureBackups(installation, component.Files.Select(file => file.RelativePath)),
            StateFingerprint(installation), CatalogFingerprint(catalog));
    }

    public static void RecheckRestore(string root, ContentPatchOwnershipCheck check,
        ContentInstallationToolState? installation, ContentPackageCatalog catalog)
    {
        if (check.StateFingerprint != StateFingerprint(installation)
            || check.CatalogFingerprint != CatalogFingerprint(catalog)
            || !EqualHashes(Capture(root, check.Snapshot.Policies), check.EvidenceHashes))
            throw new InvalidOperationException("Patch ownership changed before Restore. Review the operation again.");
        Check(root, check.Snapshot.Policies, installation?.ContentComponents, installation?.Backups, catalog);
        VerifyBackups(check.BackupHashes);
    }

    private static IReadOnlyList<PatchOwnershipPolicy> RequirePackage(ContentPackageCatalog catalog,
        ContentPatchDescriptor descriptor, string? product, IReadOnlyList<string>? modules)
    {
        RequireSchema(catalog);
        var entry = catalog.Packages.SingleOrDefault(item => item.PackageId == descriptor.ComponentId);
        var ids = entry?.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup
            ? entry.Members.Select(member => member.PackageId).ToArray() : [descriptor.ComponentId];
        var policies = ids.Select(id => catalog.OwnershipPolicies.SingleOrDefault(policy => policy.PackageId == id)
            ?? throw new InvalidOperationException($"No catalog ownership contract is available for {id}."))
            .ToArray();
        if (product is null || policies.Any(policy => !policy.SupportedProducts.Contains(product, StringComparer.Ordinal)))
            throw new InvalidOperationException("The catalog ownership contract does not support this aircraft.");
        var repository = entry?.RepositoryUrl ?? policies.Single().RepositoryUrl;
        if (descriptor.RepositoryUrl.Length > 0
            && !repository.TrimEnd('/').Equals(descriptor.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Package repository differs from the catalog ownership contract.");
        if (modules is not null)
        {
            var allowed = entry?.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup
                ? entry.Members.Select(member => member.ModuleId) : policies.SelectMany(policy => policy.ModuleIds);
            if (modules.Except(allowed, StringComparer.Ordinal).Any())
                throw new InvalidOperationException("The package declares a module outside its catalog ownership contract.");
        }
        return policies;
    }

    private static IReadOnlyList<PatchOwnershipPolicy> Resolve(ContentPackageCatalog catalog,
        IReadOnlyDictionary<string, ContentComponentState>? components, string? product)
    {
        RequireSchema(catalog);
        var policies = catalog.OwnershipPolicies
            .Concat((components?.Values ?? []).SelectMany(component =>
            {
                var snapshot = component.OwnershipSnapshot;
                if (snapshot is null) return Enumerable.Empty<PatchOwnershipPolicy>();
                if (snapshot.SchemaVersion != 1 || snapshot.Policies is null)
                    throw new InvalidOperationException("Unsupported saved patch ownership contract.");
                foreach (var policy in snapshot.Policies)
                {
                    if (policy is null) throw new InvalidOperationException("Null saved patch ownership policy.");
                    policy.Validate();
                }
                return snapshot.Policies;
            })).Where(policy => product is null || policy.SupportedProducts.Contains(product, StringComparer.Ordinal));
        // Historical snapshots may only add checks, not discard a current catalog rule.
        return policies.GroupBy(policy => policy.PackageId, StringComparer.Ordinal).Select(group =>
        {
            var merged = PatchOwnershipPolicy.Copy(group.First());
            if (group.Any(policy => !policy.RepositoryUrl.TrimEnd('/')
                .Equals(merged.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Conflicting saved patch ownership contracts.");
            merged.TargetPaths = group.SelectMany(policy => policy.TargetPaths).Distinct(StringComparer.Ordinal).ToList();
            merged.PayloadPaths = group.SelectMany(policy => policy.PayloadPaths).Distinct(StringComparer.Ordinal).ToList();
            merged.StandaloneEvidencePaths = group.SelectMany(policy => policy.StandaloneEvidencePaths).Distinct(StringComparer.Ordinal).ToList();
            merged.ModuleIds = group.SelectMany(policy => policy.ModuleIds).Distinct(StringComparer.Ordinal).ToList();
            merged.Signatures = group.SelectMany(policy => policy.Signatures)
                .DistinctBy(signature => (signature.RelativePath, signature.Text)).ToList();
            merged.ResultFiles = group.SelectMany(policy => policy.ResultFiles)
                .GroupBy(file => file.RelativePath).Select(files => new PatchOwnershipFileHash
                { RelativePath = files.Key, Sha256 = files.SelectMany(file => file.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }).ToList();
            merged.OriginalFiles = group.SelectMany(policy => policy.OriginalFiles)
                .GroupBy(file => file.RelativePath).Select(files => new PatchOwnershipFileHash
                { RelativePath = files.Key, Sha256 = files.SelectMany(file => file.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }).ToList();
            merged.MarkerNamespaces = group.SelectMany(policy => policy.MarkerNamespaces)
                .GroupBy(marker => (marker.RelativePath, marker.Namespace)).Select(markers => new PatchMarkerNamespace
                {
                    RelativePath = markers.Key.RelativePath, Namespace = markers.Key.Namespace,
                    Blocks = markers.SelectMany(marker => marker.Blocks)
                        .DistinctBy(block => (block.BeginMarker, block.EndMarker)).ToList(),
                    AllowedCommentLines = markers.SelectMany(marker => marker.AllowedCommentLines)
                        .Distinct(StringComparer.Ordinal).ToList(),
                    InformationalCommentLines = markers.SelectMany(marker => marker.InformationalCommentLines)
                        .Distinct(StringComparer.Ordinal).ToList()
                }).ToList();
            merged.Validate();
            return merged;
        }).ToArray();
    }

    private static void Check(string root, IReadOnlyList<PatchOwnershipPolicy> policies,
        IReadOnlyDictionary<string, ContentComponentState>? components, IReadOnlyList<BackupRecord>? journal,
        ContentPackageCatalog catalog, KnownAircraftBaselines? baselines = null)
    {
        ValidateNamespaces(root, policies);
        foreach (var policy in policies)
        {
            foreach (var evidence in policy.StandaloneEvidencePaths)
            {
                var path = ContentPatchPathSafety.ResolveTarget(root, evidence, "Standalone evidence");
                if (File.Exists(path) || Directory.Exists(path)) throw Unowned(policy, evidence);
            }
            var markedTargets = policy.MarkerNamespaces.Where(marker => File.Exists(
                    ContentPatchPathSafety.ResolveTarget(root, marker.RelativePath, "Marker target"))
                    && ContainsNamespaceEvidence(File.ReadAllText(ContentPatchPathSafety.ResolveTarget(root, marker.RelativePath, "Marker target")),
                        marker)).Select(marker => marker.RelativePath);
            var signatureTargets = policy.Signatures.Where(signature => File.Exists(
                    ContentPatchPathSafety.ResolveTarget(root, signature.RelativePath, "Signature target"))
                    && ContainsSignature(File.ReadAllText(ContentPatchPathSafety.ResolveTarget(root, signature.RelativePath, "Signature target")),
                        signature.Text)).Select(signature => signature.RelativePath);
            var payloads = policy.PayloadPaths.SelectMany(pattern => Expand(root, pattern)).ToHashSet(StringComparer.Ordinal);
            var results = policy.ResultFiles.Where(file => file.Sha256.Contains(FileHash(root, file.RelativePath), StringComparer.OrdinalIgnoreCase))
                .Select(file => file.RelativePath);
            var ownedFiles = (components?.Values ?? []).Where(component => Owns(component, policy, catalog))
                .SelectMany(component => component.Files.Select(file => file.RelativePath))
                .Where(relative => policy.TargetPaths.Any(pattern => PatchOwnershipPolicy.PatternCovers(pattern, relative)))
                .ToArray();
            foreach (var scope in policy.TargetPaths.Where(path => path.EndsWith("/**", StringComparison.Ordinal)))
                foreach (var relative in Expand(root, scope))
                    if (!payloads.Contains(relative)
                        && !policy.OriginalFiles.Any(file => file.RelativePath == relative
                            && file.Sha256.Contains(FileHash(root, relative), StringComparer.OrdinalIgnoreCase))
                        && !ownedFiles.Contains(relative, StringComparer.Ordinal))
                        throw new InvalidOperationException($"Unknown file in reserved patch scope: {relative}.");
            foreach (var relative in markedTargets.Concat(signatureTargets).Concat(payloads).Concat(results)
                .Concat(ownedFiles).Distinct(StringComparer.Ordinal))
            {
                var exists = File.Exists(ContentPatchPathSafety.ResolveTarget(root, relative, "Patch evidence"));
                var owners = components?.Values.Where(component => Owns(component, policy, catalog)
                    && component.Files.Any(file => file.RelativePath == relative)).ToArray() ?? [];
                if (owners.Length == 0)
                {
                    if (exists) throw Unowned(policy, relative);
                    continue;
                }
                var related = components!.Values.Where(component => component.Files.Any(file => file.RelativePath == relative)).ToArray();
                foreach (var file in related.SelectMany(component => component.Files.Where(file => file.RelativePath == relative)))
                    if (!Path.GetFullPath(file.TargetPath).Equals(ContentPatchPathSafety.ResolveTarget(root, relative, "Owned target"),
                        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException($"Recorded patch target is inconsistent: {relative}.");
                // Reuse the verified MTK hash-chain traversal, including immutable journal edges.
                var currentBytes = exists ? File.ReadAllBytes(ContentPatchPathSafety.ResolveTarget(root, relative, "Owned source")) : null;
                var baseline = currentBytes is null ? null : policy.SupportedProducts
                    .Select(product => (baselines ?? KnownAircraftBaselines.BuiltIn).Match(product, relative, currentBytes))
                    .FirstOrDefault(match => match is not null);
                // A verified official preimage can be backed up anew. An adopted
                // patch output, even with an identical hash, cannot provide that proof.
                if (related.Any(component => !component.RestoreAvailable) && baseline is null)
                    throw new InvalidOperationException($"No complete restore chain is available for {relative}.");
                var fileOwners = related.ToDictionary(component => component.ComponentId, component => new ContentComponentState
                {
                    ComponentId = component.ComponentId, RestoreAvailable = component.RestoreAvailable,
                    InstalledUtc = component.InstalledUtc,
                    Files = component.Files.Where(file => file.RelativePath == relative).ToList()
                }, StringComparer.Ordinal);
                ContentComponentFileState? proof = null;
                if (related.Length == 1)
                {
                    var owned = related[0].Files.Single(file => file.RelativePath == relative);
                    // A current single-owner generation has a direct, complete
                    // edge to its original. Repeated selections can revisit an
                    // output hash; older journal edges are not its restore chain.
                    var currentMatches = currentBytes is null ||
                        (owned.InstalledSizeBytes == currentBytes.LongLength
                            && string.Equals(owned.InstalledSha256, Hash(currentBytes), StringComparison.OrdinalIgnoreCase));
                    if (currentMatches && (!owned.OriginalExisted || BackupMatches(owned))) proof = owned;
                }
                if (proof is null)
                {
                    proof = CatalogGroupMigration.Prepare(root, new CompatibilityPackageManifest
                    {
                        PackageId = "ownership-proof",
                        Sources = related.Select(component => new ResolvedCatalogSource { PackageId = component.ComponentId }).ToList(),
                        Modules = [new() { Targets = [new() { RelativePath = relative }] }]
                    }, fileOwners, journal, verifiedBaselines: baseline is null ? null
                        : new Dictionary<string, KnownAircraftBaseline> { [relative] = baseline })?.Files.Single(file => file.RelativePath == relative)
                        ?? throw Unowned(policy, relative);
                }
                if (proof.OriginalExisted && policy.ResultFiles.Any(file => file.RelativePath == relative
                    && file.Sha256.Contains(proof.OriginalSha256, StringComparer.OrdinalIgnoreCase)))
                    throw Unowned(policy, relative);
                if (payloads.Contains(relative) && proof.OriginalExisted
                    && !policy.OriginalFiles.Any(file => file.RelativePath == relative
                        && file.Sha256.Contains(proof.OriginalSha256, StringComparer.OrdinalIgnoreCase)))
                    throw Unowned(policy, relative);
                if (proof.OriginalExisted && (policy.MarkerNamespaces.Any(marker => marker.RelativePath == relative)
                    || policy.Signatures.Any(signature => signature.RelativePath == relative)))
                {
                    var originalPath = string.IsNullOrWhiteSpace(proof.BackupPath)
                        ? ContentPatchPathSafety.ResolveTarget(root, relative, "Verified original") : proof.BackupPath;
                    _ = HashFile(originalPath);
                    var original = File.ReadAllText(originalPath);
                    if (policy.MarkerNamespaces.Any(marker => marker.RelativePath == relative
                            && ContainsNamespaceEvidence(original, marker))
                        || policy.Signatures.Any(signature => signature.RelativePath == relative
                            && ContainsSignature(original, signature.Text)))
                        throw Unowned(policy, relative);
                }
            }
        }
    }

    private static bool BackupMatches(ContentComponentFileState file) =>
        !string.IsNullOrWhiteSpace(file.BackupPath) && File.Exists(file.BackupPath)
        && new FileInfo(file.BackupPath).Length == file.OriginalSizeBytes
        && string.Equals(HashFile(file.BackupPath), file.OriginalSha256, StringComparison.OrdinalIgnoreCase);

    private static bool Owns(ContentComponentState component, PatchOwnershipPolicy policy, ContentPackageCatalog catalog)
    {
        if (component.ComponentId == policy.PackageId) return true;
        foreach (var source in component.Sources.Where(source => source.PackageId == policy.PackageId
            && policy.ModuleIds.Contains(source.ModuleId, StringComparer.Ordinal)
            && component.EnabledModules.Contains(source.ModuleId, StringComparer.Ordinal)))
        {
            if (source.RepositoryUrl.Length > 0 && !source.RepositoryUrl.TrimEnd('/')
                .Equals(policy.RepositoryUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Recorded patch source differs from its catalog ownership contract.");
            return true;
        }
        // Older Core states had module selections but no resolved source list.
        // Resolve those owners through catalog membership, then prove the same
        // file and backup chain as for newer states. No hash-only adoption.
        return component.Sources.Count == 0 && catalog.Packages.Any(group =>
            group.PackageId == component.ComponentId
            && group.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup
            && group.Members.Any(member => member.PackageId == policy.PackageId
                && policy.ModuleIds.Contains(member.ModuleId, StringComparer.Ordinal)
                && component.EnabledModules.Contains(member.ModuleId, StringComparer.Ordinal)));
    }

    private static void ValidateNamespaces(string root, IEnumerable<PatchOwnershipPolicy> policies)
    {
        foreach (var marker in policies.SelectMany(policy => policy.MarkerNamespaces))
        {
            var path = ContentPatchPathSafety.ResolveTarget(root, marker.RelativePath, "Marker namespace");
            if (!File.Exists(path)) continue;
            var stack = new Stack<PatchMarkerPair>();
            var counts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadLines(path))
            {
                if (marker.InformationalCommentLines.Contains(raw.Trim(), StringComparer.Ordinal)) continue;
                var comment = raw.IndexOf("--", StringComparison.Ordinal);
                if (comment < 0) continue;
                var line = raw[comment..].Trim();
                if (!line.Contains(marker.Namespace, StringComparison.OrdinalIgnoreCase)) continue;
                var begin = marker.Blocks.FirstOrDefault(block => block.BeginMarker == line);
                var end = marker.Blocks.FirstOrDefault(block => block.EndMarker == line);
                if ((begin is not null || end is not null) && !counts.Add(line))
                    throw new InvalidOperationException($"Duplicate {marker.Namespace} marker in {marker.RelativePath}.");
                if (begin is not null) stack.Push(begin);
                else if (end is not null)
                {
                    if (!stack.TryPop(out var current) || current.EndMarker != line)
                        throw new InvalidOperationException($"Broken {marker.Namespace} block in {marker.RelativePath}.");
                }
                else if (!marker.AllowedCommentLines.Contains(line, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Unknown {marker.Namespace} marker in {marker.RelativePath}.");
            }
            if (stack.Count != 0)
                throw new InvalidOperationException($"Incomplete {marker.Namespace} block in {marker.RelativePath}.");
        }
    }

    private static bool Touches(PatchOwnershipPolicy policy, IEnumerable<string> targets) =>
        targets.Any(path => policy.TargetPaths.Any(pattern => PatchOwnershipPolicy.PatternCovers(pattern, path)));

    private static bool ContainsNamespaceEvidence(string text, PatchMarkerNamespace marker)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            if (!marker.InformationalCommentLines.Contains(line.Trim(), StringComparer.Ordinal)
                && line.Contains(marker.Namespace, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool ContainsSignature(string text, string signature) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Contains(signature.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

    private static Dictionary<string, string?> Capture(string root, IEnumerable<PatchOwnershipPolicy> policies)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pattern in policies.SelectMany(policy => policy.TargetPaths.Concat(policy.PayloadPaths)
            .Concat(policy.StandaloneEvidencePaths)).Distinct(StringComparer.Ordinal))
        {
            foreach (var relative in Expand(root, pattern)) result[relative] = FileHash(root, relative);
            if (!pattern.Contains('*')) result.TryAdd(pattern, FileHash(root, pattern));
        }
        return result;
    }

    private static IEnumerable<string> Expand(string root, string pattern)
    {
        PatchOwnershipPolicy.ValidatePattern(pattern);
        if (!pattern.Contains('*'))
        {
            var literal = ContentPatchPathSafety.ResolveTarget(root, pattern, "Ownership file");
            var parent = Path.GetDirectoryName(literal)!;
            if (Directory.Exists(parent))
            {
                var aliases = Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName)
                    .Where(name => string.Equals(name, Path.GetFileName(literal), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (aliases.Length > 1 || aliases.Any(name => name != Path.GetFileName(literal)))
                    throw new InvalidOperationException($"Case-colliding patch path: {pattern}.");
            }
            return [pattern];
        }
        var scope = pattern.EndsWith("/**", StringComparison.Ordinal);
        var directoryRelative = scope ? pattern[..^3] : pattern[..pattern.LastIndexOf('/')];
        var directory = ContentPatchPathSafety.ResolveTarget(root, directoryRelative, "Ownership directory");
        if (File.Exists(directory)) throw new InvalidOperationException($"Ownership directory is occupied by a file: {directoryRelative}.");
        if (!Directory.Exists(directory)) return [];
        var result = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var relative = directoryRelative + "/" + Path.GetFileName(entry);
            if (!scope && !PatchOwnershipPolicy.PatternCovers(pattern, relative)) continue;
            if ((File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || !names.Add(Path.GetFileName(entry)))
                throw new InvalidOperationException($"Patch evidence contains a directory, link or case collision: {relative}.");
            _ = ContentPatchPathSafety.ResolveTarget(root, relative, "Patch evidence");
            result.Add(relative);
        }
        return result;
    }

    private static Dictionary<string, string> CaptureBackups(ContentInstallationToolState? installation,
        IEnumerable<string> targets)
    {
        var paths = targets.ToHashSet(StringComparer.Ordinal);
        var files = installation?.ContentComponents.Values.SelectMany(component => component.Files)
            .Where(file => paths.Contains(file.RelativePath) && file.OriginalExisted) ?? [];
        var backups = files.Select(file => file.BackupPath).Concat((installation?.Backups ?? [])
            .Where(record => record.Operation.StartsWith("ContentPatch", StringComparison.Ordinal))
            .Select(record => record.BackupPath));
        return backups.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, HashFile, StringComparer.Ordinal);
    }

    private static string StateFingerprint(ContentInstallationToolState? state) => Hash(JsonSerializer.SerializeToUtf8Bytes(
        new { state?.ContentComponents, Backups = state?.Backups.Where(record => record.Operation.StartsWith("ContentPatch", StringComparison.Ordinal)) }));
    private static string CatalogFingerprint(ContentPackageCatalog catalog) => Hash(JsonSerializer.SerializeToUtf8Bytes(
        new { catalog.SchemaVersion, catalog.CatalogVersion, catalog.OwnershipPolicies, Groups = catalog.Packages.Where(entry => entry.Distribution.Kind == ContentPackageDistributionKind.CatalogGroup) }));
    private static void RequireSchema(ContentPackageCatalog catalog)
    {
        if (catalog.SchemaVersion != 2)
            throw new InvalidOperationException("This catalog has no patch ownership contract. Refresh the catalog before changing aircraft files.");
    }
    private static string? FileHash(string root, string relative)
    {
        var path = ContentPatchPathSafety.ResolveTarget(root, relative, "Ownership check");
        if (Directory.Exists(path)) throw new InvalidOperationException($"Expected a patch file, found a directory: {relative}.");
        return File.Exists(path) ? HashFile(path) : null;
    }
    private static string HashFile(string path)
    {
        // Aircraft ancestors are checked by ResolveTarget; keep the chosen
        // storage root usable even when the OS exposes it through an alias.
        if (new FileInfo(path).LinkTarget is not null)
            throw new InvalidOperationException("Patch evidence or backup is a symbolic link.");
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void VerifyBackups(IReadOnlyDictionary<string, string> backups)
    {
        foreach (var backup in backups)
            if (!File.Exists(backup.Key) || HashFile(backup.Key) != backup.Value)
                throw new InvalidOperationException("A patch backup changed after planning. Review the operation again.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool EqualHashes(IReadOnlyDictionary<string, string?> a, IReadOnlyDictionary<string, string?> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && pair.Value == value);
    private static InvalidOperationException Unowned(PatchOwnershipPolicy policy, string evidence) =>
        new($"{policy.DisplayName} has patch files or standalone state at {evidence}, but no verified MTK ownership and complete restore chain. "
            + $"{policy.RecoveryInstruction} Standalone remains supported. Do not delete state or backups by hand.");
    internal static bool IsVerificationFailure(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException;
}
