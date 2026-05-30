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
        Assert.Null(new EngagementService().FindEngagement(state, state.PrimaryPlayer.Id));
    }

    [Fact]
    public void Triggers_when_enemy_adjacent_and_visible()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));

        var found = new EngagementService().FindEngagement(state, state.PrimaryPlayer.Id);
        Assert.NotNull(found);
        Assert.Equal(enemy.Id, found!.Id);
    }

    [Fact]
    public void Ignores_enemy_within_proximity_but_hidden()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));
        // Force-hide the enemy tile in the shared fog, simulating LOS broken
        // by an intervening wall.
        state.GetFog(1)![enemy.Position.X, enemy.Position.Y] = VisibilityState.Hidden;

        Assert.Null(new EngagementService().FindEngagement(state, state.PrimaryPlayer.Id));
    }

    [Fact]
    public void Ignores_enemy_visible_but_beyond_proximity()
    {
        // Build a wider room so we can place an enemy far enough away to be visible but out of proximity.
        var state = BuildSession(playerAt: new Position(2, 2), width: 20, height: 5);
        AddEnemy(state, new Position(2 + EngagementService.EngagementProximity + 1, 2));

        Assert.Null(new EngagementService().FindEngagement(state, state.PrimaryPlayer.Id));
    }

    [Fact]
    public void Ignores_dead_enemy()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));
        enemy.State = EntityState.Dead;

        Assert.Null(new EngagementService().FindEngagement(state, state.PrimaryPlayer.Id));
    }

    [Fact]
    public void PlayersInRange_includes_exploring_player_next_to_enemy()
    {
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));

        var inRange = new EngagementService()
            .PlayersInRange(state, state.GetFloorFor(state.PrimaryPlayer), enemy);

        Assert.Contains(inRange, p => p.Id == state.PrimaryPlayer.Id);
    }

    [Fact]
    public void PlayersInRange_excludes_player_already_in_combat()
    {
        // Regression: a player already locked in another fight must not be
        // pulled into a fresh engagement. CombatService.Start re-binds every
        // returned participant (state.SetCombat + Mode=Combat), so returning a
        // Combat-mode player here would orphan their original combat.
        var state = BuildSession(playerAt: new Position(2, 2));
        var enemy = AddEnemy(state, new Position(3, 2));

        var busy = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = new Position(3, 3), // Chebyshev 1 from the enemy
            Stats = new EntityStats { Hp = 10, MaxHp = 10, SightRadius = 5 },
            CurrentFloorNumber = 1,
            Mode = GameMode.Combat
        };
        state.AddPlayer(busy);

        var inRange = new EngagementService()
            .PlayersInRange(state, state.GetFloorFor(state.PrimaryPlayer), enemy);

        Assert.Contains(inRange, p => p.Id == state.PrimaryPlayer.Id);
        Assert.DoesNotContain(inRange, p => p.Id == busy.Id);
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
            FloorNumber = 1,
            Width = width,
            Height = height,
            TileGrid = grid
        };

        var stats = new EntityStats { Hp = 10, MaxHp = 10, SightRadius = 5 };
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        state.AddFloor(floor);
        state.AddPlayer(new Player
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Position = playerAt,
            Stats = stats,
            CurrentFloorNumber = 1
        });
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));
        return state;
    }

    private static Entity AddEnemy(SessionState state, Position p)
    {
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var enemy = new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Enemy,
            Name = "Husk",
            Position = p,
            State = EntityState.Alive,
            Stats = new EntityStats { Hp = 8, MaxHp = 8, SightRadius = 4 }
        };
        floor.Entities.Add(enemy);
        // Refresh shared fog so the new entity's tile is Visible if in range.
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));
        return enemy;
    }
}
