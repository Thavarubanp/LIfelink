import { useState, useEffect } from 'react';
import { bloodRequestApi, acceptanceApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { useAuth } from '../../context/AuthContext';
import { isDonorAccount as isPlainDonor } from '../../utils/roleUtils';
import { Link } from 'react-router-dom';
import { Filter, ArrowRight, CheckCircle2, Loader2 } from 'lucide-react';

export const AvailableRequestsPage = () => {
  const [requests, setRequests] = useState([]);
  const [selectedBloodGroup, setSelectedBloodGroup] = useState('');
  const [requestsLoading, setRequestsLoading] = useState(true);
  const [acceptancesLoading, setAcceptancesLoading] = useState(true);
  const [acceptanceCheckFailed, setAcceptanceCheckFailed] = useState(false);
  const [acceptedRequestIds, setAcceptedRequestIds] = useState(() => new Set());
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
      header: 'Units',
      accessor: 'unitsRequired',
      cell: (row) => (
        <div>
          <span className="font-semibold text-slate-900 dark:text-slate-100">{row.remainingUnits ?? row.unitsRequired} of {row.unitsRequired} still needed</span>
          <div className="text-[10px] text-slate-400">{row.fulfilledUnits || 0} donated, {row.reservedUnits || 0} reserved</div>
          {!row.isAcceptingDonors && <Badge variant="info" size="sm">All slots reserved</Badge>}
        </div>
      )
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
      cell: (row) => {
        const requestId = row.bloodRequestId || row.id;
        const alreadyAccepted = donorAccount && acceptedRequestIds.has(requestId);

        if (alreadyAccepted) {
          return (
            <div className="flex flex-wrap items-center gap-2">
              <span className="inline-flex items-center gap-1 rounded-lg bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300">
                <CheckCircle2 className="w-3.5 h-3.5" /> Already Accepted
              </span>
              <Link to={`/donor/requests/${requestId}`} className="text-xs font-semibold text-slate-600 hover:underline dark:text-slate-300">
                View Details
              </Link>
            </div>
          );
        }

        if (donorAccount && acceptanceCheckFailed) {
          return (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold text-amber-700 dark:text-amber-300">Acceptance status unavailable</span>
              <Link to={`/donor/requests/${requestId}`} className="text-xs font-semibold text-slate-600 hover:underline dark:text-slate-300">
                View Details
              </Link>
            </div>
          );
        }

        return (
          <Link
            to={`/donor/requests/${requestId}`}
            className={`inline-flex items-center gap-1 px-3 py-1.5 text-white font-semibold text-xs rounded-lg shadow-sm ${donorAccount ? 'bg-red-600 hover:bg-red-700' : 'bg-slate-700 hover:bg-slate-800 dark:bg-slate-600 dark:hover:bg-slate-500'}`}
          >
            <span>{donorAccount ? 'View & Donate' : 'View Details'}</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        );
      }
    }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Available Blood Requests</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Browse verified public blood requests needing eligible donors.
        </p>
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
      {loading ? (
        <div className="ll-card flex min-h-48 items-center justify-center gap-2 p-8 text-sm text-slate-500 dark:text-slate-400" role="status">
          <Loader2 className="h-5 w-5 animate-spin text-red-600" />
          Loading available blood requests...
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={requests}
          searchPlaceholder="Search hospital, blood group, reason..."
          emptyMessage="No available blood requests matching filter."
        />
      )}
    </div>
  );
};

export default AvailableRequestsPage;
