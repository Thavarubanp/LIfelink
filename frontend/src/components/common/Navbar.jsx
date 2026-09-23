import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/AuthContext';
import {
  Search,
  Bell,
  User,
  LogOut,
  ShieldAlert,
  Sparkles,
  Moon,
  Sun,
  Building2,
  Stethoscope,
  Users,
  Loader2,
  X
} from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import searchApi from '../../api/searchApi';
import notificationApi from '../../api/notificationApi';

// Global search result groups (order matches flattenedResults for keyboard navigation).
// Colors follow the role palette used by the navbar role badge.
const SEARCH_SECTIONS = [
  {
    key: 'hospitals',
    title: 'Hospitals',
    badge: 'Hospital',
    icon: Building2,
    avatar: 'H',
    headerClass: 'text-cyan-600 dark:text-cyan-400',
    selectedClass: 'bg-cyan-50 dark:bg-cyan-950/40 text-cyan-900 dark:text-cyan-200',
    avatarClass: 'bg-cyan-100 dark:bg-cyan-900/60 text-cyan-700 dark:text-cyan-300',
    badgeClass: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-950/60 dark:text-cyan-300 border-cyan-200 dark:border-cyan-900/50'
  },
  {
    key: 'doctors',
    title: 'Doctors',
    badge: 'Doctor',
    icon: Stethoscope,
    avatar: 'Dr',
    headerClass: 'text-emerald-600 dark:text-emerald-400',
    selectedClass: 'bg-emerald-50 dark:bg-emerald-950/40 text-emerald-900 dark:text-emerald-200',
    avatarClass: 'bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300',
    badgeClass: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900/50'
  },
  {
    key: 'users',
    title: 'Users',
    badge: 'User',
    icon: Users,
    avatar: null, // uses item.avatarInitial
    headerClass: 'text-violet-600 dark:text-violet-400',
    selectedClass: 'bg-violet-50 dark:bg-violet-950/40 text-violet-900 dark:text-violet-200',
    avatarClass: 'bg-violet-100 dark:bg-violet-900/60 text-violet-700 dark:text-violet-300',
    badgeClass: 'bg-violet-50 text-violet-700 dark:bg-violet-950/60 dark:text-violet-300 border-violet-200 dark:border-violet-900/50'
  }
];

