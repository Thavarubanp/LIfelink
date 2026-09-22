import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
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

  // Role check
  if (allowedRoles.length > 0) {
    const userRoles = Array.isArray(user.roles) ? user.roles : [user.roles];
    const hasRole = allowedRoles.some((role) => userRoles.includes(role));

    if (!hasRole) {
      // Redirect to appropriate dashboard based on primary role
      if (userRoles.includes('Admin')) return <Navigate to="/admin/dashboard" replace />;
      if (userRoles.includes('HospitalStaff')) return <Navigate to="/hospital/dashboard" replace />;
      if (userRoles.includes('Doctor')) return <Navigate to="/doctor/dashboard" replace />;
      return <Navigate to="/donor/dashboard" replace />;
    }
  }

  return children;
};

export default ProtectedRoute;
