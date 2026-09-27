import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, Droplet, History, Loader2, Package, PackagePlus, Pencil, Plus, Send, Settings, SlidersHorizontal, X } from 'lucide-react';
import { inventoryApi, profileApi } from '../../api';
import { DataTable, tableFilterClass } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { PacketPicker } from '../../components/inventory/PacketPicker';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const PACKET_STATUS_VARIANT = { Available: 'success', Reserved: 'info', Issued: 'default', Donated: 'primary', Expired: 'warning' };
const fmtDate = (value) => (value ? new Date(value).toLocaleDateString() : '-');
const unwrap = (res) => res?.data || (Array.isArray(res) ? res : []);
// Local calendar date (yyyy-mm-dd); the server treats "today" as the Sri Lanka date
const localToday = () => new Date().toLocaleDateString('en-CA');
const toDateInput = (value) => (value ? String(value).slice(0, 10) : '');

/** Card-header action: icon + short label; icon only (with tooltip and aria-label) on small screens. */
const HeaderAction = ({ icon: Icon, label, onClick, primary = false }) => (
  <button
    type="button"
    onClick={onClick}
    title={label}
    aria-label={label}
    className={`inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold transition-colors ${
      primary
        ? 'bg-red-600 text-white shadow-sm hover:bg-red-700'
        : 'border border-slate-200 text-slate-700 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-200 dark:hover:bg-slate-800'
    }`}
  >
    <Icon className="h-4 w-4 shrink-0" />
    <span className="hidden sm:inline">{label}</span>
  </button>
);

/** Mirrors the server rules for the collected date (required, not in the future, not already expired). */
const collectedDateError = (value, shelfLifeDays) => {
  if (!value) return 'Collected date is required.';
  if (value > localToday()) return 'Collected date cannot be in the future.';
  if (shelfLifeDays) {
    const expiry = new Date(`${value}T00:00:00`);
    expiry.setDate(expiry.getDate() + Number(shelfLifeDays));
    if (expiry.toLocaleDateString('en-CA') <= localToday()) {
      return `A packet collected on ${value} would already be expired (shelf life ${shelfLifeDays} days).`;
    }
  }
  return '';
};

/**
 * Packet-level blood inventory for the signed-in hospital. Staff add collected blood as packets (each gets its own
 * tracking number), set thresholds, and issue blood by selecting the exact packets. Only the hospital that created a
 * packet can edit it, and only while it holds the packet and it is available.
 */
