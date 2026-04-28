using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

/// <summary>
/// Step 5 — a stacking-by-duration status effect on a combatant.
/// Stacking rule per spec: applying the same kind refreshes
/// <see cref="RoundsRemaining"/> to the longer of (current, new); damage
/// per tick is NOT stacked. Different kinds coexist independently
/// (Bleed + Poison can both be active at once).
/// </summary>
public sealed record StatusEffect(StatusEffectKind Kind, int RoundsRemaining, int DamagePerTick);
