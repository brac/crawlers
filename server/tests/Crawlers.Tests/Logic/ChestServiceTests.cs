using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation.Scaling;
using Crawlers.Generation.Weapons;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Locks the contracts for Step 3.3 chest opening:
///   - validates adjacency (Chebyshev ≤ 1) and rejects far reaches,
///   - flips <see cref="Entity.IsOpen"/> = true on success,
///   - rejects double-open attempts,
///   - drops a placeholder loot item on a Standard chest open,
///   - leaves Empty / Mimic state-only (combat hookup is Step 3.6).
/// </summary>
public class ChestServiceTests
{
    private static (SessionState state, Entity chest) BuildChestScene(
        ChestKind kind,
        Position playerAt,
        Position chestAt)
    {
        var (state, _) = CombatTestFactory.BuildEngagement(playerAt: playerAt);
        // BuildEngagement seeds an enemy adjacent to the player; clear it
        // out so chest tests stay focused on chest mechanics.
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        floor.Entities.Clear();

        var chest = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Chest,
            Name = "Chest",
            Position = chestAt,
            State = EntityState.Alive,
            ChestKind = kind
        };
        floor.Entities.Add(chest);
        return (state, chest);
    }

    [Fact]
    public void Adjacent_Standard_open_marks_chest_opened()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        // Force gold path — simplest deterministic state-only assertion.
        var result = new ChestService(goldChanceOutOfTen: 10)
            .TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Opened, result);
        Assert.True(chest.IsOpen);
    }

    [Fact]
    public void Standard_open_gold_path_credits_player_and_drops_no_floor_item()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        var goldBefore = state.PrimaryPlayer.Gold;
        new ChestService(goldChanceOutOfTen: 10) // always gold
            .TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.True(state.PrimaryPlayer.Gold > goldBefore,
            "Gold path should increment the player's gold counter.");
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Item);
    }

    [Fact]
    public void Empty_open_marks_opened_with_no_loot()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Empty,
            playerAt: new Position(3, 3),
            chestAt: new Position(3, 3));

        var result = new ChestService().TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Opened, result);
        Assert.True(chest.IsOpen);

        var floor = state.GetFloorFor(state.PrimaryPlayer);
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Item);
    }

    [Fact]
    public void Mimic_open_replaces_chest_with_mimic_enemy()
    {
        // Step 3.6 — Mimic chest is removed and a Mimic enemy spawns at
        // the same tile. (Combat itself starts via the engagement check
        // in GameHub.Move on the next tick, not here.)
        var (state, chest) = BuildChestScene(
            ChestKind.Mimic,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        // ScriptedDice nat-1 → AoO misses, no damage, simpler assertions.
        var dice = new ScriptedDice(d20: new[] { 1 });
        var result = new ChestService(dice: dice).TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Opened, result);
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        Assert.DoesNotContain(floor.Entities, e => e.Type == EntityType.Chest);

        var mimic = floor.Entities.SingleOrDefault(e => e.Type == EntityType.Enemy);
        Assert.NotNull(mimic);
        Assert.Equal("Mimic", mimic!.Name);
        Assert.Equal(chest.Position, mimic.Position);
    }

    [Fact]
    public void Mimic_open_AoO_hit_damages_opener()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Mimic,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));
        var player = state.PrimaryPlayer;
        var hpBefore = player.Stats.Hp;

        // d20 = 18 → 18 + AttackMod(4) = 22, beats default test AC 12 → hit.
        // diceOnly = 5 → damage = 5 + Modifier(2) = 7.
        var dice = new ScriptedDice(d20: new[] { 18 }, diceOnly: new[] { 5 });
        new ChestService(dice: dice).TryOpen(state, player.Id, chest.Id);

        Assert.Equal(hpBefore - 7, player.Stats.Hp);
    }

    [Fact]
    public void Mimic_open_AoO_miss_leaves_opener_unhurt()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Mimic,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));
        var player = state.PrimaryPlayer;
        var hpBefore = player.Stats.Hp;

        // d20 = 2 → 2 + 4 = 6 < AC 12 → miss.
        var dice = new ScriptedDice(d20: new[] { 2 });
        new ChestService(dice: dice).TryOpen(state, player.Id, chest.Id);

        Assert.Equal(hpBefore, player.Stats.Hp);
    }

    [Fact]
    public void Open_from_too_far_is_rejected()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(2, 2),
            chestAt: new Position(7, 2));

        var result = new ChestService().TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Rejected, result);
        Assert.False(chest.IsOpen);
    }

    [Fact]
    public void Diagonal_adjacency_is_allowed()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 4));

        var result = new ChestService().TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Opened, result);
        Assert.True(chest.IsOpen);
    }

    [Fact]
    public void Already_open_chest_is_rejected_on_re_open()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));
        var svc = new ChestService();

        Assert.Equal(ChestService.OpenResult.Opened,
            svc.TryOpen(state, state.PrimaryPlayer.Id, chest.Id));
        Assert.Equal(ChestService.OpenResult.Rejected,
            svc.TryOpen(state, state.PrimaryPlayer.Id, chest.Id));
    }

    [Fact]
    public void Open_unknown_entity_is_rejected()
    {
        var (state, _) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        var result = new ChestService().TryOpen(
            state, state.PrimaryPlayer.Id, Guid.NewGuid());

        Assert.Equal(ChestService.OpenResult.Rejected, result);
    }

    // Step 3.4 — weapons drop on the chest tile so the loot visually
    // emerges from the chest. MovementService runs PickupItemsAt before
    // OpenChestsAt, so the just-dropped weapon stays put until the
    // player steps off and back.
    [Fact]
    public void Standard_open_weapon_path_drops_weapon_on_chest_tile()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        var weapons = new WeaponRegistry(new[]
        {
            new WeaponDefinition("Test Blade", new DiceRoll(1, 8, 1), 0)
        });
        var scaling = new FloorScalingTable(new[]
        {
            FloorScaling.Identity(1, 7) with
            {
                FloorNumber = 1,
                WeaponLoot = new[] { new WeaponLootEntry("Test Blade", 1) }
            }
        });

        // goldChanceOutOfTen: 0 → always weapon path.
        var svc = new ChestService(scaling, weapons, new Random(0), goldChanceOutOfTen: 0);
        var result = svc.TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        Assert.Equal(ChestService.OpenResult.Opened, result);

        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var loot = floor.Entities
            .Where(e => e.Type == EntityType.Item)
            .ToList();
        Assert.Single(loot);
        Assert.Equal("Test Blade", loot[0].Name);
        Assert.Equal(chest.Position, loot[0].Position); // weapon sits on the chest
        var weapon = loot[0].Item?.Weapon;
        Assert.NotNull(weapon);
        Assert.Equal(8, weapon!.Damage.Sides);
        Assert.Equal(1, weapon.Damage.Modifier);
        Assert.Equal(0, weapon.InitiativeMod);
    }

    [Fact]
    public void Standard_open_weapon_path_without_pool_falls_back_to_healing_draught()
    {
        var (state, chest) = BuildChestScene(
            ChestKind.Standard,
            playerAt: new Position(3, 3),
            chestAt: new Position(4, 3));

        // Identity scaling table → every floor has WeaponLoot = null.
        // Force weapon path so the gold roll doesn't short-circuit.
        var svc = new ChestService(goldChanceOutOfTen: 0);
        svc.TryOpen(state, state.PrimaryPlayer.Id, chest.Id);

        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var loot = floor.Entities.Single(e => e.Type == EntityType.Item);
        Assert.Equal("Healing Draught", loot.Name);
        Assert.Equal(chest.Position, loot.Position);
        Assert.NotNull(loot.Item);
        Assert.True(loot.Item!.IsConsumable);
        Assert.Null(loot.Item.Weapon);
    }
}
