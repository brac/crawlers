namespace Crawlers.Server.Persistence;

/// <summary>
/// In-memory fallback for the no-DB dev path. Returns an empty stats DTO
/// (everything zero / null). The flavor line falls back to the generic
/// "no one has died here yet" because there's no data to weight a
/// per-floor templated line.
/// </summary>
public class NullWorldStatsService : IWorldStatsService
{
    public Task<WorldStatsDto> GetGlobalStatsAsync(CancellationToken ct = default) =>
        Task.FromResult(new WorldStatsDto(
            TotalPlayers: 0,
            TotalDeaths: 0,
            DeepestFloorReached: 0,
            SurvivalRatePercent: 0.0,
            AverageFloorAtDeath: 0.0,
            MostCommonKiller: null,
            DeadliestTile: null,
            MostFallenPlayer: null));

    public Task<string> GetFloorFlavorAsync(int floorNumber, CancellationToken ct = default) =>
        Task.FromResult($"Floor {floorNumber}. No one has died here yet. That will change.");
}
