using System.Collections.Concurrent;
using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Logic;

namespace Crawlers.Server.Sessions;

public class SessionManager
{
    private const int MaxEnemyCount = 16;

    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly BspFloorGenerator _floorGen = new();
    private readonly EntityPlacer _entityPlacer = new();

    /// <summary>
    /// Build a fresh session and seat each provided player on their start
    /// floor — usually all on floor 1, but the model honours
    /// <see cref="PlayerStartState.FloorNumber"/> so the future continuation
    /// phase can drop a returning player back on whatever floor their saved
    /// state pinned them to without changing this signature.
    ///
    /// Players sharing a starting floor land in adjacent walkable tiles
    /// around its stairs-up. Floors are generated lazily and deterministically
    /// from the session seed so a teammate descending later sees the same
    /// dungeon a teammate who started there sees.
    /// </summary>
    public SessionState CreateSession(IReadOnlyList<PlayerStartState> startStates, int? seed = null)
    {
        if (startStates.Count == 0)
            throw new ArgumentException("Need at least one player to start a session.", nameof(startStates));

        var sessionId = Guid.NewGuid();
        var actualSeed = seed ?? Random.Shared.Next();

        var session = new Session
        {
            Id = sessionId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var state = new SessionState(session) { InitialSeed = actualSeed };

        // Group by starting floor so we generate each needed floor once and
        // BFS-spawn the cohort that starts there together.
        foreach (var group in startStates.GroupBy(s => s.FloorNumber).OrderBy(g => g.Key))
        {
            var floor = EnsureFloor(state, group.Key);
            var groupList = group.ToList();
            var anchor = FindStairsUp(floor);
            var spawns = AdjacentSpawn.Pick(floor, anchor, groupList.Count);

            for (int i = 0; i < groupList.Count; i++)
            {
                var start = groupList[i];
                var player = new Player
                {
                    Id = start.PlayerId,
                    SessionId = sessionId,
                    Position = spawns[i],
                    Stats = start.Stats,
                    CurrentFloorNumber = group.Key,
                    DeepestFloorReached = group.Key
                };
                foreach (var item in start.Inventory) player.Inventory.Add(item);
                state.AddPlayer(player);
            }

            // Compute the shared fog from the union of all spawn positions in
            // one pass — cheaper than per-player Compute calls that would each
            // demote the previous player's visibility.
            FieldOfView.RecomputeForFloor(floor, state.GetFog(group.Key)!, state.PlayersOnFloor(group.Key));
        }

        _sessions[sessionId] = state;
        return state;
    }

    /// <summary>
    /// Convenience wrapper that creates a single-player session with default
    /// stats. Used by tests; production sessions are built via the lobby
    /// bridge with the multi-player overload.
    /// </summary>
    public SessionState CreateSoloSession(int? seed = null)
    {
        var start = new PlayerStartState
        {
            PlayerId = Guid.NewGuid(),
            Stats = DefaultPlayerStats()
        };
        return CreateSession(new[] { start }, seed);
    }

    /// <summary>
    /// Add a player to an existing in-progress session — used when a code-based
    /// late-joiner enters via the lobby after the host has already pressed
    /// Start. The joiner spawns on <see cref="PlayerStartState.FloorNumber"/>
    /// (defaults to floor 1 for fresh-run lobby joins) at the next free tile
    /// near that floor's stairs-up anchor. Returns null if the session is
    /// gone (race).
    /// </summary>
    public Player? AddPlayerToSession(Guid sessionId, PlayerStartState start)
    {
        if (!_sessions.TryGetValue(sessionId, out var state)) return null;

        lock (state.SyncRoot)
        {
            // Idempotent: if a player with this id is already in the session
            // (re-enter race, refresh-and-rejoin), just hand back the existing
            // record rather than adding a duplicate.
            var already = state.GetPlayer(start.PlayerId);
            if (already is not null) return already;

            var floor = EnsureFloor(state, start.FloorNumber);

            // Existing players on this floor act as occupancy seeds so we
            // don't pile a new arrival on top of someone who hasn't moved
            // off spawn.
            var occupied = state.PlayersOnFloor(start.FloorNumber)
                .Select(p => p.Position)
                .ToHashSet();
            var anchor = FindStairsUp(floor);
            var pos = AdjacentSpawn.PickOne(floor, anchor, occupied);

            var player = new Player
            {
                Id = start.PlayerId,
                SessionId = sessionId,
                Position = pos,
                Stats = start.Stats,
                CurrentFloorNumber = start.FloorNumber,
                DeepestFloorReached = start.FloorNumber
            };
            foreach (var item in start.Inventory) player.Inventory.Add(item);
            state.AddPlayer(player);

            // Joiner contributes to this floor's shared fog. Recomputing
            // from all players keeps the union semantics correct (an existing
            // teammate's tiles stay Visible while the joiner's new view
            // unhides their part of the map).
            FieldOfView.RecomputeForFloor(floor, state.GetFog(start.FloorNumber)!, state.PlayersOnFloor(start.FloorNumber));

            return player;
        }
    }

    public SessionState? Get(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;

    public bool Remove(Guid sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public int ActiveCount => _sessions.Count;

    public static EntityStats DefaultPlayerStats() => new()
    {
        Hp = 20,
        MaxHp = 20,
        Ac = 12,
        AttackMod = 2,
        Damage = new DiceRoll(1, 6, 1),
        InitiativeMod = 0,
        Speed = 30,
        SightRadius = 5,
        StrMod = 1,
        DexMod = 1,
        ConMod = 1
    };

    /// <summary>
    /// Generate the floor for <paramref name="floorNumber"/> if the session
    /// hasn't loaded it yet, using the deterministic seed scheme
    /// <c>InitialSeed + (floorNumber - 1)</c> shared with
    /// <see cref="DescendService"/>. Returns the existing floor if already
    /// present.
    /// </summary>
    private Floor EnsureFloor(SessionState state, int floorNumber)
    {
        var existing = state.GetFloor(floorNumber);
        if (existing is not null) return existing;

        int seed = state.InitialSeed + (floorNumber - 1);
        var floor = _floorGen.Generate(new GenerationConfig
        {
            SessionId = state.Session.Id,
            FloorNumber = floorNumber,
            Seed = seed
        });
        // +1 enemy per floor depth, capped — same scaling DescendService uses.
        int enemyCount = Math.Min(EntityPlacer.DefaultEnemyCount + (floorNumber - 1), MaxEnemyCount);
        _entityPlacer.Place(floor, new Random(seed ^ 0x5af3107a), enemyCount);
        state.AddFloor(floor);
        return floor;
    }

    private static Position FindStairsUp(Floor floor)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == TileType.StairsUp)
                    return new Position(x, y);
        throw new InvalidOperationException("Generated floor has no stairs-up tile.");
    }
}
