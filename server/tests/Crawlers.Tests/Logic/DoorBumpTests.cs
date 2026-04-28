using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;
using Xunit.Abstractions;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Logic;

public class DoorBumpTests
{
    private readonly ITestOutputHelper _output;
    public DoorBumpTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Bumping_closed_door_opens_it_without_advancing(int seed)
    {
        var floor = new BspFloorGenerator().Generate(new GenerationConfig
        {
            SessionId = Guid.NewGuid(),
            FloorNumber = 1,
            Seed = seed
        });
        new EntityPlacer().Place(floor, new Random(seed ^ 0x5af3107a));

        Assert.NotNull(floor.BossDoor);
        var door = floor.BossDoor!.Value;
        Assert.Equal(TileType.Door, floor.TileGrid[door.X, door.Y].Type);

        // Place the player one tile south of the door.
        var stats = new EntityStats { Hp = 20, MaxHp = 20, Ac = 12, AttackMod = 2, Damage = new DiceRoll(1,6,1), InitiativeMod = 0, Speed = 30, SightRadius = 5, StrMod = 1, DexMod = 1, ConMod = 1 };
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        state.AddFloor(floor);
        var player = new Player {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Position = new Position(door.X, door.Y + 1),
            Stats = stats,
            CurrentFloorNumber = 1
        };
        state.AddPlayer(player);
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));

        _output.WriteLine($"seed={seed} player at {player.Position}, door at {door}, tile under door = {floor.TileGrid[door.X, door.Y].Type}");

        var moved = new MovementService().TryMove(state, player.Id, MoveDirection.North);
        _output.WriteLine($"moved={moved} new player pos={player.Position} new tile={floor.TileGrid[door.X, door.Y].Type}");

        Assert.True(moved);
        Assert.Equal(new Position(door.X, door.Y + 1), player.Position);
        Assert.Equal(TileType.OpenDoor, floor.TileGrid[door.X, door.Y].Type);
    }

    [Fact]
    public void Boss_door_does_not_lock_until_every_player_is_inside()
    {
        // Synthetic floor: 12×8, walls on the rim, with an explicit 4×3 boss
        // room at (4,2)-(8,5) and an open door on its south rim at (5,5).
        const int width = 12, height = 8;
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    ? new Tile(TileType.Wall)
                    : new Tile(TileType.Floor);

        var bounds = new Bounds(4, 2, 4, 3); // x:[4..8), y:[2..5)
        var doorPos = new Position(5, 5);    // one tile south of bounds — on the rim
        grid[doorPos.X, doorPos.Y] = new Tile(TileType.OpenDoor);

        var bossId = Guid.NewGuid();
        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            FloorNumber = 1,
            Width = width,
            Height = height,
            TileGrid = grid,
            BossDoor = doorPos,
            BossRoomBounds = bounds,
            BossEntityId = bossId
        };
        floor.Entities.Add(new Entity
        {
            Id = bossId,
            FloorId = floor.Id,
            Type = EntityType.Enemy,
            Name = "Boss",
            Position = new Position(6, 3),
            State = EntityState.Alive,
            Stats = new EntityStats { Hp = 30, MaxHp = 30 }
        });

        var stats = new EntityStats { Hp = 20, MaxHp = 20, Ac = 12, AttackMod = 2, Damage = new DiceRoll(1, 6, 1), InitiativeMod = 0, Speed = 30, SightRadius = 5 };
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);
        state.AddFloor(floor);

        // A starts inside the boss room. B starts in the corridor below the door.
        var playerA = new Player
        {
            Id = Guid.NewGuid(), SessionId = session.Id,
            Position = new Position(5, 3), Stats = stats, CurrentFloorNumber = 1
        };
        var playerB = new Player
        {
            Id = Guid.NewGuid(), SessionId = session.Id,
            Position = new Position(5, 6), Stats = stats, CurrentFloorNumber = 1
        };
        state.AddPlayer(playerA);
        state.AddPlayer(playerB);
        FieldOfView.RecomputeForFloor(floor, state.GetFog(1)!, state.PlayersOnFloor(1));

        var svc = new MovementService();

        // A moves around inside; B is still outside. Door stays open.
        Assert.True(svc.TryMove(state, playerA.Id, MoveDirection.East));
        Assert.Equal(TileType.OpenDoor, floor.TileGrid[doorPos.X, doorPos.Y].Type);

        // B steps onto the door tile itself (still outside bounds — the door
        // sits on the rim at y=5, bounds end at y<5). Door still open.
        Assert.True(svc.TryMove(state, playerB.Id, MoveDirection.North));
        Assert.Equal(new Position(5, 5), playerB.Position);
        Assert.False(Contains(bounds, playerB.Position));
        Assert.Equal(TileType.OpenDoor, floor.TileGrid[doorPos.X, doorPos.Y].Type);

        // B crosses into the room — now both inside, door locks.
        Assert.True(svc.TryMove(state, playerB.Id, MoveDirection.North));
        Assert.True(Contains(bounds, playerB.Position));
        Assert.Equal(TileType.LockedDoor, floor.TileGrid[doorPos.X, doorPos.Y].Type);
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;
}
