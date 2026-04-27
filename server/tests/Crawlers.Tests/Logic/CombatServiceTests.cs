using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

public class CombatServiceTests
{
    [Fact]
    public void Start_writes_preamble_and_flips_mode()
    {
        var (state, enemy) = BuildEngagement();
        var pid = state.PrimaryPlayer.Id;
        var dice = new ScriptedDice(d20: new[] { 18, 5 }); // player init high, enemy low

        var combat = new CombatService().Start(state, enemy, new[] { state.PrimaryPlayer }, dice);

        Assert.Equal(GameMode.Combat, state.PrimaryPlayer.Mode);
        Assert.Same(combat, state.GetCombat(pid));
        Assert.Equal(2, combat.InitiativeOrder.Count);
        Assert.Equal(pid, combat.InitiativeOrder[0]); // player rolled higher
        Assert.Single(combat.Log.Rounds);
        Assert.Equal(0, combat.Log.Rounds[0].Number);
        Assert.Equal(2, combat.Log.Rounds[0].Events.Count);
    }

    [Fact]
    public void Initiative_decides_action_order()
    {
        var (state, enemy) = BuildEngagement();
        var pid = state.PrimaryPlayer.Id;
        var dice = new ScriptedDice(
            d20: new[] {
                3,   // player init
                17,  // enemy init  (enemy first)
                15,  // enemy attack roll (hits)
                15,  // player attack roll (hits)
            },
            diceOnly: new[] { 4, 4 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.False(result.Ended);
        var round1 = combat.Log.Rounds[1];
        Assert.StartsWith("The Husk", round1.Events[0].Description); // enemy went first
    }

    [Fact]
    public void Player_wins_after_enough_hits()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 5);
        var pid = state.PrimaryPlayer.Id;
        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,   // initiative — player first
                15,      // player hit
                1,       // post-kill loot drop roll (ignored if no drop)
            },
            diceOnly: new[] { 6 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(EntityState.Dead, enemy.State);
        Assert.Equal(CombatOutcome.PlayerWon, combat.Log.Outcome);
        Assert.Equal(CombatOutcome.PlayerWon, combat.ParticipantOutcomes[pid]);
        Assert.Equal(GameMode.Exploration, state.PrimaryPlayer.Mode);
    }

