using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Contracts;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Hubs;

public class GameHub : Hub<IGameClient>
{
    private const string SessionIdKey = "sessionId";
    private const string PlayerIdKey = "playerId";

    private readonly SessionManager _sessions;
    private readonly SessionBroadcaster _broadcaster;
    private readonly MovementService _movement;
    private readonly EngagementService _engagement;
    private readonly CombatService _combat;
    private readonly CombatRunner _combatRunner;
    private readonly DescendService _descend;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        SessionManager sessions,
        SessionBroadcaster broadcaster,
        MovementService movement,
        EngagementService engagement,
        CombatService combat,
        CombatRunner combatRunner,
        DescendService descend,
        ILogger<GameHub> logger)
    {
        _sessions = sessions;
        _broadcaster = broadcaster;
        _movement = movement;
        _engagement = engagement;
        _combat = combat;
        _combatRunner = combatRunner;
        _descend = descend;
        _logger = logger;
    }

    /// <summary>
    /// Bind this connection to an existing multi-player session and player
    /// (created by the lobby bridge in <see cref="LobbyHub.StartGame"/>). The
    /// player id is supplied by the client because the lobby connection
    /// already handed them their identity; on a fresh /game connection
    /// Context.Items is empty and we have no other way to identify them
    /// without an auth layer (Step 12 territory).
    /// </summary>
    public async Task<GameStateSnapshotDto> JoinSession(Guid sessionId, Guid playerId)
    {
        var state = _sessions.Get(sessionId)
            ?? throw new HubException("SessionNotFound");
        var player = state.GetPlayer(playerId)
            ?? throw new HubException("PlayerNotInSession");

        Context.Items[SessionIdKey] = sessionId;
        Context.Items[PlayerIdKey] = playerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());

        GameStateSnapshotDto snapshot;
        lock (state.SyncRoot)
        {
            state.SetConnection(playerId, Context.ConnectionId);
            snapshot = SnapshotMapper.ToSnapshot(state, playerId);
        }

        // Re-broadcast so every other player sees the new joiner appear in
        // their OtherPlayers list (no-op for solo sessions).
        await _broadcaster.BroadcastAsync(state);

        _logger.LogInformation(
            "Connection {ConnectionId} joined session {SessionId} as player {PlayerId} on floor {Floor}",
            Context.ConnectionId, sessionId, playerId, player.CurrentFloorNumber);
        return snapshot;
    }

    public async Task Move(MoveDirection direction)
    {
        if (!TryGetContext(out var sessionId, out var playerId)) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        Guid? newCombatEnemyId = null;
        lock (state.SyncRoot)
        {
            var player = state.GetPlayer(playerId);
            if (player is null) return;
            if (player.Mode == GameMode.Combat) return;       // runner owns the combat
            if (player.Mode == GameMode.Resolution) return;   // dead

            // A previous combat may have left ActiveCombat populated so the
            // client could read the final log. The first new movement clears it.
            if (state.GetCombat(playerId) is not null)
                state.ClearCombat(playerId);

            if (!_movement.TryMove(state, playerId, direction)) return;

            var foe = _engagement.FindEngagement(state, playerId);
            if (foe is not null)
            {
                var dice = new Dice();
                var existing = state.GetCombatByEnemy(foe.Id);
                // AddPlayer returns false when the existing combat has
                // already concluded (its runner has returned). In that case
                // we treat the engagement as fresh so a brand-new combat +
                // runner spin up — otherwise the joiner ends up stuck in
                // Mode=Combat with no rounds ticking.
                bool joinedExisting =
                    existing is not null
                    && _combat.AddPlayer(state, existing, player, dice);
                if (!joinedExisting)
                {
                    var floor = state.GetFloorFor(player);
                    var participants = _engagement.PlayersInRange(state, floor, foe);
                    _combat.Start(state, foe, participants, dice);
                    newCombatEnemyId = foe.Id;
                }
            }
        }

        await _broadcaster.BroadcastAsync(state);

        if (newCombatEnemyId is Guid enemyId)
        {
            _logger.LogInformation(
                "Player {PlayerId} in session {SessionId} entered combat with enemy {EnemyId}",
                playerId, sessionId, enemyId);
            _combatRunner.Start(sessionId, enemyId);
        }
    }

    public Task Flee()
    {
        if (!TryGetContext(out var sessionId, out var playerId)) return Task.CompletedTask;
        _combatRunner.RequestFlee(sessionId, playerId);
        return Task.CompletedTask;
    }

    public async Task Descend()
    {
        if (!TryGetContext(out var sessionId, out var playerId)) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        int? newFloor = null;
        lock (state.SyncRoot)
        {
            if (!_descend.TryDescend(state, playerId)) return;
            newFloor = state.GetPlayer(playerId)?.CurrentFloorNumber;
        }

        _logger.LogInformation(
            "Player {PlayerId} in session {SessionId} descended to floor {Floor}",
            playerId, sessionId, newFloor);
        await _broadcaster.BroadcastAsync(state);
    }

    public async Task UseItem(Guid itemId)
    {
        if (!TryGetContext(out var sessionId, out var playerId)) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        Player? player;
        lock (state.SyncRoot)
        {
            player = state.GetPlayer(playerId);
            if (player is null) return;

            // In Combat: queue the action so the next runner tick consumes it.
            // The runner broadcasts the snapshot on its next tick.
            if (player.Mode == GameMode.Combat)
            {
                _combatRunner.RequestUseItem(sessionId, playerId, itemId);
                return;
            }
            // Dead — refresh required.
            if (player.Mode != GameMode.Exploration) return;

            var item = player.Inventory.FirstOrDefault(i => i.Id == itemId);
            if (item is null || !item.IsConsumable) return;

            ItemUseHelper.Apply(player, item);
            player.Inventory.Remove(item);
        }

        await _broadcaster.BroadcastAsync(state);
    }

    /// <summary>
    /// A dead player picks (or switches) the teammate they're spectating. The
    /// snapshot mapper uses this to render the run from that teammate's
    /// perspective. Validates the caller is dead and the target is alive +
    /// still connected; bad picks are silently ignored (the next snapshot
    /// will keep the prior view).
    /// </summary>
    public async Task SetSpectatorTarget(Guid targetId)
    {
        if (!TryGetContext(out var sessionId, out var playerId)) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        bool changed = false;
        lock (state.SyncRoot)
        {
            var player = state.GetPlayer(playerId);
            if (player is null) return;
            if (player.Mode != GameMode.Resolution) return;     // only the dead spectate
            if (targetId == playerId) return;                    // can't spectate yourself
            var target = state.GetPlayer(targetId);
            if (target is null || target.Mode == GameMode.Resolution) return;
            if (state.GetConnection(target.Id) is null) return;  // target must still be connected

            if (player.SpectatorTargetId != targetId)
            {
                player.SpectatorTargetId = targetId;
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation(
                "Player {PlayerId} now spectating {TargetId} in session {SessionId}",
                playerId, targetId, sessionId);
            await _broadcaster.BroadcastAsync(state);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetContext(out var sessionId, out var playerId))
        {
            var state = _sessions.Get(sessionId);
            if (state is not null)
            {
                lock (state.SyncRoot)
                {
                    state.ClearConnection(playerId);
                }
                _logger.LogInformation(
                    "Connection {ConnectionId} (player {PlayerId} session {SessionId}) disconnected",
                    Context.ConnectionId, playerId, sessionId);
            }
        }
        return base.OnDisconnectedAsync(exception);
    }

    private bool TryGetContext(out Guid sessionId, out Guid playerId)
    {
        if (Context.Items[SessionIdKey] is Guid s && Context.Items[PlayerIdKey] is Guid p)
        {
            sessionId = s;
            playerId = p;
            return true;
        }
        sessionId = default;
        playerId = default;
        return false;
    }
}
