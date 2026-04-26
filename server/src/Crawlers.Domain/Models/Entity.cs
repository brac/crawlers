using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public class Entity
{
    public Guid Id { get; init; }
    public Guid FloorId { get; init; }
    public EntityType Type { get; init; }
    public string? Name { get; set; }
    public Position Position { get; set; }
    public EntityStats? Stats { get; set; }
    public EntityState State { get; set; }

    /// <summary>The actual Item carried, when Type == EntityType.Item.</summary>
    public Item? Item { get; set; }
}
