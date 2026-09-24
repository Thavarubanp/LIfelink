import React, { useEffect, useState } from 'react';
import { ArrowDownLeft, ArrowLeftRight, ArrowUpRight, CheckCircle2, Loader2, Send, Trash2, X, XCircle } from 'lucide-react';
import { hospitalApi, inventoryApi, profileApi, transferApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const STATUS_VARIANT = { Pending: 'warning', Completed: 'success', Rejected: 'primary', Cancelled: 'default' };
const unwrap = (res) => res?.data || (Array.isArray(res) ? res : []);
const fmt = (value) => (value ? new Date(value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');

/**
 * Inter-Hospital Blood Transfer between approved hospitals: request blood from, or offer blood to, another
 * hospital. The other hospital accepts or rejects; no doctor or AI approval is involved. Accepting moves the
 * earliest-expiring packets immediately (same packet IDs, new owner) and both histories are kept.
 */
export const TransfersPage = () => {
  const { addToast } = useNotification();
  const [myHospitalId, setMyHospitalId] = useState(null);
  const [hospitals, setHospitals] = useState([]);
  const [stock, setStock] = useState([]);
  const [transfers, setTransfers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState('incoming');
  const [form, setForm] = useState({ transferType: 'Request', counterpartHospitalId: '', bloodGroup: 'O+', unitsRequested: 1, notes: '' });
  const [creating, setCreating] = useState(false);
  const [rejecting, setRejecting] = useState(null);
  const [reason, setReason] = useState('');
  const [detail, setDetail] = useState(null);

  const [reloadKey, setReloadKey] = useState(0);
  const reload = () => setReloadKey((k) => k + 1);

  useEffect(() => {
    const fetchTransfers = async () => {
      try {
        const me = await profileApi.getMyProfile();
        const [hospitalRes, stockRes, transferRes] = await Promise.all([
          hospitalApi.getHospitals(true),
          inventoryApi.getAllInventory().catch(() => []),
          transferApi.getAllTransferRequests()
        ]);
        setMyHospitalId(me.id);
        setHospitals(unwrap(hospitalRes).filter((h) => h.isVerified && !h.isSuspended && h.hospitalId !== me.id));
        setStock(unwrap(stockRes));
        setTransfers(unwrap(transferRes));
      } catch (err) {
        addToast({ title: 'Could not load transfers', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    fetchTransfers();
  }, [reloadKey, addToast]);

  const counterpartOf = (t) => (t.transferType === 'Offer' ? t.receiverHospitalId : t.senderHospitalId);
  const incoming = transfers.filter((t) => t.status === 'Pending' && counterpartOf(t) === myHospitalId);
  const outgoing = transfers.filter((t) => t.status === 'Pending' && t.createdByHospitalId === myHospitalId);
  const history = transfers.filter((t) => t.status !== 'Pending');
  const visible = tab === 'incoming' ? incoming : tab === 'outgoing' ? outgoing : history;

  const availableAt = (hospitalId, group) => stock.find((s) => s.hospitalId === hospitalId && s.bloodGroup === group)?.unitsAvailable ?? 0;
  const myAvailable = availableAt(myHospitalId, form.bloodGroup);

  const create = async (e) => {
    e.preventDefault();
    setCreating(true);
    try {
      await transferApi.createTransferRequest({ ...form, unitsRequested: Number(form.unitsRequested) });
      addToast({ title: form.transferType === 'Offer' ? 'Offer sent' : 'Request sent', message: 'The other hospital has been notified.', type: 'success' });
      setForm({ ...form, notes: '', unitsRequested: 1 });
      setTab('outgoing');
      reload();
    } catch (err) {
      addToast({ title: 'Could not create transfer', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setCreating(false);
    }
  };

  const act = async (fn, success) => {
    try {
      await fn();
      addToast(success);
      reload();
    } catch (err) {
      addToast({ title: 'Action failed', message: getApiErrorMessage(err), type: 'error' });
    }
  };

  const openDetail = async (t) => {
    try {
      const res = await transferApi.getTransferRequestById(t.transferRequestId);
      setDetail(res?.data || t);
    } catch {
      setDetail(t);
    }
  };

  if (loading) {
    return <div className="py-20 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2"><ArrowLeftRight className="w-5 h-5 text-red-600" /> Inter-Hospital Blood Transfer</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Request blood from another approved hospital or offer blood you hold. The other hospital accepts or rejects; accepted transfers move the packets straight away.
        </p>
      </div>

      <form onSubmit={create} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-sm grid grid-cols-1 md:grid-cols-6 gap-3 text-xs items-end">
        <div>
          <label className="block font-semibold mb-1">Type</label>
          <select value={form.transferType} onChange={(e) => setForm({ ...form, transferType: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl">
            <option value="Request">Request blood</option>
            <option value="Offer">Offer blood</option>
          </select>
        </div>
        <div className="md:col-span-2">
          <label className="block font-semibold mb-1">{form.transferType === 'Offer' ? 'Offer to' : 'Request from'}</label>
          <select required value={form.counterpartHospitalId} onChange={(e) => setForm({ ...form, counterpartHospitalId: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl">
            <option value="">Select hospital</option>
            {hospitals.map((h) => (
              <option key={h.hospitalId} value={h.hospitalId}>
                {h.name}{form.transferType === 'Request' ? ` (${availableAt(h.hospitalId, form.bloodGroup)} ${form.bloodGroup} available)` : ''}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="block font-semibold mb-1">Blood group</label>
          <select value={form.bloodGroup} onChange={(e) => setForm({ ...form, bloodGroup: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl">
            {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
          </select>
        </div>
        <div>
          <label className="block font-semibold mb-1">Units {form.transferType === 'Offer' && <span className="text-slate-400">({myAvailable} held)</span>}</label>
          <input type="number" min="1" required value={form.unitsRequested} onChange={(e) => setForm({ ...form, unitsRequested: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
        </div>
        <button type="submit" disabled={creating} className="py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-xl font-semibold flex items-center justify-center gap-1.5 disabled:opacity-50">
          {creating ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />} Send
        </button>
        <div className="md:col-span-6">
          <input placeholder="Notes (optional)" value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} maxLength={500} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
        </div>
      </form>

      <div className="flex flex-wrap gap-2">
        {[
          { key: 'incoming', label: 'Waiting for my response', count: incoming.length },
          { key: 'outgoing', label: 'Sent by my hospital', count: outgoing.length },
          { key: 'history', label: 'History', count: history.length }
        ].map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={`px-3 py-1.5 rounded-lg text-xs font-semibold ${tab === t.key ? 'bg-red-600 text-white' : 'bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-300'}`}>
            {t.label} ({t.count})
          </button>
        ))}
      </div>

      {visible.length === 0 ? (
        <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl text-xs text-slate-500">No transfers here.</div>
      ) : (
        <div className="space-y-3">
          {visible.map((t) => {
            const incomingToMe = counterpartOf(t) === myHospitalId;
            const other = t.senderHospitalId === myHospitalId ? t.receiverHospitalName : t.senderHospitalName;
            const sendsBlood = t.senderHospitalId === myHospitalId;
            return (
              <div key={t.transferRequestId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3 text-xs">
                <div className="space-y-1">
                  <div className="flex items-center gap-2 flex-wrap">
                    {sendsBlood ? <ArrowUpRight className="w-4 h-4 text-rose-500" /> : <ArrowDownLeft className="w-4 h-4 text-emerald-500" />}
                    <span className="font-bold text-slate-900 dark:text-slate-100">{t.unitsRequested} x {t.bloodGroup}</span>
                    <span className="text-slate-500">{sendsBlood ? 'to' : 'from'} {other}</span>
                    <Badge variant="default" size="sm">{t.transferType}</Badge>
                    <Badge variant={STATUS_VARIANT[t.status] || 'default'} size="sm">{t.status}</Badge>
                  </div>
                  <p className="text-[11px] text-slate-500">Created {fmt(t.requestedAt)}{t.notes ? ` - ${t.notes}` : ''}</p>
                  {t.rejectionReason && <p className="text-[11px] text-rose-600"><span className="font-semibold">Rejection reason:</span> {t.rejectionReason}</p>}
                </div>
                <div className="flex flex-wrap gap-2 shrink-0">
                  {t.status === 'Pending' && incomingToMe && (
                    <>
                      <button type="button" onClick={() => act(() => transferApi.approveTransferRequest(t.transferRequestId), { title: 'Transfer accepted', message: 'Packets moved and both inventories updated.', type: 'success' })}
                        className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg font-semibold bg-emerald-600 text-white hover:bg-emerald-700">
                        <CheckCircle2 className="w-3.5 h-3.5" /> Accept
                      </button>
                      <button type="button" onClick={() => { setRejecting(t); setReason(''); }}
                        className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg font-semibold bg-rose-600 text-white hover:bg-rose-700">
                        <XCircle className="w-3.5 h-3.5" /> Reject
                      </button>
                    </>
                  )}
                  {t.status === 'Pending' && t.createdByHospitalId === myHospitalId && (
                    <button type="button" onClick={() => window.confirm('Delete this pending transfer? It stays in the history as Cancelled.') &&
                      act(() => transferApi.deleteTransferRequest(t.transferRequestId), { title: 'Transfer deleted', message: 'The other hospital has been notified.', type: 'info' })}
                      className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                      <Trash2 className="w-3.5 h-3.5" /> Delete
                    </button>
                  )}
                  {t.status === 'Completed' && (
                    <button type="button" onClick={() => openDetail(t)} className="px-3 py-1.5 rounded-lg font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                      Packets moved
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {rejecting && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <form onSubmit={(e) => { e.preventDefault(); act(() => transferApi.rejectTransferRequest(rejecting.transferRequestId, reason.trim()), { title: 'Transfer rejected', message: 'The other hospital will see your reason.', type: 'info' }).then(() => setRejecting(null)); }}
            className="max-w-md w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl space-y-3 text-xs">
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Reject transfer</h3>
            <textarea required maxLength={500} rows={3} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason (shown to the other hospital)"
              className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setRejecting(null)} className="px-4 py-2 rounded-xl font-semibold hover:bg-slate-100 dark:hover:bg-slate-800">Cancel</button>
              <button type="submit" className="px-4 py-2 rounded-xl font-semibold bg-rose-600 text-white hover:bg-rose-700">Reject</button>
            </div>
          </form>
        </div>
      )}

      {detail && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="max-w-md w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl space-y-3 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Packets moved ({detail.packetIds?.length || 0})</h3>
              <button type="button" onClick={() => setDetail(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>
            <p className="text-slate-500">From {detail.senderHospitalName} to {detail.receiverHospitalName}, accepted {fmt(detail.approvedAt)}. Packet IDs did not change.</p>
            <ul className="font-mono grid grid-cols-2 gap-1">
              {(detail.packetIds || []).map((id) => <li key={id}>PKT-{String(id).substring(0, 8).toUpperCase()}</li>)}
            </ul>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransfersPage;
