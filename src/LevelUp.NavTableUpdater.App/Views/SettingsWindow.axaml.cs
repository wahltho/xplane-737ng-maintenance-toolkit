using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LevelUp.NavTableUpdater.App.ViewModels;

namespace LevelUp.NavTableUpdater.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BrowseBackupRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select toolkit backup folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetBackupRootPathFromBrowse(folders[0].Path.LocalPath);
    }

    private async void BrowseAircraftUpdateCacheRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select aircraft update ZIP cache folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetAircraftUpdateCacheRootPathFromBrowse(folders[0].Path.LocalPath);
    }

    private async void BrowseOfflinePackageRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select offline VNAV package folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetOfflinePackageRootPathFromBrowse(folders[0].Path.LocalPath);
    }

    private async void BrowseDiagnosticsExportRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select diagnostics export folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetDiagnosticsExportRootPathFromBrowse(folders[0].Path.LocalPath);
    }
}
