using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

/// <summary>
/// Finalizes the stairs-down room so it has exactly one entrance — a closed
/// door — connected by a fresh corridor in whichever cardinal direction
/// reaches existing dungeon Floor in the fewest cells. Selection is fully
/// deterministic: shortest corridor wins, ties broken by direction priority
/// (S, N, E, W).
///
/// Steps per candidate room (≥ 5×5, sorted farthest-from-spawn):
///   1. Snapshot the tile grid (so failures roll back cleanly).
///   2. Seal every rim Floor tile back into Wall — the room becomes a
///      sealed-off chamber with no entries from BSP's original carving.
///   3. For each cardinal direction, count how many Wall cells we'd need
///      to carve before reaching existing Floor (the rest of the dungeon).
///      A direction "fails" if we'd walk off the map first.
///   4. Pick the shortest successful direction. South wins ties, then N/E/W.
///   5. Carve that corridor. Move stairs-down into the room at the cell
///      farthest in Chebyshev from the door (so the boss isn't on top of
///      the player when they walk in). Drop a closed Door on the rim cell.
///
/// If no direction succeeds for any candidate, returns nulls and the floor
/// is left untouched. Callers fall back to BSP's natural layout.
/// </summary>
public static class DoorPlacer
{
    private const int MinBossRoomDim = 5;

    // Tie-breaker priority: same length → first listed wins. Order is part
    // of the determinism contract — do not reorder without bumping seeds.
    private static readonly Direction[] DirectionPriority =
    {
        Direction.South,
        Direction.North,
        Direction.East,
        Direction.West,
    };

    private enum Direction { North, South, East, West }

    public static (Position? door, Bounds? roomBounds) FinalizeBossRoom(Floor floor)
    {
        var spawn = FindTile(floor, TileType.StairsUp);
        if (spawn is null) return (null, null);

        var existingDown = FindTile(floor, TileType.StairsDown);

        var candidates = floor.Rooms
            .Where(r => r.Bounds.Width >= MinBossRoomDim && r.Bounds.Height >= MinBossRoomDim)
            .OrderByDescending(r => Manhattan(spawn.Value, Center(r.Bounds)))
            .ToList();

        foreach (var room in candidates)
        {
            var snapshot = (Tile[,])floor.TileGrid.Clone();

            SealRim(floor, room.Bounds);

            // Sealing may have severed the spawn-reachable component. Compute
            // it once now so each direction can verify its termination cell is
            // actually connected back to spawn — otherwise the boss room would
            // be reachable only from some isolated corridor stub.
            var reachable = ComputeReachable(floor, spawn.Value);

            Direction? best = null;
            int bestLen = int.MaxValue;
            foreach (var dir in DirectionPriority)
            {
                var result = FindCorridorTermination(floor, room.Bounds, dir);
                if (result is null) continue;
                var (len, hit) = result.Value;
                if (!reachable[hit.X, hit.Y]) continue;
                if (len < bestLen)
                {
                    bestLen = len;
                    best = dir;
                }
            }

            if (best is null)
            {
                floor.TileGrid = snapshot;
                continue;
            }

            var door = CarveCorridor(floor, room.Bounds, best.Value);

            if (existingDown is { } prev)
                floor.TileGrid[prev.X, prev.Y] = new Tile(TileType.Floor);
            var bossCell = FarthestInteriorCell(room.Bounds, door);
            floor.TileGrid[bossCell.X, bossCell.Y] = new Tile(TileType.StairsDown);
            floor.TileGrid[door.X, door.Y] = new Tile(TileType.Door);

            return (door, room.Bounds);
        }

        return (null, null);
    }

    private static void SealRim(Floor floor, Bounds b)
    {
        foreach (var rim in RimCells(b))
        {
            if (!InBounds(floor, rim)) continue;
            if (floor.TileGrid[rim.X, rim.Y].Type == TileType.Floor)
                floor.TileGrid[rim.X, rim.Y] = new Tile(TileType.Wall);
        }
    }

