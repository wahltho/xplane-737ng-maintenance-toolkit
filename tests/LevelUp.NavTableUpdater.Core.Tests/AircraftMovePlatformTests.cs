using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using LevelUp.NavTableUpdater.Core.Aircraft;
using Xunit.Abstractions;

namespace LevelUp.NavTableUpdater.Core.Tests;

// Platform cases are visibly skipped, rather than reported as passed, on other hosts.
public sealed class WindowsMoveFactAttribute : FactAttribute
{
    public WindowsMoveFactAttribute()
    { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows and NTFS access controls."; }
}

public sealed class CrossVolumeMoveFactAttribute : FactAttribute
{
    public CrossVolumeMoveFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MTK_MOVE_TEST_VOLUME")))
            Skip = "Run tools/test_aircraft_move_cross_volume.sh on macOS with its temporary APFS volume.";
    }
}

public sealed class CrossVolumeMoveTheoryAttribute : TheoryAttribute
{
    public CrossVolumeMoveTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MTK_MOVE_TEST_VOLUME")))
            Skip = "Run tools/test_aircraft_move_cross_volume.sh on macOS with its temporary APFS volume.";
    }
}

public sealed class AircraftMovePlatformTests(ITestOutputHelper output)
{
    private AircraftMoveOperationTests CrossVolumeFixture()
    {
        var parent = Environment.GetEnvironmentVariable("MTK_MOVE_TEST_VOLUME")!;
        Assert.True(Directory.Exists(parent));
        var fixture = new AircraftMoveOperationTests(parent);
        try
        {
            var sourceDevice = Device(fixture.Source);
            var destinationDevice = Device(parent);
            output.WriteLine($"Source device: {sourceDevice}; destination device: {destinationDevice}");
            Assert.NotEqual(sourceDevice, destinationDevice);
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    [CrossVolumeMoveFact]
    public void CrossVolume_MoveAndMoveBack_VerifiesBytesHistoryAndFreeSpace()
    {
        using var fixture = CrossVolumeFixture();
        var parent = Path.GetDirectoryName(fixture.Destination)!;
        // Verify the production free-space lookup actually selects the small test volume.
        var volume = DriveInfo.GetDrives().Single(d => d.Name.TrimEnd('/') == parent.TrimEnd('/'));
        Assert.InRange(AircraftMoveOperation.FreeSpace(parent), volume.AvailableFreeSpace - 1048576, volume.AvailableFreeSpace + 1048576);
        var operation = new AircraftMoveOperation(fixture.Store, fixture.Settings, () => false);
        var plan = operation.Prepare(fixture.Source, fixture.Destination);
        var result = operation.Execute(plan);
        Assert.False(result.CleanupPending); Assert.False(operation.HasPendingMove);
        Assert.False(Directory.Exists(fixture.Source));
        Assert.Equal(fixture.Destination, fixture.Settings.Load().SelectedAircraftPath);
        var back = operation.Prepare(fixture.Destination, fixture.Source);
        Assert.Equal(plan.Entries.Select(e => (e.RelativePath, e.Directory, e.Size, e.Sha256, e.UnixMode)),
            back.Entries.Select(e => (e.RelativePath, e.Directory, e.Size, e.Sha256, e.UnixMode)));
        Assert.False(operation.Execute(back).CleanupPending);
        Assert.True(Directory.Exists(fixture.Source)); Assert.False(Directory.Exists(fixture.Destination));
        Assert.Equal(fixture.Source, fixture.Settings.Load().SelectedAircraftPath);
        Assert.Empty(Directory.GetDirectories(parent, ".mtk-aircraft-move-*"));
    }

    [CrossVolumeMoveTheory]
    [InlineData("Prepared")]
    [InlineData("SourceRetained")]
    [InlineData("Activated")]
    [InlineData("StateSaved")]
    [InlineData("SettingsSaved")]
    public void CrossVolume_FailureRollsBackFilesAndExactSettings(string phase)
    {
        using var fixture = CrossVolumeFixture();
        fixture.Store.Save(new());
        var state = File.ReadAllBytes(fixture.Store.StatePath);
        var settings = File.ReadAllBytes(fixture.Settings.SettingsPath);
        var operation = new AircraftMoveOperation(fixture.Store, fixture.Settings, () => false, null,
            p => { if (p == phase) throw new IOException("Injected cross-volume failure."); });
        var plan = operation.Prepare(fixture.Source, fixture.Destination);
        Assert.Throws<IOException>(() => operation.Execute(plan));
        Assert.False(operation.HasPendingMove); Assert.False(Directory.Exists(fixture.Destination));
        var restored = operation.Prepare(fixture.Source, fixture.Destination);
        Assert.Equal(plan.Entries, restored.Entries);
        Assert.Equal(state, File.ReadAllBytes(fixture.Store.StatePath));
        Assert.Equal(settings, File.ReadAllBytes(fixture.Settings.SettingsPath));
    }

    [CrossVolumeMoveFact]
    public void CrossVolume_ConfigRestore_UsesOriginalBackups()
    { using var fixture = CrossVolumeFixture(); fixture.ConfigRestore_AfterMove_UsesOriginalBackupsAndNewAircraftPaths(); }

    [CrossVolumeMoveFact]
    public void CrossVolume_ToolUpdateAndRestore_UsesPreviousRuntime()
    { using var fixture = CrossVolumeFixture(); fixture.ToolUpdateAndRestore_AfterMove_ReturnsPreviousRuntimeAndPreservesScripts(); }

    [CrossVolumeMoveFact]
    public async Task CrossVolume_PatchRestore_UsesOriginalContent()
    { using var fixture = CrossVolumeFixture(); await fixture.PatchRestore_AfterMove_UsesOriginalFileAndOwnershipHistory(); }

    [WindowsMoveFact]
    public void Windows_MovePreservesProtectedAccessControlsAndReadOnlyFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new AircraftMoveOperationTests();
        var runtime = Path.Combine(fixture.Source, "plugins/runtime.xpl");
        File.WriteAllText(runtime, "test runtime");
        var user = WindowsIdentity.GetCurrent().User!;
        var guests = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null);
        var directory = new DirectorySecurity();
        directory.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        directory.AddAccessRule(new(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.AddAccessRule(new(guests, FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(fixture.Source).SetAccessControl(directory);
        var file = new FileSecurity();
        file.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        file.AddAccessRule(new(user, FileSystemRights.FullControl, AccessControlType.Allow));
        file.AddAccessRule(new(guests, FileSystemRights.Write, AccessControlType.Deny));
        new FileInfo(runtime).SetAccessControl(file);
        File.SetAttributes(runtime, File.GetAttributes(runtime) | FileAttributes.ReadOnly);
        var expectedDirectory = new DirectoryInfo(fixture.Source).GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var expectedFile = new FileInfo(runtime).GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var operation = new AircraftMoveOperation(fixture.Store, fixture.Settings, () => false);
        var plan = operation.Prepare(fixture.Source, fixture.Destination);
        var expectedAccess = plan.Entries.ToDictionary(e => e.RelativePath,
            e => ReadAccess(Path.Combine(fixture.Source, e.RelativePath), e.Directory));
        Assert.False(operation.Execute(plan).CleanupPending);
        Assert.False(operation.HasPendingMove); Assert.False(Directory.Exists(fixture.Source));
        var target = Path.Combine(fixture.Destination, "plugins/runtime.xpl");
        var targetDirectory = new DirectoryInfo(fixture.Destination).GetAccessControl(AccessControlSections.Access);
        var targetFile = new FileInfo(target).GetAccessControl(AccessControlSections.Access);
        Assert.True(targetDirectory.AreAccessRulesProtected, "Aircraft directory ACL protection was lost.");
        Assert.True(targetFile.AreAccessRulesProtected, "Runtime file ACL protection was lost.");
        Assert.Equal(expectedDirectory, targetDirectory.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.Equal(expectedFile, targetFile.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        foreach (var entry in plan.Entries)
            Assert.Equal(expectedAccess[entry.RelativePath],
                ReadAccess(Path.Combine(fixture.Destination, entry.RelativePath), entry.Directory));
        Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("test runtime", File.ReadAllText(target));
        File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);

        static string ReadAccess(string path, bool directory) => (directory
            ? (FileSystemSecurity)new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access))
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
    }

    private static string Device(string path)
    {
        var start = new ProcessStartInfo("/usr/bin/stat") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-f"); start.ArgumentList.Add("%d"); start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        var value = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        Assert.Equal(0, process.ExitCode); return value;
    }
}
