// The secret half of this client's identity for one run.
//
// The server hands this back from CreateRoom / JoinRoomByCode as the return
// value of the hub invocation, so it reaches this browser tab and nowhere
// else. GameHub.JoinSession requires it to bind the /game connection to our
// player. Our player id is not a secret (every teammate sees it in the lobby
// roster and in every snapshot), so the token is the only thing standing
// between us and someone else driving our character.
//
// Kept in a module-level variable rather than localStorage or sessionStorage
// on purpose:
//
//   - a credential in web storage outlives the run it was minted for and is
//     readable by anything else running on the origin;
//   - it dies with the tab, which matches the token's own lifetime (it is
//     scoped to one seat in one session and is worthless afterwards);
//   - there is no resume-after-refresh flow that would need it to persist.
//
// Never log this, never put it in a URL or query string, and never render it.

let token: string | null = null;

/** Store the token returned by a successful lobby create/join. */
export function rememberSessionToken(value: string): void {
  token = value;
}

/** The token for the current run, or null if we have not joined a lobby. */
export function readSessionToken(): string | null {
  return token;
}

/** Drop the token (leaving a room, or going back to the menu). */
export function forgetSessionToken(): void {
  token = null;
}
