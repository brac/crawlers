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
    string? Flavor,
    // Content-and-Depth Step 1 — per-floor color tint sourced from
    // floor-scaling.json. Hex string ("#rrggbb"); "#ffffff" = no tint.
    // Applied multiplicatively to the world container on the client.
    string Tint,
    // Step 9 — display name for the title card. Cycle-aware (e.g.
    // "The Crypt" / "Deeper Crypt" / "Forgotten Crypt") so endless
    // descent stays narratively legible. Computed by FloorNameResolver.
    string FloorName
);

public record TileHeatDto(int X, int Y, int Count);
