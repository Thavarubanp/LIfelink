import React, { useState, useEffect } from 'react';
import { acceptanceApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { AgentStatusCard } from '../../components/workflow/AgentStatusCard';
import { Stethoscope, ShieldCheck, AlertCircle, ArrowRight, UserCheck, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';

export const DoctorDashboard = () => {
  const [acceptances, setAcceptances] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchData = async () => {
      try {
        const list = await acceptanceApi.getMyAcceptances().catch(() => []);
        setAcceptances(Array.isArray(list) ? list : []);
      } catch (err) {
        console.error('Failed to load doctor dashboard data:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, []);

  const pendingCount = acceptances.filter((a) => a.status === 'PENDING' || a.status === 'UNDER_REVIEW').length;
  const approvedCount = acceptances.filter((a) => a.status === 'ACCEPTED' || a.status === 'APPROVED').length;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Medical Doctor Portal</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Review AI donor health screening reports and finalize clinical donor selection.
          </p>
        </div>
        <Link
          to="/doctor/screenings"
          className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all self-start"
        >
          <Stethoscope className="w-4 h-4" />
          <span>Review Screening Queue</span>
        </Link>
      </div>

      {/* KPI Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Total Acceptances</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : acceptances.length}
          </div>
          <p className="text-[11px] text-blue-500 mt-1">Real-time database queue</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Pending Reviews</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : pendingCount}
          </div>
          <p className="text-[11px] text-amber-500 mt-1">Awaiting clinical sign-off</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Approved Screenings</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : approvedCount}
          </div>
          <p className="text-[11px] text-emerald-500 mt-1">Cleared for donation</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Doctor Queue Status</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">Active</div>
          <p className="text-[11px] text-emerald-500 mt-1">AI pre-evaluation active</p>
        </div>
      </div>

      <AgentStatusCard
        type="screening"
        title="Intelligent Screening Assessment Engine"
        description="AI Agent evaluating donor vitals, blood pressure, hemoglobin, and systemic medical history contraindications."
        metrics={[
          { label: 'Screening Reports', value: `${acceptances.length} Active` },
          { label: 'Evaluation Engine', value: 'ONLINE' },
          { label: 'Agent Status', value: 'ACTIVE' }
        ]}
      />
    </div>
  );
};

export default DoctorDashboard;
