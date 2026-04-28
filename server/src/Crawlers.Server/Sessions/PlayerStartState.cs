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

    public required EntityStats Stats { get; init; }
    public List<Item> Inventory { get; init; } = new();

    /// <summary>
    /// Floor the player begins on. For fresh starts this is always 1; for
    /// continuation it'll be the floor they saved on.
    /// </summary>
    public int FloorNumber { get; init; } = 1;
}
