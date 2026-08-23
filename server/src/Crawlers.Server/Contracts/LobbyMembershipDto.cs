namespace Crawlers.Server.Contracts;

/// <summary>
/// Returned to the connection that just created or joined a lobby. Carries the
/// caller's local player id so the client can identify itself in the lobby's
/// member list (every <see cref="LobbyDto"/> contains every member's id, but
/// only the caller's hub response tells them which one is theirs).
///
/// SECURITY: this DTO is the one and only place a session token is allowed to
/// travel, because it is the return value of a hub invocation and therefore
/// goes to exactly one connection: the caller. It must never be pushed to a
/// group, embedded in <see cref="LobbyDto"/>, or otherwise fanned out. If you
/// add a broadcast that carries a LobbyMembershipDto, you have created the
/// hijack this token exists to prevent.
/// </summary>
/// <param name="LocalPlayerId">
/// The caller's player id. Public information: every other client in the
/// lobby learns it too.
/// </param>
/// <param name="SessionToken">
/// The caller's secret for this lobby seat. Required by
/// <c>GameHub.JoinSession</c> to bind a /game connection to this player.
/// Private to the caller.
/// </param>
/// <param name="Lobby">The lobby's public state.</param>
public record LobbyMembershipDto(
    Guid LocalPlayerId,
    string SessionToken,
    LobbyDto Lobby
);
