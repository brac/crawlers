namespace Crawlers.Domain.Enums;

/// <summary>
/// Server-side kind of a placed <see cref="EntityType.Chest"/>. The kind
/// is held in <see cref="Models.Entity.ChestKind"/> and only revealed
/// when the chest is opened (Step 3.3): Standard chests yield loot,
/// Empty chests yield nothing, Mimic chests reveal as enemies and
/// initiate combat with an attack of opportunity. Clients see only
/// "a closed chest" until the open action resolves.
/// </summary>
public enum ChestKind
{
    Standard = 0,
    Empty = 1,
    Mimic = 2
}
