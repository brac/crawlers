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
    CombatOutcome Outcome,
    IReadOnlyList<CombatRoundDto> Rounds
);
