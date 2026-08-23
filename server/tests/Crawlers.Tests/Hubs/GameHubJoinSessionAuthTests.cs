using System.Text.Json;
using Crawlers.Server.Contracts;
using Crawlers.Server.Hubs;
using Crawlers.Server.Lobbies;
using Crawlers.Server.Logic;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Crawlers.Tests.TestSupport;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crawlers.Tests.Hubs;

/// <summary>
/// Security regression tests for the connection-to-player binding.
///
/// Player ids are public inside a session (every teammate sees them in the
/// lobby roster and in OtherPlayers), so a player id alone can never be the
/// thing that proves who you are. The binding is gated on a server-minted
/// per-player session token that only ever travels to the owning caller.
/// These tests pin that property down from both sides: a replayed player id
/// is rejected without mutating anything, and the legitimate flow still works.
/// </summary>
public class GameHubJoinSessionAuthTests
{
    private sealed class Rig
    {
        public required SessionManager Sessions { get; init; }
        public required FakeGameHubContext HubContext { get; init; }
        public required SessionBroadcaster Broadcaster { get; init; }

        public (GameHub Hub, FakeHubCallerContext Ctx) NewHub(string connectionId)
        {
            var corpses = TestWorld.MakeCorpses();
            var combat = new CombatService();
            var runner = new CombatRunner(
                Broadcaster,
                Sessions,
                combat,
                new RunEndService(),
                new NullRunHistoryService(NullLogger<NullRunHistoryService>.Instance),
                corpses,
                NullLogger<CombatRunner>.Instance);

            var hub = new GameHub(
                Sessions,
                Broadcaster,
                new MovementService(),
                new EngagementService(),
                combat,
                runner,
                new DescendService(TestWorld.Make(), corpses),
                new ReviveService(),
                NullLogger<GameHub>.Instance);

            var ctx = new FakeHubCallerContext(connectionId);
            hub.Context = ctx;
            hub.Clients = HubContext.Recording;
            hub.Groups = HubContext.GroupManager;
            return (hub, ctx);
        }
    }

    private static Rig MakeRig()
    {
        var hubContext = new FakeGameHubContext();
        var broadcaster = new SessionBroadcaster(hubContext);
        var sessions = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        return new Rig { Sessions = sessions, HubContext = hubContext, Broadcaster = broadcaster };
    }

    private static SessionState TwoPlayerSession(SessionManager mgr, out Guid victimId, out Guid attackerId)
    {
        victimId = Guid.NewGuid();
        attackerId = Guid.NewGuid();
        return mgr.CreateSession(new[]
        {
            new PlayerStartState
            {
                PlayerId = victimId,
                Username = "Victim",
                Stats = SessionManager.DefaultPlayerStats()
            },
            new PlayerStartState
            {
                PlayerId = attackerId,
                Username = "Attacker",
                Stats = SessionManager.DefaultPlayerStats()
            }
        });
    }

