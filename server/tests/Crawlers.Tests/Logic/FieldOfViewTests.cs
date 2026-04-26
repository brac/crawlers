using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Xunit;

namespace Crawlers.Tests.Logic;

public class FieldOfViewTests
{
    [Fact]
    public void Bresenham_line_horizontal()
    {
        var line = FieldOfView.BresenhamLine(new Position(0, 0), new Position(3, 0))
            .ToArray();
        Assert.Equal(
            new[] { new Position(0, 0), new Position(1, 0), new Position(2, 0), new Position(3, 0) },
            line);
    }

    [Fact]
    public void Bresenham_line_diagonal()
    {
        var line = FieldOfView.BresenhamLine(new Position(0, 0), new Position(3, 3))
            .ToArray();
        Assert.Equal(
            new[] { new Position(0, 0), new Position(1, 1), new Position(2, 2), new Position(3, 3) },
            line);
    }

    [Fact]
    public void Bresenham_line_includes_both_endpoints()
    {
        var line = FieldOfView.BresenhamLine(new Position(2, 5), new Position(7, 9))
            .ToArray();
        Assert.Equal(new Position(2, 5), line.First());
        Assert.Equal(new Position(7, 9), line.Last());
    }

    [Fact]
    public void Wall_blocks_tiles_behind_it()
    {
        // Layout (x→):  # . # . . . #
        // Player at (1,1). Wall at (2,1). Tiles (3..5,1) are behind the wall.
        var floor = BuildFloor(new[]
        {
            "#######",
            "#.#...#",
            "#######",
        });
        var fog = new VisibilityState[floor.Width, floor.Height];

        FieldOfView.Compute(floor, new Position(1, 1), radius: 5, fog);

        Assert.Equal(VisibilityState.Visible, fog[1, 1]); // player
        Assert.Equal(VisibilityState.Visible, fog[2, 1]); // wall surface — we see the wall
        Assert.Equal(VisibilityState.Hidden, fog[3, 1]);  // behind wall
        Assert.Equal(VisibilityState.Hidden, fog[4, 1]);
        Assert.Equal(VisibilityState.Hidden, fog[5, 1]);
    }

    [Fact]
    public void Tiles_outside_sight_radius_remain_hidden()
    {
        var floor = BuildFloor(Repeat(".", count: 20, rows: 1));
        var fog = new VisibilityState[floor.Width, floor.Height];

        FieldOfView.Compute(floor, new Position(0, 0), radius: 3, fog);

        Assert.Equal(VisibilityState.Visible, fog[0, 0]);
        Assert.Equal(VisibilityState.Visible, fog[3, 0]);
        Assert.Equal(VisibilityState.Hidden, fog[4, 0]);
        Assert.Equal(VisibilityState.Hidden, fog[10, 0]);
    }

    [Fact]
    public void Recompute_demotes_previously_visible_to_explored()
    {
        var floor = BuildFloor(Repeat(".", count: 10, rows: 1));
        var fog = new VisibilityState[floor.Width, floor.Height];

        FieldOfView.Compute(floor, new Position(0, 0), radius: 3, fog);
        Assert.Equal(VisibilityState.Visible, fog[3, 0]);

        // Move far away; the previously-visible tile should now be Explored, not Hidden.
        FieldOfView.Compute(floor, new Position(9, 0), radius: 1, fog);

        Assert.Equal(VisibilityState.Explored, fog[3, 0]);
        Assert.Equal(VisibilityState.Visible, fog[9, 0]);
    }

    [Fact]
    public void Hidden_tiles_stay_hidden_when_recomputing_from_unrelated_position()
    {
        var floor = BuildFloor(new[]
        {
            "..........",
            "..........",
            "..........",
        });
        var fog = new VisibilityState[floor.Width, floor.Height];

        FieldOfView.Compute(floor, new Position(0, 0), radius: 1, fog);
        Assert.Equal(VisibilityState.Hidden, fog[5, 2]);

        FieldOfView.Compute(floor, new Position(0, 0), radius: 1, fog);
        Assert.Equal(VisibilityState.Hidden, fog[5, 2]);
    }

    private static Floor BuildFloor(string[] rows)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var grid = new Tile[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = rows[y][x] switch
                {
                    '#' => new Tile(TileType.Wall),
                    '.' => new Tile(TileType.Floor),
                    _ => new Tile(TileType.Floor),
                };
        return new Floor { Width = width, Height = height, TileGrid = grid };
    }

    private static string[] Repeat(string ch, int count, int rows)
    {
        var row = string.Concat(Enumerable.Repeat(ch, count));
        return Enumerable.Repeat(row, rows).ToArray();
    }
}
