namespace Crawlers.Domain.Models;

public class LobbyMember
{
    public Guid PlayerId { get; init; }

    /// <summary>
    /// Display name pulled from the persistent <c>players</c> row at the
    /// moment the player joined the lobby. Stays put for the life of the
    /// lobby — if the player re-Identifies with a new name on a later
    /// connection, the new name only takes effect the next time they join
    /// a lobby (or the next session their <see cref="Player"/> is built for).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public string ConnectionId { get; set; } = string.Empty;
    public DateTimeOffset JoinedAt { get; init; }

    /// <summary>
    /// Secret, server-minted token for this lobby seat. Handed back to the
    /// joining connection alone and carried into the session when the host
    /// starts, where it becomes the credential that binds a /game connection
    /// to this player.
    ///
    /// SECURITY: this is a bearer credential. It must never be mapped into
    /// <c>LobbyMemberDto</c> or any other payload that reaches more than one
    /// client, and it must never be logged. <see cref="PlayerId"/> is public
    /// information; this is not.
    /// </summary>
    public string SessionToken { get; init; } = string.Empty;
}
