namespace Crawlers.Server.Persistence;

/// <summary>
/// Aggregate world stats served by <c>GET /api/world-stats</c> (Step 12).
/// All counts are over the entire history of the persistent world. Nullable
/// fields are null when the world hasn't accumulated enough data yet
/// (no kills → no top killer; no deaths → no deadliest tile; etc.).
/// </summary>
public record WorldStatsDto(
    int TotalPlayers,
    int TotalDeaths,
    int DeepestFloorReached,
    /// <summary>
    /// Percentage of identified players who have NOT yet appeared as a
    /// corpse. Computed as <c>100 * (TotalPlayers - distinctDeadPlayers) /
    /// TotalPlayers</c>. 0 when there are no players yet.
    /// </summary>
    double SurvivalRatePercent,
    double AverageFloorAtDeath,
    KillerStat? MostCommonKiller,
    DeadliestTile? DeadliestTile,
    DeadliestPlayer? MostFallenPlayer
);

public record KillerStat(string Killer, int Count);
public record DeadliestTile(int FloorNumber, int X, int Y, int Count);
public record DeadliestPlayer(string Username, int Count);
