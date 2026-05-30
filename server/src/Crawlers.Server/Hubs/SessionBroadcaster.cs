using Crawlers.Server.Contracts;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Hubs;

/// <summary>
/// Sends per-player snapshots to every connected player in a session. The
/// snapshot a player receives is filtered to their floor / FOV — two players
/// in the same session see different things, so we can't use a SignalR group.
/// Disconnected players (no connection registered) are silently skipped.
/// </summary>
public class SessionBroadcaster
{
    private readonly IHubContext<GameHub, IGameClient> _hub;

    public SessionBroadcaster(IHubContext<GameHub, IGameClient> hub)
    {
        _hub = hub;
    }

    public async Task BroadcastAsync(SessionState state)
    {
        // Build every player's snapshot under SyncRoot. Callers release their
        // own lock before calling us (see GameHub / CombatRunner / EnemyAiRunner),
        // so we must re-acquire it here — otherwise the mapper reads Players /
        // ActiveCombats / fog while a background tick mutates them on another
        // thread (collection-modified throw or torn reads), and the four
        // per-player snapshots in one broadcast can disagree about world state.
        // The lock spans only the synchronous build; the awaited sends happen
        // after it's released so SignalR I/O never blocks mutators. C# locks
        // are reentrant, so a caller that still holds it is harmless.
        var sends = new List<(string connectionId, GameStateSnapshotDto snap)>();
        lock (state.SyncRoot)
        {
            foreach (var p in state.Players)
            {
                var connId = state.GetConnection(p.Id);
                if (connId is null) continue;
                sends.Add((connId, SnapshotMapper.ToSnapshot(state, p.Id)));
            }
        }

        foreach (var (connId, snap) in sends)
            await _hub.Clients.Client(connId).ReceiveSnapshot(snap);
    }
}
