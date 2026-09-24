import React, { useState, useEffect } from 'react';
import { bloodRequestApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { DataTable } from '../../components/common/DataTable';
import { Badge, RequestStatusBadge } from '../../components/common/Badge';
import { Trash2, Loader2, X, AlertTriangle } from 'lucide-react';

/**
 * "My Requests" list shown below the Create Blood Request form.
 * Rejected requests show their rejection reason. The creator can delete any request except Completed ones; history is kept.
 * Pass a changing `refreshKey` to reload after a new request is created.
 */
export const MyRequestsList = ({ refreshKey = 0 }) => {
  const { user } = useAuth();
  const { addToast } = useNotification();
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(true);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const fetchMy = async () => {
      try {
        const data = await bloodRequestApi.getMyRequests();
        setRequests(Array.isArray(data) ? data : []);
      } catch (err) {
        console.error('Failed to fetch my requests:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchMy();
  }, [refreshKey, reloadKey]);

  // Creator can delete any of their requests except Completed ones (mirrors the backend rule)
  const canDelete = (row) => row.status !== 'Completed' && row.status !== 'Deleted' && row.patientUserId === user?.userId;

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await bloodRequestApi.deleteRequest(deleteTarget.bloodRequestId);
      addToast({
        title: 'Request Deleted',
        message: 'The request was removed from active lists. Its history is kept and affected parties were notified.',
        type: 'success'
      });
      setDeleteTarget(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Delete Failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setDeleting(false);
    }
  };

  const columns = [
    {
      header: 'Request ID',
      accessor: 'bloodRequestId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.bloodRequestId || '').substring(0, 8)}</span>
    },
    {
      header: 'Hospital',
      accessor: 'hospitalName',
      cell: (row) => <span className="font-medium text-slate-700 dark:text-slate-200">{row.hospitalName || '—'}</span>
    },
    {
      header: 'Blood Group',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge>
    },
    {
      header: 'Units',
      accessor: 'unitsRequired',
      cell: (row) => <span className="font-semibold">{row.fulfilledUnits ?? 0}/{row.unitsRequired} donated{row.reservedUnits ? `, ${row.reservedUnits} reserved` : ''}</span>
    },
    {
      header: 'Priority',
      accessor: 'priority',
      cell: (row) => <Badge variant={row.priority?.toUpperCase() === 'CRITICAL' ? 'danger' : 'warning'}>{row.priority || 'URGENT'}</Badge>
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
          {row.assignedDoctorName && row.status !== 'Rejected' && (
            <p className="text-[11px] text-slate-400">Doctor: {row.assignedDoctorName}</p>
          )}
        </div>
      )
    },
    {
      header: 'Reason',
      accessor: 'reason',
      cell: (row) => <span className="text-slate-500 truncate max-w-xs block">{row.reason}</span>
    },
    {
      header: 'Actions',
      accessor: 'actions',
      sortable: false,
      cell: (row) =>
        canDelete(row) ? (
          <button
            onClick={() => setDeleteTarget(row)}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
          >
            <Trash2 className="w-3.5 h-3.5" /> Delete
          </button>
        ) : (
          <span className="text-slate-300 dark:text-slate-600">—</span>
        )
    }
  ];

  return (
    <div className="space-y-3">
      <div>
        <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">My Requests</h2>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Track the verification and donor fulfillment status of your submitted requests.
        </p>
      </div>

      {loading ? (
        <div className="p-8 flex items-center justify-center gap-2 text-xs text-slate-400">
          <Loader2 className="w-4 h-4 animate-spin text-red-500" /> Loading your requests...
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={requests}
          searchPlaceholder="Search my requests..."
          emptyMessage="You have not created any blood requests yet."
        />
      )}

      {/* Delete Confirmation Modal */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <AlertTriangle className="w-5 h-5 text-rose-600" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Delete Blood Request</h3>
              </div>
              <button onClick={() => setDeleteTarget(null)} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(deleteTarget.bloodRequestId).substring(0, 8)}</span>{' '}
              ({deleteTarget.bloodGroup}, {deleteTarget.unitsRequired} units) will be removed from all active lists and donors still in
              progress will be released. Its history (acceptances, screening reports, doctor decisions and recorded donations) is kept.
              The assigned doctor, the hospital and donors who accepted will be notified. This cannot be undone.
            </p>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
              <button
                type="button"
                onClick={() => setDeleteTarget(null)}
                disabled={deleting}
                className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleConfirmDelete}
                disabled={deleting}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-rose-600 hover:bg-rose-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
              >
                {deleting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                {deleting ? 'Deleting...' : 'Delete Request'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MyRequestsList;
