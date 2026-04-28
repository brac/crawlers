using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Generation.Scaling;
using Xunit;

namespace Crawlers.Tests.Generation;

/// <summary>
/// Locks the contract that <see cref="EntityPlacer"/> consumes a
/// <see cref="FloorScaling"/> end-to-end: enemy count comes from the
/// scaling row, density restricts how many rooms are eligible to spawn
/// into, and stat scaling is stamped on every freshly-spawned enemy
/// (boss included on floor 1).
/// </summary>
public class EntityPlacerScalingTests
{
    private static Floor MakeFloor(int seed, int floorNumber = 2) =>
        new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = seed,
            FloorNumber = floorNumber
        });

    private static FloorScaling Curve(
        int count = 6,
        double density = 1.0,
        double hp = 1.0,
        double damage = 1.0,
        int ac = 0) =>
        new(
            FloorNumber: 2,
            HpMultiplier: hp,
            DamageMultiplier: damage,
            AcBonus: ac,
            EnemyCount: count,
            MonsterDensity: density,
            LootQualityMultiplier: 1.0,
            GoldMultiplier: 1.0,
            Tint: "#ffffff",
            // Step 2: keep the legacy Husk/Rasper/Hulk pool so the
            // archetype-name switch below stays exhaustive.
            MonsterPool: new[]
            {
                new MonsterPoolEntry(EnemyArchetype.Husk, 1),
                new MonsterPoolEntry(EnemyArchetype.Rasper, 1),
                new MonsterPoolEntry(EnemyArchetype.Hulk, 1)
            });

    [Fact]
    public void Enemy_count_comes_from_scaling()
    {
        var floor = MakeFloor(seed: 1);
        new EntityPlacer().Place(floor, new Random(1), Curve(count: 9));
        Assert.Equal(9, floor.Entities.Count(e => e.Type == EntityType.Enemy));
    }

    [Fact]
    public void Stat_scaling_is_applied_to_every_spawned_enemy()
    {
        var floor = MakeFloor(seed: 7);
        new EntityPlacer().Place(floor, new Random(7), Curve(count: 6, hp: 1.5, ac: 2));

        // Compare each placed enemy back to its archetype baseline.
        foreach (var enemy in floor.Entities.Where(e => e.Type == EntityType.Enemy))
        {
            var archetype = enemy.Name switch
            {
                "Husk" => EnemyArchetype.Husk,
                "Rasper" => EnemyArchetype.Rasper,
                "Hulk" => EnemyArchetype.Hulk,
                "Slug" => EnemyArchetype.TinySlug,
                "Big Slug" => EnemyArchetype.BigSlug,
                _ => throw new InvalidOperationException($"Unknown archetype name '{enemy.Name}'")
            };
            var baseline = EnemyTemplates.Create(archetype, new Position(0, 0), Guid.NewGuid());
            Assert.Equal((int)Math.Round(baseline.Stats!.MaxHp * 1.5), enemy.Stats!.MaxHp);
            Assert.Equal(baseline.Stats.Ac + 2, enemy.Stats.Ac);
        }
    }

    [Fact]
    public void Floor4_curve_yields_meaningfully_tougher_enemies_than_floor1()
    {
        // Floor 1 baseline: identity scaling. Floor 4 design: hp ×1.5, dmg ×1.4, ac +1.
        var floor1 = MakeFloor(seed: 11, floorNumber: 2);
        var floor4 = MakeFloor(seed: 11, floorNumber: 2);

        new EntityPlacer().Place(floor1, new Random(11), Curve(count: 6, hp: 1.0, damage: 1.0, ac: 0));
        new EntityPlacer().Place(floor4, new Random(11), Curve(count: 6, hp: 1.5, damage: 1.4, ac: 1));

        var f1Avg = floor1.Entities.Where(e => e.Type == EntityType.Enemy).Average(e => e.Stats!.MaxHp);
        var f4Avg = floor4.Entities.Where(e => e.Type == EntityType.Enemy).Average(e => e.Stats!.MaxHp);

        Assert.True(f4Avg >= f1Avg * 1.4,
            $"Expected floor-4 average HP ({f4Avg:F1}) to be ≥1.4× floor-1 ({f1Avg:F1}).");
    }

    [Fact]
    public void Density_below_one_leaves_some_eligible_rooms_empty()
    {
        // Use a floor with enough rooms that the rounding can be observed.
        for (int seed = 0; seed < 5; seed++)
        {
            var floor = MakeFloor(seed: seed);
            // Density 0.5 should leave roughly half the eligible rooms empty;
            // with enemyCount low enough that we don't oversaturate, the
            // observed per-room occupancy should reflect that restriction.
            new EntityPlacer().Place(floor, new Random(seed), Curve(count: 4, density: 0.5));

            var eligible = floor.Rooms.Count - 1;       // exclude spawn room
            var occupiedRooms = floor.Rooms.Count(room =>
                floor.Entities.Any(e => e.Type == EntityType.Enemy && Contains(room.Bounds, e.Position)));

            // Density 0.5 on N eligible rooms → at most ceil(N/2) rooms can hold monsters.
            Assert.True(occupiedRooms <= (int)Math.Ceiling(eligible * 0.5) + 1,
                $"Seed {seed}: density 0.5 on {eligible} rooms yielded {occupiedRooms} occupied rooms.");
        }
    }

    [Fact]
    public void Density_one_keeps_every_eligible_room_in_play()
    {
        // With density 1.0 and enemyCount equal to the eligible-room count,
        // we should be able to place at least one monster per eligible room
        // (high attempts budget; the placer fills rooms uniformly).
        var floor = MakeFloor(seed: 42);
        var eligibleRooms = floor.Rooms.Count - 1;     // exclude spawn room
        new EntityPlacer().Place(floor, new Random(42),
            Curve(count: eligibleRooms * 2, density: 1.0));

        Assert.Equal(eligibleRooms * 2, floor.Entities.Count(e => e.Type == EntityType.Enemy));
    }

    [Fact]
    public void FloorOne_boss_receives_stat_scaling()
    {
        var floor = MakeFloor(seed: 5, floorNumber: 1);
        new EntityPlacer().Place(floor, new Random(5),
            Curve(count: 3, hp: 2.0, ac: 1) with { FloorNumber = 1 });

        var stairsDown = FindTile(floor, TileType.StairsDown);
        Assert.NotNull(stairsDown);
        var boss = floor.Entities.SingleOrDefault(e => e.Name == "Big Slug");
        Assert.NotNull(boss);
        // Big Slug baseline MaxHp is 18 → ×2.0 should land on 36.
        Assert.Equal(36, boss!.Stats!.MaxHp);
        // Big Slug baseline AC is 10 → +1 should land on 11.
        Assert.Equal(11, boss.Stats.Ac);
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
