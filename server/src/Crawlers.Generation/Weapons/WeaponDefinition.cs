using Crawlers.Domain.Models;

namespace Crawlers.Generation.Weapons;

/// <summary>
/// Per-archetype weapon stat block. Loaded from
/// <c>Config/weapons.json</c> at server startup. Step 3.4 introduces
/// the catalogue + loot drops; equip + combat impact ships in Step 3.5.
///
/// <see cref="Name"/> is also the manifest sprite key on the client
/// (<c>assets.json</c> &gt; <c>weapons</c>) — keeping one identifier
/// across the wire keeps lookup simple.
/// </summary>
public sealed record WeaponDefinition(
    string Name,
    DiceRoll Damage,
    int InitiativeMod);
