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
}
