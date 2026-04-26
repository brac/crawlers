using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Generation;

public class EntityPlacer
{
    public const int DefaultEnemyCount = 4;

    public void Place(Floor floor, Random rng, int enemyCount = DefaultEnemyCount)
    {
        var spawnRoom = FindSpawnRoom(floor);
        var candidates = spawnRoom is null
            ? floor.Rooms.ToList()
            : floor.Rooms.Where(r => r.Id != spawnRoom.Id).ToList();

        if (candidates.Count == 0) return;

        var occupied = new HashSet<Position>();
        if (FindTile(floor, TileType.StairsUp) is { } up) occupied.Add(up);
        if (FindTile(floor, TileType.StairsDown) is { } down) occupied.Add(down);

        int placed = 0;
        int attempts = 0;
        int maxAttempts = enemyCount * 50;
        while (placed < enemyCount && attempts++ < maxAttempts)
        {
            var room = candidates[rng.Next(candidates.Count)];
            int x = rng.Next(room.Bounds.X, room.Bounds.X + room.Bounds.Width);
            int y = rng.Next(room.Bounds.Y, room.Bounds.Y + room.Bounds.Height);
            var pos = new Position(x, y);
            if (occupied.Contains(pos)) continue;
            if (floor.TileGrid[x, y].Type != TileType.Floor) continue;

            occupied.Add(pos);
            var archetype = (EnemyArchetype)rng.Next(3);
            floor.Entities.Add(EnemyTemplates.Create(archetype, pos, floor.Id));
            placed++;
        }
    }

    private static Room? FindSpawnRoom(Floor floor)
    {
        if (FindTile(floor, TileType.StairsUp) is not { } up) return null;
        return floor.Rooms.FirstOrDefault(r => Contains(r.Bounds, up));
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
