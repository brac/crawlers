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
      username: string;
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
  onEditIdentity: () => void;
  onOpenStats: () => void;
}

export function Lobby(props: LobbyProps) {
  return (
    <div className="lobby-screen">
      <div className="lobby-card">
        <div className="lobby-title">CRAWLERS</div>
        {props.view.kind === "menu" ? (
          <LobbyMenu
            username={props.view.username}
            busy={props.view.busy}
            error={props.view.error}
            onCreate={props.onCreate}
            onJoin={props.onJoin}
            onEditIdentity={props.onEditIdentity}
            onOpenStats={props.onOpenStats}
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
  username,
  busy,
  error,
  onCreate,
  onJoin,
  onEditIdentity,
  onOpenStats,
}: {
  username: string;
  busy: boolean;
  error: string | null;
  onCreate: () => void;
  onJoin: (code: string) => void;
  onEditIdentity: () => void;
  onOpenStats: () => void;
}) {
  const [code, setCode] = useState("");
  const trimmed = code.trim();
  const canJoin = !busy && trimmed.length === 6;
  // Dev only — show a "spawn alt player" shortcut on localhost. Two tabs in
  // the same browser share localStorage and would collide on the same UUID,
  // so this opens a separate window with `?alt` (sessionStorage identity).
  const isLocalhost =
    typeof window !== "undefined" &&
    (window.location.hostname === "localhost" ||
      window.location.hostname === "127.0.0.1");
  const showAltLink =
    isLocalhost &&
    typeof window !== "undefined" &&
    !new URLSearchParams(window.location.search).has("alt");

  return (
    <>
      <div className="lobby-identity-row">
        <span className="lobby-identity-label">Playing as</span>
        <span className="lobby-identity-name">{username}</span>
        <button
          type="button"
          className="lobby-link"
          onClick={onEditIdentity}
          disabled={busy}
        >
          Change
        </button>
      </div>

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

      <button
        type="button"
        className="lobby-link lobby-alt-link"
        onClick={onOpenStats}
      >
        World stats →
      </button>

      {showAltLink && (
        <button
          type="button"
          className="lobby-link lobby-alt-link"
          onClick={() => {
            const url = new URL(window.location.href);
            url.searchParams.set("alt", "1");
            window.open(url.toString(), "_blank", "noopener");
          }}
        >
          + Open second test player (new window)
        </button>
      )}
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
              <span className="lobby-member-name">{m.username}</span>
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
