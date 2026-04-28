// Persistent player identity stored in localStorage.
//
// First visit: generate a UUID and prompt the user for a username, then save
// both. On every subsequent visit the same UUID is reused — that's what makes
// "your old corpses" persist across sessions. The username is editable; the
// id is sticky. Clearing localStorage means becoming a new player from the
// world's perspective (their old corpses remain, but no longer attributed
// "by" them — see PERSISTENT_WORLD.md edge case).
//
// **Dev affordance (`?alt`):** two tabs in the same browser share localStorage,
// so by default the second tab would identify as the first tab's player and
// stomp on the connection mapping (testing multiplayer locally without two
// browsers becomes impossible). When the URL carries `?alt` the identity is
// stored in sessionStorage instead — per-tab, fresh on first visit, stable
// across reloads of that one tab. Production users never hit this path.

const ID_KEY = "crawlers.playerId";
const NAME_KEY = "crawlers.username";

export function isAltMode(): boolean {
  if (typeof window === "undefined") return false;
  try {
    return new URLSearchParams(window.location.search).has("alt");
  } catch {
    return false;
  }
}

function storage(): Storage | null {
  try {
    return isAltMode() ? window.sessionStorage : window.localStorage;
  } catch {
    return null;
  }
}

export const USERNAME_MIN_LENGTH = 1;
export const USERNAME_MAX_LENGTH = 24;

export interface StoredIdentity {
  playerId: string;
  username: string;
}

export function loadIdentity(): StoredIdentity | null {
  const store = storage();
  if (!store) return null;
  try {
    const id = store.getItem(ID_KEY);
    const name = store.getItem(NAME_KEY);
    if (!id || !name) return null;
    if (!isUuid(id)) return null;
    const sanitized = sanitizeUsername(name);
    if (sanitized === null) return null;
    return { playerId: id, username: sanitized };
  } catch {
    // Storage unavailable (privacy mode, quota, etc.) — treat as anonymous.
    return null;
  }
}

export function saveIdentity(identity: StoredIdentity): void {
  const store = storage();
  if (!store) return;
  try {
    store.setItem(ID_KEY, identity.playerId);
    store.setItem(NAME_KEY, identity.username);
  } catch {
    // Best-effort — if storage fails the in-memory identity still works for
    // this session, the player just won't be recognised next time.
  }
}

export function newPlayerId(): string {
  // Prefer crypto.randomUUID() — clean, native, audited. But it's locked to
  // secure contexts (HTTPS / localhost), so on mobile over a LAN IP like
  // http://192.168.x.x:5173 it's undefined and a direct call throws,
  // which silently kills the identity-setup submit. Fall back to a v4 UUID
  // built from getRandomValues (available everywhere) so phone testing
  // and LAN demos work without a TLS-terminating proxy.
  const c = globalThis.crypto as Crypto | undefined;
  if (c?.randomUUID) return c.randomUUID();
  if (!c?.getRandomValues) {
    throw new Error("No crypto API available — cannot mint a player id.");
  }
  const bytes = new Uint8Array(16);
  c.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Trim, length-check, and reject control characters. Returns null for any
 * input that wouldn't pass the server's matching {@link sanitizeUsername}
 * — keeping the rules in sync stops a "valid locally, rejected by hub"
 * round-trip.
 */
export function sanitizeUsername(raw: string): string | null {
  const trimmed = raw.trim();
  if (trimmed.length < USERNAME_MIN_LENGTH) return null;
  if (trimmed.length > USERNAME_MAX_LENGTH) return null;
  for (const ch of trimmed) {
    const code = ch.charCodeAt(0);
    if (code < 0x20 || code === 0x7f) return null;
  }
  return trimmed;
}

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isUuid(value: string): boolean {
  return UUID_RE.test(value);
}
