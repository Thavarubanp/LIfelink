import client from './client';

// Activity logs (paged + filtered: { page, pageSize, type, from, to }) and the Admin's oversight data
export const activityApi = {
  /** The signed-in account's own activity (a hospital's staff see their hospital's log) */
  getMyActivity: async (params = {}) => {
    const response = await client.get('/activity-logs/my', { params });
    return response.data;
  },

  /** Admin: a user's full activity log */
  getUserActivity: async (userId, params = {}) => {
    const response = await client.get(`/Admin/users/${userId}/activity-log`, { params });
    return response.data;
  },

  /** Admin: a hospital's full activity log */
  getHospitalActivity: async (hospitalId, params = {}) => {
    const response = await client.get(`/Admin/hospitals/${hospitalId}/activity-log`, { params });
    return response.data;
  },

  /** Admin: every blood request (any status, including deleted), newest first */
  getAllBloodRequests: async () => {
    const response = await client.get('/Admin/blood-requests');
    return response.data;
  },

  /** Admin: badge counts. Polled in the background: never counts as user activity for the idle timeout */
  getAttentionCounts: async ({ background = false } = {}) => {
    const response = await client.get('/Admin/attention-counts', { background });
    return response.data;
  },

  /** Admin: an Activity log tab was opened ('blood-requests' | 'transfers'); its "new" highlight clears */
  markAreaSeen: async (area) => {
    await client.put(`/Admin/attention/${area}/seen`);
  },

  /** Admin: suspend an open blood request (only a donor withdrawing and the creator deleting stay possible) */
  suspendBloodRequest: async (id, reason) => {
    await client.put(`/Admin/blood-requests/${id}/suspend`, { reason });
  },

  liftBloodRequest: async (id) => {
    await client.put(`/Admin/blood-requests/${id}/lift`);
  },

  /** Admin: suspend a pending transfer (it cannot be accepted, rejected or withdrawn until lifted) */
  suspendTransfer: async (id, reason) => {
    await client.put(`/Admin/transfers/${id}/suspend`, { reason });
  },

  liftTransfer: async (id) => {
    await client.put(`/Admin/transfers/${id}/lift`);
  },

  /** Admin: one-way "Message from Administrator" to one user ({ userId }) or one hospital ({ hospitalId }) */
  sendMessage: async ({ userId, hospitalId, subject, message }) => {
    await client.post('/Admin/messages', { userId, hospitalId, subject, message });
  }
};

export default activityApi;
