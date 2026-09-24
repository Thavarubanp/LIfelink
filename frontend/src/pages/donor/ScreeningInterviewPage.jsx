import React, { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, Loader2, Lock, Send, ShieldCheck, Stethoscope } from 'lucide-react';
import { assistantApi } from '../../api';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { ChatThread } from '../../components/assistant/ChatThread';

/**
 * Donor screening interview with the Request Management agent (through the Supervisor). The agent asks the
 * 12 sections step by step, explains anything the donor asks, and sends the report to the doctor at the end.
 */
export const ScreeningInterviewPage = () => {
  const { id } = useParams();
  const [messages, setMessages] = useState([]);
  const [screening, setScreening] = useState(null);
  const [input, setInput] = useState('');
  const [checked, setChecked] = useState([]);
  const [thinking, setThinking] = useState(false);
  const [error, setError] = useState('');

  const question = screening?.question;

  const apply = useCallback((reply) => {
    setMessages((prev) => [...prev, { role: 'assistant', text: reply.reply, segments: reply.segments }]);
    if (reply.screening) setScreening(reply.screening);
    setChecked([]);
  }, []);

  useEffect(() => {
    const resume = async () => {
      setThinking(true);
      try {
        const reply = await assistantApi.chat({ acceptanceId: id, message: '' });
        const transcript = (reply.screening?.transcript || []).flatMap((t) => [
          { role: 'assistant', segments: [{ type: 'screening', text: t.question }] },
          { role: 'user', text: t.answer }
        ]);
        setMessages(transcript);
        apply(reply);
      } catch (err) {
        setError(getApiErrorMessage(err));
      } finally {
        setThinking(false);
      }
    };
    resume();
  }, [id, apply]);

  const send = async (text) => {
    const message = String(text ?? '').trim();
    if (!message || thinking) return;
    setMessages((prev) => [...prev, { role: 'user', text: message }]);
    setInput('');
    setThinking(true);
    try {
      apply(await assistantApi.chat({ acceptanceId: id, message }));
    } catch (err) {
      setMessages((prev) => [...prev, { role: 'assistant', text: getApiErrorMessage(err) }]);
    } finally {
      setThinking(false);
    }
  };

  const toggle = (option) =>
    setChecked((prev) => (prev.includes(option) ? prev.filter((o) => o !== option) : [...prev, option]));

  const finished = screening?.isComplete || screening?.status === 'Submitted';
  const closed = screening?.status === 'Closed';
  const sectionIndex = screening?.sectionIndex || 1;
  const percent = screening?.total ? Math.round((screening.answered / screening.total) * 100) : 0;

  return (
    <div className="max-w-3xl mx-auto space-y-4">
      <Link to="/donor/acceptances" className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-500 hover:text-slate-800 dark:hover:text-slate-200">
        <ArrowLeft className="w-4 h-4" /> My Acceptances
      </Link>

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-sm space-y-3">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
              <Stethoscope className="w-5 h-5 text-red-600" /> Donor Health Screening
            </h1>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Answer honestly and ask about anything you're unsure of. Only the reviewing doctor sees your report, and the doctor makes the decision.
            </p>
          </div>
          {question?.confidential && (
            <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-[10px] font-bold bg-slate-900 text-white shrink-0">
              <Lock className="w-3 h-3" /> Confidential
            </span>
          )}
        </div>
        {screening && !closed && (
          <div className="space-y-1">
            <div className="flex justify-between text-[11px] text-slate-500">
              <span>{finished ? 'All sections complete' : `Section ${sectionIndex} of ${screening.sectionCount || 12}: ${screening.section || ''}`}</span>
              <span>{finished ? 100 : percent}%</span>
            </div>
            <div className="h-2 rounded-full bg-slate-100 dark:bg-slate-800 overflow-hidden">
              <div className="h-full bg-red-600 transition-all" style={{ width: `${finished ? 100 : percent}%` }} />
            </div>
          </div>
        )}
      </div>

      {error ? (
        <div className="p-4 rounded-xl bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 text-xs text-red-700 dark:text-red-300">{error}</div>
      ) : (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl shadow-sm flex flex-col" style={{ minHeight: '55vh' }}>
          <div className="flex-1 p-4 overflow-y-auto" style={{ maxHeight: '60vh' }}>
            <ChatThread messages={messages} thinking={thinking} />
          </div>

          {finished ? (
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3 bg-emerald-50/60 dark:bg-emerald-950/20 rounded-b-2xl">
              <div className="flex items-center gap-2 text-xs text-emerald-800 dark:text-emerald-300">
                <CheckCircle2 className="w-4 h-4" /> Your report has been sent to the doctor. You will be notified of the decision.
              </div>
              <Link to="/donor/acceptances" className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-600 text-white hover:bg-emerald-700">View status</Link>
            </div>
          ) : closed ? (
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 text-xs text-slate-500">Screening is closed for this donation.</div>
          ) : question ? (
            <div className="p-3 border-t border-slate-100 dark:border-slate-800 space-y-2">
              {question.type === 'yes_no' && (
                <div className="flex gap-2">
                  {['Yes', 'No'].map((o) => (
                    <button key={o} type="button" disabled={thinking} onClick={() => send(o)}
                      className="px-4 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 hover:border-red-400 disabled:opacity-50">{o}</button>
                  ))}
                </div>
              )}
              {(question.type === 'confirm' || (question.type === 'select' && /profile says/i.test(question.text))) && (
                <button type="button" disabled={thinking} onClick={() => send('Yes')}
                  className="px-4 py-1.5 rounded-lg text-xs font-semibold border border-emerald-300 bg-emerald-50 text-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300 hover:border-emerald-500 disabled:opacity-50">
                  <ShieldCheck className="w-3.5 h-3.5 inline mr-1" /> Yes, that's correct
                </button>
              )}
              {question.type === 'select' && (
                <div className="flex flex-wrap gap-1.5">
                  {(question.options || []).map((o) => (
                    <button key={o} type="button" disabled={thinking} onClick={() => send(o)}
                      className="px-3 py-1 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 hover:border-red-400 disabled:opacity-50">{o}</button>
                  ))}
                </div>
              )}
              {question.type === 'checklist' && (
                <div className="space-y-2">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5 max-h-44 overflow-y-auto">
                    {(question.options || []).map((o) => (
                      <label key={o} className="flex items-center gap-2 px-2.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 text-xs cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800">
                        <input type="checkbox" checked={checked.includes(o)} onChange={() => toggle(o)} className="accent-red-600" />
                        <span className="text-slate-700 dark:text-slate-200">{o}</span>
                      </label>
                    ))}
                  </div>
                  <div className="flex gap-2">
                    <button type="button" disabled={thinking} onClick={() => send('None')}
                      className="px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 hover:border-red-400 disabled:opacity-50">None of these</button>
                    <button type="button" disabled={thinking || checked.length === 0} onClick={() => send(JSON.stringify(checked))}
                      className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50">Submit selected ({checked.length})</button>
                  </div>
                </div>
              )}
              {question.type === 'date' && (
                <input type="date" max={new Date().toISOString().slice(0, 10)} onChange={(e) => e.target.value && send(e.target.value)} disabled={thinking}
                  className="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs" />
              )}
              <form onSubmit={(e) => { e.preventDefault(); send(input); }} className="flex items-center gap-2">
                <input
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  maxLength={1000}
                  placeholder={question.type === 'checklist' ? 'Or type your answer, or ask what a term means...' : 'Type your answer, or ask what the question means...'}
                  className="flex-1 px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-red-500/40"
                />
                <button type="submit" disabled={thinking || !input.trim()} aria-label="Send"
                  className="p-2 rounded-xl bg-red-600 hover:bg-red-700 text-white disabled:opacity-50">
                  {thinking ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                </button>
              </form>
              {question.help && <p className="text-[10px] text-slate-400">{question.help}</p>}
            </div>
          ) : screening && (
            // Every answer is saved but the report did not reach the doctor (the service was unavailable): retry
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3">
              <span className="text-xs text-slate-500">All your answers are saved. The report has not reached the doctor yet.</span>
              <button type="button" disabled={thinking} onClick={() => send('Submit my report')}
                className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50 shrink-0">
                Submit report
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default ScreeningInterviewPage;
