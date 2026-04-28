namespace Crawlers.Server.Persistence;

/// <summary>
/// One row per player death. World-scoped persistence (Step 3 of the
/// persistent-world phase): every session loading a floor reads every
/// corpse at that depth, regardless of which session generated it. Rows
/// are never deleted by gameplay — only by a deliberate world-version
/// bump (which wipes <c>floors</c> and corpses together because the
/// stored coordinates would no longer map to coherent geometry).
/// </summary>
public class CorpseEntry
{
    public Guid Id { get; set; }

    /// <summary>The persistent player UUID (matches <c>PlayerRecord.Id</c>).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>
    /// Originating session — kept for forensic / debugging joins, not
    /// referenced by gameplay queries (which are world-scoped).
    /// </summary>
    public Guid SessionId { get; set; }

    public int FloorNumber { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public DateTimeOffset DiedAt { get; set; }

    /// <summary>Optional cause-of-death narrative, mirrored from RunHistory.</summary>
    public string? CauseOfDeath { get; set; }

    /// <summary>
    /// Display name the player carried at the moment of death. Frozen
    /// here so a later rename in the <c>players</c> table doesn't
    /// retroactively change the headstone. Null for old rows minted
    /// before Step 3 of the persistent-world phase.
    /// </summary>
    public string? PlayerUsername { get; set; }

    /// <summary>
    /// Short archetype tag for whatever killed the player ("Husk",
    /// "BigSlug", "TinySlug", …) — null when the cause wasn't an enemy
    /// (Step 3 only sets it for combat deaths).
    /// </summary>
    public string? KillerType { get; set; }

    /// <summary>
    /// Deepest floor the player had reached at the moment of death.
    /// Equal to <see cref="FloorNumber"/> for any death in this game today
    /// (no ascend mechanic), but kept distinct so the future World Stats
    /// page can answer "deepest reached" without joining run-history.
    /// </summary>
    public int DeepestFloor { get; set; }
}
