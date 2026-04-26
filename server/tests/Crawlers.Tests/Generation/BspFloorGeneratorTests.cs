using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Xunit;

namespace Crawlers.Tests.Generation;

public class BspFloorGeneratorTests
{
    private static GenerationConfig DefaultConfig(int seed) => new()
    {
        Width = 60,
        Height = 40,
        Seed = seed,
        FloorNumber = 1,
        SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111")
    };

    public static IEnumerable<object[]> SeedRange =>
        Enumerable.Range(0, 20).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(SeedRange))]
    public void Floor_perimeter_is_all_walls(int seed)
    {
        var floor = new BspFloorGenerator().Generate(DefaultConfig(seed));
        for (int x = 0; x < floor.Width; x++)
        {
            Assert.Equal(TileType.Wall, floor.TileGrid[x, 0].Type);
            Assert.Equal(TileType.Wall, floor.TileGrid[x, floor.Height - 1].Type);
        }
        for (int y = 0; y < floor.Height; y++)
        {
            Assert.Equal(TileType.Wall, floor.TileGrid[0, y].Type);
            Assert.Equal(TileType.Wall, floor.TileGrid[floor.Width - 1, y].Type);
        }
    }

    [Theory]
    [MemberData(nameof(SeedRange))]
    public void Rooms_are_inside_floor_bounds(int seed)
    {
        var floor = new BspFloorGenerator().Generate(DefaultConfig(seed));
        Assert.NotEmpty(floor.Rooms);
        foreach (var r in floor.Rooms)
        {
            Assert.InRange(r.Bounds.X, 1, floor.Width - 2);
            Assert.InRange(r.Bounds.Y, 1, floor.Height - 2);
            Assert.InRange(r.Bounds.X + r.Bounds.Width, 1, floor.Width - 1);
            Assert.InRange(r.Bounds.Y + r.Bounds.Height, 1, floor.Height - 1);
        }
    }

    [Theory]
    [MemberData(nameof(SeedRange))]
    public void Rooms_do_not_overlap(int seed)
    {
        var floor = new BspFloorGenerator().Generate(DefaultConfig(seed));
        for (int i = 0; i < floor.Rooms.Count; i++)
            for (int j = i + 1; j < floor.Rooms.Count; j++)
                Assert.False(Overlaps(floor.Rooms[i].Bounds, floor.Rooms[j].Bounds),
                    $"Rooms {i} and {j} overlap on seed {seed}");
    }

    [Theory]
    [MemberData(nameof(SeedRange))]
    public void All_walkable_tiles_are_reachable_from_stairs_up(int seed)
    {
        var floor = new BspFloorGenerator().Generate(DefaultConfig(seed));
        var start = FindFirst(floor, TileType.StairsUp);
        Assert.True(start.HasValue, $"StairsUp missing on seed {seed}");

        var visited = FloodFill(floor, start!.Value);

        int walkableCount = 0;
        for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
                if (IsWalkable(floor.TileGrid[x, y].Type))
                    walkableCount++;

        Assert.Equal(walkableCount, visited.Count);
    }

    [Theory]
    [MemberData(nameof(SeedRange))]
    public void Stairs_up_and_down_both_exist(int seed)
    {
        var floor = new BspFloorGenerator().Generate(DefaultConfig(seed));
        int up = 0, down = 0;
        for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
            {
                if (floor.TileGrid[x, y].Type == TileType.StairsUp) up++;
                if (floor.TileGrid[x, y].Type == TileType.StairsDown) down++;
            }
        Assert.Equal(1, up);
        Assert.Equal(1, down);
    }

    [Fact]
    public void Same_seed_produces_identical_floor()
    {
        var a = new BspFloorGenerator().Generate(DefaultConfig(42));
        var b = new BspFloorGenerator().Generate(DefaultConfig(42));

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Rooms.Count, b.Rooms.Count);
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                Assert.Equal(a.TileGrid[x, y].Type, b.TileGrid[x, y].Type);
        for (int i = 0; i < a.Rooms.Count; i++)
            Assert.Equal(a.Rooms[i].Bounds, b.Rooms[i].Bounds);
    }

    [Fact]
    public void Different_seeds_produce_different_floors()
    {
        var gen = new BspFloorGenerator();
        var signatures = new HashSet<string>();
        for (int s = 0; s < 10; s++)
        {
            var f = gen.Generate(DefaultConfig(s));
            signatures.Add(GridSignature(f));
        }
        Assert.True(signatures.Count >= 8,
            $"Expected most seeds to produce distinct floors, got {signatures.Count}/10");
    }

    [Fact]
    public void Floor_metadata_is_propagated_from_config()
    {
        var sessionId = Guid.NewGuid();
        var cfg = new GenerationConfig
        {
            Width = 50,
            Height = 30,
            Seed = 7,
            FloorNumber = 3,
            SessionId = sessionId
        };
        var floor = new BspFloorGenerator().Generate(cfg);
        Assert.Equal(50, floor.Width);
        Assert.Equal(30, floor.Height);
        Assert.Equal(7, floor.Seed);
        Assert.Equal(3, floor.FloorNumber);
        Assert.Equal(sessionId, floor.SessionId);
        Assert.NotEqual(Guid.Empty, floor.Id);
    }

    [Fact]
    public void Throws_when_floor_too_small_for_min_partition()
    {
        var gen = new BspFloorGenerator();
        var cfg = new GenerationConfig { Width = 8, Height = 8, MinPartitionSize = 12 };
        Assert.Throws<ArgumentException>(() => gen.Generate(cfg));
    }

    private static bool Overlaps(Bounds a, Bounds b) =>
        a.X < b.X + b.Width &&
        a.X + a.Width > b.X &&
        a.Y < b.Y + b.Height &&
        a.Y + a.Height > b.Y;

    private static bool IsWalkable(TileType t) =>
        t == TileType.Floor || t == TileType.StairsUp || t == TileType.StairsDown || t == TileType.Door;

    private static Position? FindFirst(Floor floor, TileType type)
    {
        for (int x = 0; x < floor.Width; x++)
            for (int y = 0; y < floor.Height; y++)
                if (floor.TileGrid[x, y].Type == type) return new Position(x, y);
        return null;
    }

    private static HashSet<Position> FloodFill(Floor floor, Position start)
    {
        var visited = new HashSet<Position>();
        var queue = new Queue<Position>();
        queue.Enqueue(start);
        visited.Add(start);
        var deltas = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (dx, dy) in deltas)
            {
                var n = new Position(p.X + dx, p.Y + dy);
                if (n.X < 0 || n.Y < 0 || n.X >= floor.Width || n.Y >= floor.Height) continue;
                if (visited.Contains(n)) continue;
                if (!IsWalkable(floor.TileGrid[n.X, n.Y].Type)) continue;
                visited.Add(n);
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    private static string GridSignature(Floor floor)
    {
        var chars = new char[floor.Width * floor.Height];
        int i = 0;
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                chars[i++] = (char)('0' + (int)floor.TileGrid[x, y].Type);
        return new string(chars);
    }
}
