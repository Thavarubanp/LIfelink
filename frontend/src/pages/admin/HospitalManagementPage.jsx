import React, { useState, useEffect } from 'react';
import { adminApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { DocumentPreviewModal } from '../../components/common/DocumentPreviewModal';
import {
  Building2,
  CheckCircle2,
  XCircle,
  ChevronDown,
  ChevronUp,
  FileText,
  Upload,
  Clock,
  AlertCircle,
  RotateCw,
  Eye,
  User,
  Phone,
  Mail,
  MapPin,
  Search,
  Calendar,
  Sparkles,
  ShieldCheck,
  Send
} from 'lucide-react';

export const HospitalManagementPage = () => {
  const [hospitals, setHospitals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('needsReview'); // 'all', 'needsReview', 'resubmitted', 'rejected', 'approved'
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedId, setExpandedId] = useState(null);

  // Rejection form state per hospital
  const [rejectReasons, setRejectReasons] = useState({});
  const [rejectReports, setRejectReports] = useState({}); // { [id]: { name, url } }
  const [rejectErrors, setRejectErrors] = useState({});
  const [actionLoading, setActionLoading] = useState({});

  // Document modal preview state
  const [previewDoc, setPreviewDoc] = useState(null); // { title, url, name }

  const { addToast } = useNotification();

  const fetchHospitals = async () => {
    setLoading(true);
    try {
      const res = await adminApi.getPendingHospitals();
      const list = res.data || (Array.isArray(res) ? res : []);
      setHospitals(list);
    } catch (err) {
      console.error('Failed to load pending hospitals:', err);
      addToast({ title: 'Error', message: 'Could not load hospital queue.', type: 'error' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHospitals();
  }, []);

  const handleToggleExpand = (id) => {
    setExpandedId((prev) => (prev === id ? null : id));
  };

  const handleApprove = async (id) => {
    setActionLoading((prev) => ({ ...prev, [id]: 'approve' }));
    try {
      await adminApi.approveHospital(id);
      addToast({
        title: 'Hospital Approved',
        message: 'Hospital account has been verified and granted platform access.',
        type: 'success'
      });
      fetchHospitals();
    } catch (err) {
      addToast({
        title: 'Approval Failed',
        message: err.response?.data?.message || 'Could not approve hospital.',
        type: 'error'
      });
    } finally {
      setActionLoading((prev) => ({ ...prev, [id]: null }));
    }
  };

  const handleReportFileChange = (e, id) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      setRejectReports((prev) => ({
        ...prev,
        [id]: {
          name: file.name,
          url: reader.result
        }
      }));
    };
    reader.readAsDataURL(file);
  };

  const handleRemoveReport = (id) => {
    setRejectReports((prev) => {
      const copy = { ...prev };
      delete copy[id];
      return copy;
    });
  };

  const handleReject = async (id) => {
    const reason = (rejectReasons[id] || '').trim();
    if (!reason || reason.length < 3) {
      setRejectErrors((prev) => ({
        ...prev,
        [id]: 'Please provide a mandatory rejection reason (at least 3 characters).'
      }));
      return;
    }

    setRejectErrors((prev) => ({ ...prev, [id]: null }));
    setActionLoading((prev) => ({ ...prev, [id]: 'reject' }));

    try {
      const report = rejectReports[id];
      const payload = {
        reason,
        reportDocumentName: report?.name || null,
        reportDocumentUrl: report?.url || null
      };

      await adminApi.rejectHospital(id, payload);
      addToast({
        title: 'Hospital Rejected',
        message: 'Registration was rejected and sent back to hospital with review remarks.',
        type: 'info'
      });
      // Clear inputs
      setRejectReasons((prev) => ({ ...prev, [id]: '' }));
      handleRemoveReport(id);
      fetchHospitals();
    } catch (err) {
      addToast({
        title: 'Rejection Failed',
        message: err.response?.data?.message || 'Could not reject hospital registration.',
        type: 'error'
      });
    } finally {
      setActionLoading((prev) => ({ ...prev, [id]: null }));
    }
  };

  // Filter computation
  const filteredHospitals = hospitals.filter((h) => {
    // Tab filter
    if (activeTab === 'needsReview') {
      if (h.approvalStatus !== 'Pending' && h.approvalStatus !== 'Resubmitted') return false;
    } else if (activeTab === 'resubmitted') {
      if (h.approvalStatus !== 'Resubmitted') return false;
    } else if (activeTab === 'rejected') {
      if (h.approvalStatus !== 'Rejected') return false;
    } else if (activeTab === 'approved') {
      if (h.approvalStatus !== 'Approved') return false;
    }

    // Search query filter
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      const matchName = h.name?.toLowerCase().includes(q);
      const matchLicense = h.licenseNumber?.toLowerCase().includes(q);
      const matchReg = h.registrationNumber?.toLowerCase().includes(q);
      const matchEmail = h.email?.toLowerCase().includes(q);
      const matchCity = h.city?.toLowerCase().includes(q);
      return matchName || matchLicense || matchReg || matchEmail || matchCity;
    }

    return true;
  });

  const getStatusBadge = (status) => {
    switch (status) {
      case 'Resubmitted':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-blue-500/20 text-blue-400 border border-blue-500/30">
            <Sparkles className="w-3 h-3" /> Resubmitted / Updated
          </span>
        );
      case 'Approved':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
            <CheckCircle2 className="w-3 h-3" /> Approved
          </span>
        );
      case 'Rejected':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-rose-500/20 text-rose-400 border border-rose-500/30">
            <XCircle className="w-3 h-3" /> Rejected
          </span>
        );
      default:
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-amber-500/20 text-amber-400 border border-amber-500/30">
            <Clock className="w-3 h-3" /> Pending Review
          </span>
        );
    }
  };

  const pendingCount = hospitals.filter((h) => h.approvalStatus === 'Pending').length;
  const resubmittedCount = hospitals.filter((h) => h.approvalStatus === 'Resubmitted').length;
  const rejectedCount = hospitals.filter((h) => h.approvalStatus === 'Rejected').length;
  const approvedCount = hospitals.filter((h) => h.approvalStatus === 'Approved').length;

  return (
    <div className="space-y-6">
      {/* Header & Stats Banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
            <Building2 className="w-6 h-6 text-cyan-500" />
            Hospital Registration Queue
          </h1>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Review submitted clinical documentation, audit accreditation numbers, and verify hospital operational access.
          </p>
        </div>
        <button
          onClick={fetchHospitals}
          disabled={loading}
          className="inline-flex items-center gap-2 px-3 py-2 bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200 text-xs font-semibold rounded-xl border border-slate-300 dark:border-slate-700 transition-colors w-fit"
        >
          <RotateCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>Refresh Queue</span>
        </button>
      </div>

      {/* Filter Tabs & Search Bar */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          {/* Tabs */}
          <div className="flex items-center gap-1.5 p-1 bg-slate-100 dark:bg-slate-800/80 rounded-xl text-xs font-medium">
            <button
              onClick={() => setActiveTab('needsReview')}
              className={`px-3 py-1.5 rounded-lg transition-all ${
                activeTab === 'needsReview'
                  ? 'bg-cyan-600 text-white shadow-sm font-semibold'
                  : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
              }`}
            >
              Needs Review ({pendingCount + resubmittedCount})
            </button>
            <button
              onClick={() => setActiveTab('resubmitted')}
              className={`px-3 py-1.5 rounded-lg transition-all ${
                activeTab === 'resubmitted'
                  ? 'bg-blue-600 text-white shadow-sm font-semibold'
                  : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
              }`}
            >
              Resubmitted ({resubmittedCount})
            </button>
            <button
              onClick={() => setActiveTab('rejected')}
              className={`px-3 py-1.5 rounded-lg transition-all ${
                activeTab === 'rejected'
                  ? 'bg-rose-600 text-white shadow-sm font-semibold'
                  : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
              }`}
            >
              Rejected ({rejectedCount})
            </button>
            <button
              onClick={() => setActiveTab('approved')}
              className={`px-3 py-1.5 rounded-lg transition-all ${
                activeTab === 'approved'
                  ? 'bg-emerald-600 text-white shadow-sm font-semibold'
                  : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
              }`}
            >
              Approved ({approvedCount})
            </button>
            <button
              onClick={() => setActiveTab('all')}
              className={`px-3 py-1.5 rounded-lg transition-all ${
                activeTab === 'all'
                  ? 'bg-slate-700 text-white shadow-sm font-semibold'
                  : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
              }`}
            >
              All Records ({hospitals.length})
            </button>
          </div>

          {/* Search Input */}
          <div className="relative w-full sm:w-64">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search hospital, license, city..."
              className="w-full pl-9 pr-3 py-1.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-cyan-500"
            />
          </div>
        </div>
      </div>

      {/* Hospital Registration Queue Table */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm">
        {loading ? (
          <div className="p-12 text-center text-slate-500 dark:text-slate-400 flex flex-col items-center justify-center">
            <RotateCw className="w-6 h-6 animate-spin text-cyan-500 mb-2" />
            <span className="text-xs">Loading hospital registration queue...</span>
          </div>
        ) : filteredHospitals.length === 0 ? (
          <div className="p-12 text-center text-slate-500 dark:text-slate-400 space-y-2">
            <Building2 className="w-10 h-10 text-slate-400 dark:text-slate-600 mx-auto" />
            <p className="text-xs font-semibold">No hospital registrations found in this category.</p>
          </div>
        ) : (
          <div className="divide-y divide-slate-200 dark:divide-slate-800">
            {filteredHospitals.map((hospital) => {
              const isExpanded = expandedId === (hospital.hospitalId || hospital.id);
              const isResubmitted = hospital.approvalStatus === 'Resubmitted';
              const id = hospital.hospitalId || hospital.id;

              return (
                <div key={id} className="transition-colors hover:bg-slate-50/50 dark:hover:bg-slate-800/30">
                  {/* Summary Row */}
                  <div className="p-4 sm:px-6 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                    {/* Hospital Name & Code */}
                    <div className="space-y-1 min-w-[220px]">
                      <div className="flex items-center gap-2">
                        <span className="font-bold text-sm text-slate-900 dark:text-slate-100">
                          {hospital.name}
                        </span>
                        {isResubmitted && (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-500/20 text-blue-400 border border-blue-500/30">
                            Updated
                          </span>
                        )}
                      </div>
                      <div className="text-xs text-slate-500 dark:text-slate-400 font-mono">
                        License / Reg: <span className="text-cyan-600 dark:text-cyan-400 font-bold">{hospital.licenseNumber || hospital.registrationNumber || 'N/A'}</span>
                      </div>
                    </div>

                    {/* Email / Contact */}
                    <div className="text-xs text-slate-600 dark:text-slate-300">
                      <div>{hospital.email}</div>
                      <div className="text-[11px] text-slate-400">{hospital.contactNumber || 'No phone'}</div>
                    </div>

                    {/* City / District */}
                    <div className="text-xs text-slate-600 dark:text-slate-300">
                      <div className="flex items-center gap-1">
                        <MapPin className="w-3.5 h-3.5 text-slate-400" />
                        <span>{hospital.city || 'Central District'}</span>
                      </div>
                    </div>

                    {/* Status Badge */}
                    <div>{getStatusBadge(hospital.approvalStatus)}</div>

                    {/* Actions: View Details / Approve / Reject */}
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => handleToggleExpand(id)}
                        className={`px-3 py-1.5 rounded-xl text-xs font-semibold transition-colors flex items-center gap-1.5 border ${
                          isExpanded
                            ? 'bg-slate-200 dark:bg-slate-800 text-slate-800 dark:text-slate-200 border-slate-300 dark:border-slate-700'
                            : 'bg-slate-100 hover:bg-slate-200 dark:bg-slate-800/80 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700'
                        }`}
                      >
                        <span>{isExpanded ? 'Hide Details' : 'View Details'}</span>
                        {isExpanded ? (
                          <ChevronUp className="w-3.5 h-3.5" />
                        ) : (
                          <ChevronDown className="w-3.5 h-3.5" />
                        )}
                      </button>

                      {hospital.approvalStatus !== 'Approved' && (
                        <button
                          type="button"
                          onClick={() => handleApprove(id)}
                          disabled={actionLoading[id] === 'approve'}
                          className="px-3 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs rounded-xl shadow-sm transition-all flex items-center gap-1 disabled:opacity-50"
                        >
                          <CheckCircle2 className="w-3.5 h-3.5" />
                          <span>{actionLoading[id] === 'approve' ? 'Approving...' : 'Approve'}</span>
                        </button>
                      )}

                      {hospital.approvalStatus !== 'Rejected' && (
                        <button
                          type="button"
                          onClick={() => {
                            if (!isExpanded) handleToggleExpand(id);
                          }}
                          className="px-3 py-1.5 bg-rose-600/10 hover:bg-rose-600 text-rose-500 hover:text-white font-semibold text-xs rounded-xl border border-rose-500/30 transition-all flex items-center gap-1"
                        >
                          <XCircle className="w-3.5 h-3.5" />
                          <span>Reject</span>
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Expandable Accordion Panel */}
                  {isExpanded && (
                    <div className="p-5 sm:p-6 bg-slate-50/80 dark:bg-slate-950/60 border-t border-slate-200 dark:border-slate-800 space-y-6 animate-in slide-in-from-top-2">
                      {/* Resubmitted Highlight Banner */}
                      {isResubmitted && (
                        <div className="p-3.5 bg-blue-950/40 border border-blue-800/60 rounded-xl flex items-start gap-3 text-xs text-blue-200">
                          <Sparkles className="w-4 h-4 text-blue-400 shrink-0 mt-0.5" />
                          <div>
                            <span className="font-bold text-blue-300">Resubmitted Application with Updates</span>
                            {hospital.updatedFields && (
                              <p className="mt-0.5 text-blue-100">
                                <strong>Highlighted Changes:</strong>{' '}
                                <span className="px-2 py-0.5 rounded bg-blue-900/60 text-blue-200 font-mono text-[11px]">
                                  {hospital.updatedFields}
                                </span>
                              </p>
                            )}
                            {hospital.resubmittedAt && (
                              <p className="mt-0.5 text-blue-300 text-[11px]">
                                Resubmitted on: {new Date(hospital.resubmittedAt).toLocaleString()}
                              </p>
                            )}
                          </div>
                        </div>
                      )}

                      {/* Header Title */}
                      <div className="flex items-center justify-between pb-2 border-b border-slate-200 dark:border-slate-800">
                        <h3 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                          <Building2 className="w-4 h-4 text-cyan-500" />
                          Hospital Registration Details
                        </h3>
                        <span className="text-[11px] text-slate-500 dark:text-slate-400">
                          Registered: {hospital.createdAt ? new Date(hospital.createdAt).toLocaleDateString() : 'N/A'}
                        </span>
                      </div>

                      {/* Comprehensive Details Grid */}
                      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 text-xs">
                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Hospital Name</span>
                          <span className="font-bold text-slate-800 dark:text-slate-100 mt-0.5 block">{hospital.name}</span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">License Number</span>
                          <span className="font-mono font-bold text-cyan-600 dark:text-cyan-400 mt-0.5 block">
                            {hospital.licenseNumber || 'N/A'}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Registration / Facility Code</span>
                          <span className="font-mono font-bold text-slate-800 dark:text-slate-200 mt-0.5 block">
                            {hospital.registrationNumber || hospital.licenseNumber || 'N/A'}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Official Hospital Email</span>
                          <span className="font-medium text-slate-800 dark:text-slate-200 mt-0.5 block flex items-center gap-1.5">
                            <Mail className="w-3.5 h-3.5 text-slate-400" />
                            {hospital.email}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Emergency Contact Phone</span>
                          <span className="font-medium text-slate-800 dark:text-slate-200 mt-0.5 block flex items-center gap-1.5">
                            <Phone className="w-3.5 h-3.5 text-slate-400" />
                            {hospital.contactNumber || 'N/A'}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">City / District / Region</span>
                          <span className="font-medium text-slate-800 dark:text-slate-200 mt-0.5 block flex items-center gap-1.5">
                            <MapPin className="w-3.5 h-3.5 text-slate-400" />
                            {hospital.city || 'Central District'}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 md:col-span-2">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Physical Facility Address</span>
                          <span className="font-medium text-slate-800 dark:text-slate-200 mt-0.5 block">
                            {hospital.address || 'N/A'}
                          </span>
                        </div>

                        <div className="p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800">
                          <span className="text-slate-400 text-[10px] uppercase font-semibold block">Authorized Contact Person</span>
                          <span className="font-medium text-slate-800 dark:text-slate-200 mt-0.5 block flex items-center gap-1.5">
                            <User className="w-3.5 h-3.5 text-slate-400" />
                            {hospital.contactPersonName || 'Medical Director / Administrator'}
                          </span>
                          {(hospital.contactPersonPhone || hospital.contactPersonEmail) && (
                            <span className="text-[11px] text-slate-400 mt-0.5 block">
                              {hospital.contactPersonPhone} {hospital.contactPersonEmail && `• ${hospital.contactPersonEmail}`}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Uploaded Documents Section */}
                      <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-3">
                        <h4 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                          <FileText className="w-4 h-4 text-cyan-500" />
                          Uploaded Accreditation & Verification Documents
                        </h4>

                        <div className="flex flex-wrap items-center gap-3">
                          {/* View License */}
                          <button
                            type="button"
                            onClick={() =>
                              setPreviewDoc({
                                title: `${hospital.name} - License Document`,
                                url: hospital.licenseDocumentUrl || 'data:text/plain;charset=utf-8,PHSRC%20Verified%20License%20Document%20for%20' + encodeURIComponent(hospital.name),
                                name: hospital.licenseDocumentName || `${hospital.name}_License.pdf`
                              })
                            }
                            className="inline-flex items-center gap-2 px-3 py-2 bg-cyan-600/10 hover:bg-cyan-600/20 text-cyan-600 dark:text-cyan-400 font-semibold text-xs rounded-xl border border-cyan-500/30 transition-colors"
                          >
                            <Eye className="w-3.5 h-3.5" />
                            <span>View License ({hospital.licenseDocumentName || 'PHSRC License'})</span>
                          </button>

                          {/* View Accreditation */}
                          <button
                            type="button"
                            onClick={() =>
                              setPreviewDoc({
                                title: `${hospital.name} - Accreditation Proof`,
                                url: hospital.accreditationDocumentUrl || 'data:text/plain;charset=utf-8,Ministry%20of%20Health%20Accreditation%20Certificate%20for%20' + encodeURIComponent(hospital.name),
                                name: hospital.accreditationDocumentName || `${hospital.name}_Accreditation.pdf`
                              })
                            }
                            className="inline-flex items-center gap-2 px-3 py-2 bg-purple-600/10 hover:bg-purple-600/20 text-purple-600 dark:text-purple-400 font-semibold text-xs rounded-xl border border-purple-500/30 transition-colors"
                          >
                            <FileText className="w-3.5 h-3.5" />
                            <span>View Accreditation ({hospital.accreditationDocumentName || 'MOH Certificate'})</span>
                          </button>

                          {/* Rejection Report If Present */}
                          {hospital.rejectionReportUrl && (
                            <button
                              type="button"
                              onClick={() =>
                                setPreviewDoc({
                                  title: `Rejection Review Report - ${hospital.name}`,
                                  url: hospital.rejectionReportUrl,
                                  name: hospital.rejectionReportName || 'Admin_Review_Report.pdf'
                                })
                              }
                              className="inline-flex items-center gap-2 px-3 py-2 bg-rose-600/10 hover:bg-rose-600/20 text-rose-600 dark:text-rose-400 font-semibold text-xs rounded-xl border border-rose-500/30 transition-colors"
                            >
                              <FileText className="w-3.5 h-3.5" />
                              <span>View Attached Rejection Report</span>
                            </button>
                          )}
                        </div>
                      </div>

                      {/* Approval History Timeline */}
                      {hospital.approvalHistory && hospital.approvalHistory.length > 0 && (
                        <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-3">
                          <h4 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                            <Clock className="w-4 h-4 text-cyan-500" />
                            Approval Lifecycle Audit History
                          </h4>
                          <div className="space-y-2">
                            {hospital.approvalHistory.map((step, idx) => (
                              <div
                                key={step.id || idx}
                                className="flex items-start gap-3 p-2.5 rounded-lg bg-slate-50 dark:bg-slate-800/60 text-xs border border-slate-200 dark:border-slate-700/60"
                              >
                                <div className="mt-0.5">{getStatusBadge(step.status)}</div>
                                <div className="flex-1">
                                  <div className="flex items-center justify-between text-[11px] text-slate-400">
                                    <span>{step.timestamp ? new Date(step.timestamp).toLocaleString() : ''}</span>
                                    {step.adminName && <span>Reviewed by Admin: {step.adminName}</span>}
                                  </div>
                                  {step.comments && (
                                    <p className="mt-1 text-slate-700 dark:text-slate-200 font-medium">
                                      {step.comments}
                                    </p>
                                  )}
                                  {step.changedFields && (
                                    <p className="mt-0.5 text-blue-400 text-[11px]">
                                      Updated fields: {step.changedFields}
                                    </p>
                                  )}
                                </div>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Admin Review Action Section */}
                      <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-4">
                        <div className="pb-2 border-b border-slate-200 dark:border-slate-800">
                          <h4 className="font-bold text-xs uppercase tracking-wider text-slate-800 dark:text-slate-200 flex items-center gap-1.5">
                            <ShieldCheck className="w-4 h-4 text-cyan-500" />
                            Admin Review & Decision
                          </h4>
                          <p className="text-[11px] text-slate-400 mt-0.5">
                            Authorize registration or specify rejection reasons along with optional audit reports.
                          </p>
                        </div>

                        {/* Rejection Form Input */}
                        <div className="space-y-3">
                          <div>
                            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1 text-xs">
                              Rejection Reason (Mandatory if rejecting registration)
                            </label>
                            <textarea
                              rows={2}
                              value={rejectReasons[id] || ''}
                              onChange={(e) => {
                                setRejectReasons((prev) => ({ ...prev, [id]: e.target.value }));
                                if (rejectErrors[id]) setRejectErrors((prev) => ({ ...prev, [id]: null }));
                              }}
                              placeholder="e.g. License expired on PHSRC database; please re-upload valid 2026 accreditation proof."
                              className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-rose-500"
                            />
                            {rejectErrors[id] && (
                              <span className="text-[11px] text-rose-500 font-medium mt-1 block">
                                {rejectErrors[id]}
                              </span>
                            )}
                          </div>

                          {/* Optional Review Report Attachment */}
                          <div>
                            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1 text-xs">
                              Attach Review / Audit Report (Optional)
                            </label>
                            <div className="flex items-center gap-3">
                              <input
                                type="file"
                                accept=".pdf,.doc,.docx,.png,.jpg,.jpeg"
                                onChange={(e) => handleReportFileChange(e, id)}
                                className="hidden"
                                id={`report-file-${id}`}
                              />
                              <label
                                htmlFor={`report-file-${id}`}
                                className="px-3 py-1.5 bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 text-xs font-medium rounded-xl border border-slate-300 dark:border-slate-700 cursor-pointer flex items-center gap-1.5 transition-colors"
                              >
                                <Upload className="w-3.5 h-3.5 text-cyan-500" />
                                <span>Choose Report Document</span>
                              </label>

                              {rejectReports[id] ? (
                                <div className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
                                  <FileText className="w-3.5 h-3.5 text-emerald-400" />
                                  <span className="font-mono font-medium truncate max-w-[200px]">
                                    {rejectReports[id].name}
                                  </span>
                                  <button
                                    type="button"
                                    onClick={() => handleRemoveReport(id)}
                                    className="text-rose-500 hover:underline text-[11px]"
                                  >
                                    Remove
                                  </button>
                                </div>
                              ) : (
                                <span className="text-[11px] text-slate-400">No report file chosen</span>
                              )}
                            </div>
                          </div>
                        </div>

                        {/* Direct Approve & Reject Buttons */}
                        <div className="pt-2 flex flex-wrap items-center gap-3">
                          <button
                            type="button"
                            onClick={() => handleApprove(id)}
                            disabled={actionLoading[id] === 'approve'}
                            className="px-5 py-2.5 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all flex items-center gap-2 disabled:opacity-50"
                          >
                            <CheckCircle2 className="w-4 h-4" />
                            <span>{actionLoading[id] === 'approve' ? 'Approving...' : 'Approve Hospital'}</span>
                          </button>

                          <button
                            type="button"
                            onClick={() => handleReject(id)}
                            disabled={actionLoading[id] === 'reject'}
                            className="px-5 py-2.5 bg-rose-600 hover:bg-rose-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all flex items-center gap-2 disabled:opacity-50"
                          >
                            <XCircle className="w-4 h-4" />
                            <span>{actionLoading[id] === 'reject' ? 'Rejecting...' : 'Reject Hospital Registration'}</span>
                          </button>
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Document Preview Modal */}
      {previewDoc && (
        <DocumentPreviewModal
          isOpen={!!previewDoc}
          onClose={() => setPreviewDoc(null)}
          title={previewDoc.title}
          documentUrl={previewDoc.url}
          documentName={previewDoc.name}
        />
      )}
    </div>
  );
};

export default HospitalManagementPage;
