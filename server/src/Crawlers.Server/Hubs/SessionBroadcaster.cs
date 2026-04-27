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
        // Snapshot the players list inside the lock so the caller's lock
        // serializes us with mutators. Mappers run after the lock is gone —
        // they only read primitive copies into the DTO.
        var sends = new List<(string connectionId, GameStateSnapshotDto snap)>();
        foreach (var p in state.Players)
        {
            var connId = state.GetConnection(p.Id);
            if (connId is null) continue;
            sends.Add((connId, SnapshotMapper.ToSnapshot(state, p.Id)));
        }

        foreach (var (connId, snap) in sends)
            await _hub.Clients.Client(connId).ReceiveSnapshot(snap);
    }
}
