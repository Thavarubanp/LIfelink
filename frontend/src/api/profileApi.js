import client from './client';

export const profileApi = {
  getHospitalProfile: async (hospitalId) => {
    const response = await client.get(`/profiles/hospital/${hospitalId}`);
    return response.data;
  },

  getUserProfile: async (userId) => {
    const response = await client.get(`/profiles/user/${userId}`);
    return response.data;
  },

  getDoctorProfile: async (doctorId) => {
    const response = await client.get(`/profiles/doctor/${doctorId}`);
    return response.data;
  }
};

export default profileApi;
