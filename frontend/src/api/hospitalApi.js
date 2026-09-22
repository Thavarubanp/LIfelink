import client from './client';

export const hospitalApi = {
  createHospital: async (dto) => {
    const response = await client.post('/Hospitals', dto);
    return response.data;
  },

  getHospitals: async (isVerified = null) => {
    const params = isVerified !== null ? { isVerified } : {};
    const response = await client.get('/Hospitals', { params });
    return response.data;
  },

  getHospitalById: async (id) => {
    const response = await client.get(`/Hospitals/${id}`);
    return response.data;
  },

  resubmitHospital: async (id, dto) => {
    const response = await client.put(`/Hospitals/${id}/resubmit`, dto);
    return response.data;
  }
};

export default hospitalApi;
