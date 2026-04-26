using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

public class BspFloorGenerator
{
    public Floor Generate(GenerationConfig config)
    {
        Validate(config);
        var rng = new Random(config.Seed);

        var rootBounds = new Bounds(1, 1, config.Width - 2, config.Height - 2);
        var root = new BspNode(rootBounds);
        BuildTree(root, rng, config, depth: 0);

        var rooms = new List<Room>();
        foreach (var leaf in root.Leaves())
        {
            var room = PlaceRoom(leaf.Bounds, rng, config);
            leaf.Room = room;
            rooms.Add(room);
        }

        var grid = new Tile[config.Width, config.Height];
        for (int x = 0; x < config.Width; x++)
            for (int y = 0; y < config.Height; y++)
                grid[x, y] = new Tile(TileType.Wall);

        foreach (var room in rooms)
            CarveRoom(grid, room.Bounds);

        ConnectTree(root, grid, rng);

        PlaceStairs(grid, rooms);

        return new Floor
        {
            Id = Guid.NewGuid(),
            SessionId = config.SessionId,
            FloorNumber = config.FloorNumber,
            Seed = config.Seed,
            Width = config.Width,
            Height = config.Height,
            TileGrid = grid,
            Rooms = rooms,
            Entities = new List<Entity>()
        };
    }

    private static void Validate(GenerationConfig c)
    {
        if (c.Width < c.MinPartitionSize + 2 || c.Height < c.MinPartitionSize + 2)
            throw new ArgumentException(
                $"Floor must be at least MinPartitionSize+2 in each dimension (got {c.Width}x{c.Height}, min {c.MinPartitionSize}).");
        if (c.MinRoomSize + 2 * c.RoomPadding > c.MinPartitionSize)
            throw new ArgumentException(
                "MinRoomSize + 2*RoomPadding must be <= MinPartitionSize so every partition can hold a room.");
    }

    private static void BuildTree(BspNode node, Random rng, GenerationConfig config, int depth)
    {
        if (depth >= config.MaxDepth) return;
        if (!node.TrySplit(rng, config.MinPartitionSize)) return;
        BuildTree(node.Left!, rng, config, depth + 1);
        BuildTree(node.Right!, rng, config, depth + 1);
    }

    private static Room PlaceRoom(Bounds partition, Random rng, GenerationConfig config)
    {
        int p = config.RoomPadding;
        int availW = partition.Width - 2 * p;
        int availH = partition.Height - 2 * p;
        int roomW = rng.Next(config.MinRoomSize, availW + 1);
        int roomH = rng.Next(config.MinRoomSize, availH + 1);
        int roomX = partition.X + p + rng.Next(0, availW - roomW + 1);
        int roomY = partition.Y + p + rng.Next(0, availH - roomH + 1);
        return new Room
        {
            Id = Guid.NewGuid(),
            Bounds = new Bounds(roomX, roomY, roomW, roomH)
        };
    }

    private static void CarveRoom(Tile[,] grid, Bounds b)
    {
        for (int x = b.X; x < b.X + b.Width; x++)
            for (int y = b.Y; y < b.Y + b.Height; y++)
                grid[x, y] = new Tile(TileType.Floor);
    }

    private static Room ConnectTree(BspNode node, Tile[,] grid, Random rng)
    {
        if (node.IsLeaf) return node.Room!;
        var leftRep = ConnectTree(node.Left!, grid, rng);
        var rightRep = ConnectTree(node.Right!, grid, rng);
        var a = Center(leftRep.Bounds);
        var b = Center(rightRep.Bounds);
        CarveCorridor(grid, a, b, rng);
        return rng.Next(2) == 0 ? leftRep : rightRep;
    }

    private static Position Center(Bounds b) =>
        new(b.X + b.Width / 2, b.Y + b.Height / 2);

    private static void CarveCorridor(Tile[,] grid, Position a, Position b, Random rng)
    {
        bool horizontalFirst = rng.Next(2) == 0;
        if (horizontalFirst)
        {
            CarveHorizontal(grid, a.X, b.X, a.Y);
            CarveVertical(grid, a.Y, b.Y, b.X);
        }
        else
        {
            CarveVertical(grid, a.Y, b.Y, a.X);
            CarveHorizontal(grid, a.X, b.X, b.Y);
        }
    }

    private static void CarveHorizontal(Tile[,] grid, int x1, int x2, int y)
    {
        int from = Math.Min(x1, x2), to = Math.Max(x1, x2);
        for (int x = from; x <= to; x++) grid[x, y] = new Tile(TileType.Floor);
    }

    private static void CarveVertical(Tile[,] grid, int y1, int y2, int x)
    {
        int from = Math.Min(y1, y2), to = Math.Max(y1, y2);
        for (int y = from; y <= to; y++) grid[x, y] = new Tile(TileType.Floor);
    }

    private static void PlaceStairs(Tile[,] grid, List<Room> rooms)
    {
        if (rooms.Count == 0) return;
        var first = rooms[0];
        var up = Center(first.Bounds);
        grid[up.X, up.Y] = new Tile(TileType.StairsUp);

        var farthest = rooms[0];
        int bestDist = -1;
        foreach (var r in rooms.Skip(1))
        {
            var c = Center(r.Bounds);
            int d = Math.Abs(c.X - up.X) + Math.Abs(c.Y - up.Y);
            if (d > bestDist) { bestDist = d; farthest = r; }
        }
        if (farthest.Id != first.Id)
        {
            var down = Center(farthest.Bounds);
            grid[down.X, down.Y] = new Tile(TileType.StairsDown);
        }
    }
}
