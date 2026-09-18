using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class HardwareConfigTransferOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-hardware-{Guid.NewGuid():N}");
    private string Xp => Path.Combine(_root, "XPlane");
    private string Backups => Path.Combine(_root, "backups");
    private const string Source = "b738x_hw.cfg";
    private const string Target = "737_80NG_hw.cfg";
    private const string NewTarget = "737_9ENG_hw.cfg";
    private const string Config = "*** B737-800X ZIBO MOD ***\r\nTOE BRAKE AXIS = 1\r\nTHROTTLE NOISE = 2\r\nPITCH 0 ZONE = 3\r\nFLAP DETENT UP = -1234\r\n";
    private HardwareConfigTransferOperation Operation => new(Backups, () => false);

    public HardwareConfigTransferOperationTests()
    {
        Directory.CreateDirectory(Path.Combine(Xp, "Resources"));
        Directory.CreateDirectory(Path.Combine(Xp, "Output", "preferences"));
        foreach (var reference in AircraftReferenceCatalog.All)
        {
            var folder = Path.Combine(Xp, "Aircraft", reference.Family);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, reference.AcfFileName), $"1200 Version\nP acf/_name {reference.ExpectedName}\nP acf/_descrip {reference.ExpectedDescription}\nP acf/_studio {reference.ExpectedStudioContains}\nP acf/_version test\nP acf/_cgY 0\nP acf/_cgZ 0\n");
        }
        File.WriteAllText(PathFor(Source), Config);
        File.WriteAllText(PathFor(Target), "original target bytes");
    }
    private string PathFor(string name) => Path.Combine(Xp, "Output", "preferences", name);

    [Fact]
    public void Discovery_DeduplicatesZiboAndListsFiveLevelUpVariants()
    {
        var configs = HardwareConfigTransferOperation.Discover(Xp);
        Assert.Equal(6, configs.Count);
        Assert.Single(configs, c => c.FileName == Source);
        Assert.False(configs.Single(c => c.FileName == NewTarget).Exists);
    }

    [Fact]
    public void CopyAndRestore_AcrossProducts_PreservesBytesAndRemovesCreatedFile_AfterRestart()
    {
        var result = Operation.Copy(Xp, Source, [Target, NewTarget, Target]);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(Config, File.ReadAllText(PathFor(Target)));
        Assert.Equal(Config, File.ReadAllText(PathFor(NewTarget)));
        Assert.True(File.Exists(Assert.Single(result.BackupPaths)));
        Assert.False(Operation.Copy(Xp, Source, [Target, NewTarget]).Changed);
        var restored = new HardwareConfigTransferOperation(Backups, () => false).Restore(Xp);
        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal("original target bytes", File.ReadAllText(PathFor(Target)));
        Assert.False(File.Exists(PathFor(NewTarget)));
        Assert.Equal(Config, File.ReadAllText(PathFor(Source)));
        Assert.False(Operation.Restore(Xp).Changed);
    }

    [Fact]
    public void Copy_LevelUpToZibo_Works()
    {
        File.WriteAllText(PathFor(Target), Config.Replace("AXIS = 1", "AXIS = 0"));
        Assert.True(Operation.Copy(Xp, Target, [Source]).Succeeded);
        Assert.Equal(File.ReadAllBytes(PathFor(Target)), File.ReadAllBytes(PathFor(Source)));
    }

    [Fact]
    public void Restore_ProtectsChangesMadeAfterCopy_WithoutPartialRestoration()
    {
        Assert.True(Operation.Copy(Xp, Source, [Target, NewTarget]).Succeeded);
        File.WriteAllText(PathFor(NewTarget), "new settings");
        Assert.False(Operation.Restore(Xp).Succeeded);
        Assert.Equal(Config, File.ReadAllText(PathFor(Target)));
        Assert.Equal("new settings", File.ReadAllText(PathFor(NewTarget)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOE BRAKE AXIS = 1\nTHROTTLE NOISE = nope\nPITCH 0 ZONE = 1")]
    [InlineData("TOE BRAKE AXIS = 1\nTHROTTLE NOISE = NaN\nPITCH 0 ZONE = 1")]
    [InlineData("TOE BRAKE AXIS = 1\nTHROTTLE NOISE = 0\nPITCH 0 ZONE = 1\nPITCH 0 ZONE = 2")]
    public void InvalidSource_BlocksAllWrites(string content)
    {
        File.WriteAllText(PathFor(Source), content);
        Assert.False(Operation.Copy(Xp, Source, [Target, NewTarget]).Succeeded);
        Assert.Equal("original target bytes", File.ReadAllText(PathFor(Target)));
        Assert.False(File.Exists(PathFor(NewTarget)));
    }

    [Fact]
    public void RunningSimulator_BlocksCopyAndRestore()
    {
        var operation = new HardwareConfigTransferOperation(Backups, () => true);
        Assert.False(operation.Copy(Xp, Source, [Target]).Succeeded);
        Assert.True(Operation.Copy(Xp, Source, [Target]).Succeeded);
        Assert.False(operation.Restore(Xp).Succeeded);
        Assert.Equal(Config, File.ReadAllText(PathFor(Target)));
    }

    [Theory]
    [InlineData("../unrelated.cfg")]
    [InlineData("b738x.cfg")]
    [InlineData(Source)]
    public void UnsupportedOrSourceTarget_BlocksAllWrites(string name)
    {
        Assert.False(Operation.Copy(Xp, Source, [Target, name]).Succeeded);
        Assert.Equal("original target bytes", File.ReadAllText(PathFor(Target)));
    }

    [Fact]
    public void MissingSourceOrUndetectedVariant_IsBlocked()
    {
        File.Delete(PathFor(Source));
        Assert.False(Operation.Copy(Xp, Source, [Target]).Succeeded);
        File.WriteAllText(PathFor(Source), Config);
        File.Delete(Path.Combine(Xp, "Aircraft", "levelup-737ng", "737_80NG.acf"));
        Assert.False(Operation.Copy(Xp, Source, [Target]).Succeeded);
    }

    [Fact]
    public void SymlinkTarget_IsBlockedWithoutChangingExternalFile()
    {
        var external = Path.Combine(_root, "external.cfg");
        File.WriteAllText(external, "external");
        File.CreateSymbolicLink(PathFor(NewTarget), external);
        Assert.False(Operation.Copy(Xp, Source, [Target, NewTarget]).Succeeded);
        Assert.Equal("external", File.ReadAllText(external));
        Assert.Equal("original target bytes", File.ReadAllText(PathFor(Target)));
    }

    [Fact]
    public void FailureOnSecondWrite_RollsBackFirst_AndPreservesPreviousRestore()
    {
        Assert.True(Operation.Copy(Xp, Source, [Target]).Succeeded);
        File.WriteAllText(PathFor(Source), Config.Replace("AXIS = 1", "AXIS = 0"));
        Directory.CreateDirectory(PathFor(NewTarget)); // Preflight sees absence, atomic rename fails.
        Assert.False(Operation.Copy(Xp, Source, [Target, NewTarget]).Succeeded);
        Assert.Equal(Config, File.ReadAllText(PathFor(Target)));
        Assert.True(Operation.Restore(Xp).Succeeded);
        Assert.Equal("original target bytes", File.ReadAllText(PathFor(Target)));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
