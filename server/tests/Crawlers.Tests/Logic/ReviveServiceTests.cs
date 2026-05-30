using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Multiplayer revive — locks the spec contract:
///   - reviver pays max(1, floor(currentHp * 0.20)) HP
///   - revived teammate comes back with that same amount
///   - 1-HP safety floor: if the tax would drop reviver below 1, both
///     end at 1 HP instead
///   - reviver must have HP > 1
///   - target must be Mode == Resolution AND still connected
///   - target must be on same floor with a corpse Entity adjacent to
///     the reviver
///   - corpse Entity is removed; revived teammate's StatusEffects /
///     DiedAt / CauseOfDeath / SpectatorTargetId all cleared
/// </summary>
public class ReviveServiceTests
{
    private static (SessionState state, Player live, Player dead, Entity corpse) BuildScene(
        int liveHp = 50,
        int deadMaxHp = 50,
        Position? livePos = null,
        Position? corpsePos = null,
        bool deadConnected = true,
        GameMode deadMode = GameMode.Resolution)
    {
        var (state, _) = CombatTestFactory.BuildEngagement(playerHp: liveHp);
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        floor.Entities.Clear();

        var live = state.PrimaryPlayer;
        live.Position = livePos ?? new Position(3, 3);

        // Add a second player as the dead teammate.
        var dead = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Username = "Fallen",
            Position = corpsePos ?? new Position(4, 3),
            Stats = new EntityStats { Hp = 0, MaxHp = deadMaxHp, Ac = 12, AttackMod = 2,
                                      Damage = new DiceRoll(1, 6, 1), InitiativeMod = 0,
                                      Speed = 30, SightRadius = 5 },
            CurrentFloorNumber = live.CurrentFloorNumber,
            Mode = deadMode,
            DiedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CauseOfDeath = "Slain by a Husk",
            SpectatorTargetId = live.Id
        };
        dead.StatusEffects.Add(new StatusEffect(StatusEffectKind.Bleed, 2, 1));
        state.AddPlayer(dead);

        if (deadConnected)
            state.SetConnection(dead.Id, $"conn-{dead.Id}");

