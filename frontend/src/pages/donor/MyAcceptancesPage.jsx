import React, { useState, useEffect } from 'react';
import { acceptanceApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { ScreeningProgressTimeline } from '../../components/workflow/ScreeningProgressTimeline';

export const MyAcceptancesPage = () => {
  const [acceptances, setAcceptances] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchAcceptances = async () => {
      try {
        const data = await acceptanceApi.getMyAcceptances();
        setAcceptances(Array.isArray(data) ? data : []);
      } catch (err) {
        console.error('Failed to fetch acceptances:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchAcceptances();
  }, []);

  const columns = [
    {
      header: 'Acceptance ID',
      accessor: 'acceptanceId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.acceptanceId || '').substring(0, 8)}</span>
    },
    {
      header: 'Request ID',
      accessor: 'bloodRequestId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.bloodRequestId || '').substring(0, 8)}</span>
    },
    {
      header: 'Acceptance Status',
      accessor: 'status',
      cell: (row) => {
        const val = row.status || 'Accepted';
        if (val === 'Verified' || val === 'ScreeningCompleted') return <Badge variant="success">{val}</Badge>;
        if (val === 'ScreeningPending') return <Badge variant="warning">{val}</Badge>;
        return <Badge variant="info">{val}</Badge>;
      }
    },
    {
      header: 'Accepted At',
      accessor: 'acceptedAt',
      cell: (row) => <span>{row.acceptedAt ? new Date(row.acceptedAt).toLocaleDateString() : 'Today'}</span>
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">My Donation Acceptances</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Monitor your active donation commitments and AI health screening progress.
        </p>
      </div>

      {acceptances.length > 0 && (
        <ScreeningProgressTimeline status={acceptances[0]?.status || 'Accepted'} />
      )}

      <DataTable
        columns={columns}
        data={acceptances}
        searchPlaceholder="Search acceptances..."
        emptyMessage="You have not accepted any donation requests yet."
      />
    </div>
  );
};

export default MyAcceptancesPage;
