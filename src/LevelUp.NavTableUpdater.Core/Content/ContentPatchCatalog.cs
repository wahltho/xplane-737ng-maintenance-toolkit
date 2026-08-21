namespace LevelUp.NavTableUpdater.Core.Content;

public static class ContentPatchCatalog
{
    public static ContentPatchDescriptor Vnav(string packageId, string repositoryUrl) =>
        new(
            ComponentId: packageId,
            DisplayName: "VNAV descent tables",
            RepositoryUrl: repositoryUrl,
            Lifecycle: new ContentPatchLifecyclePolicy(
                ContentPatchActivation.Managed,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual, ContentPatchTrigger.AfterAircraftUpdate }),
            RestartRequired: true);

    public static ContentPatchDescriptor FansCdu { get; } =
        new(
            ComponentId: "wahltho.levelup-737ng.fans-cdu-3d",
            DisplayName: "LevelUp FANS CDU",
            RepositoryUrl: "https://github.com/wahltho/X-Plane-LevelUp-737NG-FANS-CDU",
            Lifecycle: new ContentPatchLifecyclePolicy(
                ContentPatchActivation.ExplicitOptIn,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual }),
            RestartRequired: true);

    public static ContentPatchDescriptor OptionalPatch(ContentPackageCatalogEntry package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Category is not ContentPackageCategory.OptionalPatch
            || package.Activation is not ContentPatchActivation.ExplicitOptIn)
        {
            throw new InvalidOperationException($"Content package {package.PackageId} is not an explicit optional patch.");
        }

        return new ContentPatchDescriptor(
            package.PackageId,
            package.DisplayName,
            package.RepositoryUrl,
            new ContentPatchLifecyclePolicy(
                ContentPatchActivation.ExplicitOptIn,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual }),
            package.RestartRequired);
    }

    public static ContentPatchDescriptor CompatibilityPackage(ContentPackageCatalogEntry package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Category is not ContentPackageCategory.CompatibilityPackage
            || package.Activation is not ContentPatchActivation.Managed)
        {
            throw new InvalidOperationException($"Content package {package.PackageId} is not a managed compatibility package.");
        }

        return new ContentPatchDescriptor(
            package.PackageId,
            package.DisplayName,
            package.RepositoryUrl,
            new ContentPatchLifecyclePolicy(
                ContentPatchActivation.Managed,
                new HashSet<ContentPatchTrigger> { ContentPatchTrigger.Manual, ContentPatchTrigger.AfterAircraftUpdate }),
            package.RestartRequired);
    }

    public static bool MayOfferAfterAircraftUpdate(ContentPatchDescriptor descriptor) =>
        descriptor.Lifecycle.MayOfferAutomatically(ContentPatchTrigger.AfterAircraftUpdate);
}
