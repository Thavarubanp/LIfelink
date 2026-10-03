import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, CheckCircle2, Loader2, Lock, PauseCircle, Send, Stethoscope } from 'lucide-react';
import { assistantApi } from '../../api';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { ChatThread } from '../../components/assistant/ChatThread';
import { ScreeningParts } from '../../components/screening/ScreeningParts';
import { collectAnswers, partApplies, summarise, toInputValue } from '../../components/screening/screeningValues';

const initialValues = (question) => {
  const values = {};
  (question?.parts || []).forEach((p) => {
    values[p.id] = toInputValue(p, p.default);
  });
  return values;
};

/** The current question's inputs, shown inside its chat bubble. Free text below the thread still works. */
const QuestionInputs = ({ question, thinking, onSend }) => {
  const [values, setValues] = useState(() => initialValues(question));
  const parts = question.parts || [];
  const answers = collectAnswers(parts, values);
  const ready = Object.keys(answers).length > 0;
  return (
    <div className="pt-2 border-t border-slate-200 dark:border-slate-700 space-y-2.5">
      {question.follow_up && <p className="text-[11px] font-semibold text-red-700 dark:text-red-300">Just the highlighted part, please:</p>}
      <ScreeningParts parts={parts} values={values} missing={question.missing || []} disabled={thinking}
        onChange={(id, v) => setValues((prev) => ({ ...prev, [id]: v }))} />
      <button type="button" disabled={thinking || !ready}
        onClick={() => onSend(answers, summarise(parts.filter((p) => partApplies(p, values)), values))}
        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50">
        <Send className="w-3.5 h-3.5" /> {question.type === 'confirm' ? 'Send my answers to the doctor' : 'Send answer'}
      </button>
    </div>
  );
};

/**
 * Donor screening interview with the Request Management agent (through the Supervisor): 7 questions and a final
 * "I confirm my answers are true" tick. Each question shows its inputs inside the chat bubble (7.8); the donor can also
 * type, and can ask what anything means (explained in simple words, then the question continues).
 */
export const ScreeningInterviewPage = () => {
  const { id } = useParams();
  const [messages, setMessages] = useState([]);
  const [screening, setScreening] = useState(null);
  const [input, setInput] = useState('');
  const [thinking, setThinking] = useState(true);
  const [error, setError] = useState('');

  const question = screening?.question;

  const apply = useCallback((reply) => {
    setMessages((prev) => [...prev, { role: 'assistant', text: reply.reply, segments: reply.segments }]);
    if (reply.screening) setScreening(reply.screening);
  }, []);

  useEffect(() => {
    let active = true;
    assistantApi.chat({ acceptanceId: id, message: '' })
      .then((reply) => {
        if (!active) return;
        const transcript = (reply.screening?.transcript || []).flatMap((t) => [
          { role: 'assistant', segments: [{ type: 'screening', text: t.question }] },
          { role: 'user', text: t.answer }
        ]);
        setMessages(transcript);
        apply(reply);
      })
      .catch((err) => active && setError(getApiErrorMessage(err)))
      .finally(() => active && setThinking(false));
    return () => {
      active = false;
    };
  }, [id, apply]);

  const send = async (text, structured = null) => {
    const message = String(text ?? '').trim();
    if ((!message && !structured) || thinking) return;
    setMessages((prev) => [...prev, { role: 'user', text: message || 'Answered' }]);
    setInput('');
    setThinking(true);
    try {
      apply(await assistantApi.chat({ acceptanceId: id, message: structured ? '' : message, structured }));
    } catch (err) {
      setMessages((prev) => [...prev, { role: 'assistant', text: getApiErrorMessage(err) }]);
    } finally {
      setThinking(false);
    }
  };

  const finished = screening?.isComplete || screening?.status === 'Submitted';
  const closed = screening?.status === 'Closed';
  const paused = screening?.status === 'Paused';
  const count = screening?.sectionCount || 7;
  const percent = screening?.total ? Math.round((screening.answered / screening.total) * 100) : 0;
  const stepLabel = finished
    ? 'All questions answered'
    : question?.type === 'confirm'
      ? `All ${count} questions answered - please confirm`
      : `Question ${screening?.sectionIndex || 1} of ${count}${screening?.section ? `: ${screening.section}` : ''}`;

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
              {count} short questions. Answer honestly and ask about anything you're unsure of. Only the reviewing doctor sees your answers, and the doctor makes the decision.
            </p>
          </div>
          {question?.confidential && (
            <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-[10px] font-bold bg-slate-100 dark:bg-slate-900 text-slate-700 dark:text-white border border-slate-200 dark:border-slate-700 shrink-0">
              <Lock className="w-3 h-3" /> Confidential
            </span>
          )}
        </div>
        {screening && !closed && !paused && (
          <div className="space-y-1">
            <div className="flex justify-between text-[11px] text-slate-500">
              <span>{stepLabel}</span>
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
          <div className="flex-1 p-4 overflow-y-auto" style={{ maxHeight: '65vh' }}>
            <ChatThread
              messages={messages}
              thinking={thinking}
              lastAssistantExtra={question && !finished && !closed && !paused ? (
                <QuestionInputs key={`${question.question_id}-${question.missing?.join(',')}-${messages.length}`} question={question} thinking={thinking}
                  onSend={(answers, summary) => send(summary, answers)} />
              ) : null}
            />
          </div>

          {finished ? (
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3 bg-emerald-50/60 dark:bg-emerald-950/20 rounded-b-2xl">
              <div className="flex items-center gap-2 text-xs text-emerald-800 dark:text-emerald-300">
                <CheckCircle2 className="w-4 h-4" /> Your answers have been sent to the doctor. Remember to bring your NIC when you donate.
              </div>
              <Link to="/donor/acceptances" className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-600 text-white hover:bg-emerald-700 shrink-0">View status</Link>
            </div>
          ) : closed ? (
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 text-xs text-slate-500">Screening is closed for this donation.</div>
          ) : paused ? (
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 text-xs text-rose-700 dark:text-rose-300 flex items-center gap-2">
              <PauseCircle className="w-4 h-4" /> Screening is paused while the administrator has this request suspended. Your answers are saved.
            </div>
          ) : question ? (
            <form onSubmit={(e) => { e.preventDefault(); send(input); }} className="p-3 border-t border-slate-100 dark:border-slate-800 flex items-center gap-2">
              <input
                value={input}
                onChange={(e) => setInput(e.target.value)}
                maxLength={1000}
                placeholder="Or type your answer, or ask what something means..."
                className="flex-1 px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-red-500/40"
              />
              <button type="submit" disabled={thinking || !input.trim()} aria-label="Send"
                className="p-2 rounded-xl bg-red-600 hover:bg-red-700 text-white disabled:opacity-50">
                {thinking ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
              </button>
            </form>
          ) : screening && (
            // Every answer is saved and confirmed but the report did not reach the doctor (the service was unavailable): retry
            <div className="p-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-between gap-3">
              <span className="text-xs text-slate-500">All your answers are saved. They have not reached the doctor yet.</span>
              <button type="button" disabled={thinking} onClick={() => send('Submit my answers')}
                className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50 shrink-0">
                Send to the doctor
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default ScreeningInterviewPage;
