using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class MovementService
{
    private readonly ChestService _chests;

    public MovementService(ChestService? chests = null)
    {
        // Default to a fresh ChestService when no DI / test factory passes
        // one — keeps the 14+ existing tests that build MovementService
        // directly working without churning their call sites. Production
        // DI in Program.cs always supplies the singleton.
        _chests = chests ?? new ChestService();
    }

    public bool TryMove(SessionState state, Guid playerId, MoveDirection direction)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return false;

        var floor = state.GetFloorFor(player);
        var fog = state.GetFog(floor.FloorNumber);
        if (fog is null) return false;

        var (dx, dy) = ToDelta(direction);
        var target = new Position(player.Position.X + dx, player.Position.Y + dy);

        if (!InBounds(floor, target)) return false;

        var targetType = floor.TileGrid[target.X, target.Y].Type;

        // Bumping a closed door opens it but does NOT advance the player —
        // the next move steps through. Shared fog recomputes from all players
        // on this floor since the open doorway changes everyone's LOS.
        if (targetType == TileType.Door)
        {
            floor.TileGrid[target.X, target.Y] = new Tile(TileType.OpenDoor);
            FieldOfView.RecomputeForFloor(floor, fog, state.PlayersOnFloor(floor.FloorNumber));
            return true;
        }

        if (!IsWalkable(targetType)) return false;

        // Two living players cannot share a tile. Dead teammates (Mode ==
        // Resolution, pinned to their death tile until corpse-run mechanics
        // arrive) are filtered out so survivors can walk over them — the
        // spec's "corpses don't block movement" rule, applied to the player
        // record itself rather than just the Corpse entity beside it.
        foreach (var other in state.PlayersOnFloor(player.CurrentFloorNumber))
        {
            if (other.Id == player.Id) continue;
            if (other.Mode == GameMode.Resolution) continue;
            if (other.Position.Equals(target)) return false;
        }

        player.Position = target;
        // Order matters: pickup BEFORE chest open. Step 3.4 drops weapon
        // loot on the chest's own tile so the weapon visually emerges
        // from the chest. If we opened first, the just-dropped weapon
        // would be at the player's tile and PickupItemsAt would auto-
        // equip it on the same step — the player would never see the
        // weapon sit on the chest. Running pickup first picks up
        // anything that was already on the floor (e.g. items dropped
        // last turn the player walked off the chest tile and back),
        // then chest-open happens and the new weapon stays on the chest
        // tile until the player steps off and back.
        PickupItemsAt(player, floor, target);
        OpenChestsAt(state, player, target);
        // Step 5 — Bleed / Poison continue to tick while the player
        // walks around outside combat. Without this they'd freeze the
        // moment combat ended and the HUD badges would lie about
        // damage. Per-move tick keeps the burndown narrative honest
        // and gives Antidote a real reason to be used outside combat.
        TickStatusesPostMove(state, player, floor);
        MaybeLockBossRoomDoor(state, floor);
        FieldOfView.RecomputeForFloor(floor, fog, state.PlayersOnFloor(floor.FloorNumber));
        return true;
    }

    /// <summary>
    /// Step 5.G — apply Bleed (start-of-turn-equivalent) and Poison
    /// (end-of-turn-equivalent) ticks to the player at the end of a
    /// successful move, then decrement durations. If the ticks drop
    /// the player to 0 HP, run the same death sequence combat uses
    /// (Mode=Resolution, DiedAt, CauseOfDeath, drop a corpse). No-op
    /// when the player has no active statuses.
    /// </summary>
    private static void TickStatusesPostMove(SessionState state, Player player, Floor floor)
    {
        if (player.StatusEffects.Count == 0) return;

        var bleed = StatusEffectHelper.TickDamage(player.StatusEffects, StatusEffectKind.Bleed);
        if (bleed > 0)
        {
            player.Stats = player.Stats with { Hp = Math.Max(0, player.Stats.Hp - bleed) };
            if (player.Stats.Hp <= 0)
            {
                MarkPlayerDiedFromStatus(state, player, floor, "Bled out");
                return;
            }
        }

        var poison = StatusEffectHelper.TickDamage(player.StatusEffects, StatusEffectKind.Poison);
        if (poison > 0)
        {
            player.Stats = player.Stats with { Hp = Math.Max(0, player.Stats.Hp - poison) };
            if (player.Stats.Hp <= 0)
            {
                MarkPlayerDiedFromStatus(state, player, floor, "Succumbed to poison");
                return;
            }
        }

        StatusEffectHelper.Decrement(player.StatusEffects);
    }

    /// <summary>
    /// Non-combat death sequence — mirrors CombatService.MarkPlayerDied
    /// without the combat-log bookkeeping. Drops a corpse, flips the
    /// player to Resolution, and sets DiedAt + CauseOfDeath. The
    /// run-end check elsewhere will pick up the dead player on the
    /// next sweep just as it would for a combat death.
    /// </summary>
    private static void MarkPlayerDiedFromStatus(
        SessionState state, Player player, Floor floor, string cause)
    {
        player.Mode = GameMode.Resolution;
        player.DiedAt = DateTimeOffset.UtcNow;
        player.CauseOfDeath = cause;

        var displayPos = CorpsePlacement.PickFreeTile(floor, player.Position, Random.Shared);
        floor.Entities.Add(new Entity
        {
            Id = Guid.NewGuid(),
            FloorId = floor.Id,
            Type = EntityType.Corpse,
            Name = "Corpse",
            Position = displayPos,
            State = EntityState.Alive,
            PlayerId = player.Id,
            DiedAt = player.DiedAt,
            Username = string.IsNullOrEmpty(player.Username) ? null : player.Username,
            KillerType = cause
        });
    }

    private void OpenChestsAt(SessionState state, Player player, Position target)
    {
        var floor = state.GetFloorFor(player);
        var chests = floor.Entities
            .Where(e => e.Type == EntityType.Chest
                        && !e.IsOpen
                        && e.Position.Equals(target))
            .ToList();
        foreach (var chest in chests)
        {
            _chests.TryOpen(state, player.Id, chest.Id);
        }
    }

    /// <summary>
    /// Lock the boss-room door once *every* alive player on this floor has
    /// crossed inside the bounds. In solo this still fires the moment the
    /// player steps in; in multi-player it waits until the last teammate is
    /// committed, so a straggler never gets shut out of the boss fight.
    /// Dead players (Mode == Resolution) are excluded — their corpse sitting
    /// in the corridor shouldn't keep the door propped open.
    /// </summary>
    private static void MaybeLockBossRoomDoor(SessionState state, Floor floor)
    {
        if (floor.BossDoor is not { } door) return;
        if (floor.BossRoomBounds is not { } bounds) return;
        if (floor.BossEntityId is not { } bossId) return;

        var boss = floor.Entities.FirstOrDefault(e => e.Id == bossId);
        if (boss is null || boss.State != EntityState.Alive) return;

        var t = floor.TileGrid[door.X, door.Y].Type;
        if (t != TileType.OpenDoor && t != TileType.Door) return;

        var aliveOnFloor = state.PlayersOnFloor(floor.FloorNumber)
            .Where(p => p.Mode != GameMode.Resolution)
            .ToList();
        if (aliveOnFloor.Count == 0) return;
        if (!aliveOnFloor.All(p => Contains(bounds, p.Position))) return;

        floor.TileGrid[door.X, door.Y] = new Tile(TileType.LockedDoor);
    }

    private static bool Contains(Bounds b, Position p) =>
        p.X >= b.X && p.X < b.X + b.Width &&
        p.Y >= b.Y && p.Y < b.Y + b.Height;

    /// <summary>
    /// Step 4 — inventory capacity. Pickup of a 5th consumable is
    /// rejected (item stays on the floor); weapons are exempt because
    /// they replace the equipped slot rather than entering inventory.
    /// </summary>
    private const int MaxInventorySlots = 4;

    private static void PickupItemsAt(Player player, Floor floor, Position p)
    {
        // Snapshot the matches first so we can mutate floor.Entities safely.
        var picked = floor.Entities
            .Where(e => e.Type == EntityType.Item
                        && e.State == EntityState.Alive
                        && e.Position.Equals(p)
                        && e.Item is not null)
            .ToList();
        foreach (var entity in picked)
        {
            var item = entity.Item!;
            // Step 3.4 — weapon items REPLACE the player's equipped slot
            // rather than going into Inventory. The previous weapon is
            // discarded (we don't drop it back on the floor — keeps the
            // pickup loop simple; future polish can preserve the swap).
            // Stats.Damage/InitiativeMod are rebuilt from the new
            // weapon so combat picks them up immediately.
            if (item.Weapon is { } weapon)
            {
                player.EquippedWeapon = weapon;
                player.EquippedWeaponName = item.Name;
                player.Stats = player.Stats with
                {
                    Damage = weapon.Damage,
                    InitiativeMod = weapon.InitiativeMod
                };
                floor.Entities.Remove(entity);
            }
            else
            {
                // Step 4 — capacity check. Inventory full → leave on floor.
                // The player can come back for it after using something.
                if (player.Inventory.Count >= MaxInventorySlots) continue;
                player.Inventory.Add(item);
                floor.Entities.Remove(entity);
            }
        }
    }

    private static (int dx, int dy) ToDelta(MoveDirection d) => d switch
    {
        MoveDirection.North => (0, -1),
        MoveDirection.South => (0, 1),
        MoveDirection.East => (1, 0),
        MoveDirection.West => (-1, 0),
        _ => (0, 0)
    };

    private static bool InBounds(Floor floor, Position p) =>
        p.X >= 0 && p.Y >= 0 && p.X < floor.Width && p.Y < floor.Height;

    private static bool IsWalkable(TileType t) =>
        t == TileType.Floor
        || t == TileType.OpenDoor
        || t == TileType.StairsUp
        || t == TileType.StairsDown;
    // Wall, Door (handled separately above), and LockedDoor block movement.
}
