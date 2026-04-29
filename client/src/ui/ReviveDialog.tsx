interface ReviveDialogProps {
  username: string;
  cost: number;
  reviverHp: number;
  onRevive: () => void;
}

/// Multiplayer revive — shown when the local player is alive and stands
/// adjacent to a teammate's corpse where the teammate is still
/// spectating (`isReviveable`). Cost is precomputed by the parent
/// (the same formula the server uses) so the player sees what the
/// transaction will look like before clicking. Walking away dismisses
/// the dialog naturally — Game.tsx unmounts it when adjacency breaks.
///
/// The "1 HP both" safety floor isn't surfaced in the cost line — if
/// the reviver is on low HP and the tax would drop them to 0, the
/// server quietly clamps both sides to 1. The player sees the result
/// in the next snapshot tick.
export function ReviveDialog({ username, cost, reviverHp, onRevive }: ReviveDialogProps) {
  const wouldFloor = reviverHp - cost < 1;
  return (
    <div className="revive-dialog">
      <div className="revive-title">↑ {username} has fallen</div>
      <div className="revive-body">
        Spend <strong>{cost} HP</strong> to revive them with{" "}
        <strong>{wouldFloor ? 1 : cost} HP</strong>.
        {wouldFloor && (
          <div className="revive-note">
            You're low — both will end at 1 HP.
          </div>
        )}
      </div>
      <button type="button" className="revive-button" onClick={onRevive}>
        Revive
      </button>
    </div>
  );
}
