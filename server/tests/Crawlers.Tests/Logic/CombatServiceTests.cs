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
        var dice = new ScriptedDice(d20: new[] { 18, 5 }); // player init high, enemy low

        new CombatService().Start(state, enemy, dice);

        Assert.Equal(GameMode.Combat, state.Session.Mode);
        Assert.NotNull(state.ActiveCombat);
        Assert.True(state.ActiveCombat!.PlayerActsFirst);
        Assert.Single(state.ActiveCombat.Log.Rounds);
        Assert.Equal(0, state.ActiveCombat.Log.Rounds[0].Number);
        Assert.Equal(2, state.ActiveCombat.Log.Rounds[0].Events.Count);
    }

    [Fact]
    public void Initiative_decides_action_order()
    {
        // Enemy higher initiative — enemy attacks first in round 1.
        var (state, enemy) = BuildEngagement();
        var dice = new ScriptedDice(
            d20: new[] {
                3,  // player init
                17, // enemy init  (enemy first)
                15, // enemy attack roll (hits)
                15, // player attack roll (hits)
            },
            diceOnly: new[] { 4, 4 });

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.InProgress, outcome);
        var round1 = state.ActiveCombat!.Log.Rounds[1];
        Assert.StartsWith("The Husk", round1.Events[0].Description); // enemy went first
    }

    [Fact]
    public void Player_wins_after_enough_hits()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 5);
        // Player first, hits enemy hard, enemy can't recover.
        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,              // initiative (player wins)
                15,                  // round 1 — player hit
            },
            diceOnly: new[] { 6 });  // 6 damage > 5 HP

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.PlayerWon, outcome);
        Assert.Equal(EntityState.Dead, enemy.State);
    }

    [Fact]
    public void Critical_hit_doubles_damage_dice_keeps_modifier_once()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 50);
        // Player rolls 20 → crit; damage dice rolled twice + mod once.
        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,    // initiative
                20,        // crit
                3,         // enemy attack — miss
            },
            diceOnly: new[] { 5, 4 }); // crit damage = 5 (dice-only) + 4+1mod = 10

        // Player damage is DiceRoll(1, 6, 1) — confirm with helper below.
        Assert.Equal(new DiceRoll(1, 6, 1), state.Player.Stats.Damage);

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        svc.ProcessNextRound(state, dice);

        // Crit description fired; HP dropped by 10.
        var crit = state.ActiveCombat!.Log.Rounds[1].Events
            .FirstOrDefault(e => e.Description.Contains("crit"));
        Assert.NotNull(crit);
        Assert.Equal(40, enemy.Stats!.Hp);
    }

    [Fact]
    public void Natural_one_misses_regardless_of_modifier()
    {
        var (state, enemy) = BuildEngagement(playerHp: 100, enemyHp: 50);
        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,   // initiative
                1,        // player nat-1 fumble
                1,        // enemy nat-1 fumble
            },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        svc.ProcessNextRound(state, dice);

        Assert.Equal(50, enemy.Stats!.Hp); // unchanged
        Assert.Equal(state.Player.Stats.MaxHp, state.Player.Stats.Hp); // unchanged
    }

    [Fact]
    public void Player_dies_when_hp_reaches_zero()
    {
        var (state, enemy) = BuildEngagement(playerHp: 3, enemyHp: 50);
        var dice = new ScriptedDice(
            d20: new[] {
                3, 18,   // initiative — enemy first
                20,       // enemy crit on player
            },
            diceOnly: new[] { 5, 5 }); // crit damage

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.PlayerDied, outcome);
        Assert.Equal(0, state.Player.Stats.Hp);
    }

    [Fact]
    public void Flee_without_adjacent_enemy_succeeds_no_damage()
    {
        var (state, enemy) = BuildEngagement(playerAt: new Position(2, 2));
        // Move enemy away to break adjacency.
        enemy.Position = new Position(8, 2);

        var dice = new ScriptedDice(d20: new[] { 15, 5 });
        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.FleeRequested = true;
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.PlayerFled, outcome);
        Assert.Equal(state.Player.Stats.MaxHp, state.Player.Stats.Hp);
    }

    [Fact]
    public void Flee_with_adjacent_enemy_triggers_aoo()
    {
        var (state, enemy) = BuildEngagement(playerAt: new Position(2, 2));
        enemy.Position = new Position(3, 2); // adjacent

        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,    // initiative
                15,        // AoO hit
            },
            diceOnly: new[] { 4 });

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.FleeRequested = true;
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.PlayerFled, outcome);
        Assert.True(state.Player.Stats.Hp < state.Player.Stats.MaxHp); // ate the AoO
    }

    [Fact]
    public void AoO_can_kill_the_fleeing_player()
    {
        var (state, enemy) = BuildEngagement(playerHp: 2, playerAt: new Position(2, 2));
        enemy.Position = new Position(3, 2);

        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,
                20, // AoO crit
            },
            diceOnly: new[] { 6, 6 });

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.FleeRequested = true;
        var outcome = svc.ProcessNextRound(state, dice);

        Assert.Equal(CombatOutcome.PlayerDied, outcome);
    }

    [Fact]
    public void Finalize_sets_outcome_and_mode_for_each_branch()
    {
        var svc = new CombatService();

        // Finalize takes Dice (used for loot drop on Win); the Flee/Died paths
        // don't consume rolls but still need a Dice instance.
        var lootDice = new ScriptedDice(d20: new[] { 1 }); // forces HealingDraught (≤10)

        var s1 = StartWithMode(svc);
        svc.Finalize(s1.state, CombatOutcome.PlayerWon, lootDice);
        Assert.Equal(GameMode.Exploration, s1.state.Session.Mode);
        Assert.Equal(CombatOutcome.PlayerWon, s1.state.ActiveCombat!.Log.Outcome);

        var s2 = StartWithMode(svc);
        svc.Finalize(s2.state, CombatOutcome.PlayerFled, new Dice(0));
        Assert.Equal(GameMode.Exploration, s2.state.Session.Mode);

        var s3 = StartWithMode(svc);
        svc.Finalize(s3.state, CombatOutcome.PlayerDied, new Dice(0));
        Assert.Equal(GameMode.Resolution, s3.state.Session.Mode);
    }

    private static (SessionState state, Entity enemy) StartWithMode(CombatService svc)
    {
        var x = BuildEngagement();
        svc.Start(x.state, x.enemy, new ScriptedDice(d20: new[] { 15, 5 }));
        return x;
    }

    private static (SessionState state, Entity enemy) BuildEngagement(
        int playerHp = 20,
        int enemyHp = 8,
        Position? playerAt = null)
    {
        var pos = playerAt ?? new Position(2, 2);
        const int width = 12, height = 6;
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);

        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            Width = width,
            Height = height,
            TileGrid = grid
        };

        var session = new Session
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            FloorId = floor.Id,
            Mode = GameMode.Exploration,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var player = new Player
        {
            Id = session.PlayerId,
            SessionId = session.Id,
            Position = pos,
            Stats = new EntityStats
            {
                Hp = playerHp,
                MaxHp = playerHp,
                Ac = 12,
                AttackMod = 2,
                Damage = new DiceRoll(1, 6, 1),
                InitiativeMod = 0,
                Speed = 30,
                SightRadius = 5
            },
            FogOfWar = new VisibilityState[width, height]
        };

        var enemy = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Enemy,
            Name = "Husk",
            Position = new Position(pos.X + 1, pos.Y),
            State = EntityState.Alive,
            Stats = new EntityStats
            {
                Hp = enemyHp,
                MaxHp = enemyHp,
                Ac = 11,
                AttackMod = 2,
                Damage = new DiceRoll(1, 6, 0),
                InitiativeMod = 0,
                Speed = 25,
                SightRadius = 4
            }
        };
        floor.Entities.Add(enemy);

        return (new SessionState(session, floor, player), enemy);
    }
}

internal class ScriptedDice : Dice
{
    private readonly Queue<int> _d20;
    private readonly Queue<int> _diceOnly;

    public ScriptedDice(int[] d20, int[]? diceOnly = null) : base(0)
    {
        _d20 = new Queue<int>(d20);
        _diceOnly = new Queue<int>(diceOnly ?? Array.Empty<int>());
    }

    public override int D20() => _d20.Dequeue();
    public override int Roll(DiceRoll d) => _diceOnly.Dequeue() + d.Modifier;
    public override int RollDiceOnly(DiceRoll d) => _diceOnly.Dequeue();
}
