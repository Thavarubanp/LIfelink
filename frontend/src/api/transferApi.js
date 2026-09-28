import client from './client';

// Inter-Hospital Blood Transfer: the acting hospital always comes from the signed-in account
export const transferApi = {
  // dto: { transferType: 'Request' | 'Offer', counterpartHospitalId, bloodGroup, unitsRequested, notes, packetIds (offers) }
  // idempotencyKey: one per form, so a double submit never creates the transfer twice
  createTransferRequest: async (dto, { idempotencyKey } = {}) => {
    const response = await client.post('/transfers', dto, { idempotencyKey });
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

  // Counterpart hospital accepts: packets move immediately. A sender accepting a request passes the packets it sends.
  approveTransferRequest: async (id, packetIds = []) => {
    const response = await client.put(`/transfers/${id}/approve`, { packetIds });
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
