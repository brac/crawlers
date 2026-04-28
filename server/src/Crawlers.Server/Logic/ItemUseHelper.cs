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
            // Step 4 — permanent stat bumps. EffectValue is the integer
            // to add to the matching Stats field. No cap for now; if
            // stacking ever feels imbalanced, clamp here or move the
            // limit into a tunable somewhere.
            case ItemEffect.AttackBonusPermanent:
            {
                player.Stats = player.Stats with { AttackMod = player.Stats.AttackMod + item.EffectValue };
                return $"Player {tag} drinks a {item.Name}. Attack +{item.EffectValue} (now {player.Stats.AttackMod:+0;-#}).";
            }
            case ItemEffect.InitiativeBonusPermanent:
            {
                player.Stats = player.Stats with { InitiativeMod = player.Stats.InitiativeMod + item.EffectValue };
                return $"Player {tag} drinks a {item.Name}. Initiative +{item.EffectValue} (now {player.Stats.InitiativeMod:+0;-#}).";
            }
            // Step 5 — Antidote clears Bleed + Poison from the user's
            // StatusEffects list. Effect is silent if the player has no
            // active statuses (still consumes the item — design choice).
            case ItemEffect.CureStatuses:
            {
                int cleared = player.StatusEffects.Count;
                StatusEffectHelper.Clear(player.StatusEffects);
                return cleared > 0
                    ? $"Player {tag} drinks a {item.Name}. {cleared} status effect(s) cleared."
                    : $"Player {tag} drinks a {item.Name}, but feels no different.";
            }
            default:
                return $"Player {tag} uses a {item.Name}.";
        }
    }
}
