using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class CombatService
{
    /// <summary>
    /// Stand up a fresh combat: initiative roll for everyone (every
    /// participant + the enemy), preamble events on round 0, dict entries on
    /// the session that point every participant at the shared combat.
    /// Caller holds the SessionState lock.
    /// </summary>
    public ActiveCombat Start(
        SessionState state,
        Entity enemy,
        IReadOnlyList<Player> participants,
        Dice dice)
    {
        if (enemy.Stats is null)
            throw new InvalidOperationException($"Enemy {enemy.Id} has no stats; cannot enter combat.");
        if (participants.Count == 0)
            throw new InvalidOperationException("Combat needs at least one participant.");

        var floor = state.GetFloorFor(participants[0]);
        // Roll initiative explicitly in the sequence "all participants in order,
        // then enemy" — LINQ Append would evaluate its parameter (and therefore
        // the enemy's D20()) eagerly before Select iterates, scrambling the
        // dice queue for ScriptedDice tests.
        var rolls = new List<(Guid id, string name, int roll)>(participants.Count + 1);
        foreach (var p in participants)
        {
            int r = dice.D20() + p.Stats.InitiativeMod;
            rolls.Add((p.Id, $"Player {ShortTag(p.Id)}", r));
        }
        int er = dice.D20() + enemy.Stats.InitiativeMod;
        rolls.Add((enemy.Id, $"the {enemy.Name}", er));
        rolls.Sort((a, b) =>
            b.roll != a.roll ? b.roll.CompareTo(a.roll) : a.id.CompareTo(b.id));

        var log = new CombatLog
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            FloorId = floor.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Outcome = CombatOutcome.InProgress
        };

        var round0 = new CombatRound { Number = 0 };
        round0.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Narrative,
            Description = participants.Count == 1
                ? $"A {enemy.Name} closes in. Combat!"
                : $"A {enemy.Name} closes in. {participants.Count} of you ready your blades."
        });
        round0.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Narrative,
            Description = "Initiative — " + string.Join(", ",
                rolls.Select(r => $"{r.name}: {r.roll}"))
        });
        log.Rounds.Add(round0);

        var combat = new ActiveCombat
        {
            FloorNumber = participants[0].CurrentFloorNumber,
            EnemyId = enemy.Id,
            Log = log,
        };
        foreach (var (id, _, _) in rolls) combat.InitiativeOrder.Add(id);
        foreach (var p in participants)
        {
            combat.ParticipantPlayerIds.Add(p.Id);
            state.SetCombat(p.Id, combat);
            p.Mode = GameMode.Combat;
        }

        return combat;
    }

    /// <summary>
    /// A teammate steps into engagement range mid-fight. Returns true if the
    /// player was added; false if the combat has already concluded (so the
    /// caller knows to spin up a fresh combat for this engagement instead).
    /// Late joiners append at the back of the initiative order — they act
    /// last next round, not this one.
    /// </summary>
    public bool AddPlayer(SessionState state, ActiveCombat combat, Player player, Dice dice)
    {
        // Treat a Finalize'd combat as closed for joins. Without this guard the
        // runner has already returned (its `_active` entry was removed), so
        // adding a player would lock them into Mode=Combat with no rounds
        // ticking — the "stuck in combat" symptom.
        if (combat.Log.Outcome != CombatOutcome.InProgress) return false;
        if (combat.HasParticipant(player.Id)) return true;

        int roll = dice.D20() + player.Stats.InitiativeMod;
        combat.ParticipantPlayerIds.Add(player.Id);
        combat.InitiativeOrder.Add(player.Id);
        state.SetCombat(player.Id, combat);
        player.Mode = GameMode.Combat;

        // Slot the join as a narrative line on the in-flight round (or round 0
        // if we somehow joined before the first tick).
        var round = combat.Log.Rounds.LastOrDefault() ?? new CombatRound { Number = 0 };
        if (combat.Log.Rounds.Count == 0) combat.Log.Rounds.Add(round);
        round.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Narrative,
            ActorId = player.Id,
            Description = $"Player {ShortTag(player.Id)} joins the fight! (initiative {roll})"
        });
        return true;
    }

    /// <summary>4-char uppercase prefix of the player id — matches the
    /// floating name label so combat-log lines and the on-screen badge read
    /// the same identity.</summary>
    private static string ShortTag(Guid id) => id.ToString()[..4].ToUpperInvariant();

    /// <summary>
    /// Advance one combat round. Returns whether combat ended this round and
    /// any per-player exits (flee / die) that occurred — the runner consumes
    /// the latter to persist deaths.
    /// </summary>
    public CombatRoundResult ProcessNextRound(SessionState state, ActiveCombat combat, Dice dice)
    {
        var floor = state.GetFloor(combat.FloorNumber)
            ?? throw new InvalidOperationException($"Combat references missing floor {combat.FloorNumber}.");
        var enemy = floor.Entities.FirstOrDefault(e => e.Id == combat.EnemyId)
            ?? throw new InvalidOperationException($"Active combat references missing enemy {combat.EnemyId}.");

        combat.RoundNumber++;
        var round = new CombatRound { Number = combat.RoundNumber };
        combat.Log.Rounds.Add(round);

        var exits = new List<(Guid PlayerId, CombatOutcome Outcome)>();

        // Snapshot the order — late joiners added during this round won't act
        // until next tick.
        var actors = combat.InitiativeOrder.ToList();
        foreach (var actorId in actors)
        {
            // Bail early if the round-end conditions are already met.
            if (enemy.Stats!.Hp <= 0) break;
            if (combat.ParticipantPlayerIds.Count == 0) break;

            if (actorId == combat.EnemyId)
            {
                EnemyTurn(state, combat, enemy, dice, round, exits);
            }
            else
            {
                if (!combat.HasParticipant(actorId)) continue;
                var player = state.GetPlayer(actorId);
                if (player is null || player.Stats.Hp <= 0) continue;
                PlayerTurn(state, combat, player, enemy, dice, round, exits);
            }
        }

        bool enemyDead = enemy.Stats!.Hp <= 0;
        bool noOneLeft = combat.ParticipantPlayerIds.Count == 0;
        bool ended = enemyDead || noOneLeft;
        if (ended)
        {
            Finalize(state, combat, enemy, floor, enemyDead, dice, exits);
        }
        return new CombatRoundResult(ended, exits);
    }

    private void Finalize(
        SessionState state,
        ActiveCombat combat,
        Entity enemy,
        Floor floor,
        bool enemyDead,
        Dice dice,
        List<(Guid PlayerId, CombatOutcome Outcome)> exits)
    {
        combat.Log.EndedAt = DateTimeOffset.UtcNow;
        var lastRound = combat.Log.Rounds.LastOrDefault();

        if (enemyDead)
        {
            enemy.State = EntityState.Dead;
            lastRound?.Events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Death,
                TargetId = enemy.Id,
                Description = "The enemy crumples to dust."
            });
            DropLoot(floor, combat, dice, lastRound);
            state.EnemiesKilled++;
            MaybeUnlockBossDoor(floor, enemy.Id);

            // Snapshot remaining participants — assigning their PlayerWon
            // outcome and flipping mode out of Combat is the last thing we do
            // here, then the participant list is drained.
            var winners = combat.ParticipantPlayerIds.ToList();
            foreach (var pid in winners)
            {
                combat.ParticipantOutcomes[pid] = CombatOutcome.PlayerWon;
                exits.Add((pid, CombatOutcome.PlayerWon));
                var p = state.GetPlayer(pid);
                if (p is not null) p.Mode = GameMode.Exploration;
            }
            combat.ParticipantPlayerIds.Clear();
            combat.Log.Outcome = CombatOutcome.PlayerWon;
        }
        else
        {
            // No participants left — the log's "primary" outcome is whichever
            // is more dramatic; per-player outcomes already on the combat
            // capture each one's individual fate.
            var anyDied = combat.ParticipantOutcomes.Values.Any(o => o == CombatOutcome.PlayerDied);
            combat.Log.Outcome = anyDied ? CombatOutcome.PlayerDied : CombatOutcome.PlayerFled;
        }
    }

    private void PlayerTurn(
        SessionState state,
        ActiveCombat combat,
        Player player,
        Entity enemy,
        Dice dice,
        CombatRound round,
        List<(Guid, CombatOutcome)> exits)
    {
        // Flee request: pop and resolve. Even if the AoO kills them their
        // outcome is PlayerDied (death wins over flee).
        if (combat.FleeRequested.Remove(player.Id))
        {
            if (ResolveFlee(state, combat, player, enemy, dice, round, exits)) return;
            return;
        }

        // Use-item request: pop and apply (skips the attack action this round).
        if (combat.UseItemRequested.Remove(player.Id, out var itemId))
        {
            var item = player.Inventory.FirstOrDefault(i => i.Id == itemId);
            if (item is not null && item.IsConsumable)
            {
                ApplyConsumable(player, item, round);
                player.Inventory.Remove(item);
                return;
            }
            // Item missing or non-consumable: fall through to attack normally.
        }

        PlayerAttack(player, enemy, dice, round);
    }

    private void EnemyTurn(
        SessionState state,
        ActiveCombat combat,
        Entity enemy,
        Dice dice,
        CombatRound round,
        List<(Guid PlayerId, CombatOutcome Outcome)> exits)
    {
        if (combat.ParticipantPlayerIds.Count == 0) return;

        // Pick a random alive participant. Targeting a dead player would no-op
        // since they're already removed; we still defend with the alive check.
        var idx = dice.NextInt(combat.ParticipantPlayerIds.Count);
        var targetId = combat.ParticipantPlayerIds[idx];
        var target = state.GetPlayer(targetId);
        if (target is null || target.Stats.Hp <= 0) return;

        EnemyAttack(enemy, target, dice, round);

        if (target.Stats.Hp <= 0)
            MarkPlayerDied(state, combat, target, enemy, round, exits);
    }

    private bool ResolveFlee(
        SessionState state,
        ActiveCombat combat,
        Player player,
        Entity enemy,
        Dice dice,
        CombatRound round,
        List<(Guid PlayerId, CombatOutcome Outcome)> exits)
    {
        // AoO if the enemy is alive and adjacent (Chebyshev ≤ 1).
        if (enemy.State == EntityState.Alive)
        {
            int dx = Math.Abs(enemy.Position.X - player.Position.X);
            int dy = Math.Abs(enemy.Position.Y - player.Position.Y);
            if (Math.Max(dx, dy) <= 1)
            {
                round.Events.Add(new CombatEvent
                {
                    Kind = CombatEventKind.Narrative,
                    ActorId = enemy.Id,
                    TargetId = player.Id,
                    Description = $"The {enemy.Name} swings as Player {ShortTag(player.Id)} turns to flee!"
                });
                EnemyAttack(enemy, player, dice, round);
                if (player.Stats.Hp <= 0)
                {
                    MarkPlayerDied(state, combat, player, enemy, round, exits);
                    return true;
                }
            }
        }

        round.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Flee,
            ActorId = player.Id,
            Description = $"Player {ShortTag(player.Id)} breaks for the corridor."
        });
        combat.ParticipantPlayerIds.Remove(player.Id);
        combat.ParticipantOutcomes[player.Id] = CombatOutcome.PlayerFled;
        player.Mode = GameMode.Exploration;
        exits.Add((player.Id, CombatOutcome.PlayerFled));
        return true;
    }

    private static void MarkPlayerDied(
        SessionState state,
        ActiveCombat combat,
        Player player,
        Entity enemy,
        CombatRound round,
        List<(Guid PlayerId, CombatOutcome Outcome)> exits)
    {
        round.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Death,
            TargetId = player.Id,
            Description = $"Darkness takes Player {ShortTag(player.Id)}."
        });
        combat.ParticipantPlayerIds.Remove(player.Id);
        combat.ParticipantOutcomes[player.Id] = CombatOutcome.PlayerDied;
        player.Mode = GameMode.Resolution;
        player.DiedAt = DateTimeOffset.UtcNow;
        player.CauseOfDeath = enemy.Name is { } name ? $"Slain by a {name}" : "Slain";
        exits.Add((player.Id, CombatOutcome.PlayerDied));

        // Drop a corpse near the death tile so survivors can see where they
        // fell. CorpsePlacement.PickFreeTile scatters into a nearby walkable
        // tile when the death tile already has a corpse (from a prior run
        // hydrated onto this floor); the persisted row separately captures
        // the true death tile, so the data layer stays honest. State=Alive
        // marks the corpse as "currently in the world" — the snapshot mapper
        // filters dead-state entities (defeated enemies). Corpses don't
        // block movement; MovementService's collision rule only looks at
        // living Players.
        var floor = state.GetFloorFor(player);
        var displayPos = Crawlers.Server.Persistence.CorpsePlacement.PickFreeTile(
            floor, player.Position, Random.Shared);
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Corpse,
            Name = "Corpse",
            Position = displayPos,
            State = EntityState.Alive,
            PlayerId = player.Id,
            DiedAt = player.DiedAt,
            Username = string.IsNullOrEmpty(player.Username) ? null : player.Username,
            KillerType = enemy.Name
        });
    }

    private static void MaybeUnlockBossDoor(Floor floor, Guid? deadEnemyId)
    {
        if (deadEnemyId is null || floor.BossEntityId != deadEnemyId) return;
        if (floor.BossDoor is { } door)
            floor.TileGrid[door.X, door.Y] = new Tile(TileType.OpenDoor);
        floor.BossEntityId = null;
    }

    private static void DropLoot(Floor floor, ActiveCombat combat, Dice dice, CombatRound? lastRound)
    {
        var enemy = floor.Entities.FirstOrDefault(e => e.Id == combat.EnemyId);
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

        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Item,
            Name = item.Name,
            Position = enemy.Position,
            State = EntityState.Alive,
            Item = item
        });
        lastRound?.Events.Add(new CombatEvent
        {
            Kind = CombatEventKind.Loot,
            Description = $"It drops a {item.Name}."
        });
    }

    private static void ApplyConsumable(Player player, Item item, CombatRound round)
    {
        var description = ItemUseHelper.Apply(player, item);
        if (description is null) return;
        round.Events.Add(new CombatEvent
        {
            Kind = item.Effect == ItemEffect.Heal ? CombatEventKind.Heal : CombatEventKind.Narrative,
            ActorId = player.Id,
            TargetId = player.Id,
            Description = description
        });
    }

    private static void PlayerAttack(Player player, Entity enemy, Dice dice, CombatRound round)
    {
        var s = enemy.Stats!;
        var tag = ShortTag(player.Id);
        int d20 = dice.D20();
        if (d20 == 1)
        {
            round.Events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Fumble,
                ActorId = player.Id,
                TargetId = enemy.Id,
                Description = $"Player {tag} swings wildly and misses. (nat 1)"
            });
            return;
        }
        int passiveAtk = ItemEffects.AttackBonusFromInventory(player);
        int total = d20 + player.Stats.AttackMod + passiveAtk;
        bool crit = d20 == 20;
        if (!crit && total < s.Ac)
        {
            round.Events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Miss,
                ActorId = player.Id,
                TargetId = enemy.Id,
                Description = $"Player {tag} misses the {enemy.Name}. ({total} vs AC {s.Ac})"
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
            Kind = crit ? CombatEventKind.Crit : CombatEventKind.Hit,
            ActorId = player.Id,
            TargetId = enemy.Id,
            Damage = dmg,
            Description = crit
                ? $"Player {tag} crits the {enemy.Name} for {dmg}! ({newHp}/{s.MaxHp})"
                : $"Player {tag} hits the {enemy.Name} for {dmg}. ({newHp}/{s.MaxHp})"
        });
    }

    private static void EnemyAttack(Entity enemy, Player player, Dice dice, CombatRound round)
    {
        var es = enemy.Stats!;
        var tag = ShortTag(player.Id);
        int d20 = dice.D20();
        if (d20 == 1)
        {
            round.Events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Fumble,
                ActorId = enemy.Id,
                TargetId = player.Id,
                Description = $"The {enemy.Name}'s strike goes wide. (nat 1)"
            });
            return;
        }
        int total = d20 + es.AttackMod;
        bool crit = d20 == 20;
        if (!crit && total < player.Stats.Ac)
        {
            round.Events.Add(new CombatEvent
            {
                Kind = CombatEventKind.Miss,
                ActorId = enemy.Id,
                TargetId = player.Id,
                Description = $"The {enemy.Name} misses Player {tag}. ({total} vs AC {player.Stats.Ac})"
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
            Kind = crit ? CombatEventKind.Crit : CombatEventKind.Hit,
            ActorId = enemy.Id,
            TargetId = player.Id,
            Damage = dmg,
            Description = crit
                ? $"The {enemy.Name} crits Player {tag} for {dmg}! ({newHp}/{player.Stats.MaxHp})"
                : $"The {enemy.Name} hits Player {tag} for {dmg}. ({newHp}/{player.Stats.MaxHp})"
        });
    }
}

public record CombatRoundResult(
    bool Ended,
    IReadOnlyList<(Guid PlayerId, CombatOutcome Outcome)> ExitedThisRound
);
