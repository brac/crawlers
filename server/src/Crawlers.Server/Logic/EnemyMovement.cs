using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Tile passability rules for enemy movement. Separates tile-type
/// walkability (used by BFS route planning) from full occupancy checks
/// (used at actual move time).
/// </summary>
public static class EnemyMovement
{
    /// <summary>
    /// Full passability check — tile type, boss-room containment, and
    /// entity occupancy (no stacking with living enemies or players).
    /// Caller holds <see cref="SessionState.SyncRoot"/>.
    /// </summary>
    public static bool CanEnter(Floor floor, Entity enemy, Position target, SessionState state)
    {
        if (!InBounds(floor, target)) return false;

        if (!IsTileTypePassable(floor.TileGrid[target.X, target.Y].Type)) return false;

        // Room-bound bosses never leave their stairwell room.
        if (floor.BossEntityId == enemy.Id && floor.BossRoomBounds is { } bounds)
            if (!Contains(bounds, target)) return false;

        // No stacking with other living enemies.
        foreach (var e in floor.Entities)
        {
            if (e.Id == enemy.Id) continue;
            if (e.Type != EntityType.Enemy) continue;
            if (e.State != EntityState.Alive) continue;
            if (e.Position == target) return false;
        }

        // No stepping onto a living player's tile.
        foreach (var p in state.PlayersOnFloor(floor.FloorNumber))
        {
            if (p.Mode == GameMode.Resolution) continue;
            if (p.Position == target) return false;
        }

        return true;
    }

    /// <summary>
    /// Tile-type-only predicate used as the BFS walkable delegate.
    /// Entity occupancy is intentionally excluded so route planning can
    /// find paths through tiles that may be transiently occupied —
    /// CanEnter handles occupancy at move time.
    /// </summary>
    public static bool IsTileTypePassable(TileType type) =>
        type == TileType.Floor || type == TileType.OpenDoor;

    private static bool InBounds(Floor floor, Position p) =>
        p.X >= 0 && p.Y >= 0 && p.X < floor.Width && p.Y < floor.Height;

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;
}
