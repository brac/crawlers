import type { ItemDto } from "../api/types";

interface InventoryProps {
  items: ItemDto[];
  usable: boolean; // can the player use consumables right now? (false in Resolution)
  onUse?: (itemId: string) => void;
}

export function Inventory({ items, usable, onUse }: InventoryProps) {
  if (items.length === 0) return null;

  // Hotkey indices are stable per-render: first 9 consumables get keys 1..9.
  const consumableHotkeys = new Map<string, number>();
  let hot = 1;
  for (const item of items) {
    if (item.isConsumable && hot <= 9) {
      consumableHotkeys.set(item.id, hot);
      hot++;
    }
  }

  return (
    <div className="inventory">
      <div className="inventory-header">Inventory</div>
      <ul className="inventory-list">
        {items.map((item) => {
          const key = consumableHotkeys.get(item.id);
          const active = usable && item.isConsumable && key !== undefined;
          const tappable = active && onUse !== undefined;
          return (
            <li
              key={item.id}
              className={`inventory-item ${item.isConsumable ? "consumable" : "passive"} ${tappable ? "tappable" : ""}`}
              onPointerDown={
                tappable
                  ? (e) => {
                      e.preventDefault();
                      onUse(item.id);
                    }
                  : undefined
              }
            >
              <span className={`inventory-key ${active ? "active" : ""}`}>
                {key !== undefined ? key : item.isConsumable ? "—" : "P"}
              </span>
              <span className="inventory-body">
                <span className="inventory-name">{item.name}</span>
                {item.description && (
                  <span className="inventory-desc">{item.description}</span>
                )}
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
