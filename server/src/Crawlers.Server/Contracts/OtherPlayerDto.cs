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
    bool InCombat,
    // Step 3.4 — equipped weapon name so the renderer can swap a
    // teammate's held-weapon sprite when they pick up a new one.
    // Without this, other players' weapons stay frozen on Regular
    // Sword regardless of what they're actually wielding.
    string? EquippedWeaponName,
    // Multiplayer revive — true when this teammate is dead AND still
    // spectating (Mode == Resolution AND connected). The client uses
    // this to gate the revive dialog on adjacent corpses; without it,
    // disconnected dead teammates would also offer revive (which the
    // server would then silently reject).
    bool IsReviveable
);
