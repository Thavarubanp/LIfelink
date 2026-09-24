import React, { useState, useEffect, useCallback } from 'react';
import { X, Bell, Inbox, Loader2, CheckCheck } from 'lucide-react';
import notificationApi from '../../api/notificationApi';

export const NotificationCenterDrawer = ({ isOpen, onClose }) => {
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(false);
  const [markingAll, setMarkingAll] = useState(false);

  const fetchNotifications = useCallback(async () => {
    try {
      setLoading(true);
      const data = await notificationApi.getMyNotifications();
      setNotifications(Array.isArray(data) ? data : []);
    } catch {
      // Fallback or silence
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (isOpen) {
      fetchNotifications();
    }
  }, [isOpen, fetchNotifications]);

  const handleMarkRead = async (notificationId) => {
    try {
      await notificationApi.markRead(notificationId);
      setNotifications((prev) =>
        prev.map((n) => (n.notificationId === notificationId ? { ...n, isRead: true } : n))
      );
      window.dispatchEvent(new Event('lifelink-notifications-updated'));
    } catch {
      // Silently handle error
    }
  };

  // Dismiss permanently deletes this notification; the others stay
  const handleDismiss = async (e, notificationId) => {
    e.stopPropagation();
    try {
      await notificationApi.dismiss(notificationId);
      setNotifications((prev) => prev.filter((n) => (n.notificationId || n.id) !== notificationId));
      window.dispatchEvent(new Event('lifelink-notifications-updated'));
    } catch {
      // Silently handle error
    }
  };

  const handleMarkAllRead = async () => {
    try {
      setMarkingAll(true);
      await notificationApi.markAllRead();
      setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
      window.dispatchEvent(new Event('lifelink-notifications-updated'));
    } catch {
      // Silently handle error
    } finally {
      setMarkingAll(false);
    }
  };

  if (!isOpen) return null;

  const unreadCount = notifications.filter((n) => !n.isRead).length;

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-slate-950/40 backdrop-blur-sm flex justify-end animate-in fade-in">
      <div className="w-full max-w-sm bg-white dark:bg-slate-900 h-full border-l border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col justify-between">
        {/* Header */}
        <div className="p-4 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Bell className="w-4 h-4 text-red-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Notifications</h3>
            {unreadCount > 0 && (
              <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-400">
                {unreadCount} Unread
              </span>
            )}
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
          {loading ? (
            <div className="h-full flex flex-col items-center justify-center p-6 text-slate-400">
              <Loader2 className="w-6 h-6 animate-spin text-red-600 mb-2" />
              <p className="text-xs">Loading notifications...</p>
            </div>
          ) : notifications.length > 0 ? (
            notifications.map((n) => (
              <div
                key={n.notificationId || n.id}
                onClick={() => !n.isRead && handleMarkRead(n.notificationId || n.id)}
                className={`p-3.5 rounded-xl border transition-all cursor-pointer ${
                  !n.isRead
                    ? 'bg-red-50/40 dark:bg-red-950/20 border-red-200 dark:border-red-900/40 hover:border-red-500/60'
                    : 'bg-slate-50 dark:bg-slate-800/60 border-slate-200/80 dark:border-slate-700/60 opacity-80 hover:opacity-100'
                }`}
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-center gap-1.5 min-w-0">
                    {!n.isRead && (
                      <span className="w-2 h-2 rounded-full bg-red-600 shrink-0" title="Unread" />
                    )}
                    <h5 className="text-xs font-bold text-slate-900 dark:text-slate-100 leading-tight truncate">
                      {n.title || n.subject}
                    </h5>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <span className="text-[10px] text-slate-400">
                      {n.createdAt
                        ? new Date(n.createdAt).toLocaleDateString([], {
                            month: 'short',
                            day: 'numeric',
                            hour: '2-digit',
                            minute: '2-digit'
                          })
                        : ''}
                    </span>
                    <button
                      onClick={(e) => handleDismiss(e, n.notificationId || n.id)}
                      title="Dismiss notification"
                      aria-label="Dismiss notification"
                      className="p-0.5 rounded-md text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                    >
                      <X className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>
                <p className="text-xs text-slate-600 dark:text-slate-300 mt-1 leading-relaxed">
                  {n.message || n.description}
                </p>
                {!n.isRead && (
                  <div className="mt-2 flex justify-end">
                    <span className="text-[10px] font-semibold text-red-600 dark:text-red-400 flex items-center gap-1 hover:underline">
                      <CheckCheck className="w-3 h-3" /> Mark as read
                    </span>
                  </div>
                )}
              </div>
            ))
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-center p-6 text-slate-400">
              <Inbox className="w-10 h-10 text-slate-500 stroke-1 mb-2" />
              <p className="text-xs font-semibold text-slate-700 dark:text-slate-300">No Notifications Available</p>
              <p className="text-[11px] text-slate-500 mt-1">
                You have no notifications or system alerts at this time.
              </p>
            </div>
          )}
        </div>

        {/* Footer */}
        {notifications.length > 0 && unreadCount > 0 && (
          <div className="p-4 border-t border-slate-100 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/50 text-center">
            <button
              onClick={handleMarkAllRead}
              disabled={markingAll}
              className="text-xs font-semibold text-slate-600 dark:text-slate-400 hover:text-red-600 dark:hover:text-red-400 transition-colors disabled:opacity-50"
            >
              {markingAll ? 'Marking read...' : 'Mark All as Read'}
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

export default NotificationCenterDrawer;
