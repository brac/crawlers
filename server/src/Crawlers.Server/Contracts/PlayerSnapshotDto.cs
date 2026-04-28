namespace Crawlers.Server.Contracts;

public record PlayerSnapshotDto(
    Guid Id,
    string Username,
    int X,
    int Y,
    int Hp,
    int MaxHp,
    IReadOnlyList<ItemDto> Inventory
);
