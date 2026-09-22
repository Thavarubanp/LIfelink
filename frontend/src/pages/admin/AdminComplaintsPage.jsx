import React, { useState, useEffect } from 'react';
import { adminApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { AlertTriangle, FileText, CheckCircle2, Loader2 } from 'lucide-react';

export const AdminComplaintsPage = () => {
  const [complaints, setComplaints] = useState([]);
  const [loading, setLoading] = useState(true);
  const { addToast } = useNotification();

  const fetchComplaints = async () => {
    setLoading(true);
    try {
      const res = await adminApi.getComplaints();
      setComplaints(res.data || (Array.isArray(res) ? res : []));
    } catch (err) {
      console.error('Failed to load complaints:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchComplaints();
  }, []);

  const handleRequestActivity = async (id) => {
    const instructions = prompt('Enter evidence instructions for hospital:');
    if (!instructions) return;

    try {
      await adminApi.requestActivityReport(id, { deadlineHours: 48, instructions });
      addToast({ title: 'Evidence Requested', message: 'Activity report requested from hospital.', type: 'info' });
      fetchComplaints();
    } catch (err) {
      addToast({ title: 'Operation Failed', message: err.response?.data?.message || 'Error requesting evidence.', type: 'error' });
    }
  };

  const handleResolve = async (id) => {
    const resolutionNotes = prompt('Enter resolution summary:');
    if (!resolutionNotes) return;

    try {
      await adminApi.resolveComplaint(id, { status: 'RESOLVED', resolutionNotes });
      addToast({ title: 'Complaint Resolved', message: 'Complaint marked as resolved.', type: 'success' });
      fetchComplaints();
    } catch (err) {
      addToast({ title: 'Operation Failed', message: err.response?.data?.message || 'Error resolving complaint.', type: 'error' });
    }
  };

  const columns = [
    {
      header: 'Complaint ID',
      accessor: 'complaintId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.complaintId || '').substring(0, 8)}</span>
    },
    {
      header: 'Type',
      accessor: 'complaintType',
      cell: (row) => <Badge variant="warning">{row.complaintType}</Badge>
    },
    {
      header: 'Subject',
      accessor: 'subject',
      cell: (row) => <span className="font-bold text-slate-900 dark:text-slate-100">{row.subject}</span>
    },
    {
      header: 'Status',
      accessor: 'status',
      cell: (row) => <Badge variant={row.status === 'RESOLVED' ? 'success' : 'danger'}>{row.status || 'PENDING'}</Badge>
    },
    {
      header: 'Actions',
      accessor: 'complaintId',
      sortable: false,
      cell: (row) => (
        <div className="flex items-center gap-2">
          <button
            onClick={() => handleRequestActivity(row.complaintId || row.id)}
            className="px-2.5 py-1 bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs rounded-lg shadow-sm flex items-center gap-1"
          >
            <FileText className="w-3.5 h-3.5" /> Request Report
          </button>
          <button
            onClick={() => handleResolve(row.complaintId || row.id)}
            className="px-2.5 py-1 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs rounded-lg shadow-sm flex items-center gap-1"
          >
            <CheckCircle2 className="w-3.5 h-3.5" /> Resolve
          </button>
        </div>
      )
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Platform Complaints Hub</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Investigate reported grievances, request hospital evidence, and issue resolution findings.
        </p>
      </div>

      <DataTable
        columns={columns}
        data={complaints}
        searchPlaceholder="Search complaint subject..."
        emptyMessage="No open complaints found."
      />
    </div>
  );
};

export default AdminComplaintsPage;
