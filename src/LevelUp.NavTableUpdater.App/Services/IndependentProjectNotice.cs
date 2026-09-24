using LevelUp.NavTableUpdater.Core.Aircraft;

namespace LevelUp.NavTableUpdater.App.Services;

public static class IndependentProjectNotice
{
    public const string ToolkitUrl = "https://forums.x-plane.org/files/file/101018-x-plane-737ng-maintenance-toolkit/";
    public const string OptimizedXluaUrl = "https://forums.x-plane.org/files/file/97545-xlua-performance-improvements/";

    public static ConfirmationRequest ForAircraftMutation(
        ConfirmationRequest request,
        string? productFamily,
        bool optimizedXlua = false) =>
        (!string.IsNullOrWhiteSpace(productFamily)
         && AircraftProductIds.Normalize(productFamily) == AircraftProductIds.Zibo737Ng)
            ? request with
            {
                RequiresUnofficialAcknowledgement = true,
                IndependentProjectUrl = optimizedXlua ? OptimizedXluaUrl : ToolkitUrl,
                IndependentProjectLabel = optimizedXlua ? "Optimized XLua project and feedback" : "Toolkit project and feedback"
            }
            : request;
}
