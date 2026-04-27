using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Contracts;

public static class SnapshotMapper
{
    /// <summary>
    /// Build the snapshot for a specific player. For a living player the view
    /// is theirs. For a dead player who has chosen a spectator target, the
    /// view (floor / fog / position / combat) shifts to the target's
    /// perspective so the dead client can keep watching the run; the local
    /// player's mode stays Resolution and HP/inventory remain theirs.
    /// </summary>
    public static GameStateSnapshotDto ToSnapshot(SessionState state, Guid playerId)
    {
        var player = state.GetPlayer(playerId)
            ?? throw new InvalidOperationException($"ToSnapshot for unknown player {playerId}.");

        // Resolve the "viewer" — usually the local player, but a dead player
        // following a teammate borrows their eyes. If the chosen target has
        // since died or disconnected, drop the binding and fall back to the
        // local-corpse view so the client re-prompts.
        var viewer = player;
        Guid? activeTarget = null;
        if (player.Mode == GameMode.Resolution && player.SpectatorTargetId is { } targetId)
        {
            var target = state.GetPlayer(targetId);
            bool targetUsable = target is not null
                && target.Mode != GameMode.Resolution
                && state.GetConnection(target.Id) is not null;
            if (targetUsable)
            {
                viewer = target!;
                activeTarget = target!.Id;
            }
            else
            {
                player.SpectatorTargetId = null;
            }
        }

        var floor = state.GetFloorFor(viewer);
        var fog = state.GetFog(viewer.CurrentFloorNumber);
        var combat = state.GetCombat(viewer.Id);

        var others = state.PlayersOnFloor(viewer.CurrentFloorNumber)
            .Where(p => p.Id != playerId)
            .Select(p => new OtherPlayerDto(
                p.Id, p.Position.X, p.Position.Y,
                p.Stats.Hp, p.Stats.MaxHp,
                InCombat: p.Mode == GameMode.Combat))
            .ToList();

        // Spectator view substitutes the viewer's tile coords for the local
        // player's position so the camera follows the survivor.
        var playerDto = activeTarget is null
            ? ToPlayerSnapshot(player)
            : new PlayerSnapshotDto(
                Id: player.Id,
                X: viewer.Position.X,
                Y: viewer.Position.Y,
                Hp: player.Stats.Hp,
                MaxHp: player.Stats.MaxHp,
                Inventory: player.Inventory
                    .Select(i => new ItemDto(i.Id, i.Name, i.Description, i.IsConsumable, i.Effect, i.EffectValue))
                    .ToList());

        // Picker list — only populated when this player is dead. Filter to
        // live + connected teammates so a disconnected player isn't a valid
        // spectate target (per Step 11 spec).
        IReadOnlyList<SpectatableTargetDto> spectatable;
        if (player.Mode == GameMode.Resolution)
        {
            spectatable = state.Players
                .Where(p => p.Id != playerId
                            && p.Mode != GameMode.Resolution
                            && state.GetConnection(p.Id) is not null)
                .Select(p => new SpectatableTargetDto(
                    p.Id, p.CurrentFloorNumber, p.Mode == GameMode.Combat))
                .ToList();
        }
        else
        {
            spectatable = Array.Empty<SpectatableTargetDto>();
        }

        // Ambient combats: every active fight on the viewer's floor that
        // they're NOT in. Distinct by combat id (multiple participants
        // share one ActiveCombat instance). Lets the renderer animate
        // teammate Hits/Crits/Misses for outside observers.
        var ambient = new List<CombatLogDto>();
        var seenAmbient = new HashSet<Guid>();
        foreach (var c in state.ActiveCombats.Values)
        {
            if (c.Log.Outcome != Crawlers.Domain.Enums.CombatOutcome.InProgress) continue;
            if (c.FloorNumber != viewer.CurrentFloorNumber) continue;
            if (combat is not null && c.Log.Id == combat.Log.Id) continue; // viewer's own
            if (!seenAmbient.Add(c.Log.Id)) continue;
            ambient.Add(ToCombatLog(c.Log));
        }

        return new GameStateSnapshotDto(
            SessionId: state.Session.Id,
            Mode: player.Mode,
            FloorNumber: viewer.CurrentFloorNumber,
            Floor: ToFloorSnapshot(floor, fog),
            Player: playerDto,
            OtherPlayers: others,
            Combat: combat is null ? null : ToCombatLog(combat.Log),
            SpectatorTargetId: activeTarget,
            SpectatableTargets: spectatable,
            AmbientCombats: ambient,
            RunSummary: state.IsRunOver ? ToRunSummary(state) : null
        );
    }

