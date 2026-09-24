import client from './client';

// Doctor review of AI screening reports (every version and decision is kept)
export const screeningApi = {
  getReports: async () => {
    const response = await client.get('/donor-verification');
    return response.data;
  },

  // Approval reserves one donation slot; notes are shown to the donor
  approve: async (reportId, notes = '') => {
    const response = await client.put(`/donor-verification/${reportId}/approve`, { notes });
    return response.data;
  },

  // A reason is required and shown to the donor
  reject: async (reportId, reason) => {
    const response = await client.put(`/donor-verification/${reportId}/reject`, { notes: reason });
    return response.data;
  }
};

export default screeningApi;
