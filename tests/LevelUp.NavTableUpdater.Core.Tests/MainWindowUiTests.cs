using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;
using LevelUp.NavTableUpdater.App.Views;
using LevelUp.NavTableUpdater.Core.Aircraft;
using LevelUp.NavTableUpdater.Core.Detection;
using LevelUp.NavTableUpdater.Core.State;
using LevelUp.NavTableUpdater.Core.Upstream;


[assembly: AvaloniaTestApplication(typeof(LevelUp.NavTableUpdater.Core.Tests.UiTestAppBuilder))]

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
        .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class MainWindowUiTests
{
    [Fact]
    public Task UnofficialChange_RequiresExplicitUncheckedAcknowledgement() => Run(() =>
    {
        var request = IndependentProjectNotice.ForAircraftMutation(
            new ConfirmationRequest("Install XLua?", "Files will change.", "Install"),
            AircraftProductIds.Zibo737Ng, optimizedXlua: true);
        Assert.True(request.RequiresUnofficialAcknowledgement);
        Assert.Equal(IndependentProjectNotice.OptimizedXluaUrl, request.IndependentProjectUrl);
        Assert.False(IndependentProjectNotice.ForAircraftMutation(
            new ConfirmationRequest("Install?", "Official package.", "Install"),
            AircraftProductIds.LevelUp737Ng).RequiresUnofficialAcknowledgement);

        var dialog = new ConfirmationDialog(request);
        dialog.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var confirm = Button(dialog, "Install");
            var acknowledgement = dialog.FindControl<CheckBox>("UnofficialAcknowledgement")!;
            Assert.True(acknowledgement.IsVisible);
            Assert.False(acknowledgement.IsChecked);
            Assert.False(confirm.IsEffectivelyEnabled);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "UNOFFICIAL ZIBO AIRCRAFT CHANGE" && text.IsEffectivelyVisible);
            SaveFrame(dialog, "unofficial-confirmation.png");
            Press(dialog, acknowledgement);
            Assert.True(confirm.IsEffectivelyEnabled);
            Press(dialog, acknowledgement);
            Assert.False(confirm.IsEffectivelyEnabled);
            Press(dialog, acknowledgement);
            Assert.True(confirm.IsEffectivelyEnabled);
            Press(dialog, confirm);
            Assert.False(dialog.IsVisible);
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
        var ordinary = new ConfirmationDialog(new ConfirmationRequest("Apply official update?", "Official archive.", "Apply"));
        ordinary.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(ordinary.FindControl<Border>("UnofficialNotice")!.IsVisible);
            Assert.True(Button(ordinary, "Apply").IsEffectivelyEnabled);
        }
        finally { ordinary.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task IndependentProjectHeader_RemainsVisibleOnEveryTab() => Run(async () =>
    {
        using var fixture = new Fixture(enabled: false);
        var window = fixture.Open();
        try
        {
            await Until(() => fixture.Vm.SelectedProduct?.IsDetected == true);
            var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
            foreach (var index in Enumerable.Range(0, tabControl.ItemCount))
            {
                tabControl.SelectedIndex = index;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.StartsWith("This toolkit is NOT an official Zibo Mod product.") == true && text.IsEffectivelyVisible);
                Assert.True(Button(window, "Toolkit project and feedback").IsEffectivelyVisible);
            }
        }
        finally { Close(window); }
    });

    [Fact]
    public Task CatalogRefresh_AndRepeatedSelections_KeepPackageAndChannelDropdownsComplete() => Run(async () =>
    {
        using var fixture = new Fixture(enabled: false);
        var window = fixture.Open();
        try
        {
            await Until(() => fixture.Vm.SelectedProduct?.IsDetected == true
                && fixture.Vm.InstallLog.Contains("Content package catalog:"));

            var toolCombo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(combo => ReferenceEquals(combo.ItemsSource, fixture.Vm.AvailableToolPackages));
            var resourceCombo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(combo => ReferenceEquals(combo.ItemsSource, fixture.Vm.AvailableResourcePackages));
            var liveryCombo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(combo => ReferenceEquals(combo.ItemsSource, fixture.Vm.AvailableLiveryPackages));

            Assert.Same(fixture.Vm.SelectedToolPackage, toolCombo.SelectedItem);
            Assert.Same(fixture.Vm.SelectedResourcePackage, resourceCombo.SelectedItem);
            Assert.Same(fixture.Vm.SelectedLiveryPackage, liveryCombo.SelectedItem);
            Assert.Contains(fixture.Vm.AvailableToolPackages,
                entry => ReferenceEquals(entry, fixture.Vm.SelectedToolPackage));
            Assert.Contains(fixture.Vm.AvailableResourcePackages,
                entry => ReferenceEquals(entry, fixture.Vm.SelectedResourcePackage));
            Assert.Contains(fixture.Vm.AvailableLiveryPackages,
                entry => ReferenceEquals(entry, fixture.Vm.SelectedLiveryPackage));

            var expectedTools = fixture.Vm.AvailableToolPackages.Select(entry => entry.PackageId).ToArray();
            var xluaIndex = fixture.Vm.AvailableToolPackages.ToList()
                .FindIndex(entry => entry.PackageId == "wahltho.optimized-xlua");
            var yalIndex = fixture.Vm.AvailableToolPackages.ToList()
                .FindIndex(entry => entry.PackageId == "wahltho.yal");
            Assert.True(xluaIndex >= 0);
            Assert.True(yalIndex >= 0);

            toolCombo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedTools.Length, toolCombo.ItemCount);
            toolCombo.IsDropDownOpen = false;

            toolCombo.SelectedIndex = xluaIndex;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedTools, fixture.Vm.AvailableToolPackages.Select(entry => entry.PackageId));
            Assert.Same(fixture.Vm.AvailableToolPackages[xluaIndex], toolCombo.SelectedItem);
            toolCombo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedTools.Length, toolCombo.ItemCount);
            toolCombo.IsDropDownOpen = false;

            var channelCombo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(combo => ReferenceEquals(combo.ItemsSource, fixture.Vm.ToolReleaseChannelOptions));
            Assert.Equal(["stable", "beta"], fixture.Vm.ToolReleaseChannelOptions);
            channelCombo.SelectedItem = "beta";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["stable", "beta"], fixture.Vm.ToolReleaseChannelOptions);
            Assert.Equal("beta", channelCombo.SelectedItem);
            Assert.Equal("beta", fixture.Vm.SelectedToolReleaseChannel);
            channelCombo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, channelCombo.ItemCount);
            channelCombo.IsDropDownOpen = false;

            toolCombo.SelectedIndex = yalIndex;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedTools, fixture.Vm.AvailableToolPackages.Select(entry => entry.PackageId));
            Assert.Same(fixture.Vm.AvailableToolPackages[yalIndex], toolCombo.SelectedItem);
            toolCombo.SelectedIndex = xluaIndex;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedTools, fixture.Vm.AvailableToolPackages.Select(entry => entry.PackageId));
            Assert.Same(fixture.Vm.AvailableToolPackages[xluaIndex], toolCombo.SelectedItem);
            Assert.Equal("beta", fixture.Vm.SelectedToolReleaseChannel);
        }
        finally { Close(window); }
    });

    [Theory]
    [InlineData(980, 680)]
    [InlineData(1180, 780)]
    public Task StartLayout_KeepsTargetAndProgressVisibleWhileFunctionsAndLogScroll(int width, int height) => Run(async () =>
    {
        using var fixture = new Fixture(enabled: false);
        var window = fixture.Open(width, height);
        try
        {
            await Until(() => fixture.Vm.SelectedProduct?.IsDetected == true
                && fixture.Vm.InstallLog.Contains("Content package catalog:"));
            fixture.Vm.OperationPanelVisible = true;
            fixture.Vm.OperationTitle = "Downloading aircraft update";
            fixture.Vm.OperationSubtitle = "Verifying the package for the selected LevelUp installation.";
            fixture.Vm.OperationProgress = 45;
            fixture.Vm.OperationProgressText = "45% - Downloading and validating aircraft files";
            fixture.Vm.OperationStatus = "Download in progress";
            fixture.Vm.OperationElapsed = "00:12s";
            fixture.Vm.CanCancelOperation = true;
            fixture.Vm.OperationLog = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"[VERIFY] File {i}"));
            Dispatcher.UIThread.RunJobs();
            var left = window.FindControl<ScrollViewer>("InstallationPaneScroll")!;
            var right = window.FindControl<ScrollViewer>("FunctionsPaneScroll")!;
            var progress = window.FindControl<Border>("OperationProgressPanel")!;
            var installation = window.FindControl<Border>("SelectedInstallationCard")!;
            var log = window.FindControl<Expander>("OperationLogExpander")!;
            var cancel = window.FindControl<Button>("OperationCancelButton")!;
            var progressPosition = progress.TranslatePoint(default, window);
            var installationPosition = installation.TranslatePoint(default, window);
            Assert.False(log.IsExpanded);
            Assert.True(right.Viewport.Height > 200, $"Functions viewport is only {right.Viewport.Height}");
            Assert.True(cancel.IsEffectivelyVisible && cancel.IsEffectivelyEnabled);
            Assert.Equal(45, progress.GetVisualDescendants().OfType<ProgressBar>().Single().Value);
            SaveFrame(window, $"layout-{width}-top.png");

            Button(window, "Find hardware configurations").BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(right.Offset.Y > 0);
            Assert.Equal(0, left.Offset.Y);
            Assert.Equal(progressPosition, progress.TranslatePoint(default, window));
            Assert.Equal(installationPosition, installation.TranslatePoint(default, window));
            var targetPath = installation.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == fixture.Vm.SelectedProductFolderPath);
            Assert.True(targetPath.IsEffectivelyVisible);
            var targetPosition = targetPath.TranslatePoint(default, window)!.Value;
            Assert.InRange(targetPosition.Y, 0, window.ClientSize.Height - targetPath.Bounds.Height);
            Assert.InRange(targetPosition.X, 0, window.ClientSize.Width - targetPath.Bounds.Width);
            SaveFrame(window, $"layout-{width}-scrolled.png");

            var rightOffset = right.Offset;
            left.Offset = new Vector(0, left.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rightOffset, right.Offset);
            Assert.Equal(progressPosition, progress.TranslatePoint(default, window));
            left.Offset = default;
            right.Offset = default;
            log.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(progressPosition, progress.TranslatePoint(default, window));
            Assert.True(window.FindControl<TextBox>("OperationLogText")!.Bounds.Height <= 180);
            Assert.True(right.Viewport.Height > 200);
            SaveFrame(window, $"layout-{width}-log.png");

            fixture.Vm.OperationTitle = "LevelUp maintenance patches compatibility package Update blocked";
            fixture.Vm.OperationStatus = "Blocked";
            fixture.Vm.OperationProgress = 0;
            fixture.Vm.OperationSubtitle = string.Join(" ", Enumerable.Repeat("A long diagnostic message about the selected installation.", 20));
            fixture.Vm.OperationProgressText = fixture.Vm.OperationSubtitle;
            fixture.Vm.CanCancelOperation = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(cancel.IsEffectivelyVisible);
            Assert.True(right.Viewport.Height > 180, $"Long result consumed the functions viewport: {right.Viewport.Height}");
            SaveFrame(window, $"layout-{width}-long-result.png");

            var withProgress = right.Viewport.Height;
            fixture.Vm.OperationPanelVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(right.Viewport.Height > withProgress);
            Assert.False(log.IsEffectivelyVisible);
            Assert.Equal(installationPosition, installation.TranslatePoint(default, window));
        }
        finally { Close(window); }
    });

    [Fact]
    public Task HardwareCopy_UsesRealControlsAndDialogs_ThenRestoresFiles() => Run(async () =>
    {
        using var fixture = new Fixture(enabled: false);
        var window = fixture.Open();
        try
        {
            await Until(() => fixture.Vm.SelectedProduct?.IsDetected == true);
            Press(window, Button(window, "Find hardware configurations"));
            await Until(() => fixture.Vm.ActionsEnabled && fixture.Vm.HardwareConfigSources.Count > 0);
            var combo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, fixture.Vm.HardwareConfigSources));
            combo.SelectedIndex = fixture.Vm.HardwareConfigSources.ToList().FindIndex(s => s.Target.FileName == "b738x_hw.cfg");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("b738x_hw.cfg", fixture.Vm.SelectedHardwareConfigSource?.Target.FileName);
            foreach (var name in new[] { "737_80NG_hw.cfg", "737_9ENG_hw.cfg" })
            {
                var check = window.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => c.DataContext is HardwareConfigTargetOption o && o.Target.FileName == name);
                Press(window, check);
                Assert.True(((HardwareConfigTargetOption)check.DataContext!).IsSelected);
            }
            SaveFrame(window, "hardware-selected.png");
            Press(window, Button(window, "Copy selected"));
            await Until(() => window.OwnedWindows.Count == 1);
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Contains("b738x_hw.cfg", ((ConfirmationRequest)dialog.DataContext!).Message);
            Press(dialog, Button(dialog, "Cancel"));
            await Until(() => fixture.Vm.ActionsEnabled);
            Assert.Equal("original hardware", File.ReadAllText(fixture.Target));
            Assert.False(File.Exists(fixture.NewTarget));

            Press(window, Button(window, "Copy selected"));
            await Until(() => window.OwnedWindows.Count == 1);
            dialog = Assert.Single(window.OwnedWindows);
            SaveFrame(dialog, "hardware-confirmation.png");
            Press(dialog, Button(dialog, "Continue"));
            await Until(() => window.OwnedWindows.Any(w => w.Title == "Hardware configurations: Applied"));
            Assert.Equal(File.ReadAllBytes(fixture.Source), File.ReadAllBytes(fixture.Target));
            Assert.Equal(File.ReadAllBytes(fixture.Source), File.ReadAllBytes(fixture.NewTarget));
            Press(Assert.Single(window.OwnedWindows), Button(Assert.Single(window.OwnedWindows), "Close"));
            await Until(() => fixture.Vm.ActionsEnabled);
            Press(window, Button(window, "Restore last hardware copy"));
            await Until(() => window.OwnedWindows.Count == 1);
            Press(Assert.Single(window.OwnedWindows), Button(Assert.Single(window.OwnedWindows), "Continue"));
            await Until(() => window.OwnedWindows.Any(w => w.Title == "Hardware configurations: Applied"));
            Assert.Equal("original hardware", File.ReadAllText(fixture.Target));
            Assert.False(File.Exists(fixture.NewTarget));
            Press(Assert.Single(window.OwnedWindows), Button(Assert.Single(window.OwnedWindows), "Close"));
            await Until(() => fixture.Vm.ActionsEnabled);
        }
        finally { Close(window); }
    });

    [Fact]
    public Task Startup_ShowsCurrentVersionAndPatchAvailability_OptOutPersistsAcrossReopen() => Run(async () =>
    {
        using var fixture = new Fixture(enabled: true);
        var window = fixture.Open();
        try
        {
            await Until(() => fixture.Vm.ContentPackageCatalogStatus.StartsWith("Content-package releases checked:", StringComparison.Ordinal));
            Assert.Equal("v2.S1.51C", fixture.Vm.UpstreamAvailableVersion);
            Assert.Contains("current", fixture.Vm.UpstreamUpdateSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "v2.S1.51C" && t.IsVisible);
            Assert.Contains(fixture.Vm.AvailableContentPackages, p => p.AvailableVersion.Contains("1.0.0"));
            Assert.Equal("Required patches pending", fixture.Vm.MaintenancePatchSummary);
            Assert.Contains("0 of 3", fixture.Vm.MaintenancePatchDetail, StringComparison.Ordinal);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == fixture.Vm.MaintenancePatchSummary && text.IsVisible);
            Assert.True(fixture.Vm.CompatibilityModulesVisible);
            Assert.Equal(6, fixture.Vm.CompatibilityModules.Count);
            Assert.Equal(3, fixture.Vm.CompatibilityModules.Count(m => m.IsSelected && !m.CanChangeSelection));
            Assert.Equal(3, fixture.Vm.CompatibilityModules.Count(m => !m.IsSelected && m.CanChangeSelection));
            Assert.True(fixture.Vm.CanRunOptionalPatch);
            Assert.Empty(window.OwnedWindows);
            Assert.True(fixture.Vm.ActionsEnabled);
            Assert.DoesNotContain(fixture.Handler.Requests, u => u.EndsWith(".zip") || u.EndsWith(".7z"));
            SaveFrame(window, "startup-versions.png");
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var modules = window.GetVisualDescendants().OfType<ItemsControl>()
                .Single(c => ReferenceEquals(c.ItemsSource, fixture.Vm.CompatibilityModules));
            Assert.True(modules.IsEffectivelyVisible);
            Assert.Equal(6, modules.ItemCount);
            modules.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            SaveFrame(window, "startup-optional-patches.png");
            tabs.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            var toggle = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Check aircraft and patch releases at startup"));
            Assert.True(toggle.IsChecked);
            Press(window, toggle);
            Assert.False(fixture.Vm.CheckAircraftAndPatchUpdatesOnStartup);
            Assert.False(fixture.Store.Load().CheckAircraftAndPatchUpdatesOnStartup);
            SaveFrame(window, "startup-disabled.png");
            Close(window);
            fixture.Handler.Requests.Clear();
            window = fixture.Open();
            await Until(() => fixture.Vm.SelectedProduct?.IsDetected == true && fixture.Vm.InstallLog.Contains("Content package catalog:"));
            Assert.False(fixture.Vm.CheckAircraftAndPatchUpdatesOnStartup);
            Assert.DoesNotContain(fixture.Handler.Requests, u => u.Contains("737NG-Updates") || u.EndsWith("/releases/latest"));
            Assert.NotEqual("v2.S1.51C", fixture.Vm.UpstreamAvailableVersion);
            Assert.True(fixture.Vm.CompatibilityModulesVisible);
            Assert.Equal(6, fixture.Vm.CompatibilityModules.Count);
        }
        finally { Close(window); }
    });

    [Fact]
    public Task Startup_PatchPreviewPreservesChoicesAndInstalledSelection_AndDownloadsOnlyOnAction() => Run(async () =>
    {
        using var fixture = new Fixture(enabled: true);
        var window = fixture.Open();
        try
        {
            await Until(() => fixture.Vm.ContentPackageCatalogStatus.StartsWith("Content-package releases checked:", StringComparison.Ordinal));
            var optional = fixture.Vm.CompatibilityModules.Single(m => m.ModuleId == "cpdlc");
            optional.IsSelected = true;
            fixture.Vm.ActionsEnabled = false;
            fixture.Vm.ActionsEnabled = true;
            Assert.True(fixture.Vm.CompatibilityModules.Single(m => m.ModuleId == "cpdlc").IsSelected);
            Assert.DoesNotContain(fixture.Handler.Requests, u => u.EndsWith(".zip"));
            // The mock has release metadata but deliberately no archives. Review must
            // attempt preparation, report the failure and preserve selection, not write aircraft files.
            var aircraft = Path.GetDirectoryName(fixture.Vm.SelectedViewVariant!.AcfPath)!;
            var files = Directory.GetFiles(aircraft).ToDictionary(p => p, File.ReadAllBytes);
            await fixture.Vm.ReviewOptionalPatchCommand.ExecuteAsync(null);
            Assert.Contains(fixture.Handler.Requests, u => u.EndsWith(".zip"));
            Assert.Contains("rejected", fixture.Vm.OperationTitle);
            Assert.True(fixture.Vm.CompatibilityModules.Single(m => m.ModuleId == "cpdlc").IsSelected);
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(window.OwnedWindows);
            var state = new ToolStateStore(fixture.Store.RootPath, fixture.Store.Load().BackupRootPath);
            state.UpdateContentAndProduct(fixture.Vm.SelectedViewVariant!, (installation, product) =>
                installation.ContentComponents["wahltho.levelup-737ng.maintenance"] = new()
                { ComponentId = "wahltho.levelup-737ng.maintenance", EnabledModules = ["vnav", "fans-cdu", "weight-and-balance", "auto-jetway"] });
            Close(window);
            window = fixture.Open();
            await Until(() => fixture.Vm.ContentPackageCatalogStatus.StartsWith("Content-package releases checked:", StringComparison.Ordinal));
            Assert.True(fixture.Vm.CompatibilityModules.Single(m => m.ModuleId == "auto-jetway").IsSelected);
            Assert.False(fixture.Vm.CompatibilityModules.Single(m => m.ModuleId == "cpdlc").IsSelected);
            fixture.Vm.SetAircraftPathFromBrowse(Path.Combine(fixture.Xp, "Aircraft", "zibo-737ng"));
            Assert.False(fixture.Vm.CompatibilityModulesVisible);
            Assert.Empty(fixture.Vm.CompatibilityModules);
        }
        finally { Close(window); }
    });

    private static async Task Run(Func<Task> test)
    {
        // Use the framework-owned assembly session, as Avalonia's test adapters do.
        // Each dispatch still gets an isolated application. Per-test session disposal
        // hits a startup-task race in Avalonia 12.1.0 on fast Windows runners.
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(MainWindowUiTests).Assembly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await session.Dispatch(async () => { await test(); return true; }, timeout.Token);
    }

    private static Button Button(Window window, string text) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
    private static void Press(Window window, Control control)
    {
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(control.IsEffectivelyEnabled);
        Assert.True(control.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.True(condition(), "UI did not reach the expected state within 10 seconds.");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Close(Window window)
    {
        foreach (var child in window.OwnedWindows.ToArray()) child.Close();
        window.Close();
    }
    private static void SaveFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MTK_UI_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"mtk-ui-{Guid.NewGuid():N}");
        public string Xp => Path.Combine(_root, "XPlane");
        public string Source => Path.Combine(Xp, "Output/preferences/b738x_hw.cfg");
        public string Target => Path.Combine(Xp, "Output/preferences/737_80NG_hw.cfg");
        public string NewTarget => Path.Combine(Xp, "Output/preferences/737_9ENG_hw.cfg");
        public ToolkitSettingsStore Store { get; }
        public Releases Handler { get; } = new();
        private readonly HttpClient _client;
        public MainWindowViewModel Vm { get; private set; } = null!;
        public Fixture(bool enabled)
        {
            Directory.CreateDirectory(Path.Combine(Xp, "Resources"));
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            foreach (var r in AircraftReferenceCatalog.All)
            {
                var dir = Path.Combine(Xp, "Aircraft", r.Family);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, r.AcfFileName), $"1200 Version\nP acf/_name {r.ExpectedName}\nP acf/_descrip {r.ExpectedDescription}\nP acf/_studio {r.ExpectedStudioContains}\nP acf/_cgY 0\nP acf/_cgZ 0\n");
                File.WriteAllText(Path.Combine(dir, "version.txt"), "2.S1.51C");
            }
            File.WriteAllText(Source, "TOE BRAKE AXIS = 1\nTHROTTLE NOISE = 2\nPITCH 0 ZONE = 3\n");
            File.WriteAllText(Target, "original hardware");
            Store = new ToolkitSettingsStore(Path.Combine(_root, "state"));
            Store.Save(new ToolkitSettingsDocument
            {
                SelectedAircraftPath = Path.Combine(Xp, "Aircraft", "levelup-737ng"),
                CheckToolkitUpdatesOnStartup = false, CheckAircraftAndPatchUpdatesOnStartup = enabled,
                BackupRootPath = Path.Combine(_root, "backups"), AircraftUpdateCacheRootPath = Path.Combine(_root, "cache"),
                OfflinePackageRootPath = Path.Combine(_root, "offline"), DiagnosticsExportRootPath = Path.Combine(_root, "logs")
            });
            _client = new HttpClient(Handler);
        }
        public MainWindow Open(int width = 1500, int height = 1000)
        {
            var window = new MainWindow { Width = width, Height = height };
            Vm = new MainWindowViewModel(new MainWindowUserInteractionService(window), new NoAppUpdate(), Store, _client, new AircraftDetector(_root));
            window.DataContext = Vm;
            window.Show();
            return window;
        }
        public void Dispose() { _client.Dispose(); Directory.Delete(_root, true); }
    }
    private sealed class NoAppUpdate : IApplicationUpdateService
    {
        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync() => throw new Exception("Unexpected app check");
        public Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) => throw new Exception("Unexpected download");
        public void ApplyUpdateAndRestart() => throw new Exception("Unexpected restart");
    }
    public sealed class Releases : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);
        public Releases()
        {
            const string prefix = "https://github.com/petrolpram/737NG-Updates/releases/download/v2.S1.51C/";
            var sha = new string('1', 64);
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, productId = "levelup-737ng", packageType = "full", releaseVersion = "v2.S1.51C", releaseSequence = 5,
                contentRoot = "737NG Series", files = Array.Empty<object>(), deletedPaths = Array.Empty<string>(), archive = new { fileName = "LU.7z", size = 900, sha256 = sha } });
            _responses[prefix + "LU.manifest.json"] = manifest;
            _responses[LevelUpGitHubReleaseIndexSource.DefaultIndexUrl] = JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, productId = "levelup-737ng", repository = "petrolpram/737NG-Updates", releaseVersion = "v2.S1.51C", releaseSequence = 5, releaseTag = "v2.S1.51C", releaseChannel = "stable", minimumToolkitVersion = "0.13.8",
                packages = new[] { new { packageType = "full", releaseVersion = "v2.S1.51C", manifestFile = "LU.manifest.json", manifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), archiveFile = "LU.7z", archiveSize = 900, archiveSha256 = sha } }
            });
            using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/content-package-catalog.json")));
            var packages = catalog.RootElement.GetProperty("packages").EnumerateArray().ToArray();
            var sources = new List<(string Repo, string Pattern)>();
            foreach (var p in packages)
            {
                if (p.GetProperty("distribution").TryGetProperty("assetNamePattern", out var pattern)) sources.Add((p.GetProperty("repositoryUrl").GetString()!, pattern.GetString()!));
                if (p.TryGetProperty("members", out var members)) foreach (var member in members.EnumerateArray())
                {
                    var source = packages.Single(s => s.GetProperty("packageId").GetString() == member.GetProperty("packageId").GetString());
                    sources.Add((source.GetProperty("repositoryUrl").GetString()!, member.GetProperty("assetNamePattern").GetString()!));
                }
            }
            foreach (var group in sources.GroupBy(s => s.Repo))
            {
                var repo = new Uri(group.Key).AbsolutePath.Trim('/');
                _responses[$"https://api.github.com/repos/{repo}/releases/latest"] = JsonSerializer.SerializeToUtf8Bytes(new {
                    tag_name = "v1.0.0", html_url = group.Key + "/releases/tag/v1.0.0", draft = false, prerelease = false,
                    assets = group.Select(s => s.Pattern.Replace("*", "1.0.0")).Distinct().Select(name => new { name, browser_download_url = group.Key + "/releases/download/v1.0.0/" + name, size = 100, digest = "sha256:" + sha }).ToArray()
                });
            }
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(_responses.TryGetValue(url, out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
