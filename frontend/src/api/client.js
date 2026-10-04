import axios from 'axios';
import {
  ACTIVITY_HEADER,
  SESSION_ENDED_HEADER,
  clearSession,
  getIdleMinutesSetting,
  loginPathFor,
  markActivityConfirmed,
  releaseActivityMark,
  takeActivityMark,
} from '../session/sessionActivity';

// Base Axios instance matching LifeLink ASP.NET Core backend API
const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor to automatically attach JWT Bearer token.
// Request options understood here:
//   background: true      -> automatic refresh/polling; never counts as user activity for the idle timeout
//   reportActivity: true  -> always report activity (heartbeat, "Stay signed in")
//   idempotencyKey: '...' -> one key per form submission; a double submit with the same key creates nothing twice
client.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('lifelink_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;

      // User-driven requests keep the session alive (reported at most every few seconds)
      if (!config.background && (config.reportActivity || takeActivityMark())) {
        config.headers[ACTIVITY_HEADER] = '1';
        config.activitySentAt = Date.now();
      }
    }
    if (config.idempotencyKey) {
      config.headers['Idempotency-Key'] = config.idempotencyKey;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor for centralized error handling and 401 session expiry
client.interceptors.response.use(
  (response) => {
    if (response.config?.activitySentAt) markActivityConfirmed(response.config.activitySentAt);
    return response;
  },
  (error) => {
    const status = error.response ? error.response.status : null;
    const sentAt = error.config?.activitySentAt;
    if (sentAt) {
      // Any answer other than 401 means the backend accepted the session (and recorded the activity)
      if (status && status !== 401) markActivityConfirmed(sentAt);
      else releaseActivityMark();
    }

    if (status === 401 && error.config?.headers?.Authorization) {
      // Idle timeout (X-Session-Ended: idle) or an expired / ended session: clear it in every tab
      const reason = error.response.headers?.[SESSION_ENDED_HEADER] === 'idle' ? 'idle' : 'expired';
      const idleMinutes = getIdleMinutesSetting();
      clearSession(reason, idleMinutes);

      // Redirect to login if not already on auth page
      if (!window.location.pathname.startsWith('/login') && !window.location.pathname.startsWith('/register')) {
        window.location.href = loginPathFor(reason, idleMinutes);
      }
    }

    return Promise.reject(error);
  }
);

export default client;
