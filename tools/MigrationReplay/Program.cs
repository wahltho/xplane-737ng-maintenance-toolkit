using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Upstream;

if (args.Length == 4 && args[0] == "--restore-active-group")
{
    var root = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(root, "03-after-delta.state.json"))) throw new Exception("Not an isolated replay scenario.");
    var aircraft = Path.Combine(root, "aircraft");
    var variant = new AircraftViewAnalyzer().Analyze(aircraft).Variants.First() with { Family = "levelup-737ng" };
    var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
    var operation = new CompatibilityPackageOperation(store, () => false);
    string[] activeSelection = ["vnav", "fans-cdu", "weight-and-balance"];
    var apply = await operation.RunAsync(ContentPatchAction.Update, variant, args[2], activeSelection);
    if (!apply.Succeeded) throw new Exception(apply.Message);
    var restore = new AircraftUpdateOperation(store, isXPlaneRunning: () => false).RestoreLatest(variant);
    File.WriteAllLines(Path.Combine(root, "delta-restore-active-group.log"), restore.Log);
    if (!restore.Succeeded) throw new Exception(restore.Message);
    var reapply = await operation.RunAsync(ContentPatchAction.Update, variant, args[2], activeSelection);
    File.WriteAllLines(Path.Combine(root, "group-after-active-restore.log"), reapply.Log);
    if (!reapply.Succeeded) throw new Exception(reapply.Message);
    var repeat = await operation.RunAsync(ContentPatchAction.Update, variant, args[2], activeSelection);
    if (!repeat.Succeeded || repeat.Changed) throw new Exception(repeat.Message);
    if (!operation.Restore(variant, args[2]).Succeeded) throw new Exception("Group restore failed.");
    using var archive = ZipFile.OpenRead(args[3]);
    foreach (var relative in new[] { "objects/737_cockpit_ovhd2.obj", "plugins/xlua/scripts/B738.tablet/B738.tablet.lua" })
    {
        using var stream = archive.GetEntry(relative)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        if (!File.ReadAllBytes(Path.Combine(aircraft, relative)).SequenceEqual(memory.ToArray())) throw new Exception("Restore mismatch: " + relative);
    }
    Console.WriteLine("PASS delta restore with active group: partial standalone ownership merged; reapply, repeat, original object/tablet restore verified.");
    return;
}

