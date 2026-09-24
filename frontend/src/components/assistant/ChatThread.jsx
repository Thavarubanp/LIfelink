import React, { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bot, User, BookOpen, Compass, UserCircle, Stethoscope, ExternalLink, ArrowRight, Loader2 } from 'lucide-react';

// Where each part of an answer came from (medical guidance and LifeLink guidance are kept apart)
const SEGMENT_STYLES = {
  medical: { label: 'Medical guidance', icon: BookOpen, badge: 'bg-blue-100 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border-blue-200 dark:border-blue-900' },
  platform: { label: 'LifeLink guide', icon: Compass, badge: 'bg-purple-100 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border-purple-200 dark:border-purple-900' },
  account: { label: 'Your account', icon: UserCircle, badge: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900' },
  screening: { label: 'Screening', icon: Stethoscope, badge: 'bg-red-100 text-red-700 dark:bg-red-950/60 dark:text-red-300 border-red-200 dark:border-red-900' }
};

const Segment = ({ segment }) => {
  const style = SEGMENT_STYLES[segment.type] || SEGMENT_STYLES.platform;
  const Icon = style.icon;
  return (
    <div className="space-y-1.5">
      <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold border ${style.badge}`}>
        <Icon className="w-3 h-3" /> {style.label}
      </span>
      <p className="text-xs text-slate-700 dark:text-slate-200 whitespace-pre-line leading-relaxed">{segment.text}</p>
      {segment.sources?.length > 0 && (
        <ol className="space-y-0.5 pl-4 list-decimal text-[10px] text-slate-500 dark:text-slate-400">
          {segment.sources.map((s, i) => (
            <li key={i}>
              {s.url ? (
                <a href={s.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 hover:text-blue-600 dark:hover:text-blue-300">
                  {s.title}{s.publisher ? ` - ${s.publisher}` : ''} <ExternalLink className="w-2.5 h-2.5" />
                </a>
              ) : (
                <span>{s.title}</span>
              )}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
};

/**
 * Conversation thread shared by the assistant panel and the screening interview page, styled like the
 * complaint and appeal threads. messages: [{ role: 'user' | 'assistant', text?, segments?, actions? }]
 */
export const ChatThread = ({ messages, thinking = false, onAction, className = '' }) => {
  const navigate = useNavigate();
  const endRef = useRef(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, thinking]);

  return (
    <div className={`space-y-3 ${className}`}>
      {messages.map((m, index) => (
        <div key={index} className={`flex gap-2 ${m.role === 'user' ? 'justify-end' : 'justify-start'}`}>
          {m.role !== 'user' && (
            <div className="w-7 h-7 shrink-0 rounded-full border border-red-500/30 bg-red-50 dark:bg-red-950/40 text-red-600 dark:text-red-400 flex items-center justify-center">
              <Bot className="w-3.5 h-3.5" />
            </div>
          )}
          <div
            className={`max-w-[85%] rounded-xl p-3 border shadow-sm space-y-3 ${
              m.role === 'user'
                ? 'bg-red-600 text-white border-red-700'
                : 'bg-slate-50 dark:bg-slate-800/60 border-slate-200/80 dark:border-slate-700/60'
            }`}
          >
            {m.role === 'user' ? (
              <p className="text-xs whitespace-pre-line">{m.text}</p>
            ) : m.segments?.length ? (
              m.segments.map((s, i) => <Segment key={i} segment={s} />)
            ) : (
              <p className="text-xs text-slate-700 dark:text-slate-200 whitespace-pre-line">{m.text}</p>
            )}
            {m.actions?.length > 0 && (
              <div className="flex flex-wrap gap-1.5 pt-1">
                {m.actions.map((a) => (
                  <button
                    key={a.route}
                    type="button"
                    onClick={() => (onAction ? onAction(a) : navigate(a.route))}
                    className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-red-700 dark:text-red-300 hover:bg-red-50 dark:hover:bg-slate-800 transition-colors"
                  >
                    {a.label} <ArrowRight className="w-3 h-3" />
                  </button>
                ))}
              </div>
            )}
          </div>
          {m.role === 'user' && (
            <div className="w-7 h-7 shrink-0 rounded-full border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-slate-500 flex items-center justify-center">
              <User className="w-3.5 h-3.5" />
            </div>
          )}
        </div>
      ))}
      {thinking && (
        <div className="flex items-center gap-2 text-[11px] text-slate-400 pl-9">
          <Loader2 className="w-3.5 h-3.5 animate-spin" /> Thinking...
        </div>
      )}
      <div ref={endRef} />
    </div>
  );
};

export default ChatThread;
