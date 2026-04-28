using Crawlers.Domain.Models;

namespace Crawlers.Generation.Scaling;

/// <summary>
/// Applies a <see cref="FloorScaling"/> stat curve to a freshly-spawned
/// enemy entity. Mutates in place because <see cref="Entity.Stats"/> is
/// settable and every call site is "just spawned, about to drop into the
/// floor's entity list."
/// </summary>
public static class EnemyScaler
{
    public static void Apply(Entity entity, FloorScaling scaling)
    {
        if (entity.Stats is null) return;
        var s = entity.Stats;

        var scaledMaxHp = Math.Max(1, (int)Math.Round(s.MaxHp * scaling.HpMultiplier));
        var scaledAc = s.Ac + scaling.AcBonus;

        // Damage = NdM+K. Keep the dice shape (count + sides) so combat
        // variance scales naturally; rebalance the flat modifier so the
        // post-scaling expected damage lands on (oldExpected * multiplier).
        // diceExpected = N * (M+1) / 2; oldExpected = diceExpected + K.
        var diceExpected = s.Damage.Count * (s.Damage.Sides + 1) / 2.0;
        var oldExpected = diceExpected + s.Damage.Modifier;
        var targetExpected = oldExpected * scaling.DamageMultiplier;
        var scaledModifier = (int)Math.Round(targetExpected - diceExpected);

        entity.Stats = s with
        {
            Hp = scaledMaxHp,
            MaxHp = scaledMaxHp,
            Ac = scaledAc,
            Damage = s.Damage with { Modifier = scaledModifier }
        };
    }
}
