using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.Core.Diagnostics;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool anonymizeDiagnosticPaths = true;

    [ObservableProperty]
    private string diagnosticsExportStatus = "Exports logs, package state, file hashes and backup checks. No aircraft files or backup contents are included. Nothing is uploaded.";

    public bool CanExportDiagnostics => !IsOperationRunning && !IsToolPackageOperationRunning
        && !IsResourcePackageOperationRunning && !IsLiveryPackageOperationRunning
        && !IsContentPackageCatalogCheckRunning && !IsUpstreamCheckRunning;

    partial void OnIsOperationRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    partial void OnIsToolPackageOperationRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    partial void OnIsResourcePackageOperationRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    partial void OnIsLiveryPackageOperationRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    partial void OnIsContentPackageCatalogCheckRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    partial void OnIsUpstreamCheckRunningChanged(bool value) => ExportDiagnosticsCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanExportDiagnostics), IncludeCancelCommand = true)]
    private async Task ExportDiagnostics(CancellationToken cancellationToken)
    {
        if (!CanExportDiagnostics) return;
        // Capture UI values before leaving the UI thread. Collection and hashing
        // then run offline, without changing installation or backup state.
        var context = CaptureDiagnosticContext();
        var destination = DiagnosticsExportRootPath;
        var anonymize = AnonymizeDiagnosticPaths;
        IsOperationRunning = true;
        DiagnosticsExportStatus = "Collecting diagnostics and checking recorded file hashes…";
        try
        {
            var path = await Task.Run(() => new DiagnosticReportExporter().Export(
                context, _stateStore, destination, anonymize, cancellationToken), cancellationToken);
            DiagnosticsExportStatus = $"Diagnostics saved: {path}";
            AppendLog($"Diagnostic package exported: {path}");
            await _userInteractionService.ShowMessageAsync(new MessageRequest(
                "Diagnostics saved",
                $"Saved to:\n{path}\n\nOpen the ZIP and review report.txt and operation-log.txt before sharing it. "
                + (anonymize ? "Local paths have been anonymized. " : "Local paths are included. ")
                + "Attach the ZIP to your support request. Nothing was uploaded and no aircraft files were changed."));
        }
        catch (OperationCanceledException)
        {
            DiagnosticsExportStatus = "Diagnostic export canceled. No aircraft files or installation state were changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Text.Json.JsonException)
        {
            DiagnosticsExportStatus = $"Could not export diagnostics: {ex.Message}";
            AppendLog(DiagnosticsExportStatus);
            await _userInteractionService.ShowMessageAsync(new MessageRequest("Diagnostic export failed", DiagnosticsExportStatus));
        }
        finally
        {
            IsOperationRunning = false;
        }
    }

    private DiagnosticExportContext CaptureDiagnosticContext()
    {
        var folder = SelectedProduct?.AircraftFolderPath ?? "";
        var files = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (SelectedViewVariant is { } variant) files["Selected ACF"] = Path.GetRelativePath(folder, variant.AcfPath);
            files["FMS script"] = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua";
            files["Tablet script"] = "plugins/xlua/scripts/B738.tablet/B738.tablet.lua";
        }
        return new DiagnosticExportContext
        {
            ToolkitVersion = ToolkitVersion,
            CatalogVersion = _contentPackageCatalog.CatalogVersion,
            CatalogStatus = ContentPackageCatalogStatus,
            AircraftFolder = folder,
            SelectedAircraftPath = SelectedAircraftPath,
            Product = SelectedProductName,
            ProductFamily = SelectedProduct?.Family ?? "",
            InstalledAircraftVersion = SelectedViewVariant?.LocalVersion ?? UpstreamLocalVersion,
            AvailableAircraftVersion = UpstreamAvailableVersion,
            XPlaneRoot = ResolveCurrentXPlaneRoot() ?? "",
            PreviousAircraftFolder = _movedAircraftCandidate?.PreviousFolder ?? "",
            InstallLog = InstallLog,
            OperationLog = OperationLog,
            KeyFiles = files,
            Status = new Dictionary<string, string>
            {
                ["Aircraft"] = StatusSummary,
                ["Update plan"] = UpstreamUpdateSummary,
                ["Last update check"] = UpstreamLastChecked,
                ["Aircraft release source"] = UpstreamSource,
                ["Active patch"] = $"{PackageId}: installed={LocalPackageVersion}; available={AvailablePackageVersion}; {LineEnding}",
                ["X-Plane process"] = XPlaneProcessStatus,
                ["Last operation"] = $"{OperationTitle}: {OperationStatus}; {OperationSubtitle}",
                ["Result help"] = OperationHelp is { } help
                    ? $"{help.Reason} Affected file or record: {help.AffectedPath ?? "not identified"}. Next step: {help.NextStep}"
                    : "",
                ["Moved aircraft"] = MovedAircraftNotice,
                ["Selected tool"] = $"{SelectedToolPackage?.PackageId}: installed={ToolInstalledVersion}; available={ToolAvailableVersion}; channel={SelectedToolReleaseChannel}; {ToolPackageStatus}"
            },
            Packages = AvailableContentPackages.Select(p => new DiagnosticPackageSummary(
                p.PackageId, p.InstalledVersion, p.AvailableVersion, p.Status)).ToArray(),
            ModuleSelections = CompatibilityModules.Select(m => new DiagnosticModuleSelection(
                m.ModuleId, m.PolicyLabel, m.IsSelected)).ToArray(),
            Findings = UpstreamFindings.Concat(_lastAircraftAnalysis?.Findings ?? []).ToArray()
        };
    }
}
