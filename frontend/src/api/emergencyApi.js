import client from './client';

export const emergencyApi = {
  createEmergencyRequest: async (dto) => {
    const response = await client.post('/emergencyrequests', dto);
    return response.data;
  },

  getAllEmergencyRequests: async () => {
    const response = await client.get('/emergencyrequests');
    return response.data;
  },

  getCriticalEmergencyRequests: async () => {
    const response = await client.get('/emergencyrequests/critical');
    return response.data;
  },

  getEmergencyRequestById: async (id) => {
    const response = await client.get(`/emergencyrequests/${id}`);
    return response.data;
  },

  approveEmergencyRequest: async (id) => {
    const response = await client.put(`/emergencyrequests/${id}/approve`);
    return response.data;
  },

  rejectEmergencyRequest: async (id) => {
    const response = await client.put(`/emergencyrequests/${id}/reject`);
    return response.data;
  },

  completeEmergencyRequest: async (id) => {
    const response = await client.put(`/emergencyrequests/${id}/complete`);
    return response.data;
  }
};

export default emergencyApi;
