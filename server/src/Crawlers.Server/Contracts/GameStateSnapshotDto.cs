using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record GameStateSnapshotDto(
    Guid SessionId,
    GameMode Mode,
    int FloorNumber,
    FloorSnapshotDto Floor,
    PlayerSnapshotDto Player,
    IReadOnlyList<OtherPlayerDto> OtherPlayers,
    CombatLogDto? Combat,
    // Step 11 — spectator state. SpectatorTargetId is set when a dead player
    // is following a teammate; the snapshot's floor/position/combat are then
    // built from that teammate's perspective. SpectatableTargets lists the
    // live + connected teammates a dead player can pick from (empty for
    // living players).
    Guid? SpectatorTargetId,
    IReadOnlyList<SpectatableTargetDto> SpectatableTargets,
    // Other active combats happening on the viewer's floor that the viewer
    // is *not* a participant in. The renderer ingests events from these to
    // animate teammate swings/lunges/dodges even when the local player is
    // outside the fight (e.g. exploring while a teammate engages); the
    // CombatLog UI ignores them so it stays scoped to the viewer's own log.
    IReadOnlyList<CombatLogDto> AmbientCombats,
    // Step 13 — populated once the run has ended (today: every player in
    // Resolution = PartyWiped). Null while the run is still going. The
    // client uses presence as the trigger to render the end-of-run screen.
    RunSummaryDto? RunSummary
);
