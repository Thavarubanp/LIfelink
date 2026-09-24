import React, { useState, useEffect, useCallback } from 'react';
import { governanceApi, appealApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import AppealThread from '../../components/complaints/AppealThread';
import ComplaintReplyModal from '../../components/complaints/ComplaintReplyModal';
import {
  ShieldAlert, CheckCircle2, XCircle, Clock, Send, Loader2, LogOut, MessageSquare, RefreshCw, User, Paperclip, Lock
} from 'lucide-react';

const MAX_ATTACHMENT_BYTES = 2 * 1024 * 1024;

const statusConfig = {
  PENDING: { label: 'Awaiting Admin', color: 'bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/60 dark:text-amber-300 dark:border-amber-800', icon: Clock },
  REJECTED: { label: 'Rejected (open)', color: 'bg-rose-50 text-rose-700 border-rose-200 dark:bg-rose-950/60 dark:text-rose-300 dark:border-rose-800', icon: XCircle },
  APPROVED: { label: 'Approved', color: 'bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/60 dark:text-emerald-300 dark:border-emerald-800', icon: CheckCircle2 },
  CLOSED: { label: 'Closed', color: 'bg-slate-100 text-slate-700 border-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700', icon: Lock }
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

const fmt = (iso) => (iso ? new Date(iso).toLocaleString() : '—');

/**
 * Governance Portal: the only screen for suspended users, staff of a suspended hospital and doctors of a
 * suspended hospital. Shows the profile summary, suspension reason, appeal threads, the appeal/reply forms and
 * Sign out. Doctors can only view.
 */
export const SuspendedGovernancePage = () => {
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [appealReason, setAppealReason] = useState('');
  const [attachment, setAttachment] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [replyTarget, setReplyTarget] = useState(null);
  const { logout } = useAuth();
  const { addToast } = useNotification();

  const fetchStatus = useCallback(async () => {
    try {
      const res = await governanceApi.getStatus();
      setStatus(res.data);
    } catch (err) {
      addToast({ title: 'Error', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setLoading(false);
    }
  }, [addToast]);

  useEffect(() => { fetchStatus(); }, [fetchStatus]);

  const handleFile = (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (file.size > MAX_ATTACHMENT_BYTES) {
      addToast({ title: 'Attachment Too Large', message: 'Attachment cannot exceed 2 MB.', type: 'error' });
      e.target.value = '';
      return;
    }
    const reader = new FileReader();
    reader.onload = () => setAttachment({ url: reader.result, name: file.name });
    reader.readAsDataURL(file);
  };

  const handleSubmitAppeal = async (e) => {
    e.preventDefault();
    if (appealReason.trim().length < 10) return;
    setSubmitting(true);
    try {
      await appealApi.submitAppeal({ reason: appealReason.trim(), attachmentUrl: attachment?.url, attachmentName: attachment?.name });
      setAppealReason('');
      setAttachment(null);
      addToast({ title: 'Appeal Submitted', message: 'Your appeal has been sent to the admin.', type: 'success' });
      await fetchStatus();
    } catch (err) {
      addToast({ title: 'Submission Failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setSubmitting(false);
    }
  };

  const handleReply = async (dto) => {
    await appealApi.replyToAppeal(replyTarget.appealId, dto);
    addToast({ title: 'Reply Sent', message: 'The administrator has been notified.', type: 'success' });
    setReplyTarget(null);
    await fetchStatus();
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-slate-50 dark:bg-slate-950 flex items-center justify-center">
        <Loader2 className="w-8 h-8 animate-spin text-red-500" />
      </div>
    );
  }

  const appeals = [...(status?.allAppeals || [])].reverse(); // newest first
  const profile = status?.profile;
  const readOnly = status?.isReadOnlyViewer;
  const card = 'bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm';

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 py-10 px-4">
      <div className="max-w-2xl mx-auto space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-2xl bg-gradient-to-tr from-red-600 to-red-500 flex items-center justify-center text-xl shadow-lg shadow-red-600/30">💉</div>
            <div>
              <h1 className="text-lg font-bold">Life<span className="text-red-500">Link</span></h1>
              <p className="text-[11px] text-slate-500">Governance Portal</p>
            </div>
          </div>
          <button
            onClick={logout}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-lg transition-colors"
          >
            <LogOut className="w-3.5 h-3.5" /> Sign Out
          </button>
        </div>

        {/* Profile summary */}
        {profile && (
          <div className={`${card} p-5`}>
            <div className="flex items-center gap-2 mb-3">
              <User className="w-4 h-4 text-slate-500" />
              <h3 className="text-sm font-bold">Profile Summary</h3>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
              {[
                ['Name', profile.name],
                ['Email', profile.email],
                ['Role', profile.role === 'User' ? 'Donor / Patient' : profile.role],
                ['Phone', profile.phone || 'Not provided'],
                ['Status', profile.status],
                ['Member Since', profile.createdAt ? new Date(profile.createdAt).toLocaleDateString() : '—'],
                ...(profile.hospitalName ? [['Hospital', profile.hospitalName]] : [])
              ].map(([label, value]) => (
                <div key={label} className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
                  <span className="text-slate-400 text-[10px] uppercase font-semibold">{label}</span>
                  <div className="font-bold truncate">{value}</div>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Suspension reason */}
        <div className="bg-amber-50 dark:bg-amber-950/40 border-2 border-amber-300 dark:border-amber-700/50 rounded-2xl p-6">
          <div className="flex items-start gap-4">
            <div className="p-3 bg-amber-100 dark:bg-amber-600/20 text-amber-600 rounded-xl shrink-0">
              <ShieldAlert className="w-8 h-8" />
            </div>
            <div className="flex-1">
              <h2 className="text-base font-bold text-amber-700 dark:text-amber-400">
                {status?.suspendedEntity === 'Hospital' ? 'Hospital Suspended' : 'Account Suspended'}
              </h2>
              <p className="text-xs text-amber-800/80 dark:text-amber-200/80 mt-1 leading-relaxed">
                {status?.suspensionReason || 'Your account has been suspended for administrative review.'}
              </p>
              {status?.suspendedUntil && (
                <p className="text-[11px] text-amber-700 dark:text-amber-400 font-mono mt-2">Suspended until: {fmt(status.suspendedUntil)}</p>
              )}
              {readOnly && (
                <p className="text-[11px] text-amber-700 dark:text-amber-300 mt-2">
                  Your hospital is suspended. You can view its status and appeal thread; the hospital manages the appeal.
                </p>
              )}
            </div>
            <button onClick={fetchStatus} className="p-2 text-amber-600 hover:text-amber-800 transition-colors" title="Refresh status">
              <RefreshCw className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* Appeal threads */}
        {appeals.map((appeal) => (
          <div key={appeal.appealId} className={`${card} overflow-hidden`}>
            <div className="px-5 py-4 border-b border-slate-100 dark:border-slate-800 flex items-center gap-2 flex-wrap">
              <MessageSquare className="w-4 h-4 text-purple-500" />
              <h3 className="text-sm font-bold">Appeal</h3>
              <span className="text-[11px] text-slate-500">{fmt(appeal.submittedAt)}</span>
              <StatusBadge status={appeal.status} />
              <div className="ml-auto">
                {!readOnly && appeal.canAppellantReply && (
                  <button
                    onClick={() => setReplyTarget(appeal)}
                    className="px-3 py-1.5 bg-blue-50 hover:bg-blue-100 text-blue-700 dark:bg-blue-950/40 dark:text-blue-300 border border-blue-200 dark:border-blue-800 rounded-xl text-xs font-semibold flex items-center gap-1.5"
                  >
                    <MessageSquare className="w-3.5 h-3.5" /> Reply
                  </button>
                )}
                {appeal.awaitingAdminReply && (
                  <span className="text-xs font-semibold text-amber-700 dark:text-amber-300 px-2.5 py-1 bg-amber-50 dark:bg-amber-950/40 rounded-lg border border-amber-200 dark:border-amber-800 flex items-center gap-1.5">
                    <Clock className="w-3.5 h-3.5" /> Waiting for admin response
                  </span>
                )}
                {appeal.isClosed && <span className="text-xs font-semibold text-slate-400 italic">Read-Only</span>}
              </div>
            </div>
            <div className="p-5">
              <AppealThread appeal={appeal} appellantLabel={status?.suspendedEntity === 'Hospital' ? 'Hospital' : 'You'} />
            </div>
          </div>
        ))}

        {/* New appeal */}
        {status?.canAppeal && (
          <div className={`${card} p-6`}>
            <h3 className="text-sm font-bold mb-1">Submit an Appeal</h3>
            <p className="text-xs text-slate-500 mb-4">Explain your situation and request reinstatement. You can attach a supporting file.</p>
            <form onSubmit={handleSubmitAppeal} className="space-y-3">
              <textarea
                required
                rows={4}
                minLength={10}
                maxLength={2000}
                value={appealReason}
                onChange={(e) => setAppealReason(e.target.value)}
                placeholder="Explain why you believe this suspension should be lifted..."
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs focus:outline-none focus:border-purple-500 resize-none"
              />
              <label className="flex items-center gap-2 px-3 py-2 rounded-xl border border-dashed border-slate-300 dark:border-slate-700 text-xs text-slate-600 dark:text-slate-300 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800">
                <Paperclip className="w-3.5 h-3.5" />
                <span className="truncate">{attachment ? attachment.name : 'Attach a file (optional, max 2 MB)'}</span>
                <input type="file" className="hidden" onChange={handleFile} />
              </label>
              <div className="flex items-center justify-between">
                <span className="text-[10px] text-slate-500">{appealReason.length}/2000 chars (min 10)</span>
                <button
                  type="submit"
                  disabled={submitting || appealReason.trim().length < 10}
                  className="flex items-center gap-2 px-4 py-2 bg-purple-600 hover:bg-purple-700 disabled:opacity-50 text-white font-semibold rounded-xl text-xs shadow-lg"
                >
                  {submitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                  Submit Appeal
                </button>
              </div>
            </form>
          </div>
        )}

        {appeals.length === 0 && !status?.canAppeal && (
          <p className="text-center text-xs text-slate-500">No appeals have been submitted yet.</p>
        )}
      </div>

      {replyTarget && (
        <ComplaintReplyModal complaint={null} title="Reply to Appeal" onSubmit={handleReply} onClose={() => setReplyTarget(null)} />
      )}
    </div>
  );
};

export default SuspendedGovernancePage;
