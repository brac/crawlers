using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public record Item
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsConsumable { get; init; }
    public ItemEffect Effect { get; init; } = ItemEffect.None;
    public int EffectValue { get; init; }
}
