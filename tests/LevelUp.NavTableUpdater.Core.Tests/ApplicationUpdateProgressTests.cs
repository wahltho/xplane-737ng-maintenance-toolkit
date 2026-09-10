using LevelUp.NavTableUpdater.App.Services;
using LevelUp.NavTableUpdater.App.ViewModels;

namespace LevelUp.NavTableUpdater.Core.Tests;

public sealed class ApplicationUpdateProgressTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task QueuedProgressCannotOverwriteTerminalStateOrNextDownload(string outcome)
    {
        var previous = SynchronizationContext.Current;
        var context = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var service = new Service();
            var vm = new ApplicationUpdateViewModel(service, new Interaction(), () => true, _ => { }, _ => { });
            await vm.CheckForUpdatesAsync();
            var first = vm.DownloadCommand.ExecuteAsync(null);
            var oldProgress = service.Progress!;
            oldProgress.Report(50); // Held in the UI queue until after completion.
            if (outcome == "success") service.Completion.SetResult();
            else if (outcome == "failure") service.Completion.SetException(new IOException("offline"));
            else
            {
                vm.CancelCommand.Execute(null);
                var canceling = vm.Status;
                context.Drain();
                Assert.Equal(canceling, vm.Status);
                service.Completion.SetCanceled();
            }
            context.Drain();
            Assert.True(first.IsCompletedSuccessfully);
            var status = vm.Status;
            var progress = vm.Progress;
            oldProgress.Report(25);
            context.Drain();
            Assert.Equal(status, vm.Status);
            Assert.Equal(progress, vm.Progress);
            Assert.Equal(outcome == "success", vm.RestartVisible);

            await vm.CheckForUpdatesAsync();
            service.Completion = new TaskCompletionSource();
            var second = vm.DownloadCommand.ExecuteAsync(null);
            oldProgress.Report(90);
            service.Progress!.Report(20);
            context.Drain();
            Assert.Equal(20, vm.Progress);
            oldProgress.Report(95);
            context.Drain();
            Assert.Equal(20, vm.Progress);
            service.Completion.SetResult();
            context.Drain();
            Assert.True(second.IsCompletedSuccessfully);
            Assert.Equal(100, vm.Progress);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback, object?)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
        public void Drain() { while (_queue.TryDequeue(out var work)) work.Item1(work.Item2); }
    }
    private sealed class Service : IApplicationUpdateService
    {
        public IProgress<int>? Progress;
        public TaskCompletionSource Completion = new();
        public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync() => Task.FromResult(
            new ApplicationUpdateCheckResult(true, "0.13.1", "0.13.2", "", "https://example.invalid"));
        public Task DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        { Progress = progress; return Completion.Task; }
        public void ApplyUpdateAndRestart() { }
    }
    private sealed class Interaction : IUserInteractionService
    {
        public Task<bool> ConfirmAsync(ConfirmationRequest request) => Task.FromResult(false);
        public Task ShowMessageAsync(MessageRequest request) => Task.CompletedTask;
    }
}
