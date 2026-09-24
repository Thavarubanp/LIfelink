import React, { useState } from 'react';
import { X, Loader2, Send, AlertCircle, MessageSquare, Paperclip } from 'lucide-react';
import { getApiErrorMessage } from '../../utils/errorUtils';

const MAX_ATTACHMENT_BYTES = 2 * 1024 * 1024; // 2 MB, enforced by the backend too

/**
 * Shared complaint reply modal (admin and complaint creator): message + optional file attachment.
 * `onSubmit({ notes, attachmentUrl, attachmentName })` performs the API call; errors are shown inline.
 */
export const ComplaintReplyModal = ({ complaint, onSubmit, onClose }) => {
  const [notes, setNotes] = useState('');
  const [attachment, setAttachment] = useState(null); // { url, name }
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');

  const handleFile = (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (file.size > MAX_ATTACHMENT_BYTES) {
      setError('Attachment cannot exceed 2 MB.');
      e.target.value = '';
      return;
    }
    const reader = new FileReader();
    reader.onload = () => setAttachment({ url: reader.result, name: file.name });
    reader.readAsDataURL(file);
    setError('');
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!notes.trim()) {
      setError('A reply message is required.');
      return;
    }
    setSending(true);
    setError('');
    try {
      await onSubmit({ notes: notes.trim(), attachmentUrl: attachment?.url, attachmentName: attachment?.name });
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
      <form
        onSubmit={handleSubmit}
        className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-4"
      >
        <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
          <div className="flex items-center gap-2">
            <MessageSquare className="w-5 h-5 text-blue-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Reply to Complaint</h3>
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
            <X className="w-4 h-4" />
          </button>
        </div>

        <p className="text-xs text-slate-500 dark:text-slate-400">
          Subject: <span className="font-semibold text-slate-900 dark:text-slate-100">{complaint?.subject}</span>
        </p>

        {error && (
          <div className="p-3 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 rounded-xl flex items-start gap-2 text-xs text-red-700 dark:text-red-300">
            <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          maxLength={1000}
          rows={4}
          placeholder="Write your reply..."
          className="w-full px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 text-xs bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/40"
        />

        <label className="flex items-center gap-2 px-3 py-2 rounded-xl border border-dashed border-slate-300 dark:border-slate-700 text-xs text-slate-600 dark:text-slate-300 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800">
          <Paperclip className="w-3.5 h-3.5" />
          <span className="truncate">{attachment ? attachment.name : 'Attach a file (optional, max 2 MB)'}</span>
          <input type="file" className="hidden" onChange={handleFile} />
        </label>

        <div className="flex items-center justify-end gap-2 pt-3 border-t border-slate-100 dark:border-slate-800">
          <button
            type="button"
            onClick={onClose}
            disabled={sending}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={sending}
            className="px-4 py-2 rounded-xl text-xs font-semibold bg-blue-600 hover:bg-blue-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
          >
            {sending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
            {sending ? 'Sending...' : 'Send Reply'}
          </button>
        </div>
      </form>
    </div>
  );
};

export default ComplaintReplyModal;
