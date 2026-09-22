import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { hospitalApi } from '../../api';
import { getApiErrorMessage, getApiFieldErrors } from '../../utils/errorUtils';
import {
  Building2,
  ShieldCheck,
  Mail,
  Lock,
  Phone,
  MapPin,
  FileText,
  Clock,
  AlertCircle,
  ArrowRight,
  Loader2,
  Info,
  Upload,
  User
} from 'lucide-react';

export const RegisterHospitalPage = () => {
  const navigate = useNavigate();
  const [formData, setFormData] = useState({
    name: '',
    institutionType: 'Private Hospital',
    registrationNumber: '',
    email: '',
    password: '',
    contactNumber: '',
    address: '',
    city: '',
    contactPersonName: '',
    contactPersonPhone: '',
    contactPersonEmail: '',
    licenseDocumentUrl: '',
    licenseDocumentName: '',
    accreditationDocumentUrl: '',
    accreditationDocumentName: ''
  });

  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});
  const [loading, setLoading] = useState(false);
  const [submittedHospital, setSubmittedHospital] = useState(null);

  const institutionTypeOptions = [
    'Private Hospital',
    'Nursing Home',
    'Medical Laboratory',
    'Medical / Channeling Centre',
    'Government Hospital'
  ];

  const isGovHospital = formData.institutionType === 'Government Hospital';

  // Real-time Validation Computations
  const isNameValid = formData.name.trim().length > 0;
  
  const phsrcRegex = /^PHSRC\/(PH|L|LAB|AS|AMB|MC)\/\d{1,4}$/i;
  const isRegistrationNumberValid = isGovHospital
    ? formData.registrationNumber.trim().length > 0
    : phsrcRegex.test(formData.registrationNumber.trim());

  const isContactNumberValid = /^\d{10}$/.test(formData.contactNumber.trim());
  const isEmailValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email.trim());

  // Password Validation Rules: Min 8 chars, 1 uppercase, 1 special char
  const hasMinLen = formData.password.length >= 8;
  const hasUpper = /[A-Z]/.test(formData.password);
  const hasSpecial = /[^a-zA-Z0-9]/.test(formData.password);
  const isPasswordValid = hasMinLen && hasUpper && hasSpecial;

  const isAddressValid = formData.address.trim().length > 0;

  const isFormValid =
    isNameValid &&
    isRegistrationNumberValid &&
    isContactNumberValid &&
    isEmailValid &&
    isPasswordValid &&
    isAddressValid;

  const getFieldBorderClass = (fieldName, isValid) => {
    const value = formData[fieldName];
    const hasServerError = fieldErrors[fieldName];

    if (hasServerError) {
      return 'border-red-500 focus:border-red-500';
    }

    if (!value) {
      return 'border-slate-700/80 focus:border-cyan-500';
    }

    if (isValid) {
      return 'border-emerald-500 focus:border-emerald-500';
    }

    return 'border-red-500 focus:border-red-500';
  };

  const handleChange = (e) => {
    const { name, value } = e.target;

    if (name === 'contactNumber') {
      // Allow numbers only, exactly max 10 digits
      const digitsOnly = value.replace(/\D/g, '').slice(0, 10);
      setFormData((prev) => ({ ...prev, contactNumber: digitsOnly }));
    } else {
      // Automatically convert PHSRC registration number to uppercase
      const finalValue = name === 'registrationNumber' && !isGovHospital ? value.toUpperCase() : value;
      setFormData((prev) => ({ ...prev, [name]: finalValue }));
    }

    if (fieldErrors[name]) {
      setFieldErrors((prev) => ({ ...prev, [name]: null }));
    }
    if (error) setError('');
  };

  const handleInstitutionTypeChange = (e) => {
    const newType = e.target.value;
    setFormData((prev) => ({
      ...prev,
      institutionType: newType,
      registrationNumber: '' // Clear number when switching type to avoid cross-validation confusion
    }));
    if (fieldErrors.registrationNumber) {
      setFieldErrors((prev) => ({ ...prev, registrationNumber: null }));
    }
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

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!isFormValid) return;

    setError('');
    setFieldErrors({});
    setLoading(true);

    try {
      // Maps directly to ASP.NET Core CreateHospitalDto
      const payload = {
        name: formData.name.trim(),
        licenseNumber: formData.registrationNumber.trim(),
        registrationNumber: formData.registrationNumber.trim(),
        email: formData.email.trim(),
        password: formData.password,
        contactNumber: formData.contactNumber.trim(),
        address: formData.address.trim(),
        city: formData.city.trim() || undefined,
        contactPersonName: formData.contactPersonName.trim() || undefined,
        contactPersonPhone: formData.contactPersonPhone.trim() || undefined,
        contactPersonEmail: formData.contactPersonEmail.trim() || undefined,
        licenseDocumentUrl: formData.licenseDocumentUrl || undefined,
        licenseDocumentName: formData.licenseDocumentName || undefined,
        accreditationDocumentUrl: formData.accreditationDocumentUrl || undefined,
        accreditationDocumentName: formData.accreditationDocumentName || undefined
      };

      const res = await hospitalApi.createHospital(payload);
      setSubmittedHospital(res);
      navigate('/hospital/waiting-approval', { state: { hospital: res } });
    } catch (err) {
      const mainMsg = getApiErrorMessage(err);
      const fields = getApiFieldErrors(err);
      setError(mainMsg);
      setFieldErrors(fields);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-12 relative overflow-hidden">
      {/* Background Cyan Glow */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[750px] h-[750px] bg-cyan-600/10 rounded-full blur-3xl pointer-events-none" />

      <div className="max-w-xl w-full relative z-10">
        {/* Top Brand Nav */}
        <div className="text-center mb-8">
          <Link to="/" className="inline-flex items-center gap-2 mb-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-cyan-600 to-cyan-500 flex items-center justify-center text-white text-xl font-bold shadow-lg shadow-cyan-600/30">
              🏥
            </div>
            <span className="text-2xl font-bold text-white tracking-tight">
              Life<span className="text-cyan-500">Link</span>
            </span>
          </Link>
          <h1 className="text-xl font-bold text-slate-100">Sri Lanka Healthcare Facility Registration</h1>
          <p className="text-xs text-slate-400 mt-1">
            Register medical institutions with PHSRC or Ministry of Health (MOH) accreditation
          </p>
        </div>

        {!submittedHospital ? (
          /* Registration Form */
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl backdrop-blur-md">
            {error && (
              <div className="mb-6 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md">
                <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
                <span className="leading-relaxed font-medium">{error}</span>
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-4 text-xs">
              {/* Institution Type Selection */}
              <div>
                <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                  Institution Type *
                </label>
                <div className="relative">
                  <Building2 className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <select
                    name="institutionType"
                    value={formData.institutionType}
                    onChange={handleInstitutionTypeChange}
                    className="w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-white focus:outline-none focus:border-cyan-500 transition-colors appearance-none cursor-pointer"
                  >
                    {institutionTypeOptions.map((opt) => (
                      <option key={opt} value={opt} className="bg-slate-900 text-white">
                        {opt}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              {/* Hospital Name */}
              <div>
                <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                  Hospital / Medical Center Name *
                </label>
                <div className="relative">
                  <Building2 className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <input
                    type="text"
                    name="name"
                    required
                    maxLength={200}
                    value={formData.name}
                    onChange={handleChange}
                    placeholder={
                      isGovHospital
                        ? 'e.g. National Hospital Colombo or Teaching Hospital Jaffna'
                        : 'e.g. Lanka Hospitals PLC or Asiri Surgical Hospital'
                    }
                    className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                      'name',
                      isNameValid
                    )}`}
                  />
                </div>
                {fieldErrors.name && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.name}</span>
                )}
              </div>

              {/* Conditional Registration Number Field (Binds to backend LicenseNumber column) */}
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="block text-slate-300 font-semibold uppercase tracking-wider text-[11px]">
                    {isGovHospital
                      ? 'Health Institution Number (HIN) / Government Facility Code *'
                      : 'PHSRC Registration Number *'}
                  </label>
                  {!isGovHospital && (
                    <span className="text-[10px] text-cyan-400 font-mono font-semibold uppercase">PHSRC Validated</span>
                  )}
                </div>

                <div className="relative">
                  <FileText className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <input
                    type="text"
                    name="registrationNumber"
                    required
                    maxLength={100}
                    value={formData.registrationNumber}
                    onChange={handleChange}
                    placeholder={isGovHospital ? 'e.g. HIN-SL-10492 or GOV-COL-001' : 'e.g. PHSRC/PH/1234'}
                    className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white font-mono placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                      'registrationNumber',
                      isRegistrationNumberValid
                    )}`}
                  />
                </div>

                {/* Helper text for PHSRC */}
                {!isGovHospital && (
                  <p className="text-[11px] text-slate-400 mt-1.5 flex items-start gap-1 leading-relaxed">
                    <Info className="w-3.5 h-3.5 text-cyan-400 shrink-0 mt-0.5" />
                    <span>For private hospitals and medical institutions registered under the Private Health Services Regulatory Council (PHSRC).</span>
                  </p>
                )}

                {formData.registrationNumber && !isGovHospital && !isRegistrationNumberValid && !fieldErrors.registrationNumber && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">
                    Enter a valid PHSRC registration number (e.g., PHSRC/PH/123).
                  </span>
                )}

                {fieldErrors.registrationNumber && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">
                    {fieldErrors.registrationNumber}
                  </span>
                )}
              </div>

              {/* Contact Number and Email */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                    Emergency Phone Contact *
                  </label>
                  <div className="relative">
                    <Phone className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                    <input
                      type="tel"
                      name="contactNumber"
                      required
                      maxLength={50}
                      value={formData.contactNumber}
                      onChange={handleChange}
                      placeholder="0112691111"
                      className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                        'contactNumber',
                        isContactNumberValid
                      )}`}
                    />
                  </div>
                  <p
                    className={`text-[11px] mt-1 ${
                      formData.contactNumber && !isContactNumberValid
                        ? 'text-red-400 font-medium'
                        : formData.contactNumber && isContactNumberValid
                        ? 'text-emerald-400 font-medium'
                        : 'text-slate-400'
                    }`}
                  >
                    Phone number must contain exactly 10 digits.
                  </p>
                  {fieldErrors.contactNumber && (
                    <span className="text-[11px] text-red-400 mt-1 block font-medium">
                      {fieldErrors.contactNumber}
                    </span>
                  )}
                </div>

                <div>
                  <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                    Official Hospital Email Address *
                  </label>
                  <div className="relative">
                    <Mail className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                    <input
                      type="email"
                      name="email"
                      required
                      maxLength={200}
                      value={formData.email}
                      onChange={handleChange}
                      placeholder={isGovHospital ? 'info@nhsl.health.gov.lk' : 'admissions@lankahospitals.com'}
                      className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                        'email',
                        isEmailValid
                      )}`}
                    />
                  </div>
                  {formData.email && !isEmailValid && !fieldErrors.email && (
                    <span className="text-[11px] text-red-400 mt-1 block font-medium">Enter a valid email address.</span>
                  )}
                  {fieldErrors.email && (
                    <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.email}</span>
                  )}
                </div>
              </div>

              {/* Password Creation */}
              <div>
                <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                  Create HOSPITAL Account Password (Min 8 Chars, Uppercase, Special) *
                </label>
                <div className="relative">
                  <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <input
                    type="password"
                    name="password"
                    required
                    value={formData.password}
                    onChange={handleChange}
                    placeholder="••••••••"
                    className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                      'password',
                      isPasswordValid
                    )}`}
                  />
                </div>
                {formData.password && !isPasswordValid && !fieldErrors.password && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">
                    Password must contain min 8 characters, at least 1 uppercase letter & 1 special character.
                  </span>
                )}
                {fieldErrors.password && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.password}</span>
                )}
              </div>

              {/* Physical Address */}
              <div>
                <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                  Physical Hospital Address & City *
                </label>
                <div className="relative">
                  <MapPin className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <input
                    type="text"
                    name="address"
                    required
                    maxLength={500}
                    value={formData.address}
                    onChange={handleChange}
                    placeholder={
                      isGovHospital
                        ? 'Regent Street, Colombo 08, Sri Lanka'
                        : '578 Elvitigala Mawatha, Colombo 05, Sri Lanka'
                    }
                    className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border rounded-xl text-white placeholder-slate-500 focus:outline-none transition-colors ${getFieldBorderClass(
                      'address',
                      isAddressValid
                    )}`}
                  />
                </div>
                {fieldErrors.address && (
                  <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.address}</span>
                )}
              </div>

              {/* City / District */}
              <div>
                <label className="block text-slate-300 font-semibold mb-1.5 uppercase tracking-wider text-[11px]">
                  City / Region / District
                </label>
                <div className="relative">
                  <MapPin className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                  <input
                    type="text"
                    name="city"
                    maxLength={100}
                    value={formData.city}
                    onChange={handleChange}
                    placeholder="e.g. Colombo, Kandy, Galle, or Central District"
                    className="w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition-colors"
                  />
                </div>
              </div>

              {/* Contact Person Details */}
              <div className="p-3.5 bg-slate-800/50 rounded-xl border border-slate-700/60 space-y-3">
                <h4 className="text-[11px] font-bold text-slate-300 uppercase tracking-wider flex items-center gap-1.5">
                  <User className="w-3.5 h-3.5 text-cyan-400" /> Authorized Contact Person Details
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div>
                    <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                      Contact Person Name
                    </label>
                    <input
                      type="text"
                      name="contactPersonName"
                      maxLength={100}
                      value={formData.contactPersonName}
                      onChange={handleChange}
                      placeholder="e.g. Dr. K. Silva / Administrator"
                      className="w-full px-3 py-2 bg-slate-800 border border-slate-700/80 rounded-lg text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 text-xs"
                    />
                  </div>
                  <div>
                    <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                      Contact Person Direct Phone
                    </label>
                    <input
                      type="tel"
                      name="contactPersonPhone"
                      maxLength={20}
                      value={formData.contactPersonPhone}
                      onChange={handleChange}
                      placeholder="e.g. 0771234567"
                      className="w-full px-3 py-2 bg-slate-800 border border-slate-700/80 rounded-lg text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 text-xs"
                    />
                  </div>
                </div>
              </div>

              {/* Uploaded Documents */}
              <div className="p-3.5 bg-slate-800/50 rounded-xl border border-slate-700/60 space-y-3">
                <h4 className="text-[11px] font-bold text-slate-300 uppercase tracking-wider flex items-center gap-1.5">
                  <FileText className="w-3.5 h-3.5 text-cyan-400" /> Accreditation & License Documents
                </h4>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div>
                    <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                      PHSRC / MOH License Document
                    </label>
                    <div className="relative">
                      <input
                        type="file"
                        accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                        onChange={(e) => handleFileUpload(e, 'licenseDocument')}
                        className="hidden"
                        id="license-file-input"
                      />
                      <label
                        htmlFor="license-file-input"
                        className="flex items-center gap-2 px-3 py-2 bg-slate-800 hover:bg-slate-700/80 border border-dashed border-slate-600 rounded-lg text-slate-300 text-xs cursor-pointer transition-colors"
                      >
                        <Upload className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                        <span className="truncate">
                          {formData.licenseDocumentName || 'Choose License File (PDF/Image)'}
                        </span>
                      </label>
                    </div>
                  </div>

                  <div>
                    <label className="block text-slate-400 text-[10px] uppercase font-semibold mb-1">
                      Accreditation Certificate / Proof
                    </label>
                    <div className="relative">
                      <input
                        type="file"
                        accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                        onChange={(e) => handleFileUpload(e, 'accreditationDocument')}
                        className="hidden"
                        id="accreditation-file-input"
                      />
                      <label
                        htmlFor="accreditation-file-input"
                        className="flex items-center gap-2 px-3 py-2 bg-slate-800 hover:bg-slate-700/80 border border-dashed border-slate-600 rounded-lg text-slate-300 text-xs cursor-pointer transition-colors"
                      >
                        <Upload className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                        <span className="truncate">
                          {formData.accreditationDocumentName || 'Choose Certificate (PDF/Image)'}
                        </span>
                      </label>
                    </div>
                  </div>
                </div>
              </div>

              <button
                type="submit"
                disabled={loading || !isFormValid}
                className="w-full py-3 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold rounded-xl shadow-lg shadow-cyan-600/25 transition-all flex items-center justify-center gap-2 mt-2 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {loading ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Submitting Hospital Registration...</span>
                  </>
                ) : (
                  <>
                    <span>Submit Hospital Registration</span>
                    <ArrowRight className="w-3.5 h-3.5" />
                  </>
                )}
              </button>
            </form>

            <div className="mt-6 pt-6 border-t border-slate-800 text-center">
              <p className="text-xs text-slate-400">
                Already registered healthcare facility?{' '}
                <Link to="/login?tab=hospital" className="text-cyan-400 font-semibold hover:underline">
                  Sign In to Hospital Portal
                </Link>
              </p>
            </div>
          </div>
        ) : (
          /* Submission Confirmation View */
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-8 shadow-2xl backdrop-blur-md text-center space-y-6 animate-in fade-in">
            <div className="w-16 h-16 rounded-2xl bg-amber-500/20 text-amber-400 border border-amber-500/30 flex items-center justify-center mx-auto shadow-lg shadow-amber-500/10">
              <Clock className="w-8 h-8 animate-pulse" />
            </div>

            <div>
              <span className="px-3 py-1 rounded-full text-xs font-bold bg-amber-500/20 text-amber-400 border border-amber-500/30 uppercase tracking-wider">
                Registration Status: Pending Approval
              </span>
              <h2 className="text-xl font-bold text-white mt-3">{submittedHospital.name}</h2>
              <p className="text-xs text-slate-400 mt-1">
                {isGovHospital ? 'HIN Code:' : 'PHSRC No:'}{' '}
                <span className="font-mono text-cyan-300 font-bold">{submittedHospital.licenseNumber}</span> • Email:{' '}
                <span className="text-slate-300">{submittedHospital.email}</span>
              </p>
            </div>

            <div className="p-4 bg-slate-800/60 rounded-xl border border-slate-700/60 text-xs text-slate-300 text-left leading-relaxed">
              <div className="flex items-start gap-2.5">
                <ShieldCheck className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                <div>
                  <h4 className="font-semibold text-slate-100">Verification Under Administrative Review</h4>
                  <p className="text-slate-400 text-[11px] mt-0.5">
                    Your hospital registration request has been received and is currently in <strong className="text-amber-300">Pending Verification</strong> status. A LifeLink System Administrator will verify your PHSRC / MOH registration number before authorizing medical staff logins.
                  </p>
                </div>
              </div>
            </div>

            <div className="pt-2 flex flex-col sm:flex-row gap-3">
              <Link
                to="/register"
                className="w-full py-2.5 bg-slate-800 hover:bg-slate-700 text-white font-semibold text-xs rounded-xl border border-slate-700 transition-colors flex items-center justify-center gap-1.5"
              >
                <span>Register Staff Account</span>
              </Link>
              <Link
                to="/login?tab=hospital"
                className="w-full py-2.5 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-cyan-600/20 transition-colors flex items-center justify-center gap-1.5"
              >
                <span>Return to Hospital Login</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default RegisterHospitalPage;


