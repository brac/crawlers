using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class Session
{
    public Guid Id { get; init; }
    public Guid PlayerId { get; init; }
    public Guid FloorId { get; set; }
    public int CurrentFloorNumber { get; set; }
    public GameMode Mode { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
