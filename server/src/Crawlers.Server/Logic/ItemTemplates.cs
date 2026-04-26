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
}
