import { useEffect, useState } from 'react';
import { Filter, HeartHandshake, Loader2, Undo2, X } from 'lucide-react';
import { acceptanceApi, bloodRequestApi, profileApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { PacketPicker } from '../../components/inventory/PacketPicker';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const DONATION_STATUS = {
  Accepted: { label: 'Awaiting doctor approval', variant: 'warning' },
  Matched: { label: 'Approved - donated', variant: 'success' },
  Rejected: { label: 'Not approved', variant: 'danger' },
  Cancelled: { label: 'Withdrawn', variant: 'default' }
};
const fmt = (value) => (value ? new Date(value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');
const freeSlots = (r) => Math.max(0, (r.unitsRequired || 0) - (r.fulfilledUnits || 0) - (r.reservedUnits || 0));

/**
 * Hospital staff: publicly posted blood requests from other hospitals. The hospital donates by choosing available
 * packets of the required blood group from its own inventory; the packets are held until the doctor assigned to the
 * request approves (they are then donated) or rejects (they return). No AI screening is involved.
 */
export const DonateBloodPage = () => {
  const { addToast } = useNotification();
  const [requests, setRequests] = useState([]);
  const [donations, setDonations] = useState([]);
  const [selectedBloodGroup, setSelectedBloodGroup] = useState('');
  const [loading, setLoading] = useState(true);
  const [donating, setDonating] = useState(null); // { request, packetIds }
  const [submitting, setSubmitting] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const [me, publicRequests, mine] = await Promise.all([
          profileApi.getMyProfile(),
          bloodRequestApi.getPublicRequests(selectedBloodGroup || null),
          acceptanceApi.getMyAcceptances()
        ]);
        setRequests((Array.isArray(publicRequests) ? publicRequests : []).filter((r) => r.hospitalId !== me.id));
        setDonations(Array.isArray(mine) ? mine : []);
      } catch (err) {
        addToast({ title: 'Could not load blood requests', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [selectedBloodGroup, reloadKey, addToast]);

  const pendingFor = (requestId) => donations.some((d) => d.bloodRequestId === requestId && d.status === 'Accepted');

  const submitDonation = async (e) => {
    e.preventDefault();
    if (donating.packetIds.length === 0) {
      addToast({ title: 'Select packets', message: `Choose the ${donating.request.bloodGroup} packets to donate.`, type: 'warning' });
      return;
    }
    setSubmitting(true);
    try {
      await acceptanceApi.acceptAsHospital(donating.request.bloodRequestId, donating.packetIds);
      addToast({
        title: 'Donation offered',
        message: `${donating.packetIds.length} packet(s) are held for this request until the assigned doctor approves.`,
        type: 'success'
      });
      setDonating(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Could not donate', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setSubmitting(false);
    }
  };

  const withdraw = async (donation) => {
    if (!window.confirm('Withdraw this donation? The packets return to your available stock.')) return;
    try {
      await acceptanceApi.cancelAcceptance(donation.acceptanceId);
      addToast({ title: 'Donation withdrawn', message: 'The packets are back in your inventory.', type: 'info' });
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Could not withdraw', message: getApiErrorMessage(err), type: 'error' });
    }
  };

  const columns = [
    {
      header: 'Blood Group',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge>
    },
    {
      header: 'Hospital',
      accessor: 'hospitalName',
      cell: (row) => (
        <div>
          <div className="font-bold text-slate-900 dark:text-slate-100">{row.hospitalName}</div>
          <div className="text-[10px] text-slate-400">ID: #{String(row.bloodRequestId || '').substring(0, 8)}</div>
        </div>
      )
    },
    {
      header: 'Units',
      accessor: 'unitsRequired',
      cell: (row) => (
        <div>
          <span className="font-semibold text-slate-900 dark:text-slate-100">{row.remainingUnits ?? row.unitsRequired} of {row.unitsRequired} still needed</span>
          <div className="text-[10px] text-slate-400">{row.fulfilledUnits || 0} donated, {row.reservedUnits || 0} reserved</div>
          {!row.isAcceptingDonors && <Badge variant="info" size="sm">All slots reserved</Badge>}
        </div>
      )
    },
    {
      header: 'Priority',
      accessor: 'priority',
      cell: (row) => <Badge variant={row.priority?.toUpperCase() === 'CRITICAL' ? 'danger' : 'warning'}>{row.priority}</Badge>
    },
    {
      header: 'Reason',
      accessor: 'reason',
      cell: (row) => <span className="text-slate-600 dark:text-slate-400 max-w-xs truncate block">{row.reason}</span>
    },
    {
      header: 'Action',
      accessor: 'bloodRequestId',
      sortable: false,
      cell: (row) => pendingFor(row.bloodRequestId) ? (
        <Badge variant="warning" size="sm">Your donation awaits the doctor</Badge>
      ) : (
        <button
          type="button"
          disabled={!row.isAcceptingDonors || freeSlots(row) === 0}
          onClick={() => setDonating({ request: row, packetIds: [] })}
          className="inline-flex items-center gap-1 px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-lg shadow-sm disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <HeartHandshake className="w-3.5 h-3.5" />
          <span>Donate from inventory</span>
        </button>
      )
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <HeartHandshake className="w-5 h-5 text-red-600" /> Donate Blood
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Public blood requests from other hospitals. Donate packets from your inventory; the doctor assigned to the request approves the donation.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2 p-3 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm">
        <span className="text-xs font-semibold text-slate-500 flex items-center gap-1 mr-2">
          <Filter className="w-3.5 h-3.5" /> Filter Blood Group:
        </span>
        {['', ...BLOOD_GROUPS].map((bg) => (
          <button
            key={bg || 'all'}
            onClick={() => setSelectedBloodGroup(bg)}
            className={`px-3 py-1 text-xs rounded-lg font-semibold transition-all ${
              selectedBloodGroup === bg
                ? 'bg-red-600 text-white shadow-sm'
                : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'
            }`}
          >
            {bg || 'All Groups'}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="py-16 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>
      ) : (
        <DataTable
          columns={columns}
          data={requests}
          searchPlaceholder="Search hospital, blood group, reason..."
          emptyMessage="No public blood requests from other hospitals match this filter."
        />
      )}

      <div className="space-y-3">
        <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">My hospital's donations</h2>
        {donations.length === 0 ? (
          <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl text-xs text-slate-500">
            Your hospital has not donated to a blood request yet.
          </div>
        ) : (
          <div className="space-y-3">
            {donations.map((d) => {
              const status = DONATION_STATUS[d.status] || { label: d.status, variant: 'default' };
              return (
                <div key={d.acceptanceId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3 text-xs">
                  <div className="space-y-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <Badge variant="blood" size="sm">{d.requestBloodGroup}</Badge>
                      <span className="font-bold text-slate-900 dark:text-slate-100">{d.packets?.length || 0} packet(s) to {d.hospitalName}</span>
                      <Badge variant={status.variant} size="sm">{status.label}</Badge>
                    </div>
                    <p className="text-[11px] text-slate-500">
                      Request #{String(d.bloodRequestId).substring(0, 8)} - offered {fmt(d.acceptedAt)}
                    </p>
                    <p className="font-mono text-[11px] text-slate-600 dark:text-slate-300 break-words">
                      {(d.packets || []).map((p) => p.trackingNumber).join(', ')}
                    </p>
                    {d.rejectionReason && d.status !== 'Matched' && (
                      <p className="text-[11px] text-rose-600 dark:text-rose-400"><span className="font-semibold">Reason:</span> {d.rejectionReason}</p>
                    )}
                  </div>
                  {d.status === 'Accepted' && (
                    <button type="button" onClick={() => withdraw(d)}
                      className="shrink-0 inline-flex items-center gap-1 px-3 py-1.5 rounded-lg font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                      <Undo2 className="w-3.5 h-3.5" /> Withdraw
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {donating && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <form onSubmit={submitDonation}
            className="max-w-lg w-full max-h-[90vh] overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl space-y-3 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                Donate {donating.request.bloodGroup} to {donating.request.hospitalName}
              </h3>
              <button type="button" onClick={() => !submitting && setDonating(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>
            <p className="text-slate-500">
              Choose up to {freeSlots(donating.request)} packet(s). They are held for this request and leave your available stock.
              The doctor assigned to the request approves the donation; if it is rejected or you withdraw, the packets come back.
            </p>
            <PacketPicker
              bloodGroup={donating.request.bloodGroup}
              max={freeSlots(donating.request)}
              selected={donating.packetIds}
              onChange={(ids) => setDonating({ ...donating, packetIds: ids })}
            />
            <div className="flex justify-end gap-2 pt-1">
              <button type="button" onClick={() => setDonating(null)} disabled={submitting} className="px-4 py-2 rounded-xl font-semibold hover:bg-slate-100 dark:hover:bg-slate-800">Cancel</button>
              <button type="submit" disabled={submitting || donating.packetIds.length === 0}
                className="px-4 py-2 rounded-xl font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50 flex items-center gap-1.5">
                {submitting && <Loader2 className="w-3.5 h-3.5 animate-spin" />} Donate {donating.packetIds.length || ''} packet(s)
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};

export default DonateBloodPage;
