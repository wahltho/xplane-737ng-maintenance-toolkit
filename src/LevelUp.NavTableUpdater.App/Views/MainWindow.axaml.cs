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
}
