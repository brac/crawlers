using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Contracts;

public static class SnapshotMapper
{
    public static GameStateSnapshotDto ToSnapshot(SessionState state) => new(
        SessionId: state.Session.Id,
        Mode: state.Session.Mode,
        FloorNumber: state.Session.CurrentFloorNumber,
        Floor: ToFloorSnapshot(state.Floor, state.Player.FogOfWar),
        Player: ToPlayerSnapshot(state.Player),
        Combat: state.ActiveCombat is null ? null : ToCombatLog(state.ActiveCombat.Log)
    );

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
