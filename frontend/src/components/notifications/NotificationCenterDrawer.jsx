import React from 'react';
import { X, Bell, Inbox } from 'lucide-react';

export const NotificationCenterDrawer = ({ isOpen, onClose, notifications = [] }) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-slate-950/40 backdrop-blur-sm flex justify-end animate-in fade-in">
      <div className="w-full max-w-sm bg-white dark:bg-slate-900 h-full border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col justify-between">
        {/* Header */}
        <div className="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Bell className="w-4 h-4 text-red-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Notifications</h3>
            <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-400">
              {notifications.length} Unread
            </span>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Notifications List */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {notifications.length > 0 ? (
            notifications.map((n) => (
              <div
                key={n.id || n.notificationId || Math.random()}
                className="p-3.5 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-200/80 dark:border-slate-700/60 hover:border-red-500/50 transition-all"
              >
                <div className="flex items-start justify-between gap-2">
                  <h5 className="text-xs font-bold text-slate-900 dark:text-slate-100 leading-tight">{n.title || n.subject}</h5>
                  <span className="text-[10px] text-slate-400 shrink-0">{n.time || (n.createdAt ? new Date(n.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '')}</span>
                </div>
                <p className="text-xs text-slate-600 dark:text-slate-300 mt-1 leading-relaxed">{n.message || n.description}</p>
              </div>
            ))
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-center p-6 text-slate-400">
              <Inbox className="w-10 h-10 text-slate-500 stroke-1 mb-2" />
              <p className="text-xs font-semibold text-slate-300">No Notifications Available</p>
              <p className="text-[11px] text-slate-500 mt-1">You have no unread notifications or system alerts at this time.</p>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-slate-100 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/50 text-center">
          <button
            onClick={onClose}
            className="text-xs font-semibold text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100"
          >
            Mark All as Read
          </button>
        </div>
      </div>
    </div>
  );
};

export default NotificationCenterDrawer;
