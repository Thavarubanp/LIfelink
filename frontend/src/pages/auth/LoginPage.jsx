import React, { useState, useEffect } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { getDashboardPath, isUnapprovedHospitalStaff, HOSPITAL_WAITING_PATH } from '../../utils/roleUtils';
import { Mail, Lock, Sparkles, AlertCircle, Loader2, Heart, Building2, ShieldCheck, Stethoscope, ArrowRight } from 'lucide-react';

export const LoginPage = ({ initialRole }) => {
  const location = useLocation();
  const searchParams = new URLSearchParams(location.search);
  const tabParam = searchParams.get('tab') || searchParams.get('role');

  // Determine initial active tab based on prop or query param or path
  const getInitialTab = () => {
    if (initialRole) return initialRole;
    if (location.pathname === '/admin/login' || tabParam === 'admin' || tabParam === 'administrator') return 'admin';
    if (tabParam === 'hospital' || tabParam === 'staff') return 'hospital';
    if (tabParam === 'doctor') return 'doctor';
    return 'donor';
  };

  const [activeTab, setActiveTab] = useState(getInitialTab);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const { login } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    const tab = getInitialTab();
    setActiveTab(tab);
  }, [location.pathname, location.search]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const res = await login({ email, password });
      
      // Determine user roles from JWT response
      const userRoles = res?.data?.user?.roles || [];
      const userObj = res?.data?.user || {};

      // If account is suspended, redirect to the governance/suspension page
      if (userObj.isSuspended) {
        navigate('/governance/status', { replace: true });
        return;
      }

      // Doctor with first-login flag must change their password before anything else
      if (userRoles.includes('Doctor') && userObj.mustChangePassword) {
        navigate('/doctor/change-password', { replace: true });
        return;
      }

      // Hospital staff of a hospital that is not approved go only to their registration status page
      if (isUnapprovedHospitalStaff(userObj)) {
        navigate(HOSPITAL_WAITING_PATH, { replace: true });
        return;
      }

      // Default route based on primary JWT role
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

  // Tab dynamic styling details (4 roles: Donor, Hospital, Doctor, Admin)
  const roleConfig = {
    donor: {
      title: 'Donor & Patient Portal',
      subtitle: 'Sign in to request blood, manage donations, and view matches',
      badge: 'Donor Services',
      glowColor: 'bg-red-600/10',
      activeTabStyle: 'bg-red-600/20 text-red-400 border-red-500/30',
      buttonStyle: 'bg-red-600 hover:bg-red-700 shadow-red-600/25 focus:ring-red-500',
      inputFocusStyle: 'focus:border-red-500',
      placeholder: 'donor@lifelink.org',
      registerText: "Don't have a donor account?",
      registerLinkText: 'Register as Donor / Patient',
      registerUrl: '/register'
    },
    hospital: {
      title: 'Hospital Staff Portal',
      subtitle: 'Medical center dashboard for inventory & emergency transfers',
      badge: 'Authorized Medical Personnel',
      glowColor: 'bg-cyan-600/10',
      activeTabStyle: 'bg-cyan-600/20 text-cyan-400 border-cyan-500/30',
      buttonStyle: 'bg-cyan-600 hover:bg-cyan-700 shadow-cyan-600/25 focus:ring-cyan-500',
      inputFocusStyle: 'focus:border-cyan-500',
      placeholder: 'staff@cityhospital.org',
      registerText: 'Register new healthcare facility?',
      registerLinkText: 'Onboard Hospital Facility',
      registerUrl: '/register-hospital'
    },
    doctor: {
      title: 'Doctor Portal',
      subtitle: 'Clinical access for donor screening, verification, and patient management',
      badge: 'Medical Specialist',
      glowColor: 'bg-emerald-600/10',
      activeTabStyle: 'bg-emerald-600/20 text-emerald-400 border-emerald-500/30',
      buttonStyle: 'bg-emerald-600 hover:bg-emerald-700 shadow-emerald-600/25 focus:ring-emerald-500',
      inputFocusStyle: 'focus:border-emerald-500',
      placeholder: 'doctor@hospital.org',
      registerText: 'Doctor accounts are created by your hospital administrator.',
      registerLinkText: null,
      registerUrl: null
    },
    admin: {
      title: 'Administrator Console',
      subtitle: 'System management, governance, and audit operations',
      badge: 'System SuperUser Access',
      glowColor: 'bg-purple-600/10',
      activeTabStyle: 'bg-purple-600/20 text-purple-400 border-purple-500/30',
      buttonStyle: 'bg-purple-600 hover:bg-purple-700 shadow-purple-600/25 focus:ring-purple-500',
      inputFocusStyle: 'focus:border-purple-500',
      placeholder: 'admin@lifelink.gov',
      registerText: 'Restricted access portal.',
      registerLinkText: 'System Admin Governance',
      registerUrl: '#'
    }
  };

  const currentRole = roleConfig[activeTab] || roleConfig.donor;

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-10 relative overflow-hidden">
      {/* Background Glow dynamically changed by active tab */}
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

        {/* Role Selection Tabs — 4 roles */}
        <div className="grid grid-cols-4 gap-1 p-1 bg-slate-950/80 border border-slate-800 rounded-xl mb-6">
          <button
            type="button"
            id="login-tab-donor"
            onClick={() => { setActiveTab('donor'); setError(''); }}
            className={`flex items-center justify-center gap-1 py-2 px-1.5 rounded-lg text-[11px] font-semibold transition-all border ${
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
            id="login-tab-hospital"
            onClick={() => { setActiveTab('hospital'); setError(''); }}
            className={`flex items-center justify-center gap-1 py-2 px-1.5 rounded-lg text-[11px] font-semibold transition-all border ${
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
            id="login-tab-doctor"
            onClick={() => { setActiveTab('doctor'); setError(''); }}
            className={`flex items-center justify-center gap-1 py-2 px-1.5 rounded-lg text-[11px] font-semibold transition-all border ${
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
            id="login-tab-admin"
            onClick={() => { setActiveTab('admin'); setError(''); }}
            className={`flex items-center justify-center gap-1 py-2 px-1.5 rounded-lg text-[11px] font-semibold transition-all border ${
              activeTab === 'admin'
                ? roleConfig.admin.activeTabStyle
                : 'text-slate-400 hover:text-slate-200 border-transparent'
            }`}
          >
            <ShieldCheck className="w-3 h-3 shrink-0" />
            <span>Admin</span>
          </button>
        </div>

        {/* Active Role Title & Badge */}
        <div className="mb-5 text-center">
          <span className="inline-block px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-slate-800 text-slate-300 border border-slate-700 tracking-wider uppercase mb-1.5">
            {currentRole.badge}
          </span>
          <h2 className="text-lg font-bold text-white">{currentRole.title}</h2>
          <p className="text-xs text-slate-400 mt-0.5">{currentRole.subtitle}</p>
        </div>

        {error && (
          <div className="mb-6 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
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

          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
                Password
              </label>
              <Link to={`/forgot-password?role=${activeTab}`} className="text-xs text-slate-400 hover:text-white transition-colors font-medium">
                Forgot password?
              </Link>
            </div>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-500 absolute left-3.5 top-3" />
              <input
                type="password"
                required
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  if (error) setError('');
                }}
                placeholder="••••••••"
                className={`w-full pl-10 pr-4 py-2.5 bg-slate-800/80 border border-slate-700/80 rounded-xl text-xs text-white placeholder-slate-500 focus:outline-none transition-colors ${currentRole.inputFocusStyle}`}
              />
            </div>
          </div>

          <button
            type="submit"
            id="login-submit-btn"
            disabled={loading}
            className={`w-full py-3 text-white font-semibold rounded-xl text-xs shadow-lg transition-all flex items-center justify-center gap-2 mt-2 disabled:opacity-50 ${currentRole.buttonStyle}`}
          >
            {loading ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Authenticating JWT Credentials...</span>
              </>
            ) : (
              <>
                <span>Sign In to {activeTab === 'admin' ? 'Console' : 'Dashboard'}</span>
                <Sparkles className="w-3.5 h-3.5" />
              </>
            )}
          </button>
        </form>

        {/* Hospital Staff Onboarding Card Callout */}
        {activeTab === 'hospital' && (
          <div className="mt-6 p-4 bg-cyan-950/40 border border-cyan-800/60 rounded-xl flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-left">
            <div>
              <p className="text-xs font-semibold text-cyan-200">New Hospital Facility?</p>
              <p className="text-[11px] text-slate-400 mt-0.5 leading-relaxed">
                Register your hospital and submit required information for administrative verification and approval.
              </p>
            </div>
            <Link
              to="/register-hospital"
              className="px-3.5 py-2 bg-cyan-600 hover:bg-cyan-500 text-white font-semibold text-xs rounded-lg shrink-0 flex items-center justify-center gap-1.5 transition-colors shadow-md shadow-cyan-600/20"
            >
              <span>Register Hospital</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>
        )}

        {/* Doctor info callout */}
        {activeTab === 'doctor' && (
          <div className="mt-6 p-4 bg-emerald-950/40 border border-emerald-800/60 rounded-xl text-left">
            <p className="text-xs font-semibold text-emerald-300">Doctor Account Information</p>
            <p className="text-[11px] text-slate-400 mt-1 leading-relaxed">
              Doctor accounts are provisioned by your affiliated hospital. Contact your Hospital Administrator to have your account created. You will be required to change your password on first login.
            </p>
          </div>
        )}

        {/* Footer Navigation */}
        {activeTab === 'donor' && (
          <div className="mt-6 pt-5 border-t border-slate-800 text-center">
            <p className="text-xs text-slate-400">
              {currentRole.registerText}{' '}
              <Link to={currentRole.registerUrl} className="text-slate-200 font-semibold hover:underline">
                {currentRole.registerLinkText}
              </Link>
            </p>
          </div>
        )}

        {(activeTab === 'admin') && (
          <div className="mt-6 pt-5 border-t border-slate-800 text-center">
            <p className="text-[11px] text-slate-500">
              🔒 Multi-Factor Authentication enabled. System log audit active.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default LoginPage;
