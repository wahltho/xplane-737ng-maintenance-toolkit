using Avalonia.Controls;
using LevelUp.NavTableUpdater.App.Views;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed class MainWindowUserInteractionService(Window owner) : IUserInteractionService
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request) =>
        new ConfirmationDialog(request).ShowDialog<bool>(owner);
}