    [Fact]
    public async Task JoinSession_rejects_a_replayed_player_id_without_that_players_token()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var victimId, out var attackerId);
        var victimToken = state.GetSessionToken(victimId)!;
        var attackerToken = state.GetSessionToken(attackerId)!;

        // The victim binds legitimately.
        var (victimHub, _) = rig.NewHub("victim-conn");
        await victimHub.JoinSession(state.Session.Id, victimId, victimToken);
        Assert.Equal("victim-conn", state.GetConnection(victimId));

        // The attacker knows the victim's player id (it is broadcast to the
        // whole lobby and rides along in every snapshot) but not their token.
        var (attackerHub, attackerCtx) = rig.NewHub("attacker-conn");
        await Assert.ThrowsAsync<HubException>(() =>
            attackerHub.JoinSession(state.Session.Id, victimId, attackerToken));

        // The point of the test: nothing moved. A hijack that throws *after*
        // repointing the connection would still be a hijack.
        Assert.Equal("victim-conn", state.GetConnection(victimId));
        Assert.False(attackerCtx.Items.ContainsKey("playerId"));
        Assert.False(attackerCtx.Items.ContainsKey("sessionId"));

        // And the victim, not the attacker, is still the one being fed.
        rig.HubContext.Recording.For("victim-conn").Snapshots.Clear();
        await rig.Broadcaster.BroadcastAsync(state);
        Assert.NotEmpty(rig.HubContext.Recording.For("victim-conn").Snapshots);
        Assert.Empty(rig.HubContext.Recording.For("attacker-conn").Snapshots);
    }

    [Fact]
    public async Task JoinSession_rejects_a_guessed_token_and_an_empty_token()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var victimId, out _);

        var (hub, ctx) = rig.NewHub("guesser-conn");
        await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinSession(state.Session.Id, victimId, ""));
        await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinSession(state.Session.Id, victimId, "not-the-token"));

        Assert.Null(state.GetConnection(victimId));
        Assert.False(ctx.Items.ContainsKey("playerId"));
    }

    [Fact]
    public async Task JoinSession_rejection_message_does_not_reveal_what_was_wrong()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var victimId, out _);
        var (hub, _) = rig.NewHub("probe-conn");

        var unknownSession = await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinSession(Guid.NewGuid(), victimId, "whatever"));
        var unknownPlayer = await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinSession(state.Session.Id, Guid.NewGuid(), "whatever"));
        var badToken = await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinSession(state.Session.Id, victimId, "whatever"));

        Assert.Equal(unknownSession.Message, unknownPlayer.Message);
        Assert.Equal(unknownPlayer.Message, badToken.Message);
    }

    [Fact]
    public async Task JoinSession_accepts_the_token_minted_for_that_player()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var firstId, out var secondId);

        var (hubA, ctxA) = rig.NewHub("conn-a");
        var snapA = await hubA.JoinSession(state.Session.Id, firstId, state.GetSessionToken(firstId)!);
        Assert.Equal(firstId, snapA.Player.Id);
        Assert.Equal("conn-a", state.GetConnection(firstId));
        Assert.Equal(firstId, ctxA.Items["playerId"]);
        Assert.Equal(state.Session.Id, ctxA.Items["sessionId"]);

        var (hubB, _) = rig.NewHub("conn-b");
        var snapB = await hubB.JoinSession(state.Session.Id, secondId, state.GetSessionToken(secondId)!);
        Assert.Equal(secondId, snapB.Player.Id);
        Assert.Equal("conn-b", state.GetConnection(secondId));

        // A reconnect by the rightful owner rebinds cleanly (same token).
        var (hubA2, _) = rig.NewHub("conn-a2");
        await hubA2.JoinSession(state.Session.Id, firstId, state.GetSessionToken(firstId)!);
        Assert.Equal("conn-a2", state.GetConnection(firstId));
    }

    [Fact]
    public void Session_tokens_are_unique_per_player_and_long_enough_to_be_unguessable()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var firstId, out var secondId);

        var a = state.GetSessionToken(firstId)!;
        var b = state.GetSessionToken(secondId)!;

        Assert.NotEqual(a, b);
        // base64url of at least 16 bytes; anything shorter is brute-forceable.
        Assert.True(a.Length >= 22, $"token too short: {a.Length} chars");
        Assert.DoesNotContain("=", a);
        Assert.DoesNotContain("+", a);
        Assert.DoesNotContain("/", a);
        // Never derived from anything the client supplied.
        Assert.DoesNotContain(firstId.ToString("N"), a);
    }

    [Fact]
    public async Task Session_token_never_appears_in_anything_broadcast_to_other_clients()
    {
        var rig = MakeRig();
        var state = TwoPlayerSession(rig.Sessions, out var firstId, out var secondId);
        var firstToken = state.GetSessionToken(firstId)!;
        var secondToken = state.GetSessionToken(secondId)!;

        var (hubA, _) = rig.NewHub("conn-a");
        await hubA.JoinSession(state.Session.Id, firstId, firstToken);
        var (hubB, _) = rig.NewHub("conn-b");
        await hubB.JoinSession(state.Session.Id, secondId, secondToken);

        await rig.Broadcaster.BroadcastAsync(state);

        // Every snapshot the second connection ever received, serialized the
        // way SignalR would ship it, must be free of both tokens. Snapshots
        // are not the channel a credential travels on.
        var seen = rig.HubContext.Recording.For("conn-b").Snapshots;
        Assert.NotEmpty(seen);
        foreach (var snap in seen)
        {
            var json = JsonSerializer.Serialize(snap);
            Assert.DoesNotContain(firstToken, json, StringComparison.Ordinal);
            Assert.DoesNotContain(secondToken, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Lobby_broadcast_dto_does_not_carry_any_members_session_token()
    {
        var lobbies = new LobbyManager();
        var hostId = Guid.NewGuid();
        var joinerId = Guid.NewGuid();

        var state = lobbies.CreateLobby(hostId, "Host", "host-conn");
        var join = lobbies.JoinByCode(state.Room.Code, joinerId, "Joiner", "joiner-conn");
        Assert.Equal(LobbyJoinResult.Success, join.Result);

        var tokens = state.Room.Members.Select(m => m.SessionToken).ToList();
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.NotEqual(tokens[0], tokens[1]);

        // LobbyDto is the payload pushed to the whole lobby group via
        // ReceiveLobbyUpdate, so it is the one DTO that must never carry a
        // credential. Serializing it is the check that survives someone
        // later adding a field to LobbyMemberDto.
        var json = JsonSerializer.Serialize(LobbyMapper.ToDto(state));
        foreach (var token in tokens)
            Assert.DoesNotContain(token, json, StringComparison.Ordinal);
    }
}
