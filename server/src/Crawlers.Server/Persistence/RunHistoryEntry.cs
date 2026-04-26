namespace Crawlers.Server.Persistence;

/// <summary>
/// One row per completed run. Currently only written for permadeath outcomes
/// (Step 9 scope). Future outcomes (quit, victory) reuse the same shape.
/// </summary>
public class RunHistoryEntry
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid SessionId { get; set; }

    /// <summary>The initial floor seed — captured for replay/debugging.</summary>
    public int Seed { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>Free-form outcome label. Today: "died". Future: "quit", "won".</summary>
    public string Outcome { get; set; } = "died";

    public string? CauseOfDeath { get; set; }

    /// <summary>Deepest floor number reached (1-indexed; multi-floor lands later).</summary>
    public int DeepestFloor { get; set; }

    public int EnemiesKilled { get; set; }
    public int FinalHp { get; set; }
    public int FinalMaxHp { get; set; }
    public int InventoryCount { get; set; }
}
