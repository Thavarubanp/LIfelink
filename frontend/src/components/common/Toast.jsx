import React from 'react';
import { useNotification } from '../../context/NotificationContext';
import { CheckCircle2, AlertTriangle, AlertCircle, Info, X } from 'lucide-react';

export const Toast = () => {
  const { toasts, removeToast } = useNotification();

  if (toasts.length === 0) return null;

  const getIcon = (type) => {
    switch (type) {
      case 'success': return <CheckCircle2 className="w-5 h-5 text-emerald-500" />;
      case 'warning': return <AlertTriangle className="w-5 h-5 text-amber-500" />;
      case 'danger':
      case 'error': return <AlertCircle className="w-5 h-5 text-red-500" />;
      default: return <Info className="w-5 h-5 text-blue-500" />;
    }
  };

  return (
    <div className="fixed bottom-5 right-5 z-50 flex flex-col gap-3 max-w-sm w-full pointer-events-none">
      {toasts.map((t) => (
        <div
          key={t.id}
          className="pointer-events-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-xl flex items-start gap-3 transform transition-all duration-300 animate-slide-in"
        >
          <div className="shrink-0 mt-0.5">{getIcon(t.type)}</div>
          <div className="flex-1">
            {t.title && <h5 className="text-sm font-semibold text-slate-900 dark:text-slate-100">{t.title}</h5>}
            <p className="text-xs text-slate-600 dark:text-slate-300 mt-0.5 leading-relaxed">{t.message}</p>
            {t.actionLabel && (
              <button
                onClick={() => {
                  if (t.onAction) t.onAction();
                  removeToast(t.id);
                }}
                className="mt-2 text-xs font-semibold text-red-600 dark:text-red-400 hover:underline inline-block"
              >
                {t.actionLabel}
              </button>
            )}
          </div>
          <button
            onClick={() => removeToast(t.id)}
            className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 p-1 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      ))}
    </div>
  );
};

export default Toast;
