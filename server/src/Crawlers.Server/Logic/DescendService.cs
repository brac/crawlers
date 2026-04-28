using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Handles transition to the next dungeon floor when a player is standing
/// on a stairs-down tile. The next floor is loaded from the canonical
/// world (Step 2 of the persistent-world phase) — two players descending
/// to floor N from any session see the same dungeon — and reused if a
/// teammate has already been there. Player HP and inventory carry over.
///
/// Caller holds <see cref="SessionState.SyncRoot"/>.
/// </summary>
public class DescendService
{
    private const int MaxEnemyCount = 16;

    private readonly IFloorWorldService _world;
    private readonly ICorpseService _corpses;
    private readonly IWorldStatsService? _stats;
    private readonly EntityPlacer _entityPlacer = new();

    public DescendService(IFloorWorldService world, ICorpseService corpses, IWorldStatsService? stats = null)
    {
        _world = world;
        _corpses = corpses;
        _stats = stats;
    }

    public bool TryDescend(SessionState state, Guid playerId)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return false;

        // Per-player descent: a player in combat can't descend, but their
        // teammate on the same stairs can. So we check the *player's* combat
        // entry, not a session-wide mode flag.
        if (state.GetCombat(playerId) is not null) return false;

        var fromFloorNumber = player.CurrentFloorNumber;
        var fromFloor = state.GetFloorFor(player);
        var p = player.Position;
        if (fromFloor.TileGrid[p.X, p.Y].Type != TileType.StairsDown) return false;

        int nextFloorNumber = fromFloorNumber + 1;
        var nextFloor = state.GetFloor(nextFloorNumber) ?? GenerateFloor(state, nextFloorNumber);
        var anchor = FindStairsUp(nextFloor);

        // If a teammate has already descended and is still parked near the
        // stairs-up tile, BFS for the closest free walkable tile so the new
        // arrival doesn't land on top of them. Solo descent finds the anchor
        // immediately (no occupants).
        var occupied = state.PlayersOnFloor(nextFloorNumber)
            .Where(p => p.Id != playerId)
            .Select(p => p.Position)
            .ToHashSet();
        var spawn = AdjacentSpawn.PickOne(nextFloor, anchor, occupied);

        player.Position = spawn;
        player.CurrentFloorNumber = nextFloorNumber;
        if (nextFloorNumber > player.DeepestFloorReached)
            player.DeepestFloorReached = nextFloorNumber;

        // Source floor: one fewer pair of eyes — recompute so tiles only the
        // descender could see drop to Explored. (Skip if no shared fog grid,
        // which shouldn't happen but is defensive.)
        var fromFog = state.GetFog(fromFloorNumber);
        if (fromFog is not null)
            FieldOfView.RecomputeForFloor(fromFloor, fromFog, state.PlayersOnFloor(fromFloorNumber));

        // Destination floor: descender contributes their FOV to the shared
        // fog. If a teammate had already explored part of it the existing
        // Explored tiles stay Explored, and any tiles either of them currently
        // see become Visible.
        var nextFog = state.GetFog(nextFloorNumber)!;
        FieldOfView.RecomputeForFloor(nextFloor, nextFog, state.PlayersOnFloor(nextFloorNumber));

        return true;
    }

    private Floor GenerateFloor(SessionState state, int floorNumber)
    {
        var floor = _world.LoadFloorForSession(floorNumber, state.Session.Id);

        // Difficulty scaling: +1 enemy per floor, capped. Enemy RNG is
        // derived from the canonical floor seed so two sessions on the
        // same floor place enemies the same way (the only source of
        // session-to-session enemy variance is left intentionally as
        // their HP/death state, not their starting positions).
        int enemyCount = Math.Min(EntityPlacer.DefaultEnemyCount + (floorNumber - 1), MaxEnemyCount);
        _entityPlacer.PlaceEnemies(floor, new Random(floor.Seed ^ 0x5af3107a), enemyCount);
        PersistentCorpseHydrator.Hydrate(floor, _corpses, state, _stats);

        state.AddFloor(floor);
        return floor;
    }

    private static Position FindStairsUp(Floor floor)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == TileType.StairsUp)
                    return new Position(x, y);
        throw new InvalidOperationException("Generated floor has no stairs-up tile.");
    }
}
