using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

public static class FieldOfView
{
    /// <summary>
    /// Single-source compute: demote currently-Visible tiles to Explored,
    /// then mark everything reachable from <paramref name="center"/> within
    /// <paramref name="radius"/> as Visible. Used for tests and as a building
    /// block for multi-source recomputes.
    /// </summary>
    public static void Compute(Floor floor, Position center, int radius, VisibilityState[,] fog)
    {
        DemoteVisibleToExplored(floor, fog);
        CastRaysFrom(floor, center, radius, fog);
    }

    /// <summary>
    /// Multi-source recompute used for shared fog: every player on a floor is
    /// a vision source. Demote happens once at the start so a tile that's no
    /// longer in anyone's LOS drops to Explored, then each source contributes
    /// its visible set in a single pass.
    /// </summary>
    public static void RecomputeAll(
        Floor floor,
        IReadOnlyList<(Position center, int radius)> sources,
        VisibilityState[,] fog)
    {
        DemoteVisibleToExplored(floor, fog);
        foreach (var (center, radius) in sources)
            CastRaysFrom(floor, center, radius, fog);
    }

    /// <summary>
    /// Convenience wrapper: read positions + sight radii directly off a list
    /// of players. Used by services that already have the player list in hand.
    /// </summary>
    public static void RecomputeForFloor(
        Floor floor,
        VisibilityState[,] fog,
        IEnumerable<Player> players)
    {
        var sources = players
            .Select(p => (p.Position, p.Stats.SightRadius))
            .ToList();
        RecomputeAll(floor, sources, fog);
    }

    private static void DemoteVisibleToExplored(Floor floor, VisibilityState[,] fog)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (fog[x, y] == VisibilityState.Visible)
                    fog[x, y] = VisibilityState.Explored;
    }

    private static void CastRaysFrom(Floor floor, Position center, int radius, VisibilityState[,] fog)
    {
        int rSq = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > rSq) continue;
                int tx = center.X + dx;
                int ty = center.Y + dy;
                if (!InBounds(floor, tx, ty)) continue;
                CastRay(floor, fog, center, new Position(tx, ty));
            }
        }
    }

    private static void CastRay(Floor floor, VisibilityState[,] fog, Position from, Position to)
    {
        foreach (var p in BresenhamLine(from, to))
        {
            fog[p.X, p.Y] = VisibilityState.Visible;
            if (BlocksLos(floor.TileGrid[p.X, p.Y].Type)) return;
        }
    }

    private static bool BlocksLos(TileType t) =>
        t == TileType.Wall || t == TileType.Door || t == TileType.LockedDoor;

    public static IEnumerable<Position> BresenhamLine(Position from, Position to)
    {
        int x0 = from.X, y0 = from.Y;
        int x1 = to.X, y1 = to.Y;
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            yield return new Position(x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private static bool InBounds(Floor floor, int x, int y) =>
        x >= 0 && y >= 0 && x < floor.Width && y < floor.Height;
}
