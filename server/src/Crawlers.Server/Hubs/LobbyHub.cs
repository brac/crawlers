using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Lobbies;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Hubs;

public class LobbyHub : Hub<ILobbyClient>
{
    private const string PlayerIdKey = "playerId";
    private const string UsernameKey = "username";
    private const string LobbyIdKey = "lobbyId";

    private readonly LobbyManager _lobbies;
    private readonly SessionManager _sessions;
    private readonly IPlayerIdentityService _identities;
    private readonly ILogger<LobbyHub> _logger;

    public LobbyHub(
        LobbyManager lobbies,
        SessionManager sessions,
        IPlayerIdentityService identities,
        ILogger<LobbyHub> logger)
    {
        _lobbies = lobbies;
        _sessions = sessions;
        _identities = identities;
        _logger = logger;
    }

    /// <summary>
    /// Pre-lobby identity assertion. The client sends the UUID it stored in
    /// localStorage on first visit (or generated this visit) plus the chosen
    /// username. The hub upserts the persistent <c>players</c> row and stores
    /// the (id, name) pair on the connection so subsequent CreateRoom /
    /// JoinRoomByCode use them. Calling Identify a second time renames the
    /// player — UUID is sticky, name is editable.
    /// </summary>
    public async Task Identify(Guid playerId, string username)
    {
        if (playerId == Guid.Empty)
            throw new HubException("InvalidPlayerId");

        var sanitized = SanitizeUsername(username);
        if (sanitized is null)
            throw new HubException("InvalidUsername");

        Context.Items[PlayerIdKey] = playerId;
        Context.Items[UsernameKey] = sanitized;

        await _identities.IdentifyAsync(playerId, sanitized, Context.ConnectionAborted);

        _logger.LogInformation(
            "Player {PlayerId} identified as '{Username}'", playerId, sanitized);
    }

    public async Task<LobbyMembershipDto> CreateRoom()
    {
        await LeaveCurrentLobbyIfAny();

        var (playerId, username) = RequireIdentity();
        var state = _lobbies.CreateLobby(playerId, username, Context.ConnectionId);
        Context.Items[LobbyIdKey] = state.Room.Id;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(state.Room.Id));

        LobbyDto dto;
        lock (state.SyncRoot) dto = LobbyMapper.ToDto(state);

        _logger.LogInformation(
            "Player {PlayerId} ('{Username}') created lobby {LobbyId} (code {Code})",
            playerId, username, state.Room.Id, state.Room.Code);

        return new LobbyMembershipDto(playerId, dto);
    }

    public async Task<LobbyMembershipDto> JoinRoomByCode(string code)
    {
        await LeaveCurrentLobbyIfAny();

        var (playerId, username) = RequireIdentity();

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
                Username = username,
                Stats = SessionManager.DefaultPlayerStats(),
                Inventory = SessionManager.DefaultStartingInventory()
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
                "Player {PlayerId} ('{Username}') late-joined session {SessionId} via lobby {LobbyId} (code {Code})",
                playerId, username, sessionId, existing.Room.Id, existing.Room.Code);

            return new LobbyMembershipDto(playerId, lateDto);
        }

        var outcome = _lobbies.JoinByCode(code, playerId, username, Context.ConnectionId);
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
            "Player {PlayerId} ('{Username}') joined lobby {LobbyId} (code {Code}, {Members} members)",
            playerId, username, state.Room.Id, state.Room.Code, dto.Members.Count);

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
                    Username = m.Username,
                    Stats = SessionManager.DefaultPlayerStats(),
                    Inventory = SessionManager.DefaultStartingInventory()
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

    private (Guid PlayerId, string Username) RequireIdentity()
    {
        if (Context.Items[PlayerIdKey] is not Guid playerId
            || Context.Items[UsernameKey] is not string username)
        {
            throw new HubException("NotIdentified");
        }
        return (playerId, username);
    }

    /// <summary>
    /// Trim, length-check, and strip control characters from the supplied
    /// username. Returns null if the result is empty or too long. Validation
    /// is permissive on character set — the only real bar is "non-empty
    /// printable string under the cap" — but ASCII control characters and
    /// the C0/C1 ranges get stripped so they can't sneak through into log
    /// lines or the lobby roster.
    /// </summary>
    private static string? SanitizeUsername(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length < IdentityConstraints.UsernameMinLength) return null;
        if (trimmed.Length > IdentityConstraints.UsernameMaxLength) return null;
        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch)) return null;
        }
        return trimmed;
    }

    private static string GroupName(Guid lobbyId) => $"lobby:{lobbyId}";
}
