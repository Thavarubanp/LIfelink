import { Plus, Trash2 } from 'lucide-react';
import { DONT_REMEMBER, partApplies } from './screeningValues';

const today = () => new Date().toLocaleDateString('en-CA');
const inputClass =
  'w-full px-2.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-red-500/40';
const pill = (active) =>
  `px-3 py-1 rounded-lg text-xs font-semibold border transition-colors ${
    active
      ? 'bg-red-600 text-white border-red-600'
      : 'bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200 border-slate-200 dark:border-slate-700 hover:border-red-400'
  }`;

const DateInput = ({ part, value, onChange, disabled }) => {
  const unknown = value === DONT_REMEMBER;
  return (
    <div className="flex flex-wrap items-center gap-2">
      <input type="date" max={today()} value={unknown ? '' : value || ''} disabled={disabled || unknown}
        onChange={(e) => onChange(e.target.value)} aria-label={part.label} className={`${inputClass} max-w-[11rem]`} />
      {part.allow_unknown && (
        <label className="inline-flex items-center gap-1.5 text-[11px] text-slate-600 dark:text-slate-300 cursor-pointer">
          <input type="checkbox" className="accent-red-600" checked={unknown} disabled={disabled}
            onChange={(e) => onChange(e.target.checked ? DONT_REMEMBER : '')} />
          I don't remember
        </label>
      )}
    </div>
  );
};

const PartInput = ({ part, value, onChange, disabled }) => {
  switch (part.type) {
    case 'number':
      return <input type="number" min={part.min ?? undefined} max={part.max ?? undefined} step="0.1" value={value ?? ''} disabled={disabled}
        onChange={(e) => onChange(e.target.value)} aria-label={part.label} className={`${inputClass} max-w-[8rem]`} />;
    case 'date':
      return <DateInput part={part} value={value} onChange={onChange} disabled={disabled} />;
    case 'select':
    case 'yes_no':
      return (
        <div className="flex flex-wrap gap-1.5" role="radiogroup" aria-label={part.label}>
          {(part.options || []).map((o) => (
            <button key={o} type="button" role="radio" aria-checked={value === o} disabled={disabled} onClick={() => onChange(o)} className={pill(value === o)}>{o}</button>
          ))}
        </div>
      );
    case 'checklist': {
      const list = Array.isArray(value) ? value : [];
      const none = Array.isArray(value) && value.length === 0;
      return (
        <div className="space-y-1.5">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5">
            {(part.options || []).map((o) => (
              <label key={o} className="flex items-center gap-2 px-2.5 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs cursor-pointer hover:border-red-300">
                <input type="checkbox" className="accent-red-600" disabled={disabled} checked={list.includes(o)}
                  onChange={() => onChange(list.includes(o) ? list.filter((x) => x !== o) : [...list, o])} />
                <span className="text-slate-700 dark:text-slate-200">{o}</span>
              </label>
            ))}
          </div>
          {part.allow_none && (
            <button type="button" disabled={disabled} onClick={() => onChange(none ? null : [])} className={pill(none)}>None of these</button>
          )}
        </div>
      );
    }
    case 'trips': {
      const trips = Array.isArray(value) && value.length ? value : [{ country: '', return_date: '' }];
      const set = (i, key, v) => onChange(trips.map((t, j) => (j === i ? { ...t, [key]: v } : t)));
      return (
        <div className="space-y-1.5">
          {trips.map((t, i) => (
            <div key={i} className="flex flex-wrap items-center gap-2">
              <input value={t.country} placeholder="Country" disabled={disabled} maxLength={80} aria-label="Country"
                onChange={(e) => set(i, 'country', e.target.value)} className={`${inputClass} max-w-[11rem]`} />
              <DateInput part={{ label: 'Return date', allow_unknown: true }} value={t.return_date} disabled={disabled} onChange={(v) => set(i, 'return_date', v)} />
              {trips.length > 1 && (
                <button type="button" aria-label="Remove trip" disabled={disabled} onClick={() => onChange(trips.filter((_, j) => j !== i))}
                  className="p-1 text-slate-400 hover:text-rose-600"><Trash2 className="w-3.5 h-3.5" /></button>
              )}
            </div>
          ))}
          <button type="button" disabled={disabled} onClick={() => onChange([...trips, { country: '', return_date: '' }])}
            className="inline-flex items-center gap-1 text-[11px] font-semibold text-red-600 hover:underline"><Plus className="w-3 h-3" /> Add another country</button>
        </div>
      );
    }
    case 'confirm':
      return (
        <label className="inline-flex items-center gap-2 text-xs font-semibold text-slate-800 dark:text-slate-100 cursor-pointer">
          <input type="checkbox" className="accent-red-600 w-4 h-4" disabled={disabled} checked={value === 'Yes'} onChange={(e) => onChange(e.target.checked ? 'Yes' : '')} />
          {part.label}
        </label>
      );
    default:
      return <input value={value ?? ''} disabled={disabled} maxLength={200} onChange={(e) => onChange(e.target.value)} aria-label={part.label} className={inputClass} />;
  }
};

/**
 * The inputs of one screening question (7.8): tick boxes, options, dates, numbers, "None of these" and "I don't
 * remember". Used inside the chat bubble and, for all 7 questions, by the edit form. Parts that do not apply (a
 * follow-up whose trigger is not answered, or the pregnancy part for men) are hidden. No glossary or help texts.
 */
export const ScreeningParts = ({ parts, values, onChange, disabled = false, missing = [] }) => (
  <div className="space-y-2.5">
    {parts.filter((p) => partApplies(p, values)).map((p) => (
      <div key={p.id} className="space-y-1">
        {p.type !== 'confirm' && (
          <div className={`text-[11px] font-semibold ${missing.includes(p.id) ? 'text-red-700 dark:text-red-300' : 'text-slate-600 dark:text-slate-300'}`}>
            {p.label}{p.required === false ? ' (optional)' : ''}
          </div>
        )}
        <PartInput part={p} value={values[p.id]} disabled={disabled} onChange={(v) => onChange(p.id, v)} />
      </div>
    ))}
  </div>
);

export default ScreeningParts;
