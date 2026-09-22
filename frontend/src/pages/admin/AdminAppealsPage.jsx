import React, { useState, useEffect, useCallback } from 'react';
import { adminApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import {
  ShieldAlert, ShieldX, CheckCircle2, XCircle, Clock,
  MessageSquare, Loader2, ChevronDown, ChevronUp, User, Building2
} from 'lucide-react';

const StatusBadge = ({ status }) => {
  const cfg = {
    PENDING: { label: 'Pending', cls: 'text-amber-400 bg-amber-950/60 border-amber-700', Icon: Clock },
    APPROVED: { label: 'Approved', cls: 'text-emerald-400 bg-emerald-950/60 border-emerald-700', Icon: CheckCircle2 },
    REJECTED: { label: 'Rejected', cls: 'text-rose-400 bg-rose-950/60 border-rose-700', Icon: XCircle },
  }[status] || { label: status, cls: 'text-slate-400 bg-slate-800 border-slate-700', Icon: Clock };
  const { Icon } = cfg;
  return (
    <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold border ${cfg.cls}`}>
      <Icon className="w-3 h-3" />{cfg.label}
    </span>
  );
};

const fmt = (iso) => iso ? new Date(iso).toLocaleString() : '—';

const ReviewModal = ({ appeal, actionType, onClose, onConfirm }) => {
  const [response, setResponse] = useState('');
  const [busy, setBusy] = useState(false);

  const labels = {
    approve: { title: 'Approve Appeal & Reinstate', btn: 'Approve & Reinstate', cls: 'bg-emerald-600 hover:bg-emerald-700' },
    reject: { title: 'Reject Appeal', btn: 'Reject Appeal', cls: 'bg-rose-600 hover:bg-rose-700' },
    block: { title: 'Permanently Block Account', btn: 'Permanently Block', cls: 'bg-red-900 hover:bg-red-800 border border-red-700' },
  };
  const cfg = labels[actionType];

  const handleSubmit = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      await onConfirm(appeal.appealId, { adminResponse: response.trim() });
      onClose();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4">
      <div className="bg-slate-900 border border-slate-700 rounded-2xl p-6 w-full max-w-md shadow-2xl">
        <h3 className="text-sm font-bold text-slate-100 mb-1">{cfg.title}</h3>
        <p className="text-xs text-slate-400 mb-4">
          Appeal by: <span className="text-slate-200 font-medium">{appeal.userEmail || appeal.hospitalName || 'Unknown'}</span>
        </p>
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label className="block text-xs font-semibold text-slate-300 mb-1">
              Admin Response <span className="text-slate-500">(required)</span>
            </label>
            <textarea
              required
              rows={4}
              minLength={5}
              value={response}
              onChange={e => setResponse(e.target.value)}
              placeholder="Provide your official response to this appeal..."
              className="w-full px-3 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-xs text-slate-100 placeholder-slate-500 focus:outline-none focus:border-purple-500 resize-none"
            />
          </div>
          <div className="flex gap-2 justify-end pt-1">
            <button type="button" onClick={onClose} className="px-4 py-2 text-xs text-slate-400 hover:text-white border border-slate-700 rounded-lg transition-colors">
              Cancel
            </button>
            <button
              type="submit"
              disabled={busy || response.trim().length < 5}
              className={`px-4 py-2 text-xs text-white font-semibold rounded-lg disabled:opacity-50 transition-all flex items-center gap-1.5 ${cfg.cls}`}
            >
              {busy && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
              {cfg.btn}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export const AdminAppealsPage = () => {
  const [appeals, setAppeals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('PENDING');
  const [expandedId, setExpandedId] = useState(null);
  const [modal, setModal] = useState(null); // { appeal, actionType }
  const { addToast } = useNotification();

  const fetchAppeals = useCallback(async () => {
    setLoading(true);
    try {
      const res = await adminApi.getAppeals(filter === 'ALL' ? null : filter);
      setAppeals(res.data || []);
    } catch (err) {
      addToast({ title: 'Error', message: 'Failed to load appeals.', type: 'error' });
    } finally {
      setLoading(false);
    }
  }, [filter, addToast]);

  useEffect(() => { fetchAppeals(); }, [fetchAppeals]);

  const handleAction = async (appealId, dto) => {
    try {
      if (modal.actionType === 'approve') {
        await adminApi.approveAppeal(appealId, dto);
        addToast({ title: 'Appeal Approved', message: 'Account has been reinstated.', type: 'success' });
      } else if (modal.actionType === 'reject') {
        await adminApi.rejectAppeal(appealId, dto);
        addToast({ title: 'Appeal Rejected', message: 'The appellant can submit a new appeal.', type: 'info' });
      } else if (modal.actionType === 'block') {
        await adminApi.permanentlyBlockAppeal(appealId, dto);
        addToast({ title: 'Account Permanently Blocked', message: 'No further appeals can be submitted.', type: 'warning' });
      }
      await fetchAppeals();
    } catch (err) {
      addToast({
        title: 'Action Failed',
        message: err.response?.data?.message || err.message,
        type: 'error'
      });
      throw err; // Let modal handle it
    }
  };

  const FILTERS = ['PENDING', 'APPROVED', 'REJECTED', 'ALL'];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Suspension Appeals</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
          Review, approve, reject, or permanently block suspended accounts.
        </p>
      </div>

      {/* Filter Tabs */}
      <div className="flex gap-1.5">
        {FILTERS.map(f => (
          <button
            key={f}
            onClick={() => setFilter(f)}
            className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors ${
              filter === f
                ? 'bg-purple-600 text-white'
                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 border border-slate-200 dark:border-slate-700 hover:border-purple-400'
            }`}
          >
            {f}
          </button>
        ))}
      </div>

      {/* List */}
      {loading ? (
        <div className="flex justify-center py-16">
          <Loader2 className="w-7 h-7 animate-spin text-purple-500" />
        </div>
      ) : appeals.length === 0 ? (
        <div className="text-center py-16 text-slate-400 text-sm">
          No {filter !== 'ALL' ? filter.toLowerCase() : ''} appeals found.
        </div>
      ) : (
        <div className="space-y-3">
          {appeals.map(appeal => {
            const isExpanded = expandedId === appeal.appealId;
            const isPending = appeal.status === 'PENDING';

            return (
              <div key={appeal.appealId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm">
                {/* Card Header */}
                <div
                  className="p-5 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors"
                  onClick={() => setExpandedId(isExpanded ? null : appeal.appealId)}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-start gap-3 min-w-0">
                      {/* Entity icon */}
                      <div className={`p-2 rounded-lg shrink-0 ${appeal.hospitalId ? 'bg-cyan-100 dark:bg-cyan-900/30 text-cyan-600' : 'bg-blue-100 dark:bg-blue-900/30 text-blue-600'}`}>
                        {appeal.hospitalId ? <Building2 className="w-4 h-4" /> : <User className="w-4 h-4" />}
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-semibold text-slate-900 dark:text-slate-100 truncate">
                          {appeal.userEmail || appeal.hospitalName || 'Unknown'}
                        </p>
                        <p className="text-[11px] text-slate-500 mt-0.5">
                          {appeal.hospitalId ? 'Hospital Appeal' : 'User Appeal'} · Submitted {fmt(appeal.submittedAt)}
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center gap-2 shrink-0">
                      <StatusBadge status={appeal.status} />
                      {isExpanded ? <ChevronUp className="w-4 h-4 text-slate-400" /> : <ChevronDown className="w-4 h-4 text-slate-400" />}
                    </div>
                  </div>

                  {/* Preview of appeal reason */}
                  <p className="mt-3 text-xs text-slate-500 dark:text-slate-400 line-clamp-2 pl-11">
                    {appeal.reason}
                  </p>
                </div>

                {/* Expanded Details */}
                {isExpanded && (
                  <div className="border-t border-slate-200 dark:border-slate-800 p-5 space-y-4">
                    {/* Full appeal message */}
                    <div>
                      <p className="text-[11px] font-semibold text-slate-500 uppercase tracking-wider mb-2 flex items-center gap-1.5">
                        <MessageSquare className="w-3.5 h-3.5" /> Appeal Message
                      </p>
                      <div className="bg-slate-50 dark:bg-slate-800 rounded-xl p-3.5 text-xs text-slate-700 dark:text-slate-200 leading-relaxed">
                        {appeal.reason}
                      </div>
                    </div>

                    {/* Admin response (if reviewed) */}
                    {appeal.adminResponse && (
                      <div>
                        <p className="text-[11px] font-semibold text-slate-500 uppercase tracking-wider mb-2">
                          Admin Response · {fmt(appeal.reviewedAt)}
                        </p>
                        <div className={`rounded-xl p-3.5 text-xs leading-relaxed ${
                          appeal.status === 'APPROVED'
                            ? 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800'
                            : 'bg-rose-50 dark:bg-rose-950/40 text-rose-700 dark:text-rose-300 border border-rose-200 dark:border-rose-800'
                        }`}>
                          {appeal.adminResponse}
                        </div>
                      </div>
                    )}

                    {/* Action Buttons */}
                    {isPending && (
                      <div className="flex flex-wrap gap-2 pt-1">
                        <button
                          onClick={() => setModal({ appeal, actionType: 'approve' })}
                          className="flex items-center gap-1.5 px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold rounded-lg text-xs transition-colors shadow-sm"
                        >
                          <CheckCircle2 className="w-3.5 h-3.5" /> Approve & Reinstate
                        </button>
                        <button
                          onClick={() => setModal({ appeal, actionType: 'reject' })}
                          className="flex items-center gap-1.5 px-4 py-2 bg-amber-600 hover:bg-amber-700 text-white font-semibold rounded-lg text-xs transition-colors shadow-sm"
                        >
                          <XCircle className="w-3.5 h-3.5" /> Reject Appeal
                        </button>
                        <button
                          onClick={() => setModal({ appeal, actionType: 'block' })}
                          className="flex items-center gap-1.5 px-4 py-2 bg-rose-900 hover:bg-rose-800 border border-rose-700 text-white font-semibold rounded-lg text-xs transition-colors shadow-sm"
                        >
                          <ShieldX className="w-3.5 h-3.5" /> Permanently Block
                        </button>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Review Modal */}
      {modal && (
        <ReviewModal
          appeal={modal.appeal}
          actionType={modal.actionType}
          onClose={() => setModal(null)}
          onConfirm={handleAction}
        />
      )}
    </div>
  );
};

export default AdminAppealsPage;
