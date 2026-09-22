import React from 'react';
import { CheckCircle2, Clock, AlertCircle, Loader2 } from 'lucide-react';

export const WorkflowTracker = ({ steps = [], currentStepIndex = 0, title }) => {
  return (
    <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm my-4">
      {title && (
        <h4 className="text-sm font-semibold text-slate-900 dark:text-slate-100 uppercase tracking-wider mb-4">
          {title}
        </h4>
      )}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
        {steps.map((step, idx) => {
          const isCompleted = idx < currentStepIndex;
          const isCurrent = idx === currentStepIndex;

          return (
            <div key={idx} className="flex-1 flex items-center w-full">
              <div className="flex items-center gap-3">
                <div
                  className={`w-9 h-9 rounded-full flex items-center justify-center text-sm font-semibold transition-all ${
                    isCompleted
                      ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-400'
                      : isCurrent
                      ? 'bg-red-600 text-white shadow-md shadow-red-500/20 animate-pulse'
                      : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'
                  }`}
                >
                  {isCompleted ? (
                    <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
                  ) : isCurrent ? (
                    <Loader2 className="w-5 h-5 animate-spin text-white" />
                  ) : (
                    <span>{idx + 1}</span>
                  )}
                </div>
                <div>
                  <div className={`text-xs font-semibold ${isCurrent ? 'text-red-600 dark:text-red-400' : 'text-slate-900 dark:text-slate-100'}`}>
                    {step.label}
                  </div>
                  {step.description && (
                    <div className="text-[11px] text-slate-500 dark:text-slate-400">{step.description}</div>
                  )}
                </div>
              </div>
              {idx < steps.length - 1 && (
                <div
                  className={`hidden md:block flex-1 h-[2px] mx-3 transition-colors ${
                    isCompleted ? 'bg-emerald-500' : 'bg-slate-200 dark:bg-slate-800'
                  }`}
                />
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};

export default WorkflowTracker;
