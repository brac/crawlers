using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

public interface IRunHistoryService
{
    Task RecordDeathAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default);
}
