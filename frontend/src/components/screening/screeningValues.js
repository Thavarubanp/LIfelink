// Screening answer helpers shared by the chat inputs and the edit form

export const DONT_REMEMBER = "Donor doesn't remember";
export const CONFIRM_ID = 'CONFIRM_TRUE';

/** Stored answers can be JSON strings (tick lists, trips); the inputs work with arrays. */
export const toInputValue = (part, value) => {
  if (value === null || value === undefined) return part.type === 'checklist' ? null : part.type === 'trips' ? [] : '';
  if ((part.type === 'checklist' || part.type === 'trips') && typeof value === 'string') {
    try {
      return JSON.parse(value);
    } catch {
      return part.type === 'trips' ? [] : null;
    }
  }
  return value;
};

/** Whether a part applies to the current answers (follow-ups appear once their trigger is answered; pregnancy only for women). */
export const partApplies = (part, values) => {
  if (part.female_only && String(values.P_GENDER || '').toLowerCase() !== 'female') return false;
  if (!part.show_if) return true;
  const parent = values[part.show_if.field];
  if (part.show_if.equals !== undefined && part.show_if.equals !== null) return parent === part.show_if.equals;
  return Array.isArray(parent) && parent.includes(part.show_if.includes);
};

export const isAnswered = (part, value) => {
  if (part.type === 'checklist') return Array.isArray(value);
  if (part.type === 'trips') return Array.isArray(value) && value.length > 0 && value.every((t) => t.country && t.return_date);
  return value !== null && value !== undefined && String(value).trim() !== '';
};

/** A readable summary of the answers (shown as the donor's chat message). */
export const summarise = (parts, values) =>
  parts
    .filter((p) => p.type !== 'confirm' && isAnswered(p, values[p.id]))
    .map((p) => {
      const v = values[p.id];
      const text = p.type === 'checklist' ? (v.length ? v.join(', ') : 'None of these')
        : p.type === 'trips' ? v.map((t) => `${t.country} (back ${t.return_date})`).join('; ')
          : String(v);
      return `${p.label}: ${text}`;
    })
    .join('\n');

/** Values to send: only parts that apply and have an answer (tick lists as arrays, trips as a list). */
export const collectAnswers = (parts, values) => {
  const out = {};
  parts.forEach((p) => {
    if (partApplies(p, values) && isAnswered(p, values[p.id])) out[p.id] = values[p.id];
  });
  return out;
};
