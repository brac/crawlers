using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class Player
{
    public Guid Id { get; init; }

    /// <summary>
    /// Display name pulled from the persistent <c>players</c> row at lobby
    /// connect time. Mirrors whatever the player most recently typed into
    /// the identity-setup screen. Not unique — collisions are fine, the id
    /// is the real identity.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public Guid SessionId { get; init; }
    public Position Position { get; set; }
    public EntityStats Stats { get; set; } = new();
    public List<Item> Inventory { get; init; } = new();

    /// <summary>
    /// Floor the player is currently on. Players can be on different floors
    /// at the same time — the session holds every floor any player has
    /// reached, keyed by floor number. Fog of war is shared per floor and
    /// lives on <see cref="Crawlers.Server.Sessions.SessionState"/>, not
    /// here.
    /// </summary>
    public int CurrentFloorNumber { get; set; }

    /// <summary>
    /// Per-player game mode. One player can be in Combat while a teammate
    /// keeps exploring; one can be Resolution (dead) while others fight on.
    /// The run as a whole ends only when every player is in Resolution
    /// (Step 13 — not implemented in Step 3).
    /// </summary>
    public GameMode Mode { get; set; } = GameMode.Exploration;

    /// <summary>
    /// When this player is dead (Mode == Resolution) and has chosen a
    /// teammate to spectate, this is that teammate's id. The snapshot mapper
    /// builds the dead player's view from the target's floor/position/fog
    /// so they can keep watching the run unfold. Cleared automatically when
    /// the target dies or disconnects.
    /// </summary>
    public Guid? SpectatorTargetId { get; set; }

    /// <summary>
    /// Deepest floor number this player has set foot on. Initialized to the
    /// starting floor (always 1 in fresh-run lobbies) and bumped in
    /// <see cref="Crawlers.Server.Logic.DescendService"/>. Pinned at the
    /// last value when the player dies — the run summary uses this to show
    /// "how far they got" rather than where the corpse ended up sitting.
    /// </summary>
    public int DeepestFloorReached { get; set; } = 1;

    /// <summary>
    /// UTC timestamp the player died. Null while alive. Set by
    /// <see cref="Crawlers.Server.Logic.CombatService"/> on the same tick
    /// the player flips to <see cref="GameMode.Resolution"/>.
    /// </summary>
    public DateTimeOffset? DiedAt { get; set; }

    /// <summary>
    /// Human-readable cause of death (e.g. "Slain by a Husk"). Mirrors what
    /// the run history row records and what the run summary shows on the
    /// end-of-run screen. Null while alive.
    /// </summary>
    public string? CauseOfDeath { get; set; }
}
