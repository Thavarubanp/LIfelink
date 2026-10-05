import { useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowLeftRight, ClipboardList, History, Loader2, PauseCircle, PlayCircle } from 'lucide-react';
import { activityApi, transferApi } from '../../api';
import { DataTable, tableFilterClass } from '../../components/common/DataTable';
import { Badge, RequestStatusBadge, SuspendedBadge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { ATTENTION_UPDATED_EVENT } from '../../context/useAdminAttention';
import { formatDisplayDate } from '../../utils/dateUtils';

const TABS = [
  { key: 'blood-requests', label: 'Blood requests', icon: ClipboardList, countKey: 'newBloodRequests', seenKey: 'bloodRequestsSeenAt' },
  { key: 'transfers', label: 'Inter-hospital transfers', icon: ArrowLeftRight, countKey: 'newTransfers', seenKey: 'transfersSeenAt' }
];
const REQUEST_STATUSES = ['Pending', 'Verified', 'Approved', 'Rejected', 'Completed', 'Cancelled', 'Deleted'];
const TRANSFER_STATUSES = ['Pending', 'Approved', 'Completed', 'Rejected', 'Cancelled'];
// Only open items can be suspended (the backend enforces the same rule)
const SUSPENDABLE_REQUEST = ['Pending', 'Verified', 'Approved'];
const SUSPENDED_FILTER = '__suspended';

// Sri Lanka calendar day for display; yyyy-mm-dd remains internal for filters.
const fmt = (value) => formatDisplayDate(value);
const sriLankaDay = (value) => new Date(value).toLocaleDateString('en-CA', { timeZone: 'Asia/Colombo' }); // yyyy-mm-dd
const unwrap = (res) => (Array.isArray(res) ? res : Array.isArray(res?.data) ? res.data : []);

const NewBadge = () => <Badge variant="danger" size="sm">New</Badge>;

// "Last seen" is read before the tab is marked seen, so this visit still shows what was new; then the badge clears
const fetchTab = async (key) => {
  const counts = await activityApi.getAttentionCounts();
  const data = key === 'blood-requests' ? await activityApi.getAllBloodRequests() : await transferApi.getAllTransferRequests();
  await activityApi.markAreaSeen(key);
  window.dispatchEvent(new Event(ATTENTION_UPDATED_EVENT));
  return { counts, list: unwrap(data).sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt)) };
};

// Asks for the reason of a suspension (required, max 500 characters)
const SuspendDialog = ({ target, onCancel, onConfirm, busy }) => {
  const [reason, setReason] = useState('');
  const trimmed = reason.trim();
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4" role="dialog" aria-modal="true" aria-label="Suspend">
      <div className="w-full max-w-md rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-5 shadow-xl space-y-3">
        <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">Suspend {target.label}</h2>
        <p className="text-xs text-slate-600 dark:text-slate-300">
          {target.kind === 'request'
            ? 'Nobody can act on this request until you lift the suspension, except a donor withdrawing and the creator deleting it. The creator, the hospital, the assigned doctor and active donors are notified.'
            : 'This transfer cannot be accepted, rejected or withdrawn until you lift the suspension. Both hospitals are notified.'}
        </p>
        <label className="block text-[11px] font-semibold text-slate-600 dark:text-slate-300">
          Reason (shown to the people notified)
          <textarea
            value={reason}
            maxLength={500}
            rows={3}
            onChange={(e) => setReason(e.target.value)}
            className="mt-1 w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 px-2.5 py-1.5 text-xs text-slate-800 dark:text-slate-100 focus:outline-none focus:border-red-500"
          />
        </label>
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onCancel} disabled={busy}
            className="px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800">
            Cancel
          </button>
          <button type="button" onClick={() => onConfirm(trimmed)} disabled={busy || !trimmed}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50">
            {busy && <Loader2 className="w-3.5 h-3.5 animate-spin" />} Suspend
          </button>
        </div>
      </div>
    </div>
  );
};

/**
 * Admin "Activity log": every blood request (users', hospitals' and the Admin's, any status including deleted) and
 * every inter-hospital transfer. Items created since the admin last opened a tab show "New"; opening the tab marks it
 * seen, so its sidebar badge clears. Open items can be suspended and suspensions lifted (the admin never edits or deletes).
 */
