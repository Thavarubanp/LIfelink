import { useState, useEffect, useCallback } from 'react';
import { bloodRequestApi, acceptanceApi, profileApi, activityApi } from '../../api';
import ActivityLogList from '../../components/activity/ActivityLogList';
import { useAuth } from '../../context/AuthContext';
import { Badge } from '../../components/common/Badge';
import { Droplet, Heart, ArrowRight, ShieldCheck, Loader2, CheckCircle2, XCircle, Ban, ClipboardList } from 'lucide-react';
import { Link } from 'react-router-dom';
import { formatDisplayDate } from '../../utils/dateUtils';
import { ACCEPTANCE_FILTERS, REQUEST_FILTERS, filterHistory } from '../../utils/donorHistoryFilters';

const MetricCard = ({ label, value, description, icon: Icon, iconClass = 'text-red-500', to }) => (
  <Link
    to={to}
    aria-label={`${label}: ${value}. ${description}`}
    className="ll-card group block min-h-32 p-4 transition hover:-translate-y-0.5 hover:border-red-300 hover:shadow-md focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-red-500/20 dark:hover:border-red-800"
  >
    <div className="flex items-center justify-between gap-3">
      <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">{label}</span>
      <Icon className={`h-5 w-5 transition-transform group-hover:scale-110 ${iconClass}`} />
    </div>
    <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">{value}</div>
    <p className="mt-1 text-[11px] text-slate-400">{description}</p>
  </Link>
);