export const InventoryManagementPage = () => {
  const { addToast } = useNotification();
  const [hospital, setHospital] = useState(null);
  const [inventory, setInventory] = useState([]);
  const [packets, setPackets] = useState([]);
  const [statusFilter, setStatusFilter] = useState('Available');
  const [loading, setLoading] = useState(true);
  // { type: 'category' } | { type: 'thresholds', row } | { type: 'issue', row } | { type: 'addPackets' } | { type: 'editPacket', packet }
  const [dialog, setDialog] = useState(null);
  const [form, setForm] = useState({});
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);
  const [history, setHistory] = useState(null);

  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const fetchInventory = async () => {
      try {
        const me = await profileApi.getMyProfile();
        const [profile, inv] = await Promise.all([
          profileApi.getHospitalProfile(me.id),
          inventoryApi.getHospitalInventory(me.id)
        ]);
        setHospital(profile);
        setInventory(unwrap(inv));
      } catch (err) {
        addToast({ title: 'Could not load inventory', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    fetchInventory();
  }, [reloadKey, addToast]);

  useEffect(() => {
    const fetchPackets = async () => {
      try {
        setPackets(unwrap(await inventoryApi.getPackets(statusFilter ? { status: statusFilter } : {})));
      } catch (err) {
        addToast({ title: 'Could not load packets', message: getApiErrorMessage(err), type: 'error' });
      }
    };
    fetchPackets();
  }, [statusFilter, reloadKey, addToast]);

  const open = (next, initialForm) => {
    setForm(initialForm);
    setFormError('');
    setDialog(next);
  };

  const openCategory = () => open({ type: 'category' }, {
    bloodGroup: BLOOD_GROUPS.find((g) => !inventory.some((i) => i.bloodGroup === g)) || 'O+', minimumThreshold: 5, maximumCapacity: 100
  });
  const openThresholds = (row) => open({ type: 'thresholds', row }, { minimumThreshold: row.minimumThreshold, maximumCapacity: row.maximumCapacity });
  const openIssue = (row) => open({ type: 'issue', row }, { packetIds: [], auditNotes: '' });
  const openAddPackets = () => open({ type: 'addPackets' }, { bloodGroup: 'O+', collectionDate: localToday(), quantity: 1 });
  const openEditPacket = (packet) => open({ type: 'editPacket', packet }, { bloodGroup: packet.bloodGroup, collectionDate: toDateInput(packet.collectionDate) });

  const save = async (e) => {
    e.preventDefault();
    setFormError('');

    if (dialog.type === 'addPackets' || dialog.type === 'editPacket') {
      const dateError = collectedDateError(form.collectionDate, hospital?.packetShelfLifeDays);
      if (dateError) {
        setFormError(dateError);
        return;
      }
    }
    if (dialog.type === 'issue') {
      if (form.packetIds.length === 0) {
        setFormError('Select at least one packet to issue.');
        return;
      }
      if (!form.auditNotes.trim()) {
        setFormError('A reason is required when issuing blood.');
        return;
      }
    }

    setSaving(true);
    try {
      if (dialog.type === 'category') {
        await inventoryApi.createInventory({
          bloodGroup: form.bloodGroup,
          minimumThreshold: Number(form.minimumThreshold),
          maximumCapacity: Number(form.maximumCapacity)
        });
        addToast({ title: 'Blood group added', message: `${form.bloodGroup} thresholds saved.`, type: 'success' });
      } else if (dialog.type === 'thresholds') {
        await inventoryApi.updateInventory(dialog.row.inventoryId, {
          minimumThreshold: Number(form.minimumThreshold),
          maximumCapacity: Number(form.maximumCapacity)
        });
        addToast({ title: 'Thresholds saved', message: `${dialog.row.bloodGroup} thresholds updated.`, type: 'success' });
      } else if (dialog.type === 'issue') {
        await inventoryApi.updateInventory(dialog.row.inventoryId, {
          minimumThreshold: dialog.row.minimumThreshold,
          maximumCapacity: dialog.row.maximumCapacity,
          issuePacketIds: form.packetIds,
          auditNotes: form.auditNotes.trim()
        });
        addToast({ title: 'Blood issued', message: `${form.packetIds.length} ${dialog.row.bloodGroup} packet(s) issued.`, type: 'success' });
      } else if (dialog.type === 'addPackets') {
        const res = await inventoryApi.createPackets({
          bloodGroup: form.bloodGroup,
          collectionDate: form.collectionDate,
          quantity: Number(form.quantity) || 1
        });
        const created = unwrap(res);
        addToast({
          title: 'Packets added',
          message: `${created.length} ${form.bloodGroup} packet(s): ${created.map((p) => p.trackingNumber).join(', ')}`,
          type: 'success'
        });
      } else if (dialog.type === 'editPacket') {
        await inventoryApi.updatePacket(dialog.packet.packetId, { bloodGroup: form.bloodGroup, collectionDate: form.collectionDate });
        addToast({ title: 'Packet updated', message: `${dialog.packet.trackingNumber} saved.`, type: 'success' });
      }
      setDialog(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setFormError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const openHistory = async (packet) => {
    try {
      const [detail] = unwrap(await inventoryApi.getPackets({ packetId: packet.packetId }));
      setHistory(detail || packet);
    } catch (err) {
      addToast({ title: 'Could not load packet history', message: getApiErrorMessage(err), type: 'error' });
    }
  };

  const inventoryColumns = [
    { header: 'Blood Group', accessor: 'bloodGroup', cell: (row) => <Badge variant="blood" size="sm">{row.bloodGroup}</Badge> },
    { header: 'Available', accessor: 'unitsAvailable', cell: (row) => <span className="font-bold text-slate-900 dark:text-slate-100">{row.unitsAvailable} packet(s)</span> },
    { header: 'Threshold', accessor: 'minimumThreshold', cell: (row) => <span className="text-slate-500">{row.minimumThreshold}</span> },
    { header: 'Capacity', accessor: 'maximumCapacity', cell: (row) => <span className="text-slate-500">{row.maximumCapacity}</span> },
    {
      header: 'Expiring Soon',
      accessor: 'expiringSoonUnits',
      cell: (row) => row.expiringSoonUnits > 0
        ? <Badge variant="warning" size="sm">{row.expiringSoonUnits} within {row.expiryAlertDays}d</Badge>
        : <span className="text-slate-400">-</span>
    },
    { header: 'Next Expiry', accessor: 'nextExpiryDate', cell: (row) => <span className="text-slate-500">{fmtDate(row.nextExpiryDate)}</span> },
    {
      header: 'Stock Health',
      accessor: 'isLowStock',
      cell: (row) => row.unitsAvailable < row.minimumThreshold
        ? <Badge variant="warning" size="sm">Below threshold</Badge>
        : row.isSurplus ? <Badge variant="info" size="sm">Surplus</Badge> : <Badge variant="success" size="sm">Healthy</Badge>
    },
    {
      header: 'Action',
      accessor: 'inventoryId',
      sortable: false,
      cell: (row) => (
        <div className="flex flex-wrap gap-1.5">
          <button type="button" onClick={() => openThresholds(row)} className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
            <SlidersHorizontal className="w-3 h-3" /> Thresholds
          </button>
          <button type="button" disabled={row.unitsAvailable === 0} onClick={() => openIssue(row)} className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed">
            <Send className="w-3 h-3" /> Issue
          </button>
        </div>
      )
    }
  ];

  const packetColumns = [
    { header: 'Tracking No.', accessor: 'trackingNumber', cell: (row) => <span className="font-mono font-semibold text-slate-700 dark:text-slate-200">{row.trackingNumber}</span> },
    { header: 'Group', accessor: 'bloodGroup', cell: (row) => <Badge variant="blood" size="sm">{row.bloodGroup}</Badge> },
    { header: 'Collected', accessor: 'collectionDate', cell: (row) => <span>{fmtDate(row.collectionDate)}</span> },
    {
      header: 'Expires',
      accessor: 'expiryDate',
      cell: (row) => (
        <span className="flex items-center gap-1">
          {fmtDate(row.expiryDate)} {row.isExpiringSoon && <Badge variant="warning" size="sm">Soon</Badge>}
        </span>
      )
    },
    {
      header: 'Created By',
      accessor: 'createdByHospitalName',
      cell: (row) => (
        <div>
          <div className="text-slate-700 dark:text-slate-200">{row.createdByHospitalId === row.hospitalId ? 'Your hospital' : row.createdByHospitalName}</div>
          <div className="text-[10px] text-slate-400">{fmtDate(row.createdAt)} - {row.source}</div>
        </div>
      )
    },
    { header: 'Status', accessor: 'status', cell: (row) => <Badge variant={PACKET_STATUS_VARIANT[row.status] || 'default'} size="sm">{row.status}</Badge> },
    {
      header: 'Actions',
      accessor: 'packetId',
      sortable: false,
      cell: (row) => (
        <div className="flex items-center gap-3">
          <button type="button" onClick={() => openHistory(row)} className="inline-flex items-center gap-1 text-[11px] font-semibold text-red-600 hover:underline">
            <History className="w-3 h-3" /> History
          </button>
          {row.canEdit && (
            <button type="button" onClick={() => openEditPacket(row)} className="inline-flex items-center gap-1 text-[11px] font-semibold text-slate-600 dark:text-slate-300 hover:underline">
              <Pencil className="w-3 h-3" /> Edit
            </button>
          )}
        </div>
      )
    }
  ];

  if (loading) {
    return <div className="py-20 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>;
  }

  const inputClass = 'w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl';
  const dialogTitle = {
    category: 'Add blood group category',
    thresholds: `${dialog?.row?.bloodGroup}: thresholds`,
    issue: `Issue ${dialog?.row?.bloodGroup} blood`,
    addPackets: 'Add blood packets',
    editPacket: `Edit packet ${dialog?.packet?.trackingNumber}`
  }[dialog?.type];

  return (
    // Bottom padding keeps the floating assistant button clear of the last card's pagination
    <div className="space-y-6 pb-24">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Blood Inventory</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Every unit is a 440 ml packet with its own tracking number. Add collected blood as packets and issue blood by choosing the packets.
        </p>
      </div>

      {hospital && (
        <div className="flex flex-wrap items-center justify-between gap-3 p-3 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl text-xs">
          <span className="flex items-center gap-2 text-slate-600 dark:text-slate-300">
            <Settings className="w-4 h-4 text-slate-400" />
            Packet shelf life: <strong>{hospital.packetShelfLifeDays} days</strong> - Expiry alert window: <strong>{hospital.expiryAlertDays} days</strong>
          </span>
          <Link to={`/profiles/hospital/${hospital.hospitalId}`} className="font-semibold text-red-600 hover:underline">Change in hospital profile</Link>
        </div>
      )}

      {inventory.some((i) => i.unitsAvailable < i.minimumThreshold || i.expiringSoonUnits > 0) && (
        <div className="p-3 rounded-xl border border-amber-200 bg-amber-50 dark:bg-amber-950/30 dark:border-amber-900 text-xs text-amber-800 dark:text-amber-300 flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            Some blood groups are below threshold or have packets expiring soon. The inventory agent will suggest hospitals to request from or offer to; see your notifications or ask the assistant.
          </span>
        </div>
      )}

      <DataTable
        title="Blood groups"
        icon={Droplet}
        actions={<HeaderAction icon={Plus} label="Add blood group" onClick={openCategory} />}
        columns={inventoryColumns}
        data={inventory}
        searchPlaceholder="Search blood group..."
        emptyMessage="No blood group categories yet. Add packets or a blood group to start."
      />

      <DataTable
        title="Blood packets"
        icon={Package}
        actions={<HeaderAction icon={PackagePlus} label="Add packets" onClick={openAddPackets} primary />}
        filters={
          <select aria-label="Packet status" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className={tableFilterClass}>
            <option value="Available">Available</option>
            <option value="Reserved">Reserved (offered, awaiting answer)</option>
            <option value="Issued">Issued</option>
            <option value="Donated">Donated</option>
            <option value="Expired">Expired</option>
            <option value="">All</option>
          </select>
        }
        columns={packetColumns}
        data={packets}
        searchPlaceholder="Search tracking number or group..."
        emptyMessage="No packets with this status."
      />

      {dialog && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <form onSubmit={save} noValidate className="max-w-md w-full max-h-[90vh] overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl space-y-3 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">{dialogTitle}</h3>
              <button type="button" onClick={() => setDialog(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>

            {(dialog.type === 'category' || dialog.type === 'addPackets' || dialog.type === 'editPacket') && (
              <div>
                <label className="block font-semibold mb-1">Blood group</label>
                <select value={form.bloodGroup} onChange={(e) => setForm({ ...form, bloodGroup: e.target.value })} className={inputClass}>
                  {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
                </select>
              </div>
            )}

            {(dialog.type === 'addPackets' || dialog.type === 'editPacket') && (
              <>
                {dialog.type === 'editPacket' && (
                  <p className="text-slate-500">Tracking number, created-by hospital and created date cannot be changed.</p>
                )}
                <div className={dialog.type === 'addPackets' ? 'grid grid-cols-1 sm:grid-cols-2 gap-3' : ''}>
                  <div>
                    <label className="block font-semibold mb-1">Collected date <span className="text-red-500">*</span></label>
                    <input type="date" required max={localToday()} value={form.collectionDate}
                      onChange={(e) => setForm({ ...form, collectionDate: e.target.value })} className={inputClass} />
                  </div>
                  {dialog.type === 'addPackets' && (
                    <div>
                      <label className="block font-semibold mb-1">Number of packets</label>
                      <input type="number" min="1" max="20" required value={form.quantity}
                        onChange={(e) => setForm({ ...form, quantity: e.target.value })} className={inputClass} />
                    </div>
                  )}
                </div>
                <p className="text-slate-500">
                  Expiry is the collected date plus your shelf life ({hospital?.packetShelfLifeDays ?? '-'} days).
                  {dialog.type === 'addPackets' && ' Each packet gets its own tracking number.'}
                </p>
              </>
            )}

            {(dialog.type === 'category' || dialog.type === 'thresholds') && (
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block font-semibold mb-1">Minimum threshold</label>
                  <input type="number" min="0" required value={form.minimumThreshold} onChange={(e) => setForm({ ...form, minimumThreshold: e.target.value })} className={inputClass} />
                </div>
                <div>
                  <label className="block font-semibold mb-1">Capacity</label>
                  <input type="number" min="1" required value={form.maximumCapacity} onChange={(e) => setForm({ ...form, maximumCapacity: e.target.value })} className={inputClass} />
                </div>
              </div>
            )}

            {dialog.type === 'issue' && (
              <>
                <p className="text-slate-500">Choose the packets to issue. They leave your available stock and stay in the packet history as Issued.</p>
                <PacketPicker bloodGroup={dialog.row.bloodGroup} selected={form.packetIds} onChange={(ids) => setForm({ ...form, packetIds: ids })} />
                <input placeholder="Reason, for example: issued to theatre for patient care" value={form.auditNotes}
                  onChange={(e) => setForm({ ...form, auditNotes: e.target.value })} maxLength={500} className={inputClass} />
              </>
            )}

            {formError && (
              <p className="p-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 text-rose-700 dark:text-rose-300">{formError}</p>
            )}

            <div className="flex gap-2 pt-2">
              <button type="button" onClick={() => setDialog(null)} className="w-1/2 py-2.5 border border-slate-200 dark:border-slate-700 rounded-xl font-semibold">Cancel</button>
              <button type="submit" disabled={saving} className="w-1/2 py-2.5 bg-red-600 text-white rounded-xl font-semibold disabled:opacity-50">
                {saving ? 'Saving...' : dialog.type === 'issue' ? `Issue ${form.packetIds.length || ''} packet(s)` : 'Save'}
              </button>
            </div>
          </form>
        </div>
      )}

      {history && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex justify-end">
          <div className="w-full max-w-md h-full bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 p-5 overflow-y-auto space-y-4 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 font-mono">{history.trackingNumber}</h3>
              <button type="button" onClick={() => setHistory(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div><span className="text-slate-400">Blood group</span><div className="font-semibold">{history.bloodGroup}</div></div>
              <div><span className="text-slate-400">Current hospital</span><div className="font-semibold">{history.hospitalName}</div></div>
              <div><span className="text-slate-400">Created by</span><div className="font-semibold">{history.createdByHospitalName}</div></div>
              <div><span className="text-slate-400">Created</span><div className="font-semibold">{fmtDate(history.createdAt)}</div></div>
              <div><span className="text-slate-400">Collected</span><div className="font-semibold">{fmtDate(history.collectionDate)}</div></div>
              <div><span className="text-slate-400">Expires</span><div className="font-semibold">{fmtDate(history.expiryDate)}</div></div>
              <div><span className="text-slate-400">Source</span><div className="font-semibold">{history.source}</div></div>
              <div><span className="text-slate-400">Status</span><div className="font-semibold">{history.status}</div></div>
            </div>
            <div className="relative pl-6 space-y-3 before:absolute before:left-2.5 before:top-1 before:bottom-1 before:w-0.5 before:bg-slate-200 dark:before:bg-slate-800">
              {(history.history || []).map((t) => (
                <div key={t.transactionId} className="relative">
                  <div className="absolute -left-6 top-0.5 w-5 h-5 rounded-full border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900" />
                  <div className="font-semibold text-slate-800 dark:text-slate-100">{t.transactionType.replaceAll('_', ' ')}</div>
                  <div className="text-slate-500">{t.notes}</div>
                  <div className="text-[10px] text-slate-400">{new Date(t.createdAt).toLocaleString()}</div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default InventoryManagementPage;
