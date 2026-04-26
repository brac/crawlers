using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class CombatService
{
    /// <summary>
    /// Roll initiative, write the engagement preamble to the combat log, set
    /// SessionState.ActiveCombat and flip Session.Mode to Combat. Caller holds
    /// the SessionState lock.
    /// </summary>
    public void Start(SessionState state, Entity enemy, Dice dice)
    {
        if (enemy.Stats is null)
            throw new InvalidOperationException($"Enemy {enemy.Id} has no stats; cannot enter combat.");

        int playerInit = dice.D20() + state.Player.Stats.InitiativeMod;
        int enemyInit = dice.D20() + enemy.Stats.InitiativeMod;
        bool playerFirst = playerInit >= enemyInit;

        var log = new CombatLog
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            FloorId = state.Floor.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = CombatOutcome.InProgress
        };

        // Round 0 holds the setup events so the client can render the
        // engagement preamble in the same panel as round-by-round combat.
        var round0 = new CombatRound { Number = 0 };
        round0.Events.Add(new CombatEvent { Description = $"A {enemy.Name} closes in. Combat!" });
        round0.Events.Add(new CombatEvent
        {
            Description = playerFirst
                ? $"You move first. (initiative: you {playerInit}, them {enemyInit})"
                : $"The {enemy.Name} moves first. (initiative: them {enemyInit}, you {playerInit})"
        });
        log.Rounds.Add(round0);

        state.ActiveCombat = new ActiveCombat
        {
            EnemyId = enemy.Id,
            PlayerActsFirst = playerFirst,
            RoundNumber = 0,
            Log = log
        };
        state.Session.Mode = GameMode.Combat;
    }

    /// <summary>
    /// Advance combat by one round and return the resulting outcome. Caller
    /// holds SessionState.SyncRoot. Mutates Player.Stats.Hp and Entity.Stats.Hp;
    /// sets enemy state to Dead on kill.
    /// </summary>
    public CombatOutcome ProcessNextRound(SessionState state, Dice dice)
    {
        var combat = state.ActiveCombat
            ?? throw new InvalidOperationException("ProcessNextRound called with no active combat.");
        var enemy = state.Floor.Entities.FirstOrDefault(e => e.Id == combat.EnemyId)
            ?? throw new InvalidOperationException($"Active combat references missing enemy {combat.EnemyId}.");

        combat.RoundNumber++;
        var round = new CombatRound { Number = combat.RoundNumber };
        combat.Log.Rounds.Add(round);

        if (combat.FleeRequested)
            return ResolveFlee(state, enemy, dice, round);

        var first = combat.PlayerActsFirst ? Actor.Player : Actor.Enemy;
        var second = combat.PlayerActsFirst ? Actor.Enemy : Actor.Player;
        TakeAction(state, enemy, first, dice, round);
        if (state.Player.Stats.Hp > 0 && enemy.Stats!.Hp > 0)
            TakeAction(state, enemy, second, dice, round);

        if (state.Player.Stats.Hp <= 0) return CombatOutcome.PlayerDied;
        if (enemy.Stats!.Hp <= 0)
        {
            enemy.State = EntityState.Dead;
            return CombatOutcome.PlayerWon;
        }
        return CombatOutcome.InProgress;
    }

    /// <summary>
    /// Apply terminal state changes for a finished combat: outcome on the log,
    /// session mode transition, narrative event, loot drop on win. ActiveCombat
    /// is left in place so the client can render the final log; the next
    /// successful Move clears it.
    /// </summary>
    public void Finalize(SessionState state, CombatOutcome outcome, Dice dice)
    {
        if (state.ActiveCombat is null) return;
        state.ActiveCombat.Log.Outcome = outcome;
        state.ActiveCombat.Log.EndedAt = DateTimeOffset.UtcNow;

        var lastRound = state.ActiveCombat.Log.Rounds.Count > 0
            ? state.ActiveCombat.Log.Rounds[^1]
            : null;

        switch (outcome)
        {
            case CombatOutcome.PlayerWon:
                lastRound?.Events.Add(new CombatEvent { Description = "The enemy crumples to dust." });
                DropLoot(state, dice, lastRound);
                state.EnemiesKilled++;
                state.Session.Mode = GameMode.Exploration;
                break;
            case CombatOutcome.PlayerFled:
                state.Session.Mode = GameMode.Exploration;
                break;
            case CombatOutcome.PlayerDied:
                lastRound?.Events.Add(new CombatEvent { Description = "Darkness takes you." });
                state.Session.Mode = GameMode.Resolution;
                break;
        }
    }

    private static void DropLoot(SessionState state, Dice dice, CombatRound? lastRound)
    {
        if (state.ActiveCombat is null) return;
        var enemy = state.Floor.Entities.FirstOrDefault(e => e.Id == state.ActiveCombat.EnemyId);
        if (enemy is null) return;

        // Loot table (d20):
        //   1-8   no drop          (40%)
        //   9-16  Healing Draught  (40%)
        //   17-20 Bone Charm       (20%)
        int roll = dice.D20();
        Item? item = roll switch
        {
            <= 8 => null,
            <= 16 => ItemTemplates.HealingDraught(),
            _ => ItemTemplates.BoneCharm()
        };
        if (item is null) return;

        state.Floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = state.Floor.Id,
            Type = EntityType.Item,
            Name = item.Name,
            Position = enemy.Position,
            State = EntityState.Alive,
            Item = item
        });

        lastRound?.Events.Add(new CombatEvent { Description = $"It drops a {item.Name}." });
    }

    private CombatOutcome ResolveFlee(SessionState state, Entity enemy, Dice dice, CombatRound round)
    {
        // AoO: enemy gets one swing if alive and adjacent (Chebyshev ≤ 1).
        if (enemy.State == EntityState.Alive)
        {
            int dx = Math.Abs(enemy.Position.X - state.Player.Position.X);
            int dy = Math.Abs(enemy.Position.Y - state.Player.Position.Y);
            if (Math.Max(dx, dy) <= 1)
            {
                round.Events.Add(new CombatEvent { Description = $"The {enemy.Name} swings as you turn to flee!" });
                EnemyAttack(enemy, state.Player, dice, round);
                if (state.Player.Stats.Hp <= 0)
                    return CombatOutcome.PlayerDied;
            }
        }
        round.Events.Add(new CombatEvent { Description = "You break for the corridor." });
        return CombatOutcome.PlayerFled;
    }

    private static void TakeAction(SessionState state, Entity enemy, Actor actor, Dice dice, CombatRound round)
    {
        if (actor == Actor.Player)
        {
            if (state.Player.Stats.Hp <= 0) return;

            // Player's pending UseItem replaces the attack action for this round.
            // Pop the request whether or not the item is still usable.
            var combat = state.ActiveCombat!;
            if (combat.UseItemRequested is Guid itemId)
            {
                combat.UseItemRequested = null;
                var item = state.Player.Inventory.FirstOrDefault(i => i.Id == itemId);
                if (item is not null && item.IsConsumable)
                {
                    ApplyConsumable(state, item, round);
                    state.Player.Inventory.Remove(item);
                    return;
                }
                // Item missing or non-consumable: fall through and attack normally.
            }
            PlayerAttack(state.Player, enemy, dice, round);
        }
        else
        {
            if (enemy.Stats!.Hp <= 0 || enemy.State != EntityState.Alive) return;
            EnemyAttack(enemy, state.Player, dice, round);
        }
    }

    private static void ApplyConsumable(SessionState state, Item item, CombatRound round)
    {
        var description = ItemUseHelper.Apply(state, item);
        if (description is not null)
            round.Events.Add(new CombatEvent { Description = description });
    }

    private static void PlayerAttack(Player player, Entity enemy, Dice dice, CombatRound round)
    {
        var s = enemy.Stats!;
        int d20 = dice.D20();
        if (d20 == 1)
        {
            round.Events.Add(new CombatEvent { Description = "You swing wildly and miss. (nat 1)" });
            return;
        }
        int passiveAtk = ItemEffects.AttackBonusFromInventory(player);
        int total = d20 + player.Stats.AttackMod + passiveAtk;
        bool crit = d20 == 20;
        if (!crit && total < s.Ac)
        {
            round.Events.Add(new CombatEvent
            {
                Description = $"You miss the {enemy.Name}. ({total} vs AC {s.Ac})"
            });
            return;
        }
        int dmg = crit
            ? dice.RollDiceOnly(player.Stats.Damage) + dice.Roll(player.Stats.Damage)
            : dice.Roll(player.Stats.Damage);
        int newHp = Math.Max(0, s.Hp - dmg);
        enemy.Stats = s with { Hp = newHp };
        round.Events.Add(new CombatEvent
        {
            Description = crit
                ? $"You crit the {enemy.Name} for {dmg}! ({newHp}/{s.MaxHp})"
                : $"You hit the {enemy.Name} for {dmg}. ({newHp}/{s.MaxHp})"
        });
    }

    private static void EnemyAttack(Entity enemy, Player player, Dice dice, CombatRound round)
    {
        var es = enemy.Stats!;
        int d20 = dice.D20();
        if (d20 == 1)
        {
            round.Events.Add(new CombatEvent { Description = $"The {enemy.Name}'s strike goes wide. (nat 1)" });
            return;
        }
        int total = d20 + es.AttackMod;
        bool crit = d20 == 20;
        if (!crit && total < player.Stats.Ac)
        {
            round.Events.Add(new CombatEvent
            {
                Description = $"The {enemy.Name} misses you. ({total} vs AC {player.Stats.Ac})"
            });
            return;
        }
        int dmg = crit
            ? dice.RollDiceOnly(es.Damage) + dice.Roll(es.Damage)
            : dice.Roll(es.Damage);
        var newHp = Math.Max(0, player.Stats.Hp - dmg);
        player.Stats = player.Stats with { Hp = newHp };
        round.Events.Add(new CombatEvent
        {
            Description = crit
                ? $"The {enemy.Name} crits you for {dmg}! ({newHp}/{player.Stats.MaxHp})"
                : $"The {enemy.Name} hits you for {dmg}. ({newHp}/{player.Stats.MaxHp})"
        });
    }

    private enum Actor { Player, Enemy }
}
