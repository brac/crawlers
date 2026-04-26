using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Handles transition to the next dungeon floor when the player is standing
/// on a stairs-down tile. Generates the new floor deterministically from
/// the session's initial seed, places enemies (count scales with depth),
/// resets fog, and updates the session's CurrentFloorNumber.
///
/// Player HP and inventory carry over — descending is the way runs progress.
/// Caller holds SessionState.SyncRoot.
/// </summary>
public class DescendService
{
    private const int MaxEnemyCount = 10;

    private readonly BspFloorGenerator _floorGen = new();
    private readonly EntityPlacer _entityPlacer = new();

    public bool TryDescend(SessionState state)
    {
        // Must be exploring (not in combat or dead) and standing on stairs-down.
        if (state.Session.Mode != GameMode.Exploration) return false;
        var p = state.Player.Position;
        if (state.Floor.TileGrid[p.X, p.Y].Type != TileType.StairsDown) return false;

        int nextFloorNumber = state.Session.CurrentFloorNumber + 1;
        int newSeed = state.InitialSeed + (nextFloorNumber - 1);

        var newFloor = _floorGen.Generate(new GenerationConfig
        {
            SessionId = state.Session.Id,
            FloorNumber = nextFloorNumber,
            Seed = newSeed
        });

        // Difficulty scaling: +1 enemy per floor, capped.
        int enemyCount = Math.Min(EntityPlacer.DefaultEnemyCount + (nextFloorNumber - 1), MaxEnemyCount);
        _entityPlacer.Place(newFloor, new Random(newSeed ^ 0x5af3107a), enemyCount);

        var spawn = FindStairsUp(newFloor);
        var fog = new VisibilityState[newFloor.Width, newFloor.Height];
        FieldOfView.Compute(newFloor, spawn, state.Player.Stats.SightRadius, fog);

        state.Player.Position = spawn;
        state.Player.FogOfWar = fog;
        state.ReplaceFloor(newFloor);
        state.Session.FloorId = newFloor.Id;
        state.Session.CurrentFloorNumber = nextFloorNumber;
        // Stale combat log from a previous fight on the prior floor would only
        // confuse the client; clear it.
        state.ActiveCombat = null;
        return true;
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
