namespace Crawlers.Domain.Models;

public class Floor
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public int FloorNumber { get; init; }
    public int Seed { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public Tile[,] TileGrid { get; set; } = new Tile[0, 0];
    public List<Room> Rooms { get; init; } = new();
    public List<Entity> Entities { get; init; } = new();
}
