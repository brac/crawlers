using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Unit tests for EnemyAi.FindTarget and EnemyAi.TakeTurn.
/// Each test builds a minimal session (open room, no real floor gen).
/// </summary>
public class EnemyAiTests
{
    // ------------------------------------------------------------------ FindTarget

    [Fact]
    public void FindTarget_returns_player_when_in_LOS_and_within_radius()
    {
        var (state, floor, enemy) = Build(enemyAt: new Position(5, 2), playerAt: new Position(8, 2));
        // Player is 3 tiles east, well within sight radius of 5.
        var target = EnemyAi.FindTarget(state, enemy, floor);
        Assert.NotNull(target);
        Assert.Equal(state.PrimaryPlayer.Id, target!.Id);
    }

    [Fact]
    public void FindTarget_returns_null_when_player_beyond_sight_radius()
    {
        // Enemy sight radius = 4; player placed 6 tiles away.
        var (state, floor, enemy) = Build(
            enemyAt: new Position(2, 2),
            playerAt: new Position(9, 2),
            enemySightRadius: 4,
            roomWidth: 15);
        Assert.Null(EnemyAi.FindTarget(state, enemy, floor));
    }

    [Fact]
    public void FindTarget_returns_null_when_wall_blocks_LOS()
    {
        // Narrow grid with a wall column between enemy and player.
        //  0123456789
        // 0##########
        // 1#E # P   #
        // 2##########
        var grid = new Tile[10, 3];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 10; x++)
                grid[x, y] = new Tile((x == 0 || y == 0 || x == 9 || y == 2 || x == 4)
                    ? TileType.Wall : TileType.Floor);

