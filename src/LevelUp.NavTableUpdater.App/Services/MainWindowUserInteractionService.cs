using Avalonia.Controls;
using LevelUp.NavTableUpdater.App.Views;

namespace LevelUp.NavTableUpdater.App.Services;

public sealed class MainWindowUserInteractionService(Window owner) : IUserInteractionService
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request) =>
        new ConfirmationDialog(request).ShowDialog<bool>(owner);

    public async Task ShowMessageAsync(MessageRequest request)
    {
        await new ConfirmationDialog(
                new ConfirmationRequest(
                    request.Title,
                    request.Message,
                    request.CloseText,
                    ShowCancel: false))
            .ShowDialog<bool>(owner);
    }
}
