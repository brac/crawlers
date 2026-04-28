using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Generation.Scaling;
using Xunit;

namespace Crawlers.Tests.Generation;

/// <summary>
/// Locks the contract that <see cref="EnemyScaler.Apply"/> rebalances HP,
/// AC, and damage against the per-floor curve without touching dice shape
/// or ability mods. These assertions back the data-driven design rule
/// that "edit the JSON, restart the server" is the only retune surface.
/// </summary>
public class EnemyScalerTests
{
    private static FloorScaling Curve(
        double hp = 1.0, double damage = 1.0, int ac = 0, double density = 1.0) =>
        new(
            FloorNumber: 1,
            HpMultiplier: hp,
            DamageMultiplier: damage,
            AcBonus: ac,
            EnemyCount: 1,
            MonsterDensity: density,
            LootQualityMultiplier: 1.0,
            GoldMultiplier: 1.0,
            Tint: "#ffffff",
            MonsterPool: new[] { new MonsterPoolEntry(EnemyArchetype.Husk, 1) });

    [Fact]
    public void Identity_curve_is_a_no_op()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(0, 0), Guid.NewGuid());
        var (origHp, origAc, origDmg) = (husk.Stats!.MaxHp, husk.Stats.Ac, husk.Stats.Damage);

        EnemyScaler.Apply(husk, Curve());

        Assert.Equal(origHp, husk.Stats!.MaxHp);
        Assert.Equal(origAc, husk.Stats.Ac);
        Assert.Equal(origDmg, husk.Stats.Damage);
    }

    [Fact]
    public void Hp_multiplier_scales_max_hp_and_resets_current_hp()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(0, 0), Guid.NewGuid());
        var baseHp = husk.Stats!.MaxHp;

        EnemyScaler.Apply(husk, Curve(hp: 1.5));

        Assert.Equal((int)Math.Round(baseHp * 1.5), husk.Stats!.MaxHp);
        Assert.Equal(husk.Stats.MaxHp, husk.Stats.Hp);
    }

    [Fact]
    public void Hp_floor_is_one_even_for_aggressive_scaling_down()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(0, 0), Guid.NewGuid());
        EnemyScaler.Apply(husk, Curve(hp: 0.0));
        Assert.Equal(1, husk.Stats!.MaxHp);
    }

    [Fact]
    public void Ac_bonus_is_additive()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(0, 0), Guid.NewGuid());
        var baseAc = husk.Stats!.Ac;

        EnemyScaler.Apply(husk, Curve(ac: 2));

        Assert.Equal(baseAc + 2, husk.Stats!.Ac);
    }

    [Fact]
    public void Damage_multiplier_preserves_dice_shape_and_rebalances_modifier()
    {
        // Hulk: 1d8+1 → expected 5.5 base. At ×1.4, target 7.7 → modifier ≈ 3.
        var hulk = EnemyTemplates.Create(EnemyArchetype.Hulk, new Position(0, 0), Guid.NewGuid());
        var baseDice = hulk.Stats!.Damage;

        EnemyScaler.Apply(hulk, Curve(damage: 1.4));

        Assert.Equal(baseDice.Count, hulk.Stats!.Damage.Count);
        Assert.Equal(baseDice.Sides, hulk.Stats.Damage.Sides);
        // Expected new modifier: round((1d8 expected of 4.5 + 1) * 1.4 - 4.5) = round(7.7 - 4.5) = 3.
        Assert.Equal(3, hulk.Stats.Damage.Modifier);
    }

    [Fact]
    public void Stats_with_no_damage_dice_still_scale_safely()
    {
        // Synthetic case: 0d0+0 should pass through cleanly without divide-by-zero or NaN.
        var entity = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = Guid.NewGuid(),
            Type = EntityType.Enemy,
            Stats = new EntityStats
            {
                Hp = 5, MaxHp = 5, Ac = 10,
                AttackMod = 0, Damage = new DiceRoll(0, 0, 0),
                InitiativeMod = 0, Speed = 10, SightRadius = 3,
                StrMod = 0, DexMod = 0, ConMod = 0
            }
        };
        EnemyScaler.Apply(entity, Curve(damage: 2.0));
        Assert.Equal(0, entity.Stats!.Damage.Modifier);
    }

    [Fact]
    public void Other_stat_fields_are_untouched()
    {
        var rasper = EnemyTemplates.Create(EnemyArchetype.Rasper, new Position(0, 0), Guid.NewGuid());
        var (init, speed, sight, str, dex, con, atk) = (
            rasper.Stats!.InitiativeMod, rasper.Stats.Speed, rasper.Stats.SightRadius,
            rasper.Stats.StrMod, rasper.Stats.DexMod, rasper.Stats.ConMod, rasper.Stats.AttackMod);

        EnemyScaler.Apply(rasper, Curve(hp: 1.5, damage: 1.4, ac: 2));

        Assert.Equal(init, rasper.Stats!.InitiativeMod);
        Assert.Equal(speed, rasper.Stats.Speed);
        Assert.Equal(sight, rasper.Stats.SightRadius);
        Assert.Equal(str, rasper.Stats.StrMod);
        Assert.Equal(dex, rasper.Stats.DexMod);
        Assert.Equal(con, rasper.Stats.ConMod);
        Assert.Equal(atk, rasper.Stats.AttackMod);
    }
}
