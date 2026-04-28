using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Generation.Scaling;
using Crawlers.Generation.Weapons;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Step 3.3 — chest interaction. The hub method <c>OpenChest</c> calls
/// <see cref="TryOpen"/> under the session lock; this service does the
/// validation (player exists, chest exists on player's floor, chest
/// closed, player adjacent) and dispatches by <see cref="ChestKind"/>:
/// Standard drops a placeholder loot item on the chest tile (the proper
/// loot tables ship in Step 3.4); Empty is state-only; Mimic is also
/// state-only for now (Step 3.6 wires up the mimic-attack + AoO + combat
/// initiation). Either way the chest flips to <see cref="Entity.IsOpen"/>
/// = true so the renderer swaps to the kind-specific open sprite.
/// </summary>
public class ChestService
{
    public enum OpenResult
    {
        Opened,
        Rejected
    }

    private readonly FloorScalingTable _scaling;
    private readonly WeaponRegistry? _weapons;
    private readonly Random _lootRng;
    private readonly Dice _dice;
    private readonly int _goldChanceOutOfTen;

    public ChestService(
        FloorScalingTable? scaling = null,
        WeaponRegistry? weapons = null,
        Random? lootRng = null,
        int goldChanceOutOfTen = 7,
        Dice? dice = null)
    {
        // Tests build ChestService directly with no DI; the identity table
        // gives every floor a no-WeaponLoot config so tests fall back to
        // the Healing Draught placeholder unless they wire a custom one.
        // goldChanceOutOfTen overrides the default 70% gold roll for
        // deterministic tests (0 = always weapon, 10 = always gold).
        // dice rolls the AoO attack on Mimic chest opens — tests inject
        // a ScriptedDice for deterministic outcomes.
        _scaling = scaling ?? FloorScalingTable.Identity();
        _weapons = weapons;
        _lootRng = lootRng ?? Random.Shared;
        _goldChanceOutOfTen = Math.Clamp(goldChanceOutOfTen, 0, 10);
        _dice = dice ?? new Dice();
    }

    /// <summary>
    /// Attempt to open the given chest entity for the given player.
    /// Caller holds <see cref="SessionState.SyncRoot"/>.
    /// </summary>
    public OpenResult TryOpen(SessionState state, Guid playerId, Guid chestId)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return OpenResult.Rejected;
        if (player.Mode != GameMode.Exploration) return OpenResult.Rejected;

        var floor = state.GetFloorFor(player);
        var chest = floor.Entities.FirstOrDefault(e =>
            e.Id == chestId && e.Type == EntityType.Chest);
        if (chest is null) return OpenResult.Rejected;
        if (chest.IsOpen) return OpenResult.Rejected;

        // Adjacency: Chebyshev distance ≤ 1 (8 surrounding tiles or the
        // same tile, since chests don't block movement so a player can
        // stand on top of one).
        var dx = Math.Abs(player.Position.X - chest.Position.X);
        var dy = Math.Abs(player.Position.Y - chest.Position.Y);
        if (dx > 1 || dy > 1) return OpenResult.Rejected;

        chest.IsOpen = true;

        switch (chest.ChestKind ?? ChestKind.Standard)
        {
            case ChestKind.Standard:
                ResolveStandardOpen(player, floor, chest);
                break;

            case ChestKind.Empty:
                // No loot, nothing else to do — the open-empty sprite is
                // the entire feedback the player gets.
                break;

            case ChestKind.Mimic:
                ResolveMimicOpen(player, floor, chest);
                break;
        }

