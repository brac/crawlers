using System.Text.Json;
using System.Text.Json.Serialization;
using Crawlers.Domain.Models;
using Crawlers.Generation.Weapons;

namespace Crawlers.Server.Config;

/// <summary>
/// Loads <see cref="WeaponRegistry"/> from <c>Config/weapons.json</c>.
/// Mirrors <see cref="FloorScalingLoader"/>: comment-tolerant, trailing-
/// commas allowed, descriptive errors on bad data. Run once at startup;
/// edit + restart is the tuning loop.
/// </summary>
internal static class WeaponRegistryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static WeaponRegistry LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Weapons config not found at '{path}'. Expected JSON file shipped with the Crawlers.Server build output.",
                path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<WeaponsFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Weapons config at '{path}' parsed as null.");

        if (doc.Weapons is null || doc.Weapons.Count == 0)
            throw new InvalidOperationException(
                $"Weapons config at '{path}' has no 'weapons' entries.");

        var defs = doc.Weapons.Select(w =>
        {
            if (string.IsNullOrWhiteSpace(w.Name))
                throw new InvalidOperationException($"Weapon entry with missing name in '{path}'.");
            if (w.Damage is null)
                throw new InvalidOperationException($"Weapon '{w.Name}' has no damage block in '{path}'.");
            return new WeaponDefinition(
                Name: w.Name,
                Damage: new DiceRoll(w.Damage.Count, w.Damage.Sides, w.Damage.Modifier),
                InitiativeMod: w.InitiativeMod);
        });

        return new WeaponRegistry(defs);
    }

    private sealed record WeaponsFile(
        [property: JsonPropertyName("weapons")] List<WeaponEntry>? Weapons);

    private sealed record WeaponEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("damage")] DiceEntry? Damage,
        [property: JsonPropertyName("initiativeMod")] int InitiativeMod);

    private sealed record DiceEntry(
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("sides")] int Sides,
        [property: JsonPropertyName("modifier")] int Modifier);
}
