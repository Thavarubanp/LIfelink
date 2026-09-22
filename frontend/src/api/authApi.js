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

  logout: async () => {
    const response = await client.post('/Auth/logout');
    return response.data;
  },

  getCurrentUser: async () => {
    const response = await client.get('/Auth/me');
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
