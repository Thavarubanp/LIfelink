import React, { useState, useEffect } from 'react';
import { Navigate } from 'react-router-dom';
import { AlertCircle, Loader2 } from 'lucide-react';
import profileApi from '../../api/profileApi';
import { getApiErrorMessage } from '../../utils/errorUtils';

/**
 * "My Profile": opens the logged-in account's own profile page
 * (user profile for Users/Admins, doctor profile for Doctors, hospital profile for Hospital Staff).
 */
export const MyProfilePage = () => {
  const [target, setTarget] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    profileApi
      .getMyProfile()
      .then((me) => setTarget(`/profiles/${me.type}/${me.id}`))
      .catch((err) => setError(getApiErrorMessage(err)));
  }, []);

  if (target) return <Navigate to={target} replace />;

  if (error) {
    return (
      <div className="max-w-2xl mx-auto p-6 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-900/50 rounded-2xl text-center space-y-3">
        <AlertCircle className="w-10 h-10 text-red-600 mx-auto" />
        <p className="text-xs text-slate-600 dark:text-slate-300">{error}</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] gap-3">
      <Loader2 className="w-8 h-8 text-red-600 animate-spin" />
      <p className="text-xs text-slate-500">Loading your profile...</p>
    </div>
  );
};

export default MyProfilePage;
