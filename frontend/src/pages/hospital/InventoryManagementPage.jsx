import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, History, Loader2, Package, Plus, Settings, SlidersHorizontal, X } from 'lucide-react';
import { inventoryApi, profileApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const fmtDate = (value) => (value ? new Date(value).toLocaleDateString() : '-');
const unwrap = (res) => res?.data || (Array.isArray(res) ? res : []);

/**
 * Packet-level blood inventory for the signed-in hospital. Stock comes only from recorded donations and completed
 * transfers; staff set thresholds and issue blood (earliest-expiring packets first, with a reason).
 */
export const InventoryManagementPage = () => {
  const { addToast } = useNotification();
  const [hospital, setHospital] = useState(null);
  const [inventory, setInventory] = useState([]);
  const [packets, setPackets] = useState([]);
  const [statusFilter, setStatusFilter] = useState('Available');
  const [loading, setLoading] = useState(true);
  const [dialog, setDialog] = useState(null); // { type: 'create' } | { type: 'edit', row }
  const [form, setForm] = useState({});
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

  const openCreate = () => {
    setForm({ bloodGroup: BLOOD_GROUPS.find((g) => !inventory.some((i) => i.bloodGroup === g)) || 'O+', minimumThreshold: 5, maximumCapacity: 100 });
    setDialog({ type: 'create' });
  };

  const openEdit = (row) => {
    setForm({ minimumThreshold: row.minimumThreshold, maximumCapacity: row.maximumCapacity, issueUnits: 0, auditNotes: '' });
    setDialog({ type: 'edit', row });
  };

  const save = async (e) => {
    e.preventDefault();
    setSaving(true);
    try {
      if (dialog.type === 'create') {
        await inventoryApi.createInventory({
          bloodGroup: form.bloodGroup,
          minimumThreshold: Number(form.minimumThreshold),
          maximumCapacity: Number(form.maximumCapacity)
        });
        addToast({ title: 'Category added', message: `${form.bloodGroup} thresholds saved. Stock arrives from donations and transfers.`, type: 'success' });
      } else {
        const issue = Number(form.issueUnits) || 0;
        await inventoryApi.updateInventory(dialog.row.inventoryId, {
          minimumThreshold: Number(form.minimumThreshold),
          maximumCapacity: Number(form.maximumCapacity),
          unitsAvailable: issue > 0 ? dialog.row.unitsAvailable - issue : null,
          auditNotes: form.auditNotes
        });
        addToast({ title: 'Inventory updated', message: issue > 0 ? `${issue} packet(s) issued (earliest expiry first).` : 'Thresholds saved.', type: 'success' });
      }
      setDialog(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Update failed', message: getApiErrorMessage(err), type: 'error' });
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
    { header: 'Blood Group', accessor: 'bloodGroup', cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge> },
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
        ? <Badge variant="warning">Below threshold</Badge>
        : row.isSurplus ? <Badge variant="info">Surplus</Badge> : <Badge variant="success">Healthy</Badge>
    },
    {
      header: 'Action',
      accessor: 'inventoryId',
      sortable: false,
      cell: (row) => (
        <button type="button" onClick={() => openEdit(row)} className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
          <SlidersHorizontal className="w-3 h-3" /> Thresholds / issue
        </button>
      )
    }
  ];

  const packetColumns = [
    { header: 'Packet', accessor: 'packetCode', cell: (row) => <span className="font-mono text-slate-600 dark:text-slate-300">{row.packetCode}</span> },
    { header: 'Group', accessor: 'bloodGroup', cell: (row) => <Badge variant="blood" size="sm">{row.bloodGroup}</Badge> },
    { header: 'Volume', accessor: 'volumeMl', cell: (row) => <span>{row.volumeMl} ml</span> },
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
    { header: 'Source', accessor: 'source', cell: (row) => <span className="text-slate-500">{row.source}</span> },
    { header: 'Status', accessor: 'status', cell: (row) => <Badge variant={row.status === 'Available' ? 'success' : 'default'} size="sm">{row.status}</Badge> },
    {
      header: 'History',
      accessor: 'packetId',
      sortable: false,
      cell: (row) => (
        <button type="button" onClick={() => openHistory(row)} className="inline-flex items-center gap-1 text-[11px] font-semibold text-red-600 hover:underline">
          <History className="w-3 h-3" /> View
        </button>
      )
    }
  ];

  if (loading) {
    return <div className="py-20 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Blood Inventory</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Every unit is tracked as a 440 ml packet. Stock comes only from recorded donations and completed hospital transfers.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Link to="/hospital/transfers" className="inline-flex items-center gap-2 px-4 py-2 border border-slate-200 dark:border-slate-700 text-xs font-semibold rounded-xl hover:bg-slate-50 dark:hover:bg-slate-800">
            Inter-hospital transfers
          </Link>
          <button onClick={openCreate} className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md">
            <Plus className="w-4 h-4" /> Add blood group
          </button>
        </div>
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

      <DataTable columns={inventoryColumns} data={inventory} searchPlaceholder="Search blood group..." emptyMessage="No blood group categories yet. Add one to set its threshold." />

      <div className="space-y-3">
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2"><Package className="w-4 h-4 text-red-600" /> Blood packets</h2>
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs">
            <option value="Available">Available</option>
            <option value="Issued">Issued</option>
            <option value="Expired">Expired</option>
            <option value="">All</option>
          </select>
        </div>
        <DataTable columns={packetColumns} data={packets} searchPlaceholder="Search packet code or group..." emptyMessage="No packets with this status." />
      </div>

      {dialog && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <form onSubmit={save} className="max-w-md w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl space-y-3 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                {dialog.type === 'create' ? 'Add blood group category' : `${dialog.row.bloodGroup}: thresholds and issuing`}
              </h3>
              <button type="button" onClick={() => setDialog(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>
            {dialog.type === 'create' && (
              <div>
                <label className="block font-semibold mb-1">Blood group</label>
                <select value={form.bloodGroup} onChange={(e) => setForm({ ...form, bloodGroup: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl">
                  {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
                </select>
              </div>
            )}
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block font-semibold mb-1">Minimum threshold</label>
                <input type="number" min="0" required value={form.minimumThreshold} onChange={(e) => setForm({ ...form, minimumThreshold: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
              </div>
              <div>
                <label className="block font-semibold mb-1">Capacity</label>
                <input type="number" min="1" required value={form.maximumCapacity} onChange={(e) => setForm({ ...form, maximumCapacity: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
              </div>
            </div>
            {dialog.type === 'edit' && (
              <div className="p-3 rounded-xl border border-slate-200 dark:border-slate-700 space-y-2">
                <p className="font-semibold">Issue blood from stock (optional)</p>
                <p className="text-slate-500">The earliest-expiring packets are issued first. {dialog.row.unitsAvailable} available.</p>
                <input type="number" min="0" max={dialog.row.unitsAvailable} value={form.issueUnits} onChange={(e) => setForm({ ...form, issueUnits: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
                <input placeholder="Reason, for example: issued to theatre for patient care" required={Number(form.issueUnits) > 0} value={form.auditNotes} onChange={(e) => setForm({ ...form, auditNotes: e.target.value })} className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl" />
              </div>
            )}
            <div className="flex gap-2 pt-2">
              <button type="button" onClick={() => setDialog(null)} className="w-1/2 py-2.5 border rounded-xl font-semibold">Cancel</button>
              <button type="submit" disabled={saving} className="w-1/2 py-2.5 bg-red-600 text-white rounded-xl font-semibold disabled:opacity-50">{saving ? 'Saving...' : 'Save'}</button>
            </div>
          </form>
        </div>
      )}

      {history && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex justify-end">
          <div className="w-full max-w-md h-full bg-white dark:bg-slate-900 border-l border-slate-200 dark:border-slate-800 p-5 overflow-y-auto space-y-4 text-xs">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 font-mono">{history.packetCode}</h3>
              <button type="button" onClick={() => setHistory(null)} className="text-slate-400"><X className="w-4 h-4" /></button>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div><span className="text-slate-400">Blood group</span><div className="font-semibold">{history.bloodGroup}</div></div>
              <div><span className="text-slate-400">Current hospital</span><div className="font-semibold">{history.hospitalName}</div></div>
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
