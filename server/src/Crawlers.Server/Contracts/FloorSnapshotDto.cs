namespace Crawlers.Server.Contracts;

public record FloorSnapshotDto(
    int Width,
    int Height,
    int[] Tiles,
    int[] Visibility,
    IReadOnlyList<RoomDto> Rooms,
    IReadOnlyList<EntityDto> Entities
);
