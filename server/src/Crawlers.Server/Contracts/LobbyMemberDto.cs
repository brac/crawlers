namespace Crawlers.Server.Contracts;

public record LobbyMemberDto(
    Guid PlayerId,
    bool IsHost,
    DateTimeOffset JoinedAt
);
