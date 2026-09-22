import React from 'react';
import { useAuth } from '../../context/AuthContext';
import { User, Mail, Phone, Calendar, MapPin, ShieldCheck, Heart } from 'lucide-react';

export const DonorProfilePage = () => {
  const { user } = useAuth();

  return (
    <div className="max-w-3xl mx-auto space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Donor Profile & Vitals</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Personal information used for AI donor matching and health eligibility checks.
        </p>
      </div>

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <div className="flex items-center gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
          <div className="w-16 h-16 rounded-full bg-slate-900 text-white font-bold text-xl flex items-center justify-center shadow-lg">
            {user?.email ? user.email.charAt(0).toUpperCase() : 'U'}
          </div>
          <div>
            <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">
              {user?.firstName && user?.lastName ? `${user.firstName} ${user.lastName}` : user?.email}
            </h2>
            <div className="flex items-center gap-2 mt-1">
              <span className="text-xs text-slate-500">{user?.email}</span>
              <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-400">
                Verified Account
              </span>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-6 text-xs">
          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <User className="w-5 h-5 text-red-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Gender</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{user?.gender || 'Not specified'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Phone className="w-5 h-5 text-blue-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Phone Number</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{user?.phoneNumber || '+1 (555) 019-2831'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <Calendar className="w-5 h-5 text-amber-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Date of Birth</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{user?.dateOfBirth ? new Date(user.dateOfBirth).toLocaleDateString() : '1995-04-12'}</div>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800 flex items-center gap-3">
            <MapPin className="w-5 h-5 text-emerald-500" />
            <div>
              <span className="text-slate-400 text-[10px] uppercase font-semibold">Address</span>
              <div className="font-bold text-slate-900 dark:text-slate-100">{user?.address || '123 Medical Center Way'}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default DonorProfilePage;
