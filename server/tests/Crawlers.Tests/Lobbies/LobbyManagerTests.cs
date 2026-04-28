using Crawlers.Domain.Enums;
using Crawlers.Server.Lobbies;

namespace Crawlers.Tests.Lobbies;

public class LobbyManagerTests
{
    [Fact]
    public void CreateLobby_returns_state_with_host_as_sole_member()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();

        var state = mgr.CreateLobby(host, "Brac", "conn-host");

        Assert.Equal(host, state.Room.HostPlayerId);
        Assert.Equal(LobbyStatus.Waiting, state.Room.Status);
        Assert.Equal(LobbyManager.DefaultMaxPlayers, state.Room.MaxPlayers);
        Assert.Single(state.Room.Members);
        Assert.Equal(host, state.Room.Members[0].PlayerId);
        Assert.Equal("Brac", state.Room.Members[0].Username);
        Assert.Equal("conn-host", state.Room.Members[0].ConnectionId);
        Assert.Equal(LobbyCodeGenerator.CodeLength, state.Room.Code.Length);
        Assert.Equal(state.Room.Code, LobbyCodeGenerator.Normalize(state.Room.Code));
    }

    [Fact]
    public void JoinByCode_persists_username_on_member_record()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "Host", "conn-host");
        var joiner = Guid.NewGuid();

        var outcome = mgr.JoinByCode(state.Room.Code, joiner, "Joiner", "conn-joiner");

        Assert.Equal(LobbyJoinResult.Success, outcome.Result);
        Assert.Equal(2, state.Room.Members.Count);
        Assert.Equal("Joiner", state.Room.Members[1].Username);
    }

    [Fact]
    public void CreateLobby_assigns_unique_codes_and_ids()
    {
        var mgr = new LobbyManager(new Random(1));

        var a = mgr.CreateLobby(Guid.NewGuid(), "name", "c1");
        var b = mgr.CreateLobby(Guid.NewGuid(), "name", "c2");

        Assert.NotEqual(a.Room.Id, b.Room.Id);
        Assert.NotEqual(a.Room.Code, b.Room.Code);
        Assert.Equal(2, mgr.ActiveCount);
    }

    [Fact]
    public void CreateLobby_throws_when_player_already_in_a_lobby()
    {
        var mgr = new LobbyManager(new Random(1));
        var p = Guid.NewGuid();
        mgr.CreateLobby(p, "name", "c1");

        Assert.Throws<InvalidOperationException>(() => mgr.CreateLobby(p, "name", "c2"));
    }

    [Fact]
    public void GetByCode_finds_lobby_case_insensitively()
    {
        var mgr = new LobbyManager(new Random(1));
        var state = mgr.CreateLobby(Guid.NewGuid(), "name", "c1");

        var found = mgr.GetByCode(state.Room.Code.ToLowerInvariant());

        Assert.Same(state, found);
    }

    [Fact]
    public void GetByCode_returns_null_for_unknown_or_invalid_codes()
    {
        var mgr = new LobbyManager(new Random(1));
        Assert.Null(mgr.GetByCode("ZZZZZZ"));
        Assert.Null(mgr.GetByCode(""));
        Assert.Null(mgr.GetByCode("nope"));
    }

    [Fact]
    public void JoinByCode_adds_member_and_reports_success()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        var joiner = Guid.NewGuid();

        var outcome = mgr.JoinByCode(state.Room.Code, joiner, "name", "c2");

        Assert.Equal(LobbyJoinResult.Success, outcome.Result);
        Assert.Same(state, outcome.State);
        Assert.Equal(2, state.Room.Members.Count);
        Assert.Equal(joiner, state.Room.Members[1].PlayerId);
        Assert.Same(state, mgr.GetByPlayer(joiner));
    }

    [Fact]
    public void JoinByCode_returns_NotFound_for_unknown_code()
    {
        var mgr = new LobbyManager(new Random(1));
        var outcome = mgr.JoinByCode("ZZZZZZ", Guid.NewGuid(), "name", "c1");
        Assert.Equal(LobbyJoinResult.NotFound, outcome.Result);
        Assert.Null(outcome.State);
    }

    [Fact]
    public void JoinByCode_returns_Full_when_lobby_at_max_capacity()
    {
        var mgr = new LobbyManager(new Random(1));
        var state = mgr.CreateLobby(Guid.NewGuid(), "name", "c1");
        for (int i = 1; i < LobbyManager.DefaultMaxPlayers; i++)
            Assert.Equal(LobbyJoinResult.Success,
                mgr.JoinByCode(state.Room.Code, Guid.NewGuid(), "name", $"c{i + 1}").Result);

        var overflow = mgr.JoinByCode(state.Room.Code, Guid.NewGuid(), "name", "extra");

        Assert.Equal(LobbyJoinResult.Full, overflow.Result);
        Assert.Null(overflow.State);
        Assert.Equal(LobbyManager.DefaultMaxPlayers, state.Room.Members.Count);
    }

    [Fact]
    public void JoinByCode_returns_AlreadyStarted_when_status_is_InGame()
    {
        var mgr = new LobbyManager(new Random(1));
        var state = mgr.CreateLobby(Guid.NewGuid(), "name", "c1");
        state.Room.Status = LobbyStatus.InGame;

        var outcome = mgr.JoinByCode(state.Room.Code, Guid.NewGuid(), "name", "c2");

        Assert.Equal(LobbyJoinResult.AlreadyStarted, outcome.Result);
    }

    [Fact]
    public void JoinByCode_returns_AlreadyInLobby_when_player_is_already_a_member()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");

        var outcome = mgr.JoinByCode(state.Room.Code, host, "name", "c1");

        Assert.Equal(LobbyJoinResult.AlreadyInLobby, outcome.Result);
        Assert.Single(state.Room.Members);
    }

    [Fact]
    public void Leave_removes_member_and_keeps_lobby_when_others_remain()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        mgr.JoinByCode(state.Room.Code, joiner, "name", "c2");

        var outcome = mgr.Leave(joiner);

        Assert.Equal(LobbyLeaveResult.MemberLeft, outcome.Result);
        Assert.Same(state, outcome.State);
        Assert.Single(state.Room.Members);
        Assert.Null(mgr.GetByPlayer(joiner));
    }

    [Fact]
    public void Leave_promotes_earliest_remaining_member_to_host()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        mgr.JoinByCode(state.Room.Code, second, "name", "c2");
        mgr.JoinByCode(state.Room.Code, third, "name", "c3");

        mgr.Leave(host);

        Assert.Equal(second, state.Room.HostPlayerId);
        Assert.Equal(2, state.Room.Members.Count);
    }

    [Fact]
    public void Leave_disposes_lobby_when_last_member_leaves()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        var code = state.Room.Code;

        var outcome = mgr.Leave(host);

        Assert.Equal(LobbyLeaveResult.LobbyDisposed, outcome.Result);
        Assert.Null(outcome.State);
        Assert.True(state.Disposed);
        Assert.Equal(0, mgr.ActiveCount);
        Assert.Null(mgr.Get(state.Room.Id));
        Assert.Null(mgr.GetByCode(code));
        Assert.Null(mgr.GetByPlayer(host));
    }

    [Fact]
    public void Leave_keeps_InGame_lobby_alive_when_host_disconnects_post_start()
    {
        // The host's /lobby connection drops as a normal side effect of moving
        // to /game after StartGame. We don't want that to nuke the lobby record
        // — late joiners should see "AlreadyStarted" instead of "NotFound".
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        var code = state.Room.Code;
        Assert.Equal(LobbyStartResult.Success, mgr.StartGame(state.Room.Id, host).Result);

        var outcome = mgr.Leave(host);

        Assert.Equal(LobbyLeaveResult.MemberLeft, outcome.Result);
        Assert.False(state.Disposed);
        Assert.Same(state, mgr.Get(state.Room.Id));
        Assert.Same(state, mgr.GetByCode(code));
        // A late joiner now sees AlreadyStarted (clear UX) rather than NotFound.
        var late = mgr.JoinByCode(code, Guid.NewGuid(), "name", "c2");
        Assert.Equal(LobbyJoinResult.AlreadyStarted, late.Result);
    }

    [Fact]
    public void Leave_returns_NotInLobby_for_unknown_player()
    {
        var mgr = new LobbyManager(new Random(1));
        var outcome = mgr.Leave(Guid.NewGuid());
        Assert.Equal(LobbyLeaveResult.NotInLobby, outcome.Result);
        Assert.Null(outcome.State);
    }

    [Fact]
    public void StartGame_flips_status_to_InGame_for_host()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        mgr.JoinByCode(state.Room.Code, Guid.NewGuid(), "name", "c2");

        var outcome = mgr.StartGame(state.Room.Id, host);

        Assert.Equal(LobbyStartResult.Success, outcome.Result);
        Assert.Same(state, outcome.State);
        Assert.Equal(LobbyStatus.InGame, state.Room.Status);
    }

    [Fact]
    public void StartGame_returns_NotHost_when_caller_is_not_host()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        mgr.JoinByCode(state.Room.Code, joiner, "name", "c2");

        var outcome = mgr.StartGame(state.Room.Id, joiner);

        Assert.Equal(LobbyStartResult.NotHost, outcome.Result);
        Assert.Equal(LobbyStatus.Waiting, state.Room.Status);
    }

    [Fact]
    public void StartGame_returns_AlreadyStarted_when_status_is_InGame()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var state = mgr.CreateLobby(host, "name", "c1");
        Assert.Equal(LobbyStartResult.Success, mgr.StartGame(state.Room.Id, host).Result);

        var second = mgr.StartGame(state.Room.Id, host);

        Assert.Equal(LobbyStartResult.AlreadyStarted, second.Result);
    }

    [Fact]
    public void StartGame_returns_NotFound_for_unknown_lobby()
    {
        var mgr = new LobbyManager(new Random(1));
        var outcome = mgr.StartGame(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(LobbyStartResult.NotFound, outcome.Result);
    }

    [Fact]
    public void After_disposal_codes_can_be_reused_by_a_new_lobby()
    {
        var mgr = new LobbyManager(new Random(1));
        var host = Guid.NewGuid();
        var first = mgr.CreateLobby(host, "name", "c1");
        var firstCode = first.Room.Code;
        mgr.Leave(host);

        // New lobby is not the disposed one even if the rng repeats; the manager
        // must have released the code reservation so creation can proceed.
        var second = mgr.CreateLobby(host, "name", "c2");

        Assert.NotEqual(first.Room.Id, second.Room.Id);
        Assert.Same(second, mgr.GetByCode(second.Room.Code));
        // The freed code is no longer pointing at the disposed lobby.
        if (firstCode != second.Room.Code)
            Assert.Null(mgr.GetByCode(firstCode));
    }
}
