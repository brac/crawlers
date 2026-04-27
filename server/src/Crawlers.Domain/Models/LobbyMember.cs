namespace Crawlers.Domain.Models;

public class LobbyMember
{
    public Guid PlayerId { get; init; }
    public string ConnectionId { get; set; } = string.Empty;
    public DateTimeOffset JoinedAt { get; init; }
}
