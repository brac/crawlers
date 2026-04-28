namespace Crawlers.Generation.Scaling;

/// <summary>
/// Ordered table of <see cref="FloorScaling"/> entries keyed by floor
/// number. <see cref="For"/> wraps cyclically past the table's depth
/// (Step 10 / cycle.A — endless descent): floor 5 reuses floor 1's
/// pools and tints with cycle multipliers compounded onto HP, damage,
/// gold, etc. Cycle 0 returns the base entry unchanged.
/// </summary>
public sealed class FloorScalingTable
{
    private readonly IReadOnlyList<FloorScaling> _entries;

    public FloorScalingTable(IEnumerable<FloorScaling> entries)
    {
        _entries = entries.OrderBy(e => e.FloorNumber).ToList();
        if (_entries.Count == 0)
            throw new ArgumentException("FloorScalingTable requires at least one entry.", nameof(entries));
    }

    public IReadOnlyList<FloorScaling> Entries => _entries;

    /// <summary>
    /// A single-row identity table — multipliers all 1.0, density 1.0,
    /// no AC bonus. Used as the production DI fallback when no scaling
    /// JSON has been loaded (e.g. unit tests that build the session-stack
    /// directly without going through Program.cs).
    /// </summary>
    public static FloorScalingTable Identity(int enemyCount = 7)
        => new(new[] { FloorScaling.Identity(1, enemyCount) });

    /// <summary>
    /// Look up the scaling for a given floor number. Floors past the
    /// table's depth wrap cyclically — floor 5 = base floor 1 + cycle-1
    /// multipliers compounded; floor 6 = base floor 2 + cycle-1; …
    /// Cycle 0 (the original 1-N range) returns the base entry as-is.
    /// </summary>
    public FloorScaling For(int floorNumber)
    {
        if (floorNumber < 1) floorNumber = 1;

        var depth = _entries.Count;
        var cycle = (floorNumber - 1) / depth;             // 0 for 1-N, 1 for N+1-2N, ...
        var baseIdx = (floorNumber - 1) % depth;           // 0..N-1
        var baseScaling = _entries[baseIdx];

        if (cycle == 0)
            return baseScaling with { FloorNumber = floorNumber };

        // Cycle multipliers — compounded so each lap is meaningfully
        // tougher than the last. 1.5× HP/dmg/gold per cycle, +30% enemy
        // count, −1 chest count (min 1) per cycle, +50% loot quality.
        // Tunable here without touching JSON.
        var statMult = Math.Pow(1.5, cycle);
        var enemyMult = 1.0 + (cycle * 0.3);
        var lootMult = 1.0 + (cycle * 0.5);

        return baseScaling with
        {
            FloorNumber = floorNumber,
            HpMultiplier = baseScaling.HpMultiplier * statMult,
            DamageMultiplier = baseScaling.DamageMultiplier * statMult,
            EnemyCount = (int)Math.Round(baseScaling.EnemyCount * enemyMult),
            ChestCount = Math.Max(1, baseScaling.ChestCount - cycle),
            GoldMultiplier = baseScaling.GoldMultiplier * statMult,
            LootQualityMultiplier = baseScaling.LootQualityMultiplier * lootMult
        };
    }
}
