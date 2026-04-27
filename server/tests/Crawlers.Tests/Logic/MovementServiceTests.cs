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
        var state = BuildSimpleSession(playerAt: new Position(2, 2));
        var svc = new MovementService();

        var ok = svc.TryMove(state, state.PrimaryPlayer.Id, MoveDirection.East);

        Assert.True(ok);
        Assert.Equal(new Position(3, 2), state.PrimaryPlayer.Position);
    }

    [Fact]
    public void Move_into_wall_returns_false_and_does_not_change_position()
    {
        var state = BuildSimpleSession(playerAt: new Position(2, 2), wallAt: new Position(3, 2));
        var svc = new MovementService();

        var ok = svc.TryMove(state, state.PrimaryPlayer.Id, MoveDirection.East);

        Assert.False(ok);
        Assert.Equal(new Position(2, 2), state.PrimaryPlayer.Position);
    }

    [Fact]
    public void Move_out_of_bounds_returns_false()
    {
        var state = BuildSimpleSession(playerAt: new Position(0, 0));
        var svc = new MovementService();

        var ok = svc.TryMove(state, state.PrimaryPlayer.Id, MoveDirection.West);

        Assert.False(ok);
    }

    [Fact]
    public void Successful_move_recomputes_fog_around_new_position()
    {
        var state = BuildSimpleSession(playerAt: new Position(1, 1));
        var svc = new MovementService();

        Assert.NotEqual(state.PrimaryPlayer.Position, new Position(2, 1));

        svc.TryMove(state, state.PrimaryPlayer.Id, MoveDirection.East);

        Assert.Equal(new Position(2, 1), state.PrimaryPlayer.Position);
        Assert.Equal(VisibilityState.Visible, state.GetFog(1)![2, 1]);
    }

    [Fact]
    public void Move_demotes_previously_visible_tiles_to_explored()
    {
        var state = BuildSimpleSession(playerAt: new Position(1, 1));
        var svc = new MovementService();

        var oldPos = state.PrimaryPlayer.Position;
        Assert.Equal(VisibilityState.Visible, state.GetFog(1)![oldPos.X, oldPos.Y]);

        // Move east 6 times — sight radius is 5, so the spawn drops out of LOS.
        for (int i = 0; i < 6; i++)
            svc.TryMove(state, state.PrimaryPlayer.Id, MoveDirection.East);

        Assert.Equal(VisibilityState.Explored, state.GetFog(1)![oldPos.X, oldPos.Y]);
    }

    [Fact]
    public void Cannot_move_onto_a_teammates_tile()
    {
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(3, 2));
        var svc = new MovementService();

        var ok = svc.TryMove(state, state.Players[0].Id, MoveDirection.East);

        Assert.False(ok);
        Assert.Equal(new Position(2, 2), state.Players[0].Position);
        Assert.Equal(new Position(3, 2), state.Players[1].Position);
    }

    [Fact]
    public void Same_tile_contention_first_call_wins_second_call_blocked()
    {
        // A at (2,2), B at (4,2). Both reach for (3,2). Under the SessionState
        // SyncRoot the calls serialize, so "simultaneous" tick = sequential
        // resolution: first claim wins, second bounces.
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(4, 2));
        var svc = new MovementService();

        var aOk = svc.TryMove(state, state.Players[0].Id, MoveDirection.East);
        var bOk = svc.TryMove(state, state.Players[1].Id, MoveDirection.West);

        Assert.True(aOk);
        Assert.False(bOk);
        Assert.Equal(new Position(3, 2), state.Players[0].Position);
        Assert.Equal(new Position(4, 2), state.Players[1].Position);
    }

    [Fact]
    public void Vacated_tile_is_reusable_by_a_teammate()
    {
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(3, 2));
        var svc = new MovementService();

        // A steps off (2,2) — B can now back into it.
        Assert.True(svc.TryMove(state, state.Players[0].Id, MoveDirection.North));
        Assert.True(svc.TryMove(state, state.Players[1].Id, MoveDirection.West));

        Assert.Equal(new Position(2, 1), state.Players[0].Position);
        Assert.Equal(new Position(2, 2), state.Players[1].Position);
    }

    [Fact]
    public void Teammate_on_a_different_floor_does_not_block()
    {
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(3, 2));
        // Pretend B descended; their position on a different floor is irrelevant
        // to A's collision check. We don't need to actually load floor 2 — the
        // PlayersOnFloor filter just iterates by CurrentFloorNumber.
        state.Players[1].CurrentFloorNumber = 2;

        var ok = new MovementService().TryMove(state, state.Players[0].Id, MoveDirection.East);

        Assert.True(ok);
        Assert.Equal(new Position(3, 2), state.Players[0].Position);
    }

    [Fact]
    public void Dead_teammate_does_not_block_movement()
    {
        // A and B adjacent. Mark A's mode as Resolution (dead, pinned to tile).
        // B should be able to step onto A's tile — corpses don't block.
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(3, 2));
        state.Players[0].Mode = GameMode.Resolution;

        var ok = new MovementService().TryMove(state, state.Players[1].Id, MoveDirection.West);

        Assert.True(ok);
        Assert.Equal(new Position(2, 2), state.Players[1].Position);
    }

    [Fact]
    public void Corpse_entity_on_target_tile_does_not_block_movement()
    {
        // Drop a Corpse entity on the player's path. Movement service only
        // checks tile-walkability + living teammates, so the corpse is
        // walked-through.
        var state = BuildSimpleSession(playerAt: new Position(2, 2));
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Corpse,
            Name = "Corpse",
            Position = new Position(3, 2),
            State = EntityState.Alive,
            PlayerId = Guid.NewGuid()
        });

        var ok = new MovementService().TryMove(state, state.PrimaryPlayer.Id, MoveDirection.East);

        Assert.True(ok);
        Assert.Equal(new Position(3, 2), state.PrimaryPlayer.Position);
    }

    [Fact]
    public void Shared_fog_unions_LOS_of_both_players_on_the_floor()
    {
        // Two players standing at opposite ends of the room — each sees their
        // own neighborhood, the shared fog reflects both visibility cones.
        var state = BuildTwoPlayer(
            playerAPos: new Position(2, 2),
            playerBPos: new Position(7, 2));
        var fog = state.GetFog(1)!;

        // Each player's tile is necessarily in their own LOS, so both must
        // be Visible in the shared fog.
        Assert.Equal(VisibilityState.Visible, fog[2, 2]);
        Assert.Equal(VisibilityState.Visible, fog[7, 2]);
    }

    [Fact]
    public void One_players_move_updates_the_shared_fog_for_their_teammate()
    {
        // Setup: A at (2,2), B at (7,2). Tile (5,2) is in neither's LOS at
        // sight radius 5? Actually (5,2) is 3 from A and 2 from B, so it'll be
        // in B's LOS already. Use a tile only A can reveal: A moves toward
        // a tile that was previously beyond their original radius and confirm
        // the shared fog now sees it.
        var state = BuildTwoPlayer(
            playerAPos: new Position(1, 1),
            playerBPos: new Position(8, 3));
        var fog = state.GetFog(1)!;

        // Tile (4, 1) is 3 away from A initially — in LOS — but pick a tile
        // far from B that only A can reach. (3,1) — within A's sight radius.
        Assert.Equal(VisibilityState.Visible, fog[3, 1]);

        // Move A east; (3,1) stays Visible thanks to A. B does nothing.
        Assert.True(new MovementService().TryMove(state, state.Players[0].Id, MoveDirection.East));
        Assert.Equal(VisibilityState.Visible, state.GetFog(1)![3, 1]);
    }

    private static SessionState BuildTwoPlayer(Position playerAPos, Position playerBPos)
    {
        var state = BuildEmpty(width: 10, height: 5, wallAt: null);
        var floor = state.GetFloor(1)!;
        var stats = new EntityStats { Hp = 10, MaxHp = 10, SightRadius = 5 };
        foreach (var pos in new[] { playerAPos, playerBPos })
        {
            state.AddPlayer(new Player
            {
                Id = Guid.NewGuid(),
                SessionId = state.Session.Id,
                Position = pos,
                Stats = stats,
                CurrentFloorNumber = 1
            });
        }
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));
        return state;
    }

    private static SessionState BuildSimpleSession(Position playerAt, Position? wallAt = null)
    {
        var state = BuildEmpty(width: 10, height: 5, wallAt: wallAt);
        var floor = state.GetFloor(1)!;
        var stats = new EntityStats { Hp = 10, MaxHp = 10, SightRadius = 5 };
        state.AddPlayer(new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = playerAt,
            Stats = stats,
            CurrentFloorNumber = 1
        });
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));
        return state;
    }

    private static SessionState BuildEmpty(int width, int height, Position? wallAt)
    {
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);
        if (wallAt is { } w) grid[w.X, w.Y] = new Tile(TileType.Wall);

        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            FloorNumber = 1,
            Width = width,
            Height = height,
            TileGrid = grid
        };
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        state.AddFloor(floor);
        return state;
    }
}
