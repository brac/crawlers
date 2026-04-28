using Crawlers.Server.Config;
using Xunit;

namespace Crawlers.Tests.Persistence;

/// <summary>
/// Locks the contracts for <see cref="WeaponRegistryLoader"/>: well-
/// formed JSON resolves into a registry the chest service can use, and
/// the in-repo <c>Config/weapons.json</c> stays self-consistent (every
/// archetype name referenced by a floor's weaponLoot resolves cleanly).
/// </summary>
public class WeaponRegistryLoaderTests
{
    [Fact]
    public void Loads_well_formed_json_into_a_registry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"weapons-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "weapons": [
            { "name": "Knife",        "damage": { "count": 1, "sides": 4, "modifier": 0 }, "initiativeMod": 3 },
            { "name": "Knight Sword", "damage": { "count": 1, "sides": 8, "modifier": 1 }, "initiativeMod": 0 }
          ]
        }
        """);
        try
        {
            var reg = WeaponRegistryLoader.LoadFromFile(path);
            Assert.Equal(2, reg.All.Count);
            var knife = reg.Get("Knife");
            Assert.Equal(3, knife.InitiativeMod);
            Assert.Equal(4, knife.Damage.Sides);
            Assert.Equal(0, knife.Damage.Modifier);
            var ks = reg.Get("Knight Sword");
            Assert.Equal(8, ks.Damage.Sides);
            Assert.Equal(1, ks.Damage.Modifier);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Empty_weapons_list_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"weapons-empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "weapons": [] }""");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                WeaponRegistryLoader.LoadFromFile(path));
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
        Assert.Throws<FileNotFoundException>(() => WeaponRegistryLoader.LoadFromFile(path));
    }

    [Fact]
    public void Shipped_weapon_loot_pools_resolve_against_the_shipped_registry()
    {
        // Cross-check: every weapon name referenced by a floor's
        // weaponLoot in floor-scaling.json must resolve in weapons.json.
        // Catches typos before they hit a runtime "unknown weapon" log.
        var weaponsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Crawlers.Server", "Config", "weapons.json"));
        var scalingPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Crawlers.Server", "Config", "floor-scaling.json"));

        Assert.True(File.Exists(weaponsPath), $"weapons.json missing at {weaponsPath}");
        Assert.True(File.Exists(scalingPath), $"floor-scaling.json missing at {scalingPath}");

        var registry = WeaponRegistryLoader.LoadFromFile(weaponsPath);
        var scaling = FloorScalingLoader.LoadFromFile(scalingPath);

        foreach (var floor in scaling.Entries)
        {
            if (floor.WeaponLoot is null) continue;
            foreach (var entry in floor.WeaponLoot)
            {
                Assert.True(registry.TryGet(entry.Name, out _),
                    $"Floor {floor.FloorNumber}'s weaponLoot references unknown weapon '{entry.Name}'.");
            }
        }
    }
}
