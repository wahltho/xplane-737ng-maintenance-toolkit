namespace LevelUp.NavTableUpdater.Core.Upstream;

public interface IAircraftUpdateIndexSource
{
    Task<AircraftUpdateIndex> LoadAsync(CancellationToken cancellationToken = default);
}
