using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public record CombatEvent
{
    public Guid? ActorId { get; init; }
    public Guid? TargetId { get; init; }
    public CombatEventKind Kind { get; init; } = CombatEventKind.Narrative;
    public int? Damage { get; init; }
    public string Description { get; init; } = string.Empty;
}
