import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Calendar, CheckCircle2, FileText, Loader2, LogOut, PencilLine, Stethoscope, XCircle } from 'lucide-react';
import { acceptanceApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const STATUS = {
  Accepted: { label: 'Screening not started', variant: 'info', next: 'Start your health screening interview.' },
  ScreeningPending: { label: 'Screening in progress', variant: 'warning', next: 'Continue your health screening interview.' },
  ScreeningCompleted: { label: 'Waiting for doctor', variant: 'info', next: 'Your report is with the doctor. You can still update your answers until they decide.' },
  Verified: { label: 'Approved - slot reserved', variant: 'success', next: 'Visit the hospital to donate. If you cannot attend, please withdraw so another donor can help.' },
  Matched: { label: 'Donation recorded', variant: 'success', next: 'Thank you for donating!' },
  Rejected: { label: 'Not approved', variant: 'primary', next: null },
  Cancelled: { label: 'Withdrawn / closed', variant: 'default', next: null }
};
const ACTIVE = ['Accepted', 'ScreeningPending', 'ScreeningCompleted', 'Verified'];

const fmt = (value) => (value ? new Date(value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');

const DecisionHistory = ({ history }) => {
  if (!history?.length) return null;
  return (
    <div className="relative pl-6 space-y-2 before:absolute before:left-2.5 before:top-1 before:bottom-1 before:w-0.5 before:bg-slate-200 dark:before:bg-slate-800">
      {history.map((h) => (
        <div key={h.donorVerificationId} className="relative">
          <div className="absolute -left-6 top-0.5 w-5 h-5 rounded-full border bg-white dark:bg-slate-900 border-slate-300 dark:border-slate-700 flex items-center justify-center">
            {h.status === 'Approved' ? <CheckCircle2 className="w-3 h-3 text-emerald-600" /> : h.status === 'Rejected' ? <XCircle className="w-3 h-3 text-rose-600" /> : <FileText className="w-3 h-3 text-slate-400" />}
          </div>
          <div className="text-[11px] text-slate-600 dark:text-slate-300 space-y-0.5">
            <div className="font-semibold text-slate-800 dark:text-slate-100">
              Report version {h.reportVersion} - {h.status === 'Pending' ? 'waiting for the doctor' : h.status}
            </div>
            <div className="text-slate-400 flex items-center gap-1"><Calendar className="w-3 h-3" /> Submitted {fmt(h.submittedAt)}</div>
            {h.decidedAt && <div>Decided by {h.decidedByName} on {fmt(h.decidedAt)}</div>}
            {h.approvalNotes && <div><span className="font-semibold">Doctor's notes:</span> {h.approvalNotes}</div>}
            {h.rejectionReason && <div className="text-rose-600 dark:text-rose-400"><span className="font-semibold">Reason:</span> {h.rejectionReason}</div>}
            {h.note && <div className="text-slate-400">{h.note}</div>}
          </div>
        </div>
      ))}
    </div>
  );
};

export const MyAcceptancesPage = () => {
  const [acceptances, setAcceptances] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(null);
  const { addToast } = useNotification();
  const navigate = useNavigate();

  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const fetchAcceptances = async () => {
      try {
        const data = await acceptanceApi.getMyAcceptances();
        setAcceptances(Array.isArray(data) ? data : []);
      } catch (err) {
        addToast({ title: 'Could not load acceptances', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    fetchAcceptances();
  }, [reloadKey, addToast]);

  const withdraw = async (a) => {
    const reserved = a.status === 'Verified';
    if (!window.confirm(reserved
      ? 'Withdraw from this donation? Your reserved slot will be released for another donor.'
      : 'Withdraw from this donation? You can accept other requests afterwards.')) return;
    setBusy(a.acceptanceId);
    try {
      await acceptanceApi.cancelAcceptance(a.acceptanceId);
      addToast({ title: 'Withdrawn', message: 'You have withdrawn from this donation.', type: 'info' });
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Could not withdraw', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setBusy(null);
    }
  };

  const updateAnswers = async (a) => {
    if (!window.confirm('Update your answers? Your current report is kept in the history and a new version will be sent to the doctor.')) return;
    setBusy(a.acceptanceId);
    try {
      await acceptanceApi.reopenScreening(a.acceptanceId);
      navigate(`/donor/acceptances/${a.acceptanceId}/screening`);
    } catch (err) {
      addToast({ title: 'Could not reopen screening', message: getApiErrorMessage(err), type: 'error' });
      setBusy(null);
    }
  };

  if (loading) {
    return <div className="py-20 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">My Donation Acceptances</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Follow your health screening, the doctor's decision and your donation for each request you accepted.
        </p>
      </div>

      {acceptances.length === 0 ? (
        <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl text-xs text-slate-500">
          You have not accepted any donation requests yet. <Link to="/donor/requests" className="text-red-600 font-semibold">Browse available requests</Link>
        </div>
      ) : (
        <div className="space-y-4">
          {acceptances.map((a) => {
            const status = STATUS[a.status] || { label: a.status, variant: 'default' };
            const latest = a.screeningHistory?.[a.screeningHistory.length - 1];
            const canUpdate = a.status === 'ScreeningCompleted' && latest?.status === 'Pending';
            return (
              <div key={a.acceptanceId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-sm space-y-4">
                <div className="flex flex-col md:flex-row md:items-start justify-between gap-3">
                  <div className="flex items-start gap-3">
                    <div className="w-11 h-11 rounded-xl bg-red-600 text-white font-black flex items-center justify-center shrink-0">{a.requestBloodGroup || '?'}</div>
                    <div>
                      <div className="flex items-center gap-2 flex-wrap">
                        <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">{a.hospitalName || 'Hospital'}</h3>
                        <Badge variant={status.variant} size="sm">{status.label}</Badge>
                        {a.requestStatus === 'Deleted' && <Badge variant="default" size="sm">Request deleted</Badge>}
                      </div>
                      <p className="text-[11px] text-slate-500 mt-0.5">
                        Request #{String(a.bloodRequestId).substring(0, 8)} - accepted {fmt(a.acceptedAt)} - {a.fulfilledUnits}/{a.unitsRequired} donated, {a.reservedUnits} reserved
                      </p>
                      {status.next && <p className="text-xs text-slate-700 dark:text-slate-300 mt-1.5">{status.next}</p>}
                      {a.rejectionReason && (a.status === 'Rejected' || a.status === 'Cancelled') && (
                        <p className="text-xs text-rose-600 dark:text-rose-400 mt-1.5"><span className="font-semibold">Reason:</span> {a.rejectionReason}</p>
                      )}
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2 shrink-0">
                    {(a.status === 'Accepted' || a.status === 'ScreeningPending') && (
                      <Link to={`/donor/acceptances/${a.acceptanceId}/screening`}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700">
                        <Stethoscope className="w-3.5 h-3.5" /> {a.status === 'Accepted' ? 'Start screening' : 'Continue screening'}
                      </Link>
                    )}
                    {canUpdate && (
                      <button type="button" disabled={busy === a.acceptanceId} onClick={() => updateAnswers(a)}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-50">
                        <PencilLine className="w-3.5 h-3.5" /> Update my answers
                      </button>
                    )}
                    {ACTIVE.includes(a.status) && (
                      <button type="button" disabled={busy === a.acceptanceId} onClick={() => withdraw(a)}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 border border-rose-200 hover:bg-rose-100 dark:bg-rose-950/40 dark:text-rose-300 dark:border-rose-900 disabled:opacity-50">
                        <LogOut className="w-3.5 h-3.5" /> Withdraw
                      </button>
                    )}
                  </div>
                </div>
                <DecisionHistory history={a.screeningHistory} />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

export default MyAcceptancesPage;
