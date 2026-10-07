import { useState } from 'react';
import { Search, ChevronDown, ChevronUp, ChevronLeft, ChevronRight, Inbox } from 'lucide-react';

// Filter controls placed next to the search box (same height, border and text size as the search input)
export const tableFilterClass =
  'min-h-10 w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-900 focus:border-red-500 focus:outline-none focus:ring-4 focus:ring-red-500/10 sm:w-48 dark:border-slate-700/60 dark:bg-slate-800/80 dark:text-slate-100';

/**
 * Card with search, table (cards on mobile) and pagination. Optional: `title` + `icon` + `actions` add a card header
 * (title left, actions right) and `filters` sit next to the search box. Without them the card renders as before.
 */
export const DataTable = ({
  columns = [],
  data = [],
  searchable = true,
  searchPlaceholder = 'Search records...',
  emptyMessage = 'No matching records found.',
  onRowClick,
  title,
  icon: TitleIcon,
  actions,
  filters,
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [sortColumn, setSortColumn] = useState(null);
  const [sortDirection, setSortDirection] = useState('asc');
  const [currentPage, setCurrentPage] = useState(1);
  const pageSize = 8;

  // Filter
  const filteredData = data.filter((row) =>
    columns.some((col) => {
      const val = row[col.accessor];
      if (val === null || val === undefined) return false;
      return String(val).toLowerCase().includes(searchTerm.toLowerCase());
    })
  );

  // Sort
  const sortedData = [...filteredData].sort((a, b) => {
    if (!sortColumn) return 0;
    const valA = a[sortColumn];
    const valB = b[sortColumn];

    if (valA < valB) return sortDirection === 'asc' ? -1 : 1;
    if (valA > valB) return sortDirection === 'asc' ? 1 : -1;
    return 0;
  });

  // Paginate
  const totalPages = Math.ceil(sortedData.length / pageSize) || 1;
  const paginatedData = sortedData.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  const handleSort = (accessor) => {
    if (sortColumn === accessor) {
      setSortDirection(sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSortColumn(accessor);
      setSortDirection('asc');
    }
  };

  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-sm shadow-slate-950/5 dark:border-slate-800 dark:bg-slate-900">
      {/* Optional card header: icon + title left, actions right */}
      {title && (
        <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3 dark:border-slate-800">
          <h2 className="flex min-w-0 items-center gap-2 text-sm font-bold text-slate-900 dark:text-slate-100">
            {TitleIcon && <TitleIcon className="h-4 w-4 shrink-0 text-red-600" />}
            <span className="truncate">{title}</span>
          </h2>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}

      {/* Search Header */}
      {searchable && (
        <div className="flex flex-col gap-3 border-b border-slate-100 p-4 sm:flex-row sm:items-center sm:justify-between dark:border-slate-800">
          <div className={filters ? 'flex w-full flex-1 flex-col gap-2 sm:flex-row sm:items-center' : 'contents'}>
          <div className="relative w-full flex-1 sm:max-w-sm">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
            <input
              type="text"
              value={searchTerm}
              onChange={(e) => {
                setSearchTerm(e.target.value);
                setCurrentPage(1);
              }}
              placeholder={searchPlaceholder}
              className="min-h-10 w-full rounded-xl border border-slate-200 bg-slate-50 py-2 pl-9 pr-3 text-sm text-slate-900 placeholder-slate-400 focus:border-red-500 focus:outline-none focus:ring-4 focus:ring-red-500/10 dark:border-slate-700/60 dark:bg-slate-800/80 dark:text-slate-100"
            />
          </div>
          {filters}
          </div>
          <span className="text-xs text-slate-500 dark:text-slate-400 font-medium">
            Total Records: {filteredData.length}
          </span>
        </div>
      )}

      {/* Mobile record cards */}
      <div className="divide-y divide-slate-100 xl:hidden dark:divide-slate-800">
        {paginatedData.length > 0 ? paginatedData.map((row, idx) => (
          <div
            key={row.id || row.bloodRequestId || row.inventoryId || idx}
            onClick={() => onRowClick && onRowClick(row)}
            className={`space-y-3 p-4 ${onRowClick ? 'cursor-pointer active:bg-slate-50 dark:active:bg-slate-800/50' : ''}`}
          >
            {columns.map((col) => (
              <div key={col.accessor} className="grid grid-cols-[minmax(6.5rem,0.8fr)_minmax(0,1.2fr)] items-start gap-3">
                <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400">{col.header}</span>
                <div className="min-w-0 text-right text-sm text-slate-800 dark:text-slate-200">{col.cell ? col.cell(row) : row[col.accessor]}</div>
              </div>
            ))}
          </div>
        )) : (
          <div className="flex flex-col items-center justify-center px-4 py-12 text-center text-slate-400">
            <Inbox className="mb-2 h-10 w-10 stroke-1" />
            <p className="text-sm font-medium">{emptyMessage}</p>
          </div>
        )}
      </div>

      {/* Desktop table canvas */}
      <div className="hidden overflow-x-auto xl:block">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-slate-50 dark:bg-slate-800/60 border-b border-slate-200 dark:border-slate-800 text-[11px] font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              {columns.map((col) => (
                <th
                  key={col.accessor}
                  onClick={() => col.sortable !== false && handleSort(col.accessor)}
                  className={`px-4 py-3 ${col.sortable !== false ? 'cursor-pointer select-none hover:text-slate-900 dark:hover:text-slate-200' : ''}`}
                  aria-sort={sortColumn === col.accessor ? (sortDirection === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  <div className="flex items-center gap-1.5">
                    <span>{col.header}</span>
                    {sortColumn === col.accessor && (
                      sortDirection === 'asc' ? <ChevronUp className="w-3.5 h-3.5 text-red-600" /> : <ChevronDown className="w-3.5 h-3.5 text-red-600" />
                    )}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-800/60 text-xs text-slate-700 dark:text-slate-300">
            {paginatedData.length > 0 ? (
              paginatedData.map((row, idx) => (
                <tr
                  key={row.id || idx}
                  onClick={() => onRowClick && onRowClick(row)}
                  className={`hover:bg-slate-50/80 dark:hover:bg-slate-800/40 transition-colors ${onRowClick ? 'cursor-pointer' : ''}`}
                >
                  {columns.map((col) => (
                    <td key={col.accessor} className="px-4 py-3.5">
                      {col.cell ? col.cell(row) : row[col.accessor]}
                    </td>
                  ))}
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={columns.length} className="px-4 py-12 text-center">
                  <div className="flex flex-col items-center justify-center text-slate-400">
                    <Inbox className="w-10 h-10 mb-2 stroke-1" />
                    <p className="text-xs font-medium">{emptyMessage}</p>
                  </div>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="flex flex-col gap-3 border-t border-slate-100 px-4 py-3 text-xs text-slate-500 sm:flex-row sm:items-center sm:justify-between dark:border-slate-800 dark:text-slate-400">
        <span>
          Showing {paginatedData.length > 0 ? (currentPage - 1) * pageSize + 1 : 0} to{' '}
          {Math.min(currentPage * pageSize, filteredData.length)} of {filteredData.length} entries
        </span>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setCurrentPage((p) => Math.max(p - 1, 1))}
            disabled={currentPage === 1}
            className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-800 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Previous page"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="font-semibold text-slate-900 dark:text-slate-100">
            {currentPage} / {totalPages}
          </span>
          <button
            onClick={() => setCurrentPage((p) => Math.min(p + 1, totalPages))}
            disabled={currentPage === totalPages}
            className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-800 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Next page"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
};

export default DataTable;
