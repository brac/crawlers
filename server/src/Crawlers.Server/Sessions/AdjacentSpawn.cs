using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

/// <summary>
/// Picks N spawn positions clustered around an anchor tile, one per player.
/// BFS from the anchor over walkable tiles, skipping any tile already taken
/// by an earlier player. Used so a multi-player party starts adjacent in
/// the same room rather than scattered across the dungeon.
/// </summary>
public static class AdjacentSpawn
{
    /// <summary>
    /// Single-target variant used for late-joiners. BFS from the anchor and
    /// return the first walkable tile that isn't already in <paramref name="occupied"/>.
    /// </summary>
    public static Position PickOne(Floor floor, Position anchor, IReadOnlySet<Position> occupied)
    {
        var visited = new HashSet<Position> { anchor };
        var queue = new Queue<Position>();
        queue.Enqueue(anchor);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (IsWalkable(floor, p) && !occupied.Contains(p)) return p;
            foreach (var n in Neighbors(p))
            {
                if (!InBounds(floor, n)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        throw new InvalidOperationException(
            $"Floor has no unoccupied walkable tile reachable from anchor {anchor}.");
    }

    public static List<Position> Pick(Floor floor, Position anchor, int count)
    {
        if (count <= 0) return new();

        var picked = new List<Position>(count);
        var taken = new HashSet<Position>();
        var visited = new HashSet<Position> { anchor };
        var queue = new Queue<Position>();
        queue.Enqueue(anchor);

        while (queue.Count > 0 && picked.Count < count)
        {
            var p = queue.Dequeue();
            if (IsWalkable(floor, p) && !taken.Contains(p))
            {
                picked.Add(p);
                taken.Add(p);
            }
            foreach (var n in Neighbors(p))
            {
                if (!InBounds(floor, n)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }

        if (picked.Count < count)
            throw new InvalidOperationException(
                $"Floor doesn't have {count} reachable walkable tiles from anchor {anchor}.");

        return picked;
    }

    private static IEnumerable<Position> Neighbors(Position p)
    {
        yield return new Position(p.X, p.Y - 1);
        yield return new Position(p.X + 1, p.Y);
        yield return new Position(p.X, p.Y + 1);
        yield return new Position(p.X - 1, p.Y);
    }

    private static bool InBounds(Floor f, Position p) =>
        p.X >= 0 && p.Y >= 0 && p.X < f.Width && p.Y < f.Height;

    private static bool IsWalkable(Floor f, Position p)
    {
        var t = f.TileGrid[p.X, p.Y].Type;
        return t == TileType.Floor
            || t == TileType.OpenDoor
            || t == TileType.Door
            || t == TileType.StairsUp
            || t == TileType.StairsDown;
    }
}