if (args.Length == 4 && args[0] == "--legacy-recovery")
{
    // Clone captured production evidence, relocating paths only. No expected hash
    // or patch-history record is constructed or changed by this recovery probe.
    var source = Path.GetFullPath(args[1]);
    var destination = Path.GetFullPath(args[2]);
    if (Directory.Exists(destination)) throw new InvalidOperationException("Output must be new.");
    if (!File.Exists(Path.Combine(source, "03-after-delta.state.json"))) throw new InvalidOperationException("Missing captured legacy state.");
    foreach (var sub in new[] { "aircraft", "backups", "state" })
    {
        foreach (var file in Directory.GetFiles(Path.Combine(source, sub), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
    var variant = new AircraftViewAnalyzer().Analyze(Path.Combine(source, "aircraft")).Variants.First() with { Family = "levelup-737ng" };
    var oldStore = new ToolStateStore(Path.Combine(source, "state"), Path.Combine(source, "backups"));
    // The diagnostic restore probe saved the exact post-delta bytes as preimages.
    // Restore those captured bytes in the clone to match 03-after-delta.state.json.
    foreach (var record in oldStore.TryGetProductTarget(variant)!.Backups.Where(r => r.Operation == "AircraftUpdateRestorePreImage"))
    {
        var target = record.SourcePath.Replace(source, destination, StringComparison.Ordinal);
        if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new Exception("Evidence path outside clone.");
        if (record.SourceExisted) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(record.BackupPath, target, true); }
    }
    var store = new ToolStateStore(Path.Combine(destination, "state"), Path.Combine(destination, "backups"));
    var legacy = JsonSerializer.Deserialize<ToolStateDocument>(File.ReadAllText(Path.Combine(source, "03-after-delta.state.json"))
        .Replace(source, destination, StringComparison.Ordinal))!;
    // Installation/product lookup keys are hashes of the physical path, so they
    // must move with it as well. Keep every state value and historical hash intact.
    string Key(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).ToLowerInvariant();
    var family = AircraftProductIdentity.FromVariant(variant).Family;
    legacy.ContentInstallations = legacy.ContentInstallations.Values.ToDictionary(i => Key(i.AircraftFolder));
    legacy.Aircraft = legacy.Aircraft.Values.ToDictionary(i => string.IsNullOrEmpty(i.AcfPath)
        ? Key($"PRODUCT|{family}|{i.AircraftFolder}") : Key(i.AcfPath));
    File.WriteAllText(store.StatePath, JsonSerializer.Serialize(legacy));
    if (store.TryGetContentInstallation(Path.Combine(destination, "aircraft"))!.ContentComponents.Count != 2)
        throw new Exception("Captured standalone ownership was not preserved during relocation.");
    var aircraft = Path.Combine(destination, "aircraft");
    variant = new AircraftViewAnalyzer().Analyze(aircraft).Variants.First() with { Family = "levelup-737ng" };
    var objectPath = Path.Combine(aircraft, "objects/737_cockpit_ovhd2.obj");
    var officialObject = File.ReadAllBytes(objectPath);
    var operation = new CompatibilityPackageOperation(store, () => false);
    string[] selection = ["vnav", "fans-cdu", "weight-and-balance"];
    var recovered = await operation.RunAsync(ContentPatchAction.Update, variant, args[3], selection);
    File.WriteAllLines(Path.Combine(destination, "legacy-recovery.log"), recovered.Log);
    if (!recovered.Succeeded) throw new Exception(recovered.Message);
    Console.WriteLine("PASS captured 0.13.12 state: required group installed.");
    var repeated = await operation.RunAsync(ContentPatchAction.Update, variant, args[3], selection);
    if (!repeated.Succeeded || repeated.Changed) throw new Exception(repeated.Message);
    Console.WriteLine("PASS captured 0.13.12 state: repeat unchanged.");
    var optional = await operation.RunAsync(ContentPatchAction.Update, variant, args[3], [.. selection, "tablet-performance-calculator"]);
    File.WriteAllLines(Path.Combine(destination, "optional-enable.log"), optional.Log);
    if (!optional.Succeeded) throw new Exception(optional.Message);
    var deselected = await operation.RunAsync(ContentPatchAction.Update, variant, args[3], selection);
    File.WriteAllLines(Path.Combine(destination, "optional-disable.log"), deselected.Log);
    if (!deselected.Succeeded) throw new Exception(deselected.Message);
    Console.WriteLine("PASS captured 0.13.12 state: optional performance enabled and removed.");
    var restore = operation.Restore(variant, args[3]);
    if (!restore.Succeeded) throw new Exception(restore.Message);
    if (!File.ReadAllBytes(objectPath).SequenceEqual(officialObject)) throw new Exception("Restore lost official delta object.");
    Console.WriteLine("PASS captured 0.13.12 state: restore preserves official delta object.");
    return;
}

if (args.Length == 4 && args[0] == "--complete-controls")
{
    var replayRoot = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(replayRoot, "results.json"))) throw new InvalidOperationException("Not a completed replay.");
    using var baseline = ZipFile.OpenRead(args[3]);
    using var input = baseline.GetEntry("plugins/xlua/scripts/B738.tablet/B738.tablet.lua")!.Open();
    using var expected = new MemoryStream();
    input.CopyTo(expected);
    foreach (var scenario in new[] { "linear", "clean-original" })
    {
        var root = Path.Combine(replayRoot, scenario);
        if (!File.Exists(Path.Combine(root, "delta-restore.log"))) throw new InvalidOperationException("Run restore probe first.");
        var aircraft = Path.Combine(root, "aircraft");
        var variant = new AircraftViewAnalyzer().Analyze(aircraft).Variants.First() with { Family = "levelup-737ng" };
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
        var operation = new CompatibilityPackageOperation(store, () => false);
        var apply = await operation.RunAsync(ContentPatchAction.Update, variant, args[2], ["vnav", "fans-cdu", "weight-and-balance"]);
        File.WriteAllLines(Path.Combine(root, "control-apply.log"), apply.Log);
        if (!apply.Succeeded) throw new Exception(apply.Message);
        var repeat = await operation.RunAsync(ContentPatchAction.Update, variant, args[2], ["vnav", "fans-cdu", "weight-and-balance"]);
        File.WriteAllLines(Path.Combine(root, "control-repeat.log"), repeat.Log);
        if (!repeat.Succeeded || repeat.Changed) throw new Exception(repeat.Message);
        var restore = operation.Restore(variant, args[2]);
        File.WriteAllLines(Path.Combine(root, "control-restore.log"), restore.Log);
        if (!restore.Succeeded) throw new Exception(restore.Message);
        if (!File.ReadAllBytes(Path.Combine(aircraft, "plugins/xlua/scripts/B738.tablet/B738.tablet.lua")).SequenceEqual(expected.ToArray()))
            throw new Exception("Restored tablet does not match original.");
        Console.WriteLine($"CONTROL {scenario}: group apply, unchanged repeat and byte-exact original tablet restore succeeded.");
    }
    return;
}

