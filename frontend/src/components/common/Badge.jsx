import React from 'react';

export const Badge = ({ children, variant = 'default', size = 'md', className = '' }) => {
  const variantStyles = {
    default: 'bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200 border-slate-200 dark:border-slate-700',
    primary: 'bg-red-50 text-red-700 dark:bg-red-950/60 dark:text-red-300 border-red-200 dark:border-red-900/50',
    success: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-200 dark:border-emerald-900/50',
    warning: 'bg-amber-50 text-amber-700 dark:bg-amber-950/60 dark:text-amber-300 border-amber-200 dark:border-amber-900/50',
    info: 'bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300 border-blue-200 dark:border-blue-900/50',
    danger: 'bg-rose-100 text-rose-800 dark:bg-rose-950/80 dark:text-rose-300 border-rose-300 dark:border-rose-900 animate-pulse',
    blood: 'bg-red-600 text-white font-bold border-red-700 shadow-sm'
  };

  const sizeStyles = {
    sm: 'px-2 py-0.5 text-[10px]',
    md: 'px-2.5 py-1 text-xs',
    lg: 'px-3 py-1.5 text-sm font-semibold'
  };

  return (
    <span className={`inline-flex items-center rounded-full border font-medium transition-colors ${variantStyles[variant] || variantStyles.default} ${sizeStyles[size]} ${className}`}>
      {children}
    </span>
  );
};

// Blood request lifecycle status: Pending → Verified (doctor assigned) → Approved → Completed, or Rejected/Cancelled
const REQUEST_STATUS_VARIANTS = {
  Pending: 'warning',
  Verified: 'info',
  Approved: 'success',
  Completed: 'success',
  Rejected: 'primary',
  Cancelled: 'default'
};

export const RequestStatusBadge = ({ status, size = 'sm' }) => (
  <Badge variant={REQUEST_STATUS_VARIANTS[status] || 'default'} size={size}>
    {status || 'Unknown'}
  </Badge>
);

export default Badge;
