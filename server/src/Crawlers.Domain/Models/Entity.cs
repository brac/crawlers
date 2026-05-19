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

    /// <summary>
    /// Content-and-Depth Step 3 — the kind of chest, when
    /// <see cref="Type"/> == <see cref="EntityType.Chest"/>. Held server-
    /// side and only revealed at open time (Step 3.3); the snapshot DTO
    /// does not expose this so the client cannot distinguish a Standard
    /// chest from a Mimic before opening it. Null on every non-chest.
    /// </summary>
    public ChestKind? ChestKind { get; set; }

    /// <summary>
    /// Content-and-Depth Step 3.3 — whether a chest has been opened.
    /// Only meaningful when <see cref="Type"/> == <see cref="EntityType.Chest"/>.
    /// Drives the open/closed sprite swap on the client and the
    /// "already opened" rejection in <c>ChestService.TryOpen</c>.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// Step 5 — active Bleed / Poison status effects on enemy entities
    /// (parallel to <see cref="Player.StatusEffects"/>). Players don't
    /// inflict statuses on enemies in this phase (no on-hit weapon
    /// effects yet), but the slot exists so the status-tick loop can
    /// be uniform across combatants.
    /// </summary>
    public List<StatusEffect> StatusEffects { get; init; } = new();

    /// <summary>
    /// Step 5 — status effect this enemy applies on a successful melee
    /// hit. Set per-archetype in <c>EnemyTemplates</c>; null on
    /// archetypes that don't inflict statuses (most basic enemies). The
    /// stacking rule (refresh-to-longer) is enforced in
    /// <c>StatusEffectHelper.Apply</c>.
    /// </summary>
    public StatusEffect? OnHitStatus { get; set; }

    /// <summary>
    /// AI_BEHAVIOR — last player tile this enemy had line of sight to.
    /// Used to continue chasing after the player breaks LOS.
    /// Null until the enemy first spots a player.
    /// </summary>
    public Position? LastSeenPlayerTile { get; set; }

    /// <summary>
    /// AI_BEHAVIOR — ticks remaining to pursue <see cref="LastSeenPlayerTile"/>
    /// after losing direct line of sight. Decremented each tick while no
    /// target is visible; enemy idles once it reaches zero.
    /// </summary>
    public int GiveUpTicksRemaining { get; set; }
}
