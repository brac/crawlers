using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Hubs;

public class GameHub : Hub<IGameClient>
{
    private const string SessionIdKey = "sessionId";

    private readonly SessionManager _sessions;
    private readonly MovementService _movement;
    private readonly EngagementService _engagement;
    private readonly CombatService _combat;
    private readonly CombatRunner _combatRunner;
    private readonly DescendService _descend;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        SessionManager sessions,
        MovementService movement,
        EngagementService engagement,
        CombatService combat,
        CombatRunner combatRunner,
        DescendService descend,
        ILogger<GameHub> logger)
    {
        _sessions = sessions;
        _movement = movement;
        _engagement = engagement;
        _combat = combat;
        _combatRunner = combatRunner;
        _descend = descend;
        _logger = logger;
    }

    public async Task<GameStateSnapshotDto> JoinNewSession(int? seed)
    {
        // Clean up any prior session attached to this connection so a "restart"
        // doesn't leak the old combat runner / session state in memory.
        if (Context.Items[SessionIdKey] is Guid oldSessionId)
        {
            _combatRunner.Stop(oldSessionId);
            _sessions.Remove(oldSessionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, oldSessionId.ToString());
        }

        var state = _sessions.CreateSession(seed);
        Context.Items[SessionIdKey] = state.Session.Id;
        await Groups.AddToGroupAsync(Context.ConnectionId, state.Session.Id.ToString());
        _logger.LogInformation(
            "Connection {ConnectionId} joined new session {SessionId} (seed {Seed}, {EnemyCount} enemies)",
            Context.ConnectionId, state.Session.Id, state.Floor.Seed, state.Floor.Entities.Count);
        return SnapshotMapper.ToSnapshot(state);
    }

    public async Task Move(Domain.Enums.MoveDirection direction)
    {
        if (Context.Items[SessionIdKey] is not Guid sessionId) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        bool engaged;
        GameStateSnapshotDto snapshot;
        lock (state.SyncRoot)
        {
            if (state.Session.Mode == GameMode.Combat) return;       // runner owns the session
            if (state.Session.Mode == GameMode.Resolution) return;   // permadeath: refresh required

            // A previous combat may have left ActiveCombat populated so the
            // client could read the final log. The first new movement clears it.
            if (state.ActiveCombat is not null)
                state.ActiveCombat = null;

            if (!_movement.TryMove(state, direction)) return;

            var foe = _engagement.FindEngagement(state);
            if (foe is not null)
            {
                _combat.Start(state, foe, new Dice());
                engaged = true;
            }
            else
            {
                engaged = false;
            }
            snapshot = SnapshotMapper.ToSnapshot(state);
        }

        await Clients.Group(sessionId.ToString()).ReceiveSnapshot(snapshot);

        if (engaged)
        {
            _logger.LogInformation("Session {SessionId} entered combat", sessionId);
            _combatRunner.Start(sessionId);
        }
    }

    public Task Flee()
    {
        if (Context.Items[SessionIdKey] is not Guid sessionId) return Task.CompletedTask;
        _combatRunner.RequestFlee(sessionId);
        return Task.CompletedTask;
    }

    public async Task Descend()
    {
        if (Context.Items[SessionIdKey] is not Guid sessionId) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        GameStateSnapshotDto snapshot;
        lock (state.SyncRoot)
        {
            if (!_descend.TryDescend(state)) return;
            snapshot = SnapshotMapper.ToSnapshot(state);
        }

        _logger.LogInformation(
            "Session {SessionId} descended to floor {Floor}",
            sessionId, state.Session.CurrentFloorNumber);
        await Clients.Group(sessionId.ToString()).ReceiveSnapshot(snapshot);
    }

    public async Task UseItem(Guid itemId)
    {
        if (Context.Items[SessionIdKey] is not Guid sessionId) return;
        var state = _sessions.Get(sessionId);
        if (state is null) return;

        // In Combat: queue the action so the next runner tick consumes the
        // item, applies the effect, and skips the player's attack for that
        // round. The runner broadcasts the snapshot.
        if (state.Session.Mode == GameMode.Combat)
        {
            _combatRunner.RequestUseItem(sessionId, itemId);
            return;
        }

        // Out of combat (Exploration): apply immediately and broadcast.
        // Resolution mode (dead) is silently dropped — refresh required.
        if (state.Session.Mode != GameMode.Exploration) return;

        GameStateSnapshotDto snapshot;
        lock (state.SyncRoot)
        {
            var item = state.Player.Inventory.FirstOrDefault(i => i.Id == itemId);
            if (item is null || !item.IsConsumable) return;

            ItemUseHelper.Apply(state, item);
            state.Player.Inventory.Remove(item);
            snapshot = SnapshotMapper.ToSnapshot(state);
        }

        await Clients.Group(sessionId.ToString()).ReceiveSnapshot(snapshot);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Connection {ConnectionId} disconnected", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
