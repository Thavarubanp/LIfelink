import React, { useState } from 'react';
import { X, Loader2, Save, AlertCircle, Pencil } from 'lucide-react';
import { getApiErrorMessage, getApiFieldErrors } from '../../utils/errorUtils';

/**
 * Shared own-profile edit modal used by the User, Doctor and Hospital profile pages.
 * `fields`: [{ name, label, type?, options?, placeholder? }]; `initialValues`: current profile values.
 * `onSave(values)` performs the API call; backend validation errors are shown inline.
 */
export const EditProfileModal = ({ title, fields, initialValues, onSave, onClose }) => {
  const [values, setValues] = useState(() =>
    Object.fromEntries(fields.map((f) => [f.name, initialValues?.[f.name] ?? '']))
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});

  const handleChange = (e) => {
    const { name, value } = e.target;
    setValues((prev) => ({ ...prev, [name]: value }));
    if (fieldErrors[name]) setFieldErrors((prev) => ({ ...prev, [name]: '' }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setSaving(true);
    setError('');
    setFieldErrors({});
    try {
      await onSave(values);
    } catch (err) {
      setFieldErrors(getApiFieldErrors(err));
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  const inputClass =
    'w-full px-3 py-2 rounded-xl border text-xs bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-red-500/40 ';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm px-4 animate-in fade-in">
      <form
        onSubmit={handleSubmit}
        className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-lg shadow-2xl space-y-4 max-h-[90vh] overflow-y-auto"
      >
        <div className="flex items-center justify-between pb-3 border-b border-slate-100 dark:border-slate-800">
          <div className="flex items-center gap-2">
            <Pencil className="w-4 h-4 text-red-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">{title}</h3>
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">
            <X className="w-4 h-4" />
          </button>
        </div>

        {error && (
          <div className="p-3 bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900 rounded-xl flex items-start gap-2 text-xs text-red-700 dark:text-red-300">
            <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {fields.map((f) => (
            <div key={f.name} className={f.fullWidth ? 'sm:col-span-2' : ''}>
              <label htmlFor={`edit-${f.name}`} className="block text-[11px] font-semibold text-slate-500 dark:text-slate-400 mb-1">
                {f.label}
              </label>
              {f.options ? (
                <select
                  id={`edit-${f.name}`}
                  name={f.name}
                  value={values[f.name]}
                  onChange={handleChange}
                  className={inputClass + (fieldErrors[f.name] ? 'border-red-400' : 'border-slate-200 dark:border-slate-700')}
                >
                  <option value="">Not specified</option>
                  {f.options.map((o) => (
                    <option key={o} value={o}>{o}</option>
                  ))}
                </select>
              ) : (
                <input
                  id={`edit-${f.name}`}
                  name={f.name}
                  type={f.type || 'text'}
                  value={values[f.name]}
                  onChange={handleChange}
                  placeholder={f.placeholder}
                  className={inputClass + (fieldErrors[f.name] ? 'border-red-400' : 'border-slate-200 dark:border-slate-700')}
                />
              )}
              {fieldErrors[f.name] && <p className="text-[11px] text-red-600 mt-1">{fieldErrors[f.name]}</p>}
            </div>
          ))}
        </div>

        <div className="flex items-center justify-end gap-2 pt-3 border-t border-slate-100 dark:border-slate-800">
          <button
            type="button"
            onClick={onClose}
            disabled={saving}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="px-4 py-2 rounded-xl text-xs font-semibold bg-red-600 hover:bg-red-700 text-white shadow-sm transition-colors disabled:opacity-50 flex items-center gap-1.5"
          >
            {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
            {saving ? 'Saving...' : 'Save Changes'}
          </button>
        </div>
      </form>
    </div>
  );
};

export default EditProfileModal;
