using System.Collections.Concurrent;
using Crawlers.Domain.Enums;
using Crawlers.Server.Hubs;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Drives auto-battler combat for active multi-player combats. One Task per
/// (sessionId, enemyId) — the enemy is the natural per-combat key since each
/// enemy can only be in one combat at a time. All state mutation is funneled
/// through SessionState.SyncRoot so the hub and this runner cannot race.
/// </summary>
public class CombatRunner
{
    public const int RoundIntervalMs = 900;

    private readonly SessionBroadcaster _broadcaster;
    private readonly SessionManager _sessions;
    private readonly CombatService _combat;
    private readonly RunEndService _runEnd;
    private readonly IRunHistoryService _runHistory;
    private readonly ICorpseService _corpses;
    private readonly ILogger<CombatRunner> _logger;
    private readonly ConcurrentDictionary<(Guid sessionId, Guid enemyId), CancellationTokenSource> _active = new();

    public CombatRunner(
        SessionBroadcaster broadcaster,
        SessionManager sessions,
        CombatService combat,
        RunEndService runEnd,
        IRunHistoryService runHistory,
        ICorpseService corpses,
        ILogger<CombatRunner> logger)
    {
        _broadcaster = broadcaster;
        _sessions = sessions;
        _combat = combat;
        _runEnd = runEnd;
        _runHistory = runHistory;
        _corpses = corpses;
        _logger = logger;
    }

    public void Start(Guid sessionId, Guid enemyId)
    {
        var key = (sessionId, enemyId);
        var cts = new CancellationTokenSource();
        if (!_active.TryAdd(key, cts))
        {
            cts.Dispose();
            return;
        }
        _ = Task.Run(() => RunAsync(sessionId, enemyId, cts.Token));
    }

    public void RequestFlee(Guid sessionId, Guid playerId)
    {
        var state = _sessions.Get(sessionId);
        if (state is null) return;
        lock (state.SyncRoot)
        {
            var combat = state.GetCombat(playerId);
            if (combat is null) return;
            if (!combat.HasParticipant(playerId)) return;
            combat.FleeRequested.Add(playerId);
        }
    }

    public void RequestUseItem(Guid sessionId, Guid playerId, Guid itemId)
    {
        var state = _sessions.Get(sessionId);
        if (state is null) return;
        lock (state.SyncRoot)
        {
            var combat = state.GetCombat(playerId);
            if (combat is null) return;
            if (!combat.HasParticipant(playerId)) return;
            combat.UseItemRequested[playerId] = itemId;
        }
    }

    public void Stop(Guid sessionId, Guid enemyId)
    {
        if (_active.TryRemove((sessionId, enemyId), out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void StopAllForSession(Guid sessionId)
    {
        var keys = _active.Keys.Where(k => k.sessionId == sessionId).ToList();
        foreach (var k in keys) Stop(k.sessionId, k.enemyId);
    }

    private async Task RunAsync(Guid sessionId, Guid enemyId, CancellationToken ct)
    {
        var dice = new Dice();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(RoundIntervalMs, ct);

                var state = _sessions.Get(sessionId);
                if (state is null) return;

                CombatRoundResult result;
                ActiveCombat? snapshotCombat;
                RunOutcome? endedThisTick;
                lock (state.SyncRoot)
                {
                    var combat = state.GetCombatByEnemy(enemyId);
                    snapshotCombat = combat;
                    if (combat is null) return;

                    result = _combat.ProcessNextRound(state, combat, dice);

                    // Apply the run-end transition while the lock is held so
                    // the snapshot built by the broadcast below already shows
                    // the run summary on every client. Returns non-null only
                    // on the tick the wipe (or future quit) flips state.
                    endedThisTick = _runEnd.CheckAndApply(state);
                }

                await _broadcaster.BroadcastAsync(state);

                if (endedThisTick is RunOutcome runOutcome)
                {
                    _logger.LogInformation(
                        "Run ended for session {SessionId} with outcome {Outcome}",
                        sessionId, runOutcome);
                }

                // Persist deaths recorded this round. Run history is per-player
                // so each death gets its own row, and a parallel corpses row
                // captures the death position so the future continuation phase
                // can query "what corpses are on floor N?" without a schema change.
                foreach (var (pid, outcome) in result.ExitedThisRound)
                {
                    if (outcome != CombatOutcome.PlayerDied) continue;
                    var cause = ResolveCauseOfDeath(state, snapshotCombat);
                    try
                    {
                        await _runHistory.RecordDeathAsync(state, pid, cause, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to persist run history for session {SessionId} player {PlayerId}",
                            sessionId, pid);
                    }
                    try
                    {
                        await _corpses.RecordCorpseAsync(state, pid, cause, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to persist corpse for session {SessionId} player {PlayerId}",
                            sessionId, pid);
                    }
                }

                if (result.Ended)
                {
                    _logger.LogInformation(
                        "Combat ended for session {SessionId} enemy {EnemyId} (exits: {Exits})",
                        sessionId, enemyId, result.ExitedThisRound.Count);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Combat runner for session {SessionId} enemy {EnemyId} failed",
                sessionId, enemyId);
        }
        finally
        {
            _active.TryRemove((sessionId, enemyId), out _);
        }
    }

    private static string? ResolveCauseOfDeath(SessionState state, ActiveCombat? combat)
    {
        if (combat is null) return null;
        var floor = state.GetFloor(combat.FloorNumber);
        var enemy = floor?.Entities.FirstOrDefault(e => e.Id == combat.EnemyId);
        return enemy?.Name is { } name ? $"Slain by a {name}" : null;
    }
}
