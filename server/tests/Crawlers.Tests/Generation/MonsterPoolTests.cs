using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Generation.Scaling;
using Xunit;

namespace Crawlers.Tests.Generation;

/// <summary>
/// Locks the contracts that drive Step 2's variety pass:
///   - <see cref="EntityPlacer.PickArchetype"/> respects weights.
///   - PlaceEnemies on floors 2+ only spawns archetypes that appear in
///     the floor's monster pool — pool composition is the authoritative
///     source of "which monsters live on this floor."
/// </summary>
public class MonsterPoolTests
{
    private static FloorScaling Curve(
        IReadOnlyList<MonsterPoolEntry> pool,
        int count = 12,
        EnemyArchetype? stairwellBoss = null,
        int floorNumber = 2) =>
        new(
            FloorNumber: floorNumber,
            HpMultiplier: 1.0,
            DamageMultiplier: 1.0,
            AcBonus: 0,
            EnemyCount: count,
            MonsterDensity: 1.0,
            LootQualityMultiplier: 1.0,
            GoldMultiplier: 1.0,
            Tint: "#ffffff",
            MonsterPool: pool,
            StairwellBoss: stairwellBoss);

    private static Floor MakeFloor(int seed, int floorNumber = 2) =>
        new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = seed,
            FloorNumber = floorNumber
        });

    [Fact]
    public void PickArchetype_with_single_entry_always_returns_it()
    {
        var pool = new[] { new MonsterPoolEntry(EnemyArchetype.Skeleton, 5) };
        for (int i = 0; i < 50; i++)
            Assert.Equal(EnemyArchetype.Skeleton, EntityPlacer.PickArchetype(pool, new Random(i)));
    }

    [Fact]
    public void PickArchetype_respects_weights_in_aggregate()
    {
        // 9:1 ratio over many rolls should land in a wide band around 90/10.
        var pool = new[]
        {
            new MonsterPoolEntry(EnemyArchetype.Husk, 9),
            new MonsterPoolEntry(EnemyArchetype.BigDemon, 1)
        };
        var rng = new Random(0xC0FFEE);
        int huskCount = 0;
        const int trials = 5000;
        for (int i = 0; i < trials; i++)
        {
            if (EntityPlacer.PickArchetype(pool, rng) == EnemyArchetype.Husk) huskCount++;
        }
        Assert.InRange(huskCount, (int)(trials * 0.85), (int)(trials * 0.95));
    }

    [Fact]
    public void PickArchetype_throws_on_empty_pool()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EntityPlacer.PickArchetype(Array.Empty<MonsterPoolEntry>(), new Random(0)));
    }

    [Fact]
    public void PlaceEnemies_only_spawns_archetypes_in_pool()
    {
        // Pool only contains Skeleton + Goblin — no other archetype should appear.
        var pool = new[]
        {
            new MonsterPoolEntry(EnemyArchetype.Skeleton, 1),
            new MonsterPoolEntry(EnemyArchetype.Goblin, 1)
        };
        var allowed = new HashSet<string?> { "Skeleton", "Goblin" };

        for (int seed = 0; seed < 5; seed++)
        {
            var floor = MakeFloor(seed: seed);
            new EntityPlacer().Place(floor, new Random(seed), Curve(pool));

            foreach (var enemy in floor.Entities.Where(e => e.Type == EntityType.Enemy))
            {
                Assert.Contains(enemy.Name, allowed);
            }
        }
    }

    [Fact]
    public void Pool_with_large_archetype_can_spawn_it_in_regular_rooms()
    {
        // Floor 4's design: large 32×36 enemies (Ogre / BigZombie / BigDemon)
        // are eligible to roll into regular rooms. Verify the placer actually
        // emits them when they're in the pool.
        var pool = new[]
        {
            new MonsterPoolEntry(EnemyArchetype.Ogre, 1)
        };
        var floor = MakeFloor(seed: 99);
        new EntityPlacer().Place(floor, new Random(99), Curve(pool, count: 5));

        var ogres = floor.Entities.Count(e => e.Name == "Ogre");
        Assert.True(ogres > 0, "Ogre-only pool should produce at least one Ogre on the floor.");
    }

    [Theory]
    [InlineData(EnemyArchetype.BigZombie, "Big Zombie")]
    [InlineData(EnemyArchetype.BigDemon, "Big Demon")]
    [InlineData(EnemyArchetype.Ogre, "Ogre")]
    public void Stairwell_boss_lands_on_stairs_down_and_stamps_BossEntityId(
        EnemyArchetype boss, string expectedName)
    {
        var pool = new[] { new MonsterPoolEntry(EnemyArchetype.Husk, 1) };
        var floor = MakeFloor(seed: 17);
        new EntityPlacer().Place(floor, new Random(17),
            Curve(pool, count: 5, stairwellBoss: boss));

        var stairsDown = FindTile(floor, TileType.StairsDown);
        Assert.NotNull(stairsDown);

        var bossEntity = floor.Entities.SingleOrDefault(e =>
            e.Type == EntityType.Enemy && e.Position.Equals(stairsDown!.Value));
        Assert.NotNull(bossEntity);
        Assert.Equal(expectedName, bossEntity!.Name);
        Assert.Equal(bossEntity.Id, floor.BossEntityId);
    }

    [Fact]
    public void Stairwell_boss_room_excludes_pool_spawns()
    {
        // With a configured boss, no pool enemy should spawn inside the
        // boss room — the encounter must stay a clean fight against the
        // boss (and any teammates engaged through the same combat).
        var pool = new[] { new MonsterPoolEntry(EnemyArchetype.Goblin, 1) };
        var floor = MakeFloor(seed: 23);
        new EntityPlacer().Place(floor, new Random(23),
            Curve(pool, count: 8, stairwellBoss: EnemyArchetype.BigDemon));

        var bossRoom = floor.BossRoomBounds;
        if (bossRoom is null) return; // floor without a boss room — nothing to assert.

        // The only enemy inside the boss-room bounds should be the boss
        // itself; no Goblin should appear there.
        var insideBossRoom = floor.Entities
            .Where(e => e.Type == EntityType.Enemy && Contains(bossRoom.Value, e.Position))
            .ToList();
        Assert.Single(insideBossRoom);
        Assert.Equal("Big Demon", insideBossRoom[0].Name);
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
