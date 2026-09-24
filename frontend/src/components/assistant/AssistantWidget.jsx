import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { MessageCircle, X, Send, Sparkles, Trash2 } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { assistantApi } from '../../api';
import { getUserRoles } from '../../utils/roleUtils';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { ChatThread } from './ChatThread';

const SUGGESTIONS = {
  Admin: ['What needs my attention?', 'How does hospital approval work?'],
  HospitalStaff: ['What needs my attention?', 'Which packets expire soon?', 'How do I offer blood to another hospital?'],
  Doctor: ['What needs my attention?', 'How does donor screening review work?'],
  User: ['Can I donate after a tattoo?', 'Who can receive O+ blood?', 'What is my donation status?']
};

const storageKey = (userId) => `lifelink_assistant_${userId}`;

const readHistory = (userId) => {
  try {
    const raw = sessionStorage.getItem(storageKey(userId));
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
};

/**
 * Floating LifeLink assistant available on every signed-in page. It answers from medical guidance, the
 * LifeLink guide and the user's own data only; suggested actions open pages, the assistant never acts.
 */
export const AssistantWidget = () => {
  const { user } = useAuth();
  const navigate = useNavigate();
  const roles = getUserRoles(user);
  const role = roles.includes('Admin') ? 'Admin' : roles.includes('HospitalStaff') ? 'HospitalStaff' : roles.includes('Doctor') ? 'Doctor' : 'User';

  const [open, setOpen] = useState(false);
  const [messages, setMessages] = useState(() => readHistory(user?.userId));
  const [input, setInput] = useState('');
  const [thinking, setThinking] = useState(false);

  useEffect(() => {
    try {
      sessionStorage.setItem(storageKey(user?.userId), JSON.stringify(messages.slice(-30)));
    } catch {
      // Storage unavailable: the conversation simply is not kept across reloads
    }
  }, [messages, user?.userId]);

  if (!user || user.isSuspended) return null;

  const send = async (text) => {
    const message = text.trim();
    if (!message || thinking) return;
    const history = messages.slice(-10).map((m) => ({ role: m.role, content: m.text || (m.segments || []).map((s) => s.text).join('\n') }));
    setMessages((prev) => [...prev, { role: 'user', text: message }]);
    setInput('');
    setThinking(true);
    try {
      const reply = await assistantApi.chat({ message, history });
      setMessages((prev) => [...prev, { role: 'assistant', text: reply.reply, segments: reply.segments, actions: reply.actions }]);
    } catch (err) {
      setMessages((prev) => [...prev, { role: 'assistant', text: getApiErrorMessage(err) }]);
    } finally {
      setThinking(false);
    }
  };

  const handleAction = (action) => {
    setOpen(false);
    navigate(action.route);
  };

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Open LifeLink assistant"
        className="fixed bottom-5 right-5 z-40 w-14 h-14 rounded-full bg-red-600 hover:bg-red-700 text-white shadow-lg shadow-red-600/30 flex items-center justify-center transition-all"
      >
        <MessageCircle className="w-6 h-6" />
      </button>
    );
  }

  return (
    <div className="fixed z-40 inset-0 sm:inset-auto sm:bottom-5 sm:right-5 sm:w-[380px] sm:h-[560px] bg-white dark:bg-slate-900 sm:border border-slate-200 dark:border-slate-800 sm:rounded-2xl shadow-2xl flex flex-col">
      <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100 dark:border-slate-800">
        <div className="flex items-center gap-2">
          <div className="w-8 h-8 rounded-full bg-red-600 text-white flex items-center justify-center">
            <Sparkles className="w-4 h-4" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">LifeLink Assistant</h3>
            <p className="text-[10px] text-slate-400">Guidance only. Doctors make all medical decisions.</p>
          </div>
        </div>
        <div className="flex items-center gap-1">
          {messages.length > 0 && (
            <button type="button" onClick={() => setMessages([])} aria-label="Clear conversation" className="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800">
              <Trash2 className="w-4 h-4" />
            </button>
          )}
          <button type="button" onClick={() => setOpen(false)} aria-label="Close assistant" className="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800">
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-4">
        {messages.length === 0 ? (
          <div className="space-y-3">
            <p className="text-xs text-slate-600 dark:text-slate-300">
              Hi {user.firstName || 'there'}! Ask about blood donation, how LifeLink works, or your own requests and tasks.
            </p>
            <div className="flex flex-col gap-1.5">
              {(SUGGESTIONS[role] || SUGGESTIONS.User).map((s) => (
                <button
                  key={s}
                  type="button"
                  onClick={() => send(s)}
                  className="text-left px-3 py-2 rounded-xl text-xs bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-red-300 dark:hover:border-red-800 transition-colors"
                >
                  {s}
                </button>
              ))}
            </div>
            <p className="text-[10px] text-slate-400 leading-snug">
              Messages are processed by Google Gemini. The assistant only sees your own information.
            </p>
          </div>
        ) : (
          <ChatThread messages={messages} thinking={thinking} onAction={handleAction} />
        )}
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          send(input);
        }}
        className="p-3 border-t border-slate-100 dark:border-slate-800 flex items-center gap-2"
      >
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          maxLength={2000}
          placeholder="Type your question..."
          className="flex-1 px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-red-500/40"
        />
        <button
          type="submit"
          disabled={thinking || !input.trim()}
          aria-label="Send"
          className="p-2 rounded-xl bg-red-600 hover:bg-red-700 text-white disabled:opacity-50 transition-colors"
        >
          <Send className="w-4 h-4" />
        </button>
      </form>
    </div>
  );
};

export default AssistantWidget;
