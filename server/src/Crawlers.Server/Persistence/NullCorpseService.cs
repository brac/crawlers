using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

/// <summary>
/// No-op fallback used when ConnectionStrings:DefaultConnection is empty.
/// Corpses still drop on the in-memory floor for the rest of the run; only
/// the cross-run persistence (future continuation phase) is skipped.
/// </summary>
public class NullCorpseService : ICorpseService
{
    private readonly ILogger<NullCorpseService> _logger;
    public NullCorpseService(ILogger<NullCorpseService> logger) => _logger = logger;

    public Task RecordCorpseAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Corpse persistence disabled — death of player {PlayerId} in session {SessionId} not stored.",
            playerId, state.Session.Id);
        return Task.CompletedTask;
    }
}
