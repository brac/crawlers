using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class CombatLog
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Guid FloorId { get; init; }
    public List<CombatRound> Rounds { get; init; } = new();
    public CombatOutcome Outcome { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
}
