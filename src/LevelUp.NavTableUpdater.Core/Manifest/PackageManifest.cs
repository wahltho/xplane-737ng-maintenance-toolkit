namespace LevelUp.NavTableUpdater.Core.Manifest;

public sealed record PackageManifest(
    int SchemaVersion,
    string PackageId,
    string PackageVersion,
    string ReleaseTag,
    string ReleaseChannel,
    string RepositoryUrl,
    string AircraftFamily,
    string TargetRelativePath,
    IReadOnlyList<PayloadFile> Payloads,
    IReadOnlyList<PatchOperation> PatchOperations);

public sealed record PayloadFile(
    string Id,
    string FileName,
    long Size,
    string Sha256);

public sealed record PatchOperation(
    string Id,
    string Anchor,
    string BeginMarker,
    string EndMarker,
    IReadOnlyList<string> LegacySignatures);
