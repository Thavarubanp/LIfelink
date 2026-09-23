import React, { useState, useEffect } from 'react';
import { acceptanceApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { Link } from 'react-router-dom';
import { Stethoscope, ArrowRight, Loader2 } from 'lucide-react';

export const ScreeningReportsPage = () => {
  const [reports, setReports] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchReports = async () => {
      try {
        const list = await acceptanceApi.getMyAcceptances().catch(() => []);
        setReports(Array.isArray(list) ? list : []);
      } catch (err) {
        console.error('Failed to load screening reports:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchReports();
  }, []);

  const columns = [
    {
      header: 'Acceptance ID',
      accessor: 'acceptanceId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.acceptanceId || row.id || '').substring(0, 8)}</span>
    },
    {
      header: 'Donor / Patient',
      accessor: 'donorName',
      cell: (row) => <span className="font-bold text-slate-900 dark:text-slate-100">{row.donorName || row.userName || 'Registered Donor'}</span>
    },
    {
      header: 'Blood Group',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup || 'N/A'}</Badge>
    },
    {
      header: 'Screening Status',
      accessor: 'status',
      cell: (row) => (
        <Badge variant={row.status === 'ACCEPTED' || row.status === 'APPROVED' ? 'success' : 'warning'}>
          {row.status || 'PENDING EVALUATION'}
        </Badge>
      )
    },
    {
      header: 'Date',
      accessor: 'createdAt',
      cell: (row) => <span className="text-xs text-slate-500">{row.createdAt ? new Date(row.createdAt).toLocaleDateString() : 'Recent'}</span>
    },
    {
      header: 'Action',
      accessor: 'actions',
      sortable: false,
      cell: (row) => (
        <Link
          to={`/doctor/screenings/${row.acceptanceId || row.id}`}
          className="inline-flex items-center gap-1 px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-lg shadow-sm"
        >
          <span>Review Report</span>
          <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      )
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">AI Health Screening Reports</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Clinical evaluation queue of AI agent screening reports for donor health clearance.
        </p>
      </div>

      {loading ? (
        <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl">
          <Loader2 className="w-8 h-8 text-red-500 animate-spin mx-auto mb-2" />
          <p className="text-xs text-slate-400 font-medium">Fetching screening reports database...</p>
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={reports}
          searchPlaceholder="Search donor name or blood group..."
          emptyMessage="No health screening reports available."
        />
      )}
    </div>
  );
};

export default ScreeningReportsPage;
