namespace Crawlers.Server.Contracts;

/// <summary>
/// One entry in the dead player's spectator picker — a teammate they could
/// follow. Lean by design: just enough for the client to present the list
/// and route a SetSpectatorTarget call. The picker filters to live + still-
/// connected teammates server-side, so any id here is currently spectatable.
/// </summary>
public record SpectatableTargetDto(
    Guid Id,
    int FloorNumber,
    bool InCombat
);
