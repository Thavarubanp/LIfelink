export const ACCEPTANCE_FILTERS = {
  all: () => true,
  active: (acceptance) => ['Accepted', 'ScreeningPending', 'ScreeningCompleted', 'Verified'].includes(acceptance.status),
  matched: (acceptance) => acceptance.status === 'Matched',
  rejected: (acceptance) => acceptance.status === 'Rejected',
  cancelled: (acceptance) => acceptance.status === 'Cancelled'
};

export const REQUEST_FILTERS = {
  all: () => true,
  open: (request) => ['Pending', 'Verified', 'Approved'].includes(request.status),
  completed: (request) => request.status === 'Completed',
  rejected: (request) => request.status === 'Rejected',
  cancelled: (request) => request.status === 'Cancelled'
};

export const ACCEPTANCE_FILTER_LABELS = {
  all: 'All acceptances',
  active: 'Active',
  matched: 'Successful donations',
  rejected: 'Rejected by doctor',
  cancelled: 'Withdrawn / closed'
};

export const REQUEST_FILTER_LABELS = {
  all: 'All requests',
  open: 'Open',
  completed: 'Completed',
  rejected: 'Rejected',
  cancelled: 'Cancelled'
};

export const normalizeHistoryFilter = (value, filters) =>
  typeof value === 'string' && Object.hasOwn(filters, value) ? value : 'all';

export const filterHistory = (records, filters, filter) =>
  records.filter(filters[normalizeHistoryFilter(filter, filters)]);
