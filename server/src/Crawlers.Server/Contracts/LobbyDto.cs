using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record LobbyDto(
    Guid Id,
    string Code,
    Guid HostPlayerId,
    int MaxPlayers,
    LobbyStatus Status,
    Guid? SessionId,
    IReadOnlyList<LobbyMemberDto> Members
);
