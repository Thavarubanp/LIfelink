import React from 'react';
import { MessageSquare, ShieldCheck, Calendar, Paperclip } from 'lucide-react';

/**
 * Appeal conversation thread (appellant <-> admin), styled like the complaint activity timeline.
 * `appeal.messages`: [{ messageId, fromAdmin, adminEmail, message, attachmentUrl, attachmentName, createdAt }]
 */
export const AppealThread = ({ appeal, appellantLabel = 'You' }) => {
  const messages = appeal?.messages || [];
  if (messages.length === 0) {
    return <p className="text-xs text-slate-400">No messages yet.</p>;
  }

  return (
    <div className="relative pl-6 space-y-4 before:absolute before:left-3 before:top-2 before:bottom-2 before:w-0.5 before:bg-slate-200 dark:before:bg-slate-800">
      {messages.map((m) => (
        <div key={m.messageId} className="relative">
          <div
            className={`absolute -left-6 top-1 w-6 h-6 rounded-full border flex items-center justify-center shadow-sm ${
              m.fromAdmin
                ? 'border-purple-500/30 bg-purple-50 dark:bg-purple-950/40 text-purple-600 dark:text-purple-400'
                : 'border-blue-500/30 bg-blue-50 dark:bg-blue-950/40 text-blue-600 dark:text-blue-400'
            }`}
          >
            {m.fromAdmin ? <ShieldCheck className="w-3.5 h-3.5" /> : <MessageSquare className="w-3.5 h-3.5" />}
          </div>
          <div className="bg-slate-50 dark:bg-slate-800/60 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/60 shadow-sm space-y-1.5">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5">
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-slate-900 dark:text-slate-100">{m.fromAdmin ? 'Admin Reply' : `${appellantLabel}`}</span>
                <span
                  className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                    m.fromAdmin
                      ? 'bg-purple-100 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border-purple-200 dark:border-purple-900'
                      : 'bg-blue-100 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border-blue-200 dark:border-blue-900'
                  }`}
                >
                  {m.fromAdmin ? 'System Admin' : 'Appellant'}
                </span>
              </div>
              <span className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1 font-mono">
                <Calendar className="w-3 h-3 text-slate-400" />
                {new Date(m.createdAt).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' })}
              </span>
            </div>
            <p className="text-xs text-slate-600 dark:text-slate-300 font-medium bg-white dark:bg-slate-900/60 p-2.5 rounded-lg border border-slate-100 dark:border-slate-800 whitespace-pre-line">
              {m.message}
            </p>
            {m.attachmentUrl && (
              <a
                href={m.attachmentUrl}
                download={m.attachmentName || 'attachment'}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-blue-700 dark:text-blue-300 hover:bg-blue-50 dark:hover:bg-slate-800 transition-colors"
              >
                <Paperclip className="w-3 h-3" /> {m.attachmentName || 'Attachment'}
              </a>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};

export default AppealThread;
