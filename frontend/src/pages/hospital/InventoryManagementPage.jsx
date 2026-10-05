import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { AlertTriangle, ChevronDown, ChevronRight, Droplet, History, Loader2, PackagePlus, Pencil, Plus, Search, Send, Settings, SlidersHorizontal, X } from 'lucide-react';
import { inventoryApi, profileApi } from '../../api';
import { tableFilterClass } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { PacketPicker } from '../../components/inventory/PacketPicker';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage, isConflictError } from '../../utils/errorUtils';
import { newIdempotencyKey } from '../../session/sessionActivity';
import { formatDisplayDate } from '../../utils/dateUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const PACKET_STATUS_VARIANT = { Available: 'success', Reserved: 'info', Issued: 'default', Donated: 'primary', Expired: 'warning' };
const fmtDate = (value) => formatDisplayDate(value, '-');
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

const PACKET_PAGE = 10;
const STATUS_OPTIONS = [
  ['Available', 'Available'],
  ['Reserved', 'Reserved (offered, awaiting answer)'],
  ['Issued', 'Issued'],
  ['Donated', 'Donated'],
  ['Expired', 'Expired'],
  ['', 'All statuses']
];

/** Stock health of a blood group: the single "below threshold" rule comes from the backend (isLowStock: units < threshold). */
const HealthBadge = ({ row }) =>
  row.isLowStock ? <Badge variant="warning" size="sm">Below threshold</Badge>
    : row.isSurplus ? <Badge variant="info" size="sm">Surplus</Badge> : <Badge variant="success" size="sm">Healthy</Badge>;

const smallButton = 'inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed';

