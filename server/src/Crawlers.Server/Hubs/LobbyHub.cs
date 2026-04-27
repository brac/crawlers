using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Lobbies;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Hubs;

public class LobbyHub : Hub<ILobbyClient>
{
    private const string PlayerIdKey = "playerId";
    private const string LobbyIdKey = "lobbyId";

    private readonly LobbyManager _lobbies;
    private readonly SessionManager _sessions;
    private readonly ILogger<LobbyHub> _logger;

    public LobbyHub(LobbyManager lobbies, SessionManager sessions, ILogger<LobbyHub> logger)
    {
        _lobbies = lobbies;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<LobbyMembershipDto> CreateRoom()
    {
        await LeaveCurrentLobbyIfAny();

        var playerId = EnsurePlayerId();
        var state = _lobbies.CreateLobby(playerId, Context.ConnectionId);
        Context.Items[LobbyIdKey] = state.Room.Id;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(state.Room.Id));

        LobbyDto dto;
        lock (state.SyncRoot) dto = LobbyMapper.ToDto(state);

        _logger.LogInformation(
            "Player {PlayerId} created lobby {LobbyId} (code {Code})",
            playerId, state.Room.Id, state.Room.Code);

        return new LobbyMembershipDto(playerId, dto);
    }

    public async Task<LobbyMembershipDto> JoinRoomByCode(string code)
    {
        await LeaveCurrentLobbyIfAny();

        var playerId = EnsurePlayerId();

        // Late-join: if the lobby has already started, the joiner is added
        // straight to the running session (spawned on floor 1 near stairs-up)
        // and the response carries the session id so the client can skip the
        // lobby-room view and go directly to /game.
        var existing = _lobbies.GetByCode(code);
        if (existing is { Room: { Status: LobbyStatus.InGame, SessionId: { } sessionId } })
        {
            var startState = new PlayerStartState
            {
                PlayerId = playerId,
                Stats = SessionManager.DefaultPlayerStats()
            };
            var newPlayer = _sessions.AddPlayerToSession(sessionId, startState);
            if (newPlayer is null)
            {
                _logger.LogInformation(
                    "Player {PlayerId} late-join failed: session {SessionId} not found",
                    playerId, sessionId);
                throw new HubException("NotFound");
            }

            LobbyDto lateDto;
            lock (existing.SyncRoot) lateDto = LobbyMapper.ToDto(existing);

            _logger.LogInformation(
                "Player {PlayerId} late-joined session {SessionId} via lobby {LobbyId} (code {Code})",
                playerId, sessionId, existing.Room.Id, existing.Room.Code);

            return new LobbyMembershipDto(playerId, lateDto);
        }

        var outcome = _lobbies.JoinByCode(code, playerId, Context.ConnectionId);
        if (outcome.Result != LobbyJoinResult.Success || outcome.State is null)
        {
            _logger.LogInformation(
                "Player {PlayerId} failed to join lobby with code '{Code}': {Result}",
                playerId, code, outcome.Result);
            throw new HubException(outcome.Result.ToString());
        }

        var state = outcome.State;
        Context.Items[LobbyIdKey] = state.Room.Id;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(state.Room.Id));

        LobbyDto dto;
        lock (state.SyncRoot) dto = LobbyMapper.ToDto(state);

        await Clients.Group(GroupName(state.Room.Id)).ReceiveLobbyUpdate(dto);

        _logger.LogInformation(
            "Player {PlayerId} joined lobby {LobbyId} (code {Code}, {Members} members)",
            playerId, state.Room.Id, state.Room.Code, dto.Members.Count);

        return new LobbyMembershipDto(playerId, dto);
    }

    public async Task StartGame()
    {
        if (Context.Items[LobbyIdKey] is not Guid lobbyId)
            throw new HubException("NotInLobby");
        if (Context.Items[PlayerIdKey] is not Guid playerId)
            throw new HubException("NotInLobby");

        var outcome = _lobbies.StartGame(lobbyId, playerId);
        if (outcome.Result != LobbyStartResult.Success)
            throw new HubException(outcome.Result.ToString());

        // Build per-player start states from the lobby roster and create the
        // multi-player session. The PlayerIds carry over from the lobby so
        // each client can authenticate itself to the game hub by replaying
        // the same id it learned during lobby join.
        var lobbyState = outcome.State!;
        List<PlayerStartState> starts;
        lock (lobbyState.SyncRoot)
        {
            starts = lobbyState.Room.Members
                .Select(m => new PlayerStartState
                {
                    PlayerId = m.PlayerId,
                    Stats = SessionManager.DefaultPlayerStats()
                })
                .ToList();
        }

        var sessionState = _sessions.CreateSession(starts);

        // Wire the session id back into the lobby so late-joiners (who hit
        // JoinRoomByCode after Start) get steered straight to /game with the
        // right session id rather than landing in a stale lobby room.
        lock (lobbyState.SyncRoot)
        {
            lobbyState.Room.SessionId = sessionState.Session.Id;
        }

        await Clients.Group(GroupName(lobbyId)).GameStarting(sessionState.Session.Id);

        _logger.LogInformation(
            "Lobby {LobbyId} (host {PlayerId}, {Members} members) started → session {SessionId}",
            lobbyId, playerId, starts.Count, sessionState.Session.Id);
    }

    public Task LeaveRoom() => LeaveCurrentLobbyIfAny();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentLobbyIfAny();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveCurrentLobbyIfAny()
    {
        if (Context.Items[LobbyIdKey] is not Guid lobbyId) return;
        if (Context.Items[PlayerIdKey] is not Guid playerId) return;

        Context.Items.Remove(LobbyIdKey);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(lobbyId));

        var outcome = _lobbies.Leave(playerId);
        if (outcome.Result == LobbyLeaveResult.MemberLeft && outcome.State is not null)
        {
            LobbyDto dto;
            lock (outcome.State.SyncRoot) dto = LobbyMapper.ToDto(outcome.State);
            await Clients.Group(GroupName(lobbyId)).ReceiveLobbyUpdate(dto);
        }

        _logger.LogInformation(
            "Player {PlayerId} left lobby {LobbyId} (result {Result})",
            playerId, lobbyId, outcome.Result);
    }

    private Guid EnsurePlayerId()
    {
        if (Context.Items[PlayerIdKey] is Guid existing) return existing;
        var fresh = Guid.NewGuid();
        Context.Items[PlayerIdKey] = fresh;
        return fresh;
    }

    private static string GroupName(Guid lobbyId) => $"lobby:{lobbyId}";
}
