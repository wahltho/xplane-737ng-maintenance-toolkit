using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LevelUp.NavTableUpdater.Core.Diagnostics;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class DiagnosticReportExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-diagnostics-{Guid.NewGuid():N}");
    private string Aircraft => Path.Combine(_root, "Alice", "X-Plane", "Aircraft", "LevelUp");
    private ToolStateStore Store => new(Path.Combine(_root, "state"), Path.Combine(_root, "backups"));
    private string Output => Path.Combine(_root, "reports");
    private const string Relative = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";

    [Fact]
    public void Export_IsReadOnly_ContainsOnlyMetadata_AndAnonymizesEveryEntry()
    {
        var state = CreateState();
        var before = Snapshot();
        var context = Context() with
        {
            InstallLog = $"Selected: {Aircraft}\nPath: {Path.Combine(Aircraft, Relative)}\n",
            OperationLog = "HTTP 403 https://api.github.com/repos/petrolpram/737NG-Updates/releases?token=do-not-share"
        };
        var path = new DiagnosticReportExporter().Export(context, Store, Output);
        Assert.EndsWith(".zip", path);
        Assert.Equal(before.Keys.Order(), Snapshot(excludeOutput: true).Keys.Order());
        foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(new[] { "diagnostics.json", "operation-log.txt", "report.txt" }, archive.Entries.Select(e => e.FullName).Order());
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            Assert.DoesNotContain(_root, text);
            Assert.DoesNotContain("Alice", text);
            Assert.DoesNotContain("do-not-share", text);
            Assert.DoesNotContain("-- copyrighted aircraft payload", text);
        }
        using var jsonReader = new StreamReader(archive.GetEntry("diagnostics.json")!.Open());
        using var document = JsonDocument.Parse(jsonReader.ReadToEnd());
        Assert.True(document.RootElement.GetProperty("pathsAnonymized").GetBoolean());
        Assert.Equal("1.13.0", document.RootElement.GetProperty("context").GetProperty("catalogVersion").GetString());
        Assert.Contains(document.RootElement.GetProperty("files").EnumerateArray(), f =>
            f.GetProperty("path").GetString()?.Replace('\\', '/') == $"[AIRCRAFT]/{Relative}"
            && f.GetProperty("status").GetString() == "MatchesRecordedHash");
        using var logReader = new StreamReader(archive.GetEntry("operation-log.txt")!.Open());
        Assert.Contains("https://api.github.com/repos/petrolpram/737NG-Updates/releases", logReader.ReadToEnd());
        Assert.False(state.ContentInstallations.Count == 0);
    }

    [Fact]
    public void Collect_ReportsMissingChangedAndExpectedAbsentFiles_AndDamagedBackup()
    {
        var state = CreateState();
        var component = state.ContentInstallations.Values.Single().ContentComponents.Values.Single();
        component.Files.Add(new ContentComponentFileState { RelativePath = "missing.lua", InstalledSizeBytes = 3, InstalledSha256 = Hash("old") });
        component.Files.Add(new ContentComponentFileState { RelativePath = "retired.obj" });
        component.Files.Add(new ContentComponentFileState { RelativePath = "already-retired.obj" });
        Write(Path.Combine(Aircraft, "retired.obj"), "unexpected");
        Write(Path.Combine(Aircraft, Relative), "changed file");
        Write(component.Files[0].BackupPath, "damaged backup");
        Store.Save(state);
        var report = new DiagnosticReportExporter().Collect(Context(), Store);
        Assert.Contains(report.Files, f => f.Kind == "Managed file snapshot" && f.Path.EndsWith("B738.a_fms.lua") && f.Status == "DiffersFromRecordedHash");
        Assert.Contains(report.Files, f => f.Path.EndsWith("missing.lua") && f.Status == "Missing");
        Assert.Contains(report.Files, f => f.Path.EndsWith("retired.obj") && f.Status == "UnexpectedlyPresent");
        Assert.Contains(report.Files, f => f.Path.EndsWith("already-retired.obj") && f.Status == "AbsentAsRecorded");
        Assert.Contains(report.Files, f => f.Kind == "Original file backup" && f.Status == "DiffersFromRecordedHash");
        Assert.Contains(report.Limitations, l => l.Contains("later patch"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_WorksWithoutAircraft_AndWithUnreadableState(bool malformedState)
    {
        if (malformedState) Write(Store.StatePath, "{ invalid JSON");
        var report = new DiagnosticReportExporter().Collect(new DiagnosticExportContext(), Store);
        Assert.Equal(malformedState ? "Could not read" : "Not present", report.StateStatus);
        var path = new DiagnosticReportExporter().Export(new DiagnosticExportContext(), Store, Output);
        Assert.True(File.Exists(path));
        if (malformedState) Assert.Equal("{ invalid JSON", File.ReadAllText(Store.StatePath));
    }

    [Fact]
    public void Export_KeepingPathsIsExplicit_AndStillRemovesCredentials()
    {
        CreateState();
        var path = new DiagnosticReportExporter().Export(Context() with
        {
            OperationLog = "Authorization: Bearer private-token\napi_key=private-key"
        }, Store, Output, anonymizePaths: false);
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("operation-log.txt")!.Open());
        var log = reader.ReadToEnd();
        Assert.DoesNotContain("private-token", log);
        Assert.DoesNotContain("private-key", log);
        using var reportReader = new StreamReader(archive.GetEntry("report.txt")!.Open());
        Assert.Contains(Aircraft, reportReader.ReadToEnd());
    }

    [Fact]
    public void PrivacyFilter_CoversWindowsUnixUncAndEscapedJson_WithoutDestroyingRelativePaths()
    {
        var filter = new DiagnosticPrivacyFilter(true);
        filter.AddPath(@"C:\Users\Alice\X-Plane\Aircraft\737NG", "[AIRCRAFT]");
        var text = filter.Filter("""
            Selected: C:\Users\Alice\X-Plane\Aircraft\737NG\plugins\xlua\init.lua
            Other: C:\Users\Alice\X-Plane\Aircraft\737NG-copy\private.lua
            Unix: /home/bob/private/folder/file.lua
            UNC: \\private-server\share\folder\file.lua
            Relative: plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua
            Source: https://github.com/wahltho/project/releases/latest
            """);
        Assert.Contains(@"[AIRCRAFT]\plugins\xlua\init.lua", text);
        Assert.DoesNotContain("Alice", text);
        Assert.DoesNotContain("bob", text);
        Assert.DoesNotContain("private-server", text);
        Assert.Contains(Relative, text);
        Assert.Contains("https://github.com/wahltho/project/releases/latest", text);
        var node = System.Text.Json.Nodes.JsonNode.Parse("""{"path":"C:\\Users\\Alice\\other\\file","nested":["/home/bob/file"]}""")!;
        filter.FilterJson(node);
        Assert.DoesNotContain("Alice", node.ToJsonString());
        Assert.DoesNotContain("bob", node.ToJsonString());
        var secretText = filter.Filter("""{"token":"private token with spaces","password":"private-password"}""");
        Assert.DoesNotContain("private token", secretText);
        Assert.DoesNotContain("private-password", secretText);
    }

    [Fact]
    public void Collect_RejectsTraversalAndSymlinks_WithoutReadingTheirTargets()
    {
        var state = CreateState();
        var component = state.ContentInstallations.Values.Single().ContentComponents.Values.Single();
        var outside = Path.Combine(_root, "outside.lua");
        Write(outside, "outside content");
        component.Files.Add(new ContentComponentFileState { RelativePath = "../../outside.lua", InstalledSha256 = Hash("outside content") });
        Directory.CreateSymbolicLink(Path.Combine(Aircraft, "linked"), _root);
        component.Files.Add(new ContentComponentFileState { RelativePath = "linked/outside.lua", InstalledSha256 = Hash("outside content") });
        var backupLink = Path.Combine(Store.BackupRootPath, "linked-backup.lua");
        File.CreateSymbolicLink(backupLink, outside);
        component.Files.Add(new ContentComponentFileState
        {
            RelativePath = "none.lua", OriginalExisted = true, BackupPath = backupLink,
            OriginalSha256 = Hash("outside content")
        });
        Store.Save(state);
        var report = new DiagnosticReportExporter().Collect(Context(), Store);
        Assert.Equal(2, report.Files.Count(f => f.Status == "UnsafePath"));
        Assert.Contains(report.Files, f => f.Path == backupLink && f.Status == "SymlinkNotRead" && f.ActualSha256 is null);
        Assert.DoesNotContain(report.Files, f => f.ActualSha256 == Hash("outside content"));
    }

    [Fact]
    public void Collect_ReportsToolRuntimeRetirementsAndDirectoryBackups_AndExcludesOtherInstalls()
    {
        var state = CreateState();
        var target = Path.Combine(Aircraft, "plugins", "xlua");
        Write(Path.Combine(target, "win_x64", "xlua.xpl"), "runtime");
        Write(Path.Combine(target, "win_x64", "xlua2.xpl"), "obsolete");
        var backup = Path.Combine(Store.BackupRootPath, "xlua-directory");
        Directory.CreateDirectory(backup);
        state.ToolInstallations["xlua"] = new ToolInstallationState
        {
            XPlaneRoot = Aircraft, TargetPath = target, PackageId = "wahltho.optimized-xlua", InstalledVersion = "2.0.0",
            InstalledFiles = [new() { RelativePath = "win_x64/xlua.xpl", Size = 7, Sha256 = Hash("runtime") }],
            RetiredFiles = ["win_x64/xlua2.xpl"],
            Backups = [new() { SourceExisted = true, BackupPath = backup }]
        };
        state.ToolInstallations["other"] = new ToolInstallationState
        {
            XPlaneRoot = Path.Combine(_root, "different-xplane"), PackageId = "unrelated"
        };
        Store.Save(state);
        var report = new DiagnosticReportExporter().Collect(Context(), Store);
        Assert.Contains(report.Files, f => f.Kind == "Tool file" && f.Status == "MatchesRecordedHash");
        Assert.Contains(report.Files, f => f.Kind == "Retired tool file" && f.Status == "UnexpectedlyPresent");
        Assert.Contains(report.Files, f => f.Kind == "Tool backup directory" && f.Status == "DirectoryPresentNotHashVerified");
        Assert.DoesNotContain(report.Components, c => c.PackageId == "unrelated");
    }

    [Fact]
    public void Collect_IncludesOriginalBackupsReferencedByPreviousAircraftGeneration()
    {
        var state = CreateState();
        var backup = Path.Combine(Store.BackupRootPath, "historical.lua");
        Write(backup, "historical original");
        state.ContentInstallations.Values.Single().Backups.Add(new BackupRecord
        {
            SourceExisted = false,
            AircraftContentGeneration = new()
            {
                InstallationComponents = new()
                {
                    ["previous"] = new()
                    {
                        ComponentId = "previous", Files = [new() { RelativePath = "historical.lua", OriginalExisted = true,
                            BackupPath = backup, OriginalSizeBytes = 19, OriginalSha256 = Hash("historical original") }]
                    }
                }
            }
        });
        Store.Save(state);
        var report = new DiagnosticReportExporter().Collect(Context(), Store);
        Assert.Contains(report.Files, f => f.Owner == "previous" && f.Status == "MatchesRecordedHash");
        Assert.DoesNotContain(report.Files, f => f.Owner == "previous" && f.Kind == "Managed file snapshot");
    }

    [Fact]
    public void Collect_LargeFilesAreReportedWithoutUnboundedHashing()
    {
        Directory.CreateDirectory(Aircraft);
        using (var stream = File.Create(Path.Combine(Aircraft, "large.xpl"))) stream.SetLength(513L * 1024 * 1024);
        var report = new DiagnosticReportExporter().Collect(Context() with
        {
            KeyFiles = new Dictionary<string, string> { ["Large runtime"] = "large.xpl" }
        }, Store);
        Assert.Equal("HashBudgetExceeded", Assert.Single(report.Files).Status);
    }

    [Fact]
    public void Export_CancellationDoesNotLeaveAnyArchiveOrChangeState()
    {
        CreateState();
        var before = File.ReadAllBytes(Store.StatePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new DiagnosticReportExporter().Export(Context(), Store, Output,
            cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(Output));
        Assert.Equal(before, File.ReadAllBytes(Store.StatePath));
    }

    [Fact]
    public void Export_UnwritableDestinationLeavesNoPartialArchive()
    {
        CreateState();
        Write(Output, "occupied by a file");
        Assert.Throws<IOException>(() => new DiagnosticReportExporter().Export(Context(), Store, Output));
        Assert.Equal("occupied by a file", File.ReadAllText(Output));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    private DiagnosticExportContext Context() => new()
    {
        ToolkitVersion = "0.21.3", CatalogVersion = "1.13.0", AircraftFolder = Aircraft,
        SelectedAircraftPath = Aircraft, Product = "LevelUp", InstalledAircraftVersion = "v2.S1.51C",
        XPlaneRoot = Path.Combine(_root, "Alice", "X-Plane"),
        Packages = [new("maintenance", "vnav 0.2.0", "vnav 0.2.0", "Current")]
    };

    private ToolStateDocument CreateState()
    {
        var target = Path.Combine(Aircraft, Relative);
        var backup = Path.Combine(Store.BackupRootPath, "original.lua");
        Write(target, "-- copyrighted aircraft payload");
        Write(backup, "-- original aircraft payload");
        var state = new ToolStateDocument();
        state.ContentInstallations[ToolStateStore.PathKey(Aircraft)] = new()
        {
            AircraftFolder = Aircraft, HasAuthoritativeContentState = true,
            ContentComponents = new()
            {
                ["maintenance"] = new()
                {
                    ComponentId = "maintenance", PackageVersion = "catalog-test", EnabledModules = ["vnav"],
                    Files = [new() { RelativePath = Relative, TargetPath = target, BackupPath = backup, OriginalExisted = true,
                        OriginalSizeBytes = new FileInfo(backup).Length, OriginalSha256 = Hash(File.ReadAllText(backup)),
                        InstalledSizeBytes = new FileInfo(target).Length, InstalledSha256 = Hash(File.ReadAllText(target)) }]
                }
            }
        };
        Store.Save(state);
        return state;
    }
    private Dictionary<string, byte[]> Snapshot(bool excludeOutput = false) => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .Where(p => !excludeOutput || !p.StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(p => p, File.ReadAllBytes);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
