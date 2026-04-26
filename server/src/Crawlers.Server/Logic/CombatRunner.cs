using System.Collections.Concurrent;
using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Hubs;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Crawlers.Server.Logic;

/// <summary>
/// Drives auto-battler combat for active sessions. One Task per fighting
/// session, ticked on a fixed interval. All state mutation is funneled through
/// SessionState.SyncRoot so the hub (Move/Flee) and this runner cannot race.
/// </summary>
public class CombatRunner
{
    public const int RoundIntervalMs = 900;

    private readonly IHubContext<GameHub, IGameClient> _hub;
    private readonly SessionManager _sessions;
    private readonly CombatService _combat;
    private readonly IRunHistoryService _runHistory;
    private readonly ILogger<CombatRunner> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    public CombatRunner(
        IHubContext<GameHub, IGameClient> hub,
        SessionManager sessions,
        CombatService combat,
        IRunHistoryService runHistory,
        ILogger<CombatRunner> logger)
    {
        _hub = hub;
        _sessions = sessions;
        _combat = combat;
        _runHistory = runHistory;
        _logger = logger;
    }

    public void Start(Guid sessionId)
    {
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(sessionId, cts))
        {
            cts.Dispose();
            return;
        }
        _ = Task.Run(() => RunAsync(sessionId, cts.Token));
    }

    public void RequestFlee(Guid sessionId)
    {
        var state = _sessions.Get(sessionId);
        if (state is null) return;
        lock (state.SyncRoot)
        {
            if (state.ActiveCombat is not null)
                state.ActiveCombat.FleeRequested = true;
        }
    }

    public void RequestUseItem(Guid sessionId, Guid itemId)
    {
        var state = _sessions.Get(sessionId);
        if (state is null) return;
        lock (state.SyncRoot)
        {
            if (state.ActiveCombat is not null)
                state.ActiveCombat.UseItemRequested = itemId;
        }
    }

    public void Stop(Guid sessionId)
    {
        if (_active.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task RunAsync(Guid sessionId, CancellationToken ct)
    {
        var dice = new Dice();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(RoundIntervalMs, ct);

                var state = _sessions.Get(sessionId);
                if (state is null) return;

                CombatOutcome outcome;
                GameStateSnapshotDto snapshot;
                string? causeOfDeath = null;
                lock (state.SyncRoot)
                {
                    if (state.ActiveCombat is null || state.Session.Mode != GameMode.Combat)
                        return;

                    outcome = _combat.ProcessNextRound(state, dice);
                    if (outcome != CombatOutcome.InProgress)
                    {
                        if (outcome == CombatOutcome.PlayerDied)
                            causeOfDeath = ResolveCauseOfDeath(state);
                        _combat.Finalize(state, outcome, dice);
                    }

                    snapshot = SnapshotMapper.ToSnapshot(state);
                }

                await _hub.Clients.Group(sessionId.ToString()).ReceiveSnapshot(snapshot);

                if (outcome == CombatOutcome.PlayerDied)
                {
                    try
                    {
                        await _runHistory.RecordDeathAsync(state, causeOfDeath, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to persist run history for session {SessionId}", sessionId);
                    }
                }

                if (outcome != CombatOutcome.InProgress)
                {
                    _logger.LogInformation(
                        "Combat ended for session {SessionId}: {Outcome}",
                        sessionId, outcome);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Combat runner for session {SessionId} failed", sessionId);
        }
        finally
        {
            _active.TryRemove(sessionId, out _);
        }
    }

    private static string? ResolveCauseOfDeath(SessionState state)
    {
        var enemyId = state.ActiveCombat?.EnemyId;
        if (enemyId is null) return null;
        var enemy = state.Floor.Entities.FirstOrDefault(e => e.Id == enemyId);
        return enemy?.Name is { } name ? $"Slain by a {name}" : null;
    }
}
