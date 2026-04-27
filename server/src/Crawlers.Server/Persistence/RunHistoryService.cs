using Crawlers.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Postgres-backed run history persistence. Resolves a fresh DbContext per
/// call from the IServiceScopeFactory because the service itself is a
/// singleton (it's pulled from the long-lived CombatRunner) but DbContext
/// must not outlive a request/operation scope.
/// </summary>
public class RunHistoryService : IRunHistoryService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunHistoryService> _logger;

    public RunHistoryService(IServiceScopeFactory scopeFactory, ILogger<RunHistoryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordDeathAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return;
        var floor = state.GetFloorFor(player);

        var entry = new RunHistoryEntry
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            SessionId = state.Session.Id,
            Seed = floor.Seed,
            StartedAt = state.Session.CreatedAt,
            EndedAt = DateTimeOffset.UtcNow,
            Outcome = "died",
            CauseOfDeath = causeOfDeath,
            DeepestFloor = player.CurrentFloorNumber,
            EnemiesKilled = state.EnemiesKilled,
            FinalHp = player.Stats.Hp,
            FinalMaxHp = player.Stats.MaxHp,
            InventoryCount = player.Inventory.Count
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        db.RunHistory.Add(entry);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded run history {EntryId} for player {PlayerId} in session {SessionId} (cause: {Cause}, kills: {Kills})",
            entry.Id, player.Id, state.Session.Id, causeOfDeath ?? "unknown", state.EnemiesKilled);
    }
}
