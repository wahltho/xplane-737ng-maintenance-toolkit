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

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
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

    private async void ImportAircraftUpdatePackage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import aircraft update package or manifest",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Aircraft update packages")
                {
                    Patterns = ["*.zip", "*.7z", "*.manifest.json"],
                    MimeTypes = ["application/zip", "application/x-zip-compressed", "application/x-7z-compressed", "application/json"]
                }
            ]
        });

        if (files.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.ImportAircraftUpdatePackageAsync(files[0].Path.LocalPath);
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
            Title = "Select downloaded package cache folder",
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

    private async void BrowseOptionalPatchPackage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select optional declarative patch package",
            AllowMultiple = false
        });

        if (folders.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetOptionalPatchPackagePathFromBrowse(folders[0].Path.LocalPath);
    }

    private async void ReviewCatalogPatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button { DataContext: AvailableContentPackageStatus item }
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.ReviewCatalogPatchAsync(item);
    }

    private async void ApplyCatalogPatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button { DataContext: AvailableContentPackageStatus item }
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.ApplyCatalogPatchAsync(item);
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
