import { useEffect, useState } from 'react';
import { activityApi } from '../api';

export const ATTENTION_UPDATED_EVENT = 'lifelink-attention-updated';

/**
 * Admin badge counts (new blood requests / transfers since the Activity log tab was last opened, and pending
 * registrations, appeals and complaints). Polled every 30 s as a background request (does not keep the session alive)
 * and refreshed at once when a page announces a change with ATTENTION_UPDATED_EVENT.
 */
export const useAdminAttention = (enabled) => {
  const [counts, setCounts] = useState(null);

  useEffect(() => {
    if (!enabled) return undefined;
    let active = true;
    const refresh = () => {
      activityApi
        .getAttentionCounts({ background: true })
        .then((next) => {
          if (active) setCounts(next);
        })
        .catch(() => {
          // Badges are optional: keep the last counts on a failed refresh
        });
    };
    const first = window.setTimeout(refresh, 0);
    const timer = window.setInterval(refresh, 30000);
    window.addEventListener(ATTENTION_UPDATED_EVENT, refresh);
    return () => {
      active = false;
      window.clearTimeout(first);
      window.clearInterval(timer);
      window.removeEventListener(ATTENTION_UPDATED_EVENT, refresh);
    };
  }, [enabled]);

  return counts;
};

/** Short badge text: 0 hides the badge, more than 99 shows "99+" */
export const badgeText = (n) => (!n ? null : n > 99 ? '99+' : String(n));
