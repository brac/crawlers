using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;
using Xunit.Abstractions;

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
        var player = new Player {
            Id = Guid.NewGuid(), SessionId = Guid.NewGuid(),
            Position = new Position(door.X, door.Y + 1),
            Stats = stats,
            FogOfWar = new VisibilityState[floor.Width, floor.Height]
        };
        FieldOfView.Compute(floor, player.Position, stats.SightRadius, player.FogOfWar);
        var session = new Session { Id = Guid.NewGuid(), PlayerId = player.Id, FloorId = floor.Id, CurrentFloorNumber = 1, Mode = GameMode.Exploration };
        var state = new SessionState(session, floor, player) { InitialSeed = seed };

        _output.WriteLine($"seed={seed} player at {player.Position}, door at {door}, tile under door = {floor.TileGrid[door.X, door.Y].Type}");

        var moved = new MovementService().TryMove(state, MoveDirection.North);
        _output.WriteLine($"moved={moved} new player pos={player.Position} new tile={floor.TileGrid[door.X, door.Y].Type}");

        Assert.True(moved);
        Assert.Equal(new Position(door.X, door.Y + 1), player.Position); // player did not advance
        Assert.Equal(TileType.OpenDoor, floor.TileGrid[door.X, door.Y].Type);
    }
}
