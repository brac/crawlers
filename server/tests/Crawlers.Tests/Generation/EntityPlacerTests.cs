using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Xunit;

namespace Crawlers.Tests.Generation;

public class EntityPlacerTests
{
    private static Floor MakeFloor(int seed) =>
        new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = seed,
            FloorNumber = 1
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

    private static Position? FindStairsUp(Floor floor)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == TileType.StairsUp)
                    return new Position(x, y);
        return null;
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;
}
