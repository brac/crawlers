using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;

namespace Crawlers.Tests.Logic;

public class RunEndServiceTests
{
    [Fact]
    public void CheckAndApply_returns_PartyWiped_when_every_player_is_in_resolution()
    {
        var state = MakeStateWithPlayers(modes: new[] { GameMode.Resolution, GameMode.Resolution });
        var svc = new RunEndService();

        var result = svc.CheckAndApply(state);

        Assert.Equal(RunOutcome.PartyWiped, result);
        Assert.True(state.IsRunOver);
        Assert.Equal(RunOutcome.PartyWiped, state.Outcome);
        Assert.NotNull(state.EndedAt);
    }

    [Fact]
    public void CheckAndApply_returns_null_when_at_least_one_player_is_alive()
    {
        var state = MakeStateWithPlayers(modes: new[] { GameMode.Resolution, GameMode.Exploration });

        var result = new RunEndService().CheckAndApply(state);

        Assert.Null(result);
        Assert.False(state.IsRunOver);
        Assert.Null(state.Outcome);
        Assert.Null(state.EndedAt);
    }

    [Fact]
    public void CheckAndApply_returns_null_when_at_least_one_player_is_in_combat()
    {
        // Combat counts as alive — the runner ticks on, eventually flipping
        // them to Resolution or Exploration. The wipe-check only fires when
        // *every* mode is Resolution.
        var state = MakeStateWithPlayers(modes: new[] { GameMode.Resolution, GameMode.Combat });

        var result = new RunEndService().CheckAndApply(state);

        Assert.Null(result);
    }

    [Fact]
    public void CheckAndApply_is_idempotent_after_run_has_ended()
    {
        var state = MakeStateWithPlayers(modes: new[] { GameMode.Resolution });
        var svc = new RunEndService();
        var first = svc.CheckAndApply(state);
        var endedAt = state.EndedAt;
        Assert.NotNull(endedAt);

        // Second call: outcome already set → returns null (no *new* application).
        var second = svc.CheckAndApply(state);

        Assert.Equal(RunOutcome.PartyWiped, first);
        Assert.Null(second);
        Assert.Equal(endedAt, state.EndedAt);
    }

    [Fact]
    public void CheckAndApply_returns_null_for_empty_session()
    {
        // Defensive: a session with no players hasn't started; never end it.
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);

        var result = new RunEndService().CheckAndApply(state);

        Assert.Null(result);
        Assert.False(state.IsRunOver);
    }

    [Fact]
    public void Disconnected_alive_player_keeps_run_alive()
    {
        // Spec: "Disconnected players do not count as dead — the run continues
        // for survivors regardless of how many have disconnected." Mode, not
        // connection presence, drives the wipe check.
        var state = MakeStateWithPlayers(modes: new[] { GameMode.Resolution, GameMode.Exploration });
        var alivePlayer = state.Players[1];
        // Don't register a connection for the alive player → "disconnected".
        Assert.Null(state.GetConnection(alivePlayer.Id));

        var result = new RunEndService().CheckAndApply(state);

        Assert.Null(result);
        Assert.False(state.IsRunOver);
    }

    [Fact]
    public void Wipe_fires_even_when_no_dead_player_has_a_connection()
    {
        // All four players died, then everyone disconnected. The run is still
        // over — connection presence is not a precondition.
        var state = MakeStateWithPlayers(modes: new[]
        {
            GameMode.Resolution, GameMode.Resolution,
            GameMode.Resolution, GameMode.Resolution
        });
        // No connections registered.
        foreach (var p in state.Players)
            Assert.Null(state.GetConnection(p.Id));

        var result = new RunEndService().CheckAndApply(state);

        Assert.Equal(RunOutcome.PartyWiped, result);
        Assert.True(state.IsRunOver);
    }

    private static SessionState MakeStateWithPlayers(GameMode[] modes)
    {
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        foreach (var mode in modes)
        {
            state.AddPlayer(new Player
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Position = new Position(0, 0),
                Stats = new EntityStats { Hp = 1, MaxHp = 1, Ac = 1, AttackMod = 0, Damage = new DiceRoll(1, 1, 0), SightRadius = 5 },
                CurrentFloorNumber = 1,
                Mode = mode,
                DeepestFloorReached = 1
            });
        }
        return state;
    }
}
