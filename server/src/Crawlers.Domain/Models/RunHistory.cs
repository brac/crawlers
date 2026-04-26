namespace Crawlers.Domain.Models;

public record RunHistory
{
    public Guid Id { get; init; }
    public Guid PlayerId { get; init; }
    public int FloorsCleared { get; init; }
    public int EnemiesKilled { get; init; }
    public string? CauseOfDeath { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
