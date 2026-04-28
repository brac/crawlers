namespace Crawlers.Server.Contracts;

public record LobbyMemberDto(
    Guid PlayerId,
    string Username,
    bool IsHost,
    DateTimeOffset JoinedAt
);
