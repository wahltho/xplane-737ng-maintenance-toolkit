using System.Text;
using LevelUp.NavTableUpdater.Core.Analysis;
using LevelUp.NavTableUpdater.Core.Manifest;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class AircraftInstallAnalyzerTests
{
    [Fact]
    public void Analyze_WhenAnchorsArePresentAndHooksMissing_ReturnsNotInstalled()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.UnpatchedLua);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.NotInstalled, result.State);
        Assert.True(result.IsSafeToPatch);
        Assert.Contains(result.PlannedChanges, change => change.Contains("Would insert", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenMarkedBlocksAreCurrent_ReturnsCorrectlyInstalled()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.CurrentMarkedLua, PayloadFixtureState.Current);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.CorrectlyInstalled, result.State);
        Assert.Equal("v0.2.0", result.LocalPackageVersion);
    }

    [Fact]
    public void Analyze_WhenMarkedBlocksAreCurrentButPayloadsAreMissing_ReturnsRepairRequired()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.CurrentMarkedLua);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.RepairRequired, result.State);
        Assert.True(result.IsSafeToPatch);
        Assert.Contains(result.Components, component => component.Name == "B738.a_fms_levelup_tables.lua" && component.State == "Missing");
        Assert.Contains(result.PlannedChanges, change => change.Contains("refresh missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WhenMarkedBlocksAreCurrentButPayloadIsChanged_ReturnsRepairRequired()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.CurrentMarkedLua, PayloadFixtureState.ChangedTablePayload);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.RepairRequired, result.State);
        Assert.True(result.IsSafeToPatch);
        Assert.Contains(result.Components, component => component.Name == "B738.a_fms_levelup_tables.lua" && component.State == "Changed");
    }

    [Fact]
    public void Analyze_WhenLegacyHooksArePresent_ReturnsKnownLegacyInstallation()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.LegacyLua);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.KnownLegacyInstallation, result.State);
        Assert.True(result.IsSafeToPatch);
    }

    [Fact]
    public void Analyze_WhenMarkerIsPartial_ReturnsPartiallyInstalled()
    {
        using var fixture = AircraftFixture.Create(TestFixtures.PartialMarkerLua);
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.PartiallyInstalled, result.State);
        Assert.False(result.IsSafeToPatch);
    }

    [Fact]
    public void Analyze_WhenLevelUpPortHasNoLuaTarget_ReturnsPortNoLuaInstallation()
    {
        using var fixture = AircraftFixture.CreatePortNoLua();
        var result = Analyze(fixture.Path);

        Assert.Equal(InstallState.PortNoLuaInstallation, result.State);
        Assert.False(result.IsSafeToPatch);
        Assert.Contains(result.PlannedChanges, change => change.Contains("No Lua patch", StringComparison.Ordinal));
    }

    private static AircraftAnalysisResult Analyze(string aircraftPath)
    {
        var manifest = ManifestParser.ParsePipeManifest(TestFixtures.ManifestText);
        return new AircraftInstallAnalyzer().Analyze(aircraftPath, manifest);
    }

    private sealed class AircraftFixture : IDisposable
    {
        private AircraftFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static AircraftFixture Create(string luaText, PayloadFixtureState payloadState = PayloadFixtureState.None)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"levelup-updater-tests-{Guid.NewGuid():N}");
            var scriptFolder = System.IO.Path.Combine(root, "plugins", "xlua", "scripts", "B738.a_fms");
            Directory.CreateDirectory(scriptFolder);
            File.WriteAllText(System.IO.Path.Combine(scriptFolder, "B738.a_fms.lua"), luaText, new UTF8Encoding(false));
            WritePayloads(scriptFolder, payloadState);
            return new AircraftFixture(root);
        }

        public static AircraftFixture CreatePortNoLua()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"levelup-updater-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(System.IO.Path.Combine(root, "plugins", "zibomod"));
            File.WriteAllText(System.IO.Path.Combine(root, "737_70NG.acf"), "", new UTF8Encoding(false));
            return new AircraftFixture(root);
        }

        private static void WritePayloads(string scriptFolder, PayloadFixtureState payloadState)
        {
            if (payloadState is PayloadFixtureState.None)
            {
                return;
            }

            foreach (var (fileName, content) in TestFixtures.PayloadFiles)
            {
                File.WriteAllText(System.IO.Path.Combine(scriptFolder, fileName), content, new UTF8Encoding(false));
            }

            if (payloadState is PayloadFixtureState.ChangedTablePayload)
            {
                File.WriteAllText(
                    System.IO.Path.Combine(scriptFolder, "B738.a_fms_levelup_tables.lua"),
                    "corrupted table payload\n",
                    new UTF8Encoding(false));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private enum PayloadFixtureState
    {
        None,
        Current,
        ChangedTablePayload
    }
}
