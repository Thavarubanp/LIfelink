import React, { useState, useEffect } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import {
  Stethoscope,
  Building2,
  Mail,
  Phone,
  Calendar,
  ShieldCheck,
  ArrowLeft,
  AlertCircle,
  Loader2,
  Award
} from 'lucide-react';
import profileApi from '../../api/profileApi';

export const DoctorProfilePage = () => {
  const { id } = useParams();
  const navigate = useNavigate();
  const [profile, setProfile] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    const fetchProfile = async () => {
      try {
        setLoading(true);
        setError('');
        const data = await profileApi.getDoctorProfile(id);
        setProfile(data);
      } catch (err) {
        setError(err.response?.data?.message || 'Doctor profile not found.');
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
        <Loader2 className="w-8 h-8 text-emerald-600 animate-spin" />
        <p className="text-xs text-slate-500">Loading doctor credentials...</p>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="max-w-2xl mx-auto p-6 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-900/50 rounded-2xl text-center space-y-4">
        <AlertCircle className="w-10 h-10 text-red-600 mx-auto" />
        <h2 className="text-base font-bold text-red-700 dark:text-red-400">Doctor Profile Unavailable</h2>
        <p className="text-xs text-slate-600 dark:text-slate-300">
          {error || 'Unable to retrieve the requested medical practitioner record.'}
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

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <button
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-500 hover:text-slate-800 dark:hover:text-slate-200 transition-colors mb-2"
        >
          <ArrowLeft className="w-4 h-4" /> Back
        </button>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
          <Stethoscope className="w-6 h-6 text-emerald-600" />
          Medical Doctor Profile
        </h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Verified medical officer credentials and affiliated healthcare facility.
        </p>
      </div>

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm space-y-6">
        <div className="flex items-center gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
          <div className="w-16 h-16 rounded-full bg-emerald-600 text-white font-bold text-xl flex items-center justify-center shadow-lg shadow-emerald-600/20">
            Dr
          </div>
          <div>
            <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">
              Dr. {profile.firstName} {profile.lastName}
            </h2>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              <span className="text-xs text-slate-500">{profile.email}</span>
              <span className="px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800/60">
                <ShieldCheck className="w-3 h-3 inline mr-1" />
                Active Practitioner
              </span>
              <span className="px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border border-blue-200 dark:border-blue-900/50">
                {profile.specialization || 'General Practitioner'}
              </span>
            </div>
          </div>
        </div>

        {/* Credentials Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Award className="w-5 h-5 text-amber-500 shrink-0" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">SLMC Registration Number</span>
              <div className="font-mono font-bold text-slate-900 dark:text-slate-100">{profile.licenseNumber}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Stethoscope className="w-5 h-5 text-emerald-500 shrink-0" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Specialization Area</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{profile.specialization || 'Clinical Medicine'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Phone className="w-5 h-5 text-blue-500 shrink-0" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Phone Number</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{profile.phoneNumber || 'Not listed'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Calendar className="w-5 h-5 text-purple-500 shrink-0" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Credentialed Since</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">
                {profile.createdAt ? new Date(profile.createdAt).toLocaleDateString([], { month: 'long', year: 'numeric', day: 'numeric' }) : 'N/A'}
              </div>
            </div>
          </div>
        </div>

        {/* Affiliated Hospital Banner */}
        <div className="p-4 rounded-xl bg-cyan-50/60 dark:bg-cyan-950/30 border border-cyan-200 dark:border-cyan-900/50 flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-cyan-100 dark:bg-cyan-900/60 text-cyan-700 dark:text-cyan-300 font-bold flex items-center justify-center text-sm shrink-0">
              <Building2 className="w-5 h-5" />
            </div>
            <div>
              <span className="text-[10px] uppercase font-bold text-cyan-700 dark:text-cyan-400">
                Affiliated Healthcare Facility
              </span>
              <h4 className="text-xs font-bold text-slate-900 dark:text-slate-100">
                {profile.hospitalName}
              </h4>
              {profile.hospitalAddress && (
                <p className="text-[11px] text-slate-500 dark:text-slate-400">{profile.hospitalAddress}</p>
              )}
            </div>
          </div>

          {profile.hospitalId && (
            <Link
              to={`/profiles/hospital/${profile.hospitalId}`}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-cyan-600 hover:bg-cyan-700 text-white text-xs font-semibold transition-colors"
            >
              <Building2 className="w-3.5 h-3.5" /> View Hospital Profile
            </Link>
          )}
        </div>
      </div>
    </div>
  );
};

export default DoctorProfilePage;
