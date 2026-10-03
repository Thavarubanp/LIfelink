import client from './client';

export const authApi = {
  login: async (credentials) => {
    const response = await client.post('/Auth/login', credentials);
    return response.data;
  },

  register: async (userData) => {
    const response = await client.post('/Auth/register', userData);
    return response.data;
  },

  /** Ends the server-side session (every tab); reason 'idle' when signed out for inactivity */
  logout: async (reason) => {
    const response = await client.post('/Auth/logout', null, reason ? { params: { reason } } : undefined);
    return response.data;
  },

  /** Heartbeat: the user is active (mouse, keyboard, touch, scroll or "Stay signed in"); keeps the session alive */
  recordActivity: async () => {
    const response = await client.post('/Auth/activity', null, { reportActivity: true });
    return response.data;
  },

  getCurrentUser: async () => {
    const response = await client.get('/Auth/me');
    return response.data;
  },

  /** Donor/patient permanently deletes their own account (history kept; the email can register again) */
  deleteMyAccount: async () => {
    const response = await client.delete('/Auth/me');
    return response.data;
  },

  getUserById: async (id) => {
    const response = await client.get(`/Auth/user/${id}`);
    return response.data;
  },

  forgotPassword: async (emailData) => {
    const response = await client.post('/Auth/forgot-password', emailData);
    return response.data;
  },

  verifyOtp: async (otpData) => {
    const response = await client.post('/Auth/verify-otp', otpData);
    return response.data;
  },

  resendOtp: async (emailData) => {
    const response = await client.post('/Auth/resend-otp', emailData);
    return response.data;
  },

  resetPassword: async (resetData) => {
    const response = await client.post('/Auth/reset-password', resetData);
    return response.data;
  },

  changePassword: async (passwordData) => {
    const response = await client.post('/Auth/change-password', passwordData);
    return response.data;
  }
};

export default authApi;
