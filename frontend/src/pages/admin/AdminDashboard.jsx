import React, { useState, useEffect, useCallback } from 'react';
import { adminApi, doctorApi, hospitalApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { useAuth } from '../../context/AuthContext';
import { Badge } from '../../components/common/Badge';
import {
  ShieldAlert,
  ShieldCheck,
  Building2,
  Users,
  Stethoscope,
  AlertTriangle,
  FileText,
  Loader2,
  Search,
  X,
  ArrowLeft,
  ArrowRight,
  Calendar,
  AlertCircle
} from 'lucide-react';
import { Link } from 'react-router-dom';

// Robust helper to extract an array from any API response structure
const extractArray = (res) => {
  if (Array.isArray(res)) return res;
  if (Array.isArray(res?.data)) return res.data;
  if (Array.isArray(res?.data?.data)) return res.data.data;
  return [];
};

export const AdminDashboard = () => {
  const { addToast } = useNotification();
  const { user: currentUser, logout } = useAuth();
  const [stats, setStats] = useState(null);
  const [loadingStats, setLoadingStats] = useState(true);

  // Active directory category: null (default dashboard) | 'users' | 'hospitals' | 'doctors'
  const [selectedCategory, setSelectedCategory] = useState(null);

  // Directory lists state
  const [usersList, setUsersList] = useState([]);
  const [hospitalsList, setHospitalsList] = useState([]);
  const [doctorsList, setDoctorsList] = useState([]);
  const [doctorUserIds, setDoctorUserIds] = useState(new Set());
  const [loadingList, setLoadingList] = useState(false);
  const [filterQuery, setFilterQuery] = useState('');

  // Suspension modal state (doctors are managed by their hospital, not by admins)
  const [suspendModal, setSuspendModal] = useState({
    isOpen: false,
    type: '', // 'user' | 'hospital'
    id: null,
    name: '',
    email: ''
  });
  const [suspendReason, setSuspendReason] = useState('');
  const [suspendedUntil, setSuspendedUntil] = useState('');
  const [submittingAction, setSubmittingAction] = useState(false);

  // Fetch Dashboard KPI Stats
  const fetchStats = async () => {
    try {
      setLoadingStats(true);
      const res = await adminApi.getDashboardStats();
      setStats(res?.data || res);
    } catch (err) {
      console.error('Failed to fetch admin stats:', err);
    } finally {
      setLoadingStats(false);
    }
  };

  useEffect(() => {
    fetchStats();
  }, []);

  // Fetch data for the selected directory category
  const fetchCategoryData = useCallback(async (category) => {
    if (!category) return;
    setLoadingList(true);
    setFilterQuery('');
    try {
      if (category === 'users') {
        // Doctor logins appear in the user directory; they are managed by their hospital, not suspendable here
        const [res, doctorsRes] = await Promise.all([adminApi.getUsers(), doctorApi.getDoctors().catch(() => [])]);
        setUsersList(extractArray(res));
        setDoctorUserIds(new Set(extractArray(doctorsRes).map((d) => d.userId).filter(Boolean)));
      } else if (category === 'hospitals') {
        // Reuse existing hospitalApi.getHospitals() endpoint
        const res = await hospitalApi.getHospitals();
        setHospitalsList(extractArray(res));
      } else if (category === 'doctors') {
        // Reuse existing doctorApi.getDoctors() endpoint
        const res = await doctorApi.getDoctors();
        setDoctorsList(extractArray(res));
      }
    } catch (err) {
      console.error(`Failed to load ${category} directory:`, err);
      addToast({
        title: 'Load Error',
        message: `Unable to load ${category} records. Please try again.`,
        type: 'error'
      });
    } finally {
      setLoadingList(false);
    }
  }, [addToast]);

  useEffect(() => {
    if (selectedCategory) {
      fetchCategoryData(selectedCategory);
    }
  }, [selectedCategory, fetchCategoryData]);

  // Handle card click
  const handleCardClick = (category) => {
    if (selectedCategory === category) {
      // Toggle off to return to default dashboard if already active
      setSelectedCategory(null);
    } else {
      setSelectedCategory(category);
    }
  };

  // Open Suspend Modal
  const openSuspendModal = (type, item) => {
    let name = '';
    let email = item.email || '';
    if (type === 'user') {
      name = `${item.firstName || ''} ${item.lastName || ''}`.trim() || item.email;
    } else if (type === 'hospital') {
      name = item.name;
    }

    setSuspendModal({
      isOpen: true,
      type,
      id: item.userId || item.hospitalId,
      name,
      email
    });
    setSuspendReason('');
    setSuspendedUntil('');
  };

  // Submit Suspension
  const handleSuspendSubmit = async (e) => {
    e.preventDefault();
    if (!suspendReason.trim() || suspendReason.trim().length < 3) {
      addToast({
        title: 'Validation Error',
        message: 'Suspension reason must be at least 3 characters.',
        type: 'error'
      });
      return;
    }

    setSubmittingAction(true);
    const dto = {
      reason: suspendReason.trim(),
      suspendedUntil: suspendedUntil ? new Date(suspendedUntil).toISOString() : null
    };

    try {
      if (suspendModal.type === 'user') {
        await adminApi.suspendUser(suspendModal.id, dto);
        setUsersList((prev) =>
          prev.map((u) =>
            u.userId === suspendModal.id
              ? { ...u, isSuspended: true, accountStatus: 'Suspended', suspensionReason: dto.reason, suspendedUntil: dto.suspendedUntil }
              : u
          )
        );
      } else if (suspendModal.type === 'hospital') {
        await adminApi.suspendHospital(suspendModal.id, dto);
        setHospitalsList((prev) =>
          prev.map((h) =>
            h.hospitalId === suspendModal.id
              ? { ...h, isSuspended: true, suspensionReason: dto.reason, suspendedUntil: dto.suspendedUntil }
              : h
          )
        );
      }

      addToast({
        title: 'Account Suspended',
        message: `${suspendModal.name} has been suspended.`,
        type: 'success'
      });

      setSuspendModal({ isOpen: false, type: '', id: null, name: '', email: '' });
      fetchStats();
    } catch (err) {
      addToast({
        title: 'Action Failed',
        message: err.response?.data?.message || err.message || 'Suspension could not be executed.',
        type: 'error'
      });
    } finally {
      setSubmittingAction(false);
    }
  };

  // Permanent block (donor/patient accounts only): name stays visible, login and re-registration are refused
  const handleBlock = async (u) => {
    const name = `${u.firstName || ''} ${u.lastName || ''}`.trim();
    if (!window.confirm(`Permanently block ${name}? They can never sign in or register again with this email. Their history is kept. This cannot be undone.`)) {
      return;
    }
    try {
      await adminApi.blockUser(u.userId);
      setUsersList((prev) => prev.map((x) => (x.userId === u.userId ? { ...x, isPermanentlyBlocked: true, isSuspended: false, accountStatus: 'Blocked', roles: [] } : x)));
      addToast({ title: 'Account Permanently Blocked', message: `${name} has been permanently blocked.`, type: 'warning' });
      fetchStats();
    } catch (err) {
      addToast({ title: 'Action Failed', message: err.response?.data?.message || err.message, type: 'error' });
    }
  };

  // Admin ownership transfer: the selected user becomes the only Admin and you become a normal user (signed out)
  const handlePromote = async (u) => {
    const name = `${u.firstName || ''} ${u.lastName || ''}`.trim();
    if (!window.confirm(`Transfer Admin ownership to ${name}? You will immediately become a normal user and be signed out.`)) {
      return;
    }
    try {
      await adminApi.promoteToAdmin(u.userId);
      addToast({ title: 'Admin Ownership Transferred', message: `${name} is now the system administrator.`, type: 'success' });
      logout();
    } catch (err) {
      addToast({ title: 'Action Failed', message: err.response?.data?.message || err.message, type: 'error' });
    }
  };

  // Reinstate Action
  const handleReinstate = async (type, item) => {
    const id = item.userId || item.hospitalId;
    const name = item.name || `${item.firstName || ''} ${item.lastName || ''}`.trim();

    if (!window.confirm(`Are you sure you want to reinstate ${name} and restore active platform privileges?`)) {
      return;
    }

    try {
      if (type === 'user') {
        await adminApi.reinstateUser(id);
        setUsersList((prev) =>
          prev.map((u) =>
            u.userId === id
              ? { ...u, isSuspended: false, accountStatus: 'Active', suspensionReason: null, suspendedUntil: null }
              : u
          )
        );
      } else if (type === 'hospital') {
        await adminApi.reinstateHospital(id);
        setHospitalsList((prev) =>
          prev.map((h) =>
            h.hospitalId === id
              ? { ...h, isSuspended: false, suspensionReason: null, suspendedUntil: null }
              : h
          )
        );
      }

      addToast({
        title: 'Privileges Reinstated',
        message: `${name} has been restored to Active status.`,
        type: 'success'
      });
      fetchStats();
    } catch (err) {
      addToast({
        title: 'Reinstatement Failed',
        message: err.response?.data?.message || err.message || 'Unable to reinstate account.',
        type: 'error'
      });
    }
  };

  // Filtered lists
  const filteredUsers = usersList.filter((u) => {
    if (!filterQuery) return true;
    const q = filterQuery.toLowerCase();
    const fullName = `${u.firstName || ''} ${u.lastName || ''}`.toLowerCase();
    return fullName.includes(q) || (u.email && u.email.toLowerCase().includes(q));
  });

  const filteredHospitals = hospitalsList.filter((h) => {
    if (!filterQuery) return true;
    const q = filterQuery.toLowerCase();
    return (
      (h.name && h.name.toLowerCase().includes(q)) ||
      (h.email && h.email.toLowerCase().includes(q)) ||
      (h.city && h.city.toLowerCase().includes(q)) ||
      (h.licenseNumber && h.licenseNumber.toLowerCase().includes(q))
    );
  });

  const filteredDoctors = doctorsList.filter((d) => {
    if (!filterQuery) return true;
    const q = filterQuery.toLowerCase();
    const fullName = `${d.firstName || ''} ${d.lastName || ''}`.toLowerCase();
    return (
      fullName.includes(q) ||
      (d.email && d.email.toLowerCase().includes(q)) ||
      (d.specialization && d.specialization.toLowerCase().includes(q)) ||
      (d.hospitalName && d.hospitalName.toLowerCase().includes(q)) ||
      (d.licenseNumber && d.licenseNumber.toLowerCase().includes(q))
    );
  });

  return (
    <div className="space-y-6">
      {/* Top Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">
              System Governance & Admin Portal
            </h1>
          </div>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Platform metrics, registered entity governance, open complaints, and suspension appeals.
          </p>
        </div>
      </div>

      {/* 3 Clickable KPI Cards: Users, Hospitals, Doctors */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* Total Registered Users Card */}
        <div
          onClick={() => handleCardClick('users')}
          className={`cursor-pointer rounded-2xl p-5 border transition-all select-none ${
            selectedCategory === 'users'
              ? 'bg-blue-50/40 dark:bg-blue-950/20 border-blue-500 shadow-md ring-2 ring-blue-500/20'
              : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 shadow-sm'
          }`}
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Total Registered Users</span>
            <div className="w-9 h-9 rounded-xl bg-blue-100 dark:bg-blue-900/60 text-blue-600 dark:text-blue-300 flex items-center justify-center">
              <Users className="w-5 h-5" />
            </div>
          </div>
          <div className="text-3xl font-extrabold text-slate-900 dark:text-slate-100 mt-2">
            {loadingStats ? <Loader2 className="w-6 h-6 animate-spin text-slate-400" /> : stats?.totalUsers ?? 0}
          </div>
          <div className="flex items-center justify-between mt-3 pt-3 border-t border-slate-100 dark:border-slate-800 text-[11px]">
            <span className="text-slate-500 font-medium">
              {selectedCategory === 'users' ? 'Viewing Registered Users' : 'Click to inspect & govern users'}
            </span>
            <span className={`font-semibold ${selectedCategory === 'users' ? 'text-blue-600 dark:text-blue-400' : 'text-slate-400'}`}>
              {selectedCategory === 'users' ? '● Open (Click to Close)' : 'Inspect →'}
            </span>
          </div>
        </div>

        {/* Total Registered Hospitals Card */}
        <div
          onClick={() => handleCardClick('hospitals')}
          className={`cursor-pointer rounded-2xl p-5 border transition-all select-none ${
            selectedCategory === 'hospitals'
              ? 'bg-emerald-50/40 dark:bg-emerald-950/20 border-emerald-500 shadow-md ring-2 ring-emerald-500/20'
              : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 shadow-sm'
          }`}
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Total Registered Hospitals</span>
            <div className="w-9 h-9 rounded-xl bg-emerald-100 dark:bg-emerald-900/60 text-emerald-600 dark:text-emerald-300 flex items-center justify-center">
              <Building2 className="w-5 h-5" />
            </div>
          </div>
          <div className="text-3xl font-extrabold text-slate-900 dark:text-slate-100 mt-2">
            {loadingStats ? <Loader2 className="w-6 h-6 animate-spin text-slate-400" /> : stats?.totalHospitals ?? 0}
          </div>
          <div className="flex items-center justify-between mt-3 pt-3 border-t border-slate-100 dark:border-slate-800 text-[11px]">
            <span className="text-slate-500 font-medium">
              {selectedCategory === 'hospitals' ? 'Viewing Registered Hospitals' : `${stats?.pendingHospitalApprovals ?? 0} Pending Verification`}
            </span>
            <span className={`font-semibold ${selectedCategory === 'hospitals' ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'}`}>
              {selectedCategory === 'hospitals' ? '● Open (Click to Close)' : 'Inspect →'}
            </span>
          </div>
        </div>

        {/* Total Registered Doctors Card */}
        <div
          onClick={() => handleCardClick('doctors')}
          className={`cursor-pointer rounded-2xl p-5 border transition-all select-none ${
            selectedCategory === 'doctors'
              ? 'bg-purple-50/40 dark:bg-purple-950/20 border-purple-500 shadow-md ring-2 ring-purple-500/20'
              : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 shadow-sm'
          }`}
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">Total Registered Doctors</span>
            <div className="w-9 h-9 rounded-xl bg-purple-100 dark:bg-purple-900/60 text-purple-600 dark:text-purple-300 flex items-center justify-center">
              <Stethoscope className="w-5 h-5" />
            </div>
          </div>
          <div className="text-3xl font-extrabold text-slate-900 dark:text-slate-100 mt-2">
            {loadingStats ? <Loader2 className="w-6 h-6 animate-spin text-slate-400" /> : stats?.totalDoctors ?? 0}
          </div>
          <div className="flex items-center justify-between mt-3 pt-3 border-t border-slate-100 dark:border-slate-800 text-[11px]">
            <span className="text-slate-500 font-medium">
              {selectedCategory === 'doctors' ? 'Viewing Registered Doctors' : 'Click to view doctors (read-only)'}
            </span>
            <span className={`font-semibold ${selectedCategory === 'doctors' ? 'text-purple-600 dark:text-purple-400' : 'text-slate-400'}`}>
              {selectedCategory === 'doctors' ? '● Open (Click to Close)' : 'Inspect →'}
            </span>
          </div>
        </div>
      </div>

      {/* Directory Table View (when a category card is clicked) */}
      {selectedCategory && (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm overflow-hidden animate-in fade-in duration-200">
          {/* Table Header with Title, Search, and Visible Close (X) + Back Buttons */}
          <div className="p-4 border-b border-slate-100 dark:border-slate-800 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <div className="flex items-center gap-2">
                <span className="text-sm font-bold text-slate-900 dark:text-slate-100 capitalize">
                  {selectedCategory === 'users' && 'Registered Users Directory'}
                  {selectedCategory === 'hospitals' && 'Registered Hospitals Directory'}
                  {selectedCategory === 'doctors' && 'Registered Medical Doctors Directory'}
                </span>
                <span className="text-xs px-2.5 py-0.5 rounded-full bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400 font-semibold">
                  {selectedCategory === 'users' && filteredUsers.length}
                  {selectedCategory === 'hospitals' && filteredHospitals.length}
                  {selectedCategory === 'doctors' && filteredDoctors.length} Records
                </span>
              </div>
            </div>

            {/* Search Input & Action Buttons */}
            <div className="flex items-center gap-2">
              <div className="relative w-full sm:w-64">
                <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                <input
                  type="text"
                  value={filterQuery}
                  onChange={(e) => setFilterQuery(e.target.value)}
                  placeholder={`Search ${selectedCategory}...`}
                  className="w-full bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700/60 rounded-xl pl-9 pr-3 py-1.5 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                />
                {filterQuery && (
                  <button
                    onClick={() => setFilterQuery('')}
                    className="absolute right-2.5 top-2.5 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
                  >
                    <X className="w-3.5 h-3.5" />
                  </button>
                )}
              </div>

              {/* Single Visible Close (X) Button */}
              <button
                onClick={() => setSelectedCategory(null)}
                title="Close directory and return to dashboard"
                className="inline-flex items-center justify-center p-1.5 px-3 rounded-xl text-xs font-semibold text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-white bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 border border-slate-200 dark:border-slate-700 transition-colors shrink-0 gap-1.5"
              >
                <X className="w-4 h-4" />
                <span>Close</span>
              </button>
            </div>
          </div>

          {/* Table Content */}
          {loadingList ? (
            <div className="p-12 text-center text-slate-400 flex flex-col items-center justify-center gap-2">
              <Loader2 className="w-6 h-6 animate-spin text-red-600" />
              <span className="text-xs">Loading {selectedCategory} directory...</span>
            </div>
          ) : (
            <div className="overflow-x-auto">
              {/* VIEW 1: USERS LIST */}
              {selectedCategory === 'users' && (
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 dark:bg-slate-800/50 text-[10px] font-bold text-slate-400 uppercase tracking-wider border-b border-slate-100 dark:border-slate-800">
                    <tr>
                      <th className="p-3 pl-4">User</th>
                      <th className="p-3">Phone</th>
                      <th className="p-3">Status</th>
                      <th className="p-3">Registered Date</th>
                      <th className="p-3 pr-4 text-right">Governance Action</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                    {filteredUsers.length > 0 ? (
                      filteredUsers.map((u) => (
                        <tr key={u.userId} className="hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors">
                          <td className="p-3 pl-4">
                            <div className="flex items-center gap-3">
                              <div className="w-8 h-8 rounded-full bg-blue-100 dark:bg-blue-900/60 text-blue-700 dark:text-blue-300 font-bold flex items-center justify-center text-xs shrink-0">
                                {u.firstName ? u.firstName.charAt(0).toUpperCase() : 'U'}
                              </div>
                              <div className="min-w-0">
                                <Link
                                  to={`/profiles/user/${u.userId}`}
                                  className="font-semibold text-slate-900 dark:text-slate-100 hover:text-red-600 truncate block"
                                >
                                  {u.firstName} {u.lastName}
                                </Link>
                                <span className="text-[11px] text-slate-400 truncate block">{u.email}</span>
                              </div>
                            </div>
                          </td>
                          <td className="p-3 text-slate-600 dark:text-slate-300 font-mono">
                            {u.phoneNumber || '—'}
                          </td>
                          <td className="p-3">
                            {u.isPermanentlyBlocked ? (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-800 text-white border border-slate-700">
                                <AlertTriangle className="w-3 h-3" /> Permanently Blocked
                              </span>
                            ) : u.isSuspended ? (
                              <div>
                                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
                                  <AlertTriangle className="w-3 h-3" /> Suspended
                                </span>
                                {u.suspensionReason && (
                                  <p className="text-[10px] text-slate-400 mt-0.5 max-w-xs truncate" title={u.suspensionReason}>
                                    {u.suspensionReason}
                                  </p>
                                )}
                              </div>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-900">
                                <ShieldCheck className="w-3 h-3" /> Active
                              </span>
                            )}
                          </td>
                          <td className="p-3 text-slate-500">
                            {u.createdAt ? new Date(u.createdAt).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' }) : '—'}
                          </td>
                          <td className="p-3 pr-4 text-right">
                            {/* Governance actions apply only to donor/patient accounts (never the Admin, doctors or hospital staff) */}
                            {u.isPermanentlyBlocked ? (
                              <span className="text-[11px] font-semibold text-slate-400 italic">Permanently blocked</span>
                            ) : u.roles?.includes('Admin') ? (
                              <span className="text-[11px] font-semibold text-slate-400 italic">{u.userId === currentUser?.userId ? 'You (Admin)' : 'Admin'}</span>
                            ) : doctorUserIds.has(u.userId) || u.roles?.includes('Doctor') ? (
                              <span className="text-[11px] font-semibold text-slate-400 italic">Doctor (managed by hospital)</span>
                            ) : u.roles?.includes('HospitalStaff') ? (
                              <span className="text-[11px] font-semibold text-slate-400 italic">Hospital account (suspend via Hospitals)</span>
                            ) : (
                              <div className="inline-flex flex-wrap justify-end gap-1.5">
                                {u.isSuspended ? (
                                  <button
                                    onClick={() => handleReinstate('user', u)}
                                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800 transition-colors"
                                  >
                                    <ShieldCheck className="w-3.5 h-3.5" /> Reinstate User
                                  </button>
                                ) : (
                                  <>
                                    <button
                                      onClick={() => openSuspendModal('user', u)}
                                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
                                    >
                                      <ShieldAlert className="w-3.5 h-3.5" /> Suspend User
                                    </button>
                                    <button
                                      onClick={() => handlePromote(u)}
                                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-purple-50 text-purple-700 hover:bg-purple-100 dark:bg-purple-950/60 dark:text-purple-300 dark:hover:bg-purple-900 border border-purple-200 dark:border-purple-900 transition-colors"
                                    >
                                      <ShieldCheck className="w-3.5 h-3.5" /> Promote to Admin
                                    </button>
                                  </>
                                )}
                                <button
                                  onClick={() => handleBlock(u)}
                                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-900 hover:bg-rose-800 text-white border border-rose-700 transition-colors"
                                >
                                  <ShieldAlert className="w-3.5 h-3.5" /> Permanently Block
                                </button>
                              </div>
                            )}
                          </td>
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan={5} className="p-8 text-center text-slate-400">
                          No registered users found matching the filter.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}

              {/* VIEW 2: HOSPITALS LIST */}
              {selectedCategory === 'hospitals' && (
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 dark:bg-slate-800/50 text-[10px] font-bold text-slate-400 uppercase tracking-wider border-b border-slate-100 dark:border-slate-800">
                    <tr>
                      <th className="p-3 pl-4">Hospital Facility</th>
                      <th className="p-3">Contact</th>
                      <th className="p-3">Verification</th>
                      <th className="p-3">Governance Status</th>
                      <th className="p-3 pr-4 text-right">Governance Action</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                    {filteredHospitals.length > 0 ? (
                      filteredHospitals.map((h) => (
                        <tr key={h.hospitalId} className="hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors">
                          <td className="p-3 pl-4">
                            <div className="flex items-center gap-3">
                              <div className="w-8 h-8 rounded-full bg-cyan-100 dark:bg-cyan-900/60 text-cyan-700 dark:text-cyan-300 font-bold flex items-center justify-center text-xs shrink-0">
                                H
                              </div>
                              <div className="min-w-0">
                                <Link
                                  to={`/profiles/hospital/${h.hospitalId}`}
                                  className="font-semibold text-slate-900 dark:text-slate-100 hover:text-red-600 truncate block"
                                >
                                  {h.name}
                                </Link>
                                <span className="text-[11px] text-slate-400 truncate block">
                                  {h.city || h.address || 'Address not listed'} • Lic: {h.licenseNumber}
                                </span>
                              </div>
                            </div>
                          </td>
                          <td className="p-3">
                            <div className="text-slate-700 dark:text-slate-300 font-mono">{h.contactNumber || '—'}</div>
                            <div className="text-[11px] text-slate-400">{h.email}</div>
                          </td>
                          <td className="p-3">
                            {h.isVerified ? (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-cyan-100 text-cyan-800 dark:bg-cyan-950/60 dark:text-cyan-300 border border-cyan-200 dark:border-cyan-800">
                                Verified
                              </span>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border border-amber-200 dark:border-amber-800">
                                Pending
                              </span>
                            )}
                          </td>
                          <td className="p-3">
                            {h.isSuspended ? (
                              <div>
                                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
                                  <AlertTriangle className="w-3 h-3" /> Suspended
                                </span>
                                {h.suspensionReason && (
                                  <p className="text-[10px] text-slate-400 mt-0.5 max-w-xs truncate" title={h.suspensionReason}>
                                    {h.suspensionReason}
                                  </p>
                                )}
                              </div>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-900">
                                <ShieldCheck className="w-3 h-3" /> Active
                              </span>
                            )}
                          </td>
                          <td className="p-3 pr-4 text-right">
                            {h.isSuspended ? (
                              <button
                                onClick={() => handleReinstate('hospital', h)}
                                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800 transition-colors"
                              >
                                <ShieldCheck className="w-3.5 h-3.5" /> Reinstate Hospital
                              </button>
                            ) : (
                              <button
                                onClick={() => openSuspendModal('hospital', h)}
                                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
                              >
                                <ShieldAlert className="w-3.5 h-3.5" /> Suspend Hospital
                              </button>
                            )}
                          </td>
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan={5} className="p-8 text-center text-slate-400">
                          No registered hospitals found matching the filter.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}

              {/* VIEW 3: DOCTORS LIST */}
              {selectedCategory === 'doctors' && (
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 dark:bg-slate-800/50 text-[10px] font-bold text-slate-400 uppercase tracking-wider border-b border-slate-100 dark:border-slate-800">
                    <tr>
                      <th className="p-3 pl-4">Doctor Practitioner</th>
                      <th className="p-3">Specialization</th>
                      <th className="p-3">Affiliated Hospital</th>
                      <th className="p-3">Status</th>
                      <th className="p-3 pr-4 text-right">Governance</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                    {filteredDoctors.length > 0 ? (
                      filteredDoctors.map((d) => (
                        <tr key={d.doctorId} className="hover:bg-slate-50/50 dark:hover:bg-slate-800/30 transition-colors">
                          <td className="p-3 pl-4">
                            <div className="flex items-center gap-3">
                              <div className="w-8 h-8 rounded-full bg-purple-100 dark:bg-purple-900/60 text-purple-700 dark:text-purple-300 font-bold flex items-center justify-center text-xs shrink-0">
                                Dr
                              </div>
                              <div className="min-w-0">
                                <Link
                                  to={`/profiles/doctor/${d.doctorId}`}
                                  className="font-semibold text-slate-900 dark:text-slate-100 hover:text-red-600 truncate block"
                                >
                                  Dr. {d.firstName} {d.lastName}
                                </Link>
                                <span className="text-[11px] text-slate-400 truncate block">
                                  {d.email} • SLMC: {d.licenseNumber}
                                </span>
                              </div>
                            </div>
                          </td>
                          <td className="p-3 text-slate-700 dark:text-slate-300 font-medium">
                            {d.specialization || 'Clinical Medicine'}
                          </td>
                          <td className="p-3 text-slate-600 dark:text-slate-300">
                            {d.hospitalName || 'Affiliated Facility'}
                          </td>
                          <td className="p-3">
                            {d.isActive ? (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-900">
                                <ShieldCheck className="w-3 h-3" /> Active
                              </span>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
                                <AlertTriangle className="w-3 h-3" /> Inactive
                              </span>
                            )}
                          </td>
                          <td className="p-3 pr-4 text-right">
                            {/* Read-only: doctor lifecycle is owned by the hospital that created the doctor */}
                            <span className="text-[11px] font-semibold text-slate-400 italic">Managed by hospital</span>
                          </td>
                        </tr>
                      ))
                    ) : (
                      <tr>
                        <td colSpan={5} className="p-8 text-center text-slate-400">
                          No registered doctors found matching the filter.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}
            </div>
          )}

          {/* Directory Footer */}
          <div className="p-3.5 px-4 border-t border-slate-100 dark:border-slate-800 flex justify-between items-center text-xs text-slate-400 font-medium bg-slate-50/50 dark:bg-slate-900/50">
            <span>
              Showing {selectedCategory === 'users' && `${filteredUsers.length} registered users`}
              {selectedCategory === 'hospitals' && `${filteredHospitals.length} registered hospitals`}
              {selectedCategory === 'doctors' && `${filteredDoctors.length} registered medical doctors`}
            </span>
            <span className="text-[11px] text-slate-400">
              Use the Close (X) button to return to dashboard overview
            </span>
          </div>
        </div>
      )}

      {/* Default Dashboard Governance Operations Shortcuts (always available or highlighted) */}
      <div>
        <div className="mb-3">
          <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">Governance Operations Hub</h2>
          <p className="text-xs text-slate-400">Priority review queues and regulatory compliance workflows.</p>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <Link
            to="/admin/hospitals/pending"
            className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
          >
            <div>
              <Badge variant="warning">Action Required</Badge>
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Hospital Registration Requests</h3>
              <p className="text-xs text-slate-500 mt-1">Review newly registered hospitals seeking platform verification status.</p>
            </div>
            <span className="text-xs font-semibold text-red-600 mt-4 flex items-center gap-1">Review Requests <ArrowRight className="w-3.5 h-3.5" /></span>
          </Link>

          <Link
            to="/admin/complaints"
            className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
          >
            <div>
              <Badge variant="danger">Investigation Queue</Badge>
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Complaints Hub</h3>
              <p className="text-xs text-slate-500 mt-1">Inspect user reports, request hospital evidence, and resolve complaints.</p>
            </div>
            <span className="text-xs font-semibold text-red-600 mt-4 flex items-center gap-1">Manage Complaints <ArrowRight className="w-3.5 h-3.5" /></span>
          </Link>

          <Link
            to="/admin/appeals"
            className="p-5 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm hover:border-red-500 transition-all flex flex-col justify-between"
          >
            <div>
              <Badge variant="info">Reinstatement Queue</Badge>
              <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mt-2">Suspension Appeals</h3>
              <p className="text-xs text-slate-500 mt-1">Evaluate appeals from suspended accounts and restore platform access.</p>
            </div>
            <span className="text-xs font-semibold text-red-600 mt-4 flex items-center gap-1">Review Appeals <ArrowRight className="w-3.5 h-3.5" /></span>
          </Link>
        </div>
      </div>

      {/* Governance Suspension Modal */}
      {suspendModal.isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <ShieldAlert className="w-5 h-5 text-rose-600" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                  Suspend {suspendModal.type.charAt(0).toUpperCase() + suspendModal.type.slice(1)}
                </h3>
              </div>
              <button
                onClick={() => setSuspendModal({ isOpen: false, type: '', id: null, name: '', email: '' })}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <p className="text-xs text-slate-500 dark:text-slate-400">
              Executing administrative suspension for <span className="font-semibold text-slate-900 dark:text-slate-100">{suspendModal.name}</span>.
              This will block active platform access during the penalty period.
            </p>

            <form onSubmit={handleSuspendSubmit} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1">
                  Suspension Reason <span className="text-red-500">*</span>
                </label>
                <textarea
                  required
                  rows={3}
                  value={suspendReason}
                  onChange={(e) => setSuspendReason(e.target.value)}
                  placeholder="State the compliance policy violation or justification..."
                  className="w-full p-2.5 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1">
                  Suspended Until <span className="text-slate-400">(optional - leave blank for indefinite)</span>
                </label>
                <input
                  type="date"
                  value={suspendedUntil}
                  min={new Date().toISOString().split('T')[0]}
                  onChange={(e) => setSuspendedUntil(e.target.value)}
                  className="w-full p-2 text-xs bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
                />
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
                <button
                  type="button"
                  onClick={() => setSuspendModal({ isOpen: false, type: '', id: null, name: '', email: '' })}
                  disabled={submittingAction}
                  className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={submittingAction}
                  className="px-4 py-2 rounded-xl text-xs font-semibold bg-rose-600 hover:bg-rose-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
                >
                  {submittingAction ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" /> Processing...
                    </>
                  ) : (
                    <>
                      <ShieldAlert className="w-3.5 h-3.5" /> Confirm Suspension
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default AdminDashboard;
