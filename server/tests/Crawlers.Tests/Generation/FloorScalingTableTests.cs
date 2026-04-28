using Crawlers.Generation.Scaling;
using Xunit;

namespace Crawlers.Tests.Generation;

/// <summary>
/// Lookup contract for <see cref="FloorScalingTable"/>: cycle 0 returns
/// the base entry as-is, cycles past the table's depth wrap and apply
/// compounding multipliers (Step 10/cycle.A — endless descent).
/// </summary>
public class FloorScalingTableTests
{
    private static FloorScaling Entry(int floor, double hp = 1.0, int chestCount = 3, int enemyCount = 7) => new(
        FloorNumber: floor,
        HpMultiplier: hp,
        DamageMultiplier: 1.0,
        AcBonus: 0,
        EnemyCount: enemyCount,
        MonsterDensity: 1.0,
        LootQualityMultiplier: 1.0,
        GoldMultiplier: 1.0,
        Tint: "#ffffff",
        MonsterPool: new[] { new MonsterPoolEntry(Crawlers.Domain.Enums.EnemyArchetype.Husk, 1) },
        ChestCount: chestCount);

    [Fact]
    public void Empty_table_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FloorScalingTable(Array.Empty<FloorScaling>()));
    }

    [Fact]
    public void Cycle0_returns_base_entry_with_floor_number_set()
    {
        var table = new FloorScalingTable(new[] { Entry(1, 1.0), Entry(2, 1.5), Entry(3, 2.0) });
        var got = table.For(2);
        Assert.Equal(2, got.FloorNumber);
        Assert.Equal(1.5, got.HpMultiplier);
    }

    [Fact]
    public void Floor_below_one_is_clamped_to_floor_one()
    {
        var table = new FloorScalingTable(new[] { Entry(1, 1.0), Entry(2, 1.5) });
        var got = table.For(0);
        Assert.Equal(1.0, got.HpMultiplier);
    }

    [Fact]
    public void Cycle1_wraps_to_base_entry_with_compounded_multipliers()
    {
        // 4-entry dense table — the production shape.
        var table = new FloorScalingTable(new[]
        {
            Entry(1, hp: 1.0, chestCount: 3, enemyCount: 7),
            Entry(2, hp: 1.2),
            Entry(3, hp: 1.4),
            Entry(4, hp: 1.6)
        });

        var floor5 = table.For(5);

        // Floor 5 → cycle 1, base index 0 (floor 1).
        Assert.Equal(5, floor5.FloorNumber);
        // HpMultiplier = base × 1.5 (cycle multiplier).
        Assert.Equal(1.0 * 1.5, floor5.HpMultiplier, precision: 4);
        // EnemyCount = base × 1.3 (rounded).
        Assert.Equal((int)Math.Round(7 * 1.3), floor5.EnemyCount);
        // ChestCount = max(1, base − cycle) = max(1, 3 − 1) = 2.
        Assert.Equal(2, floor5.ChestCount);
    }

    [Fact]
    public void Cycle2_compounds_further_than_cycle1()
    {
        var table = new FloorScalingTable(new[]
        {
            Entry(1, hp: 1.0), Entry(2, hp: 1.0), Entry(3, hp: 1.0), Entry(4, hp: 1.0)
        });

        var floor5 = table.For(5);   // cycle 1
        var floor9 = table.For(9);   // cycle 2

        // 1.5^2 / 1.5^1 = 1.5×; floor 9 should have ~1.5× the HpMult of floor 5.
        Assert.True(floor9.HpMultiplier > floor5.HpMultiplier);
        Assert.Equal(1.0 * Math.Pow(1.5, 2), floor9.HpMultiplier, precision: 4);
    }

    [Fact]
    public void Chest_count_floors_at_one_no_matter_how_deep()
    {
        var table = new FloorScalingTable(new[] { Entry(1, chestCount: 2) });
        // cycle 50 → would compute 2 − 50 = −48 → clamped to 1.
        var deep = table.For(51);
        Assert.Equal(1, deep.ChestCount);
    }

    [Fact]
    public void Entries_are_sorted_regardless_of_input_order()
    {
        var table = new FloorScalingTable(new[] { Entry(3), Entry(1), Entry(2) });
        Assert.Equal(new[] { 1, 2, 3 }, table.Entries.Select(e => e.FloorNumber));
    }

    [Fact]
    public void Identity_factory_produces_a_single_no_op_entry()
    {
        var table = FloorScalingTable.Identity(enemyCount: 5);
        Assert.Single(table.Entries);
        var entry = table.For(1);
        Assert.Equal(1.0, entry.HpMultiplier);
        Assert.Equal(1.0, entry.DamageMultiplier);
        Assert.Equal(0, entry.AcBonus);
        Assert.Equal(5, entry.EnemyCount);
        Assert.Equal("#ffffff", entry.Tint);
    }
}
