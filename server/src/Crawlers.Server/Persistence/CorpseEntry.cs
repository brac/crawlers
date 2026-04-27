namespace Crawlers.Server.Persistence;

/// <summary>
/// One row per player death. Multiplayer phase only reads from this table
/// within the originating session (the corpse Entity in the in-memory floor
/// is the live render source), but the table is keyed on
/// (FloorNumber, X, Y, PlayerId) so the future continuation phase can query
/// "what corpses exist at floor N from prior runs?" without a schema change.
/// </summary>
public class CorpseEntry
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid SessionId { get; set; }

    public int FloorNumber { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public DateTimeOffset DiedAt { get; set; }

    /// <summary>Optional cause-of-death narrative, mirrored from RunHistory.</summary>
    public string? CauseOfDeath { get; set; }
}
