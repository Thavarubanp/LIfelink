import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { Clock, Loader2, LogOut } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { authApi } from '../../api';
import {
  STORAGE_KEYS,
  clearSession,
  getLastActivity,
  loginPathFor,
  parseLogoutMessage,
  setIdleMinutesSetting,
} from '../../session/sessionActivity';

// Pages where a signed-in account only waits (registration review, suspension): no idle sign-out there, the
// session is kept alive quietly (the 120-minute token limit still applies)
const PAUSED_PATHS = ['/hospital/waiting-approval', '/governance/status'];

// What counts as the user being active
const USER_EVENTS = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll', 'wheel'];

// Input reports activity to the backend at most every 30 seconds
const HEARTBEAT_INTERVAL_MS = 30000;

const formatCountdown = (ms) => {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = String(total % 60).padStart(2, '0');
  return `${minutes}:${seconds}`;
};

/**
 * Idle timeout for every signed-in role. After Session:IdleTimeoutMinutes without activity the user is signed out
 * (in every tab) and sent to Sign In with a message; Session:WarningMinutes before that a dialog counts down with
 * "Stay signed in" and "Sign out". The backend enforces the same timeout on every request.
 */
export const IdleSessionManager = () => {
  const { user } = useAuth();
  const location = useLocation();
  if (!user) return null;
  const paused = PAUSED_PATHS.some((path) => location.pathname.startsWith(path));
  return <IdleSession user={user} paused={paused} />;
};

const IdleSession = ({ user, paused }) => {
  const { logout } = useAuth();
  const idleMinutes = user.sessionIdleTimeoutMinutes > 0 ? user.sessionIdleTimeoutMinutes : 10;
  const warningMinutes = user.sessionWarningMinutes >= 0 ? user.sessionWarningMinutes : 1;
  const idleMs = idleMinutes * 60000;
  const warningMs = Math.min(warningMinutes * 60000, idleMs);

  const [remainingMs, setRemainingMs] = useState(null); // shown while the warning is open
  const [staying, setStaying] = useState(false);
  const warningOpen = useRef(false);
  const heartbeatInFlight = useRef(false);
  const signingOut = useRef(false);
  const tickRef = useRef(() => {});

  useEffect(() => {
    setIdleMinutesSetting(idleMinutes);
  }, [idleMinutes]);

  const sendHeartbeat = useCallback(async () => {
    if (heartbeatInFlight.current) return;
    heartbeatInFlight.current = true;
    try {
      await authApi.recordActivity();
    } catch {
      // A 401 (session already ended) is handled by the API client
    } finally {
      heartbeatInFlight.current = false;
    }
  }, []);

  const signOutForInactivity = useCallback(async () => {
    if (signingOut.current) return;
    signingOut.current = true;
    try {
      await authApi.logout('idle');
    } catch {
      // The backend ends idle sessions on its own as well
    }
    clearSession('idle', idleMinutes);
    window.location.href = loginPathFor('idle', idleMinutes);
  }, [idleMinutes]);

  // Countdown: every tab counts from the same backend-confirmed activity time
  useEffect(() => {
    if (paused) {
      // The dialog is hidden on paused pages (see render); a stale countdown is recomputed when leaving them
      warningOpen.current = false;
      tickRef.current = () => {};
      return undefined;
    }
    const tick = () => {
      if (signingOut.current) return;
      const last = getLastActivity();
      if (!last) {
        sendHeartbeat(); // not known yet in this browser: ask the backend to confirm now
        return;
      }
      const idle = Date.now() - last;
      if (idle >= idleMs) {
        signOutForInactivity();
      } else if (idle >= idleMs - warningMs) {
        warningOpen.current = true;
        setRemainingMs(idleMs - idle);
      } else if (warningOpen.current) {
        warningOpen.current = false;
        setRemainingMs(null);
      }
    };
    tickRef.current = tick;
    tick();
    const id = setInterval(tick, 1000);
    const onVisible = () => document.visibilityState === 'visible' && tick();
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      clearInterval(id);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [paused, idleMs, warningMs, sendHeartbeat, signOutForInactivity]);

  // User activity keeps the session alive (not while the warning is open: then only its buttons count)
  useEffect(() => {
    if (paused) return undefined;
    let lastSeen = 0;
    const onActivity = () => {
      if (warningOpen.current) return;
      const now = Date.now();
      if (now - lastSeen < 1000) return;
      lastSeen = now;
      if (now - getLastActivity() >= HEARTBEAT_INTERVAL_MS) sendHeartbeat();
    };
    USER_EVENTS.forEach((type) => window.addEventListener(type, onActivity, { passive: true, capture: true }));
    return () => USER_EVENTS.forEach((type) => window.removeEventListener(type, onActivity, { capture: true }));
  }, [paused, sendHeartbeat]);

  // Waiting / suspended pages: quiet keep-alive instead of an idle sign-out
  useEffect(() => {
    if (!paused) return undefined;
    sendHeartbeat();
    const id = setInterval(sendHeartbeat, Math.min(120000, idleMs / 3));
    return () => clearInterval(id);
  }, [paused, idleMs, sendHeartbeat]);

  // Other tabs: a sign-out there signs this tab out; confirmed activity there closes the warning here
  useEffect(() => {
    const onStorage = (event) => {
      if (event.key === STORAGE_KEYS.LOGOUT_KEY && event.newValue) {
        const message = parseLogoutMessage(event.newValue);
        signingOut.current = true;
        window.location.href = loginPathFor(message?.reason, message?.idleMinutes);
      } else if (event.key === STORAGE_KEYS.LAST_ACTIVITY_KEY) {
        tickRef.current();
      }
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const handleStay = async () => {
    setStaying(true);
    try {
      await authApi.recordActivity();
    } catch {
      // A 401 is handled by the API client
    } finally {
      setStaying(false);
      tickRef.current();
    }
  };

  if (paused || remainingMs === null) return null;

  return (
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="idle-warning-title"
      aria-describedby="idle-warning-text"
    >
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
        <div className="flex items-center gap-2 pb-3 border-b border-slate-100 dark:border-slate-800">
          <Clock className="w-5 h-5 text-amber-600" />
          <h3 id="idle-warning-title" className="text-sm font-bold text-slate-900 dark:text-slate-100">
            Are you still there?
          </h3>
        </div>
        <p id="idle-warning-text" className="text-xs text-slate-500 dark:text-slate-400">
          For your security you will be signed out after {idleMinutes} minute{idleMinutes === 1 ? '' : 's'} of inactivity.
          Anything you haven&apos;t saved will be lost.
        </p>
        <p className="text-center">
          <span className="block text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
            Signing out in
          </span>
          <span className="font-mono text-3xl font-bold text-amber-600 dark:text-amber-400" aria-live="polite">
            {formatCountdown(remainingMs)}
          </span>
        </p>
        <div className="flex flex-col-reverse sm:flex-row items-stretch sm:items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
          <button
            type="button"
            onClick={() => logout()}
            disabled={staying}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors flex items-center justify-center gap-1.5 disabled:opacity-50"
          >
            <LogOut className="w-3.5 h-3.5" />
            Sign out
          </button>
          <button
            type="button"
            onClick={handleStay}
            disabled={staying}
            autoFocus
            className="px-4 py-2 rounded-xl text-xs font-semibold bg-red-600 hover:bg-red-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center justify-center gap-1.5"
          >
            {staying && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            Stay signed in
          </button>
        </div>
      </div>
    </div>
  );
};

export default IdleSessionManager;
