using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class LobbyRoom
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public Guid HostPlayerId { get; set; }
    public int MaxPlayers { get; init; }
    public LobbyStatus Status { get; set; }

    /// <summary>
    /// The id of the session this lobby spun up when the host clicked Start.
    /// Null while Status == Waiting; set by LobbyHub.StartGame and surfaced
    /// in the LobbyDto so a code-based late-joiner knows where to connect on
    /// /game without the host having to share another id.
    /// </summary>
    public Guid? SessionId { get; set; }

    public List<LobbyMember> Members { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
}
