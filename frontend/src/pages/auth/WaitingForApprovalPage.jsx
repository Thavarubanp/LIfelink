import React, { useState, useEffect } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { hospitalApi } from '../../api';
import { DocumentPreviewModal } from '../../components/common/DocumentPreviewModal';
import {
  Clock,
  ShieldCheck,
  Building2,
  LogOut,
  RotateCw,
  ArrowRight,
  CheckCircle2,
  AlertCircle,
  FileText,
  Upload,
  Send,
  Eye,
  User,
  Phone,
  Mail,
  MapPin,
  Sparkles,
  ChevronDown,
  ChevronUp
} from 'lucide-react';

export const WaitingForApprovalPage = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const [hospitalInfo, setHospitalInfo] = useState(location.state?.hospital || null);
  const [checking, setChecking] = useState(false);
  const [checkError, setCheckError] = useState('');
  const [isApproved, setIsApproved] = useState(false);

  // Edit / Resubmission Form State
  const [showEditForm, setShowEditForm] = useState(true);
  const [resubmitLoading, setResubmitLoading] = useState(false);
  const [resubmitSuccess, setResubmitSuccess] = useState(false);
  const [resubmitError, setResubmitError] = useState('');

  const [formData, setFormData] = useState({
    name: '',
    licenseNumber: '',
    registrationNumber: '',
    contactNumber: '',
    address: '',
    city: '',
    contactPersonName: '',
    contactPersonPhone: '',
    contactPersonEmail: '',
    licenseDocumentUrl: '',
    licenseDocumentName: '',
    accreditationDocumentUrl: '',
    accreditationDocumentName: '',
    comments: ''
  });

  // Document modal preview state
  const [previewDoc, setPreviewDoc] = useState(null);

  // Populate form data whenever hospitalInfo updates
  useEffect(() => {
    if (hospitalInfo) {
      setFormData({
        name: hospitalInfo.name || '',
        licenseNumber: hospitalInfo.licenseNumber || '',
        registrationNumber: hospitalInfo.registrationNumber || hospitalInfo.licenseNumber || '',
        contactNumber: hospitalInfo.contactNumber || '',
        address: hospitalInfo.address || '',
        city: hospitalInfo.city || '',
        contactPersonName: hospitalInfo.contactPersonName || '',
        contactPersonPhone: hospitalInfo.contactPersonPhone || '',
        contactPersonEmail: hospitalInfo.contactPersonEmail || '',
        licenseDocumentUrl: hospitalInfo.licenseDocumentUrl || '',
        licenseDocumentName: hospitalInfo.licenseDocumentName || '',
        accreditationDocumentUrl: hospitalInfo.accreditationDocumentUrl || '',
        accreditationDocumentName: hospitalInfo.accreditationDocumentName || '',
        comments: ''
      });

      if (hospitalInfo.isVerified || hospitalInfo.approvalStatus === 'Approved') {
        setIsApproved(true);
      }
    }
  }, [hospitalInfo]);

  // Fetch latest hospital status
  const fetchHospitalStatus = async () => {
    try {
      const hospitals = await hospitalApi.getHospitals();
      if (Array.isArray(hospitals)) {
        const userEmail = user?.email?.toLowerCase();
        const match = hospitals.find(
          (h) => h.email?.toLowerCase() === userEmail || h.name?.toLowerCase() === hospitalInfo?.name?.toLowerCase()
        );
        if (match) {
          setHospitalInfo(match);
          if (match.isVerified || match.approvalStatus === 'Approved') {
            setIsApproved(true);
          }
        }
      }
    } catch (err) {
      console.error('Error fetching hospital status:', err);
    }
  };

  useEffect(() => {
    fetchHospitalStatus();
  }, [user]);

  const handleCheckStatus = async () => {
    setChecking(true);
    setCheckError('');
    try {
      const hospitals = await hospitalApi.getHospitals();
      if (Array.isArray(hospitals)) {
        const userEmail = user?.email?.toLowerCase() || hospitalInfo?.email?.toLowerCase();
        const match = hospitals.find(
          (h) => h.email?.toLowerCase() === userEmail || h.name?.toLowerCase() === hospitalInfo?.name?.toLowerCase()
        );

        if (match) {
          setHospitalInfo(match);
          if (match.isVerified || match.approvalStatus === 'Approved') {
            setIsApproved(true);
            setTimeout(() => {
              navigate('/hospital/dashboard', { replace: true });
            }, 1200);
            return;
          }
          setCheckError(`Status updated: Currently ${match.approvalStatus}.`);
          return;
        }
      }
      setCheckError('Status checked: Waiting for administrator review.');
    } catch (err) {
      setCheckError('Unable to refresh status. Please try again.');
    } finally {
      setChecking(false);
    }
  };

  const handleInputChange = (e) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleFileUpload = (e, fieldPrefix) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      setFormData((prev) => ({
        ...prev,
        [`${fieldPrefix}Url`]: reader.result,
        [`${fieldPrefix}Name`]: file.name
      }));
    };
    reader.readAsDataURL(file);
  };

  const handleResubmit = async (e) => {
    e.preventDefault();
    if (!hospitalInfo?.hospitalId) return;

    setResubmitLoading(true);
    setResubmitError('');
    setResubmitSuccess(false);

    try {
      const payload = {
        name: formData.name.trim(),
        licenseNumber: formData.licenseNumber.trim(),
        registrationNumber: formData.registrationNumber.trim(),
        contactNumber: formData.contactNumber.trim(),
        address: formData.address.trim(),
        city: formData.city.trim() || undefined,
        contactPersonName: formData.contactPersonName.trim() || undefined,
        contactPersonPhone: formData.contactPersonPhone.trim() || undefined,
        contactPersonEmail: formData.contactPersonEmail.trim() || undefined,
        licenseDocumentUrl: formData.licenseDocumentUrl || undefined,
        licenseDocumentName: formData.licenseDocumentName || undefined,
        accreditationDocumentUrl: formData.accreditationDocumentUrl || undefined,
        accreditationDocumentName: formData.accreditationDocumentName || undefined,
        comments: formData.comments.trim() || undefined
      };

      const updated = await hospitalApi.resubmitHospital(hospitalInfo.hospitalId, payload);
      setHospitalInfo(updated);
      setResubmitSuccess(true);
      setShowEditForm(false);
    } catch (err) {
      console.error('Failed to resubmit hospital application:', err);
      setResubmitError(err.response?.data?.message || 'Failed to resubmit application. Please try again.');
    } finally {
      setResubmitLoading(false);
    }
  };

  const isRejected = hospitalInfo?.approvalStatus === 'Rejected';
  const isResubmitted = hospitalInfo?.approvalStatus === 'Resubmitted';

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-12 relative overflow-hidden text-slate-100">
      {/* Background Glow */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[750px] h-[750px] bg-cyan-600/10 rounded-full blur-3xl pointer-events-none" />

      <div className="max-w-2xl w-full relative z-10 space-y-6">
        {/* Top Brand Nav */}
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
          <p className="text-xs text-slate-400 mt-0.5">
            Official Hospital Onboarding & Verification Status
          </p>
        </div>

        {/* Main Status Container */}
        <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl backdrop-blur-md space-y-6">
          {/* Status Header */}
          <div className="text-center space-y-3">
            {isApproved ? (
              <div className="w-16 h-16 rounded-2xl bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center justify-center mx-auto shadow-lg shadow-emerald-500/10 animate-bounce">
                <CheckCircle2 className="w-8 h-8" />
              </div>
            ) : isRejected ? (
              <div className="w-16 h-16 rounded-2xl bg-rose-500/20 text-rose-400 border border-rose-500/30 flex items-center justify-center mx-auto shadow-lg shadow-rose-500/10">
                <XCircle className="w-8 h-8" />
              </div>
            ) : isResubmitted ? (
              <div className="w-16 h-16 rounded-2xl bg-blue-500/20 text-blue-400 border border-blue-500/30 flex items-center justify-center mx-auto shadow-lg shadow-blue-500/10">
                <Sparkles className="w-8 h-8 animate-pulse" />
              </div>
            ) : (
              <div className="w-16 h-16 rounded-2xl bg-amber-500/20 text-amber-400 border border-amber-500/30 flex items-center justify-center mx-auto shadow-lg shadow-amber-500/10">
                <Clock className="w-8 h-8 animate-pulse" />
              </div>
            )}

            <div>
              {isApproved ? (
                <span className="px-3.5 py-1 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 uppercase tracking-wider">
                  Status: Registration Approved & Active
                </span>
              ) : isRejected ? (
                <span className="px-3.5 py-1 rounded-full text-xs font-bold bg-rose-500/20 text-rose-400 border border-rose-500/30 uppercase tracking-wider">
                  Status: Registration Rejected / Revision Required
                </span>
              ) : isResubmitted ? (
                <span className="px-3.5 py-1 rounded-full text-xs font-bold bg-blue-500/20 text-blue-400 border border-blue-500/30 uppercase tracking-wider">
                  Status: Resubmission Under Review
                </span>
              ) : (
                <span className="px-3.5 py-1 rounded-full text-xs font-bold bg-amber-500/20 text-amber-400 border border-amber-500/30 uppercase tracking-wider">
                  Status: Pending Administrator Review
                </span>
              )}

              <h2 className="text-xl font-bold text-white mt-3">
                {hospitalInfo?.name || 'Healthcare Facility'}
              </h2>
              <div className="flex flex-wrap items-center justify-center gap-3 text-xs text-slate-400 mt-1">
                <span>Email: <strong className="text-slate-300">{hospitalInfo?.email || user?.email}</strong></span>
                {hospitalInfo?.licenseNumber && (
                  <span>
                    License: <strong className="font-mono text-cyan-400">{hospitalInfo.licenseNumber}</strong>
                  </span>
                )}
              </div>
            </div>
          </div>

          {/* REJECTION CARD: Reason & Attached Report */}
          {isRejected && (
            <div className="p-4 bg-rose-950/40 border border-rose-900/60 rounded-xl space-y-3 animate-in fade-in">
              <div className="flex items-start gap-2.5">
                <AlertCircle className="w-5 h-5 text-rose-400 shrink-0 mt-0.5" />
                <div className="space-y-1 flex-1">
                  <h4 className="font-bold text-sm text-rose-200">Rejection Reason from LifeLink Admin</h4>
                  <p className="text-xs text-rose-100 bg-rose-900/30 p-3 rounded-lg border border-rose-800/40 leading-relaxed font-medium">
                    {hospitalInfo.rejectionReason || 'No specific rejection details provided.'}
                  </p>
                </div>
              </div>

              {/* Attached Review Report Button */}
              {hospitalInfo.rejectionReportUrl && (
                <div className="pt-2 border-t border-rose-900/40 flex items-center justify-between">
                  <span className="text-xs text-rose-300 font-medium flex items-center gap-1.5">
                    <FileText className="w-4 h-4 text-rose-400" />
                    Admin Attached Review / Audit Report:
                  </span>
                  <button
                    type="button"
                    onClick={() =>
                      setPreviewDoc({
                        title: 'Admin Review Report',
                        url: hospitalInfo.rejectionReportUrl,
                        name: hospitalInfo.rejectionReportName || 'Admin_Review_Report.pdf'
                      })
                    }
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-rose-600 hover:bg-rose-500 text-white font-semibold text-xs rounded-lg shadow-sm transition-colors"
                  >
                    <Eye className="w-3.5 h-3.5" />
                    <span>View / Download Attached Report</span>
                  </button>
                </div>
              )}
            </div>
          )}

          {/* RESUBMITTED INFO BANNER */}
          {isResubmitted && (
            <div className="p-4 bg-blue-950/40 border border-blue-900/60 rounded-xl space-y-2">
              <div className="flex items-start gap-2.5 text-xs text-blue-200">
                <Sparkles className="w-5 h-5 text-blue-400 shrink-0 mt-0.5" />
                <div>
                  <h4 className="font-bold text-sm text-blue-100">Updated Registration Submitted</h4>
                  <p className="text-blue-300 mt-0.5">
                    Your updated information has been submitted and is currently in the Administrator Queue for review.
                  </p>
                  {hospitalInfo.updatedFields && (
                    <p className="mt-1 text-[11px] text-blue-200">
                      <strong>Submitted Changes:</strong> {hospitalInfo.updatedFields}
                    </p>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* PENDING INFO BANNER */}
          {!isApproved && !isRejected && !isResubmitted && (
            <div className="p-4 bg-slate-800/60 rounded-xl border border-slate-700/60 text-xs text-slate-300 text-left leading-relaxed">
              <div className="flex items-start gap-2.5">
                <ShieldCheck className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                <div>
                  <h4 className="font-semibold text-slate-100">Verification Under Administrative Review</h4>
                  <p className="text-slate-400 text-[11px] mt-0.5">
                    Your hospital registration is currently being verified by a LifeLink Administrator. Once accredited, your clinical personnel can log in.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* APPROVAL AUDIT TIMELINE */}
          {hospitalInfo?.approvalHistory && hospitalInfo.approvalHistory.length > 0 && (
            <div className="p-4 bg-slate-800/40 rounded-xl border border-slate-800 space-y-2">
              <span className="text-[11px] font-bold text-slate-400 uppercase tracking-wider block">
                Approval Lifecycle History
              </span>
              <div className="space-y-1.5">
                {hospitalInfo.approvalHistory.map((step, idx) => (
                  <div
                    key={step.id || idx}
                    className="flex items-center justify-between text-xs p-2 rounded-lg bg-slate-800/70 border border-slate-700/50"
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-slate-200">{step.status}</span>
                      {step.comments && (
                        <span className="text-slate-400 text-[11px] truncate max-w-xs">• {step.comments}</span>
                      )}
                    </div>
                    <span className="text-[10px] text-slate-500 font-mono">
                      {step.timestamp ? new Date(step.timestamp).toLocaleDateString() : ''}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* RESUBMIT / UPDATE REGISTRATION FORM (Visible when rejected or user toggles) */}
          {isRejected && (
            <div className="pt-2 border-t border-slate-800">
              <div className="flex items-center justify-between mb-4">
                <h3 className="font-bold text-sm text-slate-100 flex items-center gap-2">
                  <FileText className="w-4 h-4 text-cyan-400" />
                  Update Registration Information & Resubmit
                </h3>
                <button
                  type="button"
                  onClick={() => setShowEditForm(!showEditForm)}
                  className="text-xs text-cyan-400 hover:underline flex items-center gap-1"
                >
                  <span>{showEditForm ? 'Hide Form' : 'Show Form'}</span>
                  {showEditForm ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                </button>
              </div>

              {resubmitSuccess && (
                <div className="mb-4 p-3.5 bg-emerald-950/60 border border-emerald-900 rounded-xl text-xs text-emerald-200 flex items-center gap-2">
                  <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                  <span>Your changes were saved and resubmitted to the Admin Queue successfully!</span>
                </div>
              )}

              {resubmitError && (
                <div className="mb-4 p-3.5 bg-red-950/60 border border-red-900 rounded-xl text-xs text-red-200 flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 text-red-400" />
                  <span>{resubmitError}</span>
                </div>
              )}

              {showEditForm && (
                <form onSubmit={handleResubmit} className="space-y-4 text-xs">
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        Hospital Name
                      </label>
                      <input
                        type="text"
                        name="name"
                        value={formData.name}
                        onChange={handleInputChange}
                        required
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>

                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        License Number / PHSRC Code
                      </label>
                      <input
                        type="text"
                        name="licenseNumber"
                        value={formData.licenseNumber}
                        onChange={handleInputChange}
                        required
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white font-mono placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>

                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        Registration / Facility Code
                      </label>
                      <input
                        type="text"
                        name="registrationNumber"
                        value={formData.registrationNumber}
                        onChange={handleInputChange}
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white font-mono placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>

                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        Emergency Contact Phone
                      </label>
                      <input
                        type="tel"
                        name="contactNumber"
                        value={formData.contactNumber}
                        onChange={handleInputChange}
                        required
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>

                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        City / District / Region
                      </label>
                      <input
                        type="text"
                        name="city"
                        value={formData.city}
                        onChange={handleInputChange}
                        placeholder="e.g. Colombo, Kandy"
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>

                    <div>
                      <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                        Authorized Contact Person
                      </label>
                      <input
                        type="text"
                        name="contactPersonName"
                        value={formData.contactPersonName}
                        onChange={handleInputChange}
                        placeholder="e.g. Dr. Silva"
                        className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                      Physical Hospital Address
                    </label>
                    <input
                      type="text"
                      name="address"
                      value={formData.address}
                      onChange={handleInputChange}
                      required
                      className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                    />
                  </div>

                  {/* Re-upload Document Section */}
                  <div className="p-3 bg-slate-800/50 rounded-xl border border-slate-700/60 space-y-3">
                    <span className="text-[11px] font-bold text-slate-300 uppercase tracking-wider block">
                      Re-Upload Supporting Documents
                    </span>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      <div>
                        <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                          PHSRC / MOH License File
                        </label>
                        <input
                          type="file"
                          accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                          onChange={(e) => handleFileUpload(e, 'licenseDocument')}
                          className="hidden"
                          id="resubmit-license-file"
                        />
                        <label
                          htmlFor="resubmit-license-file"
                          className="flex items-center gap-2 px-3 py-2 bg-slate-800 hover:bg-slate-700 border border-dashed border-slate-600 rounded-lg text-slate-300 text-xs cursor-pointer transition-colors"
                        >
                          <Upload className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                          <span className="truncate">
                            {formData.licenseDocumentName || 'Choose License File (PDF/Image)'}
                          </span>
                        </label>
                      </div>

                      <div>
                        <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                          Accreditation Certificate File
                        </label>
                        <input
                          type="file"
                          accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                          onChange={(e) => handleFileUpload(e, 'accreditationDocument')}
                          className="hidden"
                          id="resubmit-accred-file"
                        />
                        <label
                          htmlFor="resubmit-accred-file"
                          className="flex items-center gap-2 px-3 py-2 bg-slate-800 hover:bg-slate-700 border border-dashed border-slate-600 rounded-lg text-slate-300 text-xs cursor-pointer transition-colors"
                        >
                          <Upload className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                          <span className="truncate">
                            {formData.accreditationDocumentName || 'Choose Accreditation Certificate'}
                          </span>
                        </label>
                      </div>
                    </div>
                  </div>

                  {/* Resubmission Comments */}
                  <div>
                    <label className="block text-slate-300 font-semibold mb-1 text-[11px] uppercase">
                      Notes / Explanation for Administrator (Optional)
                    </label>
                    <textarea
                      rows={2}
                      name="comments"
                      value={formData.comments}
                      onChange={handleInputChange}
                      placeholder="Explain the changes made or provide clarifications addressing the rejection reason..."
                      className="w-full px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500"
                    />
                  </div>

                  <button
                    type="submit"
                    disabled={resubmitLoading}
                    className="w-full py-3 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold text-xs rounded-xl shadow-lg shadow-cyan-600/25 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
                  >
                    {resubmitLoading ? (
                      <>
                        <RotateCw className="w-4 h-4 animate-spin" />
                        <span>Submitting Updated Registration...</span>
                      </>
                    ) : (
                      <>
                        <Send className="w-3.5 h-3.5" />
                        <span>Resubmit Application for Approval</span>
                      </>
                    )}
                  </button>
                </form>
              )}
            </div>
          )}

          {/* Feedback messages */}
          {checkError && (
            <div className="p-3 bg-amber-950/50 border border-amber-800/60 rounded-xl text-amber-200 text-xs text-center font-medium">
              {checkError}
            </div>
          )}

          {/* Footer Action Buttons */}
          <div className="pt-2 flex flex-col sm:flex-row gap-3">
            {isApproved ? (
              <Link
                to="/hospital/dashboard"
                className="w-full py-3 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs rounded-xl shadow-lg shadow-emerald-600/20 transition-all flex items-center justify-center gap-2"
              >
                <span>Go to Hospital Dashboard</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
            ) : (
              <>
                <button
                  type="button"
                  onClick={handleCheckStatus}
                  disabled={checking}
                  className="w-full py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-200 font-semibold text-xs rounded-xl border border-slate-700 shadow-sm transition-all flex items-center justify-center gap-2 disabled:opacity-50"
                >
                  <RotateCw className={`w-3.5 h-3.5 ${checking ? 'animate-spin' : ''}`} />
                  <span>{checking ? 'Checking Status...' : 'Check Approval Status'}</span>
                </button>
                <button
                  type="button"
                  onClick={logout}
                  className="w-full py-2.5 bg-slate-800/60 hover:bg-slate-800 text-slate-400 hover:text-white font-semibold text-xs rounded-xl border border-slate-800 transition-colors flex items-center justify-center gap-2"
                >
                  <LogOut className="w-3.5 h-3.5" />
                  <span>Sign Out</span>
                </button>
              </>
            )}
          </div>
        </div>
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

export default WaitingForApprovalPage;
