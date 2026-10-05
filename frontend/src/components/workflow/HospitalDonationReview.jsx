import { useEffect, useState } from 'react';
import { CheckCircle2, HeartHandshake, Loader2, X, XCircle } from 'lucide-react';
import { acceptanceApi, bloodRequestApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage, isConflictError } from '../../utils/errorUtils';
import { formatDisplayDate } from '../../utils/dateUtils';

const fmtDate = (value) => formatDisplayDate(value, '-');

/**
 * Doctor: hospital donations offered to a blood request assigned to them. Each offer lists the packets the hospital
 * holds for the request. Approving donates them at once (the units count as fulfilled); rejecting needs a reason and
 * returns the packets to that hospital. No AI screening applies to hospital blood.
 */
export const HospitalDonationReview = ({ request, onClose, onDecided }) => {
  const { addToast } = useNotification();
  const [offers, setOffers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [decision, setDecision] = useState(null); // { type: 'approve' | 'reject', offer }
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const list = await bloodRequestApi.getRequestAcceptances(request.bloodRequestId);
        setOffers((Array.isArray(list) ? list : []).filter((a) => a.donorHospitalId && a.status === 'Accepted'));
      } catch (err) {
        addToast({ title: 'Could not load hospital donations', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [request.bloodRequestId, reloadKey, addToast]);

  const free = Math.max(0, request.unitsRequired - request.fulfilledUnits - (request.reservedUnits || 0));

  const submit = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      if (decision.type === 'approve') {
        await acceptanceApi.approveHospitalDonation(decision.offer.acceptanceId, text.trim());
        addToast({ title: 'Donation approved', message: `${decision.offer.packets.length} packet(s) from ${decision.offer.donorName} count as donated.`, type: 'success' });
      } else {
        await acceptanceApi.rejectHospitalDonation(decision.offer.acceptanceId, text.trim());
        addToast({ title: 'Donation rejected', message: 'The packets were returned to the hospital.', type: 'info' });
      }
      setDecision(null);
      setText('');
      setReloadKey((k) => k + 1);
      onDecided?.();
    } catch (err) {
      addToast({ title: 'Action failed', message: getApiErrorMessage(err), type: 'error' });
      // The hospital withdrew or someone else decided at the same moment: show the current offers
      if (isConflictError(err)) {
        setDecision(null);
        setReloadKey((k) => k + 1);
        onDecided?.();
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4">
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-lg max-h-[90vh] overflow-y-auto shadow-2xl space-y-4 text-xs">
        <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
          <div className="flex items-center gap-2">
            <HeartHandshake className="w-5 h-5 text-red-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Hospital donations</h3>
          </div>
          <button onClick={() => !submitting && onClose()} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
            <X className="w-4 h-4" />
          </button>
        </div>

        <p className="text-slate-500 dark:text-slate-400">
          Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(request.bloodRequestId).substring(0, 8)}</span>:{' '}
          {request.bloodGroup}, {request.fulfilledUnits} of {request.unitsRequired} unit(s) fulfilled, {free} slot(s) free.
        </p>

        {loading ? (
          <div className="p-6 flex items-center justify-center gap-2 text-slate-400"><Loader2 className="w-4 h-4 animate-spin text-red-500" /> Loading...</div>
        ) : offers.length === 0 ? (
          <p className="p-4 text-center text-slate-500">No hospital donation is waiting for your decision.</p>
        ) : (
          <div className="space-y-3">
            {offers.map((offer) => (
              <div key={offer.acceptanceId} className="p-3 rounded-xl border border-slate-200 dark:border-slate-700 space-y-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="font-bold text-slate-900 dark:text-slate-100">{offer.donorName}</p>
                    <p className="text-[11px] text-slate-500">{offer.donorPhoneNumber || offer.donorEmail} - offered {fmtDate(offer.acceptedAt)}</p>
                  </div>
                  <span className="font-semibold text-slate-700 dark:text-slate-200">{offer.packets.length} packet(s)</span>
                </div>
                <ul className="divide-y divide-slate-100 dark:divide-slate-800 rounded-lg bg-slate-50 dark:bg-slate-800/60">
                  {offer.packets.map((p) => (
                    <li key={p.packetId} className="flex flex-wrap items-center gap-x-3 px-3 py-1.5">
                      <span className="font-mono font-semibold">{p.trackingNumber}</span>
                      <span className="text-slate-500">{p.bloodGroup}</span>
                      <span className="text-slate-500 ml-auto">Collected {fmtDate(p.collectionDate)} - expires {fmtDate(p.expiryDate)}</span>
                    </li>
                  ))}
                </ul>
                {decision?.offer.acceptanceId === offer.acceptanceId ? (
                  <form onSubmit={submit} className="space-y-2">
                    <textarea
                      required={decision.type === 'reject'}
                      rows={2}
                      maxLength={500}
                      value={text}
                      onChange={(e) => setText(e.target.value)}
                      placeholder={decision.type === 'approve' ? 'Clinical notes (optional)' : 'Reason for rejecting (shown to the hospital)'}
                      className="w-full p-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                    />
                    <div className="flex justify-end gap-2">
                      <button type="button" onClick={() => setDecision(null)} disabled={submitting} className="px-3 py-1.5 rounded-lg font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800">Cancel</button>
                      <button type="submit" disabled={submitting || (decision.type === 'reject' && !text.trim())}
                        className={`px-3 py-1.5 rounded-lg font-semibold text-white disabled:opacity-50 flex items-center gap-1.5 ${decision.type === 'approve' ? 'bg-emerald-600 hover:bg-emerald-700' : 'bg-rose-600 hover:bg-rose-700'}`}>
                        {submitting && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                        {decision.type === 'approve' ? 'Confirm approval' : 'Confirm rejection'}
                      </button>
                    </div>
                  </form>
                ) : (
                  <div className="flex justify-end gap-2">
                    <button type="button" onClick={() => { setDecision({ type: 'approve', offer }); setText(''); }}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg font-semibold bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800">
                      <CheckCircle2 className="w-3.5 h-3.5" /> Approve
                    </button>
                    <button type="button" onClick={() => { setDecision({ type: 'reject', offer }); setText(''); }}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900">
                      <XCircle className="w-3.5 h-3.5" /> Reject
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default HospitalDonationReview;
