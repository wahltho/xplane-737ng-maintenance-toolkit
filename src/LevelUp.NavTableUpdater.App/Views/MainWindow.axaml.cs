using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LevelUp.NavTableUpdater.App.ViewModels;

namespace LevelUp.NavTableUpdater.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseAircraft_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Zibo or LevelUp aircraft folder",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetAircraftPathFromBrowse(folders[0].Path.LocalPath);
    }

    private async void ImportAircraftUpdateZip_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import aircraft update ZIP",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZIP packages")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip", "application/x-zip-compressed"]
                }
            ]
        });

        if (files.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ImportAircraftUpdateZip(files[0].Path.LocalPath);
    }

    private async void BrowseBackupRoot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private async void BrowseAircraftUpdateCacheRoot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private async void BrowseOfflinePackageRoot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private async void BrowseDiagnosticsExportRoot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
