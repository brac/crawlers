using System.Text.Json;
using System.Text.Json.Serialization;
using Crawlers.Domain.Enums;
using Crawlers.Generation.Scaling;

namespace Crawlers.Server.Config;

/// <summary>
/// Loads <see cref="FloorScalingTable"/> from the on-disk JSON config.
/// Run once at startup in <c>Program.cs</c> and registered as a singleton.
/// "Edit and restart" is the intended tuning loop — no live reload.
/// </summary>
internal static class FloorScalingLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static FloorScalingTable LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Floor-scaling config not found at '{path}'. Expected JSON file shipped with the Crawlers.Server build output.",
                path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<FloorScalingFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Floor-scaling config at '{path}' parsed as null.");

        if (doc.Floors is null || doc.Floors.Count == 0)
            throw new InvalidOperationException(
                $"Floor-scaling config at '{path}' has no 'floors' entries.");

        var entries = doc.Floors.Select(f => new FloorScaling(
            FloorNumber: f.Floor,
            HpMultiplier: f.HpMultiplier,
            DamageMultiplier: f.DamageMultiplier,
            AcBonus: f.AcBonus,
            EnemyCount: f.EnemyCount,
            MonsterDensity: f.MonsterDensity,
            LootQualityMultiplier: f.LootQualityMultiplier,
            GoldMultiplier: f.GoldMultiplier,
            Tint: f.Tint,
            MonsterPool: ParsePool(f.MonsterPool, f.Floor, path),
            StairwellBoss: ParseStairwellBoss(f.StairwellBoss, f.Floor, path),
            ChestCount: f.ChestCount ?? 0,
            ChestKindWeights: ParseChestKinds(f.ChestKinds, f.Floor, path),
            WeaponLoot: ParseWeaponLoot(f.WeaponLoot, f.Floor, path),
            ConsumableLoot: ParseConsumableLoot(f.ConsumableLoot, f.Floor, path)));

        return new FloorScalingTable(entries);
    }

    /// <summary>
    /// Parse the optional per-floor consumable loot pool. Names are
    /// kept as strings here — ChestService resolves them through
    /// ItemTemplates at chest-open time. Empty / missing → null
    /// (the chest's non-gold branch falls through to the weapon path).
    /// </summary>
    private static IReadOnlyList<ConsumableLootEntry>? ParseConsumableLoot(
        List<ConsumableLootEntryDto>? entries, int floor, string path)
    {
        if (entries is null || entries.Count == 0) return null;
        var list = new List<ConsumableLootEntry>(entries.Count);
        foreach (var dto in entries)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException(
                    $"Empty consumable name in consumableLoot for floor {floor} ({path}).");
            if (dto.Weight <= 0)
                throw new InvalidOperationException(
                    $"Non-positive weight {dto.Weight} for consumable '{dto.Name}' on floor {floor} ({path}).");
            list.Add(new ConsumableLootEntry(dto.Name, dto.Weight));
        }
        return list;
    }

    /// <summary>
    /// Parse the optional per-floor weapon loot pool. Names are kept as
    /// strings here — they're resolved against <see cref="WeaponRegistry"/>
    /// later, at chest-open time. Empty / missing → null (chests fall
    /// back to the legacy Healing Draught placeholder).
    /// </summary>
    private static IReadOnlyList<WeaponLootEntry>? ParseWeaponLoot(
        List<WeaponLootEntryDto>? entries, int floor, string path)
    {
        if (entries is null || entries.Count == 0) return null;
        var list = new List<WeaponLootEntry>(entries.Count);
        foreach (var dto in entries)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidOperationException(
                    $"Empty weapon name in weaponLoot for floor {floor} ({path}).");
            if (dto.Weight <= 0)
                throw new InvalidOperationException(
                    $"Non-positive weight {dto.Weight} for weapon '{dto.Name}' on floor {floor} ({path}).");
            list.Add(new WeaponLootEntry(dto.Name, dto.Weight));
        }
        return list;
    }

    /// <summary>
    /// Parse the optional chest-kind distribution. Same defensive pattern
    /// as the monster pool: typo'd kind names throw, non-positive weights
    /// throw, missing field → null (caller falls back to Standard-only).
    /// </summary>
    private static IReadOnlyList<ChestKindWeight>? ParseChestKinds(
        List<ChestKindEntryDto>? entries, int floor, string path)
    {
        if (entries is null || entries.Count == 0) return null;
        var result = new List<ChestKindWeight>(entries.Count);
        foreach (var dto in entries)
        {
            if (!Enum.TryParse<ChestKind>(dto.Kind, ignoreCase: false, out var kind))
                throw new InvalidOperationException(
                    $"Unknown ChestKind '{dto.Kind}' in chestKinds for floor {floor} ({path}).");
            if (dto.Weight <= 0)
                throw new InvalidOperationException(
                    $"Non-positive weight {dto.Weight} for ChestKind '{dto.Kind}' on floor {floor} ({path}).");
            result.Add(new ChestKindWeight(kind, dto.Weight));
        }
        return result;
    }

    /// <summary>
    /// Parse the optional stairwell-boss archetype name. Null/missing
    /// returns null (no boss); a populated string is enum-resolved with the
    /// same diagnostic as the pool. Floors without a stairwell boss spawn
    /// only from the pool and leave the boss room open.
    /// </summary>
    private static EnemyArchetype? ParseStairwellBoss(string? raw, int floor, string path)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Enum.TryParse<EnemyArchetype>(raw, ignoreCase: false, out var archetype))
            throw new InvalidOperationException(
                $"Unknown enemy archetype '{raw}' in stairwellBoss for floor {floor} ({path}).");
        return archetype;
    }

    /// <summary>
    /// Parse the per-floor monster pool. Each entry's archetype name is
    /// resolved via <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// against <see cref="EnemyArchetype"/>; mistyped names throw a
    /// descriptive error rather than silently dropping the entry. An
    /// absent or empty pool falls back to the same default Identity uses,
    /// so a freshly-bumped JSON without pools still spawns enemies.
    /// </summary>
    private static IReadOnlyList<MonsterPoolEntry> ParsePool(
        List<MonsterPoolEntryDto>? pool, int floor, string path)
    {
        if (pool is null || pool.Count == 0)
        {
            return new[]
            {
                new MonsterPoolEntry(EnemyArchetype.Husk, 1),
                new MonsterPoolEntry(EnemyArchetype.Rasper, 1),
                new MonsterPoolEntry(EnemyArchetype.Hulk, 1)
            };
        }

        var entries = new List<MonsterPoolEntry>(pool.Count);
        foreach (var dto in pool)
        {
            if (!Enum.TryParse<EnemyArchetype>(dto.Archetype, ignoreCase: false, out var archetype))
                throw new InvalidOperationException(
                    $"Unknown enemy archetype '{dto.Archetype}' in monsterPool for floor {floor} ({path}).");
            if (dto.Weight <= 0)
                throw new InvalidOperationException(
                    $"Non-positive weight {dto.Weight} for archetype '{dto.Archetype}' on floor {floor} ({path}).");
            entries.Add(new MonsterPoolEntry(archetype, dto.Weight));
        }
        return entries;
    }

    private sealed record FloorScalingFile(
        [property: JsonPropertyName("floors")] List<FloorScalingEntry>? Floors);

    private sealed record FloorScalingEntry(
        [property: JsonPropertyName("floor")] int Floor,
        [property: JsonPropertyName("hpMultiplier")] double HpMultiplier,
        [property: JsonPropertyName("damageMultiplier")] double DamageMultiplier,
        [property: JsonPropertyName("acBonus")] int AcBonus,
        [property: JsonPropertyName("enemyCount")] int EnemyCount,
        [property: JsonPropertyName("monsterDensity")] double MonsterDensity,
        [property: JsonPropertyName("lootQualityMultiplier")] double LootQualityMultiplier,
        [property: JsonPropertyName("goldMultiplier")] double GoldMultiplier,
        [property: JsonPropertyName("tint")] string Tint,
        [property: JsonPropertyName("monsterPool")] List<MonsterPoolEntryDto>? MonsterPool,
        [property: JsonPropertyName("stairwellBoss")] string? StairwellBoss,
        [property: JsonPropertyName("chestCount")] int? ChestCount,
        [property: JsonPropertyName("chestKinds")] List<ChestKindEntryDto>? ChestKinds,
        [property: JsonPropertyName("weaponLoot")] List<WeaponLootEntryDto>? WeaponLoot,
        [property: JsonPropertyName("consumableLoot")] List<ConsumableLootEntryDto>? ConsumableLoot);

    private sealed record MonsterPoolEntryDto(
        [property: JsonPropertyName("archetype")] string Archetype,
        [property: JsonPropertyName("weight")] int Weight);

    private sealed record ChestKindEntryDto(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("weight")] int Weight);

    private sealed record WeaponLootEntryDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("weight")] int Weight);

    private sealed record ConsumableLootEntryDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("weight")] int Weight);
}
