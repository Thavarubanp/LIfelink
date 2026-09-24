import React from 'react';
import {
  AlertTriangle,
  Clock,
  FileQuestion,
  FileText,
  CheckCircle2,
  XCircle,
  Building2,
  Calendar,
  MessageSquare,
  Paperclip
} from 'lucide-react';

export const buildChronologicalTimeline = (complaint) => {
  if (!complaint) return [];
  const events = [];

  // 1. Complaint Created
  if (complaint.createdAt) {
    events.push({
      id: 'created',
      date: new Date(complaint.createdAt),
      title: 'Complaint Filed',
      type: 'created',
      actor: complaint.userEmail || 'Complaint Owner',
      role: 'User',
      description: complaint.description,
      notes: `Subject: ${complaint.subject}`
    });
  }

  // 2. Audit logs (status changes, admin reviews, requests, user cancellation)
  if (Array.isArray(complaint.auditLogs)) {
    complaint.auditLogs.forEach((log) => {
      // Don't duplicate initial "Complaint submitted" log if matching created event
      if (log.previousStatus === 'NONE' && log.newStatus === 'OPEN') return;

      // Replies (status unchanged): admin <-> complaint creator messages, optionally with an attachment
      if (log.isReply) {
        const fromAdmin = !!log.adminId;
        events.push({
          id: `audit-${log.auditId || Math.random()}`,
          date: new Date(log.createdAt),
          title: fromAdmin ? 'Admin Reply' : 'Creator Reply',
          type: 'reply',
          actor: fromAdmin ? `Admin (${log.adminEmail || 'LifeLink'})` : complaint.userEmail || 'Complaint Owner',
          role: fromAdmin ? 'Admin' : 'User',
          notes: log.notes,
          attachmentUrl: log.attachmentUrl,
          attachmentName: log.attachmentName
        });
        return;
      }

      const isCancellation = log.newStatus === 'CANCELLED';
      const isReview = log.newStatus === 'UNDER_REVIEW';
      const isAwaitingInfo = log.newStatus === 'AWAITING_INFORMATION';
      const isResolved = log.newStatus === 'RESOLVED';
      const isRejected = log.newStatus === 'REJECTED';

      let title = `Status Update: ${log.newStatus}`;
      let type = 'status';
      let actor = log.adminEmail ? `Admin (${log.adminEmail})` : 'System';
      let role = log.adminEmail ? 'Admin' : 'System';

      if (isCancellation) {
        title = 'Complaint Cancelled by Owner';
        type = 'cancelled';
        actor = complaint.userEmail || 'Complaint Owner';
        role = 'User';
      } else if (isReview) {
        title = 'Investigation Initiated';
        type = 'review';
      } else if (isAwaitingInfo) {
        title = 'Hospital Activity Evidence Requested';
        type = 'evidence_requested';
      } else if (isResolved) {
        title = 'Marked as Solved';
        type = 'resolved';
        if (!log.adminEmail) {
          actor = complaint.userEmail || 'Complaint Owner';
          role = 'User';
        }
      } else if (isRejected) {
        title = 'Complaint Rejected by Administration';
        type = 'rejected';
      }

      events.push({
        id: `audit-${log.auditId || Math.random()}`,
        date: new Date(log.createdAt),
        title,
        type,
        actor,
        role,
        notes: log.notes
      });
    });
  }

  // 3. Activity reports (hospital evidence uploaded)
  if (Array.isArray(complaint.activityReports)) {
    complaint.activityReports.forEach((report) => {
      events.push({
        id: `report-${report.reportId || Math.random()}`,
        date: new Date(report.submittedAt),
        title: `Evidence Submitted: ${report.title}`,
        type: 'evidence_uploaded',
        actor: report.hospitalName || complaint.hospitalName || 'Target Hospital',
        role: 'Hospital',
        description: report.description,
        notes: 'Official hospital activity report and justification.'
      });
    });
  }

  // Sort strictly chronologically
  return events.sort((a, b) => a.date - b.date);
};

