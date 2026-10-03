import { useState } from 'react';
import { Loader2, Lock, Save, X } from 'lucide-react';
import { acceptanceApi } from '../../api';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { ScreeningParts } from './ScreeningParts';
import { CONFIRM_ID, collectAnswers, toInputValue } from './screeningValues';

const fmt = (value) => (value ? new Date(value).toLocaleString('en-GB', { timeZone: 'Asia/Colombo', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '');

const initialValues = (questionnaire, answers) => {
  const values = {};
  (questionnaire || []).forEach((q) => q.parts.forEach((p) => {
    values[p.id] = toInputValue(p, answers?.[p.id]);
  }));
  return values;
};

/**
 * The donor's screening answers (7.2 / 7.3). mode "view": everything the donor submitted in the latest version,
 * read-only. mode "edit": a normal form with the same inputs as the chat, prefilled with the submitted answers; saving
 * sends a new version to the doctor in the background (the chat interview does not run again).
 * data: GET /api/Acceptances/{id}/screening-answers.
 */
export const ScreeningAnswersForm = ({ data, mode, onClose, onSaved }) => {
  const editing = mode === 'edit' && data.canEdit && Array.isArray(data.questionnaire);
  const [values, setValues] = useState(() => initialValues(data.questionnaire, data.answers));
  const [confirmed, setConfirmed] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const allParts = (data.questionnaire || []).flatMap((q) => q.parts);

  const save = async () => {
    setSaving(true);
    setError('');
    try {
      await acceptanceApi.updateScreeningAnswers(data.acceptanceId, { ...collectAnswers(allParts, values), [CONFIRM_ID]: confirmed ? 'Yes' : '' });
      onSaved?.();
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-slate-950/60 backdrop-blur-sm flex items-center justify-center p-3 sm:p-4" role="dialog" aria-modal="true"
      aria-label={editing ? 'Update my answers' : 'My screening answers'}>
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl w-full max-w-2xl max-h-[92vh] flex flex-col shadow-2xl">
        <div className="flex items-start justify-between gap-3 px-5 py-3 border-b border-slate-100 dark:border-slate-800">
          <div>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">{editing ? 'Update my answers' : 'My screening answers'}</h3>
            <p className="text-[11px] text-slate-500 dark:text-slate-400">
              Version {data.reportVersion}, sent {fmt(data.submittedAt)}
              {editing ? ' - change what you need and save; the doctor gets a new version.' : ''}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="p-1.5 rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"><X className="w-4 h-4" /></button>
        </div>

        <div className="flex-1 overflow-y-auto p-5 space-y-3 text-xs">
          {editing ? (
            data.questionnaire.map((q) => (
              <section key={q.question_id} className={`rounded-xl border p-3 space-y-2 ${q.confidential ? 'border-slate-700 dark:border-slate-400' : 'border-slate-200 dark:border-slate-700'}`}>
                <h4 className="font-bold text-slate-800 dark:text-slate-100 flex items-center gap-1.5">
                  {q.confidential && <Lock className="w-3.5 h-3.5" />} {q.number}. {q.title}
                </h4>
                <p className="text-[11px] text-slate-500 dark:text-slate-400">{q.text}</p>
                <ScreeningParts parts={q.parts} values={values} disabled={saving} onChange={(id, v) => setValues((prev) => ({ ...prev, [id]: v }))} />
              </section>
            ))
          ) : data.sections.length === 0 ? (
            <p className="text-slate-500">There are no answers to show for this donation.</p>
          ) : (
            data.sections.map((s) => (
              <section key={s.index} className={`rounded-xl border p-3 ${s.confidential ? 'border-slate-700 dark:border-slate-400' : 'border-slate-200 dark:border-slate-700'}`}>
                <h4 className="font-bold text-slate-800 dark:text-slate-100 mb-2 flex items-center gap-1.5">
                  {s.confidential && <Lock className="w-3.5 h-3.5" />} {s.index}. {s.title}
                </h4>
                {s.items.length === 0 ? <p className="text-slate-400">Not applicable.</p> : (
                  <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
                    {s.items.map((item, i) => (
                      <div key={i}>
                        <dt className="text-[10px] text-slate-400">{item.question}</dt>
                        <dd className="font-semibold text-slate-800 dark:text-slate-100 whitespace-pre-line">{item.answer || '-'}</dd>
                      </div>
                    ))}
                  </dl>
                )}
              </section>
            ))
          )}
          {mode === 'edit' && !editing && data.editUnavailableReason && (
            <p className="p-2.5 rounded-xl bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900 text-amber-800 dark:text-amber-300">{data.editUnavailableReason}</p>
          )}
        </div>

        {editing && (
          <div className="px-5 py-3 border-t border-slate-100 dark:border-slate-800 space-y-2">
            {error && <p className="p-2 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 text-[11px] text-rose-700 dark:text-rose-300">{error}</p>}
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <label className="inline-flex items-center gap-2 text-xs font-semibold text-slate-800 dark:text-slate-100 cursor-pointer">
                <input type="checkbox" className="accent-red-600 w-4 h-4" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
                I confirm my answers are true
              </label>
              <div className="flex gap-2">
                <button type="button" onClick={onClose} disabled={saving} className="px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700">Cancel</button>
                <button type="button" onClick={save} disabled={saving || !confirmed}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50">
                  {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />} Save and send to the doctor
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default ScreeningAnswersForm;
