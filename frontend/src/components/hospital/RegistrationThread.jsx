import { Building2, Calendar, CheckCircle2, FilePlus2, MessageSquare, XCircle } from 'lucide-react';
import AttachmentLink from '../common/AttachmentLink';

// Tailwind needs literal class names, so each tone is spelled out
const TONES = {
  amber: 'border-amber-500/30 bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400',
  rose: 'border-rose-500/30 bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-400',
  purple: 'border-purple-500/30 bg-purple-50 dark:bg-purple-950/40 text-purple-600 dark:text-purple-400',
  cyan: 'border-cyan-500/30 bg-cyan-50 dark:bg-cyan-950/40 text-cyan-600 dark:text-cyan-400',
  emerald: 'border-emerald-500/30 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400'
};

// canHaveFile: entries that can carry a file show it or "No files uploaded"; the others show no file line
const ENTRY_TYPES = {
  Submitted: { title: 'Registration submitted', icon: FilePlus2, tone: 'amber', canHaveFile: false },
  Rejected: { title: 'Registration rejected', icon: XCircle, tone: 'rose', canHaveFile: true },
  AdminComment: { title: 'Admin comment', icon: MessageSquare, tone: 'purple', canHaveFile: true },
  HospitalReply: { title: 'Hospital reply', icon: Building2, tone: 'cyan', canHaveFile: true },
  Approved: { title: 'Registration approved', icon: CheckCircle2, tone: 'emerald', canHaveFile: false }
};

/**
 * A hospital registration's conversation, oldest first. The admin and the hospital see the same entries;
 * the admin's copy also names which admin acted (`adminName`).
 * `entries`: [{ id, type, fromAdmin, timestamp, adminName, message, changedFields, attachmentName, attachmentUrl }]
 */
export const RegistrationThread = ({ entries, onPreview }) => {
  if (!entries || entries.length === 0) {
    return <p className="text-xs text-slate-400">No registration activity yet.</p>;
  }

  return (
    <div className="relative pl-6 space-y-4 before:absolute before:left-3 before:top-2 before:bottom-2 before:w-0.5 before:bg-slate-200 dark:before:bg-slate-800">
      {entries.map((entry) => {
        const meta = ENTRY_TYPES[entry.type] || { title: entry.type, icon: MessageSquare, tone: 'purple', canHaveFile: true };
        const Icon = meta.icon;
        const changes = entry.changedFields ? entry.changedFields.split('\n').filter(Boolean) : [];
        const title = entry.type === 'HospitalReply' && changes.length > 0 ? 'Hospital correction' : meta.title;
        const author = entry.fromAdmin ? (entry.adminName ? `LifeLink Admin (${entry.adminName})` : 'LifeLink Admin') : 'Hospital';

        return (
          <div key={entry.id} className="relative">
            <div className={`absolute -left-6 top-1 w-6 h-6 rounded-full border flex items-center justify-center shadow-sm ${TONES[meta.tone]}`}>
              <Icon className="w-3.5 h-3.5" />
            </div>
            <div className="bg-slate-50 dark:bg-slate-800/60 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/60 shadow-sm space-y-2">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-xs font-bold text-slate-900 dark:text-slate-100">{title}</span>
                  <span
                    className={`px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                      entry.fromAdmin
                        ? 'bg-purple-100 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border-purple-200 dark:border-purple-900'
                        : 'bg-cyan-100 text-cyan-700 dark:bg-cyan-950/60 dark:text-cyan-300 border-cyan-200 dark:border-cyan-900'
                    }`}
                  >
                    {author}
                  </span>
                </div>
                <span className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1 font-mono">
                  <Calendar className="w-3 h-3 text-slate-400" />
                  {new Date(entry.timestamp).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' })}
                </span>
              </div>

              {entry.message && (
                <p className="text-xs text-slate-600 dark:text-slate-300 font-medium bg-white dark:bg-slate-900/60 p-2.5 rounded-lg border border-slate-100 dark:border-slate-800 whitespace-pre-line">
                  {entry.message}
                </p>
              )}

              {changes.length > 0 && (
                <div className="text-[11px] text-slate-600 dark:text-slate-300">
                  <span className="font-semibold">Corrected details</span>
                  <ul className="mt-1 space-y-0.5 list-disc pl-4">
                    {changes.map((change) => (
                      <li key={change} className="break-words">{change.replace(' -> ', ' → ')}</li>
                    ))}
                  </ul>
                </div>
              )}

              {meta.canHaveFile && <AttachmentLink url={entry.attachmentUrl} name={entry.attachmentName} onPreview={onPreview} />}
            </div>
          </div>
        );
      })}
    </div>
  );
};

export default RegistrationThread;
