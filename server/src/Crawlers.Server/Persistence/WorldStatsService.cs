using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Postgres-backed implementation. Aggregations are cheap (the corpses
/// table is indexed on the fields we group by) and called rarely (page
/// load, per-floor entry), so no caching today — revisit if the world
/// grows past tens of thousands of corpses.
/// </summary>
public class WorldStatsService : IWorldStatsService
{
    private readonly IServiceScopeFactory _scopes;

    public WorldStatsService(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<WorldStatsDto> GetGlobalStatsAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();

        var totalPlayers = await db.Players.CountAsync(ct);
        var totalDeaths = await db.Corpses.CountAsync(ct);
        var deepestFloor = totalDeaths == 0
            ? 0
            : await db.Corpses.MaxAsync(c => c.DeepestFloor, ct);
        var avgFloor = totalDeaths == 0
            ? 0.0
            : await db.Corpses.AverageAsync(c => (double)c.FloorNumber, ct);
        var distinctDeadPlayers = await db.Corpses
            .Select(c => c.PlayerId)
            .Distinct()
            .CountAsync(ct);

        // Survival rate: identified players who have never died.
        var survivalRate = totalPlayers == 0
            ? 0.0
            : 100.0 * (totalPlayers - distinctDeadPlayers) / totalPlayers;

        // Top killer — anonymous projection then map (same EF translation
        // dance Step 9 needed for TileHeat).
        var topKillerRaw = await db.Corpses
            .Where(c => c.KillerType != null)
            .GroupBy(c => c.KillerType!)
            .Select(g => new { Killer = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);
        var topKiller = topKillerRaw is null
            ? null
            : new KillerStat(topKillerRaw.Killer, topKillerRaw.Count);

        // Deadliest tile.
        var deadliestRaw = await db.Corpses
            .GroupBy(c => new { c.FloorNumber, c.X, c.Y })
            .Select(g => new { g.Key.FloorNumber, g.Key.X, g.Key.Y, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);
        var deadliestTile = deadliestRaw is null
            ? null
            : new DeadliestTile(deadliestRaw.FloorNumber, deadliestRaw.X, deadliestRaw.Y, deadliestRaw.Count);

        // Most-fallen player. Resolve display name from the most recent
        // corpse's PlayerUsername (frozen-at-death) so renames don't
        // overwrite the public ranking, with a fallback to the players
        // table if every corpse for that id has a null username.
        var fallenRaw = await db.Corpses
            .GroupBy(c => c.PlayerId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);
        DeadliestPlayer? mostFallen = null;
        if (fallenRaw is not null)
        {
            var name = await db.Corpses
                .Where(c => c.PlayerId == fallenRaw.PlayerId && c.PlayerUsername != null)
                .OrderByDescending(c => c.DiedAt)
                .Select(c => c.PlayerUsername!)
                .FirstOrDefaultAsync(ct)
                ?? await db.Players
                    .Where(p => p.Id == fallenRaw.PlayerId)
                    .Select(p => p.Username)
                    .FirstOrDefaultAsync(ct)
                ?? "Unknown";
            mostFallen = new DeadliestPlayer(name, fallenRaw.Count);
        }

        return new WorldStatsDto(
            TotalPlayers: totalPlayers,
            TotalDeaths: totalDeaths,
            DeepestFloorReached: deepestFloor,
            SurvivalRatePercent: survivalRate,
            AverageFloorAtDeath: avgFloor,
            MostCommonKiller: topKiller,
            DeadliestTile: deadliestTile,
            MostFallenPlayer: mostFallen);
    }

    public async Task<string> GetFloorFlavorAsync(int floorNumber, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();

        var floorDeaths = await db.Corpses.CountAsync(c => c.FloorNumber == floorNumber, ct);
        var floorTopKillerRaw = await db.Corpses
            .Where(c => c.FloorNumber == floorNumber && c.KillerType != null)
            .GroupBy(c => c.KillerType!)
            .Select(g => new { Killer = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);
        var floorDeadliestRaw = await db.Corpses
            .Where(c => c.FloorNumber == floorNumber)
            .GroupBy(c => new { c.X, c.Y })
            .Select(g => new { g.Key.X, g.Key.Y, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct);

        var totalPlayers = await db.Players.CountAsync(ct);
        var totalDeaths = await db.Corpses.CountAsync(ct);
        var distinctDead = await db.Corpses.Select(c => c.PlayerId).Distinct().CountAsync(ct);
        var survivalRate = totalPlayers == 0
            ? 0.0
            : 100.0 * (totalPlayers - distinctDead) / totalPlayers;

        return FloorFlavorPicker.Pick(
            floorNumber: floorNumber,
            floorDeaths: floorDeaths,
            floorTopKiller: floorTopKillerRaw?.Killer,
            floorTopKillerCount: floorTopKillerRaw?.Count ?? 0,
            deadliestTileX: floorDeadliestRaw?.X,
            deadliestTileY: floorDeadliestRaw?.Y,
            deadliestTileCount: floorDeadliestRaw?.Count ?? 0,
            totalPlayers: totalPlayers,
            totalDeaths: totalDeaths,
            survivalRatePercent: survivalRate);
    }
}
