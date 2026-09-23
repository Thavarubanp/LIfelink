// Normalizes the roles claim (array or single string) returned by /Auth/login and /Auth/me.
// A user with no role rows is a plain User, matching the backend's login fallback.
export const getUserRoles = (user) => {
  if (!user) return [];
  const roles = Array.isArray(user.roles) ? user.roles : user.roles ? [user.roles] : [];
  return roles.length > 0 ? roles : ['User'];
};

// Default landing dashboard for a user's primary role (Admin > HospitalStaff > Doctor > Donor/User)
export const getDashboardPath = (user) => {
  const roles = getUserRoles(user);
  if (roles.includes('Admin')) return '/admin/dashboard';
  if (roles.includes('HospitalStaff')) return '/hospital/dashboard';
  if (roles.includes('Doctor')) return '/doctor/dashboard';
  return '/donor/dashboard';
};
