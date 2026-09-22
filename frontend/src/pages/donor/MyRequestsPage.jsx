import React, { useState, useEffect } from 'react';
import { bloodRequestApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';

export const MyRequestsPage = () => {
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(true);

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
  }, []);

  const columns = [
    {
      header: 'Request ID',
      accessor: 'bloodRequestId',
      cell: (row) => <span className="font-mono text-slate-500">#{String(row.bloodRequestId || '').substring(0, 8)}</span>
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
      cell: (row) => <Badge variant={row.priority === 'CRITICAL' ? 'danger' : 'warning'}>{row.priority || 'URGENT'}</Badge>
    },
    {
      header: 'Fulfillment Status',
      accessor: 'status',
      cell: (row) => <Badge variant="info">{row.status || 'VERIFIED'}</Badge>
    },
    {
      header: 'Reason',
      accessor: 'reason',
      cell: (row) => <span className="text-slate-500 truncate max-w-xs block">{row.reason}</span>
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">My Patient Blood Requests</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Track the verification and donor fulfillment status of your submitted requests.
        </p>
      </div>

      <DataTable
        columns={columns}
        data={requests}
        searchPlaceholder="Search my requests..."
        emptyMessage="You have not created any blood requests yet."
      />
    </div>
  );
};

export default MyRequestsPage;
