import React, { useState, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import { doctorApi } from '../../api/doctorApi';
import { hospitalApi } from '../../api/hospitalApi';
import { getApiErrorMessage } from '../../utils/errorUtils';
import {
  Stethoscope,
  Plus,
  X,
  AlertCircle,
  CheckCircle2,
  Loader2,
  Eye,
  EyeOff,
  UserCheck,
  RefreshCw,
  Lock,
  Mail,
  User,
  IdCard,
  Trash2
} from 'lucide-react';

/**
 * DoctorManagementPage
 *
 * Accessible only by HospitalStaff. Allows the hospital to:
 *  - View their list of registered doctors
 *  - Create new doctor accounts with: Full Name, Email, Password, SLMC Registration Number
 *
 * Doctor accounts are automatically linked to the authenticated hospital
 * and will require the doctor to change their password on first login.
 */
export const DoctorManagementPage = () => {
  const { user } = useAuth();

  // Doctors list state
  const [doctors, setDoctors] = useState([]);
  const [listLoading, setListLoading] = useState(true);
  const [listError, setListError] = useState('');

  // Create Doctor form state
  const [showForm, setShowForm] = useState(false);
  const [formLoading, setFormLoading] = useState(false);
  const [formError, setFormError] = useState('');
  const [formSuccess, setFormSuccess] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const [form, setForm] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    licenseNumber: '',
    specialization: '',
    phoneNumber: ''
  });

  // Resolved hospital linked to current user
  const [hospitalId, setHospitalId] = useState(null);
  const [hospitalError, setHospitalError] = useState('');

  // Delete doctor confirmation
  const { addToast } = useNotification();
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    initPage();
  }, []);

  const initPage = async () => {
    await Promise.all([resolveHospital(), fetchDoctors()]);
  };

  /** Resolve the authenticated hospital by matching the user's email */
  const resolveHospital = async () => {
    setHospitalError('');
    try {
      const hospitals = await hospitalApi.getHospitals();
      const list = Array.isArray(hospitals) ? hospitals : (hospitals?.data || []);
      const normEmail = user?.email?.trim().toLowerCase();
      const match = list.find((h) => h.email?.toLowerCase() === normEmail);
      if (match) {
        setHospitalId(match.hospitalId);
      }
    } catch (err) {
      // hospitalId stays null — keep the real API error so submit can surface it
      setHospitalError(getApiErrorMessage(err));
    }
  };

  const fetchDoctors = async () => {
    setListLoading(true);
    setListError('');
    try {
      const res = await doctorApi.getDoctors();
      const data = res?.data || (Array.isArray(res) ? res : []);
      setDoctors(Array.isArray(data) ? data : []);
    } catch (err) {
      setListError(getApiErrorMessage(err));
    } finally {
      setListLoading(false);
    }
  };

  const handleDeleteDoctor = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await doctorApi.deleteDoctor(deleteTarget.doctorId);
      addToast({
        title: 'Doctor Deleted',
        message: `Dr. ${deleteTarget.firstName} ${deleteTarget.lastName} no longer has access. Request history was kept.`,
        type: 'success'
      });
      setDeleteTarget(null);
      await fetchDoctors();
    } catch (err) {
      addToast({ title: 'Delete Failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setDeleting(false);
    }
  };

  const handleFieldChange = (field, value) => {
    setForm((prev) => ({ ...prev, [field]: value }));
    if (formError) setFormError('');
    if (formSuccess) setFormSuccess('');
  };

  const resetForm = () => {
    setForm({ firstName: '', lastName: '', email: '', password: '', licenseNumber: '', specialization: '', phoneNumber: '' });
    setFormError('');
    setFormSuccess('');
    setShowPassword(false);
  };

  const handleCreateDoctor = async (e) => {
    e.preventDefault();
    setFormError('');
    setFormSuccess('');

    if (!hospitalId) {
      setFormError(hospitalError
        ? `Could not load your hospital: ${hospitalError}`
        : 'Could not resolve your hospital. Please refresh and try again.');
      return;
    }

    // Quick check against this hospital's doctors; the backend enforces uniqueness system-wide
    const slmc = form.licenseNumber.trim().toUpperCase();
    if (doctors.some((d) => (d.licenseNumber || '').trim().toUpperCase() === slmc)) {
      setFormError('A doctor with this SLMC number already exists.');
      return;
    }

    setFormLoading(true);
    try {
      await doctorApi.createDoctor({
        hospitalId,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim().toLowerCase(),
        password: form.password,
        licenseNumber: form.licenseNumber.trim(),
        specialization: form.specialization.trim(),
        phoneNumber: form.phoneNumber.trim()
      });

      setFormSuccess(`Doctor account created for ${form.firstName} ${form.lastName}. They will be required to change their password on first login.`);
      resetForm();
      // Refresh doctor list
      await fetchDoctors();
    } catch (err) {
      setFormError(getApiErrorMessage(err));
    } finally {
      setFormLoading(false);
    }
  };

  // Password strength for form
  const pwd = form.password;
  const pwdChecks = {
    len: pwd.length >= 8,
    upper: /[A-Z]/.test(pwd),
    lower: /[a-z]/.test(pwd),
    num: /\d/.test(pwd),
    special: /[^\da-zA-Z]/.test(pwd)
  };
  const pwdValid = Object.values(pwdChecks).every(Boolean);

  // Phone must be exactly 10 digits (mirrors CreateDoctorDto validation)
  const phoneTouched = form.phoneNumber.length > 0;
  const phoneValid = /^\d{10}$/.test(form.phoneNumber.trim());

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Doctor Management</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Manage doctor accounts linked to your hospital. Doctors are required to change their password on first login.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={initPage}
            className="inline-flex items-center gap-2 px-3.5 py-2 bg-slate-900 dark:bg-slate-800 hover:bg-slate-800 text-white font-semibold text-xs rounded-xl shadow-md transition-all"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            <span>Refresh</span>
          </button>
          <button
            id="add-doctor-btn"
            onClick={() => { setShowForm(true); setFormError(''); setFormSuccess(''); }}
            className="inline-flex items-center gap-2 px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white font-semibold text-xs rounded-xl shadow-md shadow-emerald-600/20 transition-all"
          >
            <Plus className="w-4 h-4" />
            <span>Add Doctor</span>
          </button>
        </div>
      </div>

      {/* Create Doctor Modal / Inline Form */}
      {showForm && (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center justify-between mb-5">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 rounded-lg bg-emerald-600/10 border border-emerald-600/20 flex items-center justify-center">
                <Stethoscope className="w-4 h-4 text-emerald-500" />
              </div>
              <div>
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Create Doctor Account</h3>
                <p className="text-[11px] text-slate-500">New account will be linked to your hospital</p>
              </div>
            </div>
            <button
              type="button"
              onClick={() => { setShowForm(false); resetForm(); }}
              className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          {formError && (
            <div className="mb-4 p-3.5 bg-red-50 dark:bg-red-950/60 border border-red-200 dark:border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-700 dark:text-red-300">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <span className="font-medium">{formError}</span>
            </div>
          )}

          {formSuccess && (
            <div className="mb-4 p-3.5 bg-emerald-50 dark:bg-emerald-950/60 border border-emerald-200 dark:border-emerald-900 rounded-xl flex items-start gap-2.5 text-xs text-emerald-700 dark:text-emerald-300">
              <CheckCircle2 className="w-4 h-4 shrink-0 mt-0.5" />
              <span className="font-medium">{formSuccess}</span>
            </div>
          )}

          <form onSubmit={handleCreateDoctor} className="space-y-4">
            {/* Name row */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                  First Name *
                </label>
                <div className="relative">
                  <User className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                  <input
                    id="doctor-first-name"
                    type="text"
                    required
                    maxLength={100}
                    value={form.firstName}
                    onChange={(e) => handleFieldChange('firstName', e.target.value)}
                    placeholder="e.g. Amara"
                    className="w-full pl-9 pr-3 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                  />
                </div>
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                  Last Name *
                </label>
                <div className="relative">
                  <User className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                  <input
                    id="doctor-last-name"
                    type="text"
                    required
                    maxLength={100}
                    value={form.lastName}
                    onChange={(e) => handleFieldChange('lastName', e.target.value)}
                    placeholder="e.g. Perera"
                    className="w-full pl-9 pr-3 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                  />
                </div>
              </div>
            </div>

            {/* Email */}
            <div>
              <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                Email Address *
              </label>
              <div className="relative">
                <Mail className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                <input
                  id="doctor-email"
                  type="email"
                  required
                  maxLength={200}
                  value={form.email}
                  onChange={(e) => handleFieldChange('email', e.target.value)}
                  placeholder="doctor@hospital.org"
                  className="w-full pl-9 pr-3 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                />
              </div>
              <p className="text-[11px] text-slate-400 mt-1">Must be unique across the entire platform.</p>
            </div>

            {/* Password */}
            <div>
              <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                Initial Password *
              </label>
              <div className="relative">
                <Lock className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                <input
                  id="doctor-password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  minLength={8}
                  value={form.password}
                  onChange={(e) => handleFieldChange('password', e.target.value)}
                  placeholder="Min 8 chars, upper, lower, number, special"
                  className="w-full pl-9 pr-10 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-2.5 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
                >
                  {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                </button>
              </div>
              {pwd.length > 0 && (
                <div className="mt-2 grid grid-cols-2 gap-1">
                  {[
                    { passed: pwdChecks.len, label: '8+ characters' },
                    { passed: pwdChecks.upper, label: 'Uppercase' },
                    { passed: pwdChecks.lower, label: 'Lowercase' },
                    { passed: pwdChecks.num, label: 'Number' },
                    { passed: pwdChecks.special, label: 'Special char' }
                  ].map(({ passed, label }) => (
                    <div key={label} className={`flex items-center gap-1 text-[10px] font-medium ${passed ? 'text-emerald-500' : 'text-slate-400'}`}>
                      <CheckCircle2 className={`w-3 h-3 shrink-0 ${passed ? 'text-emerald-500' : 'text-slate-500'}`} />
                      {label}
                    </div>
                  ))}
                </div>
              )}
              <p className="text-[11px] text-slate-400 mt-1">The doctor must change this password on their first login.</p>
            </div>

            {/* SLMC Registration Number */}
            <div>
              <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                SLMC Registration Number
              </label>
              <div className="relative">
                <IdCard className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                <input
                  id="doctor-slmc"
                  type="text"
                  required
                  maxLength={100}
                  value={form.licenseNumber}
                  onChange={(e) => handleFieldChange('licenseNumber', e.target.value)}
                  placeholder="e.g. SLMC/2024/12345"
                  className="w-full pl-9 pr-3 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                />
              </div>
              <p className="text-[11px] text-slate-400 mt-1">Sri Lanka Medical Council registration number.</p>
            </div>

            {/* Specialization (optional) & Phone (required, 10 digits) */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                  Specialization
                </label>
                <input
                  id="doctor-specialization"
                  type="text"
                  maxLength={100}
                  value={form.specialization}
                  onChange={(e) => handleFieldChange('specialization', e.target.value)}
                  placeholder="e.g. Haematology"
                  className="w-full px-3 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none focus:border-emerald-500 transition-colors"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 dark:text-slate-300 uppercase tracking-wider mb-1.5">
                  Phone Number *
                </label>
                <input
                  id="doctor-phone"
                  type="tel"
                  inputMode="numeric"
                  required
                  value={form.phoneNumber}
                  onChange={(e) => handleFieldChange('phoneNumber', e.target.value)}
                  placeholder="e.g. 0712345678"
                  aria-invalid={phoneTouched && !phoneValid}
                  className={`w-full px-3 py-2.5 bg-slate-50 dark:bg-slate-800 border rounded-xl text-xs text-slate-900 dark:text-white placeholder-slate-400 focus:outline-none transition-colors ${
                    !phoneTouched
                      ? 'border-slate-200 dark:border-slate-700 focus:border-emerald-500'
                      : phoneValid
                        ? 'border-emerald-500'
                        : 'border-red-500'
                  }`}
                />
                {phoneTouched && (
                  <p className={`flex items-center gap-1 text-[11px] font-medium mt-1 ${phoneValid ? 'text-emerald-500' : 'text-red-500'}`}>
                    {phoneValid ? <CheckCircle2 className="w-3 h-3 shrink-0" /> : <AlertCircle className="w-3 h-3 shrink-0" />}
                    {phoneValid ? 'Valid phone number' : 'Phone number must be exactly 10 digits'}
                  </p>
                )}
              </div>
            </div>

            {/* Submit */}
            <div className="flex gap-3 pt-2">
              <button
                type="button"
                onClick={() => { setShowForm(false); resetForm(); }}
                className="flex-1 py-2.5 text-xs font-semibold text-slate-600 dark:text-slate-300 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-xl transition-colors"
              >
                Cancel
              </button>
              <button
                id="create-doctor-submit"
                type="submit"
                disabled={formLoading || !form.firstName || !form.lastName || !form.email || !pwdValid || !phoneValid}
                className="flex-1 py-2.5 text-xs font-semibold text-white bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 rounded-xl shadow-md shadow-emerald-600/20 transition-all flex items-center justify-center gap-2"
              >
                {formLoading ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    <span>Creating Account...</span>
                  </>
                ) : (
                  <>
                    <Plus className="w-3.5 h-3.5" />
                    <span>Create Doctor Account</span>
                  </>
                )}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* Doctors List */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm overflow-hidden">
        <div className="p-5 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Registered Doctors</h3>
          <span className="text-xs text-slate-400 font-medium">{doctors.length} doctor{doctors.length !== 1 ? 's' : ''}</span>
        </div>

        {listError && (
          <div className="m-4 p-3.5 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-700 dark:text-red-300">
            <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
            <span>{listError}</span>
          </div>
        )}

        {listLoading ? (
          <div className="p-12 flex flex-col items-center gap-3 text-slate-400">
            <Loader2 className="w-6 h-6 animate-spin text-emerald-500" />
            <p className="text-xs font-medium">Loading doctors...</p>
          </div>
        ) : doctors.length === 0 ? (
          <div className="p-12 flex flex-col items-center gap-3 text-center">
            <div className="w-12 h-12 rounded-2xl bg-slate-100 dark:bg-slate-800 flex items-center justify-center">
              <Stethoscope className="w-6 h-6 text-slate-400" />
            </div>
            <div>
              <p className="text-sm font-semibold text-slate-600 dark:text-slate-300">No doctors yet</p>
              <p className="text-xs text-slate-400 mt-1">Create a doctor account to get started.</p>
            </div>
            <button
              onClick={() => setShowForm(true)}
              className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-semibold rounded-xl transition-colors flex items-center gap-1.5"
            >
              <Plus className="w-3.5 h-3.5" />
              Add First Doctor
            </button>
          </div>
        ) : (
          <div className="divide-y divide-slate-100 dark:divide-slate-800">
            {doctors.map((doctor) => (
              <div key={doctor.doctorId} className="p-4 flex items-center gap-4 hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors">
                {/* Avatar */}
                <div className="w-9 h-9 rounded-full bg-emerald-600/10 border border-emerald-600/20 flex items-center justify-center shrink-0">
                  <Stethoscope className="w-4 h-4 text-emerald-500" />
                </div>

                {/* Info */}
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <p className="text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">
                      Dr. {doctor.firstName} {doctor.lastName}
                    </p>
                    {doctor.mustChangePassword && (
                      <span className="px-1.5 py-0.5 bg-amber-100 dark:bg-amber-950/50 text-amber-700 dark:text-amber-400 text-[10px] font-bold rounded-full border border-amber-200 dark:border-amber-800/50">
                        Awaiting First Login
                      </span>
                    )}
                    {!doctor.mustChangePassword && (
                      <span className="px-1.5 py-0.5 bg-emerald-100 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold rounded-full border border-emerald-200 dark:border-emerald-800/50">
                        Active
                      </span>
                    )}
                  </div>
                  <p className="text-[11px] text-slate-400 truncate">{doctor.email}</p>
                  {doctor.licenseNumber && (
                    <p className="text-[10px] text-slate-500 mt-0.5">SLMC: {doctor.licenseNumber}</p>
                  )}
                </div>

                {/* Specialization */}
                {doctor.specialization && (
                  <span className="text-[11px] text-slate-500 dark:text-slate-400 hidden sm:block shrink-0">
                    {doctor.specialization}
                  </span>
                )}

                {/* Status icon */}
                <div className="shrink-0">
                  {doctor.mustChangePassword ? (
                    <Lock className="w-4 h-4 text-amber-400" />
                  ) : (
                    <UserCheck className="w-4 h-4 text-emerald-500" />
                  )}
                </div>

                {/* Delete */}
                <button
                  onClick={() => setDeleteTarget(doctor)}
                  title="Delete doctor"
                  className="shrink-0 inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-semibold bg-rose-50 text-rose-700 hover:bg-rose-100 dark:bg-rose-950/60 dark:text-rose-300 dark:hover:bg-rose-900 border border-rose-200 dark:border-rose-900 transition-colors"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  <span className="hidden sm:inline">Delete</span>
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Delete Doctor Confirmation Modal */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-5 h-5 text-rose-600" />
                <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Delete Doctor</h3>
              </div>
              <button
                onClick={() => !deleting && setDeleteTarget(null)}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Delete <span className="font-semibold text-slate-900 dark:text-slate-100">Dr. {deleteTarget.firstName} {deleteTarget.lastName}</span> ({deleteTarget.email})?
              Their login access and account will be removed permanently. Blood request history they handled is kept, and any
              requests still awaiting their decision return to Pending for reassignment.
            </p>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-800">
              <button
                type="button"
                onClick={() => setDeleteTarget(null)}
                disabled={deleting}
                className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleDeleteDoctor}
                disabled={deleting}
                className="px-4 py-2 rounded-xl text-xs font-semibold bg-rose-600 hover:bg-rose-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
              >
                {deleting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                {deleting ? 'Deleting...' : 'Delete Doctor'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default DoctorManagementPage;
