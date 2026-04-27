using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

public class SessionState
{
    public Session Session { get; }

    /// <summary>Seed used to generate floor 1; subsequent floors derive theirs from it.</summary>
    public int InitialSeed { get; init; }

    /// <summary>
    /// Lock object held during any mutation of this session — by hub methods,
    /// by the CombatRunner background tick loop, by lobby-to-session bridges.
    /// Acquire before reading or writing anything on Players / Floors /
    /// ActiveCombats / Connections.
    /// </summary>
    public object SyncRoot { get; } = new();

    /// <summary>Total enemies killed by this session over its lifetime.</summary>
    public int EnemiesKilled { get; set; }

    /// <summary>
    /// Terminal outcome of the run, or null while it's still going. Stamped
    /// by <see cref="Crawlers.Server.Logic.RunEndService.CheckAndApply"/>
    /// when the wipe condition (every player in Resolution) is detected;
    /// from then on the snapshot mapper attaches a <c>RunSummaryDto</c> to
    /// every broadcast so all clients render the end-of-run screen.
    /// </summary>
    public RunOutcome? Outcome { get; private set; }

    /// <summary>
    /// UTC timestamp the run ended, or null while it's still going. Mirrors
    /// <see cref="Outcome"/>'s lifecycle.
    /// </summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>
    /// Convenience: whether the run is over. Equivalent to
    /// <c>Outcome.HasValue</c>; provided for read-site clarity.
    /// </summary>
    public bool IsRunOver => Outcome.HasValue;

    /// <summary>
    /// Apply the run-end state transition. Idempotent — once an outcome is
    /// stamped, subsequent calls are ignored, so racing wipe-then-quit
    /// detectors can't clobber each other. Caller holds <see cref="SyncRoot"/>.
    /// </summary>
    public void EndRun(RunOutcome outcome)
    {
        if (Outcome.HasValue) return;
        Outcome = outcome;
        EndedAt = DateTimeOffset.UtcNow;
    }

    private readonly List<Player> _players = new();
    private readonly Dictionary<int, Floor> _floors = new();
    private readonly Dictionary<int, VisibilityState[,]> _fogs = new();
    private readonly Dictionary<Guid, ActiveCombat> _combats = new();
    private readonly Dictionary<Guid, string> _connections = new();

    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyDictionary<int, Floor> Floors => _floors;
    public IReadOnlyDictionary<Guid, ActiveCombat> ActiveCombats => _combats;
    public IReadOnlyDictionary<Guid, string> Connections => _connections;

    /// <summary>
    /// Convenience accessor for code that pre-dates multi-player. Returns the
    /// first player added to the session. Tests use this so a single-solo
    /// scenario doesn't have to plumb a player id through every assertion.
    /// </summary>
    public Player PrimaryPlayer => _players[0];

    public SessionState(Session session)
    {
        Session = session;
    }

    public Player? GetPlayer(Guid playerId) =>
        _players.FirstOrDefault(p => p.Id == playerId);

    public Floor? GetFloor(int floorNumber) =>
        _floors.TryGetValue(floorNumber, out var f) ? f : null;

    public Floor GetFloorFor(Player player) =>
        GetFloor(player.CurrentFloorNumber)
        ?? throw new InvalidOperationException(
            $"Player {player.Id} is on floor {player.CurrentFloorNumber} but the session has no such floor loaded.");

    public VisibilityState[,]? GetFog(int floorNumber) =>
        _fogs.TryGetValue(floorNumber, out var f) ? f : null;

    public VisibilityState[,] GetFogFor(Player player) =>
        GetFog(player.CurrentFloorNumber)
        ?? throw new InvalidOperationException(
            $"No shared fog grid for floor {player.CurrentFloorNumber}.");

    public IEnumerable<Player> PlayersOnFloor(int floorNumber) =>
        _players.Where(p => p.CurrentFloorNumber == floorNumber);

    public ActiveCombat? GetCombat(Guid playerId) =>
        _combats.TryGetValue(playerId, out var c) ? c : null;

    /// <summary>
    /// Look up the *live* shared combat for a given enemy. Finalize'd combats
    /// linger in the per-player dict so the dying player's snapshot can still
    /// show its log, but they must be invisible to engagement-time lookups
    /// and to the CombatRunner — otherwise a brand-new combat against the
    /// same enemy gets shadowed by the stale one and its runner picks up
    /// zero participants on the very first tick. Only InProgress combats
    /// are surfaced here.
    /// </summary>
    public ActiveCombat? GetCombatByEnemy(Guid enemyId) =>
        _combats.Values.FirstOrDefault(c =>
            c.EnemyId == enemyId
            && c.Log.Outcome == Crawlers.Domain.Enums.CombatOutcome.InProgress);

    public void AddPlayer(Player p) => _players.Add(p);

    /// <summary>
    /// Add a floor to the session and create its shared fog grid (all Hidden).
    /// Subsequent moves/descents recompute the fog from whichever players
    /// are on this floor at the time.
    /// </summary>
    public void AddFloor(Floor f)
    {
        _floors[f.FloorNumber] = f;
        if (!_fogs.ContainsKey(f.FloorNumber))
            _fogs[f.FloorNumber] = new VisibilityState[f.Width, f.Height];
    }
    public void SetCombat(Guid playerId, ActiveCombat combat) => _combats[playerId] = combat;
    public void ClearCombat(Guid playerId) => _combats.Remove(playerId);

    public void SetConnection(Guid playerId, string connectionId) =>
        _connections[playerId] = connectionId;
    public string? GetConnection(Guid playerId) =>
        _connections.TryGetValue(playerId, out var id) ? id : null;
    public void ClearConnection(Guid playerId) =>
        _connections.Remove(playerId);
}
