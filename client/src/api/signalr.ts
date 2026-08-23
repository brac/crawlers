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

/**
 * Bind this connection to a player in a session.
 *
 * `playerId` is public (every teammate sees it), so the server will not
 * accept it as proof of who we are. `sessionToken` is the secret the server
 * minted for this seat and handed back from CreateRoom / JoinRoomByCode; it
 * travels in the invocation body, never in the URL or a query string, and
 * must never be logged. A wrong or missing token comes back as a generic
 * "JoinRejected" HubException.
 */
export async function joinSession(
  connection: HubConnection,
  sessionId: string,
  playerId: string,
  sessionToken: string,
): Promise<GameStateSnapshotDto> {
  return connection.invoke<GameStateSnapshotDto>(
    "JoinSession",
    sessionId,
    playerId,
    sessionToken,
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

export function setSpectatorTarget(
  connection: HubConnection,
  targetId: string,
): Promise<void> {
  return connection.invoke("SetSpectatorTarget", targetId);
}

export function reviveTeammate(
  connection: HubConnection,
  corpsePlayerId: string,
): Promise<void> {
  return connection.invoke("ReviveTeammate", corpsePlayerId);
}

export function onSnapshot(
  connection: HubConnection,
  handler: (snapshot: GameStateSnapshotDto) => void,
): () => void {
  connection.on("ReceiveSnapshot", handler);
  return () => connection.off("ReceiveSnapshot", handler);
}
