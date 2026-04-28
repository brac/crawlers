using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

/// <summary>
/// No-op fallback used when ConnectionStrings:DefaultConnection is empty.
/// In-memory corpse Entities still drop on the live floor for the rest of
/// the run; world-scoped persistence (cross-session corpses) just doesn't
/// happen — every fresh process starts with an empty graveyard.
/// </summary>
public class NullCorpseService : ICorpseService
{
    private readonly ILogger<NullCorpseService> _logger;
    public NullCorpseService(ILogger<NullCorpseService> logger) => _logger = logger;

    public Task RecordCorpseAsync(
        SessionState state,
        Guid playerId,
        string? causeOfDeath,
        string? killerType,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Corpse persistence disabled — death of player {PlayerId} in session {SessionId} not stored.",
            playerId, state.Session.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CorpseEntry>> GetByFloorAsync(int floorNumber, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CorpseEntry>>(Array.Empty<CorpseEntry>());

    public Task<IReadOnlyList<TileHeat>> GetHeatmapByFloorAsync(int floorNumber, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TileHeat>>(Array.Empty<TileHeat>());
}
