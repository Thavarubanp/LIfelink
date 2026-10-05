const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

const sriLankaDateFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Colombo',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric'
});

/**
 * Formats a value for user-visible web UI as DD/MM/YYYY.
 * Date-only API values are reordered without constructing a Date, so timezone
 * conversion cannot move them to a different calendar day.
 */
export const formatDisplayDate = (value, fallback = '—') => {
  if (value === null || value === undefined || value === '') return fallback;

  if (typeof value === 'string') {
    const dateOnly = value.match(DATE_ONLY_PATTERN);
    if (dateOnly) return `${dateOnly[3]}/${dateOnly[2]}/${dateOnly[1]}`;
  }

  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? fallback : sriLankaDateFormatter.format(date);
};

export default formatDisplayDate;
