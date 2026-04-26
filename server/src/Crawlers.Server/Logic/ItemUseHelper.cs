using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

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
    public static string? Apply(SessionState state, Item item)
    {
        switch (item.Effect)
        {
            case ItemEffect.Heal:
            {
                int newHp = Math.Min(state.Player.Stats.MaxHp, state.Player.Stats.Hp + item.EffectValue);
                int healed = newHp - state.Player.Stats.Hp;
                state.Player.Stats = state.Player.Stats with { Hp = newHp };
                return $"You drink a {item.Name} and recover {healed} HP. ({newHp}/{state.Player.Stats.MaxHp})";
            }
            default:
                return $"You use a {item.Name}.";
        }
    }
}
