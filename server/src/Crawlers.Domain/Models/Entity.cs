using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class Entity
{
    public Guid Id { get; init; }
    public Guid FloorId { get; init; }
    public EntityType Type { get; init; }
    public string? Name { get; set; }
    public Position Position { get; set; }
    public EntityStats? Stats { get; set; }
    public EntityState State { get; set; }

    /// <summary>The actual Item carried, when Type == EntityType.Item.</summary>
    public Item? Item { get; set; }

    /// <summary>Owning player id, when Type == EntityType.Corpse.</summary>
    public Guid? PlayerId { get; set; }

    /// <summary>
    /// When this entity entered the world. Set on Corpse entities (mirrors
    /// the originating <see cref="Player.DiedAt"/> for fresh deaths or
    /// <c>CorpseEntry.DiedAt</c> for hydrated persistent corpses) so the
    /// renderer can fade older bodies. Null for entities the renderer
    /// doesn't age (enemies, items).
    /// </summary>
    public DateTimeOffset? DiedAt { get; set; }

    /// <summary>
    /// Display name of the player who fell, when Type == EntityType.Corpse.
    /// Frozen at the moment of death (so a later rename in the players
    /// table doesn't rewrite the headstone). Null on legacy corpse rows
    /// minted before Step 3 of the persistent-world phase.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Short archetype tag for whatever killed the player ("Husk",
    /// "TinySlug", …) when Type == EntityType.Corpse. Null for non-combat
    /// deaths and for legacy rows. Drives the Step 5 tooltip's
    /// "killed by X" line.
    /// </summary>
    public string? KillerType { get; set; }
}
