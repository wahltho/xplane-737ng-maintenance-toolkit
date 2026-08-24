using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.CompatibilityPatch.Cli;

internal static class Program
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "inspect",
        "apply",
        "uninstall",
        "restore"
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            var packageDirectory = Path.GetFullPath(options.Required("package"));
            var aircraftSelection = Path.GetFullPath(options.Required("aircraft-root"));
            var package = CompatibilityPackageLoader.LoadDirectory(packageDirectory);
            var variant = ResolveVariant(aircraftSelection, package, options.Optional("product"));
            var aircraftRoot = Path.GetDirectoryName(variant.AcfPath)
                ?? throw new InvalidOperationException("Detected aircraft variant has no product root.");
            var stateStore = options.Optional("state-root") is { } stateRoot
                ? new ToolStateStore(Path.GetFullPath(stateRoot), options.Optional("backup-root"))
                : ToolStateStore.CreateDefault(options.Optional("backup-root"));
            var selectedModules = ResolveModules(options, stateStore, aircraftRoot, package);
            var operation = new CompatibilityPackageOperation(stateStore);

            return options.Command.ToLowerInvariant() switch
            {
                "inspect" => await Inspect(operation, variant, packageDirectory, selectedModules),
                "apply" => await Apply(operation, variant, packageDirectory, selectedModules, options),
                "uninstall" => await Uninstall(operation, variant, packageDirectory, selectedModules, options),
                "restore" => Restore(operation, variant, packageDirectory, options),
                _ => throw new InvalidOperationException($"Unsupported command: {options.Command}")
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> Inspect(
        CompatibilityPackageOperation operation,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        IReadOnlyCollection<string> selectedModules)
    {
        var plan = await operation.PlanAsync(ContentPatchAction.Update, variant, packageDirectory, selectedModules);
        PrintPlan(plan);
        return plan.IsSafe ? 0 : 2;
    }

    private static async Task<int> Apply(
        CompatibilityPackageOperation operation,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        IReadOnlyCollection<string> selectedModules,
        CliOptions options)
    {
        RequireConfirmation(options);
        var result = await operation.RunAsync(ContentPatchAction.Update, variant, packageDirectory, selectedModules);
        PrintResult(result);
        return result.Succeeded ? 0 : 2;
    }

    private static async Task<int> Uninstall(
        CompatibilityPackageOperation operation,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        IReadOnlyCollection<string> selectedModules,
        CliOptions options)
    {
        RequireConfirmation(options);
        var result = await operation.RunAsync(ContentPatchAction.Uninstall, variant, packageDirectory, selectedModules);
        PrintResult(result);
        return result.Succeeded ? 0 : 2;
    }

    private static int Restore(
        CompatibilityPackageOperation operation,
        AircraftVariantViewAnalysis variant,
        string packageDirectory,
        CliOptions options)
    {
        RequireConfirmation(options);
        var result = operation.Restore(variant, packageDirectory);
        PrintResult(result);
        return result.Succeeded ? 0 : 2;
    }

    private static AircraftVariantViewAnalysis ResolveVariant(
        string aircraftSelection,
        CompatibilityPackage package,
        string? requestedProduct)
    {
        var requested = requestedProduct is null ? null : AircraftProductIds.Normalize(requestedProduct)
            ?? throw new InvalidOperationException($"Unknown product: {requestedProduct}.");
        var analysis = new AircraftViewAnalyzer().Analyze(aircraftSelection);
        var matches = analysis.Variants
            .Where(variant => AircraftProductIds.Normalize(variant.Family) is { } product
                && package.Manifest.SupportedProducts.Contains(product, StringComparer.Ordinal)
                && (requested is null || product.Equals(requested, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"No structurally supported [{string.Join(", ", package.Manifest.SupportedProducts)}] aircraft was found under {aircraftSelection}.");
        }

        var products = matches.Select(variant => AircraftProductIds.Normalize(variant.Family)!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (products.Length > 1)
        {
            throw new InvalidOperationException(
                $"Selection contains multiple supported products [{string.Join(", ", products)}]; specify --product.");
        }

        return matches.OrderBy(variant => variant.AircraftId, StringComparer.Ordinal).First();
    }

    private static IReadOnlyCollection<string> ResolveModules(
        CliOptions options,
        ToolStateStore stateStore,
        string aircraftRoot,
        CompatibilityPackage package)
    {
        if (options.Optional("modules") is { } requested)
        {
            return requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var installed = stateStore.TryGetContentInstallation(aircraftRoot)?.ContentComponents
            .GetValueOrDefault(package.Manifest.PackageId)?.EnabledModules;
        return installed is { Count: > 0 }
            ? installed
            : CompatibilityPackagePlanBuilder.DefaultSelection(package.Manifest);
    }

    private static void RequireConfirmation(CliOptions options)
    {
        if (!options.Flags.Contains("yes"))
        {
            throw new ArgumentException("Modifying commands require --yes. Run inspect first.");
        }
    }

    private static void PrintPlan(ContentPatchPlan plan)
    {
        Console.WriteLine($"Status: {(plan.IsSafe ? "Ready" : "Blocked")}");
        Console.WriteLine(plan.StatusMessage);
        Console.WriteLine($"Aircraft root: {plan.AircraftRoot}");
        Console.WriteLine($"Modules: {string.Join(", ", plan.EnabledModules)}");
        foreach (var mutation in plan.Mutations)
        {
            Console.WriteLine($"{mutation.Kind}: {mutation.RelativePath} - {mutation.Description}");
        }

        foreach (var line in plan.Log)
        {
            Console.WriteLine(line);
        }
    }

    private static void PrintResult(MaintenanceOperationResult result)
    {
        Console.WriteLine($"Status: {result.Status}");
        Console.WriteLine(result.Message);
        foreach (var backup in result.BackupPaths)
        {
            Console.WriteLine($"Backup: {backup}");
        }

        foreach (var line in result.Log)
        {
            Console.WriteLine(line);
        }
    }

    private static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || !Commands.Contains(args[0]))
        {
            throw new ArgumentException(
                "Usage: XPlane737NGPatchCli <inspect|apply|uninstall|restore> --aircraft-root <path> --package <path> [--product <id>] [--modules <id,id>] [--state-root <path>] [--backup-root <path>] [--yes]");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new ArgumentException($"Unexpected argument: {argument}");
            }

            var name = argument[2..];
            if (name.Equals("yes", StringComparison.Ordinal))
            {
                flags.Add(name);
                continue;
            }

            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option --{name} requires a value.");
            }

            if (!values.TryAdd(name, args[index]))
            {
                throw new ArgumentException($"Option --{name} was supplied more than once.");
            }
        }

        return new CliOptions(args[0], values, flags);
    }

    private sealed record CliOptions(
        string Command,
        IReadOnlyDictionary<string, string> Values,
        IReadOnlySet<string> Flags)
    {
        public string Required(string name) => Optional(name)
            ?? throw new ArgumentException($"Required option --{name} is missing.");

        public string? Optional(string name) => Values.GetValueOrDefault(name);
    }
}
