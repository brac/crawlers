using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

public interface ICorpseService
{
    /// <summary>
    /// Append a corpse row for the player who just died. Captures the
    /// player's username and depth-of-death at the moment of writing so
    /// later renames or deeper-floor explorations don't retroactively
    /// rewrite the row. <paramref name="killerType"/> is the short
    /// archetype tag (e.g. "Husk") when the death came from combat,
    /// null otherwise.
    /// </summary>
    Task RecordCorpseAsync(
        SessionState state,
        Guid playerId,
        string? causeOfDeath,
        string? killerType,
        CancellationToken ct = default);

    /// <summary>
    /// World-scoped read: every corpse ever recorded on the given floor,
    /// ordered most-recent first. Sessions call this once per floor on
    /// first entry to hydrate persistent corpses into the in-memory
    /// floor — the snapshot mapper then renders them like any other
    /// Corpse entity.
    /// </summary>
    Task<IReadOnlyList<CorpseEntry>> GetByFloorAsync(int floorNumber, CancellationToken ct = default);

    /// <summary>
    /// Step 9 derived view: per-tile death counts for a floor, ordered
    /// hottest first and capped at <see cref="WorldConstants.MaxHeatmapTilesPerFloor"/>.
    /// Driven by aggregating every corpse row at the depth (no pre-stored
    /// table). Sessions read this once per floor at hydration time and
    /// the snapshot mapper ships it to the client for environmental
    /// tile-tinting.
    /// </summary>
    Task<IReadOnlyList<TileHeat>> GetHeatmapByFloorAsync(int floorNumber, CancellationToken ct = default);
}
