import React, { useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { Search, Bell, User, LogOut, ShieldAlert, Sparkles, Moon, Sun } from 'lucide-react';
import { Link } from 'react-router-dom';

export const Navbar = ({ onOpenNotifications }) => {
  const { user, logout } = useAuth();
  const [theme, setTheme] = useState('light');
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const toggleTheme = () => {
    const nextTheme = theme === 'light' ? 'dark' : 'light';
    setTheme(nextTheme);
    if (nextTheme === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  };

  const getRoleBadge = () => {
    if (!user || !user.roles) return 'User';
    const role = Array.isArray(user.roles) ? user.roles[0] : user.roles;
    switch (role) {
      case 'Admin': return 'System Admin';
      case 'HospitalStaff': return 'Hospital Staff';
      case 'Doctor': return 'Medical Doctor';
      default: return 'Donor / Patient';
    }
  };

  return (
    <header className="h-16 border-b border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/80 backdrop-blur-md sticky top-0 z-30 px-4 md:px-6 flex items-center justify-between transition-colors">
      {/* Brand Logo & Search */}
      <div className="flex items-center gap-6">
        <Link to="/" className="flex items-center gap-2.5 group">
          <div className="w-9 h-9 rounded-xl bg-red-600 flex items-center justify-center text-white font-bold shadow-md shadow-red-600/20 group-hover:scale-105 transition-transform">
            💉
          </div>
          <div className="flex flex-col">
            <span className="font-bold text-slate-900 dark:text-slate-100 tracking-tight text-lg leading-tight">
              Life<span className="text-red-600">Link</span>
            </span>
            <span className="text-[10px] text-slate-500 dark:text-slate-400 font-medium tracking-wider uppercase">
              Emergency Platform
            </span>
          </div>
        </Link>

        {/* Global Quick Search Input */}
        <div className="hidden md:flex items-center gap-2 px-3 py-1.5 bg-slate-100 dark:bg-slate-800/80 border border-slate-200 dark:border-slate-700/60 rounded-lg text-slate-400 w-64 focus-within:w-80 focus-within:border-red-500 transition-all">
          <Search className="w-4 h-4 text-slate-400" />
          <input
            type="text"
            placeholder="Search requests, donors, hospitals... (Cmd+K)"
            className="bg-transparent text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none w-full"
          />
        </div>
      </div>

      {/* Right Controls */}
      <div className="flex items-center gap-3">
        {/* Role Pill */}
        <span className="hidden sm:inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 border border-red-200 dark:border-red-900/50">
          <Sparkles className="w-3 h-3 mr-1 text-red-600" />
          {getRoleBadge()}
        </span>

        {/* Theme Toggle */}
        <button
          onClick={toggleTheme}
          className="p-2 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Toggle Theme"
        >
          {theme === 'light' ? <Moon className="w-4 h-4" /> : <Sun className="w-4 h-4 text-amber-400" />}
        </button>

        {/* Notifications Trigger */}
        <button
          onClick={onOpenNotifications}
          className="relative p-2 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Notifications"
        >
          <Bell className="w-4 h-4" />
          <span className="absolute top-1.5 right-1.5 w-2 h-2 bg-red-600 rounded-full animate-ping" />
          <span className="absolute top-1.5 right-1.5 w-2 h-2 bg-red-600 rounded-full" />
        </button>

        {/* User Profile Menu Dropdown */}
        <div className="relative">
          <button
            onClick={() => setDropdownOpen(!dropdownOpen)}
            className="flex items-center gap-2 p-1.5 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            <div className="w-8 h-8 rounded-full bg-slate-900 text-white font-semibold flex items-center justify-center text-xs shadow-sm">
              {user?.email ? user.email.charAt(0).toUpperCase() : 'U'}
            </div>
          </button>

          {dropdownOpen && (
            <div className="absolute right-0 mt-2 w-56 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-xl py-1.5 z-50 animate-in fade-in slide-in-from-top-2">
              <div className="px-4 py-2.5 border-b border-slate-100 dark:border-slate-800">
                <p className="text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">{user?.email}</p>
                <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{getRoleBadge()}</p>
              </div>

              <Link
                to="/donor/profile"
                onClick={() => setDropdownOpen(false)}
                className="flex items-center gap-2 px-4 py-2 text-xs text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800"
              >
                <User className="w-3.5 h-3.5" />
                Profile & Settings
              </Link>

              <Link
                to="/governance/status"
                onClick={() => setDropdownOpen(false)}
                className="flex items-center gap-2 px-4 py-2 text-xs text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800"
              >
                <ShieldAlert className="w-3.5 h-3.5 text-amber-500" />
                Governance Status
              </Link>

              <div className="border-t border-slate-100 dark:border-slate-800 my-1" />

              <button
                onClick={logout}
                className="flex items-center gap-2 w-full text-left px-4 py-2 text-xs text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-950/40"
              >
                <LogOut className="w-3.5 h-3.5" />
                Sign Out
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
};

export default Navbar;