export const DonorDashboard = () => {
  const [requests, setRequests] = useState([]);
  const [acceptances, setAcceptances] = useState([]);
  const [createdRequests, setCreatedRequests] = useState([]);
  const [profile, setProfile] = useState(null);
  const [loading, setLoading] = useState(true);
  const { user } = useAuth();
  const loadActivity = useCallback((params) => activityApi.getMyActivity(params), []);

  useEffect(() => {
    let active = true;
    const fetchData = async () => {
      setLoading(true);
      try {
        const [publicData, acceptanceData, ownRequestData, me] = await Promise.all([
          bloodRequestApi.getPublicRequests(),
          acceptanceApi.getMyAcceptances(),
          bloodRequestApi.getMyRequests(),
          user?.userId ? profileApi.getUserProfile(user.userId).catch(() => null) : Promise.resolve(null)
        ]);
        if (!active) return;
        setRequests(Array.isArray(publicData) ? publicData : []);
        setAcceptances(Array.isArray(acceptanceData) ? acceptanceData : []);
        setCreatedRequests(Array.isArray(ownRequestData) ? ownRequestData : []);
        setProfile(me);
      } catch (err) {
        if (active) console.error('Failed to load donor dashboard data:', err);
      } finally {
        if (active) setLoading(false);
      }
    };

    fetchData();
    return () => { active = false; };
  }, [user?.userId]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center gap-2 text-sm text-slate-500 dark:text-slate-400" role="status">
        <Loader2 className="h-6 w-6 animate-spin text-red-600" />
        Loading your donor dashboard...
      </div>
    );
  }

  const activeAcceptances = filterHistory(acceptances, ACCEPTANCE_FILTERS, 'active').length;
  const successfulDonations = filterHistory(acceptances, ACCEPTANCE_FILTERS, 'matched').length;
  const rejectedByDoctor = filterHistory(acceptances, ACCEPTANCE_FILTERS, 'rejected').length;
  const withdrawnOrClosed = filterHistory(acceptances, ACCEPTANCE_FILTERS, 'cancelled').length;

  const acceptedRequestIds = new Set(
    acceptances
      .filter((acceptance) => acceptance.status !== 'Cancelled')
      .map((acceptance) => acceptance.bloodRequestId)
  );
  const availableOpportunities = requests.filter((request) => !acceptedRequestIds.has(request.bloodRequestId || request.id));

  const requestsPosted = filterHistory(createdRequests, REQUEST_FILTERS, 'all').length;
  const openRequests = filterHistory(createdRequests, REQUEST_FILTERS, 'open').length;
  const completedRequests = filterHistory(createdRequests, REQUEST_FILTERS, 'completed').length;
  const rejectedRequests = filterHistory(createdRequests, REQUEST_FILTERS, 'rejected').length;
  const cancelledRequests = filterHistory(createdRequests, REQUEST_FILTERS, 'cancelled').length;

  const nextEligible = profile?.nextEligibleDonationDate ? new Date(profile.nextEligibleDonationDate) : null;
  const canDonate = !nextEligible || nextEligible <= new Date();

  return (
    <div className="space-y-6">
      <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Donor Dashboard</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Track donation commitments, completed donations, and the blood requests you created.
          </p>
        </div>
        <Link to="/donor/requests" className="inline-flex items-center gap-2 self-start rounded-xl bg-red-600 px-4 py-2 text-xs font-semibold text-white shadow-md shadow-red-600/20 transition-all hover:bg-red-700">
          <Droplet className="h-4 w-4" /> Explore Requests
        </Link>
      </div>

      <div className="ll-card flex flex-col justify-between gap-3 p-4 sm:flex-row sm:items-center">
        <div className="flex items-start gap-3">
          <ShieldCheck className={`mt-0.5 h-5 w-5 ${canDonate ? 'text-emerald-500' : 'text-amber-500'}`} />
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">Donation Eligibility</h2>
              <Badge variant={canDonate ? 'success' : 'warning'}>{canDonate ? 'Ready to Donate' : '120-day interval'}</Badge>
            </div>
            <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
              {canDonate ? 'At least 120 days since your last recorded donation.' : `You can donate again from ${formatDisplayDate(nextEligible)}.`}
              {profile?.bloodGroup ? ` Blood group: ${profile.bloodGroup}.` : ' Blood group not set.'}
            </p>
          </div>
        </div>
        <Link to="/my-profile" className="text-xs font-semibold text-red-600 hover:underline dark:text-red-400">View profile</Link>
      </div>

      <section className="space-y-3" aria-labelledby="donation-statistics-title">
        <div>
          <h2 id="donation-statistics-title" className="text-sm font-bold text-slate-900 dark:text-slate-100">Donation Activity</h2>
          <p className="text-xs text-slate-500 dark:text-slate-400">Counts from your acceptance history.</p>
        </div>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <MetricCard to="/donor/acceptances?status=active" label="Active Acceptances" value={activeAcceptances} description="Accepted, screening, or approved commitments" icon={Heart} />
          <MetricCard to="/donor/acceptances?status=matched" label="Successful Donations" value={successfulDonations} description="Donations recorded as matched" icon={CheckCircle2} iconClass="text-emerald-500" />
          <MetricCard to="/donor/acceptances?status=rejected" label="Rejected by Doctor" value={rejectedByDoctor} description="Screenings not approved by a doctor" icon={XCircle} iconClass="text-rose-500" />
          <MetricCard to="/donor/acceptances?status=cancelled" label="Withdrawn / Closed" value={withdrawnOrClosed} description="Cancelled or otherwise closed acceptances" icon={Ban} iconClass="text-slate-500" />
        </div>
      </section>

      <section className="space-y-3" aria-labelledby="request-statistics-title">
        <div>
          <h2 id="request-statistics-title" className="text-sm font-bold text-slate-900 dark:text-slate-100">My Blood Requests</h2>
          <p className="text-xs text-slate-500 dark:text-slate-400">Deleted requests are excluded by your request-history API.</p>
        </div>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-5">
          <MetricCard to="/donor/requests/create?requestStatus=all#my-requests" label="Requests Posted" value={requestsPosted} description="Current records in your request history" icon={ClipboardList} iconClass="text-blue-500" />
          <MetricCard to="/donor/requests/create?requestStatus=open#my-requests" label="Open Requests" value={openRequests} description="Pending, verified, or approved" icon={Droplet} iconClass="text-red-500" />
          <MetricCard to="/donor/requests/create?requestStatus=completed#my-requests" label="Completed Requests" value={completedRequests} description="Requests fulfilled successfully" icon={CheckCircle2} iconClass="text-emerald-500" />
          <MetricCard to="/donor/requests/create?requestStatus=rejected#my-requests" label="Rejected Requests" value={rejectedRequests} description="All requests with rejected status" icon={XCircle} iconClass="text-rose-500" />
          <MetricCard to="/donor/requests/create?requestStatus=cancelled#my-requests" label="Cancelled Requests" value={cancelledRequests} description="Requests cancelled by their creator" icon={Ban} iconClass="text-slate-500" />
        </div>
      </section>

      <div className="ll-card p-5">
        <div className="mb-4 flex items-center justify-between gap-3">
          <div>
            <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">Available Blood Requests</h2>
            <p className="text-xs text-slate-500 dark:text-slate-400">Requests you have already accepted are excluded.</p>
          </div>
          <Link to="/donor/requests" className="flex shrink-0 items-center gap-1 text-xs font-semibold text-red-600 hover:underline">
            View All ({availableOpportunities.length}) <ArrowRight className="h-3.5 w-3.5" />
          </Link>
        </div>

        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {availableOpportunities.slice(0, 4).map((request) => (
            <div key={request.bloodRequestId || request.id} className="flex flex-col justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4 transition-all hover:border-red-500/50 dark:border-slate-700/60 dark:bg-slate-800/60">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <Badge variant="blood">{request.bloodGroup}</Badge>
                  <h3 className="mt-2 text-xs font-bold text-slate-900 dark:text-slate-100">{request.hospitalName || 'Hospital'}</h3>
                  <p className="mt-0.5 text-[11px] text-slate-500 dark:text-slate-400">{request.reason || 'Medical transfusion required'}</p>
                </div>
                <Badge variant={request.priority?.toUpperCase() === 'CRITICAL' ? 'danger' : 'warning'}>{request.priority || 'Normal'}</Badge>
              </div>
              <div className="flex items-center justify-between gap-3 border-t border-slate-200/60 pt-2 text-[11px] dark:border-slate-700/40">
                <span className="text-slate-500">Units needed: <strong className="text-slate-900 dark:text-slate-100">{request.remainingUnits ?? request.unitsRequired}</strong></span>
                <Link to={`/donor/requests/${request.bloodRequestId || request.id}`} className="rounded-lg bg-red-600 px-3 py-1 font-semibold text-white shadow-sm hover:bg-red-700">View & Donate</Link>
              </div>
            </div>
          ))}

          {availableOpportunities.length === 0 && (
            <div className="py-8 text-center text-xs text-slate-400 md:col-span-2">No available blood requests at this moment.</div>
          )}
        </div>
      </div>

      <ActivityLogList load={loadActivity} title="My activity" description="What you did on LifeLink, and actions taken on your account." />
    </div>
  );
};

export default DonorDashboard;
