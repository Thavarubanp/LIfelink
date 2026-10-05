import { useEffect, useState } from 'react';
import { ChevronLeft, ChevronRight, History, Loader2, RotateCcw } from 'lucide-react';
import { Badge } from '../common/Badge';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { formatDisplayDate } from '../../utils/dateUtils';

const PAGE_SIZE = 10;

// Readable names for the action types (ActivityLog.EntityType)
const TYPE_LABELS = {
  Account: 'Account',
  BloodRequest: 'Blood requests',
  Donation: 'Donations',
  Screening: 'Screening',
  Inventory: 'Inventory',
  Transfer: 'Transfers',
  Emergency: 'Emergencies',
  Doctor: 'Doctors',
  Complaint: 'Complaints',
  Appeal: 'Appeals',
  Hospital: 'Hospital',
  Governance: 'Governance'
};

const ROLE_VARIANT = { Admin: 'primary', HospitalStaff: 'info', Doctor: 'success', System: 'default', User: 'default' };
const ROLE_LABEL = { Admin: 'Admin', HospitalStaff: 'Hospital', Doctor: 'Doctor', System: 'System', User: 'User' };

const ENTITY_LABELS = {
  Account: 'Account',
  BloodRequest: 'Blood request',
  Donation: 'Donation',
  Screening: 'Screening',
  Inventory: 'Inventory',
  Transfer: 'Transfer',
  Emergency: 'Emergency',
  Doctor: 'Doctor',
  Complaint: 'Complaint',
  Appeal: 'Appeal',
  Hospital: 'Hospital',
  Governance: 'Governance'
};

const friendlyAction = (action) => {
  const name = String(action || '').split('.').pop();
  return name ? name.replace(/([a-z0-9])([A-Z])/g, '$1 $2') : 'Activity';
};

const shortReference = (label, value) => value ? `${label} #${String(value).substring(0, 8)}` : null;

const StructuredContext = ({ entry }) => {
  const primary = entry.recordReference || (entry.entityId
    ? `${ENTITY_LABELS[entry.entityType] || entry.entityType || 'Record'} #${String(entry.entityId).substring(0, 8)}`
    : null);
  const related = [];
  const primaryId = entry.entityId && String(entry.entityId).toLowerCase();
  const addRelated = (label, value) => {
    if (value && String(value).toLowerCase() !== primaryId) related.push(shortReference(label, value));
  };
  addRelated('Blood request', entry.bloodRequestId);
  addRelated('Acceptance', entry.acceptanceId);
  addRelated('Screening', entry.screeningVerificationId);

  return (
    <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-slate-500 dark:text-slate-400">
      <span className="font-semibold text-slate-700 dark:text-slate-200">{friendlyAction(entry.action)}</span>
      {primary && <span title={entry.entityId || undefined} className="font-mono">{primary}</span>}
      {related.map((reference) => <span key={reference} className="font-mono">{reference}</span>)}
      {entry.bloodGroup && <Badge variant="blood" size="sm">{entry.bloodGroup}</Badge>}
      {entry.hospitalName && <span>Hospital: {entry.hospitalName}</span>}
      {(entry.transferSourceHospitalName || entry.transferDestinationHospitalName) && (
        <span>
          {entry.transferSourceHospitalName || 'Unknown hospital'} → {entry.transferDestinationHospitalName || 'Unknown hospital'}
        </span>
      )}
      {entry.packetTrackingNumber && <span className="font-mono">Packet {entry.packetTrackingNumber}</span>}
    </div>
  );
};

/**
 * Paged activity log with a filter by action type and date range (Sri Lanka dates).
 * `load(params)` fetches one page: { page, pageSize, type, from, to } -> { items, total, page, pageSize, recordedFrom, types }.
 */
