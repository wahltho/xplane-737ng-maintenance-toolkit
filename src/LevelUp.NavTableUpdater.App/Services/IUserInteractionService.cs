namespace LevelUp.NavTableUpdater.App.Services;

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText = "Cancel",
    bool ShowCancel = true);

public sealed record MessageRequest(
    string Title,
    string Message,
    string CloseText = "Close");

public interface IUserInteractionService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request);

    Task ShowMessageAsync(MessageRequest request);
}

internal sealed class RejectingUserInteractionService : IUserInteractionService
{
    public static RejectingUserInteractionService Instance { get; } = new();

    private RejectingUserInteractionService()
    {
    }

    public Task<bool> ConfirmAsync(ConfirmationRequest request) => Task.FromResult(false);

    public Task ShowMessageAsync(MessageRequest request) => Task.CompletedTask;
}
