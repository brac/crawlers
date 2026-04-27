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

        if (!InBounds(state.Floor, target)) return false;

        var targetType = state.Floor.TileGrid[target.X, target.Y].Type;

        // Bumping a closed door opens it but does NOT advance the player —
        // the next move steps through. FOV still recomputes from the player's
        // current position so they can see into the room past the open door.
        if (targetType == TileType.Door)
        {
            state.Floor.TileGrid[target.X, target.Y] = new Tile(TileType.OpenDoor);
            FieldOfView.Compute(state.Floor, state.Player.Position, state.Player.Stats.SightRadius, state.Player.FogOfWar);
            return true;
        }

        if (!IsWalkable(targetType)) return false;

        state.Player.Position = target;
        PickupItemsAt(state, target);
        MaybeLockBossRoomDoor(state);
        FieldOfView.Compute(state.Floor, target, state.Player.Stats.SightRadius, state.Player.FogOfWar);
        return true;
    }

    /// <summary>
    /// Lock the boss-room door the moment the player crosses into the room.
    /// The door tile sits on the rim (outside <see cref="Floor.BossRoomBounds"/>),
    /// so being inside bounds means the player has already stepped through.
    /// Direction-agnostic: works for doors on any rim of the room.
    /// </summary>
    private static void MaybeLockBossRoomDoor(SessionState state)
    {
        var floor = state.Floor;
        if (floor.BossDoor is not { } door) return;
        if (floor.BossRoomBounds is not { } bounds) return;
        if (floor.BossEntityId is not { } bossId) return;
        if (!Contains(bounds, state.Player.Position)) return;

        var boss = floor.Entities.FirstOrDefault(e => e.Id == bossId);
        if (boss is null || boss.State != EntityState.Alive) return;

        var t = floor.TileGrid[door.X, door.Y].Type;
        if (t == TileType.OpenDoor || t == TileType.Door)
            floor.TileGrid[door.X, door.Y] = new Tile(TileType.LockedDoor);
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

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

    private static bool InBounds(Floor floor, Position p) =>
        p.X >= 0 && p.Y >= 0 && p.X < floor.Width && p.Y < floor.Height;

    private static bool IsWalkable(TileType t) =>
        t == TileType.Floor
        || t == TileType.OpenDoor
        || t == TileType.StairsUp
        || t == TileType.StairsDown;
    // Wall, Door (handled separately above), and LockedDoor block movement.
}