export const ActivityLogList = ({ load, title = 'Activity log', description }) => {
  const [filters, setFilters] = useState({ type: '', from: '', to: '' });
  const [page, setPage] = useState(1);
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Loads the page whenever the page or a filter changes (the handlers below set "loading")
  useEffect(() => {
    let active = true;
    const params = { page, pageSize: PAGE_SIZE };
    if (filters.type) params.type = filters.type;
    if (filters.from) params.from = filters.from;
    if (filters.to) params.to = filters.to;
    load(params)
      .then((next) => {
        if (!active) return;
        setData(next);
        setError('');
        setLoading(false);
      })
      .catch((err) => {
        if (!active) return;
        setError(getApiErrorMessage(err));
        setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [load, page, filters]);

  const goToPage = (next) => {
    setLoading(true);
    setPage(next);
  };
  const setFilter = (name, value) => {
    setLoading(true);
    setFilters((f) => ({ ...f, [name]: value }));
    setPage(1);
  };
  const reset = () => {
    setLoading(true);
    setFilters({ type: '', from: '', to: '' });
    setPage(1);
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const types = data?.types?.length ? data.types : Object.keys(TYPE_LABELS);
  const inputClass =
    'w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 px-2.5 py-1.5 text-xs text-slate-800 dark:text-slate-100 focus:outline-none focus:border-red-500';

  return (
    <section className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-sm space-y-4">
      <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-2">
        <div>
          <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
            <History className="w-4 h-4 text-red-600" /> {title}
          </h2>
          {description && <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{description}</p>}
        </div>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 sm:text-right">
          Activity is recorded from {formatDisplayDate(data?.recordedFrom || '2026-10-03T00:00:00Z')}.
        </p>
      </div>

      {/* Filters */}
      <div className="grid grid-cols-1 sm:grid-cols-[1.4fr_1fr_1fr_auto] gap-2 items-end">
        <label className="text-[11px] font-semibold text-slate-600 dark:text-slate-300">
          Action type
          <select aria-label="Action type" value={filters.type} onChange={(e) => setFilter('type', e.target.value)} className={`${inputClass} mt-1`}>
            <option value="">All actions</option>
            {types.map((t) => (
              <option key={t} value={t}>{TYPE_LABELS[t] || t}</option>
            ))}
          </select>
        </label>
        <label className="text-[11px] font-semibold text-slate-600 dark:text-slate-300">
          From
          <input type="date" aria-label="From date" value={filters.from} max={filters.to || undefined} onChange={(e) => setFilter('from', e.target.value)} className={`${inputClass} mt-1`} />
        </label>
        <label className="text-[11px] font-semibold text-slate-600 dark:text-slate-300">
          To
          <input type="date" aria-label="To date" value={filters.to} min={filters.from || undefined} onChange={(e) => setFilter('to', e.target.value)} className={`${inputClass} mt-1`} />
        </label>
        <button
          type="button"
          onClick={reset}
          disabled={!filters.type && !filters.from && !filters.to}
          className="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40"
        >
          <RotateCcw className="w-3.5 h-3.5" /> Reset
        </button>
      </div>

      {/* Entries */}
      {loading && !data ? (
        <div className="py-8 text-center text-xs text-slate-500"><Loader2 className="w-5 h-5 animate-spin mx-auto mb-2 text-red-500" />Loading activity...</div>
      ) : error ? (
        <p className="py-6 text-center text-xs text-rose-600 dark:text-rose-400">{error}</p>
      ) : !data?.items?.length ? (
        <p className="py-6 text-center text-xs text-slate-500 dark:text-slate-400">No activity matches these filters.</p>
      ) : (
        <ul className={`divide-y divide-slate-100 dark:divide-slate-800 ${loading ? 'opacity-60' : ''}`}>
          {data.items.map((entry) => (
            <li key={entry.id} className="py-2.5 flex flex-col sm:flex-row sm:items-start gap-1 sm:gap-3">
              <time className="text-[11px] font-mono text-slate-500 dark:text-slate-400 sm:w-40 shrink-0" dateTime={entry.occurredAt}>
                {formatDisplayDate(entry.occurredAt)}
              </time>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-1.5 flex-wrap">
                  <span className="text-xs font-semibold text-slate-900 dark:text-slate-100">{entry.actorName}</span>
                  <Badge variant={ROLE_VARIANT[entry.actorRole] || 'default'} size="sm">{ROLE_LABEL[entry.actorRole] || entry.actorRole}</Badge>
                  <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">{TYPE_LABELS[entry.entityType] || entry.entityType}</span>
                </div>
                <StructuredContext entry={entry} />
                <p className="text-xs text-slate-700 dark:text-slate-300 mt-0.5 break-words">{entry.summary}</p>
              </div>
            </li>
          ))}
        </ul>
      )}

      {/* Pagination */}
      {data && data.total > 0 && (
        // Right padding on phones keeps the arrows clear of the floating assistant button
        <div className="flex items-center justify-between gap-2 pt-1 pr-16 sm:pr-0 text-[11px] text-slate-500 dark:text-slate-400">
          <span>{data.total} entr{data.total === 1 ? 'y' : 'ies'}</span>
          <div className="flex items-center gap-2">
            <button type="button" aria-label="Previous page" disabled={page <= 1 || loading} onClick={() => goToPage(page - 1)}
              className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40">
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <span className="font-semibold text-slate-700 dark:text-slate-200">Page {data.page} of {totalPages}</span>
            <button type="button" aria-label="Next page" disabled={page >= totalPages || loading} onClick={() => goToPage(page + 1)}
              className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40">
              <ChevronRight className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      )}
    </section>
  );
};

export default ActivityLogList;
