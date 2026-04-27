using System.Text;
using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

public static class FloorAsciiRenderer
{
    public static string Render(Floor floor)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < floor.Height; y++)
        {
            for (int x = 0; x < floor.Width; x++)
            {
                sb.Append(floor.TileGrid[x, y].Type switch
                {
                    TileType.Wall => '#',
                    TileType.Floor => '.',
                    TileType.Door => '+',
                    TileType.OpenDoor => '/',
                    TileType.LockedDoor => 'L',
                    TileType.StairsUp => '<',
                    TileType.StairsDown => '>',
                    _ => '?'
                });
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
