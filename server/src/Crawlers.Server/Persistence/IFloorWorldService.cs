using Crawlers.Domain.Models;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Owns the canonical world: one tile grid per floor depth, shared by every
/// session. Step 2 of the persistent-world phase.
/// </summary>
public interface IFloorWorldService
{
    /// <summary>
    /// Mint floors 1..<paramref name="maxFloor"/> at startup. For each floor:
    /// load the persisted row if present and at the current <see cref="WorldConstants.Version"/>;
    /// otherwise generate via BSP, run <c>EntityPlacer.MintStructure</c>, and
    /// persist. Stale rows (older Version) and any corpses on those floors
    /// are wiped before the re-mint — coordinates would no longer be coherent.
    /// </summary>
    Task MintAsync(int maxFloor, CancellationToken ct = default);

    /// <summary>
    /// Build a fresh per-session <see cref="Floor"/> for the given depth:
    /// cloned tile grid (so gameplay door bumps don't mutate the canonical
    /// world), cloned room list, copied boss-room metadata, empty entities.
    /// The caller invokes <c>EntityPlacer.PlaceEnemies</c> next to populate
    /// session-scoped enemies. Mints lazily if <paramref name="floorNumber"/>
    /// is beyond the initial mint range.
    /// </summary>
    Floor LoadFloorForSession(int floorNumber, Guid sessionId);
}
