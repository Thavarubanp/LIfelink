import { useEffect, useState } from 'react';
import { FileText, Loader2, AlertCircle } from 'lucide-react';
import { appealApi } from '../../api';
import { getApiErrorMessage } from '../../utils/errorUtils';
import AppealThread from '../../components/complaints/AppealThread';

const STATUS_STYLES = {
  PENDING: 'bg-amber-100 text-amber-700 dark:bg-amber-950/60 dark:text-amber-300 border-amber-200 dark:border-amber-900',
  APPROVED: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900',
  REJECTED: 'bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300 border-rose-200 dark:border-rose-900',
  CLOSED: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300 border-slate-200 dark:border-slate-700'
};

/**
 * Read-only history of the signed-in user's suspension appeals, the same threads the admin sees.
 * It stays available after a suspension is lifted (the appeal page itself is only for suspended accounts).
 */
export const MyAppealsPage = () => {
  const [appeals, setAppeals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    appealApi
      .getMyAppeals()
      .then((res) => {
        const list = res?.data || (Array.isArray(res) ? res : []);
        setAppeals([...list].sort((a, b) => new Date(b.submittedAt) - new Date(a.submittedAt)));
      })
      .catch((err) => setError(getApiErrorMessage(err)))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <FileText className="w-6 h-6 text-cyan-600" />
          My Suspension Appeals
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
          Every appeal you submitted, with the full conversation and attachments.
        </p>
      </div>

      {loading ? (
        <div className="flex items-center justify-center gap-2 py-12 text-xs text-slate-500">
          <Loader2 className="w-5 h-5 animate-spin text-cyan-600" /> Loading your appeals...
        </div>
      ) : error ? (
        <div className="p-4 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-900/50 rounded-xl text-xs text-red-700 dark:text-red-300 flex items-center gap-2">
          <AlertCircle className="w-4 h-4" /> {error}
        </div>
      ) : appeals.length === 0 ? (
        <div className="p-10 text-center text-xs text-slate-500 dark:text-slate-400 bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl">
          You have not submitted any suspension appeals.
        </div>
      ) : (
        appeals.map((appeal) => (
          <div key={appeal.appealId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-xs text-slate-500 dark:text-slate-400">
                Submitted {new Date(appeal.submittedAt).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' })}
                {appeal.hospitalName ? ` · ${appeal.hospitalName}` : ''}
              </span>
              <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold border ${STATUS_STYLES[appeal.status] || STATUS_STYLES.CLOSED}`}>
                {appeal.status}
              </span>
            </div>
            <AppealThread appeal={appeal} appellantLabel={appeal.hospitalId ? 'Hospital' : 'You'} />
          </div>
        ))
      )}
    </div>
  );
};

export default MyAppealsPage;
