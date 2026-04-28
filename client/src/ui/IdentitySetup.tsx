import { useState } from "react";
import {
  USERNAME_MAX_LENGTH,
  USERNAME_MIN_LENGTH,
  sanitizeUsername,
} from "../identity";

interface IdentitySetupProps {
  initial: string;
  // True the very first time we ask (no localStorage identity yet) — the
  // copy and the "Continue" button label both adapt because a returning
  // player editing their name reads quite differently from someone naming
  // their character for the first time.
  isReturning: boolean;
  busy: boolean;
  error: string | null;
  onSubmit: (username: string) => void;
}

export function IdentitySetup(props: IdentitySetupProps) {
  const [value, setValue] = useState(props.initial);
  const sanitized = sanitizeUsername(value);
  const canSubmit = !props.busy && sanitized !== null;

  return (
    <div className="lobby-screen">
      <div className="lobby-card">
        <div className="lobby-title">CRAWLERS</div>
        <div className="lobby-section-label">
          {props.isReturning ? "Welcome back" : "Choose a name"}
        </div>
        <p className="lobby-identity-blurb">
          {props.isReturning
            ? "Your past corpses are still out there. Edit your name or keep it."
            : "Used to mark your corpses for every player who comes after you."}
        </p>
        <form
          className="lobby-identity-form"
          onSubmit={(e) => {
            e.preventDefault();
            if (canSubmit && sanitized) props.onSubmit(sanitized);
          }}
        >
          <input
            className="lobby-identity-input"
            type="text"
            maxLength={USERNAME_MAX_LENGTH}
            minLength={USERNAME_MIN_LENGTH}
            autoFocus
            autoComplete="off"
            spellCheck={false}
            placeholder="Your name"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            disabled={props.busy}
            aria-label="Username"
          />
          <button
            type="submit"
            className="lobby-primary"
            disabled={!canSubmit}
          >
            {props.busy ? "Saving…" : props.isReturning ? "Continue" : "Enter the dungeon"}
          </button>
        </form>
        {props.error && <div className="lobby-error">{props.error}</div>}
      </div>
    </div>
  );
}
