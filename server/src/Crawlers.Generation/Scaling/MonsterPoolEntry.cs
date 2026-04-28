using Crawlers.Domain.Enums;

namespace Crawlers.Generation.Scaling;

/// <summary>
/// One entry in a per-floor monster pool. The placer draws from the pool
/// with weighted random selection: an entry with weight 3 is three times
/// as likely to roll as an entry with weight 1. Weights are integers to
/// keep tuning intuitive (no floats to balance against each other).
/// </summary>
public sealed record MonsterPoolEntry(EnemyArchetype Archetype, int Weight);
