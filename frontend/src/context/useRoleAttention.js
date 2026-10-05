import { useEffect, useState } from 'react';
import { attentionApi } from '../api';

export const ROLE_ATTENTION_UPDATED_EVENT = 'lifelink-role-attention-updated';

/** One bounded aggregate poll for HospitalStaff/Doctor workflow badges. */
export const useRoleAttention = (enabled) => {
  const [counts, setCounts] = useState(null);

  useEffect(() => {
    if (!enabled) return undefined;
    let active = true;
    const refresh = () => {
      attentionApi.getRoleAttention({ background: true })
        .then((next) => {
          if (active) setCounts(next);
        })
        .catch(() => {
          // Badges are supplemental; retain the last successful values.
        });
    };
    const first = window.setTimeout(refresh, 0);
    const timer = window.setInterval(refresh, 30000);
    window.addEventListener(ROLE_ATTENTION_UPDATED_EVENT, refresh);
    return () => {
      active = false;
      window.clearTimeout(first);
      window.clearInterval(timer);
      window.removeEventListener(ROLE_ATTENTION_UPDATED_EVENT, refresh);
    };
  }, [enabled]);

  return counts;
};
