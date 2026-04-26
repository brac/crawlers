namespace Crawlers.Domain.Models;

public class CombatRound
{
    public int Number { get; init; }
    public List<CombatEvent> Events { get; init; } = new();
}
