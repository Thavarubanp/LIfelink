import React from 'react';
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
  Users,
  AlertTriangle,
  UserCheck
} from 'lucide-react';

export const Sidebar = () => {
  const { user } = useAuth();
  const { pathname } = useLocation();
  const userRoles = user?.roles ? (Array.isArray(user.roles) ? user.roles : [user.roles]) : ['User'];

  const getNavItems = () => {
    if (userRoles.includes('Admin')) {
      return [
        { label: 'Admin Dashboard', path: '/admin/dashboard', icon: LayoutDashboard },
        { label: 'Hospital Registration Requests', path: '/admin/hospitals/pending', icon: Building2 },
        { label: 'Complaints Hub', path: '/admin/complaints', icon: AlertTriangle },
        { label: 'Suspension Appeals', path: '/admin/appeals', icon: FileText },
        { label: 'Create Blood Request', path: '/donor/requests/create', icon: ClipboardList },
        { label: 'Donate Blood', path: '/donor/requests', icon: Droplet }
      ];
    }

    if (userRoles.includes('HospitalStaff')) {
      return [
        { label: 'Hospital Overview', path: '/hospital/dashboard', icon: LayoutDashboard },
        { label: 'Blood Inventory', path: '/hospital/inventory', icon: Droplet },
        { label: 'Emergency Center', path: '/hospital/emergency', icon: Zap },
        { label: 'Create Blood Request', path: '/donor/requests/create', icon: ClipboardList },
        { label: 'Verify Blood Requests', path: '/hospital/requests/verify', icon: UserCheck },
        { label: 'Inter-Hospital Transfers', path: '/hospital/transfers', icon: ArrowLeftRight },
        { label: 'Doctor Management', path: '/hospital/doctors', icon: Stethoscope },
        { label: 'Complaints', path: '/donor/complaints', icon: AlertTriangle },
        { label: 'My Appeals', path: '/appeals/history', icon: FileText }
      ];
    }

    // Doctors have no complaint functionality
    if (userRoles.includes('Doctor')) {
      return [
        { label: 'Doctor Dashboard', path: '/doctor/dashboard', icon: LayoutDashboard },
        { label: 'Screening Queue', path: '/doctor/screenings', icon: Stethoscope }
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
    <aside className="w-64 border-r border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shrink-0 hidden md:flex flex-col justify-between p-4 transition-colors">
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
              aria-current={isActive ? 'page' : undefined}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-xs font-medium transition-all ${
                isActive
                  ? 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 font-semibold border-l-4 border-red-600'
                  : 'text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100'
              }`}
            >
              <Icon className="w-4 h-4 shrink-0" />
              <span>{item.label}</span>
            </Link>
          );
        })}
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
  );
};

export default Sidebar;
