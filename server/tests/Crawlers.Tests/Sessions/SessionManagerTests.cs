using Crawlers.Domain.Enums;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Sessions;

public class SessionManagerTests
{
    [Fact]
    public void CreateSession_returns_state_with_floor_and_player()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 1);

        Assert.NotNull(state.Floor);
        Assert.NotNull(state.Player);
        Assert.NotEqual(Guid.Empty, state.Session.Id);
        Assert.Equal(state.Session.Id, state.Player.SessionId);
        Assert.Equal(state.Session.PlayerId, state.Player.Id);
        Assert.Equal(state.Floor.Id, state.Session.FloorId);
        Assert.Equal(GameMode.Exploration, state.Session.Mode);
    }

    [Fact]
    public void Player_starts_on_stairs_up_tile()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 7);

        var p = state.Player.Position;
        Assert.Equal(TileType.StairsUp, state.Floor.TileGrid[p.X, p.Y].Type);
    }

    [Fact]
    public void Sessions_have_unique_ids_and_independent_state()
    {
        var mgr = new SessionManager();
        var a = mgr.CreateSession(seed: 1);
        var b = mgr.CreateSession(seed: 2);

        Assert.NotEqual(a.Session.Id, b.Session.Id);
        Assert.NotEqual(a.Player.Id, b.Player.Id);
        Assert.NotEqual(a.Floor.Id, b.Floor.Id);
        Assert.Equal(2, mgr.ActiveCount);
    }

    [Fact]
    public void Get_returns_same_state_instance()
    {
        var mgr = new SessionManager();
        var created = mgr.CreateSession(seed: 1);

        var fetched = mgr.Get(created.Session.Id);
        Assert.Same(created, fetched);
    }

    [Fact]
    public void Get_returns_null_for_unknown_session()
    {
        var mgr = new SessionManager();
        Assert.Null(mgr.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Remove_drops_session_and_count_decreases()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 1);
        Assert.Equal(1, mgr.ActiveCount);

        Assert.True(mgr.Remove(state.Session.Id));
        Assert.Equal(0, mgr.ActiveCount);
        Assert.Null(mgr.Get(state.Session.Id));
    }

    [Fact]
    public void Default_player_stats_have_sight_radius_5_per_spec()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSession(seed: 1);
        Assert.Equal(5, state.Player.Stats.SightRadius);
    }
}
