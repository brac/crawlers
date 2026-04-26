using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class Player
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Position Position { get; set; }
    public EntityStats Stats { get; set; } = new();
    public List<Item> Inventory { get; init; } = new();
    public VisibilityState[,]? FogOfWar { get; set; }
}
