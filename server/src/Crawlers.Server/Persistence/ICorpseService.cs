using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

public interface ICorpseService
{
    /// <summary>
    /// Append a corpse row for the player who just died. The in-memory floor
    /// already holds a Corpse Entity for this run; this row is for cross-run
    /// queries the continuation phase will need.
    /// </summary>
    Task RecordCorpseAsync(SessionState state, Guid playerId, string? causeOfDeath, CancellationToken ct = default);
}
