import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import {
  LayoutDashboard,
  Droplet,
  ClipboardList,
  Stethoscope,
  Building2,
  Zap,
  ArrowLeftRight,
  ShieldCheck,
  FileText,
  AlertTriangle,
  UserCheck,
  HeartHandshake,
  History,
  X
} from 'lucide-react';
import BrandLogo from './BrandLogo';
import { useAdminAttention, badgeText } from '../../context/useAdminAttention';
import { useRoleAttention } from '../../context/useRoleAttention';

export const Sidebar = ({ mobileOpen = false, onClose }) => {
  const { user } = useAuth();
  const { pathname } = useLocation();
  const userRoles = user?.roles ? (Array.isArray(user.roles) ? user.roles : [user.roles]) : ['User'];
  const isAdmin = userRoles.includes('Admin');
  const usesRoleAttention = !isAdmin && (userRoles.includes('HospitalStaff') || userRoles.includes('Doctor'));
  // Admin badges: new requests/transfers (Activity log) and items waiting for the admin
  const attention = useAdminAttention(isAdmin);
  const roleAttention = useRoleAttention(usesRoleAttention);
  const badgeFor = (key) => {
    if (!attention || !key) return null;
    if (key === 'activity') return badgeText((attention.newBloodRequests || 0) + (attention.newTransfers || 0));
    return badgeText(attention[key]);
  };
  const roleBadgeFor = (key) => badgeText(roleAttention?.[key]);

  const getNavItems = () => {
    if (userRoles.includes('Admin')) {
      return [
        { label: 'Admin Dashboard', path: '/admin/dashboard', icon: LayoutDashboard },
        { label: 'Activity Log', path: '/admin/activity', icon: History, badge: 'activity', badgeTitle: 'new blood requests and transfers' },
        { label: 'Hospital Registration Requests', path: '/admin/hospitals/pending', icon: Building2, badge: 'pendingRegistrations', badgeTitle: 'registrations waiting for review' },
        { label: 'Complaints Hub', path: '/admin/complaints', icon: AlertTriangle, badge: 'pendingComplaints', badgeTitle: 'complaints waiting for a reply' },
        { label: 'Suspension Appeals', path: '/admin/appeals', icon: FileText, badge: 'pendingAppeals', badgeTitle: 'appeals waiting for a reply' },
        { label: 'Create Blood Request', path: '/donor/requests/create', icon: ClipboardList },
        { label: 'View Blood Requests', path: '/donor/requests', icon: Droplet },
        { label: 'My Acceptances', path: '/donor/acceptances', icon: UserCheck }
      ];
    }

    if (userRoles.includes('HospitalStaff')) {
      return [
        { label: 'Hospital Overview', path: '/hospital/dashboard', icon: LayoutDashboard },
        { label: 'Blood Inventory', path: '/hospital/inventory', icon: Droplet },
        { label: 'Emergency Center', path: '/hospital/emergency', icon: Zap },
        { label: 'Create Blood Request', path: '/donor/requests/create', icon: ClipboardList },
        { label: 'Verify Blood Requests', path: '/hospital/requests/verify', icon: UserCheck, roleBadge: 'pendingHospitalVerifications', badgeTitle: 'requests waiting for hospital verification' },
        { label: 'Inter-Hospital Transfers', path: '/hospital/transfers', icon: ArrowLeftRight, roleBadge: 'pendingTransferResponses', badgeTitle: 'transfers waiting for your hospital response' },
        { label: 'Donate Blood', path: '/hospital/donate', icon: HeartHandshake },
        { label: 'Doctor Management', path: '/hospital/doctors', icon: Stethoscope },
        { label: 'Complaints', path: '/donor/complaints', icon: AlertTriangle },
        { label: 'My Appeals', path: '/appeals/history', icon: FileText }
      ];
    }

    // Doctors have no complaint functionality
    if (userRoles.includes('Doctor')) {
      return [
        { label: 'Doctor Dashboard', path: '/doctor/dashboard', icon: LayoutDashboard },
        { label: 'Screening Queue', path: '/doctor/screenings', icon: Stethoscope, roleBadge: 'pendingScreeningReviews', badgeTitle: 'screening reports waiting for review' }
      ];
    }

    // Default Donor / User navigation
    return [
      { label: 'Donor Dashboard', path: '/donor/dashboard', icon: LayoutDashboard },
      { label: 'Available Requests', path: '/donor/requests', icon: Droplet },
      { label: 'Create Blood Request', path: '/donor/requests/create', icon: ClipboardList },
      { label: 'My Acceptances', path: '/donor/acceptances', icon: UserCheck },
      { label: 'Complaints', path: '/donor/complaints', icon: AlertTriangle },
      { label: 'My Appeals', path: '/appeals/history', icon: FileText }
    ];
  };

  const navItems = getNavItems();

  // Exactly one item is active: the most specific (longest) path that equals or is a
  // segment prefix of the current route. NavLink's default prefix matching would light up
  // both /donor/requests and /donor/requests/create at the same time.
  const activePath = navItems
    .map((item) => item.path)
    .filter((path) => pathname === path || pathname.startsWith(`${path}/`))
    .reduce((best, path) => (best && best.length >= path.length ? best : path), null);

  return (
    <>
      {mobileOpen && (
        <button type="button" aria-label="Close navigation" onClick={onClose} className="fixed inset-0 z-40 bg-slate-950/55 backdrop-blur-sm md:hidden" />
      )}
      <aside
        id="primary-navigation"
        className={`fixed inset-y-0 left-0 z-50 flex w-[min(19rem,88vw)] shrink-0 flex-col justify-between border-r border-slate-200 bg-white p-4 shadow-2xl transition-transform duration-200 md:static md:z-auto md:w-64 md:translate-x-0 md:shadow-none dark:border-slate-800 dark:bg-slate-900 ${mobileOpen ? 'translate-x-0' : '-translate-x-full'}`}
      >
      <div className="min-h-0 overflow-y-auto">
        <div className="mb-5 flex items-center justify-between md:hidden">
          <BrandLogo size="sm" tagline="Emergency blood platform" />
          <button type="button" onClick={onClose} className="inline-flex h-10 w-10 items-center justify-center rounded-xl text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" aria-label="Close navigation">
            <X className="h-5 w-5" />
          </button>
        </div>
        <div className="space-y-1">
        <div className="px-3 py-2 text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-wider">
          Main Navigation
        </div>
        {navItems.map((item) => {
          const Icon = item.icon;
          const isActive = item.path === activePath;
          return (
            <Link
              key={item.path}
              to={item.path}
              onClick={onClose}
              aria-current={isActive ? 'page' : undefined}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-xs font-medium transition-all ${
                isActive
                  ? 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 font-semibold border-l-4 border-red-600'
                  : 'text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100'
              }`}
            >
              <Icon className="w-4 h-4 shrink-0" />
              <span className="flex-1">{item.label}</span>
              {(badgeFor(item.badge) || roleBadgeFor(item.roleBadge)) && (
                <span
                  className="ml-auto inline-flex min-w-[1.25rem] h-5 items-center justify-center rounded-full bg-red-600 px-1.5 text-[10px] font-bold text-white"
                  title={`${badgeFor(item.badge) || roleBadgeFor(item.roleBadge)} ${item.badgeTitle}`}
                  aria-label={`${badgeFor(item.badge) || roleBadgeFor(item.roleBadge)} ${item.badgeTitle}`}
                >
                  {badgeFor(item.badge) || roleBadgeFor(item.roleBadge)}
                </span>
              )}
            </Link>
          );
        })}
        </div>
      </div>

      {/* Sidebar Footer Info Card */}
      <div className="bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700/60 rounded-xl p-3.5 mt-6">
        <div className="flex items-center gap-2">
          <ShieldCheck className="w-4 h-4 text-emerald-500 shrink-0" />
          <span className="text-xs font-semibold text-slate-900 dark:text-slate-100">LifeLink Verified</span>
        </div>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-1 leading-tight">
          Secure ASP.NET Core & AI Orchestration System.
        </p>
      </div>
      </aside>
    </>
  );
};

export default Sidebar;
