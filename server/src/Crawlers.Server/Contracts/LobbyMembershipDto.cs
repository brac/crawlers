namespace Crawlers.Server.Contracts;

/// <summary>
/// Returned to the connection that just created or joined a lobby. Carries the
/// caller's local player id so the client can identify itself in the lobby's
/// member list (every <see cref="LobbyDto"/> contains every member's id, but
/// only the caller's hub response tells them which one is theirs).
/// </summary>
public record LobbyMembershipDto(
    Guid LocalPlayerId,
    LobbyDto Lobby
);
