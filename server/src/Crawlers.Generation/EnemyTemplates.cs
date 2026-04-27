using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

/// <summary>
/// Per-archetype enemy factory. Stats are tuned so Husk is the baseline,
/// Rasper trades HP for speed/agility, Hulk trades speed for HP/damage.
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
            Hp = 8, MaxHp = 8,
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
            Hp = 5, MaxHp = 5,
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
            Hp = 14, MaxHp = 14,
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
            Hp = 6, MaxHp = 9,
            Ac = 9,
            AttackMod = 1,
            Damage = new DiceRoll(1, 4, 0),
            InitiativeMod = -1,
            Speed = 15,
            SightRadius = 3,
            StrMod = -1, DexMod = -1, ConMod = 1
        }
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
            Hp = 18, MaxHp = 22,
            Ac = 11,
            AttackMod = 2,
            Damage = new DiceRoll(1, 8, 1),
            InitiativeMod = -2,
            Speed = 10,
            SightRadius = 4,
            StrMod = 2, DexMod = -2, ConMod = 3
        }
    };
}
