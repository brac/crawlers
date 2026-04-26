using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

public class EngagementServiceTests
{
    [Fact]
    public void Returns_null_when_no_enemies()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        Assert.Null(new EngagementService().FindEngagement(state));
    }

    [Fact]
    public void Triggers_when_enemy_adjacent_and_visible()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));

        var found = new EngagementService().FindEngagement(state);
        Assert.NotNull(found);
        Assert.Equal(enemy.Id, found!.Id);
    }

    [Fact]
    public void Ignores_enemy_within_proximity_but_hidden()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));
        // Force-hide the enemy tile, simulating fog (e.g., wall in between).
        state.Player.FogOfWar![enemy.Position.X, enemy.Position.Y] = VisibilityState.Hidden;

        Assert.Null(new EngagementService().FindEngagement(state));
    }

    [Fact]
    public void Ignores_enemy_visible_but_beyond_proximity()
    {
        // Build a wider room so we can place an enemy far enough away to be visible but out of proximity.
        var state = BuildSession(playerAt: new Position(2, 2), width: 20, height: 5);
        AddEnemy(state, new Position(2 + EngagementService.EngagementProximity + 1, 2));

        Assert.Null(new EngagementService().FindEngagement(state));
    }

    [Fact]
    public void Ignores_dead_enemy()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));
        enemy.State = EntityState.Dead;

        Assert.Null(new EngagementService().FindEngagement(state));
    }

    private static SessionState BuildSession(Position playerAt, int width = 10, int height = 5)
    {
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);

        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            Width = width,
            Height = height,
            TileGrid = grid
        };

        var stats = new EntityStats { Hp = 10, MaxHp = 10, SightRadius = 5 };
        var fog = new VisibilityState[width, height];
        FieldOfView.Compute(floor, playerAt, stats.SightRadius, fog);

        var session = new Session
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            FloorId = floor.Id,
            Mode = GameMode.Exploration,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var player = new Player
        {
            Id = session.PlayerId,
            SessionId = session.Id,
            Position = playerAt,
            Stats = stats,
            FogOfWar = fog
        };
        return new SessionState(session, floor, player);
    }

    private static Entity AddEnemy(SessionState state, Position p)
    {
        var enemy = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = state.Floor.Id,
            Type = EntityType.Enemy,
            Name = "Husk",
            Position = p,
            State = EntityState.Alive,
            Stats = new EntityStats { Hp = 8, MaxHp = 8, SightRadius = 4 }
        };
        state.Floor.Entities.Add(enemy);
        // Make sure FOV reflects this position as Visible if within range.
        FieldOfView.Compute(state.Floor, state.Player.Position, state.Player.Stats.SightRadius, state.Player.FogOfWar!);
        return enemy;
    }
}
