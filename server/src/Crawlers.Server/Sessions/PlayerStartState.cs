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
    public required EntityStats Stats { get; init; }
    public List<Item> Inventory { get; init; } = new();

    /// <summary>
    /// Floor the player begins on. For fresh starts this is always 1; for
    /// continuation it'll be the floor they saved on.
    /// </summary>
    public int FloorNumber { get; init; } = 1;
}
