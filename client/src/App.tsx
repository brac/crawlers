import { useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import {
  connectLobby,
  createRoom,
  identify,
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
import {
  type StoredIdentity,
  isAltMode,
  loadIdentity,
  newPlayerId,
  saveIdentity,
} from "./identity";
import { IdentitySetup } from "./ui/IdentitySetup";
import { Lobby, type LobbyView } from "./ui/Lobby";
import { WorldStats } from "./ui/WorldStats";
import "./App.css";

type Phase =
  | { kind: "loading-assets" }
  | {
      kind: "identity-setup";
      // null on a true first visit (no localStorage entry); set when a
      // returning player is editing their persisted name.
      existing: StoredIdentity | null;
      busy: boolean;
      error: string | null;
    }
  | { kind: "lobby-connecting"; identity: StoredIdentity }
  | {
      kind: "lobby-menu";
      identity: StoredIdentity;
      busy: boolean;
      error: string | null;
    }
  | {
      kind: "lobby-room";
      identity: StoredIdentity;
      lobby: LobbyDto;
      starting: boolean;
      error: string | null;
    }
  | {
      kind: "in-game";
      identity: StoredIdentity;
      sessionId: string;
    }
  // Step 12 — public stats page. Reachable from the lobby menu and from
  // the run summary (visible from before-you-play and after-you-die).
  // No identity required; the connection is irrelevant here.
  | { kind: "world-stats" }
  | { kind: "fatal"; message: string };

const ERROR_TEXT: Record<string, string> = {
  NotFound: "Room not found.",
  Full: "That room is full.",
  AlreadyStarted: "That game has already started.",
  AlreadyInLobby: "You're already in that room.",
  NotHost: "Only the host can start the game.",
  NotInLobby: "You're not in a room.",
  NotIdentified: "Lost your identity. Please refresh.",
  InvalidUsername: "That name isn't allowed. Try a different one.",
  InvalidPlayerId: "Player id was invalid. Refresh to regenerate.",
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

  // Asset preload — runs once, independent of identity/lobby/game lifecycle.
  // Once assets resolve we either drop into identity setup (first visit) or
  // jump straight to the lobby connect (returning visitor).
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const lib = await loadAssets();
        if (cancelled) return;
        setAssets(lib);

        const stored = loadIdentity();
        if (stored) {
          setPhaseSync({ kind: "lobby-connecting", identity: stored });
        } else {
          setPhaseSync({
            kind: "identity-setup",
            existing: null,
            busy: false,
            error: null,
          });
        }
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

  // Lobby connection lifecycle. The dep is the player id during any
  // lobby-side phase (connecting / menu / room) — staying stable across
  // those transitions is what stops a successful Identify from immediately
  // triggering this effect's cleanup and tearing down the live connection.
  // The effect re-runs only when we (a) gain an identity for the first
  // time, (b) swap identity via "Change name", or (c) leave for in-game,
  // at which point Game.tsx opens its own /game connection.
  const lobbyPlayerId =
    phase.kind === "lobby-connecting" ||
    phase.kind === "lobby-menu" ||
    phase.kind === "lobby-room"
      ? phase.identity.playerId
      : null;
  const lobbyUsername =
    phase.kind === "lobby-connecting" ||
    phase.kind === "lobby-menu" ||
    phase.kind === "lobby-room"
      ? phase.identity.username
      : null;
  useEffect(() => {
    if (!lobbyPlayerId || !lobbyUsername) return;
    const identity = { playerId: lobbyPlayerId, username: lobbyUsername };

    let cancelled = false;
    void (async () => {
      try {
        const c = await connectLobby();
        if (cancelled) {
          await c.stop();
          return;
        }
        lobbyConnRef.current = c;

        await identify(c, identity.playerId, identity.username);
        if (cancelled) {
          await c.stop();
          lobbyConnRef.current = null;
          return;
        }

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
          if (cur.kind !== "lobby-room" && cur.kind !== "lobby-menu") return;
          void lobbyConnRef.current?.stop();
          lobbyConnRef.current = null;
          setPhaseSync({
            kind: "in-game",
            identity: cur.identity,
            sessionId,
          });
        });

        setPhaseSync({
          kind: "lobby-menu",
          identity,
          busy: false,
          error: null,
        });
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
  }, [lobbyPlayerId, lobbyUsername]);

  const handleIdentitySubmit = (username: string) => {
    const cur = phaseRef.current;
    if (cur.kind !== "identity-setup") return;
    try {
      const identity: StoredIdentity = {
        playerId: cur.existing?.playerId ?? newPlayerId(),
        username,
      };
      saveIdentity(identity);
      setPhaseSync({ kind: "lobby-connecting", identity });
    } catch (err) {
      // newPlayerId can throw if no crypto API is available (very old
      // browser, exotic webview). Surface it to the user instead of
      // silently swallowing the submit.
      const message = err instanceof Error ? err.message : String(err);
      setPhaseSync({ ...cur, error: message, busy: false });
    }
  };

  // Step 12 navigation — open the public stats page from the lobby menu
  // (or from the death screen). Tearing down the lobby connection isn't
  // strictly necessary, but the user is leaving the lobby UX entirely
  // and we don't want a stale connection holding a hub group when they
  // navigate back.
  const handleOpenStats = () => {
    void lobbyConnRef.current?.stop();
    lobbyConnRef.current = null;
    setPhaseSync({ kind: "world-stats" });
  };

  // Back from stats: if we have a stored identity, go straight to the
  // connecting phase (which will identify and land on the lobby menu).
  // Otherwise fall back to identity setup. This makes "Back" do the
  // right thing whether the visitor came from the menu or from a fresh
  // /api/world-stats deep-link in the future.
  const handleStatsBack = () => {
    const stored = loadIdentity();
    if (stored) {
      setPhaseSync({ kind: "lobby-connecting", identity: stored });
    } else {
      setPhaseSync({
        kind: "identity-setup",
        existing: null,
        busy: false,
        error: null,
      });
    }
  };

  // "Change name" from the lobby menu sends us back to identity setup with
  // the existing identity preloaded — we reuse the UUID and re-Identify with
  // the new username on the next lobby connect.
  const handleEditIdentity = () => {
    const cur = phaseRef.current;
    if (cur.kind !== "lobby-menu") return;
    void lobbyConnRef.current?.stop();
    lobbyConnRef.current = null;
    setPhaseSync({
      kind: "identity-setup",
      existing: cur.identity,
      busy: false,
      error: null,
    });
  };

  const handleCreate = async () => {
    const c = lobbyConnRef.current;
    const cur = phaseRef.current;
    if (!c || cur.kind !== "lobby-menu") return;
    setPhaseSync({ ...cur, busy: true, error: null });
    try {
      const result = await createRoom(c);
      setPhaseSync({
        kind: "lobby-room",
        identity: cur.identity,
        lobby: result.lobby,
        starting: false,
        error: null,
      });
    } catch (err) {
      setPhaseSync({ ...cur, busy: false, error: mapLobbyError(err) });
    }
  };

  const handleJoin = async (code: string) => {
    const c = lobbyConnRef.current;
    const cur = phaseRef.current;
    if (!c || cur.kind !== "lobby-menu") return;
    setPhaseSync({ ...cur, busy: true, error: null });
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
          identity: cur.identity,
          sessionId: result.lobby.sessionId,
        });
        return;
      }

      setPhaseSync({
        kind: "lobby-room",
        identity: cur.identity,
        lobby: result.lobby,
        starting: false,
        error: null,
      });
    } catch (err) {
      setPhaseSync({ ...cur, busy: false, error: mapLobbyError(err) });
    }
  };

  const handleLeave = async () => {
    const c = lobbyConnRef.current;
    const cur = phaseRef.current;
    if (!c || cur.kind !== "lobby-room") return;
    try {
      await leaveRoom(c);
    } catch {
      // Ignore — server-side state is the source of truth and we're going
      // back to the menu regardless.
    }
    setPhaseSync({
      kind: "lobby-menu",
      identity: cur.identity,
      busy: false,
      error: null,
    });
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
        <AltModeBadge />
        <div className="lobby-screen">
          <div className="lobby-card lobby-card-fatal">
            <div className="lobby-title">Crawlers</div>
            <div className="lobby-error">{phase.message}</div>
          </div>
        </div>
      </div>
    );
  }

  if (phase.kind === "loading-assets" || !assets) {
    return (
      <div className="app">
        <AltModeBadge />
        <div className="lobby-screen">
          <div className="lobby-card lobby-card-spinner">
            <div className="lobby-title">CRAWLERS</div>
            <div className="lobby-spinner-text">Loading…</div>
          </div>
        </div>
      </div>
    );
  }

  if (phase.kind === "identity-setup") {
    return (
      <div className="app">
        <AltModeBadge />
        <IdentitySetup
          initial={phase.existing?.username ?? ""}
          isReturning={phase.existing !== null}
          busy={phase.busy}
          error={phase.error}
          onSubmit={handleIdentitySubmit}
        />
      </div>
    );
  }

  if (phase.kind === "lobby-connecting") {
    return (
      <div className="app">
        <AltModeBadge />
        <div className="lobby-screen">
          <div className="lobby-card lobby-card-spinner">
            <div className="lobby-title">CRAWLERS</div>
            <div className="lobby-spinner-text">Connecting…</div>
          </div>
        </div>
      </div>
    );
  }

  if (phase.kind === "in-game") {
    return (
      <div className="app">
        <AltModeBadge />
        <Game
          assets={assets}
          sessionId={phase.sessionId}
          localPlayerId={phase.identity.playerId}
          onOpenStats={handleOpenStats}
        />
      </div>
    );
  }

  if (phase.kind === "world-stats") {
    return (
      <div className="app">
        <AltModeBadge />
        <WorldStats onBack={handleStatsBack} />
      </div>
    );
  }

  const view: LobbyView =
    phase.kind === "lobby-menu"
      ? {
          kind: "menu",
          username: phase.identity.username,
          busy: phase.busy,
          error: phase.error,
        }
      : {
          kind: "room",
          lobby: phase.lobby,
          localPlayerId: phase.identity.playerId,
          starting: phase.starting,
          error: phase.error,
        };

  return (
    <div className="app">
      <AltModeBadge />
      <Lobby
        view={view}
        onCreate={handleCreate}
        onJoin={handleJoin}
        onLeave={handleLeave}
        onStart={handleStart}
        onEditIdentity={handleEditIdentity}
        onOpenStats={handleOpenStats}
      />
    </div>
  );
}

// Persistent visual marker shown only when the URL carries `?alt`. Confirms
// at a glance that this tab is on a per-tab sessionStorage identity, not the
// regular localStorage one — the difference between "two players" and "two
// tabs of the same player" when testing multiplayer locally.
function AltModeBadge() {
  if (!isAltMode()) return null;
  return <div className="alt-mode-badge">ALT</div>;
}
