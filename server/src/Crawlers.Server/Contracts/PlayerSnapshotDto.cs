namespace Crawlers.Server.Contracts;

public record PlayerSnapshotDto(
    Guid Id,
    int X,
    int Y,
    int Hp,
    int MaxHp,
    IReadOnlyList<ItemDto> Inventory
);
