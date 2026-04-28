using Crawlers.Domain.Models;

namespace Crawlers.Server.Persistence;

/// <summary>
/// In-memory fallback used when ConnectionStrings:DefaultConnection is empty.
/// Mints canonical floor templates the same way <see cref="FloorWorldService"/>
/// does — every session in this process sees the same dungeon — but the
/// templates are lost on server restart. The world is "canonical for the
/// process lifetime."
/// </summary>
public class NullFloorWorldService : IFloorWorldService
{
    private readonly ILogger<NullFloorWorldService> _logger;
    private readonly int _baseSeed;
    private readonly object _cacheLock = new();
    private readonly Dictionary<int, FloorTemplate> _templates = new();

    public NullFloorWorldService(ILogger<NullFloorWorldService> logger, IConfiguration config)
    {
        _logger = logger;
        _baseSeed = config.GetValue<int?>("World:BaseSeed") ?? 0x1afe5c3;
    }

    public Task MintAsync(int maxFloor, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            for (int n = 1; n <= maxFloor; n++)
            {
                if (_templates.ContainsKey(n)) continue;
                _templates[n] = FloorTemplate.Mint(n, _baseSeed);
            }
        }
        _logger.LogInformation(
            "Floor persistence disabled — minted {Count} floors in-memory only (lost on restart).",
            maxFloor);
        return Task.CompletedTask;
    }

    public Floor LoadFloorForSession(int floorNumber, Guid sessionId)
    {
        FloorTemplate template;
        lock (_cacheLock)
        {
            if (!_templates.TryGetValue(floorNumber, out var existing))
            {
                existing = FloorTemplate.Mint(floorNumber, _baseSeed);
                _templates[floorNumber] = existing;
            }
            template = existing;
        }
        return template.CloneForSession(sessionId);
    }
}
