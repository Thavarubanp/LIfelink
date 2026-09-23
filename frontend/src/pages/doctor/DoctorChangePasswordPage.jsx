import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../utils/errorUtils';
import {
  Lock,
  KeyRound,
  Eye,
  EyeOff,
  CheckCircle2,
  AlertCircle,
  Loader2,
  Stethoscope,
  ShieldCheck
} from 'lucide-react';

/**
 * DoctorChangePasswordPage
 *
 * Shown exclusively to Doctor accounts with MustChangePassword = true.
 * Forces the doctor to change their hospital-assigned password before
 * accessing any dashboard page.
 *
 * On success, AuthService.ChangePasswordAsync automatically sets
 * MustChangePassword = false on the Doctor entity. The frontend then
 * refreshes the auth context so subsequent navigation proceeds normally.
 */
export const DoctorChangePasswordPage = () => {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showCurrent, setShowCurrent] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Password strength checks
  const hasMinLen = newPassword.length >= 8;
  const hasUpper = /[A-Z]/.test(newPassword);
  const hasLower = /[a-z]/.test(newPassword);
  const hasNumber = /\d/.test(newPassword);
  const hasSpecial = /[^\da-zA-Z]/.test(newPassword);
  const isMatch = newPassword.length > 0 && newPassword === confirmPassword;
  const isPasswordValid = hasMinLen && hasUpper && hasLower && hasNumber && hasSpecial && isMatch;

  const StrengthCheck = ({ passed, label }) => (
    <div className={`flex items-center gap-1.5 text-[11px] font-medium ${passed ? 'text-emerald-400' : 'text-slate-500'}`}>
      <CheckCircle2 className={`w-3 h-3 shrink-0 ${passed ? 'text-emerald-400' : 'text-slate-600'}`} />
      {label}
    </div>
  );

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!isPasswordValid) return;

    setError('');
    setLoading(true);

    try {
      // Reuse existing change-password endpoint. Backend will automatically
      // clear Doctor.MustChangePassword = false on success.
      await authApi.changePassword({
        currentPassword,
        newPassword
      });

      // Update auth context so ProtectedRoute no longer redirects to this page
      if (user) {
        setUser({ ...user, mustChangePassword: false });
      }

      navigate('/doctor/dashboard', { replace: true });
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-10 relative overflow-hidden">
      {/* Background emerald glow */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-emerald-600/8 rounded-full blur-3xl pointer-events-none" />

      <div className="max-w-md w-full bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl relative z-10">
        {/* Brand Header */}
        <div className="flex flex-col items-center text-center mb-6">
          <div className="w-12 h-12 rounded-2xl bg-gradient-to-tr from-emerald-600 to-emerald-500 flex items-center justify-center shadow-lg shadow-emerald-600/30 mb-3">
            <Stethoscope className="w-6 h-6 text-white" />
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight">
            Life<span className="text-red-500">Link</span>
          </h1>
          <p className="text-xs text-slate-400 mt-1">Emergency Blood Management & Healthcare Network</p>
        </div>

        {/* Header */}
        <div className="mb-6 text-center">
          <span className="inline-block px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-emerald-950/60 text-emerald-300 border border-emerald-800/60 tracking-wider uppercase mb-2">
            First Login — Action Required
          </span>
          <h2 className="text-lg font-bold text-white">Set Your New Password</h2>
          <p className="text-xs text-slate-400 mt-1 leading-relaxed">
            Your account was provisioned by your hospital administrator. You must set a personal password before accessing the Doctor Portal.
          </p>
        </div>

        {/* Doctor identity context */}
        {user && (
          <div className="mb-5 p-3.5 bg-emerald-950/30 border border-emerald-800/40 rounded-xl flex items-center gap-3">
            <div className="w-8 h-8 rounded-full bg-emerald-600/20 border border-emerald-600/30 flex items-center justify-center shrink-0">
              <Stethoscope className="w-4 h-4 text-emerald-400" />
            </div>
            <div className="min-w-0">
              <p className="text-xs font-semibold text-white truncate">Dr. {user.firstName} {user.lastName}</p>
              <p className="text-[11px] text-slate-400 truncate">{user.email}</p>
            </div>
            <ShieldCheck className="w-4 h-4 text-emerald-500 shrink-0 ml-auto" />
          </div>
        )}

        {error && (
          <div className="mb-5 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* Current (hospital-assigned) password */}
          <div>
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
              Current Password
            </label>
            <p className="text-[11px] text-slate-500 mb-1.5">Enter the password provided by your hospital administrator.</p>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
              <input
                id="doctor-current-password"
                type={showCurrent ? 'text' : 'password'}
                required
                value={currentPassword}
                onChange={(e) => { setCurrentPassword(e.target.value); if (error) setError(''); }}
                placeholder="Hospital-assigned password"
                className="w-full pl-10 pr-10 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition-colors"
              />
              <button
                type="button"
                onClick={() => setShowCurrent(!showCurrent)}
                className="absolute right-3 top-2.5 text-slate-500 hover:text-slate-300"
              >
                {showCurrent ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          {/* New password */}
          <div>
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
              New Password
            </label>
            <div className="relative">
              <KeyRound className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
              <input
                id="doctor-new-password"
                type={showNew ? 'text' : 'password'}
                required
                value={newPassword}
                onChange={(e) => { setNewPassword(e.target.value); if (error) setError(''); }}
                placeholder="Create a strong password"
                className="w-full pl-10 pr-10 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition-colors"
              />
              <button
                type="button"
                onClick={() => setShowNew(!showNew)}
                className="absolute right-3 top-2.5 text-slate-500 hover:text-slate-300"
              >
                {showNew ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          {/* Confirm password */}
          <div>
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
              Confirm New Password
            </label>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
              <input
                id="doctor-confirm-password"
                type={showConfirm ? 'text' : 'password'}
                required
                value={confirmPassword}
                onChange={(e) => { setConfirmPassword(e.target.value); if (error) setError(''); }}
                placeholder="Re-enter new password"
                className="w-full pl-10 pr-10 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition-colors"
              />
              <button
                type="button"
                onClick={() => setShowConfirm(!showConfirm)}
                className="absolute right-3 top-2.5 text-slate-500 hover:text-slate-300"
              >
                {showConfirm ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          {/* Password strength indicators */}
          {newPassword.length > 0 && (
            <div className="p-3.5 bg-slate-800/60 border border-slate-700/60 rounded-xl grid grid-cols-2 gap-1.5">
              <StrengthCheck passed={hasMinLen} label="At least 8 characters" />
              <StrengthCheck passed={hasUpper} label="Uppercase letter" />
              <StrengthCheck passed={hasLower} label="Lowercase letter" />
              <StrengthCheck passed={hasNumber} label="Number (0–9)" />
              <StrengthCheck passed={hasSpecial} label="Special character" />
              <StrengthCheck passed={isMatch} label="Passwords match" />
            </div>
          )}

          <button
            id="doctor-change-password-submit"
            type="submit"
            disabled={loading || !isPasswordValid || !currentPassword}
            className="w-full py-3 bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white font-semibold rounded-xl text-xs shadow-lg shadow-emerald-600/20 transition-all flex items-center justify-center gap-2 mt-2"
          >
            {loading ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Updating Password...</span>
              </>
            ) : (
              <>
                <ShieldCheck className="w-4 h-4" />
                <span>Set Password & Access Doctor Portal</span>
              </>
            )}
          </button>
        </form>
      </div>
    </div>
  );
};

export default DoctorChangePasswordPage;
