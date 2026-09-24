import React, { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, ClipboardCheck, FileText, Loader2, Lock, Undo2, X, XCircle } from 'lucide-react';
import { acceptanceApi, bloodRequestApi, screeningApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { getApiErrorMessage } from '../../utils/errorUtils';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];
const RISK_VARIANT = { HIGH: 'danger', MEDIUM: 'warning', LOW: 'success' };
const fmt = (value) => (value ? new Date(value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');
const parseReport = (json) => {
  try {
    return json ? JSON.parse(json) : null;
  } catch {
    return null;
  }
};

const ReportViewer = ({ versions, onClose }) => {
  const [selected, setSelected] = useState(versions[versions.length - 1]);
  const report = parseReport(selected.reportJson);
  return (
    <div className="fixed inset-0 z-50 bg-slate-950/60 backdrop-blur-sm flex items-center justify-center p-4">
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl w-full max-w-3xl max-h-[90vh] flex flex-col shadow-2xl">
        <div className="flex items-center justify-between px-5 py-3 border-b border-slate-100 dark:border-slate-800">
          <div>
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Screening report - {selected.donorName}</h3>
            <p className="text-[11px] text-slate-500">Request #{String(selected.bloodRequestId).substring(0, 8)} ({selected.requestBloodGroup}) - version {selected.reportVersion}, submitted {fmt(selected.createdAt)}</p>
          </div>
          <button type="button" onClick={onClose} className="p-1.5 rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"><X className="w-4 h-4" /></button>
        </div>

        {versions.length > 1 && (
          <div className="px-5 py-2 border-b border-slate-100 dark:border-slate-800 flex flex-wrap gap-1.5">
            {versions.map((v) => (
              <button key={v.donorVerificationId} type="button" onClick={() => setSelected(v)}
                className={`px-2.5 py-1 rounded-lg text-[11px] font-semibold border ${v === selected ? 'bg-red-600 text-white border-red-600' : 'border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300'}`}>
                v{v.reportVersion} - {v.status}
              </button>
            ))}
          </div>
        )}

        <div className="flex-1 overflow-y-auto p-5 space-y-4 text-xs">
          {!report ? (
            <p className="text-slate-500">This is a legacy record without an AI screening report. Ask the donor to complete screening again if needed.</p>
          ) : (
            <>
              <div className="p-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800/60 space-y-2">
                <div className="flex items-center gap-2 flex-wrap">
                  <Badge variant={RISK_VARIANT[report.risk_level] || 'default'} size="sm">Risk {report.risk_level}</Badge>
                  <Badge variant="info" size="sm">AI recommendation: {report.recommendation}</Badge>
                </div>
                <p className="text-slate-700 dark:text-slate-200">{report.summary}</p>
                {report.doctor_notes && <p className="text-slate-500"><span className="font-semibold">Suggested checks:</span> {report.doctor_notes}</p>}
                <p className="text-[10px] text-slate-400">{report.governance}</p>
              </div>
              {report.flags?.length > 0 && (
                <div className="space-y-1">
                  <h4 className="font-bold text-slate-800 dark:text-slate-100 flex items-center gap-1"><AlertTriangle className="w-3.5 h-3.5 text-amber-500" /> Flags for review</h4>
                  <ul className="list-disc pl-5 space-y-0.5 text-slate-600 dark:text-slate-300">
                    {report.flags.map((f, i) => <li key={i}><span className="font-semibold">[{f.severity}]</span> {f.message}</li>)}
                  </ul>
                </div>
              )}
              {report.sections?.map((section) => (
                <div key={section.index} className={`rounded-xl border p-3 ${section.confidential ? 'border-slate-800 dark:border-slate-300' : 'border-slate-200 dark:border-slate-700'}`}>
                  <h4 className="font-bold text-slate-800 dark:text-slate-100 mb-2 flex items-center gap-1.5">
                    {section.confidential && <Lock className="w-3.5 h-3.5" />} Section {section.index}: {section.title}
                  </h4>
                  {section.items.length === 0 ? (
                    <p className="text-slate-400">Not applicable.</p>
                  ) : (
                    <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5">
                      {section.items.map((item) => (
                        <div key={item.question_id}>
                          <dt className="text-[10px] text-slate-400">{item.question}</dt>
                          <dd className="font-semibold text-slate-800 dark:text-slate-100 whitespace-pre-line">{item.answer || '-'}</dd>
                        </div>
                      ))}
                    </dl>
                  )}
                </div>
              ))}
            </>
          )}
          {selected.status !== 'Pending' && (
            <div className="p-3 rounded-xl border border-slate-200 dark:border-slate-700">
              <span className="font-semibold">Decision:</span> {selected.status}
              {selected.decidedByName && ` by Dr. ${selected.decidedByName}`} {selected.verifiedAt && `on ${fmt(selected.verifiedAt)}`}
              {selected.notes && <p className="mt-1 text-slate-600 dark:text-slate-300">{selected.notes}</p>}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export const ScreeningReportsPage = () => {
  const { addToast } = useNotification();
  const [reports, setReports] = useState([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState('review');
  const [viewing, setViewing] = useState(null);
  const [dialog, setDialog] = useState(null); // { type: 'approve' | 'reject' | 'record' | 'release', report }
  const [text, setText] = useState('');
  const [testedGroup, setTestedGroup] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [reloadKey, setReloadKey] = useState(0);
  const reload = () => setReloadKey((k) => k + 1);

  useEffect(() => {
    const fetchReports = async () => {
      try {
        const list = await screeningApi.getReports();
        setReports(Array.isArray(list) ? list : []);
      } catch (err) {
        addToast({ title: 'Could not load screening reports', message: getApiErrorMessage(err), type: 'error' });
      } finally {
        setLoading(false);
      }
    };
    fetchReports();
  }, [reloadKey, addToast]);

  // One row per acceptance (its latest report version); all versions stay available in the viewer
  const rows = useMemo(() => {
    const byAcceptance = {};
    reports.forEach((r) => {
      (byAcceptance[r.acceptanceId] = byAcceptance[r.acceptanceId] || []).push(r);
    });
    return Object.values(byAcceptance).map((versions) => {
      const sorted = [...versions].sort((a, b) => a.reportVersion - b.reportVersion);
      return { latest: sorted[sorted.length - 1], versions: sorted };
    });
  }, [reports]);

  const riskOrder = { HIGH: 0, MEDIUM: 1, LOW: 2 };
  const review = rows
    .filter((r) => r.latest.status === 'Pending' && r.latest.acceptanceStatus === 'ScreeningCompleted')
    .sort((a, b) => Number(b.latest.isAssignedToMe) - Number(a.latest.isAssignedToMe)
      || (riskOrder[a.latest.riskLevel] ?? 3) - (riskOrder[b.latest.riskLevel] ?? 3)
      || new Date(a.latest.createdAt) - new Date(b.latest.createdAt));
  const awaiting = rows.filter((r) => r.latest.acceptanceStatus === 'Verified');
  const history = rows.filter((r) => !review.includes(r) && !awaiting.includes(r));
  const visible = tab === 'review' ? review : tab === 'awaiting' ? awaiting : history;

  const openDialog = (type, report) => {
    setDialog({ type, report });
    setText('');
    setTestedGroup(report.donorBloodGroup || '');
  };

  const submit = async (e) => {
    e.preventDefault();
    const { type, report } = dialog;
    setSubmitting(true);
    try {
      if (type === 'approve') {
        await screeningApi.approve(report.donorVerificationId, text.trim());
        addToast({ title: 'Donor approved', message: 'A donation slot is reserved for this donor.', type: 'success' });
      } else if (type === 'reject') {
        await screeningApi.reject(report.donorVerificationId, text.trim());
        addToast({ title: 'Donor not approved', message: 'The donor will see your reason. The request stays open.', type: 'info' });
      } else if (type === 'record') {
        await bloodRequestApi.finalizeDonorSelection(report.bloodRequestId, [report.acceptanceId], { [report.acceptanceId]: testedGroup });
        addToast({ title: 'Donation recorded', message: 'The donated unit now counts towards the request.', type: 'success' });
      } else {
        await acceptanceApi.releaseReservation(report.acceptanceId, text.trim());
        addToast({ title: 'Reservation released', message: 'The slot is available to other donors again.', type: 'info' });
      }
      setDialog(null);
      reload();
    } catch (err) {
      addToast({ title: 'Action failed', message: getApiErrorMessage(err), type: 'error' });
    } finally {
      setSubmitting(false);
    }
  };

  const tabs = [
    { key: 'review', label: 'Waiting for review', count: review.length },
    { key: 'awaiting', label: 'Approved - awaiting donation', count: awaiting.length },
    { key: 'history', label: 'History', count: history.length }
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Donor Screening Queue</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          AI-prepared screening reports for your hospital's requests. Reports assigned to you come first; any doctor of the hospital can act as a fallback. You make every decision.
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        {tabs.map((t) => (
          <button key={t.key} type="button" onClick={() => setTab(t.key)}
            className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-all ${tab === t.key ? 'bg-red-600 text-white shadow-sm' : 'bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-300'}`}>
            {t.label} ({t.count})
          </button>
        ))}
      </div>

      {loading ? (
        <div className="p-8 text-center"><Loader2 className="w-8 h-8 text-red-500 animate-spin mx-auto" /></div>
      ) : visible.length === 0 ? (
        <div className="p-8 text-center bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl text-xs text-slate-500">Nothing here right now.</div>
      ) : (
        <div className="space-y-3">
          {visible.map(({ latest, versions }) => (
            <div key={latest.acceptanceId} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3">
              <div className="space-y-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-sm font-bold text-slate-900 dark:text-slate-100">{latest.donorName}</span>
                  {latest.donorBloodGroup && <Badge variant="blood" size="sm">{latest.donorBloodGroup}</Badge>}
                  {latest.riskLevel && <Badge variant={RISK_VARIANT[latest.riskLevel] || 'default'} size="sm">Risk {latest.riskLevel}</Badge>}
                  {latest.isAssignedToMe && <Badge variant="primary" size="sm">Assigned to you</Badge>}
                  {latest.donorAccountStatus && latest.donorAccountStatus !== 'Active' && <Badge variant="warning" size="sm">Donor {latest.donorAccountStatus}</Badge>}
                  {versions.length > 1 && <Badge variant="default" size="sm">v{latest.reportVersion}</Badge>}
                </div>
                <p className="text-[11px] text-slate-500">
                  Request #{String(latest.bloodRequestId).substring(0, 8)} ({latest.requestBloodGroup}, {latest.requestStatus}) - {latest.fulfilledUnits}/{latest.unitsRequired} donated, {latest.reservedUnits} reserved - {latest.recommendation ? `AI: ${latest.recommendation}` : ''}
                </p>
                {tab === 'history' && (
                  <p className="text-[11px] text-slate-500">
                    {latest.status}{latest.decidedByName ? ` by Dr. ${latest.decidedByName}` : ''}{latest.verifiedAt ? ` on ${fmt(latest.verifiedAt)}` : ''} - donor status {latest.acceptanceStatus}
                    {latest.notes ? ` - ${latest.notes}` : ''}
                  </p>
                )}
                {tab === 'review' && !latest.hasFreeSlot && (
                  <p className="text-[11px] text-amber-600">No free slot: every remaining unit is reserved. The donor stays on standby.</p>
                )}
              </div>
              <div className="flex flex-wrap gap-2 shrink-0">
                <button type="button" onClick={() => setViewing(versions)}
                  className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                  <FileText className="w-3.5 h-3.5" /> View report
                </button>
                {tab === 'review' && (
                  <>
                    <button type="button" disabled={!latest.hasFreeSlot} onClick={() => openDialog('approve', latest)}
                      className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-40">
                      <CheckCircle2 className="w-3.5 h-3.5" /> Approve
                    </button>
                    <button type="button" onClick={() => openDialog('reject', latest)}
                      className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-600 text-white hover:bg-rose-700">
                      <XCircle className="w-3.5 h-3.5" /> Reject
                    </button>
                  </>
                )}
                {tab === 'awaiting' && (
                  <>
                    <button type="button" onClick={() => openDialog('record', latest)}
                      className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold bg-emerald-600 text-white hover:bg-emerald-700">
                      <ClipboardCheck className="w-3.5 h-3.5" /> Record donation
                    </button>
                    <button type="button" onClick={() => openDialog('release', latest)}
                      className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800">
                      <Undo2 className="w-3.5 h-3.5" /> Release slot
                    </button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {viewing && <ReportViewer versions={viewing} onClose={() => setViewing(null)} />}

      {dialog && (
        <div className="fixed inset-0 z-50 bg-slate-950/60 backdrop-blur-sm flex items-center justify-center p-4">
          <form onSubmit={submit} className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 w-full max-w-md shadow-2xl space-y-3 text-xs">
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">
              {{ approve: 'Approve donor', reject: 'Reject donor', record: 'Record donation', release: 'Release reservation' }[dialog.type]} - {dialog.report.donorName}
            </h3>
            {dialog.type === 'record' ? (
              <>
                <p className="text-slate-500">Confirm the blood group tested at donation. It becomes the donor's confirmed blood group.</p>
                <select required value={testedGroup} onChange={(e) => setTestedGroup(e.target.value)}
                  className="w-full px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800">
                  <option value="">Select tested blood group</option>
                  {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
                </select>
              </>
            ) : (
              <>
                <label className="block font-semibold text-slate-600 dark:text-slate-300">
                  {dialog.type === 'approve' ? 'Notes for the donor (optional, visible to the donor)' : 'Reason (required, shown to the donor)'}
                </label>
                <textarea value={text} onChange={(e) => setText(e.target.value)} rows={3} maxLength={500} required={dialog.type !== 'approve'}
                  className="w-full px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800" />
                {dialog.type === 'approve' && <p className="text-slate-400">Approval reserves one donation slot. It counts as donated only when you record the donation.</p>}
              </>
            )}
            <div className="flex justify-end gap-2 pt-2">
              <button type="button" onClick={() => setDialog(null)} className="px-4 py-2 rounded-xl font-semibold text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800">Cancel</button>
              <button type="submit" disabled={submitting} className="px-4 py-2 rounded-xl font-semibold bg-red-600 text-white hover:bg-red-700 disabled:opacity-50 flex items-center gap-1">
                {submitting && <Loader2 className="w-3.5 h-3.5 animate-spin" />} Confirm
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};

export default ScreeningReportsPage;
