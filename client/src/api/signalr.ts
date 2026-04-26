import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import type { GameStateSnapshotDto, MoveDirection } from "./types";

// Default to a same-origin relative path so Vite can proxy SignalR (HTTP +
// WebSocket) to the C# server on localhost:5238. This lets a browser on
// another LAN machine hit Vite's host and still reach the hub.
const HUB_URL =
  (import.meta.env.VITE_HUB_URL as string | undefined) ?? "/game";

export async function connect(): Promise<HubConnection> {
  const connection = new HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
  await connection.start();
  return connection;
}

export async function joinNewSession(
  connection: HubConnection,
  seed?: number,
): Promise<GameStateSnapshotDto> {
  return connection.invoke<GameStateSnapshotDto>(
    "JoinNewSession",
    seed ?? null,
  );
}

export function move(
  connection: HubConnection,
  direction: MoveDirection,
): Promise<void> {
  return connection.invoke("Move", direction);
}

export function flee(connection: HubConnection): Promise<void> {
  return connection.invoke("Flee");
}

// Renamed from `useItem` so eslint's react-hooks/rules-of-hooks doesn't
// mistake this SignalR helper for a React hook.
export function invokeUseItem(
  connection: HubConnection,
  itemId: string,
): Promise<void> {
  return connection.invoke("UseItem", itemId);
}

export function descend(connection: HubConnection): Promise<void> {
  return connection.invoke("Descend");
}

export function onSnapshot(
  connection: HubConnection,
  handler: (snapshot: GameStateSnapshotDto) => void,
): () => void {
  connection.on("ReceiveSnapshot", handler);
  return () => connection.off("ReceiveSnapshot", handler);
}
