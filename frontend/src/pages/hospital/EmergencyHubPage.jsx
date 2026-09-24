import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { emergencyApi } from '../../api';
import { useNotification } from '../../context/NotificationContext';
import { EmergencyResponseTimeline } from '../../components/workflow/EmergencyResponseTimeline';
import { Zap, AlertTriangle, Loader2 } from 'lucide-react';

export const EmergencyHubPage = () => {
  // The hospital is taken from the signed-in account on the server
  const [formData, setFormData] = useState({
    bloodGroup: 'O-',
    unitsRequired: 5,
    priority: 'CRITICAL',
    reason: 'Emergency ICU Trauma Transfusion'
  });

  const [loading, setLoading] = useState(false);
  const { addToast } = useNotification();

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);

    try {
      await emergencyApi.createEmergencyRequest(formData);
      addToast({
        title: 'Emergency raised',
        message: 'Approved hospitals holding compatible blood have been alerted and can send you a transfer offer.',
        type: 'danger'
      });
    } catch (err) {
      addToast({
        title: 'Broadcast Failed',
        message: err.response?.data?.message || err.message || 'Error triggering emergency broadcast.',
        type: 'error'
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <Zap className="w-5 h-5 text-red-600 animate-pulse" /> Emergency Blood Dispatch Center
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Hospital-to-hospital emergency support: other approved hospitals holding compatible blood are alerted and can offer a transfer.
        </p>
      </div>

      <div className="p-4 rounded-xl border border-red-200 dark:border-red-900 bg-red-50/60 dark:bg-red-950/20 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-xs">
        <span className="text-slate-700 dark:text-slate-300">
          Need donors? Create a <strong>Critical</strong> blood request. It follows the normal doctor approval, then alerts eligible donors.
        </span>
        <Link
          to={`/donor/requests/create?priority=Critical&bloodGroup=${encodeURIComponent(formData.bloodGroup)}`}
          className="shrink-0 px-3 py-1.5 rounded-lg font-semibold bg-red-600 text-white hover:bg-red-700"
        >
          Create Critical donor request
        </Link>
      </div>

      <EmergencyResponseTimeline status="CRITICAL" />

      <div className="bg-white dark:bg-slate-900 border-2 border-red-500/30 dark:border-red-900/50 rounded-2xl p-6 shadow-xl relative overflow-hidden">
        <div className="absolute top-0 right-0 w-32 h-32 bg-red-600/10 rounded-full blur-2xl pointer-events-none" />

        <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mb-4 flex items-center gap-2">
          <AlertTriangle className="w-4 h-4 text-red-600" /> Critical Request Details
        </h3>

        <form onSubmit={handleSubmit} className="space-y-4 text-xs">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Blood Group Needed</label>
              <select
                value={formData.bloodGroup}
                onChange={(e) => setFormData({ ...formData, bloodGroup: e.target.value })}
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 font-bold focus:outline-none focus:border-red-500"
              >
                {['O-', 'O+', 'A-', 'A+', 'B-', 'B+', 'AB-', 'AB+'].map((bg) => (
                  <option key={bg} value={bg}>{bg}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Units Required (Min 1)</label>
              <input
                type="number"
                min="1"
                required
                value={formData.unitsRequired}
                onChange={(e) => setFormData({ ...formData, unitsRequired: parseInt(e.target.value) || 1 })}
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 font-bold focus:outline-none focus:border-red-500"
              />
            </div>
          </div>

          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Priority Rating</label>
            <select
              value={formData.priority}
              onChange={(e) => setFormData({ ...formData, priority: e.target.value })}
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-red-600 font-bold focus:outline-none focus:border-red-500"
            >
              <option value="CRITICAL">CRITICAL EMERGENCY (Immediate Life Safety)</option>
              <option value="HIGH">HIGH PRIORITY (Urgent Operating Room)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Emergency Justification & Notes</label>
            <textarea
              required
              rows={3}
              value={formData.reason}
              onChange={(e) => setFormData({ ...formData, reason: e.target.value })}
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 resize-none"
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full py-3.5 bg-red-600 hover:bg-red-700 text-white font-bold rounded-xl text-xs shadow-lg shadow-red-600/30 transition-all flex items-center justify-center gap-2 uppercase tracking-wider disabled:opacity-50"
          >
            {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : <>Alert hospitals with compatible stock</>}
          </button>
        </form>
      </div>
    </div>
  );
};

export default EmergencyHubPage;
