using Avalonia.Controls;
using Avalonia.Interactivity;
using LevelUp.NavTableUpdater.App.Services;

namespace LevelUp.NavTableUpdater.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(ConfirmationRequest request)
        : this()
    {
        DataContext = request;
        Title = request.Title;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
