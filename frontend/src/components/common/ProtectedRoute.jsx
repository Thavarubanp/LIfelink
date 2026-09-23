import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { getUserRoles, getDashboardPath } from '../../utils/roleUtils';
import { Loader2 } from 'lucide-react';

export const ProtectedRoute = ({ children, allowedRoles = [], allowSuspended = false }) => {
  const { user, loading } = useAuth();
  const location = useLocation();

  if (loading) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center bg-slate-50 dark:bg-slate-950 text-slate-600 dark:text-slate-400">
        <Loader2 className="w-10 h-10 text-red-600 animate-spin mb-3" />
        <p className="text-sm font-medium">Verifying LifeLink Session...</p>
      </div>
    );
  }

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  // Check suspension status unless page explicitly allows suspended access
  if (user.isSuspended && !allowSuspended) {
    return <Navigate to="/governance/status" replace />;
  }

  // Doctor first-login enforcement: redirect to change-password page if flag is set.
  // Allow the change-password page itself to avoid an infinite redirect loop.
  const userRoles = getUserRoles(user);
  if (
    userRoles.includes('Doctor') &&
    user.mustChangePassword === true &&
    location.pathname !== '/doctor/change-password'
  ) {
    return <Navigate to="/doctor/change-password" replace />;
  }

  // Role check
  if (allowedRoles.length > 0) {
    const hasRole = allowedRoles.some((role) => userRoles.includes(role));

    if (!hasRole) {
      // Redirect to appropriate dashboard based on primary role
      return <Navigate to={getDashboardPath(user)} replace />;
    }
  }

  return children;
};

export default ProtectedRoute;
