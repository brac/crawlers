namespace Crawlers.Generation.Scaling;

/// <summary>
/// One entry in a per-floor consumable loot pool. <see cref="Name"/> is
/// the canonical item name resolved by <c>ChestService</c> through
/// <c>ItemTemplates</c>; <see cref="Weight"/> drives weighted random
/// selection (an entry with weight 3 is three times as likely to roll
/// as an entry with weight 1).
/// </summary>
public sealed record ConsumableLootEntry(string Name, int Weight);
