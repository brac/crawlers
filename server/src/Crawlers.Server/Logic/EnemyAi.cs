using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation.Pathfinding;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Pure enemy-AI helpers — sight, targeting, and the per-enemy turn.
/// No side effects outside the enemy entity itself, so all helpers are
/// unit-testable without a full session stack.
/// </summary>
public static class EnemyAi
{
    /// <summary>
    /// Ticks an enemy continues toward the last-seen player tile after
    /// losing line of sight before giving up and going idle.
    /// </summary>
    public const int GiveUpGrace = 3;

    /// <summary>
    /// Returns the highest-priority live player this enemy can currently
    /// see, or <c>null</c> when no player is within LOS.
    ///
    /// Priority: closest by Chebyshev within <c>Stats.SightRadius</c>;
    /// <c>Player.Id</c> lexicographic order breaks ties for determinism.
    /// Dead players (Mode == Resolution) are always ignored.
    /// </summary>
    public static Player? FindTarget(SessionState state, Entity enemy, Floor floor)
    {
        if (enemy.Stats is null) return null;

        Player? best = null;
        int bestDist = int.MaxValue;

        foreach (var player in state.PlayersOnFloor(floor.FloorNumber))
        {
            if (player.Mode == GameMode.Resolution) continue;

            int dist = Chebyshev(enemy.Position, player.Position);
            if (dist > enemy.Stats.SightRadius) continue;
            if (!HasLos(floor, enemy.Position, player.Position)) continue;

            if (dist < bestDist || (dist == bestDist && player.Id.CompareTo(best!.Id) < 0))
            {
                best = player;
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>
    /// Attempt one AI movement step for <paramref name="enemy"/>.
    /// Finds a visible target (or falls back to the last-seen-tile grace
    /// window), BFS-paths one step toward the goal, validates the move
    /// with <see cref="EnemyMovement.CanEnter"/>, and applies it.
    ///
    /// Returns <c>true</c> when the enemy moved (caller should broadcast
    /// and check for engagement). Caller holds
    /// <see cref="SessionState.SyncRoot"/>.
    /// </summary>
    public static bool TakeTurn(SessionState state, Entity enemy, Floor floor)
    {
        if (enemy.Stats is null) return false;

        var target = FindTarget(state, enemy, floor);
        Position? goal;

        if (target is not null)
        {
            // Refresh AI memory and head toward the visible player.
            enemy.LastSeenPlayerTile = target.Position;
            enemy.GiveUpTicksRemaining = GiveUpGrace;
            goal = target.Position;
        }
        else if (enemy.GiveUpTicksRemaining > 0 && enemy.LastSeenPlayerTile.HasValue)
        {
            // Player broke LOS — continue toward the last-seen tile for
            // the grace window, then go idle.
            enemy.GiveUpTicksRemaining--;
            goal = enemy.LastSeenPlayerTile.Value;
        }
        else
        {
            return false;
        }

        // BFS uses tile-type-only walkability. Entity occupancy is
        // checked by CanEnter at move time — this lets BFS plan routes
        // through tiles that may be transiently occupied by other enemies.
        int radius = enemy.Stats.SightRadius + 2;
        bool Walkable(Position p) =>
            p.X >= 0 && p.Y >= 0 && p.X < floor.Width && p.Y < floor.Height
            && EnemyMovement.IsTileTypePassable(floor.TileGrid[p.X, p.Y].Type)
            && (floor.BossEntityId != enemy.Id
                || floor.BossRoomBounds is null
                || BossContains(floor.BossRoomBounds.Value, p));

        var step = Bfs.NextStep(Walkable, enemy.Position, goal.Value, radius);
        if (step is null) return false;

        if (!EnemyMovement.CanEnter(floor, enemy, step.Value, state)) return false;

        enemy.Position = step.Value;
        return true;
    }

    public static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // -------------------------------------------------------------------

    private static bool HasLos(Floor floor, Position from, Position to)
    {
        foreach (var p in FieldOfView.BresenhamLine(from, to))
        {
            if (p == from) continue;
            if (p == to) return true;
            if (BlocksLos(floor.TileGrid[p.X, p.Y].Type)) return false;
        }
        return true;
    }

    private static bool BlocksLos(TileType t) =>
        t == TileType.Wall || t == TileType.Door || t == TileType.LockedDoor;

    private static bool BossContains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;
}
