import { useCallback, useEffect, useState } from 'react';
import { Activity, Clock, Loader2, Play } from 'lucide-react';
import { inventoryApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { formatDisplayDate } from '../../utils/dateUtils';

const POLL_MS = 15000;
const unwrap = (res) => (res && res.data !== undefined && res.success !== undefined ? res.data : res?.data ?? res);

const fmtSriLanka = (value) => formatDisplayDate(value);

const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;

const summaryOf = (run) => {
  if (run.status === 'Running') return 'running…';
  if (run.status === 'Failed') return 'failed';
  if (run.status === 'Interrupted') return 'interrupted (it did not finish)';
  const text = `${run.lowStockAlerts} low-stock, ${plural(run.expiringAlerts, 'expiring-soon alert')} sent`;
  const skipped = run.skippedDuplicates > 0 ? `, ${run.skippedDuplicates} repeated alert(s) skipped` : '';
  return text + skipped + (run.status === 'CompletedRuleBased' ? ' (rule-based: the AI agents were unavailable)' : '');
};

const fmtCountdown = (ms) => {
  const total = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
};

/**
 * Inventory analysis panel (hospital staff, Phase 4 / 5.5): the last run (Sri Lanka time, scheduled or manual by which
 * hospital, and its result), the next scheduled run, and a "Run analysis" button with a running state and the 2-minute
 * cooldown countdown. Every hospital sees the same information. Refreshes every 15 s in the background (never extends
 * the session) and right after a run. Countdowns use the server's clock.
 */
export const InventoryAnalysisStatus = ({ onRunComplete }) => {
  const { addToast } = useNotification();
  const [status, setStatus] = useState(null);
  const [offset, setOffset] = useState(0); // server clock minus this browser's clock
  const [now, setNow] = useState(() => Date.now());
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback((background) =>
    inventoryApi.getAnalysisStatus({ background })
      .then((res) => {
        const data = unwrap(res);
        setStatus(data);
        setOffset(new Date(data.serverNow).getTime() - Date.now());
        setError('');
      })
      .catch((err) => setError(getApiErrorMessage(err))), []);

  useEffect(() => {
    load(false);
    const poll = setInterval(() => load(true), POLL_MS);
    const tick = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      clearInterval(poll);
      clearInterval(tick);
    };
  }, [load]);

  const serverNow = now + offset;
  const cooldownLeft = status?.state === 'Cooldown' && status.cooldownEndsAt ? new Date(status.cooldownEndsAt).getTime() - serverNow : 0;
  const isRunning = running || status?.state === 'Running';
  const inCooldown = !isRunning && cooldownLeft > 0;

  const run = async () => {
    setRunning(true);
    try {
      const result = unwrap(await inventoryApi.runAnalysis());
      addToast({
        title: 'Analysis complete',
        message: `Analysis complete: ${result.lowStockAlerts} low-stock, ${plural(result.expiringAlerts, 'expiring-soon alert')} sent.`,
        type: 'success'
      });
      onRunComplete?.();
    } catch (err) {
      addToast({ title: 'Analysis not run', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setRunning(false);
      load(false);
    }
  };

  const last = status?.lastRun;
  let next = null;
  if (status) {
    if (!status.scheduleEnabled) next = 'Scheduled runs are off.';
    else if (status.nextScheduledAt) {
      const minutes = Math.ceil((new Date(status.nextScheduledAt).getTime() - serverNow) / 60000);
      next = minutes <= 0 ? 'Next scheduled run: due now.' : `Next scheduled run in ${minutes} min.`;
    }
  }

  return (
    <section className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3">
      <div className="space-y-1 min-w-0">
        <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <Activity className="w-4 h-4 text-red-600" /> Inventory analysis
        </h3>
        <p className="text-[11px] text-slate-500 dark:text-slate-400">
          Checks every hospital's stock: a hospital below its threshold is alerted with the hospitals holding that exact blood group, and those hospitals are asked to help; packets expiring soon are flagged.
        </p>
        {error && !status ? (
          <p className="text-xs text-rose-600 dark:text-rose-400">{error}</p>
        ) : !status ? (
          <p className="text-xs text-slate-500 flex items-center gap-1.5"><Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading…</p>
        ) : (
          <>
            <p className="text-xs text-slate-700 dark:text-slate-200" data-testid="last-run">
              {isRunning ? (
                <span className="inline-flex items-center gap-1.5 font-semibold text-red-600 dark:text-red-400"><Loader2 className="w-3.5 h-3.5 animate-spin" /> Analysis running…</span>
              ) : last ? (
                <>
                  <span className="font-semibold">Last analysis:</span> {fmtSriLanka(last.startedAt)} (
                  {last.trigger === 'Manual' ? `manual by ${last.hospitalName || 'a hospital'}` : 'scheduled'}) – {summaryOf(last)}
                </>
              ) : (
                'No analysis has run yet.'
              )}
            </p>
            {next && <p className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1"><Clock className="w-3 h-3" /> {next}</p>}
          </>
        )}
      </div>
      <button
        type="button"
        onClick={run}
        disabled={!status || isRunning || inCooldown}
        className="shrink-0 inline-flex items-center justify-center gap-1.5 px-4 py-2 rounded-xl text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed min-w-44"
      >
        {isRunning ? (
          <><Loader2 className="w-4 h-4 animate-spin" /> Analysis running…</>
        ) : inCooldown ? (
          <>Available again in {fmtCountdown(cooldownLeft)}</>
        ) : (
          <><Play className="w-4 h-4" /> Run analysis now</>
        )}
      </button>
    </section>
  );
};

export default InventoryAnalysisStatus;
