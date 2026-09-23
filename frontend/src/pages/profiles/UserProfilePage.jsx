import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  User,
  Mail,
  Phone,
  Calendar,
  MapPin,
  ShieldCheck,
  ArrowLeft,
  AlertCircle,
  Loader2,
  Sparkles
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import profileApi from '../../api/profileApi';

export const UserProfilePage = () => {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user: currentUser } = useAuth();
  const [profile, setProfile] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    const fetchProfile = async () => {
      try {
        setLoading(true);
        setError('');
        const data = await profileApi.getUserProfile(id);
        setProfile(data);
      } catch (err) {
        setError(err.response?.data?.message || 'User profile not found or access restricted.');
      } finally {
        setLoading(false);
      }
    };

    if (id) {
      fetchProfile();
    }
  }, [id]);

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] gap-3">
        <Loader2 className="w-8 h-8 text-red-600 animate-spin" />
        <p className="text-xs text-slate-500">Loading user profile...</p>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="max-w-2xl mx-auto p-6 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-900/50 rounded-2xl text-center space-y-4">
        <AlertCircle className="w-10 h-10 text-red-600 mx-auto" />
        <h2 className="text-base font-bold text-red-700 dark:text-red-400">Profile Unavailable</h2>
        <p className="text-xs text-slate-600 dark:text-slate-300">
          {error || 'This user profile could not be found or you do not have permission to view it.'}
        </p>
        <button
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-2 px-4 py-2 bg-slate-900 text-white rounded-xl text-xs font-semibold hover:bg-slate-800"
        >
          <ArrowLeft className="w-4 h-4" /> Go Back
        </button>
      </div>
    );
  }

  const isCurrentUser = currentUser?.userId === profile.userId || currentUser?.email?.toLowerCase() === profile.email?.toLowerCase();
  const primaryRole = profile.roles?.[0] || 'User';

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <button
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-500 hover:text-slate-800 dark:hover:text-slate-200 transition-colors mb-2"
        >
          <ArrowLeft className="w-4 h-4" /> Back
        </button>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">User Profile</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Public user directory details in the LifeLink emergency healthcare network.
        </p>
      </div>

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <div className="flex items-center gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
          <div className="w-16 h-16 rounded-full bg-slate-900 text-white font-bold text-xl flex items-center justify-center shadow-lg">
            {profile.firstName ? profile.firstName.charAt(0).toUpperCase() : (profile.email ? profile.email.charAt(0).toUpperCase() : 'U')}
          </div>
          <div className="flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">
                {profile.firstName} {profile.lastName}
              </h2>
              {isCurrentUser && (
                <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-400">
                  This is you
                </span>
              )}
            </div>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              <span className="text-xs text-slate-500">{profile.email}</span>
              <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-400">
                <ShieldCheck className="w-3 h-3 inline mr-1" />
                {profile.accountStatus || 'Active'}
              </span>
              <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300">
                <Sparkles className="w-3 h-3 inline mr-1" />
                {primaryRole}
              </span>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-6 text-xs">
          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <User className="w-5 h-5 text-red-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Gender</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{profile.gender || 'Not specified'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Phone className="w-5 h-5 text-blue-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Phone Number</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{profile.phoneNumber || 'Not provided'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <MapPin className="w-5 h-5 text-emerald-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Address</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{profile.address || 'Not listed'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Calendar className="w-5 h-5 text-amber-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Member Since</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">
                {profile.createdAt ? new Date(profile.createdAt).toLocaleDateString([], { month: 'long', year: 'numeric', day: 'numeric' }) : 'N/A'}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default UserProfilePage;
