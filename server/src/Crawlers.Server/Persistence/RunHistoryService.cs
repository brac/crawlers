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

    public async Task RecordDeathAsync(SessionState state, string? causeOfDeath, CancellationToken ct = default)
    {
        var entry = new RunHistoryEntry
        {
            Id = Guid.NewGuid(),
            PlayerId = state.Player.Id,
            SessionId = state.Session.Id,
            Seed = state.Floor.Seed,
            StartedAt = state.Session.CreatedAt,
            EndedAt = DateTimeOffset.UtcNow,
            Outcome = "died",
            CauseOfDeath = causeOfDeath,
            DeepestFloor = state.Session.CurrentFloorNumber,
            EnemiesKilled = state.EnemiesKilled,
            FinalHp = state.Player.Stats.Hp,
            FinalMaxHp = state.Player.Stats.MaxHp,
            InventoryCount = state.Player.Inventory.Count
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        db.RunHistory.Add(entry);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded run history {EntryId} for session {SessionId} (cause: {Cause}, kills: {Kills})",
            entry.Id, state.Session.Id, causeOfDeath ?? "unknown", state.EnemiesKilled);
    }
}
