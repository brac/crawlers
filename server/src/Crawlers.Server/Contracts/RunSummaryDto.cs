using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

/// <summary>
/// End-of-run payload attached to every snapshot once the run has ended
/// (Step 13 — see MULTIPLAYER.md). Null while the run is still going. Every
/// connected player — living, dead spectating, or reconnecting after the
/// fact — receives an identical summary so the end-of-run screen renders the
/// same thing on every client.
/// </summary>
public record RunSummaryDto(
    RunOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DeepestFloor,
    int EnemiesKilled,
    IReadOnlyList<RunSummaryPlayerDto> Players
);

/// <summary>
/// Per-player slice of the end-of-run summary. <see cref="Survived"/> exists
/// because future continuation outcomes (last-survivor quit) will leave some
/// players alive — today's only outcome (PartyWiped) means every entry has
/// <see cref="Survived"/> = false.
/// </summary>
public record RunSummaryPlayerDto(
    Guid PlayerId,
    string Username,
    int FinalFloor,
    int DeepestFloor,
    int FinalHp,
    int FinalMaxHp,
    bool Survived,
    string? CauseOfDeath,
    DateTimeOffset? DiedAt,
    int DeathX,
    int DeathY
);
