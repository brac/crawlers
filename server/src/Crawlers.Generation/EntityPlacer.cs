using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation.Scaling;

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
    /// stays null here; it's set per-session in <see cref="PlaceEnemies(Floor, Random, FloorScaling)"/>.
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
    /// Per-session pass with the full per-floor difficulty curve applied:
    /// optional stairwell boss placed on the stairs-down tile (and stamped
    /// as <see cref="Floor.BossEntityId"/> so the existing boss-door-lock
    /// mechanic activates), enemy count from the scaling table, monster
    /// density restricting how many rooms are eligible to spawn into, and
    /// HP/AC/damage scaling stamped onto each freshly-spawned enemy via
    /// <see cref="EnemyScaler"/>. Production code (DescendService,
    /// SessionManager) goes through this path. Tests that only care about
    /// positional placement use the <see cref="PlaceEnemies(Floor, Random, int)"/>
    /// count-based overload.
    /// </summary>
    public void PlaceEnemies(Floor floor, Random rng, FloorScaling scaling)
    {
        // Floor-1 legacy fallback: when no stairwell boss is configured AND
        // we're on floor 1, fill in the tutorial layout (BigSlug guard +
        // TinySlug-only scatter). Production config sets StairwellBoss
        // explicitly on every floor; this branch only fires from the
        // count-based test overload below.
        var stairwellBoss = scaling.StairwellBoss;
        var pool = scaling.MonsterPool;
        if (stairwellBoss is null && floor.FloorNumber == 1)
        {
            stairwellBoss = EnemyArchetype.BigSlug;
            pool = new[] { new MonsterPoolEntry(EnemyArchetype.TinySlug, 1) };
        }

        // Stairwell boss — placed first so it claims the stairs-down tile
        // and gets the BossEntityId stamp that drives boss-door locking.
        // The boss receives the same per-floor stat scaling as pool spawns.
        var bossPos = stairwellBoss is { } bossArchetype
            ? PlaceStairwellBoss(floor, scaling, bossArchetype)
            : (Position?)null;

        // Pool scatter — eligible rooms exclude the spawn room (so the
        // player isn't ambushed at start) and the boss room (so the
        // stairwell encounter stays a clean 1v1 / 1vN with the boss).
        var spawnRoom = FindSpawnRoom(floor);
        var bossRoomId = floor.BossRoomBounds is { } b
            ? floor.Rooms.FirstOrDefault(r => r.Bounds.Equals(b))?.Id
            : null;
        var allCandidates = floor.Rooms
            .Where(r => spawnRoom is null || r.Id != spawnRoom.Id)
            .Where(r => bossRoomId is null || r.Id != bossRoomId)
            .ToList();
        if (allCandidates.Count == 0) return;

        var rooms = SelectRoomsByDensity(allCandidates, scaling.MonsterDensity, rng);

        var occupied = new HashSet<Position>();
        if (FindTile(floor, TileType.StairsUp) is { } up) occupied.Add(up);
        if (FindTile(floor, TileType.StairsDown) is { } down) occupied.Add(down);
        if (bossPos is { } bp) occupied.Add(bp);

        SpawnInto(floor, rng, scaling, rooms, occupied, pos =>
        {
            // Step 2 — weighted draw from the per-floor monster pool.
            var archetype = PickArchetype(pool, rng);
            return EnemyTemplates.Create(archetype, pos, floor.Id);
        });

        // Step 3.2 — chests scatter through the same eligible-room set
        // (non-spawn, non-boss) but ignore monster density; they're
        // sparser than monsters and use their own count knob. Shares
        // `occupied` so a chest never lands on a monster, on the boss,
        // on a stairs tile, or on top of another chest.
        PlaceChests(floor, rng, scaling, allCandidates, occupied);
    }

    /// <summary>
    /// Step 3.2 — scatter chests through eligible rooms. Each placed chest
    /// rolls its <see cref="ChestKind"/> independently from the per-floor
    /// distribution; the kind is held server-side and only revealed when
    /// the chest is opened (Step 3.3 will wire that up).
    /// </summary>
    private static void PlaceChests(
        Floor floor,
        Random rng,
        FloorScaling scaling,
        List<Room> eligibleRooms,
        HashSet<Position> occupied)
    {
        if (scaling.ChestCount <= 0) return;
        if (eligibleRooms.Count == 0) return;

        // Step guard.A — chests only spawn in rooms that already
        // contain a monster. Player should never find a free chest
        // sitting in an empty room; every chest is "guarded." Filter
        // the eligible-room list by checking each room for an alive
        // Enemy entity inside its bounds.
        var guardedRooms = eligibleRooms
            .Where(r => floor.Entities.Any(e =>
                e.Type == EntityType.Enemy
                && e.State == EntityState.Alive
                && Contains(r.Bounds, e.Position)))
            .ToList();
        if (guardedRooms.Count == 0) return;

        var weights = scaling.EffectiveChestKindWeights;
        int placed = 0;
        int attempts = 0;
        int maxAttempts = scaling.ChestCount * 50;
        while (placed < scaling.ChestCount && attempts++ < maxAttempts)
        {
            var room = guardedRooms[rng.Next(guardedRooms.Count)];
            int x = rng.Next(room.Bounds.X, room.Bounds.X + room.Bounds.Width);
            int y = rng.Next(room.Bounds.Y, room.Bounds.Y + room.Bounds.Height);
            var pos = new Position(x, y);
            if (occupied.Contains(pos)) continue;
            if (floor.TileGrid[x, y].Type != TileType.Floor) continue;

            occupied.Add(pos);
            floor.Entities.Add(new Entity
            {
                Id = Guid.NewGuid(),
                FloorId = floor.Id,
                Type = EntityType.Chest,
                Name = "Chest",
                Position = pos,
                State = EntityState.Alive,
                ChestKind = PickChestKind(weights, rng)
            });
            placed++;
        }
    }

    /// <summary>
    /// Weighted random selection of a <see cref="ChestKind"/> from the
    /// per-floor distribution. Mirrors <see cref="PickArchetype"/>; falls
    /// back to the first kind when weights sum to zero (defensive — the
    /// loader rejects non-positive weights).
    /// </summary>
    internal static ChestKind PickChestKind(IReadOnlyList<ChestKindWeight> weights, Random rng)
    {
        if (weights.Count == 0) return ChestKind.Standard;

        int total = 0;
        for (int i = 0; i < weights.Count; i++) total += weights[i].Weight;
        if (total <= 0) return weights[0].Kind;

        int roll = rng.Next(total);
        int acc = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            acc += weights[i].Weight;
            if (roll < acc) return weights[i].Kind;
        }
        return weights[^1].Kind;
    }

    /// <summary>
    /// Place the configured stairwell boss on the stairs-down tile, apply
    /// the floor's stat scaling, and wire <see cref="Floor.BossEntityId"/>
    /// so MovementService's boss-door-lock fires on entry. Returns the
    /// occupied position so the caller can mark it as taken.
    /// </summary>
    private static Position? PlaceStairwellBoss(Floor floor, FloorScaling scaling, EnemyArchetype archetype)
    {
        var downTile = FindTile(floor, TileType.StairsDown);
        if (downTile is not { } down) return null;

        var boss = EnemyTemplates.Create(archetype, down, floor.Id);
        EnemyScaler.Apply(boss, scaling);
        floor.Entities.Add(boss);

        if (floor.BossDoor is not null && floor.BossRoomBounds is not null)
        {
            floor.BossEntityId = boss.Id;
        }
        return down;
    }

    /// <summary>
    /// Count-based overload retained for tests and any caller that doesn't
    /// have a <see cref="FloorScaling"/> in hand. Wraps the scaling-aware
    /// path with an identity scaling (no stat changes, density 1.0 — every
    /// eligible room is in play).
    /// </summary>
    public void PlaceEnemies(Floor floor, Random rng, int enemyCount = DefaultEnemyCount)
    {
        PlaceEnemies(floor, rng, FloorScaling.Identity(floor.FloorNumber, enemyCount));
    }

    /// <summary>
    /// Legacy entry point: structure + enemies in one call. Retained because
    /// existing tests build floors end-to-end without going through the
    /// world-mint pipeline.
    /// </summary>
    public void Place(Floor floor, Random rng, int enemyCount = DefaultEnemyCount)
    {
        MintStructure(floor);
        PlaceEnemies(floor, rng, enemyCount);
    }

    /// <summary>
    /// Scaling-aware sibling of <see cref="Place(Floor, Random, int)"/> —
    /// useful from tests that want to exercise the full curve in one call.
    /// </summary>
    public void Place(Floor floor, Random rng, FloorScaling scaling)
    {
        MintStructure(floor);
        PlaceEnemies(floor, rng, scaling);
    }

    /// <summary>
    /// Weighted random selection from a monster pool. An entry with weight
    /// 3 is three times as likely to land as an entry with weight 1.
    /// Falls back to the first entry if the pool sums to zero (defensive —
    /// the loader rejects non-positive weights, so this should not happen
    /// in production paths).
    /// </summary>
    internal static EnemyArchetype PickArchetype(IReadOnlyList<MonsterPoolEntry> pool, Random rng)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException("PickArchetype called with an empty monster pool.");

        int total = 0;
        for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;
        if (total <= 0) return pool[0].Archetype;

        int roll = rng.Next(total);
        int acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += pool[i].Weight;
            if (roll < acc) return pool[i].Archetype;
        }
        return pool[^1].Archetype;
    }

    /// <summary>
    /// Pick the subset of rooms eligible for monster placement based on the
    /// per-floor density. Density 1.0 keeps every room; density 0.6 keeps
    /// ~60% of them. Always at least one room (otherwise the floor would
    /// generate completely empty, which makes the descent meaningless).
    /// </summary>
    private static List<Room> SelectRoomsByDensity(List<Room> all, double density, Random rng)
    {
        var clamped = Math.Clamp(density, 0.0, 1.0);
        int roomsToUse = Math.Max(1, (int)Math.Round(all.Count * clamped));
        if (roomsToUse >= all.Count) return all;

        // Fisher-Yates shuffle, then take the first N. Avoids the bias of
        // repeated random-without-removal picks on small lists.
        var shuffled = new List<Room>(all);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled.Take(roomsToUse).ToList();
    }

    /// <summary>
    /// Walk the placement budget, repeatedly picking a random eligible room
    /// and a random in-room tile. <paramref name="factory"/> mints the
    /// archetype-correct entity at the resolved position; we apply the
    /// per-floor stat scaling before adding to the floor.
    /// </summary>
    private static void SpawnInto(
        Floor floor,
        Random rng,
        FloorScaling scaling,
        List<Room> eligibleRooms,
        HashSet<Position> occupied,
        Func<Position, Entity> factory)
    {
        if (eligibleRooms.Count == 0) return;

        int target = scaling.EnemyCount;
        int placed = 0;
        int attempts = 0;
        int maxAttempts = target * 50;
        while (placed < target && attempts++ < maxAttempts)
        {
            var room = eligibleRooms[rng.Next(eligibleRooms.Count)];
            int x = rng.Next(room.Bounds.X, room.Bounds.X + room.Bounds.Width);
            int y = rng.Next(room.Bounds.Y, room.Bounds.Y + room.Bounds.Height);
            var pos = new Position(x, y);
            if (occupied.Contains(pos)) continue;
            if (floor.TileGrid[x, y].Type != TileType.Floor) continue;

            occupied.Add(pos);
            var entity = factory(pos);
            EnemyScaler.Apply(entity, scaling);
            floor.Entities.Add(entity);
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
