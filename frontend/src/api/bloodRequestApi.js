import client from './client';

export const bloodRequestApi = {
  createRequest: async (dto) => {
    const response = await client.post('/BloodRequests', dto);
    return response.data;
  },

  getMyRequests: async () => {
    const response = await client.get('/BloodRequests/my');
    return response.data;
  },

  getPublicRequests: async (bloodGroup = null, expiringWithinHours = null) => {
    const params = {};
    if (bloodGroup) params.bloodGroup = bloodGroup;
    if (expiringWithinHours) params.expiringWithinHours = expiringWithinHours;
    const response = await client.get('/BloodRequests/public', { params });
    return response.data;
  },

  getPendingRequests: async (hospitalId = null) => {
    const params = hospitalId ? { hospitalId } : {};
    const response = await client.get('/BloodRequests/pending', { params });
    return response.data;
  },

  getRequestById: async (id) => {
    const response = await client.get(`/BloodRequests/${id}`);
    return response.data;
  },

  cancelRequest: async (id) => {
    const response = await client.put(`/BloodRequests/${id}/cancel`);
    return response.data;
  },

  getRequestAcceptances: async (id) => {
    const response = await client.get(`/BloodRequests/${id}/acceptances`);
    return response.data;
  },

  finalizeDonorSelection: async (id, selectedAcceptanceIds) => {
    const response = await client.put(`/BloodRequests/${id}/finalize-selection`, { selectedAcceptanceIds });
    return response.data;
  },

  getRequestAnalytics: async (id) => {
    const response = await client.get(`/BloodRequests/${id}/analytics`);
    return response.data;
  },

  getRequestFulfillmentHistory: async (id) => {
    const response = await client.get(`/BloodRequests/${id}/fulfillment-history`);
    return response.data;
  }
};

export default bloodRequestApi;
