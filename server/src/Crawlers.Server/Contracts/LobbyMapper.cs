using Crawlers.Server.Lobbies;

namespace Crawlers.Server.Contracts;

public static class LobbyMapper
{
    public static LobbyDto ToDto(LobbyState state)
    {
        var room = state.Room;
        var members = room.Members
            .Select(m => new LobbyMemberDto(
                PlayerId: m.PlayerId,
                IsHost: m.PlayerId == room.HostPlayerId,
                JoinedAt: m.JoinedAt))
            .ToList();
        return new LobbyDto(
            Id: room.Id,
            Code: room.Code,
            HostPlayerId: room.HostPlayerId,
            MaxPlayers: room.MaxPlayers,
            Status: room.Status,
            SessionId: room.SessionId,
            Members: members);
    }
}