    /// <summary>
    /// Walk outward from the rim cell in the given direction. Returns the
    /// number of cells that would be carved and the existing-Floor cell that
    /// terminates the carve. Returns null if we'd walk off the map without
    /// finding Floor. Pure — does not modify the grid.
    /// </summary>
    private static (int length, Position hit)? FindCorridorTermination(
        Floor floor, Bounds b, Direction d)
    {
        var (dx, dy) = DirVec(d);
        var start = StartCell(b, d);
        int len = 0;
        int x = start.X, y = start.Y;
        while (true)
        {
            if (!InBounds(floor, new Position(x, y))) return null;
            if (floor.TileGrid[x, y].Type == TileType.Floor)
                return (len, new Position(x, y));
            len++;
            x += dx;
            y += dy;
        }
    }

    /// <summary>
    /// 4-way BFS from <paramref name="from"/> through all walkable tiles
    /// (Floor / StairsUp / StairsDown). Used to verify a corridor candidate
    /// terminates in spawn-reachable Floor rather than an isolated stub left
    /// over after sealing the boss-room rim.
    /// </summary>
    private static bool[,] ComputeReachable(Floor floor, Position from)
    {
        var visited = new bool[floor.Width, floor.Height];
        var queue = new Queue<Position>();
        if (InBounds(floor, from))
        {
            visited[from.X, from.Y] = true;
            queue.Enqueue(from);
        }
        var deltas = new (int, int)[] { (0, -1), (0, 1), (1, 0), (-1, 0) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (dx, dy) in deltas)
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (nx < 0 || ny < 0 || nx >= floor.Width || ny >= floor.Height) continue;
                if (visited[nx, ny]) continue;
                var t = floor.TileGrid[nx, ny].Type;
                if (t != TileType.Floor && t != TileType.StairsUp && t != TileType.StairsDown) continue;
                visited[nx, ny] = true;
                queue.Enqueue(new Position(nx, ny));
            }
        }
        return visited;
    }

    /// <summary>
    /// Carve the corridor from the rim outward. Returns the door position
    /// (the rim cell — first cell carved). Caller must have already verified
    /// success via <see cref="CountCorridorLength"/>.
    /// </summary>
    private static Position CarveCorridor(Floor floor, Bounds b, Direction d)
    {
        var (dx, dy) = DirVec(d);
        var start = StartCell(b, d);
        int x = start.X, y = start.Y;
        while (floor.TileGrid[x, y].Type != TileType.Floor)
        {
            floor.TileGrid[x, y] = new Tile(TileType.Floor);
            x += dx;
            y += dy;
        }
        return start;
    }

    private static (int dx, int dy) DirVec(Direction d) => d switch
    {
        Direction.North => (0, -1),
        Direction.South => (0, 1),
        Direction.East => (1, 0),
        Direction.West => (-1, 0),
        _ => (0, 0),
    };

    private static Position StartCell(Bounds b, Direction d) => d switch
    {
        // Rim cell directly outside the room, centered on that side.
        Direction.North => new Position(b.X + b.Width / 2, b.Y - 1),
        Direction.South => new Position(b.X + b.Width / 2, b.Y + b.Height),
        Direction.East => new Position(b.X + b.Width, b.Y + b.Height / 2),
        Direction.West => new Position(b.X - 1, b.Y + b.Height / 2),
        _ => default,
    };

    private static Position FarthestInteriorCell(Bounds b, Position from)
    {
        Position best = new(b.X, b.Y);
        int bestDist = -1;
        for (int y = b.Y; y < b.Y + b.Height; y++)
        {
            for (int x = b.X; x < b.X + b.Width; x++)
            {
                int d = Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y));
                if (d > bestDist)
                {
                    bestDist = d;
                    best = new Position(x, y);
                }
            }
        }
        return best;
    }

    private static IEnumerable<Position> RimCells(Bounds b)
    {
        for (int x = b.X; x < b.X + b.Width; x++) yield return new Position(x, b.Y - 1);
        for (int x = b.X; x < b.X + b.Width; x++) yield return new Position(x, b.Y + b.Height);
        for (int y = b.Y; y < b.Y + b.Height; y++) yield return new Position(b.X - 1, y);
        for (int y = b.Y; y < b.Y + b.Height; y++) yield return new Position(b.X + b.Width, y);
    }

    private static bool InBounds(Floor floor, Position p) =>
        p.X >= 0 && p.Y >= 0 && p.X < floor.Width && p.Y < floor.Height;

    private static Position Center(Bounds b) =>
        new(b.X + b.Width / 2, b.Y + b.Height / 2);

    private static int Manhattan(Position a, Position b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
