namespace Crawlers.Server.Persistence;

/// <summary>
/// Step 12: aggregate stats over the persistent world. Two consumers — the
/// public stats page (<see cref="GetGlobalStatsAsync"/>) and the per-floor
/// announcer flavor line (<see cref="GetFloorFlavorAsync"/>).
/// </summary>
public interface IWorldStatsService
{
    Task<WorldStatsDto> GetGlobalStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// One bleak announcer line about the floor — pulled from a small
    /// rotation of templates, weighted by what data is available
    /// ("most common killer here", "X have fallen on this floor", etc.)
    /// plus global facts. Returns a generic welcome line when the world
    /// hasn't accumulated enough data to fill any template.
    /// </summary>
    Task<string> GetFloorFlavorAsync(int floorNumber, CancellationToken ct = default);
}
