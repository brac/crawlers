using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

public static class FieldOfView
{
    public static void Compute(Floor floor, Position center, int radius, VisibilityState[,] fog)
    {
        DemoteVisibleToExplored(floor, fog);

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

    private static void DemoteVisibleToExplored(Floor floor, VisibilityState[,] fog)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (fog[x, y] == VisibilityState.Visible)
                    fog[x, y] = VisibilityState.Explored;
    }

    private static void CastRay(Floor floor, VisibilityState[,] fog, Position from, Position to)
    {
        foreach (var p in BresenhamLine(from, to))
        {
            fog[p.X, p.Y] = VisibilityState.Visible;
            if (floor.TileGrid[p.X, p.Y].Type == TileType.Wall) return;
        }
    }

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
