using System.Text.Json;
using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Postgres-backed implementation of <see cref="IFloorWorldService"/>.
/// Mints floors at startup, persists them, and caches the resulting
/// <see cref="FloorTemplate"/> set in memory so per-session floor loads are
/// a synchronous in-memory clone (no DB round-trip on the hot path).
///
/// <para>
/// Lazy mint for floors past the initial range happens inside the cache
/// lock to serialize concurrent descenders. Persistence of those lazy
/// mints uses a fresh DbContext via <see cref="IServiceScopeFactory"/>
/// because this service is a singleton.
/// </para>
/// </summary>
public class FloorWorldService : IFloorWorldService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FloorWorldService> _logger;
    private readonly int _baseSeed;

    private readonly object _cacheLock = new();
    private readonly Dictionary<int, FloorTemplate> _templates = new();

    public FloorWorldService(
        IServiceScopeFactory scopes,
        ILogger<FloorWorldService> logger,
        IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        // Configurable base seed lets a fresh deployment pick a different
        // canonical world without code changes. Default constant ensures
        // local dev gets a stable dungeon across restarts.
        _baseSeed = config.GetValue<int?>("World:BaseSeed") ?? 0x1afe5c3;
    }

    public async Task MintAsync(int maxFloor, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();

        // Wipe rows whose Version no longer matches the current code.
        // Corpses on those floors go too — the regenerated tile grid would
        // place them in geometry that doesn't exist anymore.
        var stale = await db.Floors
            .Where(f => f.Version != WorldConstants.Version)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            var staleNumbers = stale.Select(f => f.FloorNumber).ToList();
            db.Floors.RemoveRange(stale);
            var staleCorpses = await db.Corpses
                .Where(c => staleNumbers.Contains(c.FloorNumber))
                .ToListAsync(ct);
            if (staleCorpses.Count > 0)
                db.Corpses.RemoveRange(staleCorpses);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Wiped {FloorCount} stale floor rows and {CorpseCount} corpses for world version bump → {Version}",
                stale.Count, staleCorpses.Count, WorldConstants.Version);
        }

        var present = await db.Floors
            .Where(f => f.FloorNumber >= 1 && f.FloorNumber <= maxFloor)
            .ToListAsync(ct);
        var byNumber = present.ToDictionary(f => f.FloorNumber);

        for (int n = 1; n <= maxFloor; n++)
        {
            if (byNumber.TryGetValue(n, out var row))
            {
                _templates[n] = Deserialize(row);
                continue;
            }

            var template = FloorTemplate.Mint(n, _baseSeed);
            _templates[n] = template;
            db.Floors.Add(Serialize(template));
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Minted canonical floor {FloorNumber} (seed {Seed}, version {Version})",
                n, template.Seed, WorldConstants.Version);
        }
    }

    public Floor LoadFloorForSession(int floorNumber, Guid sessionId)
    {
        FloorTemplate template;
        lock (_cacheLock)
        {
            if (!_templates.TryGetValue(floorNumber, out var existing))
            {
                // Lazy mint for floors past the initial range. Synchronous —
                // the BSP generator is fast (single-digit ms) and we're
                // already inside the session lock at the call site.
                existing = FloorTemplate.Mint(floorNumber, _baseSeed);
                _templates[floorNumber] = existing;
                PersistAsync(existing).GetAwaiter().GetResult();
                _logger.LogInformation(
                    "Lazy-minted canonical floor {FloorNumber} on first descent (seed {Seed})",
                    floorNumber, existing.Seed);
            }
            template = existing;
        }
        return template.CloneForSession(sessionId);
    }

    private async Task PersistAsync(FloorTemplate template)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        db.Floors.Add(Serialize(template));
        await db.SaveChangesAsync();
    }

    private static FloorRecord Serialize(FloorTemplate t)
    {
        var bytes = new byte[t.Width * t.Height];
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
                bytes[y * t.Width + x] = (byte)t.TileGrid[x, y].Type;

        var rooms = t.Rooms
            .Select(r => new RoomData(r.Id, r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height, r.Name))
            .ToList();

        return new FloorRecord
        {
            Id = t.CanonicalId,
            FloorNumber = t.FloorNumber,
            Seed = t.Seed,
            Version = WorldConstants.Version,
            Width = t.Width,
            Height = t.Height,
            Tiles = bytes,
            RoomsJson = JsonSerializer.Serialize(rooms),
            BossDoorX = t.BossDoor?.X,
            BossDoorY = t.BossDoor?.Y,
            BossRoomX = t.BossRoomBounds?.X,
            BossRoomY = t.BossRoomBounds?.Y,
            BossRoomWidth = t.BossRoomBounds?.Width,
            BossRoomHeight = t.BossRoomBounds?.Height,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static FloorTemplate Deserialize(FloorRecord r)
    {
        if (r.Tiles.Length != r.Width * r.Height)
            throw new InvalidOperationException(
                $"Floor {r.FloorNumber} row corrupt: tiles length {r.Tiles.Length} != {r.Width}*{r.Height}.");

        var grid = new Tile[r.Width, r.Height];
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
                grid[x, y] = new Tile((TileType)r.Tiles[y * r.Width + x]);

        var rooms = (JsonSerializer.Deserialize<List<RoomData>>(r.RoomsJson) ?? new())
            .Select(rd => new Room
            {
                Id = rd.Id,
                Bounds = new Bounds(rd.X, rd.Y, rd.Width, rd.Height),
                Name = rd.Name
            })
            .ToList();

        Position? bossDoor = (r.BossDoorX, r.BossDoorY) is (int dx, int dy)
            ? new Position(dx, dy) : null;
        Bounds? bossRoom = (r.BossRoomX, r.BossRoomY, r.BossRoomWidth, r.BossRoomHeight)
            is (int bx, int by, int bw, int bh)
                ? new Bounds(bx, by, bw, bh) : null;

        return new FloorTemplate
        {
            CanonicalId = r.Id,
            FloorNumber = r.FloorNumber,
            Seed = r.Seed,
            Width = r.Width,
            Height = r.Height,
            TileGrid = grid,
            Rooms = rooms,
            BossDoor = bossDoor,
            BossRoomBounds = bossRoom
        };
    }

    private record RoomData(Guid Id, int X, int Y, int Width, int Height, string? Name);
}
