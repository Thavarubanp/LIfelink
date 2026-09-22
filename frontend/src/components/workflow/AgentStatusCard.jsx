import React from 'react';
import { Sparkles, ShieldCheck, Zap, Database } from 'lucide-react';

export const AgentStatusCard = ({ type = 'matching', title, description, metrics = [], status = 'active' }) => {
  const getIcon = () => {
    switch (type) {
      case 'screening':
        return <ShieldCheck className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />;
      case 'matching':
        return <Sparkles className="w-5 h-5 text-blue-600 dark:text-blue-400" />;
      case 'inventory':
        return <Database className="w-5 h-5 text-amber-600 dark:text-amber-400" />;
      case 'emergency':
        return <Zap className="w-5 h-5 text-red-600 dark:text-red-400" />;
      default:
        return <Sparkles className="w-5 h-5 text-blue-600" />;
    }
  };

  return (
    <div className="bg-slate-900 text-white rounded-xl p-5 shadow-lg border border-slate-800 relative overflow-hidden my-3">
      <div className="absolute top-0 right-0 w-32 h-32 bg-gradient-to-bl from-red-600/20 via-blue-600/10 to-transparent rounded-full blur-2xl pointer-events-none" />
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2.5">
          <div className="p-2 bg-slate-800 rounded-lg border border-slate-700">
            {getIcon()}
          </div>
          <div>
            <h4 className="text-sm font-semibold tracking-wide text-slate-100">{title}</h4>
            <span className="text-[11px] text-slate-400 font-medium">Smart Orchestration Engine</span>
          </div>
        </div>
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 mr-1.5 animate-ping" />
          {status.toUpperCase()}
        </span>
      </div>
      <p className="text-xs text-slate-300 mb-4 leading-relaxed">{description}</p>
      {metrics.length > 0 && (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-2.5 pt-3 border-t border-slate-800">
          {metrics.map((m, idx) => (
            <div key={idx} className="bg-slate-800/60 p-2.5 rounded-lg border border-slate-700/50">
              <div className="text-[10px] text-slate-400 uppercase tracking-wider">{m.label}</div>
              <div className="text-sm font-bold text-white mt-0.5">{m.value}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default AgentStatusCard;
