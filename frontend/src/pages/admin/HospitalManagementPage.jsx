import React, { useState, useEffect } from 'react';
import { adminApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { readFileAsAttachment } from '../../utils/fileUtils';
import { DocumentPreviewModal } from '../../components/common/DocumentPreviewModal';
import AttachmentLink from '../../components/common/AttachmentLink';
import RegistrationThread from '../../components/hospital/RegistrationThread';
import {
  Building2,
  CheckCircle2,
  XCircle,
  ChevronDown,
  ChevronUp,
  FileText,
  Upload,
  Clock,
  RotateCw,
  User,
  Phone,
  Mail,
  MapPin,
  Search,
  ShieldCheck,
  Send,
  MessageSquare,
  Lock,
  PauseCircle
} from 'lucide-react';

// The admin acts on Pending registrations and on hospital replies (AwaitingAdminReview); Rejected waits for the hospital
const needsReview = (h) => h.approvalStatus === 'Pending' || h.approvalStatus === 'AwaitingAdminReview';
const waitingForHospital = (h) => h.approvalStatus === 'Rejected';
const lastEntryId = (h) => h.approvalHistory?.[h.approvalHistory.length - 1]?.id;

const TABS = [
  { key: 'needsReview', label: 'Needs Review', active: 'bg-cyan-600' },
  { key: 'waiting', label: 'Waiting for Hospital', active: 'bg-rose-600' },
  { key: 'approved', label: 'Approved', active: 'bg-emerald-600' },
  { key: 'all', label: 'All Records', active: 'bg-slate-700' }
];

const DetailCard = ({ label, children, wide }) => (
  <div className={`p-3 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 ${wide ? 'md:col-span-2' : ''}`}>
    <span className="text-slate-400 text-[10px] uppercase font-semibold block">{label}</span>
    <span className="font-medium text-slate-800 dark:text-slate-100 mt-0.5 block">{children}</span>
  </div>
);

export const HospitalManagementPage = () => {
  const [hospitals, setHospitals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('needsReview');
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedId, setExpandedId] = useState(null);

  // Per hospital: rejection reason (pending) or comment (rejected), with an optional file
  const [drafts, setDrafts] = useState({});
  const [draftErrors, setDraftErrors] = useState({});
  const [actionLoading, setActionLoading] = useState({});
  const [previewDoc, setPreviewDoc] = useState(null);

  const { addToast } = useNotification();

  const fetchHospitals = async () => {
    setLoading(true);
    try {
      const res = await adminApi.getAllHospitals();
      setHospitals(res.data || (Array.isArray(res) ? res : []));
    } catch (err) {
      console.error('Failed to load hospital registrations:', err);
      addToast({ title: 'Error', message: 'Could not load hospital registrations.', type: 'error' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchHospitals();
  }, []);

  const setDraft = (id, patch) =>
    setDrafts((prev) => ({ ...prev, [id]: { text: '', file: null, ...prev[id], ...patch } }));
  const clearDraft = (id) => setDrafts((prev) => ({ ...prev, [id]: { text: '', file: null } }));
  const setBusy = (id, value) => setActionLoading((prev) => ({ ...prev, [id]: value }));

  // 409: the registration changed meanwhile (for example the hospital replied); reload so the admin sees it
  const reportFailure = (err, title) => {
    addToast({ title, message: getApiErrorMessage(err), type: 'error' });
    if (err.response?.status === 409) fetchHospitals();
  };

  const handleDraftFile = async (e, id) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    try {
      setDraft(id, { file: await readFileAsAttachment(file) });
      setDraftErrors((prev) => ({ ...prev, [id]: null }));
    } catch (err) {
      setDraftErrors((prev) => ({ ...prev, [id]: err.message }));
    }
  };

  const draftText = (id, minimum, message) => {
    const text = (drafts[id]?.text || '').trim();
    if (text.length < minimum) {
      setDraftErrors((prev) => ({ ...prev, [id]: message }));
      return null;
    }
    setDraftErrors((prev) => ({ ...prev, [id]: null }));
    return text;
  };

  const handleApprove = async (hospital) => {
    const id = hospital.hospitalId;
    setBusy(id, 'approve');
    try {
      await adminApi.approveHospital(id, { lastSeenEntryId: lastEntryId(hospital) });
      addToast({ title: 'Hospital Approved', message: `${hospital.name} can now use LifeLink.`, type: 'success' });
      fetchHospitals();
    } catch (err) {
      reportFailure(err, 'Approval Failed');
    } finally {
      setBusy(id, null);
    }
  };

  const handleReject = async (hospital) => {
    const id = hospital.hospitalId;
    const reason = draftText(id, 3, 'Please give a rejection reason (at least 3 characters).');
    if (!reason) return;
    setBusy(id, 'reject');
    try {
      const file = drafts[id]?.file;
      await adminApi.rejectHospital(id, {
        reason,
        reportDocumentName: file?.name || null,
        reportDocumentUrl: file?.url || null,
        lastSeenEntryId: lastEntryId(hospital)
      });
      addToast({
        title: 'Registration Rejected',
        message: 'The hospital can now reply and correct its details in the same conversation.',
        type: 'info'
      });
      clearDraft(id);
      fetchHospitals();
    } catch (err) {
      reportFailure(err, 'Rejection Failed');
    } finally {
      setBusy(id, null);
    }
  };

  const handleComment = async (hospital) => {
    const id = hospital.hospitalId;
    const message = draftText(id, 3, 'Please write a comment (at least 3 characters).');
    if (!message) return;
    setBusy(id, 'comment');
    try {
      const file = drafts[id]?.file;
      await adminApi.commentOnHospitalRegistration(id, {
        message,
        attachmentUrl: file?.url,
        attachmentName: file?.name,
        lastSeenEntryId: lastEntryId(hospital)
      });
      addToast({ title: 'Comment Sent', message: 'The hospital has been notified.', type: 'success' });
      clearDraft(id);
      fetchHospitals();
    } catch (err) {
      reportFailure(err, 'Comment Failed');
    } finally {
      setBusy(id, null);
    }
  };

  const filteredHospitals = hospitals.filter((h) => {
    if (activeTab === 'needsReview' && !needsReview(h)) return false;
    if (activeTab === 'waiting' && !waitingForHospital(h)) return false;
    if (activeTab === 'approved' && h.approvalStatus !== 'Approved') return false;

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      return [h.name, h.licenseNumber, h.registrationNumber, h.email, h.city].some((v) => v?.toLowerCase().includes(q));
    }
    return true;
  });

  const counts = {
    needsReview: hospitals.filter(needsReview).length,
    waiting: hospitals.filter(waitingForHospital).length,
    approved: hospitals.filter((h) => h.approvalStatus === 'Approved').length,
    all: hospitals.length
  };

  const getStatusBadge = (h) => {
    if (h.approvalStatus === 'Approved') {
      return (
        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30">
          <CheckCircle2 className="w-3 h-3" /> Approved
        </span>
      );
    }
    if (h.approvalStatus === 'AwaitingAdminReview') {
      return (
        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-blue-500/20 text-blue-600 dark:text-blue-400 border border-blue-500/30">
          <MessageSquare className="w-3 h-3" /> Awaiting Admin Review
        </span>
      );
    }
    if (h.approvalStatus === 'Rejected') {
      return (
        <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-rose-500/20 text-rose-600 dark:text-rose-400 border border-rose-500/30">
          <XCircle className="w-3 h-3" /> Rejected · Waiting for Hospital
        </span>
      );
    }
    return (
      <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-amber-500/20 text-amber-600 dark:text-amber-400 border border-amber-500/30">
        <Clock className="w-3 h-3" /> Pending Review
      </span>
    );
  };

  const renderDecisionPanel = (h) => {
    const id = h.hospitalId;
    const isPending = h.approvalStatus === 'Pending';
    const isAwaitingReview = h.approvalStatus === 'AwaitingAdminReview';
    const canReject = isPending || isAwaitingReview; // never while waiting for the hospital
    const canComment = !isPending; // request more information / add to the request
    const draft = drafts[id] || { text: '', file: null };
    const panel = isPending
      ? {
          title: 'Review Decision',
          hint: 'Approve the registration, or reject it with a reason. The hospital then replies in this conversation.',
          label: 'Rejection reason (required to reject)'
        }
      : isAwaitingReview
      ? {
          title: 'Hospital Replied · Your Decision',
          hint: 'Approve, reject again with a reason, or request more information. The hospital cannot reply again until you act.',
          label: 'Rejection reason or request for more information'
        }
      : {
          title: 'Waiting for the Hospital',
          hint: 'The hospital has been asked to reply. You can add to your request or approve; rejecting again is possible once the hospital replies.',
          label: 'Comment to the hospital'
        };

    if (h.approvalStatus === 'Approved') {
      return (
        <div className="p-4 bg-emerald-50 dark:bg-emerald-950/30 rounded-xl border border-emerald-200 dark:border-emerald-900/60 text-xs text-emerald-800 dark:text-emerald-300 flex items-start gap-2">
          <Lock className="w-4 h-4 shrink-0 mt-0.5" />
          <span>
            Approved{h.approvedAt ? ` on ${new Date(h.approvedAt).toLocaleDateString()}` : ''}. Approved registrations are
            read-only; suspension is managed from the Admin Dashboard.
          </span>
        </div>
      );
    }

    return (
      <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-3">
        <div className="pb-2 border-b border-slate-200 dark:border-slate-800">
          <h4 className="font-bold text-xs uppercase tracking-wider text-slate-800 dark:text-slate-200 flex items-center gap-1.5">
            <ShieldCheck className="w-4 h-4 text-cyan-500" />
            {panel.title}
          </h4>
          <p className="text-[11px] text-slate-400 mt-0.5">{panel.hint}</p>
        </div>

        <div>
          <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1 text-xs">{panel.label}</label>
          <textarea
            rows={2}
            maxLength={canReject ? 500 : 1000}
            value={draft.text}
            onChange={(e) => {
              setDraft(id, { text: e.target.value });
              if (draftErrors[id]) setDraftErrors((prev) => ({ ...prev, [id]: null }));
            }}
            placeholder={
              isPending
                ? 'e.g. License expired on PHSRC database; please upload a valid 2026 accreditation certificate.'
                : 'e.g. Thanks. The accreditation certificate is still missing.'
            }
            className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-cyan-500"
          />
          {draftErrors[id] && <span className="text-[11px] text-rose-500 font-medium mt-1 block">{draftErrors[id]}</span>}
        </div>

        <div className="flex items-center gap-3 flex-wrap">
          <label className="px-3 py-1.5 bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 text-xs font-medium rounded-xl border border-slate-300 dark:border-slate-700 cursor-pointer flex items-center gap-1.5">
            <Upload className="w-3.5 h-3.5 text-cyan-500" />
            <span>{isPending ? 'Attach review report (optional)' : 'Attach a file (optional)'}</span>
            <input type="file" accept=".pdf,.doc,.docx,.png,.jpg,.jpeg" className="hidden" onChange={(e) => handleDraftFile(e, id)} />
          </label>
          {draft.file ? (
            <span className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
              <FileText className="w-3.5 h-3.5 text-emerald-500" />
              <span className="font-mono truncate max-w-[200px]">{draft.file.name}</span>
              <button type="button" onClick={() => setDraft(id, { file: null })} className="text-rose-500 hover:underline text-[11px]">
                Remove
              </button>
            </span>
          ) : (
            <span className="text-[11px] text-slate-400">No file chosen</span>
          )}
        </div>

        <div className="pt-1 flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={() => handleApprove(h)}
            disabled={!!actionLoading[id]}
            className="px-5 py-2.5 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs rounded-xl shadow-md flex items-center gap-2 disabled:opacity-50"
          >
            <CheckCircle2 className="w-4 h-4" />
            {actionLoading[id] === 'approve' ? 'Approving...' : 'Approve Hospital'}
          </button>
          {canReject && (
            <button
              type="button"
              onClick={() => handleReject(h)}
              disabled={!!actionLoading[id]}
              className="px-5 py-2.5 bg-rose-600 hover:bg-rose-700 text-white font-semibold text-xs rounded-xl shadow-md flex items-center gap-2 disabled:opacity-50"
            >
              <XCircle className="w-4 h-4" />
              {actionLoading[id] === 'reject' ? 'Rejecting...' : isAwaitingReview ? 'Reject Again' : 'Reject Registration'}
            </button>
          )}
          {canComment && (
            <button
              type="button"
              onClick={() => handleComment(h)}
              disabled={!!actionLoading[id]}
              className="px-5 py-2.5 bg-purple-600 hover:bg-purple-700 text-white font-semibold text-xs rounded-xl shadow-md flex items-center gap-2 disabled:opacity-50"
            >
              <Send className="w-4 h-4" />
              {actionLoading[id] === 'comment' ? 'Sending...' : isAwaitingReview ? 'Request More Information' : 'Send Comment'}
            </button>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
            <Building2 className="w-6 h-6 text-cyan-500" />
            Hospital Registration Queue
          </h1>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Review registrations, talk with hospitals about rejected ones, and view approved hospitals' registration records.
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

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-1.5 p-1 bg-slate-100 dark:bg-slate-800/80 rounded-xl text-xs font-medium">
            {TABS.map((tab) => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                className={`px-3 py-1.5 rounded-lg transition-all ${
                  activeTab === tab.key
                    ? `${tab.active} text-white shadow-sm font-semibold`
                    : 'text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white'
                }`}
              >
                {tab.label} ({counts[tab.key]})
              </button>
            ))}
          </div>

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

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm">
        {loading ? (
          <div className="p-12 text-center text-slate-500 dark:text-slate-400 flex flex-col items-center justify-center">
            <RotateCw className="w-6 h-6 animate-spin text-cyan-500 mb-2" />
            <span className="text-xs">Loading hospital registrations...</span>
          </div>
        ) : filteredHospitals.length === 0 ? (
          <div className="p-12 text-center text-slate-500 dark:text-slate-400 space-y-2">
            <Building2 className="w-10 h-10 text-slate-400 dark:text-slate-600 mx-auto" />
            <p className="text-xs font-semibold">No hospital registrations found in this category.</p>
          </div>
        ) : (
          <div className="divide-y divide-slate-200 dark:divide-slate-800">
            {filteredHospitals.map((hospital) => {
              const id = hospital.hospitalId;
              const isExpanded = expandedId === id;

              return (
                <div key={id} className="transition-colors hover:bg-slate-50/50 dark:hover:bg-slate-800/30">
                  <div className="p-4 sm:px-6 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                    <div className="space-y-1 min-w-[220px]">
                      <span className="font-bold text-sm text-slate-900 dark:text-slate-100">{hospital.name}</span>
                      <div className="text-xs text-slate-500 dark:text-slate-400 font-mono">
                        License / Reg:{' '}
                        <span className="text-cyan-600 dark:text-cyan-400 font-bold">
                          {hospital.licenseNumber || hospital.registrationNumber || 'N/A'}
                        </span>
                      </div>
                    </div>

                    <div className="text-xs text-slate-600 dark:text-slate-300">
                      <div>{hospital.email}</div>
                      <div className="text-[11px] text-slate-400">{hospital.contactNumber || 'No phone'}</div>
                    </div>

                    <div className="text-xs text-slate-600 dark:text-slate-300 flex items-center gap-1">
                      <MapPin className="w-3.5 h-3.5 text-slate-400" />
                      <span>{hospital.city || 'Not specified'}</span>
                    </div>

                    <div className="flex flex-wrap items-center gap-1.5">
                      {getStatusBadge(hospital)}
                      {hospital.isSuspended && (
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-slate-500/20 text-slate-600 dark:text-slate-300 border border-slate-500/30">
                          <PauseCircle className="w-3 h-3" /> Suspended
                        </span>
                      )}
                    </div>

                    <button
                      type="button"
                      onClick={() => setExpandedId(isExpanded ? null : id)}
                      className="px-3 py-1.5 rounded-xl text-xs font-semibold flex items-center gap-1.5 border bg-slate-100 hover:bg-slate-200 dark:bg-slate-800/80 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700"
                    >
                      <span>{isExpanded ? 'Hide Details' : needsReview(hospital) ? 'Review' : 'View Details'}</span>
                      {isExpanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                    </button>
                  </div>

                  {isExpanded && (
                    <div className="p-5 sm:p-6 bg-slate-50/80 dark:bg-slate-950/60 border-t border-slate-200 dark:border-slate-800 space-y-6">
                      <div className="flex items-center justify-between pb-2 border-b border-slate-200 dark:border-slate-800">
                        <h3 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                          <Building2 className="w-4 h-4 text-cyan-500" />
                          Registration Details
                        </h3>
                        <span className="text-[11px] text-slate-500 dark:text-slate-400">
                          Registered: {hospital.createdAt ? new Date(hospital.createdAt).toLocaleDateString() : 'N/A'}
                        </span>
                      </div>

                      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 text-xs">
                        <DetailCard label="Hospital Name">{hospital.name}</DetailCard>
                        <DetailCard label="License Number">
                          <span className="font-mono text-cyan-600 dark:text-cyan-400">{hospital.licenseNumber || 'N/A'}</span>
                        </DetailCard>
                        <DetailCard label="Registration / Facility Code">
                          <span className="font-mono">{hospital.registrationNumber || 'N/A'}</span>
                        </DetailCard>
                        <DetailCard label="Official Hospital Email">
                          <span className="flex items-center gap-1.5"><Mail className="w-3.5 h-3.5 text-slate-400" />{hospital.email}</span>
                        </DetailCard>
                        <DetailCard label="Hospital Contact Number">
                          <span className="flex items-center gap-1.5"><Phone className="w-3.5 h-3.5 text-slate-400" />{hospital.contactNumber || 'Not provided'}</span>
                        </DetailCard>
                        <DetailCard label="City / District / Region">{hospital.city || 'Not specified'}</DetailCard>
                        <DetailCard label="Physical Facility Address" wide>{hospital.address || 'N/A'}</DetailCard>
                        <DetailCard label="Authorized Person">
                          <span className="flex items-center gap-1.5"><User className="w-3.5 h-3.5 text-slate-400" />{hospital.contactPersonName || 'Not provided'}</span>
                          <span className="text-[11px] text-slate-400 block mt-0.5">
                            {hospital.contactPersonPhone || 'No phone'}
                            {hospital.contactPersonEmail ? ` • ${hospital.contactPersonEmail}` : ''}
                          </span>
                        </DetailCard>
                      </div>

                      <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-3">
                        <h4 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                          <FileText className="w-4 h-4 text-cyan-500" />
                          Accreditation & Verification Documents
                        </h4>
                        <div className="flex flex-wrap items-center gap-3">
                          <AttachmentLink label="License" url={hospital.licenseDocumentUrl} name={hospital.licenseDocumentName} onPreview={setPreviewDoc} />
                          <AttachmentLink label="Accreditation" url={hospital.accreditationDocumentUrl} name={hospital.accreditationDocumentName} onPreview={setPreviewDoc} />
                        </div>
                      </div>

                      <div className="p-4 bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 space-y-3">
                        <h4 className="font-bold text-xs uppercase tracking-wider text-slate-700 dark:text-slate-300 flex items-center gap-1.5">
                          <MessageSquare className="w-4 h-4 text-cyan-500" />
                          Registration Conversation
                        </h4>
                        <RegistrationThread entries={hospital.approvalHistory} onPreview={setPreviewDoc} />
                      </div>

                      {renderDecisionPanel(hospital)}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {previewDoc && (
        <DocumentPreviewModal
          isOpen={!!previewDoc}
          onClose={() => setPreviewDoc(null)}
          title={previewDoc.title || 'Document'}
          documentUrl={previewDoc.url}
          documentName={previewDoc.name}
        />
      )}
    </div>
  );
};

export default HospitalManagementPage;
