import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { bloodRequestApi, acceptanceApi } from '../../api';
import { Badge } from '../../components/common/Badge';
import { SmartMatchingProgress } from '../../components/workflow/SmartMatchingProgress';
import { useNotification } from '../../context/NotificationContext';
import { Building2, MapPin, Calendar, Clock, Heart, AlertCircle, CheckCircle2, Loader2 } from 'lucide-react';

export const RequestDetailPage = () => {
  const { id } = useParams();
  const [request, setRequest] = useState(null);
  const [loading, setLoading] = useState(true);
  const [accepting, setAccepting] = useState(false);
  const [accepted, setAccepted] = useState(false);
  const { addToast } = useNotification();
  const navigate = useNavigate();

  useEffect(() => {
    const fetchDetail = async () => {
      try {
        const data = await bloodRequestApi.getRequestById(id);
        setRequest(data);
      } catch (err) {
        console.error('Failed to fetch request detail:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchDetail();
  }, [id]);

  const handleAccept = async () => {
    setAccepting(true);
    try {
      await acceptanceApi.acceptRequest({ bloodRequestId: id });
      setAccepted(true);
      addToast({
        title: 'Acceptance Registered!',
        message: 'Proceeding to AI Health Screening Questionnaire.',
        type: 'success'
      });
      setTimeout(() => navigate('/donor/acceptances'), 2000);
    } catch (err) {
      addToast({
        title: 'Could not accept request',
        message: err.response?.data?.message || err.message || 'Error processing acceptance.',
        type: 'error'
      });
    } finally {
      setAccepting(false);
    }
  };

  if (loading) {
    return (
      <div className="py-20 flex justify-center items-center">
        <Loader2 className="w-8 h-8 text-red-600 animate-spin" />
      </div>
    );
  }

  if (!request) {
    return (
      <div className="p-8 text-center bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 text-slate-500">
        Blood request record not found.
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      {/* Detail Card Header */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
          <div className="flex items-center gap-4">
            <div className="w-14 h-14 rounded-2xl bg-red-600 text-white font-black text-2xl flex items-center justify-center shadow-lg shadow-red-600/30">
              {request.bloodGroup}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">
                  {request.hospitalName || 'St. Jude Memorial Hospital'}
                </h1>
                <Badge variant={request.priority === 'CRITICAL' ? 'danger' : 'warning'}>
                  {request.priority || 'URGENT'}
                </Badge>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1 flex items-center gap-1">
                <MapPin className="w-3.5 h-3.5 text-slate-400" /> Regional Medical District • Request ID #{id.substring(0, 8)}
              </p>
            </div>
          </div>

          <button
            onClick={handleAccept}
            disabled={accepting || accepted}
            className="px-6 py-3 bg-red-600 hover:bg-red-700 disabled:bg-emerald-600 text-white font-bold text-xs rounded-xl shadow-lg shadow-red-600/20 transition-all flex items-center justify-center gap-2 shrink-0"
          >
            {accepting ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : accepted ? (
              <>
                <CheckCircle2 className="w-4 h-4 text-white" />
                <span>Accepted! Redirecting...</span>
              </>
            ) : (
              <>
                <Heart className="w-4 h-4 fill-white" />
                <span>Accept & Begin Health Screening</span>
              </>
            )}
          </button>
        </div>

        {/* Requirements Breakdown */}
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 pt-6">
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Units Required</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">{request.unitsRequired} Units</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Clinical Urgency</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">{request.priority || 'URGENT'}</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Status</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">{request.status || 'VERIFIED'}</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Created</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">Today</div>
          </div>
        </div>
      </div>

      {/* Visual Workflow Stepper */}
      <SmartMatchingProgress status={request.status || 'VERIFIED'} />

      {/* Clinical Reason Description */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mb-2">Clinical Context & Requirements</h3>
        <p className="text-xs text-slate-600 dark:text-slate-300 leading-relaxed">
          {request.reason || 'This blood request has been verified by attending medical staff. Donors will undergo automated AI health screening prior to clinical sign-off.'}
        </p>
      </div>
    </div>
  );
};

export default RequestDetailPage;
