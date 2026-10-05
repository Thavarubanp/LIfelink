import { useState, useEffect } from 'react';
import { bloodRequestApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage, isConflictError } from '../../utils/errorUtils';
import { getUserRoles } from '../../utils/roleUtils';
import { DataTable } from '../../components/common/DataTable';
import { Badge, RequestStatusBadge, SuspendedBadge } from '../../components/common/Badge';
import { Trash2, Loader2, X, AlertTriangle, Pencil, Ban } from 'lucide-react';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

/**
 * "My Requests" list shown below the Create Blood Request form.
 * Rejected requests show their rejection reason. A patient can edit the blood group and units of their own request while
 * it is still Pending. The creator can delete a request at any time, in any status, even while donors take part: active
 * donors are released and notified, screening reports are kept, and the request disappears from every list (only the
 * Admin still sees it).
 * Pass a changing `refreshKey` to reload after a new request is created.
 */
export const MyRequestsList = ({ refreshKey = 0 }) => {
  const { user } = useAuth();
  const { addToast } = useNotification();
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(true);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);
  const [cancelTarget, setCancelTarget] = useState(null);
  const [cancelling, setCancelling] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [editTarget, setEditTarget] = useState(null);
  const [editForm, setEditForm] = useState({ bloodGroup: '', unitsRequired: 1 });
  const [editError, setEditError] = useState('');
  const [saving, setSaving] = useState(false);
  // Only donor/patient accounts edit their requests (hospital staff and admins cannot; the backend enforces this too)
  const isPatient = getUserRoles(user).includes('User');

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

  // Delete rule (mirrors the backend): the creator only, in any status (deleted requests are no longer listed)
  const canDelete = (row) => row.patientUserId === user?.userId && row.status !== 'Deleted';
  const isOwnOpen = (row) => row.patientUserId === user?.userId && row.status !== 'Completed' && row.status !== 'Deleted' && !(row.fulfilledUnits > 0);
  const canCancel = (row) => !row.isSuspended && isOwnOpen(row) && row.hasScreenedDonors && row.status !== 'Cancelled';

  // Editable only by the patient who created it, while it is still Pending (mirrors the backend rule)
  // While the admin has it suspended only Delete stays available (owner's Q7)
  const canEdit = (row) => !row.isSuspended && isPatient && row.status === 'Pending' && row.patientUserId === user?.userId && new Date(row.expiryDate) > new Date();

  const openEdit = (row) => {
    setEditTarget(row);
    setEditForm({ bloodGroup: row.bloodGroup, unitsRequired: row.unitsRequired });
    setEditError('');
  };

  const handleSaveEdit = async (e) => {
    e.preventDefault();
    const units = Number(editForm.unitsRequired);
    if (!Number.isInteger(units) || units < 1 || units > 10) {
      setEditError('UnitsRequired must be between 1 and 10.');
      return;
    }
    setSaving(true);
    setEditError('');
    try {
      await bloodRequestApi.updateRequest(editTarget.bloodRequestId, { bloodGroup: editForm.bloodGroup, unitsRequired: units });
      addToast({ title: 'Request Updated', message: `Now ${editForm.bloodGroup}, ${units} unit(s). The hospital sees the change.`, type: 'success' });
      setEditTarget(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      setEditError(getApiErrorMessage(err));
      // Changed by someone else at the same moment (for example the hospital verified it): show the current list
      if (isConflictError(err)) setReloadKey((k) => k + 1);
    } finally {
      setSaving(false);
    }
  };

  const handleConfirmCancel = async () => {
    if (!cancelTarget) return;
    setCancelling(true);
    try {
      await bloodRequestApi.cancelRequest(cancelTarget.bloodRequestId);
      addToast({ title: 'Request Cancelled', message: 'The request is closed. Its screening history is kept.', type: 'success' });
      setCancelTarget(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Cancel Failed', message: getApiErrorMessage(err), type: 'error' });
      if (isConflictError(err)) {
        setCancelTarget(null);
        setReloadKey((k) => k + 1);
      }
    } finally {
      setCancelling(false);
    }
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await bloodRequestApi.deleteRequest(deleteTarget.bloodRequestId);
      addToast({
        title: 'Request Deleted',
        message: 'The request was deleted. The hospital, the assigned doctor and any donors taking part were notified.',
        type: 'success'
      });
      setDeleteTarget(null);
      setReloadKey((k) => k + 1);
    } catch (err) {
      addToast({ title: 'Delete Failed', message: getApiErrorMessage(err), type: 'error' });
      if (isConflictError(err)) {
        setDeleteTarget(null);
        setReloadKey((k) => k + 1);
      }
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
          <div className="flex items-center gap-1 flex-wrap">
            <RequestStatusBadge status={row.status} />
            {row.isSuspended && <SuspendedBadge reason={row.suspensionReason} />}
          </div>
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
        canEdit(row) || canDelete(row) || canCancel(row) ? (
          <div className="flex flex-wrap items-center justify-end gap-2 md:justify-start">
            {canEdit(row) && (
              <button
                onClick={() => openEdit(row)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-50 text-slate-700 hover:bg-slate-100 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700 border border-slate-200 dark:border-slate-700 transition-colors"
              >
                <Pencil className="w-3.5 h-3.5" /> Edit
              </button>
            )}
            {canDelete(row) && (
              <button
                onClick={() => setDeleteTarget(row)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
              >
                <Trash2 className="w-3.5 h-3.5" /> Delete
              </button>
            )}
            {canCancel(row) && (
              <button
                onClick={() => setCancelTarget(row)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-amber-50 text-amber-800 hover:bg-amber-100 dark:bg-amber-950/60 dark:text-amber-300 dark:hover:bg-amber-900 border border-amber-200 dark:border-amber-900 transition-colors"
              >
                <Ban className="w-3.5 h-3.5" /> Cancel
              </button>
            )}
          </div>
        ) : (
          <span className="text-slate-300 dark:text-slate-600">—</span>
        )
    }
  ];

  return (
    // Bottom padding keeps the floating assistant button clear of the last row's actions and messages
    <div className="space-y-3 pb-24">
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

      {/* Edit Modal (Pending requests: blood group and units only) */}
      {editTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <form onSubmit={handleSaveEdit} noValidate className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <Pencil className="w-5 h-5 text-slate-600 dark:text-slate-300" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Edit Blood Request</h3>
              </div>
              <button type="button" onClick={() => !saving && setEditTarget(null)} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(editTarget.bloodRequestId).substring(0, 8)}</span>{' '}
              at {editTarget.hospitalName || 'the hospital'}. You can change the blood group and units until the hospital verifies or rejects it.
            </p>
            <div className="grid grid-cols-2 gap-4 text-xs">
              <div>
                <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Blood Group Required</label>
                <select
                  value={editForm.bloodGroup}
                  onChange={(e) => setEditForm({ ...editForm, bloodGroup: e.target.value })}
                  className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
                >
                  {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Units Required</label>
                <input
                  type="number"
                  min="1"
                  max="10"
                  value={editForm.unitsRequired}
                  onChange={(e) => setEditForm({ ...editForm, unitsRequired: e.target.value })}
                  className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
                />
              </div>
            </div>
            {editError && (
              <p className="p-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 text-xs text-rose-700 dark:text-rose-300">{editError}</p>
            )}
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
              <button
                type="button"
                onClick={() => setEditTarget(null)}
                disabled={saving}
                className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={saving}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-red-600 hover:bg-red-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
              >
                {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Pencil className="w-3.5 h-3.5" />}
                {saving ? 'Saving...' : 'Save Changes'}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Cancel Confirmation Modal (a donor was screened, so the request is cancelled instead of deleted) */}
      {cancelTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <Ban className="w-5 h-5 text-amber-600" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Cancel Blood Request</h3>
              </div>
              <button onClick={() => !cancelling && setCancelTarget(null)} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Request <span className="font-mono font-semibold text-slate-900 dark:text-slate-100">#{String(cancelTarget.bloodRequestId).substring(0, 8)}</span>{' '}
              ({cancelTarget.bloodGroup}, {cancelTarget.unitsRequired} units) can&apos;t be deleted because a donor has already been screened.
              Cancelling closes it and releases donors still in progress; the screening history is kept.
            </p>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
              <button
                type="button"
                onClick={() => setCancelTarget(null)}
                disabled={cancelling}
                className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              >
                Keep Request
              </button>
              <button
                type="button"
                onClick={handleConfirmCancel}
                disabled={cancelling}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-amber-600 hover:bg-amber-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
              >
                {cancelling ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Ban className="w-3.5 h-3.5" />}
                {cancelling ? 'Cancelling...' : 'Cancel Request'}
              </button>
            </div>
          </div>
        </div>
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
              ({deleteTarget.bloodGroup}, {deleteTarget.unitsRequired} units) will be deleted and disappear from your list.
              {deleteTarget.hasActiveAcceptances
                ? ' Donors or hospitals still taking part are released (any reserved slot or held blood packets are freed) and notified.'
                : ''}{' '}
              The hospital and the assigned doctor will be notified. Screening reports are kept. This cannot be undone.
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
