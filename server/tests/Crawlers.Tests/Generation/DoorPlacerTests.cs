using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Xunit;

namespace Crawlers.Tests.Generation;

public class DoorPlacerTests
{
    private static (Floor floor, Position spawn, Position bossDown, Position door) Build(int seed)
    {
        var floor = new BspFloorGenerator().Generate(new GenerationConfig
        {
            SessionId = Guid.NewGuid(),
            FloorNumber = 1,
            Seed = seed
        });
        new EntityPlacer().Place(floor, new Random(seed ^ 0x5af3107a));
        var spawn = FindTile(floor, TileType.StairsUp)!.Value;
        var down = FindTile(floor, TileType.StairsDown)!.Value;
        Assert.NotNull(floor.BossDoor);
        return (floor, spawn, down, floor.BossDoor!.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(100)]
    [InlineData(999)]
    [InlineData(2026)]
    public void Boss_room_is_reachable_from_spawn(int seed)
    {
        var (floor, spawn, down, _) = Build(seed);
        Assert.True(IsReachable(floor, spawn, down),
            $"seed {seed}: stairs-down at {down} unreachable from spawn at {spawn}");
    }

    [Fact]
    public void Same_seed_places_door_and_stairs_identically()
    {
        const int seed = 1234;
        var a = Build(seed);
        var b = Build(seed);
        Assert.Equal(a.door, b.door);
        Assert.Equal(a.bossDown, b.bossDown);
        Assert.Equal(a.floor.BossRoomBounds, b.floor.BossRoomBounds);
    }

    [Fact]
    public void At_least_one_seed_produces_a_non_south_door()
    {
        // The 4-direction picker must actually exercise more than just south.
        // Probe a range of seeds and assert we see at least one non-south win.
        bool nonSouthSeen = false;
        for (int seed = 1; seed <= 200; seed++)
        {
            var (_, _, _, door) = Build(seed);
            // Find which rim the door sits on by comparing to its room bounds.
            // (BossRoomBounds is set on success.)
            // We use the ascii layout: south rim has door.Y == room.Y + room.Height.
            var floor = new BspFloorGenerator().Generate(new GenerationConfig
            {
                SessionId = Guid.NewGuid(),
                FloorNumber = 1,
                Seed = seed
            });
            new EntityPlacer().Place(floor, new Random(seed ^ 0x5af3107a));
            if (floor.BossRoomBounds is not { } b) continue;
            if (floor.BossDoor is not { } d) continue;
            bool isSouth = d.Y == b.Y + b.Height;
            if (!isSouth)
            {
                nonSouthSeen = true;
                break;
            }
        }
        Assert.True(nonSouthSeen, "No seed in 1..200 produced a non-south door — direction picker may be stuck on south.");
    }

    private static bool IsReachable(Floor floor, Position from, Position to)
    {
        var visited = new bool[floor.Width, floor.Height];
        var queue = new Queue<Position>();
        queue.Enqueue(from);
        visited[from.X, from.Y] = true;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p.Equals(to)) return true;
            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (1, 0), (-1, 0) })
            {
                int nx = p.X + dx, ny = p.Y + dy;
                if (nx < 0 || ny < 0 || nx >= floor.Width || ny >= floor.Height) continue;
                if (visited[nx, ny]) continue;
                if (!IsTraversable(floor.TileGrid[nx, ny].Type)) continue;
                visited[nx, ny] = true;
                queue.Enqueue(new Position(nx, ny));
            }
        }
        return false;
    }

    private static bool IsTraversable(TileType t) =>
        t == TileType.Floor
        || t == TileType.Door
        || t == TileType.OpenDoor
        || t == TileType.StairsUp
        || t == TileType.StairsDown;
    // LockedDoor and Wall block traversal — the boss door starts as Door,
    // which IS traversable for a player who walks through. The reachability
    // test cares about static reachability before lock fires.

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
