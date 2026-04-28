using Crawlers.Domain.Enums;

namespace Crawlers.Generation.Scaling;

/// <summary>
/// Per-floor difficulty knobs. Loaded from <c>Config/floor-scaling.json</c>
/// at server startup; applied at floor generation and enemy spawn time.
/// One row per floor number; deeper floors past the table clamp to the
/// last entry.
/// </summary>
public sealed record FloorScaling(
    int FloorNumber,
    double HpMultiplier,
    double DamageMultiplier,
    int AcBonus,
    int EnemyCount,
    double MonsterDensity,
    double LootQualityMultiplier,
    double GoldMultiplier,
    string Tint,
    IReadOnlyList<MonsterPoolEntry> MonsterPool,
    EnemyArchetype? StairwellBoss = null,
    int ChestCount = 0,
    IReadOnlyList<ChestKindWeight>? ChestKindWeights = null,
    IReadOnlyList<WeaponLootEntry>? WeaponLoot = null,
    IReadOnlyList<ConsumableLootEntry>? ConsumableLoot = null)
{
    /// <summary>
    /// Resolved chest-kind weights. Falls back to "Standard only" when no
    /// JSON entry was provided so callers don't need null-checks at the
    /// placement site.
    /// </summary>
    public IReadOnlyList<ChestKindWeight> EffectiveChestKindWeights =>
        ChestKindWeights ?? new[] { new ChestKindWeight(ChestKind.Standard, 1) };

    /// <summary>
    /// Identity scaling — base stats unchanged, density 1.0 (no rooms
    /// excluded), and the legacy 3-archetype default pool
    /// (Husk / Rasper / Hulk equal-weighted) so tests that bypass JSON
    /// loading still see the pre-Step-2 spawn behavior. No stairwell boss
    /// (placer's floor-1 tutorial fallback fills that in for legacy
    /// count-based callers) and no chests — placement-only tests
    /// shouldn't have to assert chest counts.
    /// </summary>
    public static FloorScaling Identity(int floorNumber, int enemyCount) => new(
        FloorNumber: floorNumber,
        HpMultiplier: 1.0,
        DamageMultiplier: 1.0,
        AcBonus: 0,
        EnemyCount: enemyCount,
        MonsterDensity: 1.0,
        LootQualityMultiplier: 1.0,
        GoldMultiplier: 1.0,
        Tint: "#ffffff",
        MonsterPool: new[]
        {
            new MonsterPoolEntry(EnemyArchetype.Husk, 1),
            new MonsterPoolEntry(EnemyArchetype.Rasper, 1),
            new MonsterPoolEntry(EnemyArchetype.Hulk, 1)
        },
        StairwellBoss: null,
        ChestCount: 0,
        ChestKindWeights: null,
        WeaponLoot: null,
        ConsumableLoot: null);
}
