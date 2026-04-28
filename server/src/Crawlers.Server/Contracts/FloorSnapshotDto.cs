namespace Crawlers.Server.Contracts;

public record FloorSnapshotDto(
    int Width,
    int Height,
    int[] Tiles,
    int[] Visibility,
    IReadOnlyList<RoomDto> Rooms,
    IReadOnlyList<EntityDto> Entities,
    // Step 9 — sparse list of (x, y, count) entries for tiles where
    // multiple players have died across the world's history. Drives
    // environmental tile-tinting on the client; not a UI overlay.
    IReadOnlyList<TileHeatDto> Heatmap,
    // Step 12 — bleak announcer line for this floor. Same string for
    // every snapshot of this floor in this session (frozen at hydration);
    // the client detects floor change to fade it in for a few seconds.
    string? Flavor
);

public record TileHeatDto(int X, int Y, int Count);
