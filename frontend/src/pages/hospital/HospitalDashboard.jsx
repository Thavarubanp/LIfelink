import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, ChevronRight, Droplet, Loader2, X, Zap } from 'lucide-react';
import { inventoryApi, emergencyApi, profileApi, activityApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { InventoryAnalysisStatus } from '../../components/workflow/InventoryAnalysisStatus';
import ActivityLogList from '../../components/activity/ActivityLogList';
import { formatDisplayDate } from '../../utils/dateUtils';

const unwrap = (res) => res?.data || (Array.isArray(res) ? res : []);
const groupLink = (group) => `/hospital/inventory?group=${encodeURIComponent(group)}`;
const fmtSriLanka = (value) => formatDisplayDate(value);

const cardClass = 'bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm';
const clickableCard = `${cardClass} text-left w-full hover:border-red-300 dark:hover:border-red-800 hover:shadow-md transition-all focus:outline-none focus:ring-2 focus:ring-red-500/40`;

/** A list in a dialog (critical emergencies or low-stock groups). */
const DetailDialog = ({ title, onClose, children, footer }) => (
  <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4" role="dialog" aria-modal="true" aria-label={title}>
    <div className="w-full max-w-lg max-h-[85vh] overflow-y-auto rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-5 shadow-xl space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">{title}</h2>
        <button type="button" onClick={onClose} aria-label="Close" className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"><X className="w-4 h-4" /></button>
      </div>
      {children}
      {footer}
    </div>
  </div>
);

/**
 * Hospital dashboard (Phase 4 / 5.3): unit cards open the Inventory page on that group; the Critical emergencies and
 * Low stock cards open their details; the inventory analysis panel (5.5); the hospital's activity log at the end.
 * "Low" uses the backend's single rule (isLowStock: units below the threshold).
 */
export const HospitalDashboard = () => {
  const [inventory, setInventory] = useState([]);
  const [emergencies, setEmergencies] = useState([]);
  const [loading, setLoading] = useState(true);
  const [dialog, setDialog] = useState(null); // 'emergencies' | 'lowStock'
  const [reloadKey, setReloadKey] = useState(0);
  const loadActivity = useCallback((params) => activityApi.getMyActivity(params), []);

  useEffect(() => {
    let active = true;
    profileApi.getMyProfile()
      .then((me) => Promise.all([
        inventoryApi.getHospitalInventory(me.id).catch(() => []),
        emergencyApi.getCriticalEmergencyRequests().catch(() => [])
      ]))
      .then(([invRes, emRes]) => {
        if (!active) return;
        setInventory(unwrap(invRes));
        setEmergencies(unwrap(emRes));
      })
      .catch((err) => console.error('Failed to load hospital dashboard:', err))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [reloadKey]);

  const totalStockUnits = inventory.reduce((acc, curr) => acc + (curr.unitsAvailable || 0), 0);
  const lowStock = inventory.filter((item) => item.isLowStock);
  const expiringSoonUnits = inventory.reduce((sum, item) => sum + (item.expiringSoonUnits || 0), 0);
  const activeEmergencies = emergencies.filter((item) => item.status === 'Pending' || item.status === 'Approved');

  return (
    <div className="space-y-6 pb-20">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Hospital Operations Hub</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Real-time blood bank inventory stock, critical broadcasts, and inter-hospital transfers.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link to="/hospital/emergency" className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all">
            <Zap className="w-4 h-4" />
            <span>Broadcast Emergency</span>
          </Link>
          <Link to="/hospital/inventory" className="inline-flex items-center gap-2 px-4 py-2 bg-slate-900 dark:bg-slate-800 hover:bg-slate-800 text-white font-semibold text-xs rounded-xl shadow-md transition-all">
            <Droplet className="w-4 h-4" />
            <span>Manage Inventory</span>
          </Link>
        </div>
      </div>

      {/* KPI Stats: emergencies and low stock open their details */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <div className={cardClass}>
          <span className="text-xs font-semibold text-slate-500">Blood Bank Stock</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : `${totalStockUnits} Units`}
          </div>
          <p className="text-[11px] text-emerald-500 mt-1">Across {inventory.length} Blood Categories</p>
        </div>
        <button type="button" className={clickableCard} onClick={() => setDialog('emergencies')} aria-label="Show critical emergencies">
          <span className="text-xs font-semibold text-slate-500 flex items-center justify-between">Critical Emergencies <ChevronRight className="w-3.5 h-3.5" /></span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : activeEmergencies.length}
          </div>
          <p className="text-[11px] text-rose-500 mt-1">Pending or approved critical broadcasts - view details</p>
        </button>
        <button type="button" className={clickableCard} onClick={() => setDialog('lowStock')} aria-label="Show low stock blood groups">
          <span className="text-xs font-semibold text-slate-500 flex items-center justify-between">My Hospital Low Stock Alerts <ChevronRight className="w-3.5 h-3.5" /></span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : lowStock.length}
          </div>
          <p className="text-[11px] text-amber-500 mt-1">Below minimum threshold - view details</p>
        </button>
        <Link to="/hospital/inventory" className={clickableCard} aria-label="Open inventory to review expiring blood packets">
          <span className="text-xs font-semibold text-slate-500 flex items-center justify-between">Expiring Soon <ChevronRight className="w-3.5 h-3.5" /></span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : `${expiringSoonUnits} Units`}
          </div>
          <p className="text-[11px] text-blue-500 mt-1">Available packets inside my hospital's expiry alert window</p>
        </Link>
      </div>

      <InventoryAnalysisStatus onRunComplete={() => setReloadKey((k) => k + 1)} />

      {/* Stock levels: each card opens the Inventory page on that blood group */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Live Stock Levels by Blood Group</h3>
          <Link to="/hospital/inventory" className="text-xs font-semibold text-red-600 hover:underline">Full Inventory Dashboard →</Link>
        </div>

        {loading ? (
          <div className="p-8 text-center text-slate-400">
            <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2 text-red-500" />
            <p className="text-xs font-semibold">Loading live stock levels...</p>
          </div>
        ) : inventory.length > 0 ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {inventory.map((item) => {
              const units = item.unitsAvailable || 0;
              const max = item.maximumCapacity || 100;
              const isLow = item.isLowStock;
              return (
                <Link
                  key={item.inventoryId || item.bloodGroup}
                  to={groupLink(item.bloodGroup)}
                  title={`Open ${item.bloodGroup} in Inventory`}
                  className="block p-3.5 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200 dark:border-slate-700/60 hover:border-red-300 dark:hover:border-red-800 hover:shadow-md transition-all"
                >
                  <div className="flex items-center justify-between">
                    <Badge variant="blood">{item.bloodGroup}</Badge>
                    <Badge variant={isLow ? 'warning' : 'success'}>{isLow ? 'Low Stock' : 'Optimal'}</Badge>
                  </div>
                  <div className="mt-3">
                    <div className="flex items-baseline justify-between">
                      <span className="text-base font-bold text-slate-900 dark:text-slate-100">{units}</span>
                      <span className="text-[10px] text-slate-400">/ {max} Units</span>
                    </div>
                    <div className="w-full h-1.5 bg-slate-200 dark:bg-slate-700 rounded-full mt-1.5 overflow-hidden">
                      <div className={`h-full transition-all ${isLow ? 'bg-amber-500' : 'bg-emerald-500'}`} style={{ width: `${Math.min(100, (units / max) * 100)}%` }} />
                    </div>
                  </div>
                </Link>
              );
            })}
          </div>
        ) : (
          <div className="p-8 text-center text-slate-400 text-xs">
            No blood inventory records found in the database. Use "Manage Inventory" to add records.
          </div>
        )}
      </div>

      <ActivityLogList load={loadActivity} title="Hospital activity log" description="What your staff and doctors did, and administrator actions on your hospital." />

      {dialog === 'emergencies' && (
        <DetailDialog
          title={`Active critical emergencies (${activeEmergencies.length})`}
          onClose={() => setDialog(null)}
          footer={<Link to="/hospital/emergency" className="inline-flex text-xs font-semibold text-red-600 hover:underline">Open the Emergency Center →</Link>}
        >
          {activeEmergencies.length === 0 ? (
            <p className="text-xs text-slate-500">There are no active critical emergencies.</p>
          ) : (
            <ul className="divide-y divide-slate-100 dark:divide-slate-800">
              {activeEmergencies.map((e) => (
                <li key={e.emergencyRequestId} className="py-2.5 text-xs space-y-0.5">
                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="font-semibold text-slate-900 dark:text-slate-100">{e.hospitalName}</span>
                    <Badge variant="blood" size="sm">{e.bloodGroup}</Badge>
                    <span className="text-slate-600 dark:text-slate-300">{e.unitsRequired} unit(s)</span>
                    <Badge variant="danger" size="sm">{e.priority}</Badge>
                  </div>
                  {e.reason && <p className="text-slate-600 dark:text-slate-300">{e.reason}</p>}
                  <p className="text-[11px] text-slate-400">{fmtSriLanka(e.createdAt)} - {e.status}</p>
                </li>
              ))}
            </ul>
          )}
        </DetailDialog>
      )}

      {dialog === 'lowStock' && (
        <DetailDialog
          title={`My hospital low stock (${lowStock.length})`}
          onClose={() => setDialog(null)}
          footer={<Link to="/hospital/inventory" className="inline-flex text-xs font-semibold text-red-600 hover:underline">Open Inventory →</Link>}
        >
          <p className="text-[11px] text-slate-500 dark:text-slate-400">Only my hospital's inventory is shown. A blood group is low when its available units are below its minimum threshold.</p>
          {lowStock.length === 0 ? (
            <p className="text-xs text-slate-500">No blood group is below its threshold.</p>
          ) : (
            <ul className="divide-y divide-slate-100 dark:divide-slate-800">
              {lowStock.map((item) => (
                <li key={item.inventoryId} className="py-2.5 flex items-center justify-between gap-2 text-xs">
                  <span className="flex items-center gap-2">
                    <AlertTriangle className="w-4 h-4 text-amber-500" />
                    <Badge variant="blood" size="sm">{item.bloodGroup}</Badge>
                    <span className="text-slate-700 dark:text-slate-200">{item.unitsAvailable} unit(s) available</span>
                    <span className="text-slate-400">threshold {item.minimumThreshold}</span>
                  </span>
                  <Link to={groupLink(item.bloodGroup)} className="font-semibold text-red-600 hover:underline shrink-0">View</Link>
                </li>
              ))}
            </ul>
          )}
        </DetailDialog>
      )}
    </div>
  );
};

export default HospitalDashboard;
