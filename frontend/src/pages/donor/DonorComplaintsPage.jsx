import React, { useState, useEffect, useRef } from 'react';
import { complaintApi, hospitalApi, adminApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
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
  Inbox
} from 'lucide-react';

export const DonorComplaintsPage = () => {
  const { user } = useAuth();
  const { addToast } = useNotification();

  // Form State
  const [formData, setFormData] = useState({
    complaintType: 'Hospital Service',
    subject: '',
    description: '',
    hospitalId: null
  });

  // Target Autocomplete State
  const [targetSearch, setTargetSearch] = useState('');
  const [selectedTarget, setSelectedTarget] = useState(null);
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [hospitalsList, setHospitalsList] = useState([]);
  const [loadingTargets, setLoadingTargets] = useState(false);
  const dropdownRef = useRef(null);

  // Complaints History State
  const [myComplaints, setMyComplaints] = useState([]);
  const [loadingComplaints, setLoadingComplaints] = useState(true);
  const [submitting, setSubmitting] = useState(false);

  // Fetch Registered Hospitals for Target Autocomplete
  useEffect(() => {
    const loadInitialData = async () => {
      setLoadingTargets(true);
      try {
        const hospitalsData = await hospitalApi.getHospitals();
        if (Array.isArray(hospitalsData)) {
          setHospitalsList(hospitalsData);
        }
      } catch (err) {
        console.error('Failed to load hospitals list:', err);
      } finally {
        setLoadingTargets(false);
      }

      loadComplaintsHistory();
    };

    loadInitialData();
  }, [user]);

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

  // Filter Real Registered Hospitals
  const getFilteredTargets = () => {
    const query = targetSearch.trim().toLowerCase();
    const hospitalResults = hospitalsList.map((h) => ({
      id: h.hospitalId,
      name: h.name,
      type: 'Registered Hospital',
      subtitle: `${h.licenseNumber || 'Verified Medical Center'} • ${h.email || h.address || 'Sri Lanka'}`,
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
    setFormData((prev) => ({ ...prev, hospitalId: target.id }));
  };

  const handleClearTarget = () => {
    setSelectedTarget(null);
    setTargetSearch('');
    setFormData((prev) => ({ ...prev, hospitalId: null }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setSubmitting(true);

    try {
      const payload = {
        complaintType: formData.complaintType,
        subject: selectedTarget
          ? `[Target: ${selectedTarget.name}] ${formData.subject.trim()}`
          : formData.subject.trim(),
        description: formData.description.trim(),
        hospitalId: formData.hospitalId
      };

      await complaintApi.createComplaint(payload);

      addToast({
        title: 'Report Submitted Successfully',
        message: 'Your report has been logged and assigned to system administration for investigation.',
        type: 'success'
      });

      // Reset Form & Reload Database Complaints
      setFormData({ complaintType: 'Hospital Service', subject: '', description: '', hospitalId: null });
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

            {/* Target Autocomplete - Registered Hospitals */}
            <div className="relative" ref={dropdownRef}>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                Complaint Target (Registered Hospital)
              </label>
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
                      setFormData((prev) => ({ ...prev, hospitalId: null }));
                    }
                  }}
                  placeholder="Search registered hospital..."
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
                    <Building2 className="w-4 h-4 text-cyan-400 shrink-0" />
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
                  {loadingTargets ? (
                    <div className="p-3 text-center text-slate-400 flex items-center justify-center gap-2 text-xs">
                      <Loader2 className="w-4 h-4 animate-spin text-cyan-500" />
                      <span>Loading registered hospitals...</span>
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
                            <Building2 className="w-3.5 h-3.5" />
                          </div>
                          <div className="truncate">
                            <p className="font-semibold text-slate-900 dark:text-slate-100 group-hover:text-red-400 transition-colors truncate">
                              {item.name}
                            </p>
                            <p className="text-[10px] text-slate-500 dark:text-slate-400 truncate">{item.subtitle}</p>
                          </div>
                        </div>
                        <span className="text-[10px] font-bold px-2 py-0.5 rounded uppercase tracking-wider shrink-0 bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                          Hospital
                        </span>
                      </button>
                    ))
                  ) : (
                    <div className="p-3.5 text-center text-slate-400 text-xs">
                      No matching registered hospitals found in database.
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
              const hasAdminReply = Boolean(item.resolutionNotes || item.adminResponse || item.status === 'RESOLVED');

              return (
                <div
                  key={item.complaintId || item.id}
                  className={`rounded-2xl p-5 border transition-all duration-300 shadow-md ${
                    hasAdminReply
                      ? 'bg-gradient-to-b from-purple-950/30 via-slate-900 to-slate-900 border-purple-500/40 shadow-purple-950/20'
                      : 'bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800'
                  }`}
                >
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-3">
                    <div className="flex items-center gap-2 flex-wrap">
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

                  <div className="p-3.5 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-200 dark:border-slate-800/80 text-xs text-slate-700 dark:text-slate-300 leading-relaxed mb-4">
                    {item.description}
                  </div>

                  {hasAdminReply && (
                    <div className="mb-4 p-4 bg-purple-950/40 border border-purple-500/40 rounded-xl relative overflow-hidden shadow-inner">
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

                  {renderTimeline(item.status, hasAdminReply)}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};

export default DonorComplaintsPage;
