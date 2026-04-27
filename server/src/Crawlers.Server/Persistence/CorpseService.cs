using Crawlers.Server.Sessions;

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

    public async Task RecordCorpseAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default)
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
            CauseOfDeath = causeOfDeath
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        db.Corpses.Add(entry);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded corpse {CorpseId} for player {PlayerId} on floor {Floor} at ({X},{Y})",
            entry.Id, player.Id, player.CurrentFloorNumber, player.Position.X, player.Position.Y);
    }
}
