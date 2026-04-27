using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

/// <summary>
/// Pure consumable-effect application. Used by CombatService inside a round
/// (where the description is appended to the combat log) and by GameHub when
/// items are used outside combat.
///
/// Caller is responsible for: verifying the item is in inventory, removing it
/// from inventory, holding the SessionState lock, and broadcasting any
/// resulting snapshot.
/// </summary>
public static class ItemUseHelper
{
    public static string? Apply(Player player, Item item)
    {
        var tag = player.Id.ToString()[..4].ToUpperInvariant();
        switch (item.Effect)
        {
            case ItemEffect.Heal:
            {
                int newHp = Math.Min(player.Stats.MaxHp, player.Stats.Hp + item.EffectValue);
                int healed = newHp - player.Stats.Hp;
                player.Stats = player.Stats with { Hp = newHp };
                return $"Player {tag} drinks a {item.Name} and recovers {healed} HP. ({newHp}/{player.Stats.MaxHp})";
            }
            default:
                return $"Player {tag} uses a {item.Name}.";
        }
    }
}
