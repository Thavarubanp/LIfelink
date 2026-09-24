import React, { useState, useEffect } from 'react';
import { bloodRequestApi, screeningApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { Badge, RequestStatusBadge } from '../../components/common/Badge';
import { DataTable } from '../../components/common/DataTable';
import { AgentStatusCard } from '../../components/workflow/AgentStatusCard';
import { Stethoscope, CheckCircle2, XCircle, X, Loader2 } from 'lucide-react';
import { Link } from 'react-router-dom';

export const DoctorDashboard = () => {
  const { addToast } = useNotification();
  const [acceptances, setAcceptances] = useState([]);
  const [loading, setLoading] = useState(true);

  // Blood requests assigned to this doctor by their hospital
  const [assigned, setAssigned] = useState([]);
  const [loadingAssigned, setLoadingAssigned] = useState(true);
  const [decision, setDecision] = useState(null); // { type: 'approve' | 'reject', request }
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const fetchData = async () => {
      try {
        // Screening report versions for this hospital (latest version per donor is what matters here)
        const list = await screeningApi.getReports().catch(() => []);
        const latest = {};
        (Array.isArray(list) ? list : []).forEach((r) => {
          if (!latest[r.acceptanceId] || latest[r.acceptanceId].reportVersion < r.reportVersion) latest[r.acceptanceId] = r;
        });
        setAcceptances(Object.values(latest));
      } catch (err) {
        console.error('Failed to load doctor dashboard data:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, []);

  const [assignedReloadKey, setAssignedReloadKey] = useState(0);

  useEffect(() => {
    const loadAssigned = async () => {
      try {
        const list = await bloodRequestApi.getAssignedRequests();
        setAssigned(Array.isArray(list) ? list : []);
      } catch (err) {
        console.error('Failed to load assigned blood requests:', err);
      } finally {
        setLoadingAssigned(false);
      }
    };
    loadAssigned();
  }, [assignedReloadKey]);

  const openDecision = (type, request) => {
    setDecision({ type, request });
    setNotes('');
  };

  const handleDecision = async (e) => {
    e.preventDefault();
    if (!decision) return;
    setSubmitting(true);
    try {
      if (decision.type === 'approve') {
        await bloodRequestApi.approveRequest(decision.request.bloodRequestId, notes.trim());
        addToast({ title: 'Request Approved', message: 'The request is now open to eligible donors.', type: 'success' });
      } else {
        await bloodRequestApi.rejectRequest(decision.request.bloodRequestId, notes.trim());
        addToast({ title: 'Request Rejected', message: 'The creator will see your rejection message.', type: 'info' });
      }
      setDecision(null);
      setLoadingAssigned(true);
      setAssignedReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Action Failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setSubmitting(false);
    }
  };

  const awaitingCount = assigned.filter((r) => r.status === 'Verified').length;

  const assignedColumns = [
    {
      header: 'Request ID',
      accessor: 'bloodRequestId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.bloodRequestId || '').substring(0, 8)}</span>
    },
    {
      header: 'Created By',
      accessor: 'createdByName',
      cell: (row) => <span className="font-medium text-slate-700 dark:text-slate-200">{row.createdByName || '—'}</span>
    },
    {
      header: 'Blood Group',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge>
    },
    {
      header: 'Units',
      accessor: 'unitsRequired',
      cell: (row) => <span className="font-semibold">{row.unitsRequired} Units</span>
    },
    {
      header: 'Priority',
      accessor: 'priority',
      cell: (row) => <Badge variant={row.priority?.toUpperCase() === 'CRITICAL' ? 'danger' : 'warning'}>{row.priority}</Badge>
    },
    {
      header: 'Status',
      accessor: 'status',
      cell: (row) => (
        <div className="space-y-1 max-w-xs">
          <RequestStatusBadge status={row.status} />
          {row.status === 'Rejected' && row.rejectionReason && (
            <p className="text-[11px] text-rose-600 dark:text-rose-400 leading-snug">
              <span className="font-semibold">Reason:</span> {row.rejectionReason}
            </p>
          )}
        </div>
      )
    },
    {
      header: 'Clinical Reason',
      accessor: 'reason',
      cell: (row) => <span className="text-slate-500 truncate max-w-xs block">{row.reason}</span>
    },
    {
      header: 'Decision',
      accessor: 'actions',
      sortable: false,
      cell: (row) =>
        row.status === 'Verified' ? (
          <div className="flex items-center gap-2">
            <button
              onClick={() => openDecision('approve', row)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800 transition-colors"
            >
              <CheckCircle2 className="w-3.5 h-3.5" /> Approve
            </button>
            <button
              onClick={() => openDecision('reject', row)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
            >
              <XCircle className="w-3.5 h-3.5" /> Reject
            </button>
          </div>
        ) : (
          <span className="text-[11px] text-slate-400 italic">Decided</span>
        )
    }
  ];

  const pendingCount = acceptances.filter((r) => r.status === 'Pending' && r.acceptanceStatus === 'ScreeningCompleted').length;
  const approvedCount = acceptances.filter((r) => r.acceptanceStatus === 'Verified').length;

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
          <span className="text-xs font-semibold text-slate-500">Screened Donors</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : acceptances.length}
          </div>
          <p className="text-[11px] text-blue-500 mt-1">Donors with a screening report at your hospital</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Pending Reviews</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : pendingCount}
          </div>
          <p className="text-[11px] text-amber-500 mt-1">Awaiting clinical sign-off</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Approved, Awaiting Donation</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">
            {loading ? <Loader2 className="w-5 h-5 animate-spin text-slate-400" /> : approvedCount}
          </div>
          <p className="text-[11px] text-emerald-500 mt-1">Slots reserved; record the donation when done</p>
        </div>
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm">
          <span className="text-xs font-semibold text-slate-500">Doctor Queue Status</span>
          <div className="text-2xl font-bold text-slate-900 dark:text-slate-100 mt-2">Active</div>
          <p className="text-[11px] text-emerald-500 mt-1">AI pre-evaluation active</p>
        </div>
      </div>

      {/* Assigned Blood Requests (hospital-verified, awaiting this doctor's decision) */}
      <div className="space-y-3">
        <div>
          <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">Assigned Blood Requests</h2>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Requests your hospital verified and assigned to you. Approve to open them to donors, or reject with a reason.
            {!loadingAssigned && ` ${awaitingCount} awaiting your decision.`}
          </p>
        </div>
        {loadingAssigned ? (
          <div className="p-8 flex items-center justify-center gap-2 text-xs text-slate-400">
            <Loader2 className="w-4 h-4 animate-spin text-red-500" /> Loading assigned requests...
          </div>
        ) : (
          <DataTable
            columns={assignedColumns}
            data={assigned}
            searchPlaceholder="Search assigned requests..."
            emptyMessage="No blood requests have been assigned to you yet."
          />
        )}
      </div>

      <AgentStatusCard
        type="screening"
        title="Intelligent Screening Assessment Engine"
        description="The Request Management agent interviews donors and prepares screening reports. It never approves or rejects; you decide."
        metrics={[
          { label: 'Waiting for Review', value: `${pendingCount}` },
          { label: 'Evaluation Engine', value: 'ONLINE' },
          { label: 'Agent Status', value: 'ACTIVE' }
        ]}
      />

      {/* Approve / Reject Modal */}
      {decision && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                {decision.type === 'approve' ? (
                  <CheckCircle2 className="w-5 h-5 text-emerald-600" />
                ) : (
                  <XCircle className="w-5 h-5 text-rose-600" />
                )}
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                  {decision.type === 'approve' ? 'Approve Blood Request' : 'Reject Blood Request'}
                </h3>
              </div>
              <button onClick={() => !submitting && setDecision(null)} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
                <X className="w-4 h-4" />
              </button>
            </div>

            <p className="text-xs text-slate-500 dark:text-slate-400">
              Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(decision.request.bloodRequestId).substring(0, 8)}</span>{' '}
              from {decision.request.createdByName || 'requester'}: {decision.request.bloodGroup}, {decision.request.unitsRequired} units ({decision.request.priority}).
            </p>

            <form onSubmit={handleDecision} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1">
                  {decision.type === 'approve' ? (
                    <>Clinical Notes <span className="text-slate-400">(optional)</span></>
                  ) : (
                    <>Rejection Reason <span className="text-red-500">*</span></>
                  )}
                </label>
                <textarea
                  required={decision.type === 'reject'}
                  rows={3}
                  maxLength={500}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  placeholder={decision.type === 'approve' ? 'Optional notes for the record...' : 'Explain why this request is rejected...'}
                  className="w-full p-2.5 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                />
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
                <button
                  type="button"
                  onClick={() => setDecision(null)}
                  disabled={submitting}
                  className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting || (decision.type === 'reject' && !notes.trim())}
                  className={`px-4 py-2 rounded-xl text-xs font-semibold text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5 ${
                    decision.type === 'approve' ? 'bg-emerald-600 hover:bg-emerald-700' : 'bg-rose-600 hover:bg-rose-700'
                  }`}
                >
                  {submitting && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  {decision.type === 'approve' ? 'Confirm Approval' : 'Confirm Rejection'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default DoctorDashboard;
