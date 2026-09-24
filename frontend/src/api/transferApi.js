import client from './client';

// Inter-Hospital Blood Transfer: the acting hospital always comes from the signed-in account
export const transferApi = {
  // dto: { transferType: 'Request' | 'Offer', counterpartHospitalId, bloodGroup, unitsRequested, notes }
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

  // Counterpart hospital accepts: packets move immediately
  approveTransferRequest: async (id) => {
    const response = await client.put(`/transfers/${id}/approve`);
    return response.data;
  },

  rejectTransferRequest: async (id, reason) => {
    const response = await client.put(`/transfers/${id}/reject`, { reason });
    return response.data;
  },

  // Creator withdraws a pending transfer (kept in history as Cancelled)
  deleteTransferRequest: async (id) => {
    const response = await client.delete(`/transfers/${id}`);
    return response.data;
  }
};

export default transferApi;
