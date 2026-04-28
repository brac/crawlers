namespace Crawlers.Generation.Scaling;

/// <summary>
/// One entry in a per-floor weapon loot pool. <see cref="Name"/> is the
/// archetype name resolved through <see cref="Weapons.WeaponRegistry"/>;
/// <see cref="Weight"/> drives weighted random selection (an entry with
/// weight 3 is three times as likely to roll as an entry with weight 1).
/// </summary>
public sealed record WeaponLootEntry(string Name, int Weight);
