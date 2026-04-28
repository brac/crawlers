using Crawlers.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Postgres-backed corpse persistence. Resolves a fresh DbContext per call
/// from the IServiceScopeFactory because this service is a singleton (pulled
/// from the long-lived CombatRunner) but DbContext shouldn't outlive an
/// operation scope.
/// </summary>
public class CorpseService : ICorpseService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorpseService> _logger;

    public CorpseService(IServiceScopeFactory scopeFactory, ILogger<CorpseService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordCorpseAsync(
        SessionState state,
        Guid playerId,
        string? causeOfDeath,
        string? killerType,
        CancellationToken ct = default)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return;

        var entry = new CorpseEntry
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            SessionId = state.Session.Id,
            FloorNumber = player.CurrentFloorNumber,
            X = player.Position.X,
            Y = player.Position.Y,
            DiedAt = DateTimeOffset.UtcNow,
            CauseOfDeath = causeOfDeath,
            PlayerUsername = string.IsNullOrEmpty(player.Username) ? null : player.Username,
            KillerType = killerType,
            DeepestFloor = player.DeepestFloorReached
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        db.Corpses.Add(entry);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded corpse {CorpseId} for player {PlayerId} ('{Username}') on floor {Floor} at ({X},{Y}) — killer {Killer}",
            entry.Id, player.Id, player.Username, player.CurrentFloorNumber, player.Position.X, player.Position.Y, killerType ?? "(none)");
    }

    public async Task<IReadOnlyList<CorpseEntry>> GetByFloorAsync(int floorNumber, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        // Step 4 cap: only the most recent N corpses are hydrated for render.
        // Older rows stay in the DB for stats/heatmap queries — they're just
        // not stamped onto the in-memory floor.
        return await db.Corpses
            .AsNoTracking()
            .Where(c => c.FloorNumber == floorNumber)
            .OrderByDescending(c => c.DiedAt)
            .Take(WorldConstants.MaxRenderedCorpsesPerFloor)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TileHeat>> GetHeatmapByFloorAsync(int floorNumber, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        // Step 9: aggregate every corpse on the floor by tile. The render
        // cap doesn't apply here — heatmap intensity should reflect true
        // accumulated history even after the per-tile corpse cap kicks in.
        //
        // Projected through an anonymous type because EF/Npgsql can't
        // translate `new TileHeat(...)` (positional record ctor) into SQL —
        // anonymous types are translatable, then we map to the record on
        // the materialized result.
        var rows = await db.Corpses
            .AsNoTracking()
            .Where(c => c.FloorNumber == floorNumber)
            .GroupBy(c => new { c.X, c.Y })
            .Select(g => new { g.Key.X, g.Key.Y, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(WorldConstants.MaxHeatmapTilesPerFloor)
            .ToListAsync(ct);
        return rows.Select(r => new TileHeat(r.X, r.Y, r.Count)).ToList();
    }
}
