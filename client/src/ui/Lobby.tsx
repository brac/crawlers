import { useState } from "react";
import type { LobbyDto } from "../api/types";
import { LobbyStatus } from "../api/types";

// `navigator.clipboard.writeText` requires a secure context. On the LAN over
// plain HTTP (`http://192.168.x.x:5173`) iOS Safari silently rejects it, so
// we fall back to a hidden textarea + `execCommand('copy')` — deprecated but
// still the only thing that works in insecure contexts on mobile Safari.
async function writeClipboard(text: string): Promise<boolean> {
  if (window.isSecureContext && navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      /* fall through */
    }
  }
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    // Off-screen but rendered — must be in the DOM and selectable.
    ta.style.position = "fixed";
    ta.style.top = "0";
    ta.style.left = "0";
    ta.style.opacity = "0";
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    ta.setSelectionRange(0, text.length);
    const ok = document.execCommand("copy");
    document.body.removeChild(ta);
    return ok;
  } catch {
    return false;
  }
}

export type LobbyView =
  | {
      kind: "menu";
      busy: boolean;
      error: string | null;
    }
  | {
      kind: "room";
      lobby: LobbyDto;
      localPlayerId: string;
      starting: boolean;
      error: string | null;
    };

interface LobbyProps {
  view: LobbyView;
  onCreate: () => void;
  onJoin: (code: string) => void;
  onLeave: () => void;
  onStart: () => void;
}

export function Lobby(props: LobbyProps) {
  return (
    <div className="lobby-screen">
      <div className="lobby-card">
        <div className="lobby-title">CRAWLERS</div>
        {props.view.kind === "menu" ? (
          <LobbyMenu
            busy={props.view.busy}
            error={props.view.error}
            onCreate={props.onCreate}
            onJoin={props.onJoin}
          />
        ) : (
          <LobbyRoom
            lobby={props.view.lobby}
            localPlayerId={props.view.localPlayerId}
            starting={props.view.starting}
            error={props.view.error}
            onLeave={props.onLeave}
            onStart={props.onStart}
          />
        )}
      </div>
    </div>
  );
}

function LobbyMenu({
  busy,
  error,
  onCreate,
  onJoin,
}: {
  busy: boolean;
  error: string | null;
  onCreate: () => void;
  onJoin: (code: string) => void;
}) {
  const [code, setCode] = useState("");
  const trimmed = code.trim();
  const canJoin = !busy && trimmed.length === 6;

  return (
    <>
      <button
        type="button"
        className="lobby-primary"
        onClick={onCreate}
        disabled={busy}
      >
        Create new room
      </button>

      <div className="lobby-divider">
        <span>or join with a code</span>
      </div>

      <form
        className="lobby-join-form"
        onSubmit={(e) => {
          e.preventDefault();
          if (canJoin) onJoin(trimmed.toUpperCase());
        }}
      >
        <input
          className="lobby-code-input"
          type="text"
          maxLength={6}
          autoCapitalize="characters"
          autoComplete="off"
          inputMode="text"
          spellCheck={false}
          placeholder="ABCDEF"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          disabled={busy}
          aria-label="Room code"
        />
        <button
          type="submit"
          className="lobby-secondary"
          disabled={!canJoin}
        >
          Join
        </button>
      </form>

      {error && <div className="lobby-error">{error}</div>}
    </>
  );
}

function LobbyRoom({
  lobby,
  localPlayerId,
  starting,
  error,
  onLeave,
  onStart,
}: {
  lobby: LobbyDto;
  localPlayerId: string;
  starting: boolean;
  error: string | null;
  onLeave: () => void;
  onStart: () => void;
}) {
  const isHost = lobby.hostPlayerId === localPlayerId;
  const isInGame = lobby.status === LobbyStatus.InGame;
  const [copied, setCopied] = useState(false);

  const copyCode = async () => {
    if (await writeClipboard(lobby.code)) {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    }
  };

  return (
    <>
      <div className="lobby-section-label">Room code</div>
      <div className="lobby-code-row">
        <span className="lobby-code-display">{lobby.code}</span>
        <button
          type="button"
          className="lobby-copy"
          onClick={copyCode}
          aria-label="Copy room code"
        >
          {copied ? "Copied" : "Copy"}
        </button>
      </div>

      <div className="lobby-section-label">
        Party ({lobby.members.length} / {lobby.maxPlayers})
      </div>
      <ul className="lobby-members">
        {lobby.members.map((m) => {
          const isYou = m.playerId === localPlayerId;
          return (
            <li key={m.playerId} className="lobby-member">
              <span className="lobby-member-dot" aria-hidden />
              <span className="lobby-member-name">
                Player {m.playerId.slice(0, 4).toUpperCase()}
              </span>
              {m.isHost && <span className="lobby-badge host">Host</span>}
              {isYou && <span className="lobby-badge you">You</span>}
            </li>
          );
        })}
      </ul>

      <div className="lobby-actions">
        <button
          type="button"
          className="lobby-secondary"
          onClick={onLeave}
          disabled={starting}
        >
          Leave
        </button>
        {isHost && (
          <button
            type="button"
            className="lobby-primary"
            onClick={onStart}
            disabled={starting || isInGame}
          >
            {isInGame ? "Starting…" : starting ? "Starting…" : "Start game"}
          </button>
        )}
        {!isHost && (
          <span className="lobby-waiting-text">
            Waiting for host to start…
          </span>
        )}
      </div>

      {error && <div className="lobby-error">{error}</div>}
    </>
  );
}
