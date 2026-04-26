namespace Crawlers.Generation;

public record GenerationConfig
{
    public int Width { get; init; } = 80;
    public int Height { get; init; } = 50;
    public int Seed { get; init; }
    public int FloorNumber { get; init; } = 1;
    public Guid SessionId { get; init; }

    public int MinPartitionSize { get; init; } = 12;
    public int MinRoomSize { get; init; } = 4;
    public int MaxDepth { get; init; } = 5;
    public int RoomPadding { get; init; } = 1;
}
