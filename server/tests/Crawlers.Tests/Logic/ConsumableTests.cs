using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Xunit;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Step 4 — locks the new consumable effects + the inventory capacity
/// rule. Heal is already covered by ItemTests; these focus on the
/// permanent stat bumps and the cap-overflow behavior on pickup.
/// </summary>
public class ConsumableTests
{
    [Fact]
    public void Greater_healing_potion_restores_15_hp()
    {
        var (state, _) = CombatTestFactory.BuildEngagement(playerHp: 20);
        // Knock the player down so the heal has room to work.
        state.PrimaryPlayer.Stats = state.PrimaryPlayer.Stats with { Hp = 5 };

        var msg = ItemUseHelper.Apply(state.PrimaryPlayer, ItemTemplates.GreaterHealingPotion());

        Assert.Equal(20, state.PrimaryPlayer.Stats.Hp);
        Assert.NotNull(msg);
        Assert.Contains("Greater Healing Potion", msg);
        Assert.Contains("15 HP", msg);
    }

    [Fact]
    public void Strength_tonic_permanently_bumps_attack_mod()
    {
        var (state, _) = CombatTestFactory.BuildEngagement();
        var p = state.PrimaryPlayer;
        var atkBefore = p.Stats.AttackMod;

        ItemUseHelper.Apply(p, ItemTemplates.StrengthTonic());

        Assert.Equal(atkBefore + 1, p.Stats.AttackMod);
    }

    [Fact]
    public void Quickness_vial_permanently_bumps_initiative_mod()
    {
        var (state, _) = CombatTestFactory.BuildEngagement();
        var p = state.PrimaryPlayer;
        var initBefore = p.Stats.InitiativeMod;

        ItemUseHelper.Apply(p, ItemTemplates.QuicknessVial());

        Assert.Equal(initBefore + 1, p.Stats.InitiativeMod);
    }

    [Fact]
    public void Pickup_is_rejected_when_inventory_full_and_item_stays_on_floor()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var p = state.PrimaryPlayer;
        var floor = state.GetFloorFor(p);

        // Fill the inventory to the cap (4 slots). Use distinct item ids
        // so the pickup loop sees them as separate entries.
        for (int i = 0; i < 4; i++) p.Inventory.Add(ItemTemplates.HealingDraught());
        Assert.Equal(4, p.Inventory.Count);

        // Drop a 5th potion at the player's east tile.
        var fifth = ItemTemplates.HealingDraught();
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Item,
            Name = fifth.Name,
            Position = new Position(3, 2),
            State = EntityState.Alive,
            Item = fifth
        });

        new MovementService().TryMove(state, p.Id, MoveDirection.East);

        // Inventory still at cap, item still on the floor.
        Assert.Equal(4, p.Inventory.Count);
        Assert.Contains(floor.Entities, e => e.Type == EntityType.Item && e.Position.Equals(new Position(3, 2)));
    }

    [Fact]
    public void Weapon_pickup_ignores_inventory_cap()
    {
        // Weapons replace the equipped slot, not inventory. A full
        // inventory must NOT block a weapon swap.
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var p = state.PrimaryPlayer;
        var floor = state.GetFloorFor(p);
        for (int i = 0; i < 4; i++) p.Inventory.Add(ItemTemplates.HealingDraught());

        var axeWeapon = new WeaponBlock(new DiceRoll(1, 10, 3), -2);
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Item,
            Name = "Axe",
            Position = new Position(3, 2),
            State = EntityState.Alive,
            Item = new Item { Id = Guid.NewGuid(), Name = "Axe", Weapon = axeWeapon }
        });

        new MovementService().TryMove(state, p.Id, MoveDirection.East);

        Assert.Equal(axeWeapon, p.EquippedWeapon);
        Assert.Equal("Axe", p.EquippedWeaponName);
        Assert.Equal(4, p.Inventory.Count); // unchanged
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Item);
    }
}
