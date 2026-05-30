using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

/// <summary>
/// Per-archetype enemy factory. Stats are tuned so Husk is the baseline,
/// faster archetypes trade HP for initiative, larger archetypes trade
/// initiative for HP/damage. Per-floor difficulty multipliers
/// (<c>floor-scaling.json</c>) are layered on top at spawn time.
///
/// The Entity.Name string here doubles as the client renderer's sprite
/// lookup key — it must match a key under <c>characters</c> or
/// <c>characterExtras</c> in
/// <c>client/public/assets/dungeon/assets.json</c>.
/// </summary>
public static class EnemyTemplates
{
    public static Entity Create(EnemyArchetype archetype, Position position, Guid floorId) => archetype switch
    {
        EnemyArchetype.Husk => Husk(position, floorId),
        EnemyArchetype.Rasper => Rasper(position, floorId),
        EnemyArchetype.Hulk => Hulk(position, floorId),
        EnemyArchetype.TinySlug => TinySlug(position, floorId),
        EnemyArchetype.BigSlug => BigSlug(position, floorId),
        EnemyArchetype.Goblin => Goblin(position, floorId),
        EnemyArchetype.Skeleton => Skeleton(position, floorId),
        EnemyArchetype.MaskedOrc => MaskedOrc(position, floorId),
        EnemyArchetype.Chort => Chort(position, floorId),
        EnemyArchetype.BigZombie => BigZombie(position, floorId),
        EnemyArchetype.Ogre => Ogre(position, floorId),
        EnemyArchetype.BigDemon => BigDemon(position, floorId),
        EnemyArchetype.Mimic => Mimic(position, floorId),
        _ => throw new ArgumentOutOfRangeException(nameof(archetype))
    };

