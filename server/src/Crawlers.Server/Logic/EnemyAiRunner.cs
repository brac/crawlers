using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;
using Microsoft.Extensions.Hosting;

namespace Crawlers.Server.Logic;

/// <summary>
/// Background service that drives enemy AI on a fixed tick. Every
/// <see cref="TickIntervalMs"/> ms it iterates all active sessions, moves
/// each non-combat enemy on floors that have live players, then fires
/// engagement for any enemy that walked adjacent to a player.
///
/// One tick loop runs for the entire process lifetime — no per-session or
/// per-enemy tasks. All session mutations happen inside
/// <see cref="Sessions.SessionState.SyncRoot"/>;
/// <see cref="Hubs.SessionBroadcaster"/> and
/// <see cref="CombatRunner.Start"/> are called outside the lock.
/// </summary>
public class EnemyAiRunner : BackgroundService
{
    public const int TickIntervalMs = 700;

    private readonly SessionManager _sessions;
    private readonly Hubs.SessionBroadcaster _broadcaster;
    private readonly EngagementService _engagement;
    private readonly CombatService _combat;
    private readonly CombatRunner _combatRunner;
    private readonly ILogger<EnemyAiRunner> _logger;
    private readonly Dice _dice = new();

    public EnemyAiRunner(
        SessionManager sessions,
        Hubs.SessionBroadcaster broadcaster,
        EngagementService engagement,
        CombatService combat,
        CombatRunner combatRunner,
        ILogger<EnemyAiRunner> logger)
    {
        _sessions = sessions;
        _broadcaster = broadcaster;
        _engagement = engagement;
        _combat = combat;
        _combatRunner = combatRunner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await TickAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI runner tick failed");
            }
        }
    }

    private async Task TickAllAsync()
    {
        foreach (var kvp in _sessions.AllSessions)
        {
            var sessionId = kvp.Key;
            var state = kvp.Value;
            if (state.IsRunOver) continue;

            List<Guid>? newCombatEnemyIds = null;
            bool anyMoved = false;

            lock (state.SyncRoot)
            {
                foreach (var (floorNum, floor) in state.Floors)
                {
                    // Only tick floors that have at least one living player.
                    var hasAlive = false;
                    foreach (var p in state.PlayersOnFloor(floorNum))
                    {
                        if (p.Mode != GameMode.Resolution) { hasAlive = true; break; }
                    }
                    if (!hasAlive) continue;

                    // Stable iteration order prevents two enemies racing for
                    // the same tile — the one with the lower Id wins this tick.
                    var enemies = floor.Entities
                        .Where(e => e.Type == EntityType.Enemy && e.State == EntityState.Alive)
                        .OrderBy(e => e.Id)
                        .ToList();

                    foreach (var enemy in enemies)
                    {
                        // CombatRunner owns enemies currently in combat.
                        if (state.GetCombatByEnemy(enemy.Id) is not null) continue;

                        if (!EnemyAi.TakeTurn(state, enemy, floor)) continue;
                        anyMoved = true;

                        // Check whether the step brought the enemy within engagement range.
                        // Mirrors the player-side trigger (EngagementProximity = 3) so both
                        // sides start combat at the same distance.
                        foreach (var player in state.PlayersOnFloor(floorNum))
                        {
                            if (player.Mode != GameMode.Exploration) continue;
                            if (EnemyAi.Chebyshev(enemy.Position, player.Position) > EngagementService.EngagementProximity) continue;

                            var existing = state.GetCombatByEnemy(enemy.Id);
                            if (existing is not null)
                            {
                                // Already fighting — add any newly-adjacent teammates.
                                foreach (var p in _engagement.PlayersInRange(state, floor, enemy))
                                    _combat.AddPlayer(state, existing, p, _dice);
                            }
                            else
                            {
                                var participants = _engagement.PlayersInRange(state, floor, enemy);
                                if (participants.Count > 0)
                                {
                                    _combat.Start(state, enemy, participants, _dice);
                                    (newCombatEnemyIds ??= new()).Add(enemy.Id);
                                }
                            }

                            break; // one combat per enemy per tick
                        }
                    }
                }
            }

            if (anyMoved)
                await _broadcaster.BroadcastAsync(state).ConfigureAwait(false);

            if (newCombatEnemyIds is not null)
            {
                foreach (var enemyId in newCombatEnemyIds)
                {
                    _logger.LogDebug(
                        "AI runner started combat in session {SessionId} enemy {EnemyId}",
                        sessionId, enemyId);
                    _combatRunner.Start(sessionId, enemyId);
                }
            }
        }
    }
}