if (args.Length == 3 && args[0] == "--restore-probe")
{
    // Only point this mode at an isolated replay output created below.
    var replayRoot = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(replayRoot, "results.json"))) throw new InvalidOperationException("Not a completed replay.");
    foreach (var scenario in new[] { "linear", "refresh-first", "clean-original", "clean-original-missing-backups" })
    {
        var root = Path.Combine(replayRoot, scenario);
        var aircraft = Path.Combine(root, "aircraft");
        var variant = new AircraftViewAnalyzer().Analyze(aircraft).Variants.First() with { Family = "levelup-737ng" };
        var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
        var restore = new AircraftUpdateOperation(store, isXPlaneRunning: () => false).RestoreLatest(variant);
        File.WriteAllLines(Path.Combine(root, "delta-restore.log"), restore.Log);
        if (!restore.Succeeded) throw new Exception(restore.Message);
        var plan = await new CompatibilityPackageOperation(store, () => false).PlanAsync(
            ContentPatchAction.Update, variant, args[2], ["vnav", "fans-cdu", "weight-and-balance"]);
        File.WriteAllLines(Path.Combine(root, "group-after-delta-restore.log"), plan.Log.Append(plan.StatusMessage));
        Console.WriteLine($"RESTORE-PROBE {scenario}: safe={plan.IsSafe}: {plan.StatusMessage}");
    }
    return;
}

