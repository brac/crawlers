import { useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import {
  connectLobby,
  createRoom,
  joinRoomByCode,
  leaveRoom,
  onGameStarting,
  onLobbyUpdate,
  startGame,
} from "./api/lobby";
import type { LobbyDto } from "./api/types";
import { LobbyStatus } from "./api/types";
import { Game } from "./Game";
import { type AssetLibrary, loadAssets } from "./game/assets";
import { Lobby, type LobbyView } from "./ui/Lobby";
import "./App.css";

type Phase =
  | { kind: "loading-assets" }
  | { kind: "lobby-connecting" }
  | { kind: "lobby-menu"; busy: boolean; error: string | null }
  | {
      kind: "lobby-room";
      lobby: LobbyDto;
      localPlayerId: string;
      starting: boolean;
      error: string | null;
    }
  | { kind: "in-game"; sessionId: string; localPlayerId: string }
  | { kind: "fatal"; message: string };

const ERROR_TEXT: Record<string, string> = {
  NotFound: "Room not found.",
  Full: "That room is full.",
  AlreadyStarted: "That game has already started.",
  AlreadyInLobby: "You're already in that room.",
  NotHost: "Only the host can start the game.",
  NotInLobby: "You're not in a room.",
};

function mapLobbyError(err: unknown): string {
  const raw = err instanceof Error ? err.message : String(err);
  for (const code of Object.keys(ERROR_TEXT))
    if (raw.includes(code)) return ERROR_TEXT[code];
  return "Something went wrong. Please try again.";
}

export default function App() {
  const [phase, setPhase] = useState<Phase>({ kind: "loading-assets" });
  const [assets, setAssets] = useState<AssetLibrary | null>(null);
  const lobbyConnRef = useRef<HubConnection | null>(null);
  const phaseRef = useRef<Phase>(phase);

  // Always go through this so phaseRef stays accurate between renders. The
  // catch in handleStart checks the ref to decide whether to surface an error,
  // and React's setState is async — without a synchronous ref bump, two state
  // updates in the same tick (GameStarting handler vs the failed-invoke catch)
  // race and the catch wins.
  const setPhaseSync = (next: Phase) => {
    phaseRef.current = next;
    setPhase(next);
  };

  // Asset preload — runs once, independent of lobby/game lifecycle.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const lib = await loadAssets();
        if (cancelled) return;
        setAssets(lib);
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : String(err);
        setPhaseSync({ kind: "fatal", message: `Failed to load assets: ${message}` });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Lobby connection lifecycle. Fires once when assets are ready; tears down
  // on unmount or when the GameStarting handler stops the connection itself
  // (Game.tsx then owns the /game connection).
  useEffect(() => {
    if (!assets) return;

    let cancelled = false;
    setPhaseSync({ kind: "lobby-connecting" });

    void (async () => {
      try {
        const c = await connectLobby();
        // Same StrictMode caveat as Game.tsx: only assign to the ref *after*
        // confirming this effect run wasn't cancelled, otherwise a stale
        // mount can overwrite the live connection with a dead one.
        if (cancelled) {
          await c.stop();
          return;
        }
        lobbyConnRef.current = c;

        onLobbyUpdate(c, (lobby) => {
          const cur = phaseRef.current;
          if (cur.kind !== "lobby-room") return;
          if (cur.lobby.id !== lobby.id) return;
          setPhaseSync({ ...cur, lobby });
        });

        onGameStarting(c, (sessionId) => {
          // Tear down the lobby connection — Game.tsx will open a fresh
          // connection to /game on mount.
          const cur = phaseRef.current;
          const localPlayerId =
            cur.kind === "lobby-room" ? cur.localPlayerId : "";
          void lobbyConnRef.current?.stop();
          lobbyConnRef.current = null;
          setPhaseSync({ kind: "in-game", sessionId, localPlayerId });
        });

        setPhaseSync({ kind: "lobby-menu", busy: false, error: null });
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : String(err);
        setPhaseSync({ kind: "fatal", message: `Lobby connection failed: ${message}` });
      }
    })();

    return () => {
      cancelled = true;
      void lobbyConnRef.current?.stop();
      lobbyConnRef.current = null;
    };
  }, [assets]);

  const handleCreate = async () => {
    const c = lobbyConnRef.current;
    if (!c || phaseRef.current.kind !== "lobby-menu") return;
    setPhaseSync({ kind: "lobby-menu", busy: true, error: null });
    try {
      const result = await createRoom(c);
      setPhaseSync({
        kind: "lobby-room",
        lobby: result.lobby,
        localPlayerId: result.localPlayerId,
        starting: false,
        error: null,
      });
    } catch (err) {
      setPhaseSync({ kind: "lobby-menu", busy: false, error: mapLobbyError(err) });
    }
  };

  const handleJoin = async (code: string) => {
    const c = lobbyConnRef.current;
    if (!c || phaseRef.current.kind !== "lobby-menu") return;
    setPhaseSync({ kind: "lobby-menu", busy: true, error: null });
    try {
      const result = await joinRoomByCode(c, code);

      // Late-join: lobby is already InGame and carries the session id, so we
      // skip the lobby-room view and drop the joiner straight into /game.
      if (
        result.lobby.status === LobbyStatus.InGame &&
        result.lobby.sessionId
      ) {
        void lobbyConnRef.current?.stop();
        lobbyConnRef.current = null;
        setPhaseSync({
          kind: "in-game",
          sessionId: result.lobby.sessionId,
          localPlayerId: result.localPlayerId,
        });
        return;
      }

      setPhaseSync({
        kind: "lobby-room",
        lobby: result.lobby,
        localPlayerId: result.localPlayerId,
        starting: false,
        error: null,
      });
    } catch (err) {
      setPhaseSync({ kind: "lobby-menu", busy: false, error: mapLobbyError(err) });
    }
  };

  const handleLeave = async () => {
    const c = lobbyConnRef.current;
    if (!c) return;
    try {
      await leaveRoom(c);
    } catch {
      // Ignore — server-side state is the source of truth and we're going
      // back to the menu regardless.
    }
    setPhaseSync({ kind: "lobby-menu", busy: false, error: null });
  };

  const handleStart = async () => {
    const c = lobbyConnRef.current;
    const cur = phaseRef.current;
    if (!c || cur.kind !== "lobby-room") return;
    setPhaseSync({ ...cur, starting: true, error: null });
    try {
      await startGame(c);
      // The actual transition to in-game happens when GameStarting arrives
      // (sent to the whole group, including this caller).
    } catch (err) {
      // GameStarting often lands before the StartGame invocation ack — its
      // handler stops the connection, which makes the still-pending invoke
      // reject. If we've already transitioned to in-game, that rejection is
      // expected; only surface an error if we're still in the lobby.
      const now = phaseRef.current;
      if (now.kind === "lobby-room") {
        setPhaseSync({ ...now, starting: false, error: mapLobbyError(err) });
      }
    }
  };

  if (phase.kind === "fatal") {
    return (
      <div className="app">
        <div className="lobby-screen">
          <div className="lobby-card lobby-card-fatal">
            <div className="lobby-title">Crawlers</div>
            <div className="lobby-error">{phase.message}</div>
          </div>
        </div>
      </div>
    );
  }

  if (
    phase.kind === "loading-assets" ||
    phase.kind === "lobby-connecting" ||
    !assets
  ) {
    const label =
      phase.kind === "lobby-connecting" ? "Connecting…" : "Loading…";
    return (
      <div className="app">
        <div className="lobby-screen">
          <div className="lobby-card lobby-card-spinner">
            <div className="lobby-title">CRAWLERS</div>
            <div className="lobby-spinner-text">{label}</div>
          </div>
        </div>
      </div>
    );
  }

  if (phase.kind === "in-game") {
    return (
      <div className="app">
        <Game
          assets={assets}
          sessionId={phase.sessionId}
          localPlayerId={phase.localPlayerId}
        />
      </div>
    );
  }

  const view: LobbyView =
    phase.kind === "lobby-menu"
      ? { kind: "menu", busy: phase.busy, error: phase.error }
      : {
          kind: "room",
          lobby: phase.lobby,
          localPlayerId: phase.localPlayerId,
          starting: phase.starting,
          error: phase.error,
        };

  return (
    <div className="app">
      <Lobby
        view={view}
        onCreate={handleCreate}
        onJoin={handleJoin}
        onLeave={handleLeave}
        onStart={handleStart}
      />
    </div>
  );
}
