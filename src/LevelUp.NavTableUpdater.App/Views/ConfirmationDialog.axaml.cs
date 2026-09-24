using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
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
        ConfirmButton.IsEnabled = !request.RequiresUnofficialAcknowledgement;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfirmationRequest { RequiresUnofficialAcknowledgement: true }
            && UnofficialAcknowledgement.IsChecked != true)
            return;
        Close(true);
    }

    private void Acknowledgement_Changed(object? sender, RoutedEventArgs e)
    {
        if (ConfirmButton is not null && DataContext is ConfirmationRequest request)
            ConfirmButton.IsEnabled = !request.RequiresUnofficialAcknowledgement || UnofficialAcknowledgement.IsChecked == true;
    }

    private void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfirmationRequest request)
            Process.Start(new ProcessStartInfo(request.IndependentProjectUrl) { UseShellExecute = true });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
