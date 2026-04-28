using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

/// <summary>
/// Factory for the items available in the game. Each call returns a fresh
/// instance with a new Id so multiple drops don't collide.
/// </summary>
public static class ItemTemplates
{
    public static Item HealingDraught() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Healing Draught",
        Description = "Restores 6 HP when consumed.",
        IsConsumable = true,
        Effect = ItemEffect.Heal,
        EffectValue = 6
    };

    public static Item BoneCharm() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bone Charm",
        Description = "+1 to attack rolls while carried.",
        IsConsumable = false,
        Effect = ItemEffect.AttackBonus,
        EffectValue = 1
    };

    // Step 4 consumables.

    public static Item GreaterHealingPotion() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Greater Healing Potion",
        Description = "Restores 15 HP when consumed.",
        IsConsumable = true,
        Effect = ItemEffect.Heal,
        EffectValue = 15
    };

    public static Item StrengthTonic() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Strength Tonic",
        Description = "Permanent +1 to attack rolls.",
        IsConsumable = true,
        Effect = ItemEffect.AttackBonusPermanent,
        EffectValue = 1
    };

    public static Item QuicknessVial() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Quickness Vial",
        Description = "Permanent +1 to initiative rolls.",
        IsConsumable = true,
        Effect = ItemEffect.InitiativeBonusPermanent,
        EffectValue = 1
    };

    public static Item Antidote() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Antidote",
        Description = "Cures Bleed and Poison.",
        IsConsumable = true,
        Effect = ItemEffect.CureStatuses,
        EffectValue = 0
    };
}
