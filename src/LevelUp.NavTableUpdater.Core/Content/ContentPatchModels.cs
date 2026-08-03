using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Content;

public enum ContentPatchAction
{
    Install,
    Update,
    Repair,
    Uninstall
}

public enum ContentPatchActivation
{
    Managed,
    ExplicitOptIn
}

public enum ContentPatchTrigger
{
    Manual,
    AfterAircraftUpdate
}

public sealed record ContentPatchLifecyclePolicy(
    ContentPatchActivation Activation,
    IReadOnlySet<ContentPatchTrigger> Triggers)
{
    public bool MayOfferAutomatically(ContentPatchTrigger trigger) =>
        Activation is ContentPatchActivation.Managed && Triggers.Contains(trigger);
}

public sealed record ContentPatchDescriptor(
    string ComponentId,
    string DisplayName,
    string RepositoryUrl,
    ContentPatchLifecyclePolicy Lifecycle,
    bool RestartRequired);

public enum ContentPatchMutationKind
{
    Write,
    Delete
}

public sealed record ContentPatchMutation(
    string RelativePath,
    ContentPatchMutationKind Kind,
    byte[]? DesiredBytes,
    string Description)
{
    public static ContentPatchMutation Write(string relativePath, byte[] bytes, string description) =>
        new(relativePath, ContentPatchMutationKind.Write, bytes, description);

    public static ContentPatchMutation Delete(string relativePath, string description) =>
        new(relativePath, ContentPatchMutationKind.Delete, DesiredBytes: null, description);
}

public sealed record ContentPatchPlan(
    ContentPatchDescriptor Descriptor,
    string PackageVersion,
    ContentPatchAction Action,
    string AircraftRoot,
    IReadOnlyList<ContentPatchMutation> Mutations,
    IReadOnlyList<string> Log,
    bool IsSafe,
    string StatusMessage)
{
    public static ContentPatchPlan Blocked(
        ContentPatchDescriptor descriptor,
        string packageVersion,
        ContentPatchAction action,
        string aircraftRoot,
        string message,
        IReadOnlyList<string> log) =>
        new(descriptor, packageVersion, action, aircraftRoot, [], log, IsSafe: false, message);
}

public interface IContentPatchPlanBuilder<TPackage>
{
    Task<ContentPatchPlan> BuildAsync(
        ContentPatchAction action,
        AircraftVariantViewAnalysis variant,
        TPackage package,
        CancellationToken cancellationToken = default);
}
