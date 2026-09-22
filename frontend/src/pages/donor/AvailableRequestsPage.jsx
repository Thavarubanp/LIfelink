import React, { useState, useEffect } from 'react';
import { bloodRequestApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { Link } from 'react-router-dom';
import { Droplet, Filter, ArrowRight } from 'lucide-react';

export const AvailableRequestsPage = () => {
  const [requests, setRequests] = useState([]);
  const [selectedBloodGroup, setSelectedBloodGroup] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchRequests = async () => {
      setLoading(true);
      try {
        const data = await bloodRequestApi.getPublicRequests(selectedBloodGroup || null);
        setRequests(Array.isArray(data) ? data : []);
      } catch (err) {
        console.error('Failed to fetch public requests:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchRequests();
  }, [selectedBloodGroup]);

  const bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

  const columns = [
    {
      header: 'Blood Group',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge>
    },
    {
      header: 'Hospital',
      accessor: 'hospitalName',
      cell: (row) => (
        <div>
          <div className="font-bold text-slate-900 dark:text-slate-100">{row.hospitalName || 'St. Jude Memorial'}</div>
          <div className="text-[10px] text-slate-400">ID: #{String(row.bloodRequestId || '').substring(0, 8)}</div>
        </div>
      )
    },
    {
      header: 'Units Needed',
      accessor: 'unitsRequired',
      cell: (row) => <span className="font-semibold text-slate-900 dark:text-slate-100">{row.unitsRequired} Units</span>
    },
    {
      header: 'Priority',
      accessor: 'priority',
      cell: (row) => (
        <Badge variant={row.priority === 'CRITICAL' ? 'danger' : 'warning'}>
          {row.priority || 'URGENT'}
        </Badge>
      )
    },
    {
      header: 'Reason',
      accessor: 'reason',
      cell: (row) => <span className="text-slate-600 dark:text-slate-400 max-w-xs truncate block">{row.reason || 'Surgical Transfusion'}</span>
    },
    {
      header: 'Action',
      accessor: 'bloodRequestId',
      sortable: false,
      cell: (row) => (
        <Link
          to={`/donor/requests/${row.bloodRequestId || row.id}`}
          className="inline-flex items-center gap-1 px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-lg shadow-sm"
        >
          <span>View & Donate</span>
          <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      )
    }
  ];

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Available Blood Requests</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Browse verified public blood requests needing eligible donors.
          </p>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="flex flex-wrap items-center gap-2 p-3 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-sm">
        <span className="text-xs font-semibold text-slate-500 flex items-center gap-1 mr-2">
          <Filter className="w-3.5 h-3.5" /> Filter Blood Group:
        </span>
        <button
          onClick={() => setSelectedBloodGroup('')}
          className={`px-3 py-1 text-xs rounded-lg font-semibold transition-all ${
            selectedBloodGroup === ''
              ? 'bg-red-600 text-white shadow-sm'
              : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200'
          }`}
        >
          All Groups
        </button>
        {bloodGroups.map((bg) => (
          <button
            key={bg}
            onClick={() => setSelectedBloodGroup(bg)}
            className={`px-3 py-1 text-xs rounded-lg font-semibold transition-all ${
              selectedBloodGroup === bg
                ? 'bg-red-600 text-white shadow-sm'
                : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200'
            }`}
          >
            {bg}
          </button>
        ))}
      </div>

      {/* Data Table */}
      <DataTable
        columns={columns}
        data={requests}
        searchPlaceholder="Search hospital, blood group, reason..."
        emptyMessage="No available blood requests matching filter."
      />
    </div>
  );
};

export default AvailableRequestsPage;
