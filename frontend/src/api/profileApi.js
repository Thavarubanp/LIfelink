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
  },

  /** Resolves the logged-in account's own profile: { type: 'user' | 'doctor' | 'hospital', id } */
  getMyProfile: async () => {
    const response = await client.get('/profiles/me');
    return response.data;
  },

  // Own-profile edits (backend returns 403 for anyone else's profile)
  updateUserProfile: async (userId, data) => {
    const response = await client.put(`/profiles/user/${userId}`, data);
    return response.data;
  },

  updateDoctorProfile: async (doctorId, data) => {
    const response = await client.put(`/profiles/doctor/${doctorId}`, data);
    return response.data;
  },

  updateHospitalProfile: async (hospitalId, data) => {
    const response = await client.put(`/profiles/hospital/${hospitalId}`, data);
    return response.data;
  }
};

export default profileApi;
