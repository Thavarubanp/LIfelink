import client from './client';

export const complaintApi = {
  createComplaint: async (dto) => {
    const response = await client.post('/Complaints', dto);
    return response.data;
  },

  getMyComplaints: async () => {
    const response = await client.get('/Complaints/my-complaints');
    return response.data;
  },

  submitActivityReport: async (dto) => {
    const response = await client.post('/hospital/activity-reports', dto);
    return response.data;
  },

  solveComplaint: async (id, dto) => {
    const response = await client.put(`/Complaints/${id}/solve`, dto || {});
    return response.data;
  },

  cancelComplaint: async (id) => {
    const response = await client.put(`/Complaints/${id}/cancel`);
    return response.data;
  }
};

export default complaintApi;
