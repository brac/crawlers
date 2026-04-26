using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class MovementService
{
    public bool TryMove(SessionState state, MoveDirection direction)
    {
        if (state.Player.FogOfWar is null) return false;

        var (dx, dy) = ToDelta(direction);
        var target = new Position(state.Player.Position.X + dx, state.Player.Position.Y + dy);

        if (!IsWalkable(state.Floor, target)) return false;

        state.Player.Position = target;
        PickupItemsAt(state, target);
        FieldOfView.Compute(state.Floor, target, state.Player.Stats.SightRadius, state.Player.FogOfWar);
        return true;
    }

    private static void PickupItemsAt(SessionState state, Position p)
    {
        // Snapshot the matches first so we can mutate floor.Entities safely.
        var picked = state.Floor.Entities
            .Where(e => e.Type == EntityType.Item
                        && e.State == EntityState.Alive
                        && e.Position.Equals(p)
                        && e.Item is not null)
            .ToList();
        foreach (var entity in picked)
        {
            state.Player.Inventory.Add(entity.Item!);
            // Remove from floor; alternatively mark Fled to keep an audit trail.
            state.Floor.Entities.Remove(entity);
        }
    }

    private static (int dx, int dy) ToDelta(MoveDirection d) => d switch
    {
        MoveDirection.North => (0, -1),
        MoveDirection.South => (0, 1),
        MoveDirection.East => (1, 0),
        MoveDirection.West => (-1, 0),
        _ => (0, 0)
    };

    private static bool IsWalkable(Floor floor, Position p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= floor.Width || p.Y >= floor.Height) return false;
        var t = floor.TileGrid[p.X, p.Y].Type;
        return t == TileType.Floor
            || t == TileType.Door
            || t == TileType.StairsUp
            || t == TileType.StairsDown;
    }
}
