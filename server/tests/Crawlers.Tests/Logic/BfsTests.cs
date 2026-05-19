using Crawlers.Domain.Models;
using Crawlers.Generation.Pathfinding;

namespace Crawlers.Tests.Logic;

public class BfsTests
{
    [Fact]
    public void Returns_null_when_already_at_goal()
    {
        var pos = new Position(1, 1);
        Assert.Null(Bfs.NextStep(_ => true, pos, pos, 10));
    }

    [Fact]
    public void Returns_next_step_in_open_corridor()
    {
        // .....
        // .S.T.
        // .....
        // S=(1,1) heading east toward T=(3,1)
        var from = new Position(1, 1);
        var to = new Position(3, 1);
        var step = Bfs.NextStep(_ => true, from, to, 10);
        Assert.Equal(new Position(2, 1), step);
    }

    [Fact]
    public void Steps_north_when_goal_is_directly_north()
    {
        var from = new Position(3, 3);
        var to = new Position(3, 1);
        var step = Bfs.NextStep(_ => true, from, to, 10);
        Assert.Equal(new Position(3, 2), step);
    }

    [Fact]
    public void Returns_null_when_goal_unreachable_within_radius()
    {
        // Goal is 10 tiles away, radius capped at 3.
        var from = new Position(0, 0);
        var to = new Position(10, 0);
        Assert.Null(Bfs.NextStep(_ => true, from, to, 3));
    }

    [Fact]
    public void Returns_null_when_goal_completely_walled_off()
    {
        // Only (0,0) is walkable; everything else is blocked.
        var from = new Position(0, 0);
        var to = new Position(5, 0);
        Assert.Null(Bfs.NextStep(p => p == from, from, to, 10));
    }

    [Fact]
    public void Routes_around_obstacle()
    {
        // Grid (# = wall, . = floor):
        // .....
        // .S#T.
        // .....
        // Direct east path blocked at (2,1); must route via (1,0) or (1,2).

        bool Walkable(Position p) =>
            p.X >= 0 && p.Y >= 0 && p.X < 5 && p.Y < 3
            && !(p.X == 2 && p.Y == 1); // wall at (2,1)

        var step = Bfs.NextStep(Walkable, new Position(1, 1), new Position(3, 1), 10);
        // First step must go north or south to go around the wall.
        Assert.NotNull(step);
        Assert.True(step!.Value.Y != 1, "Expected vertical detour around wall");
    }

    [Fact]
    public void Reaches_adjacent_tile_in_one_step()
    {
        var from = new Position(2, 2);
        var to = new Position(2, 3);
        var step = Bfs.NextStep(_ => true, from, to, 5);
        Assert.Equal(to, step);
    }

    [Fact]
    public void Destination_only_walkable_for_last_step()
    {
        // Destination tile is blocked to all but the final BFS step;
        // the predicate allows the destination tile so BFS can reach it.
        var from = new Position(0, 0);
        var to = new Position(0, 2);
        bool Walkable(Position p) => p == to || p == from || p == new Position(0, 1);
        var step = Bfs.NextStep(Walkable, from, to, 5);
        Assert.Equal(new Position(0, 1), step);
    }
}