    [Fact]
    public void Critical_hit_doubles_damage_dice_keeps_modifier_once()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 50);
        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 20, 3 },
            diceOnly: new[] { 5, 4 }); // crit damage = 5 (dice-only) + 4+1mod = 10

        Assert.Equal(new DiceRoll(1, 6, 1), state.PrimaryPlayer.Stats.Damage);

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        svc.ProcessNextRound(state, combat, dice);

        var crit = combat.Log.Rounds[1].Events.FirstOrDefault(e => e.Description.Contains("crit"));
        Assert.NotNull(crit);
        Assert.Equal(40, enemy.Stats!.Hp);
    }

    [Fact]
    public void Natural_one_misses_regardless_of_modifier()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 50);
        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 1, 1 },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        svc.ProcessNextRound(state, combat, dice);

        Assert.Equal(50, enemy.Stats!.Hp);
        Assert.Equal(state.PrimaryPlayer.Stats.MaxHp, state.PrimaryPlayer.Stats.Hp);
    }

    [Fact]
    public void Player_dies_when_hp_reaches_zero()
    {
        var (state, enemy) = BuildEngagement(playerHp: 3, enemyHp: 50);
        var pid = state.PrimaryPlayer.Id;
        var dice = new ScriptedDice(
            d20: new[] { 3, 18, 20 },
            diceOnly: new[] { 5, 5 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(0, state.PrimaryPlayer.Stats.Hp);
        Assert.Equal(CombatOutcome.PlayerDied, combat.ParticipantOutcomes[pid]);
        Assert.Equal(GameMode.Resolution, state.PrimaryPlayer.Mode);
    }

    [Fact]
    public void Flee_without_adjacent_enemy_succeeds_no_damage()
    {
        var (state, enemy) = BuildEngagement(playerAt: new Position(2, 2));
        var pid = state.PrimaryPlayer.Id;
        enemy.Position = new Position(8, 2);

        var dice = new ScriptedDice(d20: new[] { 15, 5 });
        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.FleeRequested.Add(pid);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(state.PrimaryPlayer.Stats.MaxHp, state.PrimaryPlayer.Stats.Hp);
        Assert.Equal(CombatOutcome.PlayerFled, combat.ParticipantOutcomes[pid]);
        Assert.Equal(GameMode.Exploration, state.PrimaryPlayer.Mode);
    }

    [Fact]
    public void Flee_with_adjacent_enemy_triggers_aoo()
    {
        var (state, enemy) = BuildEngagement(playerAt: new Position(2, 2));
        var pid = state.PrimaryPlayer.Id;
        enemy.Position = new Position(3, 2);

        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 15 },
            diceOnly: new[] { 4 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.FleeRequested.Add(pid);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.True(state.PrimaryPlayer.Stats.Hp < state.PrimaryPlayer.Stats.MaxHp);
        Assert.Equal(CombatOutcome.PlayerFled, combat.ParticipantOutcomes[pid]);
    }

    [Fact]
    public void AoO_can_kill_the_fleeing_player()
    {
        var (state, enemy) = BuildEngagement(playerHp: 2, playerAt: new Position(2, 2));
        var pid = state.PrimaryPlayer.Id;
        enemy.Position = new Position(3, 2);

        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 20 },
            diceOnly: new[] { 6, 6 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.FleeRequested.Add(pid);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(CombatOutcome.PlayerDied, combat.ParticipantOutcomes[pid]);
    }

    [Fact]
    public void AddPlayer_rejects_a_combat_that_has_already_ended()
    {
        // Solo combat ends with the player dead — the spec scenario from the
        // server logs (only one participant, runner returns). A late joiner
        // arriving on the *same enemy* must not get slotted into the dead
        // combat (which has no runner ticking) — AddPlayer returns false so
        // the hub knows to start a fresh combat.
        var (state, enemy) = BuildEngagement(playerHp: 3, enemyHp: 50);
        var dyingId = state.PrimaryPlayer.Id;
        var dice = new ScriptedDice(
            d20: new[] { 3, 18, 20 },
            diceOnly: new[] { 5, 5 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);
        Assert.True(result.Ended);
        Assert.NotEqual(CombatOutcome.InProgress, combat.Log.Outcome);

        // Late joiner appears.
        var stats = SessionManager.DefaultPlayerStats();
        var late = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = new Position(2, 3),
            Stats = stats,
            CurrentFloorNumber = 1
        };
        state.AddPlayer(late);

        var added = svc.AddPlayer(state, combat, late, new ScriptedDice(d20: new[] { 10 }));

        Assert.False(added, "Joining a Finalize'd combat should be rejected.");
        Assert.DoesNotContain(late.Id, combat.ParticipantPlayerIds);
        Assert.Equal(GameMode.Exploration, late.Mode);
        Assert.Null(state.GetCombat(late.Id));
    }

    [Fact]
    public void AddPlayer_succeeds_for_an_ongoing_combat()
    {
        var (state, enemy) = BuildEngagement(playerHp: 50, enemyHp: 100);
        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, new ScriptedDice(d20: new[] { 15, 5 }));
        Assert.Equal(CombatOutcome.InProgress, combat.Log.Outcome);

        var late = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = new Position(2, 3),
            Stats = SessionManager.DefaultPlayerStats(),
            CurrentFloorNumber = 1
        };
        state.AddPlayer(late);

        var added = svc.AddPlayer(state, combat, late, new ScriptedDice(d20: new[] { 12 }));

        Assert.True(added);
        Assert.Contains(late.Id, combat.ParticipantPlayerIds);
        Assert.Equal(GameMode.Combat, late.Mode);
        Assert.Same(combat, state.GetCombat(late.Id));
    }

    [Fact]
    public void After_solo_death_a_late_engaging_teammate_starts_a_fresh_combat()
    {
        // Reproduces the production bug: P1 fought enemy E alone and died.
        // P2 then walks into proximity of the same E. Without the
        // GetCombatByEnemy InProgress filter, P1's lingering Finalize'd
        // combat shadows the lookup and steals the runner away from P2's
        // new combat (the user-visible "stuck in combat with no rounds"
        // symptom). With the filter, the engagement creates a brand-new
        // InProgress combat with P2 as the only participant.
        var (state, enemy) = BuildTwoPlayer(playerAHp: 3, playerBHp: 50, enemyHp: 100);
        // Move P2 well outside engagement range so they're NOT pulled into P1's fight.
        state.Players[1].Position = new Position(11, 5);

        var p1 = state.Players[0].Id;
        var p2 = state.Players[1].Id;

        // Round 1 of P1's solo fight: P1 misses, enemy crits → P1 dies.
        var dice1 = new ScriptedDice(
            d20: new[] { 18, 3, 3, 20 },
            diceOnly: new[] { 5, 5 });
        var svc = new CombatService();
        var p1Combat = svc.Start(state, enemy, new[] { state.Players[0] }, dice1);
        var r1 = svc.ProcessNextRound(state, p1Combat, dice1);
        Assert.True(r1.Ended);
        Assert.Equal(CombatOutcome.PlayerDied, p1Combat.Log.Outcome);
        Assert.Equal(GameMode.Resolution, state.Players[0].Mode);
        // Lingering combat is still in state for the death-log UX, but the
        // by-enemy lookup hides it.
        Assert.NotNull(state.GetCombat(p1));
        Assert.Null(state.GetCombatByEnemy(enemy.Id));

        // P2 engages the same enemy. The hub flow: GetCombatByEnemy returns
        // null → fall through to Start a brand-new combat for P2.
        var dice2 = new ScriptedDice(d20: new[] { 15, 5 });
        var p2Combat = svc.Start(state, enemy, new[] { state.Players[1] }, dice2);

        Assert.NotSame(p1Combat, p2Combat);
        Assert.Equal(CombatOutcome.InProgress, p2Combat.Log.Outcome);
        Assert.Contains(p2, p2Combat.ParticipantPlayerIds);
        // GetCombatByEnemy now returns P2's live combat, not P1's ended one.
        Assert.Same(p2Combat, state.GetCombatByEnemy(enemy.Id));
        Assert.Equal(GameMode.Combat, state.Players[1].Mode);
    }

    [Fact]
    public void Player_death_stamps_DiedAt_and_CauseOfDeath_on_the_player()
    {
        // Run-summary inputs (Step 13) are populated at the moment the
        // player dies, not lazily reconstructed at end-of-run.
        var (state, enemy) = BuildEngagement(playerHp: 3, enemyHp: 50);
        var dice = new ScriptedDice(
            d20: new[] { 3, 18, 20 },
            diceOnly: new[] { 5, 5 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        svc.ProcessNextRound(state, combat, dice);

        Assert.Equal(GameMode.Resolution, state.PrimaryPlayer.Mode);
        Assert.NotNull(state.PrimaryPlayer.DiedAt);
        Assert.Equal($"Slain by a {enemy.Name}", state.PrimaryPlayer.CauseOfDeath);
    }

    [Fact]
    public void Player_death_drops_a_corpse_entity_at_the_death_tile()
    {
        var (state, enemy) = BuildEngagement(playerHp: 3, enemyHp: 50);
        var pid = state.PrimaryPlayer.Id;
        var deathPos = state.PrimaryPlayer.Position;
        var dice = new ScriptedDice(
            d20: new[] { 3, 18, 20 },
            diceOnly: new[] { 5, 5 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(GameMode.Resolution, state.PrimaryPlayer.Mode);

        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var corpse = floor.Entities.FirstOrDefault(e => e.Type == EntityType.Corpse);
        Assert.NotNull(corpse);
        Assert.Equal(pid, corpse!.PlayerId);
        Assert.Equal(deathPos, corpse.Position);
        Assert.Equal(EntityState.Alive, corpse.State); // visible to the snapshot filter
    }

    [Fact]
    public void Two_players_share_the_combat_and_both_attack_the_same_enemy()
    {
        // Two participants; initiative: A → B → enemy. Enemy has enough HP to
        // survive both attacks so the round ends in InProgress.
        var (state, enemy) = BuildTwoPlayer(playerAHp: 50, playerBHp: 50, enemyHp: 50);
        var a = state.Players[0].Id;
        var b = state.Players[1].Id;

        // 2 init rolls (A high, B mid), enemy low; then in initiative order the
        // attack rolls are A, B, enemy. ScriptedDice produces values FIFO from
        // the d20 queue; ordering matches the order calls happen.
        var dice = new ScriptedDice(
            d20: new[] {
                18, // A init
                14, // B init
                3,  // enemy init   → order A, B, enemy
                15, // A attack — hit
                15, // B attack — hit
                3,  // enemy attack — miss
            },
            diceOnly: new[] { 4, 4 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, state.Players.ToList(), dice);
        Assert.Equal(new[] { a, b, combat.EnemyId }, combat.InitiativeOrder);

        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.False(result.Ended);
        // Both hits landed for 4+1 = 5 damage each → enemy at 50 - 10 = 40.
        Assert.Equal(40, enemy.Stats!.Hp);
    }

    [Fact]
    public void Friendly_fire_is_disabled_enemy_targets_only_participants()
    {
        // Two players, enemy. Enemy targets a random alive participant — not
        // the other player. Confirm: damage lands on a player, never on the
        // other player by some misrouting. (We assert by total HP loss across
        // both players being exactly the dealt damage.)
        var (state, enemy) = BuildTwoPlayer(playerAHp: 50, playerBHp: 50, enemyHp: 50);

        var dice = new ScriptedDice(
            d20: new[] {
                3,  // A init
                3,  // B init
                18, // enemy init   → enemy first, then A, B
                15, // enemy attack — hit
                15, // A attack — hit
                15, // B attack — hit
            },
            diceOnly: new[] { 5, 4, 4 });
        // Dice.NextInt always returns 0 in the scripted variant by default,
        // so enemy targets A.

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, state.Players.ToList(), dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.False(result.Ended);
        // Enemy's hit (5+0=5) on A; A and B both hit enemy (4+1=5 each → -10).
        Assert.Equal(45, state.Players[0].Stats.Hp);
        Assert.Equal(50, state.Players[1].Stats.Hp); // B untouched — no friendly fire
        Assert.Equal(40, enemy.Stats!.Hp);
    }

    [Fact]
    public void One_player_flees_while_the_other_keeps_fighting()
    {
        var (state, enemy) = BuildTwoPlayer(playerAHp: 50, playerBHp: 50, enemyHp: 50);
        var a = state.Players[0].Id;
        var b = state.Players[1].Id;

        // BuildTwoPlayer places A at (2,2), B at (3,2), enemy at (5,2). Enemy
        // is Chebyshev=3 from A, so A's flee doesn't trigger an AoO — no extra
        // d20 roll for that path.
        var dice = new ScriptedDice(
            d20: new[] {
                18, // A init
                10, // B init
                3,  // enemy init   → A, B, enemy
                15, // B attack — hit
                3,  // enemy attack — miss (targets B; A is gone)
            },
            diceOnly: new[] { 4 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, state.Players.ToList(), dice);
        combat.FleeRequested.Add(a);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.False(result.Ended);
        Assert.Equal(CombatOutcome.PlayerFled, combat.ParticipantOutcomes[a]);
        Assert.DoesNotContain(a, combat.ParticipantPlayerIds);
        Assert.Contains(b, combat.ParticipantPlayerIds);
        Assert.Equal(GameMode.Exploration, state.Players[0].Mode);
        Assert.Equal(GameMode.Combat, state.Players[1].Mode);
        Assert.Equal(45, enemy.Stats!.Hp); // only B's hit landed
    }

    [Fact]
    public void Late_joiner_appended_to_initiative_acts_next_round()
    {
        var (state, enemy) = BuildTwoPlayer(playerAHp: 50, playerBHp: 50, enemyHp: 50);
        // C is a third player not initially in combat.
        var stats = new EntityStats { Hp = 50, MaxHp = 50, Ac = 12, AttackMod = 2, Damage = new DiceRoll(1, 6, 1), InitiativeMod = 0, SightRadius = 5 };
        var c = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = new Position(4, 2),
            Stats = stats,
            CurrentFloorNumber = 1
        };
        state.AddPlayer(c);

        var dice = new ScriptedDice(d20: new[] {
            15, 5, 5,  // initiative for A (high), B, enemy
            10,        // late joiner C's initiative (any value)
        });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.Players[0], state.Players[1] }, dice);
        Assert.Equal(3, combat.InitiativeOrder.Count);

        svc.AddPlayer(state, combat, c, dice);

        Assert.Contains(c.Id, combat.ParticipantPlayerIds);
        Assert.Equal(c.Id, combat.InitiativeOrder.Last()); // appended at the end
        Assert.Equal(GameMode.Combat, c.Mode);
    }

    [Fact]
    public void When_one_teammate_dies_the_other_can_still_act_and_combat_continues()
    {
        // A goes first (init 18), then B, then enemy. We script the enemy to
        // crit A on round 1 — A dies mid-round. Combat must continue: round 2
        // should see B still attacking and the enemy still attacking B.
        var (state, enemy) = BuildTwoPlayer(playerAHp: 5, playerBHp: 50, enemyHp: 100);
        var a = state.Players[0].Id;
        var b = state.Players[1].Id;

        var dice = new ScriptedDice(
            d20: new[] {
                18, // A init
                10, // B init
                3,  // enemy init   → A, B, enemy
                15, // R1: A's attack — hit
                15, // R1: B's attack — hit
                20, // R1: enemy crit on first target (A, NextInt=0)
                15, // R2: B's attack — hit
                15, // R2: enemy attack — hit (only target is B now, NextInt=0)
            },
            diceOnly: new[] { 4, 4, 6, 6, 4, 4 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, state.Players.ToList(), dice);

        // Round 1: A acts, B acts, enemy crits A → A dies.
        var r1 = svc.ProcessNextRound(state, combat, dice);
        Assert.False(r1.Ended);
        Assert.Contains(r1.ExitedThisRound, x => x.PlayerId == a && x.Outcome == CombatOutcome.PlayerDied);
        Assert.Equal(GameMode.Resolution, state.Players[0].Mode);
        Assert.Equal(GameMode.Combat, state.Players[1].Mode); // B keeps fighting
        Assert.DoesNotContain(a, combat.ParticipantPlayerIds);
        Assert.Contains(b, combat.ParticipantPlayerIds);

        // Round 2: A's slot is skipped (no longer a participant), B attacks,
        // enemy targets the only alive participant — B.
        var r2 = svc.ProcessNextRound(state, combat, dice);
        Assert.False(r2.Ended);
        Assert.Empty(r2.ExitedThisRound); // B and enemy both still alive
        Assert.True(state.Players[1].Stats.Hp < 50, "B should have taken the enemy's swing");
        Assert.True(enemy.Stats!.Hp < 100, "B's attack should have landed");
    }

    [Fact]
    public void Combat_ends_when_all_participants_have_exited()
    {
        var (state, enemy) = BuildTwoPlayer(playerAHp: 50, playerBHp: 50, enemyHp: 200);
        var a = state.Players[0].Id;
        var b = state.Players[1].Id;

        var dice = new ScriptedDice(d20: new[] {
            18, 10, 3,  // initiative A, B, enemy
            // A flees (no AoO — enemy not adjacent), B flees (also no AoO),
            // enemy turn after both gone is a no-op.
        });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, state.Players.ToList(), dice);
        // Move enemy away so neither flee triggers AoO.
        enemy.Position = new Position(8, 4);
        combat.FleeRequested.Add(a);
        combat.FleeRequested.Add(b);

        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(CombatOutcome.PlayerFled, combat.Log.Outcome);
        Assert.Empty(combat.ParticipantPlayerIds);
        Assert.Equal(CombatOutcome.PlayerFled, combat.ParticipantOutcomes[a]);
        Assert.Equal(CombatOutcome.PlayerFled, combat.ParticipantOutcomes[b]);
    }

    internal static (SessionState state, Entity enemy) BuildEngagement(
        int playerHp = 20,
        int enemyHp = 8,
        Position? playerAt = null)
    {
        var pos = playerAt ?? new Position(2, 2);
        var (state, floor) = MakeFloor(width: 12, height: 6);
        state.AddPlayer(MakePlayer(state.Session.Id, pos, hp: playerHp));

        var enemy = MakeEnemy(floor, new Position(pos.X + 1, pos.Y), hp: enemyHp);
        floor.Entities.Add(enemy);

        return (state, enemy);
    }

    internal static (SessionState state, Entity enemy) BuildTwoPlayer(
        int playerAHp = 50, int playerBHp = 50, int enemyHp = 50)
    {
        var (state, floor) = MakeFloor(width: 12, height: 6);
        state.AddPlayer(MakePlayer(state.Session.Id, new Position(2, 2), hp: playerAHp));
        state.AddPlayer(MakePlayer(state.Session.Id, new Position(3, 2), hp: playerBHp));
        var enemy = MakeEnemy(floor, new Position(5, 2), hp: enemyHp);
        floor.Entities.Add(enemy);
        return (state, enemy);
    }

    private static (SessionState state, Floor floor) MakeFloor(int width, int height)
    {
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);
        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            FloorNumber = 1,
            Width = width,
            Height = height,
            TileGrid = grid
        };
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        state.AddFloor(floor);
        return (state, floor);
    }

    private static Player MakePlayer(Guid sessionId, Position pos, int hp) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Position = pos,
        Stats = new EntityStats
        {
            Hp = hp, MaxHp = hp, Ac = 12, AttackMod = 2,
            Damage = new DiceRoll(1, 6, 1),
            InitiativeMod = 0, Speed = 30, SightRadius = 5
        },
        CurrentFloorNumber = 1
    };

    private static Entity MakeEnemy(Floor floor, Position pos, int hp) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floor.Id,
        Type = EntityType.Enemy,
        Name = "Husk",
        Position = pos,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = hp, MaxHp = hp, Ac = 11, AttackMod = 2,
            Damage = new DiceRoll(1, 6, 0),
            InitiativeMod = 0, Speed = 25, SightRadius = 4
        }
    };
}

internal class ScriptedDice : Dice
{
    private readonly Queue<int> _d20;
    private readonly Queue<int> _diceOnly;
    private readonly Queue<int> _nextInt;

    public ScriptedDice(int[] d20, int[]? diceOnly = null, int[]? nextInt = null) : base(0)
    {
        _d20 = new Queue<int>(d20);
        _diceOnly = new Queue<int>(diceOnly ?? Array.Empty<int>());
        _nextInt = new Queue<int>(nextInt ?? Array.Empty<int>());
    }

    public override int D20() => _d20.Dequeue();
    public override int Roll(DiceRoll d) => _diceOnly.Dequeue() + d.Modifier;
    public override int RollDiceOnly(DiceRoll d) => _diceOnly.Dequeue();
    // Default to 0 so enemies in tests deterministically target the first
    // alive participant. Tests that need different targeting prepend values.
    public override int NextInt(int exclusiveMax) =>
        _nextInt.Count > 0 ? _nextInt.Dequeue() : 0;
}
