using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Xunit;

namespace Crawlers.Tests.Generation;

public class EntityPlacerTests
{
    // Floor 2 keeps the random Husk/Rasper/Hulk pool. Tests in this class
    // exercise that path; the floor-1 boss layout has its own tests below.
    private static Floor MakeFloor(int seed, int floorNumber = 2) =>
        new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = seed,
            FloorNumber = floorNumber
        });

    [Fact]
    public void Places_requested_enemy_count()
    {
        var floor = MakeFloor(seed: 1);
        new EntityPlacer().Place(floor, new Random(1), enemyCount: 5);
        Assert.Equal(5, floor.Entities.Count(e => e.Type == EntityType.Enemy));
    }

    [Fact]
    public void All_placed_are_alive_enemies_on_floor_tiles()
    {
        var floor = MakeFloor(seed: 2);
        new EntityPlacer().Place(floor, new Random(2), enemyCount: 4);
        Assert.NotEmpty(floor.Entities);
        foreach (var e in floor.Entities)
        {
            Assert.Equal(EntityType.Enemy, e.Type);
            Assert.Equal(EntityState.Alive, e.State);
            Assert.Equal(TileType.Floor, floor.TileGrid[e.Position.X, e.Position.Y].Type);
        }
    }

    [Fact]
    public void Never_places_on_stairs()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var floor = MakeFloor(seed: seed);
            new EntityPlacer().Place(floor, new Random(seed), enemyCount: 6);
            foreach (var e in floor.Entities)
            {
                var t = floor.TileGrid[e.Position.X, e.Position.Y].Type;
                Assert.NotEqual(TileType.StairsUp, t);
                Assert.NotEqual(TileType.StairsDown, t);
            }
        }
    }

    [Fact]
    public void Never_places_in_spawn_room()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var floor = MakeFloor(seed: seed);
            var stairs = FindStairsUp(floor);
            Assert.True(stairs.HasValue);
            var spawnRoom = floor.Rooms.FirstOrDefault(r => Contains(r.Bounds, stairs!.Value));
            Assert.NotNull(spawnRoom);

            new EntityPlacer().Place(floor, new Random(seed), enemyCount: 6);

            foreach (var e in floor.Entities)
                Assert.False(Contains(spawnRoom!.Bounds, e.Position),
                    $"Enemy placed inside spawn room on seed {seed}");
        }
    }

    [Fact]
    public void Same_seed_produces_same_placement()
    {
        var floor1 = MakeFloor(seed: 42);
        var floor2 = MakeFloor(seed: 42);

        new EntityPlacer().Place(floor1, new Random(99), enemyCount: 4);
        new EntityPlacer().Place(floor2, new Random(99), enemyCount: 4);

        var positions1 = floor1.Entities.Select(e => e.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var positions2 = floor2.Entities.Select(e => e.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        Assert.Equal(positions1, positions2);
    }

    [Fact]
    public void FloorOne_places_big_slug_on_stairs_down()
    {
        var floor = MakeFloor(seed: 7, floorNumber: 1);
        new EntityPlacer().Place(floor, new Random(7), enemyCount: 4);

        var stairsDown = FindTile(floor, TileType.StairsDown);
        Assert.NotNull(stairsDown);

        var bossOnStairs = floor.Entities.SingleOrDefault(e =>
            e.Type == EntityType.Enemy &&
            e.Position.Equals(stairsDown!.Value) &&
            e.Name == "Big Slug");
        Assert.NotNull(bossOnStairs);
        Assert.Equal(22, bossOnStairs!.Stats!.MaxHp);
    }

    [Fact]
    public void FloorOne_other_enemies_are_all_tiny_slugs()
    {
        var floor = MakeFloor(seed: 11, floorNumber: 1);
        new EntityPlacer().Place(floor, new Random(11), enemyCount: 4);

        var stairsDown = FindTile(floor, TileType.StairsDown);
        var nonBoss = floor.Entities
            .Where(e => e.Type == EntityType.Enemy)
            .Where(e => stairsDown is null || !e.Position.Equals(stairsDown.Value))
            .ToList();

        Assert.NotEmpty(nonBoss);
        Assert.All(nonBoss, e => Assert.Equal("Slug", e.Name));
    }

    [Fact]
    public void FloorOne_enemy_count_is_requested_count_plus_boss()
    {
        var floor = MakeFloor(seed: 3, floorNumber: 1);
        new EntityPlacer().Place(floor, new Random(3), enemyCount: 4);

        // 4 TinySlugs + 1 BigSlug guard
        Assert.Equal(5, floor.Entities.Count(e => e.Type == EntityType.Enemy));
    }

    private static Position? FindStairsUp(Floor floor) => FindTile(floor, TileType.StairsUp);

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;
}
