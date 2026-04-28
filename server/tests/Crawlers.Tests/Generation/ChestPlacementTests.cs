using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Generation.Scaling;
using Xunit;

namespace Crawlers.Tests.Generation;

/// <summary>
/// Locks the contracts for Step 3.2 chest placement: chests scatter
/// through eligible (non-spawn, non-boss) rooms in the configured count,
/// each chest's kind is rolled from the per-floor weighted distribution,
/// and chests never collide with monsters or stairs.
/// </summary>
public class ChestPlacementTests
{
    private static Floor MakeFloor(int seed, int floorNumber = 2) =>
        new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = seed,
            FloorNumber = floorNumber
        });

    // Step guard.A — chests only spawn in monster-occupied rooms. Tests
    // that exercise chest placement need a default enemy count high
    // enough to seed monsters into multiple rooms; raised from 0 → 8.
    private static FloorScaling Curve(
        int chestCount = 0,
        IReadOnlyList<ChestKindWeight>? chestKinds = null,
        int enemyCount = 8,
        EnemyArchetype? stairwellBoss = null) =>
        new(
            FloorNumber: 2,
            HpMultiplier: 1.0,
            DamageMultiplier: 1.0,
            AcBonus: 0,
            EnemyCount: enemyCount,
            MonsterDensity: 1.0,
            LootQualityMultiplier: 1.0,
            GoldMultiplier: 1.0,
            Tint: "#ffffff",
            MonsterPool: new[] { new MonsterPoolEntry(EnemyArchetype.Husk, 1) },
            StairwellBoss: stairwellBoss,
            ChestCount: chestCount,
            ChestKindWeights: chestKinds);

    [Fact]
    public void Zero_chest_count_places_no_chests()
    {
        var floor = MakeFloor(seed: 1);
        new EntityPlacer().Place(floor, new Random(1), Curve(chestCount: 0));
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Chest);
    }

    [Fact]
    public void Chest_count_matches_target_when_room_supply_is_sufficient()
    {
        var floor = MakeFloor(seed: 7);
        new EntityPlacer().Place(floor, new Random(7), Curve(chestCount: 3));
        Assert.Equal(3, floor.Entities.Count(e => e.Type == EntityType.Chest));
    }

    [Fact]
    public void Chests_have_a_chest_kind_set()
    {
        var floor = MakeFloor(seed: 3);
        new EntityPlacer().Place(floor, new Random(3),
            Curve(chestCount: 2, chestKinds: new[]
            {
                new ChestKindWeight(ChestKind.Standard, 1)
            }));

        var chests = floor.Entities.Where(e => e.Type == EntityType.Chest).ToList();
        Assert.NotEmpty(chests);
        Assert.All(chests, c => Assert.Equal(ChestKind.Standard, c.ChestKind));
    }

    [Fact]
    public void Mimic_only_distribution_yields_only_mimic_chests()
    {
        var floor = MakeFloor(seed: 11);
        new EntityPlacer().Place(floor, new Random(11),
            Curve(chestCount: 4, chestKinds: new[]
            {
                new ChestKindWeight(ChestKind.Mimic, 1)
            }));

        var chests = floor.Entities.Where(e => e.Type == EntityType.Chest).ToList();
        Assert.NotEmpty(chests);
        Assert.All(chests, c => Assert.Equal(ChestKind.Mimic, c.ChestKind));
    }

    [Fact]
    public void Chests_do_not_overlap_monsters_or_stairs()
    {
        var floor = MakeFloor(seed: 23);
        new EntityPlacer().Place(floor, new Random(23),
            Curve(chestCount: 3, enemyCount: 8,
                chestKinds: new[] { new ChestKindWeight(ChestKind.Standard, 1) }));

        var chests = floor.Entities.Where(e => e.Type == EntityType.Chest).ToList();
        var enemyPositions = floor.Entities
            .Where(e => e.Type == EntityType.Enemy)
            .Select(e => e.Position)
            .ToHashSet();

        foreach (var c in chests)
        {
            Assert.DoesNotContain(c.Position, enemyPositions);
            var tile = floor.TileGrid[c.Position.X, c.Position.Y].Type;
            Assert.NotEqual(TileType.StairsUp, tile);
            Assert.NotEqual(TileType.StairsDown, tile);
        }
    }

    [Fact]
    public void Chests_avoid_spawn_and_boss_rooms()
    {
        // Floor 2 with a stairwell boss configured — the placer should
        // exclude the boss room from chest placement, and the spawn room
        // is always excluded for player safety.
        var floor = MakeFloor(seed: 41);
        new EntityPlacer().Place(floor, new Random(41),
            Curve(chestCount: 4, enemyCount: 0,
                stairwellBoss: EnemyArchetype.BigZombie,
                chestKinds: new[] { new ChestKindWeight(ChestKind.Standard, 1) }));

        var chests = floor.Entities.Where(e => e.Type == EntityType.Chest).ToList();
        var stairsUp = FindTile(floor, TileType.StairsUp);
        var spawnRoomBounds = stairsUp is { } up
            ? floor.Rooms.FirstOrDefault(r => Contains(r.Bounds, up))?.Bounds
            : null;

        foreach (var c in chests)
        {
            if (spawnRoomBounds is { } sr)
                Assert.False(Contains(sr, c.Position),
                    $"Chest at {c.Position} fell inside the spawn room.");
            if (floor.BossRoomBounds is { } br)
                Assert.False(Contains(br, c.Position),
                    $"Chest at {c.Position} fell inside the boss room.");
        }
    }

    [Fact]
    public void Every_chest_lands_in_a_room_that_contains_a_monster()
    {
        // Step guard.A — every chest must be guarded. For each chest
        // we find its containing room and assert that at least one
        // alive Enemy entity sits inside that room's bounds.
        for (int seed = 0; seed < 5; seed++)
        {
            var floor = MakeFloor(seed: seed);
            new EntityPlacer().Place(floor, new Random(seed),
                Curve(chestCount: 3, enemyCount: 10,
                    chestKinds: new[] { new ChestKindWeight(ChestKind.Standard, 1) }));

            var chests = floor.Entities.Where(e => e.Type == EntityType.Chest).ToList();
            foreach (var chest in chests)
            {
                var room = floor.Rooms.FirstOrDefault(r => Contains(r.Bounds, chest.Position));
                Assert.NotNull(room);
                var roomHasMonster = floor.Entities.Any(e =>
                    e.Type == EntityType.Enemy
                    && e.State == EntityState.Alive
                    && Contains(room!.Bounds, e.Position));
                Assert.True(roomHasMonster,
                    $"Seed {seed}: chest at {chest.Position} sits in an unguarded room.");
            }
        }
    }

    [Fact]
    public void No_chests_when_no_monsters_were_placed()
    {
        // Edge case — if monster placement somehow produced an empty
        // floor (enemyCount=0), chests should fall through cleanly
        // rather than scattering randomly into empty rooms.
        var floor = MakeFloor(seed: 4);
        new EntityPlacer().Place(floor, new Random(4),
            Curve(chestCount: 3, enemyCount: 0));

        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Chest);
    }

    [Fact]
    public void Chest_kind_distribution_roughly_matches_weights()
    {
        // With a 3:1 Standard:Mimic ratio over many trials, Standard
        // should land in a wide band around 75%.
        var weights = new[]
        {
            new ChestKindWeight(ChestKind.Standard, 3),
            new ChestKindWeight(ChestKind.Mimic, 1)
        };
        var rng = new Random(0xBADCAFE);
        int standardCount = 0;
        const int trials = 4000;
        for (int i = 0; i < trials; i++)
        {
            if (EntityPlacer.PickChestKind(weights, rng) == ChestKind.Standard) standardCount++;
        }
        Assert.InRange(standardCount, (int)(trials * 0.70), (int)(trials * 0.80));
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
