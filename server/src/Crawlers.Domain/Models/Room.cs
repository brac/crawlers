namespace Crawlers.Domain.Models;

public record Room
{
    public Guid Id { get; init; }
    public Bounds Bounds { get; init; }
    public string? Name { get; init; }
}
