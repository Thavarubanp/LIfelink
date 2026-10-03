import { useState } from 'react';
import { Loader2, Megaphone, Send } from 'lucide-react';
import { activityApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const inputClass =
  'mt-1 w-full rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 px-2.5 py-1.5 text-xs text-slate-800 dark:text-slate-100 focus:outline-none focus:border-red-500';

/**
 * Admin only: a one-way "Message from Administrator" to ONE user ({ userId }) or ONE hospital ({ hospitalId }).
 * It arrives as an in-app notification; the recipient cannot reply (they file a complaint to contact the admin).
 */
export const AdminMessageButton = ({ userId, hospitalId, recipientName }) => {
  const [open, setOpen] = useState(false);
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const { addToast } = useNotification();

  const close = () => {
    setOpen(false);
    setSubject('');
    setMessage('');
    setError('');
  };

  const send = async () => {
    setSending(true);
    setError('');
    try {
      await activityApi.sendMessage({ userId, hospitalId, subject: subject.trim(), message: message.trim() });
      addToast({ title: 'Message sent', message: `${recipientName || 'The recipient'} will see it in their notifications.`, type: 'success' });
      close();
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setSending(false);
    }
  };

  const valid = subject.trim().length >= 3 && message.trim().length >= 5;

  return (
    <section className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-3">
      <div>
        <h2 className="text-sm font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <Megaphone className="w-4 h-4 text-red-600" /> Message from Administrator
        </h2>
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
          A one-way message shown in their notifications. They cannot reply; to contact you they file a complaint.
        </p>
      </div>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="inline-flex items-center justify-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-semibold bg-red-600 text-white hover:bg-red-700"
      >
        <Send className="w-3.5 h-3.5" /> Send message
      </button>

      {open && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4" role="dialog" aria-modal="true" aria-label="Send message">
          <div className="w-full max-w-md rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 p-5 shadow-xl space-y-3">
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Message to {recipientName || 'recipient'}</h3>
            <label className="block text-[11px] font-semibold text-slate-600 dark:text-slate-300">
              Subject
              <input value={subject} maxLength={120} onChange={(e) => setSubject(e.target.value)} className={inputClass} />
            </label>
            <label className="block text-[11px] font-semibold text-slate-600 dark:text-slate-300">
              Message
              <textarea value={message} maxLength={2000} rows={5} onChange={(e) => setMessage(e.target.value)} className={inputClass} />
            </label>
            {error && <p className="text-xs text-rose-600 dark:text-rose-400">{error}</p>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={close} disabled={sending}
                className="px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-800">
                Cancel
              </button>
              <button type="button" onClick={send} disabled={sending || !valid}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50">
                {sending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />} Send
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
};

export default AdminMessageButton;
