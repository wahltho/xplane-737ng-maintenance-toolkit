using System.Security.Cryptography;
using System.Text;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Content;
using LevelUp.NavTableUpdater.Core.Manifest;
using LevelUp.NavTableUpdater.Core.State;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class VnavContentOperationTests
{
    [Fact]
    public async Task Install_WhenUnpatchedLua_InstallsMarkedHooksPayloadsBackupsAndState()
    {
        using var fixture = VnavFixture.Create(VnavFixture.UnpatchedLua, lineEnding: "\r\n");
        var operation = fixture.CreateOperation();

        var result = await operation.RunAsync(VnavContentAction.Install, fixture.SingleVariant(), fixture.Manifest);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Contains("-- BEGIN TEST_VNAV DOFILE\r\n", File.ReadAllText(fixture.TargetScriptPath));
        Assert.Contains("\r\n", File.ReadAllText(fixture.TargetScriptPath));
        Assert.All(fixture.Manifest.Payloads, payload => Assert.True(File.Exists(Path.Combine(fixture.ScriptFolder, payload.FileName))));
        Assert.Equal(InstallState.CorrectlyInstalled, fixture.Analyze().State);

        var target = Assert.Single(fixture.Store.Load().Aircraft.Values);
        Assert.Equal("x-plane-test-vnav", target.InstalledContentPackageId);
        Assert.Equal("v1.0.0", target.InstalledContentPackageVersion);
        Assert.Contains(target.Backups, backup => backup.Operation == "VnavContentPatch");
    }

    [Fact]
    public async Task Repair_WhenPayloadIsMissing_RestoresPayloadWithoutDuplicatingHooks()
    {
        using var fixture = VnavFixture.Create(VnavFixture.UnpatchedLua, lineEnding: "\n");
        var operation = fixture.CreateOperation();
        await operation.RunAsync(VnavContentAction.Install, fixture.SingleVariant(), fixture.Manifest);
        File.Delete(Path.Combine(fixture.ScriptFolder, "B738.a_fms_test_tables.lua"));

        var result = await operation.RunAsync(VnavContentAction.Repair, fixture.SingleVariant(), fixture.Manifest);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.True(File.Exists(Path.Combine(fixture.ScriptFolder, "B738.a_fms_test_tables.lua")));
        var script = File.ReadAllText(fixture.TargetScriptPath);
        Assert.Equal(1, Count(script, "-- BEGIN TEST_VNAV DOFILE"));
        Assert.Equal(InstallState.CorrectlyInstalled, fixture.Analyze().State);
    }

    [Fact]
    public async Task Uninstall_WhenMarkedInstallExists_RemovesHooksAndManifestPayloads()
    {
        using var fixture = VnavFixture.Create(VnavFixture.UnpatchedLua, lineEnding: "\n");
        var operation = fixture.CreateOperation();
        await operation.RunAsync(VnavContentAction.Install, fixture.SingleVariant(), fixture.Manifest);

        var result = await operation.RunAsync(VnavContentAction.Uninstall, fixture.SingleVariant(), fixture.Manifest);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        var script = File.ReadAllText(fixture.TargetScriptPath);
        Assert.DoesNotContain("TEST_VNAV", script, StringComparison.Ordinal);
        Assert.All(fixture.Manifest.Payloads, payload => Assert.False(File.Exists(Path.Combine(fixture.ScriptFolder, payload.FileName))));
        Assert.Equal(InstallState.NotInstalled, fixture.Analyze().State);
    }

    [Fact]
    public async Task Install_WhenLegacyHooksExist_MigratesToMarkedHooks()
    {
        using var fixture = VnavFixture.Create(VnavFixture.LegacyLua, lineEnding: "\n");
        var operation = fixture.CreateOperation();

        var result = await operation.RunAsync(VnavContentAction.Install, fixture.SingleVariant(), fixture.Manifest);

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(InstallState.CorrectlyInstalled, fixture.Analyze().State);
        var script = File.ReadAllText(fixture.TargetScriptPath);
        Assert.Equal(1, Count(script, "-- BEGIN TEST_VNAV KIAS"));
        Assert.Equal(1, Count(script, "pcall(B738_variant_test_take_alt_dist,"));
    }

    private static int Count(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private sealed class VnavFixture : IDisposable
    {
        public const string UnpatchedLua = """
            jit.off()

            function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
                local altitude_distance = 0
                return altitude_distance
            end

            function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
                local altitude_distance = 0
                return altitude_distance
            end
            """;

        public const string LegacyLua = """
            jit.off()
            dofile("B738.a_fms_test_tables.lua")

            function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
                local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist, x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
                if variant_test_ok and variant_test_dist ~= nil then
                    return variant_test_dist
                end
                local altitude_distance = 0
                return altitude_distance
            end

            function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)
                local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist_mach, x_idx_alt, x_spd_alt, x_spd_wnd_alt)
                if variant_test_ok and variant_test_dist ~= nil then
                    return variant_test_dist
                end
                local altitude_distance = 0
                return altitude_distance
            end
            """;

        private VnavFixture(string path, PackageManifest manifest, IReadOnlyDictionary<string, PackagePayload> payloads)
        {
            Path = path;
            Manifest = manifest;
            Payloads = payloads;
            ScriptFolder = System.IO.Path.Combine(path, "plugins", "xlua", "scripts", "B738.a_fms");
            TargetScriptPath = System.IO.Path.Combine(ScriptFolder, "B738.a_fms.lua");
            Store = TestToolStateStore.Create(path);
        }

        public string Path { get; }

        public string ScriptFolder { get; }

        public string TargetScriptPath { get; }

        public PackageManifest Manifest { get; }

        public IReadOnlyDictionary<string, PackagePayload> Payloads { get; }

        public ToolStateStore Store { get; }

        public static VnavFixture Create(string luaText, string lineEnding)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xplane-737ng-vnav-tests-{Guid.NewGuid():N}");
            var scriptFolder = System.IO.Path.Combine(root, "plugins", "xlua", "scripts", "B738.a_fms");
            Directory.CreateDirectory(scriptFolder);
            WriteText(System.IO.Path.Combine(scriptFolder, "B738.a_fms.lua"), luaText, lineEnding);
            WriteText(
                System.IO.Path.Combine(root, "737_70NG.acf"),
                """
                1200 Version
                P acf/_descrip Boeing 737-700NG
                P acf/_file_writer_version 124311
                P acf/_name Boeing 737-700NG
                P acf/_studio LevelUp, Laminar Research, ZiboMod, flight tuned by Aeroguitarist
                P acf/_version XP12 2.S1.50B (20260709-2031 SAO)
                P acf/_cgY -2.049999952
                P acf/_cgZ 49.740001678
                P acf/_pe_xyz/0 1.000000000
                P acf/_pe_xyz/1 2.000000000
                P acf/_pe_xyz/2 3.000000000
                P acf/_ang_offset/0,1 -2.500000000
                """,
                "\n");
            WriteText(
                System.IO.Path.Combine(root, "737_70NG_prefs.txt"),
                """
                _iql_pe_x_0 1.000000
                _iql_pe_y_0 2.000000
                _iql_pe_z_0 3.000000
                _iql_look_os_the_0 -2.500000
                """,
                "\n");

            var payloads = BuildPayloads();
            var manifest = ManifestParser.ParsePipeManifest(BuildManifest(payloads));
            return new VnavFixture(root, manifest, payloads.ToDictionary(
                pair => pair.Key,
                pair => new PackagePayload(pair.Key, Encoding.UTF8.GetBytes(pair.Value), "memory"),
                StringComparer.OrdinalIgnoreCase));
        }

        public VnavContentOperation CreateOperation() =>
            new(Store, new MemoryPackagePayloadSource(Payloads), isXPlaneRunning: () => false);

        public AircraftAnalysisResult Analyze() =>
            new AircraftInstallAnalyzer().Analyze(Path, Manifest);

        public AircraftVariantViewAnalysis SingleVariant() =>
            Assert.Single(new AircraftViewAnalyzer().Analyze(Path).Variants);

        private static IReadOnlyDictionary<string, string> BuildPayloads() =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["B738.a_fms_test_tables.lua"] = "test table payload\n",
                ["Add_dofile.txt"] = """
                    -- BEGIN TEST_VNAV DOFILE
                    -- package-id|x-plane-test-vnav
                    -- package-version|v1.0.0
                    dofile("B738.a_fms_test_tables.lua")
                    -- END TEST_VNAV DOFILE
                    """,
                ["Add_to_take_alt_dist.txt"] = """
                        -- BEGIN TEST_VNAV KIAS
                        -- package-id|x-plane-test-vnav
                        -- package-version|v1.0.0
                        local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist, x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)
                        if variant_test_ok and variant_test_dist ~= nil then
                            return variant_test_dist
                        end
                        -- END TEST_VNAV KIAS
                    """,
                ["Add_to_take_alt_dist_mach.txt"] = """
                        -- BEGIN TEST_VNAV MACH
                        -- package-id|x-plane-test-vnav
                        -- package-version|v1.0.0
                        local variant_test_ok, variant_test_dist = pcall(B738_variant_test_take_alt_dist_mach, x_idx_alt, x_spd_alt, x_spd_wnd_alt)
                        if variant_test_ok and variant_test_dist ~= nil then
                            return variant_test_dist
                        end
                        -- END TEST_VNAV MACH
                    """
            };

        private static string BuildManifest(IReadOnlyDictionary<string, string> payloads)
        {
            var builder = new StringBuilder();
            builder.AppendLine("schema|package-manifest|1");
            builder.AppendLine("package|id|x-plane-test-vnav");
            builder.AppendLine("package|version|v1.0.0");
            builder.AppendLine("package|release_tag|v1.0.0");
            builder.AppendLine("aircraft|family|levelup_737ng");
            builder.AppendLine("repository|url|https://github.com/example/x-plane-test-vnav");
            builder.AppendLine("target|relative_path|plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua");
            AppendPayload(builder, "table", "B738.a_fms_test_tables.lua", payloads);
            AppendPayload(builder, "dofile", "Add_dofile.txt", payloads);
            AppendPayload(builder, "kias", "Add_to_take_alt_dist.txt", payloads);
            AppendPayload(builder, "mach", "Add_to_take_alt_dist_mach.txt", payloads);
            builder.AppendLine("anchor|dofile|jit.off()");
            builder.AppendLine("anchor|kias|function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)");
            builder.AppendLine("anchor|mach|function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)");
            builder.AppendLine("marker|dofile|begin|-- BEGIN TEST_VNAV DOFILE");
            builder.AppendLine("marker|dofile|end|-- END TEST_VNAV DOFILE");
            builder.AppendLine("marker|kias|begin|-- BEGIN TEST_VNAV KIAS");
            builder.AppendLine("marker|kias|end|-- END TEST_VNAV KIAS");
            builder.AppendLine("marker|mach|begin|-- BEGIN TEST_VNAV MACH");
            builder.AppendLine("marker|mach|end|-- END TEST_VNAV MACH");
            builder.AppendLine("legacy|v0.1.0|dofile|dofile(\"B738.a_fms_test_tables.lua\")");
            builder.AppendLine("legacy|v0.1.0|kias|pcall(B738_variant_test_take_alt_dist,");
            builder.AppendLine("legacy|v0.1.0|mach|pcall(B738_variant_test_take_alt_dist_mach,");
            return builder.ToString();
        }

        private static void AppendPayload(StringBuilder builder, string id, string fileName, IReadOnlyDictionary<string, string> payloads)
        {
            var bytes = Encoding.UTF8.GetBytes(payloads[fileName]);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            builder.AppendLine($"payload|{id}|{fileName}|size|{bytes.Length}|sha256|{hash}");
        }

        private static void WriteText(string path, string text, string lineEnding)
        {
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", lineEnding, StringComparison.Ordinal);
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class MemoryPackagePayloadSource : IPackagePayloadSource
    {
        private readonly IReadOnlyDictionary<string, PackagePayload> _payloads;

        public MemoryPackagePayloadSource(IReadOnlyDictionary<string, PackagePayload> payloads)
        {
            _payloads = payloads;
        }

        public Task<IReadOnlyDictionary<string, PackagePayload>> GetPayloadsAsync(
            PackageManifest manifest,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_payloads);
    }
}
