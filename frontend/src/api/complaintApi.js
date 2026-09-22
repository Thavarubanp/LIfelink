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
  }
};

export default complaintApi;
