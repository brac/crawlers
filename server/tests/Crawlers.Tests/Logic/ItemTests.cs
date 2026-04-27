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
            d20: new[] { 15, 5, 10, 3 },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, noCharmDice);
        svc.ProcessNextRound(state, combat, noCharmDice);
        Assert.Equal(30, enemy.Stats.Hp);

        // Now equip Bone Charm and re-run on a fresh state.
        var (state2, enemy2) = CombatTestFactory.BuildEngagement(playerHp: 30, enemyHp: 30);
        enemy2.Stats = enemy2.Stats! with { Ac = 13 };
        state2.PrimaryPlayer.Inventory.Add(ItemTemplates.BoneCharm());

        var charmDice = new ScriptedDice(
            d20: new[] { 15, 5, 10, 3 },
            diceOnly: new[] { 4 });

        var combat2 = svc.Start(state2, enemy2, new[] { state2.PrimaryPlayer }, charmDice);
        svc.ProcessNextRound(state2, combat2, charmDice);
        Assert.Equal(30 - (4 + 1), enemy2.Stats.Hp);
    }

    [Fact]
    public void Heal_item_restores_hp_capped_at_max()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 20);
        var pid = state.PrimaryPlayer.Id;
        state.PrimaryPlayer.Stats = state.PrimaryPlayer.Stats with { Hp = 8 };

        var draught = ItemTemplates.HealingDraught();
        state.PrimaryPlayer.Inventory.Add(draught);

        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 3 },
            diceOnly: Array.Empty<int>());

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.UseItemRequested[pid] = draught.Id;
        svc.ProcessNextRound(state, combat, dice);

        Assert.Equal(14, state.PrimaryPlayer.Stats.Hp);
        Assert.Empty(state.PrimaryPlayer.Inventory);
    }

    [Fact]
    public void Heal_does_not_overheal()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 20);
        var pid = state.PrimaryPlayer.Id;
        state.PrimaryPlayer.Stats = state.PrimaryPlayer.Stats with { Hp = 18 };
        state.PrimaryPlayer.Inventory.Add(ItemTemplates.HealingDraught());

        var dice = new ScriptedDice(d20: new[] { 15, 5, 3 });
        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.UseItemRequested[pid] = state.PrimaryPlayer.Inventory[0].Id;
        svc.ProcessNextRound(state, combat, dice);

        Assert.Equal(20, state.PrimaryPlayer.Stats.Hp);
    }

    [Fact]
    public void Use_item_skips_player_attack_but_enemy_still_acts()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 30, enemyHp: 30);
        var pid = state.PrimaryPlayer.Id;
        var draught = ItemTemplates.HealingDraught();
        state.PrimaryPlayer.Inventory.Add(draught);

        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 15 },
            diceOnly: new[] { 3 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        combat.UseItemRequested[pid] = draught.Id;
        svc.ProcessNextRound(state, combat, dice);

        Assert.Equal(30, enemy.Stats!.Hp);
        Assert.Equal(30 - 3, state.PrimaryPlayer.Stats.Hp);
    }

    [Fact]
    public void Loot_drop_creates_item_entity_at_corpse_tile()
    {
        var (state, enemy) = CombatTestFactory.BuildEngagement(enemyHp: 1, playerAt: new Position(2, 2));
        var dice = new ScriptedDice(
            d20: new[] { 15, 5, 15, 12 },
            diceOnly: new[] { 2 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { state.PrimaryPlayer }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);
        Assert.True(result.Ended);
        Assert.Equal(CombatOutcome.PlayerWon, combat.Log.Outcome);

        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var loot = floor.Entities.FirstOrDefault(e => e.Type == EntityType.Item);
        Assert.NotNull(loot);
        Assert.Equal(enemy.Position, loot!.Position);
        Assert.Equal("Healing Draught", loot.Item!.Name);
    }

    [Fact]
    public void ItemUseHelper_heals_capped_at_max()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        state.PrimaryPlayer.Stats = state.PrimaryPlayer.Stats with { Hp = 18, MaxHp = 20 };
        var draught = ItemTemplates.HealingDraught();

        var msg = ItemUseHelper.Apply(state.PrimaryPlayer, draught);

        Assert.Equal(20, state.PrimaryPlayer.Stats.Hp);
        Assert.NotNull(msg);
        Assert.Contains("recovers 2 HP", msg);
    }

    [Fact]
    public void Move_picks_up_item_at_target_tile()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var pid = state.PrimaryPlayer.Id;
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var item = ItemTemplates.BoneCharm();
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Item,
            Name = item.Name,
            Position = new Position(3, 2),
            State = EntityState.Alive,
            Item = item
        });

        var ok = new MovementService().TryMove(state, pid, MoveDirection.East);

        Assert.True(ok);
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Item);
        Assert.Single(state.PrimaryPlayer.Inventory);
        Assert.Equal("Bone Charm", state.PrimaryPlayer.Inventory[0].Name);
    }
}

internal static class CombatTestFactory
{
    public static (SessionState state, Entity enemy) BuildEngagement(
        int playerHp = 20,
        int enemyHp = 8,
        Position? playerAt = null)
        => CombatServiceTests.BuildEngagement(playerHp, enemyHp, playerAt);

    public static SessionState BuildExploration(Position playerAt)
    {
        var (state, _) = BuildEngagement(playerAt: playerAt);
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        floor.Entities.Clear();
        return state;
    }
}
