import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { bloodRequestApi, acceptanceApi, profileApi } from '../../api';
import { Badge, RequestStatusBadge } from '../../components/common/Badge';
import { SmartMatchingProgress } from '../../components/workflow/SmartMatchingProgress';
import { useNotification } from '../../context/NotificationContext';
import { getUserRoles } from '../../utils/roleUtils';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { MapPin, Heart, Loader2, Lock } from 'lucide-react';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

export const RequestDetailPage = () => {
  const { id } = useParams();
  const { user } = useAuth();
  const isDonorAccount = getUserRoles(user).includes('User') && !getUserRoles(user).some((r) => ['Admin', 'HospitalStaff', 'Doctor'].includes(r));
  const [request, setRequest] = useState(null);
  const [profile, setProfile] = useState(null);
  const [bloodGroup, setBloodGroup] = useState('');
  const [loading, setLoading] = useState(true);
  const [accepting, setAccepting] = useState(false);
  const { addToast } = useNotification();
  const navigate = useNavigate();

  useEffect(() => {
    const fetchDetail = async () => {
      try {
        const [data, me] = await Promise.all([
          bloodRequestApi.getRequestById(id),
          isDonorAccount && user?.userId ? profileApi.getUserProfile(user.userId).catch(() => null) : Promise.resolve(null)
        ]);
        setRequest(data);
        setProfile(me);
        if (me?.bloodGroup) setBloodGroup(me.bloodGroup);
      } catch (err) {
        console.error('Failed to fetch request detail:', err);
      } finally {
        setLoading(false);
      }
    };
    fetchDetail();
  }, [id, isDonorAccount, user?.userId]);

  const handleAccept = async () => {
    if (!bloodGroup) {
      addToast({ title: 'Blood group needed', message: 'Select your blood group before accepting.', type: 'warning' });
      return;
    }
    setAccepting(true);
    try {
      const acceptance = await acceptanceApi.acceptRequest({ bloodRequestId: id, donorBloodGroup: bloodGroup });
      addToast({ title: 'Thank you!', message: 'Next, complete your health screening interview.', type: 'success' });
      navigate(`/donor/acceptances/${acceptance.acceptanceId}/screening`);
    } catch (err) {
      addToast({ title: 'Could not accept request', message: getApiErrorMessage(err), type: 'error' });
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

  const remaining = Math.max(0, request.unitsRequired - request.fulfilledUnits);
  const blockedReason = request.status !== 'Approved'
    ? `This request is ${request.status.toLowerCase()} and is not accepting donors.`
    : !request.isAcceptingDonors
      ? 'All remaining donation slots are reserved by approved donors. New acceptances are paused until a slot is released.'
      : profile?.nextEligibleDonationDate && new Date(profile.nextEligibleDonationDate) > new Date()
        ? `You can donate again from ${new Date(profile.nextEligibleDonationDate).toLocaleDateString()} (120 days after your last donation).`
        : null;

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
          <div className="flex items-center gap-4">
            <div className="w-14 h-14 rounded-2xl bg-red-600 text-white font-black text-2xl flex items-center justify-center shadow-lg shadow-red-600/30">
              {request.bloodGroup}
            </div>
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">{request.hospitalName || 'Hospital'}</h1>
                <Badge variant={request.priority?.toUpperCase() === 'CRITICAL' ? 'danger' : 'warning'}>{request.priority}</Badge>
                {request.status === 'Approved' && !request.isAcceptingDonors && <Badge variant="info">All slots reserved</Badge>}
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1 flex items-center gap-1">
                <MapPin className="w-3.5 h-3.5 text-slate-400" /> Request ID #{id.substring(0, 8)}
              </p>
            </div>
          </div>

          {isDonorAccount && (
            <div className="flex flex-col items-stretch md:items-end gap-2 shrink-0">
              {blockedReason ? (
                <p className="text-xs text-slate-500 max-w-xs flex items-start gap-1.5"><Lock className="w-3.5 h-3.5 mt-0.5 shrink-0" /> {blockedReason}</p>
              ) : (
                <>
                  <label className="text-[11px] font-semibold text-slate-500">
                    Your blood group {profile?.bloodGroupConfirmed && <span className="text-emerald-600">(confirmed at a previous donation)</span>}
                  </label>
                  <select
                    value={bloodGroup}
                    onChange={(e) => setBloodGroup(e.target.value)}
                    disabled={profile?.bloodGroupConfirmed}
                    className="px-3 py-2 rounded-xl border border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-800 text-xs font-semibold"
                  >
                    <option value="">Select blood group</option>
                    {BLOOD_GROUPS.map((g) => <option key={g} value={g}>{g}</option>)}
                  </select>
                  <button
                    onClick={handleAccept}
                    disabled={accepting}
                    className="px-6 py-3 bg-red-600 hover:bg-red-700 disabled:opacity-60 text-white font-bold text-xs rounded-xl shadow-lg shadow-red-600/20 transition-all flex items-center justify-center gap-2"
                  >
                    {accepting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Heart className="w-4 h-4 fill-white" />}
                    <span>Accept & Begin Health Screening</span>
                  </button>
                </>
              )}
            </div>
          )}
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 pt-6">
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Units Required</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">{request.unitsRequired}</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Donated</span>
            <div className="text-lg font-bold text-emerald-600 mt-0.5">{request.fulfilledUnits}</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Reserved (approved donors)</span>
            <div className="text-lg font-bold text-blue-600 mt-0.5">{request.reservedUnits || 0}</div>
          </div>
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-[10px] text-slate-400 uppercase font-semibold">Remaining</span>
            <div className="text-lg font-bold text-slate-900 dark:text-slate-100 mt-0.5">{remaining}</div>
          </div>
        </div>
        <div className="flex items-center gap-2 pt-4 text-xs text-slate-500">
          <RequestStatusBadge status={request.status} /> Created {new Date(request.createdAt).toLocaleDateString()} - open until {new Date(request.expiryDate).toLocaleDateString()}
        </div>
      </div>

      <SmartMatchingProgress status={request.status || 'VERIFIED'} />

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mb-2">Clinical Context & Requirements</h3>
        <p className="text-xs text-slate-600 dark:text-slate-300 leading-relaxed">{request.reason}</p>
        <p className="text-[11px] text-slate-400 mt-3">
          After accepting, you complete a health screening interview with the LifeLink assistant. The hospital's doctor reviews your answers and decides whether you can donate.
        </p>
      </div>
    </div>
  );
};

export default RequestDetailPage;
