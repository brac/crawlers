using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

public class MovementServiceTests
{
    [Fact]
    public void Move_into_open_floor_succeeds()
    {
        var (state, _) = BuildSimpleSession(playerAt: new Position(2, 2));
        var svc = new MovementService();

        var ok = svc.TryMove(state, MoveDirection.East);

        Assert.True(ok);
        Assert.Equal(new Position(3, 2), state.Player.Position);
    }

    [Fact]
    public void Move_into_wall_returns_false_and_does_not_change_position()
    {
        // Player flanked east by a wall.
        var (state, _) = BuildSimpleSession(playerAt: new Position(2, 2), wallAt: new Position(3, 2));
        var svc = new MovementService();

        var ok = svc.TryMove(state, MoveDirection.East);

        Assert.False(ok);
        Assert.Equal(new Position(2, 2), state.Player.Position);
    }

    [Fact]
    public void Move_out_of_bounds_returns_false()
    {
        var (state, _) = BuildSimpleSession(playerAt: new Position(0, 0));
        // Floor is bounded by walls in BuildSimpleSession, but be defensive — try moving into the wall.
        var svc = new MovementService();

        var ok = svc.TryMove(state, MoveDirection.West);

        Assert.False(ok);
    }

    [Fact]
    public void Successful_move_recomputes_fog_around_new_position()
    {
        var (state, _) = BuildSimpleSession(playerAt: new Position(1, 1));
        var svc = new MovementService();

        var beforePos = state.Player.Position;
        var beforeVisibleAtNew = state.Player.FogOfWar![2, 1];
        Assert.NotEqual(beforePos, new Position(2, 1));

        svc.TryMove(state, MoveDirection.East);

        Assert.Equal(new Position(2, 1), state.Player.Position);
        // After moving east, the new position is visible.
        Assert.Equal(VisibilityState.Visible, state.Player.FogOfWar![2, 1]);
        // beforeVisibleAtNew was either Visible or Explored; after the move it must be Visible (we're standing on it).
        _ = beforeVisibleAtNew;
    }

    [Fact]
    public void Move_demotes_previously_visible_tiles_to_explored()
    {
        var (state, _) = BuildSimpleSession(playerAt: new Position(1, 1));
        var svc = new MovementService();

        // The player's spawn tile is currently Visible. After moving away (and being far enough that
        // the spawn is outside sight radius), it should become Explored.
        var oldPos = state.Player.Position;
        Assert.Equal(VisibilityState.Visible, state.Player.FogOfWar![oldPos.X, oldPos.Y]);

        // Move east 6 times — sight radius is 5, so the spawn should drop out of LOS.
        for (int i = 0; i < 6; i++)
            svc.TryMove(state, MoveDirection.East);

        Assert.Equal(VisibilityState.Explored, state.Player.FogOfWar![oldPos.X, oldPos.Y]);
    }

    private static (SessionState state, Floor floor) BuildSimpleSession(
        Position playerAt,
        Position? wallAt = null)
    {
        // 10x5 walled room. Optionally drop a wall on a specific tile.
        const int width = 10, height = 5;
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);

        if (wallAt is { } w)
            grid[w.X, w.Y] = new Tile(TileType.Wall);

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
        return (new SessionState(session, floor, player), floor);
    }
}
