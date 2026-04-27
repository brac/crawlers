using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class MovementService
{
    public bool TryMove(SessionState state, Guid playerId, MoveDirection direction)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return false;

        var floor = state.GetFloorFor(player);
        var fog = state.GetFog(floor.FloorNumber);
        if (fog is null) return false;

        var (dx, dy) = ToDelta(direction);
        var target = new Position(player.Position.X + dx, player.Position.Y + dy);

        if (!InBounds(floor, target)) return false;

        var targetType = floor.TileGrid[target.X, target.Y].Type;

        // Bumping a closed door opens it but does NOT advance the player —
        // the next move steps through. Shared fog recomputes from all players
        // on this floor since the open doorway changes everyone's LOS.
        if (targetType == TileType.Door)
        {
            floor.TileGrid[target.X, target.Y] = new Tile(TileType.OpenDoor);
            FieldOfView.RecomputeForFloor(floor, fog, state.PlayersOnFloor(floor.FloorNumber));
            return true;
        }

        if (!IsWalkable(targetType)) return false;

        // Two living players cannot share a tile. Dead teammates (Mode ==
        // Resolution, pinned to their death tile until corpse-run mechanics
        // arrive) are filtered out so survivors can walk over them — the
        // spec's "corpses don't block movement" rule, applied to the player
        // record itself rather than just the Corpse entity beside it.
        foreach (var other in state.PlayersOnFloor(player.CurrentFloorNumber))
        {
            if (other.Id == player.Id) continue;
            if (other.Mode == GameMode.Resolution) continue;
            if (other.Position.Equals(target)) return false;
        }

        player.Position = target;
        PickupItemsAt(player, floor, target);
        MaybeLockBossRoomDoor(state, floor);
        FieldOfView.RecomputeForFloor(floor, fog, state.PlayersOnFloor(floor.FloorNumber));
        return true;
    }

    /// <summary>
    /// Lock the boss-room door once *every* alive player on this floor has
    /// crossed inside the bounds. In solo this still fires the moment the
    /// player steps in; in multi-player it waits until the last teammate is
    /// committed, so a straggler never gets shut out of the boss fight.
    /// Dead players (Mode == Resolution) are excluded — their corpse sitting
    /// in the corridor shouldn't keep the door propped open.
    /// </summary>
    private static void MaybeLockBossRoomDoor(SessionState state, Floor floor)
    {
        if (floor.BossDoor is not { } door) return;
        if (floor.BossRoomBounds is not { } bounds) return;
        if (floor.BossEntityId is not { } bossId) return;

        var boss = floor.Entities.FirstOrDefault(e => e.Id == bossId);
        if (boss is null || boss.State != EntityState.Alive) return;

        var t = floor.TileGrid[door.X, door.Y].Type;
        if (t != TileType.OpenDoor && t != TileType.Door) return;

        var aliveOnFloor = state.PlayersOnFloor(floor.FloorNumber)
            .Where(p => p.Mode != GameMode.Resolution)
            .ToList();
        if (aliveOnFloor.Count == 0) return;
        if (!aliveOnFloor.All(p => Contains(bounds, p.Position))) return;

        floor.TileGrid[door.X, door.Y] = new Tile(TileType.LockedDoor);
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

    private static void PickupItemsAt(Player player, Floor floor, Position p)
    {
        // Snapshot the matches first so we can mutate floor.Entities safely.
        var picked = floor.Entities
            .Where(e => e.Type == EntityType.Item
                        && e.State == EntityState.Alive
                        && e.Position.Equals(p)
                        && e.Item is not null)
            .ToList();
        foreach (var entity in picked)
        {
            player.Inventory.Add(entity.Item!);
            // Remove from floor; alternatively mark Fled to keep an audit trail.
            floor.Entities.Remove(entity);
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
