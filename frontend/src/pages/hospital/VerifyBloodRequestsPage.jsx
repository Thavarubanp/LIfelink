import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { bloodRequestApi, doctorApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { DataTable } from '../../components/common/DataTable';
import { Badge, RequestStatusBadge } from '../../components/common/Badge';
import { UserCheck, XCircle, X, Loader2, RefreshCw, Stethoscope, AlertCircle } from 'lucide-react';

/**
 * Hospital Staff: every blood request sent to this hospital.
 * Pending requests can be Verified (assigning a doctor is mandatory) or Rejected (reason mandatory).
 * Verified / Approved / Rejected / Completed / Cancelled requests are read-only here.
 */
export const VerifyBloodRequestsPage = () => {
  const { addToast } = useNotification();
  const [requests, setRequests] = useState([]);
  const [doctors, setDoctors] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');

  // modal: { type: 'verify' | 'reject', request }
  const [modal, setModal] = useState(null);
  const [selectedDoctorId, setSelectedDoctorId] = useState('');
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const loadData = async () => {
      try {
        const [reqs, docs] = await Promise.all([bloodRequestApi.getHospitalRequests(), doctorApi.getDoctors()]);
        setRequests(Array.isArray(reqs) ? reqs : []);
        const docList = docs?.data || (Array.isArray(docs) ? docs : []);
        // Only doctors with an active login can be assigned (backend enforces the same rule)
        setDoctors(docList.filter((d) => d.isActive && d.userId));
        setLoadError('');
      } catch (err) {
        setLoadError(getApiErrorMessage(err));
      } finally {
        setLoading(false);
      }
    };
    loadData();
  }, [reloadKey]);

  const reload = () => {
    setLoading(true);
    setReloadKey((k) => k + 1);
  };

  const openModal = (type, request) => {
    setModal({ type, request });
    setSelectedDoctorId('');
    setReason('');
  };

  const closeModal = () => {
    if (!submitting) setModal(null);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!modal) return;
    const id = modal.request.bloodRequestId;

    setSubmitting(true);
    try {
      if (modal.type === 'verify') {
        await bloodRequestApi.verifyRequest(id, selectedDoctorId);
        const doctor = doctors.find((d) => d.doctorId === selectedDoctorId);
        addToast({
          title: 'Request Verified',
          message: `Assigned to Dr. ${doctor?.firstName || ''} ${doctor?.lastName || ''} for approval.`.replace(/\s+/g, ' '),
          type: 'success'
        });
      } else {
        await bloodRequestApi.rejectRequest(id, reason.trim());
        addToast({ title: 'Request Rejected', message: 'The creator will see your rejection message.', type: 'info' });
      }
      setModal(null);
      reload();
    } catch (err) {
      addToast({ title: 'Action Failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setSubmitting(false);
    }
  };

  const pendingCount = requests.filter((r) => r.status === 'Pending').length;

  const columns = [
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
          {row.assignedDoctorName && (
            <p className="text-[11px] text-slate-400">Doctor: {row.assignedDoctorName}</p>
          )}
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
      header: 'Actions',
      accessor: 'actions',
      sortable: false,
      cell: (row) =>
        row.status === 'Pending' ? (
          <div className="flex items-center gap-2">
            <button
              onClick={() => openModal('verify', row)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800 transition-colors"
            >
              <UserCheck className="w-3.5 h-3.5" /> Verify
            </button>
            <button
              onClick={() => openModal('reject', row)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
            >
              <XCircle className="w-3.5 h-3.5" /> Reject
            </button>
          </div>
        ) : (
          <span className="text-[11px] text-slate-400 italic">Read-only</span>
        )
    }
  ];

  const canSubmit = modal?.type === 'verify' ? Boolean(selectedDoctorId) : reason.trim().length > 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Verify Blood Requests</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            All blood requests sent to your hospital. Assign a doctor to verify a pending request, or reject it with a message.
            {!loading && ` ${pendingCount} pending.`}
          </p>
        </div>
        <button
          onClick={reload}
          className="inline-flex items-center gap-2 px-3.5 py-2 bg-slate-900 dark:bg-slate-800 hover:bg-slate-800 text-white font-semibold text-xs rounded-xl shadow-md transition-all self-start"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          <span>Refresh</span>
        </button>
      </div>

      {loadError && (
        <div className="p-3.5 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-700 dark:text-red-300">
          <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
          <span>{loadError}</span>
        </div>
      )}

      {loading ? (
        <div className="p-12 flex flex-col items-center gap-3 text-slate-400">
          <Loader2 className="w-6 h-6 animate-spin text-red-500" />
          <p className="text-xs font-medium">Loading hospital requests...</p>
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={requests}
          searchPlaceholder="Search requests..."
          emptyMessage="No blood requests have been sent to your hospital yet."
        />
      )}

      {/* Verify / Reject Modal */}
      {modal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                {modal.type === 'verify' ? (
                  <UserCheck className="w-5 h-5 text-emerald-600" />
                ) : (
                  <XCircle className="w-5 h-5 text-rose-600" />
                )}
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                  {modal.type === 'verify' ? 'Verify Request & Assign Doctor' : 'Reject Request'}
                </h3>
              </div>
              <button onClick={closeModal} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
                <X className="w-4 h-4" />
              </button>
            </div>

            <p className="text-xs text-slate-500 dark:text-slate-400">
              Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(modal.request.bloodRequestId).substring(0, 8)}</span>{' '}
              from {modal.request.createdByName || 'requester'}: {modal.request.bloodGroup}, {modal.request.unitsRequired} units ({modal.request.priority}).
            </p>

            <form onSubmit={handleSubmit} className="space-y-4">
              {modal.type === 'verify' ? (
                <div>
                  <label className="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1">
                    Assign Doctor <span className="text-red-500">*</span>
                  </label>
                  {doctors.length > 0 ? (
                    <select
                      required
                      value={selectedDoctorId}
                      onChange={(e) => setSelectedDoctorId(e.target.value)}
                      className="w-full p-2.5 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-500"
                    >
                      <option value="">Select a doctor...</option>
                      {doctors.map((d) => (
                        <option key={d.doctorId} value={d.doctorId}>
                          Dr. {d.firstName} {d.lastName}{d.specialization ? ` — ${d.specialization}` : ''}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <div className="p-3 bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900 rounded-xl text-xs text-amber-700 dark:text-amber-300 flex items-start gap-2">
                      <Stethoscope className="w-4 h-4 shrink-0 mt-0.5" />
                      <span>
                        Your hospital has no active doctors. Create one in{' '}
                        <Link to="/hospital/doctors" className="font-semibold underline">Doctor Management</Link> first.
                      </span>
                    </div>
                  )}
                  <p className="text-[11px] text-slate-400 mt-1">The assigned doctor will approve or reject this request.</p>
                </div>
              ) : (
                <div>
                  <label className="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1">
                    Rejection Message <span className="text-red-500">*</span>
                  </label>
                  <textarea
                    required
                    rows={3}
                    maxLength={500}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    placeholder="Explain why this request cannot be accepted..."
                    className="w-full p-2.5 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                  />
                  <p className="text-[11px] text-slate-400 mt-1">Shown to the person who created the request.</p>
                </div>
              )}

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
                <button
                  type="button"
                  onClick={closeModal}
                  disabled={submitting}
                  className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submitting || !canSubmit}
                  className={`px-4 py-2 rounded-xl text-xs font-semibold text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5 ${
                    modal.type === 'verify' ? 'bg-emerald-600 hover:bg-emerald-700' : 'bg-rose-600 hover:bg-rose-700'
                  }`}
                >
                  {submitting && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  {modal.type === 'verify' ? 'Verify & Assign' : 'Confirm Rejection'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default VerifyBloodRequestsPage;
