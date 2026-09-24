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

  // Hospital staff: every request sent to the signed-in hospital (all statuses)
  getHospitalRequests: async () => {
    const response = await client.get('/BloodRequests/hospital');
    return response.data;
  },

  // Doctor: requests assigned to the signed-in doctor
  getAssignedRequests: async () => {
    const response = await client.get('/BloodRequests/assigned');
    return response.data;
  },

  // Creator: delete own request (removed from active lists, history kept)
  deleteRequest: async (id) => {
    const response = await client.delete(`/BloodRequests/${id}`);
    return response.data;
  },

  // Hospital: verify a pending request and assign one of its doctors (mandatory)
  verifyRequest: async (id, doctorId) => {
    const response = await client.put(`/requests/${id}/verify`, { doctorId });
    return response.data;
  },

  // Assigned doctor: approve a verified request
  approveRequest: async (id, notes = '') => {
    const response = await client.put(`/requests/${id}/approve`, { notes });
    return response.data;
  },

  // Hospital (pending/verified) or assigned doctor (verified): reject with a mandatory reason
  rejectRequest: async (id, reason) => {
    const response = await client.put(`/requests/${id}/reject`, { notes: reason });
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

  // Record donations of approved donors; testedBloodGroups: { [acceptanceId]: 'O+' }
  finalizeDonorSelection: async (id, selectedAcceptanceIds, testedBloodGroups = null) => {
    const response = await client.put(`/BloodRequests/${id}/finalize-selection`, { selectedAcceptanceIds, testedBloodGroups });
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
