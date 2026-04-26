using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record GameStateSnapshotDto(
    Guid SessionId,
    GameMode Mode,
    int FloorNumber,
    FloorSnapshotDto Floor,
    PlayerSnapshotDto Player,
    CombatLogDto? Combat
);
