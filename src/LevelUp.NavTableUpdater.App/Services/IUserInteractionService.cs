namespace LevelUp.NavTableUpdater.App.Services;

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText = "Cancel");

public interface IUserInteractionService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request);
}

internal sealed class RejectingUserInteractionService : IUserInteractionService
{
    public static RejectingUserInteractionService Instance { get; } = new();

    private RejectingUserInteractionService()
    {
    }

    public Task<bool> ConfirmAsync(ConfirmationRequest request) => Task.FromResult(false);
}
