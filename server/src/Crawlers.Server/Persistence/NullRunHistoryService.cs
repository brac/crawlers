using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

/// <summary>
/// No-op fallback used when ConnectionStrings:DefaultConnection is empty —
/// e.g., running `dotnet run` outside docker with no Postgres available.
/// The app still functions; deaths are simply not persisted.
/// </summary>
public class NullRunHistoryService : IRunHistoryService
{
    private readonly ILogger<NullRunHistoryService> _logger;
    public NullRunHistoryService(ILogger<NullRunHistoryService> logger) => _logger = logger;

    public Task RecordDeathAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "RunHistory persistence disabled — death of player {PlayerId} in session {SessionId} not recorded.",
            playerId, state.Session.Id);
        return Task.CompletedTask;
    }
}
