using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

internal static class OwnershipTestCatalog
{
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static PatchOwnershipPolicy Policy(string id, string repository, string[] targets, string[]? modules = null) => new()
    {
        PackageId = id, DisplayName = id, RepositoryUrl = repository,
        SupportedProducts = ["levelup-737ng"], ModuleIds = [.. modules ?? []], TargetPaths = [.. targets],
        StandaloneEvidencePaths = [$".test-ownership/{id}.json"], RecoveryInstruction = "Use the fixture's restore procedure."
    };

    public static ContentPackageCatalog Create(params PatchOwnershipPolicy[] policies)
    {
        var bundled = ContentPackageCatalog.LoadBundled();
        var document = new ContentPackageCatalogDocument
        {
            SchemaVersion = 2, CatalogVersion = bundled.CatalogVersion,
            MinimumToolkitVersion = bundled.MinimumToolkitVersion,
            Packages = bundled.Packages.ToList(),
            OwnershipPolicies = bundled.OwnershipPolicies.Where(rule => !policies.Any(p => p.PackageId == rule.PackageId))
                .Concat(policies).ToList()
        };
        return Parse(document);
    }

    public static ContentPackageCatalog Parse(ContentPackageCatalogDocument document) => ContentPackageCatalog.Parse(
        JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        }));

    public static ContentPatchPlan Bind(ContentPatchPlan plan, ToolStateStore store,
        AircraftVariantViewAnalysis variant, ContentPackageCatalog catalog) => ContentPatchOwnershipVerifier.BuildPlanAsync(
            plan.Action, variant, plan.Descriptor, plan.PackageVersion, store, () => catalog,
            plan.Mutations.Select(mutation => mutation.RelativePath), () => Task.FromResult(plan)).GetAwaiter().GetResult();

    public static ContentPackageCatalog Compatibility => Create(
        Policy("levelup.compatibility", "https://github.com/example/levelup-compatibility",
            ["plugins/xlua/scripts/shared.lua", "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua", "plugins/xlua/scripts/table.lua"],
            ["core", "standard", "optional", "table-payload", "hardening", "loader", "dependent"]),
        Policy("independent.compatibility", "https://github.com/example/independent-compatibility",
            ["plugins/xlua/scripts/shared.lua"], ["independent"]),
        Policy("source.core", "https://github.com/example/core", ["plugins/xlua/scripts/shared.lua"], ["core"]),
        Policy("new.group", "https://github.com/example/levelup-compatibility",
            ["plugins/xlua/scripts/shared.lua"], ["core", "standard", "optional"]));

    public static ContentPackageCatalog Engine => Create(
        Policy("test.optional", "https://github.com/example/test",
            ["existing.txt", "created.txt", "target.txt", "first.txt", "cannot-replace-directory", "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua"]),
        Policy("first", "https://github.com/example/test", ["shared.lua"]),
        Policy("second", "https://github.com/example/test", ["shared.lua"]),
        Policy("group", "https://github.com/example/test", ["shared.lua", "write-failure"]));
}
