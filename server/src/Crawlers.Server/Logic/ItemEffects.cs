using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

public static class ItemEffects
{
    /// <summary>Sum of AttackBonus values from passive (non-consumable) items in inventory.</summary>
    public static int AttackBonusFromInventory(Player player) =>
        player.Inventory
            .Where(i => !i.IsConsumable && i.Effect == ItemEffect.AttackBonus)
            .Sum(i => i.EffectValue);

    /// <summary>Sum of DefenseBonus values from passive items in inventory.</summary>
    public static int DefenseBonusFromInventory(Player player) =>
        player.Inventory
            .Where(i => !i.IsConsumable && i.Effect == ItemEffect.DefenseBonus)
            .Sum(i => i.EffectValue);
}