    /// <summary>
    /// Build the end-of-run payload from the live session. Every connected
    /// player receives the same summary — it's a session-wide artifact, not
    /// a per-viewer one. Caller has guaranteed the run has ended.
    /// </summary>
    public static RunSummaryDto ToRunSummary(SessionState state)
    {
        if (state.Outcome is not { } outcome || state.EndedAt is not { } endedAt)
            throw new InvalidOperationException("ToRunSummary called before the run ended.");

        var players = state.Players
            .Select(p => new RunSummaryPlayerDto(
                PlayerId: p.Id,
                FinalFloor: p.CurrentFloorNumber,
                DeepestFloor: p.DeepestFloorReached,
                FinalHp: p.Stats.Hp,
                FinalMaxHp: p.Stats.MaxHp,
                Survived: p.Mode != GameMode.Resolution,
                CauseOfDeath: p.CauseOfDeath,
                DiedAt: p.DiedAt,
                DeathX: p.Position.X,
                DeathY: p.Position.Y))
            .ToList();

        int deepest = state.Players.Count > 0
            ? state.Players.Max(p => p.DeepestFloorReached)
            : 0;

        return new RunSummaryDto(
            Outcome: outcome,
            StartedAt: state.Session.CreatedAt,
            EndedAt: endedAt,
            DeepestFloor: deepest,
            EnemiesKilled: state.EnemiesKilled,
            Players: players
        );
    }

    public static FloorSnapshotDto ToFloorSnapshot(Floor floor) =>
        ToFloorSnapshot(floor, fog: null);

    public static FloorSnapshotDto ToFloorSnapshot(Floor floor, VisibilityState[,]? fog)
    {
        int n = floor.Width * floor.Height;
        var tiles = new int[n];
        var visibility = new int[n];
        for (int y = 0; y < floor.Height; y++)
        {
            for (int x = 0; x < floor.Width; x++)
            {
                int i = y * floor.Width + x;
                tiles[i] = (int)floor.TileGrid[x, y].Type;
                visibility[i] = fog is null
                    ? (int)VisibilityState.Visible
                    : (int)fog[x, y];
            }
        }

        var rooms = floor.Rooms
            .Select(r => new RoomDto(r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height))
            .ToList();

        var entities = floor.Entities
            .Where(e => e.State == EntityState.Alive)
            .Where(e => fog is null || fog[e.Position.X, e.Position.Y] == VisibilityState.Visible)
            .Select(e => new EntityDto(e.Id, e.Type, e.Name ?? "", e.Position.X, e.Position.Y))
            .ToList();

        return new FloorSnapshotDto(floor.Width, floor.Height, tiles, visibility, rooms, entities);
    }

    public static PlayerSnapshotDto ToPlayerSnapshot(Player player) => new(
        player.Id,
        player.Position.X,
        player.Position.Y,
        player.Stats.Hp,
        player.Stats.MaxHp,
        player.Inventory
            .Select(i => new ItemDto(i.Id, i.Name, i.Description, i.IsConsumable, i.Effect, i.EffectValue))
            .ToList()
    );

    public static CombatLogDto ToCombatLog(CombatLog log) => new(
        Id: log.Id,
        Outcome: log.Outcome,
        Rounds: log.Rounds
            .Select(r => new CombatRoundDto(
                r.Number,
                r.Events
                    .Select(e => new CombatEventDto(
                        e.ActorId,
                        e.TargetId,
                        e.Kind,
                        e.Damage,
                        e.Description))
                    .ToList()))
            .ToList()
    );
}