export const ComplaintActivityTimeline = ({ complaint }) => {
  const events = buildChronologicalTimeline(complaint);

  if (!events || events.length === 0) {
    return (
      <div className="p-4 text-center text-xs text-slate-400 bg-slate-50 dark:bg-slate-900/50 rounded-xl border border-slate-200 dark:border-slate-800">
        No recorded activity events yet.
      </div>
    );
  }

  const getEventBadge = (role, type) => {
    if (type === 'cancelled') {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-rose-100 text-rose-700 dark:bg-rose-950/60 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
          User Action
        </span>
      );
    }
    if (role === 'Admin') {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-purple-100 text-purple-700 dark:bg-purple-950/60 dark:text-purple-300 border border-purple-200 dark:border-purple-900">
          System Admin
        </span>
      );
    }
    if (role === 'Hospital') {
      return (
        <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-cyan-100 text-cyan-700 dark:bg-cyan-950/60 dark:text-cyan-300 border border-cyan-200 dark:border-cyan-900">
          Hospital
        </span>
      );
    }
    return (
      <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-100 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border border-blue-200 dark:border-blue-900">
        Complainant
      </span>
    );
  };

  const getEventIcon = (type) => {
    switch (type) {
      case 'created':
        return <AlertTriangle className="w-4 h-4 text-amber-500" />;
      case 'reply':
        return <MessageSquare className="w-4 h-4 text-blue-500" />;
      case 'review':
        return <Clock className="w-4 h-4 text-blue-500" />;
      case 'evidence_requested':
        return <FileQuestion className="w-4 h-4 text-indigo-500" />;
      case 'evidence_uploaded':
        return <Building2 className="w-4 h-4 text-cyan-500" />;
      case 'resolved':
        return <CheckCircle2 className="w-4 h-4 text-emerald-500" />;
      case 'cancelled':
        return <XCircle className="w-4 h-4 text-rose-500" />;
      case 'rejected':
        return <XCircle className="w-4 h-4 text-slate-500" />;
      default:
        return <Clock className="w-4 h-4 text-slate-400" />;
    }
  };

  const getNodeColor = (type) => {
    switch (type) {
      case 'created':
        return 'border-amber-500/30 bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400';
      case 'review':
        return 'border-blue-500/30 bg-blue-50 dark:bg-blue-950/40 text-blue-600 dark:text-blue-400';
      case 'evidence_requested':
        return 'border-indigo-500/30 bg-indigo-50 dark:bg-indigo-950/40 text-indigo-600 dark:text-indigo-400';
      case 'evidence_uploaded':
        return 'border-cyan-500/30 bg-cyan-50 dark:bg-cyan-950/40 text-cyan-600 dark:text-cyan-400';
      case 'resolved':
        return 'border-emerald-500/30 bg-emerald-50 dark:bg-emerald-950/40 text-emerald-600 dark:text-emerald-400';
      case 'cancelled':
        return 'border-rose-500/30 bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-400';
      default:
        return 'border-slate-300 dark:border-slate-700 bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400';
    }
  };

  return (
    <div className="relative pl-6 space-y-6 before:absolute before:left-3 before:top-2 before:bottom-2 before:w-0.5 before:bg-slate-200 dark:before:bg-slate-800">
      {events.map((event, idx) => (
        <div key={event.id || idx} className="relative group">
          {/* Node Icon */}
          <div
            className={`absolute -left-6 top-1 w-6 h-6 rounded-full border flex items-center justify-center shadow-sm shrink-0 ${getNodeColor(
              event.type
            )}`}
          >
            {getEventIcon(event.type)}
          </div>

          {/* Event Content Box */}
          <div className="bg-slate-50 dark:bg-slate-800/60 rounded-xl p-3.5 border border-slate-200/80 dark:border-slate-700/60 shadow-sm space-y-1.5 transition-all">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1.5">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-xs font-bold text-slate-900 dark:text-slate-100">
                  {event.title}
                </span>
                {getEventBadge(event.role, event.type)}
              </div>
              <span className="text-[11px] text-slate-500 dark:text-slate-400 flex items-center gap-1 font-mono">
                <Calendar className="w-3 h-3 text-slate-400" />
                {event.date.toLocaleDateString(undefined, {
                  month: 'short',
                  day: 'numeric',
                  year: 'numeric'
                })}{' '}
                {event.date.toLocaleTimeString(undefined, {
                  hour: '2-digit',
                  minute: '2-digit'
                })}
              </span>
            </div>

            {event.actor && (
              <p className="text-[11px] text-slate-500 dark:text-slate-400 font-medium">
                Recorded by: <span className="text-slate-700 dark:text-slate-200">{event.actor}</span>
              </p>
            )}

            {event.notes && (
              <p className="text-xs text-slate-600 dark:text-slate-300 font-medium bg-white dark:bg-slate-900/60 p-2.5 rounded-lg border border-slate-100 dark:border-slate-800">
                {event.notes}
              </p>
            )}

            {event.description && (
              <p className="text-xs text-slate-500 dark:text-slate-400 leading-relaxed pt-1">
                {event.description}
              </p>
            )}

            {event.attachmentUrl && (
              <a
                href={event.attachmentUrl}
                download={event.attachmentName || 'attachment'}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-blue-700 dark:text-blue-300 hover:bg-blue-50 dark:hover:bg-slate-800 transition-colors"
              >
                <Paperclip className="w-3 h-3" /> {event.attachmentName || 'Attachment'}
              </a>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};

export default ComplaintActivityTimeline;
