import { useState, useEffect } from 'react';
import { bloodRequestApi, acceptanceApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { useAuth } from '../../context/AuthContext';
import { isDonorAccount as isPlainDonor } from '../../utils/roleUtils';
import { Link } from 'react-router-dom';
import { Filter, ArrowRight, CheckCircle2, Loader2, Search, ChevronLeft, ChevronRight, Inbox } from 'lucide-react';

const PAGE_SIZE = 8;
const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

const getPriorityDisplay = (priority) => {
  const normalized = String(priority || 'Normal').toLowerCase();
  if (normalized === 'critical') return { label: 'Critical', variant: 'danger' };
  if (normalized === 'high') return { label: 'High', variant: 'warning' };
  return { label: 'Normal', variant: 'default' };
};

export const AvailableRequestsPage = () => {
  const [requests, setRequests] = useState([]);
  const [selectedBloodGroup, setSelectedBloodGroup] = useState('');
  const [requestsLoading, setRequestsLoading] = useState(true);
  const [acceptancesLoading, setAcceptancesLoading] = useState(true);
  const [acceptanceCheckFailed, setAcceptanceCheckFailed] = useState(false);
  const [acceptedRequestIds, setAcceptedRequestIds] = useState(() => new Set());
  const [searchTerm, setSearchTerm] = useState('');
  const [currentPage, setCurrentPage] = useState(1);
  const { user } = useAuth();
  const donorAccount = isPlainDonor(user);

  useEffect(() => {
    let active = true;

    if (!donorAccount) {
      return () => { active = false; };
    }

    acceptanceApi.getMyAcceptances()
      .then((data) => {
        if (!active) return;
        const ids = (Array.isArray(data) ? data : [])
          .filter((acceptance) => acceptance.status !== 'Cancelled')
          .map((acceptance) => acceptance.bloodRequestId);
        setAcceptedRequestIds(new Set(ids));
      })
      .catch((err) => {
        if (active) {
          console.error('Failed to fetch current user acceptances:', err);
          setAcceptanceCheckFailed(true);
        }
      })
      .finally(() => {
        if (active) setAcceptancesLoading(false);
      });

    return () => { active = false; };
  }, [donorAccount, user?.userId]);

  useEffect(() => {
    let active = true;
    const fetchRequests = async () => {
      setRequestsLoading(true);
      try {
        const data = await bloodRequestApi.getPublicRequests(selectedBloodGroup || null);
        if (active) setRequests(Array.isArray(data) ? data : []);
      } catch (err) {
        if (active) console.error('Failed to fetch public requests:', err);
      } finally {
        if (active) setRequestsLoading(false);
      }
    };

    fetchRequests();
    return () => { active = false; };
  }, [selectedBloodGroup]);

  const loading = requestsLoading || (donorAccount && acceptancesLoading);

  const normalizedSearch = searchTerm.trim().toLowerCase();
  const filteredRequests = requests.filter((request) => {
    if (!normalizedSearch) return true;
    return [
      request.hospitalName,
      request.bloodGroup,
      request.reason,
      request.priority,
      request.unitsRequired,
      request.remainingUnits,
      request.fulfilledUnits,
      request.reservedUnits,
      request.bloodRequestId || request.id
    ].some((value) => value !== null && value !== undefined && String(value).toLowerCase().includes(normalizedSearch));
  });
  const totalPages = Math.ceil(filteredRequests.length / PAGE_SIZE) || 1;
  const paginatedRequests = filteredRequests.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);

  const selectBloodGroup = (bloodGroup) => {
    setSelectedBloodGroup(bloodGroup);
    setCurrentPage(1);
  };

  return (
    <div className="space-y-6">
      <div className="min-w-0">
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Available Blood Requests</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Browse verified public blood requests needing eligible donors.
        </p>
      </div>

      {/* Filter Bar */}
      <div className="flex min-w-0 flex-wrap items-center gap-2 rounded-xl border border-slate-200 bg-white p-3 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <span className="mr-1 flex items-center gap-1 text-xs font-semibold text-slate-500 sm:mr-2">
          <Filter className="w-3.5 h-3.5" /> Filter Blood Group:
        </span>
        <button
          type="button"
          onClick={() => selectBloodGroup('')}
          className={`min-h-9 rounded-lg px-3 py-2 text-xs font-semibold transition-all ${
            selectedBloodGroup === ''
              ? 'bg-red-600 text-white shadow-sm'
              : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200'
          }`}
        >
          All Groups
        </button>
        {BLOOD_GROUPS.map((bg) => (
          <button
            key={bg}
            type="button"
            onClick={() => selectBloodGroup(bg)}
            className={`min-h-9 rounded-lg px-3 py-2 text-xs font-semibold transition-all ${
              selectedBloodGroup === bg
                ? 'bg-red-600 text-white shadow-sm'
                : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200'
            }`}
          >
            {bg}
          </button>
        ))}
      </div>

      {/* Search and count intentionally remain one row at every viewport width. */}
      <div className="ll-card flex min-w-0 items-center gap-2 p-2.5 sm:gap-4 sm:p-4">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => {
              setSearchTerm(event.target.value);
              setCurrentPage(1);
            }}
            placeholder="Search requests..."
            aria-label="Search by hospital, blood group, reason, priority, units, or request ID"
            className="min-h-10 w-full min-w-0 rounded-xl border border-slate-200 bg-slate-50 py-2 pl-9 pr-2 text-sm text-slate-900 placeholder-slate-400 focus:border-red-500 focus:outline-none focus:ring-4 focus:ring-red-500/10 dark:border-slate-700/60 dark:bg-slate-800/80 dark:text-slate-100"
          />
        </div>
        <div className="shrink-0 border-l border-slate-200 pl-2 text-center sm:pl-4 dark:border-slate-700" aria-live="polite">
          <div className="whitespace-nowrap text-[9px] font-bold uppercase tracking-wide text-slate-500 sm:text-[10px] dark:text-slate-400">Total Requests</div>
          <div className="text-lg font-extrabold leading-tight text-slate-900 dark:text-slate-100">{filteredRequests.length}</div>
        </div>
      </div>

      {loading ? (
        <div className="ll-card flex min-h-48 items-center justify-center gap-2 p-8 text-sm text-slate-500 dark:text-slate-400" role="status">
          <Loader2 className="h-5 w-5 animate-spin text-red-600" />
          Loading available blood requests...
        </div>
      ) : filteredRequests.length === 0 ? (
        <div className="ll-card flex min-h-48 flex-col items-center justify-center px-4 py-10 text-center text-slate-400">
          <Inbox className="mb-2 h-10 w-10 stroke-1" />
          <p className="text-sm font-medium">No available blood requests matching filter.</p>
        </div>
      ) : (
        <>
          <div className="grid min-w-0 grid-cols-[repeat(auto-fit,minmax(min(100%,19rem),1fr))] gap-4">
            {paginatedRequests.map((request) => {
              const requestId = request.bloodRequestId || request.id;
              const alreadyAccepted = donorAccount && acceptedRequestIds.has(requestId);
              const priority = getPriorityDisplay(request.priority);

              return (
                <article key={requestId} className="ll-card flex h-full min-w-0 flex-col p-4 sm:p-5">
                  <div className="flex min-w-0 items-start justify-between gap-3">
                    <Badge variant="blood" size="lg">{request.bloodGroup}</Badge>
                    <Badge variant={priority.variant} size="sm">{priority.label}</Badge>
                  </div>

                  <div className="mt-4 min-w-0">
                    <h2 className="break-words text-base font-bold text-slate-900 dark:text-slate-100">
                      {request.hospitalName || 'Hospital unavailable'}
                    </h2>
                    <p className="mt-1 break-all font-mono text-[10px] text-slate-400">Request ID: #{String(requestId || '')}</p>
                  </div>

                  <div className="mt-4 min-w-0 flex-1">
                    <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Reason</p>
                    <p className="mt-1 break-words text-sm leading-relaxed text-slate-600 dark:text-slate-300">
                      {request.reason || 'No reason provided.'}
                    </p>
                  </div>

                  <div className="mt-4 border-t border-slate-100 pt-4 dark:border-slate-800">
                    <p className="text-sm text-slate-600 dark:text-slate-300">
                      Units still needed:{' '}
                      <strong className="text-slate-900 dark:text-slate-100">
                        {request.remainingUnits ?? request.unitsRequired} of {request.unitsRequired}
                      </strong>
                    </p>
                    <div className="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-slate-500 dark:text-slate-400">
                      <span>{request.fulfilledUnits ?? 0} donated</span>
                      <span aria-hidden="true">|</span>
                      <span>{request.reservedUnits ?? 0} reserved</span>
                    </div>
                    {!request.isAcceptingDonors && <Badge variant="info" size="sm" className="mt-2">All slots reserved</Badge>}
                  </div>

                  <div className="mt-4 space-y-2">
                    {alreadyAccepted ? (
                      <>
                        <span className="flex min-h-10 w-full items-center justify-center gap-1 rounded-xl bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300">
                          <CheckCircle2 className="h-3.5 w-3.5" /> Already Accepted
                        </span>
                        <Link to={`/donor/requests/${requestId}`} className="ll-button-secondary w-full text-xs">View Details <ArrowRight className="h-3.5 w-3.5" /></Link>
                      </>
                    ) : donorAccount && acceptanceCheckFailed ? (
                      <>
                        <span className="block text-center text-xs font-semibold text-amber-700 dark:text-amber-300">Acceptance status unavailable</span>
                        <Link to={`/donor/requests/${requestId}`} className="ll-button-secondary w-full text-xs">View Details <ArrowRight className="h-3.5 w-3.5" /></Link>
                      </>
                    ) : (
                      <Link
                        to={`/donor/requests/${requestId}`}
                        className={`inline-flex min-h-11 w-full items-center justify-center gap-1 rounded-xl px-4 py-2.5 text-xs font-bold text-white shadow-sm transition-colors ${donorAccount ? 'bg-red-600 shadow-red-600/20 hover:bg-red-700' : 'bg-slate-700 hover:bg-slate-800 dark:bg-slate-600 dark:hover:bg-slate-500'}`}
                      >
                        <span>{donorAccount ? 'View & Donate' : 'View Details'}</span>
                        <ArrowRight className="h-3.5 w-3.5" />
                      </Link>
                    )}
                  </div>
                </article>
              );
            })}
          </div>

          <div className="ll-card flex flex-col gap-3 px-4 py-3 text-xs text-slate-500 sm:flex-row sm:items-center sm:justify-between dark:text-slate-400">
            <span>
              Showing {(currentPage - 1) * PAGE_SIZE + 1} to {Math.min(currentPage * PAGE_SIZE, filteredRequests.length)} of {filteredRequests.length} requests
            </span>
            <div className="flex items-center gap-2">
              <button type="button" onClick={() => setCurrentPage((page) => Math.max(page - 1, 1))} disabled={currentPage === 1} className="rounded-lg border border-slate-200 p-2 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40 dark:border-slate-800 dark:hover:bg-slate-800" aria-label="Previous page">
                <ChevronLeft className="h-4 w-4" />
              </button>
              <span className="font-semibold text-slate-900 dark:text-slate-100">{currentPage} / {totalPages}</span>
              <button type="button" onClick={() => setCurrentPage((page) => Math.min(page + 1, totalPages))} disabled={currentPage === totalPages} className="rounded-lg border border-slate-200 p-2 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40 dark:border-slate-800 dark:hover:bg-slate-800" aria-label="Next page">
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
};

export default AvailableRequestsPage;
