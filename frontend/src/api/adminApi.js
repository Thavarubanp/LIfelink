import client from './client';

export const adminApi = {
  getDashboardStats: async () => {
    const response = await client.get('/Admin/dashboard');
    return response.data;
  },

  getPendingHospitals: async () => {
    const response = await client.get('/Admin/hospitals/pending');
    return response.data;
  },

  // dto: { lastSeenEntryId } - refused (409) if the hospital replied after that conversation entry
  approveHospital: async (id, dto = {}) => {
    const response = await client.put(`/Admin/hospitals/${id}/approve`, dto);
    return response.data;
  },

  // Pending registrations only; afterwards the admin continues with comments
  rejectHospital: async (id, dto) => {
    const response = await client.put(`/Admin/hospitals/${id}/reject`, dto);
    return response.data;
  },

  // dto: { message, attachmentUrl?, attachmentName?, lastSeenEntryId }
  commentOnHospitalRegistration: async (id, dto) => {
    const response = await client.post(`/Admin/hospitals/${id}/comments`, dto);
    return response.data;
  },

  suspendUser: async (id, dto) => {
    const response = await client.put(`/Admin/users/${id}/suspend`, dto);
    return response.data;
  },

  reinstateUser: async (id) => {
    const response = await client.put(`/Admin/users/${id}/reinstate`);
    return response.data;
  },

  suspendHospital: async (id, dto) => {
    const response = await client.put(`/Admin/hospitals/${id}/suspend`, dto);
    return response.data;
  },

  reinstateHospital: async (id) => {
    const response = await client.put(`/Admin/hospitals/${id}/reinstate`);
    return response.data;
  },

  getUsers: async () => {
    const response = await client.get('/Admin/users');
    return response.data;
  },

  getAllHospitals: async () => {
    const response = await client.get('/Admin/hospitals');
    return response.data;
  },

  getComplaints: async (status = null) => {
    const params = status ? { status } : {};
    const response = await client.get('/Admin/complaints', { params });
    return response.data;
  },

  getComplaintById: async (id) => {
    const response = await client.get(`/Admin/complaints/${id}`);
    return response.data;
  },

  /** Admin reply { notes, attachmentUrl?, attachmentName? } — the only admin complaint action */
  replyToComplaint: async (id, dto) => {
    const response = await client.put(`/Admin/complaints/${id}/review`, dto);
    return response.data;
  },

  getActivityReports: async (complaintId = null, hospitalId = null) => {
    const params = {};
    if (complaintId) params.complaintId = complaintId;
    if (hospitalId) params.hospitalId = hospitalId;
    const response = await client.get('/Admin/activity-reports', { params });
    return response.data;
  },

  getActivityReportById: async (id) => {
    const response = await client.get(`/Admin/activity-reports/${id}`);
    return response.data;
  },

  getAppeals: async (status = null) => {
    const params = status ? { status } : {};
    const response = await client.get('/Admin/appeals', { params });
    return response.data;
  },

  approveAppeal: async (id, dto) => {
    const response = await client.put(`/Admin/appeals/${id}/approve`, dto);
    return response.data;
  },

  rejectAppeal: async (id, dto) => {
    const response = await client.put(`/Admin/appeals/${id}/reject`, dto);
    return response.data;
  },

  replyToAppeal: async (id, dto) => {
    const response = await client.put(`/Admin/appeals/${id}/reply`, dto);
    return response.data;
  },

  closeAppeal: async (id, dto) => {
    const response = await client.put(`/Admin/appeals/${id}/close`, dto);
    return response.data;
  },

  /** Permanently block a donor/patient account */
  blockUser: async (id) => {
    const response = await client.put(`/Admin/users/${id}/block`);
    return response.data;
  },

  /** Transfer Admin ownership to an active donor/patient (the caller becomes a normal User) */
  promoteToAdmin: async (id) => {
    const response = await client.put(`/Admin/users/${id}/promote`);
    return response.data;
  },

  permanentlyBlockAppeal: async (id, dto) => {
    const response = await client.put(`/Admin/appeals/${id}/permanently-block`, dto);
    return response.data;
  }
};

export default adminApi;
