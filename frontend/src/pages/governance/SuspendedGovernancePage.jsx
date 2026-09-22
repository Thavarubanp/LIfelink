import React, { useState, useEffect, useCallback } from 'react';
import { governanceApi, appealApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import {
  ShieldAlert, ShieldX, CheckCircle2, XCircle, Clock,
  Send, Loader2, LogOut, MessageSquare, RefreshCw
} from 'lucide-react';

const statusConfig = {
  PENDING: { label: 'Under Review', color: 'text-amber-400 bg-amber-950/60 border-amber-700', icon: Clock },
  APPROVED: { label: 'Approved', color: 'text-emerald-400 bg-emerald-950/60 border-emerald-700', icon: CheckCircle2 },
  REJECTED: { label: 'Rejected', color: 'text-rose-400 bg-rose-950/60 border-rose-700', icon: XCircle },
};

const StatusBadge = ({ status }) => {
  const cfg = statusConfig[status] || statusConfig.PENDING;
  const Icon = cfg.icon;
  return (
    <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold border ${cfg.color}`}>
      <Icon className="w-3 h-3" /> {cfg.label}
    </span>
  );
};

const fmt = (iso) => iso ? new Date(iso).toLocaleString() : '—';

export const SuspendedGovernancePage = () => {
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [appealReason, setAppealReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const { logout } = useAuth();
  const { addToast } = useNotification();

  const fetchStatus = useCallback(async () => {
    try {
      const res = await governanceApi.getStatus();
      setStatus(res.data);
    } catch (err) {
      console.error('Failed to fetch governance status:', err);
      addToast({ title: 'Error', message: 'Could not load suspension details.', type: 'error' });
    } finally {
      setLoading(false);
    }
  }, [addToast]);

  useEffect(() => { fetchStatus(); }, [fetchStatus]);

  const hasPendingAppeal = status?.allAppeals?.some(a => a.status === 'PENDING');
  const canSubmit = status?.isSuspended && !status?.isPermanentlyBlocked && !hasPendingAppeal;

  const handleSubmitAppeal = async (e) => {
    e.preventDefault();
    if (!appealReason.trim() || appealReason.trim().length < 10) return;
    setSubmitting(true);
    try {
      await appealApi.submitAppeal({ reason: appealReason.trim() });
      setAppealReason('');
      addToast({ title: 'Appeal Submitted', message: 'Your appeal has been sent to the admin.', type: 'success' });
      await fetchStatus();
    } catch (err) {
      addToast({
        title: 'Submission Failed',
        message: err.response?.data?.message || err.message || 'Could not submit appeal.',
        type: 'error'
      });
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-slate-950 flex items-center justify-center">
        <Loader2 className="w-8 h-8 animate-spin text-red-500" />
      </div>
    );
  }

  const appeals = status?.allAppeals || [];

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 py-10 px-4">
      <div className="max-w-2xl mx-auto space-y-6">

        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-2xl bg-gradient-to-tr from-red-600 to-red-500 flex items-center justify-center text-xl shadow-lg shadow-red-600/30">
              💉
            </div>
            <h1 className="text-lg font-bold text-white">Life<span className="text-red-500">Link</span></h1>
          </div>
          <button
            onClick={logout}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500 rounded-lg transition-colors"
          >
            <LogOut className="w-3.5 h-3.5" /> Sign Out
          </button>
        </div>

        {/* Suspension Banner */}
        {status?.isPermanentlyBlocked ? (
          <div className="bg-rose-950/50 border-2 border-rose-700/60 rounded-2xl p-6 shadow-xl">
            <div className="flex items-start gap-4">
              <div className="p-3 bg-rose-700/20 text-rose-400 rounded-xl shrink-0">
                <ShieldX className="w-8 h-8" />
              </div>
              <div>
                <h2 className="text-base font-bold text-rose-300">Account Permanently Blocked</h2>
                <p className="text-xs text-rose-200/80 mt-1 leading-relaxed">
                  Your account has been permanently blocked by the platform administrator. All appeal privileges have been revoked. Please contact support directly if you believe this is an error.
                </p>
              </div>
            </div>
          </div>
        ) : (
          <div className="bg-amber-950/40 border-2 border-amber-600/50 rounded-2xl p-6 shadow-xl">
            <div className="flex items-start gap-4">
              <div className="p-3 bg-amber-600/20 text-amber-500 rounded-xl shrink-0">
                <ShieldAlert className="w-8 h-8" />
              </div>
              <div className="flex-1">
                <h2 className="text-base font-bold text-amber-400">Account Suspended</h2>
                <p className="text-xs text-amber-200/80 mt-1 leading-relaxed">
                  {status?.suspensionReason || 'Your account has been suspended for administrative review.'}
                </p>
                {status?.suspendedUntil && (
                  <p className="text-[11px] text-amber-400 font-mono mt-2">
                    Suspended until: {fmt(status.suspendedUntil)}
                  </p>
                )}
                <div className="mt-3 flex items-center gap-2 text-xs text-amber-300/70">
                  <span>Suspended entity:</span>
                  <span className="font-semibold text-amber-300">{status?.suspendedEntity}</span>
                </div>
              </div>
              <button
                onClick={fetchStatus}
                className="p-2 text-amber-500 hover:text-amber-300 transition-colors"
                title="Refresh status"
              >
                <RefreshCw className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}

        {/* Appeal Conversation History */}
        {appeals.length > 0 && (
          <div className="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden">
            <div className="px-5 py-4 border-b border-slate-800 flex items-center gap-2">
              <MessageSquare className="w-4 h-4 text-purple-400" />
              <h3 className="text-sm font-bold text-slate-100">Appeal History</h3>
              <span className="ml-auto text-[11px] text-slate-500">{appeals.length} appeal{appeals.length !== 1 ? 's' : ''}</span>
            </div>
            <div className="divide-y divide-slate-800/60">
              {appeals.map((appeal, idx) => (
                <div key={appeal.appealId} className="p-5 space-y-3">
                  {/* Your message */}
                  <div className="flex items-start gap-3">
                    <div className="w-7 h-7 rounded-full bg-blue-700/30 border border-blue-700/50 flex items-center justify-center text-[10px] font-bold text-blue-300 shrink-0 mt-0.5">
                      Y
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 mb-1">
                        <span className="text-[11px] font-semibold text-slate-300">You</span>
                        <span className="text-[10px] text-slate-500">{fmt(appeal.submittedAt)}</span>
                        <StatusBadge status={appeal.status} />
                      </div>
                      <p className="text-xs text-slate-200 leading-relaxed bg-slate-800/50 rounded-xl px-3.5 py-2.5">
                        {appeal.reason}
                      </p>
                    </div>
                  </div>

                  {/* Admin reply */}
                  {appeal.adminResponse && (
                    <div className="flex items-start gap-3 pl-4">
                      <div className="w-7 h-7 rounded-full bg-purple-700/30 border border-purple-700/50 flex items-center justify-center text-[10px] font-bold text-purple-300 shrink-0 mt-0.5">
                        A
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="text-[11px] font-semibold text-purple-300">Admin</span>
                          <span className="text-[10px] text-slate-500">{fmt(appeal.reviewedAt)}</span>
                        </div>
                        <p className={`text-xs leading-relaxed rounded-xl px-3.5 py-2.5 ${
                          appeal.status === 'APPROVED'
                            ? 'bg-emerald-950/60 text-emerald-200 border border-emerald-800'
                            : 'bg-rose-950/60 text-rose-200 border border-rose-800'
                        }`}>
                          {appeal.adminResponse}
                        </p>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* New Appeal Form */}
        {!status?.isPermanentlyBlocked && status?.isSuspended && (
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6">
            <h3 className="text-sm font-bold text-slate-100 mb-1">Submit New Appeal</h3>
            <p className="text-xs text-slate-400 mb-4">
              {hasPendingAppeal
                ? 'You have a pending appeal under review. Please wait for the admin to respond before submitting another.'
                : 'Explain your situation and request reinstatement. Provide as much context as possible.'}
            </p>

            {hasPendingAppeal ? (
              <div className="p-3.5 bg-amber-950/50 border border-amber-800 rounded-xl text-xs text-amber-300 flex items-center gap-2">
                <Clock className="w-4 h-4 shrink-0" />
                Your previous appeal is awaiting admin review.
              </div>
            ) : (
              <form onSubmit={handleSubmitAppeal} className="space-y-3">
                <textarea
                  required
                  rows={4}
                  minLength={10}
                  maxLength={2000}
                  value={appealReason}
                  onChange={(e) => setAppealReason(e.target.value)}
                  placeholder="Explain why you believe this suspension should be lifted. Include any relevant context or evidence..."
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-xs text-slate-100 placeholder-slate-500 focus:outline-none focus:border-purple-500 resize-none transition-colors"
                />
                <div className="flex items-center justify-between">
                  <span className="text-[10px] text-slate-500">{appealReason.length}/2000 chars (min 10)</span>
                  <button
                    type="submit"
                    disabled={submitting || appealReason.trim().length < 10}
                    className="flex items-center gap-2 px-4 py-2 bg-purple-600 hover:bg-purple-700 disabled:opacity-50 text-white font-semibold rounded-xl text-xs shadow-lg transition-all"
                  >
                    {submitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                    Submit Appeal
                  </button>
                </div>
              </form>
            )}
          </div>
        )}

        {/* Empty state for no appeals yet */}
        {appeals.length === 0 && !status?.isPermanentlyBlocked && (
          <p className="text-center text-xs text-slate-500 pb-2">No appeals submitted yet. Use the form above to send your first appeal.</p>
        )}
      </div>
    </div>
  );
};

export default SuspendedGovernancePage;
