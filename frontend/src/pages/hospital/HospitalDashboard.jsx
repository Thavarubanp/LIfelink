import React, { useState, useEffect } from 'react';
import { inventoryApi, emergencyApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { InventoryAnalysisStatus } from '../../components/workflow/InventoryAnalysisStatus';
import { Building2, Droplet, Zap, ArrowLeftRight, AlertCircle, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';

export const HospitalDashboard = () => {
  const [inventory, setInventory] = useState([]);
  const [emergencies, setEmergencies] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const [invRes, emRes] = await Promise.all([
          inventoryApi.getAllInventory().catch(() => []),
          emergencyApi.getCriticalEmergencyRequests().catch(() => [])
        ]);
        const invList = invRes.data || (Array.isArray(invRes) ? invRes : []);
        const emList = emRes.data || (Array.isArray(emRes) ? emRes : []);
        setInventory(invList);
        setEmergencies(emList);
      } catch (err) {
        console.error('Failed to load hospital dashboard:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, []);

  const totalStockUnits = inventory.reduce((acc, curr) => acc + (curr.unitsAvailable || 0), 0);
  const lowStockCount = inventory.filter((item) => (item.unitsAvailable || 0) <= (item.minimumThreshold || 5)).length;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Hospital Operations Hub</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Real-time blood bank inventory stock, critical broadcasts, and inter-hospital transfers.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link
            to="/hospital/emergency"
            className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all"
          >
            <Zap className="w-4 h-4" />
            <span>Broadcast Emergency</span>
          </Link>
          <Link
            to="/hospital/inventory"
            className="inline-flex items-center gap-2 px-4 py-2 bg-slate-900 dark:bg-slate-800 hover:bg-slate-800 text-white font-semibold text-xs rounded-xl shadow-md transition-all"
          >
            <Droplet className="w-4 h-4" />
            <span>Manage Inventory</span>
          </Link>
        </div>
      </div>

      {/* KPI Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Blood Bank Stock</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : `${totalStockUnits} Units`}
          </div>
          <p className="text-[11px] text-emerald-500 mt-1">Across {inventory.length} Blood Categories</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Critical Emergencies</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : emergencies.length}
          </div>
          <p className="text-[11px] text-rose-500 mt-1">Active priority broadcasts</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Low Stock Alerts</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : lowStockCount}
          </div>
          <p className="text-[11px] text-amber-500 mt-1">Below minimum threshold</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Inventory Status</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {inventory.length > 0 ? 'Active' : 'No Data'}
          </div>
          <p className="text-[11px] text-blue-500 mt-1">Live database synchronized</p>
        </div>
      </div>

      <InventoryAnalysisStatus lowStockCount={lowStockCount} surplusCount={0} />

      {/* Inventory Stock Levels Preview */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Live Stock Levels by Blood Group</h3>
          <Link to="/hospital/inventory" className="text-xs font-semibold text-red-600 hover:underline">
            Full Inventory Dashboard →
          </Link>
        </div>

        {loading ? (
          <div className="p-8 text-center text-slate-400">
            <Loader2 className="w-6 h-6 animate-spin mx-auto mb-2 text-red-500" />
            <p className="text-xs font-semibold">Loading live stock levels...</p>
          </div>
        ) : inventory.length > 0 ? (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {inventory.map((item) => {
              const units = item.unitsAvailable || 0;
              const max = item.maximumCapacity || 100;
              const min = item.minimumThreshold || 5;
              const isLow = units <= min;

              return (
                <div
                  key={item.inventoryId || item.bloodGroup}
                  className="p-3.5 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200 dark:border-slate-700/60"
                >
                  <div className="flex items-center justify-between">
                    <Badge variant="blood">{item.bloodGroup}</Badge>
                    <Badge variant={isLow ? 'warning' : 'success'}>
                      {isLow ? 'Low Stock' : 'Optimal'}
                    </Badge>
                  </div>
                  <div className="mt-3">
                    <div className="flex items-baseline justify-between">
                      <span className="text-base font-bold text-slate-900 dark:text-slate-100">{units}</span>
                      <span className="text-[10px] text-slate-400">/ {max} Units</span>
                    </div>
                    <div className="w-full h-1.5 bg-slate-200 dark:bg-slate-700 rounded-full mt-1.5 overflow-hidden">
                      <div
                        className={`h-full transition-all ${isLow ? 'bg-amber-500' : 'bg-emerald-500'}`}
                        style={{ width: `${Math.min(100, (units / max) * 100)}%` }}
                      />
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div className="p-8 text-center text-slate-400 text-xs">
            No blood inventory records found in the database. Use "Manage Inventory" to add records.
          </div>
        )}
      </div>
    </div>
  );
};

export default HospitalDashboard;
