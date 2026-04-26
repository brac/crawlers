namespace Crawlers.Domain.Models;

public record CombatEvent
{
    public Guid? ActorId { get; init; }
    public Guid? TargetId { get; init; }
    public string Description { get; init; } = string.Empty;
}
