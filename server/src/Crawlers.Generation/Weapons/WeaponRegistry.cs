namespace Crawlers.Generation.Weapons;

/// <summary>
/// Name → <see cref="WeaponDefinition"/> lookup. Loaded once at startup
/// from <c>Config/weapons.json</c>; floor-scaling weapon-loot pools and
/// chest-drop logic resolve archetype names through this registry.
/// </summary>
public sealed class WeaponRegistry
{
    private readonly IReadOnlyDictionary<string, WeaponDefinition> _byName;

    public WeaponRegistry(IEnumerable<WeaponDefinition> definitions)
    {
        var map = new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
        foreach (var d in definitions)
        {
            if (map.ContainsKey(d.Name))
                throw new ArgumentException(
                    $"Duplicate weapon name '{d.Name}' in registry.", nameof(definitions));
            map[d.Name] = d;
        }
        if (map.Count == 0)
            throw new ArgumentException("WeaponRegistry requires at least one entry.", nameof(definitions));
        _byName = map;
    }

    public IReadOnlyCollection<WeaponDefinition> All => (IReadOnlyCollection<WeaponDefinition>)_byName.Values;

    public WeaponDefinition Get(string name) =>
        _byName.TryGetValue(name, out var def)
            ? def
            : throw new KeyNotFoundException($"Unknown weapon archetype '{name}'.");

    public bool TryGet(string name, out WeaponDefinition def) =>
        _byName.TryGetValue(name, out def!);
}
