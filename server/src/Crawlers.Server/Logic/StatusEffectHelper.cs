using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

/// <summary>
/// Step 5 — utility for applying / refreshing / decrementing status
/// effects on combatants. The "stacking by duration" rule lives here
/// (apply same kind → refresh to max(current, new) rounds, damage
/// doesn't compound) so the spec doesn't get re-implemented at every
/// call site.
/// </summary>
public static class StatusEffectHelper
{
    /// <summary>
    /// Apply <paramref name="incoming"/> to <paramref name="effects"/>.
    /// If a same-kind effect is already active, refresh its
    /// RoundsRemaining to the larger of (current, new) and keep the
    /// larger DamagePerTick — never combine durations or damage.
    /// </summary>
    public static void Apply(List<StatusEffect> effects, StatusEffect incoming)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i].Kind != incoming.Kind) continue;
            effects[i] = new StatusEffect(
                Kind: incoming.Kind,
                RoundsRemaining: Math.Max(effects[i].RoundsRemaining, incoming.RoundsRemaining),
                DamagePerTick: Math.Max(effects[i].DamagePerTick, incoming.DamagePerTick));
            return;
        }
        effects.Add(incoming);
    }

    /// <summary>
    /// Decrement RoundsRemaining for every active effect by one and
    /// drop any that hit zero. Called once per turn, after both Bleed
    /// and Poison ticks have resolved.
    /// </summary>
    public static void Decrement(List<StatusEffect> effects)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var e = effects[i];
            var rem = e.RoundsRemaining - 1;
            if (rem <= 0) effects.RemoveAt(i);
            else effects[i] = e with { RoundsRemaining = rem };
        }
    }

    /// <summary>
    /// Sum of damage-per-tick for all active effects of <paramref name="kind"/>.
    /// Caller invokes once per timing slot (Bleed at start of turn,
    /// Poison at end) and applies the result through the regular
    /// damage path so death is handled the same as any other source.
    /// </summary>
    public static int TickDamage(IReadOnlyList<StatusEffect> effects, StatusEffectKind kind)
    {
        int total = 0;
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].Kind == kind) total += effects[i].DamagePerTick;
        return total;
    }

    public static void Clear(List<StatusEffect> effects) => effects.Clear();
}
