using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

public class ItemTests
{
    [Fact]
    public void Bone_charm_adds_to_player_attack_total()
    {
        // Player rolls a marginal attack (10 + atkMod 2 = 12). Enemy AC = 13.
        // Without charm: miss. With charm (+1): hit.
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 30, enemyHp: 30);
        enemy.Stats = enemy.Stats! with { Ac = 13 };

        var noCharmDice = new ScriptedDice(
            d20: new[] {
                15, 5,   // initiative — player first
                10,       // player attack roll: 10 + 2 + 0 = 12 → miss
                3,        // enemy attack — miss
            },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        svc.Start(state, enemy, noCharmDice);
        svc.ProcessNextRound(state, noCharmDice);
        Assert.Equal(30, enemy.Stats.Hp); // missed

        // Now equip Bone Charm and re-run on a fresh state.
        var (state2, enemy2) = CombatTestFactory.BuildEngagement(playerHp: 30, enemyHp: 30);
        enemy2.Stats = enemy2.Stats! with { Ac = 13 };
        state2.Player.Inventory.Add(ItemTemplates.BoneCharm());

        var charmDice = new ScriptedDice(
            d20: new[] {
                15, 5,
                10,        // 10 + 2 + 1(charm) = 13 → hit
                3,
            },
            diceOnly: new[] { 4 });

        svc.Start(state2, enemy2, charmDice);
        svc.ProcessNextRound(state2, charmDice);
        Assert.Equal(30 - (4 + 1), enemy2.Stats.Hp); // 4 dmg + 1 mod
    }

    [Fact]
    public void Heal_item_restores_hp_capped_at_max()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 20);
        state.Player.Stats = state.Player.Stats with { Hp = 8 };

        var draught = ItemTemplates.HealingDraught();
        state.Player.Inventory.Add(draught);

        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,    // initiative — player first, so player goes; uses item
                3,         // enemy attack — miss
            },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.UseItemRequested = draught.Id;
        svc.ProcessNextRound(state, dice);

        Assert.Equal(14, state.Player.Stats.Hp); // 8 + 6 heal
        Assert.Empty(state.Player.Inventory); // consumed
    }

    [Fact]
    public void Heal_does_not_overheal()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 20);
        state.Player.Stats = state.Player.Stats with { Hp = 18 };
        state.Player.Inventory.Add(ItemTemplates.HealingDraught());

        var dice = new ScriptedDice(d20: new[] { 15, 5, 3 });
        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.UseItemRequested = state.Player.Inventory[0].Id;
        svc.ProcessNextRound(state, dice);

        Assert.Equal(20, state.Player.Stats.Hp); // capped
    }

    [Fact]
    public void Use_item_skips_player_attack_but_enemy_still_acts()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 30, enemyHp: 30);
        var draught = ItemTemplates.HealingDraught();
        state.Player.Inventory.Add(draught);

        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,    // initiative — player first
                15,        // enemy attack — hit
            },
            diceOnly: new[] { 3 });

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        state.ActiveCombat!.UseItemRequested = draught.Id;
        svc.ProcessNextRound(state, dice);

        // Enemy still attacked (player took 3 damage); enemy HP unchanged.
        Assert.Equal(30, enemy.Stats!.Hp);
        Assert.Equal(30 - 3, state.Player.Stats.Hp);
    }

    [Fact]
    public void Loot_drop_creates_item_entity_at_corpse_tile()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(enemyHp: 1, playerAt: new Position(2, 2));
        var dice = new ScriptedDice(
            d20: new[] {
                15, 5,    // initiative
                15,        // player kills the enemy
                12,        // loot drop — 9-16 → HealingDraught
            },
            diceOnly: new[] { 2 });

        var svc = new CombatService();
        svc.Start(state, enemy, dice);
        var outcome = svc.ProcessNextRound(state, dice);
        Assert.Equal(CombatOutcome.PlayerWon, outcome);

        svc.Finalize(state, outcome, dice);

        var loot = state.Floor.Entities.FirstOrDefault(e => e.Type == EntityType.Item);
        Assert.NotNull(loot);
        Assert.Equal(enemy.Position, loot!.Position);
        Assert.Equal("Healing Draught", loot.Item!.Name);
    }

    [Fact]
    public void ItemUseHelper_heals_capped_at_max()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        state.Player.Stats = state.Player.Stats with { Hp = 18, MaxHp = 20 };
        var draught = ItemTemplates.HealingDraught();

        var msg = ItemUseHelper.Apply(state, draught);

        Assert.Equal(20, state.Player.Stats.Hp);
        Assert.NotNull(msg);
        Assert.Contains("recover 2 HP", msg);
    }

    [Fact]
    public void Move_picks_up_item_at_target_tile()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        // Place a healing draught one tile east of the player.
        var item = ItemTemplates.BoneCharm();
        state.Floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = state.Floor.Id,
            Type = EntityType.Item,
            Name = item.Name,
            Position = new Position(3, 2),
            State = EntityState.Alive,
            Item = item
        });

        var ok = new MovementService().TryMove(state, MoveDirection.East);

        Assert.True(ok);
        Assert.DoesNotContain(state.Floor.Entities, e => e.Type == EntityType.Item);
        Assert.Single(state.Player.Inventory);
        Assert.Equal("Bone Charm", state.Player.Inventory[0].Name);
    }
}

internal static class CombatTestFactory
{
    public static (SessionState state, Entity enemy) BuildEngagement(
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

    public static SessionState BuildExploration(Position playerAt)
    {
        var (state, _) = BuildEngagement(playerAt: playerAt);
        // Strip the engagement enemy so movement isn't blocked by anything.
        state.Floor.Entities.Clear();
        return state;
    }
}
