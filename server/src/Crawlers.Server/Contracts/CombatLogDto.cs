using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record CombatEventDto(
    Guid? ActorId,
    Guid? TargetId,
    CombatEventKind Kind,
    int? Damage,
    string Description
);

public record CombatRoundDto(int Number, IReadOnlyList<CombatEventDto> Events);

public record CombatLogDto(
    // Stable id for the combat — lets the client maintain a per-combat
    // event watermark across multiple snapshots and across the viewer's
    // own combat vs. ambient (teammate) combats sharing the same snapshot.
    Guid Id,
    CombatOutcome Outcome,
    IReadOnlyList<CombatRoundDto> Rounds
);