        return OpenResult.Opened;
    }

    /// <summary>
    /// Step 3.6 — Mimic chest open: the chest entity is removed and
    /// replaced by a Mimic enemy at the same tile. The opener takes one
    /// Attack-of-Opportunity bite (d20+atkMod vs AC; mimic's damage
    /// dice on hit) before normal combat starts. The post-move
    /// engagement check in <c>GameHub.Move</c> picks up the new mimic
    /// because the player is on its tile (chebyshev 0) — combat starts
    /// next, with the player already at reduced HP from the AoO. In
    /// multiplayer only the opener takes the AoO; teammates engage
    /// normally via the same engagement path.
    /// </summary>
    private void ResolveMimicOpen(Player player, Floor floor, Entity chest)
    {
        // Replace the chest with a Mimic enemy at the same tile.
        floor.Entities.Remove(chest);
        var mimic = EnemyTemplates.Create(EnemyArchetype.Mimic, chest.Position, floor.Id);
        floor.Entities.Add(mimic);

        // Attack of Opportunity — the mimic's first bite, before
        // initiative. d20 + atkMod vs player's AC. On hit, roll damage
        // and apply. Crits / fumbles use the same nat-20 / nat-1 rules
        // as regular combat for consistency, even though there's no
        // CombatLog narrating this hit (it's a pre-combat surprise).
        var roll = _dice.D20();
        var atk = roll + (mimic.Stats?.AttackMod ?? 0);
        var hits = roll != 1 && (roll == 20 || atk >= player.Stats.Ac);
        if (hits)
        {
            var dmg = roll == 20
                ? _dice.RollDiceOnly(mimic.Stats!.Damage) + _dice.RollDiceOnly(mimic.Stats.Damage) + mimic.Stats.Damage.Modifier
                : _dice.Roll(mimic.Stats!.Damage);
            var newHp = Math.Max(0, player.Stats.Hp - dmg);
            player.Stats = player.Stats with { Hp = newHp };
            // Step 5 — apply mimic's on-hit status (Bleed) to the AoO
            // victim, so the combat that immediately follows starts
            // with the player already bleeding. Skipped if the bite
            // killed them.
            if (player.Stats.Hp > 0 && mimic.OnHitStatus is { } status)
            {
                StatusEffectHelper.Apply(player.StatusEffects, status);
            }
            // Note: if the AoO drops the player to 0 HP, RunEnd / death
            // bookkeeping is left to the next combat tick or movement
            // pass — this method only applies the damage. (Realistically
            // a buffed dev player won't die to one bite; if it ever
            // matters, the death path can be lifted out.)
        }
    }

    /// <summary>
    /// Step 3.4 — Standard chest open: most rolls credit gold, the rest
    /// drop a weapon on an adjacent free tile so the player can see it
    /// and walk to it (auto-pickup-on-walk-over equips it).
    /// </summary>
    private void ResolveStandardOpen(Player player, Floor floor, Entity chest)
    {
        var scaling = _scaling.For(floor.FloorNumber);

        // Gold branch — credit immediately. The client plays a transient
        // spinning-coin animation when it sees the player's gold counter
        // bump on the next snapshot; no floor entity persists.
        if (_lootRng.Next(10) < _goldChanceOutOfTen)
        {
            var baseAmount = 5 + _lootRng.Next(10);                       // 5..14
            var amount = Math.Max(1, (int)Math.Round(baseAmount * scaling.GoldMultiplier));
            player.Gold += amount;
            return;
        }

        // Non-gold branch — pick weapon vs consumable based on which
        // pools the floor has configured. Both populated → 50/50 coin
        // flip. Only one populated → that one. Neither populated →
        // legacy Healing Draught fallback (kept inside RollWeaponLoot).
        var item = ResolveNonGoldDrop(scaling);
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Item,
            Name = item.Name,
            Position = chest.Position,
            State = EntityState.Alive,
            Item = item
        });
    }

    /// <summary>
    /// Step 4 — pick the non-gold drop for a Standard chest. Weapon vs
    /// consumable is a 50/50 when both pools have entries on this
    /// floor; otherwise we use whichever pool is configured. With
    /// neither configured, RollWeaponLoot's HealingDraught fallback
    /// keeps a useful drop coming.
    /// </summary>
    private Item ResolveNonGoldDrop(FloorScaling scaling)
    {
        var hasWeapons = scaling.WeaponLoot is { Count: > 0 };
        var hasConsumables = scaling.ConsumableLoot is { Count: > 0 };

        if (hasWeapons && hasConsumables)
        {
            return _lootRng.Next(2) == 0
                ? RollWeaponLoot(scaling.FloorNumber)
                : RollConsumableLoot(scaling);
        }
        if (hasConsumables) return RollConsumableLoot(scaling);
        return RollWeaponLoot(scaling.FloorNumber);
    }

    /// <summary>
    /// Weighted draw from the floor's <see cref="FloorScaling.ConsumableLoot"/>
    /// pool, resolved through <see cref="ItemTemplates"/>. Falls back to
    /// a Healing Draught when the rolled name doesn't resolve (defensive
    /// — typo in JSON would only surface here at runtime).
    /// </summary>
    private Item RollConsumableLoot(FloorScaling scaling)
    {
        var pool = scaling.ConsumableLoot!;
        int total = 0;
        for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;
        if (total <= 0) return ItemTemplates.HealingDraught();

        int roll = _lootRng.Next(total);
        int acc = 0;
        string? name = null;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += pool[i].Weight;
            if (roll < acc) { name = pool[i].Name; break; }
        }
        return BuildConsumable(name) ?? ItemTemplates.HealingDraught();
    }

    private static Item? BuildConsumable(string? name) => name switch
    {
        "Healing Draught"        => ItemTemplates.HealingDraught(),
        "Greater Healing Potion" => ItemTemplates.GreaterHealingPotion(),
        "Strength Tonic"         => ItemTemplates.StrengthTonic(),
        "Quickness Vial"         => ItemTemplates.QuicknessVial(),
        "Antidote"               => ItemTemplates.Antidote(),
        _ => null
    };

    /// <summary>
    /// Weighted draw from the floor's <see cref="FloorScaling.WeaponLoot"/>
    /// pool, resolved through <see cref="WeaponRegistry"/>. Falls back to
    /// a Healing Draught when no pool is configured or the rolled name
    /// doesn't resolve — keeps tests + un-wired callers working.
    /// </summary>
    private Item RollWeaponLoot(int floorNumber)
    {
        var pool = _scaling.For(floorNumber).WeaponLoot;
        if (pool is null || pool.Count == 0 || _weapons is null)
            return ItemTemplates.HealingDraught();

        var name = WeightedPick(pool, _lootRng);
        if (!_weapons.TryGet(name, out var def))
            return ItemTemplates.HealingDraught();

        return new Item
        {
            Id = Guid.NewGuid(),
            Name = def.Name,
            Description = $"{Format(def.Damage)} damage, {FormatInit(def.InitiativeMod)} init.",
            IsConsumable = false,
            Effect = ItemEffect.None,
            EffectValue = 0,
            Weapon = new WeaponBlock(def.Damage, def.InitiativeMod)
        };
    }


    private static string WeightedPick(IReadOnlyList<WeaponLootEntry> pool, Random rng)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;
        if (total <= 0) return pool[0].Name;
        int roll = rng.Next(total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += pool[i].Weight;
            if (roll < acc) return pool[i].Name;
        }
        return pool[^1].Name;
    }

    private static string Format(DiceRoll d)
    {
        var dice = $"{d.Count}d{d.Sides}";
        if (d.Modifier == 0) return dice;
        return d.Modifier > 0 ? $"{dice}+{d.Modifier}" : $"{dice}{d.Modifier}";
    }

    private static string FormatInit(int mod) =>
        mod > 0 ? $"+{mod}" : mod.ToString();
}
