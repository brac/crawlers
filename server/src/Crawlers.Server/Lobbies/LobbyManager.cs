using System.Collections.Concurrent;
using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Lobbies;

public class LobbyManager
{
    public const int DefaultMaxPlayers = 4;
    private const int CodeCollisionRetryLimit = 16;

    private readonly ConcurrentDictionary<Guid, LobbyState> _lobbies = new();
    private readonly ConcurrentDictionary<string, Guid> _byCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Guid> _playerToLobby = new();
    private readonly Random _rng;

    public LobbyManager() : this(null) { }

    public LobbyManager(Random? rng)
    {
        _rng = rng ?? Random.Shared;
    }

    public int ActiveCount => _lobbies.Count;

    public LobbyState? Get(Guid lobbyId) =>
        _lobbies.TryGetValue(lobbyId, out var s) ? s : null;

    public LobbyState? GetByCode(string code)
    {
        var normalized = LobbyCodeGenerator.Normalize(code);
        if (normalized is null) return null;
        return _byCode.TryGetValue(normalized, out var id) ? Get(id) : null;
    }

    public LobbyState? GetByPlayer(Guid playerId) =>
        _playerToLobby.TryGetValue(playerId, out var id) ? Get(id) : null;

    public LobbyState CreateLobby(Guid hostPlayerId, string hostUsername, string hostConnectionId)
    {
        if (_playerToLobby.ContainsKey(hostPlayerId))
            throw new InvalidOperationException(
                $"Player {hostPlayerId} is already in a lobby; leave it before creating a new one.");

        var lobbyId = Guid.NewGuid();
        var code = ReserveUniqueCode(lobbyId);

        var room = new LobbyRoom
        {
            Id = lobbyId,
            Code = code,
            HostPlayerId = hostPlayerId,
            MaxPlayers = DefaultMaxPlayers,
            Status = LobbyStatus.Waiting,
            CreatedAt = DateTimeOffset.UtcNow
        };
        room.Members.Add(new LobbyMember
        {
            PlayerId = hostPlayerId,
            Username = hostUsername,
            ConnectionId = hostConnectionId,
            JoinedAt = room.CreatedAt
        });

        var state = new LobbyState(room);
        _lobbies[lobbyId] = state;
        _playerToLobby[hostPlayerId] = lobbyId;
        return state;
    }

    public LobbyJoinOutcome JoinByCode(string code, Guid playerId, string username, string connectionId)
    {
        var normalized = LobbyCodeGenerator.Normalize(code);
        if (normalized is null) return new(LobbyJoinResult.NotFound, null);
        if (!_byCode.TryGetValue(normalized, out var lobbyId)) return new(LobbyJoinResult.NotFound, null);
        if (!_lobbies.TryGetValue(lobbyId, out var state)) return new(LobbyJoinResult.NotFound, null);

        lock (state.SyncRoot)
        {
            if (state.Disposed) return new(LobbyJoinResult.NotFound, null);
            if (state.Room.Status != LobbyStatus.Waiting) return new(LobbyJoinResult.AlreadyStarted, null);
            if (state.Room.Members.Any(m => m.PlayerId == playerId))
                return new(LobbyJoinResult.AlreadyInLobby, null);
            if (state.Room.Members.Count >= state.Room.MaxPlayers)
                return new(LobbyJoinResult.Full, null);

            state.Room.Members.Add(new LobbyMember
            {
                PlayerId = playerId,
                Username = username,
                ConnectionId = connectionId,
                JoinedAt = DateTimeOffset.UtcNow
            });
            _playerToLobby[playerId] = lobbyId;
            return new(LobbyJoinResult.Success, state);
        }
    }

    public LobbyLeaveOutcome Leave(Guid playerId)
    {
        if (!_playerToLobby.TryRemove(playerId, out var lobbyId))
            return new(LobbyLeaveResult.NotInLobby, null);
        if (!_lobbies.TryGetValue(lobbyId, out var state))
            return new(LobbyLeaveResult.NotInLobby, null);

        lock (state.SyncRoot)
        {
            var member = state.Room.Members.FirstOrDefault(m => m.PlayerId == playerId);
            if (member is null) return new(LobbyLeaveResult.NotInLobby, null);

            state.Room.Members.Remove(member);

            if (state.Room.Members.Count == 0)
            {
                // Once a lobby has flipped to InGame the host is *expected* to
                // disconnect from /lobby (they've moved to /game). We keep the
                // lobby record around in that case so a late joiner gets a
                // clear "AlreadyStarted" instead of a misleading "NotFound".
                // Lobbies that emptied while still Waiting are real abandons
                // and get disposed normally.
                if (state.Room.Status == LobbyStatus.Waiting)
                {
                    state.Disposed = true;
                    _lobbies.TryRemove(lobbyId, out _);
                    _byCode.TryRemove(state.Room.Code, out _);
                    return new(LobbyLeaveResult.LobbyDisposed, null);
                }
                return new(LobbyLeaveResult.MemberLeft, state);
            }

            if (state.Room.HostPlayerId == playerId)
            {
                // Promote the earliest-joined remaining member to host. Members
                // are appended in join order, so [0] is the next-most-senior.
                state.Room.HostPlayerId = state.Room.Members[0].PlayerId;
            }

            return new(LobbyLeaveResult.MemberLeft, state);
        }
    }

    public LobbyStartOutcome StartGame(Guid lobbyId, Guid requestingPlayerId)
    {
        if (!_lobbies.TryGetValue(lobbyId, out var state))
            return new(LobbyStartResult.NotFound, null);

        lock (state.SyncRoot)
        {
            if (state.Disposed) return new(LobbyStartResult.NotFound, null);
            if (state.Room.HostPlayerId != requestingPlayerId)
                return new(LobbyStartResult.NotHost, null);
            if (state.Room.Status != LobbyStatus.Waiting)
                return new(LobbyStartResult.AlreadyStarted, null);

            state.Room.Status = LobbyStatus.InGame;
            return new(LobbyStartResult.Success, state);
        }
    }

    private string ReserveUniqueCode(Guid lobbyId)
    {
        for (int attempt = 0; attempt < CodeCollisionRetryLimit; attempt++)
        {
            var code = LobbyCodeGenerator.Generate(_rng);
            if (_byCode.TryAdd(code, lobbyId)) return code;
        }
        throw new InvalidOperationException(
            $"Failed to generate a unique lobby code after {CodeCollisionRetryLimit} attempts.");
    }
}