        var corpse = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Corpse,
            Name = "Corpse",
            Position = dead.Position,
            State = EntityState.Alive,
            PlayerId = dead.Id,
            DiedAt = dead.DiedAt,
            Username = dead.Username
        };
        floor.Entities.Add(corpse);

        return (state, live, dead, corpse);
    }

    [Fact]
    public void Standard_revive_pays_20pct_and_brings_teammate_back()
    {
        var (state, live, dead, corpse) = BuildScene(liveHp: 50, deadMaxHp: 50);

        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);

        Assert.Equal(ReviveService.ReviveResult.Success, result);
        // Tax = floor(50 * 0.20) = 10. Live = 40, revived = 10.
        Assert.Equal(40, live.Stats.Hp);
        Assert.Equal(10, dead.Stats.Hp);
        Assert.Equal(GameMode.Exploration, dead.Mode);
        Assert.Null(dead.DiedAt);
        Assert.Null(dead.CauseOfDeath);
        Assert.Null(dead.SpectatorTargetId);
        Assert.Empty(dead.StatusEffects);
        // Revived teammate stands on the corpse tile.
        Assert.Equal(corpse.Position, dead.Position);
        // Corpse entity is consumed.
        Assert.DoesNotContain(corpse, state.GetFloorFor(live).Entities);
    }

    [Fact]
    public void Tax_floors_at_one_even_for_low_hp_reviver()
    {
        // Live player at 5 HP — 20% = 1, after tax = 4. Revived gets 1.
        var (state, live, dead, _) = BuildScene(liveHp: 5);
        new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(4, live.Stats.Hp);
        Assert.Equal(1, dead.Stats.Hp);
    }

    [Fact]
    public void Reviver_at_1_HP_is_rejected()
    {
        // The "more than 1 HP" rule — a 1-HP player can't pay any tax.
        var (state, live, dead, _) = BuildScene(liveHp: 1);
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
        Assert.Equal(GameMode.Resolution, dead.Mode);
    }

    [Fact]
    public void Reviver_at_2_HP_lands_both_on_one_via_safety_floor()
    {
        // 2 HP: tax = max(1, floor(0.4)) = 1. Live - tax = 1, which
        // doesn't trigger the safety floor — clean transaction.
        var (state, live, dead, _) = BuildScene(liveHp: 2);
        new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(1, live.Stats.Hp);
        Assert.Equal(1, dead.Stats.Hp);
    }

    [Fact]
    public void Distant_corpse_is_rejected()
    {
        // Live at (3,3), corpse at (8,3) → Chebyshev 5, far out of range.
        var (state, live, dead, _) = BuildScene(
            livePos: new Position(3, 3),
            corpsePos: new Position(8, 3));
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
    }

    [Fact]
    public void Diagonal_adjacency_is_allowed()
    {
        var (state, live, dead, _) = BuildScene(
            livePos: new Position(3, 3),
            corpsePos: new Position(4, 4));
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Success, result);
    }

    [Fact]
    public void Disconnected_dead_teammate_is_rejected()
    {
        var (state, live, dead, _) = BuildScene(deadConnected: false);
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
        Assert.Equal(GameMode.Resolution, dead.Mode);
    }

    [Fact]
    public void Already_alive_target_is_rejected()
    {
        var (state, live, dead, _) = BuildScene(deadMode: GameMode.Exploration);
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
    }

    [Fact]
    public void Closest_corpse_wins_when_multiple_share_a_player_id()
    {
        // Persistent-corpse hydration can leave a past-life corpse on
        // the same floor with the same PlayerId. The reviver standing
        // next to the FRESH corpse should consume that one — not the
        // distant past-life corpse.
        var (state, live, dead, fresh) = BuildScene(
            livePos: new Position(3, 3),
            corpsePos: new Position(4, 3));
        var floor = state.GetFloorFor(live);
        var distant = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Corpse,
            Name = "Corpse",
            Position = new Position(10, 10),
            State = EntityState.Alive,
            PlayerId = dead.Id
        };
        floor.Entities.Add(distant);

        new ReviveService().TryRevive(state, live.Id, dead.Id);

        Assert.DoesNotContain(fresh, floor.Entities);   // consumed
        Assert.Contains(distant, floor.Entities);        // untouched
    }

    [Fact]
    public void Reviver_in_combat_is_rejected()
    {
        var (state, live, dead, _) = BuildScene();
        live.Mode = GameMode.Combat;
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
    }

    [Fact]
    public void Cross_floor_revive_is_rejected()
    {
        var (state, live, dead, _) = BuildScene();
        dead.CurrentFloorNumber = live.CurrentFloorNumber + 1;
        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);
        Assert.Equal(ReviveService.ReviveResult.Rejected, result);
    }

    [Fact]
    public void Revive_does_not_place_teammate_on_an_occupied_tile()
    {
        // Reviver stands ON the corpse tile (Chebyshev 0 is valid adjacency).
        // The revived teammate must be relocated to a free tile rather than
        // stacked on top of the living reviver.
        var (state, live, dead, _) = BuildScene(
            livePos: new Position(4, 3),
            corpsePos: new Position(4, 3));

        var result = new ReviveService().TryRevive(state, live.Id, dead.Id);

        Assert.Equal(ReviveService.ReviveResult.Success, result);
        Assert.NotEqual(live.Position, dead.Position);
    }

    [Fact]
    public void Revive_lands_on_corpse_tile_when_it_is_clear()
    {
        // The common case: corpse tile is free, so the revived teammate stands
        // exactly where they fell.
        var (state, live, dead, corpse) = BuildScene(
            livePos: new Position(3, 3),
            corpsePos: new Position(4, 3));

        new ReviveService().TryRevive(state, live.Id, dead.Id);

        Assert.Equal(corpse.Position, dead.Position);
    }
}
