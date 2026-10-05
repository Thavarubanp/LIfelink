import client from './client';

export const attentionApi = {
  // Aggregate workflow counts for the authenticated HospitalStaff or Doctor; no record data is returned.
  getRoleAttention: async ({ background = false } = {}) => {
    const response = await client.get('/attention', { background });
    return response.data;
  }
};

export default attentionApi;
