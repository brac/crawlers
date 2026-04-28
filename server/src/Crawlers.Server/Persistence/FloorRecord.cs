namespace Crawlers.Server.Persistence;

/// <summary>
/// Canonical persisted floor — one row per <see cref="FloorNumber"/>, shared
/// across every player session that ever runs against this database. Stores
/// the structural output of <c>BspFloorGenerator</c> + <c>EntityPlacer.MintStructure</c>:
/// tile grid, room list, and boss-room metadata. Enemies are not persisted —
/// they're spawned per session by <c>EntityPlacer.PlaceEnemies</c>.
///
/// <para>
/// <see cref="Version"/> matches <see cref="WorldConstants.Version"/> at the
/// time the row was minted. On startup, rows whose version is below the
/// current constant are deleted (along with their corpses) and re-minted.
/// </para>
/// </summary>
public class FloorRecord
{
    public Guid Id { get; set; }

    /// <summary>1-based depth. Unique — one canonical floor per depth.</summary>
    public int FloorNumber { get; set; }

    public int Seed { get; set; }
    public int Version { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Tile grid serialized row-major as a single byte per cell, value =
    /// (byte)<c>TileType</c>. Length == <see cref="Width"/> * <see cref="Height"/>.
    /// </summary>
    public byte[] Tiles { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Room metadata serialized as JSON (jsonb in Postgres). Format defined
    /// by <c>FloorWorldService</c>'s internal RoomData record.
    /// </summary>
    public string RoomsJson { get; set; } = "[]";

    // Boss-room geometry. Null on floors without a boss room (every floor
    // past 1, today). When set, BossDoor is the single closed-door tile and
    // BossRoomBounds is the chamber's rectangle. The boss entity itself is
    // session-scoped, not persisted — see Floor.BossEntityId.
    public int? BossDoorX { get; set; }
    public int? BossDoorY { get; set; }
    public int? BossRoomX { get; set; }
    public int? BossRoomY { get; set; }
    public int? BossRoomWidth { get; set; }
    public int? BossRoomHeight { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