export const AdminActivityPage = () => {
  const [tab, setTab] = useState(TABS[0].key);
  const [rows, setRows] = useState([]);
  const [seenAt, setSeenAt] = useState(null);
  const [newCounts, setNewCounts] = useState({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [filters, setFilters] = useState({ status: '', type: '', from: '', to: '' });
  const [suspendTarget, setSuspendTarget] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const { addToast } = useNotification();

  // Loads the open tab (loading state and filter reset are set by the tab handler, never synchronously in the effect)
  useEffect(() => {
    let active = true;
    fetchTab(tab)
      .then(({ counts, list }) => {
        if (!active) return;
        setSeenAt(counts?.[TABS.find((t) => t.key === tab).seenKey] || null);
        setNewCounts(counts || {});
        setRows(list);
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
  }, [tab]);

  const selectTab = (key) => {
    if (key === tab) return;
    setLoading(true);
    setError('');
    setFilters({ status: '', type: '', from: '', to: '' });
    setTab(key);
  };

  // Re-reads the open tab after a suspend or lift (the list itself is the source of truth)
  const reloadRows = async () => {
    const data = tab === 'blood-requests' ? await activityApi.getAllBloodRequests() : await transferApi.getAllTransferRequests();
    setRows(unwrap(data).sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt)));
  };

  const runAction = async (id, action, successTitle) => {
    setBusyId(id);
    try {
      await action();
      addToast({ title: successTitle, type: 'success' });
      setSuspendTarget(null);
      await reloadRows();
    } catch (err) {
      addToast({ title: 'Action failed', message: getApiErrorMessage(err), type: 'error' });
      await reloadRows().catch(() => {});
    } finally {
      setBusyId(null);
    }
  };

  const confirmSuspend = (reason) => {
    const t = suspendTarget;
    runAction(t.id, () => (t.kind === 'request' ? activityApi.suspendBloodRequest(t.id, reason) : activityApi.suspendTransfer(t.id, reason)),
      t.kind === 'request' ? 'Blood request suspended' : 'Transfer suspended');
  };

  const lift = (kind, id) =>
    runAction(id, () => (kind === 'request' ? activityApi.liftBloodRequest(id) : activityApi.liftTransfer(id)), 'Suspension lifted');

  const actionCell = (kind, id, row, canSuspend) =>
    row.isSuspended ? (
      <button type="button" disabled={busyId === id} onClick={() => lift(kind, id)}
        className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-emerald-300 dark:border-emerald-800 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-50 dark:hover:bg-emerald-950/40 disabled:opacity-50">
        {busyId === id ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <PlayCircle className="w-3.5 h-3.5" />} Lift
      </button>
    ) : canSuspend ? (
      <button type="button" disabled={busyId === id}
        onClick={() => setSuspendTarget({ kind, id, label: `${kind === 'request' ? 'request' : 'transfer'} #${String(id).substring(0, 8)}` })}
        className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-rose-300 dark:border-rose-800 text-rose-700 dark:text-rose-300 hover:bg-rose-50 dark:hover:bg-rose-950/40 disabled:opacity-50">
        <PauseCircle className="w-3.5 h-3.5" /> Suspend
      </button>
    ) : (
      <span className="text-[11px] text-slate-400">-</span>
    );

  const isNew = useCallback((row) => !seenAt || new Date(row.createdAt) > new Date(seenAt), [seenAt]);

  const filtered = useMemo(
    () =>
      rows
        .filter((r) => !filters.status || (filters.status === SUSPENDED_FILTER ? r.isSuspended : r.status === filters.status))
        .filter((r) => !filters.type || r.transferType === filters.type)
        .filter((r) => !filters.from || sriLankaDay(r.createdAt) >= filters.from)
        .filter((r) => !filters.to || sriLankaDay(r.createdAt) <= filters.to)
        .map((r) => ({ ...r, created: fmt(r.createdAt), isNew: isNew(r) })),
    [rows, filters, isNew]
  );

  const requestColumns = [
    {
      header: 'Request',
      accessor: 'bloodRequestId',
      cell: (r) => (
        <div className="space-y-0.5">
          <div className="flex items-center gap-1.5 flex-wrap">
            {r.isNew && <NewBadge />}
            <span className="font-mono text-[11px] text-slate-500">#{String(r.bloodRequestId).substring(0, 8)}</span>
            <Badge variant="blood" size="sm">{r.bloodGroup}</Badge>
            <span className="text-xs text-slate-700 dark:text-slate-300">{r.unitsRequired} unit(s) - {r.priority}</span>
          </div>
        </div>
      )
    },
    { header: 'Hospital', accessor: 'hospitalName' },
    { header: 'Created by', accessor: 'createdByName', cell: (r) => r.createdByName || '-' },
    {
      header: 'Status',
      accessor: 'status',
      cell: (r) => (
        <div className="flex items-center gap-1 flex-wrap">
          <RequestStatusBadge status={r.status} />
          {r.isSuspended && <SuspendedBadge reason={r.suspensionReason} />}
        </div>
      )
    },
    { header: 'Created', accessor: 'created' },
    { header: 'Action', accessor: 'priority', cell: (r) => actionCell('request', r.bloodRequestId, r, SUSPENDABLE_REQUEST.includes(r.status)) }
  ];

  const transferColumns = [
    {
      header: 'Transfer',
      accessor: 'transferRequestId',
      cell: (r) => (
        <div className="flex items-center gap-1.5 flex-wrap">
          {r.isNew && <NewBadge />}
          <span className="font-mono text-[11px] text-slate-500">#{String(r.transferRequestId).substring(0, 8)}</span>
          <Badge variant="blood" size="sm">{r.bloodGroup}</Badge>
          <span className="text-xs text-slate-700 dark:text-slate-300">{r.unitsRequested} unit(s) - {r.transferType}</span>
        </div>
      )
    },
    { header: 'From', accessor: 'senderHospitalName' },
    { header: 'To', accessor: 'receiverHospitalName' },
    {
      header: 'Status',
      accessor: 'status',
      cell: (r) => (
        <div className="flex items-center gap-1 flex-wrap">
          <Badge variant={r.status === 'Completed' ? 'success' : r.status === 'Pending' ? 'warning' : 'default'} size="sm">{r.status}</Badge>
          {r.isSuspended && <SuspendedBadge reason={r.suspensionReason} />}
        </div>
      )
    },
    { header: 'Created', accessor: 'created' },
    { header: 'Action', accessor: 'transferType', cell: (r) => actionCell('transfer', r.transferRequestId, r, r.status === 'Pending') }
  ];

  const isRequests = tab === 'blood-requests';
  const statuses = isRequests ? REQUEST_STATUSES : TRANSFER_STATUSES;
  const setFilter = (name, value) => setFilters((f) => ({ ...f, [name]: value }));
  const newHere = filtered.filter((r) => r.isNew).length;

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <History className="w-5 h-5 text-red-600" /> Activity Log
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
          Every blood request (from users, hospitals and the Admin, including deleted ones) and every inter-hospital transfer.
          Items created since you last opened a tab are marked <span className="font-semibold text-red-600 dark:text-red-400">New</span>.
          You can suspend open items and lift suspensions; you cannot edit or delete them. User and hospital activity logs are on their profile pages.
        </p>
      </div>

      <div className="flex flex-wrap gap-2" role="tablist">
        {TABS.map((t) => {
          const Icon = t.icon;
          const count = t.key === tab ? 0 : newCounts[t.countKey];
          return (
            <button
              key={t.key}
              type="button"
              role="tab"
              aria-selected={tab === t.key}
              onClick={() => selectTab(t.key)}
              className={`inline-flex items-center gap-2 px-3.5 py-2 rounded-xl text-xs font-semibold border transition-colors ${
                tab === t.key
                  ? 'bg-red-600 text-white border-red-600'
                  : 'bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800'
              }`}
            >
              <Icon className="w-4 h-4" /> {t.label}
              {count > 0 && <span className="rounded-full bg-red-600 text-white px-1.5 text-[10px] font-bold">{count > 99 ? '99+' : count}</span>}
            </button>
          );
        })}
      </div>

      {error && <p className="text-xs text-rose-600 dark:text-rose-400">{error}</p>}

      {loading ? (
        <div className="p-8 text-center text-xs text-slate-500"><Loader2 className="w-5 h-5 animate-spin mx-auto mb-2 text-red-500" />Loading...</div>
      ) : (
        <DataTable
          title={`${isRequests ? 'All blood requests' : 'All inter-hospital transfers'}${newHere ? ` - ${newHere} new` : ''}`}
          icon={isRequests ? ClipboardList : ArrowLeftRight}
          columns={isRequests ? requestColumns : transferColumns}
          data={filtered}
          searchPlaceholder={isRequests ? 'Search hospital, creator, blood group...' : 'Search hospitals, blood group...'}
          emptyMessage="Nothing matches these filters."
          filters={
            <>
              <select aria-label="Status" value={filters.status} onChange={(e) => setFilter('status', e.target.value)} className={tableFilterClass}>
                <option value="">All statuses</option>
                {statuses.map((s) => <option key={s} value={s}>{s}</option>)}
                <option value={SUSPENDED_FILTER}>Suspended by admin</option>
              </select>
              {!isRequests && (
                <select aria-label="Transfer type" value={filters.type} onChange={(e) => setFilter('type', e.target.value)} className={tableFilterClass}>
                  <option value="">Requests and offers</option>
                  <option value="Request">Requests</option>
                  <option value="Offer">Offers</option>
                </select>
              )}
              <input type="date" aria-label="Created from" title="Created from" value={filters.from} max={filters.to || undefined} onChange={(e) => setFilter('from', e.target.value)} className={tableFilterClass} />
              <input type="date" aria-label="Created to" title="Created to" value={filters.to} min={filters.from || undefined} onChange={(e) => setFilter('to', e.target.value)} className={tableFilterClass} />
            </>
          }
        />
      )}

      {suspendTarget && (
        <SuspendDialog target={suspendTarget} busy={busyId === suspendTarget.id} onCancel={() => setSuspendTarget(null)} onConfirm={confirmSuspend} />
      )}
    </div>
  );
};

export default AdminActivityPage;
