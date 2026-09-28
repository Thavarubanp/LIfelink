/**
 * Idle-timeout bookkeeping shared by every open tab (through localStorage).
 *
 * The backend ends a session after Session:IdleTimeoutMinutes without activity. Only requests marked with the
 * X-LifeLink-Activity header count as activity there, so the app marks user-driven requests (never background
 * polling) and remembers when the backend last confirmed activity. Every tab counts down from that same
 * confirmed time, so all tabs warn and sign out together, and never later than the backend.
 */

export const ACTIVITY_HEADER = 'X-LifeLink-Activity';
export const SESSION_ENDED_HEADER = 'x-session-ended';

const LAST_ACTIVITY_KEY = 'lifelink_last_activity';
const LOGOUT_KEY = 'lifelink_logout';
const TOKEN_KEY = 'lifelink_token';
const USER_KEY = 'lifelink_user';

// A user-driven request reports activity at most every 15 seconds (per browser)
const REQUEST_REPORT_INTERVAL_MS = 15000;

let lastMarkedAt = 0;
let idleMinutesSetting = null;

/** The configured idle timeout (minutes), known once the signed-in user is loaded; used in the sign-in message. */
export const setIdleMinutesSetting = (minutes) => {
  idleMinutesSetting = minutes || null;
};
export const getIdleMinutesSetting = () => idleMinutesSetting;

const read = (key) => {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
};

const write = (key, value) => {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // Storage unavailable: the backend still enforces the timeout
  }
};

/** Time (ms) the backend last confirmed activity; 0 when unknown. */
export const getLastActivity = () => Number(read(LAST_ACTIVITY_KEY)) || 0;

/** Records that the backend accepted activity sent at `sentAt` (keeps the latest). */
export const markActivityConfirmed = (sentAt) => {
  if (sentAt > getLastActivity()) write(LAST_ACTIVITY_KEY, String(sentAt));
};

/** Whether a user-driven request sent now should carry the activity header. */
export const takeActivityMark = (now = Date.now(), minIntervalMs = REQUEST_REPORT_INTERVAL_MS) => {
  if (now - Math.max(lastMarkedAt, getLastActivity()) < minIntervalMs) return false;
  lastMarkedAt = now;
  return true;
};

/** A marked request failed before reaching the backend: allow the next one to report again. */
export const releaseActivityMark = () => {
  lastMarkedAt = 0;
};

/**
 * Clears the session in this browser and tells the other tabs (they listen for LOGOUT_KEY).
 * reason: 'idle' | 'signed-out' | 'expired'
 */
export const clearSession = (reason, idleMinutes) => {
  write(LOGOUT_KEY, JSON.stringify({ reason, idleMinutes: idleMinutes ?? null, at: Date.now() }));
  write(TOKEN_KEY, null);
  write(USER_KEY, null);
  write(LAST_ACTIVITY_KEY, null);
  lastMarkedAt = 0;
};

/** Sign-in page address for a sign-out reason. */
export const loginPathFor = (reason, idleMinutes) => {
  if (reason === 'idle') {
    return idleMinutes ? `/login?reason=idle&m=${encodeURIComponent(idleMinutes)}` : '/login?reason=idle';
  }
  if (reason === 'expired') return '/login?expired=1';
  return '/login';
};

/** Parses the cross-tab logout message from a storage event value. */
export const parseLogoutMessage = (value) => {
  try {
    return value ? JSON.parse(value) : null;
  } catch {
    return null;
  }
};

export const STORAGE_KEYS = { LAST_ACTIVITY_KEY, LOGOUT_KEY, TOKEN_KEY };

/** New random key for one form submission (Idempotency-Key header). */
export const newIdempotencyKey = () =>
  (globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`);
