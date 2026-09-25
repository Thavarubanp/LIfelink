import React, { useCallback, useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { hospitalApi } from '../../api';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { readFileAsAttachment } from '../../utils/fileUtils';
import { DocumentPreviewModal } from '../../components/common/DocumentPreviewModal';
import AttachmentLink from '../../components/common/AttachmentLink';
import RegistrationThread from '../../components/hospital/RegistrationThread';
import {
  Clock,
  ShieldCheck,
  LogOut,
  LogIn,
  RotateCw,
  ArrowRight,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Upload,
  Send,
  Paperclip,
  MessageSquare,
  ChevronDown,
  ChevronUp
} from 'lucide-react';

const TEN_DIGITS = /^\d{10}$/;
const PHONE_FIELDS = ['contactNumber', 'contactPersonPhone'];

const correctionsFrom = (h) => ({
  name: h?.name || '',
  licenseNumber: h?.licenseNumber || '',
  registrationNumber: h?.registrationNumber || '',
  contactNumber: h?.contactNumber || '',
  address: h?.address || '',
  city: h?.city || '',
  contactPersonName: h?.contactPersonName || '',
  contactPersonPhone: h?.contactPersonPhone || '',
  contactPersonEmail: h?.contactPersonEmail || ''
});

const CORRECTION_FIELDS = [
  { name: 'name', label: 'Hospital Name' },
  { name: 'licenseNumber', label: 'License Number / PHSRC Code', mono: true },
  { name: 'registrationNumber', label: 'Registration / Facility Code', mono: true },
  { name: 'contactNumber', label: 'Hospital Contact Number *', type: 'tel', placeholder: '10 digits' },
  { name: 'city', label: 'City / District / Region' },
  { name: 'contactPersonName', label: 'Authorized Person Name *' },
  { name: 'contactPersonPhone', label: 'Authorized Person Phone Number *', type: 'tel', placeholder: '10 digits' },
  { name: 'contactPersonEmail', label: 'Authorized Person Email', type: 'email' },
  { name: 'address', label: 'Physical Hospital Address', wide: true }
];

const inputClass =
  'w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 text-xs';

/**
 * The hospital's registration page: status, submitted details and documents, and the registration conversation
 * with the administrator. While the registration is rejected, the hospital replies here and can correct its details.
 */
export const WaitingForApprovalPage = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const signedIn = !!user;

  const [hospital, setHospital] = useState(location.state?.hospital || null);
  const [loading, setLoading] = useState(signedIn);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState('');
  const [previewDoc, setPreviewDoc] = useState(null);

  // Reply form
  const [message, setMessage] = useState('');
  const [attachment, setAttachment] = useState(null);
  const [showCorrections, setShowCorrections] = useState(false);
  const [corrections, setCorrections] = useState(correctionsFrom(null));
  const [documents, setDocuments] = useState({ license: null, accreditation: null });
  const [formError, setFormError] = useState('');
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);

  const loadHospital = useCallback(async () => {
    if (!signedIn) return;
    setLoadError('');
    try {
      setHospital(await hospitalApi.getMyHospital());
    } catch (err) {
      setLoadError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [signedIn]);

  useEffect(() => {
    loadHospital();
  }, [loadHospital]);

  useEffect(() => {
    setCorrections(correctionsFrom(hospital));
  }, [hospital]);

  const handleRefresh = async () => {
    setRefreshing(true);
    await loadHospital();
    setRefreshing(false);
  };

  const handleCorrectionChange = (e) => {
    const { name, value } = e.target;
    const clean = PHONE_FIELDS.includes(name) ? value.replace(/\D/g, '').slice(0, 10) : value;
    setCorrections((prev) => ({ ...prev, [name]: clean }));
  };

  const handleFile = async (e, target) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    try {
      const picked = await readFileAsAttachment(file);
      if (target === 'attachment') {
        setAttachment(picked);
      } else {
        setDocuments((prev) => ({ ...prev, [target]: picked }));
      }
      setFormError('');
    } catch (err) {
      setFormError(err.message);
    }
  };

  const correctionError = () => {
    if (!showCorrections) return '';
    if (!corrections.contactPersonName.trim()) return 'Authorized person name is required.';
    if (!TEN_DIGITS.test(corrections.contactPersonPhone.trim())) return 'Authorized person phone number must be exactly 10 digits.';
    if (!TEN_DIGITS.test(corrections.contactNumber.trim())) return 'Hospital contact number must be exactly 10 digits.';
    return '';
  };

  const handleReply = async (e) => {
    e.preventDefault();
    if (message.trim().length < 3) {
      setFormError('Please write a message of at least 3 characters.');
      return;
    }
    const invalid = correctionError();
    if (invalid) {
      setFormError(invalid);
      return;
    }

    setSending(true);
    setFormError('');
    setSent(false);
    try {
      const payload = { message: message.trim(), attachmentUrl: attachment?.url, attachmentName: attachment?.name };
      if (showCorrections) {
        // Unchanged values are ignored by the server; only real corrections are recorded in the conversation
        Object.entries(corrections).forEach(([key, value]) => {
          payload[key] = value.trim() || undefined;
        });
        if (documents.license) {
          payload.licenseDocumentUrl = documents.license.url;
          payload.licenseDocumentName = documents.license.name;
        }
        if (documents.accreditation) {
          payload.accreditationDocumentUrl = documents.accreditation.url;
          payload.accreditationDocumentName = documents.accreditation.name;
        }
      }
      setHospital(await hospitalApi.replyToRegistration(hospital.hospitalId, payload));
      setMessage('');
      setAttachment(null);
      setDocuments({ license: null, accreditation: null });
      setShowCorrections(false);
      setSent(true);
    } catch (err) {
      setFormError(getApiErrorMessage(err));
      if (err.response?.status === 409) loadHospital();
    } finally {
      setSending(false);
    }
  };

  const status = hospital?.approvalStatus;
  const isApproved = status === 'Approved';
  const isRejected = status === 'Rejected';
  const awaitingAdmin = isRejected && hospital?.awaitingAdminReview;

  const statusBadge = isApproved
    ? { text: 'Registration Approved', icon: CheckCircle2, className: 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30' }
    : awaitingAdmin
    ? { text: 'Rejected · Your Reply Is With the Administrator', icon: Clock, className: 'bg-blue-500/20 text-blue-400 border-blue-500/30' }
    : isRejected
    ? { text: 'Rejected · Please Reply or Send Corrections', icon: XCircle, className: 'bg-rose-500/20 text-rose-400 border-rose-500/30' }
    : { text: 'Pending Administrator Review', icon: Clock, className: 'bg-amber-500/20 text-amber-400 border-amber-500/30' };
  const StatusIcon = statusBadge.icon;

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-12 relative overflow-hidden text-slate-100">
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[750px] h-[750px] bg-cyan-600/10 rounded-full blur-3xl pointer-events-none" />

      <div className="max-w-2xl w-full relative z-10 space-y-6">
        <div className="text-center">
          <Link to="/" className="inline-flex items-center gap-2 mb-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-cyan-600 to-cyan-500 flex items-center justify-center text-white text-xl font-bold shadow-lg shadow-cyan-600/30">
              🏥
            </div>
            <span className="text-2xl font-bold text-white tracking-tight">
              Life<span className="text-cyan-500">Link</span>
            </span>
          </Link>
          <h1 className="text-xl font-bold text-slate-100">Healthcare Facility Portal</h1>
          <p className="text-xs text-slate-400 mt-0.5">Hospital registration status and review conversation</p>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl space-y-6">
          {loading ? (
            <div className="py-10 flex flex-col items-center gap-2 text-xs text-slate-400">
              <RotateCw className="w-6 h-6 animate-spin text-cyan-500" />
              Loading your registration...
            </div>
          ) : !hospital ? (
            <div className="p-4 bg-slate-800/60 rounded-xl border border-slate-700 text-xs text-slate-300 text-center space-y-3">
              <p>{loadError || 'Sign in with your hospital account to see your registration.'}</p>
              {!signedIn && (
                <Link to="/login" className="inline-flex items-center gap-1.5 px-4 py-2 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold rounded-xl">
                  <LogIn className="w-3.5 h-3.5" /> Sign In
                </Link>
              )}
            </div>
          ) : (
            <>
              {/* Status */}
              <div className="text-center space-y-3">
                <span className={`inline-flex items-center gap-1.5 px-3.5 py-1 rounded-full text-xs font-bold border uppercase tracking-wider ${statusBadge.className}`}>
                  <StatusIcon className="w-3.5 h-3.5" /> {statusBadge.text}
                </span>
                <h2 className="text-xl font-bold text-white">{hospital.name}</h2>
                <div className="flex flex-wrap items-center justify-center gap-3 text-xs text-slate-400">
                  <span>Email: <strong className="text-slate-300">{hospital.email}</strong></span>
                  {hospital.licenseNumber && (
                    <span>License: <strong className="font-mono text-cyan-400">{hospital.licenseNumber}</strong></span>
                  )}
                </div>
              </div>

              {!signedIn && (
                <div className="p-3.5 bg-cyan-950/40 border border-cyan-900/60 rounded-xl text-xs text-cyan-100 flex items-start gap-2.5">
                  <ShieldCheck className="w-4 h-4 text-cyan-400 shrink-0 mt-0.5" />
                  <span>
                    Your registration was submitted. Sign in with your hospital account to check its latest status, read the
                    administrator's messages and reply.
                  </span>
                </div>
              )}

              {loadError && (
                <div className="p-3 bg-red-950/60 border border-red-900 rounded-xl text-xs text-red-200 flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 text-red-400" /> {loadError}
                </div>
              )}

              {/* Submitted details */}
              <div className="p-4 bg-slate-800/40 rounded-xl border border-slate-800 space-y-3 text-xs">
                <span className="text-[11px] font-bold text-slate-400 uppercase tracking-wider block">Registration Details</span>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-slate-300">
                  <div><span className="text-slate-500">Hospital Contact Number:</span> {hospital.contactNumber || 'Not provided'}</div>
                  <div><span className="text-slate-500">Registration Code:</span> {hospital.registrationNumber || 'Not provided'}</div>
                  <div><span className="text-slate-500">Authorized Person:</span> {hospital.contactPersonName || 'Not provided'}</div>
                  <div><span className="text-slate-500">Authorized Person Phone:</span> {hospital.contactPersonPhone || 'Not provided'}</div>
                  <div className="sm:col-span-2">
                    <span className="text-slate-500">Address:</span> {hospital.address || 'Not provided'}
                    {hospital.city ? `, ${hospital.city}` : ''}
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 pt-1">
                  <AttachmentLink label="License" url={hospital.licenseDocumentUrl} name={hospital.licenseDocumentName} onPreview={setPreviewDoc} />
                  <AttachmentLink label="Accreditation" url={hospital.accreditationDocumentUrl} name={hospital.accreditationDocumentName} onPreview={setPreviewDoc} />
                </div>
              </div>

              {/* Registration conversation */}
              <div className="space-y-2">
                <span className="text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
                  <MessageSquare className="w-3.5 h-3.5 text-cyan-400" /> Registration Conversation
                </span>
                <RegistrationThread entries={hospital.approvalHistory} onPreview={setPreviewDoc} />
              </div>

              {/* Reply form: only while rejected */}
              {signedIn && isRejected && (
                <form onSubmit={handleReply} className="pt-4 border-t border-slate-800 space-y-3 text-xs">
                  <h3 className="font-bold text-sm text-slate-100 flex items-center gap-2">
                    <Send className="w-4 h-4 text-cyan-400" /> Reply to the Administrator
                  </h3>
                  <p className="text-[11px] text-slate-400">
                    Your registration stays in this conversation until the administrator approves it. Explain your changes
                    and, if needed, correct your details or upload new documents.
                  </p>

                  {sent && (
                    <div className="p-3 bg-emerald-950/60 border border-emerald-900 rounded-xl text-emerald-200 flex items-center gap-2">
                      <CheckCircle2 className="w-4 h-4 text-emerald-400" /> Your reply was sent to the administrator.
                    </div>
                  )}
                  {formError && (
                    <div className="p-3 bg-red-950/60 border border-red-900 rounded-xl text-red-200 flex items-center gap-2">
                      <AlertCircle className="w-4 h-4 text-red-400" /> {formError}
                    </div>
                  )}

                  <textarea
                    rows={3}
                    maxLength={1000}
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                    placeholder="Write your reply to the administrator..."
                    className={inputClass}
                  />

                  <label className="flex items-center gap-2 px-3 py-2 rounded-xl border border-dashed border-slate-600 text-slate-300 cursor-pointer hover:bg-slate-800">
                    <Paperclip className="w-3.5 h-3.5 text-cyan-400" />
                    <span className="truncate">{attachment ? attachment.name : 'Attach a file (optional, max 2 MB)'}</span>
                    <input type="file" className="hidden" onChange={(e) => handleFile(e, 'attachment')} />
                  </label>

                  <button
                    type="button"
                    onClick={() => setShowCorrections((v) => !v)}
                    className="text-cyan-400 hover:underline flex items-center gap-1"
                  >
                    {showCorrections ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                    {showCorrections ? 'Hide registration details' : 'Also correct registration details or documents'}
                  </button>

                  {showCorrections && (
                    <div className="p-3 bg-slate-800/50 rounded-xl border border-slate-700/60 space-y-3">
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                        {CORRECTION_FIELDS.map((f) => (
                          <div key={f.name} className={f.wide ? 'md:col-span-2' : ''}>
                            <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">{f.label}</label>
                            <input
                              type={f.type || 'text'}
                              name={f.name}
                              value={corrections[f.name]}
                              onChange={handleCorrectionChange}
                              placeholder={f.placeholder}
                              inputMode={PHONE_FIELDS.includes(f.name) ? 'numeric' : undefined}
                              className={`${inputClass} ${f.mono ? 'font-mono' : ''}`}
                            />
                          </div>
                        ))}
                      </div>
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                        {[
                          { key: 'license', label: 'New PHSRC / MOH License File' },
                          { key: 'accreditation', label: 'New Accreditation Certificate File' }
                        ].map((doc) => (
                          <label
                            key={doc.key}
                            className="flex items-center gap-2 px-3 py-2 bg-slate-800 hover:bg-slate-700 border border-dashed border-slate-600 rounded-lg text-slate-300 cursor-pointer"
                          >
                            <Upload className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                            <span className="truncate">{documents[doc.key] ? documents[doc.key].name : doc.label}</span>
                            <input
                              type="file"
                              accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                              className="hidden"
                              onChange={(e) => handleFile(e, doc.key)}
                            />
                          </label>
                        ))}
                      </div>
                    </div>
                  )}

                  <button
                    type="submit"
                    disabled={sending}
                    className="w-full py-3 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold rounded-xl shadow-lg shadow-cyan-600/25 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
                  >
                    {sending ? <RotateCw className="w-4 h-4 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
                    {sending ? 'Sending...' : 'Send Reply'}
                  </button>
                </form>
              )}
            </>
          )}

          {/* Footer actions */}
          <div className="pt-2 flex flex-col sm:flex-row gap-3">
            {isApproved && signedIn ? (
              <button
                type="button"
                onClick={() => navigate('/hospital/dashboard', { replace: true })}
                className="w-full py-3 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs rounded-xl shadow-lg shadow-emerald-600/20 transition-all flex items-center justify-center gap-2"
              >
                <span>Go to Hospital Dashboard</span>
                <ArrowRight className="w-4 h-4" />
              </button>
            ) : signedIn ? (
              <button
                type="button"
                onClick={handleRefresh}
                disabled={refreshing}
                className="w-full py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-200 font-semibold text-xs rounded-xl border border-slate-700 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
              >
                <RotateCw className={`w-3.5 h-3.5 ${refreshing ? 'animate-spin' : ''}`} />
                <span>{refreshing ? 'Checking Status...' : 'Check Approval Status'}</span>
              </button>
            ) : (
              <Link
                to="/login"
                className="w-full py-2.5 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold text-xs rounded-xl transition-all flex items-center justify-center gap-2"
              >
                <LogIn className="w-3.5 h-3.5" /> <span>Sign In to Check Status</span>
              </Link>
            )}
            {signedIn && (
              <button
                type="button"
                onClick={logout}
                className="w-full py-2.5 bg-slate-800/60 hover:bg-slate-800 text-slate-400 hover:text-white font-semibold text-xs rounded-xl border border-slate-800 transition-colors flex items-center justify-center gap-2"
              >
                <LogOut className="w-3.5 h-3.5" />
                <span>Sign Out</span>
              </button>
            )}
          </div>
        </div>
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

export default WaitingForApprovalPage;
