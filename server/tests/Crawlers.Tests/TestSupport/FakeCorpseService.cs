using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;

namespace Crawlers.Tests.TestSupport;

/// <summary>
/// Shared in-memory <see cref="ICorpseService"/> for unit tests. Pre-seed
/// floors via <see cref="SetFloor"/>; reads return what was seeded plus
/// anything written via <see cref="RecordCorpseAsync"/> mid-test (so a
/// session can drop a corpse in test setup and a second session loading
/// the same floor sees it). The heatmap derives from the same in-memory
/// corpses so Step 9 plumbing is exercised end-to-end.
/// </summary>
internal sealed class FakeCorpseService : ICorpseService
{
    private readonly Dictionary<int, List<CorpseEntry>> _byFloor = new();
    private readonly object _lock = new();

    public void SetFloor(int floorNumber, IReadOnlyList<CorpseEntry> rows)
    {
        lock (_lock) _byFloor[floorNumber] = rows.ToList();
    }

    public Task RecordCorpseAsync(
        SessionState state,
        Guid playerId,
        string? causeOfDeath,
        string? killerType,
        CancellationToken ct = default)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return Task.CompletedTask;
        var entry = new CorpseEntry
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            SessionId = state.Session.Id,
            FloorNumber = player.CurrentFloorNumber,
            X = player.Position.X,
            Y = player.Position.Y,
            DiedAt = DateTimeOffset.UtcNow,
            CauseOfDeath = causeOfDeath,
            PlayerUsername = string.IsNullOrEmpty(player.Username) ? null : player.Username,
            KillerType = killerType,
            DeepestFloor = player.DeepestFloorReached
        };
        lock (_lock)
        {
            if (!_byFloor.TryGetValue(player.CurrentFloorNumber, out var list))
            {
                list = new List<CorpseEntry>();
                _byFloor[player.CurrentFloorNumber] = list;
            }
            list.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CorpseEntry>> GetByFloorAsync(int floorNumber, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<CorpseEntry>>(
                _byFloor.TryGetValue(floorNumber, out var v)
                    ? v.OrderByDescending(c => c.DiedAt).ToList()
                    : Array.Empty<CorpseEntry>());
        }
    }

    public Task<IReadOnlyList<TileHeat>> GetHeatmapByFloorAsync(int floorNumber, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_byFloor.TryGetValue(floorNumber, out var rows))
                return Task.FromResult<IReadOnlyList<TileHeat>>(Array.Empty<TileHeat>());
            var heat = rows
                .GroupBy(r => (r.X, r.Y))
                .Select(g => new TileHeat(g.Key.X, g.Key.Y, g.Count()))
                .OrderByDescending(h => h.Count)
                .ToList();
            return Task.FromResult<IReadOnlyList<TileHeat>>(heat);
        }
    }
}
