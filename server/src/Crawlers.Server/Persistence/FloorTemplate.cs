using Crawlers.Domain.Models;
using Crawlers.Generation;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Canonical structural snapshot of a floor — the bits that are shared by
/// every session that ever runs against this world. Held in-memory by the
/// <see cref="IFloorWorldService"/> after mint and cloned into per-session
/// <see cref="Floor"/> instances on demand. Immutable: callers should never
/// mutate <see cref="TileGrid"/> directly because they'd be mutating the
/// canonical world for every other session at the same time.
/// </summary>
internal sealed record FloorTemplate
{
    public required Guid CanonicalId { get; init; }
    public required int FloorNumber { get; init; }
    public required int Seed { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Tile[,] TileGrid { get; init; }
    public required IReadOnlyList<Room> Rooms { get; init; }
    public Position? BossDoor { get; init; }
    public Bounds? BossRoomBounds { get; init; }

    /// <summary>
    /// Run BSP + structural mint for <paramref name="floorNumber"/> using
    /// the canonical seed scheme <c>baseSeed + (floorNumber - 1)</c>.
    /// </summary>
    public static FloorTemplate Mint(int floorNumber, int baseSeed)
    {
        var seed = baseSeed + (floorNumber - 1);
        var floor = new BspFloorGenerator().Generate(new GenerationConfig
        {
            // CanonicalId — sessions reuse this so per-session Entity.FloorId
            // stays consistent without a per-session re-id pass.
            SessionId = Guid.Empty,
            FloorNumber = floorNumber,
            Seed = seed
        });
        EntityPlacer.MintStructure(floor);
        return new FloorTemplate
        {
            CanonicalId = floor.Id,
            FloorNumber = floor.FloorNumber,
            Seed = floor.Seed,
            Width = floor.Width,
            Height = floor.Height,
            TileGrid = floor.TileGrid,
            Rooms = floor.Rooms,
            BossDoor = floor.BossDoor,
            BossRoomBounds = floor.BossRoomBounds
        };
    }

    /// <summary>
    /// Clone this canonical template into a per-session floor. Tile grid is
    /// deep-copied so gameplay door-bumps stay scoped to the session;
    /// rooms list is copied (rooms themselves are records and therefore
    /// safe to share); entities list starts empty.
    /// </summary>
    public Floor CloneForSession(Guid sessionId)
    {
        var grid = (Tile[,])TileGrid.Clone();
        var floor = new Floor
        {
            Id = CanonicalId,
            SessionId = sessionId,
            FloorNumber = FloorNumber,
            Seed = Seed,
            Width = Width,
            Height = Height,
            TileGrid = grid,
            BossDoor = BossDoor,
            BossRoomBounds = BossRoomBounds
        };
        foreach (var r in Rooms) floor.Rooms.Add(r);
        return floor;
    }
}