    private static Entity Husk(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Husk",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 16, MaxHp = 16,
            Ac = 11,
            AttackMod = 2,
            Damage = new DiceRoll(1, 6, 0),
            InitiativeMod = 0,
            Speed = 25,
            SightRadius = 4,
            StrMod = 1, DexMod = 0, ConMod = 1
        }
    };

    private static Entity Rasper(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Rasper",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 12, MaxHp = 12,
            Ac = 13,
            AttackMod = 3,
            Damage = new DiceRoll(1, 4, 0),
            InitiativeMod = 3,
            Speed = 35,
            SightRadius = 5,
            StrMod = 0, DexMod = 2, ConMod = -1
        }
    };

    private static Entity Hulk(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Hulk",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 28, MaxHp = 28,
            Ac = 10,
            AttackMod = 3,
            Damage = new DiceRoll(1, 8, 1),
            InitiativeMod = -1,
            Speed = 15,
            SightRadius = 3,
            StrMod = 2, DexMod = -1, ConMod = 2
        }
    };

    private static Entity TinySlug(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Slug",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 1, MaxHp = 9,
            Ac = 9,
            AttackMod = 1,
            Damage = new DiceRoll(1, 4, 0),
            InitiativeMod = -1,
            Speed = 15,
            SightRadius = 3,
            StrMod = -1, DexMod = -1, ConMod = 1
        },
        // Step 5 — slime trail: each successful bite leaves a mild
        // poison that ticks for 2 rounds.
        OnHitStatus = new StatusEffect(StatusEffectKind.Poison, RoundsRemaining: 2, DamagePerTick: 1)
    };

    private static Entity BigSlug(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Big Slug",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            // Tutorial-floor boss — tuned so a fresh-Regular-Sword
            // player can just barely beat it with average rolls.
            // Player avg dmg ~2.7/round vs AC 10 → ~7 rounds to kill.
            // BigSlug avg dmg ~2.5/round + 1 dmg/turn poison ticks →
            // player drops in ~7-8 rounds. Bad rolls tip it to a loss.
            Hp = 18, MaxHp = 18,
            Ac = 10,
            AttackMod = 2,
            Damage = new DiceRoll(1, 6, 1),
            InitiativeMod = -2,
            Speed = 10,
            SightRadius = 4,
            StrMod = 2, DexMod = -2, ConMod = 2
        },
        // Step 5 — light poison so the bite lingers without dominating
        // the encounter.
        OnHitStatus = new StatusEffect(StatusEffectKind.Poison, RoundsRemaining: 2, DamagePerTick: 1)
    };

    // Lean small attacker — between Husk and Rasper. Slightly faster
    // initiative than Husk, similar HP, modest accuracy.
    private static Entity Goblin(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Goblin",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 14, MaxHp = 14,
            Ac = 12,
            AttackMod = 2,
            Damage = new DiceRoll(1, 6, 0),
            InitiativeMod = 1,
            Speed = 28,
            SightRadius = 4,
            StrMod = 0, DexMod = 1, ConMod = 0
        }
    };

    // Tougher small enemy — bone tank. More HP than Husk and a small flat
    // damage bonus, neutral initiative.
    private static Entity Skeleton(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Skeleton",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 18, MaxHp = 18,
            Ac = 12,
            AttackMod = 3,
            Damage = new DiceRoll(1, 6, 1),
            InitiativeMod = 0,
            Speed = 25,
            SightRadius = 5,
            StrMod = 1, DexMod = 0, ConMod = 1
        }
    };

    // Balanced medium orc — solid HP and AC, predictable damage. Floor 3
    // workhorse alongside Hulk.
    private static Entity MaskedOrc(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Masked Orc",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 24, MaxHp = 24,
            Ac = 13,
            AttackMod = 3,
            Damage = new DiceRoll(1, 8, 0),
            InitiativeMod = 0,
            Speed = 25,
            SightRadius = 5,
            StrMod = 2, DexMod = 0, ConMod = 1
        }
    };

    // Agile medium horned demon — slightly less HP than the orcs, faster
    // and harder to hit.
    private static Entity Chort(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Chort",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 22, MaxHp = 22,
            Ac = 14,
            AttackMod = 4,
            Damage = new DiceRoll(1, 8, 0),
            InitiativeMod = 1,
            Speed = 30,
            SightRadius = 5,
            StrMod = 1, DexMod = 2, ConMod = 1
        }
    };

    // Large shambler. Slow but two-dice damage punishes hesitation.
    private static Entity BigZombie(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Big Zombie",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            // Floor-2 stairwell boss. 60 HP made it an unkillable wall for a
            // solo player; 46 keeps it a clear step up from regulars but
            // beatable with a found weapon upgrade and a couple of heals.
            Hp = 46, MaxHp = 46,
            Ac = 11,
            AttackMod = 4,
            Damage = new DiceRoll(2, 6, 1),
            InitiativeMod = -1,
            Speed = 15,
            SightRadius = 4,
            StrMod = 3, DexMod = -1, ConMod = 3
        }
    };

    // Massive bruiser. Highest HP and damage of the regular roster — the
    // late-floor "rooms feel dangerous" anchor.
    private static Entity Ogre(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Ogre",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            // Floor-3 stairwell boss. Trimmed from 70 HP / 2d6+2 so a
            // geared solo player can grind it down with heals rather than
            // losing the DPS race outright. Still the hardest-hitting,
            // highest-HP regular-roster enemy.
            Hp = 56, MaxHp = 56,
            Ac = 12,
            AttackMod = 4,
            Damage = new DiceRoll(2, 6, 1),
            InitiativeMod = -2,
            Speed = 12,
            SightRadius = 4,
            StrMod = 4, DexMod = -2, ConMod = 4
        }
    };

    // Mimic — pops out of a chest and bites. Tough mid-tier threat with
    // a flat damage bonus to make the AoO sting. Slow initiative (the
    // surprise wears off and the player can react). Bleed on hit gives
    // the chest-trap encounter narrative weight per the design doc.
    private static Entity Mimic(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Mimic",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            // 32→26 HP — a mimic ambush plus its bleed could end a solo
            // run outright; trimmed so the surprise stings without being a
            // death sentence.
            Hp = 26, MaxHp = 26,
            Ac = 13,
            AttackMod = 4,
            Damage = new DiceRoll(1, 8, 2),
            InitiativeMod = -1,
            Speed = 12,
            SightRadius = 3,
            StrMod = 2, DexMod = 0, ConMod = 2
        },
        // Step 5 — a Mimic bite leaves the victim bleeding. Used both
        // for regular combat hits and for the AoO that ChestService
        // applies on chest open.
        OnHitStatus = new StatusEffect(StatusEffectKind.Bleed, RoundsRemaining: 3, DamagePerTick: 2)
    };

    // Fast large enemy — leans on accuracy and initiative over raw HP.
    // Different threat shape from the Ogre/BigZombie pair.
    private static Entity BigDemon(Position position, Guid floorId) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floorId,
        Type = EntityType.Enemy,
        Name = "Big Demon",
        Position = position,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            // Floor-4 capstone. 65→54 HP and +5→+4 attack so the final
            // gate is a tense fight a well-geared solo player can win
            // rather than a guaranteed wipe.
            Hp = 54, MaxHp = 54,
            Ac = 13,
            AttackMod = 4,
            Damage = new DiceRoll(2, 6, 1),
            InitiativeMod = 0,
            Speed = 22,
            SightRadius = 5,
            StrMod = 3, DexMod = 1, ConMod = 3
        }
    };
}
