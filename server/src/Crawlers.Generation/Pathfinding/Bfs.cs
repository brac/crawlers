using Crawlers.Domain.Models;

namespace Crawlers.Generation.Pathfinding;

/// <summary>
/// BFS pathfinder on the tile grid. Returns the first step toward a goal
/// without loading session state — callers supply the walkability predicate.
/// </summary>
public static class Bfs
{
    /// <summary>
    /// Returns the first step from <paramref name="from"/> toward
    /// <paramref name="to"/> along the shortest walkable path, or
    /// <c>null</c> if <paramref name="to"/> is unreachable within
    /// <paramref name="maxRadius"/> steps.
    ///
    /// The <paramref name="walkable"/> predicate is called for every candidate
    /// tile, including the destination itself. Callers that want to route
    /// toward an occupied tile (e.g. a player's position) should allow the
    /// destination through the predicate.
    /// </summary>
    public static Position? NextStep(
        Func<Position, bool> walkable,
        Position from,
        Position to,
        int maxRadius)
    {
        if (from == to) return null;

        var visited = new HashSet<Position> { from };
        var queue = new Queue<(Position pos, Position firstStep)>();

        foreach (var nb in CardinalNeighbors(from))
        {
            if (!walkable(nb)) continue;
            if (!visited.Add(nb)) continue;
            queue.Enqueue((nb, nb));
        }

        while (queue.Count > 0)
        {
            var (pos, firstStep) = queue.Dequeue();

            if (pos == to) return firstStep;

            if (Chebyshev(from, pos) >= maxRadius) continue;

            foreach (var nb in CardinalNeighbors(pos))
            {
                if (!walkable(nb)) continue;
                if (!visited.Add(nb)) continue;
                queue.Enqueue((nb, firstStep));
            }
        }

        return null;
    }

    private static IEnumerable<Position> CardinalNeighbors(Position p)
    {
        yield return new Position(p.X, p.Y - 1);
        yield return new Position(p.X, p.Y + 1);
        yield return new Position(p.X + 1, p.Y);
        yield return new Position(p.X - 1, p.Y);
    }

    internal static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
