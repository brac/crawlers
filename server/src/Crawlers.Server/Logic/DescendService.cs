using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Handles transition to the next dungeon floor when a player is standing
/// on a stairs-down tile. The next floor is generated deterministically from
/// the session's initial seed (so two players who descend to the same floor
/// independently arrive in the same dungeon), or reused if a teammate has
/// already been there. Player HP and inventory carry over.
///
/// Caller holds <see cref="SessionState.SyncRoot"/>.
/// </summary>
public class DescendService
{
    private const int MaxEnemyCount = 16;

    private readonly BspFloorGenerator _floorGen = new();
    private readonly EntityPlacer _entityPlacer = new();

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
        int seed = state.InitialSeed + (floorNumber - 1);

        var floor = _floorGen.Generate(new GenerationConfig
        {
            SessionId = state.Session.Id,
            FloorNumber = floorNumber,
            Seed = seed
        });

        // Difficulty scaling: +1 enemy per floor, capped.
        int enemyCount = Math.Min(EntityPlacer.DefaultEnemyCount + (floorNumber - 1), MaxEnemyCount);
        _entityPlacer.Place(floor, new Random(seed ^ 0x5af3107a), enemyCount);

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