// Evidence harness: only the supplied NEW output directory is modified.
// Arguments: output, baseline-subset.zip, FANS package, performance package,
// catalog group, delta manifest. Input packages are loaded by production validators.
if (args.Length != 6) throw new ArgumentException("Expected six paths; see source header.");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new InvalidOperationException("Output must be a new directory.");
Directory.CreateDirectory(output);
var baselineZip = Path.GetFullPath(args[1]);
var fansPackage = Path.GetFullPath(args[2]);
var performancePackage = Path.GetFullPath(args[3]);
var groupPackage = Path.GetFullPath(args[4]);
var deltaManifest = Path.GetFullPath(args[5]);
const string tablet = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";
string[] required = ["vnav", "fans-cdu", "weight-and-balance"];
var rows = new List<object>();
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
void Require(bool condition, string message)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    if (!condition) throw new Exception(message);
}
foreach (var scenario in new[] { "linear", "refresh-first", "clean-original", "clean-original-missing-backups" })
{
    var root = Path.Combine(output, scenario);
    var aircraft = Path.Combine(root, "aircraft");
    ZipFile.ExtractToDirectory(baselineZip, aircraft);
    var variant = new AircraftViewAnalyzer().Analyze(aircraft).Variants.First() with
    {
        Family = "levelup-737ng", LocalVersion = "2.S1.50"
    };
    var store = new ToolStateStore(Path.Combine(root, "state"), Path.Combine(root, "backups"));
    var compatibility = new CompatibilityPackageOperation(store, () => false);
    var declarative = new DeclarativeContentPatchOperation(store, () => false);
    var clean = File.ReadAllBytes(Path.Combine(aircraft, tablet));

    void Snapshot(string label)
    {
        File.Copy(store.StatePath, Path.Combine(root, label + ".state.json"));
        var currentHash = Hash(File.ReadAllBytes(Path.Combine(aircraft, tablet)));
        var records = store.TryGetContentInstallation(aircraft)!.ContentComponents.Values
            .SelectMany(c => c.Files.Where(f => f.RelativePath == tablet).Select(f => new
            {
                c.ComponentId, f.OriginalSha256, f.InstalledSha256,
                BackupExists = File.Exists(f.BackupPath),
                BackupVerified = File.Exists(f.BackupPath) && Hash(File.ReadAllBytes(f.BackupPath)) == f.OriginalSha256
            })).ToArray();
        File.WriteAllText(Path.Combine(root, label + ".tablet.json"), JsonSerializer.Serialize(new { currentHash, records }, new JsonSerializerOptions { WriteIndented = true }));
    }

    var performance = await compatibility.RunAsync(ContentPatchAction.Install, variant, performancePackage, ["tablet-performance-calculator"]);
    Require(performance.Succeeded, scenario + " install released performance: " + performance.Message);
    var fans = await declarative.RunAsync(ContentPatchAction.Install, variant, fansPackage);
    Require(fans.Succeeded, scenario + " install released FANS: " + fans.Message);
    Snapshot("01-linear");
    var before = await compatibility.PlanAsync(ContentPatchAction.Update, variant, groupPackage, required);
    Require(before.IsSafe, scenario + " initial group migration is valid: " + before.StatusMessage);

    if (scenario == "refresh-first")
    {
        var refreshed = await compatibility.RunAsync(ContentPatchAction.Update, variant, performancePackage, ["tablet-performance-calculator"]);
        Require(refreshed.Succeeded && !refreshed.Changed, "first patch refresh reports success without aircraft changes");
        File.WriteAllLines(Path.Combine(root, "refresh.log"), refreshed.Log);
    }
    if (scenario.StartsWith("clean-original"))
    {
        // Explicit external action, modelling replacement of original aircraft files.
        ZipFile.ExtractToDirectory(baselineZip, aircraft, overwriteFiles: true);
        if (scenario.EndsWith("missing-backups"))
        {
            // Delete ONLY backups generated inside this new isolated scenario.
            foreach (var file in Directory.GetFiles(Path.Combine(root, "backups"), "*", SearchOption.AllDirectories)) File.Delete(file);
        }
    }
    Snapshot("02-before-delta");
    var beforeDeltaHash = Hash(File.ReadAllBytes(Path.Combine(aircraft, tablet)));
    var selection = new LevelUpAircraftUpdatePackageLoader().Load(deltaManifest, variant);
    var package = selection.Package ?? throw new Exception("Delta package unavailable.");
    using (var stream = File.OpenRead(selection.ArchivePath!))
    {
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Require(hash == package.ExpectedSha256 && stream.Length == package.ExpectedSizeBytes, scenario + " actual delta archive checksum");
    }
    var entry = new AircraftUpdatePackageCacheEntry(package, selection.ArchivePath!, AircraftUpdatePackageCacheState.Cached, package.ExpectedSizeBytes, package.ExpectedSha256);
    var updated = new AircraftUpdateOperation(store, isXPlaneRunning: () => false).Apply(variant, selection.UpdateCheck, [entry]);
    Require(updated.Succeeded, scenario + " actual S1.51C delta applied: " + updated.Message);
    File.WriteAllLines(Path.Combine(root, "delta.log"), updated.Log);
    Require(Hash(File.ReadAllBytes(Path.Combine(aircraft, tablet))) == beforeDeltaHash, scenario + " delta leaves tablet bytes unchanged");
    Snapshot("03-after-delta");
    var stateBefore = File.ReadAllBytes(store.StatePath);
    var result = await compatibility.RunAsync(ContentPatchAction.Update, variant, groupPackage, required);
    File.WriteAllLines(Path.Combine(root, "group.log"), result.Log);
    Console.WriteLine($"RESULT {scenario}: success={result.Succeeded}, changed={result.Changed}: {result.Message}");
    rows.Add(new { scenario, result.Succeeded, result.Changed, result.Message });
    if (!result.Succeeded)
    {
        Require(stateBefore.SequenceEqual(File.ReadAllBytes(store.StatePath)), scenario + " blocked migration leaves state untouched");
        Require(Hash(File.ReadAllBytes(Path.Combine(aircraft, tablet))) == beforeDeltaHash, scenario + " blocked migration leaves tablet untouched");
    }
    else
    {
        var repeat = await compatibility.RunAsync(ContentPatchAction.Update, variant, groupPackage, required);
        Require(repeat.Succeeded && !repeat.Changed, scenario + " group repeat unchanged");
        var restored = compatibility.Restore(variant, groupPackage);
        Require(restored.Succeeded, scenario + " group restore: " + restored.Message);
        Require(File.ReadAllBytes(Path.Combine(aircraft, tablet)).SequenceEqual(clean), scenario + " restore returns original tablet bytes");
    }
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
