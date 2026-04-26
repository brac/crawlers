using System.Collections.Concurrent;
using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Logic;

namespace Crawlers.Server.Sessions;

public class SessionManager
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly BspFloorGenerator _floorGen = new();
    private readonly EntityPlacer _entityPlacer = new();

    public SessionState CreateSession(int? seed = null)
    {
        var sessionId = Guid.NewGuid();
        var actualSeed = seed ?? Random.Shared.Next();

        var floor = _floorGen.Generate(new GenerationConfig
        {
            SessionId = sessionId,
            FloorNumber = 1,
            Seed = actualSeed
        });

        // Use a separate rng stream for entity placement so floor and entity
        // placements remain stable independently if either algorithm changes.
        _entityPlacer.Place(floor, new Random(actualSeed ^ 0x5af3107a));

        var session = new Session
        {
            Id = sessionId,
            PlayerId = Guid.NewGuid(),
            FloorId = floor.Id,
            CurrentFloorNumber = 1,
            Mode = GameMode.Exploration,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var spawn = FindStairsUp(floor);
        var stats = DefaultPlayerStats();
        var fog = new VisibilityState[floor.Width, floor.Height];
        FieldOfView.Compute(floor, spawn, stats.SightRadius, fog);

        var player = new Player
        {
            Id = session.PlayerId,
            SessionId = sessionId,
            Position = spawn,
            Stats = stats,
            FogOfWar = fog
        };

        var state = new SessionState(session, floor, player) { InitialSeed = actualSeed };
        _sessions[sessionId] = state;
        return state;
    }

    public SessionState? Get(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;

    public bool Remove(Guid sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public int ActiveCount => _sessions.Count;

    private static Position FindStairsUp(Floor floor)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == TileType.StairsUp)
                    return new Position(x, y);
        throw new InvalidOperationException("Generated floor has no stairs-up tile.");
    }

    private static EntityStats DefaultPlayerStats() => new()
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
}
