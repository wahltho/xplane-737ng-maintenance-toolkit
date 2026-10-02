using CommunityToolkit.Mvvm.ComponentModel;
using LevelUp.NavTableUpdater.App.Services;

namespace LevelUp.NavTableUpdater.App.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationHelpVisible))]
    private BlockedOperationHelp? operationHelp;

    public bool OperationHelpVisible => OperationPanelVisible && OperationHelp is not null;

    partial void OnOperationStatusChanged(string value) => RefreshOperationHelp();
    partial void OnOperationSubtitleChanged(string value) => RefreshOperationHelp();

    private void RefreshOperationHelp() =>
        OperationHelp = BlockedOperationHelp.FromResult(OperationStatus, OperationSubtitle);
}
