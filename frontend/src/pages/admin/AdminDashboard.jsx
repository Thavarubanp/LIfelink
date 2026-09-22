import React, { useState, useEffect } from 'react';
import { adminApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { ShieldCheck, Building2, Users, AlertTriangle, FileText, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';

export const AdminDashboard = () => {
  const [stats, setStats] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchStats = async () => {
      try {
        const res = await adminApi.getDashboardStats();
        setStats(res.data);
      } catch (err) {
        console.error('Failed to fetch admin stats:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchStats();
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">System Governance & Admin Portal</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Platform metrics, hospital verifications, open complaints, and suspension appeals.
          </p>
        </div>
      </div>

      {/* KPI Overview Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500">Total Registered Users</span>
            <Users className="w-5 h-5 text-blue-500" />
          </div>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : stats?.totalUsers ?? 0}
          </div>
          <p className="text-[11px] text-slate-400 mt-1">Registered LifeLink accounts</p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500">Registered Hospitals</span>
            <Building2 className="w-5 h-5 text-emerald-500" />
          </div>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : stats?.totalHospitals ?? 0}
          </div>
          <p className="text-[11px] text-amber-500 mt-1">
            {stats?.pendingHospitalApprovals ?? 0} Pending Approval
          </p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500">Pending Complaints</span>
            <AlertTriangle className="w-5 h-5 text-amber-500" />
          </div>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : stats?.pendingComplaints ?? 0}
          </div>
          <p className="text-[11px] text-rose-500 mt-1">Under Active Investigation</p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500">Pending Appeals</span>
            <FileText className="w-5 h-5 text-purple-500" />
          </div>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : stats?.pendingAppeals ?? 0}
          </div>
          <p className="text-[11px] text-purple-500 mt-1">Reinstatement Reviews</p>
        </div>
      </div>

      {/* Quick Governance Links Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Link
          to="/admin/hospitals/pending"
          className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
        >
          <div>
            <Badge variant="warning">Action Required</Badge>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Hospital Registration Approvals</h3>
            <p className="text-xs text-slate-500 mt-1">Review newly registered hospitals seeking verification status.</p>
          </div>
          <span className="text-xs font-semibold text-red-600 mt-4">Review Approvals →</span>
        </Link>

        <Link
          to="/admin/complaints"
          className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
        >
          <div>
            <Badge variant="danger">Investigation Queue</Badge>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Platform Complaints Hub</h3>
            <p className="text-xs text-slate-500 mt-1">Inspect user reports, request hospital evidence, and resolve complaints.</p>
          </div>
          <span className="text-xs font-semibold text-red-600 mt-4">Manage Complaints →</span>
        </Link>

        <Link
          to="/admin/appeals"
          className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
        >
          <div>
            <Badge variant="info">Reinstatement Queue</Badge>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Suspension Appeals Evaluation</h3>
            <p className="text-xs text-slate-500 mt-1">Evaluate appeals from suspended accounts and restore access.</p>
          </div>
          <span className="text-xs font-semibold text-red-600 mt-4">Review Appeals →</span>
        </Link>
      </div>
    </div>
  );
};

export default AdminDashboard;
