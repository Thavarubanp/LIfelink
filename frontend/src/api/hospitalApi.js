import client from './client';

export const hospitalApi = {
  createHospital: async (dto) => {
    const response = await client.post('/Hospitals', dto);
    return response.data;
  },

  // Directory for signed-in users (summary fields only)
  getHospitals: async (isVerified = null) => {
    const params = isVerified !== null ? { isVerified } : {};
    const response = await client.get('/Hospitals', { params });
    return response.data;
  },

  // Full record with the registration conversation: admins, or the hospital's own staff
  getHospitalById: async (id) => {
    const response = await client.get(`/Hospitals/${id}`);
    return response.data;
  },

  // The signed-in hospital staff member's own hospital
  getMyHospital: async () => {
    const response = await client.get('/Hospitals/me');
    return response.data;
  },

  // Reply in a rejected registration's conversation, optionally with corrected details and documents
  replyToRegistration: async (id, dto) => {
    const response = await client.post(`/Hospitals/${id}/replies`, dto);
    return response.data;
  }
};

export default hospitalApi;
