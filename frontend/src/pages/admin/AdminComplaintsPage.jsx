import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { adminApi, profileApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import ComplaintActivityTimeline from '../../components/complaints/ComplaintActivityTimeline';
import ComplaintReplyModal from '../../components/complaints/ComplaintReplyModal';
import {
  MessageSquare,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  Loader2,
  ChevronDown,
  ChevronUp,
  Search,
  Building2,
  User,
  Clock,
  X,
  ExternalLink,
  Phone,
  MapPin
} from 'lucide-react';

export const AdminComplaintsPage = () => {
  const [complaints, setComplaints] = useState([]);
  const [loading, setLoading] = useState(true);
  const [filterQuery, setFilterQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [expandedComplaintIds, setExpandedComplaintIds] = useState(new Set());
  const { addToast } = useNotification();

  // Reply Modal State (admins can only reply; only the creator can mark solved or delete)
  const [replyTarget, setReplyTarget] = useState(null);

  // Creator Profile Modal State
  const [creatorModal, setCreatorModal] = useState({
    isOpen: false,
    loading: false,
    data: null,
    error: null,
    creatorType: 'User'
  });

  const fetchComplaints = async () => {
    setLoading(true);
    try {
      const res = await adminApi.getComplaints();
      const list = res.data || (Array.isArray(res) ? res : []);
      setComplaints(list);
    } catch (err) {
      console.error('Failed to load complaints:', err);
      addToast({
        title: 'Load Error',
        message: 'Unable to load platform complaints.',
        type: 'error'
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchComplaints();
  }, []);

  const toggleExpand = (id) => {
    setExpandedComplaintIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  // Open Creator Profile Modal (Dynamic detection: User, Doctor, Hospital)
  const handleOpenCreatorProfile = async (complaint) => {
    setCreatorModal({ isOpen: true, loading: true, data: null, error: null, creatorType: 'User' });
    try {
      if (complaint.userId) {
        const userProfile = await profileApi.getUserProfile(complaint.userId);
        let hospitalDetails = null;
        let detectedType = 'User';

        // Complaint creators are Users or Hospital Staff only (doctors cannot create complaints)
        if (userProfile.roles && userProfile.roles.includes('HospitalStaff')) {
          detectedType = 'HospitalStaff';
          if (complaint.hospitalId) {
            try {
              hospitalDetails = await profileApi.getHospitalProfile(complaint.hospitalId);
            } catch {
              // fallback
            }
          }
        }

        setCreatorModal({
          isOpen: true,
          loading: false,
          data: { ...userProfile, hospitalDetails },
          error: null,
          creatorType: detectedType
        });
      } else if (complaint.hospitalId) {
        const hospitalProfile = await profileApi.getHospitalProfile(complaint.hospitalId);
        setCreatorModal({
          isOpen: true,
          loading: false,
          data: hospitalProfile,
          error: null,
          creatorType: 'Hospital'
        });
      } else {
        setCreatorModal({
          isOpen: true,
          loading: false,
          data: null,
          error: 'No creator profile available for this complaint.',
          creatorType: 'Unknown'
        });
      }
    } catch (err) {
      setCreatorModal({
        isOpen: true,
        loading: false,
        data: null,
        error: err.response?.data?.message || 'Failed to load creator profile.',
        creatorType: 'User'
      });
    }
  };

  // Admin reply (the only admin complaint action); errors are shown inside the modal
  const handleSendReply = async (dto) => {
    await adminApi.replyToComplaint(replyTarget.complaintId, dto);
    addToast({ title: 'Reply Sent', message: 'The complaint creator has been notified.', type: 'success' });
    setReplyTarget(null);
    await fetchComplaints();
  };

  // Status Badge Component
  const renderStatusBadge = (status) => {
    const s = (status || 'OPEN').toUpperCase();
    if (s === 'CANCELLED') {
      return (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-rose-50 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
          <XCircle className="w-3.5 h-3.5" /> Cancelled by User
        </span>
      );
    }
    if (s === 'RESOLVED') {
      return (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-900">
          <CheckCircle2 className="w-3.5 h-3.5" /> Resolved
        </span>
      );
    }
    if (s === 'REJECTED') {
      return (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
          <X className="w-3.5 h-3.5" /> Rejected
        </span>
      );
    }
    if (s === 'AWAITING_INFORMATION') {
      return (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-indigo-50 text-indigo-700 dark:bg-indigo-950/60 dark:text-indigo-300 border border-indigo-200 dark:border-indigo-900">
          <Clock className="w-3.5 h-3.5" /> Evidence Requested
        </span>
      );
    }
    if (s === 'UNDER_REVIEW') {
      return (
        <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border border-blue-200 dark:border-blue-900">
          <Clock className="w-3.5 h-3.5 animate-spin" /> Under Review
        </span>
      );
    }
    return (
      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-amber-50 text-amber-700 dark:bg-amber-950/60 dark:text-amber-300 border border-amber-200 dark:border-amber-900">
        <Clock className="w-3.5 h-3.5" /> Open / Pending
      </span>
    );
  };

  // Filter complaints
  const filteredComplaints = complaints.filter((c) => {
    // Status filter
    if (statusFilter !== 'ALL') {
      const s = (c.status || 'OPEN').toUpperCase();
      if (statusFilter === 'OPEN' && (s === 'RESOLVED' || s === 'REJECTED' || s === 'CANCELLED')) return false;
      if (statusFilter === 'RESOLVED' && s !== 'RESOLVED') return false;
      if (statusFilter === 'CANCELLED' && s !== 'CANCELLED') return false;
    }

    // Query filter
    if (!filterQuery) return true;
    const q = filterQuery.toLowerCase();
    const subject = (c.subject || '').toLowerCase();
    const desc = (c.description || '').toLowerCase();
    const type = (c.complaintType || '').toLowerCase();
    const userEmail = (c.userEmail || '').toLowerCase();
    const hospital = (c.hospitalName || '').toLowerCase();
    return (
      subject.includes(q) ||
      desc.includes(q) ||
      type.includes(q) ||
      userEmail.includes(q) ||
      hospital.includes(q)
    );
  });

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">
            Platform Complaints Hub
          </h1>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Investigate reported grievances, request hospital evidence, and issue resolution findings.
          </p>
        </div>

        <button
          onClick={fetchComplaints}
          disabled={loading}
          className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-semibold bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 border border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800 shadow-sm transition-all self-start md:self-auto"
        >
          {loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Clock className="w-3.5 h-3.5" />}
          <span>Refresh</span>
        </button>
      </div>

      {/* Filter and Search Bar */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3 p-3 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm">
        <div className="relative w-full sm:w-80">
          <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
          <input
            type="text"
            value={filterQuery}
            onChange={(e) => setFilterQuery(e.target.value)}
            placeholder="Search complaints, user, hospital, or subject..."
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

        <div className="flex items-center gap-1.5 overflow-x-auto w-full sm:w-auto">
          {['ALL', 'OPEN', 'RESOLVED', 'CANCELLED'].map((st) => (
            <button
              key={st}
              onClick={() => setStatusFilter(st)}
              className={`px-3 py-1.5 rounded-xl text-xs font-semibold transition-all shrink-0 ${
                statusFilter === st
                  ? 'bg-red-600 text-white shadow-sm shadow-red-600/20'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700'
              }`}
            >
              {st === 'ALL' && 'All Complaints'}
              {st === 'OPEN' && 'Active / In Review'}
              {st === 'RESOLVED' && 'Resolved'}
              {st === 'CANCELLED' && 'Cancelled'}
            </button>
          ))}
        </div>
      </div>

      {/* Complaints List with Expand/Collapse & Timeline */}
      {loading ? (
        <div className="p-16 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl flex flex-col items-center justify-center gap-2">
          <Loader2 className="w-8 h-8 text-red-600 animate-spin" />
          <span className="text-xs text-slate-400">Loading complaints registry...</span>
        </div>
      ) : filteredComplaints.length === 0 ? (
        <div className="p-16 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl text-slate-400">
          <AlertTriangle className="w-10 h-10 text-slate-300 dark:text-slate-600 mx-auto mb-2" />
          <h3 className="text-sm font-bold text-slate-700 dark:text-slate-300">No Complaints Found</h3>
          <p className="text-xs text-slate-400 mt-1">There are no complaints matching the selected filter criteria.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {filteredComplaints.map((c) => {
            const complaintId = c.complaintId || c.id;
            const isExpanded = expandedComplaintIds.has(complaintId);
            const statusUpper = (c.status || 'OPEN').toUpperCase();
            const isClosedOrCancelled =
              statusUpper === 'CANCELLED' || statusUpper === 'RESOLVED' || statusUpper === 'REJECTED';

            return (
              <div
                key={complaintId}
                className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm overflow-hidden transition-all"
              >
                {/* Main Card Summary Row */}
                <div className="p-5 flex flex-col lg:flex-row lg:items-center justify-between gap-4">
                  <div className="flex-1 space-y-2">
                    <div className="flex items-center gap-2.5 flex-wrap">
                      <span className="font-mono text-[11px] font-bold text-slate-400 bg-slate-100 dark:bg-slate-800 px-2 py-0.5 rounded-md">
                        #{String(complaintId).substring(0, 8)}
                      </span>
                      <span className="px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border border-amber-200 dark:border-amber-900">
                        {c.complaintType || 'Grievance'}
                      </span>
                      {renderStatusBadge(c.status)}
                    </div>

                    <h3 className="text-base font-bold text-slate-900 dark:text-slate-100">
                      {c.subject}
                    </h3>

                    <div className="flex items-center gap-4 text-xs text-slate-500 dark:text-slate-400 flex-wrap">
                      <div className="flex items-center gap-1.5">
                        <span className="flex items-center gap-1 text-slate-500 dark:text-slate-400">
                          <User className="w-3.5 h-3.5 text-slate-400" />
                          Complainant:
                        </span>
                        <button
                          type="button"
                          onClick={() => handleOpenCreatorProfile(c)}
                          title="Click to view creator profile"
                          className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-lg bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200 font-medium transition-colors border border-slate-200 dark:border-slate-700 cursor-pointer"
                        >
                          <div className="w-4 h-4 rounded-full bg-red-100 dark:bg-red-950/60 text-red-600 dark:text-red-400 flex items-center justify-center font-bold text-[9px]">
                            {(c.userEmail || 'U').charAt(0).toUpperCase()}
                          </div>
                          <span>{c.userEmail || 'View Profile'}</span>
                          <ExternalLink className="w-3 h-3 text-slate-400" />
                        </button>
                      </div>

                      {c.hospitalName && (
                        <span className="flex items-center gap-1">
                          <Building2 className="w-3.5 h-3.5 text-cyan-500" />
                          Target Hospital: <span className="text-slate-700 dark:text-slate-200 font-medium">{c.hospitalName}</span>
                        </span>
                      )}

                      {c.targetUserId && (
                        <span className="flex items-center gap-1">
                          <User className="w-3.5 h-3.5 text-purple-500" />
                          Target User:{' '}
                          <Link
                            to={`/profiles/user/${c.targetUserId}`}
                            className="text-slate-700 dark:text-slate-200 font-medium hover:text-red-600 underline-offset-2 hover:underline"
                          >
                            {c.targetUserName || c.targetUserEmail || 'View Profile'}
                          </Link>
                          {c.targetUserEmail && <span className="text-[11px] text-slate-400">({c.targetUserEmail})</span>}
                        </span>
                      )}

                      <span className="text-[11px] text-slate-400">
                        Logged:{' '}
                        {new Date(c.createdAt).toLocaleDateString(undefined, {
                          month: 'short',
                          day: 'numeric',
                          year: 'numeric'
                        })}
                      </span>
                    </div>
                  </div>

                  {/* Actions & Expand Toggle */}
                  <div className="flex items-center gap-2 shrink-0 pt-2 lg:pt-0 border-t lg:border-t-0 border-slate-100 dark:border-slate-800 flex-wrap">
                    {/* Active Governance Action Buttons (only visible if active) */}
                    {/* Admins can only reply; replies alternate with the complaint creator */}
                    {!isClosedOrCancelled ? (
                      c.awaitingAdminReply ? (
                        <button
                          onClick={() => setReplyTarget(c)}
                          className="px-3 py-1.5 bg-blue-600 hover:bg-blue-700 text-white font-semibold text-xs rounded-xl shadow-sm transition-colors flex items-center gap-1.5"
                        >
                          <MessageSquare className="w-3.5 h-3.5" /> Reply
                        </button>
                      ) : (
                        <span className="text-xs font-semibold text-amber-700 dark:text-amber-300 px-2.5 py-1 bg-amber-50 dark:bg-amber-950/40 rounded-lg border border-amber-200 dark:border-amber-800 flex items-center gap-1.5">
                          <Clock className="w-3.5 h-3.5" /> Waiting for creator response
                        </span>
                      )
                    ) : (
                      <span className="text-xs font-semibold text-slate-400 italic px-2 py-1 bg-slate-50 dark:bg-slate-800 rounded-lg border border-slate-200 dark:border-slate-700">
                        {statusUpper === 'RESOLVED' ? 'Resolved by Creator (Read-Only)' : 'Closed (Read-Only)'}
                      </span>
                    )}

                    {/* Expand/Collapse Toggle */}
                    <button
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
                </div>

                {/* Expanded Details Panel */}
                {isExpanded && (
                  <div className="border-t border-slate-100 dark:border-slate-800 p-5 bg-slate-50/50 dark:bg-slate-950/40 space-y-6 animate-in fade-in duration-200">
                    {/* Grid of Complaint Metadata */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      <div className="p-3.5 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-1">
                        <span className="text-[10px] font-bold uppercase text-slate-400">Complaint Details</span>
                        <div className="text-xs text-slate-700 dark:text-slate-300 font-medium">Type: {c.complaintType}</div>
                        <div className="text-xs text-slate-700 dark:text-slate-300 font-medium">ID: #{complaintId}</div>
                      </div>

                      <div className="p-3.5 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-1">
                        <span className="text-[10px] font-bold uppercase text-slate-400">Target Facility</span>
                        <div className="text-xs text-slate-700 dark:text-slate-300 font-medium">
                          {c.hospitalName || 'General Platform Service'}
                        </div>
                        {c.hospitalId && (
                          <div className="text-[11px] text-slate-400 font-mono">Hospital ID: {c.hospitalId}</div>
                        )}
                      </div>

                      <div className="p-3.5 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-1">
                        <span className="text-[10px] font-bold uppercase text-slate-400">Status & Governance</span>
                        <div className="pt-0.5">{renderStatusBadge(c.status)}</div>
                        {c.assignedAdminEmail && (
                          <div className="text-[11px] text-slate-400">Admin: {c.assignedAdminEmail}</div>
                        )}
                      </div>
                    </div>

                    {/* Complaint Description */}
                    <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-1.5">
                      <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100 uppercase tracking-wider">
                        Complainant Narrative
                      </h4>
                      <p className="text-xs text-slate-700 dark:text-slate-300 leading-relaxed whitespace-pre-wrap">
                        {c.description || 'No detailed narrative provided.'}
                      </p>
                    </div>

                    {/* Resolution Summary (if resolved or closed) */}
                    {c.resolutionNotes && (
                      <div className="p-4 bg-emerald-50/50 dark:bg-emerald-950/20 border border-emerald-200 dark:border-emerald-900/60 rounded-xl space-y-1.5">
                        <div className="flex items-center gap-2">
                          <CheckCircle2 className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                          <h4 className="text-xs font-bold text-emerald-900 dark:text-emerald-200">
                            Resolution Findings & Case Summary
                          </h4>
                        </div>
                        <p className="text-xs text-emerald-800 dark:text-emerald-300 leading-relaxed font-medium">
                          {c.resolutionNotes}
                        </p>
                        {c.resolvedAt && (
                          <p className="text-[10px] text-emerald-600/80 dark:text-emerald-400/80 pt-1">
                            Recorded on {new Date(c.resolvedAt).toLocaleString()}
                          </p>
                        )}
                      </div>
                    )}

                    {/* Attached Hospital Evidence Reports */}
                    {Array.isArray(c.activityReports) && c.activityReports.length > 0 && (
                      <div className="space-y-3">
                        <div className="flex items-center gap-2">
                          <Building2 className="w-4 h-4 text-cyan-600" />
                          <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100">
                            Hospital Evidence & Activity Reports ({c.activityReports.length})
                          </h4>
                        </div>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                          {c.activityReports.map((report) => (
                            <div
                              key={report.reportId}
                              className="p-3.5 bg-white dark:bg-slate-900 border border-cyan-200/60 dark:border-cyan-900/40 rounded-xl space-y-1.5"
                            >
                              <div className="flex items-center justify-between">
                                <span className="text-xs font-bold text-cyan-700 dark:text-cyan-300">
                                  {report.title}
                                </span>
                                <span className="text-[10px] text-slate-400 font-mono">
                                  {new Date(report.submittedAt).toLocaleDateString()}
                                </span>
                              </div>
                              <p className="text-xs text-slate-600 dark:text-slate-300 leading-relaxed">
                                {report.description}
                              </p>
                              <p className="text-[10px] text-slate-400 italic">
                                Facility: {report.hospitalName || c.hospitalName}
                              </p>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Chronological Activity Timeline */}
                    <div className="space-y-3 pt-2">
                      <div className="flex items-center justify-between pb-2 border-b border-slate-200 dark:border-slate-800">
                        <div className="flex items-center gap-2">
                          <Clock className="w-4 h-4 text-slate-600 dark:text-slate-400" />
                          <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100 uppercase tracking-wider">
                            Chronological Complaint Activity Timeline
                          </h4>
                        </div>
                        <span className="text-[11px] text-slate-400">
                          Complete audit record of events and state transitions
                        </span>
                      </div>

                      <ComplaintActivityTimeline complaint={c} />
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Reply Modal (shared with the creator's complaint page) */}
      {replyTarget && (
        <ComplaintReplyModal complaint={replyTarget} onSubmit={handleSendReply} onClose={() => setReplyTarget(null)} />
      )}

      {/* Creator Profile Modal (Reusing existing profile data) */}
      {creatorModal.isOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-lg shadow-2xl space-y-5">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-full bg-red-100 dark:bg-red-950/60 text-red-600 dark:text-red-400 flex items-center justify-center font-bold text-xs">
                  <User className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                    Complaint Creator Profile
                  </h3>
                  <span className="text-[11px] text-slate-400">
                    Account type: <strong className="text-slate-700 dark:text-slate-300">{creatorModal.creatorType}</strong>
                  </span>
                </div>
              </div>
              <button
                onClick={() => setCreatorModal({ isOpen: false, loading: false, data: null, error: null, creatorType: 'User' })}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {creatorModal.loading ? (
              <div className="py-12 text-center flex flex-col items-center justify-center gap-2">
                <Loader2 className="w-6 h-6 text-red-600 animate-spin" />
                <span className="text-xs text-slate-400">Fetching creator profile...</span>
              </div>
            ) : creatorModal.error ? (
              <div className="p-4 bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 rounded-xl text-rose-700 dark:text-rose-300 text-xs">
                {creatorModal.error}
              </div>
            ) : creatorModal.data ? (
              <div className="space-y-4 text-xs">
                {/* Profile Header Box */}
                <div className="p-3.5 bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700/60 rounded-xl flex items-center justify-between">
                  <div>
                    <h4 className="text-sm font-bold text-slate-900 dark:text-slate-100">
                      {creatorModal.data.firstName || creatorModal.data.name
                        ? `${creatorModal.data.firstName || ''} ${creatorModal.data.lastName || ''}`.trim() || creatorModal.data.name
                        : 'Registered Account'}
                    </h4>
                    <span className="text-slate-500 dark:text-slate-400 text-[11px]">
                      {creatorModal.data.email || 'No email provided'}
                    </span>
                  </div>
                  <div className="flex items-center gap-1.5 flex-wrap">
                    {creatorModal.data.roles && creatorModal.data.roles.map((r) => (
                      <span
                        key={r}
                        className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300 border border-blue-200 dark:border-blue-900"
                      >
                        {r}
                      </span>
                    ))}
                    <span
                      className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                        creatorModal.data.accountStatus === 'Active'
                          ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900'
                          : 'bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border-amber-200 dark:border-amber-900'
                      }`}
                    >
                      {creatorModal.data.accountStatus || 'ACTIVE'}
                    </span>
                  </div>
                </div>

                {/* Details Grid */}
                <div className="grid grid-cols-2 gap-3">
                  <div className="p-3 bg-slate-50/60 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 space-y-1">
                    <span className="text-[10px] font-bold uppercase text-slate-400 flex items-center gap-1">
                      <Phone className="w-3 h-3" /> Contact Phone
                    </span>
                    <p className="text-slate-700 dark:text-slate-200 font-medium">
                      {creatorModal.data.phoneNumber || creatorModal.data.contactNumber || 'Not provided'}
                    </p>
                  </div>

                  <div className="p-3 bg-slate-50/60 dark:bg-slate-800/40 rounded-xl border border-slate-100 dark:border-slate-800 space-y-1">
                    <span className="text-[10px] font-bold uppercase text-slate-400 flex items-center gap-1">
                      <MapPin className="w-3 h-3" /> Location / Address
                    </span>
                    <p className="text-slate-700 dark:text-slate-200 font-medium truncate">
                      {creatorModal.data.address || creatorModal.data.city || 'Not provided'}
                    </p>
                  </div>
                </div>

                {/* Doctor specifics if available */}
                <div className="text-[11px] text-slate-400 pt-1">
                  Registered:{' '}
                  {creatorModal.data.createdAt
                    ? new Date(creatorModal.data.createdAt).toLocaleDateString(undefined, {
                        month: 'short',
                        day: 'numeric',
                        year: 'numeric'
                      })
                    : 'N/A'}
                </div>
              </div>
            ) : null}

            <div className="flex justify-end pt-3 border-t border-slate-100 dark:border-slate-800">
              <button
                type="button"
                onClick={() => setCreatorModal({ isOpen: false, loading: false, data: null, error: null, creatorType: 'User' })}
                className="px-4 py-2 rounded-xl font-semibold bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200 transition-colors text-xs"
              >
                Close Profile
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default AdminComplaintsPage;