/**
 * Packet-level blood inventory for the signed-in hospital. Staff add collected blood as packets (each gets its own
 * tracking number), set thresholds, and issue blood by selecting the exact packets. Only the hospital that created a
 * packet can edit it, and only while it holds the packet and it is available.
 * One "Blood groups" card (Phase 4 / 5.2): each group row expands to show its packets with their own status filter;
 * `?group=A%2B` (from the dashboard) expands that group and scrolls to it.
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
  const [searchParams] = useSearchParams();
  const requestedGroup = searchParams.get('group');
  const [expanded, setExpanded] = useState(() => (requestedGroup ? [requestedGroup] : []));
  const [search, setSearch] = useState('');
  const [healthFilter, setHealthFilter] = useState('');
  const [packetLimit, setPacketLimit] = useState({});
  const rowRefs = useRef({});
  const scrolledTo = useRef(null);

  // ?group= from the dashboard: scroll to that group once the rows are on screen
  useEffect(() => {
    if (loading || !requestedGroup || scrolledTo.current === requestedGroup) return;
    const el = rowRefs.current[requestedGroup];
    if (el) {
      scrolledTo.current = requestedGroup;
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [loading, requestedGroup, inventory]);

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
  const openIssue = (row, packetIds = []) => open({ type: 'issue', row }, { packetIds, auditNotes: '' });
  // One idempotency key per opened form: a double submit or retry never adds the packets twice
  const openAddPackets = () => open({ type: 'addPackets' }, { bloodGroup: 'O+', collectionDate: localToday(), quantity: 1, idempotencyKey: newIdempotencyKey() });
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
        setFormError('Select at least one packet to remove from available stock.');
        return;
      }
      if (!form.auditNotes.trim()) {
        setFormError('A reason is required when removing blood from available stock.');
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
        addToast({ title: 'Blood removed from available stock', message: `${form.packetIds.length} ${dialog.row.bloodGroup} packet(s) removed from available stock.`, type: 'success' });
      } else if (dialog.type === 'addPackets') {
        const res = await inventoryApi.createPackets({
          bloodGroup: form.bloodGroup,
          collectionDate: form.collectionDate,
          quantity: Number(form.quantity) || 1
        }, { idempotencyKey: form.idempotencyKey });
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
      if (isConflictError(err)) {
        // Used or changed elsewhere at the same moment (or already submitted): show the current stock
        addToast({ title: 'Not saved', message: getApiErrorMessage(err), type: 'error' });
        setDialog(null);
        setReloadKey((k) => k + 1);
      } else {
        setFormError(getApiErrorMessage(err));
      }
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

  const toggle = (group) => setExpanded((list) => (list.includes(group) ? list.filter((g) => g !== group) : [...list, group]));
  const groupRow = (group) => inventory.find((i) => i.bloodGroup === group);

  const term = search.trim().toLowerCase();
  const visibleGroups = inventory
    .filter((row) => !healthFilter || (healthFilter === 'low' ? row.isLowStock : row.expiringSoonUnits > 0))
    .filter((row) => !term || row.bloodGroup.toLowerCase().includes(term) ||
      packets.some((p) => p.bloodGroup === row.bloodGroup && p.trackingNumber.toLowerCase().includes(term)));
  const packetsOf = (group) => packets.filter((p) => p.bloodGroup === group && (!term || group.toLowerCase().includes(term) || p.trackingNumber.toLowerCase().includes(term)));

  // The packets of one expanded group (with the status filter inside the expanded area)
  const renderPackets = (row) => {
    const list = packetsOf(row.bloodGroup);
    const limit = packetLimit[row.bloodGroup] || PACKET_PAGE;
    return (
      <div className="space-y-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-[11px] font-semibold text-slate-600 dark:text-slate-300">{row.bloodGroup} packets ({list.length})</span>
          <select aria-label={`${row.bloodGroup} packet status`} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className={tableFilterClass}>
            {STATUS_OPTIONS.map(([value, label]) => <option key={value || 'all'} value={value}>{label}</option>)}
          </select>
        </div>
        {list.length === 0 ? (
          <p className="py-3 text-center text-[11px] text-slate-400">No {row.bloodGroup} packets with this status.</p>
        ) : (
          <div className="divide-y divide-slate-100 dark:divide-slate-800 rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900">
            <div className="hidden md:grid grid-cols-[1.2fr_0.9fr_1fr_1.3fr_0.8fr_1.4fr] gap-2 px-3 py-2 text-[10px] font-bold uppercase tracking-wide text-slate-400">
              <span>Tracking no.</span><span>Collected</span><span>Expires</span><span>Created by</span><span>Status</span><span>Actions</span>
            </div>
            {list.slice(0, limit).map((p) => (
              <div key={p.packetId} className="grid grid-cols-2 md:grid-cols-[1.2fr_0.9fr_1fr_1.3fr_0.8fr_1.4fr] gap-x-2 gap-y-1 px-3 py-2 text-[11px] items-center">
                <span className="font-mono font-semibold text-slate-700 dark:text-slate-200">{p.trackingNumber}</span>
                <span className="text-slate-600 dark:text-slate-300"><span className="md:hidden text-slate-400">Collected </span>{fmtDate(p.collectionDate)}</span>
                <span className="flex items-center gap-1 text-slate-600 dark:text-slate-300">
                  <span className="md:hidden text-slate-400">Expires </span>{fmtDate(p.expiryDate)} {p.isExpiringSoon && <Badge variant="warning" size="sm">Soon</Badge>}
                </span>
                <span className="text-slate-600 dark:text-slate-300">
                  {p.createdByHospitalId === p.hospitalId ? 'Your hospital' : p.createdByHospitalName}
                  <span className="block text-[10px] text-slate-400">{fmtDate(p.createdAt)} - {p.source}</span>
                </span>
                <span><Badge variant={PACKET_STATUS_VARIANT[p.status] || 'default'} size="sm">{p.status}</Badge></span>
                <span className="col-span-2 md:col-span-1 flex flex-wrap gap-1.5">
                  {p.canEdit && (
                    <button type="button" onClick={() => openEditPacket(p)} className={smallButton}><Pencil className="w-3 h-3" /> Edit</button>
                  )}
                  {p.status === 'Available' && (
                    <button type="button" onClick={() => openIssue(row, [p.packetId])} className={smallButton}><Send className="w-3 h-3" /> Remove</button>
                  )}
                  <button type="button" onClick={() => openHistory(p)} className={smallButton}><History className="w-3 h-3" /> History</button>
                </span>
              </div>
            ))}
          </div>
        )}
        {list.length > limit && (
          <button type="button" onClick={() => setPacketLimit((m) => ({ ...m, [row.bloodGroup]: limit + PACKET_PAGE }))}
            className="text-[11px] font-semibold text-red-600 hover:underline">
            Show {Math.min(PACKET_PAGE, list.length - limit)} more of {list.length - limit}
          </button>
        )}
      </div>
    );
  };

  if (loading) {
    return <div className="py-20 flex justify-center"><Loader2 className="w-8 h-8 text-red-600 animate-spin" /></div>;
  }

  const inputClass = 'w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl';
  const dialogTitle = {
    category: 'Add blood group category',
    thresholds: `${dialog?.row?.bloodGroup}: thresholds`,
    issue: `Remove ${dialog?.row?.bloodGroup} blood from available stock`,
    addPackets: 'Add blood packets',
    editPacket: `Edit packet ${dialog?.packet?.trackingNumber}`
  }[dialog?.type];

  return (
    // Bottom padding keeps the floating assistant button clear of the last card's pagination
    <div className="space-y-6 pb-24">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Blood Inventory</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Every unit is a 440 ml packet with its own tracking number. Add collected blood as packets and remove blood from available stock by choosing exact packets.
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

      {inventory.some((i) => i.isLowStock || i.expiringSoonUnits > 0) && (
        <div className="p-3 rounded-xl border border-amber-200 bg-amber-50 dark:bg-amber-950/30 dark:border-amber-900 text-xs text-amber-800 dark:text-amber-300 flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            Some blood groups are below threshold or have packets expiring soon. The inventory analysis alerts the hospitals holding that exact blood group; see your notifications or run the analysis from the dashboard.
          </span>
        </div>
      )}

      {/* One card: blood groups, each expanding to its packets */}
      <section className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm">
        <div className="p-4 border-b border-slate-100 dark:border-slate-800 space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
              <Droplet className="w-4 h-4 text-red-600" /> Blood groups
              <span className="text-[11px] font-normal text-slate-400">({inventory.length})</span>
            </h2>
            <div className="flex items-center gap-2">
              <HeaderAction icon={Plus} label="Add blood group" onClick={openCategory} />
              <HeaderAction icon={PackagePlus} label="Add packets" onClick={openAddPackets} primary />
            </div>
          </div>
          <div className="flex flex-col sm:flex-row gap-2">
            <div className="relative flex-1">
              <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search blood group or tracking number..." aria-label="Search"
                className="w-full pl-9 pr-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs text-slate-800 dark:text-slate-100 focus:outline-none focus:border-red-500" />
            </div>
            <select aria-label="Stock filter" value={healthFilter} onChange={(e) => setHealthFilter(e.target.value)} className={tableFilterClass}>
              <option value="">All blood groups</option>
              <option value="low">Below threshold</option>
              <option value="expiring">Packets expiring soon</option>
            </select>
          </div>
        </div>

        {visibleGroups.length === 0 ? (
          <p className="p-8 text-center text-xs text-slate-500">
            {inventory.length === 0 ? 'No blood groups yet. Add packets or a blood group to start.' : 'No blood group matches these filters.'}
          </p>
        ) : (
          <div className="divide-y divide-slate-100 dark:divide-slate-800">
            <div className="hidden lg:grid grid-cols-[2rem_0.8fr_1fr_0.8fr_0.8fr_1.1fr_1fr_1.1fr_1.6fr] gap-2 px-4 py-2 text-[10px] font-bold uppercase tracking-wide text-slate-400">
              <span /><span>Group</span><span>Available</span><span>Threshold</span><span>Capacity</span><span>Expiring soon</span><span>Next expiry</span><span>Health</span><span>Actions</span>
            </div>
            {visibleGroups.map((row) => {
              const isOpen = expanded.includes(row.bloodGroup);
              return (
                <div key={row.inventoryId} ref={(el) => { rowRefs.current[row.bloodGroup] = el; }} className="scroll-mt-20">
                  <div className={`grid grid-cols-[2rem_1fr_auto] lg:grid-cols-[2rem_0.8fr_1fr_0.8fr_0.8fr_1.1fr_1fr_1.1fr_1.6fr] gap-2 px-4 py-3 items-center text-xs ${isOpen ? 'bg-red-50/40 dark:bg-red-950/10' : ''}`}>
                    <button type="button" onClick={() => toggle(row.bloodGroup)} aria-expanded={isOpen} aria-label={`${isOpen ? 'Hide' : 'Show'} ${row.bloodGroup} packets`}
                      className="w-7 h-7 rounded-lg flex items-center justify-center border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                      {isOpen ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                    </button>
                    <div className="flex items-center gap-2 flex-wrap">
                      <Badge variant="blood" size="sm">{row.bloodGroup}</Badge>
                      <span className="lg:hidden font-bold text-slate-900 dark:text-slate-100">{row.unitsAvailable} unit(s)</span>
                      <span className="lg:hidden"><HealthBadge row={row} /></span>
                    </div>
                    <span className="hidden lg:block font-bold text-slate-900 dark:text-slate-100">{row.unitsAvailable} packet(s)</span>
                    <span className="hidden lg:block text-slate-500">{row.minimumThreshold}</span>
                    <span className="hidden lg:block text-slate-500">{row.maximumCapacity}</span>
                    <span className="hidden lg:block">
                      {row.expiringSoonUnits > 0 ? <Badge variant="warning" size="sm">{row.expiringSoonUnits} within {row.expiryAlertDays}d</Badge> : <span className="text-slate-400">-</span>}
                    </span>
                    <span className="hidden lg:block text-slate-500">{fmtDate(row.nextExpiryDate)}</span>
                    <span className="hidden lg:block"><HealthBadge row={row} /></span>
                    <div className="col-span-3 lg:col-span-1 flex flex-wrap gap-1.5 pl-9 lg:pl-0">
                      <span className="lg:hidden w-full text-[11px] text-slate-500">
                        Threshold {row.minimumThreshold} - capacity {row.maximumCapacity}
                        {row.expiringSoonUnits > 0 ? ` - ${row.expiringSoonUnits} expiring within ${row.expiryAlertDays}d` : ''}
                      </span>
                      <button type="button" onClick={() => openThresholds(row)} className={smallButton}><SlidersHorizontal className="w-3 h-3" /> Thresholds</button>
                      <button type="button" disabled={row.unitsAvailable === 0} onClick={() => openIssue(row)} className={smallButton}><Send className="w-3 h-3" /> Remove</button>
                    </div>
                  </div>
                  {isOpen && <div className="px-4 pb-4 pt-1 bg-slate-50/60 dark:bg-slate-950/30">{renderPackets(row)}</div>}
                </div>
              );
            })}
          </div>
        )}
      </section>

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
                <p className="text-slate-500">Choose the packets to remove from available stock. They remain in packet history with status Issued.</p>
                <PacketPicker bloodGroup={(groupRow(dialog.row.bloodGroup) || dialog.row).bloodGroup} selected={form.packetIds} onChange={(ids) => setForm({ ...form, packetIds: ids })} />
                <input placeholder="Reason for removing these packets from available stock" value={form.auditNotes}
                  onChange={(e) => setForm({ ...form, auditNotes: e.target.value })} maxLength={500} className={inputClass} />
              </>
            )}

            {formError && (
              <p className="p-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 text-rose-700 dark:text-rose-300">{formError}</p>
            )}

            <div className="flex gap-2 pt-2">
              <button type="button" onClick={() => setDialog(null)} className="w-1/2 py-2.5 border border-slate-200 dark:border-slate-700 rounded-xl font-semibold">Cancel</button>
              <button type="submit" disabled={saving} className="w-1/2 py-2.5 bg-red-600 text-white rounded-xl font-semibold disabled:opacity-50">
                {saving ? 'Saving...' : dialog.type === 'issue' ? `Remove ${form.packetIds.length || ''} packet(s)` : 'Save'}
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
                  <div className="text-[10px] text-slate-400">{formatDisplayDate(t.createdAt)}</div>
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
