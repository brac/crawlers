using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

public class EntityPlacer
{
    public const int DefaultEnemyCount = 7;

    /// <summary>
    /// Mint-time pass: finalize the structural geometry of the floor (boss
    /// room sealing, door carving, stairs-down repositioning) and stamp the
    /// resulting <see cref="Floor.BossDoor"/> / <see cref="Floor.BossRoomBounds"/>
    /// onto the floor. This pass mutates the tile grid permanently and is
    /// what the persistent <c>floors</c> table snapshots — every session
    /// reading that floor sees the same structure. <see cref="Floor.BossEntityId"/>
    /// stays null here; it's set per-session in <see cref="PlaceEnemies"/>.
    /// </summary>
    public static void MintStructure(Floor floor)
    {
        var (doorPos, roomBounds) = DoorPlacer.FinalizeBossRoom(floor);
        if (doorPos is not null && roomBounds is not null)
        {
            floor.BossDoor = doorPos;
            floor.BossRoomBounds = roomBounds;
        }
    }

    /// <summary>
    /// Per-session pass: spawn enemies (including the boss on floor 1 if the
    /// canonical structure has a boss room) onto a freshly-cloned floor that
    /// has already been minted by <see cref="MintStructure"/>. Each call
    /// stamps a new <see cref="Floor.BossEntityId"/> because the boss entity
    /// itself is session-scoped — it dies, the door unlocks, and that's the
    /// run's relationship with that boss.
    /// </summary>
    public void PlaceEnemies(Floor floor, Random rng, int enemyCount = DefaultEnemyCount)
    {
        if (floor.FloorNumber == 1)
        {
            PlaceFloorOne(floor, rng, enemyCount);
            return;
        }
        PlaceDefault(floor, rng, enemyCount);
    }

    /// <summary>
    /// Legacy entry point: structure + enemies in one call. Retained because
    /// existing tests build floors end-to-end without going through the
    /// world-mint pipeline. Production code (SessionManager, DescendService)
    /// goes through <see cref="MintStructure"/> + <see cref="PlaceEnemies"/>.
    /// </summary>
    public void Place(Floor floor, Random rng, int enemyCount = DefaultEnemyCount)
    {
        MintStructure(floor);
        PlaceEnemies(floor, rng, enemyCount);
    }

    /// <summary>
    /// Floor 1 layout: place the BigSlug boss at the (already-finalized)
    /// stairs-down tile and scatter TinySlugs through the rest of the floor.
    /// The boss is additive — it doesn't count toward <paramref name="enemyCount"/>.
    /// </summary>
    private void PlaceFloorOne(Floor floor, Random rng, int enemyCount)
    {
        var downTile = FindTile(floor, TileType.StairsDown);
        Entity? boss = null;
        if (downTile is { } down)
        {
            boss = EnemyTemplates.Create(EnemyArchetype.BigSlug, down, floor.Id);
            floor.Entities.Add(boss);
        }

        if (floor.BossDoor is not null && floor.BossRoomBounds is not null && boss is not null)
        {
            floor.BossEntityId = boss.Id;
        }

        var spawnRoom = FindSpawnRoom(floor);
        var bossRoomId = floor.BossRoomBounds is { } b
            ? floor.Rooms.FirstOrDefault(r => r.Bounds.Equals(b))?.Id
            : null;
        // TinySlugs go in any room that isn't the spawn or boss room.
        var candidates = floor.Rooms
            .Where(r => spawnRoom is null || r.Id != spawnRoom.Id)
            .Where(r => bossRoomId is null || r.Id != bossRoomId)
            .ToList();
        if (candidates.Count == 0) return;

        var occupied = new HashSet<Position>();
        if (FindTile(floor, TileType.StairsUp) is { } up) occupied.Add(up);
        if (downTile is { } d) occupied.Add(d);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = enemyCount * 50;
        while (placed < enemyCount && attempts++ < maxAttempts)
        {
            var room = candidates[rng.Next(candidates.Count)];
            int x = rng.Next(room.Bounds.X, room.Bounds.X + room.Bounds.Width);
            int y = rng.Next(room.Bounds.Y, room.Bounds.Y + room.Bounds.Height);
            var pos = new Position(x, y);
            if (occupied.Contains(pos)) continue;
            if (floor.TileGrid[x, y].Type != TileType.Floor) continue;

            occupied.Add(pos);
            floor.Entities.Add(EnemyTemplates.Create(EnemyArchetype.TinySlug, pos, floor.Id));
            placed++;
        }
    }

    private void PlaceDefault(Floor floor, Random rng, int enemyCount)
    {
        var spawnRoom = FindSpawnRoom(floor);
        var candidates = spawnRoom is null
            ? floor.Rooms.ToList()
            : floor.Rooms.Where(r => r.Id != spawnRoom.Id).ToList();

        if (candidates.Count == 0) return;

        var occupied = new HashSet<Position>();
        if (FindTile(floor, TileType.StairsUp) is { } up) occupied.Add(up);
        if (FindTile(floor, TileType.StairsDown) is { } down) occupied.Add(down);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = enemyCount * 50;
        while (placed < enemyCount && attempts++ < maxAttempts)
        {
            var room = candidates[rng.Next(candidates.Count)];
            int x = rng.Next(room.Bounds.X, room.Bounds.X + room.Bounds.Width);
            int y = rng.Next(room.Bounds.Y, room.Bounds.Y + room.Bounds.Height);
            var pos = new Position(x, y);
            if (occupied.Contains(pos)) continue;
            if (floor.TileGrid[x, y].Type != TileType.Floor) continue;

            occupied.Add(pos);
            // Random pick among the original three archetypes (Husk/Rasper/Hulk).
            var archetype = (EnemyArchetype)rng.Next(3);
            floor.Entities.Add(EnemyTemplates.Create(archetype, pos, floor.Id));
            placed++;
        }
    }

    private static Room? FindSpawnRoom(Floor floor)
    {
        if (FindTile(floor, TileType.StairsUp) is not { } up) return null;
        return floor.Rooms.FirstOrDefault(r => Contains(r.Bounds, up));
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