        var (state, floor, enemy) = BuildRaw(grid, enemyAt: new Position(2, 1), playerAt: new Position(6, 1));
        Assert.Null(EnemyAi.FindTarget(state, enemy, floor));
    }

    [Fact]
    public void FindTarget_ignores_dead_player()
    {
        var (state, floor, enemy) = Build(enemyAt: new Position(3, 2), playerAt: new Position(5, 2));
        state.PrimaryPlayer.Mode = GameMode.Resolution;
        Assert.Null(EnemyAi.FindTarget(state, enemy, floor));
    }

    [Fact]
    public void FindTarget_picks_closer_of_two_players()
    {
        var (state, floor, enemy) = Build(enemyAt: new Position(5, 2), playerAt: new Position(7, 2));
        // Add a second player closer to the enemy.
        var closer = new Player
        {
            Id = Guid.NewGuid(),
            SessionId = state.Session.Id,
            Position = new Position(6, 2),
            Stats = PlayerStats(),
            CurrentFloorNumber = 1,
            Mode = GameMode.Exploration
        };
        state.AddPlayer(closer);

        var target = EnemyAi.FindTarget(state, enemy, floor);
        Assert.Equal(closer.Id, target!.Id);
    }

    // ------------------------------------------------------------------ TakeTurn

    [Fact]
    public void TakeTurn_moves_enemy_one_step_toward_player()
    {
        // Enemy at (2,2), player at (5,2) — enemy should step east.
        var (state, floor, enemy) = Build(enemyAt: new Position(2, 2), playerAt: new Position(5, 2));
        var moved = EnemyAi.TakeTurn(state, enemy, floor);
        Assert.True(moved);
        Assert.Equal(new Position(3, 2), enemy.Position);
    }

    [Fact]
    public void TakeTurn_updates_last_seen_tile_on_visible_target()
    {
        var (state, floor, enemy) = Build(enemyAt: new Position(2, 2), playerAt: new Position(5, 2));
        EnemyAi.TakeTurn(state, enemy, floor);
        Assert.Equal(new Position(5, 2), enemy.LastSeenPlayerTile);
        Assert.Equal(EnemyAi.GiveUpGrace, enemy.GiveUpTicksRemaining);
    }

    [Fact]
    public void TakeTurn_chases_last_seen_tile_after_player_breaks_LOS()
    {
        // Room is 16 wide so the player can be moved beyond sight radius (5).
        var (state, floor, enemy) = Build(
            enemyAt: new Position(2, 2), playerAt: new Position(4, 2), roomWidth: 16);
        EnemyAi.TakeTurn(state, enemy, floor); // spots player, steps to (3,2)

        // Move player to col 10 — distance from enemy (now at 3,2) is 7, outside radius 5.
        state.PrimaryPlayer.Position = new Position(10, 2);

        // Enemy should continue toward last-seen tile (4,2) for GiveUpGrace ticks.
        var moved = EnemyAi.TakeTurn(state, enemy, floor);
        Assert.True(moved);
        Assert.Equal(EnemyAi.GiveUpGrace - 1, enemy.GiveUpTicksRemaining);
    }

    [Fact]
    public void TakeTurn_goes_idle_after_grace_ticks_expire()
    {
        // Wide room so we can exile the player beyond sight radius (5)
        // and give the enemy enough space to chase without hitting a wall
        // before the grace window expires.
        var (state, floor, enemy) = Build(
            enemyAt: new Position(2, 2), playerAt: new Position(4, 2), roomWidth: 60);
        EnemyAi.TakeTurn(state, enemy, floor); // spots player, enemy steps toward (4,2)

        // Exile player — guaranteed > 5 tiles from any position enemy reaches.
        state.PrimaryPlayer.Position = new Position(54, 2);

        // Burn through all grace ticks.
        for (int i = 0; i < EnemyAi.GiveUpGrace; i++)
            EnemyAi.TakeTurn(state, enemy, floor);

        // Grace expired — next tick should return false.
        var moved = EnemyAi.TakeTurn(state, enemy, floor);
        Assert.False(moved);
        Assert.Equal(0, enemy.GiveUpTicksRemaining);
    }

    [Fact]
    public void TakeTurn_does_nothing_without_any_target()
    {
        // Enemy has never seen a player and no player is in range.
        var (state, floor, enemy) = Build(enemyAt: new Position(2, 2), playerAt: new Position(1, 2));
        // Move player outside sight radius.
        state.PrimaryPlayer.Position = new Position(1, 2);
        enemy.Stats = enemy.Stats! with { SightRadius = 1 }; // shrink radius

        // Player is adjacent, which is ≤1 radius. Let's move them farther.
        state.PrimaryPlayer.Position = new Position(9, 2);
        enemy.GiveUpTicksRemaining = 0;
        enemy.LastSeenPlayerTile = null;

        var moved = EnemyAi.TakeTurn(state, enemy, floor);
        Assert.False(moved);
    }

    [Fact]
    public void TakeTurn_blocked_by_wall_returns_false()
    {
        // Enemy is completely surrounded by walls except its own tile.
        var grid = new Tile[5, 5];
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                grid[x, y] = new Tile(TileType.Wall);
        grid[2, 2] = new Tile(TileType.Floor);
        grid[4, 2] = new Tile(TileType.Floor); // player position (unreachable)

        var (state, floor, enemy) = BuildRaw(grid, enemyAt: new Position(2, 2), playerAt: new Position(4, 2));
        var moved = EnemyAi.TakeTurn(state, enemy, floor);
        Assert.False(moved);
    }

    [Fact]
    public void TakeTurn_boss_does_not_leave_room_bounds()
    {
        // Boss room is columns 3-7, rows 1-4 inside a wider floor.
        const int W = 12, H = 7;
        var grid = new Tile[W, H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                grid[x, y] = new Tile(TileType.Floor);

        var bounds = new Bounds(3, 1, 5, 4); // x=3..7, y=1..4
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);

        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            FloorNumber = 1,
            Width = W,
            Height = H,
            TileGrid = grid,
            BossRoomBounds = bounds
        };

        var bossId = Guid.NewGuid();
        floor.BossEntityId = bossId;

        var boss = new Entity
        {
            Id = bossId,
            FloorId = floor.Id,
            Type = EntityType.Enemy,
            Name = "Boss",
            Position = new Position(5, 2), // inside room
            State = EntityState.Alive,
            Stats = new EntityStats { Hp = 60, MaxHp = 60, SightRadius = 8 }
        };
        floor.Entities.Add(boss);

        state.AddFloor(floor);

        // Player is outside the room (col 1), clearly visible across open floor.
        state.AddPlayer(new Player
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Position = new Position(1, 2),
            Stats = PlayerStats(),
            CurrentFloorNumber = 1,
            Mode = GameMode.Exploration
        });

        // Boss should not move because player tile is outside BossRoomBounds.
        for (int i = 0; i < 10; i++)
            EnemyAi.TakeTurn(state, boss, floor);

        // Boss stays inside its room (x in 3..7).
        Assert.True(boss.Position.X >= bounds.X, "Boss left room on west side");
        Assert.True(boss.Position.X < bounds.X + bounds.Width, "Boss left room on east side");
        Assert.True(boss.Position.Y >= bounds.Y, "Boss left room on north side");
        Assert.True(boss.Position.Y < bounds.Y + bounds.Height, "Boss left room on south side");
    }

    [Fact]
    public void Two_enemies_do_not_stack_on_same_tile()
    {
        // Two enemies chasing the same player from the same column.
        const int W = 10, H = 5;
        var grid = new Tile[W, H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                grid[x, y] = new Tile(x == 0 || y == 0 || x == W - 1 || y == H - 1
                    ? TileType.Wall : TileType.Floor);

        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        var floor = new Floor { Id = Guid.NewGuid(), FloorNumber = 1, Width = W, Height = H, TileGrid = grid };

        var e1 = MakeEnemy(floor, new Position(1, 2));
        var e2 = MakeEnemy(floor, new Position(2, 2));
        floor.Entities.Add(e1);
        floor.Entities.Add(e2);
        state.AddFloor(floor);

        state.AddPlayer(new Player
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Position = new Position(7, 2),
            Stats = PlayerStats(),
            CurrentFloorNumber = 1,
            Mode = GameMode.Exploration
        });

        // BFS stable order (by Id) means one wins, the other waits.
        EnemyAi.TakeTurn(state, e1, floor);
        EnemyAi.TakeTurn(state, e2, floor);

        Assert.NotEqual(e1.Position, e2.Position);
    }

    // ------------------------------------------------------------------ helpers

    private static (SessionState state, Floor floor, Entity enemy) Build(
        Position enemyAt,
        Position playerAt,
        int enemySightRadius = 5,
        int roomWidth = 12)
    {
        const int H = 5;
        var grid = new Tile[roomWidth, H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < roomWidth; x++)
                grid[x, y] = new Tile(x == 0 || y == 0 || x == roomWidth - 1 || y == H - 1
                    ? TileType.Wall : TileType.Floor);

        return BuildRaw(grid, enemyAt, playerAt, enemySightRadius);
    }

    private static (SessionState state, Floor floor, Entity enemy) BuildRaw(
        Tile[,] grid,
        Position enemyAt,
        Position playerAt,
        int enemySightRadius = 5)
    {
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            FloorNumber = 1,
            Width = grid.GetLength(0),
            Height = grid.GetLength(1),
            TileGrid = grid
        };

        var enemy = MakeEnemy(floor, enemyAt, enemySightRadius);
        floor.Entities.Add(enemy);
        state.AddFloor(floor);

        state.AddPlayer(new Player
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Position = playerAt,
            Stats = PlayerStats(),
            CurrentFloorNumber = 1,
            Mode = GameMode.Exploration
        });

        return (state, floor, enemy);
    }

    private static Entity MakeEnemy(Floor floor, Position pos, int sightRadius = 5) => new()
    {
        Id = Guid.NewGuid(),
        FloorId = floor.Id,
        Type = EntityType.Enemy,
        Name = "Husk",
        Position = pos,
        State = EntityState.Alive,
        Stats = new EntityStats
        {
            Hp = 16, MaxHp = 16, Ac = 11,
            AttackMod = 2, Damage = new DiceRoll(1, 6, 0),
            InitiativeMod = 0, Speed = 25, SightRadius = sightRadius
        }
    };

    private static EntityStats PlayerStats() => new()
    {
        Hp = 20, MaxHp = 20, Ac = 12,
        AttackMod = 2, Damage = new DiceRoll(1, 6, 1),
        InitiativeMod = 1, Speed = 30, SightRadius = 5
    };
}
