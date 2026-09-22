import client from './client';

export const acceptanceApi = {
  acceptRequest: async (dto) => {
    const response = await client.post('/Acceptances', dto);
    return response.data;
  },

  getMyAcceptances: async () => {
    const response = await client.get('/Acceptances/my');
    return response.data;
  },

  getAcceptanceById: async (id) => {
    const response = await client.get(`/Acceptances/${id}`);
    return response.data;
  },

  cancelAcceptance: async (id) => {
    const response = await client.put(`/Acceptances/${id}/cancel`);
    return response.data;
  },

  updateScreeningStatus: async (id, status) => {
    const response = await client.put(`/Acceptances/${id}/status?status=${status}`);
    return response.data;
  }
};

export default acceptanceApi;
