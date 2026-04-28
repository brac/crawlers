import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import type { LobbyDto, LobbyMembershipDto } from "./types";

const HUB_URL =
  (import.meta.env.VITE_LOBBY_URL as string | undefined) ?? "/lobby";

export async function connectLobby(): Promise<HubConnection> {
  const connection = new HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
  await connection.start();
  return connection;
}

// Asserts the persistent identity (UUID + username) for this connection.
// Must be called before CreateRoom / JoinRoomByCode — the server gates
// every lobby action on it and throws "NotIdentified" otherwise. Calling
// it again with a different username renames the player; the UUID is
// sticky for the life of the localStorage entry.
export function identify(
  connection: HubConnection,
  playerId: string,
  username: string,
): Promise<void> {
  return connection.invoke("Identify", playerId, username);
}

export function createRoom(
  connection: HubConnection,
): Promise<LobbyMembershipDto> {
  return connection.invoke<LobbyMembershipDto>("CreateRoom");
}

export function joinRoomByCode(
  connection: HubConnection,
  code: string,
): Promise<LobbyMembershipDto> {
  return connection.invoke<LobbyMembershipDto>("JoinRoomByCode", code);
}

export function leaveRoom(connection: HubConnection): Promise<void> {
  return connection.invoke("LeaveRoom");
}

export function startGame(connection: HubConnection): Promise<void> {
  return connection.invoke("StartGame");
}

export function onLobbyUpdate(
  connection: HubConnection,
  handler: (lobby: LobbyDto) => void,
): () => void {
  connection.on("ReceiveLobbyUpdate", handler);
  return () => connection.off("ReceiveLobbyUpdate", handler);
}

export function onGameStarting(
  connection: HubConnection,
  handler: (sessionId: string) => void,
): () => void {
  connection.on("GameStarting", handler);
  return () => connection.off("GameStarting", handler);
}
