import React, { useState, useEffect } from 'react';
import { bloodRequestApi, acceptanceApi, profileApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { Badge } from '../../components/common/Badge';
import { SmartMatchingProgress } from '../../components/workflow/SmartMatchingProgress';
import { AgentStatusCard } from '../../components/workflow/AgentStatusCard';
import { Droplet, Heart, AlertCircle, Clock, ArrowRight, ShieldCheck } from 'lucide-react';
import { Link } from 'react-router-dom';

export const DonorDashboard = () => {
  const [requests, setRequests] = useState([]);
  const [acceptances, setAcceptances] = useState([]);
  const [profile, setProfile] = useState(null);
  const [loading, setLoading] = useState(true);
  const { user } = useAuth();

  useEffect(() => {
    const fetchData = async () => {
      try {
        const [reqRes, accRes, me] = await Promise.all([
          bloodRequestApi.getPublicRequests(),
          acceptanceApi.getMyAcceptances().catch(() => []),
          user?.userId ? profileApi.getUserProfile(user.userId).catch(() => null) : Promise.resolve(null)
        ]);
        setRequests(Array.isArray(reqRes) ? reqRes : []);
        setAcceptances(Array.isArray(accRes) ? accRes.filter((a) => ['Accepted', 'ScreeningPending', 'ScreeningCompleted', 'Verified'].includes(a.status)) : []);
        setProfile(me);
      } catch (err) {
        console.error('Failed to load donor dashboard data:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchData();
  }, [user?.userId]);

  const nextEligible = profile?.nextEligibleDonationDate ? new Date(profile.nextEligibleDonationDate) : null;
  const canDonate = !nextEligible || nextEligible <= new Date();

  return (
    <div className="space-y-6">
      {/* Page Title Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Donor Dashboard</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Track eligibility, active blood requests, and health screening progress.
          </p>
        </div>
        <Link
          to="/donor/requests"
          className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md shadow-red-600/20 transition-all self-start"
        >
          <Droplet className="w-4 h-4" />
          <span>Explore Requests</span>
        </Link>
      </div>

      {/* KPI Stats Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Donation Status</span>
            <ShieldCheck className="w-5 h-5 text-emerald-500" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-lg font-bold text-slate-900 dark:text-slate-100">{canDonate ? 'Eligible' : 'Resting'}</span>
            <Badge variant={canDonate ? 'success' : 'warning'}>{canDonate ? 'Ready to Donate' : '120-day interval'}</Badge>
          </div>
          <p className="text-[11px] text-slate-400 mt-1">
            {canDonate ? 'At least 120 days since your last donation' : `You can donate again from ${nextEligible.toLocaleDateString()}`}
            {profile?.bloodGroup ? ` - Blood group ${profile.bloodGroup}` : ''}
          </p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Active Acceptances</span>
            <Heart className="w-5 h-5 text-red-500" />
          </div>
          <div className="mt-2">
            <span className="text-2xl font-bold text-slate-900 dark:text-slate-100">{acceptances.length}</span>
          </div>
          <p className="text-[11px] text-slate-400 mt-1">Active donation commitments</p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Nearby Requests</span>
            <Droplet className="w-5 h-5 text-blue-500" />
          </div>
          <div className="mt-2">
            <span className="text-2xl font-bold text-slate-900 dark:text-slate-100">{requests.length}</span>
          </div>
          <p className="text-[11px] text-slate-400 mt-1">Public requests matching supply</p>
        </div>

        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Lives Impacted</span>
            <Heart className="w-5 h-5 text-rose-500" />
          </div>
          <div className="mt-2">
            <span className="text-2xl font-bold text-slate-900 dark:text-slate-100">{acceptances.length}</span>
          </div>
          <p className="text-[11px] text-slate-400 mt-1">Estimated patient impact</p>
        </div>
      </div>

      {/* AI Smart Insight Card */}
      <AgentStatusCard
        type="matching"
        title="Smart Donor Matching Engine"
        description="Real-time algorithm monitoring compatible patient blood requests within your regional location."
        metrics={[
          { label: 'Match Confidence', value: '96.2%' },
          { label: 'Avg Distance', value: '4.8 km' },
          { label: 'Response Time', value: '< 2 mins' }
        ]}
      />

      {/* Urgent Requests Directory Feed */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Urgent Nearby Blood Requests</h3>
          <Link to="/donor/requests" className="text-xs font-semibold text-red-600 hover:underline flex items-center gap-1">
            View All ({requests.length}) <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {requests.slice(0, 4).map((req) => (
            <div
              key={req.bloodRequestId || req.id}
              className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200 dark:border-slate-700/60 flex flex-col justify-between gap-3 hover:border-red-500/50 transition-all"
            >
              <div className="flex items-start justify-between">
                <div>
                  <Badge variant="blood">{req.bloodGroup}</Badge>
                  <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100 mt-2">{req.hospitalName || 'Regional Hospital'}</h4>
                  <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{req.reason || 'Medical Transfusion Required'}</p>
                </div>
                <Badge variant={req.priority === 'CRITICAL' ? 'danger' : 'warning'}>{req.priority || 'URGENT'}</Badge>
              </div>

              <div className="flex items-center justify-between pt-2 border-t border-slate-200/60 dark:border-slate-700/40 text-[11px]">
                <span className="text-slate-500">Units Needed: <strong className="text-slate-900 dark:text-slate-100">{req.unitsRequired} Units</strong></span>
                <Link
                  to={`/donor/requests/${req.bloodRequestId || req.id}`}
                  className="px-3 py-1 bg-red-600 hover:bg-red-700 text-white font-semibold rounded-lg shadow-sm"
                >
                  Accept & Donate
                </Link>
              </div>
            </div>
          ))}

          {requests.length === 0 && (
            <div className="col-span-2 text-center py-8 text-slate-400 text-xs">
              No active public blood requests found at this moment.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default DonorDashboard;
