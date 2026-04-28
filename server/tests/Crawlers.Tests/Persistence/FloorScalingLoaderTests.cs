using Crawlers.Domain.Enums;
using Crawlers.Server.Config;
using Xunit;

namespace Crawlers.Tests.Persistence;

/// <summary>
/// Locks the contract that <see cref="FloorScalingLoader"/> parses the
/// shipped <c>Config/floor-scaling.json</c> shape into a usable
/// <c>FloorScalingTable</c>. The test writes a synthetic JSON file to a
/// temp path so it stays deterministic even if the in-repo config gets
/// retuned later in this phase.
/// </summary>
public class FloorScalingLoaderTests
{
    [Fact]
    public void Loads_well_formed_json_into_a_lookup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"floor-scaling-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "floors": [
            { "floor": 1, "hpMultiplier": 1.0, "damageMultiplier": 1.0, "acBonus": 0, "enemyCount": 7,  "monsterDensity": 0.6, "lootQualityMultiplier": 1.0, "goldMultiplier": 1.0, "tint": "#ffffff" },
            { "floor": 2, "hpMultiplier": 1.2, "damageMultiplier": 1.1, "acBonus": 0, "enemyCount": 10, "monsterDensity": 0.7, "lootQualityMultiplier": 1.1, "goldMultiplier": 1.2, "tint": "#cfe5cf" }
          ]
        }
        """);
        try
        {
            var table = FloorScalingLoader.LoadFromFile(path);
            Assert.Equal(2, table.Entries.Count);
            Assert.Equal(1.0, table.For(1).HpMultiplier);
            Assert.Equal(7, table.For(1).EnemyCount);
            Assert.Equal("#cfe5cf", table.For(2).Tint);
            // Cycle behavior (Step 10/cycle.A): floor 3 wraps to base
            // floor 1 at cycle 1 → HpMultiplier = 1.0 × 1.5^1 = 1.5.
            Assert.Equal(1.5, table.For(3).HpMultiplier, precision: 4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"definitely-not-here-{Guid.NewGuid():N}.json");
        Assert.Throws<FileNotFoundException>(() => FloorScalingLoader.LoadFromFile(path));
    }

    [Fact]
    public void Empty_floors_list_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "floors": [] }""");
        try
        {
            Assert.Throws<InvalidOperationException>(() => FloorScalingLoader.LoadFromFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Shipped_config_loads_and_covers_the_4_floor_design()
    {
        // Locate the in-repo config relative to the test bin path. The build
        // copies it into Crawlers.Server's output, but the source-of-truth
        // we want to lock here is the file in the repo, not the copy.
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Crawlers.Server", "Config", "floor-scaling.json"));

        Assert.True(File.Exists(sourcePath),
            $"Expected shipped floor-scaling.json at {sourcePath}");

        var table = FloorScalingLoader.LoadFromFile(sourcePath);

        // The 4-floor design (CONTENT_AND_DEPTH.md) requires entries 1..4.
        for (int floor = 1; floor <= 4; floor++)
        {
            var entry = table.For(floor);
            Assert.Equal(floor, entry.FloorNumber);
            Assert.True(entry.HpMultiplier >= 1.0, $"Floor {floor} HP multiplier should be ≥1.0");
            Assert.InRange(entry.MonsterDensity, 0.0, 1.0);
            Assert.False(string.IsNullOrWhiteSpace(entry.Tint));
        }

        // Floor 4 must be meaningfully tougher than floor 1 — locks the
        // intent that the curve actually escalates.
        Assert.True(table.For(4).HpMultiplier > table.For(1).HpMultiplier);
        Assert.True(table.For(4).DamageMultiplier > table.For(1).DamageMultiplier);
        Assert.True(table.For(4).EnemyCount > table.For(1).EnemyCount);

        // Step 2 — every floor declares a non-empty monster pool. Floor 4
        // must include at least one large 32×36 archetype to satisfy the
        // design ("large enemies start rolling into regular rooms").
        Assert.NotEmpty(table.For(2).MonsterPool);
        var floor4Pool = table.For(4).MonsterPool;
        Assert.Contains(floor4Pool, e =>
            e.Archetype is EnemyArchetype.Ogre or EnemyArchetype.BigZombie or EnemyArchetype.BigDemon);
    }

    [Fact]
    public void Pool_with_unknown_archetype_throws_with_clear_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bad-archetype-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "floors": [
            {
              "floor": 1, "hpMultiplier": 1.0, "damageMultiplier": 1.0, "acBonus": 0,
              "enemyCount": 7, "monsterDensity": 1.0,
              "lootQualityMultiplier": 1.0, "goldMultiplier": 1.0, "tint": "#ffffff",
              "monsterPool": [ { "archetype": "Wyvern", "weight": 1 } ]
            }
          ]
        }
        """);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FloorScalingLoader.LoadFromFile(path));
            Assert.Contains("Wyvern", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pool_with_zero_weight_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zero-weight-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "floors": [
            {
              "floor": 1, "hpMultiplier": 1.0, "damageMultiplier": 1.0, "acBonus": 0,
              "enemyCount": 7, "monsterDensity": 1.0,
              "lootQualityMultiplier": 1.0, "goldMultiplier": 1.0, "tint": "#ffffff",
              "monsterPool": [ { "archetype": "Husk", "weight": 0 } ]
            }
          ]
        }
        """);
        try
        {
            Assert.Throws<InvalidOperationException>(() => FloorScalingLoader.LoadFromFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_pool_field_falls_back_to_legacy_default()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-pool-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "floors": [
            {
              "floor": 1, "hpMultiplier": 1.0, "damageMultiplier": 1.0, "acBonus": 0,
              "enemyCount": 7, "monsterDensity": 1.0,
              "lootQualityMultiplier": 1.0, "goldMultiplier": 1.0, "tint": "#ffffff"
            }
          ]
        }
        """);
        try
        {
            var table = FloorScalingLoader.LoadFromFile(path);
            var pool = table.For(1).MonsterPool;
            Assert.NotEmpty(pool);
            // Default fallback covers the legacy 3-archetype mix so a JSON
            // pre-Step-2 doesn't break the spawner.
            Assert.Contains(pool, e => e.Archetype == EnemyArchetype.Husk);
            Assert.Contains(pool, e => e.Archetype == EnemyArchetype.Rasper);
            Assert.Contains(pool, e => e.Archetype == EnemyArchetype.Hulk);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
