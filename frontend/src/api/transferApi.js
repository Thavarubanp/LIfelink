import client from './client';

export const transferApi = {
  createTransferRequest: async (dto) => {
    const response = await client.post('/transfers', dto);
    return response.data;
  },

  getAllTransferRequests: async () => {
    const response = await client.get('/transfers');
    return response.data;
  },

  getPendingTransferRequests: async () => {
    const response = await client.get('/transfers/pending');
    return response.data;
  },

  getTransferRequestById: async (id) => {
    const response = await client.get(`/transfers/${id}`);
    return response.data;
  },

  approveTransferRequest: async (id) => {
    const response = await client.put(`/transfers/${id}/approve`);
    return response.data;
  },

  rejectTransferRequest: async (id) => {
    const response = await client.put(`/transfers/${id}/reject`);
    return response.data;
  },

  completeTransferRequest: async (id) => {
    const response = await client.put(`/transfers/${id}/complete`);
    return response.data;
  }
};

export default transferApi;
