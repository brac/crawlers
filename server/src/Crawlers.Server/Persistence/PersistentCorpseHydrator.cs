using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Step 3 + 9 of the persistent-world phase: when a session loads a floor,
/// it pulls every corpse ever recorded at that depth (Step 3 — stamped
/// as in-memory Corpse <see cref="Entity"/> instances the snapshot mapper
/// renders like any other) and the per-tile death heatmap (Step 9 —
/// stashed on <see cref="SessionState"/> for the snapshot mapper to ship
/// to the client for environmental tile-tinting).
///
/// <para>
/// Synchronous wrappers over the async service calls — floor loads happen
/// inside <c>SessionState.SyncRoot</c>, and each read is one indexed
/// query per floor per session, so blocking is acceptable. The cost
/// amortizes over every move on that floor.
/// </para>
/// </summary>
internal static class PersistentCorpseHydrator
{
    public static void Hydrate(Floor floor, ICorpseService corpses, SessionState state, IWorldStatsService? stats = null)
    {
        var rows = corpses.GetByFloorAsync(floor.FloorNumber).GetAwaiter().GetResult();
        // Process oldest-first so the canonical "first death gets the tile"
        // ordering is stable: later corpses scatter around the original
        // occupant rather than the original occupant being displaced by a
        // newer death's load order.
        var rng = Random.Shared;
        foreach (var row in rows.OrderBy(r => r.DiedAt))
        {
            if (row.X < 0 || row.X >= floor.Width || row.Y < 0 || row.Y >= floor.Height) continue;

            var displayPos = CorpsePlacement.PickFreeTile(floor, new Position(row.X, row.Y), rng);
            floor.Entities.Add(new Entity
            {
                Id = row.Id,
                FloorId = floor.Id,
                Type = EntityType.Corpse,
                Name = "Corpse",
                Position = displayPos,
                State = EntityState.Alive,
                PlayerId = row.PlayerId,
                DiedAt = row.DiedAt,
                Username = row.PlayerUsername,
                KillerType = row.KillerType
            });
        }

        // Step 9 — derive the per-tile death heatmap from the same DB. This
        // is independent of the rendered-corpse cap (Step 4): even tiles
        // whose corpses have aged past the render window still contribute
        // their accumulated weight to the "feels dangerous" tinting.
        var heat = corpses.GetHeatmapByFloorAsync(floor.FloorNumber).GetAwaiter().GetResult();
        state.SetHeatmap(floor.FloorNumber, heat);

        // Step 12 — announcer flavor line for this floor's entry. Picked
        // once at hydration and frozen for the rest of the session so a
        // mid-run death on the same floor doesn't reshuffle the line on
        // the next snapshot. Optional service param so test paths that
        // don't care about the announcer can skip the extra plumbing.
        if (stats is not null)
        {
            var flavor = stats.GetFloorFlavorAsync(floor.FloorNumber).GetAwaiter().GetResult();
            state.SetFloorFlavor(floor.FloorNumber, flavor);
        }
    }
}
