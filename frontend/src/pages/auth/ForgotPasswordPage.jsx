import React, { useState, useEffect, useRef } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../utils/errorUtils';
import {
  Mail,
  Lock,
  KeyRound,
  ShieldCheck,
  Heart,
  Building2,
  Stethoscope,
  AlertCircle,
  CheckCircle2,
  Loader2,
  ArrowRight,
  ArrowLeft,
  RefreshCw,
  Clock,
  Eye,
  EyeOff,
  Sparkles
} from 'lucide-react';

export const ForgotPasswordPage = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const searchParams = new URLSearchParams(location.search);
  const initialRoleParam = searchParams.get('role') || 'donor';

  const [activeTab, setActiveTab] = useState(initialRoleParam);
  const [step, setStep] = useState(1); // Step 1: Request OTP, Step 2: Verify OTP, Step 3: Reset Password, Step 4: Success

  // Form State
  const [email, setEmail] = useState('');
  const [otp, setOtp] = useState(['', '', '', '', '', '']);
  const [resetSessionToken, setResetSessionToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  // UI State
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [infoMessage, setInfoMessage] = useState('');

  // Timers
  const [expirySeconds, setExpirySeconds] = useState(600); // 10 minutes (600 seconds)
  const [cooldownSeconds, setCooldownSeconds] = useState(0); // 30 seconds resend cooldown

  const otpInputRefs = useRef([]);

  // Handle countdown timers
  useEffect(() => {
    let timer;
    if (step === 2 && expirySeconds > 0) {
      timer = setInterval(() => {
        setExpirySeconds((prev) => (prev > 0 ? prev - 1 : 0));
      }, 1000);
    }
    return () => clearInterval(timer);
  }, [step, expirySeconds]);

  useEffect(() => {
    let cooldownTimer;
    if (cooldownSeconds > 0) {
      cooldownTimer = setInterval(() => {
        setCooldownSeconds((prev) => (prev > 0 ? prev - 1 : 0));
      }, 1000);
    }
    return () => clearInterval(cooldownTimer);
  }, [cooldownSeconds]);

  // Format seconds to mm:ss
  const formatTime = (secs) => {
    const mins = Math.floor(secs / 60);
    const remainder = secs % 60;
    return `${mins.toString().padStart(2, '0')}:${remainder.toString().padStart(2, '0')}`;
  };

  // Role Theme Configs
  const roleConfig = {
    donor: {
      title: 'Donor Account Password Recovery',
      badge: 'Donor / Patient Services',
      glowColor: 'bg-red-600/10',
      activeTabStyle: 'bg-red-600/20 text-red-400 border-red-500/30',
      buttonStyle: 'bg-red-600 hover:bg-red-700 shadow-red-600/25 focus:ring-red-500',
      inputFocusStyle: 'focus:border-red-500',
      placeholder: 'donor@lifelink.org'
    },
    hospital: {
      title: 'Hospital Staff Password Recovery',
      badge: 'Authorized Medical Center',
      glowColor: 'bg-cyan-600/10',
      activeTabStyle: 'bg-cyan-600/20 text-cyan-400 border-cyan-500/30',
      buttonStyle: 'bg-cyan-600 hover:bg-cyan-700 shadow-cyan-600/25 focus:ring-cyan-500',
      inputFocusStyle: 'focus:border-cyan-500',
      placeholder: 'staff@cityhospital.org'
    },
    doctor: {
      title: 'Doctor Account Password Recovery',
      badge: 'Medical Specialist Access',
      glowColor: 'bg-emerald-600/10',
      activeTabStyle: 'bg-emerald-600/20 text-emerald-400 border-emerald-500/30',
      buttonStyle: 'bg-emerald-600 hover:bg-emerald-700 shadow-emerald-600/25 focus:ring-emerald-500',
      inputFocusStyle: 'focus:border-emerald-500',
      placeholder: 'doctor@hospital.org'
    },
    admin: {
      title: 'Admin Console Password Recovery',
      badge: 'System Administrator Access',
      glowColor: 'bg-purple-600/10',
      activeTabStyle: 'bg-purple-600/20 text-purple-400 border-purple-500/30',
      buttonStyle: 'bg-purple-600 hover:bg-purple-700 shadow-purple-600/25 focus:ring-purple-500',
      inputFocusStyle: 'focus:border-purple-500',
      placeholder: 'admin@lifelink.gov'
    }
  };

  const currentRole = roleConfig[activeTab] || roleConfig.donor;

  // Password validation checks
  const hasMinLen = newPassword.length >= 8;
  const hasUpper = /[A-Z]/.test(newPassword);
  const hasLower = /[a-z]/.test(newPassword);
  const hasNumber = /\d/.test(newPassword);
  const hasSpecial = /[^a-zA-Z0-9]/.test(newPassword);
  const isMatch = newPassword.length > 0 && newPassword === confirmPassword;
  const isPasswordValid = hasMinLen && hasUpper && hasLower && hasNumber && hasSpecial && isMatch;

  // --- Step 1: Request OTP ---
  const handleRequestOtp = async (e) => {
    e.preventDefault();
    setError('');
    setInfoMessage('');
    setLoading(true);

    try {
      await authApi.forgotPassword({ email: email.trim() });
      setInfoMessage('If an account exists for this email, a 6-digit verification code has been dispatched.');
      setExpirySeconds(600); // 10 minutes
      setCooldownSeconds(30); // 30s resend cooldown
      setStep(2);
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  // --- OTP Digit Input Handling ---
  const handleOtpChange = (index, value) => {
    if (!/^\d*$/.test(value)) return;

    const newOtp = [...otp];
    newOtp[index] = value.slice(-1);
    setOtp(newOtp);

    if (error) setError('');

    // Auto-advance to next input
    if (value && index < 5 && otpInputRefs.current[index + 1]) {
      otpInputRefs.current[index + 1].focus();
    }
  };

  const handleOtpKeyDown = (index, e) => {
    if (e.key === 'Backspace' && !otp[index] && index > 0) {
      otpInputRefs.current[index - 1].focus();
    }
  };

  const handleOtpPaste = (e) => {
    e.preventDefault();
    const pastedData = e.clipboardData.getData('text').trim().replace(/\D/g, '').slice(0, 6);
    if (pastedData) {
      const newOtp = pastedData.split('').concat(Array(6 - pastedData.length).fill(''));
      setOtp(newOtp);
      const nextIndex = Math.min(pastedData.length, 5);
      if (otpInputRefs.current[nextIndex]) {
        otpInputRefs.current[nextIndex].focus();
      }
    }
  };

  // --- Step 2: Verify OTP ---
  const handleVerifyOtp = async (e) => {
    e.preventDefault();
    const fullOtp = otp.join('');
    if (fullOtp.length < 6) {
      setError('Please enter the full 6-digit verification code.');
      return;
    }

    if (expirySeconds <= 0) {
      setError('Verification code has expired. Please request a new OTP.');
      return;
    }

    setError('');
    setLoading(true);

    try {
      const res = await authApi.verifyOtp({ email: email.trim(), otp: fullOtp });
      const token = res?.data?.resetSessionToken || res?.resetSessionToken;
      if (!token) {
        throw new Error('Verification failed. No reset token issued.');
      }
      setResetSessionToken(token);
      setStep(3);
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  // --- Resend OTP ---
  const handleResendOtp = async () => {
    if (cooldownSeconds > 0 || loading) return;

    setError('');
    setInfoMessage('');
    setLoading(true);

    try {
      await authApi.resendOtp({ email: email.trim() });
      setOtp(['', '', '', '', '', '']);
      setExpirySeconds(600);
      setCooldownSeconds(30);
      setInfoMessage('A new 6-digit verification code has been dispatched to your email.');
      if (otpInputRefs.current[0]) {
        otpInputRefs.current[0].focus();
      }
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  // --- Step 3: Reset Password ---
  const handleResetPassword = async (e) => {
    e.preventDefault();
    if (!isPasswordValid) return;

    setError('');
    setLoading(true);

    try {
      await authApi.resetPassword({
        email: email.trim(),
        token: resetSessionToken,
        newPassword
      });
      setStep(4);
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-10 relative overflow-hidden font-sans">
      {/* Dynamic Background Glow */}
      <div className={`absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[650px] h-[650px] ${currentRole.glowColor} rounded-full blur-3xl pointer-events-none transition-all duration-700`} />

      <div className="max-w-md w-full bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl relative z-10 backdrop-blur-md">
        {/* Brand Header */}
        <div className="flex flex-col items-center text-center mb-6">
          <div className="w-12 h-12 rounded-2xl bg-gradient-to-tr from-red-600 to-red-500 flex items-center justify-center text-white text-2xl font-bold shadow-lg shadow-red-600/30 mb-3">
            💉
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight">
            Life<span className="text-red-500">Link</span>
          </h1>
          <p className="text-xs text-slate-400 mt-1">
            Emergency Blood Management & Healthcare Network
          </p>
        </div>

        {/* Role Selection Tabs (Only on Step 1) */}
        {step === 1 && (
          <div className="grid grid-cols-4 gap-1 p-1 bg-slate-950/80 border border-slate-800 rounded-xl mb-6">
            <button
              type="button"
              onClick={() => { setActiveTab('donor'); setError(''); }}
              className={`flex items-center justify-center gap-1 py-2 px-1 rounded-lg text-[11px] font-semibold transition-all border ${
                activeTab === 'donor'
                  ? roleConfig.donor.activeTabStyle
                  : 'text-slate-400 hover:text-slate-200 border-transparent'
              }`}
            >
              <Heart className="w-3 h-3 shrink-0" />
              <span>Donor</span>
            </button>

            <button
              type="button"
              onClick={() => { setActiveTab('hospital'); setError(''); }}
              className={`flex items-center justify-center gap-1 py-2 px-1 rounded-lg text-[11px] font-semibold transition-all border ${
                activeTab === 'hospital'
                  ? roleConfig.hospital.activeTabStyle
                  : 'text-slate-400 hover:text-slate-200 border-transparent'
              }`}
            >
              <Building2 className="w-3 h-3 shrink-0" />
              <span>Hospital</span>
            </button>

            <button
              type="button"
              onClick={() => { setActiveTab('doctor'); setError(''); }}
              className={`flex items-center justify-center gap-1 py-2 px-1 rounded-lg text-[11px] font-semibold transition-all border ${
                activeTab === 'doctor'
                  ? roleConfig.doctor.activeTabStyle
                  : 'text-slate-400 hover:text-slate-200 border-transparent'
              }`}
            >
              <Stethoscope className="w-3 h-3 shrink-0" />
              <span>Doctor</span>
            </button>

            <button
              type="button"
              onClick={() => { setActiveTab('admin'); setError(''); }}
              className={`flex items-center justify-center gap-1 py-2 px-1 rounded-lg text-[11px] font-semibold transition-all border ${
                activeTab === 'admin'
                  ? roleConfig.admin.activeTabStyle
                  : 'text-slate-400 hover:text-slate-200 border-transparent'
              }`}
            >
              <ShieldCheck className="w-3 h-3 shrink-0" />
              <span>Admin</span>
            </button>
          </div>
        )}

        {/* Progress Step Indicator */}
        <div className="flex items-center justify-between mb-6 px-2">
          <div className="flex items-center gap-2">
            <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${step >= 1 ? 'bg-red-500 text-white' : 'bg-slate-800 text-slate-500'}`}>1</span>
            <span className={`h-0.5 w-6 ${step >= 2 ? 'bg-red-500' : 'bg-slate-800'}`} />
            <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${step >= 2 ? 'bg-red-500 text-white' : 'bg-slate-800 text-slate-500'}`}>2</span>
            <span className={`h-0.5 w-6 ${step >= 3 ? 'bg-red-500' : 'bg-slate-800'}`} />
            <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold ${step >= 3 ? 'bg-red-500 text-white' : 'bg-slate-800 text-slate-500'}`}>3</span>
          </div>
          <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider">
            {step === 1 && 'Request OTP'}
            {step === 2 && 'Verify OTP'}
            {step === 3 && 'New Password'}
            {step === 4 && 'Complete'}
          </span>
        </div>

        {/* Header Title */}
        <div className="mb-5 text-center">
          <span className="inline-block px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-800 text-slate-300 border border-slate-700 tracking-wider uppercase mb-1.5">
            {currentRole.badge}
          </span>
          <h2 className="text-lg font-bold text-white">
            {step === 1 && 'Forgot Password?'}
            {step === 2 && 'Enter Verification OTP'}
            {step === 3 && 'Set New Password'}
            {step === 4 && 'Password Reset Complete'}
          </h2>
          <p className="text-xs text-slate-400 mt-0.5">
            {step === 1 && 'Enter your registered email address to receive a secure 6-digit OTP code.'}
            {step === 2 && `We sent a 6-digit OTP verification code to ${email}.`}
            {step === 3 && 'Choose a strong, secure password for your account.'}
            {step === 4 && 'Your password has been successfully updated. You can now sign in.'}
          </p>
        </div>

        {/* Global Error Banner */}
        {error && (
          <div className="mb-5 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{error}</span>
          </div>
        )}

        {/* Global Info Banner */}
        {infoMessage && (
          <div className="mb-5 p-3.5 bg-cyan-950/80 border border-cyan-900 rounded-xl flex items-start gap-2.5 text-xs text-cyan-200 shadow-md animate-in fade-in">
            <CheckCircle2 className="w-4 h-4 text-cyan-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{infoMessage}</span>
          </div>
        )}

        {/* ================= STEP 1: REQUEST OTP ================= */}
        {step === 1 && (
          <form onSubmit={handleRequestOtp} className="space-y-4">
            <div>
              <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
                Account Email
              </label>
              <div className="relative">
                <Mail className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                <input
                  type="email"
                  required
                  value={email}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    if (error) setError('');
                  }}
                  placeholder={currentRole.placeholder}
                  className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none transition-colors ${currentRole.inputFocusStyle}`}
                />
              </div>
            </div>

            <button
              type="submit"
              disabled={loading || !email.trim()}
              className={`w-full py-3 text-white font-semibold rounded-xl text-xs shadow-lg transition-all flex items-center justify-center gap-2 mt-2 disabled:opacity-50 ${currentRole.buttonStyle}`}
            >
              {loading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Sending Verification Code...</span>
                </>
              ) : (
                <>
                  <span>Send Verification Code</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        )}

        {/* ================= STEP 2: VERIFY OTP ================= */}
        {step === 2 && (
          <form onSubmit={handleVerifyOtp} className="space-y-5">
            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
                  6-Digit Verification Code
                </label>
                <div className="flex items-center gap-1 text-[11px] text-amber-400 font-mono font-medium">
                  <Clock className="w-3 h-3" />
                  <span>{formatTime(expirySeconds)}</span>
                </div>
              </div>

              {/* 6-Digit OTP Inputs */}
              <div className="flex justify-between gap-2" onPaste={handleOtpPaste}>
                {otp.map((digit, idx) => (
                  <input
                    key={idx}
                    ref={(el) => (otpInputRefs.current[idx] = el)}
                    type="text"
                    inputMode="numeric"
                    maxLength={1}
                    value={digit}
                    onChange={(e) => handleOtpChange(idx, e.target.value)}
                    onKeyDown={(e) => handleOtpKeyDown(idx, e)}
                    className="w-11 h-12 text-center text-lg font-bold text-white bg-slate-800/90 border border-slate-700 rounded-xl focus:border-red-500 focus:outline-none focus:ring-1 focus:ring-red-500 transition-colors font-mono"
                  />
                ))}
              </div>
            </div>

            <button
              type="submit"
              disabled={loading || otp.join('').length < 6 || expirySeconds <= 0}
              className={`w-full py-3 text-white font-semibold rounded-xl text-xs shadow-lg transition-all flex items-center justify-center gap-2 disabled:opacity-50 ${currentRole.buttonStyle}`}
            >
              {loading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Verifying Code...</span>
                </>
              ) : (
                <>
                  <span>Verify Code</span>
                  <KeyRound className="w-3.5 h-3.5" />
                </>
              )}
            </button>

            {/* Resend OTP Action */}
            <div className="flex items-center justify-between pt-2 text-xs">
              <button
                type="button"
                onClick={() => { setStep(1); setError(''); setInfoMessage(''); }}
                className="text-slate-400 hover:text-slate-200 transition-colors flex items-center gap-1"
              >
                <ArrowLeft className="w-3 h-3" />
                <span>Change Email</span>
              </button>

              <button
                type="button"
                onClick={handleResendOtp}
                disabled={cooldownSeconds > 0 || loading}
                className="text-red-400 hover:text-red-300 font-semibold disabled:text-slate-600 transition-colors flex items-center gap-1"
              >
                <RefreshCw className={`w-3 h-3 ${loading ? 'animate-spin' : ''}`} />
                <span>
                  {cooldownSeconds > 0 ? `Resend Code (${cooldownSeconds}s)` : 'Resend Code'}
                </span>
              </button>
            </div>
          </form>
        )}

        {/* ================= STEP 3: RESET PASSWORD ================= */}
        {step === 3 && (
          <form onSubmit={handleResetPassword} className="space-y-4">
            <div>
              <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
                New Password
              </label>
              <div className="relative">
                <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                <input
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={newPassword}
                  onChange={(e) => {
                    setNewPassword(e.target.value);
                    if (error) setError('');
                  }}
                  placeholder="••••••••"
                  className={`w-full pl-10 pr-10 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none transition-colors ${currentRole.inputFocusStyle}`}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-3 text-slate-500 hover:text-slate-300 transition-colors"
                >
                  {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                </button>
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5">
                Confirm New Password
              </label>
              <div className="relative">
                <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
                <input
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={confirmPassword}
                  onChange={(e) => {
                    setConfirmPassword(e.target.value);
                    if (error) setError('');
                  }}
                  placeholder="••••••••"
                  className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none transition-colors ${currentRole.inputFocusStyle}`}
                />
              </div>
            </div>

            {/* Password Validation Checklist */}
            <div className="p-3 bg-slate-950/60 border border-slate-800 rounded-xl space-y-1.5 text-[11px]">
              <p className="font-semibold text-slate-400 mb-1">Password Requirements:</p>
              <div className="grid grid-cols-2 gap-1">
                <div className={`flex items-center gap-1.5 ${hasMinLen ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>At least 8 characters</span>
                </div>
                <div className={`flex items-center gap-1.5 ${hasUpper ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>1 Uppercase letter (A-Z)</span>
                </div>
                <div className={`flex items-center gap-1.5 ${hasLower ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>1 Lowercase letter (a-z)</span>
                </div>
                <div className={`flex items-center gap-1.5 ${hasNumber ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>1 Number (0-9)</span>
                </div>
                <div className={`flex items-center gap-1.5 ${hasSpecial ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>1 Special character</span>
                </div>
                <div className={`flex items-center gap-1.5 ${isMatch ? 'text-emerald-400' : 'text-slate-500'}`}>
                  <CheckCircle2 className="w-3 h-3 shrink-0" />
                  <span>Passwords match</span>
                </div>
              </div>
            </div>

            <button
              type="submit"
              disabled={loading || !isPasswordValid}
              className={`w-full py-3 text-white font-semibold rounded-xl text-xs shadow-lg transition-all flex items-center justify-center gap-2 mt-2 disabled:opacity-50 ${currentRole.buttonStyle}`}
            >
              {loading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Updating Password...</span>
                </>
              ) : (
                <>
                  <span>Save New Password</span>
                  <Sparkles className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        )}

        {/* ================= STEP 4: SUCCESS ================= */}
        {step === 4 && (
          <div className="text-center space-y-4 py-4 animate-in fade-in">
            <div className="w-16 h-16 bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 rounded-full flex items-center justify-center mx-auto mb-2">
              <CheckCircle2 className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-white">Password Updated!</h3>
            <p className="text-xs text-slate-300 leading-relaxed max-w-xs mx-auto">
              Your account password has been successfully updated in PostgreSQL database. You can now log in with your new password.
            </p>

            <button
              type="button"
              onClick={() => navigate(`/login?role=${activeTab}`)}
              className={`w-full py-3 text-white font-semibold rounded-xl text-xs shadow-lg transition-all flex items-center justify-center gap-2 mt-4 ${currentRole.buttonStyle}`}
            >
              <span>Back to Sign In</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>
        )}

        {/* Footer Link */}
        {step !== 4 && (
          <div className="mt-6 pt-5 border-t border-slate-800 text-center">
            <Link to={`/login?role=${activeTab}`} className="text-xs text-slate-400 hover:text-white transition-colors flex items-center justify-center gap-1 font-medium">
              <ArrowLeft className="w-3.5 h-3.5" />
              <span>Remember your password? Sign In</span>
            </Link>
          </div>
        )}
      </div>
    </div>
  );
};

export default ForgotPasswordPage;
