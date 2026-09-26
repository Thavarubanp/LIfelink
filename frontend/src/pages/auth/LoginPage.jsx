import React, { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { getDashboardPath, isUnapprovedHospitalStaff, HOSPITAL_WAITING_PATH } from '../../utils/roleUtils';
import {
  Mail,
  Lock,
  Sparkles,
  AlertCircle,
  Loader2,
  Building2,
  Heart,
  Eye,
  EyeOff,
  X,
  ArrowRight,
  ShieldAlert,
  Stethoscope,
  UserPlus
} from 'lucide-react';

export const LoginPage = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const { login } = useAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [showRegisterModal, setShowRegisterModal] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const res = await login({ email: email.trim(), password });

      // Determine user roles from JWT response
      const userRoles = res?.data?.user?.roles || [];
      const userObj = res?.data?.user || {};

      // 1. If account is suspended, redirect to the governance/suspension page
      if (userObj.isSuspended) {
        navigate('/governance/status', { replace: true });
        return;
      }

      // 2. Doctor with first-login flag must change their password before anything else
      if (userRoles.includes('Doctor') && userObj.mustChangePassword) {
        navigate('/doctor/change-password', { replace: true });
        return;
      }

      // 3. Hospital staff of a hospital that is not approved go only to their registration status page
      if (isUnapprovedHospitalStaff(userObj)) {
        navigate(HOSPITAL_WAITING_PATH, { replace: true });
        return;
      }

      // 4. Default route based on primary JWT role via existing roleUtils hierarchy:
      // (Admin > HospitalStaff > Doctor > Donor/User)
      const targetDashboard = getDashboardPath(userObj);

      // Route to the originally attempted page, unless it was the generic root ("/"),
      // which would otherwise resolve before the role-aware dashboard is chosen
      const from = location.state?.from?.pathname;
      const excludedFrom = ['/', '/login', '/admin/login', '/hospital/waiting-approval'];
      const destination = (from && !excludedFrom.includes(from)) ? from : targetDashboard;
      navigate(destination, { replace: true });
    } catch (err) {
      const parsedMessage = getApiErrorMessage(err);
      setError(parsedMessage);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-10 relative overflow-hidden">
      {/* Background Ambient Glows */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-red-600/10 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-10 right-1/4 w-[350px] h-[350px] bg-rose-600/5 rounded-full blur-3xl pointer-events-none" />

      <div className="max-w-md w-full bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-2xl relative z-10 backdrop-blur-md">
        {/* Brand Header */}
        <div className="flex flex-col items-center text-center mb-7">
          <div className="w-14 h-14 rounded-2xl bg-gradient-to-tr from-red-600 to-rose-500 flex items-center justify-center text-white text-2xl font-bold shadow-lg shadow-red-600/30 mb-3.5 transition-transform hover:scale-105 duration-300">
            💉
          </div>
          <h1 className="text-2xl sm:text-3xl font-bold text-white tracking-tight">
            Life<span className="text-red-500">Link</span>
          </h1>
          <p className="text-xs sm:text-sm text-slate-400 mt-1 max-w-xs">
            Emergency Blood Management & Healthcare Network
          </p>
        </div>

        {/* Portal Greeting */}
        <div className="mb-6 text-center">
          <h2 className="text-lg font-semibold text-white">Sign In to Your Account</h2>
          <p className="text-xs text-slate-400 mt-0.5">
            Enter your credentials to access your LifeLink portal
          </p>
        </div>

        {/* Error Alert */}
        {error && (
          <div className="mb-6 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{error}</span>
          </div>
        )}

        {/* Unified Authentication Form */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label
              htmlFor="login-email"
              className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-1.5"
            >
              Email or Username
            </label>
            <div className="relative">
              <Mail className="w-4 h-4 text-slate-500 absolute left-3.5 top-3 pointer-events-none" />
              <input
                id="login-email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value);
                  if (error) setError('');
                }}
                placeholder="name@example.com"
                className="w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs sm:text-sm text-white placeholder-slate-500 focus:outline-none focus:border-red-500 focus:ring-1 focus:ring-red-500/50 transition-colors"
              />
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label
                htmlFor="login-password"
                className="block text-xs font-semibold text-slate-300 uppercase tracking-wider"
              >
                Password
              </label>
              <Link
                to="/forgot-password"
                className="text-xs text-slate-400 hover:text-red-400 transition-colors font-medium"
              >
                Forgot password?
              </Link>
            </div>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3 pointer-events-none" />
              <input
                id="login-password"
                type={showPassword ? 'text' : 'password'}
                required
                autoComplete="current-password"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  if (error) setError('');
                }}
                placeholder="••••••••"
                className="w-full pl-10 pr-10 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs sm:text-sm text-white placeholder-slate-500 focus:outline-none focus:border-red-500 focus:ring-1 focus:ring-red-500/50 transition-colors"
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-3 top-2.5 text-slate-500 hover:text-slate-300 transition-colors p-0.5"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>
          </div>

          <button
            type="submit"
            id="login-submit-btn"
            disabled={loading}
            className="w-full py-3 text-white font-semibold rounded-xl text-xs sm:text-sm shadow-lg bg-red-600 hover:bg-red-500 shadow-red-600/25 focus:ring-2 focus:ring-red-500 focus:outline-none transition-all flex items-center justify-center gap-2 mt-3 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {loading ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Signing in...</span>
              </>
            ) : (
              <>
                <span>Sign In</span>
                <Sparkles className="w-3.5 h-3.5" />
              </>
            )}
          </button>
        </form>

        {/* Registration Section */}
        <div className="mt-6 pt-5 border-t border-slate-800 text-center">
          <p className="text-xs text-slate-400">
            Don't have an account?{' '}
            <button
              type="button"
              id="open-register-btn"
              onClick={() => setShowRegisterModal(true)}
              className="text-red-400 hover:text-red-300 font-semibold hover:underline inline-flex items-center gap-1 transition-colors"
            >
              <span>Register</span>
              <ArrowRight className="w-3 h-3" />
            </button>
          </p>
        </div>

        {/* Security & Access Notice */}
        <div className="mt-4 text-center">
          <p className="text-[11px] text-slate-500">
            🔒 Protected by LifeLink Role-Based Access Governance & Audit Security.
          </p>
        </div>
      </div>

      {/* Registration Choice Modal */}
      {showRegisterModal && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-in fade-in duration-200"
          role="dialog"
          aria-modal="true"
          aria-labelledby="register-dialog-title"
        >
          <div
            className="bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-7 max-w-lg w-full shadow-2xl relative animate-in zoom-in-95 duration-200"
            onClick={(e) => e.stopPropagation()}
          >
            {/* Close Button */}
            <button
              type="button"
              onClick={() => setShowRegisterModal(false)}
              className="absolute top-4 right-4 text-slate-400 hover:text-white p-1 rounded-lg hover:bg-slate-800 transition-colors"
              aria-label="Close dialog"
            >
              <X className="w-5 h-5" />
            </button>

            {/* Modal Header */}
            <div className="flex items-center gap-3 mb-5">
              <div className="w-10 h-10 rounded-xl bg-red-600/20 border border-red-500/30 flex items-center justify-center text-red-400 shrink-0">
                <UserPlus className="w-5 h-5" />
              </div>
              <div>
                <h3 id="register-dialog-title" className="text-lg font-bold text-white">
                  Join the LifeLink Network
                </h3>
                <p className="text-xs text-slate-400">
                  Select the registration option that best suits your needs
                </p>
              </div>
            </div>

            {/* Options Grid */}
            <div className="space-y-3 mb-5">
              {/* Option 1: Individual / Donor Registration */}
              <Link
                to="/register"
                id="register-user-option"
                onClick={() => setShowRegisterModal(false)}
                className="group flex items-start gap-4 p-4 rounded-xl bg-slate-800/60 hover:bg-slate-800 border border-slate-700/70 hover:border-red-500/50 transition-all text-left"
              >
                <div className="w-10 h-10 rounded-xl bg-red-600/20 text-red-400 flex items-center justify-center shrink-0 group-hover:scale-105 group-hover:bg-red-600/30 transition-all">
                  <Heart className="w-5 h-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-white group-hover:text-red-400 transition-colors">
                      User Registration
                    </span>
                    <ArrowRight className="w-4 h-4 text-slate-400 group-hover:text-red-400 group-hover:translate-x-1 transition-all" />
                  </div>
                  <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                    For blood donors, patients requesting blood, and community members managing donations and requests.
                  </p>
                </div>
              </Link>

              {/* Option 2: Hospital / Facility Registration */}
              <Link
                to="/register-hospital"
                id="register-hospital-option"
                onClick={() => setShowRegisterModal(false)}
                className="group flex items-start gap-4 p-4 rounded-xl bg-slate-800/60 hover:bg-slate-800 border border-slate-700/70 hover:border-cyan-500/50 transition-all text-left"
              >
                <div className="w-10 h-10 rounded-xl bg-cyan-600/20 text-cyan-400 flex items-center justify-center shrink-0 group-hover:scale-105 group-hover:bg-cyan-600/30 transition-all">
                  <Building2 className="w-5 h-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-white group-hover:text-cyan-400 transition-colors">
                      Hospital Registration
                    </span>
                    <ArrowRight className="w-4 h-4 text-slate-400 group-hover:text-cyan-400 group-hover:translate-x-1 transition-all" />
                  </div>
                  <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                    For hospitals, clinics, and medical facilities requiring inventory management, blood verification, and emergency transfers.
                  </p>
                </div>
              </Link>
            </div>

            {/* Informational Guidance for Doctors & Admins */}
            <div className="p-3 bg-slate-950/70 border border-slate-800 rounded-xl space-y-2 text-[11px] text-slate-400">
              <div className="flex items-start gap-2">
                <Stethoscope className="w-3.5 h-3.5 text-emerald-400 shrink-0 mt-0.5" />
                <p>
                  <strong className="text-slate-300">Doctors:</strong> Accounts are provisioned directly by your affiliated hospital administrator.
                </p>
              </div>
              <div className="flex items-start gap-2">
                <ShieldAlert className="w-3.5 h-3.5 text-purple-400 shrink-0 mt-0.5" />
                <p>
                  <strong className="text-slate-300">Administrators:</strong> System access is strictly restricted to authorized governance personnel.
                </p>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default LoginPage;
