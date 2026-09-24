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

  /** Creator reply { notes, attachmentUrl?, attachmentName? } — allowed only after an admin reply */
  replyToComplaint: async (id, dto) => {
    const response = await client.post(`/Complaints/${id}/reply`, dto);
    return response.data;
  },

  /** Creator permanently deletes the complaint (any status) */
  deleteComplaint: async (id) => {
    const response = await client.put(`/Complaints/${id}/cancel`);
    return response.data;
  }
};

// Complaint categories per creator role (mirrors ComplaintService on the backend)
export const USER_COMPLAINT_CATEGORIES = ['Donation Process', 'Blood Request', 'Hospital Service', 'Account Issue', 'Technical Issue', 'Other'];
export const HOSPITAL_COMPLAINT_CATEGORIES = ['Donor Misconduct', 'Fake Blood Request', 'Policy Violation', 'Account Issue', 'Technical Issue', 'Other'];

export default complaintApi;
