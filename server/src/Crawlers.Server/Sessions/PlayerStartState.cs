using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

/// <summary>
/// Per-player initialization data used when a session starts. In the
/// multiplayer phase this is always "fresh" (floor 1, default stats, empty
/// inventory). In the future continuation phase the same shape will be
/// loaded from each player's saved run state — that's why initialization is
/// a value object rather than hardcoded inside SessionManager.
/// </summary>
public class PlayerStartState
{
    public required Guid PlayerId { get; init; }

    /// <summary>
    /// Display name copied from the lobby member (which copies it from the
    /// persistent <c>players</c> row at Identify time). Carried through here
    /// so <see cref="Player.Username"/> is set the moment the session is
    /// created without the session graph needing to know about the identity
    /// service.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// The secret token this player will use to bind a /game connection to
    /// themselves. Minted by the lobby the moment the player takes a seat and
    /// carried through here so the token the client was handed is the same one
    /// the session ends up trusting.
    ///
    /// Null means "no token came from upstream", in which case
    /// <see cref="SessionManager"/> mints one. That keeps every seated player
    /// covered by a token no matter which path created them, so there is no
    /// code path where an empty token is the expected value.
    /// </summary>
    public string? SessionToken { get; init; }

    public required EntityStats Stats { get; init; }
    public List<Item> Inventory { get; init; } = new();

    /// <summary>
    /// Step 3.4 — weapon to equip on session entry. Optional; null
    /// triggers <c>SessionManager.DefaultEquippedWeapon()</c> (Regular
    /// Sword baseline). Continuation phase will populate this from the
    /// saved run state.
    /// </summary>
    public WeaponBlock? EquippedWeapon { get; init; }

    /// <summary>Display name of the supplied <see cref="EquippedWeapon"/>.</summary>
    public string? EquippedWeaponName { get; init; }

    /// <summary>
    /// Floor the player begins on. For fresh starts this is always 1; for
    /// continuation it'll be the floor they saved on.
    /// </summary>
    public int FloorNumber { get; init; } = 1;
}
