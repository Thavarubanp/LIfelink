import React from 'react';
import { NavLink } from 'react-router-dom';
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
  History,
  UserCheck
} from 'lucide-react';

export const Sidebar = () => {
  const { user } = useAuth();
  const userRoles = user?.roles ? (Array.isArray(user.roles) ? user.roles : [user.roles]) : ['User'];

  const getNavItems = () => {
    if (userRoles.includes('Admin')) {
      return [
        { label: 'Admin Dashboard', path: '/admin/dashboard', icon: LayoutDashboard },
        { label: 'Hospital Approvals', path: '/admin/hospitals/pending', icon: Building2 },
        { label: 'Hospitals Directory', path: '/admin/hospitals', icon: Building2 },
        { label: 'User Governance', path: '/admin/users', icon: Users },
        { label: 'Complaints Hub', path: '/admin/complaints', icon: AlertTriangle },
        { label: 'Appeals Queue', path: '/admin/appeals', icon: FileText },
        { label: 'Platform Governance', path: '/admin/governance', icon: ShieldCheck },
        { label: 'Audit Logs', path: '/admin/audit-logs', icon: History }
      ];
    }

    if (userRoles.includes('HospitalStaff')) {
      return [
        { label: 'Hospital Overview', path: '/hospital/dashboard', icon: LayoutDashboard },
        { label: 'Blood Inventory', path: '/hospital/inventory', icon: Droplet },
        { label: 'Emergency Center', path: '/hospital/emergency', icon: Zap },
        { label: 'Hospital Requests', path: '/hospital/requests', icon: ClipboardList },
        { label: 'Patient Verification', path: '/hospital/requests/verify', icon: UserCheck },
        { label: 'Inter-Hospital Transfers', path: '/hospital/transfers', icon: ArrowLeftRight },
        { label: 'Submit Evidence', path: '/hospital/activity-reports', icon: FileText }
      ];
    }

    if (userRoles.includes('Doctor')) {
      return [
        { label: 'Doctor Dashboard', path: '/doctor/dashboard', icon: LayoutDashboard },
        { label: 'Screening Queue', path: '/doctor/screenings', icon: Stethoscope },
        { label: 'Hospital Requests', path: '/doctor/blood-requests', icon: ClipboardList },
        { label: 'Donor Final Selection', path: '/doctor/reviews', icon: UserCheck }
      ];
    }

    // Default Donor / User navigation
    return [
      { label: 'Donor Dashboard', path: '/donor/dashboard', icon: LayoutDashboard },
      { label: 'Available Requests', path: '/donor/requests', icon: Droplet },
      { label: 'Create Request', path: '/donor/requests/create', icon: ClipboardList },
      { label: 'My Requests', path: '/donor/my-requests', icon: History },
      { label: 'My Acceptances', path: '/donor/acceptances', icon: UserCheck },
      { label: 'Complaints', path: '/donor/complaints', icon: AlertTriangle }
    ];
  };

  const navItems = getNavItems();

  return (
    <aside className="w-64 border-r border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shrink-0 hidden md:flex flex-col justify-between p-4 transition-colors">
      <div className="space-y-1">
        <div className="px-3 py-2 text-[10px] font-bold text-slate-400 dark:text-slate-500 uppercase tracking-wider">
          Main Navigation
        </div>
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2.5 rounded-lg text-xs font-medium transition-all ${
                  isActive
                    ? 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 font-semibold border-l-4 border-red-600'
                    : 'text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100'
                }`
              }
            >
              <Icon className="w-4 h-4 shrink-0" />
              <span>{item.label}</span>
            </NavLink>
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