export const Navbar = ({ onOpenNotifications }) => {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [theme, setTheme] = useState('light');
  const [dropdownOpen, setDropdownOpen] = useState(false);

  // Search state
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState(null);
  const [isSearching, setIsSearching] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState(-1);
  const searchInputRef = useRef(null);
  const searchContainerRef = useRef(null);

  // Notification state
  const [unreadCount, setUnreadCount] = useState(0);

  // Fetch unread notification count
  const fetchUnread = async () => {
    if (!user) return;
    try {
      const res = await notificationApi.getUnreadCount();
      if (res && typeof res.count === 'number') {
        setUnreadCount(res.count);
      }
    } catch {
      // Ignore unread fetch failure
    }
  };

  useEffect(() => {
    fetchUnread();
    const interval = setInterval(fetchUnread, 30000);
    const handleUpdate = () => fetchUnread();
    window.addEventListener('lifelink-notifications-updated', handleUpdate);

    return () => {
      clearInterval(interval);
      window.removeEventListener('lifelink-notifications-updated', handleUpdate);
    };
  }, [user]);

  // Global Ctrl+K / Cmd+K listener
  useEffect(() => {
    const handleKeyDown = (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Debounced search query
  useEffect(() => {
    if (!searchQuery || searchQuery.trim().length < 2) {
      setSearchResults(null);
      setIsSearching(false);
      return;
    }

    setIsSearching(true);
    const timer = setTimeout(async () => {
      try {
        const data = await searchApi.globalSearch(searchQuery.trim());
        setSearchResults(data);
        setSearchOpen(true);
        setSelectedIndex(-1);
      } catch {
        setSearchResults(null);
      } finally {
        setIsSearching(false);
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [searchQuery]);

  // Close search on click outside
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (searchContainerRef.current && !searchContainerRef.current.contains(e.target)) {
        setSearchOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const flattenedResults = React.useMemo(() => {
    if (!searchResults) return [];
    return [
      ...(searchResults.hospitals || []),
      ...(searchResults.doctors || []),
      ...(searchResults.users || [])
    ];
  }, [searchResults]);

  const handleSelectResult = (item) => {
    if (!item) return;
    setSearchOpen(false);
    setSearchQuery('');
    setSelectedIndex(-1);
    navigate(item.route);
  };

  const handleSearchKeyDown = (e) => {
    if (!searchOpen || flattenedResults.length === 0) {
      if (e.key === 'Escape') {
        setSearchOpen(false);
        searchInputRef.current?.blur();
      }
      return;
    }

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setSelectedIndex((prev) => (prev < flattenedResults.length - 1 ? prev + 1 : 0));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelectedIndex((prev) => (prev > 0 ? prev - 1 : flattenedResults.length - 1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (selectedIndex >= 0 && selectedIndex < flattenedResults.length) {
        handleSelectResult(flattenedResults[selectedIndex]);
      } else if (flattenedResults.length > 0) {
        handleSelectResult(flattenedResults[0]);
      }
    } else if (e.key === 'Escape') {
      setSearchOpen(false);
      searchInputRef.current?.blur();
    }
  };

  const toggleTheme = () => {
    const nextTheme = theme === 'light' ? 'dark' : 'light';
    setTheme(nextTheme);
    if (nextTheme === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  };

  const getRoleInfo = () => {
    if (!user || !user.roles) {
      return {
        label: 'Donor / Patient',
        classes: 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 border-red-200 dark:border-red-900/50'
      };
    }
    const roles = Array.isArray(user.roles) ? user.roles : [user.roles];
    if (roles.includes('Admin')) {
      return {
        label: 'Admin',
        classes: 'bg-purple-50 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border-purple-200 dark:border-purple-900/50'
      };
    }
    if (roles.includes('HospitalStaff')) {
      return {
        label: 'Hospital',
        classes: 'bg-cyan-50 text-cyan-700 dark:bg-cyan-950/60 dark:text-cyan-300 border-cyan-200 dark:border-cyan-900/50'
      };
    }
    if (roles.includes('Doctor')) {
      return {
        label: 'Doctor',
        classes: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900/50'
      };
    }
    return {
      label: 'Donor / Patient',
      classes: 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 border-red-200 dark:border-red-900/50'
    };
  };

  const roleInfo = getRoleInfo();

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

        {/* Global Quick Search Input with Suggestions Dropdown */}
        <div ref={searchContainerRef} className="relative hidden md:block">
          <div className="flex items-center gap-2 px-3 py-1.5 bg-slate-100 dark:bg-slate-800/80 border border-slate-200 dark:border-slate-700/60 rounded-lg text-slate-400 w-64 focus-within:w-80 focus-within:border-red-500 transition-all">
            {isSearching ? (
              <Loader2 className="w-4 h-4 text-red-500 animate-spin" />
            ) : (
              <Search className="w-4 h-4 text-slate-400" />
            )}
            <input
              ref={searchInputRef}
              type="text"
              value={searchQuery}
              onChange={(e) => {
                setSearchQuery(e.target.value);
                if (!searchOpen) setSearchOpen(true);
              }}
              onFocus={() => {
                if (searchQuery.trim().length >= 2) setSearchOpen(true);
              }}
              onKeyDown={handleSearchKeyDown}
              placeholder="Search by name or email... (Ctrl+K)"
              className="bg-transparent text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none w-full"
            />
            {searchQuery && (
              <button
                onClick={() => {
                  setSearchQuery('');
                  setSearchResults(null);
                  setSearchOpen(false);
                }}
                className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>

          {/* Search Results Dropdown */}
          {searchOpen && searchQuery.trim().length >= 2 && (
            <div className="absolute left-0 mt-2 w-80 md:w-96 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl shadow-2xl overflow-hidden z-50 animate-in fade-in slide-in-from-top-2">
              {isSearching && !searchResults && (
                <div className="p-4 text-center text-xs text-slate-400 flex items-center justify-center gap-2">
                  <Loader2 className="w-4 h-4 animate-spin text-red-500" />
                  <span>Searching LifeLink directory...</span>
                </div>
              )}

              {searchResults && searchResults.totalCount === 0 && (
                <div className="p-5 text-center text-xs text-slate-400">
                  <p className="font-semibold text-slate-700 dark:text-slate-300">No matching users, doctors, or hospitals found.</p>
                </div>
              )}

              {searchResults && searchResults.totalCount > 0 && (
                <div className="max-h-96 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
                  {SEARCH_SECTIONS.filter((section) => searchResults[section.key]?.length > 0).map((section) => {
                    const SectionIcon = section.icon;
                    const items = searchResults[section.key];
                    return (
                      <div key={section.key} className="p-2">
                        <div className={`flex items-center gap-1.5 px-2 py-1 text-[11px] font-bold uppercase tracking-wider ${section.headerClass}`}>
                          <SectionIcon className="w-3.5 h-3.5" />
                          <span>{section.title} ({items.length})</span>
                        </div>
                        {items.map((item) => {
                          const globalIdx = flattenedResults.findIndex(r => r.id === item.id && r.resultType === item.resultType);
                          const isSelected = globalIdx === selectedIndex;
                          return (
                            <div
                              key={item.id}
                              onClick={() => handleSelectResult(item)}
                              className={`flex items-center gap-3 px-3 py-2 rounded-lg cursor-pointer transition-colors ${
                                isSelected ? section.selectedClass : 'hover:bg-slate-50 dark:hover:bg-slate-800/60'
                              }`}
                            >
                              <div className={`w-8 h-8 rounded-lg font-bold flex items-center justify-center text-xs shrink-0 ${section.avatarClass}`}>
                                {section.avatar ?? (item.avatarInitial || 'U')}
                              </div>
                              <div className="flex-1 min-w-0">
                                <p className="text-xs font-semibold text-slate-900 dark:text-slate-100 truncate">{item.displayName}</p>
                                <p className="text-[11px] text-slate-500 dark:text-slate-400 truncate">{item.subText}</p>
                              </div>
                              <span className={`shrink-0 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${section.badgeClass}`}>
                                {section.badge}
                              </span>
                            </div>
                          );
                        })}
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Right Controls */}
      <div className="flex items-center gap-3">
        {/* Dynamic Role Badge */}
        <span
          className={`hidden sm:inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold border ${roleInfo.classes} transition-all`}
        >
          <Sparkles className="w-3 h-3 mr-1" />
          {roleInfo.label}
        </span>

        {/* Theme Toggle */}
        <button
          onClick={toggleTheme}
          className="p-2 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Toggle Theme"
        >
          {theme === 'light' ? <Moon className="w-4 h-4" /> : <Sun className="w-4 h-4 text-amber-400" />}
        </button>

        {/* Notifications Trigger with Database-Backed Unread Indicator */}
        <button
          onClick={onOpenNotifications}
          className="relative p-2 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title={unreadCount > 0 ? `${unreadCount} unread notifications` : 'Notifications'}
        >
          <Bell className="w-4 h-4" />
          {unreadCount > 0 && (
            <>
              <span className="absolute top-1.5 right-1.5 w-2 h-2 bg-red-600 rounded-full animate-ping" />
              <span className="absolute top-1.5 right-1.5 w-2 h-2 bg-red-600 rounded-full" />
            </>
          )}
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
                <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{roleInfo.label}</p>
              </div>

              <Link
                to="/my-profile"
                onClick={() => setDropdownOpen(false)}
                className="flex items-center gap-2 px-4 py-2 text-xs text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800"
              >
                <User className="w-3.5 h-3.5" />
                My Profile
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
