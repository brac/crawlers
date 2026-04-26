using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record CombatRoundDto(int Number, IReadOnlyList<string> Events);

public record CombatLogDto(
    CombatOutcome Outcome,
    IReadOnlyList<CombatRoundDto> Rounds
);
