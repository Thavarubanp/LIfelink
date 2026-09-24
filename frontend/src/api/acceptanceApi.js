import client from './client';

export const acceptanceApi = {
  acceptRequest: async (dto) => {
    const response = await client.post('/Acceptances', dto);
    return response.data;
  },

  getMyAcceptances: async () => {
    const response = await client.get('/Acceptances/my');
    return response.data;
  },

  getAcceptanceById: async (id) => {
    const response = await client.get(`/Acceptances/${id}`);
    return response.data;
  },

  cancelAcceptance: async (id) => {
    const response = await client.put(`/Acceptances/${id}/cancel`);
    return response.data;
  },

  // Donor: "Update my answers" while the report still waits for the doctor (a new report version follows)
  reopenScreening: async (id) => {
    const response = await client.put(`/Acceptances/${id}/status?status=ScreeningPending`);
    return response.data;
  },

  // Doctor or hospital staff: release an acceptance that cannot proceed (reason shown to the donor)
  releaseReservation: async (id, reason) => {
    const response = await client.put(`/Acceptances/${id}/release`, { reason });
    return response.data;
  }
};

export default acceptanceApi;
