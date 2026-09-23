import React, { useState, useEffect, useRef } from 'react';
import { complaintApi, hospitalApi, searchApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import ComplaintActivityTimeline from '../../components/complaints/ComplaintActivityTimeline';
import {
  AlertTriangle,
  Send,
  Loader2,
  Search,
  Building2,
  UserCheck,
  CheckCircle2,
  Clock,
  ShieldCheck,
  Sparkles,
  X,
  XCircle,
  Inbox,
  ChevronDown,
  ChevronUp,
  User
} from 'lucide-react';

const EMPTY_FORM = {
  complaintType: 'Hospital Service',
  subject: '',
  description: '',
  hospitalId: null,
  targetUserId: null
};

export const DonorComplaintsPage = () => {
  const { user } = useAuth();
  const { addToast } = useNotification();

  // Form State
  const [formData, setFormData] = useState(EMPTY_FORM);

  // Target Autocomplete State (doctors cannot be targeted; doctor-caused issues go against the hospital)
  const [targetCategory, setTargetCategory] = useState('HOSPITAL'); // 'HOSPITAL' | 'USER'
  const [targetSearch, setTargetSearch] = useState('');
  const [selectedTarget, setSelectedTarget] = useState(null);
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [hospitalsList, setHospitalsList] = useState([]);
  const [userResults, setUserResults] = useState([]);
  const [searchingUsers, setSearchingUsers] = useState(false);
  const [loadingTargets, setLoadingTargets] = useState(false);
  const dropdownRef = useRef(null);

  // Complaints History State
  const [myComplaints, setMyComplaints] = useState([]);
  const [loadingComplaints, setLoadingComplaints] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [expandedComplaintIds, setExpandedComplaintIds] = useState(new Set());
  const [cancellingId, setCancellingId] = useState(null);

  // Solve Modal State (Only complaint owner can mark solved)
  const [solveModal, setSolveModal] = useState({ isOpen: false, complaint: null, notes: '' });
  const [solvingId, setSolvingId] = useState(null);

  const toggleExpand = (id) => {
    setExpandedComplaintIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleOpenSolveModal = (complaint) => {
    setSolveModal({
      isOpen: true,
      complaint,
      notes: ''
    });
  };

  const handleConfirmSolve = async (e) => {
    e.preventDefault();
    if (!solveModal.complaint) return;
    const complaintId = solveModal.complaint.complaintId || solveModal.complaint.id;
    setSolvingId(complaintId);
    try {
      await complaintApi.solveComplaint(complaintId, { notes: solveModal.notes.trim() || undefined });
      addToast({
        title: 'Complaint Marked as Solved',
        message: 'Grievance resolved and permanently closed. It remains visible in your history.',
        type: 'success'
      });
      setSolveModal({ isOpen: false, complaint: null, notes: '' });
      await loadComplaintsHistory();
    } catch (err) {
      addToast({
        title: 'Action Failed',
        message: err.response?.data?.message || 'Could not mark complaint as solved.',
        type: 'error'
      });
    } finally {
      setSolvingId(null);
    }
  };

  const handleCancelComplaint = async (complaint) => {
    const complaintId = complaint.complaintId || complaint.id;
    if (
      !window.confirm(
        `Are you sure you want to cancel this complaint ("${complaint.subject}")? Once cancelled, this complaint and its entire activity history will be permanently deleted from the database.`
      )
    ) {
      return;
    }

    setCancellingId(complaintId);
    try {
      await complaintApi.cancelComplaint(complaintId);
      addToast({
        title: 'Complaint Cancelled',
        message: 'Your complaint has been permanently cancelled and removed from the system.',
        type: 'info'
      });
      await loadComplaintsHistory();
    } catch (err) {
      addToast({
        title: 'Action Failed',
        message: err.response?.data?.message || 'Could not cancel complaint.',
        type: 'error'
      });
    } finally {
      setCancellingId(null);
    }
  };

  // Fetch Registered Directories for Target Autocomplete
  useEffect(() => {
    const loadInitialData = async () => {
      setLoadingTargets(true);
      try {
        const hospitalsData = await hospitalApi.getHospitals();
        if (Array.isArray(hospitalsData)) {
          setHospitalsList(hospitalsData);
        }
      } catch (err) {
        console.error('Failed to load target directories:', err);
      } finally {
        setLoadingTargets(false);
      }

      loadComplaintsHistory();
    };

    loadInitialData();
  }, [user]);

  // Real-user search for the USER target (reuses the global search API; min 2 characters, debounced)
  const userSearchActive = targetCategory === 'USER' && !selectedTarget && targetSearch.trim().length >= 2;

  useEffect(() => {
    if (!userSearchActive) return;
    const query = targetSearch.trim();

    const timer = setTimeout(async () => {
      setSearchingUsers(true);
      try {
        const data = await searchApi.globalSearch(query);
        // The search already excludes doctor and hospital accounts; also drop admins and yourself
        const users = (data?.users || []).filter((u) => u.extraInfo !== 'Admin' && u.id !== user?.userId);
        setUserResults(users);
      } catch {
        setUserResults([]);
      } finally {
        setSearchingUsers(false);
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [userSearchActive, targetSearch, user?.userId]);

  // Close dropdown on click outside
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setIsDropdownOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const loadComplaintsHistory = async () => {
    setLoadingComplaints(true);
    try {
      const res = await complaintApi.getMyComplaints();
      const list = res?.data || (Array.isArray(res) ? res : []);
      const sorted = list.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
      setMyComplaints(sorted);
    } catch (e) {
      console.error('Error loading complaints from database:', e);
      setMyComplaints([]);
    } finally {
      setLoadingComplaints(false);
    }
  };

  // Filter Targets based on Active Target Category (Hospital or User)
  const getFilteredTargets = () => {
    const query = targetSearch.trim().toLowerCase();

    if (targetCategory === 'USER') {
      // Only real registered users returned by the search API can be selected
      if (!userSearchActive) return [];
      return userResults.map((u) => ({
        id: u.id,
        name: u.displayName,
        type: 'Donor / Patient',
        subtitle: u.subText || 'Registered platform user',
        hospitalId: null,
        targetUserId: u.id
      }));
    }

    // Default: HOSPITAL
    const hospitalResults = hospitalsList.map((h) => ({
      id: h.hospitalId,
      name: h.name,
      type: 'Registered Hospital',
      subtitle: `${h.licenseNumber || 'Verified Medical Center'} • ${h.email || h.address || 'Sri Lanka'}`,
      hospitalId: h.hospitalId,
      raw: h
    }));

    if (!query) return hospitalResults;
    return hospitalResults.filter(
      (item) =>
        item.name.toLowerCase().includes(query) ||
        item.subtitle.toLowerCase().includes(query)
    );
  };

  const handleSelectTarget = (target) => {
    setSelectedTarget(target);
    setTargetSearch(target.name);
    setIsDropdownOpen(false);
    setFormData((prev) => ({
      ...prev,
      hospitalId: target.hospitalId || null,
      targetUserId: target.targetUserId || null
    }));
  };

  const handleClearTarget = () => {
    setSelectedTarget(null);
    setTargetSearch('');
    setUserResults([]);
    setFormData((prev) => ({ ...prev, hospitalId: null, targetUserId: null }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();

    // Typed text that was never picked from the list is not a valid target
    if (targetSearch.trim() && !selectedTarget) {
      addToast({
        title: 'Select a Target',
        message: `Choose a ${targetCategory === 'USER' ? 'registered user' : 'hospital'} from the list, or clear the target field.`,
        type: 'error'
      });
      return;
    }

    setSubmitting(true);

    try {
      const payload = {
        complaintType: formData.complaintType,
        subject: selectedTarget
          ? `[Target: ${selectedTarget.type} - ${selectedTarget.name}] ${formData.subject.trim()}`
          : formData.subject.trim(),
        description: formData.description.trim(),
        hospitalId: formData.hospitalId,
        targetUserId: formData.targetUserId
      };

      await complaintApi.createComplaint(payload);

      addToast({
        title: 'Report Submitted Successfully',
        message: 'Your report has been logged and assigned to system administration for investigation.',
        type: 'success'
      });

      // Reset Form & Reload Database Complaints
      setFormData(EMPTY_FORM);
      setSelectedTarget(null);
      setTargetSearch('');
      await loadComplaintsHistory();
    } catch (err) {
      addToast({
        title: 'Submission Failed',
        message: err.response?.data?.message || err.message || 'Could not file complaint.',
        type: 'error'
      });
    } finally {
      setSubmitting(false);
    }
  };

  // Status Badge Config
  const getStatusBadge = (status) => {
    const normalized = (status || 'OPEN').toUpperCase();
    if (normalized === 'RESOLVED') {
      return (
        <span className="px-2.5 py-1 rounded-full text-[11px] font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/30 flex items-center gap-1.5 shrink-0">
          <CheckCircle2 className="w-3 h-3" />
          <span>Resolved</span>
        </span>
      );
    }
    if (normalized === 'UNDER_REVIEW' || normalized === 'AWAITING_INFORMATION' || normalized === 'IN PROGRESS') {
      return (
        <span className="px-2.5 py-1 rounded-full text-[11px] font-bold bg-blue-500/10 text-blue-400 border border-blue-500/30 flex items-center gap-1.5 shrink-0">
          <Clock className="w-3 h-3 animate-spin" />
          <span>Under Investigation</span>
        </span>
      );
    }
    if (normalized === 'CANCELLED') {
      return (
        <span className="px-2.5 py-1 rounded-full text-[11px] font-bold bg-rose-500/10 text-rose-400 border border-rose-500/30 flex items-center gap-1.5 shrink-0">
          <XCircle className="w-3 h-3" />
          <span>Closed / Cancelled</span>
        </span>
      );
    }
    if (normalized === 'REJECTED') {
      return (
        <span className="px-2.5 py-1 rounded-full text-[11px] font-bold bg-red-500/10 text-red-400 border border-red-500/30 flex items-center gap-1.5 shrink-0">
          <X className="w-3 h-3" />
          <span>Rejected</span>
        </span>
      );
    }
    return (
      <span className="px-2.5 py-1 rounded-full text-[11px] font-bold bg-amber-500/10 text-amber-400 border border-amber-500/30 flex items-center gap-1.5 shrink-0">
        <Clock className="w-3 h-3 animate-pulse" />
        <span>Pending Review</span>
      </span>
    );
  };

  // 3-Stage Progress Timeline Component
  const renderTimeline = (status, hasReply) => {
    const norm = (status || 'OPEN').toUpperCase();
    let step = 1;
    if (norm === 'UNDER_REVIEW' || norm === 'AWAITING_INFORMATION' || norm === 'IN PROGRESS') step = 2;
    if (norm === 'RESOLVED' || norm === 'REJECTED' || hasReply) step = 3;

    return (
      <div className="pt-3 pb-1 border-t border-slate-800/80">
        <div className="flex items-center justify-between text-[10px] font-semibold text-slate-400 mb-1.5">
          <span className={step >= 1 ? 'text-cyan-400' : ''}>1. Submitted</span>
          <span className={step >= 2 ? 'text-blue-400' : ''}>2. In Review</span>
          <span className={step >= 3 ? (norm === 'REJECTED' ? 'text-red-400' : 'text-emerald-400') : ''}>
            3. Action Taken
          </span>
        </div>
        <div className="w-full bg-slate-800 rounded-full h-1.5 flex overflow-hidden">
          <div className={`h-full transition-all duration-500 ${step >= 1 ? 'bg-cyan-500 w-1/3' : 'w-0'}`} />
          <div className={`h-full transition-all duration-500 ${step >= 2 ? 'bg-blue-500 w-1/3' : 'w-0'}`} />
          <div
            className={`h-full transition-all duration-500 ${
              step >= 3 ? (norm === 'REJECTED' ? 'bg-red-500 w-1/3' : 'bg-emerald-500 w-1/3') : 'w-0'
            }`}
          />
        </div>
      </div>
    );
  };

  return (
    <div className="max-w-4xl mx-auto space-y-8">
      {/* Header */}
      <div>
        <div className="flex items-center gap-2 mb-1">
          <span className="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-red-500/10 text-red-400 border border-red-500/20 uppercase tracking-wider">
            Governance & Quality Assurance
          </span>
        </div>
        <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-100 tracking-tight">
          Platform Incident & Hospital Complaint Center
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
          Submit formal grievances regarding hospital procedures, donor treatment, or platform operations. Every report is audited by System Administration.
        </p>
      </div>

      {/* Complaint Form Card */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-xl relative overflow-hidden">
        <div className="flex items-center gap-2 mb-4 pb-3 border-b border-slate-100 dark:border-slate-800">
          <AlertTriangle className="w-5 h-5 text-red-500 shrink-0" />
          <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100">File a New Complaint / Report</h2>
        </div>

        <form onSubmit={handleSubmit} className="space-y-5 text-xs">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Category Select */}
            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                Complaint Category *
              </label>
              <select
                value={formData.complaintType}
                onChange={(e) => setFormData({ ...formData, complaintType: e.target.value })}
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 transition-colors"
              >
                <option value="Hospital Service">Hospital Service / Facility Misconduct</option>
                <option value="Donor Screening">Donor Screening Dispute</option>
                <option value="Medical Staff">Medical Staff Professional Misconduct</option>
                <option value="Platform Issue">Technical / System Operation Bug</option>
                <option value="Other">Other Operational Concern</option>
              </select>
            </div>

            {/* Target Selection: Hospital, Doctor, or User */}
            <div className="relative" ref={dropdownRef}>
              <div className="flex items-center justify-between mb-1.5">
                <label className="block text-slate-700 dark:text-slate-300 font-semibold uppercase tracking-wider text-[11px]">
                  Target Entity
                </label>
                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() => {
                      setTargetCategory('HOSPITAL');
                      handleClearTarget();
                    }}
                    className={`px-2 py-0.5 rounded text-[10px] font-bold border transition-colors flex items-center gap-1 ${
                      targetCategory === 'HOSPITAL'
                        ? 'bg-cyan-50 text-cyan-700 dark:bg-cyan-950/60 dark:text-cyan-300 border-cyan-300 dark:border-cyan-800'
                        : 'bg-slate-100 dark:bg-slate-800 text-slate-500 border-slate-200 dark:border-slate-700 hover:text-slate-700'
                    }`}
                  >
                    <Building2 className="w-3 h-3" /> Hospital
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setTargetCategory('USER');
                      handleClearTarget();
                    }}
                    className={`px-2 py-0.5 rounded text-[10px] font-bold border transition-colors flex items-center gap-1 ${
                      targetCategory === 'USER'
                        ? 'bg-purple-50 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border-purple-300 dark:border-purple-800'
                        : 'bg-slate-100 dark:bg-slate-800 text-slate-500 border-slate-200 dark:border-slate-700 hover:text-slate-700'
                    }`}
                  >
                    <User className="w-3 h-3" /> User
                  </button>
                </div>
              </div>

              <div className="relative">
                <Search className="w-4 h-4 text-slate-400 absolute left-3.5 top-3 pointer-events-none" />
                <input
                  type="text"
                  value={targetSearch}
                  onFocus={() => setIsDropdownOpen(true)}
                  onChange={(e) => {
                    setTargetSearch(e.target.value);
                    setIsDropdownOpen(true);
                    if (selectedTarget && e.target.value !== selectedTarget.name) {
                      setSelectedTarget(null);
                      setFormData((prev) => ({ ...prev, hospitalId: null, targetUserId: null }));
                    }
                  }}
                  placeholder={
                    targetCategory === 'HOSPITAL'
                      ? 'Search registered hospitals...'
                      : 'Search registered users by name or email...'
                  }
                  className="w-full pl-10 pr-9 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 transition-colors"
                />
                {selectedTarget ? (
                  <button
                    type="button"
                    onClick={handleClearTarget}
                    className="absolute right-3 top-3 text-slate-400 hover:text-red-400 transition-colors"
                    title="Clear selection"
                  >
                    <X className="w-4 h-4" />
                  </button>
                ) : null}
              </div>

              {/* Selected Target Badge Callout */}
              {selectedTarget && (
                <div className="mt-2 p-2 px-3 bg-red-500/10 border border-red-500/20 rounded-lg flex items-center justify-between text-[11px] text-red-300">
                  <div className="flex items-center gap-2">
                    {targetCategory === 'USER' ? (
                      <User className="w-4 h-4 text-purple-400 shrink-0" />
                    ) : (
                      <Building2 className="w-4 h-4 text-cyan-400 shrink-0" />
                    )}
                    <div>
                      <span className="font-bold text-white">{selectedTarget.name}</span>
                      <span className="text-[10px] text-slate-400 ml-1.5">({selectedTarget.type})</span>
                    </div>
                  </div>
                  <span className="text-[10px] text-emerald-400 font-semibold">Target Linked</span>
                </div>
              )}

              {/* Autocomplete Results Dropdown */}
              {isDropdownOpen && (
                <div className="absolute z-30 top-full left-0 right-0 mt-1 max-h-56 overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-xl shadow-2xl divide-y divide-slate-100 dark:divide-slate-800 animate-in fade-in">
                  {loadingTargets || (userSearchActive && searchingUsers) ? (
                    <div className="p-3 text-center text-slate-400 flex items-center justify-center gap-2 text-xs">
                      <Loader2 className="w-4 h-4 animate-spin text-cyan-500" />
                      <span>{loadingTargets ? 'Loading target directory...' : 'Searching registered users...'}</span>
                    </div>
                  ) : targetCategory === 'USER' && targetSearch.trim().length < 2 ? (
                    <div className="p-3.5 text-center text-slate-400 text-xs">
                      Type at least 2 characters of a user's name or email.
                    </div>
                  ) : getFilteredTargets().length > 0 ? (
                    getFilteredTargets().map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        onClick={() => handleSelectTarget(item)}
                        className="w-full p-2.5 px-3.5 text-left hover:bg-slate-100 dark:hover:bg-slate-800/80 flex items-center justify-between transition-colors group"
                      >
                        <div className="flex items-center gap-2.5 min-w-0">
                          <div className="w-7 h-7 rounded-lg flex items-center justify-center shrink-0 bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                            {item.type === 'Donor / Patient' ? (
                              <User className="w-3.5 h-3.5 text-purple-400" />
                            ) : (
                              <Building2 className="w-3.5 h-3.5 text-cyan-400" />
                            )}
                          </div>
                          <div className="truncate">
                            <p className="font-semibold text-slate-900 dark:text-slate-100 group-hover:text-red-400 transition-colors truncate">
                              {item.name}
                            </p>
                            <p className="text-[10px] text-slate-500 dark:text-slate-400 truncate">{item.subtitle}</p>
                          </div>
                        </div>
                        <span className="text-[10px] font-bold px-2 py-0.5 rounded uppercase tracking-wider shrink-0 bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
                          {item.type}
                        </span>
                      </button>
                    ))
                  ) : (
                    <div className="p-3.5 text-center text-slate-400 text-xs">
                      No matching registered {targetCategory === 'USER' ? 'users' : 'hospitals'} found.
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>

          {/* Subject Field */}
          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
              Complaint Subject * (Min 5, Max 200 chars)
            </label>
            <input
              type="text"
              required
              minLength={5}
              maxLength={200}
              value={formData.subject}
              onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
              placeholder="e.g. Unprofessional conduct during blood request verification"
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 transition-colors"
            />
          </div>

          {/* Detailed Description */}
          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
              Detailed Incident Description & Evidence * (Min 10, Max 2000 chars)
            </label>
            <textarea
              required
              rows={4}
              minLength={10}
              maxLength={2000}
              value={formData.description}
              onChange={(e) => setFormData({ ...formData, description: e.target.value })}
              placeholder="Describe what occurred, dates, time, personnel involved, and location..."
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 resize-none transition-colors"
            />
          </div>

          <button
            type="submit"
            disabled={submitting}
            className="w-full py-3 bg-red-600 hover:bg-red-700 text-white font-semibold rounded-xl shadow-lg shadow-red-600/25 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
          >
            {submitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Logging Formal Grievance...</span>
              </>
            ) : (
              <>
                <span>Submit Grievance Report</span>
                <Send className="w-4 h-4" />
              </>
            )}
          </button>
        </form>
      </div>

      {/* "My Complaints" Section Driven Exclusively by Database */}
      <div className="space-y-4">
        <div className="flex items-center justify-between pt-2 border-t border-slate-200 dark:border-slate-800">
          <div className="flex items-center gap-2">
            <Inbox className="w-5 h-5 text-cyan-500" />
            <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">My Submitted Complaints</h2>
            <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-slate-800 text-slate-300 border border-slate-700">
              {myComplaints.length}
            </span>
          </div>
          <button
            type="button"
            onClick={loadComplaintsHistory}
            className="text-xs text-cyan-400 hover:underline flex items-center gap-1 font-medium"
          >
            <span>Refresh Complaints</span>
          </button>
        </div>

        {loadingComplaints ? (
          <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl">
            <Loader2 className="w-8 h-8 text-red-500 animate-spin mx-auto mb-2" />
            <p className="text-xs text-slate-400 font-medium">Retrieving grievance history from database...</p>
          </div>
        ) : myComplaints.length === 0 ? (
          <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl">
            <ShieldCheck className="w-10 h-10 text-slate-600 mx-auto mb-2" />
            <h3 className="text-sm font-bold text-slate-300">No Complaints Submitted Yet</h3>
            <p className="text-xs text-slate-500 mt-1">No complaint records found for your account in the database.</p>
          </div>
        ) : (
          <div className="space-y-4">
            {myComplaints.map((item) => {
              const complaintId = item.complaintId || item.id;
              const isExpanded = expandedComplaintIds.has(complaintId);
              const statusUpper = (item.status || 'OPEN').toUpperCase();
              const isClosed = statusUpper === 'CANCELLED' || statusUpper === 'RESOLVED' || statusUpper === 'REJECTED';
              const hasAdminReply = Boolean(item.resolutionNotes || item.adminResponse || item.status === 'RESOLVED');

              return (
                <div
                  key={complaintId}
                  className={`rounded-2xl p-5 border transition-all duration-300 shadow-md ${
                    hasAdminReply
                      ? 'bg-gradient-to-b from-purple-950/20 via-slate-900 to-slate-900 border-purple-500/40 shadow-purple-950/10'
                      : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800'
                  }`}
                >
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-3">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-mono text-[10px] font-bold text-slate-400 bg-slate-100 dark:bg-slate-800 px-2 py-0.5 rounded-md">
                        #{String(complaintId).substring(0, 8)}
                      </span>

                      <span className="px-2.5 py-0.5 rounded-md text-[10px] font-bold bg-slate-800 text-slate-300 border border-slate-700 uppercase tracking-wider">
                        {item.complaintType || 'Grievance'}
                      </span>

                      {(item.hospitalName || item.targetName) && (
                        <span className="px-2.5 py-0.5 rounded-md text-[10px] font-bold bg-cyan-500/10 text-cyan-400 border border-cyan-500/20 flex items-center gap-1">
                          <Building2 className="w-3 h-3" />
                          <span>Target: {item.hospitalName || item.targetName}</span>
                        </span>
                      )}

                      {hasAdminReply && (
                        <span className="px-2.5 py-0.5 rounded-md text-[10px] font-bold bg-purple-500/20 text-purple-300 border border-purple-500/40 flex items-center gap-1.5 animate-pulse">
                          <span className="w-2 h-2 rounded-full bg-purple-400 animate-ping shrink-0" />
                          <Sparkles className="w-3 h-3 text-purple-300" />
                          <span>Admin Replied</span>
                        </span>
                      )}
                    </div>

                    {getStatusBadge(item.status)}
                  </div>

                  <div className="mb-3">
                    <h3 className="text-base font-bold text-slate-900 dark:text-slate-100">{item.subject}</h3>
                    <p className="text-[11px] text-slate-400 mt-0.5">
                      Submitted on{' '}
                      <span className="font-semibold text-slate-300">
                        {new Date(item.createdAt).toLocaleDateString(undefined, {
                          year: 'numeric',
                          month: 'short',
                          day: 'numeric',
                          hour: '2-digit',
                          minute: '2-digit'
                        })}
                      </span>
                    </p>
                  </div>

                  {/* Actions & Toggle Bar */}
                  <div className="flex items-center justify-between gap-3 pt-3 border-t border-slate-100 dark:border-slate-800/80 flex-wrap">
                    <div>
                      {!isClosed ? (
                        <div className="flex items-center gap-2">
                          <button
                            type="button"
                            onClick={() => handleOpenSolveModal(item)}
                            disabled={solvingId === complaintId}
                            className="px-3 py-1.5 bg-emerald-50 hover:bg-emerald-100 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300 dark:hover:bg-emerald-900 border border-emerald-200 dark:border-emerald-800 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition-colors disabled:opacity-50"
                          >
                            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" />
                            <span>Mark as Solved</span>
                          </button>
                          <button
                            type="button"
                            onClick={() => handleCancelComplaint(item)}
                            disabled={cancellingId === complaintId}
                            className="px-3 py-1.5 bg-rose-50 hover:bg-rose-100 text-rose-700 dark:bg-rose-950/40 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-800 rounded-xl text-xs font-semibold flex items-center gap-1.5 transition-colors disabled:opacity-50"
                          >
                            {cancellingId === complaintId ? (
                              <Loader2 className="w-3.5 h-3.5 animate-spin" />
                            ) : (
                              <XCircle className="w-3.5 h-3.5 text-rose-500" />
                            )}
                            <span>Cancel Complaint</span>
                          </button>
                        </div>
                      ) : (
                        <span className="text-xs font-semibold text-slate-400 italic px-2.5 py-1 bg-slate-100 dark:bg-slate-800/80 rounded-lg border border-slate-200 dark:border-slate-700">
                          {statusUpper === 'RESOLVED'
                            ? 'Complaint Solved (Read-Only)'
                            : statusUpper === 'REJECTED'
                            ? 'Complaint Rejected (Read-Only)'
                            : 'Complaint Closed (Read-Only)'}
                        </span>
                      )}
                    </div>

                    <button
                      type="button"
                      onClick={() => toggleExpand(complaintId)}
                      className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border transition-all ${
                        isExpanded
                          ? 'bg-slate-200 dark:bg-slate-700 text-slate-900 dark:text-slate-100 border-slate-300 dark:border-slate-600'
                          : 'bg-slate-50 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-700'
                      }`}
                    >
                      <span>{isExpanded ? 'Hide Details' : 'View Details & Timeline'}</span>
                      {isExpanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                    </button>
                  </div>

                  {/* Expanded Details Panel */}
                  {isExpanded && (
                    <div className="mt-4 pt-4 border-t border-slate-100 dark:border-slate-800/80 space-y-5 animate-in fade-in duration-200">
                      {/* Description */}
                      <div className="p-3.5 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-200 dark:border-slate-800/80 text-xs text-slate-700 dark:text-slate-300 leading-relaxed">
                        <span className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider mb-1">
                          Grievance Description
                        </span>
                        {item.description}
                      </div>

                      {/* Official Admin Reply */}
                      {hasAdminReply && (
                        <div className="p-4 bg-purple-950/40 border border-purple-500/40 rounded-xl relative overflow-hidden shadow-inner">
                          <div className="flex items-center gap-2 mb-2 text-xs font-bold text-purple-300">
                            <ShieldCheck className="w-4 h-4 text-purple-400 shrink-0" />
                            <span>Official LifeLink System Admin Response</span>
                          </div>
                          <p className="text-xs text-slate-200 leading-relaxed font-medium">
                            {item.resolutionNotes || item.adminResponse || 'Your complaint has been formally reviewed and addressed by System Governance Administration.'}
                          </p>
                          {item.assignedAdminEmail && (
                            <p className="text-[10px] text-purple-300/80 mt-2 italic">
                              Reviewed by: {item.assignedAdminEmail}
                            </p>
                          )}
                        </div>
                      )}

                      {/* Attached Hospital Evidence Reports */}
                      {Array.isArray(item.activityReports) && item.activityReports.length > 0 && (
                        <div className="space-y-2">
                          <div className="flex items-center gap-2">
                            <Building2 className="w-4 h-4 text-cyan-400" />
                            <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100">
                              Hospital Activity Evidence Reports ({item.activityReports.length})
                            </h4>
                          </div>
                          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                            {item.activityReports.map((report) => (
                              <div
                                key={report.reportId}
                                className="p-3 bg-slate-50 dark:bg-slate-950/60 border border-cyan-500/30 rounded-xl text-xs space-y-1"
                              >
                                <div className="flex items-center justify-between">
                                  <span className="font-bold text-cyan-400">{report.title}</span>
                                  <span className="text-[10px] text-slate-400 font-mono">
                                    {new Date(report.submittedAt).toLocaleDateString()}
                                  </span>
                                </div>
                                <p className="text-slate-300">{report.description}</p>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Chronological Activity Timeline */}
                      <div className="space-y-2 pt-2">
                        <div className="flex items-center justify-between pb-1.5 border-b border-slate-200 dark:border-slate-800">
                          <div className="flex items-center gap-2">
                            <Clock className="w-4 h-4 text-slate-400" />
                            <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100 uppercase tracking-wider">
                              Activity History Timeline
                            </h4>
                          </div>
                          <span className="text-[10px] text-slate-400">Chronological case progress</span>
                        </div>
                        <ComplaintActivityTimeline complaint={item} />
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Solve Complaint Confirmation Modal */}
      {solveModal.isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-5 h-5 text-emerald-500" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                  Mark Complaint as Solved
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setSolveModal({ isOpen: false, complaint: null, notes: '' })}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <p className="text-xs text-slate-500 dark:text-slate-400">
              Are you satisfied that the grievance regarding{' '}
              <strong className="text-slate-900 dark:text-slate-100">{solveModal.complaint?.subject}</strong> has been resolved?
              Once marked as solved, the complaint becomes permanently read-only.
            </p>

            <form onSubmit={handleConfirmSolve} className="space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-slate-700 dark:text-slate-300 mb-1">
                  Resolution Notes (Optional)
                </label>
                <textarea
                  rows={3}
                  value={solveModal.notes}
                  onChange={(e) => setSolveModal({ ...solveModal, notes: e.target.value })}
                  placeholder="Describe how the matter was remedied (e.g. Received clarification, medical team apologized, hospital adjusted record)..."
                  className="w-full p-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-red-500"
                />
              </div>

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
                <button
                  type="button"
                  onClick={() => setSolveModal({ isOpen: false, complaint: null, notes: '' })}
                  disabled={Boolean(solvingId)}
                  className="px-4 py-2 rounded-xl font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                >
                  Keep Open
                </button>
                <button
                  type="submit"
                  disabled={Boolean(solvingId)}
                  className="px-4 py-2 rounded-xl font-semibold bg-emerald-600 hover:bg-emerald-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
                >
                  {solvingId ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" /> Closing case...
                    </>
                  ) : (
                    <>
                      <CheckCircle2 className="w-3.5 h-3.5" /> Confirm Solved
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

export default DonorComplaintsPage;
