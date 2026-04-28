namespace Crawlers.Server.Contracts;

/// <summary>
/// Lean view of a teammate the local player shares a floor with. No inventory
/// (private to the owning player) — just what the renderer needs to draw,
/// label, and badge them. <see cref="InCombat"/> drives the "⚔" suffix on
/// their label so other players can see at a glance who's fighting.
/// Username is the persistent display name pulled from the <c>players</c>
/// row at lobby connect time.
/// </summary>
public record OtherPlayerDto(
    Guid Id,
    string Username,
    int X,
    int Y,
    int Hp,
    int MaxHp,
    bool InCombat
);
